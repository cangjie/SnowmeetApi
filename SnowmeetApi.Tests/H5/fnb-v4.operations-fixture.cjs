// 离线 UI 契约夹具；业务算法由现有 SQL Server 集成测试验证。
const assert = require('node:assert/strict');
module.exports = function createFixture(getData) {
  let next = 100, order = null, sheet = null, snapshot = null;
  const areas = [1,2].map(id => ({ area:{id,parent_id:id===1?null:1,name:id===1?'后厨':'冷藏柜',area_type:'cold',valid:true},effectiveValid:true,photos:[],checks:[],bindings:{food:0,supplies:0,tools:0,checks:0} }));
  const supplies = [],tools = [], supplyLog = [],toolLog=[];
  const batch = { id:41,item_id:23,form_id:8,state:'staged',batch_no:'261006-MLK-01',quantity:5000,amount:80,storage_type:'chilled',expire_date:'2026-10-15T00:00:00',effective_expire:'2026-10-12T00:00:00',ready_at:null };
  const bd = () => ({batch,itemName:'鲜牛奶',formName:'独立桶',displayQuantity:2.5,unitName:'桶',baseUnit:'ml',available:false,remainingDays:6,areaId:2,photos:[]});
  const label = {batchId:'41',name:'鲜牛奶',batchNo:batch.batch_no,expireDate:'2026-10-12'};
  const stock = () => [{itemId:23,name:'鲜牛奶',categoryId:2,baseUnit:'ml',availableQuantity:1000,stagedQuantity:5000,sealedQuantity:0,totalQuantity:6000,amount:100,lowThreshold:1500,lowStock:true,batchCount:1,servings:5,layers:[{formId:8,formName:'独立桶',quantity:5000,displayQuantity:2.5,availableQuantity:0,batches:[batch]}]}];
  const dish = {productId:77,name:'拿铁',specs:[{spec:{id:88,name:'标准份'},recipe:{id:'90',status:'published'},lines:[{item_id:23,quantity:200}],servings:5}]};
  const dishes = [dish],preps = [];
  function checkData() { return {sheet,stale:false,total:sheet?areas.flatMap(a=>a.checks).filter(x=>x.valid).length:0,filled:0,abnormal:0,requiredRemaining:0,lines:sheet?areas.flatMap(a=>a.checks).filter(x=>x.valid).map(x=>({item:x,line:{id:String(x.id),item_id:x.id,active:true,result:x.result||null,value:x.value,reason:x.reason,upload_id:x.upload_id},handlings:x.handlings||[]})):[]}; }
  const journal = new Set(['PostReceipt','DeleteReceipt','PostWaste','PostDestroy','PostOperation','CompleteOperation','PostPreparation','CreateOrder','PostServe','CreateSnapshot','SaveCount','PostStocktake','PostMovement','UndoReceipt','ChangeStatus','StartToday','RefreshSnapshot','SaveDraft','PassAll','Submit','HandleAbnormal','Confirm']);
  return function respond(pathname,body,params) {
    const action=pathname.split('/').pop(),controller=pathname.split('/')[2];
    if (body) { assert.equal(body.shopId,12); if(journal.has(action)) assert.match(body.requestId,/^[a-f0-9-]{36}$/); }
    if(action==='GetHome') return {businessDate:'2026-10-06',lowStockCount:1,nearExpiryCount:1,expiredCount:0,stockItemCount:1,batchCount:1,openedBatchCount:0,ongoingOperationCount:1,recommendedOperationCount:1,checkStatus:sheet?sheet.status:'none'};
    if(action==='ListStock') return stock();
    if(action==='ListAreas') return areas;
    if(action==='SaveArea') { let a=areas.find(x=>x.area.id===body.id); if(!a){a={area:{id:next++},photos:[],checks:[],bindings:{food:0,supplies:0,tools:0,checks:0}};areas.push(a);} Object.assign(a.area,{name:body.name,parent_id:body.parentId,area_type:body.areaType,valid:body.valid});a.effectiveValid=body.valid;return a.area; }
    if(action==='AddPhoto'){areas.find(a=>a.area.id===body.areaId).photos.push({upload_id:body.uploadId,file_path_name:'/qa-image.png',created_at:'2026-10-06T09:00:00'});return {};}
    if(action==='RemovePhoto'){const a=areas.find(x=>x.area.id===body.areaId);a.photos=a.photos.filter(p=>p.upload_id!==body.uploadId);return {};}
    if(action==='DeleteArea') {const i=areas.findIndex(x=>x.area.id===body.id);areas.splice(i,1);return {};}
    if(action==='NextBatchNo')return {batchNo:batch.batch_no,reserved:false};
    if(action==='PreviewExpiry')return {expireDate:'2026-10-15T00:00:00',expirySource:'rule',expired:false};
    if(action==='PostReceipt'){assert.ok(body.lines.length);assert.ok(body.lines.every(l=>l.areaId&&typeof l.quantity==='number'&&typeof l.amount==='number'));return {documentId:'9007199254740993',batches:[bd()]};}
    if(action==='DeleteReceipt')return {documentId:body.documentId,status:'cancelled'};
    if(action==='GetBatch')return {...bd(),batch:{...batch,id:params.get('batchId')}};
    if(action==='GetLabelData')return {...label,batchId:params.get('batchId')};
    if(action==='GetWorkbench')return {ongoing:[{id:'9007199254740995',op_name:'解冻',ready_at:'2026-10-07T09:00:00',output_batch_id:42}],recommended:[{stock:stock()[0],batches:[batch]}],canOperate:[batch],todayRecords:[]};
    if(action==='PreviewOperation')return {inputQuantity:1,inputUnit:'桶',expectedQuantity:1960,outputUnit:'ml',standardYield:.98,durationHours:0,effectiveExpire:'2026-10-12',batchNo:'261006-MLK-01-F'};
    if(action==='PostOperation')return {operation:{id:'99'},actualYield:.96,lowYield:true,batch:bd()};
    if(action==='CompleteOperation')return {id:body.operationId};
    if(action==='ListExpiry')return [{kind:'near',data:bd()}];
    if(action==='ListDestroy')return {pending:[{data:bd()}],records:[]};
    if(action==='PostWaste'||action==='PostDestroy')return {documentId:'100',batch:bd()};
    if(action==='ListLowStock')return {food:stock(),supplies:[]};
    if(action==='SaveLowStockRule')return {};
    if(action==='ListPreps')return preps;
    if(action==='CreatePrep'){const item={id:next++,category_id:body.categoryId,name:body.name,item_type:'prepared',base_unit_code:body.baseUnitCode,valid:true}; const r={item,recipe:{id:String(next++),status:body.lines.length?'published':'draft',output_qty:body.outputQuantity,version_no:1},lines:body.lines.map(l=>({item_id:l.itemId,quantity:l.quantity}))};r.lines.forEach(l=>l.recipe_id=r.recipe.id);preps.push(r);getData().moreItems=(getData().moreItems||[]).concat(item);return r;}
    if(action==='SavePrepBom'){const p=preps.find(p=>p.item.id===body.ownerId);p.recipe.status='published';p.recipe.output_qty=body.outputQuantity;p.lines=body.lines.map(l=>({...l,item_id:l.itemId,recipe_id:p.recipe.id}));return p;}
    if(action==='PreviewPreparation')return {outputQuantity:12,ingredients:[{name:'鲜牛奶',requiredQuantity:200,availableQuantity:1000,shortageQuantity:0}]};
    if(action==='PostPreparation')return {documentId:'101',batch:bd()};
    if(action==='ListPrepRecords'||action==='ListServeLog')return [];
    if(action==='ListDishes')return dishes;
    if(action==='CreateDish'){const d={productId:next++,name:body.name,specs:[{spec:{id:next++,name:body.specName},recipe:null,lines:[],servings:0}]};dishes.push(d);return d;}
    if(action==='AddSpec'){const spec={id:next++,name:body.name};dishes.find(d=>d.productId===body.productId).specs.push({spec,recipe:null,lines:[],servings:0});return spec;}
    if(action==='DeleteSpec')return {};
    if(action==='SaveSpecLines'){const d=dishes.flatMap(d=>d.specs).find(r=>r.spec.id===body.ownerId);d.recipe={id:'99',status:'published'};d.lines=body.lines.map(l=>({item_id:l.itemId,quantity:l.quantity}));return d;}
    if(action==='CreateOrder'){order={order:{id:'9007199254740997',order_no:'K-01',table_no:body.tableNo,remark:body.remark,status:'pending'},lines:body.lines.map(l=>({dish_name:'拿铁',spec_name:'标准份',quantity:l.quantity}))};return order;}
    if(action==='ListPendingOrders')return order&&order.order.status==='pending'?[order]:[];
    if(action==='PreviewServe')return {order,ingredients:[{itemId:23,name:'鲜牛奶',quantity:400,baseUnit:'ml',availableQuantity:1000,shortageQuantity:0,suggestedAction:'ready'}]};
    if(action==='PostServe'){assert.equal(typeof body.orderId,'string');order.order.status='served';return {documentId:'123',cost:8,order};}
    if(action==='CreateSnapshot'){snapshot={document:{id:'9007199254740999',document_no:'C-01',status:'draft'},lines:[{item_id:23,system_qty:1000,counted_qty:null,difference_qty:null,remark:null}]};return snapshot;}
    if(action==='GetSnapshot')return snapshot;
    if(action==='SaveCount'){assert.equal(typeof body.documentId,'string');snapshot.lines[0].counted_qty=body.lines[0].quantity;return snapshot;}
    if(action==='PostStocktake'){snapshot.document.status='posted';snapshot.lines[0].difference_qty=-20;return snapshot;}
    if(action==='GetDashboard')return {from:'2026-10-05',to:'2026-10-06',lossAmount:2,inventoryCost:100,lossRate:.02,averageTurnoverDays:3,costStructure:[{categoryName:'鲜奶',amount:100}],lossLedger:[],stocktakeGains:[]};
    if(action==='GetRecipeChain')return [{dish:'拿铁',spec:'标准份',ingredient:'鲜牛奶',recipeQuantity:200,baseUnit:'ml',purchaseQuantity:.1,purchaseUnit:'桶',steps:[{operation:'开盖',form:'液体',unit:'ml',storage:'chilled',standardYield:.98,hours:0}]}];
    if(action==='ListSupplies')return supplies;
    if(action==='SaveSupply'){let s=supplies.find(x=>x.supply.id===body.id);if(!s){s={supply:{id:next++,quantity:0},threshold:10};supplies.push(s);}Object.assign(s.supply,{name:body.name,supply_type:body.supplyType,pack_size:body.packSize,pack_label:body.packLabel,area_id:body.areaId,valid:body.valid});return s.supply;}
    if(action==='PostMovement'){const s=supplies.find(x=>x.supply.id===body.supplyId).supply;s.quantity=100;supplyLog.push({id:'9007199254741001',movement_type:body.type,quantity:100,balance_qty:100,reason:body.reason,created_at:'2026-10-06T09:00:00',cancelled:false});return {supply:s};}
    if(action==='UndoReceipt'){supplyLog[0].cancelled=true;return {};}
    if(action==='ListTools')return tools;
    if(action==='SaveTool'){let t=tools.find(x=>x.id===body.id);if(!t){t={id:next++,status:'normal'};tools.push(t);}Object.assign(t,{name:body.name,quantity:body.quantity,spec:body.spec,area_id:body.areaId,daily_check:body.dailyCheck,owner_staff_id:body.ownerStaffId,valid:body.valid});return t;}
    if(action==='ChangeStatus'){const t=tools.find(x=>x.id===body.toolId);toolLog.push({from_status:t.status,to_status:body.status,from_area_id:t.area_id,to_area_id:body.areaId,remark:body.remark,created_at:'2026-10-06T09:00:00'});t.status=body.status;return t;}
    if(action==='ListLog')return controller==='FnbTool'?toolLog:supplyLog;
    if(action==='SaveItem'){const a=areas.find(x=>x.area.id===body.areaId);let x=a.checks.find(x=>x.id===body.id);if(!x){x={id:next++};a.checks.push(x);}Object.assign(x,{area_id:body.areaId,name:body.name,method:body.method,kind:body.kind,required:body.required,minimum:body.minimum,maximum:body.maximum,unit:body.unit,valid:body.valid});return x;}
    if(action==='GetToday')return sheet?checkData():{businessDate:'2026-10-06',status:'none',items:areas.flatMap(a=>a.checks)};
    if(action==='StartToday'){sheet={id:'9007199254741003',status:'in_progress',business_date:'2026-10-06'};return checkData();}
    if(action==='SaveDraft'){for(const l of body.lines){const x=areas.flatMap(a=>a.checks).find(x=>x.id===l.itemId);Object.assign(x,{result:x.method==='number'&&l.value!=null?'pass':l.result,value:l.value,reason:l.reason,upload_id:l.uploadId});}return checkData();}
    if(action==='PassAll'){areas.flatMap(a=>a.checks).filter(x=>!x.result&&x.method!=='number').forEach(x=>x.result='pass');return checkData();}
    if(action==='RefreshSnapshot')return checkData();
    if(action==='Submit'){sheet.status='submitted';return checkData();}
    if(action==='Confirm'){sheet.status='confirmed';return checkData();}
    if(action==='HandleAbnormal'){const x=areas.flatMap(a=>a.checks).find(x=>String(x.id)===body.lineId);x.handlings=[{remark:body.remark,created_at:'2026-10-06T09:00:00'}];return checkData();}
    if(action==='ListHistory')return sheet?[checkData()]:[];
    return undefined;
  };
};
