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
  <a href="https://github.com/kevinboss/heartbeat"><img src="https://raw.githubusercontent.com/kevinboss/heartbeat/main/badges/kevinboss_vscode-tabs.svg" alt="Heartbeat"></a>
</p>

> This project is developed largely by AI agents, directed by a human who sets the goals and makes the decisions.
> The code is tested and its results are checked, but not every line is reviewed by a human.

VS Code Tabs groups open VS Code windows into tabs in the title bar on Windows
x64, keeping one window visible per group while parking the rest off-screen and
off the taskbar. The extension prepares the title bar and starts a bundled
helper (`vscode-tabs.exe`) that tracks the windows and draws the tab strip.

Install from the
[VS Code Marketplace](https://marketplace.visualstudio.com/items?itemName=kevinboss.vscode-tabs)
(Windows x64, VS Code 1.90.0 or later):

```sh
code --install-extension kevinboss.vscode-tabs
```

On first start, the extension prompts to update four VS Code settings so the
title bar has room for tabs and `Ctrl+Tab` works from the integrated terminal.
Choose "Apply", then open a second VS Code window to see both appear as tabs in
the title bar. Click a tab or press `Ctrl+Tab` and `Ctrl+Shift+Tab` to switch
windows, and click `+` to open a new window in the current group. See
[extension/README.md](extension/README.md) for managing tabs, stopping the
helper, settings, and limitations.

## How to contribute

Issues and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for
building, testing, and releasing, and [docs/design.md](docs/design.md) for how
the helper works.
