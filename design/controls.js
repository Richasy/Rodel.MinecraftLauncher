/* Shared review controls: closed/open/hover/pressed/selected/disabled/focus states. */
let comboSource=null, comboTrigger=null, comboFocus=0;
const comboBindings=new WeakMap();
let comboSerial=0;
const reviewControlValues=new Map();
function controlStateKey(control) { return control.closest('.settings-body')?`settings:global:${settingsSection}:${control.dataset.preferenceKey||control.closest('[data-setting-id]')?.dataset.settingId||control.id||control.getAttribute('aria-label')}`:null; }
function upgradeCombos(root=document) {
  root.querySelectorAll('select:not([data-enhanced])').forEach(select=>{
    const key=controlStateKey(select);if(key&&reviewControlValues.has(key))select.value=reviewControlValues.get(key);
    const width=select.getBoundingClientRect().width;
    const wrapper=document.createElement('div');wrapper.className='ui-select';
    const button=document.createElement('button');button.type='button';button.className='combo-face';button.dataset.comboOpen='';
    button.setAttribute('role','combobox');button.setAttribute('aria-haspopup','listbox');button.setAttribute('aria-expanded','false');button.disabled=select.disabled;
    button.setAttribute('aria-label',select.getAttribute('aria-label')||select.closest('label')?.childNodes[0]?.textContent?.trim()||'选择');
    button.innerHTML=`<span>${select.selectedOptions[0]?.textContent||'请选择'}</span><span class="combo-arrow">${vectorIcon('chevron')}</span>`;
    wrapper.style.width=`${Math.max(150,width)}px`;select.before(wrapper);wrapper.append(button);select.hidden=true;select.dataset.enhanced='true';select.setAttribute('aria-hidden','true');wrapper.append(select);
    comboBindings.set(button,select);
  });
  root.querySelectorAll('.settings-body input,.settings-body textarea').forEach(input=>{const key=controlStateKey(input);if(reviewControlValues.has(key)){if(input.type==='checkbox')input.checked=reviewControlValues.get(key)==='true';else input.value=reviewControlValues.get(key);}});
  syncConditionalFields();
}
function syncConditionalFields() {
  const pair=[['Java 运行时','Java 路径','手工指定'],['下载来源','镜像地址','自定义镜像'],['代理模式','代理地址','手工配置']];
  for(const [name,path,value]of pair){const select=document.querySelector(`select[aria-label="${name}"]`),input=document.querySelector(`input[aria-label="${path}"]`);if(select&&input)input.disabled=select.value!==value;}
}
function closeCombo(restore=false) {
  document.querySelector('.combo-menu')?.remove();comboTrigger?.setAttribute('aria-expanded','false');comboTrigger?.removeAttribute('aria-controls');
  if(restore&&comboTrigger?.isConnected)comboTrigger.focus();comboSource=null;comboTrigger=null;
}
function focusComboOption(index) {
  const options=[...document.querySelectorAll('.combo-option:not(:disabled):not([hidden])')];if(!options.length)return;
  comboFocus=(index+options.length)%options.length;options.forEach((option,i)=>option.tabIndex=i===comboFocus?0:-1);options[comboFocus].focus();
}
function openCombo(button) {
  if(comboTrigger===button){closeCombo(true);return;}closeCombo();
  const select=comboBindings.get(button);if(!select||select.disabled)return;
  comboSource=select;comboTrigger=button;
  const menu=document.createElement('div');menu.className='combo-menu acrylic';if(select.name==='model-binding')menu.classList.add('assistant-model-menu');menu.setAttribute('role','listbox');menu.id=`combo-menu-${++comboSerial}`;
  menu.setAttribute('aria-label',button.getAttribute('aria-label'));button.setAttribute('aria-controls',menu.id);button.setAttribute('aria-expanded','true');
  const appendOption = (option,parent,groupDisabled=false) => {const index=[...select.options].indexOf(option);const item=document.createElement('button');item.type='button';item.className='combo-option';item.setAttribute('role','option');item.setAttribute('aria-selected',index===select.selectedIndex);item.dataset.comboIndex=index;item.disabled=option.disabled||groupDisabled;item.textContent=option.textContent;if(option.dataset.description){item.classList.add('has-description');const description=document.createElement('small');description.className='combo-option-description';description.textContent=option.dataset.description;description.id=`${menu.id}-description-${index}`;item.setAttribute('aria-label',option.textContent);item.setAttribute('aria-describedby',description.id);item.append(description);}item.tabIndex=-1;parent.append(item);};
  [...select.children].forEach(child=>{
    if(child.tagName==='OPTGROUP') {const group=document.createElement('div');group.className='combo-group';group.setAttribute('role','group');const label=document.createElement('div');label.className='combo-group-label';label.id=`${menu.id}-group-${menu.childElementCount}`;label.textContent=child.label;group.setAttribute('aria-labelledby',label.id);group.append(label);[...child.children].forEach(option=>appendOption(option,group,child.disabled));menu.append(group);}
    else appendOption(child,menu);
  });
  if(select.hasAttribute('data-update-source')){
    menu.classList.add('update-source-menu');
    menu.querySelectorAll('.combo-option').forEach(item=>{
      const option=select.options[Number(item.dataset.comboIndex)];
      if(!option.dataset.contentIcon)return;
      const labels=document.createElement('span');labels.className='update-source-labels';
      while(item.firstChild)labels.append(item.firstChild);
      item.classList.add('update-source-option');item.innerHTML=contentIcon(option.dataset.contentIcon);item.append(labels);
    });
  }
  if(select.hasAttribute('data-searchable')){
    menu.classList.add('combo-searchable');
    const search=document.createElement('input');search.type='search';search.className='combo-search';search.placeholder='搜索版本';search.setAttribute('aria-label','搜索游戏版本');
    const empty=document.createElement('div');empty.className='combo-empty';empty.textContent='没有匹配的版本';empty.hidden=true;
    search.addEventListener('input',()=>{const query=search.value.trim().toLowerCase();let count=0;menu.querySelectorAll('.combo-option').forEach(option=>{option.hidden=!option.textContent.toLowerCase().includes(query);option.tabIndex=-1;if(!option.hidden&&!option.disabled)count++;});menu.querySelectorAll('.combo-group').forEach(group=>group.hidden=!group.querySelector('.combo-option:not([hidden])'));empty.hidden=count>0;comboFocus=-1;});
    search.addEventListener('keydown',event=>{if(['ArrowDown','ArrowUp'].includes(event.key)){event.preventDefault();event.stopPropagation();focusComboOption(event.key==='ArrowDown'?0:-1);}if(event.key==='Enter'){event.preventDefault();event.stopPropagation();const options=menu.querySelectorAll('.combo-option:not(:disabled):not([hidden])');if(options.length===1)chooseCombo(Number(options[0].dataset.comboIndex));}});
    menu.prepend(search);menu.append(empty);
  }
  const launcher=button.closest('dialog')||document.querySelector('.launcher'),base=launcher.getBoundingClientRect(),r=button.getBoundingClientRect();launcher.append(menu);
  const width=r.width;menu.style.width=`${Math.min(width,base.width-32)}px`;menu.style.left=`${Math.max(16,Math.min(r.left-base.left,base.width-width-16))}px`;
  const availableBelow=base.bottom-r.bottom-16,availableAbove=r.top-base.top-16;
  const below=availableBelow>=Math.min(menu.scrollHeight,240)||availableBelow>=availableAbove;
  menu.style.maxHeight=`${Math.min(280,Math.max(100,below?availableBelow:availableAbove))}px`;
  menu.style.top=`${below?r.bottom-base.top+8:r.top-base.top-menu.getBoundingClientRect().height-8}px`;
  const enabled=[...menu.querySelectorAll('.combo-option:not(:disabled)')];if(select.hasAttribute('data-searchable'))menu.querySelector('.combo-search').focus();else focusComboOption(Math.max(0,enabled.findIndex(option=>Number(option.dataset.comboIndex)===select.selectedIndex)));
}
function chooseCombo(index) {
  const select=comboSource,button=comboTrigger;if(!select)return;
  select.selectedIndex=index;button.querySelector('span').textContent=select.selectedOptions[0].textContent;
  closeCombo(true);select.dispatchEvent(new Event('change',{bubbles:true}));
  if(!button.isConnected&&select.id)document.getElementById(select.id)?.parentElement.querySelector('.combo-face')?.focus();
}
document.addEventListener('click',event=>{
  const trigger=event.target.closest('[data-combo-open]');if(trigger){openCombo(trigger);return;}
  const option=event.target.closest('[data-combo-index]');if(option){chooseCombo(Number(option.dataset.comboIndex));return;}
  if(!event.target.closest('.combo-menu'))closeCombo();
});
document.addEventListener('keydown',event=>{
  const trigger=event.target.closest('[data-combo-open]');
  if(trigger&&['ArrowDown','ArrowUp','Enter',' '].includes(event.key)){event.preventDefault();openCombo(trigger);return;}
  if(!comboSource)return;
  if(event.key==='ArrowDown'){event.preventDefault();focusComboOption(comboFocus+1);}
  if(event.key==='ArrowUp'){event.preventDefault();focusComboOption(comboFocus-1);}
  if(event.key==='Escape'){event.preventDefault();event.stopImmediatePropagation();closeCombo(true);}
  if(event.key==='Tab')closeCombo(true);
  if(['Home','End'].includes(event.key)&&!event.target.matches('.combo-search')){event.preventDefault();focusComboOption(event.key==='Home'?0:-1);}
});
window.addEventListener('resize',()=>closeCombo());
document.addEventListener('change',event=>{const key=controlStateKey(event.target);if(key)reviewControlValues.set(key,event.target.type==='checkbox'?String(event.target.checked):event.target.value);syncConditionalFields();});
