(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.QisemiApi = api;
})(typeof globalThis === 'object' ? globalThis : this, function () {
  'use strict';
  const inventoryPaths = /^(Inventory|Styles\/|Warehouses$|TopCategories$|StockTotals$|WarehouseStocks$)/;
  class ApiError extends Error { constructor(message, status) { super(message); this.status = status; } }
  function create(options = {}) {
    const transport = options.fetch || globalThis.fetch.bind(globalThis);
    const base = options.base || '/api/Qisemi/';
    const batches = {}, snapshots = {};
    let source = null;
    function sessionHeaders() {
      const s = options.session ? options.session() : {};
      const headers = { Accept: 'application/json' };
      if (s.SessionKey) headers.SessionKey = s.SessionKey;
      if (s.SessionType) headers.SessionType = s.SessionType;
      return headers;
    }
    function selectSource(value) { source = String(value); reset(); }
    function reset() { for (const key of Object.keys(batches)) delete batches[key]; for (const key of Object.keys(snapshots)) delete snapshots[key]; }
    async function request(path, params = {}, signal) {
      const scope = inventoryPaths.test(path) ? 'inventory' : 'full';
      const q = new URLSearchParams();
      for (const [k, v] of Object.entries(params)) if (v !== '' && v !== null && v !== undefined) q.set(k, String(v));
      if (path !== 'Sources') {
        if (source) q.set('sourceId', source);
        if (batches[scope] && !q.has('batchId')) q.set('batchId', batches[scope]);
      }
      let response;
      try { response = await transport(base + path + (q.size ? '?' + q : ''), { headers: sessionHeaders(), signal, cache: 'no-store' }); }
      catch (error) { if (error.name === 'AbortError') throw error; throw new ApiError('网络连接失败，请重试', 0); }
      let body;
      try { body = await response.json(); } catch (_) { throw new ApiError('服务返回格式异常，请稍后重试', response.status); }
      if (!response.ok || body.code !== 0) throw new ApiError(body.message || body.title || '读取失败，请稍后重试', response.status);
      if (body.data?.snapshot) {
        const snapshot = body.data.snapshot;
        if (batches[scope] && batches[scope] !== snapshot.batchId) throw new ApiError('数据批次已变化，请刷新后重新查询', 409);
        batches[scope] = snapshot.batchId; snapshots[scope] = snapshot;
      }
      return body.data;
    }
    async function file(path, signal) {
      // File links are controlled application routes, never external source URLs.
      if (!/^\/api\/Qisemi\/FileReferences\/\d+\/Content(?:\?|$)/.test(path)) throw new ApiError('文件地址无效', 400);
      const response = await transport(path, { headers: sessionHeaders(), signal, cache: 'no-store' });
      if (!response.ok) {
        let message = '文件暂时无法读取'; try { message = (await response.json()).message || message; } catch (_) { }
        throw new ApiError(message, response.status);
      }
      if ((response.headers.get('content-type') || '').includes('application/json')) throw new ApiError('未取得文件内容', 503);
      return response.blob();
    }
    return { request, file, selectSource, reset, snapshots, batches };
  }
  const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const quantity = value => value === null || value === undefined ? '未知' : Number(value).toLocaleString('zh-CN', { maximumFractionDigits: 6 });
  return { create, ApiError, escape, quantity };
});
