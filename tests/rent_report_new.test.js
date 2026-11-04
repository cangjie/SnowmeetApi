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
            attributes: { setNamedItem() {} }, appendChild(child) { this.children.push(child); } };
    }
    const document = { getElementById(id) { return elements[id] ||= element(); }, createElement: element,
        createAttribute() { return {}; } };
    let request;
    const context = { document, console: { log() {} }, sessionKey: 'test', startDate: '2026-10-28', endDate: '2026-10-30',
        formatDate(value) {
            if (Object.prototype.toString.call(value) === '[object Date]')
                return `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`;
            return String(value).slice(0, 10);
        }, formatTime(value) { return String(value).slice(11, 19); },
        $: { ajax(options) { request = options; } } };
    vm.createContext(context);
    vm.runInContext(script, context);
    return { context, elements, query(orders) { context.getData(); request.success({ data: orders }, 'success'); } };
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
    assert.equal(p.elements.total.innerText, '总计：120.00');
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
    assert.equal(p.elements.total.innerText, '总计：12.00');
    assert.equal(p.elements.dailyTotals.innerText, '');
});

test('空结果跨月查询显示每天的零金额', () => {
    const p = page();
    p.context.startDate = '2026-10-31';
    p.context.endDate = '2026-11-02';
    p.query([]);
    assert.equal(p.elements.total.innerText, '总计：0.00');
    assert.equal(p.elements.dailyTotals.innerText,
        '每日汇总（结算日期）：\n2026-10-31：0.00\n2026-11-01：0.00\n2026-11-02：0.00');
});
