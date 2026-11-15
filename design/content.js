/* Content design only: sample records and file names stay in memory. No file reads, copies or installs. */
const contentKinds = { mods: '模组', packs: '资源包', shaders: '光影包' };
const contentDirectories = { mods: 'mods', packs: 'resourcepacks', shaders: 'shaderpacks' };
const contentSampleItems = {
  mods: [
    { id: 'sodium', name: 'Sodium', description: '渲染优化', version: '0.6.13', file: 'sodium-fabric-0.6.13.jar', icon: 'layers', enabled: true },
    { id: 'mod-menu', name: 'Mod Menu', description: '模组管理', version: '11.0.3', file: 'modmenu-11.0.3.jar', icon: 'grid', enabled: true },
    { id: 'fabric-api', name: 'Fabric API', description: '基础依赖', version: '0.116.1+1.21.1', file: 'fabric-api-0.116.1+1.21.1.jar', icon: 'weave', enabled: true },
    { id: 'iris', name: 'Iris', description: '光影加载支持 · 模组', version: '1.8.8', file: 'iris-fabric-1.8.8.jar', icon: 'sun', enabled: true },
    { id: 'continuity', name: 'Continuity', description: '连接纹理', version: '3.0.0', file: 'continuity-3.0.0.jar.disabled', icon: 'tiles', enabled: false },
    { id: 'local-mod', name: 'my-local-mod.jar', description: '本地文件 · 没有图标与版本信息', version: '—', file: 'my-local-mod.jar', icon: 'fallback', enabled: true },
  ],
  packs: [
    { id: 'block-detail', name: '方块细节', description: '资源包 · 32×', version: '1.4', file: 'block-detail.zip', icon: 'tiles', enabled: true },
    { id: 'soft-light', name: '柔和光照', description: '资源包 · 16×', version: '2.0', file: 'soft-light.zip', icon: 'sun', enabled: false },
  ],
  shaders: [
    { id: 'forest-light', name: '林间光影', description: '光影包', version: '1.2', file: 'forest-light.zip', icon: 'sun', enabled: true },
  ],
};
const contentAdditionalSamples = {
  mods: [{ id: 'lithium', name: 'Lithium', description: '游戏逻辑优化', version: '0.14.0', file: 'lithium-fabric-0.14.0.jar', icon: 'layers', enabled: true }],
  packs: [{ id: 'warm-blocks', name: '暖色方块', description: '资源包 · 16×', version: '1.0', file: 'warm-blocks.zip', icon: 'tiles', enabled: true }],
  shaders: [{ id: 'evening-light', name: '黄昏光影', description: '光影包', version: '1.0', file: 'evening-light.zip', icon: 'sun', enabled: true }],
};
const contentCollections = new Map();
let contentFilter = '';
let contentNoInstances = false;
let contentDragPreview = false;
let contentDragDepth = 0;
let contentImportSerial = 0;

function contentEntries() {
  const key = `${selected.id}:${contentTab}`;
  if (!contentCollections.has(key)) {
    const samples = selected.mods || contentTab !== 'mods' ? contentSampleItems[contentTab] : [];
    contentCollections.set(key, samples.map(item => contentTab === 'mods' ? { ...item } : { ...item, enabled: true, selected: item.enabled }));
  }
  return contentCollections.get(key);
}

