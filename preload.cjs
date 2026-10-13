const { contextBridge, ipcRenderer } = require('electron');
const allowed = new Set(['state', 'add', 'login', 'import', 'refresh', 'remove', 'switch', 'settings', 'docs']);
contextBridge.exposeInMainWorld('manager', {
  call: (channel, payload) => {
    if (!allowed.has(channel)) throw new Error('不支持的操作');
    return ipcRenderer.invoke(channel, payload);
  },
  onState: callback => ipcRenderer.on('state', (_event, state) => callback(state))
});
