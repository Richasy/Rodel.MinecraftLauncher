/* HTML review only: local files stay in this browser and are never uploaded. */
const wallpaperDefaults = { light: 'light-desert-temple', dark: 'dark-river-valley' };
const wallpaperStorageKey = 'rodel-wallpaper-review-v1';
let wallpaperSelection = { ...wallpaperDefaults };
const customWallpapers = { light: null, dark: null };
let customWallpaperIds = {};
let wallpaperPreview = null;
let wallpaperReturnFocus = null;
let wallpaperInertState = [];
let wallpaperStorageAvailable = true;
let wallpaperDatabase;

try {
  const saved = JSON.parse(localStorage.getItem(wallpaperStorageKey) || 'null');
  for (const theme of ['light', 'dark']) {
    if (typeof saved?.[theme] === 'string') wallpaperSelection[theme] = saved[theme];
    if (typeof saved?.custom?.[theme] === 'string') customWallpaperIds[theme] = saved.custom[theme];
  }
} catch { wallpaperStorageAvailable = false; }

function findWallpaper(theme, id) {
  return (customWallpapers[theme]?.id === id ? customWallpapers[theme] : null)
    || wallpaperCatalog.find(item => item.theme === theme && item.id === id)
    || wallpaperCatalog.find(item => item.id === wallpaperDefaults[theme]);
}
function openWallpaperDatabase() {
  if (!wallpaperDatabase) wallpaperDatabase = new Promise((resolve, reject) => {
    const request = indexedDB.open('rodel-wallpaper-review', 1);
    request.onupgradeneeded = () => request.result.createObjectStore('images', { keyPath: 'id' });
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
    request.onblocked = () => reject(new Error('Local image storage unavailable'));
  });
  return wallpaperDatabase;
}
async function wallpaperImageRecord(action, value) {
  const database = await openWallpaperDatabase();
  return new Promise((resolve, reject) => {
    const transaction = database.transaction('images', action === 'get' ? 'readonly' : 'readwrite');
    const store = transaction.objectStore('images');
    const request = action === 'get' ? store.get(value) : action === 'delete' ? store.delete(value) : store.put(value);
    transaction.oncomplete = () => resolve(request.result);
    transaction.onabort = () => reject(transaction.error);
    transaction.onerror = () => reject(transaction.error);
  });
}
async function loadCustomWallpapers() {
  for (const theme of ['light', 'dark']) {
    try {
      if (!customWallpaperIds[theme]) continue;
      const record = await wallpaperImageRecord('get', customWallpaperIds[theme]);
      if (record?.blob instanceof Blob && record.theme === theme) {
        customWallpapers[theme] = { ...record, image: URL.createObjectURL(record.blob), persisted: true, dimension: '自定义', position: 'center center' };
      }
    } catch { wallpaperStorageAvailable = false; }
  }
  for (const theme of ['light', 'dark']) wallpaperSelection[theme] = findWallpaper(theme, wallpaperSelection[theme]).id;
  displayWallpaper(); syncWallpaperSettings();
}
function wallpaperSettingsMarkup() {
  return `<section class="wallpaper-settings" aria-labelledby="wallpaper-heading">
    <div class="wallpaper-heading"><h3 id="wallpaper-heading">壁纸</h3></div>
    <p class="wallpaper-contrast-note" role="status" hidden>高对比度模式下不可使用壁纸。</p><div class="wallpaper-slots"></div></section>`;
}
function syncWallpaperSettings() {
  const slots = document.querySelector('.wallpaper-slots');
  if (!slots) return;
  const resolved = document.documentElement.dataset.theme;
  const contrast = resolved === 'contrast';
  document.querySelector('.wallpaper-contrast-note').hidden = !contrast;
  slots.innerHTML = ['light', 'dark'].map(theme => {
    const item = findWallpaper(theme, wallpaperSelection[theme]);
    const label = theme === 'light' ? '浅色主题' : '深色主题';
    return `<article class="wallpaper-slot" aria-label="${label}壁纸"><header><span>${label}</span>${resolved === theme ? '<small>当前使用</small>' : ''}</header>
      ${contrast ? '' : `<img src="${item.image}" alt="" style="object-position:${item.position}" loading="lazy">`}
      <div class="wallpaper-slot-actions"><button class="small-button" data-wallpaper-open="${theme}" ${contrast ? 'disabled' : ''}>选择壁纸</button>
        <button class="small-button subtle" data-wallpaper-reset="${theme}" ${contrast || item.id === wallpaperDefaults[theme] ? 'disabled' : ''}>恢复默认</button></div></article>`;
  }).join('');
}
function scrollToWallpaperSettings() {
  const section = document.querySelector('.wallpaper-settings'), body = document.querySelector('.settings-body');
  if (section && body) body.scrollTop += section.getBoundingClientRect().top - body.getBoundingClientRect().top - 24;
}
function displayWallpaper() {
  const resolved = document.documentElement.dataset.theme;
  const world = document.querySelector('.world');
  if (resolved === 'contrast') { world.style.removeProperty('background-image'); return; }
  const theme = resolved === 'dark' ? 'dark' : 'light';
  const item = findWallpaper(theme, wallpaperSelection[theme]);
  world.style.backgroundImage = `url("${item.image}")`; world.style.backgroundPosition = item.position;
}
function setWallpaperInert(enabled) {
  if (!enabled) { wallpaperInertState.forEach(([element, previous]) => { element.inert = previous; }); wallpaperInertState = []; return; }
  if (wallpaperInertState.length) return;
  document.querySelectorAll('.launcher > :not(.wallpaper-modal):not(.toast)').forEach(element => {
    wallpaperInertState.push([element, element.inert]); element.inert = true;
  });
}
function openWallpaperPicker(theme, trigger) {
  if (document.documentElement.dataset.theme === 'contrast') return;
  closeCombo(); closeDrawer(); wallpaperReturnFocus = trigger;
  wallpaperPreview = { theme, candidate: findWallpaper(theme, wallpaperSelection[theme]).id, customItem: null, saving: false };
  renderWallpaperPicker();
}
function renderWallpaperPicker() {
  const previousScroll = document.querySelector('.wallpaper-dialog-body')?.scrollTop || 0;
  let modal = document.querySelector('.wallpaper-modal');
  if (!modal) { modal = document.createElement('div'); modal.className = 'wallpaper-modal'; document.querySelector('.launcher').append(modal); }
  const { theme, candidate, customItem, saving } = wallpaperPreview;
  const title = theme === 'light' ? '选择浅色壁纸' : '选择深色壁纸';
  const items = wallpaperCatalog.filter(entry => entry.theme === theme), custom = customItem || customWallpapers[theme];
  if (custom) items.push(custom);
  modal.innerHTML = `<section class="wallpaper-dialog surface" role="dialog" aria-modal="true" aria-labelledby="wallpaper-picker-title" aria-busy="${saving}">
    <header class="wallpaper-dialog-header"><div><span class="pixel-label">YOUR SCENERY</span><h2 id="wallpaper-picker-title">${title}</h2></div>
      <div class="wallpaper-header-actions"><button class="small-button" data-wallpaper-import ${saving ? 'disabled' : ''}>导入壁纸</button><button class="close-button" data-wallpaper-cancel aria-label="关闭壁纸选择器" ${saving ? 'disabled' : ''}><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6 18 18M18 6 6 18"/></svg></button></div></header>
    <div class="wallpaper-dialog-body"><div class="wallpaper-grid" role="group" aria-label="${title}">
      ${items.map(entry => `<button class="wallpaper-tile" data-wallpaper-id="${entry.id}" ${entry.dimension === '自定义' ? 'aria-haspopup="menu" title="右键重命名或删除"' : ''} aria-label="${escapeHtml(entry.name)}" aria-pressed="${entry.id === candidate}" tabindex="${entry.id === candidate ? 0 : -1}" ${saving ? 'disabled' : ''}>
        <img src="${entry.image}" alt="" style="object-position:${entry.position}"><span><strong title="${escapeHtml(entry.name)}">${escapeHtml(entry.name)}</strong><small>${entry.id === candidate ? '已选' : entry.dimension}</small></span></button>`).join('')}
      </div>${saving ? '<p class="wallpaper-selection-note" role="status">正在保存壁纸</p>' : ''}
      </div>
    <input type="file" data-wallpaper-file accept="image/png,image/jpeg,image/webp" aria-label="导入${theme === 'light' ? '浅色' : '深色'}主题壁纸" hidden></section>`;
  setWallpaperInert(true);
  const body = modal.querySelector('.wallpaper-dialog-body'); body.scrollTop = previousScroll;
  const selected = modal.querySelector('.wallpaper-tile[aria-pressed="true"]');
  if (!saving) selected.focus({ preventScroll: true });
  const bounds = body.getBoundingClientRect(), tile = selected.getBoundingClientRect();
  if (tile.bottom > bounds.bottom) body.scrollTop += tile.bottom - bounds.bottom + 6;
  else if (tile.top < bounds.top) body.scrollTop += tile.top - bounds.top - 6;
}
async function importWallpaper(file) {
  const preview = wallpaperPreview;
  if (!preview || !file || preview.saving) return;
  preview.saving = true; renderWallpaperPicker();
  const importRevision = preview.importRevision = (preview.importRevision || 0) + 1;
  const image = URL.createObjectURL(file);
  try {
    const decoded = new Image(); decoded.src = image; await decoded.decode();
    if (wallpaperPreview !== preview || preview.importRevision !== importRevision || document.documentElement.dataset.theme === 'contrast') { URL.revokeObjectURL(image); return; }
    if (preview.customItem) URL.revokeObjectURL(preview.customItem.image);
    preview.customItem = { id: `custom-${preview.theme}-${crypto.randomUUID()}`, theme: preview.theme, name: file.name, blob: file, image, persisted: false, dimension: '自定义', position: 'center center' };
    preview.candidate = preview.customItem.id;
  } catch {
    URL.revokeObjectURL(image); if (wallpaperPreview !== preview || preview.importRevision !== importRevision) return;
    preview.saving = false; renderWallpaperPicker();
    showToast('无法读取这张图片，请选择有效的 PNG、JPG 或 WebP 图片。');
    return;
  }
  preview.saving = false;
  await finishWallpaperPicker(true);
}
function saveWallpaperSelection() {
  const saved = { ...wallpaperSelection, custom: {} };
  for (const theme of ['light', 'dark']) {
    const custom = customWallpapers[theme];
    if (custom?.persisted) saved.custom[theme] = custom.id;
    else if (saved[theme] === custom?.id) saved[theme] = wallpaperDefaults[theme];
  }
  try { localStorage.setItem(wallpaperStorageKey, JSON.stringify(saved)); return true; }
  catch { wallpaperStorageAvailable = false; return false; }
}
function closeWallpaperMenu(restoreFocus = false) {
  document.querySelector('.wallpaper-context-menu')?.remove();
  if (restoreFocus) document.querySelector('.wallpaper-tile[aria-haspopup="menu"]')?.focus({ preventScroll: true });
}
function closeWallpaperEditor() {
  document.querySelector('.wallpaper-editor')?.remove();
  const dialog = document.querySelector('.wallpaper-dialog');
  if (dialog) { dialog.inert = false; dialog.hidden = false; }
  document.querySelector('.wallpaper-tile[aria-haspopup="menu"]')?.focus({ preventScroll: true });
}
function openWallpaperMenu(tile, x, y) {
  closeWallpaperMenu();
  if (!wallpaperPreview || wallpaperPreview.saving || !tile.hasAttribute('aria-haspopup')) return;
  const modal = document.querySelector('.wallpaper-modal'), menu = document.createElement('div');
  menu.className = 'wallpaper-context-menu'; menu.setAttribute('role', 'menu'); menu.setAttribute('aria-label', '自定义壁纸');
  menu.innerHTML = '<button role="menuitem" data-wallpaper-rename>重命名</button><button role="menuitem" data-wallpaper-delete>删除壁纸</button>';
  modal.append(menu);
  const bounds = modal.getBoundingClientRect();
  menu.style.left = `${Math.max(8, Math.min(x - bounds.left, bounds.width - menu.offsetWidth - 8))}px`;
  menu.style.top = `${Math.max(8, Math.min(y - bounds.top, bounds.height - menu.offsetHeight - 8))}px`;
  menu.querySelector('button').focus({ preventScroll: true });
}
function openWallpaperRename() {
  const item = customWallpapers[wallpaperPreview?.theme]; if (!item) return;
  closeWallpaperMenu();
  const editor = document.createElement('div'); editor.className = 'wallpaper-editor';
  editor.innerHTML = `<form class="wallpaper-rename surface" role="dialog" aria-modal="true" aria-labelledby="wallpaper-rename-title">
    <header><h2 id="wallpaper-rename-title">重命名壁纸</h2></header>
    <div class="wallpaper-rename-body"><label for="wallpaper-name">壁纸名称</label>
    <input id="wallpaper-name" type="text" value="${escapeHtml(item.name)}" maxlength="80" required autocomplete="off"></div>
    <footer><button type="submit" class="small-button dialog-primary">保存</button><button type="button" class="small-button subtle" data-wallpaper-rename-cancel>取消</button></footer></form>`;
  document.querySelector('.wallpaper-dialog').inert = true;
  document.querySelector('.wallpaper-modal').append(editor);
  editor.querySelector('form').addEventListener('submit', event => { event.preventDefault(); renameWallpaper(editor.querySelector('input').value.trim()); });
  editor.querySelector('input').focus(); editor.querySelector('input').select();
}
async function renameWallpaper(name) {
  const preview = wallpaperPreview, item = customWallpapers[preview?.theme];
  if (!item || preview.saving) return;
  if (!name) { showToast('请输入壁纸名称。'); return; }
  preview.saving = true;
  const editor = document.querySelector('.wallpaper-editor'); editor.querySelectorAll('input,button').forEach(control => control.disabled = true);
  try {
    await wallpaperImageRecord('put', { id: item.id, theme: item.theme, name, blob: item.blob });
    item.name = name;
    if (wallpaperPreview !== preview) return;
    closeWallpaperEditor(); preview.saving = false; renderWallpaperPicker();
    displayWallpaper(); syncWallpaperSettings();
    document.querySelector('.wallpaper-tile[aria-haspopup="menu"]')?.focus({ preventScroll: true });
  } catch {
    if (wallpaperPreview !== preview) return;
    preview.saving = false; editor.querySelectorAll('input,button').forEach(control => control.disabled = false);
    showToast('重命名失败，请重试。');
  }
}
async function deleteWallpaper() {
  const preview = wallpaperPreview, item = customWallpapers[preview?.theme];
  if (!item || preview.saving) return;
  closeWallpaperMenu(); preview.saving = true; renderWallpaperPicker();
  const previous = wallpaperSelection[preview.theme];
  // Clear the persistent reference first. Failed storage leaves the image and selection intact.
  customWallpapers[preview.theme] = null;
  if (previous === item.id) wallpaperSelection[preview.theme] = wallpaperDefaults[preview.theme];
  if (!saveWallpaperSelection()) {
    customWallpapers[preview.theme] = item; wallpaperSelection[preview.theme] = previous;
    preview.saving = false; renderWallpaperPicker(); showToast('删除失败，请重试。'); return;
  }
  await wallpaperImageRecord('delete', item.id).catch(() => {});
  URL.revokeObjectURL(item.image);
  if (wallpaperPreview !== preview) return;
  preview.candidate = wallpaperSelection[preview.theme]; preview.saving = false;
  displayWallpaper(); syncWallpaperSettings(); renderWallpaperPicker();
  showToast(previous === item.id ? '自定义壁纸已删除，已恢复默认壁纸。' : '自定义壁纸已删除。');
}
async function finishWallpaperPicker(apply) {
  const preview = wallpaperPreview;
  if (!preview || (preview.saving && apply)) return;
  const theme = preview.theme, previousCustom = customWallpapers[theme], previousSelection = wallpaperSelection[theme];
  if (apply) {
    preview.saving = true; renderWallpaperPicker();
    try {
      if (preview.customItem?.id === preview.candidate) {
        const item = preview.customItem;
        await wallpaperImageRecord('put', { id: item.id, theme: item.theme, name: item.name, blob: item.blob });
        item.persisted = true;
        if (wallpaperPreview !== preview) { wallpaperImageRecord('delete', item.id).catch(() => {}); URL.revokeObjectURL(item.image); return; }
        customWallpapers[theme] = item;
      }
      wallpaperSelection[theme] = preview.candidate;
      if (!saveWallpaperSelection()) throw new Error('Storage unavailable');
    } catch {
      customWallpapers[theme] = previousCustom; wallpaperSelection[theme] = previousSelection;
      if (wallpaperPreview !== preview) return;
      preview.saving = false; renderWallpaperPicker(); showToast('壁纸保存失败，请重试。'); return;
    }
  }
  if (preview.customItem && (!apply || preview.candidate !== preview.customItem.id)) URL.revokeObjectURL(preview.customItem.image);
  wallpaperPreview = null; setWallpaperInert(false); document.querySelector('.wallpaper-modal')?.remove();
  displayWallpaper(); syncWallpaperSettings();
  if (apply && previousCustom && customWallpapers[theme] !== previousCustom) {
    URL.revokeObjectURL(previousCustom.image); wallpaperImageRecord('delete', previousCustom.id).catch(() => {});
  }
  const target = document.querySelector(`[data-wallpaper-open="${theme}"]`) || wallpaperReturnFocus;
  if (!target?.disabled) target?.focus({ preventScroll: true });
  if (document.documentElement.dataset.theme === 'contrast') scrollToWallpaperSettings();
}

