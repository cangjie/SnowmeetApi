const assert = require('node:assert/strict');
const path = require('node:path');

// 字段与 FnbOrder / FnbOrderLine 的真实 JSON 一致；只模拟接口，不写生产库存。
module.exports = async ({ page, go, calls }) => {
  const orders = [
    { order: { id: '9007199254741021', display_no: 'SO261016-A1', table_no: 'A1', remark: '少糖', order_status: 'pending' }, lines: [
      { item_name: '拿铁', spec_name: '200ml', quantity: 2, cancelled_qty: 0, remark: '热饮' },
      { item_name: '冰美式', spec_name: '标准杯', quantity: 1, cancelled_qty: 0 }
    ] },
    { order: { id: '9007199254741023', display_no: 'SO261016-A2', table_no: 'A2', remark: '', order_status: 'pending' }, lines: [
      { item_name: '拿铁', spec_name: '200ml', quantity: 1.2, cancelled_qty: 1.1 }
    ] }
  ];
  const ingredients = [
    { itemId: 23, name: '鲜牛奶', quantity: 400, baseUnit: 'ml', availableQuantity: 1960, upstreamQuantity: 0, shortageQuantity: 0, suggestedAction: 'ready' },
    { itemId: 24, name: '咖啡豆', quantity: 18, baseUnit: 'g', availableQuantity: 10, upstreamQuantity: 50, shortageQuantity: 8, suggestedAction: 'operate' },
    { itemId: 25, name: '糖浆', quantity: 20, baseUnit: 'ml', availableQuantity: 100, upstreamQuantity: 0, shortageQuantity: 0, suggestedAction: 'ready' }
  ];
  const sent = [];
  const routePattern = '**/api/FnbServe/*';
  await page.route(routePattern, async route => {
    const url = new URL(route.request().url()), action = url.pathname.split('/').pop();
    if (!['ListPendingOrders', 'PreviewServe', 'PostServe'].includes(action)) { await route.fallback(); return; }
    let body; try { body = route.request().postDataJSON(); } catch (_) {}
    calls.push({ path: url.pathname, params: Object.fromEntries(url.searchParams), body });
    let data;
    if (action === 'ListPendingOrders') data = orders.filter(r => r.order.order_status === 'pending');
    if (action === 'PreviewServe') {
      const order = orders.find(r => r.order.id === url.searchParams.get('orderId'));
      data = { order, ingredients: order === orders[0] ? ingredients : [{ ...ingredients[0], quantity: 20 }] };
    }
    if (action === 'PostServe') {
      sent.push(body);
      const order = orders.find(r => r.order.id === body.orderId); order.order.order_status = 'served';
      data = { documentId: '9007199254741025', cost: 9, order, lines: body.lines.map(l => ({ item_id: l.itemId, actual_qty: l.quantity, shortage_qty: 0 })) };
    }
    await route.fulfill({ json: { code: 0, data } });
  });
  const qty = id => page.locator('[name=serveQty][data-item="' + id + '"]');
  const open = id => page.locator('[data-action=op-order][data-id="' + id + '"]').click();
  try {
    await go(page, 'serve');
    assert.equal(await page.getByText('SO261016-A1', { exact: true }).count(), 1, '厨房单号不可为空');
    assert.equal(await page.getByText('拿铁 / 200ml × 2', { exact: true }).count(), 1, '菜品名称、规格、份数须完整');
    await open(orders[0].order.id);
    await page.getByRole('heading', { name: '厨房单详情', exact: true }).waitFor();
    assert.equal(await page.getByRole('heading', { name: '待出餐订单', exact: true }).count(), 0, '详情不能埋在待出餐列表下方');
    assert.equal(await qty(23).inputValue(), '400');
    assert.equal(await qty(24).inputValue(), '18');
    assert.equal(await qty(25).inputValue(), '20');
    assert.equal(await page.getByText('备注：少糖', { exact: true }).count(), 1);
    await qty(23).fill('350'); await qty(24).fill('22'); await qty(25).fill('0');
    assert.equal(await page.getByText('缺口 12 g · 建议先作业', { exact: true }).count(), 1);
    assert.equal(await page.getByText('本单不使用', { exact: true }).count(), 1);
    assert.equal(sent.length, 0, '编辑用量时不能扣库存');
    await page.getByRole('button', { name: '恢复配方用量', exact: true }).click();
    assert.deepEqual(await page.locator('[name=serveQty]').evaluateAll(inputs => inputs.map(x => x.value)), ['400', '18', '20']);
    await page.getByRole('button', { name: '‹ 返回待出餐订单', exact: true }).click();
    await open(orders[1].order.id);
    await qty(23).waitFor();
    assert.equal(await page.locator('[name=serveQty]').count(), 1);
    assert.equal(await qty(23).inputValue(), '20', '切换订单不能沿用前一单用料');
    assert.equal(await page.getByText('拿铁 / 200ml × 0.1', { exact: true }).count(), 1, '已取消份数须扣除，且不能显示浮点尾数');
    await go(page, 'serve?order=' + orders[0].order.id);
    for (const width of [320, 390]) {
      await page.setViewportSize({ width, height: 844 });
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, width + ' 像素不能横向溢出');
    }
    await qty(23).fill('-1');
    await page.getByRole('button', { name: '确认出餐并扣减', exact: true }).click();
    assert.equal(sent.length, 0, '负数用料不能提交');
    await qty(23).fill('350'); await qty(24).fill('22'); await qty(25).fill('0');
    if (process.env.FNB_H5_SCREENSHOTS) {
      await page.screenshot({ path: path.join(process.env.FNB_H5_SCREENSHOTS, 'serve-order-ingredients.png'), fullPage: true });
      await page.locator('#page').evaluate(el => { el.scrollTop = 0; });
      await page.screenshot({ path: path.join(process.env.FNB_H5_SCREENSHOTS, 'serve-order-detail.png'), fullPage: true });
    }
    await page.getByRole('button', { name: '确认出餐并扣减', exact: true }).click();
    await page.waitForFunction(() => location.hash === '#serve');
    await page.getByRole('heading', { name: '待出餐订单', exact: true }).waitFor();
    assert.equal(sent.length, 1);
    assert.equal(sent[0].orderId, orders[0].order.id);
    assert.deepEqual(sent[0].lines, [{ itemId: 23, quantity: 350 }, { itemId: 24, quantity: 22 }, { itemId: 25, quantity: 0 }]);
    assert.match(sent[0].requestId, /^[a-f0-9-]{36}$/);
    assert.equal(await page.getByText('SO261016-A1', { exact: true }).count(), 0);
    assert.equal(await page.getByText('SO261016-A2', { exact: true }).count(), 1);
  } finally { await page.unroute(routePattern); }
};
