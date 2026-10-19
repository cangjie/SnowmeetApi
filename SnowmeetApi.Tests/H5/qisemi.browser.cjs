/* Local browser acceptance with synthetic API data. Never starts production Startup. */
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),http=require('node:http');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'../../wwwroot'),out=path.resolve(__dirname,'../../../.local/qisemi-h5-qa');
const catalog=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../../Services/Qisemi/QisemiModules.json'),'utf8'));
const image=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jM9sAAAAASUVORK5CYII=','base64');
const big='9007199254740993';
const warehouses=[{id:'1',name:'【旗舰】零售商品仓',coverageComplete:true},{id:'2',name:'测试负库存仓',coverageComplete:true},{id:'3',name:'未完整采集仓',coverageComplete:false}];
const style={id:big,displayName:'Fischer RC4 110 MV BOA VAC',brand:'Fischer',season:'24-25',styleCode:'U06424',sizeCount:'3',quantityMain:1};
const sizes=['25.5','26.5','27.5'].map((size,n)=>({id:String(11+n),sizeLabel:size,code:'TEST-'+size,unitName:'双',warehouses:warehouses.map((w,i)=>({warehouseId:w.id,warehouseName:w.name,quantityMain:i===2?null:n===1?(i===0?2:-1):0,quantityState:i===2?'unknown':n===1?'reported':'inferred_zero'}))}));
const doc={id:'4',documentId:'1',documentNo:'TEST-0001',documentType:'sale',businessAt:'2020-01-01T00:00:00+08:00',partnerName:'测试客户',amount:100,versionKind:'current',detailCoverageJson:{product_lines:{state:'complete',count:1}}};
const file={id:'9',fileId:'1',originalName:'main',mimeType:'image/png',extension:'.png',byteLength:'3',skuId:'12',contentPath:'/api/Qisemi/FileReferences/9/Content?sourceId=1&batchId=2'};
const page=items=>({items,total:String(items.length),page:1,pageSize:30});
function data(p,q){
  if(p==='Sources')return {items:[{id:'1',displayName:'测试账套',currentFullBatchId:'2',currentInventoryBatchId:'1'}]};
  if(p==='Modules')return catalog.map(m=>({...m,coverage:(m.sources||[]).filter(s=>!s.includes('*')).map(s=>({moduleKey:s,state:m.key==='other_inbound'?'empty':'complete',actualCount:m.key==='other_inbound'?'0':'1'}))}));
  if(p==='Warehouses')return warehouses;
  if(p==='Inventory')return page(q.get('keyword')==='不存在的商品'?[]:[{...style,quantityMain:q.get('warehouseId')==='2'?-1:1}]);
  if(p.startsWith('Styles/'))return {style,sizes,warehouses};
  if(p==='Documents')return page(q.get('module')==='other_inbound'?[]:[{...doc,id:'1',versionId:'4'}]);
  if(/^Documents\/\d+$/.test(p)||/^Versions\/\d+$/.test(p))return {document:{...doc,versionKind:p.startsWith('Versions')?'historical':'current'},raw:{record:{payloadJson:{中文自定义字段:'完整资料'}}},sections:['lines','settlements','writeoffs','relations','movements','versions','files','raw']};
  if(p.includes('/Sections/')){
    const section=p.split('/').pop();
    if(section==='files')return page([file]);
    if(section==='versions')return page([{...doc,id:'5',versionKind:'historical'}]);
    if(section==='lines')return page([{id:'20',productName:'Fischer 110 26.5',quantity:1,rawRecordId:'100',skuId:'12',bookQuantity:2,countedQuantity:1,differenceQuantity:-1}]);
    if(section==='raw')return page([{id:'100',rowOrdinal:'1',payloadJson:{customArray:[{文本:'完整补充字段'}]}}]);
    return page([]);
  }
  if(p==='Versions')return page([{...doc,id:'5',versionKind:'historical'}]);
  if(p==='Files')return page([file]);
  if(p==='ExportSheets')return page([{id:'7',sheetName:'总表',rowCount:'1',exportKey:'test',headersJson:['单号','商品']}]);
  if(p==='ExportSheets/7/Rows')return {sheet:{id:'7',sheetName:'总表',headersJson:['单号','商品']},rows:page([{id:'100',rowOrdinal:'1',payloadJson:{商品:'Fischer',单号:'TEST-0001'}}])};
  if(p==='Snapshot')return {batch:{id:'2',captureFinishedAt:'2026-10-18T05:27:45Z',verificationPassed:true},modules:[{moduleKey:'sales',state:'complete',actualCount:'1',coverageJson:{state:'complete'}}]};
  if(p==='DataIssues')return page([{id:'1',issueCode:'test',summary:'测试核对说明',state:'open',detailsJson:{info:'保留来源'}}]);
  if(p.startsWith('Archives/'))return p.split('/').length===3?{archive:{id:'12',name:'Fischer 110 26.5',code:'TEST-26.5'},raw:{record:{payloadJson:{Property:'完整商品字段'}}}}:page([{id:'12',name:'Fischer 110 26.5',code:'TEST-26.5'}]);
  if(/^Records\/\d+$/.test(p))return {record:{id:'100',moduleKey:'sales',rowOrdinal:'1',payloadJson:{text:'<img src=x onerror="window.pwned=1">'}},documents:[{documentId:'1',documentType:'sale'}]};
  if(p==='Records')return page([]);
  return page([{id:'20',documentId:'1',skuId:'12',rawRecordId:'100',documentNo:'TEST-0001',productName:'Fischer',quantityMain:-1,quantity:1,businessAt:'2020-01-01'}]);
}
async function main(){
  fs.mkdirSync(out,{recursive:true});
  const server=http.createServer((req,res)=>{
    const url=new URL(req.url,'http://localhost');let filePath;
    try{filePath=path.resolve(root,'.'+decodeURIComponent(url.pathname));}catch(_){res.writeHead(400).end();return;}
    if(!filePath.startsWith(root+path.sep)||!fs.existsSync(filePath)||!fs.statSync(filePath).isFile()){res.writeHead(404).end();return;}
    res.setHeader('Content-Type',({'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8'})[path.extname(filePath)]||'application/octet-stream');fs.createReadStream(filePath).pipe(res);
  });
  await new Promise(r=>server.listen(0,'127.0.0.1',r));const origin='http://127.0.0.1:'+server.address().port;
  const browser=await chromium.launch({executablePath:process.env.QISEMI_H5_BROWSER||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});
  const context=await browser.newContext({viewport:{width:390,height:844},userAgent:'Qisemi QA wxwork'}),errors=[],calls=[];let stale=false;
  const tab=await context.newPage();tab.on('pageerror',e=>errors.push(e.message));
  await context.route('**/api/Qisemi/**',async route=>{
    const u=new URL(route.request().url()),p=u.pathname.replace('/api/Qisemi/','');calls.push({p,q:Object.fromEntries(u.searchParams),headers:route.request().headers()});
    if(p==='FileReferences/9/Content'){await route.fulfill({status:200,contentType:'image/png',body:image});return;}
    if(p==='Inventory'&&u.searchParams.get('keyword')==='stale'&&!stale){stale=true;await route.fulfill({status:409,json:{code:4,message:'数据批次已变化，请刷新后重新查询'}});return;}
    if(p==='Inventory'&&u.searchParams.get('keyword')==='slow')await new Promise(r=>setTimeout(r,350));
    const result=data(p,u.searchParams),inventory=/^(Inventory|Styles\/|Warehouses$|StockTotals$|WarehouseStocks$)/.test(p);
    await route.fulfill({status:200,json:{code:0,data:p==='Sources'?result:{snapshot:{batchId:inventory?'1':'2',batch:{captureFinishedAt:'2026-10-18T05:27:45Z'}},result}}});
  });
  try{
    await tab.goto(origin+'/wecom/qisemi/index.html');await tab.locator('[data-action="expand"]').first().waitFor();
    await tab.locator('#inventory-keyword').fill('Fischer 110 26.5');await tab.locator('#inventory-search').evaluate(f=>f.requestSubmit());
    await tab.locator('[data-action="expand"]').first().click();await tab.locator('.qs-size').first().waitFor();assert.equal(await tab.locator('.qs-size').count(),3);
    assert.equal(await tab.locator('.qs-size[aria-pressed="true"] b').innerText(),'26.5');assert.match(await tab.locator('.qs-detail').innerText(),/负库存|未知/);
    await tab.locator('[data-action="style-view"][data-view="matrix"]').click();await tab.locator('table.qs-table').waitFor();assert.equal(await tab.locator('table.qs-table thead th').count(),4);
    for(const width of [320,390]){await tab.setViewportSize({width,height:844});assert.ok(await tab.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'matrix horizontal overflow '+width);}
    await tab.screenshot({path:path.join(out,'inventory-390.png'),fullPage:true});
    await tab.locator('[data-sku="13"][data-action="size"]').click();assert.match(await tab.locator('.qs-detail').innerText(),/未取得完整数量/);
    await tab.locator('[data-zero]').check();assert.equal(await tab.locator('.qs-stock-row').count(),3);
    await tab.locator('[data-action="sku-flow"]').click();await tab.locator('[data-action="linked-document"]').first().waitFor();
    await tab.locator('[data-action="linked-document"]').first().click();await tab.locator('[data-action="section"][data-section="lines"]').waitFor();
    await tab.locator('[data-action="section"][data-section="lines"]').click();await tab.getByText('Fischer 110 26.5',{exact:false}).first().waitFor();
    await tab.locator('[data-action="section"][data-section="files"]').click();await tab.locator('[data-action="file"]').first().click();await tab.locator('#file-preview[open]').waitFor();await tab.locator('#preview-close').click();
    for(const name of ['business','archives','more']){
      await tab.locator('[data-tab="'+name+'"]').click();await tab.locator('[data-action="module"]').first().waitFor();
      for(const m of catalog.filter(m=>m.tab===name)){
        await tab.locator('[data-action="module"][data-module="'+m.key+'"]').click();await tab.waitForFunction(()=>!document.getElementById('screen').hasAttribute('aria-busy'));
        assert.equal(await tab.locator('#notice:not([hidden])').count(),0,m.key);
        await tab.locator('[data-action="catalog"]').click();await tab.locator('[data-action="module"]').first().waitFor();
      }
    }
    await tab.locator('[data-tab="business"]').click();await tab.locator('[data-action="module"][data-module="sales"]').click();await tab.locator('#browser-state').selectOption('red');await tab.locator('#browser-search').evaluate(f=>f.requestSubmit());await tab.waitForFunction(()=>!document.getElementById('screen').hasAttribute('aria-busy'));assert.ok(calls.some(c=>c.p==='Documents'&&c.q.writeBack==='true'));
    await tab.goto(origin+'/wecom/qisemi/index.html#more?module=stock_movements');await tab.locator('[data-action="linked-raw"]').first().click();await tab.locator('details.qs-raw').waitFor();assert.equal(await tab.evaluate(()=>window.pwned),undefined);
    await tab.locator('[data-tab="inventory"]').click();await tab.locator('#inventory-keyword').waitFor();await tab.locator('#inventory-keyword').fill('不存在的商品');await tab.locator('#inventory-search').evaluate(f=>f.requestSubmit());await tab.getByText('没有匹配的商品',{exact:false}).waitFor();
    await tab.locator('#inventory-keyword').fill('stale');await tab.locator('#inventory-search').evaluate(f=>f.requestSubmit());await tab.locator('[data-action="refresh"]').waitFor();await tab.locator('[data-action="refresh"]').click();await tab.locator('[data-action="expand"]').first().waitFor();
    await tab.locator('#inventory-keyword').fill('slow');await tab.locator('#inventory-search').evaluate(f=>f.requestSubmit());await tab.waitForFunction(()=>location.hash.includes('slow'));await tab.locator('[data-tab="business"]').click();await tab.locator('[data-action="module"]').first().waitFor();await tab.waitForTimeout(450);assert.equal(await tab.locator('[data-action="module"]').count(),catalog.filter(m=>m.tab==='business').length);
    await tab.locator('[data-tab="inventory"]').click();await tab.locator('[data-action="expand"]').first().waitFor();
    for(const width of [320,390,768,1280]){await tab.setViewportSize({width,height:844});assert.ok(await tab.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'horizontal overflow '+width);}
    await tab.setViewportSize({width:390,height:844});await tab.screenshot({path:path.join(out,'inventory-final.png'),fullPage:true});
    assert.ok(calls.some(c=>c.p==='Styles/'+big&&c.q.batchId==='1'));assert.ok(calls.some(c=>c.p==='Documents'&&c.q.batchId==='2'));
    assert.deepEqual(errors,[]);console.log(JSON.stringify({passed:true,modules:catalog.length,viewports:[320,390,768,1280],apiCalls:calls.length,screenshots:out}));
  }finally{await browser.close();await new Promise(r=>server.close(r));}
}
main().catch(e=>{console.error(e);process.exitCode=1;});
