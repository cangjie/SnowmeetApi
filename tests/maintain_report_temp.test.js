const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function page() {
    const html = fs.readFileSync(path.join(__dirname, '../wwwroot/background/maintain/maintain_report_temp.html'), 'utf8');
    const script = [...html.matchAll(/<script type="text\/javascript">([\s\S]*?)<\/script>/g)]
        .map(match => match[1]).find(text => text.includes('function download()'));
    function element() {
        return { innerText: '', children: [], value: '', disabled: false,
            set innerHTML(value) { this.children = []; },
            attributes: { setNamedItem() {} }, appendChild(child) { this.children.push(child); }, click() {} };
    }
    const elements = {};
    const body = { appendChild(child) { this.lastChild = child; }, removeChild() {} };
    const document = { body, getElementById(id) { return elements[id] ||= element(); },
        createElement: element, createAttribute() { return {}; } };
    let request;
    const errors = [];
    const context = { document, console: { log() {} }, alert: message => errors.push(message),
        sessionKey: 'test', startDate: '2026-11-08', endDate: '2026-11-08',
        formatDate: value => String(value).slice(0, 10), formatTime: value => String(value).slice(11, 19),
        formatAmount: value => Number(value).toFixed(2),
        $: { ajax(options) { request = options; } } };
    vm.createContext(context);
    vm.runInContext(script, context);
    return { context, elements, body, errors,
        query(orders) { context.getData(); request.success({ code: 0, data: orders }, 'success'); },
        fail() { context.getData(); request.error(); },
        url() { return request.url; } };
}

function order(code, tasks, extra = {}) {
    return { code, shop: '门店', customerCalledName: '顾客', customerCell: '',
        paidAmount: 100, refundAmount: 0, entertain: 0, biz_date: '2026-11-08T12:00:00',
        memo: '备注', staff: { name: '销售员' }, availablePayments: [],
        cares: [{ valid: 1, tasks }], ...extra };
}
function task(task_name, name, extra = {}) {
    return { task_name, valid: 1, staff: name ? { name } : null, ...extra };
}

test('动态步骤列覆盖所有可见订单和装备，按指定顺序排列并汇总去重执行人', () => {
    const p = page();
    const first = order('A', [task('发板', '甲'), task('热蜡', '乙'), task(' 修刃 ', '丙'),
        task('安全检查', '丁'), task('刮蜡', null), task('维修', '戊'), task('无效', '某', { valid: 0 })]);
    first.cares.push({ valid: 1, tasks: [task('热蜡', '乙'), task('热蜡', '己'), task('机打蜡', '庚')] });
    first.cares.push({ valid: 0, tasks: [task('无效装备', '某')] });
    const second = order('B', [task('寄存或快递', '辛'), task('修刃', '壬'), task('机打蜡', null)]);
    const hidden = order('C', [task('不显示', '某')], { paidAmount: 0 });
    p.query([first, second, hidden]);
    const head = Array.from(p.context.tHead);
    assert.deepEqual(head.slice(13), ['安全检查', '修刃', '机打蜡', '热蜡', '刮蜡', '寄存或快递', '维修', '发板']);
    const rows = Array.from(p.context.tData, row => Array.from(row));
    const at = (row, title) => row[head.indexOf(title)];
    assert.equal(rows.length, 2);
    assert.equal(at(rows[0], '开单'), '销售员');
    assert.equal(at(rows[0], '热蜡'), '乙、己');
    assert.equal(at(rows[0], '发板'), '甲');
    assert.equal(at(rows[0], '刮蜡'), '');
    assert.equal(at(rows[1], '安全检查'), '');
    assert.equal(at(rows[1], '修刃'), '壬');
    assert.deepEqual(p.elements.report_head.children.map(td => td.innerText), head);
    assert.ok(p.url().includes('includeCareTaskStaff=true'));
});

test('日期和时间位于订单号之前，页面数据和 CSV 列顺序一致', () => {
    const p = page();
    p.query([order('A', [task('修刃', '甲')])]);
    const head = Array.from(p.context.tHead);
    assert.deepEqual(head.slice(0, 4), ['序号', '日期', '时间', '订单号']);
    assert.deepEqual(Array.from(p.context.tData[0]).slice(0, 4), [1, '2026-11-08', '12:00:00', 'A']);
    assert.deepEqual(p.elements.report_rows.children[0].children.slice(0, 4).map(cell => cell.innerText),
        ['1', '2026-11-08', '12:00:00', 'A']);
    p.context.download();
    const csv = decodeURIComponent(p.body.lastChild.href.split(',').slice(1).join(','));
    assert.match(csv, /^序号,日期,时间,订单号,/);
    assert.match(csv, /\r\n1,2026-11-08,12:00:00,A,/);
});

test('重新查询清除旧步骤列，空结果及查询错误不会保留旧数据', () => {
    const p = page();
    p.query([order('A', [task('修刃', '甲')])]);
    p.query([order('B', [task('维修', '乙')])]);
    assert.equal(p.context.tHead.includes('修刃'), false);
    assert.equal(p.context.tHead.includes('维修'), true);
    p.query([]);
    assert.equal(p.context.tHead.length, 13);
    assert.equal(p.context.tData.length, 0);
    p.fail();
    assert.match(p.errors[0], /查询养护报表失败/);
});

test('下载列与页面一致并正确转义备注和执行人', () => {
    const p = page();
    p.query([order('A', [task('修刃', '张,"三"'), task('发板', '李')], { memo: '有,逗号' })]);
    p.context.download();
    const csv = decodeURIComponent(p.body.lastChild.href.split(',').slice(1).join(','));
    const [header, row] = csv.split('\r\n');
    assert.match(header, /备注,开单,修刃,发板$/);
    assert.match(row, /"有,逗号",销售员,"张,""三""",李$/);
});
