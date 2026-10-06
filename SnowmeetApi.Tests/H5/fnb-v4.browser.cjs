/* 本机静态服务 + 截获的模拟 API；不启动 SnowmeetApi，不读取连接配置，不访问生产。 */
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../../wwwroot');
const out = path.resolve(process.env.FNB_H5_SCREENSHOTS || path.join(__dirname, '../../../.local/fnb-v4-h5-qa'));
const clone = data => JSON.parse(JSON.stringify(data));
const base = {
  categories: [{ id: 1, level: 1, parent_id: null, name: '乳制品', valid: true, sort: 0 }, { id: 2, level: 2, parent_id: 1, name: '鲜奶', batch_code: 'MLK', measure_type: 'volume', default_storage: 'chilled', warn_days: 2, open_days: 3, is_prepared: false, valid: true, sort: 0 }],
  item: { id: 23, category_id: 2, name: '鲜牛奶', base_unit_code: 'ml', item_type: 'raw', warn_days: null, open_days: null, valid: true },
  forms: [{ id: 7, item_id: 23, seq: 0, name: '整箱', unit_name: '箱', per_base: 12000, storage_type: 'chilled', form_code: 'BOX', valid: true }, { id: 8, item_id: 23, seq: 1, name: '独立桶', unit_name: '桶', per_base: 2000, storage_type: 'chilled', form_code: 'TUB', in_op_name: '拆箱', in_op_ratio: 6, in_op_yield: 1, in_op_hours: 0, valid: true }, { id: 9, item_id: 23, seq: 2, name: '液体鲜奶', unit_name: 'ml', per_base: 1, storage_type: 'chilled', form_code: 'F', in_op_name: '开盖', in_op_ratio: 2000, in_op_yield: .98, in_op_hours: 0, valid: true }],
  specs: [{ id: 30, item_id: 23, name: '整箱', entry_form_id: 7, pack_desc: '6 桶 × 2 L', barcode: '6921234567890', valid: true, sort: 0 }, { id: 31, item_id: 23, name: '单桶', entry_form_id: 8, pack_desc: '2 L / 桶', barcode: '6921234567891', valid: true, sort: 1 }],
  units: [{ code: 'g', name: '克', dimension: 1 }, { code: 'ml', name: '毫升', dimension: 2 }, { code: 'piece', name: '个', dimension: 3 }],
  rules: [{ id: 42, category_id: 2, item_id: null, storage_type: 'chilled', season: 'all', days: 14, valid: true }]
};
function staticServer() {
  return http.createServer((req, res) => {
    const url = new URL(req.url, 'http://localhost');
    if (url.pathname === '/fnb/b') { res.writeHead(302, { Location: '/fnb/v4/index.html' + url.search }); res.end(); return; }
    let file; try { file = path.resolve(root, '.' + decodeURIComponent(url.pathname)); } catch (_) { res.writeHead(400).end(); return; }
    if (!file.startsWith(root + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) { res.writeHead(404).end(); return; }
    res.setHeader('Content-Type', ({ '.js': 'text/javascript; charset=utf-8', '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8' })[path.extname(file)] || 'application/octet-stream'); fs.createReadStream(file).pipe(res);
  });
}
async function main() {
  fs.mkdirSync(out, { recursive: true }); const server = staticServer(); await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const origin = 'http://127.0.0.1:' + server.address().port;
  const browser = await chromium.launch({ executablePath: process.env.FNB_H5_BROWSER || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true, args: ['--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'] });
  const errors = [], calls = [], unexpected = []; let data = clone(base), role = true, deny = false, delayWrites = 0;
  const operations = require('./fnb-v4.operations-fixture.cjs')(() => data);
  async function contextFor(isWecom = true) {
    const context = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1, userAgent: isWecom ? 'H5-QA wxwork' : 'H5-QA Desktop', permissions: ['camera'] });
    await context.route('**/*', async route => {
      const u = new URL(route.request().url());
      if (u.hostname === 'wwcdn.weixin.qq.com') { await route.fulfill({ contentType: 'text/javascript', body: `window.ww={register:function(){},ensureCorpConfigReady:async()=>{},ensureAgentConfigReady:async()=>{},onBluetoothDeviceFound:()=>{},onBLEConnectionStateChange:()=>{},openBluetoothAdapter:async()=>({}),startBluetoothDevicesDiscovery:async()=>({}),getBluetoothDevices:async()=>({devices:[{deviceId:'qa-printer',name:'Printer_QA',RSSI:-10}]}),stopBluetoothDevicesDiscovery:async()=>({}),createBLEConnection:async()=>({}),getBLEDeviceServices:async()=>({services:[{uuid:'qa-s'}]}),getBLEDeviceCharacteristics:async()=>({characteristics:[{uuid:'qa-c',properties:{write:true,notify:true}}]}),closeBLEConnection:async()=>({}),closeBluetoothAdapter:async()=>({}),scanQRCode:async()=>({resultStr:'CODE_128,6921234567891'})};` }); return; }
      if (u.origin !== origin) { unexpected.push(u.origin); await route.abort(); return; }
      if (!u.pathname.startsWith('/api/')) { await route.continue(); return; }
      let body; try { body = route.request().postDataJSON(); } catch (_) {}
      const action = u.pathname.split('/').pop(); calls.push({ path: u.pathname, params: Object.fromEntries(u.searchParams), body });
      let result;
      if (action === 'GetMe' || action === 'WeComLogin') result = deny ? { code: 3, message: '请管理员配置门店' } : { code: 0, data: { staffId: 7, name: '陈', shopId: 12, shopName: '测试门店', isManager: role, clientType: 'wecom', sessionKey: action === 'WeComLogin' ? 'new-session' : null } };
      else if (action === 'ListCategories') result = { code: 0, data: data.categories };
      else if (action === 'ListUnits') result = { code: 0, data: data.units };
      else if (action === 'ListItems') result = { code: 0, data: { total: 1 + (data.moreItems || []).length, rows: [data.item].concat(data.moreItems || []).map(item => ({ item, categoryName: '鲜奶', effectiveWarnDays: item.warn_days ?? 2, effectiveOpenDays: item.open_days ?? 3, defaultStorage: 'chilled', measureType: 'volume' })) } };
      else if (action === 'GetRoute') result = { code: 0, data: { item: data.item, defaults: { warnDays: 2, openDays: 3, storageType: 'chilled', measureType: 'volume' }, forms: data.forms, specs: data.specs, shelfRules: data.rules } };
      else if (action === 'ListShelfRules') result = { code: 0, data: data.rules };
      else if (action === 'SaveCategory') { assert.equal(body.shopId, 12); const row = data.categories.find(c => c.id === body.id); if (row) Object.assign(row, { name: body.name, warn_days: body.warnDays, open_days: body.openDays, batch_code: body.batchCode, measure_type: body.measureType, default_storage: body.defaultStorage, is_prepared: body.isPrepared, valid: body.valid }); else data.categories.push({ id: 55, name: body.name, level: body.level, parent_id: body.parentId, valid: body.valid }); result = { code: 0, data: row || data.categories.at(-1) }; }
      else if (action === 'SaveItemDefaults') { data.item.warn_days = body.warnDays; data.item.open_days = body.openDays; result = { code: 0, data: data.item }; }
      else if (action === 'CreateItem') result = { code: 0, data: { ...data.item, id: 24, name: body.name } };
      else if (action === 'SaveShelfRule') result = { code: 0, data: body };
      else if (action === 'SaveSpec') { const row = data.specs.find(s => s.id === body.id); if (row) Object.assign(row, { name: body.name, barcode: body.barcode, pack_desc: body.packDesc, brand: body.brand, entry_form_id: body.entryFormId, valid: body.valid }); result = { code: 0, data: row || body }; }
      else if (action === 'AddUpstreamForm' || action === 'UpdateForm') result = { code: 0, data: body };
      else if (action === 'FindSpecByBarcode') result = { code: 0, data: { item: data.item, spec: data.specs[1], entryForm: data.forms[1] } };
      else if (action === 'OcrScanName') result = { code: 0, data: { candidates: [{ text: '牛奶' }, { text: '鲜' }], dates: ['2026-10-01'], expireDates: ['2026-10-15'], shelfLives: [{ value: 14, unit: '天' }] } };
      else if (action === 'UploadPhoto') result = { code: 0, data: { id: 101, file_path_name: origin + '/qa-image.png' } };
      else if (action === 'GetJsSdkSignature') result = { code: 0, data: { config: {}, agentConfig: {} } };
      else if (action === 'GetAllPrinters') result = { code: 0, data: [{ name: 'Printer_QA' }] };
      else if (action === 'DownloadRecipeChain') { await route.fulfill({contentType:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',body:'offline-xlsx-fixture'}); return; }
      else { const value = operations(u.pathname,body,u.searchParams); if(value===undefined){unexpected.push(u.pathname);await route.fulfill({status:404});return;} result={code:0,data:value}; }
      if (body && delayWrites) await new Promise(resolve => setTimeout(resolve, delayWrites));
      await route.fulfill({ json: result });
    });
    const page = await context.newPage(); page.on('pageerror', e => errors.push(e.message)); return { context, page };
  }
  async function go(page, screen) { await page.goto(origin + '/fnb/v4/index.html?sessionKey=qa-session#' + screen); await page.locator('#page').getByText('正在加载…', { exact: true }).waitFor({ state: 'hidden' }); await page.waitForTimeout(80); }
  try {
    const { context, page } = await contextFor();
    await go(page, 'stock'); assert.equal(new URL(page.url()).searchParams.has('sessionKey'), false); await page.getByRole('button', { name: '更多', exact: true }).click(); await page.getByRole('button', { name: /食材分类/ }).click(); await page.getByRole('button', { name: '分类属性', exact: true }).click();
    await page.locator('[name=warnDays]').fill('0'); await page.locator('[name=openDays]').fill(''); await page.locator('[data-form=category] [type=submit]').click(); await page.locator('#modal').waitFor({ state: 'hidden' });
    const savedCategory = calls.findLast(c => c.path.endsWith('/SaveCategory')); assert.equal(savedCategory.body.warnDays, 0); assert.equal(savedCategory.body.openDays, null);
    await page.getByRole('button', { name: '保质期规则', exact: true }).click(); await page.getByRole('button', { name: '+ 新增规则', exact: true }).click(); await page.locator('[name=days]').fill('30'); await page.locator('[data-form=rule] [type=submit]').click(); await page.locator('#modal').waitFor({ state: 'hidden' }); assert.equal(calls.findLast(c => c.path.endsWith('/SaveShelfRule')).body.categoryId, 2);
    await go(page, 'routes?item=23'); await page.getByRole('button', { name: '+ 在上游加一个形态' }).click(); await page.locator('[name=name]').fill('托盘'); await page.locator('[name=unitName]').fill('托'); await page.locator('[name=perBase]').fill('60000'); await page.locator('[name=formCode]').fill('PALLET'); await page.locator('[name=opName]').fill('拆托'); await page.locator('[name=standardYield]').fill('98'); await page.locator('[data-form=form] [type=submit]').click(); await page.locator('#modal').waitFor({ state: 'hidden' }); assert.equal(calls.findLast(c => c.path.endsWith('/AddUpstreamForm')).body.standardYield, .98);
    await page.getByRole('button', { name: '编辑', exact: true }).last().click(); await page.locator('[name=name]').fill('单桶采购'); await page.locator('[name=barcode]').fill('6921234567891'); delayWrites = 250; await page.locator('[data-form=spec] [type=submit]').dblclick(); await page.locator('#modal').waitFor({ state: 'hidden' }); delayWrites = 0; assert.equal(calls.filter(c => c.path.endsWith('/SaveSpec')).length, 1);
    await page.getByRole('button', { name: '食材覆盖属性' }).click(); await page.locator('[name=warnDays]').fill('0'); await page.locator('[name=openDays]').fill(''); await page.locator('[data-form=defaults] [type=submit]').click(); await page.locator('#modal').waitFor({ state: 'hidden' }); assert.equal(calls.findLast(c => c.path.endsWith('/SaveItemDefaults')).body.openDays, null);
    await page.getByRole('button', { name: '+ 新建食材档案' }).click(); assert.equal(await page.locator('[name=baseUnitCode]').inputValue(), 'ml'); await page.locator('[name=name]').fill('新鲜牛奶'); await page.locator('[name=finalFormName]').fill('牛奶液体'); await page.locator('[data-item-photo]').setInputFiles({ name: 'qa.png', mimeType: 'image/png', buffer: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j4gkAAAAASUVORK5CYII=', 'base64') }); await page.waitForFunction(() => document.querySelector('[name=imageId]')?.value === '101'); await page.locator('[data-form=item] [type=submit]').click(); await page.locator('#modal').waitFor({ state: 'hidden' }); assert.equal(calls.findLast(c => c.path.endsWith('/CreateItem')).body.baseUnitCode, 'ml'); assert.equal(calls.findLast(c => c.path.endsWith('/CreateItem')).body.imageId, 101);
    await require('./fnb-v4.inbound-flows.cjs')({ page, go, data });
    await go(page, 'inbound'); await page.locator('[name=itemId]').selectOption('23'); await page.locator('[name=specId]').waitFor(); await page.getByRole('button', { name: '扫一扫', exact: true }).click(); await page.waitForFunction(() => document.querySelector('[name=specId]')?.value === '31'); await page.locator('[name=quantity]').fill('2.5');
    await page.getByRole('button', { name: '识别', exact: true }).first().click(); await page.locator('[data-date="2026-10-01"]').click({ timeout: 10000 }); assert.equal(await page.locator('[name=productionDate]').inputValue(), '2026-10-01'); assert.equal(await page.locator('[name=expireDate]').inputValue(), '');
    await page.locator('[name=areaId]').selectOption('2'); await page.locator('[data-form=op-inbound-draft] [type=submit]').click(); await page.locator('#inbound-drafts').getByText(/2.5 桶/).waitFor(); assert.equal(calls.some(c => /PostReceipt/.test(c.path)), false); assert.equal(calls.some(c=>/PreviewExpiry/.test(c.path)),true);
    await page.getByRole('button',{name:'确认入库',exact:true}).click(); await page.locator('#inbound-drafts').getByText('还没有待提交的入库条目').waitFor(); assert.equal(calls.findLast(c=>c.path.endsWith('/PostReceipt')).body.lines[0].quantity,2.5);
    await go(page, 'printer'); await page.getByRole('button', { name: '搜索并连接' }).click(); await page.getByText('已连接 Printer_QA', { exact: true }).waitFor({ timeout: 15000 });
    const labelBytes = await page.evaluate(() => FnbFoodLabel.build({ batchId: '9223372036854775806', name: '鲜牛奶', batchNo: '261006-MLK-01', expireDate: '2026-10-15' }, 2).length); assert.ok(labelBytes > 100);
    await page.getByRole('button', { name: '断开', exact: true }).click(); await page.getByText('尚未连接', { exact: true }).waitFor();
    const screens = ['stock', 'inbound', 'ops', 'job', 'check', 'serve', 'more', 'cats', 'routes?item=23', 'expiry', 'destroy', 'low', 'prep', 'recipe', 'count', 'dash', 'areas', 'supplies', 'tools', 'checkHist', 'printer'];
    for (const screen of screens) {
      await go(page, screen); assert.equal(await page.locator('#tabs button').count(), 6);
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, screen + ' 页面横向溢出');
      if (['stock', 'cats', 'routes?item=23', 'inbound', 'more'].includes(screen)) await page.screenshot({ path: path.join(out, screen.split('?')[0] + '.png') });
    }
    await page.setViewportSize({ width: 320, height: 640 }); await go(page, 'routes?item=23'); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false); await page.screenshot({ path: path.join(out, 'routes-320.png') });
    await go(page, 'batch?id=9223372036854775806'); await page.getByRole('heading', { name: '批次 9223372036854775806' }).waitFor();
    data.forms = [{ ...data.forms[2], seq: 0, in_op_name: null }]; data.specs = [];
    await go(page, 'inbound'); await page.locator('[name=itemId]').selectOption('23'); await page.locator('[name=packMode]').selectOption('sealed'); await page.locator('[name=packSize]').fill('1000'); await page.locator('[name=packLabel]').fill('件'); await page.locator('[name=areaId]').selectOption('2'); await page.locator('[name=quantity]').fill('2'); assert.equal(await page.locator('[name=quantity]').getAttribute('step'), '1'); await page.locator('[data-form=op-inbound-draft] [type=submit]').click(); await page.locator('#inbound-drafts').getByText(/2 件/).waitFor();
    await require('./fnb-v4.operations-flows.cjs')({page,go,calls,origin,data});
    await page.setViewportSize({width:390,height:844});
    for(const screen of ['stock','areas','check','supplies','tools','dash']) {await go(page,screen);await page.screenshot({path:path.join(out,'operations-'+screen+'.png'),fullPage:true});}
    await context.close();
    role = false; const staff = await contextFor(); await go(staff.page, 'routes'); assert.equal(await staff.page.getByRole('button', { name: '+ 新建食材档案' }).count(), 0); assert.equal(await staff.page.getByRole('button', { name: '+ 在上游加一个形态' }).count(), 0); await go(staff.page, 'cats'); await staff.page.getByRole('button', { name: '查看属性' }).first().click(); assert.equal(await staff.page.locator('[data-form=category] input:not(:disabled)').count(), 0);
    await go(staff.page,'areas');assert.equal(await staff.page.getByRole('button',{name:'+ 新增一级区域'}).count(),0);
    await go(staff.page,'ops');assert.equal(await staff.page.getByRole('button',{name:'提前完成'}).count(),0);
    await staff.context.close();
    deny = true; const denied = await contextFor(); await denied.page.goto(origin + '/fnb/v4/index.html?sessionKey=qa-session'); await denied.page.getByText('请管理员配置门店', { exact: true }).waitFor(); assert.equal(await denied.page.locator('#tabs').isVisible(), false); await denied.context.close();
    deny = false; const desktop = await contextFor(false); await desktop.page.goto(origin + '/fnb/v4/index.html'); await desktop.page.getByText('请在企业微信中打开本页面', { exact: true }).waitFor(); await desktop.context.close();
    assert.deepEqual(errors, []); assert.deepEqual(unexpected, []); console.log('PASS: 22 页面及运营流程；主数据、区域、检查、入库、作业、出餐、盘点、物资、工具、导出、角色、OCR、蓝牙与 320/390 布局；无生产访问。');
  } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
}
main().catch(e => { console.error(e.stack); process.exitCode = 1; });
