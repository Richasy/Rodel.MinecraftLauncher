/* Interactive design only: all rooms, files, members and outcomes are simulated in memory. */
window.Multiplayer = (() => {
  iconPaths.multiplayer = 'M8 9a3 3 0 1 0 0-6 3 3 0 0 0 0 6M2 20v-3a6 6 0 0 1 12 0v3M17 4a3 3 0 0 1 0 6M17 14a5 5 0 0 1 5 5v1';
  iconPaths.link = 'M10 14l4-4M8 16l-2 2a4 4 0 0 1-6-6l5-5a4 4 0 0 1 6 0M16 8l2-2a4 4 0 0 1 6 6l-5 5a4 4 0 0 1-6 0';
  iconPaths.mpArrow = 'M5 12h14M13 6l6 6-6 6';
  iconPaths.mpBack = 'M19 12H5M11 6l-6 6 6 6';
  iconPaths.mpCopy = 'M8 8h12v13H8zM16 8V3H3v13h5';
  iconPaths.mpWorld = 'M4 9l8-6 8 6v12H4zM9 21v-7h6v7M2 9l10-8 10 8';
  iconPaths.mpOff = 'M3 3l18 18M7 7a8 8 0 0 0-3 6M11 5a9 9 0 0 1 10 8M7 17a5 5 0 0 1 5-4M16 16l1 1M11 21h2';
  iconPaths.mpChevron = 'M6 9l6 6 6-6';
  iconPaths.mpExit = 'M9 3H3v18h6M10 12h12M17 7l5 5-5 5';
  const sampleCode = 'U/4K3M-9V6A-2R8D-5XQ4';
  const hostPending = room => ['waiting','opening','auto-failed','manual','multiple'].includes(room.status);
  const localInstances = () => instances.filter(item => !item.serverId);
  const findInstance = id => instances.find(item => item.id === id) || localInstances()[0];
  const model = { view:'empty', room:null, instanceId:localInstances()[0]?.id, name:'雾谷周末', codeInput:'', packName:'', error:'', external:false, target:'copy', aligned:false, preparation:'idle', progress:0, retries:0, scenario:'empty' };
  let timer = null;
  let modalTrigger = null;
  let workflowMarkup = '';
  let emptyReviewInstances = null;
  iconPaths.mpClose = 'M6 6l12 12M18 6L6 18';
  iconPaths.mpPerson = 'M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8M5 21v-3a7 7 0 0 1 14 0v3';
  const icon = name => vectorIcon(name);
  const safe = value => escapeHtml(value ?? '');
  const button = (action, text, primary = false, glyph = '') => `<button class="small-button mp-button${primary ? ' mp-primary' : ''}" data-mp="${action}">${glyph ? icon(glyph) : ''}${text}</button>`;
  const back = action => `<button class="mp-back" data-mp="${action}" aria-label="返回">${icon('mpBack')}</button>`;
  const status = (text, kind = '') => `<span class="mp-status ${kind}">${text}</span>`;
  function parseRoomCode(text){
    // Mirrors the selected Terracotta parser: scan text, accept ASCII case and I/O aliases,
    // decode 16 base-34 digits in little-endian order, and require divisibility by seven.
    const alphabet='0123456789ABCDEFGHJKLMNPQRSTUVWXYZ';
    const input=String(text).replace(/[a-z]/g,char=>char.toUpperCase());
    for(const match of input.matchAll(/U\/[0-9A-Z]{4}(?:-[0-9A-Z]{4}){3}/g)){
      const digits=match[0].slice(2).replaceAll('-','').replaceAll('I','1').replaceAll('O','0');
      let value=0n;
      for(let i=digits.length-1;i>=0;i--)value=value*34n+BigInt(alphabet.indexOf(digits[i]));
      if(value%7n===0n)return 'U/'+digits.match(/.{4}/g).join('-');
    }
    return null;
  }
  function instancePreview(id = model.instanceId) {
      const item = findInstance(id);
      return item ? `<div class="mp-instance-preview"><span class="instance-icon">${cubeIcon(item.icon)}</span><div class="mp-instance-copy"><strong>${safe(item.name)}</strong><small>${safe(item.version)} · ${item.mods || 0} 个模组</small></div><button class="mp-info-button" data-mp="instance-content" aria-label="管理此实例的模组" title="管理模组">${icon('content')}</button></div>` : '<p>暂无本地实例</p>';
    }

  function instanceSelect(){
   const items=localInstances();
   if(!items.length)return `<section class="mp-local-empty" aria-label="本机游戏环境"><span>本机游戏</span><div><strong>尚无实例</strong><div class="mp-local-actions">${button('new-local','创建游戏实例',false,'plus')}${button('select-pack','导入整合包',false,'folder')}</div></div></section>`;
   return `<section class="mp-instance-setup"><label class="mp-field">使用实例<select id="mp-instance" aria-label="本地游戏实例">${items.map(item=>`<option value="${safe(item.id)}" ${item.id===model.instanceId?'selected':''}>${safe(item.name)} · ${safe(item.version)}</option>`).join('')}</select></label><div class="mp-local-actions">${button('new-local','创建游戏实例',false,'plus')}${button('select-pack','导入整合包',false,'folder')}</div></section>`;
  }

  function networkDrawing() {
     return '<span class="mp-entry-art"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 2 L21 7 L12 12 L3 7 Z M3 7 V17 L12 22 L21 17 V7 M12 12 V22"/></svg></span>';
  }

  function welcome(){
     const resume=['working','ready','failed','canceled'].includes(model.preparation) && model.view==='prepare';
     const task=resume?`<button class="mp-resume-task" data-mp="resume-workflow">${icon('download')}环境准备 · ${model.progress}%${icon('mpArrow')}</button>`:'';
     return `<div class="mp-entry-grid"><section class="mp-entry"><div class="mp-entry-main">${networkDrawing()}<h2>创建房间</h2><span class="mp-entry-label">本机世界</span>${button('create','选择实例',true,'plus')}</div><div class="mp-entry-recovery">${model.localOrigin==='create'?task:''}</div></section><section class="mp-entry"><div class="mp-entry-main"><span class="mp-entry-art">${icon('multiplayer')}</span><h2>加入房间</h2><span class="mp-entry-label">朋友的世界</span>${button('join','填写房间码',false,'mpArrow')}</div><div class="mp-entry-recovery">${model.localOrigin!=='create'?task:''}</div></section></div>`;
   }

  function creation() {
      const item = findInstance(model.instanceId);
      return `<header class="mp-task-heading"><div>${back('empty')}<h2>创建房间</h2></div></header><div class="mp-scroll"><section class="mp-form">${instanceSelect()}<input id="mp-pack-file" type="file" accept=".mrpack,.zip" hidden><label class="mp-field">房间名称<input id="mp-name" maxlength="60" value="${safe(model.name)}" autocomplete="off"></label>${model.error ? `<p class="mp-error" role="alert">${safe(model.error)}</p>` : ''}<div class="mp-actions"><button class="small-button mp-primary mp-button" data-mp="host-start" ${item && model.name.trim() ? '' : 'disabled'}>${icon('play')}创建并启动</button></div></section></div>`;
    }

  function joining(){
   const file=`<label class="mp-field">所选整合包<div class="mp-pack-input"><input readonly value="${safe(model.packName)}" aria-label="所选整合包"><button class="small-button mp-button" data-mp="select-pack" aria-label="更换整合包文件">${icon('folder')}</button><button class="mp-info-button" data-mp="clear-pack" aria-label="清除整合包选择">${icon('mpClose')}</button></div></label>`;
   return `<header class="mp-task-heading"><div>${back('empty')}<h2>加入房间</h2></div></header><div class="mp-scroll"><section class="mp-form"><label class="mp-field">陶瓦房间码<input type="text" id="mp-room-code" placeholder="U/XXXX-XXXX-XXXX-XXXX" spellcheck="false" ${model.error?'aria-invalid="true"':''} value="${safe(model.codeInput)}"></label>${model.error?`<p class="mp-error" role="alert">${safe(model.error)}</p>`:''}${instanceSelect()}<input id="mp-pack-file" type="file" accept=".mrpack,.zip" hidden>${model.packName?file:''}<div class="mp-actions"><button class="small-button mp-primary mp-button" data-mp="inspect" ${!localInstances().length&&!model.packName?'disabled':''}>检查并继续</button></div></section></div>`;
  }

  function alignment() {
      const item = findInstance(model.instanceId);
      const heading = `<header class="mp-task-heading"><div>${back('join')}<h2>${model.external?'选择游戏实例':'对齐游戏环境'}</h2></div></header>`;
      if (model.external) return `${heading}<div class="mp-scroll"><section class="mp-alignment"><div class="mp-alignment-main"><div class="mp-environment-state"></div><label class="mp-field">陶瓦房间码<input value="${safe(model.code || sampleCode)}" readonly aria-label="陶瓦房间码"></label>${instanceSelect()}<div class="mp-actions">${button('join-unverified','连接房间',true)}</div></div></section></div>`;
      if(!item||model.forceNewPackage)return `${heading}<div class="mp-scroll"><section class="mp-form"><div class="mp-environment-state"><strong>${safe(model.packName)}</strong><span class="mp-tag">新实例</span></div><label class="mp-field">实例名称<input id="mp-local-name" value="${safe(model.localName||model.packName.replace(/\.(?:mrpack|zip)$/i,'')+' · 联机')}" autocomplete="off"></label><dl class="mp-facts"><div><dt>游戏环境</dt><dd>按整合包配置</dd></div><div><dt>准备位置</dt><dd>新建独立实例</dd></div></dl><div class="mp-actions">${button('align-start','准备游戏实例',true)}</div></section></div>`;
      const same = item?.version === '1.21.1';
      return `${heading}<div class="mp-scroll"><section class="mp-alignment"><div class="mp-alignment-main"><div class="mp-environment-state"><strong>${safe(model.packName || '雾谷探索-1.4.mrpack')}</strong><span class="mp-tag">整合包环境</span></div><table class="mp-diff"><thead><tr><th>检查项</th><th>整合包环境</th><th>本地 · ${safe(item?.name)}</th><th>状态</th></tr></thead><tbody><tr><td>游戏版本</td><td>1.21.1</td><td>${safe(item?.version)}</td><td>${same ? '✓ 匹配' : '! 需要匹配'}</td></tr><tr><td>加载环境</td><td>0.16.10</td><td>待核验</td><td class="mp-difference">! 待核验</td></tr><tr><td>必需模组</td><td>100 个</td><td>97 个匹配</td><td class="mp-difference">+ 2 / ~ 1</td></tr><tr><td>游戏配置</td><td>1 份清单</td><td>1 个不同</td><td class="mp-difference">~ 1</td></tr><tr><td>客户端可选内容</td><td>不要求一致</td><td>性能优化、光影支持</td><td>保留本地</td></tr></tbody></table>${roomExpander('files','文件差异 · 4 项',`<table class="mp-diff"><tbody><tr><td>+ 缺少</td><td>mods/travel-points.jar</td></tr><tr><td>+ 缺少</td><td>mods/shared-base.jar</td></tr><tr><td>~ 替换</td><td>mods/shared-api.jar<small>0.115.0 → 0.116.1</small></td></tr><tr><td>~ 配置</td><td>config/travel-common.toml</td></tr></tbody></table>`)}<h3>准备位置</h3><label class="mp-choice"><input type="radio" name="mp-target" value="copy" ${model.target === 'copy' ? 'checked' : ''}><span>独立联机实例<small>雾谷探索 · 联机</small></span></label><label class="mp-choice"><input type="radio" name="mp-target" value="existing" ${model.target === 'existing' ? 'checked' : ''}><span>当前实例<small>${safe(item?.name)}</small></span></label><div class="mp-actions">${button('align-start',model.target === 'copy' ? '准备联机实例' : '审核这 4 项变更',true)}</div></div></section></div>`;
    }

  function versionSelect(label){
    return window.gameVersionSelect('mp-local-version',label,model.localVersion||'');
  }
  function localSetup(){
    const joining=model.localOrigin!=='create',confirming=joining&&!model.joinVersion;
    const heading=`<header class="mp-task-heading"><div>${back('local-back')}<h2>${confirming?'确认房主版本':'创建游戏实例'}</h2></div></header>`;
    if(confirming)return `${heading}<div class="mp-scroll"><section class="mp-form">${versionSelect('房主使用的版本')}<div class="mp-actions"><button class="small-button mp-primary mp-button" data-mp="confirm-target" ${model.localVersion?'':'disabled'}>使用此版本</button></div></section></div>`;
    return `${heading}<div class="mp-scroll"><section class="mp-form"><label class="mp-field">实例名称<input id="mp-local-name" value="${safe(model.localName??'联机游戏')}" autocomplete="off"></label>${joining?`<div class="mp-target-version"><div><span>房主游戏版本</span><strong>${safe(model.joinVersion)}</strong></div><button class="mp-inline-link" data-mp="change-target">更正</button></div>`:versionSelect('游戏版本')}<div class="mp-actions"><button class="small-button mp-primary mp-button" data-mp="prepare-local" ${model.localVersion&&model.localName?.trim()?'':'disabled'}>准备游戏实例</button></div></section></div>`;
   }
  function registerLocalEnvironment(){
    if(!model.localFlow||model.localRegisteredId)return;
    const version=model.localFlow==='base'?model.localVersion:'1.21.1';
    const record={id:`mp-local-${Date.now()}`,name:model.localName||'联机游戏',version,loader:model.localFlow==='base'?'原版':'Fabric',mods:model.localFlow==='base'?0:102,icon:'grass',java:version==='1.20.1'?17:21,memory:4,packFormat:model.localFlow==='pack'?'mrpack':undefined};
    instances.push(record);model.localRegisteredId=record.id;model.instanceId=record.id;selected=record;render(false);
   }
  function preparation() {
      const failed = model.preparation === 'failed', canceled = model.preparation === 'canceled', ready = model.preparation === 'ready';
      if(model.localFlow)return `<header class="mp-task-heading"><div>${back('local-prepare-back')}<h2>${ready?'游戏实例已就绪':canceled?'准备已取消':'准备游戏实例'}</h2></div></header><div class="mp-scroll"><section class="mp-preparation"><div class="mp-environment-state"><strong>${safe(model.localName??'联机游戏')}</strong><span class="mp-tag">新实例</span></div><div class="mp-prep-row">${icon(ready?'success':'download')}<div><strong>${model.localFlow==='pack'?'整合包与游戏文件':'游戏文件与运行环境'}</strong><small>${ready?'已准备':model.progress+'%'}</small><progress value="${model.progress}" max="100" aria-label="新实例准备进度"></progress></div></div><div class="mp-actions">${ready?button('finish-local',model.localOrigin==='create'?'继续创建房间':'继续加入房间',true):canceled||failed?button('prepare-retry','继续准备',true):button('prepare-cancel','取消准备')}</div></section></div>`;

      const recovery=failed?`<div class="mp-download-failure" aria-label="中断的文件"><div class="mp-file-title"><strong>travel-points.jar</strong><span class="mp-tag">下载中断</span></div><div class="mp-file-actions">${button('manual-file',model.manualFileName?'更换本地文件':'选择本地文件')}${model.manualFileName?`<small>${safe(model.manualFileName)} · 待校验</small>`:''}</div><input id="mp-repair-file" type="file" accept=".jar" aria-label="选择 travel-points.jar 的本地文件" hidden></div>`:'';
      return `<header class="mp-task-heading"><div>${back('align')}<h2>${ready ? '环境已就绪' : failed ? '准备中断' : canceled ? '准备已取消' : '准备游戏环境'}</h2></div></header><div class="mp-scroll"><section class="mp-preparation"><div class="mp-environment-state"><strong>${model.target === 'copy' ? '雾谷探索 · 联机' : safe(findInstance(model.instanceId)?.name)}</strong><span class="mp-tag">${model.target === 'copy' ? '独立实例' : '已建立备份'}</span></div><div class="mp-prep-row">${icon('success')}<div><strong>环境清单</strong><small>已核对</small></div></div><div class="mp-prep-row">${icon(ready ? 'success' : 'download')}<div><strong>${ready?'模组与配置':model.manualFileName&&!failed?'校验本地文件与补全配置':'补全模组与配置'}</strong><small>${ready ? '已完成' : `${model.progress}% · ${Math.round(model.progress * 1.28)} / 128 MB`}</small><progress value="${model.progress}" max="100" aria-label="环境准备进度"></progress>${recovery}</div></div><div class="mp-prep-row">${icon(ready ? 'success' : 'instances')}<div><strong>游戏运行环境</strong><small>${ready ? '已就绪' : '等待补全'}</small></div></div><div class="mp-actions">${ready ? button('connect','连接房间',true) : failed || canceled ? button('prepare-retry','继续准备',true) : button('prepare-cancel','取消准备')}</div></section></div>`;
    }

  function roomNotice(room) {
      if (room.status === 'waiting') return `<div class="mp-aside-actions">${button('return-game','返回游戏',true)}</div>`;
      if (room.status === 'opening') return '';
      if (room.status === 'auto-failed') return `<section class="mp-notice error" role="alert">${icon('info')}<div><strong>未能自动开启联机</strong><p>在游戏暂停菜单中选择“对局域网开放”。</p><div class="mp-aside-actions">${button('retry-publish','重试',true)}${button('return-game','返回游戏')}</div></div></section>`;
      if (room.status === 'manual') return `<section class="mp-notice">${icon('mpWorld')}<div><p>在游戏暂停菜单中选择“对局域网开放”。</p><div class="mp-aside-actions">${button('return-game','返回游戏',true)}</div></div></section>`;
      if (room.status === 'multiple') return `<section class="mp-notice error" role="alert">${icon('info')}<div><strong>检测到两个已开放的游戏</strong><p>请先关闭另一个游戏的局域网联机。</p><div class="mp-aside-actions">${button('detect-lan','重新检测',true)}</div></div></section>`;
      if (room.status === 'connecting') return '';
      if (['disconnected','retrying','closed'].includes(room.status)) {
        const closed = room.status === 'closed', retrying = room.status === 'retrying';
        return `<section class="mp-notice error" role="alert">${icon('mpOff')}<div><div class="mp-aside-actions">${retrying ? button('retry-stop','停止重试') : button('retry','重试连接',true,'refresh')}${!retrying ? button('fallback',room.role === 'host' ? '备用线路' : '备用地址') : ''}${closed ? button('change-code','更换房间码') : ''}</div></div></section>`;
      }
      if (room.path === 'fallback') return `<section class="mp-notice">${icon('link')}<div><strong>请在游戏中重新连接</strong></div></section>`;
      if (room.role === 'guest') return `<section class="mp-notice">${icon('play')}<div><div class="mp-aside-actions">${button('launch-guest',room.launched ? '返回游戏' : '启动并连接',true,'play')}</div></div></section>`;
      return '';
    }

  function member(name, detail, label, self = false, kind = '') {
    return `<div class="mp-member"><span class="mp-member-avatar">${icon(self ? 'avatar' : 'mpPerson')}</span><div class="mp-member-copy"><strong>${safe(name)}${self ? ' <span class="mp-tag">你</span>' : ''}</strong><small>${safe(detail)}</small></div>${status(label,kind)}</div>`;
  }
  function roomIdentity(room){
    const world=room.role==='host'?room.world?.name:'';
    return `<div class="mp-room-title"><span class="mp-world-icon">${icon('mpWorld')}</span><div><h2>${safe(room.name)}</h2>${world&&world!==room.name?`<p>${safe(world)}</p>`:''}<small>${room.role==='host'?'本机房主':'加入者'}</small></div></div>`;
  }
  function roomInvite(room){
    if(room.status!=='connected')return '';
    const address=room.path==='fallback'?room.fallbackAddress||'demo-relay.example:25565':room.role==='host'?sampleCode:'127.0.0.1:34567';
    return `<div class="mp-room-connection"><input class="mp-invite-code" readonly value="${safe(address)}" aria-label="${room.role==='host'&&room.path!=='fallback'?'陶瓦房间码':'游戏连接地址'}"><button class="small-button subtle" data-mp="copy-code" aria-label="复制">${icon('copy')}</button></div>`;
  }
  function roomSidebar(room) {
      const host = room.role === 'host', active = room.status === 'connected', fallbackPath = room.path === 'fallback';
      const address = fallbackPath ? room.fallbackAddress || 'demo-relay.example:25565' : host ? sampleCode : '127.0.0.1:34567';
      return `<section class="mp-aside-section"><div class="mp-section-title"><h3>游戏环境</h3></div>${instancePreview(room.instanceId)}${host ? `<div class="mp-aside-actions">${button('share-pack','分享整合包')}</div>` : ''}<button class="mp-inline-link mp-export-log" data-mp="export-connection-log">${icon('download')}导出连接记录</button></section>`;
    }

  function roomPage(){
     const room=model.room,host=room.role==='host',active=room.status==='connected',offline=['disconnected','retrying','closed'].includes(room.status);
     const stateText={waiting:'等待进入世界',opening:'开启中', 'auto-failed':'待恢复',manual:'等待开放',multiple:'待确认',connecting:'连接中',connected:room.path==='fallback'?'备用线路':'已连接',disconnected:model.retries>=3?'重试未成功':'已断开',retrying:`重试连接 · ${model.retries} / 3`,closed:'房间已结束'}[room.status];
     const exitAction=room.status==='connecting'?'cancel-connect':'leave';
     const exitText=room.status==='connecting'?'取消连接':host&&hostPending(room)?'取消创建':host?'结束并退出游戏':'离开并退出游戏';
     const members=host?member('Steve','房主',offline?'已断开':active?'已连接':'待开放',true,offline?'warn':active?'ok':''):member('林间','房主',active?'已连接':'未连通',false,active?'ok':'');
     const guests=host&&active?member('林间','成员','已连接',false,'ok')+member('方糖','成员','已连接',false,'ok'):!host?member('Steve','成员',active?'已连接':'待连接',true,active?'ok':''):'';
     return `<header class="mp-room-header">${roomIdentity(room)}<div class="mp-room-header-trailing"><div class="mp-room-header-actions">${status(stateText,offline?'warn':active?'ok':'')}${room.status==='closed' ? button('create','创建房间')+button('join','加入房间') : `<div class="mp-room-split"><button class="small-button mp-button" data-mp="${exitAction}">${exitText}</button><details><summary aria-label="更多结束选项">${icon('mpChevron')}</summary><div class="mp-room-split-menu">${button('leave-only',host?'仅停止联机':'仅离开房间')}</div></details></div>`}</div>${roomInvite(room)}</div></header><div class="mp-room-grid"><section class="mp-room-main" tabindex="0" aria-label="联机成员区域">${roomNotice(room)}<div class="mp-section-title"><h3>联机成员</h3><span>${offline?'上次成员':(host&&active?'3':active?'2':'1')+' 人'}</span></div><div class="mp-members">${members}${guests}</div></section><aside class="mp-aside" tabindex="0" aria-label="房间邀请与环境区域">${roomSidebar(room)}</aside></div>`;
   }

  function page(){
   return `<header class="page-heading"><div class="mp-heading-title"><h1>联机</h1><span class="mp-provider-label">陶瓦联机</span><button class="mp-info-button" data-mp="help-multiplayer" aria-label="联机说明" aria-expanded="false">${icon('info')}</button></div></header><section class="mp-workspace page-surface" data-mp-view="${model.room?'room':'empty'}" aria-label="联机房间工作区">${model.room?roomPage():welcome()}</section>`;
   }

  function roomExpander(id,title,body){
     return settingsGroup('multiplayer.'+id,title,body).replace('class="settings-expander"','class="settings-expander mp-expander"');
   }
  function closeWorkflow(){
     const popup=document.querySelector('.mp-workflow-popup');
     if(popup){popup.close();popup.remove();}
     workflowMarkup='';
   }
  function dismissWorkflow(){
     if(model.preparation==='working'){clearTimeout(timer);model.preparation='canceled';}
     model.popupOpen=false;closeWorkflow();refresh();
     document.querySelector(`[data-mp="${model.view==='create'?'create':'join'}"]`)?.focus();
   }
  function positionWorkflow(){
     const popup=document.querySelector('.mp-workflow-popup');if(!popup)return;
     const bounds=document.querySelector('.launcher').getBoundingClientRect();
     popup.style.maxHeight=`${Math.max(200,bounds.height-32)}px`;
     popup.style.width=`${Math.min(720,bounds.width-40)}px`;
     const rect=popup.getBoundingClientRect();
     popup.style.left=`${bounds.left+(bounds.width-rect.width)/2}px`;
     popup.style.top=`${bounds.top+Math.max(16,(bounds.height-rect.height)/2)}px`;
   }
  function mountWorkflow(){
     if(currentPage!=='multiplayer'||(model.room&&model.room.status!=='closed')||!['create','join','align','prepare','local'].includes(model.view)||model.popupOpen===false){closeWorkflow();return;}
     const markup=model.view==='create'?creation():model.view==='join'?joining():model.view==='local'?localSetup():model.view==='align'?alignment():preparation();
     let popup=document.querySelector('.mp-workflow-popup');
     if(popup&&workflowMarkup===markup){positionWorkflow();return;}
     const scroll=popup?.dataset.stage===model.view?popup.querySelector('.mp-scroll')?.scrollTop||0:0;
     const focusId=popup?.contains(document.activeElement)?document.activeElement.id:null;
     const fresh=!popup;
     if(fresh){popup=document.createElement('dialog');popup.className='mp-workflow-popup';popup.setAttribute('aria-labelledby','mp-workflow-title');popup.addEventListener('cancel',event=>{if(event.target!==popup){if(event.target.matches('input[type="file"]'))popup.querySelector(event.target.id==='mp-repair-file'?'[data-mp="manual-file"]':'[data-mp="select-pack"]')?.focus();return;}event.preventDefault();dismissWorkflow();});document.querySelector('.launcher').append(popup);}
     popup.dataset.stage=model.view;workflowMarkup=markup;popup.innerHTML=markup;
     const heading=popup.querySelector('.mp-task-heading');heading.querySelector('h2').id='mp-workflow-title';
     if(['create','join'].includes(model.view))heading.querySelector('.mp-back')?.remove();
     const tools=document.createElement('div');tools.className='mp-popup-title-tools';
     const info=heading.querySelector('.mp-info-button');if(info)tools.append(info);
     tools.insertAdjacentHTML('beforeend',`<button class="mp-popup-close" data-mp="dismiss-workflow" aria-label="关闭">${icon('mpClose')}</button>`);heading.append(tools);
     const actions=popup.querySelector('.mp-actions');
     if(actions){actions.querySelectorAll('svg,[data-icon]').forEach(glyph=>glyph.remove());const footer=document.createElement('footer');footer.className='mp-popup-actions';footer.append(actions);popup.append(footer);}
     upgradeCombos(popup);if(fresh)popup.showModal();positionWorkflow();
     const body=popup.querySelector('.mp-scroll');if(body)body.scrollTop=scroll;
     if(focusId)popup.querySelector(`#${focusId}`)?.focus();else if(fresh)popup.querySelector(model.view==='create'?'#mp-name':model.view==='join'?'#mp-room-code':'.mp-task-heading button')?.focus();
   }
  function refresh(focusSelector) {
    if (currentPage === 'multiplayer') {
      document.querySelectorAll('.mp-workflow-popup [data-settings-group],.mp-workspace [data-settings-group]').forEach(group => expandedSettingGroups.set(group.dataset.settingsGroup,group.open));
      const scrolls = ['.mp-room-main','.mp-room-grid > .mp-aside'].map(selector => [selector,document.querySelector(selector)?.scrollTop || 0]);
      renderPage();
      for (const [selector,offset] of scrolls) { const column = document.querySelector(selector); if (column) column.scrollTop = offset; }
      if (focusSelector) document.querySelector(focusSelector)?.focus();
    }
    syncShortcuts();
  }
  function setView(view, focusSelector) { model.view = view; model.popupOpen = true; model.error = ''; refresh(focusSelector); }
  function create(instanceId) {
    if (model.room && model.room.status !== 'closed') { model.view = 'room'; navigate('multiplayer'); return; }
    model.instanceId = instanceId || selected?.id || localInstances()[0]?.id;
    model.view = 'create'; model.popupOpen = true; model.error = ''; navigate('multiplayer');
    document.getElementById('mp-name')?.focus();
  }
  function makeRoom(role, settings = {}) {
    return { role, name:model.name || '雾谷周末', instanceId:model.instanceId, status:'connected', path:'terracotta', verified:model.aligned, external:model.external, world:null, ...settings };
  }
  function connect() {
    model.room = makeRoom('guest',{status:'connecting',verified:model.aligned});
    model.view = 'room'; refresh(); clearTimeout(timer);
    timer = setTimeout(() => { if (model.room?.status === 'connecting') { model.room.status = 'connected'; refresh(); } }, 1100);
  }
  function prepare() {
    clearTimeout(timer); model.view = 'prepare'; model.popupOpen = true; model.preparation = 'working'; model.progress = Math.max(model.progress,12); refresh();
    const tick = () => {
      if (model.preparation !== 'working') return;
      model.progress = Math.min(100,model.progress + 22);
      if (model.progress === 100) { model.preparation = 'ready'; model.aligned = true; registerLocalEnvironment(); }
      refresh(); if (model.preparation === 'working') timer = setTimeout(tick,750);
    };
    timer = setTimeout(tick,750);
  }
  function showInfo(action,trigger) {
    document.querySelector('.mp-info-popover')?.remove();
    const content = ['联机说明','<p>世界存档保存在房主使用的实例中。</p><p>© Burning_TNT</p>'];
    const popup = document.createElement('section'); popup.className = 'mp-info-popover'; popup.setAttribute('popover','auto'); popup.setAttribute('aria-label',content[0]);
    popup.innerHTML = `<h3>${content[0]}</h3>${content[1]}`;
    (trigger.closest('dialog') || document.querySelector('.launcher')).append(popup); popup.showPopover(); trigger.setAttribute('aria-expanded','true');
    const r = trigger.getBoundingClientRect(), bounds = popup.getBoundingClientRect();
    popup.style.left = `${Math.max(12,Math.min(r.right-bounds.width,innerWidth-bounds.width-12))}px`;
    popup.style.top = `${r.bottom+bounds.height+12 <= innerHeight ? r.bottom+8 : Math.max(12,r.top-bounds.height-8)}px`;
    popup.addEventListener('toggle',event => { if (event.newState === 'closed') trigger.setAttribute('aria-expanded','false'); });
  }
  function closeModal() { document.querySelector('.mp-modal')?.close(); }
  function modal(title, body, primary, action, trigger, secondary = '取消') {
    closeModal(); modalTrigger = trigger || document.activeElement;
    const dialog = document.createElement('dialog'); dialog.className = 'mp-modal'; dialog.setAttribute('aria-labelledby','mp-modal-title');
    dialog.innerHTML = `<header><h2 id="mp-modal-title">${title}</h2></header>${body?`<div class="mp-modal-body"><div class="mp-modal-content">${body}</div></div>`:''}<footer>${primary && action !== 'modal-close' ? button(action,primary,true) : ''}${button('modal-close',secondary)}</footer>`;
    dialog.addEventListener('close',() => { dialog.remove(); if (modalTrigger?.isConnected) modalTrigger.focus(); else document.querySelector('.nav-button[data-page="multiplayer"]')?.focus(); });
    document.querySelector('.launcher').append(dialog); dialog.showModal();
  }
  function fallback(trigger) {
      const host = model.room.role === 'host';
      modal('使用服务器地址',`<label class="mp-field">${host?'服务器地址':'房主提供的地址'}<input id="mp-fallback-address" placeholder="play.example.com:25565" autocomplete="off"></label>`,'使用此地址',host?'fallback-host':'fallback-guest',trigger);
    }

  function scenario(name) {
    clearTimeout(timer); closeModal();
    if(emptyReviewInstances){instances.splice(0,instances.length,...emptyReviewInstances);selected=instances[0]||null;emptyReviewInstances=null;render(false);}
    if(name==='no-instances'){emptyReviewInstances=instances.slice();instances.length=0;selected=null;render(false);}
    Object.assign(model,{view:'empty',popupOpen:true,room:null,localFlow:null,forceNewPackage:false,manualFileName:'',localName:'',localVersion:'',joinVersion:'',hostVersion:'',localRegisteredId:null,localOrigin:'join',scenario:name,error:'',instanceId:localInstances()[0]?.id,name:'雾谷周末',external:false,aligned:false,target:'copy',progress:0,preparation:'idle',retries:0,codeInput:sampleCode,packName:['align','download-failed','guest'].includes(name)?'雾谷探索-1.4.mrpack':''});
    if (name === 'create' || name === 'join') model.view = name;
    if (name === 'join') model.codeInput = '';
    if(name==='no-instances'){model.view='join';model.codeInput='';}
    model.code = sampleCode;
    if (name === 'align' || name === 'download-failed') model.view = 'align';
    if (name === 'download-failed') { model.view = 'prepare'; model.preparation = 'failed'; model.progress = 64; }
    if (['host','guest','disconnected','closed','multiple','external','waiting','auto-failed','manual'].includes(name)) {
      model.view = 'room'; model.aligned = name === 'guest'; model.external = name === 'external';
      model.room = makeRoom(['host','multiple','waiting','auto-failed','manual'].includes(name) ? 'host' : 'guest',{status:name === 'disconnected' || name === 'external' ? 'disconnected' : ['closed','multiple','waiting','auto-failed','manual'].includes(name) ? name : 'connected'});
      if(name==='host')model.room.world={name:'雾谷营地',state:'open'};
    }
    navigate('multiplayer'); syncShortcuts();
  }
  async function copyText(value, message) {
    try { await navigator.clipboard.writeText(value); showToast(message); }
    catch { modal('手动复制',`<label class="mp-field"><textarea readonly>${safe(value)}</textarea></label>`,'选择全部','select-copy',null,'关闭'); }
  }
  function syncShortcuts() {
    document.querySelector('.mp-info-popover')?.remove();
    const hero = document.querySelector('.hero');
    hero.querySelector('.mp-home-link')?.remove();
    if (model.room && window.reviewMultiplayerEnabled===true) {
      const link = document.createElement('button'); link.className = 'mp-home-link'; link.dataset.mp = 'return-room';
      const interrupted = ['disconnected','retrying','closed'].includes(model.room.status);
      link.innerHTML = `${icon(interrupted ? 'mpOff' : 'multiplayer')}<span><strong>${safe(model.room.name)}</strong><small>${interrupted ? '房间需要恢复连接' : hostPending(model.room) ? '联机准备中' : '当前联机房间'} · 返回房间</small></span>${icon('mpArrow')}`;
      hero.append(link);
    }
    mountWorkflow();
    const multiplayer = currentPage === 'multiplayer';
    const review = document.querySelector('.mp-review'); if (review) review.hidden = !multiplayer;
    document.querySelector('.review-states').hidden = multiplayer;
    if (multiplayer) {
      const currentScenario = model.view === 'room' ? model.room.status === 'multiple' ? 'multiple' : model.room.status === 'closed' ? 'closed' : ['disconnected','retrying'].includes(model.room.status) ? model.room.external ? 'external' : 'disconnected' : model.room.status === 'waiting' ? 'create' : model.room.role === 'host' ? 'host' : 'guest' : model.view === 'prepare' ? 'download-failed' : model.view;
      document.querySelectorAll('[data-mp-scenario]').forEach(item => item.setAttribute('aria-pressed',String(item.dataset.mpScenario === currentScenario)));
      document.querySelector('.review-note').textContent = '联机设计评审 · 全部为模拟数据';
    }
    document.querySelector('.review-bar > div:first-child strong').textContent = multiplayer ? '07 / MULTIPLAYER' : reviewHeading;
    document.querySelector('.review-footer > span').textContent = multiplayer ? '' : reviewFooter;
    const restore = document.querySelector('[data-mp="network-restored"]');
    if (restore) restore.hidden = !multiplayer || !model.room || !['disconnected','retrying'].includes(model.room.status);
    for(const action of ['world-ready','publish-failed','lan-observed']){
      const control=document.querySelector(`[data-mp="${action}"]`);
      if(control)control.hidden=!multiplayer||model.room?.role!=='host'||(action==='world-ready'?model.room.status!=='waiting':action==='publish-failed'?!['waiting','opening'].includes(model.room.status):!['manual','auto-failed','multiple'].includes(model.room.status));
    }
  }
  document.addEventListener('input',event => {
    if(event.target.id==='mp-local-name'){model.localName=event.target.value;const action=document.querySelector('[data-mp="prepare-local"]')||document.querySelector('[data-mp="align-start"]');if(action)action.disabled=!model.localName.trim()||(action.dataset.mp==='prepare-local'&&!model.localVersion);}
    if (event.target.id === 'mp-name') { model.name = event.target.value; model.error = ''; event.target.closest('.mp-form').querySelector('.mp-error')?.remove(); const start=document.querySelector('[data-mp="host-start"]'); if(start)start.disabled=!findInstance()||!model.name.trim(); }
    if (event.target.id === 'mp-room-code') { if(parseRoomCode(event.target.value)!==parseRoomCode(model.codeInput)){model.joinVersion='';if(model.localOrigin==='join')model.localVersion='';}model.codeInput = event.target.value; model.error = ''; event.target.removeAttribute('aria-invalid'); event.target.closest('.mp-form').querySelector('.mp-error')?.remove(); }
  });
  document.addEventListener('change',event => {
    if(event.target.id==='mp-repair-file'&&event.target.files[0]){model.manualFileName=event.target.files[0].name;refresh('[data-mp="manual-file"]');return;}
    if (event.target.id === 'mp-pack-file' && event.target.files[0]) { model.packName = event.target.files[0].name; model.aligned = false; if(model.view==='create'){model.forceNewPackage=true;model.localName=model.packName.replace(/\.(?:mrpack|zip)$/i,'')+' · 联机';setView('align');}else refresh('[data-mp="select-pack"]'); }
    if(event.target.id==='mp-local-version'){model.localVersion=event.target.value;if(model.localOrigin==='create')model.hostVersion=model.localVersion;refresh();}
    if (event.target.id === 'mp-instance') { model.instanceId = event.target.value; model.aligned = false; refresh(); document.querySelector('#mp-instance')?.parentElement.querySelector('.combo-face')?.focus(); }
    if (event.target.name === 'mp-target') { model.target = event.target.value; refresh(`[name="mp-target"][value="${model.target}"]`); }
  });
  document.addEventListener('click',event => {
    const scene = event.target.closest('[data-mp-scenario]'); if (scene) { event.stopImmediatePropagation(); scenario(scene.dataset.mpScenario); return; }
    const trigger = event.target.closest('[data-mp]'); if (!trigger) return;
    event.stopImmediatePropagation(); const action = trigger.dataset.mp; const room = model.room;
    if (['empty','join','align'].includes(action)) { if (room && room.status !== 'closed') { setView('room'); return; } if (action === 'join') model.error = ''; setView(action); return; }
    if (action === 'dismiss-workflow') { dismissWorkflow(); return; }
    if (action === 'resume-workflow') { model.popupOpen = true; refresh(); return; }
    if (action.startsWith('help-')) { showInfo(action,trigger); return; }
    if(action==='new-local'){model.localOrigin=model.view==='create'?'create':'join';model.localRegisteredId=null;if(model.localOrigin==='join'&&model.packName){model.forceNewPackage=true;model.external=false;model.localName=model.packName.replace(/\.(?:mrpack|zip)$/i,'')+' · 联机';setView('align');return;}model.forceNewPackage=false;model.localFlow='base';model.localName||='联机游戏';model.localVersion=model.localOrigin==='create'?model.hostVersion||'':model.joinVersion||'';setView('local');return;}
    if(action==='confirm-target'){if(!model.localVersion)return;model.joinVersion=model.localVersion;setView('local');return;}
    if(action==='change-target'){model.joinVersion='';setView('local');return;}
    if(action==='local-back'){setView(model.localOrigin||'join');return;}
    if(action==='local-prepare-back'){if(model.preparation==='working'){clearTimeout(timer);model.preparation='canceled';}setView(model.localFlow==='base'?'local':'align');return;}
    if(action==='prepare-local'){if(!model.localVersion||!(model.localName||'联机游戏').trim()||model.localOrigin==='join'&&model.localVersion!==model.joinVersion)return;model.progress=0;model.localRegisteredId=null;prepare();return;}
    if(action==='finish-local'){model.localFlow=null;model.forceNewPackage=false;model.preparation='idle';model.progress=0;model.aligned=false;setView(model.localOrigin||'join');return;}
    if (action === 'select-pack') { model.localOrigin=model.view==='create'?'create':'join';document.getElementById('mp-pack-file').click(); return; }
    if (action === 'clear-pack') { model.packName = ''; model.aligned = false; refresh('[data-mp="select-pack"]'); return; }
    if (action === 'create') { create(); return; }
    if (action === 'return-room') { model.view = 'room'; navigate('multiplayer'); return; }
    if(action==='instance-content'){const item=instances.find(item=>item.id===room?.instanceId);if(!item)return;selected=item;contentTab='mods';contentFilter='';navigate('content');return;}
    if (action === 'host-start') {
      if (!model.name.trim()) { model.error = '请为这次房间填写一个名称。'; refresh('#mp-name'); return; }
      model.room = makeRoom('host',{status:'waiting'}); model.view = 'room'; refresh(); return;
    }
    if (action === 'detect-lan') { room.status = 'connected'; refresh(); return; }
    if(action==='return-game'){return;}
    if(action==='world-ready'||action==='retry-publish'){clearTimeout(timer);if(action==='world-ready')room.world={name:'雾谷营地',state:'opening'};else if(room.world)room.world.state='opening';room.status='opening';refresh();timer=setTimeout(()=>{if(model.room===room&&room.status==='opening'){room.status='connected';if(room.world)room.world.state='open';refresh();}},1300);return;}
    if(action==='publish-failed'){clearTimeout(timer);room.status='auto-failed';if(room.world)room.world.state='loaded';refresh();return;}
    if(action==='lan-observed'){room.status='connected';if(room.world)room.world.state='open';refresh();return;}
    if (action === 'inspect') {
      const code = parseRoomCode(model.codeInput);
      if (!code) { model.error = '房间码校验未通过，请重新复制。'; refresh('#mp-room-code'); return; }
      clearTimeout(timer); model.code = code; model.codeInput = code; model.external = !model.packName;
      model.name = '朋友的房间'; model.localFlow=null;model.manualFileName=''; model.aligned = false; model.preparation = 'idle'; model.progress = 0; if(!findInstance(model.instanceId)&&!model.packName){refresh('[data-mp="new-local"]');return;} setView('align'); return;
    }
    if (action === 'align-start') {
      if(!findInstance(model.instanceId)||model.forceNewPackage){const nameInput=document.getElementById('mp-local-name');model.localName=nameInput.value.trim();if(!model.localName)return;model.localFlow='pack';model.localRegisteredId=null;model.progress=0;prepare();return;}
      if (model.target === 'existing') modal('确认调整本地实例',`<p>将为「${safe(findInstance(model.instanceId)?.name)}」补充 2 个模组、替换 1 个模组与 1 个配置。</p>`,'备份并应用','review-confirm',trigger);
      else prepare(); return;
    }
    if (action === 'review-confirm') { closeModal(); prepare(); return; }
    if (action === 'prepare-cancel') { clearTimeout(timer); model.preparation = 'canceled'; refresh(); return; }
    if(action==='manual-file'){document.getElementById('mp-repair-file')?.click();return;}
    if (action === 'prepare-retry') { prepare(); return; }
    if (action === 'join-unverified' || action === 'connect') {
      if (action === 'connect' && model.target === 'copy' && !instances.some(item => item.id === 'mp-demo-copy')) {
        const source = findInstance(model.instanceId); instances.push({...source,id:'mp-demo-copy',name:'雾谷探索 · 联机',version:'1.21.1',loader:'Fabric',mods:102,packFormat:'mrpack'}); model.instanceId = 'mp-demo-copy'; render(false);
      }
      connect(); return;
    }
    if (action === 'cancel-connect') { clearTimeout(timer); model.room = null; setView('align'); return; }
    if (action === 'launch-guest') { room.launched = true; refresh(); return; }
    if (action === 'copy-code') { copyText(room.path === 'fallback' ? room.fallbackAddress || 'demo-relay.example:25565' : room.role === 'host' ? sampleCode : '127.0.0.1:34567','已复制'+(room.role === 'host' && room.path !== 'fallback' ? '房间码' : '游戏地址')); return; }
    if (action === 'share-pack') {
      const item = findInstance(room.instanceId);
      modal('分享整合包',`<dl class="mp-facts"><div><dt>实例</dt><dd>${safe(item?.name)}</dd></div><div><dt>游戏版本</dt><dd>${safe(item?.version)}</dd></div><div><dt>环境状态</dt><dd>${room.verified ? '整合包已准备' : '本机环境'}</dd></div><div><dt>格式</dt><dd>MRPACK</dd></div><div><dt>包含</dt><dd>版本、模组与必要配置</dd></div><div><dt>排除</dt><dd>存档、账号、个人选项</dd></div></dl>`,'导出整合包','export-pack',trigger); return;
    }
    if (action === 'export-pack') { closeModal(); return; }
    if(action==='export-connection-log'){
      const contents=['联机连接记录（模拟）','本文件仅包含HTML交互原型中的状态快照，不是真实联机日志。',`房间：${room.name}`,`角色：${room.role==='host'?'房主':'加入者'}`,`线路：${room.path==='fallback'?'备用地址':'陶瓦'}`,`状态：${room.status}`,`重试次数：${model.retries}`].join('\n');
      const url=URL.createObjectURL(new Blob([contents],{type:'text/plain;charset=utf-8'})),link=document.createElement('a');link.href=url;link.download='联机连接记录-示例.txt';document.body.append(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);return;
    }
    if (action === 'leave-only') { clearTimeout(timer);room.status='closed';model.view='room';model.popupOpen=false;model.retries=0;refresh();return; }
    if (action === 'leave') {
      clearTimeout(timer);room.status='closed';room.launched=false;model.view='room';model.popupOpen=false;model.retries=0;refresh();return;
    }
    if (action === 'retry') {
      clearTimeout(timer); model.retries = 0; room.status = 'retrying';
      const retry = () => { if (room !== model.room || room.status !== 'retrying') return; model.retries++; refresh(); if (model.retries < 3) timer = setTimeout(retry,1000); else timer = setTimeout(() => { if (room === model.room && room.status === 'retrying') { room.status = 'disconnected'; refresh(); } },700); };
      retry(); return;
    }
    if (action === 'retry-stop') { clearTimeout(timer); room.status = 'disconnected'; refresh(); return; }
    if (action === 'network-restored') { clearTimeout(timer); room.status = 'connected'; model.retries = 0; refresh(); return; }
    if (action === 'fallback') { fallback(trigger); return; }
    if (action === 'fallback-host' || action === 'fallback-guest') {
      const address = document.getElementById('mp-fallback-address').value.trim();
      if (!/^[a-z0-9.-]+(?::\d{1,5})?$/i.test(address) || address.includes('..') || Number(address.split(':')[1] || 25565) > 65535) {
        const content = trigger.closest('dialog').querySelector('.mp-modal-content'); content.querySelector('.mp-error')?.remove(); content.insertAdjacentHTML('beforeend','<p class="mp-error" role="alert">请输入有效的服务器地址，例如 play.example.com:25565。</p>'); document.getElementById('mp-fallback-address').focus(); return;
      }
      clearTimeout(timer); room.path = 'fallback'; room.fallbackAddress = address; room.status = 'connected'; closeModal(); refresh(); return;
    }
    if (action === 'change-code') { modal('更新房主的房间码','<label>新房间码<input id="mp-new-code" placeholder="U/…" autocomplete="off"></label>','重新连接','code-confirm',trigger); return; }
    if (action === 'code-confirm') {
      const input = document.getElementById('mp-new-code'), code = parseRoomCode(input.value);
      if (!code) { input.setAttribute('aria-invalid','true'); input.focus(); return; }
      model.code = code; model.codeInput = code; room.status = 'connected'; closeModal(); refresh(); return;
    }
    if (action === 'select-copy') { trigger.closest('dialog').querySelector('textarea').select(); return; }
    if (action === 'modal-close') closeModal();
  },true);
  document.addEventListener('keydown',event => {
    const help = document.querySelector('.mp-info-popover:popover-open');
    if (event.key === 'Escape' && help) { event.preventDefault(); event.stopImmediatePropagation(); help.hidePopover(); document.querySelector('.mp-info-button[aria-expanded="true"]')?.focus(); return; }
    if (event.key === 'Enter' && event.target.id === 'mp-name' && document.querySelector('.mp-workflow-popup[open]')) { event.preventDefault(); document.querySelector('[data-mp="host-start"]')?.click(); return; }
    if (currentPage !== 'multiplayer' || document.querySelector('dialog[open]')) return;
    if (event.key === 'Enter' && event.target.id === 'mp-name') { event.preventDefault(); document.querySelector('[data-mp="host-start"]')?.click(); }
    if (event.key === 'Escape' && ['create','join'].includes(model.view) && !comboSource) { event.preventDefault(); setView('empty','.nav-button[data-page="multiplayer"]'); }
  });
  window.addEventListener('prototype-page-changed',syncShortcuts);
  window.addEventListener('prototype-theme-changed',syncShortcuts);
  window.addEventListener('resize',positionWorkflow);
  document.addEventListener('toggle',event => { if (event.target.matches('[data-settings-group]') && event.target.closest('.mp-workflow-popup')) positionWorkflow(); },true);
  const review = document.createElement('div'); review.className = 'mp-review'; review.setAttribute('role','group'); review.setAttribute('aria-label','联机设计评审场景'); review.hidden = true;
  const reviewHeading = document.querySelector('.review-bar > div:first-child strong').textContent;
  const reviewFooter = document.querySelector('.review-footer > span').textContent;
  review.innerHTML = `<span class="mp-review-label">模拟场景</span>${[['empty','空态'],['create','创建'],['join','加入'],['no-instances','没有实例'],['align','环境差异'],['host','房主房间'],['guest','加入者'],['disconnected','断线恢复'],['download-failed','准备中断'],['external','外部房主'],['closed','世界关闭'],['multiple','多个游戏']].map(([key,label]) => `<button data-mp-scenario="${key}" aria-pressed="false">${label}</button>`).join('')}`;
  document.querySelector('.review-note').before(review);
  review.insertAdjacentHTML('beforeend','<button data-mp="network-restored" hidden>模拟网络恢复</button><button data-mp="world-ready" hidden>模拟进入世界</button><button data-mp="publish-failed" hidden>模拟自动开放失败</button><button data-mp="lan-observed" hidden>模拟检测到手动开放</button><button data-mp-scenario="manual">未适配版本</button>');
  decorate(document.querySelector('.rail'));
  const query = new URLSearchParams(location.search);
  window.Multiplayer = {page,create,scenario,syncShortcuts,hasActiveRoom:()=>!!model.room&&model.room.status!=='closed',endForFeature:()=>{clearTimeout(timer);if(model.room)model.room.status='closed';model.retries=0;syncShortcuts();}};
  if (query.get('presentation') === '1') document.documentElement.classList.add('mp-presentation');
  if (query.get('page') === 'multiplayer') scenario(query.get('room') || 'empty'); else syncShortcuts();
  return {page,create,scenario};
})();
