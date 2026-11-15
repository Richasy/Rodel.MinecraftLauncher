/* Presentation-only state model. No filesystem, network, Core or process calls. */
const iconPaths = {
  folder:'M3 6 H10 L12 8 H21 V20 H3 Z M3 6 V4 H9 L11 6 H19 V8',
  success:'M12 3 A9 9 0 1 0 12 21 A9 9 0 0 0 12 3 M7 12 L10.5 15.5 L17 9',
  refresh:'M20 7v5h-5M4 17v-5h5M5.1 8a7 7 0 0 1 11.6-3L20 8M4 16l3.3 3a7 7 0 0 0 11.6-3',
  more:'M5 12h.01M12 12h.01M19 12h.01',
  chevron:'M6 9l6 6 6-6',
  close:'M6 6l12 12M18 6L6 18',
  instances:'M12 2l9 5v10l-9 5-9-5V7zM3 7l9 5 9-5M12 12v10',
  home:'M3 11L12 3l9 8M5 10v11h14V10M9 21v-7h6v7',
  content:'M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z',
  download:'M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5',
  logs:'M6 3h9l4 4v14H6zM14 3v5h5M9 12h7M9 16h7',
  settings:'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8M10 2h4l1 3 3 1 3-1 2 4-2 2v3l2 2-2 4-3-1-3 1-1 3h-4l-1-3-3-1-3 1-2-4 2-2v-3L1 9l2-4 3 1 3-1z',
  sliders:'M4 6h16M4 12h16M4 18h16M8 3v6M16 9v6M10 15v6',
  wrench:'M19 2 L15 6 V9 H18 L22 5 C23 9 20 13 16 12 L8 20 C5 23 1 19 4 16 L12 8 C11 4 15 1 19 2 Z',
  plus:'M12 4v16M4 12h16',
  spark:'M12 3l2.5 6.5L21 12l-6.5 2.5L12 21l-2.5-6.5L3 12l6.5-2.5z'
};
function vectorIcon(name) {
  if(name==='avatar')return '<svg viewBox="0 0 8 8" aria-hidden="true"><path fill="#533c29" d="M0 0h8v3H0z"/><path fill="#b78d64" d="M0 3h8v5H0z"/><path fill="#e5ceb0" d="M1 4h2v1H1zM5 4h2v1H5z"/><path fill="#6c6893" d="M2 4h1v1H2zM5 4h1v1H5z"/><path fill="#765134" d="M3 5h2v1H3zM2 6h4v1H2z"/></svg>';
  if(name==='play')return '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M7 4l14 8-14 8z"/></svg>';
  return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round" stroke-linecap="round" aria-hidden="true"><path d="${iconPaths[name]||iconPaths.home}"/></svg>`;
}
function cubeIcon(kind) { return window.rodelInstanceIconAssets.image(kind);
}
function decorate(root = document) { root.querySelectorAll('[data-icon]').forEach(e => e.innerHTML = vectorIcon(e.dataset.icon)); }
const instances = [
  { id:'valley', name:'雾谷生存', version:'1.21.1', loader:'Fabric', packName:'雾谷探索整合包', mods:102, icon:'grass', java:21, memory:4, packFormat:'hmcl', copy:'继续你的世界。' },
  { id:'redstone', name:'红石实验室', version:'1.21.1', loader:'原版', mods:0, icon:'redstone', java:21, memory:2, copy:'继续你的世界。' },
  { id:'adventure', name:'冒险整合包', version:'1.20.1', loader:'Forge', mods:186, icon:'adventure', java:17, memory:6, copy:'继续你的世界。' },
  { id:'old', name:'旧日世界', version:'1.12.2', loader:'原版', mods:0, icon:'old', java:8, memory:2, copy:'继续你的世界。' }
];
let selected = instances[0];
const jobs = new Map();
const timers = new Map();
let returnFocus = null;
let activePanel = null;
let toastTimer;
const el = id => document.getElementById(id);
const stateOf = () => jobs.get(selected?.id)?.state || 'ready';
function clearTimers(id = selected.id) { (timers.get(id) || []).forEach(clearTimeout); timers.delete(id); }
function showToast(message) { el('toast').textContent = message; el('toast').hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => el('toast').hidden = true, 3600); }
function homeEmptyMarkup(server = false) {
  return `<div class="home-target-empty"><div class="home-empty-symbol">${vectorIcon(server ? 'servers' : 'instances')}</div><div><strong>${server ? '还没有收藏服务器' : '还没有实例。'}</strong><p>${server ? '添加常玩的服务器，之后从这里连接。' : '新建一个实例，或导入已有游戏。'}</p></div></div>`;
}
function renderInstances() {
  const local = instances.filter(instance => !instance.serverId);
  document.querySelector('.count').textContent=String(local.length).padStart(2,'0');
  el('instance-list').innerHTML = local.map(i => {
    const state = jobs.get(i.id)?.state;
    const label = { preparing:'准备中', downloading:'下载中 · 64%', waitingWindow:'等待游戏窗口', downloadFailed:'下载中断', failed:'启动失败', running:'游戏运行中' }[state];
    return `<button class="instance-card" data-instance="${i.id}" aria-pressed="${i.id === selected?.id}"><span class="instance-icon">${cubeIcon(i.icon)}</span><span><strong>${escapeHtml(i.name)}</strong><small>${escapeHtml([i.version, i.loader, i.packName].filter(Boolean).join(' · '))}</small>${label ? `<span class="instance-task">${label}</span>` : ''}</span></button>`;
  }).join('') || homeEmptyMarkup();
}
function readyMarkup(label = '启动游戏', disabled = false, empty = false) {
  return `<div class="launch-actions"><button class="launch-button" id="launch" ${disabled ? 'disabled' : ''}><span class="button-face"><span data-icon="play"></span><strong>${label}</strong><span class="key-hint">↵</span></span></button><button class="secondary-button" data-panel="instance" aria-label="当前实例设置" title="当前实例设置" ${disabled ? 'disabled' : ''}><span data-icon="wrench"></span></button></div>${disabled || empty ? '' : `<p class="launch-caption"><span class="status-square"></span> 已就绪 <span class="caption-separator">/</span> Java ${selected.java} <span class="caption-separator">/</span> ${selected.memory} GB 内存</p>`}`;
}
function stageMarkup(state) {
  if (state === 'ready') return readyMarkup();
  if (state === 'waitingWindow') return `${readyMarkup('正在等待游戏窗口', true)}<div class="stage-panel" aria-live="polite"><div class="stage-heading"><strong>正在等待游戏窗口</strong></div><p class="stage-copy">游戏正在初始化，请稍候。</p><div class="progress-track" role="progressbar" aria-label="等待游戏窗口" aria-busy="true"><div class="progress-fill waiting-window-progress"></div></div></div>`;
  if (state === 'preparing') return `${readyMarkup('正在准备启动', true)}<div class="stage-panel"><div class="stage-heading"><strong>正在准备启动</strong><span>01 / 03</span></div><p class="stage-copy">读取实例与运行配置，检查本次需要的文件。</p><div class="progress-track"><div class="progress-fill" style="width:24%"></div></div><p class="stage-copy">兼容建议供参考，不影响可运行的配置。</p><div class="stage-actions"><button class="small-button subtle" data-action="cancel">取消准备</button><button class="text-button" data-panel="launch-plan">查看启动信息 ↗</button></div></div>`;
  if (state === 'downloading') return `${readyMarkup('正在准备启动', true)}<div class="stage-panel"><div class="stage-heading"><strong>正在补全游戏文件</strong><span>64%</span></div><p class="stage-copy">资源文件 · 128 / 200 MB · 8.4 MB/s</p><div class="progress-track" role="progressbar" aria-label="游戏文件下载" aria-valuenow="64" aria-valuemin="0" aria-valuemax="100"><div class="progress-fill"></div></div><p class="stage-copy">完成后自动启动「${escapeHtml(selected.name)}」。你可以切换到其他实例。</p><div class="stage-actions"><button class="small-button subtle" data-action="cancel">取消本次启动</button></div></div>`;
  if (state === 'downloadFailed') return `<div class="stage-panel warning" role="alert"><div class="stage-heading"><strong>下载暂时中断</strong><span>网络连接失败</span></div><p class="stage-copy">已完成的文件保留供下次复用。重试下载后继续本次启动。</p><div class="stage-actions"><button class="small-button" data-action="resume">重试下载</button><button class="small-button subtle" data-action="cancel-download">取消任务</button></div></div>`;
  if (state === 'failed') return `<div class="stage-panel warning" role="alert"><div class="stage-heading"><strong>游戏未能启动</strong><span>退出码 1</span></div><p class="stage-copy">示例诊断：Grove Routes 缺少 Transit API 依赖。</p><div class="stage-actions"><button class="small-button primary" data-ai-repair>AI 分析并修复</button><button class="small-button subtle" data-panel="diagnosis">错误详情</button><button class="small-button subtle" data-action="retry">重新启动</button><button class="text-button" data-panel="logs">日志设置 ↗</button></div></div>`;
  return `<div class="stage-panel"><div class="stage-heading"><strong>游戏正在运行</strong><span>JAVA ${selected.java}</span></div><p class="stage-copy">「${escapeHtml(selected.name)}」已启动，去创造你的世界吧。</p><div class="progress-track" role="progressbar" aria-label="启动完成" aria-valuenow="100" aria-valuemin="0" aria-valuemax="100"><div class="progress-fill" style="width:100%"></div></div><div class="stage-actions"><button class="small-button" data-action="game">返回游戏</button><button class="small-button subtle" data-panel="logs">日志设置</button><button class="text-button" data-action="stop">结束游戏</button></div></div>`;
}
function render(refreshPage=true) {
  el('press-preview').setAttribute('aria-pressed','false');
  const target = instances.find(instance => !instance.serverId && instance.id === selected?.id);
  el('instance-title').textContent = target?.name ?? '开启新冒险';
  el('instance-copy').hidden = !!target;
  el('instance-copy').textContent = target ? '' : '为下一次探索，准备一个游戏实例。';
  el('runtime').hidden = !target;
  el('runtime').innerHTML = target ? `<span>${target.version}</span><b>·</b><span>${target.loader}</span><b>·</b><span>${target.mods ? `${target.mods} 个模组` : '纯净游戏'}</span>` : '';
  document.querySelector('.eyebrow').hidden = false;
  el('launch-area').innerHTML = target ? stageMarkup(stateOf()) : readyMarkup('启动游戏', true, true);
  document.querySelectorAll('[data-state]').forEach(b => b.setAttribute('aria-pressed', b.dataset.state===stateOf()));
  renderInstances(); decorate(el('launch-area')); window.renderHomeTargets?.(); if(refreshPage)renderPage();
}
function setState(state, instanceId = selected.id) {
  jobs.set(instanceId,{state});render(false);
}
function beginLaunch() {
  if(window.rodelEnsureLaunchIdentity && !window.rodelEnsureLaunchIdentity(beginLaunch))return;
  const id = selected.id;
  clearTimers(); closeDrawer(); setState('preparing', id);
  timers.set(id,[setTimeout(() => setState('downloading', id), 1800),setTimeout(() => setState('waitingWindow', id), 6500),setTimeout(() => setState('running', id), 11000)]);
}
function panelContent(name) {
  const row=(a,b)=>`<div class="detail-row"><span>${escapeHtml(a)}</span><span>${escapeHtml(b)}</span></div>`;
  const definitions={
    identity:['PLAYER','游戏身份',`${row('Steve · 离线身份','当前使用')}<p>离线身份可以继续使用已有实例。需要在线服务时，再选择添加微软账户。</p><div class="stage-actions"><button class="small-button" data-action="offline">添加离线身份</button><button class="small-button subtle" data-action="microsoft">添加微软账户</button></div>`],
    instance:['INSTANCE',selected.name,`${row('Minecraft',selected.version)}${row('加载器',selected.loader)}${row('Java',`Java ${selected.java}`)}${row('最大内存',`${selected.memory} GB`)}<div class="stage-actions"><button class="small-button" data-instance-command="settings">编辑配置</button><button class="small-button subtle" data-page="content">管理内容</button></div>`],
    'launch-plan':['LAUNCH','本次启动信息',`${row('实例',selected.name)}${row('身份','Steve · 离线')}${row('Java',`Java ${selected.java}`)}${row('内存',`${selected.memory} GB`)}<p>准备完成后直接启动，兼容建议供参考。</p>`],
    diagnosis:['DIAGNOSIS','为什么没有启动','<p><strong>缺少模组依赖</strong></p><p>这次退出信息指出一个模组需要 Fabric API。可先检查内容选择，再尝试启动。</p><div class="stage-actions"><button class="small-button" data-page="content">检查内容</button><button class="small-button subtle" data-action="retry">重新启动</button><button class="text-button" data-page="settings" data-topic="logs">导出日志 ↗</button></div>'],
    'ai-model':['MODEL','连接模型服务','<p>选择已经运行的模型服务，连接后即可开始对话。</p><label>服务地址<input aria-label="模型服务地址" placeholder="http://127.0.0.1:11922/"></label><label>模型<select aria-label="助手模型"><option>选择可用模型</option><option>本地模型 · 示例</option></select></label><div class="stage-actions"><button class="small-button" data-action="ai-config">测试连接</button></div>'],
    instances:['INSTANCES','我的实例',`${instances.map(i=>row(i.name,`${i.version} · ${i.loader}`)).join('')}<div class="stage-actions"><button class="small-button" data-panel="add">添加实例</button><button class="small-button subtle" data-action="import-instance">导入已有实例</button></div>`],
  }; return definitions[name];
}
function openInstanceManagement(trigger) { openInstanceMenu(trigger); }
function positionPanel(trigger) {
  const container=document.querySelector('.launcher').getBoundingClientRect();
  const drawer=el('drawer'); const r=trigger?.getBoundingClientRect();
  if(!r)return;
  const width=Math.min(420,container.width-36);drawer.style.width=`${width}px`;
  const height=drawer.getBoundingClientRect().height;
  let x=r.left-container.left; let y=r.bottom-container.top+12;
  if(y+height>container.height-24)y=r.top-container.top-height-12;
  // The trigger's own edge anchors each popover; only screen collisions clamp it.
  x=Math.max(18,Math.min(x,container.width-width-18));y=Math.max(48,Math.min(y,container.height-height-24));
  drawer.style.left=`${x}px`;drawer.style.top=`${y}px`;drawer.style.right='auto';
}
function openDrawer(name,trigger=document.activeElement) {
  if(name==='instance'){instanceMenuTrigger=trigger;closeDrawer();openInstanceTask('configure');return;}
  if(name==='add'){openInstanceEntry('create');return;}
  if(name==='ai'){navigate('ai');return;}
  if(['home','content','settings','instances'].includes(name)){navigate(name);return;}
  if(name==='logs'){navigate('settings','logs');return;}
  const data=panelContent(name);if(!data)return;
  if(el('drawer').hidden)returnFocus=trigger;
  activePanel=name;el('drawer-kicker').textContent=data[0];el('drawer-title').textContent=data[1];el('drawer-content').innerHTML=data[2];el('drawer').hidden=false;
  upgradeCombos(el('drawer'));positionPanel(trigger);el('close-drawer').focus();
}
function closeDrawer() {
  if(el('drawer').hidden)return;
  el('drawer').hidden=true;activePanel=null;if(returnFocus?.isConnected)returnFocus.focus();
}
document.addEventListener('click',event=>{
  const b=event.target.closest('button');if(!b)return;
  if(b.dataset.manageLaunch){selected=instances.find(i=>i.id===b.dataset.manageLaunch);navigate('home');beginLaunch();return;}
  if(b.dataset.manageContent){selected=instances.find(i=>i.id===b.dataset.manageContent);navigate('content');return;}
  if(b.dataset.manageSettings){selected=instances.find(i=>i.id===b.dataset.manageSettings);instanceMenuTrigger=b;openInstanceTask('configure');return;}
  if(b.dataset.manageMore){selected=instances.find(i=>i.id===b.dataset.manageMore);openInstanceManagement(b);return;}
  if(b.dataset.page){navigate(b.dataset.page,b.dataset.topic);return;}
  if(b.dataset.settingsSection){settingsSection=b.dataset.settingsSection;renderPage();return;}
  if(b.dataset.contentTab){contentTab=b.dataset.contentTab;renderPage();return;}
  if(b.dataset.state){clearTimers();navigate('home');setState(b.dataset.state);return;}
  if(b.dataset.instance){selected=instances.find(i=>i.id===b.dataset.instance);closeDrawer();render();return;}
  if(b.id==='launch'){beginLaunch();return;}
  if(b.id==='press-preview'){const launch=el('launch');if(currentPage!=='home'||!launch){showToast('回到游戏首屏后可预览按钮下沉。');return;}b.setAttribute('aria-pressed',launch.toggleAttribute('data-pressed'));return;}
  if(b.id==='close-drawer'){closeDrawer();return;}
  if(b.dataset.panel){openDrawer(b.dataset.panel,b);return;}
  const action=b.dataset.action;
  if(action==='import-instance'){openInstanceEntry('import');return;}
  if(action==='component-demo'){showToast('组件状态演示：操作已触发。');return;}
  if(['open-data-folder','discover-java','pack-tools','cache-status','instance-backup','export-instance','import-icon'].includes(action)){showToast('原型演示：'+({ 'open-data-folder':'打开数据文件夹', 'discover-java':'发现本机 Java', 'pack-tools':'整合包导入与导出', 'cache-status':'检查缓存占用与安全回收', 'instance-backup':'选择备份范围与保存位置', 'export-instance':'导出实例与内容清单', 'import-icon':'选择本地图片作为实例图标' }[action]));return;}
  if(action?.startsWith('window-')){showToast('原型演示：窗口操作。');return;}
  if(action==='cancel'){clearTimers();setState('ready');showToast('已取消本次启动，保留已完成的文件。');}
  else if(action==='retry')beginLaunch();
  else if(action==='stop'){setState('ready');showToast('原型演示：游戏会话已结束。');}
  else if(action==='resume'){const id=selected.id;setState('downloading');timers.set(id,[setTimeout(()=>setState('waitingWindow',id),4000),setTimeout(()=>setState('running',id),8000)]);}
  else if(action==='cancel-download'){clearTimers();setState('ready');}
  else if(action==='save-settings'){selected.memory=Number(el('memory').value);render();showToast('实例选择已在原型内更新。');}
  else if(action==='toggle-content'){const pressed=b.getAttribute('aria-pressed')==='true';b.setAttribute('aria-pressed',!pressed);b.classList.toggle('is-on',!pressed);b.textContent=pressed?'已停用':'已启用';}
  else if(action==='rename-instance'){const value=el('rename-instance').value.trim();if(!value){el('rename-instance').setAttribute('aria-invalid','true');return;}selected.name=value;closeDrawer();render();}
  else if(action==='copy-instance'){instances.push({...selected,id:`mock-${Date.now()}`,name:selected.name+' · 副本'});closeDrawer();navigate('instances');render();}
  else if(action==='set-instance-icon'){selected.icon=el('instance-icon-style').value;render();closeDrawer();}

  else {const feedback={'open-log-folder':'原型演示：在文件管理器中打开日志文件夹。',game:'原型演示：返回游戏窗口。',offline:'原型演示：输入昵称添加离线身份。',microsoft:'微软账户是可选入口。','import-content':'原型演示：选择模组、资源包或光影文件。','import-instance':'原型演示：预览并导入已有实例。','ask-ai':'原型演示：将问题交给已配置的模型。','ai-config':'原型演示：连接已有模型服务。','search-platform':'原型演示：使用当前实例上下文搜索 Modrinth。','content-detail':'原型演示：查看当前内容的来源、版本与依赖。'};if(feedback[action])showToast(feedback[action]);}
});
document.addEventListener('change',event=>{
  if(event.target.id==='page-instance'){selected=instances.find(i=>i.id===event.target.value);render();}
  if(event.target.id==='reduce-motion')document.querySelector('.launcher').classList.toggle('reduce-motion',event.target.checked);
});
document.addEventListener('keydown',event=>{if(event.key==='Escape')closeDrawer();if(event.key==='Enter'&&event.target===document.body&&currentPage==='home'&&el('drawer').hidden&&stateOf()==='ready'){event.preventDefault();el('launch')?.click();}});
window.rodelPrototype={setState,navigate,closeDrawer};
decorate();render();

document.addEventListener('click', event => { if (event.target.closest('[data-ai-repair]')) Assistant.openRepair('dependency'); });
