namespace VsCodeTabs;

internal sealed class Transitions : IDisposable
{
    readonly Dictionary<IntPtr, long> quietUntil = [];
    readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };

    public Transitions() => timer.Tick += (_, _) => Restore(all: false);

    public void Hold(IntPtr hwnd, int milliseconds) =>
        Quiet(hwnd, Math.Max(Environment.TickCount64 + milliseconds, quietUntil.GetValueOrDefault(hwnd)));

    public void ReleaseAfter(IntPtr hwnd, int milliseconds) =>
        Quiet(hwnd, Environment.TickCount64 + milliseconds);

    public static void Enable(IntPtr hwnd) => Set(hwnd, enabled: true);

    public void Dispose()
    {
        Restore(all: true);
        timer.Dispose();
    }

    void Quiet(IntPtr hwnd, long until)
    {
        Set(hwnd, enabled: false);
        quietUntil[hwnd] = until;
        timer.Start();
    }

    void Restore(bool all)
    {
        var now = Environment.TickCount64;
        foreach (var hwnd in quietUntil.Where(entry => all || entry.Value <= now).Select(entry => entry.Key).ToList())
        {
            quietUntil.Remove(hwnd);
            if (Native.IsWindow(hwnd))
                Set(hwnd, enabled: true);
        }
        if (quietUntil.Count == 0)
            timer.Stop();
    }

    static void Set(IntPtr hwnd, bool enabled)
    {
        var disabled = enabled ? 0 : 1;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_TRANSITIONS_FORCEDISABLED, ref disabled, sizeof(int));
    }
}
