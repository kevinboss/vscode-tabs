namespace VsCodeTabs;

internal sealed class TabManager : IDisposable
{
    const int IdleSecondsBeforeExit = 10;
    const int RememberedFolders = 100;
    const int GrabLimitDip = 100;
    const int NewWindowQuietMilliseconds = 15000;
    const int CloseQuietMilliseconds = 10000;

    static readonly string StateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vscode-tabs");

    readonly List<TabGroup> groups = [];
    readonly List<IntPtr> hooks = [];
    readonly Queue<(uint Event, IntPtr Hwnd)> pending = new();
    readonly Native.WinEventProc proc;
    readonly System.Windows.Forms.Timer pruneTimer = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer dragTimer = new() { Interval = 10 };
    readonly string orderPath = Path.Combine(StateDirectory, "order.txt");

    SynchronizationContext? context;
    List<string> savedOrder = [];
    TabGroup? focused;
    TabGroup? dragGroup;
    IntPtr dragWindow;
    Point dragGrab;
    TabGhost? ghost;
    TabGroup? dropTarget;
    (uint Event, IntPtr Hwnd) lastQueued;
    int idleSeconds;
    bool draining;
    bool drainPosted;
    bool handingOver;

    public TabManager()
    {
        proc = OnWinEvent;
        pruneTimer.Tick += (_, _) => Prune();
        dragTimer.Tick += (_, _) => DragTick();
    }

    public Dictionary<bool, TitleBar> TitleBars { get; } = [];

    public Transitions Transitions { get; } = new();

    public void Start()
    {
        var group = CreateGroup();
        _ = group.Strip.Handle;
        context = new WindowsFormsSynchronizationContext();
        savedOrder = File.Exists(orderPath) ? File.ReadAllLines(orderPath).ToList() : [];

        var windows = new List<IntPtr>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (Native.IsWindowVisible(hwnd) && Win.IsVsCodeMain(hwnd, includeStashed: true))
                windows.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        group.Load(windows);

        const uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
        hooks.Add(Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, proc, 0, 0, flags));
        hooks.Add(Native.SetWinEventHook(Native.EVENT_OBJECT_CREATE, Native.EVENT_OBJECT_NAMECHANGE, IntPtr.Zero, proc, 0, 0, flags));
        pruneTimer.Start();

