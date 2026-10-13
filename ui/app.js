const $ = id => document.getElementById(id);
let state = { accounts: [], settings: { interval: 300, warning: 80, critical: 95, lowBalance: 5, notifications: true } };
let method = 'password';
let toastTimer;
const desktop = Boolean(window.manager);
const money = value => Number.isFinite(value) ? `$${value.toFixed(2)}` : '未获取';
const date = value => value && Number.isFinite(Date.parse(value)) ? new Date(value).toLocaleString('zh-CN', { timeZone: 'Asia/Hong_Kong', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false }) : '未获取';
function element(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text != null) node.textContent = text;
  return node;
}
function toast(message) {
  $('toast').textContent = message;
  $('toast').hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { $('toast').hidden = true; }, 8000);
}
async function call(channel, payload) {
  if (!desktop) throw new Error('请启动桌面客户端后使用此功能');
  const result = await window.manager.call(channel, payload);
  if (!result.ok) throw new Error(result.error);
  return result.value;
}
function meter(label, data) {
  const root = element('div', 'meter');
  const expired = data?.resetAt && Date.parse(data.resetAt) <= Date.now();
  const percent = data?.percent;
  const row = element('div', 'meter-label');
  row.append(element('span', '', label), element('span', '', expired ? '窗口已结束，待刷新' : Number.isFinite(percent) ? `${percent.toFixed(0)}% · ${money(data.used)} / ${money(data.cap)}` : '未获取'));
  const track = element('div', 'meter-track');
  const severity = percent >= state.settings.critical ? 'critical' : percent >= state.settings.warning ? 'warning' : '';
  const fill = element('div', `meter-fill ${severity}`);
  fill.style.setProperty('--ratio', expired ? 0 : Math.max(0, Math.min(1, (percent || 0) / 100)));
  track.append(fill);
  root.append(row, track, element('small', '', `重置：${date(data?.resetAt)}`));
  return root;
}
function actionButton(label, className, action) {
  const button = element('button', className, label);
  button.addEventListener('click', async () => {
    button.disabled = true;
    try { await action(); } catch (error) { toast(error.message); }
    finally { button.disabled = false; }
  });
  return button;
}
function card(account) {
  const data = account.data || {};
  const root = element('article', `account-card${account.active ? ' active' : ''}`);
  const top = element('div', 'card-top');
  const identity = element('div', 'identity');
  identity.append(element('h3', '', account.name), element('p', '', data.user?.email || data.user?.userName || '身份未获取'));
  top.append(element('div', 'avatar', account.name.slice(0, 1).toUpperCase()), identity, element('span', `badge ${account.active ? 'active' : account.alerts?.length ? 'warning' : ''}`, account.active ? '当前账号' : account.alerts?.length ? '需要关注' : '已添加'));
  const balance = element('div', 'card-balance');
  const amount = element('div');
  amount.append(element('label', '', '剩余额度'), element('strong', '', money(data.remaining)));
  const plan = element('div', 'plan', data.plan || '套餐未获取');
  plan.append(element('small', '', ({ active: '套餐有效', canceled: '套餐已取消', past_due: '待付款' })[data.subscriptionStatus] || data.subscriptionStatus || '状态未获取'));
  balance.append(amount, plan);
  root.append(top, balance);
  if (account.error || data.errors?.length) root.append(element('div', 'card-error', `${account.error || data.errors.join('；')}。下方已获取数值可能是上次同步的数据。`));
  if (data.limited === false) root.append(element('p', 'credit-detail', '此账号不适用 5 小时 / 每周额度限制'));
  else root.append(meter('5 小时额度', data.fiveHour), meter('每周额度', data.weekly));
  root.append(element('div', 'cycle', `套餐周期结束：${date(data.periodEnd)}${data.cancelAtPeriodEnd === true ? ' · 到期取消' : data.cancelAtPeriodEnd === false ? ' · 到期续费' : ''}`));
  root.append(element('div', 'credit-detail', `周期余额 ${money(data.monthlyRemaining)} · 购买余额 ${money(data.purchasedRemaining)} · 免费余额 ${money(data.freeRemaining)}`));
  const footer = element('div', 'card-footer');
  footer.append(element('small', '', `${account.method === 'password' ? '密码登录' : account.method === 'import' ? '本机导入' : 'API Key'} · 同步 ${date(data.fetchedAt)}`));
  const buttons = element('div');
  buttons.append(actionButton('移除', 'delete', async () => { state = await call('remove', account.id); render(); }));
  const switchButton = actionButton(account.active ? '正在使用' : '切换账号 ↗', account.active ? '' : 'primary', async () => {
    const result = await call('switch', account.id);
    state = result.state; render(); toast(result.message);
  });
  switchButton.disabled = account.active || state.refreshing;
  buttons.append(switchButton);
  footer.append(buttons);
  root.append(footer);
  return root;
}
function render() {
  const accounts = state.accounts;
  const filter = $('search').value.toLowerCase();
  const matching = accounts.filter(a => `${a.name} ${a.data?.user?.email || ''} ${a.data?.plan || ''}`.toLowerCase().includes(filter));
  $('accounts').replaceChildren(...matching.map(card));
  if (accounts.length && !matching.length) $('accounts').append(element('p', '', '没有匹配的账号'));
  $('empty').hidden = accounts.length > 0;
  $('total').replaceChildren(document.createTextNode(`${accounts.length} `), element('small', '', '个账号'));
  const known = accounts.filter(a => Number.isFinite(a.data?.remaining) && !a.error && !a.data.errors?.length);
  $('balance').textContent = known.length ? money(known.reduce((sum, a) => sum + a.data.remaining, 0)) : '—';
  $('balance-note').textContent = accounts.length ? `${known.length} / ${accounts.length} 个账号成功获取` : '添加账号后自动同步';
  const affected = accounts.filter(a => a.alerts?.length || a.error || a.data?.errors?.length);
  $('warning-total').replaceChildren(document.createTextNode(`${affected.length} `), element('small', '', '个账号'));
  $('active-name').textContent = accounts.find(a => a.active)?.name || '尚未识别';
  $('list-count').textContent = String(accounts.length).padStart(2, '0');
  $('alert-count').textContent = affected.length;
  $('refresh').disabled = state.refreshing || !desktop;
  $('refresh').textContent = state.refreshing ? '↻ 正在同步…' : '↻ 刷新用量';
  $('monitor-status').textContent = state.refreshing ? '正在刷新账号用量' : `每 ${state.settings.interval / 60} 分钟自动刷新`;
  $('environment-banner').hidden = !state.environmentOverride;
  $('environment-banner').textContent = '检测到环境变量覆盖登录账号。请清除 COMMAND_CODE_API_KEY 后再切换；已有终端也需要检查该变量。';
  $('alert-list').replaceChildren();
  for (const account of affected) {
    const row = element('div', 'alert-row');
    const details = element('div');
    details.append(element('strong', '', account.name));
    for (const alert of account.alerts || []) details.append(element('p', '', alert.text));
    if (account.error || account.data?.errors?.length) details.append(element('p', '', account.error || account.data.errors.join('；')));
    row.append(element('span', 'amber', '◉'), details);
    $('alert-list').append(row);
  }
  if (!affected.length) $('alert-list').append(element('div', 'empty', accounts.length ? '当前没有需要关注的账号' : '添加账号后显示真实用量提醒'));
}
function showPage(page) {
  for (const id of ['overview', 'alerts', 'settings']) { $(id).hidden = id !== page; $(`${id}-nav`).classList.toggle('selected', id === page); }
  $('page-title').textContent = ({ overview: '账号总览', alerts: '预警中心', settings: '监控设置' })[page];
  if (page === 'settings') for (const [name, value] of Object.entries(state.settings)) {
    const input = $('settings-form').elements[name];
    if (input.type === 'checkbox') input.checked = value; else input.value = value;
  }
}
function chooseMethod(value) {
  method = value;
  $('password-tab').classList.toggle('selected', value === 'password');
  $('api-tab').classList.toggle('selected', value === 'api');
  $('password-section').hidden = value !== 'password';
  $('api-section').hidden = value !== 'api';
  $('api-key').required = value === 'api';
  $('submit-add').textContent = value === 'password' ? '打开官方登录窗口' : '验证并添加账号';
  $('add-error').textContent = '';
}
function openAdd() { $('add-error').textContent = ''; $('add-dialog').showModal(); }
for (const id of ['add', 'empty-add']) $(id).addEventListener('click', openAdd);
$('close-dialog').addEventListener('click', () => { $('add-dialog').close(); $('api-key').value = ''; });
$('add-dialog').addEventListener('close', () => { $('api-key').value = ''; });
$('password-tab').addEventListener('click', () => chooseMethod('password'));
$('api-tab').addEventListener('click', () => chooseMethod('api'));
$('search').addEventListener('input', render);
for (const page of ['overview', 'alerts', 'settings']) $(`${page}-nav`).addEventListener('click', () => showPage(page));
$('add-form').addEventListener('submit', async event => {
  event.preventDefault();
  $('submit-add').disabled = true;
  $('password-tab').disabled = true;
  $('api-tab').disabled = true;
  $('add-error').textContent = '';
  try {
    const payload = { name: $('account-name').value.trim(), apiKey: $('api-key').value.trim() };
    state = await call(method === 'password' ? 'login' : 'add', payload);
    $('api-key').value = ''; $('account-name').value = ''; $('add-dialog').close(); render(); toast('账号已添加，自动监控已开启');
  } catch (error) { $('add-error').textContent = error.message; }
  finally { $('submit-add').disabled = false; $('password-tab').disabled = false; $('api-tab').disabled = false; }
});
$('refresh').addEventListener('click', async () => { try { state = await call('refresh'); render(); } catch (error) { toast(error.message); } });
$('import').addEventListener('click', async () => { $('import').disabled = true; try { state = await call('import'); render(); toast('本机账号已导入'); } catch (error) { toast(error.message); } finally { $('import').disabled = false; } });
$('docs').addEventListener('click', async () => { try { await call('docs'); } catch (error) { toast(error.message); } });
$('settings-form').addEventListener('submit', async event => {
  event.preventDefault();
  const fields = $('settings-form').elements;
  const payload = { notifications: fields.notifications.checked };
  for (const key of ['interval', 'warning', 'critical', 'lowBalance']) payload[key] = Number(fields[key].value);
  try { state = await call('settings', payload); render(); toast('监控设置已保存'); } catch (error) { toast(error.message); }
});
chooseMethod('password');
render();
if (desktop) {
  window.manager.onState(value => { state = value; render(); });
  call('state').then(value => { state = value; render(); }).catch(error => toast(error.message));
} else { $('preview-banner').hidden = false; $('monitor-status').textContent = '桌面界面预览'; }
