<p align="center">
  <img src="icon.png" width="120" alt="VS Code Tabs logo">
</p>

<h1 align="center">VS Code Tabs</h1>

<p align="center">
  Group VS Code windows into tabs in the title bar on Windows.
</p>

<p align="center">
  <a href="https://marketplace.visualstudio.com/items?itemName=kevinboss.vscode-tabs"><img src="https://vsmarketplacebadges.dev/version-short/kevinboss.vscode-tabs.svg" alt="VS Code Marketplace version"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0-blue.svg" alt="License: GPL-3.0"></a>
</p>

VS Code Tabs groups open VS Code windows into tabs in the title bar on Windows
x64, keeping one window visible per group while parking the rest off-screen and
off the taskbar. The extension prepares the title bar and starts a bundled
helper (`vscode-tabs.exe`) that tracks the windows and draws the tab strip.

Install from the VS Code Marketplace (Windows x64, VS Code 1.90.0 or later):

```sh
code --install-extension kevinboss.vscode-tabs
```

On first start, the extension prompts to update four VS Code settings so the
title bar has room for tabs and `Ctrl+Tab` works from the integrated terminal.
Choose "Apply", then open a second VS Code window to see both appear as tabs in
the title bar. Click a tab or press `Ctrl+Tab` and `Ctrl+Shift+Tab` to switch
windows, and click `+` to open a new window in the current group. See
[docs/usage.md](https://github.com/kevinboss/vscode-tabs/blob/main/docs/usage.md)
for managing tabs, stopping the helper, settings, and limitations.
