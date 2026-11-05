const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function page() {
    const html = fs.readFileSync(path.join(__dirname, '../wwwroot/background/rent/rent_report_new.html'), 'utf8');
    assert.match(html, /id="dailyTotals"/);
    const script = [...html.matchAll(/<script type="text\/javascript">([\s\S]*?)<\/script>/g)]
        .map(match => match[1]).find(body => body.includes('function download()'));
    const elements = {};
    function element() {
        return { innerText: '', innerHTML: '', children: [], disabled: false,
            attributes: { setNamedItem() {} }, appendChild(child) { this.children.push(child); }, click() {} };
    }
    const body = { lastChild: null, appendChild(child) { this.lastChild = child; }, removeChild() {} };
    const document = { body, getElementById(id) { return elements[id] ||= element(); }, createElement: element,
        createAttribute() { return {}; } };
    let request;
    const context = { document, console: { log() {} }, sessionKey: 'test', startDate: '2026-10-28', endDate: '2026-10-30',
        formatDate(value) {
            if (Object.prototype.toString.call(value) === '[object Date]')
                return `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`;
            return String(value).slice(0, 10);
        }, formatTime(value) { return String(value).slice(11, 19); },
        $: { ajax(options) { request = options; context.lastRequestUrl = options.url; } } };
    vm.createContext(context);
    vm.runInContext(script, context);
    return { context, elements, body, query(orders) { context.getData(); request.success({ data: orders }, 'success'); } };
}

function normal(date, amounts, extra = {}) {
    return { code: 'WT', biz_date: date + 'T10:00:00', close_date: date + 'T20:00:00',
        rentals: amounts.map(amount => ({ entertain: 0, name: '装备', realStartDate: date, realEndDate: date,
            totalRentalAmount: amount, totalDiscountAmount: 0 })), ...extra };
}

test('跨天查询按关单日期汇总可见行，包括空白日期与临时订单', () => {
    const p = page();
    const temporary = { code: 'TEMP', biz_date: '2026-10-27', close_date: '2026-10-30', rentals: [],
        paidAmount: 40, refundAmount: 10, availableRefunds: [{ create_date: '2026-10-29' }] };
    const ignored = normal('2026-10-30', [100]);
    ignored.rentals[0].entertain = 1;
    p.query([normal('2026-10-28', [70, 20]), temporary, ignored]);
    assert.match(p.elements.total.innerText, /^总计：120\.00/);
    assert.match(p.elements.dailyTotals.innerText, /2026-10-28[^\n]*90\.00/);
    assert.match(p.elements.dailyTotals.innerText, /2026-10-29[^\n]*0\.00/);
    assert.match(p.elements.dailyTotals.innerText, /2026-10-30[^\n]*30\.00/);
});

test('单日查询隐藏每日汇总，重新查询清除旧汇总', () => {
    const p = page();
    p.query([normal('2026-10-28', [70])]);
    assert.match(p.elements.dailyTotals.innerText, /70\.00/);
    p.context.startDate = '2026-11-01';
    p.context.endDate = '2026-11-01';
    p.query([normal('2026-11-01', [12])]);
    assert.match(p.elements.total.innerText, /^总计：12\.00/);
    assert.equal(p.elements.dailyTotals.innerText, '');
});

test('空结果跨月查询显示每天的零金额', () => {
    const p = page();
    p.context.startDate = '2026-10-31';
    p.context.endDate = '2026-11-02';
    p.query([]);
    assert.match(p.elements.total.innerText, /^总计：0\.00/);
    assert.equal(p.elements.dailyTotals.innerText,
        '每日汇总（结算日期）：\n' +
        '2026-10-31：租金 0.00  |  微信支付结余：0.00  |  支付宝结余：0.00\n' +
        '2026-11-01：租金 0.00  |  微信支付结余：0.00  |  支付宝结余：0.00\n' +
        '2026-11-02：租金 0.00  |  微信支付结余：0.00  |  支付宝结余：0.00');
});

test('同一订单的收款、退款与结余只出现在第一条可见明细，支持多次支付及其他方式', () => {
    const p = page();
    const first = normal('2026-10-28', [0, 70, 20], { code: 'MIX', availablePayments: [
        { pay_method: '微信支付', amount: 300, refundedAmount: 200 },
        { pay_method: '微信支付', amount: 200, refundedAmount: 100 },
        { pay_method: '支付宝', amount: 50, refundedAmount: 0 },
        { pay_method: '现金', amount: 20, refundedAmount: 5 }
    ] });
    const second = normal('2026-10-29', [60], { code: 'WX', availablePayments: [
        { pay_method: '微信支付', amount: 100, refundedAmount: 40 }
    ] });
    p.query([first, second]);
    const head = [...p.context.tHead], rows = p.context.tData.map(row => [...row]);
    const at = (row, title) => row[head.indexOf(title)];
    assert.equal(rows.length, 3);
    assert.equal(at(rows[0], '微信支付收款'), '500.00');
    assert.equal(at(rows[0], '微信支付退款'), '300.00');
    assert.equal(at(rows[0], '微信支付结余'), '200.00');
    assert.equal(at(rows[0], '支付宝收款'), '50.00');
    assert.equal(at(rows[0], '支付宝退款'), '0.00');
    assert.equal(at(rows[0], '现金结余'), '15.00');
    assert.equal(at(rows[1], '微信支付收款'), '');
    assert.equal(at(rows[1], '现金结余'), '');
    assert.equal(at(rows[2], '微信支付结余'), '60.00');
    assert.equal(at(rows[2], '支付宝收款'), '');
    assert.equal(at(rows[2], '现金收款'), '');
    assert.match(p.context.lastRequestUrl, /includeOtherPaymentMethods=true/);
});

