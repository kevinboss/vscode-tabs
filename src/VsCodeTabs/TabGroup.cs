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

    readonly TabManager manager;
    TabStrip strip;
    TabStrip spare;
    readonly List<IntPtr> tabs = [];
    readonly HashSet<IntPtr> stashed = [];
    readonly Dictionary<IntPtr, string> identities = [];
    readonly System.Windows.Forms.Timer measureTimer = new() { Interval = 300 };

    IntPtr active;
    Rectangle frame;
    IntPtr settling;
    long settlingUntil;
    int failedMeasurements;

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
    }

    public IReadOnlyList<IntPtr> Tabs => tabs;

    public IntPtr Active => active;

    public TabStrip Strip => strip;

    public bool Contains(IntPtr hwnd) => tabs.Contains(hwnd);

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
        Switch(tabs[(index + step + tabs.Count) % tabs.Count]);
    }

    public void Close(IntPtr hwnd)
    {
        if (hwnd != active)
            Activate(hwnd, focus: true);
        if (tabs.Count > 1)
            manager.Transitions.Hold(hwnd, CloseQuietMilliseconds);
        Native.PostMessage(hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
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

    public void NewTab()
    {
        manager.MarkFocused(this);
        var executable = active != IntPtr.Zero ? Win.ProcessPath(active) : null;
        Native.AllowSetForegroundWindow(Native.ASFW_ANY);
        var start = new ProcessStartInfo(executable ?? "code", "--new-window")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var name in start.Environment.Keys.Where(IsVsCodeInternal).ToList())
            start.Environment.Remove(name);
        Process.Start(start);
    }

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
        measureTimer.Dispose();
        strip.Dispose();
        spare.Dispose();
    }

    public void OnForeground(IntPtr hwnd)
    {
        if (hwnd == active)
            PlaceStrip();
        else
            Activate(hwnd, focus: false);
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
        PlaceStrip();
        strip.Invalidate();
    }

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
        if (!tabs.Contains(hwnd))
            return;
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
            if (tabs.Count > 0)
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
        Win.SetVisibleBounds(hwnd, frame, IntPtr.Zero, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
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
            Color.Empty);

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
        strip.ApplyTheme(bar.Background);
        if (manager.TitleBars.TryGetValue(zoomed, out var known) && known == bar)
            return;
        manager.TitleBars[zoomed] = bar;
        PlaceStrip();
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
