const BASE = 'https://api.commandcode.ai';
async function request(key, endpoint, params = {}, fetchFn = fetch) {
  const url = new URL(endpoint, BASE);
  for (const [name, value] of Object.entries(params)) if (value != null) url.searchParams.set(name, value);
  let response;
  try {
    response = await fetchFn(url, { headers: { Authorization: `Bearer ${key}`, Accept: 'application/json' }, signal: AbortSignal.timeout(20000), redirect: 'error' });
  } catch {
    throw new Error('无法连接 CommandCode，请检查网络后重试');
  }
  if (!response.ok) throw new Error(response.status === 401 || response.status === 403 ? 'API Key 无效、已撤销或没有访问权限' : `CommandCode 返回 HTTP ${response.status}`);
  const data = await response.json();
  if (!data || data.success === false) throw new Error('CommandCode 未返回有效数据');
  return data;
}
const numeric = value => typeof value === 'number' && Number.isFinite(value) ? value : null;
function windowView(value) {
  if (!value) return null;
  const used = numeric(value.used);
  const cap = numeric(value.cap);
  return { used, cap, percent: used !== null && cap !== null && cap > 0 ? used / cap * 100 : null, resetAt: value.resetAt || null };
}
function normalize(whoami, credits, subscription, summary) {
  const c = credits?.credits;
  const sub = subscription?.data;
  const fields = c ? [c.monthlyCredits, c.purchasedCredits, c.freeCredits] : [];
  const remaining = fields.length && fields.some(v => numeric(v) !== null) && fields.every(v => v == null || numeric(v) !== null) ? fields.reduce((sum, v) => sum + (v ?? 0), 0) : null;
  return {
    user: whoami.user ? { id: whoami.user.id, name: whoami.user.name, userName: whoami.user.userName, email: whoami.user.email } : null,
    org: whoami.org ? { id: whoami.org.id, name: whoami.org.name, login: whoami.org.login } : null,
    plan: sub?.planId ?? c?.planId ?? null,
    subscriptionStatus: sub?.status ?? null,
    periodEnd: sub?.currentPeriodEnd ?? null,
    cancelAtPeriodEnd: sub?.cancelAtPeriodEnd ?? null,
    remaining,
    monthlyRemaining: numeric(c?.monthlyCredits),
    purchasedRemaining: numeric(c?.purchasedCredits),
    freeRemaining: numeric(c?.freeCredits),
    spent: numeric(summary?.totalCost),
    limited: credits?.windowLimits?.limited ?? null,
    fiveHour: windowView(credits?.windowLimits?.fiveHour),
    weekly: windowView(credits?.windowLimits?.weekly),
    fetchedAt: new Date().toISOString()
  };
}
async function fetchAccount(key, fetchFn = fetch) {
  const whoami = await request(key, '/alpha/whoami', { limits: '1' }, fetchFn);
  if (!whoami.user?.id) throw new Error('无法确认账号身份，请检查 API Key');
  const params = { orgId: whoami.org?.id };
  const endpoints = ['/alpha/billing/credits', '/alpha/billing/subscriptions'];
  const results = await Promise.allSettled(endpoints.map(endpoint => request(key, endpoint, params, fetchFn)));
  const credits = results[0].status === 'fulfilled' ? results[0].value : null;
  const subscription = results[1].status === 'fulfilled' ? results[1].value : null;
  const errors = results.flatMap((r, i) => r.status === 'rejected' ? [`${i ? '套餐' : '额度'}：${r.reason.message}`] : []);
  let summary = null;
  if (subscription?.data?.currentPeriodStart) {
    try { summary = await request(key, '/alpha/usage/summary', { ...params, since: subscription.data.currentPeriodStart }, fetchFn); }
    catch (error) { errors.push(`消费汇总：${error.message}`); }
  }
  return { ...normalize(whoami, credits, subscription, summary), errors };
}
function alertsFor(account, settings, now = Date.now()) {
  const data = account.data;
  if (!data || account.error || data.errors?.length) return [];
  const alerts = [];
  for (const [name, label] of [['fiveHour', '5 小时'], ['weekly', '每周']]) {
    const window = data[name];
    if (!data.limited || !window || window.percent === null) continue;
    const reset = Date.parse(window.resetAt);
    if (Number.isFinite(reset) && reset <= now) continue;
    const threshold = window.percent >= settings.critical ? settings.critical : window.percent >= settings.warning ? settings.warning : null;
    if (threshold !== null) alerts.push({ id: `${account.id}:${name}:${threshold}:${window.resetAt}`, severity: threshold === settings.critical ? 'critical' : 'warning', text: `${account.name} · ${label}额度已用 ${window.percent.toFixed(0)}%` });
  }
  if (data.remaining !== null && data.remaining <= settings.lowBalance) alerts.push({ id: `${account.id}:balance`, severity: 'warning', text: `${account.name} · 剩余额度 $${data.remaining.toFixed(2)}` });
  const end = Date.parse(data.periodEnd);
  if (Number.isFinite(end)) {
    const days = Math.ceil((end - now) / 86400000);
    const bucket = [1, 3, 7].find(value => days <= value);
    if (bucket !== undefined) alerts.push({ id: `${account.id}:period:${bucket}:${data.periodEnd}`, severity: days <= 1 ? 'critical' : 'warning', text: `${account.name} · ${days <= 0 ? '套餐周期已结束，请刷新确认' : `套餐周期将在 ${days} 天内结束`}` });
  }
  return alerts;
}
module.exports = { request, fetchAccount, normalize, windowView, alertsFor };