function contentIcon(kind) {
  const drawings = {
    layers: '<path d="m8 15 16-8 16 8-16 8zM8 22l16 8 16-8M8 29l16 8 16-8"/>',
    grid: '<path d="M10 10h11v11H10zM27 10h11v11H27zM10 27h11v11H10zM27 27h11v11H27z"/>',
    weave: '<path d="M12 12h24v24H12zM12 20h24M12 28h24M20 12v24M28 12v24"/>',
    sun: '<circle cx="24" cy="24" r="9"/><path d="M24 5v5M24 38v5M5 24h5M38 24h5M10 10l4 4M34 34l4 4M10 38l4-4M34 14l4-4"/>',
    tiles: '<path d="M8 8h15v15H8zM25 25h15v15H25zM25 8h15v15H25zM8 25h15v15H8zM23 15h2M15 23v2M33 23v2M23 33h2"/>',
    fallback: '<path d="m24 8 15 8v17l-15 8-15-8V16zM9 16l15 8 15-8M24 24v17"/>',
  };
  return `<span class="mod-art mod-art-${kind}" aria-hidden="true"><svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="miter">${drawings[kind] || drawings.fallback}</svg></span>`;
}

function contentRows(searching = false) {
  const entries = contentEntries();
  const query = searching ? contentFilter.trim().toLocaleLowerCase() : '';
  const visible = entries.filter(item => `${item.name} ${item.file}`.toLocaleLowerCase().includes(query));
  if (searching && !query) return '<p class="content-search-empty-line">输入名称或关键词，查找已安装内容和社区项目。设计稿不联网。</p>';
  if (query) {
    const results = contentCatalog().filter(item => `${item.name} ${item.description}`.toLocaleLowerCase().includes(query));
    if (!visible.length && !results.length) {
      return `<div class="collection-empty content-filter-empty"><span class="empty-search" data-icon="content"></span><div class="collection-empty-copy"><h2>没有匹配的${contentKinds[contentTab]}</h2><p>当前设计稿仅包含少量在线示例，可换个关键词。已安装内容不会改变。</p><button class="small-button subtle" data-content-clear-filter>清除筛选</button></div></div>`;
    }
    return `<div class="content-search-group"><h2>已安装 <small>${visible.length}</small></h2></div>
      ${visible.length ? visible.map(item => `<article class="content-catalog-row" role="listitem">${contentIcon(item.icon)}<div class="content-name"><strong>${escapeHtml(item.name)}</strong><small>${escapeHtml(item.file)}</small></div><span class="content-installed-label">已安装</span></article>`).join('') : '<p class="content-search-empty-line">当前实例没有匹配的已安装内容。</p>'}
      <div class="content-search-group"><h2>社区结果 <small>${contentKinds[contentTab]}</small></h2></div>
      ${results.length ? results.map(item => `<article class="content-catalog-row" role="listitem" data-catalog-kind="${contentTab}">
        ${contentIcon(item.icon)}<div class="content-name"><strong>${escapeHtml(item.name)}</strong><small>${escapeHtml(item.description)} · ${contentKinds[contentTab]}</small></div>
        <button class="small-button subtle" data-content-add-catalog="${item.id}" ${entries.some(local => local.id === item.id) ? 'disabled' : ''}>${entries.some(local => local.id === item.id) ? '已安装' : '添加'}</button>
      </article>`).join('') : '<p class="content-search-empty-line">设计稿中没有该关键词的在线示例，正式实现将在此显示真实结果。</p>'}`;
  }
  if (!entries.length) {
    return `<div class="collection-empty">
      <div class="collection-empty-art" aria-hidden="true">${contentIcon('fallback')}<span class="empty-plus">${vectorIcon('plus')}</span></div>
      <div class="collection-empty-copy"><h2>这个实例还没有${contentKinds[contentTab]}</h2>
      <p>搜索或导入${contentKinds[contentTab]}到「${escapeHtml(selected.name)}」，也可直接拖入文件。原文件保持不变。</p>
      <div class="collection-empty-actions"><button class="small-button" data-content-import><span data-icon="plus"></span>导入文件</button></div></div>
    </div>`;
  }
  return contentItemRows(visible);
}

function contentCatalog() {
  return [...contentSampleItems[contentTab], ...contentAdditionalSamples[contentTab]].filter(item => item.icon !== 'fallback');
}

