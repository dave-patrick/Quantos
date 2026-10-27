const { app, BrowserWindow, shell, session } = require('electron');
const fs = require('fs');
const path = require('path');

const START_URL = 'https://messages.google.com/web/';
const PARTITION = 'persist:messages'; // "persist:" keeps cookies, logins and cache on disk
const stateFile = path.join(app.getPath('userData'), 'window-state.json');

function loadState() {
  try { return JSON.parse(fs.readFileSync(stateFile, 'utf8')); }
  catch { return { width: 1100, height: 800 }; }
}

function saveState(win) {
  if (win.isMinimized()) return;
  const state = { ...win.getNormalBounds(), maximized: win.isMaximized() };
  try { fs.writeFileSync(stateFile, JSON.stringify(state)); } catch {}
}

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  let win;

  app.on('second-instance', () => {
    if (!win) return;
    if (win.isMinimized()) win.restore();
    win.focus();
  });

  app.whenReady().then(() => {
    const ses = session.fromPartition(PARTITION);

    // Google rejects sign-in from user agents containing "Electron"
    ses.setUserAgent(ses.getUserAgent().replace(/\s*Electron\/\S+/, '').replace(/\s*messages-app\/\S+/, ''));

    // Allow notifications (and nothing else) so new-message alerts work
    ses.setPermissionRequestHandler((_wc, permission, cb) => cb(permission === 'notifications'));

    const state = loadState();
    win = new BrowserWindow({
      ...state,
      title: 'Messages',
      autoHideMenuBar: true,
      webPreferences: { partition: PARTITION, contextIsolation: true, sandbox: true },
    });
    if (state.maximized) win.maximize();

    // Links to other sites open in your normal browser
    win.webContents.setWindowOpenHandler(({ url }) => {
      if (new URL(url).hostname.endsWith('google.com')) return { action: 'allow' };
      shell.openExternal(url);
      return { action: 'deny' };
    });

    win.on('close', () => saveState(win));
    win.loadURL(START_URL);
  });

  app.on('window-all-closed', () => app.quit());
}
