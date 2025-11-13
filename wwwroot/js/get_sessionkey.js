var sessionKey = getCookie('sessionKey');
var titleLevel = getCookie('title_level');
alert(titleLevel);
alert(sessionKey);
//sessionKey = '%2F4mpN1%2FtWiXk0N3%2BR55KVw%3D%3D';
var orderDetails = [];
if (sessionKey == '') {

    window.location.href = '/background/index.html';
}
