# Changelog

All notable changes to the VS Code Tabs extension are documented here. The
format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the
project uses [semantic versioning](https://semver.org/spec/v2.0.0.html).

## [0.6.1] - 2026-11-27

### Fixed
- After closing a VS Code window, its tab bar could stay behind as an empty bar at the top of the screen.

## [0.6.0] - 2026-11-27

### Changed
- Tabs follow VS Code's current tab look: rounded tabs, a thin outline around the active tab, faint dividers between the other tabs and a lighter + button.
- Tab colours come from your colour theme, read from the window itself, so the strip matches dark, light and tinted themes without any setting.
- The first tab sits right after the VS Code icon, at the same distance the icon keeps from the window edge.

### Fixed
- After switching colour theme, switching tabs no longer shows the previous theme's colours for a moment.

## [0.4.0] - 2026-11-27

### Added
- Opening a tab with the + button shows the new tab right away and fades it in and out until VS Code has loaded the window. The current tab stays on screen in the meantime, instead of an empty grey window.

### Fixed
- The tab strip no longer covers VS Code's layout buttons at the right end of the title bar.
- Closing a tab switches to the tab next to it instead of one Windows picks, and no longer shows the desktop for a moment.
- A closed tab leaves the strip immediately instead of after VS Code has finished shutting the window down.
- When VS Code asks whether to save changes in a tab you are closing, the tab comes back with the dialog centred on it.

## [0.2.0] - 2026-11-26

### Added
- Pre-release channel. Every change pushed to `main` now publishes a pre-release build to the Marketplace. Select "Switch to Pre-Release Version" on the extension's page in VS Code to opt in, and "Switch to Release Version" to go back to stable builds. Stable releases sit on even minor versions (starting with 0.2.0) and pre-releases use the odd minor above them.

### Changed
- Shortened the Marketplace page to what the extension does and how to use it, with the license at the bottom.

## [0.1.2] - 2026-11-26

### Changed
- Shorter Marketplace page: what the extension does, how to install it and what to do first. Usage details, settings and limitations moved to the repository's docs.

## [0.1.1] - 2026-11-26

### Changed
- Rewrote the Marketplace page: getting started, features, the settings the extension asks for, and known limitations.

## [0.1.0] - 2026-11-25

First Marketplace release.

### Added
- VS Code windows on Windows are grouped into tabs drawn in the title bar. Inactive windows in a group leave the taskbar and come back when their tab is selected.
- Ctrl+Tab and Ctrl+Shift+Tab switch between window tabs, also from the integrated terminal. Holding the key down switches once per repeat burst instead of once per key event.
- Tabs can be reordered, dragged out into a window of their own, or dropped onto another window's tab bar. Tab order is kept across sessions.
- The tab bar takes its colours from the title bar of the current theme and follows focus changes as VS Code repaints.
- On first start the extension offers to set the four VS Code settings the tab bar needs.
- The helper is bundled as a self-contained exe. When the extension updates, the newer exe takes over from the running one without restoring the grouped windows.

[0.6.1]: https://github.com/kevinboss/vscode-tabs/releases/tag/v0.6.1
[0.6.0]: https://github.com/kevinboss/vscode-tabs/releases/tag/v0.6.0
[0.4.0]: https://github.com/kevinboss/vscode-tabs/releases/tag/v0.4.0
[0.2.0]: https://github.com/kevinboss/vscode-tabs/releases/tag/v0.2.0
[0.1.2]: https://github.com/kevinboss/vscode-tabs/releases/tag/v0.1.2
[0.1.1]: https://github.com/kevinboss/vscode-tabs/releases/tag/v0.1.1
[0.1.0]: https://github.com/kevinboss/vscode-tabs/releases/tag/v0.1.0