document.addEventListener('click', event => {
  if (!event.target.closest('.wallpaper-context-menu')) closeWallpaperMenu();
  const button = event.target.closest('button'); if (!button || button.disabled) return;
  if (button.hasAttribute('data-wallpaper-rename')) { openWallpaperRename(); return; }
  if (button.hasAttribute('data-wallpaper-delete')) { deleteWallpaper(); return; }
  if (button.hasAttribute('data-wallpaper-rename-cancel')) { closeWallpaperEditor(); return; }
  if (button.hasAttribute('data-wallpaper-settings')) scrollToWallpaperSettings();
  if (button.dataset.wallpaperOpen) openWallpaperPicker(button.dataset.wallpaperOpen, button);
  if (button.dataset.wallpaperReset) {
    wallpaperSelection[button.dataset.wallpaperReset] = wallpaperDefaults[button.dataset.wallpaperReset]; saveWallpaperSelection();
    displayWallpaper(); syncWallpaperSettings(); document.querySelector(`[data-wallpaper-open="${button.dataset.wallpaperReset}"]`)?.focus({ preventScroll: true });
  }
  if (button.dataset.wallpaperId && wallpaperPreview && !wallpaperPreview.saving) { wallpaperPreview.candidate = button.dataset.wallpaperId; finishWallpaperPicker(true); }
  if (button.hasAttribute('data-wallpaper-import')) document.querySelector('[data-wallpaper-file]').click();
  if (button.hasAttribute('data-wallpaper-cancel')) finishWallpaperPicker(false);
});
document.addEventListener('change', event => { if (event.target.matches('[data-wallpaper-file]')) importWallpaper(event.target.files[0]); });
document.addEventListener('scroll', event => { if (event.target.classList?.contains('wallpaper-dialog-body')) closeWallpaperMenu(); }, true);
document.addEventListener('contextmenu', event => {
  const tile = event.target.closest('.wallpaper-tile');
  if (tile && wallpaperPreview) { event.preventDefault(); openWallpaperMenu(tile, event.clientX, event.clientY); }
});
document.addEventListener('keydown', event => {
  if (!wallpaperPreview) return;
  const editor = document.querySelector('.wallpaper-editor'), menu = document.querySelector('.wallpaper-context-menu');
  if (editor) {
    if (event.key === 'Escape') { event.preventDefault(); event.stopImmediatePropagation(); if (!wallpaperPreview.saving) closeWallpaperEditor(); }
    if (event.key === 'Tab') trapWallpaperFocus(event, editor);
    return;
  }
  if (menu) {
    const entries = [...menu.querySelectorAll('button')];
    if (event.key === 'Escape' || event.key === 'Tab') { event.preventDefault(); event.stopImmediatePropagation(); closeWallpaperMenu(true); return; }
    if (['ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) {
      event.preventDefault(); const index = entries.indexOf(document.activeElement);
      entries[event.key === 'Home' ? 0 : event.key === 'End' ? entries.length - 1 : (index + (event.key === 'ArrowUp' ? -1 : 1) + entries.length) % entries.length].focus();
    }
    return;
  }
  if (event.key === 'ContextMenu' || (event.shiftKey && event.key === 'F10')) {
    const tile = event.target.closest('.wallpaper-tile[aria-haspopup="menu"]');
    if (tile) { event.preventDefault(); const bounds = tile.getBoundingClientRect(); openWallpaperMenu(tile, bounds.left + 16, bounds.bottom - 16); }
    return;
  }
  if (event.key === 'Escape') { event.preventDefault(); event.stopImmediatePropagation(); if (!wallpaperPreview.saving) finishWallpaperPicker(false); return; }
  const tile = event.target.closest('[data-wallpaper-id]');
  if (tile && ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) {
    event.preventDefault(); const tiles = [...document.querySelectorAll('.wallpaper-tile')];
    const columns = getComputedStyle(tile.parentElement).gridTemplateColumns.split(' ').length; let index = tiles.indexOf(tile);
    if (event.key === 'Home') index = 0; else if (event.key === 'End') index = tiles.length - 1;
    else index = Math.max(0, Math.min(tiles.length - 1, index + ({ ArrowLeft: -1, ArrowRight: 1, ArrowUp: -columns, ArrowDown: columns }[event.key])));
    tiles.forEach((entry, position) => { entry.tabIndex = position === index ? 0 : -1; }); tiles[index].focus();
  }
  if (event.key === 'Tab') trapWallpaperFocus(event, document.querySelector('.wallpaper-dialog'));
}, true);
function trapWallpaperFocus(event, container) {
  const controls = [...container.querySelectorAll('button:not(:disabled),input:not(:disabled):not([hidden])')].filter(control => control.tabIndex >= 0);
  const first = controls[0], last = controls[controls.length - 1];
  if (!first) { event.preventDefault(); return; }
  if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
  else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
}
window.addEventListener('prototype-theme-changed', () => {
  if (document.documentElement.dataset.theme === 'contrast' && wallpaperPreview) finishWallpaperPicker(false);
  displayWallpaper(); syncWallpaperSettings();
});
window.addEventListener('prototype-page-changed', () => { displayWallpaper(); syncWallpaperSettings(); });
const chromeObserver = new ResizeObserver(() => {
  const height = document.querySelector('.review-bar').offsetHeight + document.querySelector('.review-footer').offsetHeight;
  document.documentElement.style.setProperty('--prototype-chrome-height', `${height}px`);
});
chromeObserver.observe(document.querySelector('.review-bar')); chromeObserver.observe(document.querySelector('.review-footer'));
loadCustomWallpapers();

