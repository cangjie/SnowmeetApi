/* 原实验页的 GB18030 与 TSPL 库加载流程，库文件仍从原位置读取。 */
window.FnbPrinterReady = (async function () {
  const load = src => new Promise((resolve, reject) => {
    const script = document.createElement('script'); script.src = src;
    script.onload = resolve; script.onerror = () => reject(new Error('打印库加载失败')); document.head.appendChild(script);
  });
  const encoder = window.TextEncoder, decoder = window.TextDecoder;
  try {
    window.TextEncoder = undefined; window.TextDecoder = undefined;
    await load('/wecom/ble_print_test/encoding-indexes.js');
    await load('/wecom/ble_print_test/encoding.js');
    const encoding = { TextEncoder: window.TextEncoder, TextDecoder: window.TextDecoder };
    window.TextEncoder = encoder; window.TextDecoder = decoder;
    window.getApp = () => ({}); window.require = () => encoding; window.module = { exports: {} };
    await load('/wecom/ble_print_test/tsc.js'); window.Tsc = window.module.exports;
  } finally {
    window.TextEncoder = encoder; window.TextDecoder = decoder;
    delete window.getApp; delete window.require; delete window.module;
  }
})();
window.FnbPrinterReady.catch(function () { /* 在打印页展示加载错误，其余功能仍可使用。 */ });
