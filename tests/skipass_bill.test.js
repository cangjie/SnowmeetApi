const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function page() {
    const html = fs.readFileSync(path.join(__dirname, '../wwwroot/background/skipass/dhhs_list.html'), 'utf8');
    const script = [...html.matchAll(/<script type="text\/javascript">([\s\S]*?)<\/script>/g)]
        .map(m => m[1]).find(s => s.includes('function FillItem'));
    const elements = {};
    function element() {
        let content = '';
        return { value: '', innerText: '', children: [], style: {}, disabled: false,
            get innerHTML() { return content; }, set innerHTML(v) { content = v; this.children = []; },
            attributes: { setNamedItem() {} }, appendChild(x) { this.children.push(x); }, click() {} };
    }
    const body = { last: null, appendChild(x) { this.last = x; }, removeChild() {} };
    const requests = [];
    const context = { document: { body, title: '', getElementById(id) { return elements[id] ||= element(); },
        createElement: element, createAttribute() { return {}; } }, sessionKey: 'test',
        startDate: '2026-11-13', endDate: '2026-11-13', formatDate(v) { return v instanceof Date ? v.toISOString().slice(0, 10) : String(v).slice(0, 10); },
        formatTime() { return '12:00:00'; }, formatAmount(v) { return Number(v).toFixed(2); },
        $: { ajax(options) { const r = { options, aborted: false, abort() { this.aborted = true; } }; requests.push(r); return r; } } };
    vm.createContext(context); vm.runInContext(script, context);
    context.document.getElementById('source').value = '大好河山';
    return { context, elements, requests, body, query(rows) {
        context.getData(); const r = requests.at(-1); r.options.success(rows, 'success'); r.options.complete();
    } };
}
function row(extra = {}) {
    return { orderId: 'A', orderDate: '2026-11-13T12:00:00', productName: '测试票', num: 1,
        orderState: 2, settlementAmount: 20, isSystemOrder: true, paidAmount: 30, refundAmount: 0,
        staffName: '测试店员', shareAmount: 1, linkMan: '顾客', linkPhone: '13800000000', ...extra };
}

test('默认查询大好河山，逐票收退款与系统合计及导出一致，供应商直订不计入合计', () => {
    const p = page();
    p.query([row(), row({ orderId: 'B', paidAmount: 50, refundAmount: 50, orderState: 3, settlementAmount: 0 }),
        row({ orderId: 'DIRECT', isSystemOrder: false, paidAmount: 0, shareAmount: 0 })]);
    assert.equal(new URL(p.requests[0].options.url, 'https://example.test/background/skipass/').searchParams.get('source'), '大好河山');
    const total = p.context.tData.at(-1);
    assert.deepEqual(Array.from(total).slice(5, 11), [2, '——', '20.00', '80.00', '50.00', '10.00']);
    assert.equal(total[14], '2.00');
    assert.equal(p.elements.report_rows.children.length, 4);
    p.context.download();
    const csv = decodeURIComponent(p.body.last.href.split(',').slice(1).join(','));
    assert.match(csv, /总计,——,——,——,2,——,20\.00,80\.00,50\.00,10\.00/);
});

test('重复查询清空导出内容，切换来源丢弃旧请求结果并更新标题与下载名称', () => {
    const p = page(); p.query([row()]); p.query([row()]);
    assert.equal(p.context.tData.length, 2);
    p.context.getData(); const old = p.requests.at(-1);
    p.elements.source.value = '万龙自我游'; p.context.onSourceChanged();
    old.options.success([row({ paidAmount: 999 })], 'success'); old.options.complete();
    assert.equal(old.aborted, true); assert.equal(p.context.tData.length, 0);
    assert.equal(p.context.document.title, '万龙自我游对账单');
    p.query([row()]); p.context.download();
    assert.match(decodeURIComponent(p.body.last.download), /^万龙自我游_雪票对账_/);
});

test('供应商失败与登录失效有明确提示，不把失败当成零订单并允许重试', () => {
    const p = page(); p.context.getData(); let r = p.requests.at(-1);
    r.options.error({ status: 502 }, 'error'); r.options.complete();
    assert.match(p.elements.report_status.innerText, /获取失败/);
    assert.equal(p.context.tData.length, 0); assert.equal(p.elements.btn.disabled, false);
    p.context.getData(); r = p.requests.at(-1); r.options.error({ status: 401 }, 'error'); r.options.complete();
    assert.match(p.elements.report_status.innerText, /登录已失效/);
    p.query([]); assert.match(p.elements.report_status.innerText, /没有订单/);
});

test('商品名称含逗号引号和换行时导出保持列数', () => {
    const p = page(); p.query([row({ productName: '测试,"票"\n一天' })]); p.context.download();
    const csv = decodeURIComponent(p.body.last.href.split(',').slice(1).join(','));
    assert.ok(csv.includes('"测试,""票""\n一天"'));
    assert.equal(p.context.tData[0].length, p.context.tHead.length);
});
