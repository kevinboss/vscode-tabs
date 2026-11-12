using System.Runtime.InteropServices;

namespace VsCodeTabs;

[ComImport]
[Guid("56FDF342-FD6D-11d0-958A-006097C9A090")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarList
{
    void HrInit();

    void AddTab(IntPtr hwnd);

    void DeleteTab(IntPtr hwnd);

    void ActivateTab(IntPtr hwnd);

    void SetActiveAlt(IntPtr hwnd);
}

[ComImport]
[Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
internal class TaskbarList;

internal static class Taskbar
{
    static readonly ITaskbarList list = Create();

    public static void Add(IntPtr hwnd) => list.AddTab(hwnd);

    public static void Remove(IntPtr hwnd) => list.DeleteTab(hwnd);

    static ITaskbarList Create()
    {
        var created = (ITaskbarList)new TaskbarList();
        created.HrInit();
        return created;
    }
}
