const test = require('node:test');
const assert = require('node:assert/strict');
const { fetchAccount, normalize, alertsFor } = require('../lib/provider.cjs');
test('查询使用官方接口、组织 ID 和计费周期起点', async () => {
  const calls = [];
  const fetcher = async (url, options) => {
    calls.push({ url, options });
    const responses = {
      '/alpha/whoami': { success: true, user: { id: 'u1', email: 'test@example.com' }, org: { id: 'org1' } },
      '/alpha/billing/credits': { credits: { monthlyCredits: 10, purchasedCredits: 3, freeCredits: 2 }, windowLimits: { limited: true, fiveHour: { used: 8, cap: 10, resetAt: '2030-01-01T12:00:00Z' } } },
      '/alpha/billing/subscriptions': { data: { status: 'active', planId: 'pro', currentPeriodStart: '2026-10-01T00:00:00Z', currentPeriodEnd: '2026-11-01T00:00:00Z' } },
      '/alpha/usage/summary': { totalCost: 12 }
    };
    return { ok: true, json: async () => responses[url.pathname] };
  };
  const result = await fetchAccount('fake-key', fetcher);
  assert.equal(result.remaining, 15);
  assert.equal(result.fiveHour.percent, 80);
  assert.equal(result.spent, 12);
  assert.equal(calls[1].url.searchParams.get('orgId'), 'org1');
  assert.equal(calls[3].url.searchParams.get('since'), '2026-10-01T00:00:00Z');
  assert.equal(calls[0].options.headers.Authorization, 'Bearer fake-key');
  assert.equal(calls[0].options.redirect, 'error');
});
test('未获取的额度保持未知，部分失败保留错误', async () => {
  const result = await fetchAccount('fake-key', async url => ({ ok: url.pathname.includes('whoami'), status: 503, json: async () => ({ user: { id: 'u1' } }) }));
  assert.equal(result.remaining, null);
  assert.equal(result.periodEnd, null);
  assert.equal(result.errors.length, 2);
});
test('无计费起点时不会把所有历史消费误报为当月消费', async () => {
  let count = 0;
  const result = await fetchAccount('fake-key', async url => { count++; return { ok: true, json: async () => url.pathname.includes('whoami') ? { user: { id: 'u1' } } : {} }; });
  assert.equal(count, 3);
  assert.equal(result.spent, null);
});
test('预警区分窗口重置、套餐周期和无窗口限制账号', () => {
  const settings = { warning: 80, critical: 95, lowBalance: 5 };
  const now = Date.parse('2026-10-13T00:00:00Z');
  const data = normalize({ user: { id: 'u1' } }, { credits: { monthlyCredits: 2 }, windowLimits: { limited: true, fiveHour: { used: 96, cap: 100, resetAt: '2026-10-13T04:00:00Z' }, weekly: { used: 90, cap: 100, resetAt: '2026-10-12T00:00:00Z' } } }, { data: { currentPeriodEnd: '2026-10-15T00:00:00Z' } }, null);
  const account = { id: 'a1', name: '测试账号', data };
  const alerts = alertsFor(account, settings, now);
  assert.equal(alerts.length, 3);
  assert.equal(alerts[0].severity, 'critical');
  assert.equal(alerts.some(a => a.id.includes('weekly')), false);
  data.limited = false;
  assert.equal(alertsFor(account, settings, now).length, 2);
  account.error = '连接失败';
  assert.equal(alertsFor(account, settings, now).length, 0);
});