function contentItemRows(items) {
  return items.map(item => { const active = contentTab === 'mods' ? item.enabled : !!item.selected; return `<article class="content-row ${contentTab === 'mods' && !active ? 'mod-is-disabled' : ''}" role="listitem" data-content-id="${item.id}">
    ${contentIcon(item.icon)}
    <div class="content-name"><strong title="${escapeHtml(item.name)}">${escapeHtml(item.name)}${item.pinned?' <small class="content-pin">已固定</small>':''}</strong><small title="${escapeHtml(item.description)}">${escapeHtml(item.description)}</small>${contentTab === 'packs' && item.selected ? `<small>优先级 ${item.priority || 1}</small>` : ''}</div>
    <span class="content-version" title="${escapeHtml(item.version)}">${escapeHtml(item.version)}</span>
    <div class="mod-toggle-group"><span class="mod-state" aria-hidden="true">${contentTab === 'mods' ? active ? '已启用' : '已禁用' : active ? '使用中' : '未使用'}</span><button class="mod-switch" type="button" role="switch" aria-checked="${active}" aria-label="${contentTab === 'mods' ? '启用' : '使用'} ${escapeHtml(item.name)}" data-content-toggle="${item.id}"><span class="mod-switch-thumb"></span></button></div>
    <button class="mod-remove content-more-button" data-content-more="${item.id}" aria-label="管理 ${escapeHtml(item.name)}" aria-haspopup="menu" title="更多">···</button>
  </article>`; }).join('');
}

function contentCountText() {
  const entries = contentEntries();
  const enabled = entries.filter(item => contentTab === 'mods' ? item.enabled : item.selected).length;
  return `${entries.length} 个已安装${contentKinds[contentTab]}<span>·</span>${enabled} 个${contentTab === 'mods' ? '已启用' : '使用中'}`;
}

function contentPage() {
  const hasTarget = !contentNoInstances && instances.length > 0;
  const heading = `<header class="page-heading content-page-heading">
    <h1>内容</h1>
    <div class="toolbar-actions">
      <button class="small-button subtle" data-content-search-open>搜索内容</button>
      ${hasTarget ? `<button class="small-button" data-content-import>从文件添加</button>
        <button class="icon-button content-tools-button" data-content-tools aria-haspopup="menu" aria-label="更多操作" title="更多操作">${vectorIcon('more')}</button>` : ''}
    </div>
  </header>${hasTarget && contentTab === 'mods' ? contentBundledFlyout() : ''}`;
  if (!hasTarget) {
    return `${heading}<section class="page-surface acrylic content-onboarding">
      <div class="collection-empty">
        <div class="collection-empty-art no-instance-art" aria-hidden="true">${cubeIcon('grass')}<span class="empty-plus">${vectorIcon('plus')}</span></div>
        <h2>先添加一个实例</h2><p>每个实例都有自己的模组、资源包和光影。<br>新建一个游戏实例，或把已有的实例导入进来，再管理它的内容。</p>
        <div class="collection-empty-actions"><button class="small-button" data-content-create-instance><span data-icon="plus"></span>新建实例</button><button class="small-button subtle" data-content-import-instance>导入已有实例</button></div>
      </div></section>`;
  }
  const entries = contentEntries();
  return `${heading}<section class="page-surface acrylic content-collection" data-drag-active="${contentDragPreview}">
    <div class="content-instance-bar">
      <span class="content-instance-art" aria-hidden="true">${cubeIcon(selected.icon)}</span>
      <div class="content-instance-selection"><label class="context-picker content-context-picker"><span>当前实例 <small>点击切换</small></span><select id="page-instance" aria-label="切换当前实例">${instances.map(instance => `<option value="${instance.id}" ${instance.id === selected.id ? 'selected' : ''}>${escapeHtml(instance.name)}</option>`).join('')}</select></label></div>
      <span class="content-context-runtime">Minecraft ${escapeHtml(selected.version)}<span>·</span>${escapeHtml(selected.loader)}</span>
      ${contentTab === 'mods' ? '<button class="small-button subtle content-bundled-open" popovertarget="content-bundled" data-content-bundled-open>内置模组 ▾</button>' : ''}
    </div>
    <div class="content-toolbar"><div class="page-tabs" aria-label="内容类别">${Object.entries(contentKinds).map(([key, text]) => `<button data-content-tab="${key}" aria-pressed="${contentTab === key}">${text}</button>`).join('')}</div></div>
    ${entries.length ? `<div class="content-table-head"><span>名称</span><span>${contentTab === 'mods' ? '模组版本' : '版本'}</span><span>状态</span><span class="visually-hidden">移除</span></div>` : ''}
    <div id="content-rows" role="list" aria-label="已安装${contentKinds[contentTab]}">${contentRows()}</div>
    <footer class="content-bottom"><span id="content-count" aria-live="polite">${contentCountText()}</span><button class="text-button" data-panel="instance">实例配置 ↗</button></footer>
    <div class="content-drop-overlay" aria-hidden="true"><span class="drop-frame"><span class="drop-icon" data-icon="download"></span><strong>松开以添加到「${escapeHtml(selected.name)}」</strong><span>目标：${contentKinds[contentTab]} · ${contentDirectories[contentTab]} 目录</span><span>文件将复制到此实例，原文件保持不变。</span></span></div>
    <input type="file" id="content-import-files" multiple hidden>
  </section>`;
}

