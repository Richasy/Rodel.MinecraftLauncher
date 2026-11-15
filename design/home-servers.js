/* Home review only. Reuses fictional favorites; no network, credentials, files or game processes. */
let homeServerMode = new URLSearchParams(location.search).get('target') === 'server';
let homeServerId = serverFavorites[0]?.id ?? '';
const homeServerAttempts = new Map();
window.renderHomeTargets = function () {
  document.querySelector('.launcher').dataset.homeTarget = homeServerMode ? 'server' : 'local';
  const panel = document.querySelector('.instances');
  let segments = panel.querySelector('.home-target-segments');
  if (!segments) {
    segments = document.createElement('div');
    segments.className = 'home-target-segments instance-display-segmented';
    segments.setAttribute('role', 'tablist'); segments.setAttribute('aria-label', '首页启动目标');
    panel.querySelector('.section-heading').after(segments);
  }
  segments.innerHTML = [['local','本地实例'],['server','服务器']].map(([id,label]) => `<button role="tab" data-home-target="${id}" aria-selected="${homeServerMode === (id === 'server')}" tabindex="${homeServerMode === (id === 'server') ? 0 : -1}">${label}</button>`).join('');
  panel.querySelector('h2').textContent = '我的游戏';
  const manage = panel.querySelector('.section-heading > button');
  manage.removeAttribute('data-panel'); manage.dataset.homeManage = '';
  const add = panel.querySelector('.add-instance');
  add.removeAttribute('data-panel'); add.dataset.homeAdd = '';
  panel.querySelector('.count').textContent = String(homeServerMode ? serverFavorites.length : instances.filter(instance => !instance.serverId).length).padStart(2, '0');
  add.innerHTML = `${vectorIcon('plus')} ${homeServerMode ? '添加服务器' : '添加实例'}`;
  add.classList.toggle('home-add-accent', homeServerMode ? serverFavorites.length === 0 : !instances.some(instance => !instance.serverId));
  clearPackDropHints();
  if (!homeServerMode) {
    document.querySelector('.eyebrow').innerHTML = `<span class="tiny-block"></span> ${instances.some(instance => !instance.serverId && instance.id === selected?.id) ? '当前实例' : '本地实例'} <span class="pixel-label">/ JAVA EDITION</span>`;
    el('instance-copy').removeAttribute('dir');
    return;
  }
  el('instance-list').innerHTML = serverFavorites.length ? serverFavorites.map(server => `<button class="instance-card home-server-card" data-home-server="${server.id}" aria-pressed="${homeServerId === server.id}">${serverArtwork(server)}<span class="home-server-copy"><strong>${escapeHtml(server.name)}</strong><small dir="ltr">${escapeHtml(server.address)}</small></span>${serverDot(server)}</button>`).join('') : homeEmptyMarkup(true);
  const server = serverFavorites.find(item => item.id === homeServerId);
  const instance = server && instances.find(item => item.id === (server.preferred ?? (server.instanceIds.length === 1 ? server.instanceIds[0] : null)));
  document.querySelector('.eyebrow').innerHTML = `<span class="tiny-block"></span> ${server ? '当前服务器' : '服务器'} <span class="pixel-label">/ MULTIPLAYER</span>`;
  document.querySelector('.eyebrow').hidden = false;
  el('instance-title').textContent = server?.name ?? '开启新冒险';
  el('instance-copy').hidden = false;
  el('instance-copy').textContent = server?.address ?? '从一个服务器地址，连接更大的世界。';
  if (server) el('instance-copy').dir = 'ltr'; else el('instance-copy').removeAttribute('dir');
  el('runtime').textContent = instance ? `客户端：${instance.name} · ${instance.version} · ${instance.loader}` : server ? '首次连接时选择客户端实例' : '';
  el('runtime').hidden = !server;
  const attempt = homeServerAttempts.get(homeServerId) ?? 'idle';
  const label = !server ? '连接服务器' : attempt === 'preparing' ? '正在准备启动' : attempt === 'running' ? '游戏运行中' : attempt === 'failed' || attempt === 'canceled' ? '重试连接' : instance ? '连接服务器' : '选择客户端并连接';
  const busy = attempt === 'preparing' || attempt === 'running';
  el('launch-area').innerHTML = `<div class="launch-actions"><button class="launch-button" id="launch" ${!server || busy ? 'disabled' : ''}><span class="button-face">${vectorIcon('play')}<strong>${label}</strong><span class="key-hint">↵</span></span></button><button class="secondary-button" data-home-configure aria-label="服务器连接设置" title="服务器连接设置" ${!server || busy ? 'disabled' : ''}>${vectorIcon('wrench')}</button></div>
    ${instance ? `<p class="launch-caption">沿用实例身份 · Steve（离线）<span class="caption-separator">/</span>Java ${instance.java}<span class="caption-separator">/</span>${instance.memory} GB 内存</p>` : ''}
    ${attempt !== 'idle' ? `<section class="home-server-feedback" role="status"><strong>${({preparing:'正在准备游戏',canceled:'准备已取消',failed:'连接准备未完成',running:'游戏已启动，连接结果请在游戏中查看。'})[attempt]}</strong>${attempt === 'preparing' ? '<button class="text-button" data-home-cancel>取消准备</button><div class="server-progress"></div><aside class="server-review-tools">设计评审 · 非产品界面<div><button data-home-outcome="failed">模拟准备失败</button><button data-home-outcome="running">模拟进程启动</button></div></aside>' : ''}</section>` : ''}`;
};
document.addEventListener('click', event => {
  const button = event.target.closest('button'); if (!button) return;
  if (button.dataset.homeTarget) {
    event.stopImmediatePropagation(); homeServerMode = button.dataset.homeTarget === 'server'; render(false);
    document.querySelector(`[data-home-target="${homeServerMode ? 'server' : 'local'}"]`)?.focus(); return;
  }
  if (button.dataset.homeServer) { event.stopImmediatePropagation(); homeServerId = button.dataset.homeServer; render(false); return; }
  if (button.hasAttribute('data-home-manage')) {
    event.stopImmediatePropagation(); serverClientContext = null; instancePageSection = homeServerMode ? 'servers' : 'local'; navigate('instances'); return;
  }
  if (button.hasAttribute('data-home-add')) {
    event.stopImmediatePropagation(); if (homeServerMode) openServerDialog('add', null, button); else openInstanceEntry('create'); return;
  }
  if (button.dataset.homeOutcome) { event.stopImmediatePropagation(); homeServerAttempts.set(homeServerId, button.dataset.homeOutcome); render(false); return; }
  if (button.hasAttribute('data-home-cancel')) { event.stopImmediatePropagation(); homeServerAttempts.set(homeServerId, 'canceled'); render(false); return; }
  if (homeServerMode && (button.id === 'launch' || button.hasAttribute('data-home-configure'))) {
    event.stopImmediatePropagation();
    const server = serverFavorites.find(item => item.id === homeServerId); if (!server) return;
    const instance = instances.find(item => item.id === (server.preferred ?? (server.instanceIds.length === 1 ? server.instanceIds[0] : null)));
    if (button.hasAttribute('data-home-configure') || !instance) { openServerDialog('connect', server, button); return; }
    if (instance.packFormat === 'hmcl' && homeServerAttempts.get(homeServerId) !== 'preparing') {
      selected = instance;
      const targetId = homeServerId;
      openPackMaintenanceReview(() => { homeServerAttempts.set(targetId, 'preparing'); render(false); }); return;
    }
    homeServerAttempts.set(homeServerId, homeServerAttempts.get(homeServerId) === 'preparing' ? 'canceled' : 'preparing'); render(false);
  }
}, true);
document.addEventListener('keydown', event => {
  if (homeServerMode && currentPage === 'home' && event.target === document.body && event.key === 'Enter' && !document.querySelector('dialog[open]')) {
    event.preventDefault(); event.stopImmediatePropagation(); document.querySelector('#launch:not(:disabled)')?.click(); return;
  }
  if (!event.target.matches('[data-home-target]') || !['ArrowLeft','ArrowRight','Home','End'].includes(event.key)) return;
  event.preventDefault();
  const target = event.key === 'Home' ? 'local' : event.key === 'End' ? 'server' : homeServerMode ? 'local' : 'server';
  document.querySelector(`[data-home-target="${target}"]`).click();
}, true);
window.addEventListener('prototype-page-changed', window.renderHomeTargets);
window.renderHomeTargets();
if (homeServerMode) navigate('home');
