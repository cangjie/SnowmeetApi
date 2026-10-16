const assert = require('node:assert/strict');

module.exports = async ({ page, go }) => {
  // 这里只验证 iOS 分支的事件和 viewport 策略；桌面浏览器不能验证真机软键盘或 WebKit 自动缩放。
  await page.addInitScript(() => Object.defineProperty(navigator, 'userAgent', { get: () => 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) wxwork' }));
  await go(page, 'inbound');
  assert.equal(await page.locator('[name=quantity]').getAttribute('inputmode'), 'decimal');
  assert.equal(await page.locator('[name=amount]').getAttribute('inputmode'), 'decimal');
  assert.equal(await page.locator('[name=shelfValue]').getAttribute('inputmode'), 'numeric');
  await page.locator('[name=quantity]').fill('2.5'); await page.locator('[name=amount]').fill('12.75');
  assert.equal(await page.locator('[name=quantity]').inputValue(), '2.5');
  const viewport = page.locator('meta[name=viewport]');
  await page.locator('[name=amount]').evaluate(el => el.blur());
  await page.waitForFunction(() => !document.querySelector('meta[name=viewport]').content.includes('maximum-scale'));
  const normal = await viewport.getAttribute('content');
  await page.locator('[name=quantity]').focus();
  assert.match(await viewport.getAttribute('content'), /maximum-scale=1(?:,|$)/);
  await page.locator('[name=amount]').focus();
  assert.match(await viewport.getAttribute('content'), /maximum-scale=1(?:,|$)/, '切换输入框期间保持缩放保护');
  await page.locator('[name=amount]').evaluate(el => el.blur());
  await page.waitForFunction(content => document.querySelector('meta[name=viewport]').content === content, normal);
  await go(page, 'batch?id=41');
  assert.equal(await page.locator('[name=copies]').getAttribute('inputmode'), 'numeric');
  await go(page, 'areas');
  await page.getByRole('button', { name: '+ 检查项', exact: true }).first().click();
  const minimum = page.locator('#modal [name=minimum]');
  assert.equal(await minimum.getAttribute('inputmode'), 'decimal');
  await minimum.fill('18');
  await page.getByRole('button', { name: '切换标准最小值的正负号', exact: true }).click();
  assert.equal(await minimum.inputValue(), '-18', '数字键盘不带负号时仍能填写温度等负数');
  await page.getByRole('button', { name: '切换标准最小值的正负号', exact: true }).click();
  assert.equal(await minimum.inputValue(), '18');
  const issues = await page.locator('input,select,textarea').evaluateAll(controls => controls
    .filter(el => !['hidden', 'file', 'checkbox', 'radio'].includes(el.type))
    .filter(el => parseFloat(getComputedStyle(el).fontSize) < 16)
    .map(el => el.name || el.tagName));
  assert.deepEqual(issues, [], '弹窗输入框实际字号不能小于 16px');
  await page.getByRole('button', { name: '关闭', exact: true }).click();
  await page.waitForFunction(() => !document.querySelector('meta[name=viewport]').content.includes('maximum-scale'));
};
