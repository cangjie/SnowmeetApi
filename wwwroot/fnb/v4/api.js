/* 企业微信员工认证沿用原应用、会话键与 OAuth 流程。业务身份由 FnbAuth/GetMe 确认。 */
(function (root) {
  'use strict';
  const SESSION_KEY = 'fnb_mat_sessionKey';
  class ApiError extends Error {
    constructor(message, code, status) { super(message); this.code = code; this.status = status; }
  }
  function create(options) {
    const opts = options || {}, fetcher = opts.fetch || root.fetch.bind(root), storage = opts.storage || root.localStorage;
    let actor = null, invalidated = false;
    const session = () => storage.getItem(SESSION_KEY) || '';
    function clear() { storage.removeItem(SESSION_KEY); actor = null; }
    async function request(controller, action, options) {
      const o = options || {}, query = new URLSearchParams(o.params || {});
      if (!o.anonymous) query.set('sessionKey', session());
      const init = { credentials: 'same-origin', headers: {} };
      if (o.signal) init.signal = o.signal;
      if (o.body !== undefined) {
        init.method = 'POST'; init.headers['Content-Type'] = 'application/json';
        init.body = JSON.stringify(o.shop === false ? o.body : Object.assign({}, o.body, { shopId: requireActor().shopId }));
      } else if (o.formData) { init.method = 'POST'; init.body = o.formData; }
      else if (o.shop !== false) query.set('shopId', requireActor().shopId);
      let response;
      try { response = await fetcher('/api/' + controller + '/' + action + (query.size ? '?' + query : ''), init); }
      catch (e) { if (e.name === 'AbortError') throw e; throw new ApiError('网络连接失败，请重试', null, 0); }
      if (!response.ok) throw new ApiError(response.status === 404 ? '该功能暂未开通' : '请求失败，请重试（' + response.status + '）', null, response.status);
      let result;
      try { result = await response.json(); } catch (_) { throw new ApiError('服务器返回内容异常，请重试', null, response.status); }
      if (result.code !== 0) {
        if (result.code === 2 && !o.anonymous) {
          clear();
          if (!invalidated) { invalidated = true; if (opts.onExpired) opts.onExpired(); }
        }
        throw new ApiError(result.message || '操作失败', result.code, response.status);
      }
      return result.data;
    }
    function requireActor() { if (!actor) throw new ApiError('请先登录', 2); return actor; }
    async function authenticate(location, history) {
      const params = new URLSearchParams(location.search), key = params.get('sessionKey'), code = params.get('code');
      if (key) storage.setItem(SESSION_KEY, key);
      // 及时清理一次性 code 和会话参数，保留批次 id、hash 页面与其他业务参数。
      params.delete('sessionKey'); params.delete('code'); params.delete('state');
      history.replaceState(null, '', location.pathname + (params.size ? '?' + params : '') + location.hash);
      if (code) {
        clear();
        const me = await request('FnbAuth', 'WeComLogin', { anonymous: true, shop: false, body: { code: code } });
        if (!me || !me.sessionKey) throw new ApiError('登录失败，请重新进入', 2);
        storage.setItem(SESSION_KEY, me.sessionKey);
      }
      if (!session()) return null;
      actor = await request('FnbAuth', 'GetMe', { shop: false }); invalidated = false;
      return actor;
    }
    function oauthUrl(location) {
      const redirect = location.origin + location.pathname + location.search + location.hash;
      return 'https://open.weixin.qq.com/connect/oauth2/authorize?appid=ww3a46c4555ae069f9&redirect_uri=' + encodeURIComponent(redirect)
        + '&response_type=code&scope=snsapi_base&agentid=1000009#wechat_redirect';
    }
    async function listItems(params) {
      const rows = []; let page = 1, total;
      do {
        const result = await request('FnbRoute', 'ListItems', { params: Object.assign({}, params, { page: page++, pageSize: 100 }) });
        rows.push(...result.rows); total = result.total;
        if (!result.rows.length) break;
      } while (rows.length < total);
      return rows;
    }
    return { request: request, authenticate: authenticate, oauthUrl: oauthUrl, session: session, clear: clear,
      actor: () => actor, listItems: listItems, upload: file => { const form = new FormData(); form.append('file', file); return request('FnbMaterial', 'UploadPhoto', { formData: form, shop: false }); } };
  }
  function escape(value) { return String(value == null ? '' : value).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]); }
  // 后续库存写接口使用；同一单据保存键值重试，UI 不自动重复 POST。
  function requestId() { return root.crypto.randomUUID(); }
  // 百分比表单的呈现单位转换，避免 96.1234 / 100 的二进制尾数违反服务端六位小数格式。
  function percentInput(value) { return new Intl.NumberFormat('en-US', { style: 'percent', useGrouping: false, maximumFractionDigits: 4 }).format(value).replace('%', ''); }
  function percentFraction(value) { return Number((Number(value) / 100).toFixed(6)); }
  root.FnbClient = { create: create, ApiError: ApiError, escape: escape, requestId: requestId, sessionKey: SESSION_KEY,
    percentInput: percentInput, percentFraction: percentFraction };
  if (typeof module !== 'undefined') module.exports = root.FnbClient;
})(typeof window === 'undefined' ? globalThis : window);
