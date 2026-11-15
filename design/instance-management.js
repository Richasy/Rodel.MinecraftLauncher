/* Design interaction only. No filesystem operations or Core calls. */
let instanceMenuTrigger = null;
let instanceDialogTrigger = null;
let instanceMenuTarget = null;
function closeInstanceMenu(restore = false) {
  document.querySelectorAll('.instance-command-menu').forEach(menu => menu.remove());
  instanceMenuTrigger?.setAttribute('aria-expanded', 'false');
  instanceMenuTarget = null;
  if (restore && instanceMenuTrigger?.isConnected) instanceMenuTrigger.focus();
}
function closeInstanceSubmenu(restore = false) {
  const child = document.querySelector('.instance-submenu');
  if (!child) return;
  const parent = child.menuTrigger;
  child.remove();
  parent?.setAttribute('aria-expanded', 'false');
  if (restore && parent?.isConnected) parent.focus();
}
function openInstanceSubmenu(trigger, focus = false) {
  if (document.querySelector('.instance-submenu')?.menuTrigger === trigger) {
    if (focus) document.querySelector('.instance-submenu button')?.focus();
    return;
  }
  closeInstanceSubmenu();
  const root = trigger.closest('.instance-command-menu');
  const menu = document.createElement('div');
  menu.className = 'instance-command-menu instance-submenu';
  menu.setAttribute('role', 'menu');
  menu.setAttribute('aria-label', trigger.textContent.trim());
  menu.menuTrigger = trigger;
  menu.innerHTML = root.menuGroups[trigger.dataset.instanceSubmenu];
  document.querySelector('.launcher').append(menu);
  trigger.setAttribute('aria-expanded', 'true');
  const base = menu.parentElement.getBoundingClientRect(), anchor = trigger.getBoundingClientRect();
  const rtl = getComputedStyle(root).direction === 'rtl';
  let left = rtl ? anchor.left - base.left - menu.offsetWidth : anchor.right - base.left;
  if (left < 8 || left + menu.offsetWidth > base.width - 8)
    left = rtl ? anchor.right - base.left : anchor.left - base.left - menu.offsetWidth;
  menu.style.left = `${Math.max(8, Math.min(left, base.width - menu.offsetWidth - 8))}px`;
  menu.style.top = `${Math.max(40, Math.min(anchor.top - base.top, base.height - menu.offsetHeight - 16))}px`;
  if (focus) menu.querySelector('button')?.focus();
}
function openInstanceMenu(trigger, target = selected, simple = false, point = null) {
  closeInstanceMenu();
  const selected = target;
  instanceMenuTarget = target;
  instanceMenuTrigger = trigger;
  trigger.setAttribute('aria-expanded', 'true');
  const menu = document.createElement('div');
  menu.className = 'instance-command-menu';
  menu.setAttribute('role', 'menu');
  menu.setAttribute('aria-label', `管理 ${selected.name}`);
  const command = (id, name) => `<button role="menuitem" data-instance-command="${id}" class="${id==='remove'?'danger':''}">${name}</button>`;
  const group = (id, name) => `<button role="menuitem" data-instance-submenu="${id}" aria-haspopup="menu" aria-expanded="false">${name}<span aria-hidden="true">›</span></button>`;
  const separator = '<hr role="separator">';
  menu.menuGroups = {
    tools: command('runtime','运行环境') + command('bridge','游戏桥') + command('content','内容摘要')
      + (selected.packFormat ? separator + command('pack','整合包准备') + command('pack-update','整合包更新') : ''),
    backups: command('backup','备份全部游戏内容') + command('restore','恢复所选备份') + separator + command('export','导出整合包'),
    folders: command('folder','打开游戏目录') + command('runtime-folder','打开版本目录')
  };
  menu.innerHTML = simple
    ? command('rename','重命名实例') + command('icon','更换图标') + separator + command('remove','删除实例')
    : command('launch', selected.serverId ? '连接' : '开始游戏')
    + (selected.serverId ? command('local-launch','普通启动（不连接服务器）') : '')
    + command('settings','当前实例设置')
    + (!selected.serverId ? command('multiplayer','用此实例创建房间') : '')
    + separator + command('rename','重命名实例') + command('icon','更换图标') + command('copy','创建副本')
    + (selected.serverId ? command('release-client','转为本地实例') : '')
    + separator + group('tools','游戏与工具') + group('backups','备份与导出') + group('folders','打开目录')
    + separator + command('remove','删除实例');
  menu.addEventListener('pointerover', event => {
    const button = event.target.closest('button');
    if (button?.hasAttribute('data-instance-submenu')) openInstanceSubmenu(button);
    else if (button) closeInstanceSubmenu();
  });
  menu.addEventListener('scroll', () => closeInstanceSubmenu());
  document.querySelector('.launcher').append(menu);
  const base=menu.parentElement.getBoundingClientRect(), r=trigger.getBoundingClientRect();
  menu.style.left=`${Math.max(16,Math.min(point ? point.x-base.left : r.right-base.left-menu.offsetWidth,base.width-menu.offsetWidth-16))}px`;
  menu.style.top=`${Math.max(40,Math.min(point ? point.y-base.top : r.bottom-base.top+6,base.height-menu.offsetHeight-16))}px`;
  [...menu.querySelectorAll('button')].find(item=>item.offsetParent)?.focus();
}
function openInstanceEntry(kind) {
  if (kind === 'import') { if(currentPage!=='instances')navigate('instances'); openImportEntryMenu(document.querySelector('[data-instance-entry="import"]')); return; }
  if (kind !== 'create' && kind !== 'import') return;
  closeCombo();
  closeDrawer();
  closeInstanceMenu();
  if (currentPage !== 'instances') navigate('instances');
  instanceMenuTrigger = document.querySelector(`[data-instance-entry="${kind}"]`);
  openInstanceTask(kind);
}
function openInstanceTask(kind, target = selected) {
  const selected = target;
  if (kind === 'bridge') { openGameBridgeDesign(selected, instanceMenuTrigger); return; }
  closeInstanceMenu();
  if(kind === 'icon') {
    const target=selected;
    window.rodelAvatarPicker.chooseInstance(target, image => { if(image){target.icon=image;render();}restoreInstanceTrigger(); });
    return;
  }
  document.querySelector('.instance-dialog')?.remove();
  instanceDialogTrigger=instanceMenuTrigger;
  const dialog=document.createElement('dialog');
  dialog.instanceTarget = selected;
  dialog.className='instance-dialog';
  dialog.setAttribute('aria-labelledby','instance-task-title');
  const names={content:'内容摘要',create:'新建实例',import:'导入已有实例',configure:'实例运行配置',rename:'重命名实例',copy:'创建副本',icon:'更换图标',backup:'备份与恢复',restore:'恢复备份',export:'导出整合包',remove:'删除实例',pack:'整合包准备'};
  const bodies={
    content:`<p>设计示例：仅展示浏览器中的实例摘要，不读取游戏目录。</p><p>模组：${selected.mods} · 资源包：2 · 光影：1</p>`,
    configure:`<p>这些是当前实例的单独设置，优先于启动器默认值。</p><label>运行版本<input value="${escapeHtml(selected.version)} · ${escapeHtml(selected.loader)}" readonly></label><label>Java 可执行文件路径<input id="instance-config-java" readonly value="${escapeHtml(selected.javaPath||'')}" placeholder="自动选择 Java" autocomplete="off" spellcheck="false"></label><label>内存分配<select id="instance-config-memory-mode"><option value="manual" ${selected.memoryMode!=='auto'?'selected':''}>手动</option><option value="auto" ${selected.memoryMode==='auto'?'selected':''}>自动</option></select></label><label>最大内存（MB）<input id="instance-config-memory" type="number" min="1" required value="${selected.memory*1024}"></label><label>离线玩家名<input id="instance-config-player" value="${escapeHtml(selected.playerName||'')}" placeholder="留空沿用默认" autocomplete="off" spellcheck="false"></label><label>游戏桥<select id="instance-config-bridge"><option value="inherit" ${selected.gameBridgeEnabled==null?'selected':''}>沿用全局（${gameBridgeDesignGlobalEnabled()?'开启':'关闭'}）</option><option value="false" ${selected.gameBridgeEnabled===false?'selected':''}>关闭</option><option value="true" ${selected.gameBridgeEnabled===true?'selected':''}>开启</option></select></label><p>开启后可读取游戏信息、操作本地角色；服务器只读，更改在下次启动时生效。</p>`,
    create:`<label>实例名称<input id="instance-task-name" required></label>${window.gameVersionSelect('instance-task-version','游戏版本',window.gameVersions.find(item=>item.type==='release')?.id||'')}`,
    import:'<p>将已有实例的游戏内容复制到启动器数据目录，保留原目录、存档和配置不变。</p><label>来源目录<input id="instance-task-source" readonly placeholder="选择已有实例的游戏目录" required></label><label>实例名称<input id="instance-task-name" placeholder="为迁入的实例命名" required></label>',
    rename:`<label>实例名称<input id="instance-task-name" value="${escapeHtml(selected.name)}" required></label>`,
    copy:`<p>复制完整游戏内容、存档、配置与所选版本。共享依赖安全复用，原实例保持不变。</p><label>副本名称<input id="instance-task-name" value="${escapeHtml(selected.name)} · 副本" required></label>`,
    backup:'<p>备份游戏目录的全部内容，包含存档和配置；Java、资源和共享依赖仍保留在启动器数据目录。</p><div class="backup-design-entry"><div><strong>全部游戏内容</strong><small>存档、模组、资源包、光影、配置与选项</small></div><button class="small-button" data-instance-command="restore">查看恢复流程</button></div>',
    restore:'<p>恢复会覆盖备份中已有的文件，保留其他文件。执行前自动创建安全备份与可回退目录。</p><label>选择备份<select aria-label="选择备份"><option>2026-10-16  14:30 · 全部游戏内容（设计示例）</option></select></label>',
    export:'<p>导出为 Modrinth 整合包，包含本地模组与配置。存档、Java 与共享依赖不在包内；保存存档请使用备份。</p><label>文件名<input value="'+escapeHtml(selected.name)+'.mrpack" readonly></label>',
    pack:'<p>继续处理此实例的整合包缺件、游戏运行环境和 Java，不创建新实例。</p>',
    remove:'<p>删除此实例登记及其备份。游戏文件按所选范围处理，共享依赖与其他实例不受影响。</p><label class="remove-choice"><input type="radio" name="remove-scope" value="registration" checked><span><strong>仅移除登记</strong><small>保留游戏文件、存档和配置</small></span></label><label class="remove-choice"><input type="radio" name="remove-scope" value="files"><span><strong>同时删除此实例的游戏文件</strong><small>包括存档和配置，此操作无法撤销</small></span></label>',
  };
  const primary={create:'创建实例',import:'导入实例',rename:'保存名称',copy:'创建副本',icon:'应用图标',backup:'创建备份',restore:'确认恢复',export:'选择保存位置',remove:'确认移除',pack:'继续准备'};
  dialog.innerHTML=`<header><div><h2 id="instance-task-title">${names[kind]}</h2>${['rename','create','import'].includes(kind)?'':`<div class="instance-dialog-context">${escapeHtml(selected.name)}</div>`}</div></header><div class="instance-dialog-body">${bodies[kind]}</div>${kind==='configure'?'':`<footer><button class="small-button dialog-primary" data-instance-submit="${kind}" ${kind==='content'?'hidden':''}>${primary[kind]}</button><button class="small-button subtle" data-instance-close>${kind==='content'?'关闭':'取消'}</button></footer>`}`;
  dialog.querySelectorAll('#instance-config-java,#instance-task-source').forEach(input=>{const row=document.createElement('div'),browse=document.createElement('button');row.className='pack-path-input';browse.type='button';browse.className='small-button subtle pack-path-browse';browse.innerHTML=vectorIcon('folder');browse.setAttribute('aria-label',input.id==='instance-config-java'?'选择 Java 可执行文件':'选择来源目录');browse.onclick=()=>{input.value=input.id==='instance-config-java'?'C:/Demo/Java/bin/javaw.exe':'C:/Demo/Minecraft';input.dispatchEvent(new Event('input',{bubbles:true}));showToast('设计示例路径；正式应用打开系统选择器。');};input.before(row);row.append(input,browse);if(input.id==='instance-config-java'){const clear=document.createElement('button');clear.type='button';clear.className='small-button subtle pack-path-browse';clear.innerHTML=vectorIcon('close');clear.setAttribute('aria-label','清除所选路径');clear.onclick=()=>{input.value='';};row.append(clear);}});
  const memoryMode=dialog.querySelector('#instance-config-memory-mode');
  if(memoryMode){const syncMemory=()=>{const field=dialog.querySelector('#instance-config-memory');field.closest('label').hidden=memoryMode.value==='auto';field.required=memoryMode.value==='manual';};memoryMode.addEventListener('change',syncMemory);syncMemory();}
  dialog.dataset.kind=kind;
  dialog.dataset.icon=selected.icon;
  document.querySelector('.launcher').append(dialog);
  dialog.addEventListener('close',()=>{dialog.remove();restoreInstanceTrigger();});
  dialog.showModal();
  upgradeCombos(dialog);
  if(kind==='configure') {
    const target=dialog.instanceTarget;
    const memory=dialog.querySelector('#instance-config-memory'),player=dialog.querySelector('#instance-config-player');
    const save=()=>{
      target.memoryMode=memoryMode.value;
      if(memory.validity.valid)target.memory=Number(memory.value)/1024;
      target.javaPath=dialog.querySelector('#instance-config-java').value;
      const bridge=dialog.querySelector('#instance-config-bridge').value;
      if(bridge==='inherit')delete target.gameBridgeEnabled;else target.gameBridgeEnabled=bridge==='true';
    };
    const dismiss=()=>{
      save();
      if(memoryMode.value==='manual'&&!memory.reportValidity())return;
      target.playerName=player.value.trim();
      dialog.close();render();
    };
    dialog.addEventListener('input',save);
    dialog.addEventListener('change',event=>{save();if(event.target===player)target.playerName=player.value.trim();});
    dialog.addEventListener('cancel',event=>{event.preventDefault();dismiss();});
    dialog.addEventListener('click',event=>{
      if(event.target!==dialog)return;
      const r=dialog.getBoundingClientRect();
      if(event.clientX<r.left||event.clientX>r.right||event.clientY<r.top||event.clientY>r.bottom)dismiss();
    });
  }
  if (kind === 'create' || kind === 'import') {
    const refreshValidity = () => {
      dialog.querySelector('[data-instance-submit]').disabled = !dialog.querySelector('#instance-task-name').value.trim()
        || kind === 'import' && !dialog.querySelector('#instance-task-source').value.trim();
    };
    dialog.addEventListener('input', refreshValidity);
    refreshValidity();
  }
  (dialog.querySelector('#instance-task-name')||dialog.querySelector('[data-instance-close]')||dialog.querySelector('input,select')).focus();
}
function restoreInstanceTrigger() {
  const target=instanceDialogTrigger?.isConnected&&instanceDialogTrigger.offsetParent?instanceDialogTrigger:
    document.querySelector(`[data-manage-settings="${selected.id}"]`)||document.querySelector(`[data-manage-more="${selected.id}"]`)||document.querySelector('[data-panel="instance"]');
  target?.focus();
}
function launchManagedClient(instance, ordinary = false) {
  if (instance.serverId && ordinary) {
    showToast('设计演示：普通启动，不携带所属服务器地址。');
    return;
  }
  selected = instance;
  if (instance.serverId && !ordinary) {
    homeServerMode = true; homeServerId = instance.serverId;
    homeServerAttempts.set(homeServerId, 'preparing');
    navigate('home'); render(false);
  } else {
    homeServerMode = false; navigate('home'); beginLaunch();
  }
}
document.addEventListener('click', event => {
  const button = event.target.closest('[data-manage-launch]');
  const instance = button && instances.find(item => item.id === button.dataset.manageLaunch);
  if (!instance?.serverId) return;
  event.stopImmediatePropagation();
  launchManagedClient(instance);
}, true);
document.addEventListener('click',event=>{
  const button=event.target.closest('button');
  if(!event.target.closest('.instance-command-menu')&&!event.target.closest('[data-manage-more]'))closeInstanceMenu();
  if(!button)return;
  if(button.hasAttribute('data-instance-submenu')){openInstanceSubmenu(button,true);return;}
  if(button.dataset.instanceEntry){openInstanceEntry(button.dataset.instanceEntry);return;}
  if(button.dataset.instanceSelect){selected=instances.find(i=>i.id===button.dataset.instanceSelect);render();document.querySelector(`[data-instance-select="${selected.id}"]`)?.focus();}
  if(button.hasAttribute('data-instance-close'))button.closest('dialog').close();
  if(button.dataset.instanceCommand){
    const kind=button.dataset.instanceCommand;
    const target=instanceMenuTarget||selected;
    if(kind==='launch'){closeInstanceMenu();launchManagedClient(target);}
    else if(kind==='multiplayer'){closeInstanceMenu();window.Multiplayer.create(target.id);}
    else if(kind==='local-launch'){closeInstanceMenu();launchManagedClient(target, true);}
    else if(kind==='release-client'){delete target.serverId;closeInstanceMenu();render();restoreInstanceTrigger();}
    else if(kind==='pack'){openInstanceTask('pack');}
    else if(kind==='pack-update'){closeInstanceMenu();openPackMaintenanceReview();}
    else if(kind==='runtime'){openRuntimeReview();}
    else if(kind==='content'){openInstanceTask('content');}
    else if(kind==='settings'){instanceMenuTrigger=button;closeDrawer();openInstanceTask('configure');}
    else if(kind==='folder'||kind==='runtime-folder'){closeInstanceMenu(true);showToast('设计演示：原生实现将打开该实例的'+(kind==='folder'?'游戏目录。':'运行文件目录。'));}
    else openInstanceTask(kind,target);
  }
  if(button.dataset.instanceSubmit){
    const dialog=button.closest('dialog'),kind=button.dataset.instanceSubmit;
    if(kind==='create'||kind==='import'){
      const name=dialog.querySelector('#instance-task-name').value.trim();
      const source=dialog.querySelector('#instance-task-source');
      if(!name||kind==='import'&&!source.value.trim())return;
      const version=kind==='create'?dialog.querySelector('#instance-task-version').value:'未识别';
      const record={id:`mock-${Date.now()}`,name,version,loader:kind==='create'?'原版':'未识别',mods:0,icon:'grass',java:version==='1.20.1'?17:21,memory:2,copy:'继续你的世界。',serverId:serverClientContext?.id};
      if(contentNoInstances){instances.length=0;contentCollections.clear();contentNoInstances=false;}
      instances.push(record);
      if (serverClientContext) {
        serverClientContext.instanceIds.push(record.id);
        serverClientContext.preferred ??= record.id;
      }
      selected=record;
      showToast('设计演示：已加入示例实例列表，未下载游戏、扫描目录或迁入文件。');
    }else if(kind==='rename'||kind==='copy'){
      const input=dialog.querySelector('input');if(!input.value.trim()){input.setAttribute('aria-invalid','true');input.focus();return;}
      const target=dialog.instanceTarget||selected;
      if(kind==='rename')target.name=input.value.trim();
      else instances.push({...target,id:`mock-${Date.now()}`,name:input.value.trim()});
      showToast(kind==='rename'?'示例实例已重命名。':'示例副本已加入列表。');
    }
    else {showToast('设计演示：此原型不执行文件操作。');dialog.close();return;}
    dialog.close();render();restoreInstanceTrigger();
  }
});
document.addEventListener('contextmenu',event=>{
  const row=event.target.closest('#instance-list [data-instance]');
  const target=row&&instances.find(item=>item.id===row.dataset.instance);
  if(currentPage!=='home'||!target)return;
  event.preventDefault();
  openInstanceMenu(row,target,true,{x:event.clientX,y:event.clientY});
});
document.addEventListener('keydown',event=>{
  const row=event.target.closest('#instance-list [data-instance]');
  if(currentPage==='home'&&row&&(event.key==='ContextMenu'||event.key==='F10'&&event.shiftKey)){
    const target=instances.find(item=>item.id===row.dataset.instance);
    if(target){event.preventDefault();openInstanceMenu(row,target,true);return;}
  }
  const menu=document.querySelector('.instance-command-menu');
  const dialog=document.querySelector('.instance-dialog[open]');
  if(dialog&&event.key==='Enter'&&['instance-task-name','instance-task-source'].includes(event.target.id)){
    event.preventDefault();dialog.querySelector('[data-instance-submit]').click();
  }
  if(!menu)return;
  const activeMenu = document.activeElement?.closest('.instance-command-menu') || menu;
  const toward = getComputedStyle(activeMenu).direction === 'rtl' ? 'ArrowLeft' : 'ArrowRight';
  const away = toward === 'ArrowRight' ? 'ArrowLeft' : 'ArrowRight';
  if(event.key==='Escape'){event.preventDefault();if(activeMenu.classList.contains('instance-submenu'))closeInstanceSubmenu(true);else closeInstanceMenu(true);}
  if(event.key===toward && document.activeElement?.hasAttribute('data-instance-submenu')){event.preventDefault();openInstanceSubmenu(document.activeElement,true);}
  if(event.key===away && activeMenu.classList.contains('instance-submenu')){event.preventDefault();closeInstanceSubmenu(true);}
  if(['ArrowDown','ArrowUp','Home','End'].includes(event.key)){
    event.preventDefault();const items=[...activeMenu.querySelectorAll('button')].filter(item=>item.offsetParent),index=items.indexOf(document.activeElement);
    const next=event.key==='Home'?0:event.key==='End'?items.length-1:(index+(event.key==='ArrowDown'?1:-1)+items.length)%items.length;
    items[next].focus();
  }
  if(event.key==='Tab')closeInstanceMenu(true);
});


