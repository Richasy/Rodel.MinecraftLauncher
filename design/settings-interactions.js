/* Browser-only review interactions. No Core, filesystem, process or network operations. */
let settingsQuery = '';
let settingsMatches = [];
let settingsMatchIndex = -1;
const savedReviewSettings = new Map();
function changeReviewMultiplayer(enabled){
  window.reviewMultiplayerEnabled=enabled;
  window.Multiplayer?.syncShortcuts();
  if(currentPage==='settings')render(false);
}
document.addEventListener('change',event=>{
  if(event.target.matches('[data-review-multiplayer-mini]')){window.reviewMultiplayerMini=event.target.checked;return;}
  if(!event.target.matches('[data-review-multiplayer-enabled]'))return;
  event.stopImmediatePropagation();
  if(event.target.checked||!window.Multiplayer?.hasActiveRoom()){changeReviewMultiplayer(event.target.checked);return;}
  event.target.checked=true;
  const dialog=document.createElement('dialog');dialog.className='settings-multiplayer-confirm';
  dialog.innerHTML='<h2>关闭联机功能？</h2><p>当前正在联机，关闭此选项会终止联机。</p><div><button class="small-button subtle" data-confirm>关闭联机</button><button class="small-button mp-primary" data-cancel>取消</button></div>';
  document.body.append(dialog);dialog.showModal();dialog.querySelector('[data-cancel]').focus();
  const close=()=>{dialog.close();dialog.remove();};
  dialog.querySelector('[data-cancel]').onclick=close;
  dialog.addEventListener('cancel',()=>dialog.remove());
  dialog.querySelector('[data-confirm]').onclick=()=>{window.Multiplayer.endForFeature();close();changeReviewMultiplayer(false);};
});
document.addEventListener('toggle',event=>{if(event.target.matches('[data-settings-group]')&&event.target.isConnected)expandedSettingGroups.set(event.target.dataset.settingsGroup,event.target.open);},true);
const normalizeSettingText = text => text.normalize('NFKC').toLocaleLowerCase(document.documentElement.lang);
function settingsSearchEntries() {
  const fields=preferenceFields.map(field=>({...field}));
  fields.push({id:'settings.input.network.proxy',category:'network',title:'代理地址',description:'HTTP HTTPS SOCKS4 SOCKS5 手工指定'});
  const extras=Object.entries(settingExtras).map(([id,entry])=>({id,...entry,category:
    id==='settings.java.manager'?'java':id.startsWith('settings.cleanup')||id.startsWith('settings.cache')?'content':id==='settings.diagnostics.open'||id==='settings.game-logs.open'?'logs':'general'}));
  const ai=[['defaults','默认模式','允许全部 安全模式 新对话'],['services','模型服务','模型服务 地址 凭据'],['capabilities','文件与网络','目录 授权 网络 搜索'],['integrations','外部 AI 工具','Codex Claude GitHub Copilot CLI DeepSeek Harness 安装 更新']].map(([section,title,keywords])=>({id:`ai.${section}`,category:'ai',title,description:'',keywords,aiSection:section}));
  const about=[
    ['copy-version','关于此应用','复制版本信息'],
    ['developer-apps','开发者的其他应用','云之幻 Microsoft Store'],
    ['repository','GitHub 仓库','查看项目与更新'],
    ['feedback','问题与建议','在 GitHub 上反馈与交流'],
    ['credits','技术与致谢','项目与许可证'],
    ['privacy','隐私说明',''],
    ['terms','使用说明与条款',''],
  ].map(([id,title,description])=>({id:`settings.about.${id}`,category:'about',title,description}));
  return [...fields,...extras,...ai,...about];
}
function closeSettingsResults() {
  const input=el('settings-search'),results=el('settings-search-results');
  if(results) results.hidden=true;
  input?.setAttribute('aria-expanded','false');
  input?.removeAttribute('aria-activedescendant');
  settingsMatchIndex=-1;
}
function updateSettingsResults() {
  const input=el('settings-search'),results=el('settings-search-results');
  if(!input||!results)return;
  settingsQuery=input.value;
  const query=normalizeSettingText(settingsQuery.trim()),terms=query.split(/\s+/).filter(Boolean);
  if(!terms.length){settingsMatches=[];closeSettingsResults();return;}
  const categoryName=entry=>settingsTopics.find(([key])=>key===entry.category)[1];
  const score=entry=>normalizeSettingText(entry.title)===query?0:normalizeSettingText(entry.title).startsWith(query)?1:normalizeSettingText(entry.title).includes(query)?2:3;
  settingsMatches=settingsSearchEntries().filter(entry=>terms.every(term=>normalizeSettingText(`${entry.title} ${entry.description} ${entry.keywords||''} ${categoryName(entry)}`).includes(term)))
    .sort((a,b)=>score(a)-score(b)||a.title.localeCompare(b.title,document.documentElement.lang)).slice(0,15);
  results.innerHTML=settingsMatches.length?settingsMatches.map((entry,index)=>`<button type="button" role="option" tabindex="-1" aria-selected="false" id="settings-result-${index}" data-setting-result="${index}"><strong>${escapeHtml(entry.title)}</strong><span>${escapeHtml(categoryName(entry))}</span></button>`).join(''):'<p role="status">没有找到匹配设置</p>';
  results.hidden=false; input.setAttribute('aria-expanded','true'); settingsMatchIndex=-1;
  input.removeAttribute('aria-activedescendant');
}
function chooseSettingResult(index) {
  const entry=settingsMatches[index]; if(!entry)return;
  if(entry.aiSection) Assistant.openSettings(entry.aiSection); else navigate('settings',entry.category);
  const query=el('settings-search'); query.value=settingsQuery;
  let row=document.querySelector(`[data-setting-id="${entry.id}"]`);
  if(!row)return;
  if(row.hidden){
    const proxy=entry.id==='settings.input.network.proxy';
    row=document.querySelector(`[data-setting-id="${proxy?'settings.field.network.proxy':'settings.field.memoryMode'}"]`);
    if(!row)return;
    const notice=document.createElement('p'); notice.className='settings-note'; notice.setAttribute('role','status');
    notice.textContent=proxy?'代理地址仅在手工指定模式下显示。当前已定位到代理模式，未更改你的配置。':'此项随内存分配方式显示，可在“内存分配”中查看当前模式。';row.after(notice);
  }
  for (let group=row.closest('details');group;group=group.parentElement.closest('details')) group.open=true;
  row.scrollIntoView({block:'center'});
  const selector='button:not(:disabled),a[href],input:not(:disabled),textarea:not(:disabled),select:not([hidden]):not(:disabled)';
  const control=row.matches(selector)?row:row.querySelector(selector);
  control?.focus({preventScroll:true});closeSettingsResults();
}
function syncReviewMemory() {
  const mode=el('preference-memoryMode'); if(!mode)return;
  const automatic=mode.value==='auto';
  for(const key of ['memoryReserveMb','autoMemoryMinMb','autoMemoryMaxMb']) {
    const row=el(`preference-${key}`)?.closest('.setting-row');if(row)row.hidden=!automatic;
  }
  const maximum=el('preference-maxMemoryMb')?.closest('.setting-row');
  if(maximum)maximum.hidden=automatic;
}
window.addEventListener('prototype-page-changed',()=>{
  const search=el('settings-search');if(search)search.value=settingsQuery;
  document.querySelector('.settings-body')?.classList.toggle('has-core-form',!!document.querySelector('.core-review-form'));
  syncReviewMemory();syncReviewProxy();refreshReviewDraft();syncReviewEncoding();
});
function refreshReviewDraft() {
  const footer=document.querySelector('.settings-draft-footer');if(!footer)return;
  const invalid=[...document.querySelectorAll('.core-review-fields input:not([data-proxy-address])')].find(input=>!input.checkValidity());
  const validation=footer.querySelector('.settings-validation');
  footer.hidden=!invalid;validation.hidden=!invalid;
  if(invalid)validation.textContent=`${invalid.getAttribute('aria-label')}：请输入有效数值。此输入尚未保存。`;
  else if(!document.querySelector('[data-proxy-address][aria-invalid="true"]'))savedReviewSettings.set(settingsSection,new Map(reviewControlValues));
}
function syncReviewEncoding() {
  document.querySelectorAll('[data-encoding]').forEach(select=>select.closest('.encoding-controls').querySelector('input').hidden=select.value!=='custom');
}
function syncReviewProxy() {
  const proxy=el('preference-network.proxy'),address=document.querySelector('[data-proxy-address]');
  if(!proxy||!address)return;
  const manual=proxy.value==='custom',card=address.closest('.proxy-address-card');
  card.hidden=!manual;address.required=manual;
  let valid=false;
  try {const url=new URL(address.value);valid=['http:','https:','socks4:','socks4a:','socks5:'].includes(url.protocol)&&!!url.hostname;}catch{}
  address.setAttribute('aria-invalid',String(manual&&!valid));
  const error=el('proxy-address-error');error.hidden=!manual||valid;
  error.textContent=manual&&!valid?'请输入有效的 HTTP、HTTPS 或 SOCKS 代理地址。':'';
}

