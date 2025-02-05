var menuItems = [{ title: '销售业务报表', link: '../sale/sale_report.html' },
    { title: '养护业务报表', link: '../maintain/task_report.html' },
    { title: '租赁业务报表', link: '../rent/rent_report.html' },
    { title: '租赁订单报表(交易表)', link: '../rent/rent_order_list.html' },
    { title: '微信支付订单(交易表)', link: '../wepay/wepay_list.html' },
    { title: '大好河山对账单', link: '../skipass/dhhs_list.html' }];
var width = 100 / menuItems.length;
var menu = document.getElementById('menu');
for (var i = 0; i < menuItems.length; i++) {
    var div = document.createElement('div');
    var style = document.createAttribute('style');
    style.value = 'width:' + width.toString() + '%; text-align:center'
    div.attributes.setNamedItem(style);
    div.innerHTML = "<a href='" + menuItems[i].link + "' >" + menuItems[i].title + "</a>";
    menu.appendChild(div);
    
}
