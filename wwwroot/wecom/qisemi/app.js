(() => {
  'use strict';
  const { escape: esc, quantity: qty } = QisemiApi;
  const screen = document.getElementById('screen'), notice = document.getElementById('notice');
  const api = QisemiApi.create({ session: () => {
    try { return { SessionKey: sessionStorage.getItem('SessionKey'), SessionType: sessionStorage.getItem('SessionType') }; } catch (_) { return {}; }
  } });
  const state = { modules: [], warehouses: [], categories: [], sources: [], seq: 0, styles: new Map(), list: null, controller: null, route: {}, files: new Map(), previewUrl: null };
  function clearStyles(){for(const d of state.styles.values())if(d.photo)URL.revokeObjectURL(d.photo);state.styles.clear();}
  const archiveKeys = new Set(['products_all','accessories','product_classes_tree','units','warehouses','branches','warehouse_positions','clients','client_classes','suppliers','supplier_classes','accounts_all','projects','employees_all','roles']);
  const tabs = { inventory: '查库存', business: '业务单据', archives: '基础档案', more: '更多数据' };
  const sections = { summary: '基本信息', lines: '商品明细', settlements: '分账', writeoffs: '核销', relations: '关联单据', movements: '库存流水', versions: '历史版本', files: '图片附件', raw: '完整来源' };
  const labels = { id:'记录编号',name:'名称',displayName:'款式名称',code:'商品编号',barcode:'条码',brand:'品牌',model:'型号',styleCode:'款号',season:'雪季',color:'颜色',gender:'性别',flexLabel:'硬度',sizeLabel:'尺码',unitName:'单位',documentNo:'单据编号',documentType:'业务类型',businessAt:'业务日期',branchName:'门店',warehouseName:'仓库',partnerName:'往来单位',businessEmployeeName:'经办人',creatorName:'制单人',quantityMain:'账面数量',quantity:'数量',quantityBase:'基本单位数量',unitPrice:'单价',amount:'金额',costAmount:'成本金额',stockAmount:'库存金额',bookQuantity:'账面数量',countedQuantity:'实盘数量',differenceQuantity:'盘点差异',returnQuantity:'归还数量',inQuantity:'入库数量',outQuantity:'出库数量',balanceQuantity:'结存数量',allocatedAmount:'核销金额',settledAmount:'已结算金额',unsettledAmount:'未结算金额',accountName:'结算账户',telephone:'联系电话',contactName:'联系人',address:'地址',email:'邮箱',remark:'备注',insideRemark:'内部备注',sourceBillState:'来源单据状态',sourcePaymentState:'来源收付款状态',writeBack:'红冲',sourceEnabled:'来源启用',sourceDeleted:'来源删除',sourceMissing:'来源缺失',isStub:'缺档占位',versionKind:'版本种类',listingState:'来源列表状态',downloadState:'文件状态',originalName:'文件名',mimeType:'文件类型',byteLength:'文件大小',sheetName:'工作表',sheetRole:'表类型',rowCount:'原始行数',bindingState:'关联状态',issueCode:'核对项目',summary:'说明',severity:'级别',state:'状态',captureStartedAt:'采集开始',captureFinishedAt:'采集完成',publishedAt:'发布时间',verificationPassed:'核对通过',scope:'数据范围',moduleKey:'来源模块',carrier:'数据载体',actualCount:'原始记录数',expectedCount:'预计记录数',sourceTimezone:'来源时区' };
  Object.assign(labels,{sourceKey:'来源编号',rowOrdinal:'行号',batchId:'数据批次',receivable:'应收余额',payable:'应付余额',storedAmount:'储值余额',points:'积分',accountNumber:'账号',bankAccountName:'开户名称',currentAmount:'当前余额',initialAmount:'期初余额'});
  const enumNames={versionKind:{current:'当前版本',historical:'历史版本'},listingState:{current:'当前单据',history_only:'仅历史记录',missing_source:'来源缺失'},carrier:{api:'接口采集',native_sheet:'平台原表',media:'图片附件',metadata:'采集说明'},scope:{full:'全部业务',inventory:'库存'},state:{complete:'已取得',empty:'暂无记录',failed:'失败',omitted:'未采集',published:'已发布',open:'待核对'},bindingState:{unresolved:'未唯一关联',ambiguous:'有歧义',exact:'已关联'}};
  const scalar = v => v === null || v === undefined ? '—' : typeof v === 'boolean' ? (v ? '是' : '否') : typeof v === 'object' ? JSON.stringify(v) : String(v);
  const raw = value => `<details class="qs-raw"><summary>查看完整字段</summary><pre>${esc(JSON.stringify(value, null, 2))}</pre></details>`;
  const quantity = value => `<span class="${value < 0 ? 'qs-negative' : value === 0 ? 'qs-zero' : ''}">${esc(qty(value))}${value < 0 ? '<small> · 负库存</small>' : ''}</span>`;
  const button = (action, title, attrs = '') => `<button type="button" class="qs-text-button" data-action="${action}" ${attrs}>${title}</button>`;
  const empty = text => `<div class="qs-empty">${esc(text)}</div>`;
  const fields = (row, keys) => `<dl class="qs-property-list">${(keys || Object.keys(row).filter(k => labels[k] && k!=='id' && !k.endsWith('Id'))).filter(k => row[k] !== undefined).map(k => `<dt>${esc(labels[k] || k)}</dt><dd>${esc(scalar(enumNames[k]?.[row[k]] ?? row[k]))}</dd>`).join('')}</dl>`;
  function status(row) {
    const text = [row.writeBack ? '红冲' : '', row.versionKind === 'historical' ? '历史版本' : '', row.documentType === 'sale_draft' ? '草稿' : '', row.sourceEnabled === false ? '停用' : '', row.sourceDeleted ? '来源删除' : '', row.sourceMissing ? '来源缺失' : '', row.isStub ? '缺档占位' : ''].filter(Boolean);
    return text.map(t => `<span class="qs-state ${t === '红冲' ? 'qs-state-red' : ''}">${t}</span>`).join(' ');
  }
  function show(html) {
    const active = document.activeElement, id = active?.id, position = active?.selectionStart;
    screen.innerHTML = html;
    if (id && document.getElementById(id)) { const input = document.getElementById(id); input.focus({ preventScroll: true }); try { input.setSelectionRange(position, position); } catch (_) { } }
    else if(active?.dataset?.action){const keys=['action','id','sku','view'];const replacement=[...screen.querySelectorAll('button[data-action]')].find(b=>keys.every(k=>b.dataset[k]===active.dataset[k]));replacement?.focus({preventScroll:true});}
  }
  function fail(error) {
    if (error.name === 'AbortError') return;
    notice.hidden = false;
    notice.innerHTML = `${esc(error.message || '读取失败，请重试')} ${button(error.status === 409 ? 'refresh' : 'retry', error.status === 409 ? '刷新数据' : '重试')}`;
    if (screen.getAttribute('aria-busy') === 'true') show(empty('暂时未能取得数据，请点击上方重试。'));
    screen.removeAttribute('aria-busy');
  }
  function snapshot() {
    const inventory = api.snapshots.inventory?.batch?.captureFinishedAt, full = api.snapshots.full?.batch?.captureFinishedAt;
    const time = value => value ? new Date(value).toLocaleString('zh-CN', { timeZone: 'Asia/Shanghai', hour12: false }) : '未取得';
    document.getElementById('snapshot-status').textContent = `库存采集：${time(inventory)} · 业务采集：${time(full)}（北京时间）\n以最近成功导入为准 · 当前显示账面库存`;
  }
  function route() {
    const [tab, query = ''] = location.hash.slice(1).split('?');
    return { sortBy:'name', sortDirection:'asc', ...Object.fromEntries(new URLSearchParams(query)), tab: tabs[tab] ? tab : 'inventory' };
  }
  function go(next) {
    const q = new URLSearchParams();
    for (const [key, value] of Object.entries(next)) if (key !== 'tab' && value !== '' && value !== null && value !== undefined) q.set(key, String(value));
    const hash = '#' + (next.tab || 'inventory') + (q.size ? '?' + q : '');
    if (hash === location.hash) render(); else location.hash = hash;
    window.scrollTo({top:0});
  }
  function back() { go({ ...state.route, view: 'list', id: '', kind: '', section: '', detailPage: '' }); }
  function pager(total, page, detail = false) {
    const count = BigInt(total || '0'), p = Number(page || 1), last = BigInt(p * 30) >= count;
    return `<div class="qs-pager">${button(detail ? 'detail-prev' : 'prev', '上一页', p <= 1 ? 'disabled' : '')}<span>第 ${p} 页 · 共 ${esc(total || '0')} 条</span>${button(detail ? 'detail-next' : 'next', '下一页', last ? 'disabled' : '')}</div>`;
  }
  async function boot() {
    state.controller?.abort(); state.controller = new AbortController();
    const data = await api.request('Sources', {}, state.controller.signal); state.sources = data.items;
    if (!state.sources.length) throw new Error('暂无可用账套，请先完成数据导入');
    const picker = document.getElementById('source-select');
    const previous = picker.value;
    picker.innerHTML = state.sources.map(s => `<option value="${esc(s.id)}">${esc(s.displayName)}</option>`).join('');
    if (state.sources.some(s => s.id === previous)) picker.value = previous;
    document.getElementById('source-area').hidden = state.sources.length <= 1;
    await loadSource(picker.value);
  }
  async function loadSource(id) {
    state.seq++; state.controller?.abort(); state.controller = new AbortController();
    api.selectSource(id); clearStyles(); state.list = null;
    show(empty('正在读取账套与仓库…')); notice.hidden = true;
    const signal = state.controller.signal;
    const [modules, warehouses, categories] = await Promise.all([api.request('Modules', {}, signal), api.request('Warehouses', {}, signal), api.request('TopCategories', {}, signal)]);
    state.modules = modules.result; state.warehouses = warehouses.result; state.categories = categories.result;
    snapshot(); await render();
  }
  async function render() {
    const seq = ++state.seq; state.controller?.abort(); state.controller = new AbortController();
    for(const d of state.styles.values())if(d.loading){d.loading=false;if(!d.data)d.expanded=false;}
    const nextRoute = route();
    if ((nextRoute.resultWarehouseId || nextRoute.warehouseId) !== (state.route.resultWarehouseId || state.route.warehouseId)) clearStyles();
    state.route = nextRoute; state.files.clear(); notice.hidden = true;
    document.querySelectorAll('[data-tab]').forEach(b => b.setAttribute('aria-current', b.dataset.tab === state.route.tab ? 'page' : 'false'));
    show(empty('正在读取…')); screen.setAttribute('aria-busy', 'true');
    try {
      if (state.route.tab === 'inventory') await inventory(seq);
      else if (!state.route.module) catalog();
      else await browse(seq);
      if (seq === state.seq) { screen.removeAttribute('aria-busy'); snapshot(); }
    } catch (e) { if (seq === state.seq) fail(e); }
  }
  function inventoryForm() {
    const r = state.route;
    return `<form id="inventory-search" class="qs-search-card" role="search"><div class="qs-search-row"><div class="qs-input-wrap"><label class="qs-sr-only" for="inventory-keyword">关键词模糊查询：商品名称、品牌、款号、条码、尺码或分类</label><input id="inventory-keyword" class="qs-input" type="search" placeholder="关键词：商品 / 品牌 / 款号 / 分类" value="${esc(r.keyword || '')}" autocomplete="off" enterkeyhint="search" maxlength="160"><button type="button" class="qs-clear" data-action="clear-search" aria-label="清空搜索">×</button></div><button class="qs-button" type="submit">搜索</button></div><p class="qs-search-hint">支持部分词和多关键词，如 RC4 110、双板鞋 Fischer 24-25</p><div class="qs-filters"><label for="inventory-category" class="qs-filter-label">顶级分类</label><select id="inventory-category" class="qs-warehouse" aria-label="按顶级分类筛选"><option value="">全部分类</option>${state.categories.map(c => `<option value="${esc(c.id)}" ${r.topCategoryId === c.id ? 'selected' : ''}>${esc(c.name)}</option>`).join('')}</select></div><div class="qs-filters"><label for="inventory-warehouse" class="qs-filter-label">筛选仓库</label><select id="inventory-warehouse" class="qs-warehouse" aria-label="按仓库筛选"><option value="">全部仓库</option>${state.warehouses.map(w => `<option value="${esc(w.id)}" ${r.warehouseId === w.id ? 'selected' : ''}>${esc(w.name)}</option>`).join('')}</select></div><div class="qs-inventory-options"><label class="qs-check" for="inventory-in-stock"><input id="inventory-in-stock" type="checkbox" ${r.inStockOnly !== 'false' ? 'checked' : ''}>只看有库存</label></div>${r.skuId ? `<p class="qs-search-hint">已按指定 SKU 定位同款 ${button('clear-sku', '取消定位')}</p>` : ''}</form>`;
  }
  async function inventory(seq) {
    const r = state.route;
    show(inventoryForm()+empty('正在读取库存…'));
    const data = await api.request('Inventory', { keyword: r.keyword, warehouseId: r.warehouseId, resultWarehouseId:r.resultWarehouseId, topCategoryId: r.topCategoryId, resultCategoryId: r.resultCategoryId, sortBy:r.sortBy || 'name', sortDirection:r.sortDirection || 'asc', inStockOnly: r.inStockOnly !== 'false', skuId: r.skuId, page: r.page || 1 }, state.controller.signal);
    if (seq !== state.seq) return;
    const visibleIds=new Set(data.result.items.map(row=>row.id));
    for(const [key,d] of state.styles)if(!visibleIds.has(key)){if(d.photo)URL.revokeObjectURL(d.photo);state.styles.delete(key);}
    state.list = data.result; paintInventory();
    void loadListThumbnails(seq);
  }
  function paintInventory() {
    const rows = state.list.items;
    show(inventoryForm() + inventorySorting() + inventoryFacets() + inventoryWarehouseFacets() + `<div class="qs-meta"><span role="status">共 <strong>${esc(state.list.total)}</strong> 款</span>${button('about', '数据说明')}</div><div id="inventory-info" class="qs-info" hidden>仓库括号为当前搜索与分类下的账面库存件数，汇总同款全部尺码，负库存参与抵扣；数量不完整时显示未知。“全部仓库”包含其他仓的负库存，可能与有货仓标签之和不同。有货仓按至少一个尺码有正库存定位商品；零、负数或未知库存仓也可用上方仓库筛选查看。点击后按所选仓显示数量，点“全部仓库”恢复。分类括号内为当前搜索、顶级分类、仓库和库存条件下全部结果的款数，不是库存件数；点击分类可二次筛选，同款多个分类可分别计入。分类排序按分类名称，同款跨分类时取匹配分类中名称最前的一项，无分类排最后。“只看有库存”按所选仓库范围内至少一个尺码的账面数量大于零筛选。全部仓库按尺码总库存判断。取消勾选可查看零或负库存款式。零库存尺码保留；仓库未完整采集时数量显示“未知”。各模块采集时间可能不同，详见页面底部。</div><div class="qs-results">${rows.length ? rows.map(card).join('') : empty('没有匹配的商品，请更换关键词、调整分类或仓库，或取消“只看有库存”。')}</div>` + pager(state.list.total, state.route.page));
    for (const strip of document.querySelectorAll('.qs-category-facets')) {
      const selected = strip.querySelector('[aria-pressed="true"]');
      if (selected && selected.offsetLeft + selected.offsetWidth > strip.clientWidth) strip.scrollLeft = selected.offsetLeft + selected.offsetWidth - strip.clientWidth + 8;
    }
  }
  function inventorySorting() {
    const r = state.route;
    return `<div class="qs-inventory-sort"><label for="inventory-sort-by">排序</label><select id="inventory-sort-by" aria-label="排序字段"><option value="name" ${r.sortBy !== 'topCategory' ? 'selected' : ''}>名称</option><option value="topCategory" ${r.sortBy === 'topCategory' ? 'selected' : ''}>顶级分类</option></select><label class="qs-sr-only" for="inventory-sort-direction">排序方向</label><select id="inventory-sort-direction" aria-label="排序方向"><option value="asc" ${r.sortDirection !== 'desc' ? 'selected' : ''}>升序 ↑</option><option value="desc" ${r.sortDirection === 'desc' ? 'selected' : ''}>降序 ↓</option></select></div>`;
  }
  function inventoryFacets() {
    const selected = state.route.resultCategoryId || '';
    const choices = [{id:'',name:'全部',count:state.list.unfilteredTotal ?? state.list.total},...(state.list.categoryFacets || [])];
    return `<div class="qs-result-facet-label">商品分类</div><nav class="qs-category-facets" aria-label="结果分类二次筛选" tabindex="0">${choices.map(c => `<button type="button" data-action="result-category" data-id="${esc(c.id)}" aria-pressed="${selected === c.id}">${esc(c.name)}<span>（${esc(c.count)}款）</span></button>`).join('')}</nav>`;
  }
  function inventoryWarehouseFacets() {
    const selected = state.route.resultWarehouseId || '';
    const choices = [{id:'',name:'全部仓库',quantityMain:state.list.warehouseUnfilteredQuantityMain},...(state.list.warehouseFacets || [])];
    return `<div class="qs-result-facet-label">有货仓库 · 账面件数</div><nav class="qs-category-facets qs-warehouse-facets" aria-label="结果仓库二次筛选" tabindex="0">${choices.map(w => `<button type="button" data-action="result-warehouse" data-id="${esc(w.id)}" aria-pressed="${selected === w.id}">${esc(w.name)}<span>（${w.quantityMain == null ? '数量未知' : esc(qty(w.quantityMain)) + '件'}）</span></button>`).join('')}</nav>`;
  }
  function categoryPaths(row) {
    const paths = row.categoryPaths || [];
    return `<div class="qs-category-paths">${paths.length ? paths.map(path => `<p>分类：${(path.levels || []).map(level => esc(level.name)).join(' → ')}${path.complete === false ? '（上级分类缺失）' : ''}</p>`).join('') : '<p>分类：暂无分类资料</p>'}</div>`;
  }
  function thumbnail(d) {
    return d?.photo ? `<img src="${esc(d.photo)}" alt="商品图片" loading="lazy" decoding="async">` : `<span>${d?.photoLoading ? '加载中' : d?.noPhoto ? '暂无图片' : d?.photoFailed ? '图片暂不可用' : '商品'}</span>`;
  }
  function updateThumbnail(id, d) {
    if (state.route.tab !== 'inventory' || state.styles.get(id) !== d) return;
    const photo = screen.querySelector(`.qs-product[data-style="${id}"] .qs-photo`);
    if (photo) photo.innerHTML = thumbnail(d);
  }
  async function loadListThumbnails(seq) {
    const rows = state.list.items, signal = state.controller.signal;
    let index = 0;
    async function worker() {
      while (seq === state.seq && !signal.aborted && index < rows.length) {
        const id = rows[index++].id, d = state.styles.get(id) || {};
        state.styles.set(id, d);
        if (!d.photo && !d.noPhoto) await loadThumbnail(id, d, seq, signal);
      }
    }
    await Promise.all([worker(), worker()]);
  }
  function card(row) {
    const d = state.styles.get(row.id);
    return `<article class="qs-product" data-style="${esc(row.id)}"><button type="button" class="qs-product-head" data-action="expand" data-id="${esc(row.id)}" aria-expanded="${!!d?.expanded}"><div class="qs-photo">${thumbnail(d)}</div><div class="qs-product-main"><div class="qs-brand">${esc(row.brand || '')}</div><div class="qs-name">${esc(row.displayName)}</div><div class="qs-attributes">${[row.styleCode,row.season,row.color,row.gender].filter(Boolean).map(v => `<span>${esc(v)}</span>`).join('')}</div>${categoryPaths(row)}<div class="qs-overview"><span>${esc(row.sizeCount)} 个尺码</span><b>${quantity(row.quantityMain)}</b></div></div><span class="qs-chevron">${d?.expanded ? '⌃' : '⌄'}</span></button>${d?.expanded ? `<div class="qs-detail">${d.loading ? empty('正在读取尺码与各仓库存…') : d.error ? `${esc(d.error)} ${button('style-retry','重试',`data-id="${row.id}"`)}` : stylePanel(row.id, d)}</div>` : ''}</article>`;
  }
  function stylePanel(id, d) {
    if (!d.data) return '';
    const sizes = d.data.sizes, active = sizes.find(s => s.id === d.active) || sizes.find(s => s.id === state.route.skuId) || sizes.find(s => s.sizeLabel && (state.route.keyword || '').includes(s.sizeLabel)) || sizes[0];
    d.active = active.id;
    const warehouseId = state.route.resultWarehouseId || state.route.warehouseId;
    const warehouses = d.data.warehouses.filter(w => (!warehouseId || w.id === warehouseId) && (!d.query || w.name.toLowerCase().includes(d.query.toLowerCase())));
    const cell = (sku, w) => sku.warehouses.find(x => x.warehouseId === w.id) || { quantityMain: null, quantityState: 'unknown' };
    const sum = sku => warehouses.length && warehouses.every(w => cell(sku,w).quantityMain !== null) ? warehouses.reduce((n,w) => n + cell(sku,w).quantityMain, 0) : null;
    const visible = warehouses.filter(w => d.zero || (d.matrix ? sizes.some(s => cell(s,w).quantityMain !== 0) : cell(active,w).quantityMain !== 0));
    return `<div class="qs-detail-top"><span class="qs-detail-title">同款尺码 · 全部保留</span><div class="qs-view-switch"><button type="button" class="qs-view" data-action="style-view" data-id="${id}" data-view="size" aria-pressed="${!d.matrix}">按尺码</button><button type="button" class="qs-view" data-action="style-view" data-id="${id}" data-view="matrix" aria-pressed="${!!d.matrix}">库存对照</button></div></div><div class="qs-sizes">${sizes.map(s => `<button type="button" class="qs-size" data-action="size" data-id="${id}" data-sku="${esc(s.id)}" aria-pressed="${s.id === active.id}"><b>${esc(s.sizeLabel || '未标尺码')}</b><span>账面 ${esc(qty(sum(s)))}</span></button>`).join('')}</div><div class="qs-warehouse-tools"><strong>${d.matrix ? '各仓尺码库存' : esc(active.sizeLabel || '') + ' 码 · 各仓库存'}</strong><label class="qs-check"><input type="checkbox" data-zero="${id}" ${d.zero ? 'checked' : ''}>显示零库存仓</label></div><input id="warehouse-search-${id}" class="qs-warehouse-search" data-style-search="${id}" type="search" value="${esc(d.query || '')}" aria-label="筛选当前产品的仓库" placeholder="查找仓库，如：旗舰、南山">${!visible.length ? empty(warehouses.length ? '当前没有非零或未知库存仓，可打开“显示零库存仓”。' : '没有匹配的仓库。') : d.matrix ? `<div class="qs-table-scroll"><table class="qs-table"><caption class="qs-table-caption">各仓为行、尺码为列 · 账面数量</caption><thead><tr><th>仓库</th>${sizes.map(s => `<th>${esc(s.sizeLabel || '未标尺码')}</th>`).join('')}</tr></thead><tbody>${visible.map(w => `<tr><th>${esc(w.name)}</th>${sizes.map(s => `<td>${quantity(cell(s,w).quantityMain)}</td>`).join('')}</tr>`).join('')}</tbody><tfoot><tr><th>所选仓合计</th>${sizes.map(s => `<td>${quantity(sum(s))}</td>`).join('')}</tr></tfoot></table></div>` : `<div class="qs-stock-list">${visible.map(w => `<div class="qs-stock-row"><span>${esc(w.name)}${cell(active,w).quantityState === 'unknown' ? '<small> · 未取得完整数量</small>' : ''}</span><b>${quantity(cell(active,w).quantityMain)}</b></div>`).join('')}</div>`}<div class="qs-sku-line">${esc(active.code || '')} · ${esc(active.unitName || '')}</div>${button('sku-flow', '查看此尺码库存流水 ›', `data-sku="${esc(active.id)}"`)}${button('sku-files', '商品图片与附件 ›', `data-sku="${esc(active.id)}"`)}${raw(d.data.style)}`;
  }
  async function expand(id, retry = false) {
    let d = state.styles.get(id) || {};
    d.expanded = retry || !d.expanded; state.styles.set(id, d); paintInventory();
    if (!d.expanded || (d.data && !retry)) return;
    const seq = state.seq; d.loading = true; d.error = ''; paintInventory();
    try {
      const data = await api.request('Styles/' + id, { includeZero: true, warehouseId: state.route.resultWarehouseId || state.route.warehouseId }, state.controller.signal);
      if (seq !== state.seq) return; d.data = data.result;
    } catch (e) { if (seq !== state.seq || e.name === 'AbortError') return; if (e.status === 409) fail(e); d.error = e.message; }
    finally { if (seq === state.seq) { d.loading = false; paintInventory(); } }
  }
  async function loadThumbnail(id,d,seq,signal){
    d.photoLoading = true; d.photoFailed = false; updateThumbnail(id,d);
    try{
      const images=await api.request('Files',{styleId:id,fileType:'image',pageSize:1},signal);
      if(seq!==state.seq || signal.aborted)return;
      const image=images.result.items[0];if(!image?.contentPath){d.noPhoto=true;return;}
      const blob=await api.file(image.contentPath,signal);if(seq!==state.seq || signal.aborted)return;
      if(d.photo)URL.revokeObjectURL(d.photo);d.photo=URL.createObjectURL(blob);
    }catch(error){if(error.name!=='AbortError' && seq===state.seq)d.photoFailed=true;}
    finally{if(seq===state.seq){d.photoLoading=false;updateThumbnail(id,d);}}
  }
  function catalog() {
    const r = state.route, modules = state.modules.filter(m => m.tab === r.tab);
    const coverage = m => { const main = m.coverage?.find(c => c.moduleKey === m.sources?.[0]); return main ? main.state === 'empty' ? '本次快照暂无记录' : `已采集 ${main.actualCount} 条来源记录` : m.coverage?.length ? '已取得来源资料' : '查看文件或快照资料'; };
    show(`<div class="qs-browser-heading"><h2>${tabs[r.tab]}</h2><p>查看最近成功导入的业务资料</p></div><input class="qs-input" id="catalog-search" type="search" placeholder="查找模块"><div id="module-catalog">${[...new Set(modules.map(m => m.section))].map(section => `<section><h3 class="qs-section-title">${esc(section)}</h3><div class="qs-module-grid">${modules.filter(m => m.section === section).map(m => `<button type="button" class="qs-module-button" data-action="module" data-module="${esc(m.key)}" data-label="${esc(m.label)}"><span><b>${esc(m.label)}</b><small>${esc(coverage(m))}</small></span><span>›</span></button>`).join('')}</div></section>`).join('')}</div>`);
  }
  function listConfig(m) {
    const r = state.route, q = { keyword: r.keyword, page: r.page || 1 };
    if(r.rawModule)return {path:'Records',q:{...q,module:r.rawModule},kind:'record'};
    if (['document','finance','history','flow','line'].includes(m.kind)) { q.dateFrom = r.dateFrom; q.dateTo = r.dateTo; }
    if (['document','finance'].includes(m.kind)) return { path: 'Documents', q: { ...q, module: m.key, writeBack:r.state==='red'?true:r.state==='normal'?false:null }, kind: 'document' };
    if (m.kind === 'history') return { path: 'Versions', q: { ...q, module: m.key }, kind: 'version' };
    if (archiveKeys.has(m.key)) return { path: 'Archives/' + m.key, q: { ...q, sourceEnabled: r.state === 'enabled' ? true : r.state === 'disabled' ? false : null, sourceDeleted: r.state === 'deleted' ? true : null }, kind: 'archive' };
    if (m.key === 'stock') return { path: 'StockTotals', q, kind: 'record' };
    if (m.key === 'stock_by_warehouse') return { path: 'WarehouseStocks', q: { ...q, warehouseId: r.warehouseId }, kind: 'record' };
    if (m.key === 'stock_movements') return { path: 'StockMovements', q: { ...q, skuId: r.skuId, warehouseId: r.warehouseId, documentId: r.documentId }, kind: 'record' };
    if (m.key === 'sales_line_items') return { path: 'SalesLines', q: { ...q, skuId: r.skuId, documentId: r.documentId }, kind: 'record' };
    if (m.key === 'media' || (m.key === 'exports' && r.exportMode === 'files')) return { path: 'Files', q: { ...q, skuId: r.skuId, documentId: r.documentId, fileType: m.key === 'exports' ? 'export' : r.fileType }, kind: 'file' };
    if (m.key === 'exports') return { path: 'ExportSheets', q, kind: 'sheet' };
    if (m.key === 'snapshot') return { path: r.issues ? 'DataIssues' : 'Snapshot', q, kind: r.issues ? 'issue' : 'snapshot' };
    return { path: 'Records', q: { ...q, module: m.sources[0] }, kind: 'record' };
  }
  function browserTools(m) {
    const r = state.route, dates = ['document','finance','history','flow','line'].includes(m.kind);
    return `<button type="button" class="qs-browser-back" data-action="catalog">‹ 返回${esc(tabs[r.tab])}</button><div class="qs-browser-heading"><h2>${esc(m.label)}</h2></div>${m.key === 'exports' ? `<div class="qs-detail-tabs">${button('export-sheets','工作表与原始行')}${button('export-files','原文件下载')}</div>` : ''}<form id="browser-search" class="qs-list-tools"><div class="qs-search-row"><input id="browser-keyword" class="qs-input" type="search" value="${esc(r.keyword || '')}" placeholder="关键词搜索" aria-label="搜索当前模块" maxlength="160"><button type="submit" class="qs-button">查询</button></div>${dates ? `<div class="qs-date-row"><label class="qs-date-field">开始日期<input id="date-from" type="date" value="${esc(r.dateFrom || '')}"></label><label class="qs-date-field">结束日期<input id="date-to" type="date" value="${esc(r.dateTo || '')}"></label></div>` : ''}${['document','finance'].includes(m.kind) ? `<select id="browser-state" class="qs-warehouse" aria-label="单据状态"><option value="">全部状态</option><option value="normal" ${r.state==='normal'?'selected':''}>未红冲</option><option value="red" ${r.state==='red'?'selected':''}>红冲</option></select>` : ''}${archiveKeys.has(m.key) ? `<label class="qs-filter-label">来源状态<select id="browser-state" class="qs-warehouse"><option value="">全部状态</option><option value="enabled" ${r.state === 'enabled' ? 'selected' : ''}>启用</option><option value="disabled" ${r.state === 'disabled' ? 'selected' : ''}>停用</option><option value="deleted" ${r.state === 'deleted' ? 'selected' : ''}>来源删除</option></select></label>` : ''}${m.key === 'media' ? `<select id="file-type" class="qs-warehouse" aria-label="文件类型"><option value="">全部文件</option>${[['image','图片'],['attachment','单据附件'],['pdf','PDF'],['export','原表文件']].map(([v,t]) => `<option value="${v}" ${r.fileType === v ? 'selected' : ''}>${t}</option>`).join('')}</select>` : ''}${r.skuId || r.documentId ? `<p class="qs-plain-note">已按关联商品或单据筛选 ${button('clear-links','查看全部')}</p>` : ''}</form>`;
  }
  async function browse(seq) {
    const r = state.route, m = state.modules.find(m => m.key === r.module);
    if (!m) throw new Error('模块不存在，请返回选择');
    if (['document','version','archive','record','sheet'].includes(r.view)) return detail(seq, m);
    show(browserTools(m)+empty('正在读取记录…'));
    const config = listConfig(m), data = await api.request(config.path, config.q, state.controller.signal);
    if (seq !== state.seq) return;
    if (config.kind === 'snapshot') return show(browserTools(m) + snapshotPanel(data.result));
    state.list = data.result;
    show(browserTools(m) + listRows(data.result, config.kind, false));
  }
  function recordLinks(row) {
    return (row.documentId ? button('linked-document','查看来源单据 ›',`data-id="${esc(row.documentId)}"`) : '')
      + (row.toDocumentId ? button('linked-document','查看关联单据 ›',`data-id="${esc(row.toDocumentId)}"`) : '')
      + (row.skuId ? button('linked-stock','查询该商品库存 ›',`data-id="${esc(row.skuId)}"`) : '')
      + (row.rawRecordId ? button('linked-raw','完整来源字段 ›',`data-id="${esc(row.rawRecordId)}"`) : '');
  }
  function listRows(data, kind, detailPage) {
    const rows = data.items || [];
    return `<div class="qs-list-count">共 ${esc(data.total || '0')} 条${kind === 'version' ? ' · 历史版本单列' : ''}</div><div class="qs-list-stack">${rows.length ? rows.map(row => {
      if(kind==='issue')return `<article class="qs-detail-card"><h3>${esc(row.summary)}</h3>${fields(row,['issueCode','severity','state'])}${recordLinks(row)}${raw(row.detailsJson)}</article>`;
      if (kind === 'file') {
        state.files.set(row.id, row);
        return `<div class="qs-detail-card"><button type="button" class="qs-file-button" data-action="file" data-id="${esc(row.id)}"><span class="qs-file-type">${esc(row.extension || '文件')}</span><span class="qs-file-copy">${esc(row.originalName || row.productName || '图片或附件')}<small>${esc(row.productName || row.documentNo || '')} · ${esc(row.byteLength || '未知')} 字节</small></span><span>›</span></button>${row.fileId ? '' : '<p>来源未取得原文件</p>'}${recordLinks(row)}</div>`;
      }
      const title = row.documentNo || row.name || row.productName || row.sheetName || row.summary || row.payloadJson?.['商品名称'] || row.payloadJson?.['单据编号'] || row.payloadJson?.['客户名称'] || row.sourceEntityKey || `来源行 ${row.rowOrdinal || row.id}`;
      const id = kind === 'record' ? row.rawRecordId || row.id : row.id;
      return `<article class="qs-detail-card"><button type="button" class="qs-data-record" data-action="detail" data-kind="${kind}" data-id="${esc(id)}"><div class="qs-record-top"><span class="qs-record-title">${esc(title)}</span><span>${status(row)}</span></div><p class="qs-record-sub">${esc([row.businessAt,row.partnerName,row.warehouseName,row.sizeLabel,row.moduleKey].filter(Boolean).join(' · '))}</p><div class="qs-record-bottom"><span>${esc(row.code || row.productCode || enumNames.versionKind[row.versionKind] || '')}</span><b>${row.quantityMain !== undefined ? quantity(row.quantityMain) : row.quantity !== undefined ? `数量 ${esc(qty(row.quantity))}` : row.amount !== undefined ? `¥${esc(qty(row.amount))}` : row.rowCount !== undefined ? `${esc(row.rowCount)} 行` : '查看详情 ›'}</b></div></button>${recordLinks(row)}${row.payloadJson ? raw(row.payloadJson) : ''}</article>`;
    }).join('') : empty('最近一次快照中暂无记录。')}</div>` + pager(data.total, detailPage ? state.route.detailPage : state.route.page, detailPage);
  }
  function snapshotPanel(data) {
    return `<section class="qs-detail-card">${fields(data.batch)}${raw(data.batch.verificationJson)}${button('issues','查看来源核对问题 ›')}</section><h3>逐模块采集情况</h3>${data.modules.map(m => `<details class="qs-detail-card"><summary>${esc(m.moduleKey)} · ${esc(m.state)} · ${esc(m.actualCount)} 条</summary>${fields(m)}${raw(m.coverageJson)}${button('raw-module','查看完整来源记录 ›',`data-module="${esc(m.moduleKey)}"`)}</details>`).join('')}`;
  }
  async function detail(seq, m) {
    const r = state.route, page = r.detailPage || 1;
    if(!/^\d+$/.test(r.id || ''))throw new Error('记录地址无效，请返回列表选择');
    let data;
    if (r.view === 'document') data = await api.request('Documents/' + r.id, {}, state.controller.signal);
    if (r.view === 'version') data = await api.request('Versions/' + r.id, {}, state.controller.signal);
    if (r.view === 'archive') data = await api.request('Archives/' + m.key + '/' + r.id, {}, state.controller.signal);
    if (r.view === 'record') data = await api.request('Records/' + r.id, {}, state.controller.signal);
    if (r.view === 'sheet') data = await api.request('ExportSheets/' + r.id + '/Rows', { page }, state.controller.signal);
    if (seq !== state.seq) return;
    const content = data.result, head = button('back','‹ 返回列表');
    if (r.view === 'document' || r.view === 'version') {
      const doc = content.document, section = r.section || 'summary';
      const controls = `<div class="qs-browser-heading"><h2>${esc(doc.documentNo || m.label)}</h2>${status(doc)}</div><div class="qs-detail-tabs">${['summary',...content.sections].map(s => `<button type="button" data-action="section" data-section="${s}" aria-pressed="${section === s}">${esc(sections[s])}</button>`).join('')}</div>`;
      let panel = `<section class="qs-detail-card">${fields(doc)}${raw(doc.detailCoverageJson)}${raw(content.raw?.record?.payloadJson || content.raw)}</section>`;
      if (section !== 'summary') {
        const part = await api.request('Documents/' + doc.documentId + '/Sections/' + section, { versionId: doc.id, page }, state.controller.signal);
        if (seq !== state.seq) return;
        panel = listRows(part.result, section === 'files' ? 'file' : section === 'versions' ? 'version' : 'record', true);
      }
      show(head + controls + panel); return;
    }
    if (r.view === 'archive') {
      const row = content.archive;
      show(head + `<h2>${esc(row.name)}</h2><section class="qs-detail-card">${status(row)}${fields(row)}${m.key === 'products_all' ? button('linked-stock','查询同款各尺码库存 ›',`data-id="${esc(row.id)}"`) + button('sku-files','商品图片与附件 ›',`data-sku="${esc(row.id)}"`) : ''}${m.key === 'warehouses' ? button('warehouse-stock','查询此仓库存 ›',`data-id="${esc(row.id)}"`) : ''}${raw(content.raw?.record?.payloadJson || content.raw)}</section>`); return;
    }
    if (r.view === 'sheet') { show(head + `<h2>${esc(content.sheet.sheetName)}</h2><section class="qs-detail-card">${fields(content.sheet)}${button('export-files','查找原表文件 ›')}</section>` + listRows(content.rows,'record',true)); return; }
    show(head + `<h2>完整来源记录</h2><section class="qs-detail-card">${fields(content.record,['moduleKey','carrier','rowOrdinal','batchId'])}${(content.documents || []).map(d => button('linked-document',`查看关联单据 · ${esc(d.documentType)} ›`,`data-id="${esc(d.documentId)}"`)).join('')}${raw(content.record.payloadJson)}</section>`);
  }
  async function file(id) {
    const row = state.files.get(id); if (!row?.contentPath) return fail(new Error('本记录没有可用原文件'));
    const seq=state.seq;
    try {
      const blob = await api.file(row.contentPath, state.controller.signal);if(seq!==state.seq)return;
      const url = URL.createObjectURL(blob);
      if (/^image\/(jpeg|png|gif|webp|bmp|heic)$/.test(blob.type)) {
        closePreview(); state.previewUrl = url; document.getElementById('preview-image').src = url;
        document.getElementById('preview-name').textContent = row.originalName || row.productName || '图片'; document.getElementById('file-preview').showModal();
      } else { const a = document.createElement('a'); a.href = url; a.download = row.originalName || '七色米文件'; document.body.append(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 60000); }
    } catch (e) { fail(e); }
  }
  function closePreview() { document.getElementById('file-preview').close(); document.getElementById('preview-image').removeAttribute('src'); if (state.previewUrl) URL.revokeObjectURL(state.previewUrl); state.previewUrl = null; }
  document.getElementById('preview-close').onclick = closePreview;
  document.getElementById('file-preview').addEventListener('cancel', closePreview);
  document.getElementById('source-select').onchange = e => loadSource(e.target.value).catch(fail);
  screen.addEventListener('error', e => {
    if (!e.target.matches?.('.qs-photo img')) return;
    const id = e.target.closest('.qs-product')?.dataset.style, d = state.styles.get(id);
    if (!d?.photo || e.target.getAttribute('src') !== d.photo) return;
    URL.revokeObjectURL(d.photo); delete d.photo; d.photoFailed = true; updateThumbnail(id,d);
  }, true);
  document.addEventListener('click', e => {
    const tab = e.target.closest('[data-tab]'); if (tab) { go({tab:tab.dataset.tab}); return; }
    const target = e.target.closest('[data-action]'); if (!target || target.disabled) return;
    const { action, id, sku, module, kind, section } = target.dataset, r = state.route;
    if (action === 'refresh') { api.reset(); clearStyles(); boot().catch(fail); }
    else if (action === 'retry') {if(state.modules.length)render();else boot().catch(fail);}
    else if (action === 'catalog') go({tab:r.tab});
    else if (action === 'module') go({tab:r.tab,module,view:'list'});
    else if (action === 'clear-search') go({...r,keyword:'',skuId:'',resultCategoryId:'',resultWarehouseId:'',sortBy:'name',sortDirection:'asc',page:1});
    else if (action === 'result-category') go({...r,resultCategoryId:id,page:1});
    else if (action === 'result-warehouse') go({...r,resultWarehouseId:id,page:1});
    else if (action === 'clear-sku') go({...r,skuId:'',page:1});
    else if (action === 'clear-links') go({...r,skuId:'',documentId:'',warehouseId:'',page:1});
    else if (action === 'about') document.getElementById('inventory-info').hidden = !document.getElementById('inventory-info').hidden;
    else if (action === 'expand' || action === 'style-retry') expand(id,action === 'style-retry');
    else if (action === 'size' || action === 'style-view') { const d = state.styles.get(id); if (action === 'size') { d.active=sku;d.matrix=false; } else d.matrix=target.dataset.view==='matrix'; paintInventory(); }
    else if (action === 'sku-flow') go({tab:'more',module:'stock_movements',skuId:sku,warehouseId:r.resultWarehouseId || r.warehouseId});
    else if (action === 'sku-files') go({tab:'more',module:'media',skuId:sku});
    else if (action === 'linked-stock') go({tab:'inventory',skuId:id});
    else if (action === 'warehouse-stock') go({tab:'inventory',warehouseId:id});
    else if (action === 'detail') go({...r,view:kind,id,section:'',detailPage:1});
    else if (action === 'linked-document') go({...r,view:'document',id,section:'',detailPage:1});
    else if (action === 'linked-raw') go({...r,view:'record',id,section:'',detailPage:1});
    else if (action === 'back') back();
    else if (action === 'section') go({...r,section,detailPage:1});
    else if (action === 'prev' || action === 'next') go({...r,page:Math.max(1,Number(r.page||1)+(action==='next'?1:-1))});
    else if (action === 'detail-prev' || action === 'detail-next') go({...r,detailPage:Math.max(1,Number(r.detailPage||1)+(action==='detail-next'?1:-1))});
    else if (action === 'file') file(id);
    else if (action === 'export-files' || action === 'export-sheets') go({tab:'more',module:'exports',exportMode:action==='export-files'?'files':'sheets'});
    else if (action === 'issues') go({...r,issues:1,view:'list',page:1});
    else if (action === 'raw-module') go({tab:'more',module:'snapshot',rawModule:module,view:'list'});
  });
  document.addEventListener('submit', e => {
    if (!['inventory-search','browser-search'].includes(e.target.id)) return; e.preventDefault();
    if (e.target.id === 'inventory-search') go({tab:'inventory',keyword:document.getElementById('inventory-keyword').value.trim(),warehouseId:document.getElementById('inventory-warehouse').value,topCategoryId:document.getElementById('inventory-category').value,inStockOnly:document.getElementById('inventory-in-stock').checked,sortBy:'name',sortDirection:'asc'});
    else go({...state.route,keyword:document.getElementById('browser-keyword').value.trim(),dateFrom:document.getElementById('date-from')?.value,dateTo:document.getElementById('date-to')?.value,state:document.getElementById('browser-state')?.value,fileType:document.getElementById('file-type')?.value,page:1});
  });
  document.addEventListener('change', e => {
    if (e.target.id === 'inventory-warehouse') { clearStyles();go({...state.route,warehouseId:e.target.value,resultCategoryId:'',resultWarehouseId:'',page:1}); }
    if (e.target.id === 'inventory-category') go({...state.route,topCategoryId:e.target.value,resultCategoryId:'',resultWarehouseId:'',page:1});
    if (e.target.id === 'inventory-in-stock') go({...state.route,inStockOnly:e.target.checked,resultCategoryId:'',resultWarehouseId:'',page:1});
    if (e.target.id === 'inventory-sort-by') go({...state.route,sortBy:e.target.value,page:1});
    if (e.target.id === 'inventory-sort-direction') go({...state.route,sortDirection:e.target.value,page:1});
    if (e.target.dataset.zero) { state.styles.get(e.target.dataset.zero).zero=e.target.checked;paintInventory(); }
  });
  document.addEventListener('input', e => {
    if (e.target.dataset.styleSearch) { state.styles.get(e.target.dataset.styleSearch).query=e.target.value;paintInventory(); }
    if (e.target.id==='catalog-search') document.querySelectorAll('[data-label]').forEach(b=>b.hidden=!b.dataset.label.includes(e.target.value.trim()));
  });
  window.addEventListener('hashchange', () => { closePreview(); if(state.modules.length) render(); });
  window.addEventListener('pagehide', () => { state.controller?.abort();closePreview(); });
  boot().catch(fail);
})();
