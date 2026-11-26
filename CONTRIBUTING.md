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
  named pipe.
- `src/VsCodeTabs/` — the .NET 10 WinForms helper (`VsCodeTabs.csproj`,
  `app.manifest`, `app.ico`, and the C# sources). Tracks `Code.exe` windows with
  `SetWinEventHook` (`TabManager.cs`, `Win.cs`), manages tab groups, stashing,
  and DWM transitions (`TabGroup.cs`, `Taskbar.cs`, `Transitions.cs`), draws the
  title bar overlay and drag ghost (`TabStrip.cs`, `TabGhost.cs`,
  `TitleBarProbe.cs`, `TitleParser.cs`, `Palette.cs`), and runs the
  single-instance mutex, tray icon, and named pipe (`Program.cs`,
  `CommandPipe.cs`, `Native.cs`).
- `docs/` — user documentation (`usage.md`) and design notes (`design.md`).
- `scripts/publish-version.mjs` — version guard called by the Release workflow
  for both lanes. Checks that stable versions sit on an even minor and computes
  pre-release versions as `<major>.<minor + 1>.<commit count>`. Exits with an
  error on an odd stable minor, a tag that does not match
  `extension/package.json`, a stable version behind an existing `v*` tag, or a
  version already on the Marketplace or not above the highest published build in
  its lane.
- `.github/workflows/release.yml` — Release workflow with two jobs sharing the
  `marketplace-publish` concurrency group and Marketplace Trusted Publishing
  (OIDC). A `vX.Y.Z` tag runs the `release` job, which runs
  `scripts/publish-version.mjs stable`, checks `extension/CHANGELOG.md`,
  cross-builds the Windows helper on Linux, packages the `win32-x64` `.vsix`,
  attaches it to a GitHub Release, and publishes to the Marketplace. Every other
  push to `main` runs the `pre-release` job, skipping tagged commits and commits
  that change the version in `extension/package.json`, and publishes a
  `--pre-release` build on the odd minor above the stable version.

## Releasing

The Release workflow (`.github/workflows/release.yml`) has two jobs: `release`
for stable builds and `pre-release` for commits on `main`. Both jobs share the
`marketplace-publish` concurrency group so publishes run one at a time, and both
publish to the VS Code Marketplace with `vsce publish --oidc` via Marketplace
Trusted Publishing (OIDC).

Because the Marketplace rejects semver pre-release suffixes, the two lanes are
separated by minor-version parity. Stable releases sit on even minors (such as
`0.2.0` and `0.4.0`), and pre-releases use the odd minor above the current
stable version with the git commit count as the patch number, written as
`<major>.<stable minor + 1>.<commit count>` (for example `0.3.<commit count>`
after `0.2.0`).

To publish a stable release, bump `extension/package.json` to an even minor, add
a matching `## [X.Y.Z]` entry in `extension/CHANGELOG.md`, commit, and push a
`vX.Y.Z` tag. The `release` job runs `scripts/publish-version.mjs stable <tag>`,
checks the changelog entry, cross-builds `vscode-tabs.exe` on `ubuntu-latest`,
packages the `win32-x64` `.vsix`, attaches it to a GitHub Release, and publishes
it to the Marketplace.

Every other push to `main` runs the `pre-release` job. It skips tagged commits
and commits that change `version` in `extension/package.json` (release commits).
For all other commits it runs `scripts/publish-version.mjs prerelease`, updates
the manifest version in the runner workspace, packages the `win32-x64` `.vsix`
with `vsce package --pre-release`, and publishes it with
`vsce publish --oidc --pre-release`.

Before packaging, `scripts/publish-version.mjs` checks the target version and
exits with an error in any of these cases:

- `extension/package.json` has an odd minor version.
- The pushed tag does not match `v<version>` in `extension/package.json`.
- A stable version is behind an existing `v*` tag in git.
- The version is already published on the Marketplace, or is not above the
  highest published version in its `<major>.<minor>.x` lane.
- The Marketplace version list cannot be read via `vsce show --json`.