function contentUpdateList() {
  el('content-rows').innerHTML = contentRows();
  el('content-count').innerHTML = contentCountText();
  decorate(el('content-rows'));
  const results = el('content-search-results');
  if (results) results.innerHTML = contentRows(true);
}

const contentBundledMods = [
  {id:'rodel_ai', name:'Rodel AI', version:'0.0.1', description:'在暂停菜单与 AI 对话，或用 /rai 快速提问，共用启动器的模型配置。'},
  {id:'worldreroll', name:'World Reroll', version:'0.0.1', description:'保留世界设置，用新种子重新开局；支持村庄出生与奖励箱。'},
  {id:'bettertools', name:'Better Tools', version:'0.0.1', description:'按目标自动切换原装备，保留各自附魔与耐久。'},
];
function contentBundledFlyout() {
  return `<div id="content-bundled" popover="auto" class="content-bundled acrylic" aria-label="内置模组">
    <strong>内置模组</strong><div class="content-bundled-choices">${contentBundledMods.map(mod => {
      const installed = contentEntries().some(entry => entry.id === mod.id);
      const icon = `<img alt="" src="../src/Rodel.MinecraftLauncher.App/Assets/BundledMods/${mod.id}.png">`;
      return `<label class="content-bundled-option">${icon}<span class="content-bundled-copy"><strong>${mod.name}</strong><span>${mod.description}</span><small>${mod.version}${installed ? '<span class="content-bundled-installed">已安装</span>' : ''}</small></span><input aria-label="${mod.name}" type="checkbox" value="${mod.id}" ${installed ? 'checked disabled' : ''}></label>`;
    }).join('')}</div><div class="content-bundled-footer"><p>按实例版本和加载器安装匹配的模组及所需依赖。</p><button class="small-button dialog-primary" data-content-bundled-install disabled>安装所选</button></div></div>`;
}
document.addEventListener('toggle', event => {
  if (event.target.id !== 'content-bundled' || event.newState !== 'open') return;
  const trigger = document.querySelector('[data-content-bundled-open]');
  if (!trigger) return;
  const bounds = trigger.getBoundingClientRect();
  event.target.style.left = `${Math.max(12, Math.min(bounds.right - event.target.offsetWidth, innerWidth - event.target.offsetWidth - 12))}px`;
  event.target.style.top = `${Math.max(12, Math.min(bounds.bottom + 8, innerHeight - event.target.offsetHeight - 12))}px`;
}, true);
document.addEventListener('change', event => {
  if (!event.target.matches('#content-bundled input')) return;
  const popover = event.target.closest('#content-bundled');
  popover.querySelector('[data-content-bundled-install]').disabled = !popover.querySelector('input:checked:not(:disabled)');
});
document.addEventListener('click', event => {
  if (!event.target.closest('[data-content-bundled-install]')) return;
  const popover = document.querySelector('#content-bundled');
  const ids = [...popover.querySelectorAll('input:checked:not(:disabled)')].map(input => input.value);
  ids.push('fabric-api');
  for (const id of ids) {
    if (contentEntries().some(entry => entry.id === id)) continue;
    const mod = contentBundledMods.find(item => item.id === id) || {name:'Fabric API', version:'0.162.0+26.3'};
    contentEntries().push({id, name:mod.name, version:mod.version, file:`${id}-${mod.version}.jar`, enabled:true, icon:'mods'});
  }
  popover.hidePopover();
  renderPage();
  document.querySelector('[data-content-bundled-open]')?.focus();
  showToast('设计演示：已添加所选内置模组及依赖，未安装磁盘文件。');
});

