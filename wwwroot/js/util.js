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