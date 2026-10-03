// 企业微信 H5 蓝牙打印：JS-SDK 注册 + 搜索/连接打印机 + 分包写入。
// 连接流程参照小程序 utils/util.js（getBLEDeviceNameListInRangePromise / connectBLEPromise），
// 分包写入参照 components/fnb/print_food_label 的 _sendBuff（一包写完再写下一包）。
// 写入的 value 传 ArrayBuffer：@wecom/jssdk 内部会转成 base64 交给客户端。
var BlePrint = (function () {
  var CORP_ID = 'ww3a46c4555ae069f9';   // 与 FnbWeComController.CORP_ID 一致
  var AGENT_ID = 1000009;               // 餐饮通知自建应用（可信域名 mini.snowmeet.top）
  var BLE_APIS = [
    'openBluetoothAdapter', 'closeBluetoothAdapter', 'getBluetoothAdapterState', 'onBluetoothAdapterStateChange',
    'startBluetoothDevicesDiscovery', 'stopBluetoothDevicesDiscovery', 'getBluetoothDevices', 'onBluetoothDeviceFound',
    'createBLEConnection', 'closeBLEConnection', 'onBLEConnectionStateChange',
    'getBLEDeviceServices', 'getBLEDeviceCharacteristics', 'readBLECharacteristicValue',
    'writeBLECharacteristicValue', 'notifyBLECharacteristicValueChange', 'onBLECharacteristicValueChange'
  ];
  var SCAN_MS = 3000;
  // 数据库 printer 表没拉到时的兜底：现有打印机名都是这两种前缀
  var FALLBACK_PREFIXES = ['Printer_', 'GP-'];

  var log = function () {};
  var sigCache = {};          // url → Promise(签名数据)，企业签名和应用签名一次取回
  var printerNames = null;    // printer 表里的打印机名
  var found = {};             // 本次搜索到的设备 deviceId → device
  var adapterOpen = false;
  var connected = null;       // { deviceId, name, printerName, serviceId, characteristicId }

  function sleep(ms) {
    return new Promise(function (r) { setTimeout(r, ms); });
  }

  function errText(e) {
    if (!e) return '未知错误';
    if (e.errMsg) return e.errMsg + (e.errCode != null ? '（errCode ' + e.errCode + '）' : '');
    return e.message || String(e);
  }

  // 调 ww 的接口并记日志；quiet=true 时只记失败（分包写入用）
  function call(name, params, quiet) {
    if (!quiet) log('→ ' + name + (params ? ' ' + JSON.stringify(params) : ''));
    return ww[name](params || {}).then(function (res) {
      if (!quiet) log('✓ ' + name + ' ' + (res && res.errMsg ? res.errMsg : ''));
      return res;
    }, function (e) {
      log('✗ ' + name + ' ' + errText(e));
      throw e;
    });
  }

  function fetchSignature(url) {
    var key = (url || location.href).split('#')[0];
    if (!sigCache[key]) {
      sigCache[key] = fetch('/api/FnbWeCom/GetJsSdkSignature?url=' + encodeURIComponent(key))
        .then(function (r) { return r.json(); })
        .then(function (j) {
          if (j.code !== 0) throw new Error(j.message || '签名失败');
          return j.data;
        });
      sigCache[key].catch(function (e) {
        delete sigCache[key];     // 失败不缓存，下次重试
        log('✗ 取签名失败：' + errText(e));
      });
    }
    return sigCache[key];
  }

  function getConfigSignature(url) {
    return fetchSignature(url).then(function (d) { return d.config; });
  }

  function getAgentConfigSignature(url) {
    return fetchSignature(url).then(function (d) { return d.agentConfig; });
  }

  // 企业签名（config）和应用签名（agentConfig）都试一遍，结果写进日志。
  // 应用签名失败时改为只用企业签名注册：否则 SDK 每次调接口都会先重试 agentConfig 并整体失败。
  async function register(logFn) {
    log = logFn || log;
    if (typeof ww === 'undefined') {
      log('✗ JS-SDK 没加载成功（wecom-jssdk 脚本）');
      return false;
    }
    log('页面地址：' + location.href.split('#')[0]);
    ww.register({
      corpId: CORP_ID,
      agentId: AGENT_ID,
      jsApiList: BLE_APIS,
      getConfigSignature: getConfigSignature,
      getAgentConfigSignature: getAgentConfigSignature
    });
    var corpOk = false;
    var agentOk = false;
    try {
      await ww.ensureCorpConfigReady();
      corpOk = true;
      log('✓ 企业签名（config）通过');
    } catch (e) {
      log('✗ 企业签名（config）失败：' + errText(e));
    }
    try {
      await ww.ensureAgentConfigReady();
      agentOk = true;
      log('✓ 应用签名（agentConfig）通过');
    } catch (e) {
      log('✗ 应用签名（agentConfig）失败：' + errText(e));
    }
    if (!agentOk && corpOk) {
      ww.register({ corpId: CORP_ID, agentId: AGENT_ID, jsApiList: BLE_APIS, getConfigSignature: getConfigSignature });
      log('改为只用企业签名');
    }
    ww.onBluetoothDeviceFound(function (res) {
      (res.devices || []).forEach(function (d) { found[d.deviceId] = d; });
    });
    ww.onBLEConnectionStateChange(function (res) {
      if (connected && res.deviceId === connected.deviceId && !res.connected) {
        log('打印机连接已断开：' + connected.name);
        connected = null;
      }
    });
    return corpOk || agentOk;
  }

  async function loadPrinterNames() {
    if (printerNames) return printerNames;
    try {
      var j = await (await fetch('/api/Printer/GetAllPrinters')).json();
      printerNames = (j.data || []).map(function (p) { return p.name; }).filter(Boolean);
      log('printer 表 ' + printerNames.length + ' 台：' + printerNames.join('、'));
    } catch (e) {
      printerNames = [];
      log('✗ 取打印机名单失败，只按 ' + FALLBACK_PREFIXES.join('/') + ' 前缀匹配：' + errText(e));
    }
    return printerNames;
  }

  // 设备名包含 printer 表里的名字就算匹配（同小程序）；返回对应的 printer 名（决定字体）
  function matchPrinter(deviceName, names) {
    for (var i = 0; i < names.length; i++) {
      if (deviceName.indexOf(names[i]) >= 0) return names[i];
    }
    for (var k = 0; k < FALLBACK_PREFIXES.length; k++) {
      if (deviceName.indexOf(FALLBACK_PREFIXES[k]) === 0) return deviceName;
    }
    return null;
  }

  async function openAdapter() {
    if (adapterOpen) return;
    try {
      await call('openBluetoothAdapter');
    } catch (e) {
      if (e && e.errCode === 10001) {
        throw new Error('蓝牙不可用：请打开手机蓝牙；安卓还要打开定位，并允许企业微信使用定位');
      }
      throw e;
    }
    adapterOpen = true;
  }

  // 搜索 SCAN_MS 毫秒，返回匹配到的打印机（信号强的在前）
  async function scan() {
    var names = await loadPrinterNames();
    await openAdapter();
    found = {};
    await call('startBluetoothDevicesDiscovery', { allowDuplicatesKey: false });
    await sleep(SCAN_MS);
    try {
      var res = await call('getBluetoothDevices');
      (res.devices || []).forEach(function (d) { if (!found[d.deviceId]) found[d.deviceId] = d; });
    } catch (e) { /* 已记日志，用监听到的结果继续 */ }
    await call('stopBluetoothDevicesDiscovery').catch(function () {});
    var all = Object.keys(found).map(function (id) { return found[id]; });
    var named = all.filter(function (d) { return d.name || d.localName; });
    log('搜到 ' + all.length + ' 个设备，有名字的：' + named.slice(0, 20).map(function (d) {
      return (d.name || d.localName) + '(' + d.RSSI + ')';
    }).join('、'));
    var candidates = [];
    named.forEach(function (d) {
      var name = d.name || d.localName;
      var printerName = matchPrinter(name, names);
      if (printerName) {
        candidates.push({ deviceId: d.deviceId, name: name, printerName: printerName, RSSI: d.RSSI == null ? -999 : d.RSSI });
      }
    });
    candidates.sort(function (a, b) { return b.RSSI - a.RSSI; });
    return candidates;
  }

  // 找可写特征：优先「同一服务里还有 notify」的那个（打印机串口服务通常如此），否则取第一个可写的
  async function connect(c) {
    await call('createBLEConnection', { deviceId: c.deviceId });
    var services = (await call('getBLEDeviceServices', { deviceId: c.deviceId })).services || [];
    if (services.length === 0) {
      await sleep(500);
      services = (await call('getBLEDeviceServices', { deviceId: c.deviceId })).services || [];
    }
    var firstWritable = null;
    for (var i = 0; i < services.length; i++) {
      var res;
      try {
        res = await call('getBLEDeviceCharacteristics', { deviceId: c.deviceId, serviceId: services[i].uuid });
      } catch (e) {
        continue;
      }
      var chars = res.characteristics || [];
      var writable = chars.filter(function (ch) { return ch.properties && (ch.properties.write || ch.properties.writeNoResponse); });
      var hasNotify = chars.some(function (ch) { return ch.properties && (ch.properties.notify || ch.properties.indicate); });
      if (writable.length > 0) {
        var pick = { serviceId: services[i].uuid, characteristicId: writable[0].uuid };
        if (hasNotify) {
          firstWritable = pick;
          break;
        }
        if (!firstWritable) firstWritable = pick;
      }
    }
    if (!firstWritable) {
      await call('closeBLEConnection', { deviceId: c.deviceId }).catch(function () {});
      throw new Error('打印机没有可写的蓝牙特征');
    }
    log('写入通道 service ' + firstWritable.serviceId + ' / characteristic ' + firstWritable.characteristicId);
    return {
      deviceId: c.deviceId, name: c.name, printerName: c.printerName,
      serviceId: firstWritable.serviceId, characteristicId: firstWritable.characteristicId
    };
  }

  async function ensureConnected(onStatus) {
    if (connected) return connected;
    onStatus('正在搜索打印机…');
    var candidates = await scan();
    if (candidates.length === 0) {
      throw new Error('附近没有找到打印机（请确认打印机已开机、在旁边）');
    }
    log('候选打印机：' + candidates.map(function (c) { return c.name + '(' + c.RSSI + ')'; }).join('、'));
    for (var i = 0; i < candidates.length; i++) {
      onStatus('正在连接 ' + candidates[i].name + '…');
      try {
        connected = await connect(candidates[i]);
        return connected;
      } catch (e) {
        log('✗ 连接 ' + candidates[i].name + ' 失败：' + errText(e));
      }
    }
    throw new Error('打印机都连接失败');
  }

  async function send(bytes, chunkSize, intervalMs, onStatus) {
    for (var off = 0; off < bytes.length; off += chunkSize) {
      var part = bytes.slice(off, off + chunkSize);
      await call('writeBLECharacteristicValue', {
        deviceId: connected.deviceId,
        serviceId: connected.serviceId,
        characteristicId: connected.characteristicId,
        value: new Uint8Array(part).buffer
      }, true);
      if (intervalMs > 0) await sleep(intervalMs);
      onStatus('正在发送 ' + Math.min(off + chunkSize, bytes.length) + '/' + bytes.length + ' 字节');
    }
  }

  // buildBytes(printerName) 生成打印数据（字体随打印机型号变）
  async function print(buildBytes, opts, onStatus) {
    var dev = await ensureConnected(onStatus);
    var bytes = buildBytes(dev.printerName);
    log('打印到 ' + dev.name + '：' + bytes.length + ' 字节，每包 ' + opts.chunkSize + ' 字节，间隔 ' + opts.intervalMs + 'ms');
    var t0 = Date.now();
    try {
      await send(bytes, opts.chunkSize, opts.intervalMs, onStatus);
    } catch (e) {
      // 写失败就断开，下次重新搜索连接
      await disconnect();
      throw new Error('发送失败：' + errText(e));
    }
    log('✓ 发送完成，用时 ' + (Date.now() - t0) + 'ms');
    return dev;
  }

  async function disconnect() {
    if (connected) {
      var id = connected.deviceId;
      connected = null;
      await call('closeBLEConnection', { deviceId: id }).catch(function () {});
    }
  }

  async function release() {
    await disconnect();
    if (adapterOpen) {
      adapterOpen = false;
      await call('closeBluetoothAdapter').catch(function () {});
    }
  }

  return { register: register, print: print, disconnect: disconnect, release: release };
})();
