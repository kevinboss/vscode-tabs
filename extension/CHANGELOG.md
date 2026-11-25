# Changelog

All notable changes to the VS Code Tabs extension are documented here. The
format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the
project uses [semantic versioning](https://semver.org/spec/v2.0.0.html).

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

[0.1.1]: https://github.com/kevinboss/vscode-tabs/releases/tag/v0.1.1
[0.1.0]: https://github.com/kevinboss/vscode-tabs/releases/tag/v0.1.0
