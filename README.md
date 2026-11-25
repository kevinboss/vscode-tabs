# VS Code Tabs

A Windows-only VS Code extension that groups VS Code windows into macOS-style tabs in the title bar.

Install from the [Visual Studio Code Marketplace](https://marketplace.visualstudio.com/items?itemName=kevinboss.vscode-tabs). User documentation is in [extension/README.md](extension/README.md).

## How it works

The project has two parts: a VS Code extension (`extension/extension.js`) and a .NET WinForms helper (`src/VsCodeTabs`, compiled to `vscode-tabs.exe`).

### Extension

On Windows (`process.platform === 'win32'`), the extension activates on `onStartupFinished`:

- Checks four VS Code settings (`window.menuBarVisibility`, `window.commandCenter`, `window.title`, `terminal.integrated.commandsToSkipShell`) and offers to apply them globally.
- Registers `vscodeTabs.nextTab` (`Ctrl+Tab`) and `vscodeTabs.previousTab` (`Ctrl+Shift+Tab`).
- Spawns `vscode-tabs.exe` as a detached, hidden background process using `vscodeTabs.executable` if set, or `extension/bin/vscode-tabs.exe` otherwise.

### Inter-process communication

The extension and the helper talk over a per-user Windows named pipe: `\\.\pipe\vscode-tabs-<username>`. Each connection sends a single newline-terminated command to the helper's `CommandPipe` server:

- `next` and `previous`: sent by `vscodeTabs.nextTab` and `vscodeTabs.previousTab` to cycle tabs in the focused group. If connecting to the pipe fails, the extension falls back to VS Code's built-in editor switcher commands.
- `closing <folder>`: sent from `deactivate()` with the window's first workspace folder path so the helper suppresses DWM close animations while another tab takes over.
- `handover`: sent by a newer version of `vscode-tabs.exe` on startup to take over from an older running instance without restoring stashed windows to the desktop.

### Helper (`vscode-tabs.exe`)

The helper is a single-instance .NET 10 WinForms application guarded by the `Local\vscode-tabs` mutex.

- Window tracking: subscribes to Win32 window events with `SetWinEventHook` and matches top-level `Chrome_WidgetWin_1` windows owned by `Code.exe`.
- Tab strip overlay: attaches a borderless layered child `Form` (`TabStrip`) to the active VS Code window with `SetParent`. `TitleBarProbe` captures the window via `PrintWindow` to measure title bar height, left and right insets, and background color. `Palette` derives tab colors from that background.
- Stashing inactive tabs: only one window per `TabGroup` stays visible at the group's bounds. Inactive windows in the group receive `WS_EX_TOOLWINDOW`, are removed from the taskbar via `ITaskbarList.DeleteTab`, and are parked below the virtual screen.
- Dragging and detaching: dragging within the tab bar reorders tabs. Dragging outside shows a translucent `TabGhost` window. Dropping on another group's tab bar transfers the tab, and dropping elsewhere detaches it into a new `TabGroup`.
- State and lifecycle: tab order (up to 100 entries) is saved in `%LOCALAPPDATA%\vscode-tabs\order.txt`, and unhandled exceptions are logged to `%LOCALAPPDATA%\vscode-tabs\error.log`. A tray icon provides an Exit action that restores all stashed windows. The process exits after 10 seconds with no tracked windows.

## Building from source

Prerequisites:

- .NET 10 SDK (`net10.0-windows`)
- Node.js and npm

Install packaging dependencies in `extension/`:

```sh
cd extension
npm install
```

Build the helper binary using the `vscode:prepublish` script:

```sh
npm run vscode:prepublish
```

This runs `dotnet publish ../src/VsCodeTabs` for `win-x64` as a self-contained, compressed single-file executable and outputs `extension/bin/vscode-tabs.exe`. The assembly version is read from `extension/package.json`.

Package the `win32-x64` VSIX:

```sh
npm run package
```

`vsce package --target win32-x64 --no-dependencies` runs `vscode:prepublish` automatically before producing the `.vsix` file.

For local testing against a custom build of the helper, point the `vscodeTabs.executable` setting at your compiled `vscode-tabs.exe`.

## Repository layout

```text
extension/
  extension.js        Extension entry point, settings prompt, pipe client, helper launcher
  package.json        Extension manifest, commands, keybindings, packaging scripts
  README.md           Marketplace documentation
  icon.png            Extension icon
  LICENSE             GPL-3.0 license text
  .vscodeignore       Files excluded from the VSIX package
src/VsCodeTabs/
  VsCodeTabs.csproj   .NET 10 WinForms project (reads version from extension/package.json)
  Program.cs          Entry point, single-instance mutex, version handover, tray icon
  CommandPipe.cs      Per-user named pipe server and handover client
  TabManager.cs       WinEvent hooks, tab groups, drag-and-drop, saved tab order, idle exit
  TabGroup.cs         Window stashing, activation, strip placement, focus tracking
  TabStrip.cs         Title bar overlay rendering, mouse handling, click-through
  TabGhost.cs         Translucent floating preview shown while dragging a tab out
  TitleBarProbe.cs    PrintWindow capture to detect title bar geometry and theme color
  TitleParser.cs      Extracts folder name, parent folder, and identity from window titles
  Palette.cs          Derives dark or light tab colors from the sampled title bar color
  Transitions.cs      Temporarily disables DWM window animations during tab switches
  Taskbar.cs          ITaskbarList COM wrapper for hiding and restoring taskbar buttons
  Win.cs              Window identification, bounds, DPI, and process path helpers
  Native.cs           Win32 and DWM P/Invoke declarations
  app.manifest        Windows compatibility manifest
  app.ico             Tray and executable icon
LICENSE               GPL-3.0 license text
README.md             This file
```

## License

Licensed under the GNU General Public License v3.0 or later (`GPL-3.0-or-later`). See [LICENSE](LICENSE).
