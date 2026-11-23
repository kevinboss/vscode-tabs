using System.Diagnostics;

namespace VsCodeTabs;

internal sealed class TabGroup : IDisposable
{
    const int DefaultTitleBarHeightDip = 29;
    const int DefaultLeftInsetDip = 38;
    const int DefaultRightInsetDip = 280;
    const int IdleSecondsBeforeExit = 10;
    const int RememberedFolders = 100;
    const int MeasureAttempts = 10;
    const int ParkingGap = 1000;
    const int SettleMilliseconds = 2000;

    static readonly string StateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vscode-tabs");

    TabStrip strip;
    readonly List<IntPtr> tabs = [];
    readonly HashSet<IntPtr> stashed = [];
    readonly Dictionary<IntPtr, string> identities = [];
    readonly List<IntPtr> hooks = [];
    readonly Queue<(uint Event, IntPtr Hwnd)> pending = new();
    readonly Native.WinEventProc proc;
    readonly System.Windows.Forms.Timer pruneTimer = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer measureTimer = new() { Interval = 300 };
    readonly string orderPath = Path.Combine(StateDirectory, "order.txt");

    SynchronizationContext? context;
    readonly Dictionary<bool, TitleBar> titleBars = [];
    List<string> savedOrder = [];
    IntPtr active;
    Rectangle frame;
    IntPtr settling;
    long settlingUntil;
    (uint Event, IntPtr Hwnd) lastQueued;
    int idleSeconds;
    int failedMeasurements;
    bool draining;
    bool drainPosted;

    public TabGroup()
    {
        strip = new TabStrip(this);
        proc = OnWinEvent;
        pruneTimer.Tick += (_, _) => Prune();
        measureTimer.Tick += (_, _) =>
        {
            measureTimer.Stop();
            MeasureTitleBar();
        };
    }

    public IReadOnlyList<IntPtr> Tabs => tabs;

    public IntPtr Active => active;

    public void Start()
    {
        _ = strip.Handle;
        context = new WindowsFormsSynchronizationContext();
        savedOrder = File.Exists(orderPath) ? File.ReadAllLines(orderPath).ToList() : [];

        var shown = new List<IntPtr>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || !Win.IsVsCodeMain(hwnd, includeStashed: true))
                return true;
            if (Win.IsStashed(hwnd))
                stashed.Add(hwnd);
            else
                shown.Add(hwnd);
            tabs.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        SortTabs();

