# Design decisions

## Separate native helper

VS Code's extension API does not expose Win32 window handles, title bar
rendering, or taskbar state, and each window runs its own extension host. A
single WinForms process (`vscode-tabs.exe`, guarded by the `Local\vscode-tabs`
mutex) watches all top-level `Chrome_WidgetWin_1` windows owned by `Code.exe`
through `SetWinEventHook`, attaches a borderless layered child `TabStrip` to the
active window with `SetParent`, and exits after 10 seconds with no tracked
windows.
A group only holds windows of one `Code.exe`, so a user install, a system install and a portable copy each get their own groups.

## Closing a window closes its group

When a group's active window hides without the tab strip having closed it (the title bar X, `Alt+F4`, `Ctrl+Shift+W` or File > Close Window), the helper posts `WM_CLOSE` to every other tab in the group.
It reacts to the hide and not to the extension's `deactivate()`, which also runs when a window reloads.
The window closes with the normal Windows animation.
On hide, Windows gives focus to a parked window, and the closed window's destruction would normally bring its neighbour forward, so for the next 15 seconds the group neither follows focus to parked tabs nor replaces the closed tab.
A save prompt from one of the closing windows ends that period, and its window comes back with the dialog.
A tab still open after 15 seconds is brought back as the group's active window.

## Parking inactive windows off-screen and off the taskbar

Each `TabGroup` keeps only its active window at the group's bounds. Inactive
tabs receive `WS_EX_TOOLWINDOW`, are removed from the taskbar through
`ITaskbarList.DeleteTab`, and are parked 1000 pixels below the virtual screen at
`HWND_BOTTOM`. DWM window transitions (`DWMWA_TRANSITIONS_FORCEDISABLED`) are
disabled briefly during switches, opens, and closes so windows swap in place
instead of animating from off-screen.

## Named pipe per user

The extension and helper communicate over `\\.\pipe\vscode-tabs-<username>` (`PipeOptions.CurrentUserOnly`).
Most commands are one newline-terminated line per connection: `next`, `previous`, `theme`, or `handover`.
Keeping these connectionless lets any VS Code window reach the helper without maintaining sockets.
If connecting fails, `Ctrl+Tab` and `Ctrl+Shift+Tab` fall back to VS Code's built-in recent editor switcher.

The helper also needs to ask an extension host to act, so each extension host keeps one connection open, which it starts with `listen`.
The `+` button sends `new-window <id> <folder>` to every listener.
The extension host whose first workspace folder or workspace file matches `<folder>` (or, for a window without a local folder, the focused one) replies `accepted <id>` and runs `workbench.action.newWindow`, so the new window opens in the same VS Code instance and profile as the group.
If no listener accepts within a second, the helper starts the active window's `Code.exe --new-window` instead.

## Handing over to a newer executable on update

When the extension updates while an older `vscode-tabs.exe` is still running,
the new process compares file versions (injected from `extension/package.json`
at build time). If its own version is newer, it sends `handover` over the pipe
and waits up to 5 seconds for the mutex. The older helper exits via `Release()`
instead of `Dispose()`, leaving stashed windows parked so the new instance
adopts them on startup instead of scattering every grouped window across the
desktop.

## Sampling the title bar colour

`TitleBarProbe` captures the active window with `PrintWindow`, finds the modal
title bar background pixel, and measures the height and left/right insets
occupied by VS Code's own controls. `Palette` derives dark or light tab shades
from that colour, and `TabGroup.FollowFocus` polls a pixel beside the strip
after focus changes so the bar switches between focused and unfocused colours
once VS Code repaints.
