using System.Diagnostics;

namespace VsCodeTabs;

internal sealed class TabGroup : IDisposable
{
    const int DefaultTitleBarHeightDip = 29;
    const int DefaultLeftInsetDip = 38;
    const int DefaultRightInsetDip = 280;
    const int MeasureAttempts = 10;
    const int ParkingGap = 1000;
    const int SettleMilliseconds = 2000;
    const int QuietMilliseconds = 300;
    const int CloseQuietMilliseconds = 10000;
    const int ClosingTimeoutMilliseconds = 15000;
    const int CoverTimeoutMilliseconds = 3000;
    const int FollowMilliseconds = 300;

    readonly TabManager manager;
    TabStrip strip;
    TabStrip spare;
    readonly List<IntPtr> tabs = [];
    readonly HashSet<IntPtr> stashed = [];
    readonly Dictionary<IntPtr, int> closing = [];
    readonly Dictionary<IntPtr, string> identities = [];
    readonly System.Windows.Forms.Timer measureTimer = new() { Interval = 300 };
    readonly System.Windows.Forms.Timer coverTimer = new() { Interval = 100 };
    readonly System.Windows.Forms.Timer closeTimer = new() { Interval = ClosingTimeoutMilliseconds };

    Cover? cover;
    bool pendingTab;
    IntPtr loadingTab;
    long loadingSince;
    IntPtr coveredFrom;
    long coverUntil;
    IntPtr active;
    Rectangle frame;
    IntPtr settling;
    long settlingUntil;
    int failedMeasurements;
    int geometryChanges;
    int focusWatch;

    public TabGroup(TabManager manager)
    {
        this.manager = manager;
        strip = new TabStrip(this);
        spare = new TabStrip(this);
        measureTimer.Tick += (_, _) =>
        {
            measureTimer.Stop();
            MeasureTitleBar();
        };
        coverTimer.Tick += (_, _) => CheckCover();
        closeTimer.Tick += (_, _) => FinishGroupClose();
    }

    public IReadOnlyList<IntPtr> Tabs => tabs;

    public IntPtr Active => active;

    public TabStrip Strip => strip;

    public bool Contains(IntPtr hwnd) => tabs.Contains(hwnd) || closing.ContainsKey(hwnd);

    public bool Accepts(IntPtr hwnd)
    {
        var member = tabs.FirstOrDefault(Native.IsWindow);
        return member == IntPtr.Zero
            || string.Equals(Win.ProcessPath(member), Win.ProcessPath(hwnd), StringComparison.OrdinalIgnoreCase);
    }

    public void Load(IEnumerable<IntPtr> windows)
    {
        foreach (var hwnd in windows)
        {
            Transitions.Enable(hwnd);
            tabs.Add(hwnd);
            if (Win.IsStashed(hwnd))
                stashed.Add(hwnd);
        }
        SortTabs();
    }

    public void Switch(IntPtr hwnd)
    {
        if (hwnd == active)
            Native.SetForegroundWindow(hwnd);
        else
            Activate(hwnd, focus: true);
    }

    public void Cycle(int step)
    {
        var index = tabs.IndexOf(active);
        if (index < 0 || tabs.Count < 2)
            return;
        var target = ((index + step) % tabs.Count + tabs.Count) % tabs.Count;
        if (target != index)
            Switch(tabs[target]);
    }

