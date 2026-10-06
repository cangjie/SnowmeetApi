const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const client = require('../../wwwroot/fnb/v4/api.js');
function fixture(responses, extra) {
  const calls = [], store = new Map([[client.sessionKey, 'previous-wecom-session']]);
  const api = client.create(Object.assign({ storage: { getItem: k => store.get(k), setItem: (k, v) => store.set(k, v), removeItem: k => store.delete(k) }, fetch: async (url, init) => {
    calls.push({ url, init }); const result = responses.shift(); if (result instanceof Error) throw result;
    return { ok: true, status: 200, json: async () => result };
  } }, extra));
  return { api, calls, store };
}
const identity = { code: 0, data: { staffId: 7, shopId: 12, shopName: '测试门店', name: '陈', isManager: true } };
test('未确认的库存提交持久保存原 ID，刷新后同内容重试；改内容拒绝', async () => {
  const f = fixture([identity, new Error('connection lost'), { code: 0, data: { documentId: '9007199254740993' } }]);
  await f.api.authenticate({ search:'',pathname:'/',hash:'' }, { replaceState() {} });
  const storage = { getItem:k=>f.store.get(k),setItem:(k,v)=>f.store.set(k,v) };
  const body = { lines:[{itemId:1,quantity:2}],remark:'测试单' };
  await assert.rejects(client.createWrites(f.api,storage).post('FnbInbound','PostReceipt',body));
  const original = JSON.parse(f.calls[1].init.body).requestId;
  const resumed = client.createWrites(f.api,storage);
  await assert.rejects(resumed.post('FnbInbound','PostReceipt',{...body,remark:'改过的单'}),/先重试原单/);
  const result = await resumed.retry('FnbInbound/PostReceipt');
  assert.equal(JSON.parse(f.calls[2].init.body).requestId,original); assert.equal(result.documentId,'9007199254740993');
  assert.equal(resumed.pending().length,0);
});
test('明确业务拒绝允许改表单生成新 ID，原内容重试仍沿用 ID', async () => {
  const f = fixture([identity,{code:4,message:'库存冲突'},{code:1,message:'数量错误'},{code:0,data:{}}]);
  await f.api.authenticate({search:'',pathname:'/',hash:''},{replaceState(){}});
  const w = client.createWrites(f.api,{getItem:k=>f.store.get(k),setItem:(k,v)=>f.store.set(k,v)});
  await assert.rejects(w.post('FnbStock','PostWaste',{batchId:1,quantity:2}));
  const id = JSON.parse(f.calls[1].init.body).requestId;
  await assert.rejects(w.post('FnbStock','PostWaste',{batchId:1,quantity:2}));
  assert.equal(JSON.parse(f.calls[2].init.body).requestId,id);
  await w.post('FnbStock','PostWaste',{batchId:1,quantity:1}); assert.notEqual(JSON.parse(f.calls[3].init.body).requestId,id);
});
test('报表下载共用会话和门店，JSON 业务失败不会伪装成 Excel', async () => {
  const f = fixture([identity]); await f.api.authenticate({search:'',pathname:'/',hash:''},{replaceState(){}});
  let url;
  const download = client.create({ storage:{getItem:()=> 'qa-session',removeItem(){}}, fetch:async u=>{url=u;return {ok:true,status:200,headers:{get:()=> 'application/json'},json:async()=>({code:2,message:'会话失效'})};} });
  // 验证通用 request 的 JSON 下载分支，身份接口无需门店。
  await assert.rejects(download.request('FnbAuth','GetMe',{shop:false,download:true}),e=>e.code===2);
  assert.match(url,/sessionKey=qa-session/);
});
const location = { origin: 'https://mini.snowmeet.top', pathname: '/fnb/v4/index.html', search: '', hash: '#routes?item=23' };
test('百分比呈现与输入不引入二进制尾数或超过接口的小数精度', () => {
  assert.equal(client.percentInput(.961234), '96.1234');
  assert.equal(client.percentFraction('96.1234'), .961234);
  assert.equal(client.percentFraction('0.0001'), .000001);
});
test('复用员工会话，由 GetMe 确认门店；GET query 与 POST body 按契约发送', async () => {
  const f = fixture([identity, { code: 0, data: [] }, { code: 0, data: { id: 9 } }]);
  await f.api.authenticate(location, { replaceState() {} });
  await f.api.request('FnbCatalog', 'ListCategories');
  await f.api.request('FnbCatalog', 'SaveCategory', { body: { shopId: 999, level: 1, name: '奶制品', valid: true } });
  assert.equal(new URL(f.calls[0].url, location.origin).searchParams.has('shopId'), false);
  assert.equal(new URL(f.calls[1].url, location.origin).searchParams.get('shopId'), '12');
  assert.equal(JSON.parse(f.calls[2].init.body).shopId, 12);
  assert.equal(new URL(f.calls[2].url, location.origin).searchParams.has('shopId'), false);
});
test('OAuth code 只发送新认证接口，清理 URL，保留 id 与页面锚点', async () => {
  const f = fixture([{ code: 0, data: { sessionKey: 'new-session' } }, identity]); let clean;
  await f.api.authenticate({ ...location, search: '?code=oauth-code&state=x&id=9223372036854775806' }, { replaceState: (a, b, url) => { clean = url; } });
  assert.equal(clean, '/fnb/v4/index.html?id=9223372036854775806#routes?item=23');
  assert.match(f.calls[0].url, /FnbAuth\/WeComLogin/);
  assert.deepEqual(JSON.parse(f.calls[0].init.body), { code: 'oauth-code' });
  assert.equal(f.calls[0].url.includes('sessionKey'), false);
  assert.match(f.calls[1].url, /sessionKey=new-session/);
});
test('权限拒绝不清会话或自动重新授权；code 4 不重复写请求', async () => {
  let expired = 0; const f = fixture([identity, { code: 3, message: '无权限' }, { code: 4, message: '请重试' }], { onExpired: () => expired++ });
  await f.api.authenticate(location, { replaceState() {} });
  await assert.rejects(f.api.request('FnbRoute', 'CreateItem', { body: { name: '牛奶' } }), e => e.code === 3);
  await assert.rejects(f.api.request('FnbCatalog', 'SaveCategory', { body: { name: '肉类' } }), e => e.code === 4);
  assert.equal(expired, 0); assert.equal(f.calls.length, 3); assert.equal(f.store.get(client.sessionKey), 'previous-wecom-session');
});
test('会话失效清理凭证，连续失败只触发一次重新登录', async () => {
  let expired = 0; const f = fixture([identity, { code: 2 }, { code: 2 }], { onExpired: () => expired++ });
  await f.api.authenticate(location, { replaceState() {} });
  await assert.rejects(f.api.request('FnbCatalog', 'ListCategories'));
  await assert.rejects(f.api.request('FnbAuth', 'GetMe', { shop: false }));
  assert.equal(f.store.has(client.sessionKey), false); assert.equal(expired, 1);
});
test('超过 100 种食材加载全部分页', async () => {
  const f = fixture([identity, { code: 0, data: { total: 103, rows: Array.from({ length: 100 }, (_, id) => ({ item: { id } })) } }, { code: 0, data: { total: 103, rows: [100, 101, 102].map(id => ({ item: { id } })) } }]);
  await f.api.authenticate(location, { replaceState() {} }); const rows = await f.api.listItems();
  assert.equal(rows.length, 103); assert.match(f.calls[2].url, /page=2/);
});
test('文本转义与 long ID 保持精度', async () => {
  assert.equal(client.escape('<img src=x onerror="attack()">'), '&lt;img src=x onerror=&quot;attack()&quot;&gt;');
  const f = fixture([identity, { code: 0, data: { id: '9223372036854775806' } }]); await f.api.authenticate(location, { replaceState() {} });
  const data = await f.api.request('Future', 'GetBatch', { params: { id: '9223372036854775806' } });
  assert.equal(data.id, '9223372036854775806'); assert.match(f.calls[1].url, /9223372036854775806/);
});
test('食材标签沿用 TSPL 参数，使用统一 v4 二维码，不推算到期', () => {
  const calls = []; const sandbox = { Tsc: { jpPrinter: { createNew: () => new Proxy({}, { get: (o, name) => (...args) => { calls.push([name, ...args]); return name === 'getData' ? [1, 2, 3] : undefined; } }) } } };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../wwwroot/fnb/v4/food-label.js'), 'utf8'), sandbox);
  sandbox.FnbFoodLabel.build({ batchId: '9223372036854775806', name: '鲜牛奶', batchNo: '261006-MLK-01', expireDate: '2026-10-09' }, 2);
  assert.deepEqual(calls.find(x => x[0] === 'setSize'), ['setSize', 60, 40]);
  assert.equal(calls.find(x => x[0] === 'setQrcode')[6], 'https://mini.snowmeet.top/fnb/b?id=9223372036854775806');
  assert.equal(calls.find(x => x[0] === 'setPrint')[1], 2);
  assert.throws(() => sandbox.FnbFoodLabel.build({}, 1), /缺少服务器标签数据/);
});
test('现有蓝牙模块保留连接、顺序分包和掉线重连，扩展给 v4 使用', async () => {
  let lost, writes = []; const sandbox = { location: { href: 'https://mini.snowmeet.top/fnb/v4/index.html' }, setTimeout: fn => { fn(); }, Uint8Array, fetch: async url => ({ json: async () => url.includes('Signature') ? { code: 0, data: { config: {}, agentConfig: {} } } : { data: [{ name: 'Printer_Test' }] } }), ww: {
    register() {}, ensureCorpConfigReady: async () => {}, ensureAgentConfigReady: async () => {}, onBluetoothDeviceFound() {}, onBLEConnectionStateChange: fn => { lost = fn; }, openBluetoothAdapter: async () => ({}), startBluetoothDevicesDiscovery: async () => ({}), getBluetoothDevices: async () => ({ devices: [{ deviceId: 'p1', name: 'Printer_Test', RSSI: -10 }] }), stopBluetoothDevicesDiscovery: async () => ({}), createBLEConnection: async () => ({}), getBLEDeviceServices: async () => ({ services: [{ uuid: 's1' }] }), getBLEDeviceCharacteristics: async () => ({ characteristics: [{ uuid: 'c1', properties: { write: true, notify: true } }] }), writeBLECharacteristicValue: async p => { writes.push([...new Uint8Array(p.value)]); }, closeBLEConnection: async () => ({}), closeBluetoothAdapter: async () => ({})
  } };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../wwwroot/wecom/ble_print_test/ble_print.js'), 'utf8'), sandbox);
  await sandbox.BlePrint.register(); await sandbox.BlePrint.connect(() => {});
  await sandbox.BlePrint.print(() => [1, 2, 3, 4, 5], { chunkSize: 2, intervalMs: 0 }, () => {});
  assert.deepEqual(writes, [[1, 2], [3, 4], [5]]); assert.equal(sandbox.BlePrint.getConnection().deviceId, 'p1');
  lost({ deviceId: 'p1', connected: false }); assert.equal(sandbox.BlePrint.getConnection(), null);
  await sandbox.BlePrint.connect(() => {}); assert.equal(sandbox.BlePrint.getConnection().deviceId, 'p1'); await sandbox.BlePrint.release();
});