function contentSearchDialog(trigger) {
  if (document.querySelector('dialog[open]') || contentNoInstances || !instances.length) return;
  const dialog = document.createElement('dialog');
  dialog.className = 'content-search-popup';
  dialog.setAttribute('aria-labelledby', 'content-search-title');
  dialog.innerHTML = `<header><div><h2 id="content-search-title">搜索${contentKinds[contentTab]}</h2><p>${escapeHtml(selected.name)} · ${contentKinds[contentTab]}</p></div><button class="icon-button" data-content-search-close aria-label="关闭搜索">${vectorIcon('close')}</button></header>
    <section class="content-search-controls"><div><span>搜索社区</span><strong>Modrinth</strong><span>${selected.version ? `游戏版本：${escapeHtml(selected.version)}` : '游戏版本：不限'}</span></div><input id="content-search" type="search" aria-label="搜索已安装内容或 Modrinth" placeholder="搜索${contentKinds[contentTab]}或在线查找" value="${escapeHtml(contentFilter)}"></section>
    <div id="content-search-results" role="list" aria-label="内容搜索结果">${contentRows(true)}</div>`;
  dialog.querySelector('[data-content-search-close]').addEventListener('click', () => dialog.close());
  dialog.addEventListener('keydown', event => {
    if (event.key !== 'Escape') return;
    event.preventDefault();
    event.stopPropagation();
    dialog.close();
  });
  dialog.addEventListener('close', () => { dialog.remove(); if (trigger.isConnected) trigger.focus(); });
  document.querySelector('.launcher').append(dialog);
  dialog.showModal();
  dialog.querySelector('input').focus();
}

function contentAddFiles(files) {
  if (!files.length) return;
  const entries = contentEntries();
  for (const file of files) {
    const original = file.name;
    let name = original;
    const dot = original.lastIndexOf('.');
    for (let suffix = 1; entries.some(item => item.file === name); suffix++) {
      name = dot > 0 ? `${original.slice(0, dot)}-${suffix}${original.slice(dot)}` : `${original}-${suffix}`;
    }
    entries.push({ id: `import-${++contentImportSerial}`, name, description: '本地导入 · 设计示例', version: '—', file: name, icon: 'fallback', enabled: !name.endsWith('.disabled') });
  }
  contentFilter = '';
  renderPage();
  showToast(`设计演示：${files.length} 个文件名已加入当前列表，未读取、复制或安装文件。`);
}

