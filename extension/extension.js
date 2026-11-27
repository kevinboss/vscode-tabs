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
    vscode.window.onDidChangeActiveColorTheme(() => send('theme')),
  );
  promptForSettings(context);
  listen();

  const configured = vscode.workspace.getConfiguration('vscodeTabs').get('executable');
  const executable = configured || path.join(context.extensionPath, 'bin', 'vscode-tabs.exe');
  if (!fs.existsSync(executable)) {
    return;
  }
  spawn(executable, [], { detached: true, stdio: 'ignore', windowsHide: true }).unref();
}

const pipeName = () => `\\\\.\\pipe\\vscode-tabs-${os.userInfo().username}`;

const listener = { socket: undefined, timer: undefined, delay: 1000, stopped: false };

function listen() {
  if (listener.stopped) {
    return;
  }
  const socket = net.connect(pipeName());
  const openedAt = Date.now();
  let buffer = '';
  listener.socket = socket;
  socket.setEncoding('utf8');
  socket.on('connect', () => socket.write('listen\n'));
  socket.on('data', (chunk) => {
    buffer += chunk;
    let end;
    while ((end = buffer.indexOf('\n')) >= 0) {
      answer(socket, buffer.slice(0, end).trim());
      buffer = buffer.slice(end + 1);
    }
  });
  socket.on('error', () => {});
  socket.on('close', () => {
    if (listener.stopped) {
      return;
    }
    listener.delay = Date.now() - openedAt > 10000 ? 1000 : Math.min(listener.delay * 2, 30000);
    listener.timer = setTimeout(listen, listener.delay);
  });
}

function answer(socket, line) {
  const request = /^new-window (\d+) ?(.*)$/.exec(line);
  if (!request || !ownsWindow(request[2])) {
    return;
  }
  socket.write(`accepted ${request[1]}\n`);
  vscode.commands.executeCommand('workbench.action.newWindow');
}

function ownsWindow(folder) {
  if (!folder) {
    return vscode.window.state.focused;
  }
  const normalize = (value) => value.replace(/[\\/]+$/, '').toLowerCase();
  return [vscode.workspace.workspaceFolders?.[0]?.uri, vscode.workspace.workspaceFile]
    .filter((uri) => uri?.scheme === 'file')
    .some((uri) => normalize(uri.fsPath) === normalize(folder));
}

function send(command, fallback) {
  let connected = false;
  const socket = net.connect(pipeName());
  socket.on('connect', () => {
    connected = true;
  });
  socket.on('error', () => {
    if (!connected && fallback) {
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

function deactivate() {
  listener.stopped = true;
  clearTimeout(listener.timer);
  listener.socket?.destroy();
}

module.exports = { activate, deactivate };
