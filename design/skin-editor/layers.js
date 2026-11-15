const paths = {
  eye: 'M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12Z M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6',
  eyeOff: 'M3 3l18 18 M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12',
  drag: 'M8 5v.1 M8 12v.1 M8 19v.1 M16 5v.1 M16 12v.1 M16 19v.1',
  rename: 'm15 3 6 6-11 12H3v-7Z M13 5l6 6',
  export: 'M12 15V3 M7 8l5-5 5 5 M4 14v7h16v-7',
  up: 'M12 20V4 M5 11l7-7 7 7',
  down: 'M12 4v16 M5 13l7 7 7-7',
  lock: 'M6 10h12v11H6Z M8 10V6a4 4 0 0 1 8 0v4'
};

function icon(name) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('aria-hidden', 'true');
  const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  path.setAttribute('d', paths[name]);
  svg.append(path);
  return svg;
}

function button(label, name, className = '') {
  const element = document.createElement('button');
  element.type = 'button';
  element.className = `lp-button ${className}`.trim();
  element.setAttribute('aria-label', label);
  element.title = label;
  if (name) element.append(icon(name));
  return element;
}

function createThumbnail(atlas, context, pixels) {
  const size = Math.sqrt(pixels.length / 4);
  if (!Number.isInteger(size) || size < 1) throw new RangeError('Layer pixels must form a square RGBA image.');
  if (atlas.width !== size || atlas.height !== size) {
    atlas.width = size;
    atlas.height = size;
  }
  // Preserve every texel, including alpha; CSS alone scales the displayed image.
  context.putImageData(new ImageData(new Uint8ClampedArray(pixels), size, size), 0, 0);
  return {src: atlas.toDataURL('image/png'), size};
}

/**
 * Presents the caller's bottom-to-top layer array without owning document state.
 * All writes are callbacks, allowing the caller to make one undo transaction.
 * Reorder callbacks receive bottom-to-top IDs, retaining the fixed base layer.
 */
