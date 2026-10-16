/* iOS 企业微信输入焦点保护：输入期间保持 1 倍，离开输入框后恢复原 viewport。 */
(function () {
  'use strict';
  const ios = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
  const viewport = document.querySelector('meta[name="viewport"]');
  if (!ios || !viewport) return;
  const normal = viewport.getAttribute('content');
  const focused = normal.split(',').map(x => x.trim()).filter(x => !/^(initial|minimum|maximum)-scale\s*=/.test(x))
    .concat('initial-scale=1', 'minimum-scale=1', 'maximum-scale=1').join(', ');
  let restoreTimer = 0;
  function entry(element) {
    return element && /^(INPUT|TEXTAREA|SELECT)$/.test(element.tagName) && !element.disabled
      && !/^(hidden|file|checkbox|radio|button|submit|reset|range|color|image)$/.test(element.type);
  }
  function lock() { clearTimeout(restoreTimer); viewport.setAttribute('content', focused); }
  function restore() { clearTimeout(restoreTimer); viewport.setAttribute('content', normal); }
  function afterInput() {
    clearTimeout(restoreTimer);
    restoreTimer = setTimeout(() => { if (!entry(document.activeElement)) restore(); }, 350);
  }
  function beforeFocus(event) {
    const target = event.target.closest('input,textarea,select') || event.target.closest('label')?.control;
    if (entry(target)) { lock(); afterInput(); }
  }
  // 在原生键盘决定缩放前处理触摸；focusin 同时覆盖程序自动聚焦和键盘切换。
  document.addEventListener('pointerdown', beforeFocus, true);
  document.addEventListener('touchstart', beforeFocus, { capture: true, passive: true });
  document.addEventListener('focusin', event => { if (entry(event.target)) lock(); }, true);
  document.addEventListener('focusout', afterInput, true);
  // 关闭弹窗或切换页面会直接移除输入框，部分 iOS WebView 不会补发 focusout。
  new MutationObserver(() => {
    if (viewport.getAttribute('content') === focused && !entry(document.activeElement)) afterInput();
  }).observe(document.documentElement, { childList: true, subtree: true });
  window.addEventListener('pagehide', restore);
  window.addEventListener('pageshow', () => { if (entry(document.activeElement)) lock(); else restore(); });
})();
