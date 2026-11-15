/* HTML review only. Fictional favorites; no network, file, account or game operations. */
const serverSamples = () => [
  { id: 'survival', name: '朋友的生存服', address: 'play.example.net', online: true, icon: 'grass', version: '1.21.1', players: '3 / 20', latency: '42 ms', instanceIds: [instances[0].id], preferred: instances[0].id },
  { id: 'local', name: '本机测试服', address: '127.0.0.1:25578', online: true, icon: 'redstone', version: '26.3', players: '0 / 8', latency: '1 ms', instanceIds: [] },
  { id: 'unavailable', name: '周末一起玩', address: '[::1]:25579', online: false, instanceIds: [instances[1].id, instances[2].id] }
];
let serverFavorites = serverSamples();
let serverDialog, serverMenu, serverTrigger, serverTarget, serverView, serverAttempt = 'idle';
let serverDraft = {};
const serverIcon = name => {
  const path = { servers: 'M3 3h18v7H3zM3 14h18v7H3zM7 6v1M7 17v1M11 6h6M11 17h6', plus: 'M12 4v16M4 12h16', refresh: 'M20 7a9 9 0 1 0 1 8M20 2v6h-6', edit: 'm15 4 5 5M4 15 15 4l5 5L9 20H4z', link: 'm8 16 8-8M9 7l2-2a5 5 0 0 1 7 7l-2 2M15 17l-2 2a5 5 0 0 1-7-7l2-2', trash: 'M3 6h18M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7m4-7v7', play: 'm7 3 14 9-14 9z' }[name];
  return `<span class="server-icon" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">${name === 'more' ? '<circle cx="4" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="20" cy="12" r="1"/>' : `<path d="${path}"/>`}</svg></span>`;
};
function serverArtwork(server) {
  return `<span class="server-favicon" aria-hidden="true">${server.icon ? cubeIcon(server.icon) : serverIcon('servers')}</span>`;
}
function serverDot(server) {
  const status = server.online === true ? 'online' : server.online === false ? 'unavailable' : 'unknown';
  const label = status === 'online' ? '在线' : status === 'unavailable' ? '状态暂不可用，仍可尝试连接' : '尚未查询';
  return `<span class="server-online-dot ${status}" role="img" aria-label="${label}" title="${label}"></span>`;
}
function serverPage() {
  const row = server => {
    const preferred = instances.find(instance => instance.id === server.preferred);
    const count = server.instanceIds.length;
    return `<article class="server-row" aria-label="${escapeHtml(server.name)}">
      <div class="server-identity">${serverArtwork(server)}<div class="server-copy"><strong title="${escapeHtml(server.name)}">${escapeHtml(server.name)}</strong><small class="server-address" dir="ltr">${escapeHtml(server.address)}</small></div></div>
      <div class="server-copy server-status-copy"><span class="server-state ${server.online ? 'online' : 'unavailable'}">${server.online ? '在线' : '状态查询失败'}</span><small>${server.online ? `${server.version} · ${server.players} · ${server.latency}` : '仍可尝试连接'}</small></div>
      <div class="server-copy server-instance"><span>${escapeHtml(preferred?.name ?? (count === 1 ? instances.find(i => i.id === server.instanceIds[0])?.name : count ? `${count} 个关联实例` : '连接时选择实例'))}</span><small>${preferred ? '首选实例' : '不自动安装或更新内容'}</small></div>
      <div class="server-actions"><button class="small-button subtle" data-server-connect="${server.id}">连接</button><button class="server-more" data-server-more="${server.id}" aria-label="${escapeHtml(server.name)} 的更多操作" aria-haspopup="menu">${serverIcon('more')}</button></div>
    </article>`;
  };
  return `<header class="server-heading"><h1>实例</h1><div class="server-actions"><button class="small-button subtle" data-server-action="direct">直接连接</button><button class="small-button" data-server-action="add">${serverIcon('plus')}添加服务器</button></div></header>
    <section class="page-surface acrylic server-list" aria-label="收藏的服务器">
      ${serverFavorites.length ? '<div class="server-list-header"><span>名称 / 地址</span><span>服务器状态</span><span>客户端实例</span><span>操作</span></div>' : ''}
      <div class="server-rows">${serverFavorites.length ? serverFavorites.map(row).join('') : homeEmptyMarkup(true)}</div>
      <footer class="server-list-footer"><span>${serverFavorites.length} 个收藏</span><button class="small-button subtle" data-server-action="refresh">${serverIcon('refresh')}刷新状态</button></footer>
    </section>`;
}
function closeServerMenu(restore = false) {
  serverMenu?.remove(); serverMenu = null;
  if (restore && serverTrigger?.isConnected) serverTrigger.focus();
}
function closeServerDialog() {
  if (serverAttempt === 'preparing') return;
  closeCombo(); serverDialog?.close(); serverDialog?.remove(); serverDialog = null;
  (serverTrigger?.isConnected ? serverTrigger : document.querySelector('[data-server-action="add"]'))?.focus();
}
function positionServerDialog() {
  if (!serverDialog) return;
  const bounds = document.querySelector('.launcher').getBoundingClientRect();
  serverDialog.style.left = `${bounds.left + bounds.width / 2}px`;
  serverDialog.style.top = `${bounds.top + bounds.height / 2}px`;
  serverDialog.style.maxHeight = `${Math.max(160, bounds.height - 48)}px`;
}
function openServerDialog(view, target, trigger) {
  if (document.querySelector('dialog[open]')) return;
  closeServerMenu(); closeCombo();
  serverTrigger = trigger; serverTarget = target; serverView = view; serverAttempt = 'idle';
  serverDraft = { name: target?.name ?? '', address: target?.address ?? '', instance: target?.preferred ?? (target?.instanceIds.length === 1 ? target.instanceIds[0] : ''), account: 'configured', remember: false, associations: [...(target?.instanceIds ?? [])], preferred: target?.preferred ?? '' };
  serverDialog = document.createElement('dialog'); serverDialog.className = 'server-dialog';
  serverDialog.setAttribute('aria-labelledby', 'server-dialog-title');
  serverDialog.addEventListener('submit', event => { event.preventDefault(); serverDialog.querySelector('[data-server-submit]:not(:disabled)')?.click(); });
  serverDialog.addEventListener('cancel', event => { event.preventDefault(); if (serverAttempt === 'preparing') { serverAttempt = 'canceled'; renderServerDialog(); } else closeServerDialog(); });
  document.querySelector('.launcher').append(serverDialog); renderServerDialog(); serverDialog.showModal(); positionServerDialog();
  serverDialog.querySelector('input, .combo-face, button')?.focus();
}
function renderServerDialog() {
  closeCombo();
  const field = (name, id, value) => `<label class="server-field">${name}<input id="${id}" value="${escapeHtml(value)}" ${id === 'server-address' ? 'dir="ltr" spellcheck="false"' : ''}></label>`;
  const instanceOptions = instances.filter(i => !i.serverId || (serverView !== 'claim' && i.serverId === serverTarget?.id))
    .map(i => `<option value="${i.id}" ${serverDraft.instance === i.id ? 'selected' : ''}>${escapeHtml(i.name)} · ${i.version} · ${i.loader}</option>`).join('');
  let title, body, primary;
  if (serverView === 'add' || serverView === 'edit') {
    title = serverView === 'add' ? '添加服务器' : '编辑服务器'; primary = '保存';
    body = `<form>${field('名称', 'server-name', serverDraft.name)}${field('服务器地址', 'server-address', serverDraft.address)}<p class="server-dialog-note">域名、IP 或 [IPv6]:端口。这里保存游戏连接地址，不是整合包下载地址。</p></form>`;
  } else if (serverView === 'remove') {
    title = '移除服务器收藏'; primary = '移除收藏';
    body = `<p>移除「${escapeHtml(serverTarget.name)}」？</p><p class="server-dialog-note">移除收藏及关联，所属客户端转为本地实例；不删除游戏文件或存档。</p>`;
  } else if (serverView === 'claim') {
    title = '添加已有客户端'; primary = '移入此服务器';
    body = `<p class="server-dialog-note">所选本地实例将归此服务器管理，启动时默认连接此服务器。不复制或移动游戏文件。</p><label class="server-field">客户端实例<select id="server-instance"><option value="">选择已有实例</option>${instanceOptions}</select></label>`;
  } else if (serverView === 'associate') {
    title = '关联客户端实例'; primary = '保存';
    body = `<p class="server-dialog-note">${escapeHtml(serverTarget.name)} · 仅保存选择，不更改实例的游戏版本或内容。</p><form>${instances.map(i => `<label class="server-check"><input type="checkbox" data-server-association="${i.id}" ${serverDraft.associations.includes(i.id) ? 'checked' : ''}><span>${escapeHtml(i.name)} · ${i.version} · ${i.loader}</span></label>`).join('')}<label class="server-field">首选实例<select id="server-preferred"><option value="">每次选择</option>${instances.filter(i => serverDraft.associations.includes(i.id)).map(i => `<option value="${i.id}" ${serverDraft.preferred === i.id ? 'selected' : ''}>${escapeHtml(i.name)}</option>`).join('')}</select></label></form>`;
  } else {
    title = serverTarget ? `连接 ${serverTarget.name}` : '直接连接'; primary = serverAttempt === 'failed' || serverAttempt === 'canceled' ? '重试连接' : '连接';
    const busy = serverAttempt === 'preparing' || serverAttempt === 'running';
    body = `<form><fieldset ${busy ? 'disabled' : ''} style="border:0;padding:0;margin:0;display:contents">
      ${serverTarget ? `<p class="server-address" dir="ltr">${escapeHtml(serverDraft.address)}</p>` : field('服务器地址', 'server-address', serverDraft.address)}
      ${serverTarget?.online === false ? '<p class="server-dialog-note">服务器状态查询失败，仍可尝试连接。</p>' : ''}
      <label class="server-field">客户端实例<select id="server-instance"><option value="">选择已有实例</option>${instanceOptions}</select></label>
      <label class="server-field">本次身份<select id="server-account"><option value="configured" ${serverDraft.account === 'configured' ? 'selected' : ''}>沿用实例身份 · Steve（离线）</option><option value="microsoft" ${serverDraft.account === 'microsoft' ? 'selected' : ''}>RodelExplorer（微软）</option><option value="offline" ${serverDraft.account === 'offline' ? 'selected' : ''}>Steve（离线）</option></select></label>
      ${serverTarget ? `<label class="server-check"><input id="server-remember" type="checkbox" ${serverDraft.remember ? 'checked' : ''}>将此实例记为该服务器的首选</label>` : ''}
      </fieldset></form>`;
    if (serverAttempt === 'preparing') body += '<div class="server-progress" role="progressbar" aria-label="正在准备游戏"></div>';
    if (serverAttempt === 'failed') body += '<p class="server-dialog-error" role="alert">游戏准备失败。请检查实例的 Java 配置后重试；本次选择已保留。</p>';
    if (serverAttempt === 'preparing') body += '<aside class="server-review-tools">设计评审 · 非产品界面<div><button data-server-outcome="failed">模拟准备失败</button><button data-server-outcome="running">模拟进程启动</button></div></aside>';
  }
  serverDialog.innerHTML = `<header><h2 id="server-dialog-title">${escapeHtml(title)}</h2></header><div class="server-dialog-body">${body}<p id="server-validation" class="server-dialog-error" role="alert" hidden></p></div><footer>${['preparing', 'running'].includes(serverAttempt) ? '' : `<button class="small-button server-primary" data-server-submit>${primary}</button>`}<button class="small-button subtle" data-server-close>${serverAttempt === 'preparing' ? '取消准备' : serverAttempt === 'running' ? '关闭' : '取消'}</button></footer>`;
  upgradeCombos(serverDialog); validateServerDraft(); positionServerDialog();
}
function validServerAddress(value) {
  value = value.trim();
  if (!value || /[\s/\\]/.test(value)) return false;
  if (value.startsWith('[')) return /^\[[0-9a-f:]+\](?::\d+)?$/i.test(value) && (!value.includes(']:') || +value.split(']:')[1] > 0 && +value.split(']:')[1] <= 65535);
  if ((value.match(/:/g) ?? []).length > 1) return /^[0-9a-f:]+$/i.test(value);
  const parts = value.split(':'); return !!parts[0] && (parts.length === 1 || /^\d+$/.test(parts[1]) && +parts[1] > 0 && +parts[1] <= 65535);
}
function validateServerDraft() {
  const submit = serverDialog?.querySelector('[data-server-submit]'); if (!submit) return;
  submit.disabled = ['add', 'edit'].includes(serverView) ? !serverDraft.name.trim() || !validServerAddress(serverDraft.address) : serverView === 'claim' ? !serverDraft.instance : ['connect', 'direct'].includes(serverView) ? !serverDraft.instance || !validServerAddress(serverDraft.address) : false;
}
document.addEventListener('input', event => {
  if (!serverDialog?.contains(event.target)) return;
  if (event.target.id === 'server-name') serverDraft.name = event.target.value;
  if (event.target.id === 'server-address') serverDraft.address = event.target.value;
  validateServerDraft();
});
document.addEventListener('change', event => {
  if (!serverDialog?.contains(event.target)) return;
  const id = event.target.id;
  if (id === 'server-instance') serverDraft.instance = event.target.value;
  if (id === 'server-account') serverDraft.account = event.target.value;
  if (id === 'server-remember') serverDraft.remember = event.target.checked;
  if (id === 'server-preferred') serverDraft.preferred = event.target.value;
  if (event.target.dataset.serverAssociation) {
    const instance = event.target.dataset.serverAssociation;
    serverDraft.associations = event.target.checked ? [...serverDraft.associations, instance] : serverDraft.associations.filter(i => i !== instance);
    if (!serverDraft.associations.includes(serverDraft.preferred)) serverDraft.preferred = '';
    renderServerDialog(); serverDialog.querySelector(`[data-server-association="${instance}"]`)?.focus();
  }
  validateServerDraft();
});
document.addEventListener('click', event => {
  const button = event.target.closest('button'); if (!button) { if (!serverMenu?.contains(event.target)) closeServerMenu(); return; }
  if (button.dataset.instanceView) {
    serverClientContext = null;
    const scroll = document.querySelector(instancePageSection === 'servers' ? '.server-rows' : '.instance-list-scroll');
    if (scroll) instanceSectionScrolls[instancePageSection] = scroll.scrollTop;
    instancePageSection = button.dataset.instanceView; renderPage();
    document.getElementById(`instance-view-${instancePageSection}`).focus(); return;
  }
  const id = button.dataset.serverConnect ?? button.dataset.serverMore;
  const target = serverFavorites.find(s => s.id === id);
  if (button.hasAttribute('data-server-connect')) { openServerDialog('connect', target, button); return; }
  if (button.hasAttribute('data-server-more')) {
    closeServerMenu(); serverTarget = target; serverTrigger = button;
    serverMenu = document.createElement('div'); serverMenu.className = 'server-menu'; serverMenu.setAttribute('role', 'menu');
    serverMenu.innerHTML = [['clients', '管理客户端'], ['claim', '添加已有客户端'], ['edit', '编辑服务器'], ['remove', '移除收藏']].map(([action, text]) => `<button role="menuitem" data-server-action="${action}">${serverIcon(action === 'clients' || action === 'claim' ? 'link' : action === 'remove' ? 'trash' : 'edit')}${text}</button>`).join('');
    document.body.append(serverMenu); const bounds = button.getBoundingClientRect();
    serverMenu.style.left = `${Math.min(bounds.right - serverMenu.offsetWidth, innerWidth - serverMenu.offsetWidth - 8)}px`;
    serverMenu.style.top = `${Math.min(bounds.bottom + 4, innerHeight - serverMenu.offsetHeight - 8)}px`;
    serverMenu.querySelector('button').focus(); return;
  }
  const action = button.dataset.serverAction;
  if (button.hasAttribute('data-server-clients-back')) { serverClientContext = null; instancePageSection = 'servers'; renderPage(); return; }
  if (action === 'clients') { serverClientContext = serverTarget; instancePageSection = 'local'; closeServerMenu(); renderPage(); return; }
  if (action === 'add' || action === 'direct') openServerDialog(action, null, button);
  else if (['edit', 'associate', 'remove', 'claim'].includes(action)) openServerDialog(action, serverTarget, serverTrigger);
  else if (action === 'refresh') showToast('设计稿不查询网络；状态仅用于界面评审。');
  else if (button.hasAttribute('data-server-close')) { if (serverAttempt === 'preparing') { serverAttempt = 'canceled'; renderServerDialog(); serverDialog.querySelector('[data-server-submit]').focus(); } else closeServerDialog(); }
  else if (button.dataset.serverOutcome) { serverAttempt = button.dataset.serverOutcome; renderServerDialog(); serverDialog.querySelector('footer button').focus(); }
  else if (button.hasAttribute('data-server-submit') && !button.disabled) {
    if (serverView === 'add') serverFavorites.push({ id: `favorite-${Date.now()}`, name: serverDraft.name.trim(), address: serverDraft.address.trim(), online: false, instanceIds: [] });
    else if (serverView === 'edit') Object.assign(serverTarget, { name: serverDraft.name.trim(), address: serverDraft.address.trim() });
    else if (serverView === 'remove') {
      instances.filter(instance => instance.serverId === serverTarget.id).forEach(instance => delete instance.serverId);
      serverFavorites = serverFavorites.filter(s => s !== serverTarget);
    }
    else if (serverView === 'claim') {
      instances.find(instance => instance.id === serverDraft.instance).serverId = serverTarget.id;
      serverTarget.instanceIds = [...new Set([...serverTarget.instanceIds, serverDraft.instance])];
      serverTarget.preferred ??= serverDraft.instance;
      renderInstances();
    }
    else if (serverView === 'associate') Object.assign(serverTarget, { instanceIds: [...serverDraft.associations], preferred: serverDraft.preferred });
    else {
      if (serverDraft.remember && serverTarget) { serverTarget.instanceIds = [...new Set([...serverTarget.instanceIds, serverDraft.instance])]; serverTarget.preferred = serverDraft.instance; }
      const client = instances.find(instance => instance.id === serverDraft.instance);
      if (client?.packFormat === 'hmcl') {
        selected = client; closeServerDialog();
        openPackMaintenanceReview(() => { serverAttempt = 'preparing'; renderPage(); }); return;
      }
      serverAttempt = 'preparing'; renderServerDialog(); serverDialog.querySelector('[data-server-close]').focus(); return;
    }
    closeServerDialog(); renderPage(); document.querySelector('[data-server-action="add"]').focus();
  }
  if (!serverMenu?.contains(button)) closeServerMenu();
});
document.addEventListener('keydown', event => {
  if (event.target.matches('[data-instance-view]') && ['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
    event.preventDefault();
    const next = event.key === 'Home' ? 'local' : event.key === 'End' ? 'servers' : instancePageSection === 'local' ? 'servers' : 'local';
    document.getElementById(`instance-view-${next}`).click(); return;
  }
  if (!serverMenu) return;
  const items = [...serverMenu.querySelectorAll('button')], index = items.indexOf(document.activeElement);
  if (event.key === 'Escape') { event.preventDefault(); closeServerMenu(true); }
  else if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) { event.preventDefault(); items[event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1 : (index + (event.key === 'ArrowUp' ? -1 : 1) + items.length) % items.length].focus(); }
  else if (event.key === 'Tab') closeServerMenu();
});
window.addEventListener('resize', () => { closeServerMenu(); positionServerDialog(); });
window.addEventListener('prototype-page-changed', () => {
  closeServerMenu();
  const active = currentPage === 'instances' && instancePageSection === 'servers';
  if (document.querySelector('.server-review-scenarios')) document.querySelector('.server-review-scenarios').hidden = !active;
  if (active) {
    document.querySelector('.review-note').textContent = '服务器设计稿 · 不联网、不启动游戏';
    document.querySelector('.review-bar strong').textContent = '09 / SERVERS';
    document.querySelector('.review-states').hidden = true;
    document.querySelector('.review-footer > span').textContent = '服务器界面审阅稿；所有记录、状态及连接结果均为内存模拟，不作为真实业务证据。';
  }
});
window.addEventListener('prototype-theme-changed', () => {
  if (currentPage === 'instances' && instancePageSection === 'servers') document.querySelector('.review-note').textContent = '服务器设计稿 · 不联网、不启动游戏';
});
if (new URLSearchParams(location.search).get('page') === 'servers' || new URLSearchParams(location.search).get('view') === 'servers') {
  document.querySelector('.review-bar strong').textContent = '09 / SERVERS';
  document.querySelector('.review-states').hidden = true;
  document.querySelector('.review-footer > span').textContent = '服务器界面审阅稿；所有记录、状态及连接结果均为内存模拟，不作为真实业务证据。';
  const scenarios = document.createElement('div'); scenarios.className = 'server-review-scenarios';
  scenarios.innerHTML = '<button data-server-scenario="normal">收藏列表</button><button data-server-scenario="empty">空列表</button><button data-server-scenario="long">长列表</button>';
  document.querySelector('.review-bar').append(scenarios);
  scenarios.addEventListener('click', event => {
    if (serverDialog) return;
    const choice = event.target.dataset.serverScenario; if (!choice) return;
    serverFavorites = choice === 'empty' ? [] : serverSamples();
    if (choice === 'long') serverFavorites = Array.from({ length: 18 }, (_, i) => ({ ...serverSamples()[i % 3], id: `long-${i}`, name: `朋友的长期生存服务器 · 周末探索 ${i + 1}` }));
    navigate('servers');
  });
  navigate('servers');
}
