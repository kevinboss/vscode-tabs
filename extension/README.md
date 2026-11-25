# VS Code Tabs

Groups VS Code windows into macOS-style tabs in the title bar on Windows.

## Usage

When VS Code starts, the extension launches a background helper (`vscode-tabs.exe`) that places open VS Code windows into a single tabbed window. New VS Code windows open as tabs in the focused group.

- Click a tab to switch to that window. Hover over a tab to see the full window title.
- Click the close button on a tab, or middle-click the tab, to close that window.
- Click the `+` button to open a new VS Code window in the current group.
- Press `Ctrl+Tab` and `Ctrl+Shift+Tab` to cycle forward and backward through window tabs. These shortcuts also work from the integrated terminal once the settings prompt is applied.
- Drag a tab sideways inside the tab bar to reorder tabs. Tab order is saved across sessions.
- Drag a tab out of the tab bar and drop it on the desktop to detach it into a separate window.
- Drag a tab onto another VS Code window's tab bar to move it into that group.
- Press `Esc` while dragging to cancel.

Empty space in the title bar passes mouse clicks through to VS Code, so dragging or double-clicking the title bar still moves and maximizes the window.

The tab bar matches the active VS Code theme's title bar color (light or dark) and hides when the window enters fullscreen.

A "VS Code Tabs" icon sits in the Windows system tray while the helper is running. Right-click it and select Exit to stop the helper and restore all grouped windows to the desktop and taskbar. The helper also exits on its own 10 seconds after the last VS Code window closes.

## Requirements

- Windows x64 (Windows 10 or Windows 11)
- VS Code 1.90.0 or later

The bundled `vscode-tabs.exe` is self-contained and does not require a separate .NET runtime.

## Settings

### Required VS Code settings

On startup, the extension checks four settings and prompts to update any that do not match. Choose Apply to update your user settings, or Don't Ask Again to dismiss the prompt.

- `window.menuBarVisibility`: set to `"compact"`. Moves the menu bar into the menu button on the left so the title bar has horizontal space for tabs.
- `window.commandCenter`: set to `false`. Removes the search box in the middle of the title bar so it does not overlap the tab bar.
- `window.title`: must include `${rootPath}` (applied as `${dirty}${activeEditorShort}${separator}${rootPath}`). The helper reads the window title to display the folder name and its parent folder on each tab. It also uses that path to remember tab order.
- `terminal.integrated.commandsToSkipShell`: adds `vscodeTabs.nextTab` and `vscodeTabs.previousTab`. Prevents the integrated terminal from capturing `Ctrl+Tab` and `Ctrl+Shift+Tab` so those keys switch window tabs instead.

### Extension settings

- `vscodeTabs.executable`: path to a custom `vscode-tabs.exe`. Leave empty (the default) to use the copy bundled with the extension.

### Keyboard shortcuts

The extension contributes two commands: `VS Code Tabs: Next Window Tab` (`vscodeTabs.nextTab`) and `VS Code Tabs: Previous Window Tab` (`vscodeTabs.previousTab`). They are bound to `Ctrl+Tab` and `Ctrl+Shift+Tab` on Windows outside Quick Open. You can change or remove these bindings in VS Code Keyboard Shortcuts. If the helper is not running, the commands fall back to VS Code's built-in recent editor switcher.

## Known limitations

- Windows x64 only. The extension does nothing on macOS or Linux, and the Marketplace package targets `win32-x64`.
- Only detects stable VS Code (`Code.exe`). VS Code Insiders (`Code - Insiders.exe`) and forks are not grouped.
- Replaces the default `Ctrl+Tab` and `Ctrl+Shift+Tab` editor switcher while the helper is running unless rebound in Keyboard Shortcuts.
- Tab labels parse Windows drive-letter paths (such as `C:\project\app`). Remote workspaces and windows without a local folder path fall back to the window title text without a parent folder label.
- Tabs shrink equally to fit the title bar when many windows are open. There is no horizontal scrolling.
- The tab bar is hidden in fullscreen mode.
- Inactive tabs in a group are parked off-screen and hidden from the Windows taskbar. If `vscode-tabs.exe` is killed forcefully instead of closed from the tray menu, those windows remain off-screen until the helper starts again.
- Windows on other Windows virtual desktops are ignored while cloaked.
