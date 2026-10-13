const { app } = require('electron');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const testData = fs.mkdtempSync(path.join(os.tmpdir(), 'commandcode-startup-'));
app.setPath('userData', testData);
app.on('browser-window-created', (_event, window) => {
  window.hide();
  window.webContents.once('did-finish-load', async () => {
    try {
      const result = await window.webContents.executeJavaScript("window.manager.call('state')");
      if (!result.ok || result.value.accounts.length !== 0 || !result.value.encryptionAvailable) throw new Error('桌面服务或系统凭据加密未通过检查');
      fs.writeFileSync(path.join(__dirname, 'preview', 'startup-result.json'), JSON.stringify({ passed: true, encryptionAvailable: result.value.encryptionAvailable, accounts: result.value.accounts.length }, null, 2));
      app.quit();
    } catch (error) { fs.writeFileSync(path.join(__dirname, 'preview', 'startup-result.json'), JSON.stringify({ passed: false, error: error.message })); app.quit(); }
  });
});
require(process.env.COMMANDCODE_PACKAGE_TEST || './main.cjs');
