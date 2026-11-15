/* HTML review only. Fictional accounts in memory; no OAuth, credentials or storage. */
(() => {
  const $ = selector => document.querySelector(selector);
  const esc = escapeHtml;
  const paths = {
    user:'M20 21v-2a6 6 0 0 0-12 0v2M14 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8',
    check:'M5 12l4 4L19 6',
    edit:'M4 16L16 4l4 4L8 20H4zM13 7l4 4',
    chevron:'M6 9l6 6 6-6',
    back:'M19 12H5m6-6-6 6 6 6',
    trash:'M3 6h18M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7m4-7v7',
    login:'M9 5H4v14h5M14 5l7 7-7 7M8 12h13',
    alert:'M12 3L2 21h20L12 3zM12 9v5m0 3v1',
    plus:'M12 4v16M4 12h16',
    computer:'M3 4h18v13H3zM12 17v4M7 21h10',
    manage:'M4 6h16M4 12h16M4 18h16M8 3v6M16 9v6M10 15v6',
    close:'M6 6l12 12M18 6L6 18',
    globe:'M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0M3 12h18M12 3a19 19 0 0 1 0 18 19 19 0 0 1 0-18'
  };
  const icon = name => name==='more'?'<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><circle cx="4" cy="12" r="1.8"/><circle cx="12" cy="12" r="1.8"/><circle cx="20" cy="12" r="1.8"/></svg>':`<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${paths[name] || paths.user}"/></svg>`;
  const typeIcon = account => `<span class="account-menu-type" role="img" aria-label="${type(account)}" title="${type(account)}">${account.kind==='microsoft'?'<svg viewBox="0 0 23 23"><path fill="#f25022" d="M0 0h11v11H0z"/><path fill="#7fba00" d="M12 0h11v11H12z"/><path fill="#00a4ef" d="M0 12h11v11H0z"/><path fill="#ffb900" d="M12 12h11v11H12z"/></svg>':icon(account.kind==='external'?'globe':'computer')}</span>`;
  const face = account => `<span class="account-face">${window.rodelAvatarPicker.image(account?.avatar)}</span>`;
  const samples = () => [
    {id:'offline-steve',name:'Steve',kind:'offline',status:'ready'},
    {id:'msa-explorer',name:'RodelExplorer',kind:'microsoft',status:'ready',avatar:{kind:'preset',id:'alex'}},
    {id:'msa-builder',name:'CloudBuilder',kind:'microsoft',status:'expired',avatar:{kind:'preset',id:'miner'}},
    {id:'offline-builder',name:'PixelCat',kind:'offline',status:'ready',avatar:{kind:'preset',id:'cat'}},
    {id:'external-littleskin-north',name:'NorthWind',kind:'external',status:'ready',source:'LittleSkin',endpoint:'https://littleskin.cn/api/yggdrasil',role:'north',avatar:{kind:'preset',id:'young-girl'}},
    {id:'external-starlight-river',name:'RiverStone',kind:'external',status:'expired',source:'星河社区',endpoint:'https://skin.example.com/api/yggdrasil',role:'river',avatar:{kind:'preset',id:'wolf'}}
  ];
  const ordered = () => [...accounts].sort((a,b) => ({microsoft:0,external:1,offline:2}[a.kind]??3)-({microsoft:0,external:1,offline:2}[b.kind]??3));
  let accounts = samples();
  let currentId = accounts[0].id;
  let menu, dialog, trigger, view = 'list', targetId, loginMode = 'system', attempt = 0;
  let scenario = 'normal';
  let pendingLaunch;
  let overflow, overflowTrigger;
  let typePopup, typeTrigger;
  function closeTypePopup(restore=false) {
    typePopup?.close();
    typePopup?.remove();typePopup=null;typeTrigger?.setAttribute('aria-expanded','false');
    if(restore)(typeTrigger?.isConnected?typeTrigger:identity).focus();
  }
  function openTypePopup(control) {
    closeTypePopup();closeOverflow();
    if(dialog)closePopup();
    typeTrigger=control?.isConnected?control:identity;
    if(menu){closeMenu();typeTrigger=identity;}
    typePopup=document.createElement('dialog');typePopup.className='account-type-popup';
    typePopup.setAttribute('aria-labelledby','account-type-title');typePopup.setAttribute('aria-modal','true');
    const microsoftLogo='<svg class="microsoft-logo" viewBox="0 0 23 23" aria-hidden="true"><path fill="#f25022" d="M0 0h11v11H0z"/><path fill="#7fba00" d="M12 0h11v11H12z"/><path fill="#00a4ef" d="M0 12h11v11H0z"/><path fill="#ffb900" d="M12 12h11v11H12z"/></svg>';
    typePopup.innerHTML=`<header><h2 id="account-type-title">添加账户</h2></header><div class="account-type-body"><button class="account-choice" data-account-action="microsoft">${microsoftLogo}<span><strong>微软账户</strong><small>使用 Microsoft 账户登录，验证 Minecraft Java 版游戏权限。</small></span>${icon('chevron')}</button><button class="account-choice" data-account-action="offline">${icon('user')}<span><strong>离线身份</strong><small>无需登录，用于单人世界或允许离线身份的服务器。</small></span>${icon('chevron')}</button><button class="account-choice" data-account-action="external">${icon('globe')}<span><strong>外置账户</strong><small>使用 LittleSkin 或其他认证服务的游戏角色。</small></span>${icon('chevron')}</button></div>`;
    $('.launcher').append(typePopup);
    typePopup.addEventListener('cancel',event=>{event.preventDefault();closeTypePopup(true);});
    typePopup.addEventListener('click',event=>{
      if(event.target!==typePopup)return;
      const r=typePopup.getBoundingClientRect();
      if(event.clientX<r.left||event.clientX>r.right||event.clientY<r.top||event.clientY>r.bottom)closeTypePopup(true);
    });
    typePopup.showModal();positionTypePopup();
    typeTrigger.setAttribute('aria-expanded','true');typePopup.querySelector('[data-account-action]').focus();
  }
  function positionTypePopup() {
    if(!typePopup)return;
    const base=$('.launcher').getBoundingClientRect();
    typePopup.style.left=`${base.left+base.width/2}px`;
    typePopup.style.top=`${base.top+base.height/2}px`;
  }
  const identity = $('.identity');
  const type = account => account.kind === 'offline' ? '本地账号' : account.kind==='external'?`外置账户 · ${account.source}`:'微软账户';
  const statusText = account => account.kind === 'offline' ? '可用于离线游玩' : account.kind==='external'?`${account.source} · ${{ready:'可用',expired:'需要重新登录',missing:'角色已不存在'}[account.status]||'待验证'}`: ({ready:'可用于 Java 版',expired:'需要重新登录',unowned:'尚无可用的 Java 版权限',network:'暂时无法验证会话'}[account.status]);
  function updateIdentity() {
    const current = accounts.find(a => a.id === currentId);
    identity.setAttribute('aria-label',current?`账户：${current.name}，${type(current)}`:'账户：尚未选择身份');
    identity.setAttribute('aria-haspopup','menu');
    identity.innerHTML = face(current);
  }
  function closeMenu(restore = false) {
    menu?.remove();menu = null;identity.setAttribute('aria-expanded','false');
    if(restore) identity.focus();
  }
  function openMenu() {
    closeMenu();closeDrawer();
    menu = document.createElement('section');menu.className = 'account-menu';
    menu.setAttribute('role','menu');menu.setAttribute('aria-label','切换账户');
    menu.innerHTML = `<div class="account-menu-label">本次使用的身份</div><div class="account-menu-list">${accounts.length?ordered().map((a,i,all)=>`${i && a.kind!==all[i-1].kind ? '<hr role="separator">' : ''}<button role="menuitemradio" aria-label="${esc(a.name)}，${type(a)}" aria-checked="${a.id===currentId}" data-account-select="${a.id}">${face(a)}<span class="account-copy"><strong>${esc(a.name)}</strong>${a.status!=='ready'?`<small class="${a.status==='expired'?'account-status-critical':''}">${statusText(a)}</small>`:''}</span>${typeIcon(a)}</button>`).join(''):'<div class="account-menu-label">尚未添加账户</div>'}</div><hr role="separator">${accounts.find(a=>a.id===currentId)?.kind==='offline'?`<button role="menuitem" data-account-avatar="${currentId}">${icon('edit')}更换头像</button>`:''}<button role="menuitem" data-account-action="manage">${icon('manage')}管理账户</button><button role="menuitem" data-account-action="add">${icon('plus')}添加账户</button>`;
    $('.launcher').append(menu);
    const base=$('.launcher').getBoundingClientRect(),r=identity.getBoundingClientRect();
    menu.style.left=`${Math.min(r.right-base.left+12,base.width-menu.offsetWidth-16)}px`;
    menu.style.top=`${Math.max(12,Math.min(r.top-base.top,base.height-menu.offsetHeight-16))}px`;
    identity.setAttribute('aria-expanded','true');menu.querySelector('button')?.focus();
  }
  function closePopup() {
    pendingLaunch=null;
    closeTypePopup();
    closeOverflow();
    attempt++;dialog?.close();dialog?.remove();dialog=null;
    (trigger?.isConnected?trigger:identity).focus();
  }
  function openPopup(next = 'list') {
    closeTypePopup();
    closeMenu();closeDrawer();
    if(!dialog) {
      trigger=identity;
      dialog=document.createElement('dialog');dialog.className='account-popup';
      dialog.setAttribute('aria-labelledby','account-title');dialog.setAttribute('aria-modal','true');
      $('.launcher').append(dialog);
      dialog.addEventListener('cancel',event=>{
        event.preventDefault();
        if(view==='waiting')action('cancel-login');else closePopup();
      });
      dialog.addEventListener('click',event=>{
        if(event.target!==dialog||view==='waiting'||view==='warning')return;
        const r=dialog.getBoundingClientRect();
        if(event.clientX<r.left||event.clientX>r.right||event.clientY<r.top||event.clientY>r.bottom)closePopup();
      });
      dialog.showModal();
    }
    renderPopup(next);
  }
  const button=(label,action,primary=false)=>`<button class="small-button ${primary?'account-primary':'subtle'}" data-account-action="${action}">${label}</button>`;
  function renderPopup(next) {
    closeTypePopup();
    closeOverflow();
    view=next;
    dialog.dataset.view=next;
    const target=accounts.find(a=>a.id===targetId),current=accounts.find(a=>a.id===currentId);
    const titles={list:'账户管理',offline:'添加离线身份',waiting:'正在登录',error:'登录未完成',warning:'暂时无法登录微软账户',unowned:'检查游戏权限',remove:'移除账户',success:'账户已添加'};
    let body='',footer='',headerAction='';
    if(next==='list') {
      headerAction=button('添加账户','add',true).replace('<button ','<button aria-haspopup="dialog" aria-expanded="false" ');
      body=accounts.length?`${ordered().map((a,i,all)=>`<article class="account-row${i<all.length-1 && a.kind!==all[i+1].kind ? ' account-group-end' : ''}" aria-label="${esc(a.name)}，${type(a)}，${statusText(a)}">${a.kind==='offline'?`<button class="account-avatar-edit" data-account-avatar="${a.id}" aria-label="更换 ${esc(a.name)} 的头像" title="更换头像">${face(a)}</button>`:face(a)}<div class="account-copy"><strong>${esc(a.name)}</strong><div class="account-state ${a.status==='expired'?'account-status-critical':a.status==='ready'?'':'attention'}">${statusText(a)}</div></div><div class="account-actions">${a.id===currentId?'<span class="account-tag">当前使用</span>':`<button class="small-button subtle" data-account-use="${a.id}">使用</button>`}<span class="account-kind" role="img" aria-label="${type(a)}" title="${type(a)}">${a.kind==='microsoft'?'<svg viewBox="0 0 23 23" aria-hidden="true"><path fill="#f25022" d="M0 0h11v11H0z"/><path fill="#7fba00" d="M12 0h11v11H12z"/><path fill="#00a4ef" d="M0 12h11v11H0z"/><path fill="#ffb900" d="M12 12h11v11H12z"/></svg>':icon(a.kind==='external'?'globe':'computer')}</span><button class="account-overflow" data-account-more="${a.id}" aria-label="${esc(a.name)} 的更多操作" aria-haspopup="menu" aria-expanded="false" title="更多操作">${icon('more')}</button></div></article>`).join('')}`:`<div class="account-empty">${icon('user')}<h3>还没有账户</h3><p>添加微软、外置账户，或创建一个离线身份。</p></div>`;
      footer='';
    } else if(next==='offline') {
      body='<form id="offline-account-form"><label for="offline-account-name">玩家名称</label><input id="offline-account-name" name="player" autocomplete="off" placeholder="输入游戏中的玩家名" maxlength="64" aria-describedby="offline-name-error"><p id="offline-name-error" class="account-error" role="alert" hidden></p><p class="account-note">名称决定本地身份。更换名称可能影响存档中对应的玩家数据。</p></form>';
      footer=button('添加并使用','save-offline',true);
    } else if(next==='waiting') {
      body=`<div class="account-login-state"><div class="account-authorization-line"><span role="status">${loginMode==='browser'?'等待浏览器授权':'等待 Microsoft 授权'}</span><button type="button" class="account-inline-cancel" data-account-action="cancel-login" aria-label="取消登录">取消</button></div><div class="progress-track" role="progressbar" aria-label="登录进行中"><div class="progress-fill"></div></div><p class="account-status-copy">请在${loginMode==='browser'?'浏览器':'系统登录窗口'}中完成操作。</p></div>`;
    } else if(next==='warning') {
      body=`<div class="account-warning-message">${icon('alert')}<p>此版本尚未配置微软登录，仍可使用离线身份。</p></div>`;
      footer=button('确认','list',true);
    } else if(next==='error') {
      body=`<div class="account-login-state">${icon('alert')}<h3>暂时无法完成登录</h3><p class="account-status-copy">${scenario==='broker'?'系统登录组件暂不可用。可以重试，或改用浏览器登录。':'无法连接到认证服务。请检查网络后重试。'}</p></div>`;
      footer=button('重试','start-login',true)+(scenario==='broker'?button('改用浏览器登录','browser'):'');
    } else if(next==='unowned') {
      body=`<div class="account-login-state">${icon('alert')}<h3>尚未取得可用的 Java 版游戏权限</h3><p class="account-status-copy">请确认选中的账户和游戏订阅状态。Microsoft 授权成功不代表游戏权限验证已完成。</p></div><p>可以切换账户重新登录；已添加的离线身份仍可继续用于本地游玩。</p>`;
      footer=button('换个账户登录','other-account',true);
    } else if(next==='remove') {
      body=`<h3>移除 ${esc(target?.name || '')}？</h3><p>从启动器移除此账户${target?.kind!=='offline'?'及其本地登录凭据':''}。游戏文件和存档保留，Windows 系统账户不受影响。</p>${targetId===currentId?'<p class="account-note">当前身份会清空，下次启动前需要重新选择。</p>':''}`;
      footer=button('确认移除','confirm-remove',true);
    } else if(next==='success') {
      body=`<div class="account-login-state">${icon('check')}<h3>${target?'会话已更新':'已添加 RodelExplorer'}</h3><p>已${target?'验证':'读取'} Minecraft Java 版资料，可在账户管理中选择使用。</p></div>`;
      footer='';
    }
    dialog.innerHTML=`<header>${['list','warning','waiting'].includes(next)?'':`<button class="account-back" data-account-action="list" aria-label="返回账户管理">${icon('back')}</button>`}<h2 id="account-title">${titles[next]}</h2>${headerAction}${['list','warning','waiting'].includes(next)?'':`<button class="account-close" data-account-action="close" aria-label="关闭账户面板" title="关闭账户面板">${icon('close')}</button>`}</header><div class="account-body" aria-label="账户内容" tabindex="0"><div class="account-body-inner">${body}</div></div>${footer?`<footer>${footer}</footer>`:''}${reviewMode?`<aside class="account-simulation" aria-label="HTML 设计评审工具"><span>设计评审 · 非产品界面</span><div class="account-review">${[['light','浅色'],['dark','深色'],['contrast','高对比']].map(([id,label])=>`<button data-theme-choice="${id}">${label}</button>`).join('')}${next==='waiting'?[['success','成功'],['error','网络失败'],['not-configured','未配置登录'],['unowned','无游戏权限'],['cancel','用户取消']].map(([id,label])=>`<button data-account-result="${id}">${label}</button>`).join(''):''}</div></aside>`:''}`;
    const renderedDialog=dialog;
    requestAnimationFrame(()=>{
      if(dialog!==renderedDialog||!renderedDialog.isConnected||renderedDialog.dataset.view!==next)return;
      const focus=next==='offline'?$('#offline-account-name'):next==='warning'?dialog.querySelector('footer button'):next==='list'?dialog.querySelector('[data-account-action="add"]'):dialog.querySelector(next==='waiting'?'.account-inline-cancel':'.account-back');
      focus?.focus();updateTrimTips();
    });
    updateReviewStatus();
  }
  function closeOverflow(restore=false) {
    overflow?.remove();overflow=null;overflowTrigger?.setAttribute('aria-expanded','false');
    if(restore&&overflowTrigger?.isConnected)overflowTrigger.focus();
  }
  function openOverflow(control) {
    const account=accounts.find(a=>a.id===control.dataset.accountMore);
    const same=overflowTrigger===control&&overflow;
    closeOverflow();if(same)return;
    overflowTrigger=control;
    overflow=document.createElement('div');overflow.className='account-overflow-menu';
    overflow.setAttribute('role','menu');overflow.setAttribute('aria-label',`${account.name} 的更多操作`);
    overflow.innerHTML=`${account.kind==='offline'?`<button role="menuitem" data-account-avatar="${account.id}">${icon('edit')}更换头像</button>`:''}${account.kind!=='offline'?`<button role="menuitem" data-account-login="${account.id}">${icon('login')}${account.status==='expired'?'重新登录':'验证会话'}</button>`:''}<button role="menuitem" class="danger" data-account-remove="${account.id}">${icon('trash')}移除账户</button>`;
    dialog.append(overflow);control.setAttribute('aria-expanded','true');
    positionOverflow();
    overflow.querySelector('button').focus();
  }
  function positionOverflow() {
    if(!overflow)return;
    const base=dialog.getBoundingClientRect(),r=overflowTrigger.getBoundingClientRect(),body=dialog.querySelector('.account-body').getBoundingClientRect();
    if(r.bottom<body.top||r.top>body.bottom){closeOverflow(true);return;}
    overflow.style.left=`${Math.max(8,Math.min(r.right-base.left-overflow.offsetWidth,base.width-overflow.offsetWidth-8))}px`;
    const below=r.bottom-base.top+4;
    overflow.style.top=`${below+overflow.offsetHeight<base.height-8?below:Math.max(8,r.top-base.top-overflow.offsetHeight-4)}px`;
  }
  function updateTrimTips() {
    document.querySelectorAll('.account-copy strong,.account-copy small').forEach(node=>{
      if(node.scrollWidth>node.clientWidth)node.title=node.textContent;else node.removeAttribute('title');
    });
  }
  function selectAccount(id) {
    currentId=id;updateIdentity();closeMenu(true);
    if(dialog)renderPopup('list');
    showToast(`原型：已切换为 ${accounts.find(a=>a.id===id).name}。`);
    resumeLaunch();
  }
  function resumeLaunch() {
    if(!pendingLaunch || !accounts.some(account=>account.id===currentId))return;
    const resume=pendingLaunch;pendingLaunch=null;closePopup();resume();
  }
  window.rodelEnsureLaunchIdentity = resume => {
    if(accounts.some(account=>account.id===currentId))return true;
    pendingLaunch=resume;openPopup(accounts.length?'list':'offline');return false;
  };
  function changeAvatar(id) {
    const account=accounts.find(a=>a.id===id);
    if(!account || account.kind!=='offline')return;
    const returnToList=!!dialog;
    closeMenu();closeOverflow();if(dialog)closePopup();
    window.rodelAvatarPicker.choose(account,avatar=>{
      if(avatar)account.avatar=avatar;
      updateIdentity();
      if(returnToList)openPopup('list');else openMenu();
    });
  }
  function startLogin(mode='system') {
    loginMode=mode;attempt++;renderPopup('waiting');
    // The review toolbar settles this attempt explicitly. No timer claims a real login.
  }
  function settleLogin(outcome) {
    if(view!=='waiting'||!dialog){$('.account-review-status').textContent='请先在账户菜单中开始登录，再模拟返回结果。';return;}
    attempt++;
    if(outcome==='not-configured'){renderPopup('warning');return;}
    if(outcome==='error'||outcome==='broker'){scenario=outcome;renderPopup('error');return;}
    if(outcome==='unowned'){renderPopup('unowned');return;}
    if(outcome==='cancel'){renderPopup('list');return;}
    const target=accounts.find(a=>a.id===targetId);
    const firstAccount=accounts.length===0;
    if(target)target.status='ready';
    else if(!accounts.some(a=>a.id==='msa-explorer'))accounts.push({id:'msa-explorer',name:'RodelExplorer',kind:'microsoft',status:'ready'});
    if(firstAccount)currentId=accounts[0]?.id;
    // Only the first registration selects itself; existing choices are retained.
    updateIdentity();renderPopup('list');
    resumeLaunch();
  }
  function action(name) {
    if(name==='manage'){openPopup('list');return;}
    if(name==='add'){openTypePopup(document.activeElement);return;}
    if(name==='close'){closePopup();return;}
    if(name==='external'){closePopup();window.rodelExternalAccounts.open();return;}
    if(name==='microsoft'){targetId=null;scenario='normal';if(!dialog)openPopup();startLogin();return;}
    if(name==='start-login'){startLogin();return;}
    if(name==='browser'){startLogin('browser');return;}
    if(name==='cancel-login'){attempt++;renderPopup('list');return;}
    if(name==='other-account'){targetId=null;startLogin();return;}
    if(name==='save-offline') {
      const input=$('#offline-account-name'),value=input.value.trim(),error=$('#offline-name-error');
      const duplicate=accounts.some(a=>a.kind==='offline'&&a.name===value);
      if(!value||duplicate){input.setAttribute('aria-invalid','true');error.textContent=duplicate?'已存在同名离线身份，请直接选择使用。':'请输入玩家名称。';error.hidden=false;input.focus();return;}
      const id=`offline-${++attempt}`;accounts.push({id,name:value,kind:'offline',status:'ready'});currentId=id;updateIdentity();renderPopup('list');resumeLaunch();return;
    }
    if(name==='confirm-remove') {
      accounts=accounts.filter(a=>a.id!==targetId);
      if(currentId===targetId)currentId=null;
      targetId=null;updateIdentity();renderPopup('list');return;
    }
    if(['list','offline'].includes(name))openPopup(name);
  }
  document.addEventListener('click',event=>{
    const button=event.target.closest('button');
    if(typePopup&&!typePopup.contains(event.target))closeTypePopup(true);
    if(button?.dataset.accountAvatar){event.stopImmediatePropagation();changeAvatar(button.dataset.accountAvatar);return;}
    if(button?.dataset.avatarReview){event.stopImmediatePropagation();if(button.dataset.avatarReview==='menu')openMenu();else changeAvatar(accounts.find(a=>a.kind==='offline').id);return;}
    if(button?.dataset.accountAction==='add'){event.stopImmediatePropagation();openTypePopup(button);return;}
    if(button?.dataset.accountMore){event.stopImmediatePropagation();openOverflow(button);return;}
    if(overflow&&!overflow.contains(event.target))closeOverflow();
    if(button===identity){event.stopImmediatePropagation();menu?closeMenu(true):openMenu();return;}
    if(button?.dataset.accountAction){event.stopImmediatePropagation();action(button.dataset.accountAction);return;}
    if(button?.dataset.accountSelect){selectAccount(button.dataset.accountSelect);return;}
    if(button?.dataset.accountUse){selectAccount(button.dataset.accountUse);return;}
    if(button?.dataset.accountRemove){targetId=button.dataset.accountRemove;renderPopup('remove');return;}
    if(button?.dataset.accountLogin){targetId=button.dataset.accountLogin;scenario='normal';const account=accounts.find(a=>a.id===targetId);if(account?.kind==='external'){closePopup();window.rodelExternalAccounts.reauthenticate(account);}else startLogin();return;}
    if(button?.dataset.accountScenario){setScenario(button.dataset.accountScenario);return;}
    if(button?.dataset.accountResult){settleLogin(button.dataset.accountResult);return;}
    if(menu&&!menu.contains(event.target))closeMenu();
  },true);
  document.addEventListener('submit',event=>{if(event.target.id==='offline-account-form'){event.preventDefault();action('save-offline');}});
  document.addEventListener('keydown',event=>{
    if(typePopup) {
      const items=[...typePopup.querySelectorAll('button')],index=items.indexOf(document.activeElement);
      if(event.key==='Escape'){event.preventDefault();event.stopImmediatePropagation();closeTypePopup(true);return;}
      if(['ArrowDown','ArrowUp','Home','End'].includes(event.key)){
        event.preventDefault();items[event.key==='Home'?0:event.key==='End'?items.length-1:(index+(event.key==='ArrowDown'?1:-1)+items.length)%items.length].focus();return;
      }
      if(event.key==='Tab'){
        event.preventDefault();items[(index+(event.shiftKey?-1:1)+items.length)%items.length].focus();return;
      }
    }
    if(overflow) {
      const items=[...overflow.querySelectorAll('button')],index=items.indexOf(document.activeElement);
      if(event.key==='Escape'){event.preventDefault();event.stopImmediatePropagation();closeOverflow(true);return;}
      if(['ArrowDown','ArrowUp','Home','End'].includes(event.key)){
        event.preventDefault();items[event.key==='Home'?0:event.key==='End'?items.length-1:(index+(event.key==='ArrowDown'?1:-1)+items.length)%items.length].focus();return;
      }
      if(event.key==='Tab')closeOverflow(true);
    }
    if(dialog&&event.key==='Tab') {
      const stops=[...dialog.querySelectorAll('button:not(:disabled),input:not(:disabled),[tabindex="0"]')].filter(n=>n.getClientRects().length);
      const first=stops[0],last=stops.at(-1),active=document.activeElement;
      if(event.shiftKey&&(active===first||!dialog.contains(active))){event.preventDefault();last?.focus();}
      else if(!event.shiftKey&&(active===last||!dialog.contains(active))){event.preventDefault();first?.focus();}
    }
    if(!menu)return;
    const items=[...menu.querySelectorAll('button')],index=items.indexOf(document.activeElement);
    if(event.key==='Escape'){event.preventDefault();event.stopImmediatePropagation();closeMenu(true);}
    if(['ArrowDown','ArrowUp','Home','End'].includes(event.key)){
      event.preventDefault();const next=event.key==='Home'?0:event.key==='End'?items.length-1:(index+(event.key==='ArrowDown'?1:-1)+items.length)%items.length;items[next].focus();
    }
    if(event.key==='Tab')closeMenu();
  },true);
  document.addEventListener('scroll',event=>{if(overflow&&event.target.classList?.contains('account-body'))positionOverflow();},true);
  function updateReviewStatus() {
    const label=$('.account-review-status');
    if(label)label.textContent=view==='waiting'?'模拟登录结果：使用下方评审按钮返回。':'HTML 模拟 · 不连接认证服务';
  }
  function setScenario(name) {
    closeMenu();if(dialog)closePopup();accounts=samples();currentId=accounts[0].id;targetId=null;scenario=name;
    if(name==='empty'){accounts=[];currentId=null;}
    if(name==='many')accounts.push(...Array.from({length:18},(_,i)=>({id:`demo-${i}`,name:`Explorer_${i+1}_LongAccountName`,kind:i%2?'offline':'microsoft',status:'ready'})));
    updateIdentity();openPopup('list');
    document.querySelectorAll('[data-account-scenario]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.accountScenario===name)));
  }
  const reviewMode=new URLSearchParams(location.search).get('accounts')==='1';
  if(reviewMode) {
    document.body.classList.add('account-review-mode');
    $('.review-bar strong').textContent='08 / ACCOUNTS';
    const toolbar=document.createElement('div');toolbar.className='account-review';toolbar.setAttribute('aria-label','账户评审场景');
    toolbar.innerHTML=`<button data-account-action="manage">账户管理</button>${[['normal','示例账户'],['empty','空列表'],['many','长列表']].map(([id,label])=>`<button data-account-scenario="${id}">${label}</button>`).join('')}<span class="account-review-status"></span>`;
    $('.review-bar').append(toolbar);
    $('.review-footer').innerHTML=`<div class="account-review" aria-label="模拟登录回调"><span>仅评审工具 · 登录返回</span>${[['success','成功'],['error','网络失败'],['unowned','无游戏权限'],['cancel','用户取消']].map(([id,label])=>`<button data-account-result="${id}">${label}</button>`).join('')}</div><a href="ACCOUNTS.md">账户交互说明 ↗</a>`;
    // Explicit review shortcuts supplement the labelled simulation strip in the modal.
    document.addEventListener('keydown',event=>{
      if(event.altKey&&['1','2','3','4','5'].includes(event.key)){event.preventDefault();settleLogin(['success','error','broker','unowned','cancel'][Number(event.key)-1]);}
    });
    $('.review-note').textContent='账户设计待评审 · 模拟数据';
    window.addEventListener('prototype-theme-changed',()=>$('.review-note').textContent='账户设计待评审 · 模拟数据');
  }
  new ResizeObserver(()=>{updateTrimTips();positionTypePopup();}).observe($('.launcher'));
  window.rodelAccountsReview={setScenario,settleLogin,open:()=>openPopup('list'),close:closePopup,changeAvatar,
    addExternal:account=>{const existing=accounts.find(a=>a.id===account.id);if(existing)Object.assign(existing,account);else accounts.push(account);updateIdentity();openPopup('list');},
    updateExternal:(id,patch)=>{const account=accounts.find(a=>a.id===id);if(account)Object.assign(account,patch);updateIdentity();},
    getState:()=>({view,currentId,count:accounts.length,attempt,accounts:accounts.map(a=>({...a}))})};
  updateIdentity();
  if(new URLSearchParams(location.search).get('avatars')==='1') {
    document.body.classList.add('avatar-review-mode');
    $('.review-bar strong').textContent='09 / AVATARS';
    $('.review-bar > .account-review').innerHTML='<button data-avatar-review="menu">头像菜单</button><button data-avatar-review="picker">头像选择</button><button data-account-action="manage">账户管理</button>';
    $('.review-footer').innerHTML='<div class="avatar-review-note"><span>设计评审 · 模拟账号与头像，仅在本页内存保存</span><a href="account-avatars/gallery.html" target="_blank">查看全部 28 个头像 ↗</a><a href="account-avatars/DESIGN.md" target="_blank">交互流程 ↗</a></div>';
    $('.review-note').textContent='头像设计已认可 · HTML 演示';
    window.addEventListener('prototype-theme-changed',()=>$('.review-note').textContent='头像设计已认可 · HTML 演示');
    requestAnimationFrame(openMenu);
  }
  if(reviewMode&&new URLSearchParams(location.search).get('panel')!=='closed')openPopup('list');
})();
