const { app, BrowserWindow, ipcMain } = require('electron');
const fs = require('node:fs');
const path = require('node:path');
app.whenReady().then(async () => {
  const screenshots = path.join(__dirname, 'preview');
  fs.mkdirSync(screenshots, { recursive: true });
  const errors = [];
  const window = new BrowserWindow({ width: 1280, height: 850, show: false, webPreferences: { preload: path.join(__dirname, 'preload.cjs'), contextIsolation: true, sandbox: true } });
  window.webContents.on('console-message', (_event, level, message) => { if (level >= 3) errors.push(message); });
  const settings = { interval: 300, warning: 80, critical: 95, lowBalance: 5, notifications: true };
  ipcMain.handle('state', () => ({ ok: true, value: { accounts: [], settings, refreshing: false, environmentOverride: false } }));
  try {
    await window.loadFile(path.join(__dirname, 'ui', 'index.html'));
    await new Promise(resolve => setTimeout(resolve, 500));
    fs.writeFileSync(path.join(screenshots, 'overview-empty.png'), (await window.webContents.capturePage()).toPNG());
    await window.webContents.executeJavaScript("document.getElementById('add').click()");
    await new Promise(resolve => setTimeout(resolve, 200));
    fs.writeFileSync(path.join(screenshots, 'password-login.png'), (await window.webContents.capturePage()).toPNG());
    await window.webContents.executeJavaScript("document.getElementById('api-tab').click()");
    await new Promise(resolve => setTimeout(resolve, 100));
    const apiVisible = await window.webContents.executeJavaScript("!document.getElementById('api-section').hidden && document.getElementById('api-key').required");
    if (!apiVisible) throw new Error('API 添加入口不可用');
    await window.webContents.executeJavaScript("document.getElementById('close-dialog').click()");
    const example = {
      id: 'fixture', name: '界面测试账号（示例）', method: 'password', active: true, alerts: [],
      data: { user: { email: 'demo@example.com' }, plan: 'pro', subscriptionStatus: 'active', remaining: 32.8, monthlyRemaining: 28, purchasedRemaining: 4.8, freeRemaining: 0, limited: true, fiveHour: { percent: 62, used: 9.92, cap: 16, resetAt: '2030-10-13T12:00:00Z' }, weekly: { percent: 85, used: 34, cap: 40, resetAt: '2030-10-19T08:00:00Z' }, periodEnd: '2030-10-30T08:00:00Z', cancelAtPeriodEnd: false, fetchedAt: '2026-10-13T07:00:00Z' }
    };
    window.webContents.send('state', { accounts: [example], settings, refreshing: false });
    await new Promise(resolve => setTimeout(resolve, 150));
    const count = await window.webContents.executeJavaScript("document.querySelectorAll('.account-card').length");
    if (count !== 1) throw new Error('账号卡片渲染失败');
    fs.writeFileSync(path.join(screenshots, 'overview-fixture.png'), (await window.webContents.capturePage()).toPNG());
    await window.webContents.executeJavaScript("document.getElementById('settings-nav').click()");
    const settingsValue = await window.webContents.executeJavaScript("document.getElementById('settings-form').elements.interval.value");
    if (settingsValue !== '300') throw new Error('设置页加载失败');
    if (errors.length) throw new Error(errors.join(';'));
    fs.writeFileSync(path.join(screenshots, 'smoke-result.json'), JSON.stringify({ passed: true, checks: ['空列表', '账号密码入口', 'API Key 入口', '卡片渲染', '设置页'], consoleErrors: errors }, null, 2));
    console.log('桌面界面检查通过：空列表、账号密码入口、API Key 入口、卡片渲染、设置页');
    app.exit(0);
  } catch (error) { console.error(error.message); app.exit(1); }
});