function contentRemoveDialog(id, trigger) {
  const item = contentEntries().find(entry => entry.id === id);
  if (!item) return;
  let removed = false;
  const dialog = document.createElement('dialog');
  dialog.className = 'instance-dialog content-remove-dialog';
  dialog.setAttribute('aria-labelledby', 'content-remove-title');
  dialog.innerHTML = `<header><div><h2 id="content-remove-title">移除${contentKinds[contentTab]}</h2><div class="instance-dialog-context">${escapeHtml(selected.name)}</div></div></header>
    <div class="instance-dialog-body"><div class="content-remove-summary">${contentIcon(item.icon)}<div><strong>${escapeHtml(item.name)}</strong><small>${escapeHtml(item.file)}</small></div></div><p>将此文件移出当前实例，文件保留在内容恢复目录。<br>不会删除存档、配置或导入时的原文件。</p></div>
    <footer><button class="small-button dialog-primary" data-content-confirm-remove>确认</button><button class="small-button subtle" data-content-cancel-remove>取消</button></footer>`;
  dialog.querySelector('[data-content-confirm-remove]').addEventListener('click', () => {
    const entries = contentEntries();
    entries.splice(entries.indexOf(item), 1);
    removed = true;
    dialog.close();
    showToast('设计演示：已从示例列表移除，未移动或删除磁盘文件。');
  });
  dialog.querySelector('[data-content-cancel-remove]').addEventListener('click', () => dialog.close());
  dialog.addEventListener('close', () => {
    dialog.remove();
    if (removed) {
      renderPage();
      (document.querySelector('[data-content-toggle]') || document.querySelector('[data-content-import]'))?.focus();
    } else if (trigger.isConnected) trigger.focus();
  });
  document.querySelector('.launcher').append(dialog);
  dialog.showModal();
  dialog.querySelector('[data-content-cancel-remove]').focus();
}

function contentSetScenario(scenario) {
  closeCombo();
  closeDrawer();
  contentNoInstances = scenario === 'no-instances';
  contentDragPreview = scenario === 'drag';
  contentFilter = '';
  contentTab = 'mods';
  if (scenario === 'empty') {
    selected = instances.find(instance => !instance.mods) || selected;
    contentCollections.set(`${selected.id}:mods`, []);
  }
  else if (!contentNoInstances) selected = instances.find(instance => instance.mods > 0) || selected;
  if ((scenario === 'installed' || scenario === 'drag') && !contentEntries().length)
    contentCollections.set(`${selected.id}:mods`, contentSampleItems.mods.map(item => ({ ...item })));
  navigate('content');
  render(false);
}