        const uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
        hooks.Add(Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, proc, 0, 0, flags));
        hooks.Add(Native.SetWinEventHook(Native.EVENT_OBJECT_DESTROY, Native.EVENT_OBJECT_NAMECHANGE, IntPtr.Zero, proc, 0, 0, flags));
        pruneTimer.Start();

        var foreground = Native.GetForegroundWindow();
        var first = shown.Contains(foreground) ? foreground : shown.Concat(stashed).FirstOrDefault();
        if (first != IntPtr.Zero)
            Activate(first, focus: false);
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

    public void NewTab()
    {
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
            }
            else if (!frame.IsEmpty)
                Win.SetVisibleBounds(hwnd, frame, active, Native.SWP_NOACTIVATE);
            else
                Native.ShowWindow(hwnd, Native.SW_SHOWMAXIMIZED);
        }
    }

    public void Dispose()
    {
        foreach (var hook in hooks)
            Native.UnhookWinEvent(hook);
        hooks.Clear();
        pruneTimer.Dispose();
        measureTimer.Dispose();
        RestoreAll();
        strip.Dispose();
    }

    void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != Native.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero)
            return;

        switch (evt)
        {
            case Native.EVENT_SYSTEM_FOREGROUND:
                break;
            case Native.EVENT_OBJECT_SHOW:
                if (tabs.Contains(hwnd))
                    return;
                break;
            case Native.EVENT_SYSTEM_MOVESIZESTART:
            case Native.EVENT_SYSTEM_MINIMIZESTART:
            case Native.EVENT_SYSTEM_MINIMIZEEND:
            case Native.EVENT_OBJECT_DESTROY:
            case Native.EVENT_OBJECT_LOCATIONCHANGE:
            case Native.EVENT_OBJECT_NAMECHANGE:
                if (!tabs.Contains(hwnd))
                    return;
                break;
            default:
                return;
        }

        if (pending.Count > 0 && lastQueued == (evt, hwnd))
            return;
        lastQueued = (evt, hwnd);
        pending.Enqueue(lastQueued);

        if (!drainPosted && context is not null)
        {
            drainPosted = true;
            context.Post(_ => Drain(), null);
        }
    }

    void Drain()
    {
        drainPosted = false;
        if (draining)
            return;
        draining = true;
        try
        {
            while (pending.TryDequeue(out var item))
                Handle(item.Event, item.Hwnd);
        }
        finally
        {
            draining = false;
        }
    }

    void Handle(uint evt, IntPtr hwnd)
    {
        switch (evt)
        {
            case Native.EVENT_SYSTEM_FOREGROUND:
                if (Native.GetForegroundWindow() != hwnd)
                    return;
                if (hwnd == active)
                    PlaceStrip();
                else if (tabs.Contains(hwnd) || Win.IsVsCodeMain(hwnd))
                    Activate(hwnd, focus: false);
                else if (active != IntPtr.Zero)
                    ScheduleMeasure();
                break;

            case Native.EVENT_OBJECT_SHOW:
                if (!tabs.Contains(hwnd) && Native.IsWindowVisible(hwnd) && Win.IsVsCodeMain(hwnd))
                    Activate(hwnd, focus: true);
                break;

            case Native.EVENT_OBJECT_DESTROY:
                Remove(hwnd);
                break;

            case Native.EVENT_SYSTEM_MOVESIZESTART:
                settling = IntPtr.Zero;
                break;

            case Native.EVENT_OBJECT_LOCATIONCHANGE:
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
                break;

            case Native.EVENT_SYSTEM_MINIMIZESTART:
            case Native.EVENT_SYSTEM_MINIMIZEEND:
                if (hwnd == active)
                    PlaceStrip();
                break;

            case Native.EVENT_OBJECT_NAMECHANGE:
                if (identities.GetValueOrDefault(hwnd) != TitleParser.Identity(Win.Title(hwnd)))
                {
                    SortTabs();
                    SaveOrder();
                    PlaceStrip();
                }
                strip.Invalidate();
                break;
        }
    }

    void Activate(IntPtr hwnd, bool focus)
    {
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
            tabs.Add(hwnd);
            SortTabs();
            SaveOrder();
            settling = hwnd;
            settlingUntil = Environment.TickCount64 + SettleMilliseconds;
        }
        active = hwnd;
        strip.Attach(hwnd);
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

        PlaceStrip();
        strip.Invalidate();
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

    void Remove(IntPtr hwnd)
    {
        var index = tabs.IndexOf(hwnd);
        if (index < 0)
            return;

        tabs.RemoveAt(index);
        stashed.Remove(hwnd);
        identities.Remove(hwnd);
        Win.Forget(hwnd);
        if (strip.IsDisposed)
            strip = new TabStrip(this);

        if (hwnd == active)
        {
            active = IntPtr.Zero;
            if (tabs.Count > 0)
                Activate(tabs[Math.Min(index, tabs.Count - 1)], focus: true);
        }

        PlaceStrip();
        strip.Invalidate();
    }

    void Prune()
    {
        foreach (var hwnd in tabs.Where(hwnd => !Native.IsWindow(hwnd)).ToList())
            Remove(hwnd);

        idleSeconds = tabs.Count == 0 ? idleSeconds + 1 : 0;
        if (idleSeconds >= IdleSecondsBeforeExit)
            Application.Exit();
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
        var bar = titleBars.TryGetValue(zoomed, out var measured) ? measured : new TitleBar(
            zoomed ? 0 : 1,
            Dip(DefaultTitleBarHeightDip, scale),
            Dip(DefaultLeftInsetDip, scale),
            Dip(DefaultRightInsetDip, scale),
            Color.Empty);
        var left = window.Left + bar.LeftInset;
        var available = window.Right - bar.RightInset - left;
        var bounds = new Rectangle(left, window.Top + bar.Top, Math.Min(available, strip.ContentWidth()), bar.Height);

        strip.Attach(active);
        strip.Place(bounds, scale);
        ScheduleMeasure();
    }

    void ScheduleMeasure()
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
        if (TitleBarProbe.Measure(active, window, Win.Scale(active), strip.ScreenBounds) is not { } bar)
        {
            if (++failedMeasurements < MeasureAttempts)
                measureTimer.Start();
            return;
        }
        strip.ApplyTheme(bar.Background);
        if (titleBars.TryGetValue(zoomed, out var known) && known == bar)
            return;
        titleBars[zoomed] = bar;
        PlaceStrip();
        measureTimer.Stop();
    }

    static int Dip(int value, float scale) => (int)Math.Round(value * scale);

    void SortTabs()
    {
        foreach (var hwnd in tabs)
            identities[hwnd] = TitleParser.Identity(Win.Title(hwnd));
        var sorted = tabs.OrderBy(hwnd => Rank(identities[hwnd])).ToList();
        tabs.Clear();
        tabs.AddRange(sorted);
    }

    int Rank(string identity)
    {
        var index = savedOrder.IndexOf(identity);
        return index < 0 ? int.MaxValue : index;
    }

    void SaveOrder()
    {
        var current = tabs.Select(hwnd => identities.GetValueOrDefault(hwnd) ?? TitleParser.Identity(Win.Title(hwnd))).ToList();
        savedOrder = current.Concat(savedOrder.Except(current)).Distinct().Take(RememberedFolders).ToList();
        Directory.CreateDirectory(StateDirectory);
        File.WriteAllLines(orderPath, savedOrder);
    }
}
