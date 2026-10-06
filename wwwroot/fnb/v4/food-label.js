/* 从小程序 print_food_label 复用 60×40mm TSPL 食材标签排版；仅二维码改为 v4 统一入口。 */
(function (root) {
  'use strict';
  const clean = text => String(text == null ? '' : text).replace(/["\r\n\x00-\x1f]/g, '');
  const short = (text, n) => clean(text).slice(0, n);
  function build(label, copies) {
    if (!label || !label.batchId || !label.name || !label.batchNo || !label.expireDate) throw new Error('缺少服务器标签数据');
    if (!Number.isInteger(copies) || copies < 1 || copies > 10) throw new Error('张数请填 1–10');
    const command = root.Tsc.jpPrinter.createNew();
    command.setCls(); command.setSize(60, 40); command.setGap(2); command.setCls();
    command.setText(16, 16, 'TSS32.BF2', 0, 1, 1, short(label.name, 5));
    command.setText(16, 120, '3', 0, 1, 1, short(label.batchNo, 11));
    command.setText(16, 200, '3', 0, 1, 1, short(label.expireDate, 10));
    command.setQrcode(218, 36, 'L', 6, 'M', 'https://mini.snowmeet.top/fnb/b?id=' + encodeURIComponent(String(label.batchId)));
    command.setPrint(copies); return command.getData();
  }
  root.FnbFoodLabel = { build: build };
  if (typeof module !== 'undefined') module.exports = root.FnbFoodLabel;
})(typeof window === 'undefined' ? globalThis : window);
