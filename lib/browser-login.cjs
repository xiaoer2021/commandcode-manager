const http = require('node:http');
const crypto = require('node:crypto');
async function createLoginFlow(timeoutMs = 300000) {
  const state = crypto.randomBytes(32).toString('base64url');
  let settle;
  let reject;
  let timer;
  let done = false;
  const result = new Promise((resolve, fail) => { settle = resolve; reject = fail; });
  result.catch(() => {});
  function finish(error, credential) {
    if (done) return;
    done = true;
    clearTimeout(timer);
    server.close();
    server.closeIdleConnections();
    if (error) reject(error);
    else settle(credential);
  }
  const server = http.createServer((req, res) => {
    res.setHeader('Cache-Control', 'no-store');
    const origin = req.headers.origin;
    if (origin && origin !== 'https://commandcode.ai') { res.writeHead(403); res.end(); return; }
    if (origin) res.setHeader('Access-Control-Allow-Origin', origin);
    res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type');
    if (req.method === 'OPTIONS') {
      if (req.headers['access-control-request-private-network'] === 'true') res.setHeader('Access-Control-Allow-Private-Network', 'true');
      res.writeHead(204); res.end(); return;
    }
    if (req.url !== '/callback' || req.method !== 'POST') { res.writeHead(404); res.end(); return; }
    let body = '';
    let tooLarge = false;
    req.on('data', chunk => {
      body += chunk.toString();
      if (Buffer.byteLength(body) > 32768) { tooLarge = true; res.writeHead(413); res.end(); req.destroy(); }
    });
    req.on('end', () => {
      if (tooLarge || done) return;
      let value;
      const json = (req.headers['content-type'] || '').split(';')[0] === 'application/json';
      try { value = json ? JSON.parse(body) : Object.fromEntries(new URLSearchParams(body)); }
      catch { res.writeHead(400); res.end(); return; }
      if (value.state !== state) { res.writeHead(403); res.end('Invalid state'); return; }
      if (value.error) { res.writeHead(200); res.end('Authorization cancelled'); finish(new Error('官方授权被取消')); return; }
      if (!['apiKey', 'userId', 'userName', 'keyName'].every(name => typeof value[name] === 'string' && value[name])) { res.writeHead(400); res.end('Missing fields'); return; }
      res.writeHead(200, { 'Content-Type': json ? 'application/json' : 'text/html; charset=utf-8' });
      res.end(json ? '{"success":true}' : '<!doctype html><meta charset="utf-8"><title>登录成功</title><h2>登录授权已完成，可以返回账号管理客户端。</h2>');
      finish(null, { apiKey: value.apiKey, userId: value.userId, userName: value.userName, keyName: value.keyName });
    });
    req.on('error', () => { if (!res.writableEnded) res.end(); });
  });
  server.requestTimeout = 15000;
  await new Promise((resolve, fail) => { server.once('error', fail); server.listen(0, '127.0.0.1', resolve); });
  const port = server.address().port;
  const callback = `http://127.0.0.1:${port}/callback`;
  const url = `https://commandcode.ai/studio/auth/cli?callback=${encodeURIComponent(callback)}&state=${state}&mode=redirect`;
  timer = setTimeout(() => finish(new Error('登录等待超时，请重试')), timeoutMs);
  return { url, result, cancel: () => finish(new Error('登录窗口已关闭')) };
}
module.exports = { createLoginFlow };
