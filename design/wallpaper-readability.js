/* Review implementation: bounded local sampling, no polling or per-frame pixel readbacks. */
(() => {
  const launcher = document.querySelector('.launcher'), world = document.querySelector('.world');
  const selector = '.eyebrow,.hero h1,.hero-copy,.runtime span,.runtime b,.launch-caption';
  let worker, timer, busy = false, dirty = false, revision = 0, lastRun = 0, signature = '', nodes = [];
  const metrics = { runs: 0, discarded: 0, decodes: 0, main: [], analysis: [], worker: [] };
  const active = () => launcher.dataset.page === 'home' && document.documentElement.dataset.theme !== 'contrast' && !document.hidden;
  const color = value => (value.match(/[\d.]+/g) || []).slice(0, 3).map(Number);
  function publish() {
    const summary = values => { const sorted = [...values].sort((a, b) => a - b); return { count: sorted.length, p95: sorted[Math.floor(sorted.length * .95)] || 0, max: sorted.at(-1) || 0 }; };
    launcher.dataset.contrastMetrics = JSON.stringify({ runs: metrics.runs, discarded: metrics.discarded, decodes: metrics.decodes, main: summary(metrics.main), analysis: summary(metrics.analysis), worker: summary(metrics.worker) });
  }
  function remember(list, value) { list.push(Math.round(value * 100) / 100); if (list.length > 120) list.shift(); }
  function applyFallback() {
    nodes.forEach(node => { node.dataset.wallpaperOutline = 'true'; node.style.setProperty('--wallpaper-outline-rgb', document.documentElement.dataset.theme === 'light' ? '255,255,255' : '0,0,0'); });
    launcher.dataset.contrastState = 'fallback';
  }
  function complete(result) {
    busy = false;
    if (result.revision === revision && active()) {
      if (result.failed) applyFallback();
      else {
        result.decisions.forEach((decision, index) => { nodes[index].dataset.wallpaperOutline = String(decision.outline); nodes[index].style.setProperty('--wallpaper-outline-alpha', decision.strength); nodes[index].style.setProperty('--wallpaper-outline-rgb', [decision.edge, decision.edge, decision.edge].join(',')); });
        launcher.dataset.contrastState = 'ready';
        launcher.dataset.contrastSamples = result.decisions.reduce((sum, decision) => sum + decision.samples, 0);
        launcher.dataset.contrastCache = result.cache;
        metrics.runs++; if (result.decoded) metrics.decodes++;
        remember(metrics.analysis, result.analysis); remember(metrics.worker, result.elapsed); publish();
      }
    } else { metrics.discarded++; }
    if (dirty) schedule(false);
  }
  function run() {
    timer = null;
    if (!active() || !dirty || busy) return;
    dirty = false;
    const started = performance.now(), bounds = world.getBoundingClientRect(), style = getComputedStyle(world);
    const image = style.backgroundImage.match(/^url\(["']?(.*?)["']?\)$/)?.[1];
    if (!image) return;
    nodes = [...document.querySelectorAll(selector)].filter(node => node.getClientRects().length && !node.closest('[hidden]')).slice(0, 16);
    const groups = nodes.map(node => {
      const computed = getComputedStyle(node), rects = [], walker = document.createTreeWalker(node, NodeFilter.SHOW_TEXT);
      let text, count = 0;
      while ((text = walker.nextNode()) && count < 256) {
        const range = document.createRange();
        for (let offset = 0; offset < text.length && count < 256; offset++) {
          if (/\s/.test(text.data[offset])) continue;
          range.setStart(text, offset); range.setEnd(text, offset + 1);
          for (const rect of range.getClientRects()) if (rect.width && rect.height) rects.push([rect.x - bounds.x, rect.y - bounds.y, rect.width, rect.height]);
          count++;
        }
      }
      const size = parseFloat(computed.fontSize);
      return { rects, color: color(computed.color), large: size >= 24 || (size >= 18.66 && parseInt(computed.fontWeight) >= 700), outlined: node.dataset.wallpaperOutline === 'true' };
    });
    const position = style.backgroundPosition.split(' ').map(value => parseFloat(value) / 100);
    const layout = { width: bounds.width, height: bounds.height, position, blur: 10, light: document.documentElement.dataset.theme === 'light', background: color(style.backgroundColor), groups };
    // Ignore outline state in this key: hysteresis must not schedule itself.
    const key = JSON.stringify([image, layout.width, layout.height, position, layout.blur, layout.light, layout.background, groups.map(({ rects, color, large }) => [rects, color, large])]);
    remember(metrics.main, performance.now() - started);
    if (signature === key) { publish(); return; }
    signature = key; lastRun = performance.now();
    try {
      if (!worker) { worker = new Worker('wallpaper-contrast-worker.js?v=20261016-diagonal-blur2'); worker.onmessage = event => complete(event.data); worker.onerror = () => { worker.terminate(); worker = null; signature = ''; complete({ revision, failed: true }); }; }
      busy = true; launcher.dataset.contrastState = 'sampling';
      worker.postMessage({ image, layout, revision });
    } catch { busy = false; applyFallback(); }
  }
  function schedule(invalidate = true) {
    if (invalidate) { revision++; dirty = true; }
    clearTimeout(timer);
    if (!active()) { document.querySelectorAll('[data-wallpaper-outline]').forEach(node => delete node.dataset.wallpaperOutline); signature = ''; worker?.postMessage({ cancel: true }); return; }
    if (busy) return;
    // At most one request per 120 ms, and always one trailing pass after a resize burst.
    timer = setTimeout(run, Math.max(32, 120 - (performance.now() - lastRun)));
  }
  new ResizeObserver(() => schedule()).observe(launcher);
  new ResizeObserver(() => schedule()).observe(document.querySelector('.hero'));
  new MutationObserver(() => schedule()).observe(document.querySelector('.hero'), { childList: true, characterData: true, subtree: true });
  new MutationObserver(() => { signature = ''; schedule(); }).observe(world, { attributes: true, attributeFilter: ['style'] });
  window.addEventListener('prototype-page-changed', () => schedule());
  window.addEventListener('prototype-theme-changed', () => { signature = ''; schedule(); });
  document.addEventListener('visibilitychange', () => schedule());
  document.fonts.ready.then(() => schedule());
  schedule();
})();