test('再次查询时只保留本次涉及的其他付款方式', () => {
    const p = page();
    p.query([normal('2026-10-28', [10], { availablePayments: [{ pay_method: '现金', amount: 10, refundedAmount: 0 }] })]);
    assert.ok(p.context.tHead.includes('现金收款'));
    p.query([normal('2026-10-28', [20], { availablePayments: [{ pay_method: '支付宝', amount: 20, refundedAmount: 0 }] })]);
    assert.ok(!p.context.tHead.includes('现金收款'));
    assert.ok(p.context.tHead.includes('支付宝收款'));
});

test('下载的 CSV 包含支付列，且带逗号和引号的字段不拆列', () => {
    const p = page();
    const order = normal('2026-10-28', [70], { availablePayments: [
        { pay_method: '现金', amount: 100, refundedAmount: 30 }
    ] });
    order.rentals[0].name = '头盔, "高级"';
    p.query([order]);
    p.context.download();
    const csv = decodeURIComponent(p.body.lastChild.href.split(',').slice(1).join(','));
    assert.match(csv, /现金收款,现金退款,现金结余/);
    assert.match(csv, /"头盔, ""高级"""/);
    assert.match(csv, /100\.00,30\.00,70\.00/);
});

test('每日按订单结算日期汇总各支付方式结余，不重复计算多条明细', () => {
    const p = page();
    const first = normal('2026-10-28', [70, 20], { availablePayments: [
        { pay_method: '微信支付', amount: 100, refundedAmount: 40 },
        { pay_method: '支付宝', amount: 50, refundedAmount: 20 },
        { pay_method: '现金', amount: 20, refundedAmount: 8 }
    ] });
    const second = normal('2026-10-28', [50], { availablePayments: [
        { pay_method: '微信支付', amount: 80, refundedAmount: 70 }
    ] });
    const third = normal('2026-10-30', [30], { availablePayments: [
        { pay_method: '支付宝', amount: 90, refundedAmount: 60 }
    ] });
    const excluded = normal('2026-10-29', [90], { availablePayments: [
        { pay_method: '现金', amount: 999, refundedAmount: 0 }
    ] });
    excluded.rentals[0].entertain = 1;
    p.query([first, second, third, excluded]);
    const lines = p.elements.dailyTotals.innerText.split('\n');
    assert.match(lines[1], /2026-10-28.*140\.00.*微信支付结余[：:]\s*70\.00.*支付宝结余[：:]\s*30\.00.*现金结余[：:]\s*12\.00/);
    assert.match(lines[2], /2026-10-29.*0\.00.*微信支付结余[：:]\s*0\.00.*支付宝结余[：:]\s*0\.00.*现金结余[：:]\s*0\.00/);
    assert.match(lines[3], /2026-10-30.*30\.00.*微信支付结余[：:]\s*0\.00.*支付宝结余[：:]\s*30\.00.*现金结余[：:]\s*0\.00/);
});

test('重新查询时每日支付汇总不会保留前次数据或其他支付方式', () => {
    const p = page();
    p.query([normal('2026-10-28', [10], { availablePayments: [{ pay_method: '现金', amount: 10, refundedAmount: 0 }] })]);
    assert.match(p.elements.dailyTotals.innerText, /现金结余[：:]\s*10\.00/);
    p.query([normal('2026-10-28', [20], { availablePayments: [{ pay_method: '支付宝', amount: 20, refundedAmount: 5 }] })]);
    assert.match(p.elements.dailyTotals.innerText, /支付宝结余[：:]\s*15\.00/);
    assert.doesNotMatch(p.elements.dailyTotals.innerText, /现金结余/);
});

test('总计显示各支付方式跨日结余，同订单多条明细只计一次', () => {
    const p = page();
    p.query([
        normal('2026-10-28', [70, 20], { availablePayments: [
            { pay_method: '微信支付', amount: 100, refundedAmount: 40 },
            { pay_method: '现金', amount: 30, refundedAmount: 10 }
        ] }),
        normal('2026-10-30', [50], { availablePayments: [
            { pay_method: '微信支付', amount: 10, refundedAmount: 2 },
            { pay_method: '支付宝', amount: 90, refundedAmount: 60 }
        ] })
    ]);
    assert.match(p.elements.total.innerText, /总计：140\.00.*微信支付结余[：:]\s*68\.00.*支付宝结余[：:]\s*30\.00.*现金结余[：:]\s*20\.00/);
    p.context.startDate = '2026-10-30';
    p.context.endDate = '2026-10-30';
    p.query([normal('2026-10-30', [50], { availablePayments: [
        { pay_method: '支付宝', amount: 90, refundedAmount: 60 }
    ] })]);
    assert.match(p.elements.total.innerText, /总计：50\.00.*微信支付结余[：:]\s*0\.00.*支付宝结余[：:]\s*30\.00/);
    assert.doesNotMatch(p.elements.total.innerText, /现金结余/);
});
