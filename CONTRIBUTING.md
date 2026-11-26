# Contributing

## Prerequisites

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download)
(`net10.0-windows`) and [Node.js](https://nodejs.org/) with npm.
`VsCodeTabs.csproj` sets `EnableWindowsTargeting`, so the Windows helper binary
can also cross-build on Linux.

## Building and packaging

Install packaging dependencies and build `extension/bin/vscode-tabs.exe`:

```sh
cd extension
npm install
npm run vscode:prepublish
```

`vscode:prepublish` runs `dotnet publish ../src/VsCodeTabs` for `win-x64` as a
self-contained, compressed single-file executable and reads the assembly version
from `extension/package.json`.

Package the `win32-x64` `.vsix` (which runs `vscode:prepublish` first):

```sh
npm run package
```

## Testing and debugging

To test a custom build of the helper without repackaging, point the
`vscodeTabs.executable` setting at your compiled `vscode-tabs.exe`.

Tab order (up to 100 entries) is saved in
`%LOCALAPPDATA%\vscode-tabs\order.txt`. Unhandled exceptions are logged to
`%LOCALAPPDATA%\vscode-tabs\error.log` before the helper restores all stashed
windows and exits.

## Repository layout

- `extension/` — the VS Code extension (`extension.js`, `package.json`,
  `README.md`, `CHANGELOG.md`, `icon.png`, `LICENSE`, `.vscodeignore`). On
  Windows it activates on `onStartupFinished`, prompts for the four required
  settings, binds `Ctrl+Tab` and `Ctrl+Shift+Tab` to `vscodeTabs.nextTab` and
  `vscodeTabs.previousTab`, spawns `bin/vscode-tabs.exe` (or
  `vscodeTabs.executable` when set), and talks to the helper over the per-user
  named pipe. User documentation lives in `extension/README.md`.
- `src/VsCodeTabs/` — the .NET 10 WinForms helper (`VsCodeTabs.csproj`,
  `app.manifest`, `app.ico`, and the C# sources). Tracks `Code.exe` windows with
  `SetWinEventHook` (`TabManager.cs`, `Win.cs`), manages tab groups, stashing,
  and DWM transitions (`TabGroup.cs`, `Taskbar.cs`, `Transitions.cs`), draws the
  title bar overlay and drag ghost (`TabStrip.cs`, `TabGhost.cs`,
  `TitleBarProbe.cs`, `TitleParser.cs`, `Palette.cs`), and runs the
  single-instance mutex, tray icon, and named pipe (`Program.cs`,
  `CommandPipe.cs`, `Native.cs`).
- `docs/` — design notes (`design.md`).
- `.github/workflows/release.yml` — tag-triggered Release workflow that checks
  the tag against `extension/package.json` and `extension/CHANGELOG.md`,
  cross-builds the Windows helper on Linux, packages the `win32-x64` `.vsix`,
  attaches it to a GitHub Release, and publishes to the Marketplace.

## Releasing

Releases are published by pushing a `vX.Y.Z` tag. The Release workflow
(`.github/workflows/release.yml`) checks that the tag matches
`extension/package.json` and has a matching `## [X.Y.Z]` entry in
`extension/CHANGELOG.md`, cross-builds `vscode-tabs.exe` on `ubuntu-latest`,
packages the `win32-x64` `.vsix`, attaches it to a GitHub Release, and publishes
it to the VS Code Marketplace with `vsce publish --oidc` via Marketplace Trusted
Publishing.
