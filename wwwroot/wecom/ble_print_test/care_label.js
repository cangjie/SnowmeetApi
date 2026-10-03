// 养护标签（存根）：逐行移植小程序 components/care/print_care_label.js 的 getCommand。
// 纸 75×50mm、间隙 4mm、坐标与二维码地址都与小程序一致；指令由原样拷贝的 tsc.js 生成。
// 与小程序的差别：① 取板日期在 member_pick_date 为空时按次日算（小程序那里引用了未定义的 pickData）；
// ② 文本里的 " 去掉（TEXT 指令用 " 包内容）；③ 张数用 PRINT n,1 交给打印机重复打。
var CareLabel = (function () {
  // 测试单：care 25693 / order 71887（万龙服务中心）。按存根格式只留姓、手机号中间打码。
  var SAMPLE_ORDER = {
    id: 71887,
    code: 'WF_YH_260715_00004',
    paidAmount: 0.01,
    shop: '万龙服务中心'
  };
  var SAMPLE_CARE = {
    id: 25693,
    task_flow_code: 'WF-260715-002',
    customerName: '苍',
    customerCell: '186****7897',
    equipment: '单板',
    brand: 'Burton',
    scale: '170',
    with_pole: null,
    need_edge: 1,
    edge_degree: '89',
    need_wax: 1,
    free_wax: 0,
    urgent: 0,
    entertain: false,
    warranty: false,
    biz_type: null,
    repair_memo: null,
    create_date: '2026-07-15T12:23:16.717',
    member_pick_date: '2026-07-16T00:00:00'
  };

  // 打印机名含 Printer_ 用 24 点阵字体，其余（如 GP-3120TUC）用 32 点阵，同小程序
  function fontFor(printerName) {
    return (printerName || '').indexOf('Printer_') >= 0 ? 'TSS24.BF2' : 'TSS32.BF2';
  }

  function clean(s) {
    return String(s == null ? '' : s).replace(/"/g, '');
  }

  // 返回 GB18030 字节数组（同小程序 command.getData()）
  function build(care, order, labelType, copies, printerName) {
    var font = fontFor(printerName);
    var brand = care.brand == null ? '未填' : care.brand;
    var orderNum = care.task_flow_code;
    var name = care.customerName == null ? '' : care.customerName;
    var cell = care.customerCell == null ? '' : care.customerCell;
    var maskedCell = '';
    if (cell.length == 11) {
      maskedCell = cell.substring(0, 3) + '****' + cell.substring(7, 11);
    }
    var maskedName = '';
    if (name.length > 0) {
      maskedName = name.substring(0, 1);
    }
    if (labelType == '【客户联】') {
      maskedName = name;
      maskedCell = cell;
    }
    var summer = care.biz_type == '非雪季养护';
    var edge = care.need_edge;
    var candle = care.need_wax;
    var degree = care.edge_degree == null ? '89' : care.edge_degree;
    var type = care.equipment;
    var more = care.repair_more ? care.repair_more : '';
    var memo = care.repair_memo ? care.repair_memo : '';
    var pole = care.with_pole == true ? '含杖' : '';
    var orderDate = new Date(care.create_date);
    var orderDateStr = (orderDate.getMonth() + 1).toString() + '-' + orderDate.getDate().toString();
    var urgent = care.urgent == 1;
    var pickDate = new Date(orderDate);
    if (care.member_pick_date != null) {
      pickDate = new Date(care.member_pick_date);
    }
    else if (!urgent) {
      pickDate.setDate(pickDate.getDate() + 1);
    }
    var pickDateStr = (pickDate.getMonth() + 1).toString() + '-' + pickDate.getDate().toString();
    var scale = care.scale ? care.scale : '未填';
    var pickDateTitle = '次日';
    if (pickDate.getDate() == orderDate.getDate()) {
      pickDateTitle = '当日';
    }
    else if (pickDate.getDate() - orderDate.getDate() != 1) {
      pickDateTitle = '多日';
    }
    var command = Tsc.jpPrinter.createNew();
    command.setCls();
    command.setSize(75, 50);
    command.setGap(4);
    command.setCls();
    command.setText(20, 20, font, 0, 1, 1, clean(labelType + ' ' + (urgent ? '(急)' : '') + orderNum));
    command.setText(20, 20 + 40, font, 0, 1, 1, clean(maskedName + ' ' + maskedCell));
    command.setText(20, 20 + 40 + 40, font, 0, 1, 1, clean(type + '：' + brand + ' 长度：' + scale + '  ' + pole));
    if (edge.toString() == '1') {
      command.setText(20, 20 + 40 + 40 + 55, font, 0, 1, 1, clean('修刃 ' + degree + '：'));
    }
    else if (summer) {
      command.setText(20, 20 + 40 + 40 + 55, font, 0, 1, 1, clean('非雪季养护 ' + degree + '：'));
    }
    if (more != '') {
      command.setText(300, 20 + 40 + 40 + 55, font, 0, 1, 1, clean('其他：' + more));
    }
    if (memo != '') {
      command.setText(200, 20 + 40 + 40 + 55 + 35, font, 0, 1, 1, clean('注：' + memo));
    }
    if (candle.toString() == '1') {
      command.setText(20, 20 + 40 + 40 + 55 + 55, font, 0, 1, 1, '热打蜡：');
      command.setText(20, 20 + 40 + 40 + 55 + 55 + 55, font, 0, 1, 1, '刮蜡：');
    }
    else if (care.free_wax == 1) {
      command.setText(20, 20 + 40 + 40 + 55 + 55, font, 0, 1, 1, '机打蜡：');
    }
    var orderInfoStr = '';
    var priceStr = '';
    if (care.entertain == true) {
      orderInfoStr = '招待';
    }
    else if (care.warranty == true) {
      orderInfoStr = '质保';
    }
    else {
      orderInfoStr = order.code;
      priceStr = '金额：' + parseFloat(order.paidAmount).toFixed(2);
    }
    command.setText(290, 20 + 40 + 40 + 55 + 55 + 55 + 50, 'TSS24.BF2', 0, 1, 1, clean(orderInfoStr));
    command.setText(20, 20 + 40 + 40 + 55 + 55 + 55 + 50, font, 0, 1, 1, priceStr);
    command.setText(20, 350, font, 0, 1, 1, '取板 ' + pickDateTitle + ' ' + pickDateStr);
    command.setText(300, 350, font, 0, 1, 1, '订单日期：' + orderDateStr);
    var qrCodeText = 'https://mini.snowmeet.top/mapp/admin/care/care_order_detail/care_order_detail?orderId=' + order.id.toString() + '&careId=' + care.id.toString();
    command.setQrcode(360, 20 + 40 + 65 + 25, 'L', 4, 'M', qrCodeText);
    command.setPrint(copies);
    return command.getData();
  }

  // 页面预览：把字节按 GB18030 解回文字（只为人看，打印用 build 的字节）
  function preview(bytes) {
    try {
      return new GbEncoding.TextDecoder('gb18030').decode(new Uint8Array(bytes));
    } catch (e) {
      return '（预览失败：' + e.message + '）';
    }
  }

  return { SAMPLE_CARE: SAMPLE_CARE, SAMPLE_ORDER: SAMPLE_ORDER, fontFor: fontFor, build: build, preview: preview };
})();
