const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
function switchAuth(file, key, user, codec) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const before = fs.existsSync(file) ? fs.readFileSync(file) : null;
  let existing;
  try { existing = before ? JSON.parse(before.toString('utf8')) : {}; }
  catch { throw new Error('本机登录文件无法解析，已停止切换并保留原文件'); }
  if (!existing || typeof existing !== 'object' || Array.isArray(existing)) throw new Error('本机登录文件格式异常，已停止切换');
  const next = { ...existing, apiKey: key, userId: user.id, userName: user.userName || user.name || 'API Key', keyName: 'account-manager', authenticatedAt: new Date().toISOString() };
  const backup = before ? `${file}.manager-${Date.now()}-${crypto.randomUUID()}.encrypted` : null;
  if (before) fs.writeFileSync(backup, codec.encryptString(before.toString('utf8')), { mode: 0o600, flag: 'wx' });
  const temp = `${file}.${crypto.randomUUID()}.tmp`;
  try {
    fs.writeFileSync(temp, JSON.stringify(next, null, 2), { mode: 0o600, flag: 'wx' });
    fs.renameSync(temp, file);
    const readback = JSON.parse(fs.readFileSync(file, 'utf8'));
    if (readback.apiKey !== key || readback.userId !== user.id) throw new Error('登录配置回读失败');
  } catch (error) {
    if (before) fs.writeFileSync(file, before, { mode: 0o600 });
    else if (fs.existsSync(file)) fs.unlinkSync(file);
    throw error;
  } finally {
    if (fs.existsSync(temp)) fs.unlinkSync(temp);
  }
  return backup;
}
module.exports = { switchAuth };