function openImportEntryMenu(trigger) {
  closeInstanceMenu();instanceMenuTrigger=trigger;trigger.setAttribute('aria-expanded','true');
  const menu=document.createElement('div');menu.className='instance-command-menu';menu.setAttribute('role','menu');
  menu.innerHTML='<button role="menuitem" data-import-directory>导入游戏目录</button><button role="menuitem" data-import-pack>导入整合包</button>';
  document.querySelector('.launcher').append(menu);
  const base=menu.parentElement.getBoundingClientRect(),r=trigger.getBoundingClientRect();
  menu.style.left=`${Math.max(16,Math.min(r.right-base.left-menu.offsetWidth,base.width-menu.offsetWidth-16))}px`;
  menu.style.top=`${Math.max(40,Math.min(r.bottom-base.top+6,base.height-menu.offsetHeight-16))}px`;
  menu.querySelector('button').focus();
}
function openPackImportReview(fileName = '') {
  const trigger=instanceMenuTrigger;closeInstanceMenu();
  const dialog=document.createElement('dialog');dialog.className='instance-dialog pack-preparation-dialog';dialog.setAttribute('aria-labelledby','pack-review-title');
  const titles=['整合包文件','游戏与加载器','Java 运行环境'];
  dialog.innerHTML=`<header><h2 id="pack-review-title">整合包准备</h2><button class="small-button subtle pack-preparation-close" data-pack-x aria-label="关闭" title="关闭">×</button></header><div class="pack-preparation-workspace"><section class="pack-preparation-steps"><p class="settings-note" data-pack-count>0 / 3 步骤已处理</p>${titles.map((title,index)=>`<section class="recovery-summary" data-pack-step="${index}"><span class="recovery-icon">${vectorIcon('info')}</span><div><h3>${title}</h3><p data-pack-step-detail>等待处理</p><progress hidden aria-label="${title}"></progress></div></section>`).join('')}</section><div class="pack-preparation-fields"><label>整合包文件<input data-pack-review-source placeholder="选择整合包文件"></label><section data-pack-review-fields hidden><p class="settings-note">test-1 · Minecraft 26.3 · mrpack</p><label>新实例名称<input data-pack-review-name value="示例整合包"></label><label><input type="checkbox" data-pack-review-download checked> 联网下载可取得的文件</label><label><input type="checkbox" data-pack-review-runtime checked> 同时安装已识别的游戏运行环境</label><label>本地补件目录<input data-pack-manual title="MRPACK 按相对路径放置；CF 按 project:file 映射。"></label><h3>可选客户端文件</h3><label><input type="checkbox"> config/example.txt</label></section><section data-pack-missing hidden><h3>待补齐 2 个文件</h3><div class="pack-missing-card"><span>example-api.jar</span><span class="pack-category-tag">mods</span></div><div class="pack-missing-card"><span>example-content.jar</span><span class="pack-category-tag">mods</span></div></section><section data-pack-review-result role="status" hidden></section><p class="settings-note">设计示例：不读取文件、下载内容或创建真实实例。</p></div></div><footer><button class="small-button" data-pack-review-confirm disabled>读取包信息</button><button class="small-button subtle" data-pack-review-close>取消</button></footer>`;
  const stepList=dialog.querySelector('.pack-preparation-steps');
  stepList.append(stepList.querySelector('[data-pack-count]'));
  dialog.querySelectorAll('[data-pack-review-source],[data-pack-manual]').forEach(input=>{
    const row=document.createElement('div'),browse=document.createElement('button');
    row.className='pack-path-input';browse.type='button';browse.className='small-button subtle pack-path-browse';
    browse.innerHTML=vectorIcon('folder');browse.title=input.hasAttribute('data-pack-manual')?'选择本地补件目录':'选择整合包文件';
    browse.setAttribute('aria-label',browse.title);
    browse.onclick=()=>showToast('设计演示：原生入口将打开文件或目录选择器。');
    input.readOnly=true;input.placeholder=input.hasAttribute('data-pack-manual')?'选择本地补件目录':'选择整合包文件';input.before(row);row.append(input,browse);
  });
  document.body.append(dialog);dialog.showModal();
  const source=dialog.querySelector('[data-pack-review-source]'),confirm=dialog.querySelector('[data-pack-review-confirm]'),fields=dialog.querySelector('[data-pack-review-fields]');
  const close=dialog.querySelector('[data-pack-review-close]'),download=dialog.querySelector('[data-pack-review-download]'),runtime=dialog.querySelector('[data-pack-review-runtime]');
  const statuses=['pending','pending','pending'],timers=[];let busy=false,active=0,runtimePreference=true;
  function step(index,status,detail) {
    statuses[index]=status;
    const row=dialog.querySelector(`[data-pack-step="${index}"]`);
    row.classList.toggle('success',status==='completed');row.classList.toggle('critical',status==='blocked');
    row.querySelector('.recovery-icon').innerHTML=vectorIcon(status==='completed'?'success':status==='blocked'?'error':'info');
    row.querySelector('[data-pack-step-detail]').textContent=detail;
    row.querySelector('progress').hidden=status!=='running';
    dialog.querySelector('[data-pack-count]').textContent=`${statuses.filter(s=>s==='completed'||s==='skipped').length} / 3 步骤已处理`;
  }
  function finish(contentOnly) {
    busy=false;dialog.close();
    showToast(contentOnly?'设计演示：所选内容步骤已处理；未读写文件或启动游戏。':'设计演示：准备已完成；未下载或启动游戏。');
  }
  function run(index) {
    active=index;step(index,'running','正在处理所选步骤');
    timers.push(setTimeout(()=>{
      if(index===0&&!download.checked&&!dialog.querySelector('[data-pack-manual]').value.trim()) {
        step(0,'blocked','请提供本地文件后继续。');busy=false;dialog.querySelector('[data-pack-missing]').hidden=false;
        dialog.querySelector('[data-pack-manual]').disabled=false;confirm.hidden=false;confirm.textContent='继续准备';close.textContent='取消';return;
      }
      step(index,'completed',index===0?'包内文件已处理':index===1?'Minecraft 26.3 · Fabric':'Java 25');
      if(index===0)dialog.querySelector('[data-pack-missing] h3').textContent='本次补件';
      if(index===0&&(!download.checked||!runtime.checked)){step(1,'skipped','本次未选择');step(2,'skipped','本次未选择');finish(true);}
      else if(index<2)run(index+1);else finish(false);
    },1000));
  }
  source.addEventListener('input',()=>{fields.hidden=true;confirm.textContent='读取包信息';confirm.disabled=!source.value.trim();});
  runtime.addEventListener('change',()=>runtimePreference=runtime.checked);
  download.addEventListener('change',()=>{runtime.disabled=!download.checked;runtime.checked=download.checked&&runtimePreference;});
  confirm.addEventListener('click',()=>{
    if(busy)return;
    if(fields.hidden){fields.hidden=false;confirm.textContent='开始准备';return;}
    busy=true;confirm.hidden=true;close.textContent='取消';
    dialog.querySelectorAll('.pack-preparation-fields input,.pack-path-browse').forEach(input=>input.disabled=true);
    run(0);
  });
  close.addEventListener('click',()=>{
    if(busy){timers.forEach(clearTimeout);busy=false;step(active,'canceled','已取消，完成步骤保留。');confirm.hidden=false;confirm.textContent='继续准备';close.textContent='取消';}
    else dialog.close();
  });
  function closePopup(){timers.forEach(clearTimeout);busy=false;dialog.close();}
  dialog.querySelector('[data-pack-x]').addEventListener('click',closePopup);
  dialog.addEventListener('cancel',event=>{event.preventDefault();closePopup();});
  dialog.addEventListener('close',()=>{timers.forEach(clearTimeout);dialog.remove();trigger?.focus();});
  if(fileName){source.value=fileName;fields.hidden=false;confirm.disabled=false;confirm.textContent='开始准备';}
  source.focus();
}
function packReviewNotice(title,detail) {
  return `<section class="recovery-summary success"><span class="recovery-icon">${vectorIcon('success')}</span><div><h3>${escapeHtml(title)}</h3>${detail?`<p>${escapeHtml(detail)}</p>`:''}</div></section>`;
}
document.addEventListener('click',event=>{
  if(event.target.closest('[data-import-directory]'))openInstanceTask('import');
  if(event.target.closest('[data-import-pack]'))openPackImportReview();
});

