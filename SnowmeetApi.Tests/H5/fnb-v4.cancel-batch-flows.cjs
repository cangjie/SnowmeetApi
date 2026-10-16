const assert = require('node:assert/strict');

// 批次详情页撤销入库：服务器给出剩余秒数才显示，页面倒计时，到点收起；只撤销这一个批次。确认框由 operations-flows 统一接受。
module.exports = async ({ page, go, calls }) => {
  const cancelButton = () => page.getByRole('button', { name: '撤销入库', exact: true });
  await go(page, 'batch?id=41');
  assert.equal(await cancelButton().count(), 0);

  await go(page, 'batch?id=44');
  await page.getByText('入库 10 分钟内可撤销 · 还剩 0 分 02 秒').waitFor();
  await page.getByText('入库已超过 10 分钟，不能撤销').waitFor({ timeout: 5000 });
  assert.equal(await cancelButton().count(), 0);

  await go(page, 'batch?id=43');
  await page.getByText('入库 10 分钟内可撤销 · 还剩 2 分 05 秒').waitFor();
  await page.getByText(/还剩 2 分 0[34] 秒/).waitFor({ timeout: 3000 });
  const before = calls.filter(c => c.path.endsWith('/CancelReceiptBatch')).length;
  await cancelButton().click();
  await page.waitForFunction(() => location.hash === '#stock');
  await page.getByText('已撤销入库', { exact: true }).waitFor();
  const sent = calls.filter(c => c.path.endsWith('/CancelReceiptBatch'));
  assert.equal(sent.length, before + 1); assert.equal(sent.at(-1).body.batchId, 43);
};