    public void Close(IntPtr hwnd)
    {
        var index = tabs.IndexOf(hwnd);
        if (index >= 0 && tabs.Count > 1)
        {
            if (hwnd == active)
                Activate(Neighbour(index), focus: true);
            tabs.RemoveAt(index);
            closing[hwnd] = index;
            manager.Transitions.Hold(hwnd, CloseQuietMilliseconds);
            var timeout = new System.Windows.Forms.Timer { Interval = ClosingTimeoutMilliseconds };
            timeout.Tick += (_, _) =>
            {
                timeout.Dispose();
                Reinstate(hwnd);
            };
            timeout.Start();
            PlaceStrip();
            strip.Invalidate();
        }
        Native.PostMessage(hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    IntPtr Neighbour(int index) => tabs[index + 1 < tabs.Count ? index + 1 : index - 1];

    void Reinstate(IntPtr hwnd)
    {
        if (!closing.Remove(hwnd, out var index) || !Native.IsWindow(hwnd))
            return;
        tabs.Insert(Math.Min(index, tabs.Count), hwnd);
        PlaceStrip();
        strip.Invalidate();
    }

    public void Move(int from, int to)
    {
        var hwnd = tabs[from];
        tabs.RemoveAt(from);
        tabs.Insert(to, hwnd);
        SaveOrder();
        strip.Invalidate();
    }

    public void MoveTo(IntPtr hwnd, int index)
    {
        var from = tabs.IndexOf(hwnd);
        if (from >= 0 && index >= 0 && index < tabs.Count && from != index)
            Move(from, index);
    }

    public void BeginDrag(IntPtr hwnd, Point grab) => manager.BeginDrag(this, hwnd, grab);

    public async void NewTab()
    {
        manager.MarkFocused(this);
        CoverActive();
        var executable = active != IntPtr.Zero ? Win.ProcessPath(active) : null;
        var folder = active != IntPtr.Zero ? TitleParser.Folder(Win.Title(active)) : null;
        Native.AllowSetForegroundWindow(Native.ASFW_ANY);
        if (await CommandPipe.RequestNewWindow(folder ?? ""))
            return;
        var start = new ProcessStartInfo(executable ?? "code", "--new-window")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var name in start.Environment.Keys.Where(IsVsCodeInternal).ToList())
            start.Environment.Remove(name);
        Process.Start(start);
    }

    void CoverActive()
    {
        DropCover();
        if (active == IntPtr.Zero || !Native.IsWindowVisible(active) || Native.IsIconic(active))
            return;
        pendingTab = true;
        loadingSince = Environment.TickCount64;
        strip.Refresh();
        cover = Cover.Over(TitleBarArea(active), strip.ScreenBounds);
        coveredFrom = active;
        coverUntil = Environment.TickCount64 + CoverTimeoutMilliseconds;
        coverTimer.Start();
    }

    void CheckCover()
    {
        if (cover is null || Environment.TickCount64 >= coverUntil)
            DropCover();
        else if (active != coveredFrom && Native.IsWindowVisible(active)
            && TitleBarProbe.Measure(active, TitleBarArea(active), Win.Scale(active), strip.TabAreaScreenBounds) is not null)
            DropCover();
    }

    void DropCover()
    {
        coverTimer.Stop();
        cover?.Dispose();
        cover = null;
        pendingTab = false;
        loadingTab = IntPtr.Zero;
        if (!strip.IsDisposed)
            strip.Invalidate();
    }

    public bool PendingTab => pendingTab;

    public IntPtr LoadingTab => loadingTab;

    public long LoadingSince => loadingSince;

    static Rectangle TitleBarArea(IntPtr hwnd) => Native.IsZoomed(hwnd) ? Win.WorkArea(hwnd) : Win.VisibleBounds(hwnd);

    static bool IsVsCodeInternal(string name) =>
        name.Equals("ELECTRON_RUN_AS_NODE", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("VSCODE_", StringComparison.OrdinalIgnoreCase);

    public void RestoreAll()
    {
        foreach (var hwnd in stashed.Where(Native.IsWindow).ToList())
        {
            Unstash(hwnd);
            if (Native.IsZoomed(hwnd))
            {
                var bounds = Win.MaximizedBounds(active != IntPtr.Zero ? active : hwnd);
                Native.SetWindowPos(hwnd, active, bounds.X, bounds.Y, bounds.Width, bounds.Height, Native.SWP_NOACTIVATE);
                Win.RepairMaximizedFrame(hwnd);
            }
            else if (!frame.IsEmpty)
                Win.SetVisibleBounds(hwnd, frame, active, Native.SWP_NOACTIVATE);
            else
                Native.ShowWindow(hwnd, Native.SW_SHOWMAXIMIZED);
        }
    }

    public void Dispose()
    {
        RestoreAll();
        Release();
    }

    public void Release()
    {
        closing.Clear();
        DropCover();
        coverTimer.Dispose();
        measureTimer.Dispose();
        closeTimer.Dispose();
        strip.Dispose();
        spare.Dispose();
    }

    public void OnForeground(IntPtr hwnd)
    {
        if (closeTimer.Enabled && stashed.Contains(hwnd))
            return;
        Reinstate(hwnd);
        if (hwnd == active)
            PlaceStrip();
        else
            Activate(hwnd, focus: false);
    }

    public void OnOwnedForeground(IntPtr owner, IntPtr dialog)
    {
        closeTimer.Stop();
        Reinstate(owner);
        Activate(owner, focus: false);
        if (Win.ClassName(dialog) != "#32770")
            return;
        var area = Native.IsZoomed(owner) ? Win.WorkArea(owner) : Win.VisibleBounds(owner);
        Native.GetWindowRect(dialog, out var rect);
        var bounds = rect.ToRectangle();
        if (area.Contains(bounds))
            return;
        Native.SetWindowPos(dialog, IntPtr.Zero, area.Left + (area.Width - bounds.Width) / 2,
            area.Top + (area.Height - bounds.Height) / 2, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    public void OnHidden(IntPtr hwnd)
    {
        if (hwnd != active || !tabs.Contains(hwnd) || tabs.Count < 2)
            return;
        closeTimer.Stop();
        closeTimer.Start();
        foreach (var tab in tabs.Where(tab => tab != hwnd).ToList())
            Native.PostMessage(tab, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    void FinishGroupClose()
    {
        closeTimer.Stop();
        if (active != IntPtr.Zero && Native.IsWindowVisible(active))
            return;
        var next = tabs.FirstOrDefault(tab => tab != active && Native.IsWindow(tab));
        if (next != IntPtr.Zero)
            Activate(next, focus: true);
    }

    public void OnMoveSizeStart() => settling = IntPtr.Zero;

    public void OnLocationChange(IntPtr hwnd)
    {
        if (hwnd != active || stashed.Contains(hwnd))
            return;
        if (Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd) && !Native.IsZoomed(hwnd))
        {
            var bounds = Win.VisibleBounds(hwnd);
            if (hwnd == settling && Environment.TickCount64 < settlingUntil && !frame.IsEmpty)
            {
                if (bounds != frame)
                    Win.SetVisibleBounds(hwnd, frame, IntPtr.Zero, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            }
            else
            {
                frame = bounds;
            }
        }
        PlaceStrip();
    }

    public void OnMinimize(IntPtr hwnd)
    {
        if (hwnd == active)
            PlaceStrip();
    }

    public void OnNameChange(IntPtr hwnd)
    {
        var identity = TitleParser.Identity(Win.Title(hwnd));
        if (identities.GetValueOrDefault(hwnd) != identity)
        {
            identities[hwnd] = identity;
            SaveOrder();
            PlaceStrip();
        }
        strip.Invalidate();
    }

    public void Activate(IntPtr hwnd, bool focus)
    {
        manager.Transitions.Hold(hwnd, QuietMilliseconds);
        var previous = active;
        var anchor = hwnd;
        var maximized = Native.IsZoomed(hwnd) || (frame.IsEmpty && stashed.Contains(hwnd));
        if (previous != IntPtr.Zero && previous != hwnd && Native.IsWindowVisible(previous)
            && !Native.IsIconic(previous) && !stashed.Contains(previous))
        {
            anchor = previous;
            maximized = Native.IsZoomed(previous);
            if (!maximized)
                frame = Win.VisibleBounds(previous);
        }

        if (!tabs.Contains(hwnd))
        {
            manager.Transitions.ReleaseAfter(hwnd, QuietMilliseconds);
            tabs.Add(hwnd);
            identities[hwnd] = TitleParser.Identity(Win.Title(hwnd));
            SaveOrder();
            settling = hwnd;
            settlingUntil = Environment.TickCount64 + SettleMilliseconds;
            if (pendingTab)
            {
                pendingTab = false;
                loadingTab = hwnd;
            }
        }
        active = hwnd;
        var swapped = SwapStrips(previous, hwnd);
        Unstash(hwnd);

        if (maximized)
        {
            if (!Native.IsZoomed(hwnd))
            {
                if (!frame.IsEmpty && !Native.IsIconic(hwnd))
                    Win.SetVisibleBounds(hwnd, frame, IntPtr.Zero, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                Native.ShowWindow(hwnd, Native.SW_SHOWMAXIMIZED);
            }
            else
            {
                var bounds = Win.MaximizedBounds(anchor);
                Native.SetWindowPos(hwnd, Native.HWND_TOP, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }
            Win.RepairMaximizedFrame(hwnd);
        }
        else
        {
            if (Native.IsIconic(hwnd) || Native.IsZoomed(hwnd))
                Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
            var current = Win.VisibleBounds(hwnd);
            if (frame.IsEmpty)
                frame = current;
            Win.SetVisibleBounds(hwnd, frame, Native.HWND_TOP, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        if (focus)
            Native.SetForegroundWindow(hwnd);

        foreach (var tab in tabs.Where(tab => tab != hwnd && !stashed.Contains(tab)).ToList())
            Stash(tab);
        if (stashed.Contains(Native.GetForegroundWindow()))
            Native.SetForegroundWindow(hwnd);

        if (swapped)
            spare.HideStrip();
        ShowFocus();
        PlaceStrip();
        strip.Invalidate();
    }

    public void ShowFocus()
    {
        Interlocked.Increment(ref focusWatch);
        if (active != IntPtr.Zero && manager.Colors(active, Native.GetForegroundWindow() == active) is { } colors)
            strip.ApplyTheme(colors);
    }

    public void FollowFocus()
    {
        var window = active;
        var previous = strip.Colors.Background;
        if (window == IntPtr.Zero || manager.Colors(window, Native.GetForegroundWindow() == window) is not { } colors)
            return;
        var expected = colors.Background;
        if (expected.ToArgb() == previous.ToArgb())
        {
            strip.ApplyTheme(colors);
            return;
        }
        var bounds = strip.ScreenBounds;
        if (bounds.IsEmpty)
        {
            ShowFocus();
            return;
        }
        var sample = new Point(bounds.Right + Dip(4, Win.Scale(window)), bounds.Top + bounds.Height / 2);
        var watch = Interlocked.Increment(ref focusWatch);
        var ui = SynchronizationContext.Current!;
        Task.Run(() =>
        {
            var deadline = Environment.TickCount64 + FollowMilliseconds;
            while (Environment.TickCount64 < deadline && Volatile.Read(ref focusWatch) == watch && !Repainted(window, sample, previous, expected))
            {
            }
            ui.Post(_ =>
            {
                if (focusWatch == watch && active == window)
                    strip.ApplyTheme(colors);
            }, null);
        });
    }

    static bool Repainted(IntPtr window, Point sample, Color previous, Color expected)
    {
        if (Native.GetAncestor(Native.WindowFromPoint(sample), Native.GA_ROOT) != window)
            return true;
        var pixel = Win.ScreenPixel(sample);
        return Distance(pixel, expected) < Distance(pixel, previous);
    }

    static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    bool SwapStrips(IntPtr previous, IntPtr hwnd)
    {
        if (spare.IsDisposed)
            spare = new TabStrip(this);
        if (previous == IntPtr.Zero || previous == hwnd || strip.AttachedTo != previous || !Native.IsWindowVisible(strip.Handle))
        {
            strip.Attach(hwnd);
            strip.Refresh();
            return false;
        }
        spare.Mirror(strip, hwnd);
        (strip, spare) = (spare, strip);
        return true;
    }

    void Stash(IntPtr hwnd)
    {
        stashed.Add(hwnd);
        Win.SetToolWindow(hwnd, true);
        Taskbar.Remove(hwnd);
        var parking = ParkingSpot(hwnd);
        Native.SetWindowPos(hwnd, Native.HWND_BOTTOM, parking.X, parking.Y, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    void Unstash(IntPtr hwnd)
    {
        if (!stashed.Remove(hwnd) && !Win.IsStashed(hwnd))
            return;
        Win.SetToolWindow(hwnd, false);
        Taskbar.Add(hwnd);
    }

    Point ParkingSpot(IntPtr hwnd)
    {
        var anchor = active != IntPtr.Zero ? active : hwnd;
        var monitor = Win.MonitorBounds(anchor);
        var width = Win.OuterBounds(hwnd).Width;
        return new Point(monitor.Left + (monitor.Width - width) / 2, SystemInformation.VirtualScreen.Bottom + ParkingGap);
    }

    public void Remove(IntPtr hwnd)
    {
        if (!Contains(hwnd))
            return;
        if (closing.Remove(hwnd))
        {
            stashed.Remove(hwnd);
            identities.Remove(hwnd);
        }
        Win.Forget(hwnd);
        if (strip.IsDisposed)
            strip = new TabStrip(this);
        if (spare.IsDisposed)
            spare = new TabStrip(this);
        Drop(hwnd, closed: true);
    }

    public void Release(IntPtr hwnd) => Drop(hwnd, closed: false);

    void Drop(IntPtr hwnd, bool closed)
    {
        var index = tabs.IndexOf(hwnd);
        if (index < 0)
            return;

        tabs.RemoveAt(index);
        stashed.Remove(hwnd);
        identities.Remove(hwnd);

        if (hwnd == active)
        {
            if (closed || tabs.Count == 0)
                active = IntPtr.Zero;
            if (tabs.Count > 0 && !closeTimer.Enabled)
                Activate(tabs[Math.Min(index, tabs.Count - 1)], focus: closed);
        }

        PlaceStrip();
        strip.Invalidate();
    }

    public void Float(IntPtr hwnd, Rectangle bounds)
    {
        if (!tabs.Contains(hwnd))
        {
            tabs.Add(hwnd);
            identities[hwnd] = TitleParser.Identity(Win.Title(hwnd));
        }
        active = hwnd;
        settling = IntPtr.Zero;
        manager.Transitions.Hold(hwnd, QuietMilliseconds);
        Unstash(hwnd);
        strip.Attach(hwnd);
        if (Native.IsIconic(hwnd) || Native.IsZoomed(hwnd))
            Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
        frame = bounds;
        Win.SetVisibleBounds(hwnd, frame, Native.HWND_TOP, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        Native.SetForegroundWindow(hwnd);
        PlaceStrip();
        strip.Invalidate();
    }

    public void Insert(IntPtr hwnd, int index)
    {
        tabs.Insert(Math.Clamp(index, 0, tabs.Count), hwnd);
        identities[hwnd] = TitleParser.Identity(Win.Title(hwnd));
        SaveOrder();
        Activate(hwnd, focus: true);
    }

    public bool AcceptsDrop(Point cursor)
    {
        var bounds = strip.ScreenBounds;
        if (active == IntPtr.Zero || bounds.IsEmpty)
            return false;
        return Rectangle.Inflate(bounds, 0, Dip(8, Win.Scale(active))).Contains(cursor);
    }

    public Size RestoredSize(IntPtr hwnd)
    {
        var work = Win.WorkArea(hwnd);
        var size = frame.IsEmpty ? new Size(work.Width * 2 / 3, work.Height * 2 / 3) : frame.Size;
        return new Size(Math.Min(size.Width, work.Width), Math.Min(size.Height, work.Height));
    }

    void PlaceStrip()
    {
        if (active == IntPtr.Zero || !Native.IsWindowVisible(active))
        {
            strip.HideStrip();
            return;
        }
        if (Native.IsIconic(active))
            return;

        var zoomed = Native.IsZoomed(active);
        var window = zoomed ? Win.WorkArea(active) : Win.VisibleBounds(active);
        if (!zoomed && window == Win.MonitorBounds(active))
        {
            strip.HideStrip();
            return;
        }

        var scale = Win.Scale(active);
        var bar = Bar(zoomed, scale);
        var available = window.Width - bar.RightInset;
        var bounds = new Rectangle(window.Left, window.Top + bar.Top, Math.Max(0, available), bar.Height);

        strip.Attach(active);
        strip.Place(bounds, scale, bar.LeftInset);
        ScheduleMeasure();
    }

    public TitleBar Bar(bool zoomed, float scale) =>
        manager.TitleBars.TryGetValue(zoomed, out var measured) ? measured : new TitleBar(
            zoomed ? 0 : 1,
            Dip(DefaultTitleBarHeightDip, scale),
            Dip(DefaultLeftInsetDip, scale),
            Dip(DefaultRightInsetDip, scale),
            default);

    public void ScheduleMeasure()
    {
        failedMeasurements = 0;
        measureTimer.Stop();
        measureTimer.Start();
    }

    void MeasureTitleBar()
    {
        if (active == IntPtr.Zero || !Native.IsWindowVisible(active) || Native.IsIconic(active))
            return;
        var zoomed = Native.IsZoomed(active);
        var window = zoomed ? Win.WorkArea(active) : Win.VisibleBounds(active);
        if (TitleBarProbe.Measure(active, window, Win.Scale(active), strip.TabAreaScreenBounds) is not { } bar)
        {
            if (++failedMeasurements < MeasureAttempts)
                measureTimer.Start();
            return;
        }
        manager.RememberColors(active, Native.GetForegroundWindow() == active, bar.Colors);
        strip.ApplyTheme(bar.Colors);
        var geometry = bar with { Colors = default };
        if (manager.TitleBars.TryGetValue(zoomed, out var known) && known == geometry)
        {
            geometryChanges = 0;
            return;
        }
        manager.TitleBars[zoomed] = geometry;
        PlaceStrip();
        if (++geometryChanges >= MeasureAttempts)
            measureTimer.Stop();
    }

    static int Dip(int value, float scale) => (int)Math.Round(value * scale);

    void SortTabs()
    {
        foreach (var hwnd in tabs)
            identities[hwnd] = TitleParser.Identity(Win.Title(hwnd));
        var sorted = tabs.OrderBy(hwnd => manager.Rank(identities[hwnd])).ToList();
        tabs.Clear();
        tabs.AddRange(sorted);
    }

    void SaveOrder() =>
        manager.SaveOrder(tabs.Select(hwnd => identities.GetValueOrDefault(hwnd) ?? TitleParser.Identity(Win.Title(hwnd))));
}
