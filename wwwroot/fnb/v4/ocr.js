/* 复用原食材 H5 的相机逐帧识别、候选点选与 30 秒节流；日期和保质期直接采用服务端候选。 */
(function () {
  'use strict';
  let stream = null, timer = null, busy = false, generation = 0, picked = [], last = {}, options;
  const el = id => document.getElementById(id), esc = FnbClient.escape;
  function stop() {
    generation++; clearInterval(timer); timer = null; busy = false;
    if (stream) stream.getTracks().forEach(t => t.stop()); stream = null;
    el('ocr').hidden = true; el('ocr').innerHTML = '';
    document.dispatchEvent(new CustomEvent('fnb-ocr-closed'));
  }
  function choices(data) {
    last = data; const mode = options.mode;
    const names = mode === 'name' ? (data.candidates || []).map(c => c.text) : [];
    const dates = mode === 'expire' ? ((data.expireDates || []).length ? data.expireDates : data.dates || []) : mode === 'date' ? data.dates || [] : [];
    const shelf = mode === 'shelf' ? data.shelfLives || [] : [];
    el('ocr-choices').innerHTML = names.map(name => '<button type="button" class="chip ' + (picked.includes(name) ? 'on' : '') + '" data-name="' + esc(name) + '">' + esc(name) + '</button>').join('')
      + dates.map(date => '<button type="button" class="chip" data-date="' + esc(date) + '">' + esc(date) + '</button>').join('')
      + shelf.map((s, index) => '<button type="button" class="chip" data-shelf="' + index + '">' + esc(s.value) + ' ' + esc(s.unit) + '</button>').join('');
    el('ocr-fill').hidden = mode !== 'name' || !picked.length;
    el('ocr-fill').textContent = '填入：' + picked.join('');
    el('ocr-status').textContent = names.length || dates.length || shelf.length ? '点选识别结果后填入' : '暂未识别到内容，请换个角度';
  }
  async function capture(token, deadline) {
    if (busy || !stream || picked.length) return;
    if (Date.now() > deadline) { clearInterval(timer); timer = null; el('ocr-status').textContent = '扫描已暂停，关闭后可重新扫描或手动输入'; return; }
    const video = el('ocr-video'); if (!video || !video.videoWidth) return;
    const canvas = el('ocr-canvas'); canvas.width = Math.min(1000, video.videoWidth); canvas.height = Math.round(video.videoHeight * canvas.width / video.videoWidth);
    canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height); busy = true;
    try {
      const data = await options.api.request('FnbMaterial', 'OcrScanName', { shop: false, body: { image: canvas.toDataURL('image/jpeg', .7).split(',')[1] } });
      if (token === generation && stream) choices(data);
    } catch (e) { if (token === generation && el('ocr-status')) el('ocr-status').textContent = e.message; }
    finally { if (token === generation) busy = false; }
  }
  async function start(opts) {
    stop(); options = opts; picked = []; last = {}; const token = generation;
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) throw new Error('当前环境不支持相机，请手动输入');
    const incoming = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' }, audio: false });
    if (token !== generation) { incoming.getTracks().forEach(t => t.stop()); return; } stream = incoming;
    el('ocr').innerHTML = '<section class="dialog" role="dialog" aria-modal="true" aria-label="包装识别"><header><h2>包装识别</h2><button class="close" data-ocr-close aria-label="关闭">×</button></header><video id="ocr-video" class="scan-video" autoplay muted playsinline></video><canvas id="ocr-canvas" hidden></canvas><p id="ocr-status" class="hint">对准包装上的文字</p><div id="ocr-choices" class="ocr-picks"></div><button id="ocr-fill" class="btn full" hidden></button></section>';
    el('ocr').hidden = false; el('ocr-video').srcObject = stream;
    const deadline = Date.now() + 30000; timer = setInterval(() => capture(token, deadline), 1300);
    el('ocr').onclick = e => {
      const b = e.target.closest('button'); if (!b) return;
      if (b.hasAttribute('data-ocr-close')) { stop(); return; }
      if (b.hasAttribute('data-name')) { const name = b.dataset.name; picked = picked.includes(name) ? picked.filter(n => n !== name) : picked.concat(name); choices(last); }
      else if (b.hasAttribute('data-date')) { const value = b.dataset.date; stop(); opts.onPick(value); }
      else if (b.hasAttribute('data-shelf')) { const value = last.shelfLives[Number(b.dataset.shelf)]; stop(); opts.onPick(value); }
      else if (b.id === 'ocr-fill') { const value = picked.join(''); stop(); opts.onPick(value); }
    };
  }
  window.FnbOcr = { start: start, stop: stop };
  window.addEventListener('pagehide', stop);
})();
