<p align="center">
  <img src="extension/icon.png" width="120" alt="VS Code Tabs logo">
</p>

<h1 align="center">VS Code Tabs</h1>

<p align="center">
  Group VS Code windows into tabs in the title bar on Windows.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-512BD4?logo=dotnet&logoColor=white" alt=".NET">
  <a href="https://marketplace.visualstudio.com/items?itemName=kevinboss.vscode-tabs"><img src="https://vsmarketplacebadges.dev/version-short/kevinboss.vscode-tabs.svg" alt="VS Marketplace"></a>
  <a href="https://github.com/kevinboss/vscode-tabs/actions/workflows/release.yml"><img src="https://img.shields.io/github/actions/workflow/status/kevinboss/vscode-tabs/release.yml?label=Release" alt="Release"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/kevinboss/vscode-tabs" alt="License"></a>
</p>

> This project is being developed using AI development tools. However, every line of code is reviewed and approved by a human before being committed.

On Windows, every open VS Code window is its own taskbar entry and Alt+Tab stop; there are no window tabs.
VS Code Tabs groups open `Code.exe` windows behind a single tabbed title bar, keeping one window visible per group while the rest sit off-screen and off the taskbar.
A small VS Code extension configures the title bar, registers `Ctrl+Tab` and `Ctrl+Shift+Tab`, and launches a native .NET WinForms helper (`vscode-tabs.exe`) that tracks the windows and draws the tab strip.
Tabs can be reordered, dragged out into windows of their own, or dropped onto another group's tab bar.

The extension targets Windows x64 only.
User documentation lives in [extension/README.md](extension/README.md).

## Installing

