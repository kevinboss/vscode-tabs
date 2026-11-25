# Design decisions

## Separate native helper

VS Code's extension API does not expose Win32 window handles, title bar
rendering, or taskbar state, and each window runs its own extension host. A
single WinForms process (`vscode-tabs.exe`, guarded by the `Local\vscode-tabs`
mutex) watches all top-level `Chrome_WidgetWin_1` windows owned by `Code.exe`
through `SetWinEventHook`, attaches a borderless layered child `TabStrip` to the
active window with `SetParent`, and exits after 10 seconds with no tracked
windows.

## Parking inactive windows off-screen and off the taskbar

Each `TabGroup` keeps only its active window at the group's bounds. Inactive
tabs receive `WS_EX_TOOLWINDOW`, are removed from the taskbar through
`ITaskbarList.DeleteTab`, and are parked 1000 pixels below the virtual screen at
`HWND_BOTTOM`. DWM window transitions (`DWMWA_TRANSITIONS_FORCEDISABLED`) are
disabled briefly during switches, opens, and closes so windows swap in place
instead of animating from off-screen.

## Named pipe per user

The extension and helper communicate over `\\.\pipe\vscode-tabs-<username>`
(`PipeOptions.CurrentUserOnly`), sending one newline-terminated command per
connection: `next`, `previous`, `closing <folder>`, or `handover`. Keeping
commands connectionless lets any VS Code window reach the helper without
maintaining sockets; if connecting fails, `Ctrl+Tab` and `Ctrl+Shift+Tab` fall
back to VS Code's built-in recent editor switcher.

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
