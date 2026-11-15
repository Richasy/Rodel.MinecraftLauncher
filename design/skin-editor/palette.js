// Palette preferences are local to this browser; skin pixels are never stored here.
import {readPaletteReference, mountPaletteReference} from './palette-reference.js';

const STORAGE_KEY = 'rodel.skin-editor.palettes.v1';
const BUILTIN_ID = 'forest';
const MAX_PALETTES = 24, MAX_COLORS = 128;
const validColor = value => typeof value === 'string' && /^#[0-9a-f]{6}([0-9a-f]{2})?$/i.test(value);
const BUILTINS = [
  {id:'forest',name:'森林自然',colors:['#E3D9B9','#D5A279','#CC7D3E','#A15B2B','#638B4D','#4F7048','#355035','#283D31','#BBA078','#79634A','#68543F','#534435','#573C2A','#452F25','#38332C','#272723']},
  {id:'sunset',name:'日落暖调',colors:['#FFF3D6','#FFD391','#FFAA67','#F67B4D','#DF4D3F','#B92C47','#862B49','#4B253D','#F5C7AB','#D99B81','#B87160','#8C4C49','#6E3940','#512B38','#382331','#231A28']},
  {id:'ocean',name:'深海冷调',colors:['#E0FBFC','#AEEDF0','#6CD6DD','#36ABB8','#21868F','#176672','#104851','#0C303B','#B3D8F2','#78B2DC','#4B87C2','#35639F','#2B467C','#26345C','#1B2441','#11192D']},
  {id:'pastel',name:'柔雾粉彩',colors:['#FFF5EC','#F4D1D7','#DEB6D2','#B9B2D9','#B9D4E5','#B7DDD6','#D7E5BD','#F2DEAC','#D7B6A9','#B68F9D','#9B83A5','#7E83A4','#78999E','#7C9C87','#9A9D7A','#605A70']},
  {id:'neon',name:'霓虹夜色',colors:['#F2F5FF','#F5FF59','#B5FF35','#45F6A8','#30E8F7','#4796FF','#A168FF','#E557E9','#FF5BA6','#FF7054','#B93A78','#793CB6','#344D9C','#194854','#252138','#10121E']}
];
const ICON_PATHS = {
  more:'M5 12h.01 M12 12h.01 M19 12h.01',
  pencil:'m16 3 5 5-12 12H4v-5Z M14 5l5 5',
  trash:'M4 6h16 M9 6V3h6v3 M6 6l1 15h10l1-15 M10 10v7 M14 10v7',
  'palette-new':'M3 3 H10 V10 H3 Z M14 3 H21 V10 H14 Z M3 14 H10 V21 H3 Z M17.5 13 V22 M13 17.5 H22',
  'image-picker':'M21 9 V4 H3 V20 H9 M3 15 L8 10 L12 14 M17.5 8 A1.5 1.5 0 1 0 14.5 8 A1.5 1.5 0 1 0 17.5 8 M18 10 L23 15 M20 12 L12 20 L11 23 L14 22 L22 14 M15 17 L18 20'
};
let paletteInstance = 0;