1. **Install the extension.** Get **VS Code Tabs** from the [VS Code Marketplace](https://marketplace.visualstudio.com/items?itemName=kevinboss.vscode-tabs), or download `vscode-tabs-<version>.vsix` from [Releases](https://github.com/kevinboss/vscode-tabs/releases) and run "Extensions: Install from VSIX…" in VS Code (or `code --install-extension <file>.vsix`). The package bundles `vscode-tabs.exe` as a self-contained binary, so no separate .NET runtime is needed.
2. **Apply the title bar settings.** On first start the extension checks four settings (`window.menuBarVisibility`, `window.commandCenter`, `window.title`, `terminal.integrated.commandsToSkipShell`) and offers to update them in your user settings so the title bar has room for tabs and `Ctrl+Tab` works from the integrated terminal.

## Repository layout

- **`extension/`** — the VS Code extension (`extension.js`, `package.json`, `README.md`, `CHANGELOG.md`, `icon.png`, `LICENSE`, `.vscodeignore`). On Windows it activates on `onStartupFinished`, prompts for the four required settings, binds `Ctrl+Tab` and `Ctrl+Shift+Tab` to `vscodeTabs.nextTab` and `vscodeTabs.previousTab`, spawns `bin/vscode-tabs.exe` (or `vscodeTabs.executable` when set), and talks to the helper over the per-user named pipe.
- **`src/VsCodeTabs/`** — the .NET 10 WinForms helper (`VsCodeTabs.csproj`, `app.manifest`, `app.ico`, and the C# sources). Tracks `Code.exe` windows with `SetWinEventHook` (`TabManager.cs`, `Win.cs`), manages tab groups, stashing, and DWM transitions (`TabGroup.cs`, `Taskbar.cs`, `Transitions.cs`), draws the title bar overlay and drag ghost (`TabStrip.cs`, `TabGhost.cs`, `TitleBarProbe.cs`, `TitleParser.cs`, `Palette.cs`), and runs the single-instance mutex, tray icon, and named pipe (`Program.cs`, `CommandPipe.cs`, `Native.cs`).
- **`.github/workflows/release.yml`** — tag-triggered Release workflow that checks the tag against `extension/package.json` and `extension/CHANGELOG.md`, cross-builds the Windows helper on Linux, packages the `win32-x64` `.vsix`, attaches it to a GitHub Release, and publishes to the Marketplace.

## Design decisions

- **A separate native helper instead of doing it inside the extension host.** VS Code's extension API does not expose Win32 window handles, title bar rendering, or taskbar state, and each window runs its own extension host. A single WinForms process (`vscode-tabs.exe`, guarded by the `Local\vscode-tabs` mutex) watches all top-level `Chrome_WidgetWin_1` windows owned by `Code.exe` through `SetWinEventHook`, attaches a borderless layered child `TabStrip` to the active window with `SetParent`, and exits after 10 seconds with no tracked windows.
- **Parking inactive windows off-screen and off the taskbar.** Each `TabGroup` keeps only its active window at the group's bounds. Inactive tabs receive `WS_EX_TOOLWINDOW`, are removed from the taskbar through `ITaskbarList.DeleteTab`, and are parked 1000 pixels below the virtual screen at `HWND_BOTTOM`. DWM window transitions (`DWMWA_TRANSITIONS_FORCEDISABLED`) are disabled briefly during switches, opens, and closes so windows swap in place instead of animating from off-screen.
- **A named pipe per user.** The extension and helper communicate over `\\.\pipe\vscode-tabs-<username>` (`PipeOptions.CurrentUserOnly`), sending one newline-terminated command per connection: `next`, `previous`, `closing <folder>`, or `handover`. Keeping commands connectionless lets any VS Code window reach the helper without maintaining sockets; if connecting fails, `Ctrl+Tab` and `Ctrl+Shift+Tab` fall back to VS Code's built-in recent editor switcher.
- **Handing over to a newer exe on update.** When the extension updates while an older `vscode-tabs.exe` is still running, the new process compares file versions (injected from `extension/package.json` at build time). If its own version is newer, it sends `handover` over the pipe and waits up to 5 seconds for the mutex. The older helper exits via `Release()` instead of `Dispose()`, leaving stashed windows parked so the new instance adopts them on startup instead of scattering every grouped window across the desktop.
- **Sampling the title bar colour instead of reading theme files.** `TitleBarProbe` captures the active window with `PrintWindow`, finds the modal title bar background pixel, and measures the height and left/right insets occupied by VS Code's own controls. `Palette` derives dark or light tab shades from that colour, and `TabGroup.FollowFocus` polls a pixel beside the strip after focus changes so the bar switches between focused and unfocused colours once VS Code repaints.

## Developing

Prerequisites: .NET 10 SDK (`net10.0-windows`; `VsCodeTabs.csproj` sets `EnableWindowsTargeting`, so the Windows binary can also cross-build on Linux) and Node.js with npm.

Install packaging dependencies and build `extension/bin/vscode-tabs.exe`:

```sh
cd extension
npm install
npm run vscode:prepublish
```

`vscode:prepublish` runs `dotnet publish ../src/VsCodeTabs` for `win-x64` as a self-contained, compressed single-file executable and reads the assembly version from `extension/package.json`.

Package the `win32-x64` `.vsix` (which runs `vscode:prepublish` first):

```sh
npm run package
```

To test a custom build of the helper without repackaging, point the `vscodeTabs.executable` setting at your compiled `vscode-tabs.exe`. Tab order (up to 100 entries) is saved in `%LOCALAPPDATA%\vscode-tabs\order.txt`, and unhandled exceptions are logged to `%LOCALAPPDATA%\vscode-tabs\error.log` before the helper restores all stashed windows and exits.

## Status

Releases are published by pushing a `vX.Y.Z` tag. The Release workflow (`.github/workflows/release.yml`) checks that the tag matches `extension/package.json` and has a matching `## [X.Y.Z]` entry in `extension/CHANGELOG.md`, cross-builds `vscode-tabs.exe` on `ubuntu-latest`, packages the `win32-x64` `.vsix`, attaches it to a GitHub Release, and publishes it to the VS Code Marketplace with `vsce publish --oidc` via Marketplace Trusted Publishing.

## License

GPL-3.0-or-later. See [LICENSE](LICENSE).
