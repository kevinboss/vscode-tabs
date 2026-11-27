<h1 align="center">VS Code Tabs</h1>

<p align="center">
  Group VS Code windows into tabs in the title bar on Windows.
</p>

<p align="center">
  <a href="https://marketplace.visualstudio.com/items?itemName=kevinboss.vscode-tabs"><img src="https://vsmarketplacebadges.dev/version-short/kevinboss.vscode-tabs.svg" alt="VS Code Marketplace version"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0-blue.svg" alt="License: GPL-3.0"></a>
</p>

VS Code Tabs groups open VS Code windows into tabs in the title bar, keeping one window visible per group while parking the rest off-screen and off the taskbar.
The extension prepares the title bar and starts a bundled helper (`vscode-tabs.exe`) that tracks the windows and draws the tab strip.
It runs on Windows x64 only.

![Four VS Code windows as tabs in one title bar, switched by clicking the tabs and with Ctrl+Tab and Ctrl+Shift+Tab](https://raw.githubusercontent.com/kevinboss/vscode-tabs/main/docs/media/switching.gif)

## Usage

1. **Apply the title bar settings.**
   On first start, the extension prompts to update four VS Code settings so the title bar has room for tabs and `Ctrl+Tab` works from the integrated terminal.
   Choose "Apply" to write those user settings.
2. **Open a second VS Code window.**
   The bundled helper starts automatically with VS Code and places both windows as tabs in the title bar.
   New VS Code windows join the focused group as tabs.
   Windows of a different VS Code installation, such as a portable copy, form their own groups.

   ![Running code ..\billing-api in the integrated terminal opens the folder as a new tab in the same title bar](https://raw.githubusercontent.com/kevinboss/vscode-tabs/main/docs/media/grouping.gif)
3. **Switch windows or open a new one.**
   Click a tab or press `Ctrl+Tab` and `Ctrl+Shift+Tab` to move between windows in the group.
   Click `+` in the tab bar to open a new VS Code window in the current group.

   ![Clicking + opens a new VS Code window as a tab, and its close button closes it again](https://raw.githubusercontent.com/kevinboss/vscode-tabs/main/docs/media/new-close.gif)
4. **Close a tab or the whole group.**
   Click a tab's close button or middle-click the tab to close that one window.
   Closing the window itself with its X, `Alt+F4` or `Ctrl+Shift+W` closes every tab in its group, like closing a browser window.
5. **Reorder or detach tabs.**
   Drag a tab sideways in the bar to reorder it, or drag it out to the desktop to detach it into its own window.
   You can also drop a dragged tab onto another VS Code window's tab bar to move it into that group.

   ![Dragging the docs tab to the front of the tab bar and back to the end](https://raw.githubusercontent.com/kevinboss/vscode-tabs/main/docs/media/reorder.gif)

   ![Dragging the billing-api tab out into its own window and back into the first window's tab bar](https://raw.githubusercontent.com/kevinboss/vscode-tabs/main/docs/media/drag.gif)
6. **Stop the helper from the system tray.**
   While the helper is running, a VS Code Tabs icon sits in the Windows system tray.
   Right-click the icon and select "Exit" to stop the helper and restore all grouped windows to the desktop and taskbar.

## Colour themes

The tab bar takes its colours from the window's own title bar, so it follows any colour theme without extra settings, including the live preview while you pick a theme.

![Arrowing through light themes in the theme picker while the tab bar changes colour with each preview, then Esc back to the dark theme](https://raw.githubusercontent.com/kevinboss/vscode-tabs/main/docs/media/theme.gif)

## License

GPL-3.0-or-later. See [LICENSE](LICENSE).
