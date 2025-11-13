var sessionKey = getCookie('sessionKey');
var titleLevel = getCookie('title_level');
alert(titleLevel);
if (titleLevel == 50){
    window.location.href = '/background/rent/wl_list.html';
}
//sessionKey = '%2F4mpN1%2FtWiXk0N3%2BR55KVw%3D%3D';
var orderDetails = [];
if (sessionKey == '') {
    window.location.href = '/background/index.html';
}