/** Mount an editable palette collection and synchronize its selected foreground. */
export function createPalette(container, {getColor, onPick, notify = () => {}}) {
  const serial = ++paletteInstance;
  let custom = [], activeId = BUILTIN_ID, selectedIndex = -1, foreground = getColor().toUpperCase();
  let storageMessage = '', returnFocus = null, view = 'manage';
  let removedPresets = [], draft = null, disposeReference = null;
  const node = (tag, className, text) => {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;
    return element;
  };
  const button = (text, handler, className = 'e-button') => {
    const element = node('button', className, text);
    element.type = 'button'; element.onclick = handler;
    return element;
  };
  const iconButton = (label, icon, handler) => {
    const element = button('', handler, 'palette-icon');
    if (icon === 'trash') element.classList.add('palette-critical');
    element.setAttribute('aria-label', label); element.title = label;
    element.innerHTML = ICON_PATHS[icon]
      ? `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${ICON_PATHS[icon]}"${icon === 'more' ? ' style="stroke-width:3"' : ''}/></svg>`
      : window.vectorIcon(icon);
    return element;
  };
  const allPalettes = () => [...BUILTINS.filter(item => !removedPresets.includes(item.id)), ...custom];
  const selectedPalette = () => allPalettes().find(item => item.id === activeId) || {id: '', name: '暂无色板', colors: []};
  const current = () => draft || selectedPalette();
  const isBuiltin = () => BUILTINS.some(item => item.id === current().id);
  function readStorage() {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (!stored) return;
      const data = JSON.parse(stored);
      if (data.version !== 1 || !Array.isArray(data.palettes) || data.palettes.length > MAX_PALETTES) throw new Error('Invalid palettes');
      const ids = new Set(BUILTINS.map(item => item.id));
      for (const item of data.palettes) {
        if (!item || typeof item.id !== 'string' || !/^[a-z0-9-]{1,80}$/i.test(item.id) || ids.has(item.id)
          || typeof item.name !== 'string' || !item.name.trim() || item.name.length > 40
          || !Array.isArray(item.colors) || item.colors.length > MAX_COLORS || !item.colors.every(validColor)) throw new Error('Invalid palette');
        ids.add(item.id);
      }
      custom = data.palettes.map(item => ({id: item.id, name: item.name.trim(), colors: item.colors.map(color => color.toUpperCase())}));
      removedPresets = Array.isArray(data.removedPresets) ? data.removedPresets.filter(id => BUILTINS.some(item => item.id === id)) : [];
      activeId = allPalettes().some(item => item.id === data.activeId) ? data.activeId : allPalettes()[0]?.id || '';
    } catch {
      storageMessage = '未能读取已保存的色板，已使用默认色板。';
    }
  }
  readStorage();

  container.classList.add('palette-editor');
  const toolbar = node('div', 'palette-toolbar');
  const selector = node('select'); selector.setAttribute('aria-label', '当前色板');
  const placeholder = node('option', '', '森林自然'); selector.append(placeholder); toolbar.append(selector);
  const createMenu = node('div', 'palette-command-menu palette-create-menu'); createMenu.hidden = true; createMenu.setAttribute('role', 'menu');
  const createButton = iconButton('新建色板', 'plus', () => {
    createMenu.hidden = !createMenu.hidden; createButton.setAttribute('aria-expanded', String(!createMenu.hidden));
    if (!createMenu.hidden) createMenu.firstElementChild.focus();
  });
  createButton.setAttribute('aria-haspopup', 'menu'); createButton.setAttribute('aria-expanded', 'false');
  const closeCreateMenu = () => { createMenu.hidden = true; createButton.setAttribute('aria-expanded', 'false'); };
  const imageInput = node('input'); imageInput.type = 'file'; imageInput.accept = 'image/png,image/jpeg,image/webp,image/bmp'; imageInput.hidden = true;
  for (const [label, icon, run] of [
    ['新建空白色板', 'palette-new', () => openManager('new')],
    ['从参考图新建色板', 'image-picker', () => { imageInput.value = ''; imageInput.click(); }]
  ]) {
    const item = button('', () => { closeCreateMenu(); run(); }, 'palette-command');
    const glyph = node('span', 'palette-command-icon');
    glyph.innerHTML = `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${ICON_PATHS[icon]}"/></svg>`;
    item.append(glyph, node('span', '', label));
    item.setAttribute('role', 'menuitem'); createMenu.append(item);
  }
  createMenu.onkeydown = event => {
    const items = [...createMenu.children], index = items.indexOf(document.activeElement);
    if (['ArrowDown', 'ArrowUp'].includes(event.key)) { event.preventDefault(); items[(index + (event.key === 'ArrowDown' ? 1 : items.length - 1)) % items.length].focus(); }
    if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); closeCreateMenu(); createButton.focus(); }
    if (event.key === 'Tab') closeCreateMenu();
  };
  imageInput.onchange = async () => {
    if (!imageInput.files[0]) return;
    createButton.disabled = true;
    try { openManager('reference', await readPaletteReference(imageInput.files[0])); }
    catch { notify('无法读取参考图，请选择有效图片。'); }
    finally { createButton.disabled = false; }
  };
  toolbar.append(createButton, createMenu, imageInput);
  const manageButton = iconButton('管理色板', 'more', () => openManager()); toolbar.append(manageButton);
  const swatches = node('div', 'palette-swatches'); swatches.setAttribute('role', 'group'); swatches.setAttribute('aria-label', '色板颜色');
  const empty = node('p', 'palette-empty', '色板还是空的，点击 + 加入前景色。');
  const status = node('p', 'palette-storage-note'); status.setAttribute('role', 'status');
  container.append(toolbar, swatches, empty, status);
  // The shared upgrader interpolates the first label. Upgrade with a fixed label,
  // then assign every user-supplied palette name through textContent only.
  window.upgradeCombos?.(container);
  const dialog = node('dialog', 'palette-dialog');
  dialog.setAttribute('aria-labelledby', `palette-title-${serial}`);
  const heading = node('div', 'palette-dialog-heading'), titleArea = node('div', 'palette-title-area');
  const title = node('h2'); title.id = `palette-title-${serial}`;
  const meta = node('span', 'palette-title-meta'); titleArea.append(title, meta);
  const renameButton = iconButton('重命名色板', 'pencil', () => editName());
  const moreButton = iconButton('色板操作', 'more', () => menu.hidden ? openMenu() : closeMenu(true));
  moreButton.setAttribute('aria-haspopup', 'menu'); moreButton.setAttribute('aria-expanded', 'false');
  const menu = node('div', 'palette-command-menu'); menu.hidden = true; menu.id = `palette-commands-${serial}`; menu.setAttribute('role', 'menu'); menu.setAttribute('aria-label', '色板操作'); moreButton.setAttribute('aria-controls', menu.id);
  const closeButton = iconButton('关闭色板管理', 'close', () => dialog.close());
  heading.append(titleArea, renameButton, moreButton, closeButton, menu);
  const body = node('div', 'palette-dialog-body'), footer = node('div', 'palette-dialog-footer');
  dialog.append(heading, body, footer); container.append(dialog);
  dialog.addEventListener('close', () => {
    closeMenu(); draft = null; disposeReference?.(); disposeReference = null;
    foreground = getColor().toUpperCase(); render();
    (returnFocus?.isConnected && returnFocus.getClientRects().length && !returnFocus.disabled ? returnFocus : createButton).focus();
  });
  dialog.addEventListener('cancel', event => {
    if (!menu.hidden) { event.preventDefault(); closeMenu(true); }
    else if (view !== 'manage' && !['new', 'reference'].includes(view)) { event.preventDefault(); renderManager(); }
  });
  selector.addEventListener('change', () => {
    activeId = selector.value; selectedIndex = current().colors.indexOf(foreground); save(); render();
  });
  document.addEventListener('pointerdown', event => {
    if (!menu.hidden && !menu.contains(event.target) && !moreButton.contains(event.target)) closeMenu();
    if (!createMenu.hidden && !createMenu.contains(event.target) && !createButton.contains(event.target)) closeCreateMenu();
  });
  function closeMenu(restore = false) {
    menu.hidden = true; moreButton.setAttribute('aria-expanded', 'false'); if (restore) moreButton.focus();
  }
  function openMenu() {
    const item = (label, run, critical = false) => {
      const action = button(label, () => { closeMenu(); run(); }, 'palette-command'); action.setAttribute('role', 'menuitem');
      if (critical) action.classList.add('palette-critical');
      return action;
    };
    menu.replaceChildren(item('复制色板', () => renderNameForm('copy')));
    if (!isBuiltin()) {
      if (current().colors.length) menu.append(item('移除所选颜色', () => {
        current().colors.splice(selectedIndex, 1); selectedIndex = Math.min(selectedIndex, current().colors.length - 1);
        save(); render(); renderColorEditor();
      }, true));
    }
    menu.append(item('删除色板', () => renderDelete(), true));
    menu.hidden = false; moreButton.setAttribute('aria-expanded', 'true'); menu.firstElementChild.focus();
  }
  moreButton.addEventListener('keydown', event => {
    if (['ArrowDown', 'ArrowUp'].includes(event.key)) { event.preventDefault(); openMenu(); if (event.key === 'ArrowUp') menu.lastElementChild.focus(); }
  });
  menu.addEventListener('keydown', event => {
    const items = [...menu.children], index = items.indexOf(document.activeElement);
    if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
      event.preventDefault();
      const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1 : (index + (event.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
      items[next].focus();
    }
    if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); closeMenu(true); }
    if (event.key === 'Tab') closeMenu(true);
  });
  function save() {
    if (draft) return;
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify({version: 1, activeId, palettes: custom, removedPresets}));
      storageMessage = '';
    } catch {
      storageMessage = '浏览器无法保存色板。本页仍可使用，刷新后可能丢失修改。';
      notify(storageMessage);
    }
    renderStatus();
  }
  function renderStatus() {
    status.textContent = storageMessage; status.hidden = !storageMessage;
    const message = body.querySelector('.palette-storage-note');
    if (message) { message.textContent = storageMessage; message.hidden = !storageMessage; }
  }
  function colorButton(color, index, manager) {
    const item = button('', () => {
      selectedIndex = index; foreground = color;
      if (!draft) { onPick(color); syncColor(color); }
      if (manager) {
        renderColorEditor();
        if (!isBuiltin()) colorEditor.querySelector('input[type="color"]')?.click();
        else managerGrid.querySelector(`[data-palette-index="${index}"]`)?.focus();
      }
    }, 'palette-swatch');
    item.style.backgroundColor = color;
    item.dataset.paletteIndex = String(index);
    item.dataset.color = color;
    item.title = color; item.setAttribute('aria-label', `${color}，第 ${index + 1} 色`);
    item.setAttribute('aria-pressed', String(manager ? selectedIndex === index : foreground === color));
    return item;
  }
  function addSwatch() {
    const add = iconButton(isBuiltin() ? '复制色板并加入前景色' : '加入当前前景色', 'plus', () => addForeground());
    add.className = 'palette-add-swatch'; return add;
  }
  function render() {
    selector.replaceChildren(...allPalettes().map(item => {
      const option = node('option', '', item.name); option.value = item.id; return option;
    }));
    const selected = selectedPalette();
    selector.disabled = !selected.id; manageButton.disabled = !selected.id;
    if (!selected.id) selector.append(node('option', '', selected.name));
    selector.value = selected.id;
    const face = toolbar.querySelector('.combo-face > span'); if (face) face.textContent = selected.name;
    swatches.replaceChildren(...selected.colors.map((color, index) => colorButton(color, index, false)), ...(selected.id ? [addSwatch()] : []));
    empty.hidden = !selected.id || selected.colors.length > 0;
    renderStatus();
  }
  function syncColor(color) {
    if (!validColor(color)) return;
    foreground = color.toUpperCase();
    swatches.querySelectorAll('[data-color]').forEach(item => item.setAttribute('aria-pressed', String(item.dataset.color === foreground)));
  }
  function openManager(mode = 'manage', reference = null) {
    if (!dialog.open) returnFocus = document.activeElement;
    selectedIndex = current().colors.indexOf(foreground);
    if (selectedIndex < 0 && current().colors.length) selectedIndex = 0;
    if (mode === 'manage') renderManager(); else renderNameForm(mode, reference);
    if (!dialog.open) dialog.showModal();
    (body.querySelector('input') || body.querySelector('button') || moreButton).focus();
  }
  function addForeground() {
    const color = draft ? foreground : getColor().toUpperCase();
    if (!validColor(color)) { notify('请先选择一个有效的前景色。'); return; }
    if (isBuiltin()) { openManager('copy-add'); return; }
    if (current().colors.length >= MAX_COLORS) { notify(`每个色板最多保留 ${MAX_COLORS} 色。`); return; }
    current().colors.push(color); selectedIndex = current().colors.length - 1; foreground = color;
    save(); render(); if (dialog.open) { renderColorEditor(); colorEditor.querySelector('input[type="color"]')?.click(); }
    notify('前景色已加入色板。');
  }
  let managerGrid, colorEditor;
  function renderManager() {
    const previousView = view;
    draft = null; disposeReference?.(); disposeReference = null; foreground = getColor().toUpperCase();
    dialog.classList.remove('palette-reference-dialog');
    closeMenu(); view = 'manage'; title.textContent = current().name;
    meta.hidden = false; moreButton.hidden = false; renameButton.hidden = isBuiltin(); closeButton.hidden = false;
    managerGrid = node('div', 'palette-swatches palette-manager-grid'); managerGrid.setAttribute('role', 'group'); managerGrid.setAttribute('aria-label', '选择要编辑的颜色');
    colorEditor = node('div', 'palette-color-editor');
    const storageNote = node('p', 'palette-storage-note'); storageNote.setAttribute('role', 'status');
    body.replaceChildren(managerGrid, colorEditor, storageNote);
    footer.hidden = true; footer.replaceChildren(); renderColorEditor(); renderStatus();
    if (dialog.open && previousView !== 'manage') (managerGrid.querySelector('button') || moreButton).focus();
  }
  function renderManagerGrid() {
    meta.textContent = `${current().colors.length} 色${isBuiltin() ? ' · 内置' : ''}`;
    selectedIndex = Math.min(Math.max(0, selectedIndex), current().colors.length - 1);
    managerGrid.replaceChildren(...current().colors.map((color, index) => colorButton(color, index, true)), addSwatch());
  }
  function renderColorEditor() {
    renderManagerGrid();
    colorEditor.replaceChildren();
    colorEditor.hidden = !isBuiltin();
    if (!current().colors.length) return;
    const color = current().colors[selectedIndex];
    if (isBuiltin()) {
      const readonly = node('div', 'palette-readonly-color');
      readonly.append(node('code', '', color), node('span', '', '内置色板 · 复制后可编辑')); colorEditor.append(readonly); return;
    }
    const picker = node('input'); picker.type = 'color'; picker.value = color.slice(0, 7); picker.hidden = true;
    picker.setAttribute('aria-label', '选择颜色');
    picker.oninput = () => {
      const value = picker.value.toUpperCase() + (color.length === 9 ? color.slice(7) : '');
      current().colors[selectedIndex] = value; foreground = value;
      if (!draft) onPick(value);
      save(); render(); renderManagerGrid();
    };
    const remove = button('移除此颜色', () => {
      current().colors.splice(selectedIndex, 1); selectedIndex = Math.min(selectedIndex, current().colors.length - 1);
      save(); render(); renderColorEditor();
    }, 'palette-command palette-critical');
    colorEditor.hidden = !draft; colorEditor.append(picker, remove);
  }
  function nameError(name, omitId = '') {
    if (!name) return '请填写色板名称。';
    if (name.length > 40) return '色板名称最多 40 个字符。';
    if (allPalettes().some(item => item.id !== omitId && item.name.toLocaleLowerCase() === name.toLocaleLowerCase())) return '已有同名色板，请换一个名称。';
    return '';
  }
  function editName() {
    if (isBuiltin()) return;
    closeMenu(); renameButton.hidden = true;
    const input = node('input', 'palette-inline-name'); input.type = 'text'; input.maxLength = 40; input.value = current().name; input.setAttribute('aria-label', '色板名称');
    const error = node('span', 'palette-error'); error.hidden = true; error.id = `palette-rename-error-${serial}`; input.setAttribute('aria-describedby', error.id);
    title.replaceChildren(input, error);
    let finished = false;
    const commit = () => {
      if (finished) return;
      const name = input.value.trim(), message = nameError(name, activeId);
      if (message) { error.textContent = message; error.hidden = false; input.setAttribute('aria-invalid', 'true'); return; }
      finished = true; current().name = name; save(); render(); title.textContent = name; renameButton.hidden = false;
    };
    input.onblur = commit;
    input.oninput = () => { input.removeAttribute('aria-invalid'); error.hidden = true; };
    input.onkeydown = event => {
      if (event.isComposing) return;
      if (event.key === 'Enter') { event.preventDefault(); commit(); if (finished) renameButton.focus(); }
      if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); finished = true; title.textContent = current().name; renameButton.hidden = false; renameButton.focus(); }
    };
    input.focus(); input.select();
  }
  function renderNameForm(mode, reference = null) {
    closeMenu(); view = mode;
    const copying = mode === 'copy' || mode === 'copy-add';
    const source = current();
    let suggested = copying ? `${source.name} 副本`.slice(0, 40) : reference?.name.replace(/\.[^.]+$/, '').slice(0, 40) || '我的色板';
    const stem = suggested;
    for (let number = 2; nameError(suggested); number++) suggested = `${stem.slice(0, 34)} (${number})`;
    draft = {id: 'draft', name: suggested, colors: copying ? [...source.colors] : [...(reference?.colors || [])]};
    if (mode === 'copy-add') draft.colors.push(getColor().toUpperCase());
    dialog.classList.toggle('palette-reference-dialog', Boolean(reference));
    title.textContent = copying ? '复制色板' : reference ? '从参考图新建色板' : '新建空白色板';
    meta.hidden = true; moreButton.hidden = true; renameButton.hidden = true; closeButton.hidden = true;
    const form = node('form'); form.id = `palette-name-form-${serial}`;
    const label = node('label', 'palette-name-label', '色板名称'); label.htmlFor = `palette-name-${serial}`;
    const input = node('input', 'palette-name-input'); input.id = label.htmlFor; input.type = 'text'; input.maxLength = 40; input.autocomplete = 'off';
    input.value = suggested;
    const error = node('p', 'palette-error'); error.hidden = true; error.id = `palette-name-error-${serial}`; input.setAttribute('aria-describedby', error.id);
    form.append(label, input, error); body.replaceChildren(form);
    disposeReference?.(); disposeReference = null;
    if (reference) disposeReference = mountPaletteReference(body, reference, color => {
      draft.colors.push(color); foreground = color; selectedIndex = draft.colors.length - 1; renderColorEditor();
    });
    managerGrid = node('div', 'palette-swatches palette-manager-grid');
    managerGrid.setAttribute('role', 'group'); managerGrid.setAttribute('aria-label', '新色板颜色');
    colorEditor = node('div', 'palette-color-editor'); body.append(managerGrid, colorEditor); renderColorEditor();
    const submit = button(copying ? '创建副本' : '创建色板', () => {}, 'e-button primary');
    submit.type = 'submit'; submit.setAttribute('form', form.id);
    footer.hidden = false; footer.replaceChildren(submit, button('取消', () => copying ? renderManager() : dialog.close()));
    form.onsubmit = event => {
      event.preventDefault(); const name = input.value.trim();
      const message = custom.length >= MAX_PALETTES ? `最多保留 ${MAX_PALETTES} 个自定义色板，请先删除不需要的色板。` : nameError(name);
      if (message) { error.textContent = message; error.hidden = false; input.setAttribute('aria-invalid', 'true'); input.focus(); return; }
      const colors = [...draft.colors]; draft = null;
      activeId = `palette-${crypto.randomUUID()}`; custom.push({id: activeId, name, colors}); selectedIndex = colors.indexOf(foreground);
      save(); render();
      if (!copying) { dialog.close(); return; }
      renderManager();
      if (mode === 'copy-add') {
        selectedIndex = current().colors.length - 1;
        renderColorEditor(); colorEditor.querySelector('input[type="color"]')?.click();
      }
    };
    input.oninput = () => { input.removeAttribute('aria-invalid'); error.hidden = true; };
    if (dialog.open) { input.focus(); input.select(); }
  }
  function renderDelete() {
    closeMenu(); view = 'delete'; title.textContent = '删除色板'; meta.hidden = true; moreButton.hidden = true; renameButton.hidden = true; closeButton.hidden = true;
    body.replaceChildren(node('p', 'palette-delete-copy', `删除“${current().name}”？色板中的 ${current().colors.length} 个颜色会移除，已经绘制的像素和当前前景色保持不变。`));
    footer.hidden = false; footer.replaceChildren(button('删除色板', () => {
      if (isBuiltin()) removedPresets.push(activeId);
      else custom = custom.filter(item => item.id !== activeId);
      activeId = allPalettes()[0]?.id || ''; selectedIndex = -1;
      save(); render();
      if (activeId) renderManager(); else dialog.close();
    }), button('取消', () => renderManager(), 'e-button primary'));
    footer.lastElementChild.focus();
  }
  render();
  return {syncColor};
}
