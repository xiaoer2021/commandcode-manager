const { app, BrowserWindow, ipcMain, safeStorage, Notification, Tray, Menu, nativeImage, dialog, shell } = require('electron');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');
const { fetchAccount, alertsFor } = require('./lib/provider.cjs');
const { switchAuth } = require('./lib/auth.cjs');
const { createLoginFlow } = require('./lib/browser-login.cjs');
let win;
let tray;
let store;
let storeFile;
let refreshTimer;
let refreshing = false;
let quitting = false;
let loggingIn = false;
const authFile = path.join(os.homedir(), '.commandcode', 'auth.json');
const defaultSettings = { interval: 300, warning: 80, critical: 95, lowBalance: 5, notifications: true };
function persist() {
  const temp = `${storeFile}.tmp`;
  fs.writeFileSync(temp, JSON.stringify(store, null, 2), { mode: 0o600 });
  fs.renameSync(temp, storeFile);
}
function encrypt(key) {
  if (!safeStorage.isEncryptionAvailable()) throw new Error('系统加密不可用，无法安全保存凭据');
  return safeStorage.encryptString(key).toString('base64');
}
function decrypt(account) { return safeStorage.decryptString(Buffer.from(account.secret, 'base64')); }
function accountById(id) {
  const account = store.accounts.find(value => value.id === id);
  if (!account) throw new Error('账号不存在');
  return account;
}
function localKey() {
  try { return JSON.parse(fs.readFileSync(authFile, 'utf8')).apiKey || null; }
  catch { return null; }
}
function publicState() {
  const current = localKey();
  return {
    accounts: store.accounts.map(({ secret, ...account }) => ({ ...account, active: current ? decrypt({ secret }) === current : false, alerts: alertsFor(account, store.settings) })),
    settings: store.settings,
    refreshing,
    loggingIn,
    environmentOverride: Boolean(process.env.COMMAND_CODE_API_KEY),
    encryptionAvailable: safeStorage.isEncryptionAvailable()
  };
}
function push() { if (win && !win.isDestroyed()) win.webContents.send('state', publicState()); }
function notify(account) {
  const alerts = alertsFor(account, store.settings);
  const activeIds = new Set(alerts.map(alert => alert.id));
  const previous = account.alertIds || [];
  if (store.settings.notifications && Notification.isSupported()) {
    for (const alert of alerts) if (!previous.includes(alert.id)) {
      const notification = new Notification({ title: 'CommandCode 用量提醒', body: alert.text });
      notification.on('click', () => { win.show(); win.focus(); });
      notification.show();
    }
  }
  account.alertIds = [...activeIds];
}
async function refreshOne(account) {
  try { account.data = await fetchAccount(decrypt(account)); account.error = null; notify(account); }
  catch (error) { account.error = error.message; }
  account.checkedAt = new Date().toISOString();
}
async function refreshAll() {
  if (refreshing) return publicState();
  refreshing = true;
  push();
  try {
    for (const account of [...store.accounts]) {
      await refreshOne(account);
      persist();
      push();
    }
  } finally { refreshing = false; push(); }
  return publicState();
}
function schedule() {
  clearInterval(refreshTimer);
  refreshTimer = setInterval(() => { refreshAll().catch(() => {}); }, store.settings.interval * 1000);
}
async function addAccount(payload, method = 'api') {
  if (!payload || typeof payload.apiKey !== 'string' || !payload.apiKey.trim()) throw new Error('请填写 API Key');
  const key = payload.apiKey.trim();
  if (key.length > 8192 || /[\r\n]/.test(key)) throw new Error('API Key 格式不正确');
  const encrypted = encrypt(key);
  const data = await fetchAccount(key);
  if (store.accounts.some(account => decrypt(account) === key || (account.data?.user?.id === data.user.id && (account.data?.org?.id || '') === (data.org?.id || '')))) throw new Error('这个账号已经添加，请先移除旧账号后重新添加');
  const account = { id: crypto.randomUUID(), name: String(payload.name || data.user.email || data.user.name || data.user.userName).slice(0,80), method, secret: encrypted, data, error: null, checkedAt: new Date().toISOString(), alertIds: [] };
  store.accounts.push(account);
  notify(account);
  persist();
  push();
  return publicState();
}
function secureRemoteWindow(url) {
  const login = new BrowserWindow({ width: 560, height: 760, title: 'CommandCode 官方账号密码登录', autoHideMenuBar: true, webPreferences: { nodeIntegration: false, contextIsolation: true, sandbox: true, partition: `login-${crypto.randomUUID()}` } });
  login.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  login.webContents.on('will-navigate', (event, target) => {
    const parsed = new URL(target);
    const official = parsed.protocol === 'https:' && (parsed.hostname === 'commandcode.ai' || parsed.hostname.endsWith('.commandcode.ai'));
    const callback = parsed.origin === new URL(new URL(url).searchParams.get('callback')).origin;
    if (!official && !callback) event.preventDefault();
  });
  login.loadURL(url).catch(() => login.close());
  return login;
}
async function browserLogin(name) {
  if (loggingIn) throw new Error('已有登录窗口，请完成或关闭后重试');
  if (!safeStorage.isEncryptionAvailable()) throw new Error('系统加密不可用');
  loggingIn = true;
  push();
  let flow;
  let login;
  try {
    flow = await createLoginFlow();
    login = secureRemoteWindow(flow.url);
    login.on('closed', () => flow.cancel());
    const credential = await flow.result;
    return await addAccount({ name, apiKey: credential.apiKey }, 'password');
  } finally {
    flow?.cancel();
    if (login && !login.isDestroyed()) login.destroy();
    loggingIn = false;
    push();
  }
}
function register() {
  const handlers = {
    state: () => publicState(),
    add: payload => addAccount(payload),
    login: payload => browserLogin(payload?.name),
    import: () => {
      const key = localKey();
      if (!key) throw new Error('未发现本机已登录的 CommandCode 账号');
      return addAccount({ apiKey: key }, 'import');
    },
    refresh: () => refreshAll(),
    remove: async id => {
      const account = accountById(id);
      const answer = await dialog.showMessageBox(win, { type: 'question', buttons: ['取消', '移除'], defaultId: 0, cancelId: 0, message: `移除“${account.name}”？`, detail: '仅从管理列表中移除；本机 CommandCode 的登录配置保持原样。' });
      if (answer.response === 1) { store.accounts = store.accounts.filter(value => value.id !== id); persist(); push(); }
      return publicState();
    },
    switch: async id => {
      const account = accountById(id);
      if (process.env.COMMAND_CODE_API_KEY) throw new Error('当前环境设置了 COMMAND_CODE_API_KEY，会覆盖登录文件。请先清除此变量，再切换账号。');
      const key = decrypt(account);
      const data = await fetchAccount(key);
      if (data.user.id !== account.data?.user?.id || (data.org?.id || '') !== (account.data?.org?.id || '')) throw new Error('账号或组织身份发生变化，已停止切换');
      switchAuth(authFile, key, data.user, safeStorage);
      account.data = data;
      account.error = null;
      persist();
      push();
      return { state: publicState(), message: '已切换本机默认账号，新开的 CommandCode 会话使用此账号。已运行的会话请关闭后重新打开。' };
    },
    settings: payload => {
      if (!payload || ![payload.interval, payload.warning, payload.critical, payload.lowBalance].every(Number.isFinite)) throw new Error('设置必须是有效数字');
      if (payload.interval < 60 || payload.interval > 3600 || payload.warning < 1 || payload.warning >= payload.critical || payload.critical > 100 || payload.lowBalance < 0 || typeof payload.notifications !== 'boolean') throw new Error('刷新间隔需为 60–3600 秒；预警比例需小于严重预警比例');
      store.settings = { interval: payload.interval, warning: payload.warning, critical: payload.critical, lowBalance: payload.lowBalance, notifications: payload.notifications };
      persist(); schedule(); push(); return publicState();
    },
    docs: () => shell.openExternal('https://commandcode.ai/docs')
  };
  for (const [channel, handler] of Object.entries(handlers)) ipcMain.handle(channel, async (event, payload) => {
    if (event.sender !== win.webContents || event.senderFrame !== win.webContents.mainFrame) throw new Error('访问被拒绝');
    try { return { ok: true, value: await handler(payload) }; }
    catch (error) { return { ok: false, error: error.message }; }
  });
}
function createWindow() {
  win = new BrowserWindow({ width: 1280, height: 850, minWidth: 900, minHeight: 680, title: 'CommandCode Manager', backgroundColor: '#090d17', autoHideMenuBar: true, webPreferences: { preload: path.join(__dirname, 'preload.cjs'), contextIsolation: true, nodeIntegration: false, sandbox: true } });
  win.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  win.webContents.on('will-navigate', event => event.preventDefault());
  win.loadFile(path.join(__dirname, 'ui', 'index.html'));
  win.on('close', event => { if (!quitting) { event.preventDefault(); win.hide(); } });
  const icon = nativeImage.createFromDataURL('data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==');
  tray = new Tray(icon.resize({ width: 16, height: 16 }));
  tray.setToolTip('CommandCode 多账号管理');
  tray.setContextMenu(Menu.buildFromTemplate([{ label: '打开账号总览', click: () => { win.show(); win.focus(); } }, { label: '刷新用量', click: () => refreshAll().catch(() => {}) }, { type: 'separator' }, { label: '退出', click: () => app.quit() }]));
  tray.on('double-click', () => { win.show(); win.focus(); });
}
if (!app.requestSingleInstanceLock()) app.quit();
else {
  app.on('second-instance', () => { if (win) { win.show(); win.focus(); } });
  app.whenReady().then(() => {
    app.setAppUserModelId('local.commandcode.manager');
    storeFile = path.join(app.getPath('userData'), 'accounts.json');
    try { store = fs.existsSync(storeFile) ? JSON.parse(fs.readFileSync(storeFile, 'utf8')) : { accounts: [], settings: { ...defaultSettings } }; }
    catch { dialog.showErrorBox('无法读取账号数据', '账号数据文件异常，原文件已保留。请备份后检查。'); app.quit(); return; }
    store.settings = { ...defaultSettings, ...store.settings };
    if (!Array.isArray(store.accounts)) { dialog.showErrorBox('账号数据异常', '账号列表格式不正确，原文件已保留。'); app.quit(); return; }
    register(); createWindow(); schedule();
    refreshAll().catch(() => {});
  });
}
app.on('before-quit', () => { quitting = true; clearInterval(refreshTimer); });