// Only the file name enters this design simulation; archive inspection belongs to Core.
function clearPackDropHints() {
  document.querySelectorAll('[data-pack-drop-hint]').forEach(hint=>hint.remove());
}
function packDropTarget(event) {
  if(document.querySelector('dialog[open]'))return null;
  if(currentPage==='home'&&document.querySelector('.launcher').dataset.homeTarget!=='server')return event.target.closest('.instances');
  if(currentPage==='instances'&&instancePageSection==='local'&&!serverClientContext&&!(typeof runtimeReview!=='undefined'&&runtimeReview))return event.target.closest('.instance-list-scroll');
  return null;
}
document.addEventListener('dragover',event=>{
  const target=packDropTarget(event);
  if(!target||event.dataTransfer.items.length!==1||event.dataTransfer.items[0].kind!=='file')return;
  event.preventDefault();event.dataTransfer.dropEffect='copy';
  if(!document.querySelector('[data-pack-drop-hint]')){
    const hint=document.createElement('p');hint.className='settings-note pack-drop-hint';hint.dataset.packDropHint='';
    hint.textContent='松开以填写新实例信息';
    if(currentPage==='home')target.querySelector('.add-instance').before(hint);else target.after(hint);
  }
});
document.addEventListener('dragleave',event=>{
  const target=packDropTarget(event);
  if(target&&(!event.relatedTarget||!target.contains(event.relatedTarget)))clearPackDropHints();
});
document.addEventListener('drop',event=>{
  const target=packDropTarget(event);if(!target)return;
  event.preventDefault();clearPackDropHints();
  const files=event.dataTransfer.files;
  if(files.length!==1||event.dataTransfer.items[0]?.webkitGetAsEntry?.()?.isDirectory||! /\.(mrpack|zip)$/i.test(files[0].name)){showToast('请选择一个 MRPACK 或 ZIP 整合包文件。');return;}
  openPackImportReview(files[0].name);
});
window.addEventListener('prototype-page-changed',clearPackDropHints);
