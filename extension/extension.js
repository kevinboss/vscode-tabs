const { spawn } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');
const vscode = require('vscode');

function activate() {
  if (process.platform !== 'win32') {
    return;
  }
  const configured = vscode.workspace.getConfiguration('vscodeTabs').get('executable');
  const executable = configured || path.join(os.homedir(), '.local', 'bin', 'vscode-tabs.exe');
  if (!fs.existsSync(executable)) {
    return;
  }
  spawn(executable, [], { detached: true, stdio: 'ignore', windowsHide: true }).unref();
}

module.exports = { activate, deactivate() {} };
