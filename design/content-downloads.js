/* In-memory interaction preview only. Timers simulate transfers; no network or player files are accessed. */
const contentDownloads = new Map();
let contentDownloadDialog = null;

function contentDownloadOpen(item, existing) {
  document.querySelector('.content-search-popup')?.close();
  document.querySelector('.content-download-flyout')?.remove();
  const key = existing?.key || `${selected.id}:${contentTab}:${item.id}`;
  let task = contentDownloads.get(key);
  if (!task) task = { key, item, percent: 0, state: 'ready', collection: contentEntries() };
  const dialog = document.createElement('dialog');
  contentDownloadDialog = dialog;
  task.dialog = dialog;
  dialog.className = 'instance-dialog content-download-dialog';
  dialog.innerHTML = `<header><h2>安装 ${escapeHtml(item.name)}</h2><button class="icon-button" data-minimize aria-label="最小化下载" title="最小化下载">−</button></header>
    <div class="instance-dialog-body"><div class="content-download-summary">${contentIcon(item.icon)}<div><strong>${escapeHtml(item.name)}</strong><p>${escapeHtml(item.description)}</p></div></div><p>版本 ${escapeHtml(item.version)}</p><p data-status></p><progress max="100" value="${task.percent}"></progress><p data-speed></p></div>
    <footer><button class="small-button dialog-primary" data-start>确认安装</button><button class="small-button subtle" data-cancel>取消</button></footer>`;
  const update = () => {
    dialog.querySelector('[data-status]').textContent = task.state === 'failed' ? '下载失败，请重试' : task.state === 'ready' ? '安装所选版本' : `正在下载 · ${Math.floor(task.percent)}%`;
    dialog.querySelector('progress').value = task.percent;
    dialog.querySelector('[data-speed]').textContent = task.state === 'running' ? '384 KB/秒' : '';
    dialog.querySelector('[data-start]').disabled = task.state === 'running';
    dialog.querySelector('[data-minimize]').disabled = task.state === 'ready';
  };
  task.update = update;
  dialog.querySelector('[data-start]').onclick = () => {
    task.state = 'running';
    contentDownloads.set(key, task);
    update(); contentDownloadsRender();
    task.timer = setInterval(() => {
      task.percent = Math.min(100, task.percent + 1.2);
      task.update?.(); contentDownloadsRender();
      if (task.percent < 100) return;
      clearInterval(task.timer);
      task.collection.push({ ...item, enabled: true, selected: false });
      task.state = 'succeeded'; contentDownloads.delete(key);
      task.dialog?.close();
      contentDownloadsRender(); contentUpdateList();
    }, 400);
  };
  dialog.querySelector('[data-minimize]').onclick = () => dialog.close();
  const cancel = () => task.state === 'running' ? contentDownloadCancel(task, () => dialog.close()) : dialog.close();
  dialog.querySelector('[data-cancel]').onclick = cancel;
  dialog.oncancel = event => { event.preventDefault(); cancel(); };
  dialog.onclose = () => { if (contentDownloadDialog === dialog) contentDownloadDialog = null; task.update = null; dialog.remove(); };
  document.querySelector('.launcher').append(dialog); update(); dialog.showModal();
}

function contentDownloadCancel(task, done = () => {}) {
  const dialog = document.createElement('dialog');
  dialog.className = 'instance-dialog';
  dialog.innerHTML = `<header><h2>终止下载？</h2></header><div class="instance-dialog-body">确定终止下载“${escapeHtml(task.item.name)}”吗？</div><footer><button class="small-button subtle" data-stop>终止下载</button><button class="small-button dialog-primary" data-keep>取消</button></footer>`;
  dialog.querySelector('[data-stop]').onclick = () => { clearInterval(task.timer); contentDownloads.delete(task.key); dialog.close(); done(); contentDownloadsRender(); };
  dialog.querySelector('[data-keep]').onclick = () => dialog.close();
  dialog.onclose = () => dialog.remove();
  document.querySelector('.launcher').append(dialog); dialog.showModal(); dialog.querySelector('[data-keep]').focus();
}

function contentDownloadsRender() {
  let host = document.querySelector('.content-download-host');
  if (!contentDownloads.size) { host?.remove(); return; }
  if (!host) {
    host = document.createElement('div'); host.className = 'content-download-host';
    host.innerHTML = '<button class="small-button" aria-expanded="false" data-download-list></button>';
    document.querySelector('.launcher').append(host);
    host.querySelector('button').onclick = () => {
      const open = !host.querySelector('.content-download-flyout');
      host.querySelector('button').setAttribute('aria-expanded', String(open));
      if (open) host.insertAdjacentHTML('afterbegin', '<div class="content-download-flyout"><strong>下载列表</strong><div data-download-rows></div></div>');
      else host.querySelector('.content-download-flyout').remove();
      contentDownloadsRender();
    };
  }
  host.querySelector('[data-download-list]').textContent = `↓ 正在下载（${contentDownloads.size}）`;
  const rows = host.querySelector('[data-download-rows]');
  if (!rows) return;
  for (const task of contentDownloads.values()) {
    let row = [...rows.children].find(element => element.dataset.key === task.key);
    if (!row) {
      row = document.createElement('div'); row.className = 'content-download-row'; row.dataset.key = task.key;
      row.innerHTML = `<button class="content-download-open">${contentIcon(task.item.icon)}<span><strong>${escapeHtml(task.item.name)}</strong><progress max="100"></progress></span></button><button class="icon-button content-download-cancel" aria-label="取消 ${escapeHtml(task.item.name)}">×</button>`;
      row.querySelector('.content-download-open').onclick = () => contentDownloadOpen(task.item, task);
      row.querySelector('.content-download-cancel').onclick = () => contentDownloadCancel(task);
      rows.append(row);
    }
    row.querySelector('progress').title = `${Math.floor(task.percent)}% · ${(task.percent * .12).toFixed(1)} / 12 MB · 384 KB/秒`;
    row.querySelector('progress').value = task.percent;
  }
  [...rows.children].filter(row => !contentDownloads.has(row.dataset.key)).forEach(row => row.remove());
}
document.addEventListener('pointerdown', event => {
  const host = document.querySelector('.content-download-host');
  if (host && !host.contains(event.target)) { host.querySelector('.content-download-flyout')?.remove(); host.querySelector('button').setAttribute('aria-expanded', 'false'); }
});
document.addEventListener('keydown', event => {
  if (event.key !== 'Escape' || document.querySelector('dialog[open]')) return;
  const host = document.querySelector('.content-download-host');
  host?.querySelector('.content-download-flyout')?.remove(); host?.querySelector('button')?.focus();
});
