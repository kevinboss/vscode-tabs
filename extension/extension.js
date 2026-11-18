const { spawn } = require('child_process');
const fs = require('fs');
const net = require('net');
const os = require('os');
const path = require('path');
const vscode = require('vscode');

const tabCommands = ['vscodeTabs.nextTab', 'vscodeTabs.previousTab'];

const requiredSettings = [
  { section: 'window', name: 'menuBarVisibility', accepts: (value) => value === 'compact', fix: () => 'compact' },
  { section: 'window', name: 'commandCenter', accepts: (value) => value === false, fix: () => false },
  {
    section: 'window',
    name: 'title',
    accepts: (value) => typeof value === 'string' && value.includes('${rootPath}'),
    fix: () => '${dirty}${activeEditorShort}${separator}${rootPath}',
  },
  {
    section: 'terminal.integrated',
    name: 'commandsToSkipShell',
    accepts: (value) => tabCommands.every((command) => value.includes(command)),
    fix: (value = []) => [...value, ...tabCommands.filter((command) => !value.includes(command))],
  },
];

function activate(context) {
  if (process.platform !== 'win32') {
    return;
  }
  context.subscriptions.push(
    vscode.commands.registerCommand('vscodeTabs.nextTab', () =>
      send('next', 'workbench.action.quickOpenPreviousRecentlyUsedEditorInGroup')),
    vscode.commands.registerCommand('vscodeTabs.previousTab', () =>
      send('previous', 'workbench.action.quickOpenLeastRecentlyUsedEditorInGroup')),
  );
  promptForSettings(context);

  const configured = vscode.workspace.getConfiguration('vscodeTabs').get('executable');
  const executable = configured || path.join(context.extensionPath, 'bin', 'vscode-tabs.exe');
  if (!fs.existsSync(executable)) {
    return;
  }
  spawn(executable, [], { detached: true, stdio: 'ignore', windowsHide: true }).unref();
}

function send(command, fallback) {
  let connected = false;
  const socket = net.connect(`\\\\.\\pipe\\vscode-tabs-${os.userInfo().username}`);
  socket.on('connect', () => {
    connected = true;
  });
  socket.on('error', () => {
    if (!connected) {
      vscode.commands.executeCommand(fallback);
    }
  });
  socket.end(`${command}\n`);
}

async function promptForSettings(context) {
  if (context.globalState.get('settingsPromptDeclined')) {
    return;
  }
  const missing = requiredSettings.filter(
    (setting) => !setting.accepts(vscode.workspace.getConfiguration(setting.section).get(setting.name)),
  );
  if (missing.length === 0) {
    return;
  }
  const names = missing.map((setting) => `${setting.section}.${setting.name}`).join(', ');
  const choice = await vscode.window.showInformationMessage(
    `VS Code Tabs needs these settings changed: ${names}.`,
    'Apply',
    "Don't Ask Again",
  );
  if (choice === 'Apply') {
    for (const setting of missing) {
      const config = vscode.workspace.getConfiguration(setting.section);
      const value = setting.fix(config.inspect(setting.name)?.globalValue);
      await config.update(setting.name, value, vscode.ConfigurationTarget.Global);
    }
  } else if (choice === "Don't Ask Again") {
    await context.globalState.update('settingsPromptDeclined', true);
  }
}

module.exports = { activate, deactivate() {} };
