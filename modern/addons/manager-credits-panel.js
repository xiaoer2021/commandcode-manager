/**
 * Command Code 额度面板（常驻）
 * ---------------------------------------------------------------
 * 解决什么问题：
 *   官方界面里其实有额度条，但只在「5 小时窗口 / 周窗口用到 85%」时才出现
 *   （workspace-screen 里的 AF=.85 阈值），平时完全静默 —— 所以用起来心里没底。
 *   本脚本常驻显示剩余额度和两个滚动窗口的消耗。
 *
 * 显示位置（2026-09-24 改）：
 *   **嵌在左侧边栏底部、账号行（用户名 · 套餐）上方那块空白**，随侧边栏一起布局。
 *   原来挂在右下角的悬浮胶囊，用户反馈"看不到"。
 *   锚点取 `button[aria-label="Settings"]`（整份 bundle 唯一），插在它的祖父容器里、
 *   账号行之前；找不到锚点（侧边栏收起 / 界面改版）时自动退回右下角悬浮。
 *
 * 实现方式：
 *   - 不改应用源码，只往 DOM 里插自己的节点 + 一张自己的样式表
 *   - 数据源用应用自带的 IPC：window.electronAPI.accountOverview({refresh})
 *     （渲染层 CSP 是 connect-src 'self'，不能直接 fetch 外部 API；走 IPC 由主进程代发，合法且稳定）
 *   - 不额外消耗额度：/alpha/billing/credits 是账单只读接口，不计费；主进程还有 60s 缓存
 *   - 放置结果会通过应用自带的 log-renderer 通道写进 main.log，便于排查（只读，无副作用）
 *
 * 数据字段来源（main/index.js 的 toOverview）：
 *   overview.credits.monthlyCredits   本月剩余额度
 *   overview.usage.totalCost          本周期已用
 *   overview.usage.totalCount / averageCost / successRate
 *   overview.windowLimits.fiveHour|weekly  { used, cap, resetAt, exceeded }
 *   overview.subscription.planId / currentPeriodEnd
 */
