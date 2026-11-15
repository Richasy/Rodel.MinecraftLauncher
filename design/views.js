/* Review-only page composition. Data and actions remain in memory. */
let currentPage = 'home';
let instancePageSection = 'local';
let serverClientContext = null;
const instanceSectionScrolls = { local: 0, servers: 0 };
let contentTab = 'mods';
let settingsSection = 'general';
let listResizeObserver = null;
const escapeHtml=value=>String(value).replace(/[&<>"']/g,char=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
const settingRow=(name,description,control,id='')=>`<div class="setting-row settings-card" ${id?`data-setting-id="${id}"`:''}><div class="setting-copy"><strong>${escapeHtml(name)}</strong>${description?`<p>${escapeHtml(description)}</p>`:''}</div>${control}</div>`;
function settingBody() {
  return expandedSettingBody();
}
function settingsPage() {
  return `<header class="page-heading settings-page-heading"><h1>设置</h1><div class="settings-search"><input class="setting-input" id="settings-search" type="search" role="combobox" aria-label="搜索设置" aria-autocomplete="list" aria-expanded="false" aria-controls="settings-search-results" autocomplete="off" placeholder="搜索设置"><div id="settings-search-results" class="settings-search-results acrylic" role="listbox" aria-label="设置搜索结果" hidden></div></div></header><div class="settings-layout"><nav class="settings-nav" aria-label="设置分类">${settingsTopics.map(([key,text])=>`<button data-settings-section="${key}" aria-pressed="${settingsSection===key}">${text}</button>`).join('')}</nav><section class="settings-body" aria-label="设置内容">${settingBody()}</section></div>`;
}
function renderPage() {
  closeCombo();
  listResizeObserver?.disconnect();
  const home=currentPage==='home';
  document.querySelector('.launcher').dataset.page=currentPage;
  document.querySelector('.launcher').dataset.instanceView=instancePageSection;
  document.querySelector('.hero').hidden=!home; document.querySelector('.instances').hidden=!home;
  const page=el('nav-page'); page.hidden=home;
  if(!home)page.innerHTML=currentPage==='multiplayer'?window.Multiplayer.page():currentPage==='ai'?aiPage():currentPage==='content'?contentPage():currentPage==='instances'?(instancePageSection==='servers'?serverPage():instancesPage()):currentPage==='components'?componentsPage():settingsPage();
  if(currentPage==='instances' && !serverClientContext && !(instancePageSection==='local' && typeof runtimeReview!=='undefined' && runtimeReview)) {
    page.querySelector('header').insertAdjacentHTML('afterend', `<div class="instance-display-segmented" role="tablist" aria-label="实例视图">${[['local','本地实例'],['servers','服务器']].map(([id,label])=>`<button id="instance-view-${id}" role="tab" data-instance-view="${id}" aria-controls="instance-display-panel" aria-selected="${instancePageSection===id}" tabindex="${instancePageSection===id?0:-1}">${label}</button>`).join('')}</div>`);
    const panel=page.querySelector('.page-surface');panel.id='instance-display-panel';panel.setAttribute('role','tabpanel');panel.setAttribute('aria-labelledby',`instance-view-${instancePageSection}`);
    const scroll=page.querySelector(instancePageSection==='servers'?'.server-rows':'.instance-list-scroll');
    if(scroll)scroll.scrollTop=instanceSectionScrolls[instancePageSection];
  }
  document.querySelectorAll('.nav-button').forEach(b=>{const active=b.dataset.page===currentPage;b.classList.toggle('active',active); if(active)b.setAttribute('aria-current','page');else b.removeAttribute('aria-current');});
  decorate(page);
  upgradeCombos(page);
  if(currentPage==='ai'||(currentPage==='settings'&&settingsSection==='ai'))Assistant.mount(page);
  document.querySelector('.review-assistant-states').hidden=currentPage!=='ai';
  if(currentPage==='instances'&&instancePageSection==='local') {
    const list=page.querySelector('.instance-list-scroll');
    if(list){const align=()=>list.closest('.page-surface').style.setProperty('--instance-gutter',`${list.offsetWidth-list.clientWidth}px`);
    align();listResizeObserver=new ResizeObserver(align);listResizeObserver.observe(list);}
  }
  if(currentPage==='content') {
    const list=el('content-rows');
    if(list) {
      const alignColumns=()=>list.closest('.page-surface').style.setProperty('--list-gutter',`${list.offsetWidth-list.clientWidth}px`);
      alignColumns();listResizeObserver=new ResizeObserver(alignColumns);listResizeObserver.observe(list);
    }
  }
  window.dispatchEvent(new Event('prototype-page-changed'));
}
function navigate(page,topic) {
  closeDrawer();
  if(page==='servers') { page='instances';instancePageSection='servers';serverClientContext=null; }
  currentPage=page;
  if(topic){settingsSection=topic;settingsSubview='';}
  renderPage();
}
