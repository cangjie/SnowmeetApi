const test = require('node:test');
const assert = require('node:assert/strict');
const apiModule = require('../../wwwroot/wecom/qisemi/api.js');
const response = (data, status=200) => ({ok:status===200,status,json:async()=>status===200?{code:0,data}:{code:4,message:'数据批次变化'}});
test('会话只放请求头，库存与业务分别固定批次，大整数身份保持字符串',async()=>{
  const calls=[];
  const api=apiModule.create({session:()=>({SessionKey:'reserved',SessionType:'wecom'}),fetch:async(url,init)=>{
    calls.push({url,init});const inventory=/Inventory|Styles\/|TopCategories/.test(url);
    return response({snapshot:{batchId:inventory?'9007199254740993':'2'},result:{items:[]}});
  }});
  api.selectSource('9007199254740994');await api.request('Inventory');await api.request('Styles/1');await api.request('TopCategories');await api.request('Documents');await api.request('Documents',{page:2});
  assert.equal(new URL(calls[1].url,'https://example.test').searchParams.get('batchId'),'9007199254740993');
  assert.equal(new URL(calls[2].url,'https://example.test').searchParams.get('batchId'),'9007199254740993');
  assert.equal(new URL(calls[4].url,'https://example.test').searchParams.get('batchId'),'2');
  assert.equal(calls[0].init.headers.SessionKey,'reserved');assert.equal(calls[0].init.headers.SessionType,'wecom');
  assert.ok(calls.every(c=>!c.url.includes('reserved')&&!c.url.includes('SessionKey')));
});
test('409 不自动切换批次；刷新显式解除原固定点',async()=>{
  let n=0;const urls=[];const api=apiModule.create({fetch:async url=>{urls.push(url);return ++n===2?response(null,409):response({snapshot:{batchId:'1'},result:{}});}});
  api.selectSource('1');await api.request('Inventory');await assert.rejects(api.request('Inventory'),e=>e.status===409);
  assert.equal(api.batches.inventory,'1');api.reset();await api.request('Inventory');assert.ok(!urls[2].includes('batchId'));
});
test('文件也携带会话头，拒绝外部链接及伪装的 JSON 文件',async()=>{
  let headers;const api=apiModule.create({session:()=>({SessionKey:'header-only'}),fetch:async(_,init)=>{headers=init.headers;return {ok:true,status:200,headers:{get:()=> 'application/json'},blob:async()=>null};}});
  await assert.rejects(api.file('https://example.test/file'),/地址无效/);
  await assert.rejects(api.file('/api/Qisemi/FileReferences/1/Content'),/未取得文件内容/);
  assert.equal(headers.SessionKey,'header-only');
});
test('中文字段和未知、零、负数分别呈现，原始字段不会注入 HTML',()=>{
  assert.equal(apiModule.quantity(null),'未知');assert.equal(apiModule.quantity(0),'0');assert.equal(apiModule.quantity(-1),'-1');
  assert.equal(apiModule.escape('<img onerror="x">'),'&lt;img onerror=&quot;x&quot;&gt;');
});
