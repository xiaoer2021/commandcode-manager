const test = require('node:test');
const assert = require('node:assert/strict');
const { createLoginFlow } = require('../lib/browser-login.cjs');
test('官方密码登录回调支持表单提交，校验随机 state', async () => {
  const flow = await createLoginFlow(10000);
  try {
    const url = new URL(flow.url);
    const callback = url.searchParams.get('callback');
    const fields = { state: 'wrong', apiKey: 'fake', userId: 'u1', userName: 'test', keyName: 'cli' };
    const bad = await fetch(callback, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded', Origin: 'https://commandcode.ai' }, body: new URLSearchParams(fields) });
    assert.equal(bad.status, 403);
    fields.state = url.searchParams.get('state');
    const good = await fetch(callback, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded', Origin: 'https://commandcode.ai' }, body: new URLSearchParams(fields) });
    assert.equal(good.status, 200);
    const credential = await flow.result;
    assert.equal(credential.apiKey, 'fake');
    assert.equal(credential.userId, 'u1');
  } finally { flow.cancel(); }
});
test('不接受第三方来源，取消登录关闭回调服务', async () => {
  const flow = await createLoginFlow(10000);
  const callback = new URL(flow.url).searchParams.get('callback');
  const response = await fetch(callback, { method: 'OPTIONS', headers: { Origin: 'https://example.com' } });
  assert.equal(response.status, 403);
  flow.cancel();
  await assert.rejects(flow.result, /关闭/);
});
