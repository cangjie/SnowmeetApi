function showNavigator() {
    var navMenuText = ['上传七色米订单明细',
        '查看上传历史'];
    var navMenuUrl = ['/background/mi7_upload/upload_orders.html',
        '/background/mi7_upload/upload_history.html']
    var href = window.location.pathname;
    //alert(href);
    var navElement = document.getElementById("navigator_menu");
    if (navElement != undefined && navElement != null) {
        var ulElement = document.createElement("ul");
        for (var i = 0; i < navMenuText.length; i++) {
            var isActive = false;
            if (href == navMenuUrl[i]) {
                isActive = true;
            }
            var itemElement = document.createElement("li");
            var anchElement = document.createElement("a");
            anchElement.href = navMenuUrl[i].trim();
            anchElement.innerText = navMenuText[i].trim();
            
            var anchClassStr = "nav-link"
            if (isActive) {
                
                anchClassStr += " disabled";
                
            }
            anchElement.setAttribute("class", anchClassStr);

            itemElement.appendChild(anchElement);
            itemElement.setAttribute("class", "nav-item");
            ulElement.appendChild(itemElement);


        }
        ulElement.setAttribute("class", "nav");
        navElement.appendChild(ulElement);
        
    }
}
showNavigator();