document.addEventListener('input',event=>{
  if(event.target.id==='settings-search')updateSettingsResults();
  const key=controlStateKey(event.target);if(key){reviewControlValues.set(key,event.target.type==='checkbox'?String(event.target.checked):event.target.value);syncReviewProxy();refreshReviewDraft();}
});
document.addEventListener('change',event=>{
  if(event.target.id==='preference-memoryMode')syncReviewMemory();
  syncReviewProxy();refreshReviewDraft();
  syncReviewEncoding();
});
document.addEventListener('pointerdown',event=>{if(event.target.closest('[data-setting-result]'))event.preventDefault();});
document.addEventListener('focusout',event=>{if(event.target.id==='settings-search'&&!event.relatedTarget?.closest('.settings-search'))closeSettingsResults();});
document.addEventListener('keydown',event=>{
  if(event.target.id!=='settings-search')return;
  if(event.key==='Escape'){event.preventDefault();event.stopImmediatePropagation();closeSettingsResults();}
  if(['ArrowDown','ArrowUp'].includes(event.key)){
    event.preventDefault();if(el('settings-search-results').hidden)updateSettingsResults();if(!settingsMatches.length)return;
    settingsMatchIndex=(settingsMatchIndex+(event.key==='ArrowDown'?1:settingsMatchIndex<0?0:-1)+settingsMatches.length)%settingsMatches.length;
    const options=el('settings-search-results').querySelectorAll('[role="option"]');
    options.forEach((option,index)=>option.setAttribute('aria-selected',index===settingsMatchIndex));
    options[settingsMatchIndex].scrollIntoView({block:'nearest'});event.target.setAttribute('aria-activedescendant',options[settingsMatchIndex].id);
  }
  if(event.key==='Enter'){
    event.preventDefault();event.stopImmediatePropagation();
    if(settingsMatchIndex>=0)chooseSettingResult(settingsMatchIndex);else if(settingsMatches.length===1)chooseSettingResult(0);
  }
});
document.addEventListener('click',event=>{
  const button=event.target.closest('button');
  if(!event.target.closest('.settings-search'))closeSettingsResults();
  if(!button)return;
  if(button.hasAttribute('data-setting-result'))chooseSettingResult(Number(button.dataset.settingResult));
  if(button.hasAttribute('data-settings-section'))settingsSubview='';
  if(button.hasAttribute('data-settings-java')){settingsSubview='java';renderPage();}
  if(button.hasAttribute('data-settings-java-back')){settingsSubview='';renderPage();document.querySelector('[data-settings-java]')?.focus();}
  if(button.hasAttribute('data-settings-directory')){reviewDataDirectory='D:\\Games\\Rodel';reviewDirectoryChanged=true;renderPage();document.querySelector('[data-settings-directory]')?.focus();}
  if(button.hasAttribute('data-settings-java-clear')){const input=el('preference-javaPath');input.value='';input.dispatchEvent(new Event('input',{bubbles:true}));}
  if(button.hasAttribute('data-settings-java-file')){const input=el('preference-javaPath');input.value='C:\\Example\\Java\\bin\\javaw.exe';input.dispatchEvent(new Event('input',{bubbles:true}));showToast('原型示例路径；正式应用打开系统文件选择器。');}
  if(button.hasAttribute('data-settings-java-auto')){reviewDefaultJava=-1;renderPage();}
  if(button.hasAttribute('data-settings-java-default')){const selected=document.querySelector('input[name="java-runtime"]:checked');reviewDefaultJava=Number(selected.value);renderPage();document.querySelector('[data-settings-java-default]')?.focus();}
  if(button.hasAttribute('data-settings-java-details'))showToast('设计示例：查看运行时详情或移除登记。');
  if(button.hasAttribute('data-settings-task'))openReviewSettingsTask(button.dataset.settingsTask,button);
  const feedback={ 'data-settings-directory':'原型演示：选择新的应用数据目录，重启生效；不会自动迁移已有数据。',
    'data-settings-images':'原型演示：清理社区图片缓存，不影响实例、存档或自定义壁纸。',
    'data-settings-java-add':'原型演示：从本机添加 Java 或下载 Mojang 运行库。',
    'data-settings-java-refresh':'原型演示：刷新本机已登记的 Java。' };
  for(const [attribute,message]of Object.entries(feedback))if(button.hasAttribute(attribute))showToast(message);
});
function openReviewSettingsTask(kind,trigger) {
  if(document.querySelector('dialog[open]'))return;
  const dialog=document.createElement('dialog'); dialog.className='instance-dialog settings-task';
  dialog.setAttribute('aria-labelledby','settings-task-title');
  const diagnostic=kind==='diagnostics'||kind==='game-logs', game=kind==='game-logs';
  const title=diagnostic?(game?'游戏日志':'启动器日志'):'下载与内容缓存';
  const runChoices=instance=>(instance==='creative'?['2026-10-29 18:30:42']:['2026-10-29 19:40:21','2026-10-29 19:25:08','2026-10-28 20:10:34']).map((time,index)=>`<label><input type="radio" name="diagnostic-session" value="${instance}-${index}"><span><strong>${time}</strong><small>已退出（退出码 0）</small></span></label>`).join('');
  const body=diagnostic?`${game?`<p class="settings-note">按实例选择某一次游戏运行。</p><label>选择实例<select data-review-instance><option value="survival">雾谷生存</option><option value="creative">建筑练习</option></select></label><fieldset class="diagnostic-session"><legend>选择启动记录</legend>${runChoices('survival')}</fieldset><label>导出内容<select aria-label="导出内容"><option>诊断包（隐藏常见凭据）</option><option>原始日志（保持原始字节）</option></select></label>`:'<p class="settings-note">汇总最近7天的启动器运行记录，并隐藏常见凭据；无需选择实例或游戏记录。</p>'}<label for="review-diagnostic-path">目标 ZIP 文件</label><div class="diagnostic-destination"><input id="review-diagnostic-path" readonly placeholder="选择保存位置"><button class="small-button subtle" data-review-path aria-label="选择保存位置"><span data-icon="folder"></span></button></div>`:
    `<div class="cache-age"><div><strong>清理多少天前的缓存</strong><p>执行时重新检查引用和占用，保留未知文件。</p></div><input type="number" min="0" required value="30" aria-label="缓存保留天数"></div><p data-review-cache-estimate>预计清理：1 个文件，2 B（设计示例）</p><div class="cache-review-item"><strong>fabric-catalog.json</strong><p>过期版本目录缓存</p></div>`;
  dialog.innerHTML=`<header><h2 id="settings-task-title">${title}</h2></header><div class="instance-dialog-body">${body}</div><footer><button class="small-button" data-review-task-submit ${diagnostic?'disabled':''}>${diagnostic?'导出':'清理缓存'}</button><button class="small-button subtle" data-review-task-close>取消</button></footer>`;
  document.querySelector('.launcher').append(dialog);dialog.showModal();upgradeCombos(dialog);
  dialog.addEventListener('close',()=>{dialog.remove();if(trigger.isConnected)trigger.focus();});
  let cachePlanValid=true;
  const ready=()=>{
    if(diagnostic)dialog.querySelector('[data-review-task-submit]').disabled=(game&&!dialog.querySelector('input[type="radio"]:checked'))||!el('review-diagnostic-path').value.trim();
    else if(!dialog.classList.contains('settings-report')){
      cachePlanValid=false;dialog.querySelector('[data-review-task-submit]').textContent='分析缓存';
      dialog.querySelector('[data-review-cache-estimate]').textContent='保留天数已改变，请重新分析。';dialog.querySelector('.cache-review-item').hidden=true;
    }
  };
  dialog.addEventListener('input',ready);dialog.addEventListener('change',ready);
  dialog.querySelector('[data-review-instance]')?.addEventListener('change',event=>{dialog.querySelector('.diagnostic-session').innerHTML=`<legend>选择启动记录</legend>${runChoices(event.target.value)}`;ready();});
  dialog.addEventListener('click',event=>{
    if(event.target.closest('[data-review-path]')){el('review-diagnostic-path').value='C:\\Example\\diagnostics.zip';ready();showToast('原型示例路径；未打开系统选择器或创建文件。');}
    if(event.target.closest('[data-review-task-close]'))dialog.close();
    if(event.target.closest('[data-review-task-submit]')){
      if(kind==='cache'&&!cachePlanValid){
        const age=dialog.querySelector('input[type="number"]');if(!age.reportValidity())return;
        cachePlanValid=true;dialog.querySelector('[data-review-task-submit]').textContent='清理缓存';
        dialog.querySelector('[data-review-cache-estimate]').textContent='预计清理：1 个文件，2 B（设计示例）';dialog.querySelector('.cache-review-item').hidden=false;return;
      }
      const readable=kind==='cache'?'设计示例：已释放 0 B。\n\nfabric-catalog.json\n文件被其它程序占用，已保留。':'设计示例：目标 ZIP 文件已存在。\n请选择其它目标，或在正式应用保存选择器中确认替换。';
      const raw=JSON.stringify({simulation:true,operation:kind,reason:kind==='cache'?'file-in-use':'destination-exists'},null,2);
      showReviewReport(dialog,readable,raw);
    }
  });
}
function showReviewReport(dialog,readable,raw) {
  closeCombo();
  const height=dialog.getBoundingClientRect().height;
  const parent=document.createElement('div');
  while(dialog.firstChild)parent.append(dialog.firstChild);
  dialog.style.height=`${height}px`;dialog.classList.add('settings-report');
  dialog.innerHTML='<header><button class="text-button" data-report-back aria-label="返回操作">←</button><h2 id="settings-task-title">操作详情</h2><button class="small-button subtle" data-report-raw>原始内容</button></header><div class="review-report-body"><div class="review-report-panel"><textarea aria-label="完整操作详情" readonly spellcheck="false"></textarea><div class="review-report-tools"><button class="text-button" data-report-copy>复制详情</button><button class="text-button" data-report-export>导出文本</button></div></div><p class="settings-note" data-report-feedback role="status" hidden></p></div><footer><button class="small-button subtle" data-review-task-close>关闭</button></footer>';
  const text=dialog.querySelector('textarea');text.value=readable;let rawMode=false;
  dialog.querySelector('[data-report-raw]').onclick=()=>{rawMode=!rawMode;text.value=rawMode?raw:readable;text.dir=rawMode?'ltr':document.documentElement.dir||'ltr';dialog.querySelector('[data-report-raw]').textContent=rawMode?'易读内容':'原始内容';};
  dialog.querySelector('[data-report-back]').onclick=()=>{dialog.classList.remove('settings-report');dialog.replaceChildren(...parent.childNodes);dialog.querySelector('[data-review-task-submit]')?.focus();};
  dialog.querySelector('[data-report-copy]').onclick=async()=>{const feedback=dialog.querySelector('[data-report-feedback]');feedback.hidden=false;try{await navigator.clipboard.writeText(text.value);feedback.textContent='已复制详情。';}catch{feedback.textContent='无法访问剪贴板，可在文本区选取并复制。';}};
  dialog.querySelector('[data-report-export]').onclick=()=>showToast('原型演示：正式应用使用系统保存选择器导出文本。');
  dialog.querySelector('[data-report-back]').focus();
}
