// 食材过期提醒 H5 公共模块：企微 OAuth 会话、API 包装、状态派生、日期工具、toast。
// 会话凭证存 localStorage；接口 code=2（会话失效）时静默清 key 重走 OAuth。
// 联调后门：URL 带 ?sessionKey=xxx 直接采用（写入 localStorage），桌面浏览器可跳过 OAuth。
var MAT = (function () {
  var CORP_ID = 'ww3a46c4555ae069f9';   // 与 FnbWeComController.CORP_ID 一致
  var AGENT_ID = 1000009;               // 餐饮通知自建应用
  var API_BASE = '/api/FnbMaterial/';
  var SK_KEY = 'fnb_mat_sessionKey';

  function getSessionKey() { return localStorage.getItem(SK_KEY) || ''; }
  function setSessionKey(k) { localStorage.setItem(SK_KEY, k); }
  function clearSessionKey() { localStorage.removeItem(SK_KEY); }

  // 企业微信内置浏览器 UA 含 wxwork；非企微环境不做 OAuth 重定向（会死循环），改显示提示
  function inWeCom() { return /wxwork/i.test(navigator.userAgent); }

  function showEnvHint() {
    document.body.innerHTML = '<div style="padding:80px 32px;text-align:center;color:#3f4850;font-size:14px;line-height:2">'
      + '请在<b>企业微信</b>中打开本页面<br><span style="font-size:12px;color:#6f7881">（调试可在 URL 加 ?sessionKey=…）</span></div>';
  }

  function gotoOAuth() {
    if (!inWeCom()) {
      showEnvHint();
      return;
    }
    var redirect = encodeURIComponent(location.origin + location.pathname);
    location.replace('https://open.weixin.qq.com/connect/oauth2/authorize?appid=' + CORP_ID
      + '&redirect_uri=' + redirect + '&response_type=code&scope=snsapi_base&agentid=' + AGENT_ID
      + '#wechat_redirect');
  }

  function stripParams(params) {
    var qs = params.toString();
    history.replaceState(null, '', location.pathname + (qs ? '?' + qs : ''));
  }

  // 页面入口调用：resolve(true)=会话就绪可继续；false=已跳转 OAuth 或登录失败
  async function ensureSession() {
    var params = new URLSearchParams(location.search);
    var debugKey = params.get('sessionKey');
    if (debugKey) {
      setSessionKey(debugKey);
      params.delete('sessionKey');
      stripParams(params);
      return true;
    }
    var code = params.get('code');
    if (code) {
      // code 一次性：先从 URL 清掉再换 session，防刷新重复消费
      params.delete('code');
      params.delete('state');
      stripParams(params);
      try {
        var res = await fetch(API_BASE + 'OAuthLogin?code=' + encodeURIComponent(code));
        var json = await res.json();
        if (json.code === 0 && json.data && json.data.sessionKey) {
          setSessionKey(json.data.sessionKey);
          return true;
        }
        showToast(json.message || '登录失败');
        return false;
      } catch (e) {
        showToast('网络异常，请重试');
        return false;
      }
    }
    if (getSessionKey()) { return true; }
    gotoOAuth();
    return false;
  }

  // fetch 包装：opts.params 拼 query；opts.body 走 POST JSON；opts.formData 走 POST 表单（上传）。
  // code=2 → 清 key 重走 OAuth；code!=0 → 抛 Error(message)
  async function api(action, opts) {
    opts = opts || {};
    var qs = new URLSearchParams(opts.params || {});
    qs.set('sessionKey', getSessionKey());
    var init = {};
    if (opts.body !== undefined) {
      init.method = 'POST';
      init.headers = { 'Content-Type': 'application/json' };
      init.body = JSON.stringify(opts.body);
    } else if (opts.formData) {
      init.method = 'POST';
      init.body = opts.formData;
    }
    var res = await fetch(API_BASE + action + '?' + qs.toString(), init);
    if (!res.ok) { throw new Error('HTTP ' + res.status); }
    var json = await res.json();
    if (json.code === 2) {
      clearSessionKey();
      gotoOAuth();
      throw new Error('会话失效');
    }
    if (json.code !== 0) { throw new Error(json.message || '操作失败'); }
    return json.data;
  }

  // ---- 状态派生（与后端 FnbMaterialController.DeriveStatus 同口径）----
  // batch.expire_date 形如 '2026-07-15T00:00:00'；today 用 GetBatches 返回的服务器日期 'yyyy-MM-dd'
  function deriveStatus(batch, today) {
    if (batch.dispose_status) { return '已处理'; }
    var e = (batch.expire_date || '').slice(0, 10);
    if (!e) { return '正常'; }
    if (e < today) { return '已过期'; }
    if (e === today) { return '今日'; }
    if (e <= addDays(today, batch.warn_days || 0)) { return '临期'; }
    return '正常';
  }

  function addDays(dateStr, n) {
    var d = new Date(dateStr + 'T00:00:00');
    d.setDate(d.getDate() + n);
    return fmtDate(d);
  }
  function fmtDate(d) {
    return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
  }
  // to − from 的天数（同日=0，to 在过去为负）
  function daysBetween(fromStr, toStr) {
    return Math.round((new Date(toStr + 'T00:00:00') - new Date(fromStr + 'T00:00:00')) / 86400000);
  }

  var toastTimer = null;
  function showToast(msg) {
    var el = document.getElementById('mat-toast');
    if (!el) {
      el = document.createElement('div');
      el.id = 'mat-toast';
      document.body.appendChild(el);
    }
    el.textContent = msg;
    el.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { el.classList.remove('show'); }, 2200);
  }

  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  return {
    ensureSession: ensureSession,
    api: api,
    deriveStatus: deriveStatus,
    addDays: addDays,
    fmtDate: fmtDate,
    daysBetween: daysBetween,
    showToast: showToast,
    esc: esc
  };
})();