(() => {
  'use strict';

  // 幂等：重复注入 / 热重载时不重复建面板
  if (window.__cmdCreditsPanelLoaded) return;
  window.__cmdCreditsPanelLoaded = true;

  const HOST_ID = 'cmd-credits-panel';
  if (document.getElementById(HOST_ID)) return;

  const POLL_MS = 60000; // 与主进程 ACCOUNT_CACHE_TTL_MS 一致
  const WARN_RATIO = 0.85; // 与官方内置额度条的告警阈值保持一致
  const DANGER_RATIO = 0.95;
  const STORE_KEY = 'cmd-credits-panel-open';

  let host = null;
  let timer = null;
  let enabled = false;
  let started = false;
  let visibilityBound = false;
  let settleTimer = null;
  let lastDiag = '';
  let lastVia = '';
  let dumpCount = 0;
  let bootTries = 0;
  let open = false;
  try {
    open = localStorage.getItem(STORE_KEY) === '1';
  } catch (_) {}

  const state = { ov: null, error: null, loading: false, at: 0 };

  // ---------------------------------------------------------------- 工具函数
  /** 金额：整数省略小数位（$14 而不是 $14.00），便于在窄胶囊里看清 */
  function usd(n, digits = 2) {
    if (typeof n !== 'number' || !isFinite(n)) return '$—';
    if (Number.isInteger(n)) return '$' + n.toFixed(0);
    return '$' + n.toFixed(digits);
  }
  function count(n) {
    if (typeof n !== 'number' || !isFinite(n)) return '—';
    return n >= 10000 ? (n / 1000).toFixed(1) + 'k' : String(n);
  }
  function ratio(used, cap) {
    if (typeof used !== 'number' || typeof cap !== 'number' || cap <= 0) return 0;
    return Math.max(0, Math.min(1, used / cap));
  }
  function tone(r) {
    if (r >= DANGER_RATIO) return 'danger';
    if (r >= WARN_RATIO) return 'warn';
    return 'ok';
  }
  /** 重置倒计时：<1h 用分钟，<48h 用小时，再往上用天 */
  function resetText(resetAt) {
    if (typeof resetAt !== 'number' || !isFinite(resetAt)) return '';
    if (resetAt === 0) return '首次使用后开始计时';
    const ms = (resetAt < 1e11 ? resetAt * 1000 : resetAt) - Date.now();
    if (ms <= 0) return '即将重置';
    const min = Math.ceil(ms / 60000);
    if (min < 60) return min + ' 分钟后重置';
    const hr = Math.floor(min / 60);
    if (hr < 48) return hr + ' 小时' + (min % 60 ? ' ' + (min % 60) + ' 分钟' : '') + '后重置';
    return Math.round(hr / 24) + ' 天后重置';
  }
  function resetAbs(resetAt) {
    try {
      if (!resetAt) return '';
      const d = new Date(resetAt < 1e11 ? resetAt * 1000 : resetAt);
      return (
        d.getFullYear() + '年' + (d.getMonth() + 1) + '月' + d.getDate() + '日 ' +
        String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0')
      );
    } catch (_) {
      return '';
    }
  }
  function periodEndText(ms) {
    if (typeof ms !== 'number' || !isFinite(ms)) return '';
    try {
      const d = new Date(ms);
      return '周期至 ' + d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
    } catch (_) {
      return '';
    }
  }
  /** individual-goat -> GOAT */
  function planLabel(sub) {
    if (!sub || !sub.planId) return '';
    const id = String(sub.planId);
    const tail = id.split('-').filter(Boolean).pop() || id;
    return tail.toUpperCase();
  }

  // ------------------------------------------------------------------- 样式
  /* 全部走应用自己的 CSS 变量（HSL 三元组），明/暗主题自动跟随，无需监听主题切换 */
  const CSS = `
#${HOST_ID}{
  z-index:1000;
  font-family:var(--font-ui, ui-sans-serif, system-ui, sans-serif);
  font-size:12px; line-height:1.4;
  -webkit-app-region:no-drag;
}
/* 侧边栏模式（默认）：嵌在侧边栏底部、账号行上方那块空白里，随侧边栏一起布局 */
#${HOST_ID}[data-place="sidebar"]{
  position:relative; display:block;
  width:100%; min-width:0; margin:0;
}
/* 悬浮模式：只有在找不到侧边栏账号区时才用（比如侧边栏收起 / 界面改版） */
#${HOST_ID}[data-place="floating"]{
  position:fixed; right:14px; bottom:14px;
  pointer-events:none;
}
#${HOST_ID} *{box-sizing:border-box}
#${HOST_ID} [hidden]{display:none !important}
#${HOST_ID} > *{pointer-events:auto}

#${HOST_ID} .ccd-pill{
  display:flex; align-items:center; gap:7px;
  height:28px; padding:0 11px 0 9px;
  border-radius:999px; cursor:pointer;
  border:1px solid hsl(var(--border) / var(--border-alpha, .12));
  background:hsl(var(--popover, 0 0% 100%) / .94);
  color:hsl(var(--popover-foreground, 0 0% 4%));
  backdrop-filter:blur(10px);
  box-shadow:0 1px 2px hsl(var(--border) / .07), 0 6px 16px hsl(var(--border) / .07);
  transition:background .12s var(--ease-out, ease), transform .12s var(--ease-out, ease);
}
#${HOST_ID} .ccd-pill:hover{background:hsl(var(--accent, 240 5% 88%) / .95)}
#${HOST_ID} .ccd-pill:active{transform:scale(.98)}

/* 侧边栏模式：整行铺满，看着就像侧边栏自己的一行 */
#${HOST_ID}[data-place="sidebar"] .ccd-pill{
  width:100%; height:30px; border-radius:8px;
  padding:0 8px; gap:6px;
  background:transparent; box-shadow:none;
}
#${HOST_ID}[data-place="sidebar"] .ccd-pill:hover{background:hsl(var(--accent, 240 5% 88%) / .7)}
#${HOST_ID}[data-place="sidebar"] .ccd-bal{
  flex:1; min-width:0; overflow:hidden;
  text-overflow:ellipsis; white-space:nowrap;
}
#${HOST_ID}[data-place="sidebar"] .ccd-tag{flex:none}
#${HOST_ID}[data-place="sidebar"] .ccd-hero-num{font-size:22px}
#${HOST_ID}[data-place="sidebar"] .ccd-foot{font-size:10px}

#${HOST_ID} .ccd-dot{
  width:7px; height:7px; border-radius:999px; flex:none;
  background:hsl(var(--success, 142 71% 45%));
}
#${HOST_ID} .ccd-dot[data-tone="warn"]{background:hsl(var(--warning, 38 92% 48%))}
#${HOST_ID} .ccd-dot[data-tone="danger"]{background:hsl(var(--destructive, 0 72% 51%))}
#${HOST_ID} .ccd-dot[data-tone="err"]{background:hsl(var(--muted-foreground, 240 4% 40%))}
#${HOST_ID} .ccd-dot[data-tone="loading"]{background:hsl(var(--muted-foreground, 240 4% 40%)); animation:ccd-pulse 1.1s ease-in-out infinite}
@keyframes ccd-pulse{0%,100%{opacity:.35}50%{opacity:1}}

#${HOST_ID} .ccd-bal{font-weight:600; letter-spacing:-.01em}
#${HOST_ID} .ccd-tag{
  color:hsl(var(--muted-foreground, 240 4% 40%));
  font-variant-numeric:tabular-nums;
}
#${HOST_ID} .ccd-tag b{font-weight:600; color:inherit}

/* 展开的明细卡：悬浮模式下向上弹出 */
#${HOST_ID}[data-place="floating"] .ccd-card{
  position:absolute; right:0; bottom:36px; width:300px;
  padding:12px 14px 9px;
  border-radius:12px;
  border:1px solid hsl(var(--border) / var(--border-alpha, .12));
  background:hsl(var(--popover, 0 0% 100%));
  color:hsl(var(--popover-foreground, 0 0% 4%));
  box-shadow:0 10px 34px hsl(var(--border) / .18), 0 2px 6px hsl(var(--border) / .08);
}
/* 侧边栏模式下原地展开（不弹层，避免被侧边栏的 overflow 裁掉） */
#${HOST_ID}[data-place="sidebar"] .ccd-card{
  position:static; width:auto;
  margin:8px 0 2px;
  padding:11px 12px 8px;
  border-radius:10px;
  border:1px solid hsl(var(--border) / var(--border-alpha, .12));
  background:hsl(var(--popover, 0 0% 100%) / .6);
  color:hsl(var(--popover-foreground, 0 0% 4%));
}
#${HOST_ID} .ccd-head{display:flex; align-items:center; gap:6px; margin-bottom:10px}
#${HOST_ID} .ccd-title{font-weight:600}
#${HOST_ID} .ccd-plan{
  padding:1px 6px; border-radius:5px; font-size:10px; font-weight:600; letter-spacing:.02em;
  background:hsl(var(--primary, 248.8 70.5% 35.9%) / .12);
  color:hsl(var(--primary, 248.8 70.5% 35.9%));
}
#${HOST_ID} .ccd-flex{flex:1}
#${HOST_ID} .ccd-icon{
  width:22px; height:22px; padding:0; cursor:pointer;
  display:inline-flex; align-items:center; justify-content:center;
  border-radius:6px; border:none; background:transparent;
  color:hsl(var(--muted-foreground, 240 4% 40%)); font-size:13px; line-height:1;
}
#${HOST_ID} .ccd-icon:hover{background:hsl(var(--accent, 240 5% 88%) / .8); color:hsl(var(--popover-foreground, 0 0% 4%))}
#${HOST_ID} .ccd-icon[data-spin="1"]{animation:ccd-spin 900ms linear infinite}
@keyframes ccd-spin{to{transform:rotate(360deg)}}

#${HOST_ID} .ccd-hero{margin-bottom:12px}
#${HOST_ID} .ccd-hero-num{
  font-size:24px; font-weight:700; letter-spacing:-.02em; cursor:pointer;
  font-variant-numeric:tabular-nums;
}
#${HOST_ID} .ccd-hero-num:hover{opacity:.75}
#${HOST_ID} .ccd-hero-lab{color:hsl(var(--muted-foreground, 240 4% 40%)); margin-top:1px}
#${HOST_ID} .ccd-hero-sub{color:hsl(var(--muted-foreground, 240 4% 40%)); margin-top:3px; font-size:11px}

#${HOST_ID} .ccd-meter{margin-bottom:11px}
#${HOST_ID} .ccd-meter-top{display:flex; justify-content:space-between; align-items:baseline; margin-bottom:5px}
#${HOST_ID} .ccd-meter-name{color:hsl(var(--muted-foreground, 240 4% 40%))}
#${HOST_ID} .ccd-meter-val{font-variant-numeric:tabular-nums; font-weight:600}
#${HOST_ID} .ccd-bar{height:5px; border-radius:999px; overflow:hidden; background:hsl(var(--muted, 240 5% 92%))}
/* 健康色用语义变量而非 --primary：purple 主题的 primary 是黄色，会和 warning 撞色 */
#${HOST_ID} .ccd-bar > i{
  display:block; height:100%; border-radius:999px; width:0;
  background:hsl(var(--success, 142 71% 40%));
  transition:width .35s var(--ease-out, ease), background .2s ease;
}
#${HOST_ID} .ccd-bar[data-tone="warn"] > i{background:hsl(var(--warning, 38 92% 48%))}
#${HOST_ID} .ccd-bar[data-tone="danger"] > i{background:hsl(var(--destructive, 0 72% 51%))}
#${HOST_ID} .ccd-meter-foot{
  margin-top:4px; font-size:11px;
  color:hsl(var(--muted-foreground, 240 4% 40%));
}
#${HOST_ID} .ccd-meter-foot b{color:hsl(var(--warning, 38 92% 48%)); font-weight:600}
#${HOST_ID} .ccd-meter-foot[data-tone="danger"] b{color:hsl(var(--destructive, 0 72% 51%))}

#${HOST_ID} .ccd-stats{
  padding-top:9px; margin-top:2px;
  border-top:1px solid hsl(var(--border) / var(--border-alpha, .12));
  color:hsl(var(--muted-foreground, 240 4% 40%));
  font-variant-numeric:tabular-nums;
}
#${HOST_ID} .ccd-foot{margin-top:6px; font-size:10px; color:hsl(var(--muted-foreground, 240 4% 40%) / .8)}
@media (prefers-reduced-motion: reduce){
  #${HOST_ID} .ccd-bar > i, #${HOST_ID} .ccd-pill{transition:none}
  #${HOST_ID} .ccd-icon[data-spin="1"]{animation:none}
  #${HOST_ID} .ccd-dot[data-tone="loading"]{animation:none; opacity:.7}
}
`;

  // ------------------------------------------------------------------- 骨架
  const SKELETON = `
<button class="ccd-pill" type="button" data-el="pill">
  <span class="ccd-dot" data-el="dot" data-tone="loading"></span>
  <span class="ccd-bal" data-el="pillBal">额度加载中…</span>
  <span class="ccd-tag" data-el="pill5" hidden></span>
  <span class="ccd-tag" data-el="pillW" hidden></span>
</button>
<section class="ccd-card" data-el="card" hidden>
  <div class="ccd-head">
    <span class="ccd-title">额度用量</span>
    <span class="ccd-plan" data-el="plan" hidden></span>
    <span class="ccd-flex"></span>
    <button class="ccd-icon" type="button" data-el="btnRefresh" title="立即刷新">&#8635;</button>
  </div>
  <div class="ccd-hero">
    <div class="ccd-hero-num" data-el="heroNum" title="点击立即刷新">$—</div>
    <div class="ccd-hero-lab">本月剩余额度</div>
    <div class="ccd-hero-sub" data-el="heroSub"></div>
  </div>
  <div class="ccd-meter">
    <div class="ccd-meter-top">
      <span class="ccd-meter-name">5 小时窗口</span>
      <span class="ccd-meter-val" data-el="fiveVal">—</span>
    </div>
    <div class="ccd-bar" data-el="fiveBar"><i></i></div>
    <div class="ccd-meter-foot" data-el="fiveFoot"></div>
  </div>
  <div class="ccd-meter">
    <div class="ccd-meter-top">
      <span class="ccd-meter-name">本周窗口</span>
      <span class="ccd-meter-val" data-el="weekVal">—</span>
    </div>
    <div class="ccd-bar" data-el="weekBar"><i></i></div>
    <div class="ccd-meter-foot" data-el="weekFoot"></div>
  </div>
  <div class="ccd-stats" data-el="stats"></div>
  <div class="ccd-foot" data-el="foot">每 60 秒自动刷新 · 点数字立即刷新 · 点胶囊或 Esc 收起</div>
</section>
`;

  // ------------------------------------------------------------------- 构建
  function build() {
    if (host) return;
    const style = document.createElement('style');
    style.setAttribute('data-cmd-credits', '1');
    style.textContent = CSS;
    document.head.appendChild(style);

    host = document.createElement('div');
    host.id = HOST_ID;
    // 先当悬浮处理（万一侧边栏锚点找不到，也不会闪一个没定位的块）
    host.setAttribute('data-place', 'floating');
    // 让汉化器跳过本面板（i18n-zh.js 认这个属性）
    host.setAttribute('data-cmd-zh-skip', '1');
    host.setAttribute('data-cmd-credits', '1');
    host.innerHTML = SKELETON;
    document.body.appendChild(host);

    const q = (name) => host.querySelector('[data-el="' + name + '"]');

    // 胶囊：切换展开
    q('pill').addEventListener('click', (ev) => {
      ev.stopPropagation();
      setOpen(!open);
    });
    // 刷新按钮
    q('btnRefresh').addEventListener('click', (ev) => {
      ev.stopPropagation();
      void load(true);
    });
    // 点大数字也能刷新
    q('heroNum').addEventListener('click', (ev) => {
      ev.stopPropagation();
      void load(true);
    });
    // 点卡片内部不冒泡（防御性：避免将来新增的全局监听误伤卡片）
    q('card').addEventListener('click', (ev) => ev.stopPropagation());

    // 收起方式：只认「点胶囊」和「Esc」。
    // 特意不做「点页面其他地方就收起」—— 那个交互一碰别处就把额度视野打断，
    // 而且收起会写进 localStorage，反过来把用户「想展开」的意愿覆盖掉。
    document.addEventListener('keydown', (ev) => {
      if (ev.key === 'Escape' && open) setOpen(false);
    });

    setOpen(open);
  }

  function setOpen(next) {
    open = !!next;
    const card = host.querySelector('[data-el="card"]');
    if (card) card.hidden = !open;
    host.querySelector('[data-el="pill"]').setAttribute('aria-expanded', open ? 'true' : 'false');
    try {
      localStorage.setItem(STORE_KEY, open ? '1' : '0');
    } catch (_) {}
  }

  // ------------------------------------------------------------------- 渲染
  function paint() {
    if (!host) return;
    const q = (name) => host.querySelector('[data-el="' + name + '"]');
    const dot = q('dot');
    const pill = q('pill');

    // 加载中且尚无数据
    if (!state.ov) {
      if (state.loading) {
        dot.dataset.tone = 'loading';
        q('pillBal').textContent = '额度加载中…';
        q('pill5').hidden = true;
        q('pillW').hidden = true;
        pill.title = '正在读取额度';
        return;
      }
      // 取数失败
      dot.dataset.tone = 'err';
      q('pillBal').textContent = '额度不可用';
      q('pill5').hidden = true;
      q('pillW').hidden = true;
      pill.title = state.error ? '额度信息获取失败：' + state.error : '额度信息不可用';
      q('heroNum').textContent = '$—';
      q('heroSub').textContent = state.error ? String(state.error) : '未登录或暂不可用';
      q('stats').textContent = '';
      q('foot').textContent = '点 ↻ 重试';
      ['plan', 'fiveVal', 'weekVal'].forEach((n) => (q(n).textContent = '—'));
      q('fiveFoot').textContent = '';
      q('weekFoot').textContent = '';
      return;
    }

    const ov = state.ov;
    const cr = ov.credits || {};
    const us = ov.usage || {};
    const wl = ov.windowLimits || null;
    const five = wl && wl.fiveHour ? wl.fiveHour : null;
    const week = wl && wl.weekly ? wl.weekly : null;

    const r5 = five ? ratio(five.used, five.cap) : 0;
    const rw = week ? ratio(week.used, week.cap) : 0;
    const worst = Math.max(r5, rw);

    // ---- 胶囊 ----
    dot.dataset.tone = tone(worst);
    q('pillBal').textContent = usd(cr.monthlyCredits) + ' 剩余';

    if (five) {
      const t = q('pill5');
      t.hidden = false;
      t.innerHTML = '5h <b>' + String(Math.round(r5 * 100)) + '%</b>';
      t.title = '5 小时窗口已用 ' + usd(five.used) + ' / ' + usd(five.cap) + '（' + resetText(five.resetAt) + '）';
    }
    if (week) {
      const t = q('pillW');
      t.hidden = false;
      t.innerHTML = '周 <b>' + String(Math.round(rw * 100)) + '%</b>';
      t.title = '本周窗口已用 ' + usd(week.used) + ' / ' + usd(week.cap) + '（' + resetText(week.resetAt) + '）';
    }
    pill.title =
      '本月剩余 ' + usd(cr.monthlyCredits) +
      (five ? '\n5 小时：已用 ' + usd(five.used) + ' / ' + usd(five.cap) : '') +
      (week ? '\n本周：已用 ' + usd(week.used) + ' / ' + usd(week.cap) : '');

    // ---- 卡片头部 ----
    const pl = planLabel(ov.subscription);
    const planEl = q('plan');
    if (pl) {
      planEl.hidden = false;
      planEl.textContent = pl;
      planEl.title = ov.subscription && ov.subscription.planId ? ov.subscription.planId : '';
    } else {
      planEl.hidden = true;
    }

    // ---- 大数字 ----
    q('heroNum').textContent = usd(cr.monthlyCredits);
    const subParts = [];
    if (typeof us.totalCost === 'number') subParts.push('本期请求费用 ' + usd(us.totalCost));
    if (ov.subscription && ov.subscription.currentPeriodEnd) {
      const p = periodEndText(ov.subscription.currentPeriodEnd);
      if (p) subParts.push(p);
    }
    if (cr.belowThreshold) subParts.push('已低于告警线');
    q('heroSub').textContent = subParts.join(' · ');

    // ---- 两个窗口 ----
    paintMeter(q, 'five', five, r5, '5 小时窗口');
    paintMeter(q, 'week', week, rw, '本周窗口');

    // ---- 统计 ----
    const stats = [];
    if (typeof us.totalCount === 'number') stats.push(count(us.totalCount) + ' 次请求');
    if (typeof us.averageCost === 'number') stats.push('平均 ' + usd(us.averageCost, 4));
    if (typeof us.successRate === 'number') stats.push('成功率 ' + Math.round(us.successRate) + '%');
    q('stats').textContent = stats.join(' · ');

    const age = state.at ? Math.max(0, Math.round((Date.now() - state.at) / 1000)) : null;
    q('foot').textContent = state.loading
      ? '正在刷新…'
      : '每 60 秒自动刷新；点数字可立即刷新' + (age !== null ? '（' + age + ' 秒前更新）' : '');
  }

  function paintMeter(q, key, win, r, label) {
    const valEl = q(key + 'Val');
    const barEl = q(key + 'Bar');
    const footEl = q(key + 'Foot');
    if (!win) {
      valEl.textContent = '—';
      barEl.querySelector('i').style.width = '0%';
      barEl.dataset.tone = 'ok';
      footEl.textContent = '该窗口未启用';
      return;
    }
    valEl.textContent = usd(win.used) + ' / ' + usd(win.cap);
    const i = barEl.querySelector('i');
    i.style.width = String(Math.min(100, Math.round(r * 1000) / 10)) + '%';
    barEl.dataset.tone = tone(r);
    footEl.dataset.tone = tone(r);
    const abs = resetAbs(win.resetAt);
    let txt = resetText(win.resetAt);
    if (abs) txt += '（' + abs + '）';
    if (win.exceeded) txt = '已超额 · ' + txt;
    footEl.innerHTML = '';
    if (win.exceeded) {
      const b = document.createElement('b');
      b.textContent = '已超额';
      footEl.appendChild(b);
      footEl.appendChild(document.createTextNode(' · ' + txt.replace('已超额 · ', '')));
    } else {
      footEl.textContent = label ? txt : txt;
    }
    valEl.title = label + '：已用 ' + Math.round(r * 100) + '%';
  }

  // ------------------------------------------------------------------- 取数
  async function load(refresh) {
    if (!enabled) return;
    const api = window.electronAPI;
    const fn = api && api.accountOverview;
    if (typeof fn !== 'function') {
      state.error = '未找到额度接口（accountOverview），应用版本可能已变更';
      paint();
      return;
    }
    if (state.loading) return;
    state.loading = true;
    const btn = host && host.querySelector('[data-el="btnRefresh"]');
    if (btn) btn.dataset.spin = '1';
    if (!state.ov) paint();

    try {
      const res = await fn.call(api, { refresh: !!refresh });
      if (res && res.success && res.overview) {
        const target = window.__cmdManagerCreditsUser;
        if (target && (String(res.overview.user && res.overview.user.id) + '|' + String(res.overview.user && res.overview.user.org && res.overview.user.org.id || '')) !== target) { if (host) host.style.setProperty('display', 'none', 'important'); return; }
        if (enabled && host) host.style.removeProperty('display');
        state.ov = res.overview;
        state.error = null;
        state.at = Date.now();
      } else {
        state.error = (res && res.error) || '未知错误';
      }
    } catch (e) {
      state.error = (e && e.message) || String(e);
    } finally {
      state.loading = false;
      if (btn) btn.dataset.spin = '0';
      paint();
    }
  }

  function schedule() {
    if (timer) clearInterval(timer);
    timer = setInterval(() => {
      // 后台不刷，省一次无意义的 IPC
      if (document.visibilityState !== 'visible') return;
      void load(false);
    }, POLL_MS);
    // 从后台切回前台：立即补刷一次（轮询计时器可能刚好在后台期间走过）
    if (visibilityBound) return;
    visibilityBound = true;
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') void load(false);
    });
  }

  // ------------------------------------------------- 位置：侧边栏底部 / 悬浮
  /**
   * 账号行锚点（从 workspace-screen 的真实 JSX 里读出来的结构）：
   *   footer  div.shrink-0.space-y-0.5.border-t.border-hairline.px-3.pb-3.pt-2.5
   *     ├ 余额偏低提示（默认不渲染）
   *     ├ 更新提示（默认不渲染）
   *     └ 账号行 div.flex.h-10.w-full（头像 + 用户名 · 套餐 + 齿轮按钮）
   *   面板插在账号行**之前**，正好落在「用户名上方那块空白」。
   *
   * 锚点用四条策略依次尝试，第一条命中即用（真机上哪条命中的会写进 main.log）：
   *   ① 账号行上的设置齿轮按钮：button[aria-label="Settings"] / button[title="Settings"]
   *   ② 结构法：侧边栏 nav[aria-label="Views"] 的祖先里，带 border-t 的那块 footer
   *   ③ 结构法（不依赖任何文案）：class 同时含 shrink-0 + border-t + px-3 的窄条容器
   *   ④ 文本法：套餐名（GOAT/PRO/MAX/TEAM…）所在的那一行
   */
  function isRow(el) {
    const c = String((el && el.className) || '');
    return c.indexOf('flex') >= 0 && c.indexOf('h-10') >= 0;
  }

  function usableFooter(footer, row) {
    if (!footer || !row) return null;
    if (String(footer.className || '').indexOf('border-t') < 0) return null;
    const r = footer.getBoundingClientRect();
    if (r.height < 20 || r.width < 140) return null; // 侧边栏收起 / 太窄就别硬塞
    return { footer: footer, row: row };
  }

  /** 从某个元素往上找「账号行」那一层 */
  function climbToRow(el, maxUp) {
    let p = el;
    for (let i = 0; i <= (maxUp || 3) && p; i++) {
      if (isRow(p)) return p;
      p = p.parentElement;
    }
    return null;
  }

  /** ④ 用套餐名 / 账号名定位那一行 */
  function rowByPlanText() {
    const PLAN = /^(GOAT|PRO|MAX|TEAM|GO|PLUS|ULTRA|FREE)$/i;
    let nodes;
    try {
      nodes = document.querySelectorAll('span,div');
    } catch (_) {
      return null;
    }
    for (let i = 0; i < nodes.length; i++) {
      const e = nodes[i];
      if (e.children.length) continue;
      const t = (e.textContent || '').replace(/^[\s·]+/, '').trim();
      if (!t || t.length > 16 || !PLAN.test(t)) continue;
      const row = climbToRow(e);
      if (row) return row;
    }
    return null;
  }

  /** ③ 纯结构：窄条 footer 容器（shrink-0 + border-t + px-3） */
  function footerByStructure() {
    let nodes;
    try {
      nodes = document.querySelectorAll('div');
    } catch (_) {
      return null;
    }
    for (let i = 0; i < nodes.length; i++) {
      const e = nodes[i];
      const c = String(e.className || '');
      if (c.indexOf('shrink-0') < 0 || c.indexOf('border-t') < 0 || c.indexOf('px-3') < 0) continue;
      const r = e.getBoundingClientRect();
      if (r.width < 140 || r.width > 460 || r.height < 24) continue;
      const row = climbToRow(e.lastElementChild, 1) || e.lastElementChild;
      const ok = usableFooter(e, row);
      if (ok) return { footer: e, row: row };
    }
    return null;
  }

  /** ② 用侧边栏的 nav[aria-label="Views"] 往上找 footer 兄弟 */
  function footerByNav() {
    let nav = null;
    try {
      nav = document.querySelector('nav[aria-label="Views"]');
    } catch (_) {
      return null;
    }
    if (!nav) return null;
    let p = nav.parentElement;
    for (let up = 0; up < 4 && p; up++) {
      const kids = p.children;
      for (let i = 0; i < kids.length; i++) {
        const kid = kids[i];
        if (String(kid.className || '').indexOf('border-t') < 0) continue;
        const row = kid.lastElementChild;
        const ok = usableFooter(kid, row);
        if (ok) return ok;
      }
      p = p.parentElement;
    }
    return null;
  }

  const GEAR_SELECTORS = ['button[aria-label="Settings"]', 'button[title="Settings"]'];

  function findSidebarSlot() {
    // ① 设置齿轮按钮
    for (let i = 0; i < GEAR_SELECTORS.length; i++) {
      const sel = GEAR_SELECTORS[i];
      let list;
      try {
        list = document.querySelectorAll(sel);
      } catch (_) {
        continue;
      }
      for (let k = 0; k < list.length; k++) {
        const row = climbToRow(list[k], 2);
        const ok = usableFooter(row && row.parentElement, row);
        if (ok) {
          ok.via = sel;
          return ok;
        }
      }
    }
    // ② 侧边栏 nav 的兄弟 footer
    const byNav = footerByNav();
    if (byNav) {
      byNav.via = 'nav[aria-label=Views] 的兄弟';
      return byNav;
    }
    // ③ 纯结构
    const byStruct = footerByStructure();
    if (byStruct) {
      byStruct.via = 'shrink-0+border-t+px-3 结构';
      return byStruct;
    }
    // ④ 套餐名
    const row4 = rowByPlanText();
    if (row4) {
      const ok = usableFooter(row4.parentElement, row4);
      if (ok) {
        ok.via = '套餐名所在行';
        return ok;
      }
    }
    return null;
  }

  /** 把面板放到该在的位置；返回实际生效的模式 */
  function place() {
    if (!host) return null;
    const slot = findSidebarSlot();
    if (slot) {
      if (host.parentElement !== slot.footer || host.nextElementSibling !== slot.row) {
        slot.footer.insertBefore(host, slot.row);
      }
      host.dataset.place = 'sidebar';
      lastVia = slot.via || '';
      return 'sidebar';
    }
    if (host.parentElement !== document.body) document.body.appendChild(host);
    host.dataset.place = 'floating';
    return 'floating';
  }

  /** 找不到锚点时，把侧边栏结构 dump 出来（只进日志，不碰界面） */
  function outline() {
    let nav = null;
    try {
      nav = document.querySelector('nav[aria-label="Views"]');
    } catch (_) {}
    const root = nav ? nav.parentElement : null;
    if (!root) {
      return {
        navFound: false,
        bodyKids: Array.prototype.map.call(document.body.children, function (e) {
          return e.tagName.toLowerCase() + (e.id ? '#' + e.id : '') + ' .' + String(e.className || '').slice(0, 40);
        }),
        ariaButtons: Array.prototype.slice
          .call(document.querySelectorAll('button[aria-label]'), 0, 20)
          .map(function (b) {
            return b.getAttribute('aria-label') + ' > ' + String(b.className || '').slice(0, 60);
          }),
      };
    }
    const out = [];
    (function walk(el, d) {
      if (d > 3 || out.length > 34) return;
      const kids = el.children;
      for (let i = 0; i < kids.length; i++) {
        const k = kids[i];
        const r = k.getBoundingClientRect();
        const al = k.getAttribute ? k.getAttribute('aria-label') : null;
        out.push(
          new Array(d + 1).join('  ') +
            k.tagName.toLowerCase() +
            ' .' + String(k.className || '').slice(0, 66) +
            ' [' + Math.round(r.width) + 'x' + Math.round(r.height) + ']' +
            (al ? ' aria=' + al : '')
        );
        walk(k, d + 1);
      }
    })(root, 0);
    return { navFound: true, tree: out };
  }

  /** 渲染层诊断 -> main.log（走应用自带的 log-renderer 通道，只读无副作用） */
  function diag(why) {
    try {
      const api = window.electronAPI;
      if (!api || typeof api.logRenderer !== 'function') return;
      const rect = host ? host.getBoundingClientRect() : null;
      const payload = {
        why: why,
        place: host ? host.dataset.place : null,
        via: lastVia,
        hostRect: rect ? [Math.round(rect.x), Math.round(rect.y), Math.round(rect.width), Math.round(rect.height)] : null,
      };
      // 找不到锚点时额外 dump 一次结构（最多 3 次，别刷日志）
      if (payload.place !== 'sidebar' && dumpCount < 3) {
        dumpCount++;
        payload.outline = outline();
      }
      const sig = JSON.stringify(payload);
      if (sig === lastDiag) return;
      lastDiag = sig;
      api.logRenderer({ level: 'info', message: '[cmd-credits] 放置检查', data: payload });
    } catch (_) {}
  }

  // ------------------------------------------------------------------- 启动
  function start() {
    started = true;
    if (!enabled) return;
    build();
    place();
    paint();
    void load(false);
    schedule();
    diag('启动');
    // 侧边栏可能晚一步挂载，或在工作区/设置页之间切换；低频校正一次就够
    settleTimer = setInterval(function () {
      const before = host ? host.dataset.place : null;
      place();
      if (before !== (host ? host.dataset.place : null)) diag('位置变化');
    }, 2000);
    setTimeout(function () {
      diag('启动后 3 秒');
    }, 3000);
  }

  // preload 正常情况下在脚本执行前就已就绪。
  // 两种兜底：electronAPI 完全没出现 → 等（最多 8 秒）；
  //             electronAPI 在但缺 accountOverview → 判定应用版本变了，快速报错。
  window.__cmdManagerCreditsController = { setEnabled(value) {
    value = value === true;
    if (enabled === value) return;
    enabled = value;
    if (!enabled) {
      clearInterval(timer);
      clearInterval(settleTimer);
      timer = settleTimer = null;
      state.ov = null; state.error = null;
      if (host) host.style.setProperty('display', 'none', 'important');
      return;
    }
    if (!started) return;
    if (host) host.style.removeProperty('display');
    start();
  }};
  enabled = window.__cmdManagerCreditsEnabled === true;
  (function wait() {
    const api = window.electronAPI;
    if (api && typeof api.accountOverview === 'function') return start();
    if (api) {
      if (++bootTries > 2) return start();
    } else if (++bootTries > 40) {
      return start();
    }
    setTimeout(wait, 200);
  })();
})();