function contentSyncReview() {
  if (document.body.classList.contains('account-review-mode')) return;
  const active = currentPage === 'content';
  document.querySelector('.review-content-states').hidden = !active;
  document.querySelector('.review-states').hidden = active;
  document.querySelector('.review-bar strong').textContent = active ? '07 / CONTENT' : '06 / WALLPAPERS';
  document.querySelector('.review-footer > span').textContent = active
    ? '内容设计评审：行更多提供更新、固定、通道与移除；分类更多可检查更新。全部为模拟数据。'
    : '点击右侧切换实例；点击「开始游戏」演示准备 → 下载 → 运行。状态按钮可直达评审画面。';
  if (!active) {
    if (currentPage !== 'instances') contentNoInstances = false;
    contentDragPreview = false;
    contentDragDepth = 0;
    document.querySelector('.review-note').textContent = '交互原型 · 模拟数据';
    return;
  }
  document.querySelector('.review-note').textContent = '内容设计评审 · 模拟数据';
  const scenario = contentNoInstances ? 'no-instances' : contentDragPreview ? 'drag' : contentEntries().length ? 'installed' : 'empty';
  document.querySelectorAll('[data-content-scenario]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.contentScenario === scenario)));
}

function contentSyncDrag() {
  const collection = document.querySelector('.content-collection');
  if (collection) collection.dataset.dragActive = String(contentDragPreview || contentDragDepth > 0);
}

document.addEventListener('click', event => {
  const button = event.target.closest('button');
  if (!button) return;
  if (button.dataset.contentScenario) { contentSetScenario(button.dataset.contentScenario); return; }
  if (button.hasAttribute('data-content-search-open')) { contentSearchDialog(button); return; }
  if (button.dataset.contentTab) contentFilter = '';
  if (button.dataset.contentAddCatalog) {
    const item = contentCatalog().find(entry => entry.id === button.dataset.contentAddCatalog);
    if (item && !contentEntries().some(entry => entry.id === item.id)) {
      contentDownloadOpen(item);
    }
  }
  if (button.dataset.contentToggle) {
    const item = contentEntries().find(entry => entry.id === button.dataset.contentToggle);
    if (!item) return;
    if (contentTab === 'mods') item.enabled = !item.enabled;
    else {
      item.selected = !item.selected;
      if (contentTab === 'packs' && item.selected) { contentEntries().forEach(other => { if (other !== item && other.selected) other.priority = (other.priority || 1) + 1; }); item.priority = 1; }
      if (contentTab === 'shaders' && item.selected) contentEntries().forEach(other => { if (other !== item) other.selected = false; });
    }
    contentUpdateList();
  }
  if (button.dataset.contentRemove) contentRemoveDialog(button.dataset.contentRemove, button);
  if (button.hasAttribute('data-content-import')) el('content-import-files').click();
  if (button.hasAttribute('data-content-clear-filter')) { contentFilter = ''; el('content-search').value = ''; contentUpdateList(); el('content-search').focus(); }
  if (button.hasAttribute('data-content-create-instance')) openInstanceEntry('create');
  if (button.hasAttribute('data-content-import-instance')) openInstanceEntry('import');
});
document.addEventListener('input', event => {
  if (event.target.id === 'content-search') { contentFilter = event.target.value; contentUpdateList(); }
});
document.addEventListener('change', event => {
  if (event.target.id === 'content-import-files') contentAddFiles([...event.target.files]);
  if (event.target.id === 'page-instance') { contentFilter = ''; contentDragPreview = false; }
});
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && currentPage === 'content') { contentDragPreview = false; contentDragDepth = 0; contentSyncDrag(); contentSyncReview(); }
});
document.addEventListener('dragenter', event => {
  if (currentPage !== 'content' || !event.dataTransfer?.types.includes('Files')) return;
  event.preventDefault();
  if (!contentNoInstances && !document.querySelector('dialog[open]')) { contentDragDepth++; contentSyncDrag(); }
});
document.addEventListener('dragover', event => {
  if (currentPage !== 'content' || !event.dataTransfer?.types.includes('Files')) return;
  event.preventDefault();
  event.dataTransfer.dropEffect = contentNoInstances || document.querySelector('dialog[open]') ? 'none' : 'copy';
});
document.addEventListener('dragleave', () => {
  contentDragDepth = Math.max(0, contentDragDepth - 1);
  contentSyncDrag();
});
document.addEventListener('drop', event => {
  if (currentPage !== 'content' || !event.dataTransfer?.types.includes('Files')) return;
  event.preventDefault();
  contentDragDepth = 0;
  contentDragPreview = false;
  contentSyncDrag();
  if (contentNoInstances || !instances.length) { showToast('请先添加一个实例，再导入内容。'); return; }
  if (document.querySelector('dialog[open]')) { showToast('请先关闭当前对话框，再添加文件。'); return; }
  if ([...event.dataTransfer.items].some(item => item.webkitGetAsEntry?.()?.isDirectory)) { showToast('此设计稿演示文件拖入，不读取文件夹内容。'); return; }
  contentAddFiles([...event.dataTransfer.files]);
});
document.addEventListener('dragend', () => { contentDragDepth = 0; contentSyncDrag(); });
window.addEventListener('prototype-page-changed', contentSyncReview);
window.addEventListener('prototype-theme-changed', contentSyncReview);
window.addEventListener('DOMContentLoaded', () => {
  const query = new URLSearchParams(location.search);
  if (query.get('page') === 'content') contentSetScenario(query.get('contentState') || 'installed');
});
