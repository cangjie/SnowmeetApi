const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function page() {
    const html = fs.readFileSync(path.join(__dirname, '../wwwroot/background/sale/sale_report_temp.html'), 'utf8');
    const headHtml = html.match(/<thead>([\s\S]*?)<\/thead>/)[1];
    const headers = [...headHtml.matchAll(/<td class="table_head">([^<]+)<\/td>/g)].map(m => m[1]);
    const script = [...html.matchAll(/<script type="text\/javascript">([\s\S]*?)<\/script>/g)]
        .map(m => m[1]).find(s => s.includes('function download()'));
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
    const context = { document, console: { log() {} }, sessionKey: 'test',
        startDate: '2026-11-01', endDate: '2026-11-08',
        formatDate: value => String(value).slice(0, 10), formatTime: value => String(value).slice(11, 19),
        formatAmount: value => Number(value).toFixed(2), $: { ajax(options) { request = options; } } };
    vm.createContext(context);
    vm.runInContext(script, context);
    return { context, elements, body, headers,
        query(orders) { context.getData(); request.success({ data: orders }, 'success'); } };
}

function order(code, extra = {}) {
    return { code, shop: '门店', customerCalledName: '顾客', customerCell: '13800000000',
        paidAmount: 100, refundAmount: 20, entertain: 0, biz_date: '2026-11-08T11:28:29',
        memo: '备注', staff: { name: '销售员' }, retails: [{ mi7_code: 'MI7' }],
        availablePayments: [{ status: '支付成功', pay_method: '微信支付' }], ...extra };
}

test('页面和数据列将日期时间放在订单号之前，七色米订单号及金额保持不变', () => {
    const p = page();
    p.query([order('ORDER')]);
    assert.deepEqual(p.headers, Array.from(p.context.tHead));
    assert.deepEqual(p.headers.slice(0, 5), ['序号', '日期', '时间', '订单号', '七色米订单号']);
    const values = p.elements.report_rows.children[0].children.map(td => td.innerText);
    assert.deepEqual(values.slice(0, 5), ['1', '2026-11-08', '11:28:29', 'ORDER', 'MI7']);
    assert.equal(values[p.headers.indexOf('订单金额')], '80.00');
    assert.equal(p.elements.total.innerText, '总计：80.00');
});

test('下载与页面列对齐，不保留旧订单并正确转义', () => {
    const p = page();
    p.query([order('OLD')]);
    p.query([order('NEW', { memo: '带,逗号', staff: { name: '张"三' } })]);
    p.context.download();
    const csv = decodeURIComponent(p.body.lastChild.href.split(',').slice(1).join(','));
    const rows = csv.split('\r\n');
    assert.equal(rows.length, 2);
    assert.match(rows[0], /^序号,日期,时间,订单号,七色米订单号,/);
    assert.match(rows[1], /^1,2026-11-08,11:28:29,NEW,MI7,/);
    assert.match(rows[1], /"张""三","带,逗号"$/);
    assert.doesNotMatch(csv, /OLD|undefined|\[object Object\]/);
});

test('次卡季卡和含卡类的混合订单整单排除，不进入页面、总计或下载', () => {
    const p = page();
    const card = (code, retail_type, extra = {}) => order(code, {
        retails: [{ valid: 1, retail_type, ...extra }]
    });
    p.query([
        order('NORMAL'),
        card('RENT', '租赁卡类'),
        card('CARE', '养护卡类'),
        card('SEASON', '养护卡', { order_type: '卡类销售' }),
        order('MIXED', { retails: [{ valid: 1, retail_type: '养护附加费用' },
            { valid: 1, retail_type: '养护卡', order_type: '卡类销售' }] }),
        card('ISSUED', '其他', { punch_card_id: 10 }),
        order('SOURCE', { order_source: 'care_card_single' }),
        order('SOURCE2', { order_source: 'care_card_reception' }),
        order('INVALID', { retails: [{ mi7_code: 'MI7', valid: 1, retail_type: '零售' },
            { valid: 0, retail_type: '租赁卡类' }] })
    ]);
    assert.equal(p.elements.report_rows.children.length, 2);
    assert.equal(p.elements.total.innerText, '总计：160.00');
    assert.deepEqual(Array.from(p.context.tData, row => row[3]), ['NORMAL', 'INVALID']);
    p.context.download();
    const csv = decodeURIComponent(p.body.lastChild.href.split(',').slice(1).join(','));
    assert.match(csv, /NORMAL/);
    assert.match(csv, /INVALID/);
    assert.doesNotMatch(csv, /RENT|CARE|SEASON|MIXED|ISSUED|SOURCE/);
});
