/* Browser-only workflow samples. Never reads archives, contacts services or changes player files. */
let runtimeReview = null;
const runtimeSamples = new Map();
const sampleNotice = '<p class="workflow-simulation">交互示例 · 仅修改浏览器内存，不调用 Core、不联网或修改游戏文件。</p>';
const workflowButton = (text,attrs='',secondary=false) => `<button class="small-button ${secondary?'subtle':''}" ${attrs}>${text}</button>`;
function runtimeSample(instance) {
  if(!runtimeSamples.has(instance.id))runtimeSamples.set(instance.id,{current:instance.loader==='原版'?`原版 · ${instance.version}`:`${instance.loader.toLowerCase()} 0.16.14 · ${instance.version}`,history:[]});
  return runtimeSamples.get(instance.id);
}
function openRuntimeReview() {
  closeInstanceMenu();closeDrawer();
  runtimeReview={instance:selected,mode:'online',minecraft:selected.version,loader:selected.loader==='原版'?'vanilla':selected.loader.toLowerCase(),version:'0.16.14',profile:'原版 · 1.21.1',plan:null,active:false,status:'',error:'',timer:null};
  navigate('instances');
}
function runtimeReviewMarkup() {
  const s=runtimeReview,model=runtimeSample(s.instance),e=escapeHtml;
  const field=(title,body)=>`<label class="runtime-review-field"><span>${title}</span>${body}</label>`;
  const select=(id,values,value)=>`<select id="${id}" aria-label="${id==='review-runtime-mode'?'变更方式':id==='review-runtime-loader'?'加载器':'已安装组合'}" ${s.active?'disabled':''}>${values.map(([v,t])=>`<option value="${v}" ${v===value?'selected':''}>${t}</option>`).join('')}</select>`;
  return `<header class="page-heading runtime-page-heading">${workflowButton('返回实例',`data-runtime-back ${s.active?'disabled':''}`,true)}<h1>运行环境</h1><span>${e(s.instance.name)}</span></header><section class="page-surface acrylic runtime-review-page"><div class="runtime-review-scroll">${sampleNotice}<h2>当前运行组合</h2><p data-runtime-current>${e(model.current)}</p><p class="settings-note">建议 Java 版本：${s.instance.java}（示例）</p><div class="runtime-review-grid">
    ${field('变更方式',select('review-runtime-mode',[['online','安装或更换组件'],['installed','选择已安装组合']],s.mode))}
    ${s.mode==='installed'?field('已安装组合',select('review-runtime-profile',[['原版 · 1.21.1','原版 · 1.21.1'],['Fabric · 1.21.1','Fabric · 1.21.1']],s.profile)):
      field('Minecraft 版本',`<input id="review-runtime-minecraft" list="runtime-minecraft-samples" aria-label="Minecraft 版本" value="${e(s.minecraft)}" ${s.active?'disabled':''}>`)+field('加载器',select('review-runtime-loader',[['vanilla','原版'],['fabric','Fabric'],['quilt','Quilt'],['forge','Forge'],['neoforge','NeoForge']],s.loader))+(s.loader==='vanilla'?'':field('加载器版本',`<input id="review-runtime-version" list="runtime-loader-samples" aria-label="加载器版本" value="${e(s.version)}" ${s.active?'disabled':''}>`))}
    </div>${s.catalog?'<datalist id="runtime-minecraft-samples"><option value="1.21.1"><option value="1.20.1"></datalist><datalist id="runtime-loader-samples"><option value="'+(s.loader==='fabric'?'0.16.14':s.loader==='quilt'?'0.27.1':s.loader==='forge'?'52.0.0':'21.1.1')+'"></datalist>':''}${s.mode==='online'?workflowButton('读取可用版本',`data-runtime-catalog ${s.active?'disabled':''}`,true):''}<p class="settings-note" data-runtime-catalog-state>${s.catalog?'版本来源：浏览器内置示例目录，未联网；可手工填写精确版本。':''}</p><p class="settings-note">仅变更运行组合，保留模组、存档及实例其它设置。</p>
    ${model.history.length?`<h2>变更与回退记录</h2>${model.history.map((h,index)=>`<div class="runtime-history"><span>${e(h.target)} · 设计示例${h.rolled?' · 已回退':''}</span>${workflowButton(h.rolled?'已回退':'回退到此前组合',`data-runtime-rollback="${index}" ${h.rolled||s.active||model.current!==h.target?'disabled':''}`,true)}</div>`).join('')}`:''}</div>
    <footer class="runtime-review-footer">${s.plan?`<div class="workflow-review-summary" data-runtime-plan><strong>审核运行组合</strong><p dir="ltr">${e(s.plan.previous)} → ${e(s.plan.target)}</p></div>`:''}<p role="status" data-runtime-status>${e(s.status)}</p>${s.error?`<p role="alert">${e(s.error)}</p>`:''}<div class="workflow-actions">${workflowButton(s.plan?'应用运行组合':'审核运行组合',`data-runtime-confirm ${s.active||runtimeTarget(s)===model.current||!(s.mode==='installed'?s.profile:s.minecraft.trim()&&(s.loader==='vanilla'||s.version.trim()))?'disabled':''}`)}${s.active?workflowButton('取消任务','data-runtime-cancel',true):''}</div></footer></section>`;
}
function renderRuntimeReview() { renderPage(); }
function runtimeTarget(s) { return s.mode==='installed'?s.profile:`${s.loader==='vanilla'?'原版':s.loader+' '+s.version} · ${s.minecraft}`; }
function applyRuntimeReview() {
  const s=runtimeReview;if(!s||s.active)return;
  if(!s.plan){s.plan={previous:runtimeSample(s.instance).current,target:runtimeTarget(s)};s.status='审核示例已冻结；确认应用前不会变更运行组合。';renderRuntimeReview();return;}
  const plan={...s.plan};s.active=true;s.status='正在准备运行环境（模拟任务）';renderRuntimeReview();
  s.timer=setTimeout(()=>{if(runtimeReview!==s||!s.active)return;const model=runtimeSample(s.instance);model.history.unshift({...plan,rolled:false});model.current=plan.target;s.plan=null;s.active=false;s.status='模拟完成：运行组合已更新；未启动游戏或写入真实实例。';renderRuntimeReview();},1100);
}
function workflowMenu(trigger,entries,label) {
  closeWorkflowMenu();const menu=document.createElement('div');menu.className='workflow-menu instance-command-menu';menu.setAttribute('role','menu');menu.setAttribute('aria-label',label);menu._trigger=trigger;
  entries.forEach(entry=>{const button=document.createElement('button');button.type='button';button.setAttribute('role','menuitem');button.textContent=entry.text;button.disabled=!!entry.disabled;button.onclick=()=>{closeWorkflowMenu();entry.run();};menu.append(button);});
  document.querySelector('.launcher').append(menu);const b=menu.parentElement.getBoundingClientRect(),r=trigger.getBoundingClientRect();menu.style.left=`${Math.max(12,Math.min(r.right-b.left-menu.offsetWidth,b.width-menu.offsetWidth-12))}px`;menu.style.top=`${Math.max(12,Math.min(r.bottom-b.top+6,b.height-menu.offsetHeight-12))}px`;trigger.setAttribute('aria-expanded','true');menu.querySelector('button:not(:disabled)')?.focus();
}
function closeWorkflowMenu(restore=false) {const menu=document.querySelector('.workflow-menu');if(!menu)return;const trigger=menu._trigger;menu.remove();trigger?.setAttribute('aria-expanded','false');if(restore&&trigger?.isConnected)trigger.focus();}
document.addEventListener('pointerdown',event=>{const menu=document.querySelector('.workflow-menu');if(menu&&!menu.contains(event.target)&&!menu._trigger?.contains(event.target))closeWorkflowMenu();},true);
const updateTargets={'sodium':'0.6.14','mod-menu':'11.0.4','fabric-api':'0.116.2+1.21.1','iris':'1.8.9','continuity':'3.0.1','block-detail':'1.5','soft-light':'2.1','forest-light':'1.3'};
function contentPolicyMenu(trigger,item) {
  const tracked=item.icon!=='fallback';
  const refresh=()=>{contentUpdateList();document.querySelector(`[data-content-more="${item.id}"]`)?.focus();};
  if(contentTab==='packs'&&item.selected){
    const selectedPacks=contentEntries().filter(entry=>entry.selected).sort((a,b)=>(a.priority||1)-(b.priority||1));
    const index=selectedPacks.indexOf(item);
    const move=offset=>{const other=selectedPacks[index+offset];if(other){const priority=item.priority||1;item.priority=other.priority||1;other.priority=priority;refresh();}};
    workflowMenu(trigger,[{text:'提高优先级',disabled:index===0,run:()=>move(-1)},{text:'降低优先级',disabled:index===selectedPacks.length-1,run:()=>move(1)},{text:'移除',run:()=>contentRemoveDialog(item.id,trigger)}],`管理 ${item.name}`);return;
  }
  workflowMenu(trigger,[{text:'检查此项更新',run:()=>openContentUpdates(trigger,[item])},...(tracked?[{text:item.pinned?'允许更新':'固定当前版本',run:()=>{item.pinned=!item.pinned;refresh();}},{text:'更新通道',run:()=>workflowMenu(trigger,['release','beta','alpha'].map((channel,index)=>({text:['正式版','Beta','Alpha'][index]+((item.channel||'release')===channel?'（当前）':''),disabled:(item.channel||'release')===channel,run:()=>{item.channel=channel;refresh();}})),'更新通道')}]:[]),{text:'移除',run:()=>contentRemoveDialog(item.id,trigger)}],`管理 ${item.name}`);
}
function openContentUpdates(trigger,only=null) {
  if(document.querySelector('dialog[open]'))return;
  closeCombo();const instance=selected,kind=contentTab,items=only||[...contentEntries()],scroll=el('content-rows')?.scrollTop||0;
  const dialog=document.createElement('dialog');dialog.className='instance-dialog updates-review-dialog';dialog.setAttribute('aria-labelledby','updates-review-title');
  const s={phase:'selection',items,selected:new Set(),frozen:null,scenario:'normal',status:'',needsCheck:false,timer:null};
  const sources=new Map();
  const localCandidate=item=>item.icon==='fallback';
  const sourceChoice=item=>localCandidate(item)?`<select data-update-source="${item.id}" aria-label="选择 ${escapeHtml(item.name)} 的在线来源"><option value="">选择在线来源</option><option value="matching" data-content-icon="${escapeHtml(item.icon)}" data-description="matching-project" ${sources.get(item.id)==='matching'?'selected':''}>${escapeHtml(item.name)}</option><option value="different" data-content-icon="fallback" data-description="similar-title" ${sources.get(item.id)==='different'?'selected':''}>${escapeHtml(item.name)}</option></select>`:'';
  const reason=item=>item.pinned?'已固定当前版本':localCandidate(item)&&!sources.has(item.id)?'选择在线来源':sources.get(item.id)==='different'?'候选 JAR 的模组 ID 不一致，请重新选择来源':item.checkFailed?'检查失败，可重试':item.version===updateTargets[item.id]?'已是最新版本':!updateTargets[item.id]&&!sources.has(item.id)?'没有适用的更新版本':'';
  const available=item=>!reason(item);
  const check=()=>{s.phase='selection';s.frozen=null;s.needsCheck=false;s.status='';s.selected=new Set(items.filter(available).map(item=>item.id));};check();
  function summary(item){return `<div class="update-version" dir="ltr">${escapeHtml(item.version)} → ${escapeHtml(updateTargets[item.id]||(sources.has(item.id)?'新版（示例）':item.version))}</div>`;}
  function draw(){
    const previousFocus=document.activeElement;let restore='';
    if(dialog.contains(previousFocus))for(const attr of ['data-update-select','data-update-all','data-update-confirm','data-update-close','data-update-back'])if(previousFocus.hasAttribute(attr)){restore=`[${attr}="${previousFocus.getAttribute(attr)}"]`;break;}
    const selecting=s.phase==='selection',busy=s.phase==='active',done=s.phase==='done',readied=s.phase==='review',hasAvailable=items.some(available);
    const rows=selecting?items.map(item=>`<article class="update-candidate"><div class="update-select">${available(item)?`<input type="checkbox" data-update-select="${item.id}" aria-label="更新 ${escapeHtml(item.name)}" ${s.selected.has(item.id)?'checked':''}>`:''}</div>${contentIcon(item.icon)}<div class="content-name"><strong>${escapeHtml(item.name)}</strong>${available(item)?summary(item):`<div class="update-version">${escapeHtml(item.version)}</div>${reason(item)==='选择在线来源'?'':`<p>${escapeHtml(reason(item))}</p>`}`}${!item.enabled&&available(item)?'<small>更新后仍保持禁用</small>':''}${sourceChoice(item)}${item.channel&&item.channel!=='release'?`<small>${escapeHtml(item.channel)} 通道</small>`:''}</div></article>`).join(''):
      (s.frozen||[]).map(item=>`<article class="update-plan-row">${contentIcon(item.icon)}<div><strong>${escapeHtml(item.name)}</strong><div dir="ltr">${escapeHtml(item.current)} → ${escapeHtml(item.target)}</div><small>替换已有文件${!item.enabled?' · 保持禁用':''}</small></div></article>`).join('')+(s.frozen?.some(item=>item.id==='iris')?'<article class="update-plan-row"><div class="update-dependency"><strong>Fabric API（必要依赖）</strong><div>复用已安装版本 · 由 Iris 要求</div><small>依赖只读，不提供可取消勾选</small></div></article>':'');
    dialog.innerHTML=`<header><div><h2 id="updates-review-title">更新已安装内容</h2><small>${escapeHtml(instance.name)} · ${contentKinds[kind]}</small></div>${s.status&&!busy&&!done||readied&&s.scenario==='conflict'?workflowButton('操作详情','data-update-details',true):''}<button class="small-button subtle updates-close" data-update-close aria-label="关闭" title="关闭">${vectorIcon('close')}</button></header><div class="instance-dialog-body">${sampleNotice}${selecting&&hasAvailable?`<label class="updates-all"><input type="checkbox" data-update-all ${s.selected.size===items.filter(available).length?'checked':''}> 全选 · 已选择 ${s.selected.size} 项</label>`:''}${rows}${readied&&s.scenario==='conflict'?'<p class="workflow-error" role="alert">依赖冲突（示例）：保留的 Iris 要求原 Sodium 版本。请返回并调整选择。</p>':''}${s.status?`<p role="status" class="${s.scenario==='failed'?'workflow-error':''}">${escapeHtml(s.status)}</p>`:''}${selecting&&items.some(i=>i.checkFailed)?workflowButton('重试失败项目','data-update-retry',true):''}${busy?'<progress aria-label="模拟更新任务"></progress>':''}<details class="workflow-fixtures"><summary>评审场景（模拟数据）</summary><select data-update-scenario aria-label="模拟更新场景" ${busy?'disabled':''}>${[['normal','正常'],['conflict','依赖冲突'],['baseline','确认时基线改变'],['failed','任务失败后重新审核'],['read-failure','一个项目读取失败']].map(([v,t])=>`<option value="${v}" ${s.scenario===v?'selected':''}>${t}</option>`).join('')}</select></details></div><footer ${!readied&&(done||!hasAvailable&&!s.needsCheck)?'hidden':''}>${readied?workflowButton('返回项目选择','data-update-back',true):''}${!done&&(hasAvailable||s.needsCheck)?workflowButton(busy?'更新中':s.needsCheck?'重新检查':readied?'确认更新':'审核所选项目',`data-update-confirm ${busy||!s.needsCheck&&(!s.selected.size||readied&&s.scenario==='conflict')?'disabled':''}`):''}</footer>`;
    dialog.querySelectorAll('[data-update-source]').forEach(choice=>choice.onchange=()=>{const id=choice.dataset.updateSource;if(choice.value)sources.set(id,choice.value);else sources.delete(id);check();draw();});
    dialog.querySelector('[data-update-confirm]')?.addEventListener('click',confirm);
    dialog.querySelector('[data-update-retry]')?.addEventListener('click',()=>{items.filter(i=>i.checkFailed).forEach(item=>{item.checkFailed=false;if(available(item))s.selected.add(item.id);});draw();});
    dialog.querySelector('[data-update-details]')?.addEventListener('click',()=>showReviewReport(dialog,s.status||'依赖冲突（模拟）：保留的 Iris 要求旧版 Sodium，请返回修改选择。',JSON.stringify({simulation:true,instance:instance.name,category:kind,scenario:s.scenario,frozen:s.frozen},null,2)));
    dialog.querySelector('[data-update-close]').onclick=()=>{if(busy)clearTimeout(s.timer);dialog.close();};
    dialog.querySelector('[data-update-back]')?.addEventListener('click',()=>{s.phase='selection';s.frozen=null;draw();});
    dialog.querySelector('[data-update-all]')?.addEventListener('change',event=>{s.selected=event.target.checked?new Set(items.filter(available).map(i=>i.id)):new Set();draw();});
    dialog.querySelectorAll('[data-update-select]').forEach(box=>box.onchange=()=>{if(box.checked)s.selected.add(box.dataset.updateSelect);else s.selected.delete(box.dataset.updateSelect);draw();});
    dialog.querySelector('[data-update-scenario]').onchange=event=>{s.scenario=event.target.value;items.forEach(item=>item.checkFailed=false);if(s.scenario==='read-failure'){const item=items.find(i=>i.icon!=='fallback'&&!i.pinned);if(item)item.checkFailed=true;}check();draw();};
    const target=restore?dialog.querySelector(restore):null;if(target&&!target.disabled)target.focus({preventScroll:true});else if(restore)dialog.querySelector('[data-update-close]').focus({preventScroll:true});
  }
  function confirm(){
    if(s.needsCheck){items.forEach(item=>item.checkFailed=false);check();draw();return;}
    if(s.phase==='selection'){s.frozen=items.filter(item=>s.selected.has(item.id)).map(item=>({id:item.id,name:item.name,icon:item.icon,current:item.version,target:updateTargets[item.id]||'新版（示例）',enabled:item.enabled}));s.phase='review';s.status='';draw();return;}
    if(s.phase!=='review')return;s.phase='active';s.status='模拟任务正在应用冻结清单';draw();
    s.timer=setTimeout(()=>{if(!dialog.isConnected||s.phase!=='active')return;if(s.scenario==='baseline'){s.phase='selection';s.frozen=null;s.needsCheck=true;s.status='实例策略基线已改变（示例），旧审核已失效。请重新检查。';}else if(s.scenario==='failed'){s.phase='selection';s.frozen=null;s.status='模拟任务失败，已保留原文件与恢复记录；旧审核已作废，可以重新审核选择。';}else{s.frozen.forEach(f=>{const item=items.find(i=>i.id===f.id);item.version=f.target;});s.phase='done';s.status='模拟更新完成，原禁用状态已保留。未下载或写入磁盘。';}draw();},1100);
  }
  dialog.addEventListener('cancel',event=>{if(s.phase==='active'){event.preventDefault();dialog.querySelector('[data-update-close]').click();}});
  dialog.addEventListener('click',event=>{if(event.target.closest('[data-review-task-close]'))dialog.close();});
  dialog.addEventListener('close',()=>{clearTimeout(s.timer);items.forEach(item=>item.checkFailed=false);dialog.remove();if(currentPage==='content'&&selected===instance&&contentTab===kind){contentUpdateList();if(el('content-rows'))el('content-rows').scrollTop=scroll;}(trigger?.isConnected?trigger:document.querySelector('[data-content-tools]'))?.focus();});
  document.querySelector('.launcher').append(dialog);draw();dialog.showModal();dialog.querySelector('[data-update-close]').focus();
}
document.addEventListener('input',event=>{
  if(!runtimeReview||!['review-runtime-minecraft','review-runtime-version'].includes(event.target.id))return;
  runtimeReview[event.target.id==='review-runtime-minecraft'?'minecraft':'version']=event.target.value;runtimeReview.plan=null;runtimeReview.status='';
  document.querySelector('[data-runtime-plan]')?.remove();const button=document.querySelector('[data-runtime-confirm]');button.textContent='审核运行组合';button.disabled=!runtimeReview.minecraft.trim()||runtimeReview.loader!=='vanilla'&&!runtimeReview.version.trim()||runtimeTarget(runtimeReview)===runtimeSample(runtimeReview.instance).current;
});
document.addEventListener('change',event=>{
  if(!runtimeReview||!event.target.id.startsWith('review-runtime-'))return;
  const key={'review-runtime-mode':'mode','review-runtime-profile':'profile','review-runtime-minecraft':'minecraft','review-runtime-loader':'loader','review-runtime-version':'version'}[event.target.id];
  if(key&&!['minecraft','version'].includes(key)){runtimeReview[key]=event.target.value;runtimeReview.plan=null;runtimeReview.status='';if(key==='loader'){runtimeReview.version='';runtimeReview.catalog=false;}renderRuntimeReview();}
});
document.addEventListener('click',event=>{
  const b=event.target.closest('button');if(!b)return;
  if(b.hasAttribute('data-runtime-back')&&!runtimeReview?.active){const id=runtimeReview.instance.id;runtimeReview=null;navigate('instances');document.querySelector(`[data-manage-more="${id}"]`)?.focus();}
  if(b.hasAttribute('data-runtime-catalog')){runtimeReview.catalog=true;runtimeReview.plan=null;renderRuntimeReview();}
  if(b.hasAttribute('data-runtime-confirm'))applyRuntimeReview();
  if(b.hasAttribute('data-runtime-cancel')){clearTimeout(runtimeReview.timer);runtimeReview.active=false;runtimeReview.plan=null;runtimeReview.status='模拟任务已取消；当前运行组合未改变，可重新审核。';renderRuntimeReview();}
  if(b.hasAttribute('data-runtime-rollback')){const model=runtimeSample(runtimeReview.instance),h=model.history[Number(b.dataset.runtimeRollback)];model.current=h.previous;h.rolled=true;runtimeReview.plan=null;runtimeReview.status='已回退运行组合（模拟）；存档和其它设置保留。';renderRuntimeReview();}
  if(b.hasAttribute('data-content-tools')){
    const tools=[{text:'刷新列表',run:()=>{contentUpdateList();showToast('设计示例列表已刷新，未访问文件。');}},{text:'检查当前分类更新',run:()=>openContentUpdates(b)}];
    if(contentTab==='mods')tools.push({text:'检查模组依赖',run:()=>openContentDependencyReport(b)});
    workflowMenu(b,tools,'内容工具');
  }
  if(b.dataset.contentMore){const item=contentEntries().find(i=>i.id===b.dataset.contentMore);if(item)contentPolicyMenu(b,item);}
});
function openContentDependencyReport(trigger){
  const dialog=document.createElement('dialog');dialog.className='instance-dialog content-dependency-report';
  dialog.setAttribute('aria-labelledby','content-dependency-report-title');
  dialog.innerHTML=`<header><h2 id="content-dependency-report-title">检查模组依赖</h2></header><div class="instance-dialog-body"><p>${escapeHtml(selected.name)}</p><p>原生检查读取本地JAR的真实模组ID、声明依赖和别名，区分缺失ID、重复ID及未求值的冲突。</p><p class="settings-note">版本范围和嵌套归档尚未完全求值；这是只读提示，不下载、安装、移除文件，也不增加启动门槛。</p><aside class="server-review-tools">设计示意：不读取文件，不生成虚假的检查结论。</aside></div><footer><span></span><button class="small-button subtle" data-dependency-report-close>关闭</button></footer>`;
  dialog.addEventListener('click',event=>{if(event.target.closest('[data-dependency-report-close]'))dialog.close();});
  dialog.addEventListener('close',()=>{dialog.remove();if(trigger?.isConnected)trigger.focus();});
  document.querySelector('.launcher').append(dialog);dialog.showModal();
}
document.addEventListener('contextmenu',event=>{const row=event.target.closest('[data-content-id]');if(!row)return;event.preventDefault();const item=contentEntries().find(i=>i.id===row.dataset.contentId);if(item)contentPolicyMenu(row.querySelector('[data-content-tools]'),item);});
document.addEventListener('keydown',event=>{const menu=document.querySelector('.workflow-menu');if(!menu)return;if(event.key==='Escape'){event.preventDefault();event.stopImmediatePropagation();closeWorkflowMenu(true);}else if(['ArrowDown','ArrowUp','Home','End'].includes(event.key)){event.preventDefault();event.stopImmediatePropagation();const items=[...menu.querySelectorAll('button:not(:disabled)')],index=items.indexOf(document.activeElement);if(items.length)items[event.key==='Home'?0:event.key==='End'?items.length-1:(index+(event.key==='ArrowDown'?1:-1)+items.length)%items.length].focus();}else if(event.key==='Tab'){closeWorkflowMenu(true);}} ,true);