        var shown = windows.Where(hwnd => !Win.IsStashed(hwnd)).ToList();
        var foreground = Native.GetForegroundWindow();
        var first = shown.Contains(foreground) ? foreground : shown.Concat(windows).FirstOrDefault();
        if (first != IntPtr.Zero)
            group.Activate(first, focus: false);
        focused = group;
    }

    public void Cycle(int step) => (GroupOf(Native.GetForegroundWindow()) ?? focused)?.Cycle(step);

    public void MarkFocused(TabGroup group) => focused = group;

    public void RestoreAll()
    {
        foreach (var group in groups)
            group.RestoreAll();
    }

    public void Dispose()
    {
        foreach (var hook in hooks)
            Native.UnhookWinEvent(hook);
        hooks.Clear();
        EndDrag();
        pruneTimer.Dispose();
        dragTimer.Dispose();
        foreach (var group in groups)
        {
            if (handingOver)
                group.Release();
            else
                group.Dispose();
        }
        groups.Clear();
        Transitions.Dispose();
    }

    public void HandOver()
    {
        handingOver = true;
        Application.Exit();
    }

    public void Closing(string folder)
    {
        var foreground = Native.GetForegroundWindow();
        var identity = folder.TrimEnd('\\', '/');
        List<IntPtr> matches = identity.Length == 0 ? [] : groups.SelectMany(group => group.Tabs)
            .Where(hwnd => string.Equals(TitleParser.Identity(Win.Title(hwnd)), identity, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var hwnd = matches.Contains(foreground) ? foreground : matches.FirstOrDefault(foreground);
        if (GroupOf(hwnd) is { Tabs.Count: > 1 })
            Transitions.Hold(hwnd, CloseQuietMilliseconds);
    }

    public int Rank(string identity)
    {
        var index = savedOrder.IndexOf(identity);
        return index < 0 ? int.MaxValue : index;
    }

    public void SaveOrder(IEnumerable<string> identities)
    {
        var current = identities.ToList();
        savedOrder = current.Concat(savedOrder.Except(current)).Distinct().Take(RememberedFolders).ToList();
        Directory.CreateDirectory(StateDirectory);
        File.WriteAllLines(orderPath, savedOrder);
    }

    public void BeginDrag(TabGroup group, IntPtr hwnd, Point grab)
    {
        EndDrag();
        var tab = group.Strip.TabScreenBounds(hwnd);
        if (tab.IsEmpty)
            return;
        dragGroup = group;
        dragWindow = hwnd;
        dragGrab = grab;
        ghost = new TabGhost(TitleParser.Label(Win.Title(hwnd)), group.Strip.Colors, group.Strip.DpiScale, tab.Size);
        dragTimer.Start();
    }

    TabGroup? GroupOf(IntPtr hwnd) => groups.FirstOrDefault(group => group.Contains(hwnd));

    TabGroup Target() =>
        focused is not null && groups.Contains(focused) ? focused : groups.FirstOrDefault() ?? CreateGroup();

    TabGroup CreateGroup()
    {
        var group = new TabGroup(this);
        groups.Add(group);
        return group;
    }

    void DropIfEmpty(TabGroup group)
    {
        if (group.Tabs.Count > 0 || !groups.Remove(group))
            return;
        if (focused == group)
            focused = null;
        group.Dispose();
    }

    void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != Native.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero)
            return;

        if (evt == Native.EVENT_OBJECT_CREATE)
        {
            if (groups.Any(group => group.Tabs.Count > 0) && Win.IsVsCodeFrame(hwnd))
                Transitions.Hold(hwnd, NewWindowQuietMilliseconds);
            return;
        }

        switch (evt)
        {
            case Native.EVENT_SYSTEM_FOREGROUND:
                break;
            case Native.EVENT_OBJECT_SHOW:
                if (GroupOf(hwnd) is not null)
                    return;
                break;
            case Native.EVENT_SYSTEM_MOVESIZESTART:
            case Native.EVENT_SYSTEM_MINIMIZESTART:
            case Native.EVENT_SYSTEM_MINIMIZEEND:
            case Native.EVENT_OBJECT_DESTROY:
            case Native.EVENT_OBJECT_LOCATIONCHANGE:
            case Native.EVENT_OBJECT_NAMECHANGE:
                if (GroupOf(hwnd) is null)
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
        var group = GroupOf(hwnd);
        switch (evt)
        {
            case Native.EVENT_SYSTEM_FOREGROUND:
                if (Native.GetForegroundWindow() != hwnd)
                    return;
                if (group is not null)
                {
                    focused = group;
                    group.OnForeground(hwnd);
                }
                else if (Win.IsVsCodeMain(hwnd))
                {
                    focused = Target();
                    focused.Activate(hwnd, focus: false);
                }
                else
                {
                    foreach (var each in groups)
                        each.ScheduleMeasure();
                }
                break;

            case Native.EVENT_OBJECT_SHOW:
                if (group is null && Native.IsWindowVisible(hwnd) && Win.IsVsCodeMain(hwnd))
                    Target().Activate(hwnd, focus: true);
                break;

            case Native.EVENT_OBJECT_DESTROY:
                if (group is not null)
                {
                    group.Remove(hwnd);
                    DropIfEmpty(group);
                }
                break;

            case Native.EVENT_SYSTEM_MOVESIZESTART:
                group?.OnMoveSizeStart();
                break;

            case Native.EVENT_OBJECT_LOCATIONCHANGE:
                group?.OnLocationChange(hwnd);
                break;

            case Native.EVENT_SYSTEM_MINIMIZESTART:
            case Native.EVENT_SYSTEM_MINIMIZEEND:
                group?.OnMinimize(hwnd);
                break;

            case Native.EVENT_OBJECT_NAMECHANGE:
                group?.OnNameChange(hwnd);
                break;
        }
    }

    void Prune()
    {
        foreach (var group in groups.ToList())
        {
            foreach (var hwnd in group.Tabs.Where(hwnd => !Native.IsWindow(hwnd)).ToList())
                group.Remove(hwnd);
            DropIfEmpty(group);
        }

        idleSeconds = groups.Sum(group => group.Tabs.Count) == 0 ? idleSeconds + 1 : 0;
        if (idleSeconds >= IdleSecondsBeforeExit)
            Application.Exit();
    }

    void DragTick()
    {
        if (dragGroup is null || !groups.Contains(dragGroup) || !dragGroup.Contains(dragWindow)
            || !Native.IsWindow(dragWindow) || KeyDown(Native.VK_ESCAPE))
        {
            EndDrag();
            return;
        }

        Native.GetCursorPos(out var cursor);
        if (!KeyDown(SystemInformation.MouseButtonsSwapped ? Native.VK_RBUTTON : Native.VK_LBUTTON))
        {
            Drop(dragGroup, cursor);
            EndDrag();
            return;
        }

        if (InTabBar(dragGroup, cursor))
        {
            ShowDropTarget(null, -1);
            ghost?.HideGhost();
            dragGroup.MoveTo(dragWindow, dragGroup.Strip.IndexAtScreen(cursor.X, insert: false));
            return;
        }

        var target = DropTargetAt(dragGroup, cursor);
        ShowDropTarget(target, target?.Strip.IndexAtScreen(cursor.X, insert: true) ?? -1);
        ghost?.MoveTo(new Point(cursor.X - dragGrab.X, cursor.Y - dragGrab.Y));
    }

    void Drop(TabGroup source, Point cursor)
    {
        if (InTabBar(source, cursor))
            return;

        var target = DropTargetAt(source, cursor);
        if (target is null)
        {
            Detach(source, cursor);
            return;
        }

        var index = target.Strip.IndexAtScreen(cursor.X, insert: true);
        source.Release(dragWindow);
        DropIfEmpty(source);
        target.Insert(dragWindow, index);
        focused = target;
    }

    void EndDrag()
    {
        dragTimer.Stop();
        ShowDropTarget(null, -1);
        ghost?.Dispose();
        ghost = null;
        dragGroup = null;
        dragWindow = IntPtr.Zero;
    }

    static bool KeyDown(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;

    bool InTabBar(TabGroup group, Point cursor)
    {
        var strip = group.Strip.ScreenBounds;
        if (strip.IsEmpty)
            return false;
        var window = Win.VisibleBounds(dragWindow);
        var reach = strip.Height / 2;
        return Rectangle.FromLTRB(window.Left, strip.Top - reach, window.Right, strip.Bottom + reach).Contains(cursor);
    }

    TabGroup? DropTargetAt(TabGroup source, Point cursor) =>
        groups.FirstOrDefault(other => other != source && other.AcceptsDrop(cursor));

    void ShowDropTarget(TabGroup? target, int index)
    {
        if (dropTarget is not null && dropTarget != target && groups.Contains(dropTarget))
            dropTarget.Strip.SetDropIndex(-1);
        dropTarget = target;
        target?.Strip.SetDropIndex(index);
    }

    void Detach(TabGroup source, Point cursor)
    {
        var scale = Win.Scale(dragWindow);
        var bar = source.Bar(zoomed: false, scale);
        var grabX = bar.LeftInset + Dip(4, scale) + Math.Clamp(dragGrab.X, 0, Dip(GrabLimitDip, scale));
        var grabY = bar.Top + bar.Height / 2;
        var size = source.RestoredSize(dragWindow);
        var bounds = new Rectangle(cursor.X - grabX, cursor.Y - grabY, size.Width, size.Height);

        var target = source.Tabs.Count > 1 ? CreateGroup() : source;
        if (target != source)
            source.Release(dragWindow);
        target.Float(dragWindow, bounds);
        focused = target;
    }

    static int Dip(int value, float scale) => (int)Math.Round(value * scale);
}
