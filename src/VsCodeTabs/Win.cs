using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VsCodeTabs;

internal readonly record struct FrameMargins(int Left, int Top, int Right, int Bottom);

internal static class Win
{
    static readonly Dictionary<uint, string?> processPaths = [];
    static readonly Dictionary<IntPtr, FrameMargins> margins = [];
    static FrameMargins lastMargins;

    public static bool IsVsCodeMain(IntPtr hwnd, bool includeStashed = false)
    {
        if (!Native.IsWindow(hwnd) || ClassName(hwnd) != "Chrome_WidgetWin_1")
            return false;
        if (Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero)
            return false;
        var style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64();
        if ((style & Native.WS_THICKFRAME) == 0 || (IsStashed(hwnd) && !includeStashed))
            return false;
        if (IsCloaked(hwnd))
            return false;
        return string.Equals(Path.GetFileName(ProcessPath(hwnd)), "Code.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStashed(IntPtr hwnd) =>
        (Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() & Native.WS_EX_TOOLWINDOW) != 0;

    public static void SetToolWindow(IntPtr hwnd, bool value)
    {
        var exStyle = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        exStyle = value ? exStyle | Native.WS_EX_TOOLWINDOW : exStyle & ~(long)Native.WS_EX_TOOLWINDOW;
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(exStyle));
    }

    public static Rectangle OuterBounds(IntPtr hwnd)
    {
        Native.GetWindowRect(hwnd, out var rect);
        return rect.ToRectangle();
    }

    public static string? ProcessPath(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var processId);
        if (processPaths.TryGetValue(processId, out var path))
            return path;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            path = process.MainModule?.FileName;
        }
        catch (Exception)
        {
            path = null;
        }
        processPaths[processId] = path;
        return path;
    }

    public static string Title(IntPtr hwnd)
    {
        var buffer = new StringBuilder(Native.GetWindowTextLength(hwnd) + 1);
        Native.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static bool IsCloaked(IntPtr hwnd) =>
        Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    public static float Scale(IntPtr hwnd)
    {
        var dpi = Native.GetDpiForWindow(hwnd);
        return dpi == 0 ? 1f : dpi / 96f;
    }

    public static Rectangle WorkArea(IntPtr hwnd) => MonitorInfo(hwnd).rcWork.ToRectangle();

    public static Rectangle MonitorBounds(IntPtr hwnd) => MonitorInfo(hwnd).rcMonitor.ToRectangle();

    static Native.MONITORINFO MonitorInfo(IntPtr hwnd)
    {
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST), ref info);
        return info;
    }

    public static Rectangle VisibleBounds(IntPtr hwnd)
    {
        Native.GetWindowRect(hwnd, out var outer);
        var measurable = Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd) && !Native.IsZoomed(hwnd);
        if (measurable && Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT inner, Marshal.SizeOf<Native.RECT>()) == 0)
        {
            lastMargins = margins[hwnd] = new FrameMargins(inner.Left - outer.Left, inner.Top - outer.Top, outer.Right - inner.Right, outer.Bottom - inner.Bottom);
            return inner.ToRectangle();
        }
        var known = MarginsOf(hwnd);
        return Rectangle.FromLTRB(outer.Left + known.Left, outer.Top + known.Top, outer.Right - known.Right, outer.Bottom - known.Bottom);
    }

    public static void SetVisibleBounds(IntPtr hwnd, Rectangle bounds, IntPtr insertAfter, uint flags)
    {
        var known = MarginsOf(hwnd);
        Native.SetWindowPos(
            hwnd,
            insertAfter,
            bounds.Left - known.Left,
            bounds.Top - known.Top,
            bounds.Width + known.Left + known.Right,
            bounds.Height + known.Top + known.Bottom,
            flags);
    }

    public static void Forget(IntPtr hwnd) => margins.Remove(hwnd);

    static FrameMargins MarginsOf(IntPtr hwnd) => margins.TryGetValue(hwnd, out var known) ? known : lastMargins;

    static string ClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(64);
        Native.GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }
}
