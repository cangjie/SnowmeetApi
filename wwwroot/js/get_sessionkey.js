var sessionKey = getCookie('sessionKey');
var orderDetails = [];
if (sessionKey == '') {
    window.location.href = '/background/index.html';
}
