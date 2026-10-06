(function () {
  'use strict';
  const $ = id => document.getElementById(id), esc = FnbClient.escape;
  const storageNames = { ambient: '常温', chilled: '冷藏', frozen: '冷冻' };
  const measureNames = { weight: '按重量', volume: '按体积', count: '按件数' };
  const unitNames = { g: 'g', ml: 'ml', piece: '个' };
  const titles = { stock: ['食材库总览', '单店 · 批次级库存'], inbound: ['每日入库', '分类 → 名称 → 拍照 → 批次号'], ops: ['作业台', '拆箱 · 解冻 · 切片 · 开盒'], check: ['今日开门检查', '按存储区域逐项检查'], serve: ['出餐扣减', '按订单扣减 · 配方可现场微调'], more: ['更多功能', '基础资料与管理'], cats: ['分类维护', '一级分类 / 二级分类'], routes: ['进货规格与链路', '形态 · 作业 · 进货规格'], expiry: ['临期与过期', '未开封 / 作业后有效到期'], destroy: ['过期食材销毁', '待销毁与已销毁记录'], low: ['用量预警', '比例预警 / 固定预警'], prep: ['半成品制作', '制作即核销原料，产出入库'], recipe: ['菜品配方', '每道菜的用料与规格'], count: ['盘点核对', '系统数量与实盘数量'], dash: ['数据看板', '损耗 · 周转 · 成本'], areas: ['存储区域', '区域树 · 照片 · 检查项'], supplies: ['餐饮物资', '一次性 / 可重复使用餐具'], tools: ['餐饮工具', '工具档案与状态'], checkHist: ['开门检查记录', '历史检查单与异常'], job: ['执行作业', '投入 → 产出 · 记录出成率'], batch: ['批次详情', '照片 · 有效到期 · 标签'], printer: ['标签打印', '企业微信蓝牙打印机'] };
  const tabs = [['stock', '库存', 'M4 5h16v15H4z M8 5V3h8v2 M4 10h16 M9 14h6'], ['inbound', '入库', 'M4 14v6h16v-6 M12 3v12 M7 10l5 5 5-5'], ['ops', '作业', 'M14 5l5 5 M3 21l8-8 M9 3l-6 6 6 6 6-6z M16 15l5 6'], ['check', '开门检查', 'M8 4H5v17h14V4h-3 M8 2h8v5H8z M8 13l3 3 5-6'], ['serve', '出餐', 'M3 20h18 M5 17a7 7 0 0114 0 M12 7V4 M10 4h4'], ['more', '更多', 'M5 5h4v4H5z M15 5h4v4h-4z M5 15h4v4H5z M15 15h4v4h-4z']];
  const menus = [['areas', '存储区域', '两级区域 · 照片 · 检查项'], ['tools', '餐饮工具', '状态 · 位置 · 日志'], ['supplies', '餐饮物资', '包装入库 · 领用 · 报损'], ['cats', '食材分类', '分类属性 · 保质期规则'], ['routes', '进货规格与链路', '食材档案 · 形态与作业 · 条码'], ['low', '用量预警', '比例或固定值'], ['prep', '半成品制作', '多原料作业与配方'], ['recipe', '菜品配方', '按规格维护用料'], ['count', '盘点核对', '只盘出品态余量'], ['dash', '数据看板', '损耗 · 周转 · 报表'], ['printer', '标签打印机', '连接 · 断开 · 食材标签']];
  const state = { actor: null, categories: [], items: [], units: [], route: null, screen: 'stock', params: new URLSearchParams(), generation: 0, drafts: [], masterLoaded: false };
  let toastTimer, sdkPromise, writing = false, ocrReturnFocus;
  const api = FnbClient.create({ onExpired: () => { state.actor = null; FnbOcr.stop(); closeModal(); if (/wxwork/i.test(navigator.userAgent)) location.replace(api.oauthUrl(location)); else loginHint('会话已失效，请从企业微信重新进入'); } });
  const manager = () => Boolean(state.actor && state.actor.isManager);
  const attr = (name, value) => ' data-' + name + '="' + esc(value) + '"';
  function button(text, action, id, cls, disabled) { return '<button type="button" class="' + (cls || 'btn') + '"' + attr('action', action) + (id == null ? '' : attr('id', id)) + (disabled ? ' disabled' : '') + '>' + esc(text) + '</button>'; }
  const empty = text => '<div class="empty">' + esc(text) + '</div>';
  const badge = (text, color) => '<span class="badge ' + (color || '') + '">' + esc(text) + '</span>';
  const note = text => '<p class="section-note">' + esc(text) + '</p>';
  function field(name, label, value, type, extra) { return '<label class="field"><span>' + esc(label) + '</span><input name="' + name + '" type="' + (type || 'text') + '" value="' + esc(value) + '" ' + (extra || '') + '></label>'; }
  function select(name, label, entries, value, extra) { return '<label class="field"><span>' + esc(label) + '</span><select name="' + name + '" ' + (extra || '') + '>' + entries.map(e => '<option value="' + esc(e[0]) + '"' + (String(e[0]) === String(value) ? ' selected' : '') + '>' + esc(e[1]) + '</option>').join('') + '</select></label>'; }
  function check(name, text, value) { return '<label class="field check"><input type="checkbox" name="' + name + '"' + (value ? ' checked' : '') + '><span>' + esc(text) + '</span></label>'; }
  const num = (f, k, fallback) => { const v = f.get(k); return v === '' || v == null ? fallback : Number(v); };
  const text = (f, k) => String(f.get(k) || '').trim();
  const storeOptions = Object.entries(storageNames);
  const categories2 = () => state.categories.filter(c => c.level === 2 && c.valid);
  const catOptions = () => categories2().map(c => [c.id, c.name]);
  const itemOptions = () => state.items.map(r => [r.item.id, r.item.name]);
  const beforeSave = '<p class="form-error" role="alert" hidden></p>';
  function saveButton(label) { return '<button type="submit" class="btn full">' + esc(label || '保存') + '</button>'; }
  function toast(message) { $('toast').textContent = message; $('toast').hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => { $('toast').hidden = true; }, 2800); }
  function loginHint(message) { $('tabs').hidden = true; $('identity').textContent = '未登录'; $('page-subtitle').textContent = '企业微信员工入口'; $('page').innerHTML = '<section class="card"><h2>无法进入系统</h2><p class="small">' + esc(message) + '</p><div class="actions">' + button('重新进入', 'login', null, 'btn') + '</div></section>'; }
  function openModal(title, html) { $('modal').innerHTML = '<section class="dialog" role="dialog" aria-modal="true" aria-labelledby="modal-title"><header><h2 id="modal-title">' + esc(title) + '</h2><button type="button" class="close" data-action="close-modal" aria-label="关闭">×</button></header>' + html + '</section>'; $('modal').hidden = false; $('modal').querySelector('input:not([type=hidden]),select,button').focus(); }
  function closeModal() { $('modal').hidden = true; $('modal').innerHTML = ''; }
  function navigate(screen, params) { if (writing) { toast('正在保存，请稍候'); return; } location.hash = screen + (params ? '?' + new URLSearchParams(params) : ''); }
  function footer() { const group = state.screen === 'expiry' || state.screen === 'destroy' || state.screen === 'batch' ? 'stock' : state.screen === 'job' ? 'ops' : state.screen === 'checkHist' ? 'check' : tabs.some(t => t[0] === state.screen) ? state.screen : 'more'; $('tabs').innerHTML = tabs.map(t => '<button type="button" class="tab" data-action="navigate" data-id="' + t[0] + '"' + (group === t[0] ? ' aria-current="page"' : '') + '><svg viewBox="0 0 24 24" aria-hidden="true"><path d="' + t[2] + '"></path></svg><span>' + t[1] + '</span></button>').join(''); $('tabs').hidden = false; }
  async function loadMaster(force) {
    if (state.masterLoaded && !force) return;
    const results = await Promise.all([api.request('FnbCatalog', 'ListCategories', { params: { includeDisabled: true } }), api.listItems(), api.request('FnbCatalog', 'ListUnits')]);
    state.categories = results[0]; state.items = results[1]; state.units = results[2]; state.masterLoaded = true;
  }
  async function renderPage(force) {
    if (!state.actor) return;
    FnbOcr.stop(); closeModal();
    const generation = ++state.generation, hash = location.hash.slice(1).split('?');
    state.screen = titles[hash[0]] ? hash[0] : 'stock'; state.params = new URLSearchParams(hash[1] || '');
    $('page-title').textContent = titles[state.screen][0]; $('page-subtitle').textContent = titles[state.screen][1]; footer();
    $('page').innerHTML = empty('正在加载…');
    try {
      await loadMaster(force);
      const operationData = await operations.load(state.screen, state.params);
      let route = null;
      if (state.screen === 'routes' && state.items.length) {
        const id = state.params.get('item') || state.items[0].item.id;
        route = await api.request('FnbRoute', 'GetRoute', { params: { itemId: id } });
      }
      if (generation !== state.generation) return;
      operations.setModel(operationData);
      state.route = route; $('page').innerHTML = views[state.screen](); $('page').scrollTop = 0;
      if (state.screen === 'inbound') await inboundSelection();
      if (state.screen === 'printer') updatePrinterState();
    } catch (e) { if (generation === state.generation && state.actor) $('page').innerHTML = '<section class="card">' + empty(e.message) + button('重新加载', 'refresh', null, 'btn full') + '</section>'; }
  }
  function back(screen) { return button('‹ 返回' + (titles[screen] ? titles[screen][0] : ''), 'navigate', screen, 'back'); }
  function quick(title, subtitle, screen, square) { return '<button type="button" class="card quick" data-action="navigate" data-id="' + screen + '"><span class="square">' + esc(square || '—') + '</span><span class="grow"><span class="strong">' + esc(title) + '</span><span class="hint" style="display:block">' + esc(subtitle) + '</span></span><span class="chevron">›</span></button>'; }
  function itemList(rows) { return rows.length ? rows.map(r => '<button type="button" class="menu list-row" data-action="item-route" data-id="' + esc(r.item.id) + '"><span class="thumb">' + esc(r.item.name.slice(0, 2)) + '</span><span class="grow"><span class="strong">' + esc(r.item.name) + '</span><span class="hint" style="display:block">' + esc(r.categoryName) + ' · ' + esc(unitNames[r.item.base_unit_code] || r.item.base_unit_code) + '</span></span>' + badge('已建档', 'blue') + '</button>').join('') : empty('还没有食材档案，先建分类，再创建食材'); }
  function more() { return '<div class="stack">' + menus.map(m => '<button type="button" class="menu" data-action="navigate" data-id="' + m[0] + '"><span class="grow"><span class="menu-title">' + m[1] + '</span><span class="hint" style="display:block">' + m[2] + '</span></span><span class="chevron">›</span></button>').join('') + '</div>'; }
  function cats() {
    return '<div class="stack">' + note('二级分类决定建议储存方式、默认保质期与临期阈值。入库仍可选择三种储存方式。')
      + (manager() ? button('+ 新增一级分类', 'new-category', null, 'btn secondary full') : note('员工可以查看分类，维护需店长。'))
      + state.categories.filter(c => c.level === 1).map(c => '<details class="card" open><summary>' + esc(c.name) + ' ' + (!c.valid ? badge('已停用') : '') + '</summary>'
        + (manager() ? '<div class="row">' + button('编辑', 'edit-category', c.id, 'link') + button('停用分类', 'disable-category', c.id, 'link', !c.valid) + '</div>' : '')
        + state.categories.filter(s => s.parent_id === c.id).map(s => '<div class="list-row"><div class="row between"><strong>' + esc(s.name) + '</strong>' + badge(s.valid ? s.is_prepared ? '半成品' : '启用' : '已停用', s.valid ? 'blue' : '') + '</div><p class="hint">' + esc(s.batch_code) + ' · ' + esc(measureNames[s.measure_type]) + ' · ' + esc(storageNames[s.default_storage]) + '</p><p class="hint">临期提醒 ' + esc(s.warn_days == null ? '跟随默认' : s.warn_days + ' 天') + ' · 开封/作业后保质 ' + esc(s.open_days == null ? '不改变到期' : s.open_days + ' 天') + '</p><div class="row wrap">' + button(manager() ? '分类属性' : '查看属性', 'edit-category', s.id, 'link') + button('保质期规则', 'category-rules', s.id, 'link') + (manager() ? button('停用', 'disable-category', s.id, 'link', !s.valid) : '') + '</div></div>').join('')
        + (manager() && c.valid ? button('+ 新增二级分类', 'new-subcategory', c.id, 'link') : '') + '</details>').join('')
      + (!state.categories.length ? empty('先创建一级分类，再添加二级分类') : '') + '</div>';
  }
  function routes() {
    const r = state.route;
    return '<div class="stack">' + note('先建出品态，再向上游添加形态。进货规格决定从哪一层入库，配方和出餐只扣出品态。')
      + '<div class="chips scroll">' + state.items.map(it => button(it.item.name, 'item-route', it.item.id, 'chip ' + (r && r.item.id === it.item.id ? 'on' : ''))).join('') + '</div>'
      + (manager() ? button('+ 新建食材档案', 'new-item', null, 'btn secondary full', !categories2().length) : '')
      + (!categories2().length ? '<section class="card">' + empty('建食材前需要二级分类') + button('去分类页添加', 'navigate', 'cats', 'btn full') + '</section>' : '')
      + (!r ? empty('还没有食材档案') : '<section class="card"><div class="row between"><h2>' + esc(r.item.name) + '</h2>' + badge(r.item.item_type === 'prepared' ? '半成品' : '普通食材', 'blue') + '</div><p class="hint">临期提前 ' + esc(r.defaults.warnDays) + ' 天 · 开封/作业后保质 ' + esc(r.defaults.openDays == null ? '不改变到期' : r.defaults.openDays + ' 天') + '</p><div class="row wrap">' + (manager() ? button('食材覆盖属性', 'item-defaults', r.item.id, 'link') : '') + button('食材保质期规则', 'item-rules', r.item.id, 'link') + '</div><hr class="divider"><h3>形态与作业</h3>'
        + r.forms.map((f, i) => (i ? '<div class="chain-op">↓ ' + esc(f.in_op_name) + '<p>换算 1 ' + esc(r.forms[i - 1].unit_name) + ' = ' + esc(f.in_op_ratio) + ' ' + esc(f.unit_name) + ' · 标准出成率 ' + esc(percent(f.in_op_yield)) + ' · 耗时 ' + esc(f.in_op_hours) + ' h</p></div>' : '')
          + '<div class="chain-form"><div class="row between"><strong>' + esc(f.name) + '</strong>' + badge(i === r.forms.length - 1 ? '出品态' : i === 0 ? '采购态' : '中间态', i === r.forms.length - 1 ? 'green' : 'blue') + '</div><p class="hint">' + esc(f.unit_name) + ' · ' + esc(storageNames[f.storage_type]) + ' · 编码 ' + esc(f.form_code) + '</p><p class="hint">每单位基本量 ' + esc(f.per_base) + ' ' + esc(unitNames[r.item.base_unit_code]) + '</p>' + (manager() ? '<div class="row">' + button('编辑', 'edit-form', f.id, 'link') + (i === 0 && r.forms.length > 1 ? button('移除上游形态', 'remove-form', f.id, 'link') : '') + '</div>' : '') + '</div>').join('')
        + (manager() ? '<div class="actions">' + button('+ 在上游加一个形态', 'new-form', r.item.id, 'btn secondary full') + '</div>' : '')
        + '</section><section class="card"><div class="row between"><h2>进货规格 ' + r.specs.length + '</h2>' + badge('编辑需店长') + '</div>'
        + r.specs.map(s => '<div class="list-row"><div class="row between"><strong>' + esc(s.name) + '</strong>' + badge(s.valid ? '启用' : '已停用', s.valid ? 'blue' : '') + '</div><p class="hint">' + esc(s.pack_desc || '未填写包装说明') + '</p><p class="hint">入口：' + esc((r.forms.find(f => f.id === s.entry_form_id) || {}).name || '已停用形态') + '</p><p class="hint">条码 ' + esc(s.barcode || '未填写') + '</p>' + (manager() ? '<div class="row">' + button('编辑', 'edit-spec', s.id, 'link') + button(s.valid ? '停用' : '启用', 'toggle-spec', s.id, 'link') + '</div>' : '') + '</div>').join('')
        + (!r.specs.length ? empty('还没有进货规格') : '') + (manager() ? button('+ 新增进货规格', 'new-spec', r.item.id, 'link') : '') + '<p class="hint">有库存的规格只能停用，修改限制由服务器校验。</p></section>') + '</div>';
  }
  const percent = value => value == null ? '—' : new Intl.NumberFormat('zh-CN', { style: 'percent', maximumFractionDigits: 4 }).format(value);
  function categoryEditor(c, parent) {
    const level = c ? c.level : parent ? 2 : 1, disabled = manager() ? '' : 'disabled';
    const entry = c || { id: 0, parent_id: parent || null, level: level, name: '', batch_code: '', measure_type: 'weight', default_storage: 'chilled', warn_days: null, open_days: null, is_prepared: false, valid: true, sort: 0 };
    openModal(level === 1 ? '一级分类' : '二级分类属性', '<form data-form="category" data-id="' + entry.id + '" data-parent="' + esc(entry.parent_id) + '" data-level="' + level + '">' + field('name', '分类名称', entry.name, 'text', 'required maxlength="100" ' + disabled)
      + (level === 2 ? field('batchCode', '批次号编码（大写字母 / 数字）', entry.batch_code, 'text', 'required pattern="[A-Za-z0-9]{1,16}" maxlength="16" ' + disabled)
        + select('measureType', '计量方式', Object.entries(measureNames), entry.measure_type, disabled) + select('defaultStorage', '建议储存方式', storeOptions, entry.default_storage, disabled)
        + '<div class="form-grid">' + field('warnDays', '临期提前提醒（天，空白跟默认）', entry.warn_days, 'number', 'min="0" max="36500" step="1" ' + disabled) + field('openDays', '开封 / 作业后保质（天空白不改变）', entry.open_days, 'number', 'min="0" max="36500" step="1" ' + disabled) + '</div>' + check('isPrepared', '半成品分类', entry.is_prepared) : '')
      + field('sort', '排序', entry.sort, 'number', 'step="1" ' + disabled) + check('valid', '启用分类', entry.valid) + beforeSave + (manager() ? saveButton() : '') + '</form>');
    if (!manager()) $('modal').querySelectorAll('input,select').forEach(e => { e.disabled = true; });
  }
  function itemEditor() {
    openModal('新建食材档案', '<form data-form="item">' + select('categoryId', '二级分类', catOptions(), catOptions()[0] && catOptions()[0][0], 'required')
      + '<div class="inline-input">' + field('name', '食材名称', '', 'text', 'required maxlength="100"') + button('识别', 'ocr-name', 'name', 'btn compact secondary') + '</div>'
      + field('finalFormName', '出品态名称（配方实际使用的形态）', '', 'text', 'required maxlength="100"')
      + '<div class="form-grid">' + select('baseUnitCode', '基本单位', [['g', 'g'], ['ml', 'ml'], ['piece', '个']], 'g') + field('finalFormCode', '出品态编码', 'F', 'text', 'required pattern="[A-Za-z0-9]{1,16}" maxlength="16"') + '</div>'
      + select('storageType', '储存方式', [['', '跟随分类建议']].concat(storeOptions), '') + '<label class="field file-picker"><span>食材照片（选填）</span><span class="btn secondary">拍照 / 上传<input class="file-input" type="file" accept="image/*" data-item-photo aria-label="食材照片"></span></label><input type="hidden" name="imageId"><div id="item-photo" class="photo-picker"></div>'
      + '<p class="hint">先建出品态，再逐层往上游添加采购态和作业。</p>' + beforeSave + saveButton('创建食材') + '</form>');
    syncItemUnit();
  }
  function syncItemUnit() {
    const form = $('modal').querySelector('[data-form=item]'); if (!form) return;
    const category = state.categories.find(c => String(c.id) === form.elements.categoryId.value);
    const codes = category ? state.units.filter(u => ['g', 'ml', 'piece'].includes(u.code) && u.dimension === ({ weight: 1, volume: 2, count: 3 })[category.measure_type]) : [];
    form.elements.baseUnitCode.innerHTML = codes.map(u => '<option value="' + esc(u.code) + '">' + esc(u.name) + '</option>').join('');
  }
  function defaultsEditor() {
    const item = state.route.item;
    openModal('食材覆盖属性', '<form data-form="defaults" data-id="' + item.id + '">' + field('warnDays', '临期提前提醒（天）', item.warn_days, 'number', 'min="0" max="36500" step="1"') + field('openDays', '开封 / 作业后保质（天）', item.open_days, 'number', 'min="0" max="36500" step="1"') + '<p class="hint">留空恢复分类默认值，0 是有效设置。保存不改变历史批次。</p>' + beforeSave + saveButton() + '</form>');
  }
  function formEditor(f) {
    const r = state.route, top = r.forms[0], isFinal = f && f.seq === r.forms[r.forms.length - 1].seq;
    openModal(f ? '编辑形态' : '在「' + top.name + '」上游加一个形态', '<form data-form="form" data-id="' + (f ? f.id : 0) + '">' + field('name', '形态名称', f ? f.name : '', 'text', 'required maxlength="100"')
      + '<div class="form-grid">' + field('unitName', '计量单位（箱 / 桶 / 袋等）', f ? f.unit_name : '', 'text', 'required maxlength="16" ' + (isFinal ? 'readonly' : '')) + field('perBase', '每 1 单位含基本量（' + esc(unitNames[r.item.base_unit_code]) + '）', f ? f.per_base : '', 'number', 'required min="0.000001" step="0.000001" ' + (isFinal ? 'readonly' : '')) + '</div>'
      + select('storageType', '储存方式', storeOptions, f ? f.storage_type : top.storage_type) + field('formCode', '形态编码（批次号后缀）', f ? f.form_code : '', 'text', 'required maxlength="16" pattern="[A-Za-z0-9]{1,16}"')
      + field('shelfAfterOpDays', '作业后保质（天，留空沿用）', f ? f.shelf_after_op_days : null, 'number', 'min="0" max="36500" step="1"')
      + ((!f || f.seq > 0) ? '<hr class="divider"><h3>↓ ' + esc(f ? '到达本形态的作业' : '转成「' + top.name + '」的作业') + '</h3>' + field('opName', '作业名称', f ? f.in_op_name : '', 'text', 'required maxlength="100"') + '<div class="form-grid">' + field('standardYield', '标准出成率（%）', f ? FnbClient.percentInput(f.in_op_yield) : 100, 'number', 'required min="0.0001" max="100" step="0.0001"') + field('durationHours', '耗时（h）', f ? f.in_op_hours : 0, 'number', 'required min="0" step="0.000001"') + '</div>' : '')
      + '<p class="hint">填写每个形态单位的基本量，相邻换算由服务器返回。实际作业产出以称重或点数为准。</p>' + beforeSave + saveButton(f ? '保存形态' : '加入链路') + '</form>');
  }
  function specEditor(s) {
    const r = state.route;
    openModal(s ? '编辑进货规格' : '新增进货规格', '<form data-form="spec" data-id="' + (s ? s.id : 0) + '">' + field('name', '规格名称', s ? s.name : '', 'text', 'required maxlength="100"') + field('packDesc', '包装说明', s && s.pack_desc, 'text', 'maxlength="200"') + field('brand', '品牌（选填）', s && s.brand, 'text', 'maxlength="100"')
      + '<div class="inline-input">' + field('barcode', '商品条码（选填）', s && s.barcode, 'text', 'maxlength="100"') + button('扫一扫', 'scan-barcode', 'barcode', 'btn compact secondary') + '</div>'
      + select('entryFormId', '入口形态（从这一层入库）', r.forms.map(f => [f.id, f.name + ' · ' + f.unit_name]), s ? s.entry_form_id : r.forms[0].id)
      + field('sort', '排序', s ? s.sort : 0, 'number', 'step="1"') + check('valid', '启用规格', s ? s.valid : true) + '<p class="hint">有库存或入库记录的规格只能停用。条码在启用规格中全局唯一。</p>' + beforeSave + saveButton('完成') + '</form>');
  }
  async function rulesEditor(kind, id) {
    const params = { includeDisabled: true }; params[kind + 'Id'] = id;
    const rows = await api.request('FnbCatalog', 'ListShelfRules', { params: params });
    openModal(kind === 'category' ? '分类保质期规则' : '食材保质期规则', '<p class="hint">生产月份 6–9 月采用暖季，其余月份为冷季。回退顺序由服务器执行。</p>'
      + rows.map(r => '<div class="list-row"><div class="row between"><span>' + esc(storageNames[r.storage_type]) + ' · ' + esc({ all: '全年', warm: '暖季 6–9 月', cold: '冷季' }[r.season]) + '</span>' + badge(r.valid ? r.days + ' 天' : '已停用', r.valid ? 'blue' : '') + '</div>' + (manager() ? button('编辑规则', 'edit-rule', r.id, 'link') : '') + '</div>').join('')
      + (!rows.length ? empty('暂未配置保质期规则') : '') + (manager() ? button('+ 新增规则', 'new-rule', null, 'btn secondary full') : ''));
    $('modal')._rules = { kind: kind, id: id, rows: rows };
  }
  function ruleForm(rule) {
    const context = $('modal')._rules; if (!context) return;
    openModal('保质期规则', '<form data-form="rule" data-kind="' + context.kind + '" data-owner="' + context.id + '" data-id="' + (rule ? rule.id : 0) + '">' + select('storageType', '储存方式', storeOptions, rule ? rule.storage_type : 'chilled') + select('season', '生产月份分档', [['all', '全年'], ['warm', '暖季（6–9 月）'], ['cold', '冷季（其他月份）']], rule ? rule.season : 'all') + field('days', '保质天数', rule ? rule.days : '', 'number', 'required min="1" max="36500" step="1"') + check('valid', '启用规则', rule ? rule.valid : true) + beforeSave + saveButton() + '</form>');
  }
  function draftRows() { return state.drafts.length ? state.drafts.map((d, i) => '<div class="draft-row"><div class="row between"><strong>' + esc(d.itemName) + '</strong>' + button('移除', 'remove-draft', i, 'link') + '</div><p class="hint">' + esc(d.specName || d.packMode) + ' · ' + esc(d.quantity) + ' ' + esc(d.unitName) + ' · ' + esc(storageNames[d.storageType]) + '</p><p class="hint">' + esc(d.batchNo || '批次号待生成') + ' · ' + esc(d.expireDate || '到期待确认') + '</p></div>').join('') : empty('还没有待提交的入库条目'); }
  let inboundToken = 0, inboundRoute;
  async function inboundSelection() {
    const form = $('inbound-form'); if (!form) return;
    const token = ++inboundToken, id = form.elements.itemId.value; inboundRoute = null;
    if (!id) { $('inbound-specs').innerHTML = empty('先选择食材'); return; }
    $('inbound-specs').innerHTML = empty('正在读取进货规格…');
    try {
      const r = await api.request('FnbRoute', 'GetRoute', { params: { itemId: id } });
      if (token !== inboundToken || !$('inbound-form') || $('inbound-form').elements.itemId.value !== String(id)) return;
      inboundRoute = r; form.elements.storageType.value = r.defaults.storageType;
      const specs = r.specs.filter(s => s.valid);
      $('inbound-specs').innerHTML = specs.length ? select('specId', '进货规格', specs.map(s => [s.id, s.name + (s.pack_desc ? ' · ' + s.pack_desc : '')]), specs[0].id, 'required')
        : r.forms.length > 1 ? '<p class="form-error">这个食材已配置链路，请先添加启用的进货规格。</p>' : select('packMode', '入库包装', [['bulk', '散装（入库即出品态）'], ['sealed', '封装（开封后使用）']], 'bulk') + field('packSize', '每件含量（封装时填写）', '', 'number', 'min="0.000001" step="0.000001"') + field('packLabel', '包装说明', '', 'text', 'maxlength="100"') + select('openedStorage', '开封后储存方式', storeOptions, r.defaults.storageType) + field('openedDays', '开封后保质（天空白跟随默认）', r.defaults.openDays, 'number', 'min="0" max="36500" step="1"');
      $('inbound-specs').innerHTML += button('维护进货规格与链路 ›', 'item-route', id, 'link'); updateInboundUnit();
      await operations.selected();
    } catch (e) { if (token === inboundToken && $('inbound-specs')) $('inbound-specs').innerHTML = '<p class="form-error">' + esc(e.message) + '</p>'; }
  }
  function updateInboundUnit() {
    const f = $('inbound-form'); if (!f || !inboundRoute) return;
    const s = inboundRoute.specs.find(s => String(s.id) === (f.elements.specId && f.elements.specId.value)), form = s ? inboundRoute.forms.find(x => x.id === s.entry_form_id) : inboundRoute.forms[inboundRoute.forms.length - 1];
    const sealed = !s && f.elements.packMode && f.elements.packMode.value === 'sealed';
    f.elements.quantity.step = sealed ? '1' : '0.000001'; f.elements.quantity.min = sealed ? '1' : '0.000001';
    $('inbound-unit').textContent = form ? '数量单位：' + (sealed ? '件（封装整件数量）' : form.unit_name) + '；换算与预计入库量等待服务器预览' : '请选择进货规格';
  }
  let printerLogs = [];
  function updatePrinterState(message) { if ($('printer-state')) { const connection = window.BlePrint && BlePrint.getConnection(); $('printer-state').textContent = message || (connection ? '已连接 ' + connection.name : '尚未连接'); } if ($('printer-log')) $('printer-log').textContent = printerLogs.join('\n'); }
  function sdkReady() {
    if (!/wxwork/i.test(navigator.userAgent)) return Promise.reject(new Error('请在企业微信中使用扫一扫与蓝牙打印'));
    if (!sdkPromise) sdkPromise = BlePrint.register(message => { printerLogs.push(message); printerLogs = printerLogs.slice(-100); updatePrinterState(); }).then(ok => { if (!ok) throw new Error('企业微信初始化失败，请重新进入后重试'); return true; }).catch(e => { sdkPromise = null; throw e; });
    return sdkPromise;
  }
  async function scanCode() { await sdkReady(); const result = await ww.scanQRCode({ needResult: 1, scanType: ['barCode', 'qrCode'] }); const raw = String(result.resultStr || ''); return raw.replace(/^(?:CODE_\d+|EAN_\d+|UPC_A),/, ''); }
  async function lookupBarcode(scan) {
    const f = $('inbound-form'); if (!f) return;
    const value = scan ? await scanCode() : f.elements.barcodeLookup.value.trim(); if (!value) throw new Error('请扫描或输入条码');
    f.elements.barcodeLookup.value = value;
    const result = await api.request('FnbRoute', 'FindSpecByBarcode', { params: { barcode: value } });
    f.elements.categoryId.value = ''; f.elements.itemId.innerHTML = '<option value="">请选择食材</option>' + itemOptions().map(x => '<option value="' + esc(x[0]) + '">' + esc(x[1]) + '</option>').join(''); f.elements.itemId.value = result.item.id;
    await inboundSelection(); if (f.elements.specId) f.elements.specId.value = result.spec.id; updateInboundUnit(); toast('已选中 ' + result.spec.name);
  }
  async function photo(input, targetId) {
    const file = input.files && input.files[0]; if (!file) return;
    if (!file.type.startsWith('image/')) throw new Error('请选择图片文件');
    if (file.size > 10 * 1024 * 1024) throw new Error('照片请控制在 10 MB 内');
    const target = $(targetId), form = input.closest('form'); target.innerHTML = '<p class="hint">正在上传…</p>'; input.disabled = true; form.dataset.uploading = 'true';
    try { const result = await api.upload(file); if (!form.isConnected) return; form.elements.imageId.value = result.id; const img = document.createElement('img'); img.alt = '已上传照片'; img.src = imageUrl(result.file_path_name); target.replaceChildren(img); }
    catch (e) { if (target.isConnected) target.textContent = e.message; throw e; }
    finally { delete form.dataset.uploading; if (input.isConnected) input.disabled = false; }
  }
  function imageUrl(path) { const url = new URL(path, 'https://img.snowmeet.top'); return url.protocol === 'https:' || url.protocol === 'http:' ? url.href : ''; }
  async function submit(form) {
    if (writing) return;
    if (form.dataset.uploading) throw new Error('照片正在上传，请稍候');
    if (await operations.submit(form)) return;
    const kind = form.dataset.form, f = new FormData(form), id = Number(form.dataset.id || 0);
    if (kind !== 'inbound-draft' && !manager()) throw new Error('维护资料需店长权限');
    let controller = 'FnbRoute', action, body;
    if (kind === 'category') { controller = 'FnbCatalog'; action = 'SaveCategory'; const level = Number(form.dataset.level); body = { id: id, parentId: level === 2 ? Number(form.dataset.parent) : null, level: level, name: text(f, 'name'), sort: num(f, 'sort', 0), valid: f.has('valid') }; if (level === 2) Object.assign(body, { batchCode: text(f, 'batchCode'), measureType: text(f, 'measureType'), defaultStorage: text(f, 'defaultStorage'), warnDays: num(f, 'warnDays', null), openDays: num(f, 'openDays', null), isPrepared: f.has('isPrepared') }); }
    else if (kind === 'item') { action = 'CreateItem'; body = { categoryId: num(f, 'categoryId'), name: text(f, 'name'), finalFormName: text(f, 'finalFormName'), finalFormCode: text(f, 'finalFormCode'), baseUnitCode: text(f, 'baseUnitCode'), storageType: text(f, 'storageType') || null, imageId: num(f, 'imageId', null) }; }
    else if (kind === 'defaults') { action = 'SaveItemDefaults'; body = { itemId: id, warnDays: num(f, 'warnDays', null), openDays: num(f, 'openDays', null) }; }
    else if (kind === 'form') { action = id ? 'UpdateForm' : 'AddUpstreamForm'; body = { itemId: state.route.item.id, name: text(f, 'name'), unitName: text(f, 'unitName'), perBase: num(f, 'perBase'), storageType: text(f, 'storageType'), formCode: text(f, 'formCode'), shelfAfterOpDays: num(f, 'shelfAfterOpDays', null), opName: text(f, 'opName') || null, standardYield: FnbClient.percentFraction(num(f, 'standardYield', 100)), durationHours: num(f, 'durationHours', 0) }; if (id) body.id = id; }
    else if (kind === 'spec') { action = 'SaveSpec'; body = { id: id, itemId: state.route.item.id, entryFormId: num(f, 'entryFormId'), name: text(f, 'name'), brand: text(f, 'brand') || null, packDesc: text(f, 'packDesc') || null, barcode: text(f, 'barcode') || null, sort: num(f, 'sort', 0), valid: f.has('valid') }; }
    else if (kind === 'rule') { controller = 'FnbCatalog'; action = 'SaveShelfRule'; body = { id: id, storageType: text(f, 'storageType'), season: text(f, 'season'), days: num(f, 'days'), valid: f.has('valid') }; body[form.dataset.kind + 'Id'] = Number(form.dataset.owner); }
    else if (kind === 'inbound-draft') { if (!inboundRoute) throw new Error('请先选择食材'); const spec = inboundRoute.specs.find(s => String(s.id) === text(f, 'specId')); if (inboundRoute.forms.length > 1 && !spec) throw new Error('请先配置并选择启用进货规格'); if (text(f, 'packMode') === 'sealed' && !(num(f, 'packSize', 0) > 0)) throw new Error('请填写每件含量'); const entry = spec ? inboundRoute.forms.find(x => x.id === spec.entry_form_id) : inboundRoute.forms[inboundRoute.forms.length - 1]; state.drafts.push(Object.assign(Object.fromEntries(f), { itemName: inboundRoute.item.name, specName: spec && spec.name, unitName: !spec && text(f, 'packMode') === 'sealed' ? '件' : entry.unit_name })); $('inbound-drafts').innerHTML = draftRows(); toast('已加入待提交清单，尚未入库'); return; }
    else return;
    writing = true; const buttons = form.querySelectorAll('button'); buttons.forEach(b => { b.disabled = true; }); const error = form.querySelector('.form-error'); if (error) error.hidden = true;
    try { const result = await api.request(controller, action, { body: body }); if (kind === 'item') location.hash = 'routes?item=' + result.id; closeModal(); writing = false; await renderPage(true); toast('已保存'); }
    catch (e) { if (error && form.isConnected) { error.textContent = e.message; error.hidden = false; } else toast(e.message); }
    finally { writing = false; if (form.isConnected) buttons.forEach(b => { b.disabled = false; }); }
  }
  async function handleAction(buttonElement) {
    const action = buttonElement.dataset.action, id = buttonElement.dataset.id;
    if (writing && action !== 'close-modal') { toast('正在保存，请稍候'); return; }
    if (await operations.action(buttonElement)) return;
    if (action === 'close-modal') { if (!writing) closeModal(); return; }
    if (action === 'navigate') { navigate(id); return; }
    if (action === 'item-route') { navigate('routes', { item: id }); return; }
    if (action === 'refresh') { await renderPage(true); return; }
    if (action === 'login') { if (/wxwork/i.test(navigator.userAgent)) location.replace(api.oauthUrl(location)); else toast('请在企业微信中打开本页面'); return; }
    if (action === 'new-category') categoryEditor();
    else if (action === 'new-subcategory') categoryEditor(null, Number(id));
    else if (action === 'edit-category') categoryEditor(state.categories.find(c => String(c.id) === id));
    else if (action === 'category-rules') await rulesEditor('category', Number(id));
    else if (action === 'item-rules') await rulesEditor('item', Number(id));
    else if (action === 'new-rule') ruleForm();
    else if (action === 'edit-rule') ruleForm($('modal')._rules.rows.find(r => String(r.id) === id));
    else if (action === 'new-item') itemEditor();
    else if (action === 'item-defaults') defaultsEditor();
    else if (action === 'new-form') formEditor();
    else if (action === 'edit-form') formEditor(state.route.forms.find(f => String(f.id) === id));
    else if (action === 'new-spec') specEditor();
    else if (action === 'edit-spec') specEditor(state.route.specs.find(s => String(s.id) === id));
    else if (action === 'disable-category') {
      if (!manager()) throw new Error('需店长权限'); if (!confirm('停用这个分类？一级分类会连同下级停用；有关联食材时服务器会拒绝。')) return;
      await mutation('FnbCatalog', 'DeleteCategory', { id: Number(id) });
    } else if (action === 'remove-form') {
      if (!manager()) throw new Error('需店长权限'); if (!confirm('移除这个上游形态？已被规格、批次或流水引用时服务器会拒绝。')) return;
      await mutation('FnbRoute', 'RemoveForm', { itemId: state.route.item.id, id: Number(id) });
    } else if (action === 'toggle-spec') {
      if (!manager()) throw new Error('需店长权限'); const s = state.route.specs.find(x => String(x.id) === id);
      if (!confirm((s.valid ? '停用' : '启用') + '「' + s.name + '」？')) return;
      await mutation('FnbRoute', 'SaveSpec', { id: s.id, itemId: s.item_id, entryFormId: s.entry_form_id, name: s.name, brand: s.brand, packDesc: s.pack_desc, barcode: s.barcode, sort: s.sort, valid: !s.valid });
    } else if (action.startsWith('ocr-')) {
      const form = buttonElement.closest('form'); if (!form) return; const target = form.elements[id]; ocrReturnFocus = buttonElement;
      await FnbOcr.start({ api: api, mode: { 'ocr-name': 'name', 'ocr-date': 'date', 'ocr-expire': 'expire', 'ocr-shelf': 'shelf' }[action], onPick: value => { if (!form.isConnected) return; if (typeof value === 'object') { target.value = value.value; form.elements.shelfUnit.value = value.unit; } else target.value = value; target.dispatchEvent(new Event('change', { bubbles: true })); } });
    } else if (action === 'scan-barcode') { const input = buttonElement.closest('form').elements[id]; input.value = await scanCode(); }
    else if (action === 'lookup-barcode' || action === 'lookup-scan') await lookupBarcode(action === 'lookup-scan');
    else if (action === 'remove-draft') { state.drafts.splice(Number(id), 1); $('inbound-drafts').innerHTML = draftRows(); }
    else if (action === 'connect-printer') { buttonElement.disabled = true; try { await window.FnbPrinterReady; await sdkReady(); await BlePrint.connect(message => updatePrinterState(message)); updatePrinterState(); } finally { buttonElement.disabled = false; } }
    else if (action === 'disconnect-printer') { await BlePrint.disconnect(); updatePrinterState(); }
  }
  async function mutation(controller, action, body) { if (writing) return; writing = true; try { await api.request(controller, action, { body: body }); writing = false; await renderPage(true); toast('已保存'); } finally { writing = false; } }
  const operations = FnbOperations.create({ api, state, esc, field, select, check, button, empty, note, badge, manager,
    openModal, toast, navigate, catOptions, draftRows, photo, imageUrl, sdkReady, updatePrinterState,
    inboundRoute: () => inboundRoute, isWriting: () => writing, setWriting: value => { writing = value; }, render: renderPage });
  const views = Object.assign({ more, cats, routes }, operations.views);
  document.addEventListener('click', e => { const buttonElement = e.target.closest('[data-action]'); if (buttonElement && !buttonElement.disabled) handleAction(buttonElement).catch(e => toast(e.message)); });
  document.addEventListener('submit', e => { if (!e.target.dataset.form) return; e.preventDefault(); submit(e.target).catch(error => { const alert = e.target.querySelector('.form-error'); if (alert) { alert.textContent = error.message; alert.hidden = false; } else toast(error.message); }); });
  document.addEventListener('input', e => operations.input(e));
  document.addEventListener('change', e => {
    if (e.target.hasAttribute('data-op-photo')) operations.photo(e.target).catch(e => toast(e.message));
    else if (e.target.hasAttribute('data-item-photo')) photo(e.target, 'item-photo').catch(e => toast(e.message));
    else if (e.target.hasAttribute('data-inbound-photo')) photo(e.target, 'inbound-photo').catch(e => toast(e.message));
    else if (e.target.name === 'categoryId' && e.target.closest('[data-form=item]')) syncItemUnit();
    else if (e.target.closest('#inbound-form') && e.target.name === 'categoryId') { const rows = state.items.filter(r => !e.target.value || String(r.item.category_id) === e.target.value); $('inbound-form').elements.itemId.innerHTML = '<option value="">请选择食材</option>' + rows.map(r => '<option value="' + r.item.id + '">' + esc(r.item.name) + '</option>').join(''); inboundSelection().catch(e => toast(e.message)); }
    else if (e.target.closest('#inbound-form') && e.target.name === 'itemId') inboundSelection().catch(e => toast(e.message));
    else if (e.target.name === 'specId' || e.target.name === 'packMode') updateInboundUnit();
    else if (e.target.closest('#inbound-form') && ['storageType', 'productionDate', 'expireDate'].includes(e.target.name)) operations.previewExpiry().catch(e => toast(e.message));
  });
  document.addEventListener('keydown', e => { if (e.key === 'Escape' && !writing) { FnbOcr.stop(); closeModal(); } if (e.key === 'Tab' && !$('modal').hidden) { const elements = [...$('modal').querySelectorAll('button:not(:disabled),input:not(:disabled):not([type=hidden]),select:not(:disabled),textarea:not(:disabled),a[href]')]; if (elements.length && e.shiftKey && document.activeElement === elements[0]) { e.preventDefault(); elements[elements.length - 1].focus(); } else if (elements.length && !e.shiftKey && document.activeElement === elements[elements.length - 1]) { e.preventDefault(); elements[0].focus(); } } });
  document.addEventListener('fnb-ocr-closed', () => { if (ocrReturnFocus && ocrReturnFocus.isConnected) ocrReturnFocus.focus(); });
  $('identity').onclick = () => { if (state.actor) openModal('当前员工与门店', '<p><strong>' + esc(state.actor.name) + '</strong> ' + badge(manager() ? '店长' : '员工', 'blue') + '</p><p class="hint">' + esc(state.actor.shopName) + '</p><p class="hint">权限来自员工在职信息及所属门店。</p>'); };
  window.addEventListener('hashchange', () => renderPage());
  window.addEventListener('pagehide', () => { FnbOcr.stop(); if (window.BlePrint) BlePrint.release(); });
  async function start() {
    try {
      state.actor = await api.authenticate(location, history);
      if (!state.actor) { if (/wxwork/i.test(navigator.userAgent)) location.replace(api.oauthUrl(location)); else loginHint('请在企业微信中打开本页面'); return; }
      $('identity').textContent = (manager() ? '店长 ' : '员工 ') + state.actor.name;
      const batchId = new URLSearchParams(location.search).get('id'); if (batchId && !location.hash) history.replaceState(null, '', location.pathname + location.search + '#batch?id=' + encodeURIComponent(batchId));
      await renderPage();
    } catch (e) { if (state.actor) toast(e.message); else loginHint(e.message); }
  }
  start();
})();
