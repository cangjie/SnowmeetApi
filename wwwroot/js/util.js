function setCookie(cname, cvalue, exdays) {
    var d = new Date();
    d.setTime(d.getTime() + (exdays * 1000 * 60));
    var expires = "expires=" + d.toGMTString();
    document.cookie = cname + "=" + cvalue + "; " + expires;
}

function getCookie(cname) {
    var name = cname + "=";
    var ca = document.cookie.split(';');
    for (var i = 0; i < ca.length; i++) {
        var c = ca[i].trim();
        if (c.indexOf(name) == 0) return c.substring(name.length, c.length);
    }
    return "";
}

function formatDate(dateStr) {
    var date = new Date(dateStr);
    var monthStr = (date.getMonth() + 1).toString();
    var dayStr = date.getDate().toString();
    return date.getFullYear().toString() + '-' + '00'.substr(0, 2 - monthStr.length) + monthStr + '-' + '00'.substr(0, 2 - dayStr.length) + dayStr;
}

function formatTime(dateStr) {
    var date = new Date(dateStr);
    const year = date.getFullYear()
    const month = date.getMonth() + 1
    const day = date.getDate()
    const hour = date.getHours()
    const minute = date.getMinutes()
    const second = date.getSeconds()

    return [hour, minute, second].map(formatNumber).join(':')

}

function formatNumber (n) {
    n = n.toString()
    return n[1] ? n : '0' + n
}

function formatAmount(n) {
    var amount = parseFloat(n);
    if (isNaN(amount)) {
        return '--';
    }
    amount = Math.round(100 * amount, 2)/100;
    var amountStrArr = amount.toString().split('.');
    var amountStr = amountStrArr[0] + (amountStrArr.length == 1 ? '.00'
        : (amountStrArr[1] + '00'.substring(0, 2 - amountStrArr[1].length)))
    if (amountStr.indexOf('.') < 0) {
        amountStr = amount.toString();
    }
    return '¥' + amountStr;
}