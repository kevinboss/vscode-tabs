# Usage

## Requirements

- Windows x64 (Windows 10 or Windows 11).
- VS Code 1.90.0 or later.

The bundled `vscode-tabs.exe` is self-contained and does not require a separate
.NET runtime. You can install the extension from the
[VS Code Marketplace](https://marketplace.visualstudio.com/items?itemName=kevinboss.vscode-tabs)
or download a `vscode-tabs-<version>.vsix` file from
[Releases](https://github.com/kevinboss/vscode-tabs/releases) and run
"Extensions: Install from VSIX…" in VS Code (or
`code --install-extension <file>.vsix`).

## Getting started

1. Apply the title bar settings. On startup the extension checks four VS Code
   settings (`window.menuBarVisibility`, `window.commandCenter`, `window.title`,
   `terminal.integrated.commandsToSkipShell`) and asks to update any that do not
   match. Choose "Apply" to update your user settings, or "Don't Ask Again" to
   dismiss the prompt.
2. Open your workspaces. The helper (`vscode-tabs.exe`) starts automatically with
   VS Code and groups open VS Code windows into a single tabbed window. New VS
   Code windows join the focused group as tabs.
3. Switch and arrange tabs. Click a tab or press `Ctrl+Tab` and `Ctrl+Shift+Tab`
   to move between windows. Drag a tab inside the bar to reorder it, drag it out
   to the desktop to detach it into its own window, or drop it onto another VS
   Code window's tab bar to move it into that group.
4. Stop the helper when needed. A VS Code Tabs icon sits in the Windows system
   tray while the helper is running. Right-click it and select "Exit" to stop
   the helper and restore all grouped windows to the desktop and taskbar. The
   helper also exits on its own 10 seconds after the last VS Code window closes.

## Features

- Title bar tab strip showing each window's folder name and parent folder, with
  the full window title on hover.
- Click a tab to switch to that window, click its close button or middle-click
  the tab to close it, and click `+` to open a new VS Code window in the current
  group.
- `Ctrl+Tab` and `Ctrl+Shift+Tab` cycle forward and backward through window tabs
  on Windows outside Quick Open, including from the integrated terminal once the
  settings prompt is applied. If the helper is not running, both shortcuts fall
  back to VS Code's built-in recent editor switcher.
- Drag a tab sideways to reorder it; tab order is remembered across sessions (up
  to 100 folders). Drag a tab out of the bar to detach it into a separate
  window, drop it on another VS Code window's tab bar to move it into that
  group, or press `Esc` while dragging to cancel.
- Empty space in the title bar passes mouse clicks through to VS Code, so
  dragging or double-clicking the title bar still moves and maximizes the
  window.
- Matches the active VS Code theme's title bar colour (light or dark), follows
  focus changes as VS Code repaints, and hides in fullscreen mode.
- Preserves grouped windows across extension updates: a newer `vscode-tabs.exe`
  takes over from the running copy without restoring stashed windows to the
  desktop.

## Required VS Code settings

On startup, the extension checks these four settings and prompts to update any
that do not match:

- `window.menuBarVisibility` — set to `"compact"`. Moves the menu bar into the
  menu button on the left so the title bar has horizontal space for tabs.
- `window.commandCenter` — set to `false`. Removes the search box in the middle
  of the title bar so it does not overlap the tab bar.
- `window.title` — must include `${rootPath}` (applied as
  `${dirty}${activeEditorShort}${separator}${rootPath}`). The helper reads the
  window title to display the folder name and its parent folder on each tab, and
  uses that path to remember tab order.
- `terminal.integrated.commandsToSkipShell` — adds `vscodeTabs.nextTab` and
  `vscodeTabs.previousTab`. Keeps the integrated terminal from capturing
  `Ctrl+Tab` and `Ctrl+Shift+Tab` so those keys switch window tabs instead.

## Extension settings

- `vscodeTabs.executable` — path to a custom `vscode-tabs.exe`. Leave empty (the
  default) to use the copy bundled with the extension.

## Keyboard shortcuts

The extension contributes "VS Code Tabs: Next Window Tab" (`vscodeTabs.nextTab`)
and "VS Code Tabs: Previous Window Tab" (`vscodeTabs.previousTab`), bound to
`Ctrl+Tab` and `Ctrl+Shift+Tab` on Windows outside Quick Open. Change or remove
these bindings in VS Code Keyboard Shortcuts. If the helper is not running, the
commands fall back to VS Code's built-in recent editor switcher.

## Known limitations

- Windows x64 only. The extension does nothing on macOS or Linux, and the
  Marketplace package targets `win32-x64`.
- Only detects stable VS Code (`Code.exe`). VS Code Insiders
  (`Code - Insiders.exe`) and forks are not grouped.
- Replaces the default `Ctrl+Tab` and `Ctrl+Shift+Tab` editor switcher while the
  helper is running unless rebound in Keyboard Shortcuts.
- Tab labels parse Windows drive-letter paths (such as `C:\project\app`). Remote
  workspaces and windows without a local folder path fall back to the window
  title text without a parent folder label.
- Tabs shrink equally to fit the title bar when many windows are open. There is
  no horizontal scrolling.
- The tab bar is hidden in fullscreen mode.
- Inactive tabs in a group are parked off-screen and hidden from the Windows
  taskbar. If `vscode-tabs.exe` is killed forcefully instead of closed from the
  tray menu, those windows remain off-screen until the helper starts again.
- Windows on other Windows virtual desktops are ignored while cloaked.
