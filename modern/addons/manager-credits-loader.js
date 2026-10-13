(() => {
  'use strict';
  if (window.__cmdManagerCreditsLoader) return;
  window.__cmdManagerCreditsLoader = true;
  const base = new URL('.', document.currentScript.src);
  let loading = false;
  let panelLoading = false;
  let lastTarget = null;
  function apply() {
    const enabled = window.__cmdManagerCreditsEnabled === true;
    const controller = window.__cmdManagerCreditsController;
    if (controller) { const target = window.__cmdManagerCreditsUser || ''; if (target !== lastTarget) { controller.setEnabled(false); lastTarget = target; } controller.setEnabled(enabled); return; }
    if (!enabled || panelLoading) return;
    panelLoading = true;
    const script = document.createElement('script');
    script.src = new URL('manager-credits-panel.js', base).href;
    script.onload = () => { panelLoading = false; if (window.__cmdManagerCreditsController) window.__cmdManagerCreditsController.setEnabled(window.__cmdManagerCreditsEnabled === true); };
    script.onerror = () => { panelLoading = false; };
    document.head.appendChild(script);
  }
  function refresh() {
    if (loading) return;
    loading = true;
    const script = document.createElement('script');
    script.src = new URL('manager-credits-state.js?v=' + Date.now(), base).href;
    script.onload = () => { loading = false; script.remove(); apply(); };
    script.onerror = () => { loading = false; script.remove(); };
    document.head.appendChild(script);
  }
  refresh();
  setInterval(() => { if (document.visibilityState === 'visible') refresh(); }, 5000);
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') refresh(); });
  window.addEventListener('focus', refresh);
})();