export function createLayerPanel(container, { getLayers, getActiveId, onSelect, onToggleVisible, onRename, onReorder, onExport, notify = () => {} }) {
  container.classList.add('lp-panel');
  let draggedId = null;
  let renamingId = null;
  let renameDraft = '';
  const atlas = document.createElement('canvas');
  const atlasContext = atlas.getContext('2d');

  function clearDrop() {
    container.querySelectorAll('.lp-drop-before,.lp-drop-after').forEach(row => row.classList.remove('lp-drop-before', 'lp-drop-after'));
  }

  function move(id, direction) {
    const layers = getLayers();
    const index = layers.findIndex(layer => layer.id === id);
    const next = index + direction;
    if (index < 0 || layers[index].base || next < 0 || next >= layers.length || layers[next].base) return;
    const ids = layers.map(layer => layer.id);
    [ids[index], ids[next]] = [ids[next], ids[index]];
    onReorder(ids);
    notify(direction > 0 ? '图层已上移' : '图层已下移');
  }

  function reorderAt(targetId, before) {
    const layers = getLayers();
    const source = layers.find(layer => layer.id === draggedId);
    if (!source || source.base || source.id === targetId) return;
    const remaining = layers.filter(layer => layer.id !== draggedId);
    const targetIndex = remaining.findIndex(layer => layer.id === targetId);
    if (targetIndex < 0) return;
    const baseBoundary = remaining.reduce((position, layer, index) => layer.base ? index + 1 : position, 0);
    // Visual "before" means above the target, hence after it in bottom-to-top order.
    const insertIndex = Math.max(baseBoundary, targetIndex + (before ? 1 : 0));
    const ids = remaining.map(layer => layer.id);
    ids.splice(insertIndex, 0, draggedId);
    if (ids.some((id, index) => id !== layers[index].id)) {
      onReorder(ids);
      notify('图层顺序已更新');
    }
  }

  function beginRename(id) {
    const layer = getLayers().find(item => item.id === id);
    if (!layer) return;
    renamingId = id;
    renameDraft = layer.name;
    if (getActiveId() !== id) onSelect(id);
    render();
    const input = container.querySelector('.lp-rename-input');
    input?.focus();
    input?.select();
  }

  function render() {
    const focused = container.contains(document.activeElement) ? document.activeElement?.dataset.lpFocus : null;
    const layers = getLayers();
    if (!layers.some(layer => layer.id === renamingId)) renamingId = null;
    const list = document.createElement('div');
    list.className = 'lp-list';
    list.setAttribute('role', 'list');
    list.setAttribute('aria-label', '绘图图层，从上到下');
    for (const layer of [...layers].reverse()) {
      const selected = layer.id === getActiveId();
      const row = document.createElement('div');
      row.className = `lp-row${selected ? ' lp-selected' : ''}${layer.visible ? '' : ' lp-hidden'}`;
      row.setAttribute('role', 'listitem');
      row.dataset.layerId = String(layer.id);
      const handle = button(layer.base ? '基础皮肤固定在最下方' : `拖动排序 ${layer.name}`, layer.base ? 'lock' : 'drag', 'lp-handle');
      handle.disabled = layer.base;
      handle.draggable = !layer.base;
      handle.dataset.lpFocus = `drag-${layer.id}`;
      handle.ondragstart = event => {
        if (layer.base || renamingId !== null) { event.preventDefault(); return; }
        draggedId = layer.id;
        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('text/plain', String(layer.id));
        row.classList.add('lp-dragging');
      };
      handle.ondragend = () => {
        draggedId = null;
        clearDrop();
        row.classList.remove('lp-dragging');
      };
      handle.onkeydown = event => {
        if (event.altKey && ['ArrowUp', 'ArrowDown'].includes(event.key)) {
          event.preventDefault();
          move(layer.id, event.key === 'ArrowUp' ? 1 : -1);
        }
      };
      const visibility = button(layer.base ? '基础皮肤始终显示' : `${layer.visible ? '隐藏' : '显示'} ${layer.name}`, layer.visible ? 'eye' : 'eyeOff', 'lp-visibility');
      visibility.disabled = layer.base;
      visibility.dataset.lpFocus = `visibility-${layer.id}`;
      visibility.setAttribute('aria-pressed', String(layer.visible));
      visibility.onclick = () => onToggleVisible(layer.id);
      const preview = button(`选择 ${layer.name}`, null, 'lp-thumbnail');
      preview.dataset.lpFocus = `thumbnail-${layer.id}`;
      preview.setAttribute('aria-pressed', String(selected));
      const image = document.createElement('img');
      const thumbnail = createThumbnail(atlas, atlasContext, layer.pixels);
      image.src = thumbnail.src;
      image.width = thumbnail.size;
      image.height = thumbnail.size;
      image.alt = '';
      image.draggable = false;
      preview.append(image);
      preview.onclick = () => { if (!selected) onSelect(layer.id); };
      const details = document.createElement('div');
      details.className = 'lp-details';
      if (renamingId === layer.id) {
        const input = document.createElement('input');
        input.className = 'lp-rename-input';
        input.value = renameDraft;
        input.setAttribute('aria-label', '图层名称');
        input.maxLength = 80;
        input.oninput = () => { renameDraft = input.value; };
        let finished = false;
        const finish = (save, restoreFocus = true) => {
          if (finished) return;
          finished = true;
          const name = input.value.trim();
          const finishingCurrent = renamingId === layer.id;
          if (finishingCurrent) renamingId = null;
          if (save && name && name !== layer.name) onRename(layer.id, name);
          if (save && !name) notify('名称不能为空，已保留原名称');
          render();
          if (restoreFocus && finishingCurrent) container.querySelectorAll('[data-lp-focus]').forEach(element => {
            if (element.dataset.lpFocus === `rename-${layer.id}`) element.focus();
          });
        };
        // Let the newly clicked control receive its click before replacing rows.
        input.onblur = () => setTimeout(() => finish(true, false), 0);
        input.onkeydown = event => {
          if (event.isComposing) return;
          if (event.key === 'Enter' || event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            finish(event.key === 'Enter');
          }
        };
        details.append(input);
      } else {
        const name = button(`选择 ${layer.name}`, null, 'lp-name');
        name.textContent = layer.name;
        name.dataset.lpFocus = `name-${layer.id}`;
        name.setAttribute('aria-pressed', String(selected));
        name.onclick = event => {
          if (event.detail === 2) beginRename(layer.id);
          else if (!selected) onSelect(layer.id);
        };
        name.ondblclick = () => beginRename(layer.id);
        name.onkeydown = event => {
          if (event.key === 'F2') { event.preventDefault(); beginRename(layer.id); }
        };
        details.append(name);
      }
      const meta = document.createElement('span');
      meta.className = 'lp-meta';
      meta.textContent = layer.base ? '不透明背景' : `${layer.opacity}%${layer.locked ? ' · 已锁定' : ''}${layer.visible ? '' : ' · 已隐藏'}`;
      details.append(meta);
      const rename = button(`重命名 ${layer.name}`, 'rename', 'lp-rename');
      rename.dataset.lpFocus = `rename-${layer.id}`;
      rename.onclick = () => beginRename(layer.id);
      const share = button(`导出 ${layer.name} 为 PNG`, 'export');
      share.dataset.lpFocus = `export-${layer.id}`;
      share.onclick = () => onExport?.(layer.id);
      row.append(handle, visibility, preview, details, share, rename);
      row.ondragover = event => {
        if (draggedId === null || layer.id === draggedId) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = 'move';
        clearDrop();
        const before = layer.base || event.clientY < row.getBoundingClientRect().top + row.offsetHeight / 2;
        row.classList.add(before ? 'lp-drop-before' : 'lp-drop-after');
      };
      row.ondrop = event => {
        if (draggedId === null) return;
        event.preventDefault();
        const before = layer.base || event.clientY < row.getBoundingClientRect().top + row.offsetHeight / 2;
        reorderAt(layer.id, before);
        draggedId = null;
        clearDrop();
      };
      list.append(row);
    }
    const order = document.createElement('div');
    order.className = 'lp-order';
    const hint = document.createElement('span');
    hint.textContent = '拖动图层调整叠放顺序';
    const activeIndex = layers.findIndex(layer => layer.id === getActiveId());
    const active = layers[activeIndex];
    const up = button('将选中图层上移一层', 'up');
    up.dataset.lpFocus = 'up';
    up.disabled = !active || active.base || activeIndex === layers.length - 1;
    up.onclick = () => move(getActiveId(), 1);
    const down = button('将选中图层下移一层', 'down');
    down.dataset.lpFocus = 'down';
    down.disabled = !active || active.base || activeIndex <= 0 || layers[activeIndex - 1]?.base;
    down.onclick = () => move(getActiveId(), -1);
    order.append(hint, up, down);
    container.replaceChildren(list, order);
    if (focused && renamingId === null) container.querySelectorAll('[data-lp-focus]').forEach(element => {
      if (element.dataset.lpFocus === focused && !element.disabled) element.focus();
    });
  }

  container.addEventListener('dragleave', event => { if (!container.contains(event.relatedTarget)) clearDrop(); });
  return { render };
}
