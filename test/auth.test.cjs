const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { switchAuth } = require('../lib/auth.cjs');
test('切换保留其他供应商凭据，并备份及回读', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'cmd-manager-test-'));
  const file = path.join(dir, 'auth.json');
  const original = { apiKey: 'old-fake-key', userId: 'u0', otherProvider: { token: 'untouched' } };
  fs.writeFileSync(file, JSON.stringify(original));
  const codec = { encryptString: text => Buffer.from(`encrypted:${text}`) };
  try {
    const backup = switchAuth(file, 'new-fake-key', { id: 'u1', userName: 'test' }, codec);
    const result = JSON.parse(fs.readFileSync(file));
    assert.equal(result.apiKey, 'new-fake-key');
    assert.equal(result.userId, 'u1');
    assert.deepEqual(result.otherProvider, original.otherProvider);
    assert.equal(fs.readFileSync(backup, 'utf8'), `encrypted:${JSON.stringify(original)}`);
  } finally { fs.rmSync(dir, { recursive: true }); }
});
test('损坏的登录配置不会被覆盖', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'cmd-manager-test-'));
  const file = path.join(dir, 'auth.json');
  fs.writeFileSync(file, '{broken');
  try {
    assert.throws(() => switchAuth(file, 'fake', { id: 'u1' }, {}));
    assert.equal(fs.readFileSync(file, 'utf8'), '{broken');
  } finally { fs.rmSync(dir, { recursive: true }); }
});
