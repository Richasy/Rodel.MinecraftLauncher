/* Presentation-only settings. CorePreferenceFields and native resource text are the canonical scope. */
const settingsTopics = [['general','启动器'],['launch','游戏启动'],['java','Java 与内存'],['network','下载与网络'],['content','内容与缓存'],['logs','日志与诊断'],['ai','AI 助手'],['advanced','高级参数'],['about','关于此应用']];
let settingsSubview = '';
let reviewDataDirectory = 'C:\\Users\\Player\\.rmcl';
let reviewDirectoryChanged = false;
let reviewDefaultJava = -1;
const expandedSettingGroups = new Map();
window.reviewMultiplayerEnabled ??= false;
window.reviewMultiplayerMini ??= true;
settingExtras['settings.multiplayer.enabled']={title:'启用联机功能',description:'开启后，可创建房间邀请好友进入你的世界，也可加入好友的房间。'};
settingExtras['settings.multiplayer-mini']={title:'联机迷你窗口',description:''};
function multiplayerSettingsMarkup(){
  const enabled=window.reviewMultiplayerEnabled;
  const child=settingRow('联机迷你窗口','',`<label class="setting-switch"><input type="checkbox" role="switch" data-review-multiplayer-mini aria-label="联机迷你窗口" ${window.reviewMultiplayerMini?'checked':''} ${enabled?'':'disabled'}><span aria-hidden="true"></span></label>`,'settings.multiplayer-mini');
  const header=`<label class="setting-switch settings-multiplayer-header-toggle" data-setting-id="settings.multiplayer.enabled"><input type="checkbox" role="switch" data-review-multiplayer-enabled aria-label="启用联机功能" ${enabled?'checked':''}><span aria-hidden="true"></span></label>`;
  return settingsGroup('multiplayer','联机',child,false,settingExtras['settings.multiplayer.enabled'].description).replace('<span class="settings-expander-chevron"',header+'<span class="settings-expander-chevron"');
}
const settingSelect = (label,items,id='') => `<select aria-label="${escapeHtml(label)}" ${id?`id="${id}"`:''}>${items.map(item=>`<option>${escapeHtml(item)}</option>`).join('')}</select>`;
const settingInput = (label,value,placeholder='') => `<input class="setting-input" aria-label="${escapeHtml(label)}" autocomplete="off" spellcheck="false" value="${escapeHtml(value)}" placeholder="${escapeHtml(placeholder)}">`;
const settingNumber = (label,value,unit) => `<label class="number-unit"><input type="number" min="1" value="${value}" aria-label="${escapeHtml(label)}"><span>${unit}</span></label>`;
function extraSetting(id, control) {
  const entry=settingExtras[id];
  return settingRow(entry.title,entry.description,control,id);
}
function preferenceRow(field) {
  const id=`preference-${field.key}`, label=escapeHtml(field.title);
  const shared=`id="${id}" data-preference-key="${field.key}" aria-label="${label}" name="${field.key}"`;
  let control;
  if(field.kind==='Boolean') control=`<label class="setting-switch"><input type="checkbox" role="switch" ${shared} ${field.value==='true'?'checked':''}><span aria-hidden="true"></span></label>`;
  else if(field.kind==='MemoryMode') control=`<select ${shared}><option value="manual">手工指定</option><option value="auto">自动估算</option></select>`;
  else if(field.kind==='DownloadSource') control=`<select ${shared}><option value="official">官方源（默认）</option><option value="bmclapi">BMCLAPI</option></select>`;
  else if(field.kind==='Proxy') control=`<select ${shared}><option value="">系统代理</option><option value="direct">直接连接</option><option value="custom">手工指定</option></select>`;
  else if(field.kind==='Integer') control=`<label class="number-unit"><input ${shared} type="number" min="${field.minimum}" value="${field.value}"><span>${field.key.includes('Memory')||field.key==='memoryReserveMb'?'MB':field.key.includes('Seconds')||field.key.includes('Idle')?'秒':''}</span></label>`;
  else if(['Arguments','Environment','Mirrors'].includes(field.kind)) control=`<textarea ${shared} class="setting-textarea" autocomplete="off" spellcheck="false" placeholder="${field.kind==='Mirrors'?'来源前缀=镜像地址':field.kind==='Environment'?'名称=值':'每行一个完整参数'}"></textarea>`;
  else if(field.key.endsWith('Encoding')) control=`<div class="encoding-controls"><select ${shared} data-encoding><option value="">自动选择</option>${['utf-8','gb18030','gbk','windows-1252','utf-16'].map(value=>`<option value="${value}">${value.toUpperCase()}</option>`).join('')}<option value="custom">自定义…</option></select><input class="setting-input" data-preference-key="${field.key}.custom" aria-label="${label}自定义值" placeholder="编码名称或代码页" hidden></div>`;
  else control=`<input ${shared} class="setting-input" autocomplete="off" spellcheck="false" value="${escapeHtml(field.value)}" placeholder="留空沿用默认">`;
  if(field.key==='javaPath') control=`<div class="settings-path-control">${control.replace('<input ', '<input readonly ').replace('留空沿用默认', '自动选择 Java')}<button class="small-button subtle" data-settings-java-file aria-label="选择 Java 可执行文件">${vectorIcon('folder')}</button><button class="small-button subtle" data-settings-java-clear aria-label="清除所选路径">${vectorIcon('close')}</button></div>`;
  return settingRow(field.presentationTitle??field.title,field.description,control,field.id);
}
function proxyAddressSetting() {
  return `<div class="setting-row settings-card proxy-address-card" data-setting-id="settings.input.network.proxy" hidden><div class="setting-copy"><strong>代理地址</strong><p>支持 HTTP、HTTPS、SOCKS4 和 SOCKS5。</p></div><div class="proxy-address-editor"><input id="preference-network.proxyAddress" class="setting-input" data-preference-key="network.proxyAddress" data-proxy-address aria-label="代理地址" aria-describedby="proxy-address-error" autocomplete="off" spellcheck="false" placeholder="http://127.0.0.1:7890"><p id="proxy-address-error" class="settings-field-error" role="alert" hidden></p></div></div>`;
}
function settingsGroup(id,title,body,open=false,description='') {
  const expanded=expandedSettingGroups.get(id)??open;
  return `<details class="settings-expander" data-settings-group="${id}" ${expanded?'open':''}><summary class="settings-expander-header"><span class="settings-expander-copy"><span class="settings-expander-title">${escapeHtml(title)}</span><small>${escapeHtml(description)}</small></span><span class="settings-expander-chevron" aria-hidden="true"><svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="m6 9 6 6 6-6"/></svg></span></summary><div class="settings-expander-body">${body}</div></details>`;
}
function coreSettings(category) {
  const scope=category==='network'?'':'<p class="settings-scope">默认值用于未单独覆盖的实例，在下次启动时生效。实例自己的配置优先。</p>';
  const fields=preferenceFields.filter(field=>field.category===category);
  const row=key=>preferenceRow(fields.find(field=>field.key===key));
  let rows=fields.map(preferenceRow);
  if(category==='java') {
    rows=[`<button class="settings-card settings-navigation" data-setting-id="settings.java.manager" data-settings-java><span class="setting-copy"><strong>Java 运行时管理</strong><span class="setting-description">查看本机运行时，添加、下载或设置默认 Java。</span></span><span aria-hidden="true">${vectorIcon('chevron')}</span></button>`,rows[0],settingsGroup('memory','内存',row('memoryMode')+row('maxMemoryMb'),true,'选择分配方式，并调整游戏可使用的内存。'),settingsGroup('memory-advanced','高级参数',['minMemoryMb','memoryReserveMb','autoMemoryMinMb','autoMemoryMaxMb'].map(row).join(''))];
  }
  if(category==='launch') rows=[settingsGroup('window','游戏窗口',row('fullscreen')+row('width')+row('height'),true,'选择全屏或窗口启动；默认窗口为 1280 × 720。'),row('quickPlaySingleplayer'),row('gameBridgeEnabled')];
  if(category==='network') rows=[row('network.source'),row('network.proxy'),proxyAddressSetting(),row('network.concurrency'),settingsGroup('timeouts','连接与超时',fields.filter(field=>field.key.includes('Seconds')).map(preferenceRow).join(''),false,'调整连接、请求与下载读取的等待时限。'),settingsGroup('mirrors','镜像规则',row('network.mirrors'),false,'按地址前缀为下载选择镜像来源。')];
  if(category==='logs') rows=[row('loggingCompatibility'),settingsGroup('encoding','输出编码',row('stdoutEncoding')+row('stderrEncoding'),false,'选择游戏进程的日志编码，默认自动判断。'),extraSetting('settings.diagnostics.open','<button class="small-button subtle" data-settings-task="diagnostics">导出</button>'),extraSetting('settings.game-logs.open','<button class="small-button subtle" data-settings-task="game-logs">导出</button>')];
  if(category==='advanced') rows=fields.map(field=>settingsGroup(field.key,field.title,preferenceRow({...field,presentationTitle:'',description:''}),false,field.description));
  return `<div class="core-review-form">${scope}<div class="core-review-fields">${rows.join('')}</div><div class="settings-draft-footer" hidden><p class="settings-validation" role="alert" hidden></p></div></div>`;
}
function javaManagerMarkup() {
  return `<div class="settings-child-header"><button class="text-button" data-settings-java-back>← Java 与内存</button><h2>Java 运行时管理</h2></div>${settingRow('默认运行时','按游戏版本选择合适的运行时，实例可单独覆盖。',`<span class="settings-value">${reviewDefaultJava<0?'自动选择 Java':reviewDefaultJava===0?'OpenJDK 25.0.1':'OpenJDK 21.0.8'}</span>${reviewDefaultJava>=0?'<button class="small-button subtle" data-settings-java-auto>自动选择 Java</button>':''}`)}<section class="java-installations"><header class="java-review-toolbar"><div><h3>本机 Java · 2</h3><p>选择一个运行时，可设为默认或查看详情。</p></div><div><button class="small-button subtle" data-settings-java-refresh aria-label="刷新 Java 列表">${vectorIcon('refresh')}</button><button class="small-button subtle" data-settings-java-add>添加 Java</button></div></header><div class="java-runtime-list">${[['OpenJDK 25.0.1','Mojang · x64 · 启动器管理','C:\\Example\\.rmcl\\dependencies\\java\\java-25\\bin\\javaw.exe'],['OpenJDK 21.0.8','Eclipse Adoptium · x64 · 本机发现','C:\\Program Files\\Eclipse Adoptium\\jdk-21\\bin\\javaw.exe']].map(([name,source,path],i)=>`<label class="java-runtime-row"><input type="radio" name="java-runtime" value="${i}" ${i===0?'checked':''}><span class="java-runtime-copy"><strong>${name}</strong><small>${source}</small><span class="java-runtime-path" dir="ltr">${escapeHtml(path)}</span></span><span class="java-runtime-status">可用</span></label>`).join('')}</div><footer class="java-runtime-actions"><button class="small-button" data-settings-java-default>设为默认</button><button class="small-button subtle" data-settings-java-details>更多操作</button></footer></section>`;
}
function expandedSettingBody() {
  if(settingsSection==='about') return aboutSettingsMarkup();
  if(settingsSection==='ai') return Assistant.settingsMarkup();
  if(settingsSubview==='java'&&settingsSection==='java') return javaManagerMarkup();
  if(settingsSection==='general') return extraSetting('settings.language',settingSelect('界面语言',["简体中文", "繁體中文", "English", "日本語", "한국어", "Deutsch", "Français", "Español", "Português (Brasil)", "Русский", "Italiano", "Polski", "Türkçe", "Bahasa Indonesia", "العربية"]))+
    extraSetting('settings.theme',settingSelect('界面主题',['跟随系统','浅色','深色']))+
    `<div data-setting-id="wallpaper.light.choose">${wallpaperSettingsMarkup()}</div>`+
    multiplayerSettingsMarkup()+
    extraSetting('settings.after-launch',settingSelect('游戏启动后',['最小化到系统托盘','最小化到任务栏','保持显示']))+
    extraSetting('settings.close-behavior',settingSelect('关闭主窗口时',['退出启动器','最小化到系统托盘']))+
    settingRow('应用数据目录',reviewDataDirectory,`<div class="settings-directory-actions">${reviewDirectoryChanged?'<small class="settings-restart" role="status">重启软件生效</small>':''}<button class="small-button subtle" data-settings-directory>更改文件夹</button></div>`,'settings.data.choose');
  if(settingsSection==='content') return settingsGroup('cache','缓存维护',extraSetting('settings.cleanup.open','<div class="settings-cache-actions"><small role="status">12.8 MB · 设计示例</small><button class="small-button subtle" data-settings-task="cache">管理缓存</button></div>')+extraSetting('settings.cache.clear','<div class="settings-cache-actions"><small role="status">4.2 MB · 设计示例</small><button class="small-button subtle" data-settings-images>清理图片缓存</button></div>'),true,'进入此分区时检查缓存占用，按需清理可回收文件。');
  return coreSettings(settingsSection);
}
function instancesPage() {
  if(typeof runtimeReview!=='undefined'&&runtimeReview)return runtimeReviewMarkup();
  const items=contentNoInstances?[]:instances.filter(instance => (instance.serverId ?? null) === (serverClientContext?.id ?? null));
  const taskLabels={preparing:'准备中',downloading:'下载中',downloadFailed:'下载中断',failed:'启动失败',ready:'已就绪'};
  return `<header class="server-heading"><h1>${serverClientContext ? `<button class="text-button" data-server-clients-back aria-label="返回服务器">←</button> ${escapeHtml(serverClientContext.name)} · 客户端` : '实例'}</h1><div class="server-actions"><button class="small-button" data-instance-entry="create">${serverClientContext ? '新建客户端' : '新建实例'}</button><button class="small-button subtle" data-instance-entry="import">导入实例</button></div></header>
    <section class="page-surface acrylic instance-management-list">
      <div class="instance-table-heading instance-columns" aria-hidden="true" ${items.length?'':'hidden'}><span>名称</span><span>运行配置</span><span>内容</span><span>状态</span><span>操作</span></div>
      <div class="instance-list-scroll" aria-label="实例列表">${items.length?items.map(i=>{
        const state=jobs.get(i.id)?.state;
        return `<article class="managed-instance-row instance-columns" aria-label="${escapeHtml(i.name)}">
          <button class="managed-instance-title" data-instance-select="${i.id}" aria-pressed="${i.id===selected.id}" aria-label="选择 ${escapeHtml(i.name)}"><span class="instance-icon">${cubeIcon(i.icon)}</span><span class="instance-name"><strong title="${escapeHtml(i.name)}">${escapeHtml(i.name)}</strong><small>${i.serverId ? '服务器专用客户端' : i.id===selected.id?'当前实例':'本地实例'}</small></span></button>
          <div class="instance-runtime"><strong>${i.version} · ${i.loader}</strong><small>Java ${i.java} · ${i.memory} GB</small></div>
          <button class="instance-content-link" data-manage-content="${i.id}" aria-label="管理 ${escapeHtml(i.name)} 的内容">${i.mods?i.mods+' 个模组':'纯净游戏'}</button>
          <span class="instance-state ${state==='running'?'is-running':''}">${state==='running'?'运行中':taskLabels[state]||'已就绪'}</span>
          <div class="managed-instance-actions"><button class="small-button" data-manage-launch="${i.id}" aria-label="${i.serverId ? '连接' : '启动'} ${escapeHtml(i.name)}">${i.serverId ? '连接' : '启动'}</button><button class="flat-config-button" data-manage-settings="${i.id}" aria-label="配置 ${escapeHtml(i.name)}" title="当前实例设置">${vectorIcon('wrench')}</button><button class="row-more" data-manage-more="${i.id}" aria-haspopup="menu" aria-label="管理 ${escapeHtml(i.name)}">···</button></div>
        </article>`;
      }).join(''):homeEmptyMarkup()}</div>
      <footer class="content-bottom"><span>${items.length} 个实例</span><span>实例独立保存存档与配置</span></footer>
    </section>`;
}
function componentsPage() {
  return `<header class="page-heading"><div><span class="pixel-label">DESIGN REFERENCE</span><h1>组件状态</h1><p>同一套材质，完整的交互状态。</p></div></header><section class="page-surface acrylic component-gallery"><div class="component-sample"><h2>ComboBox</h2><p>点击展开；支持方向键、Enter 和 Escape。</p>${settingSelect('正常选择',['沿用实例配置','自动发现','手工指定'])}<select aria-label="禁用选择" disabled><option>当前不可用</option></select></div><div class="component-sample"><h2>输入框</h2><p>正常、禁用、错误；键盘焦点清晰可见。</p><input class="setting-input" aria-label="正常输入" placeholder="输入内容"><input class="setting-input" aria-label="禁用输入" value="当前不可编辑" disabled><input class="setting-input" aria-label="错误输入" value="无效路径" aria-invalid="true"></div><div class="component-sample"><h2>按钮</h2><p>悬停、按下、禁用；外框保持对齐。</p><div class="component-buttons"><button class="small-button" data-action="component-demo">普通操作</button><button class="small-button" disabled>不可用</button><button class="enable-button is-on" aria-pressed="true" data-action="toggle-content">已启用</button></div></div><div class="component-sample"><h2>选中</h2><p>选中以内凹与主题色表现，不加左侧指示条。</p><div class="page-tabs"><button aria-pressed="false" data-action="component-demo">未选中</button><button aria-pressed="true" data-action="component-demo">已选中</button></div></div></section>`;
}
