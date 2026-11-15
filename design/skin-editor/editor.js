import { createSkinCanvas, normalizeBaseAlpha, SKIN_TEMPLATES, createTemplateSkin, getSkinFrames } from './canvas.js';
import { createPalette } from './palette.js';
import { createLayerPanel } from './layers.js';
import { createColorPicker } from './color-picker.js';
import { bindAnimationPreview } from './animation.js';

// This is a reviewable design, not a second product editor or a Core adapter.
// No account, model, MCP, game directory, or persisted skin document is accessed.
const $ = selector => document.querySelector(selector);
const $$ = selector => [...document.querySelectorAll(selector)];
const icons = {
  cube:'M12 3 3 8v9l9 5 9-5V8Z M3 8l9 5 9-5 M12 13v9 M7.5 5.5l9 5',
  skin:'M8 3h8v7H8Z M8 11h8v7H8Z M3 11h4v7H3Z M17 11h4v7h-4Z M8 19h3v3H8Z M13 19h3v3h-3Z',
  brush:'m15 3 6 6-10 10-6-6Z M4 16l4 4-5 1Z M13 5l6 6',
  eraser:'m15 3 6 6-11 12H5l-4-4Z M7 11l7 7 M10 21h11',
  fill:'m5 8 7-7 10 10-9 9L3 10 M5 8l8 8 M2 22h20 M4 14s-3 3-3 5a3 3 0 0 0 6 0c0-2-3-5-3-5Z',
  picker:'m14 4 6 6 M16 2l6 6-3 3-6-6Z M14 7 3 18v3h3L17 10',
  select:'M3 7V3h4 M17 3h4v4 M21 17v4h-4 M7 21H3v-4 M11 3h2 M21 11v2 M11 21h2 M3 11v2',
  orbit:'M20 8a9 9 0 1 0 1 8 M16 3l5 5-6 1 M8 9l4-2 4 2v6l-4 2-4-2Z',
  plus:'M12 4v16 M4 12h16',minus:'M5 12h14',
  folder:'M3 6h7l2 3h9v11H3Z M3 6V4h6l2 2h8v3',
  image:'M3 4h18v16H3Z M3 16l5-5 5 5 3-3 5 5 M17 8a1.5 1.5 0 1 0-3 0 1.5 1.5 0 1 0 3 0',
  chatAdd:'M21 12V4H3v13h4v4l5-4h1 M18 14v8 M14 18h8',
  export:'M12 15V3 M7 8l5-5 5 5 M4 14v7h16v-7',
  import:'M9 3h12v18H9v-4 M9 3v4 M2 12h13 M11 8l4 4-4 4 M14 18l3-3 4 4',
  undo:'M8 5 3 10l5 5 M3 10h11a6 6 0 0 1 0 12',
  redo:'m16 5 5 5-5 5 M21 10H10a6 6 0 0 0 0 12',
  close:'M5 5l14 14 M19 5 5 19',chevron:'m7 9 5 5 5-5',
  mirror:'M12 2v20 M8 6 2 12l6 6Z M16 6l6 6-6 6Z',
  grid:'M3 3h18v18H3Z M9 3v18 M15 3v18 M3 9h18 M3 15h18',
  fit:'M3 8V3h5 M16 3h5v5 M21 16v5h-5 M8 21H3v-5 M8 8h8v8H8Z',
  split:'M3 4h18v16H3Z M14 4v16',
  keyboard:'M2 5h20v14H2Z M5 9h1 M9 9h1 M13 9h1 M17 9h2 M5 12h1 M9 12h1 M13 12h1 M17 12h2 M6 16h12',
  mouse:'M12 3a6 6 0 0 1 6 6v6a6 6 0 0 1-12 0V9a6 6 0 0 1 6-6Z M12 3v6 M6 10h12',
  info:'M12 8v.1 M12 11v6 M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20',
  spark:'m12 2 2.5 7.5L22 12l-7.5 2.5L12 22l-2.5-7.5L2 12l7.5-2.5Z M21 2v4 M19 4h4',
  eye:'M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12Z M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6',
  eyeOff:'M3 3l18 18 M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12',
  lock:'M6 10h12v11H6Z M8 10V6a4 4 0 0 1 8 0v4',
  unlock:'M6 10h12v11H6Z M8 10V6a4 4 0 0 1 8 0',
  arrow:'M4 12h16 M14 6l6 6-6 6',external:'M14 3h7v7 M21 3 11 13 M10 3H3v18h18v-7',
  check:'m4 12 5 5L20 6',layers:'m12 3 10 5-10 5L2 8Z M2 12l10 5 10-5 M2 16l10 5 10-5'
};
window.vectorIcon = name => `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${icons[name] || icons.cube}"/></svg>`;
function hydrate(root=document) { root.querySelectorAll('[data-icon]').forEach(el=>{ el.innerHTML=window.vectorIcon(el.dataset.icon); }); }
hydrate();
window.upgradeCombos();

const names = {brush:'画笔',eraser:'橡皮',fill:'当前面连通填充',picker:'取色',select:'矩形选区'};
const parts = {all:'全部部位',head:'头部',body:'躯干',rightArm:'右臂',leftArm:'左臂',rightLeg:'右腿',leftLeg:'左腿'};
const faces = {front:'正面',back:'背面',left:'左侧',right:'右侧',top:'顶部',bottom:'底部'};
const state = {tool:'brush',color:'#638B4D',size:1,mirror:false,part:'all',surface:'base',revision:1,selection:null,alt:false,ctrl:false,tab:'edit',view:'3d',uvVisible:true};
let documentOptions = {model:'classic',size:64};
let documentFrames = getSkinFrames(documentOptions);
const modelLabel = value => value==='slim'?'Slim · 细手臂':'Classic · 标准手臂';
const dimensions = size => `${size} × ${size}`;
const model = createSkinCanvas($('#model-canvas'));
bindAnimationPreview(model);
const uv = createSkinCanvas($('#uv-canvas'));
uv.setView({mode:'uv',zoom:1});
let layers = [{id:1,name:'基础皮肤',pixels:model.getPixels(),visible:true,locked:false,opacity:100,base:true}];
let activeId = 1, nextId = 2, history = [], cursor = 0;
let initialTemplate = SKIN_TEMPLATES[0], templatePreviews = [];
let stroke = null, lastHit = null, toastTimer, hover = null, palette;
const viewTransforms={ '3d':{zoom:1,panX:0,panY:0}, uv:{zoom:1,panX:0,panY:0} };
const cloneLayers = () => layers.map(layer=>({...layer,pixels:new Uint8ClampedArray(layer.pixels)}));
const activeLayer = () => layers.find(layer=>layer.id===activeId) || layers[0];
const snapshot = () => ({layers:cloneLayers(),activeId,options:{...documentOptions}});
const esc = value => String(value).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

// Mirrors a Sprout Standard Flyout: anchored, light-dismissed, with keyboard focus.
let openFlyout = null, flyoutTrigger = null;
function positionFlyout() {
  if(!openFlyout)return;
  const bar=$('.editor-titlebar').getBoundingClientRect(),anchor=flyoutTrigger.getBoundingClientRect();
  openFlyout.style.left=`${Math.max(12,Math.min(anchor.left-bar.left,bar.width-openFlyout.offsetWidth-12))}px`;
}
function closeFlyout(restoreFocus=true) {
  if(!openFlyout)return;
  window.closeCombo();
  openFlyout.hidden=true;flyoutTrigger.setAttribute('aria-expanded','false');
  if(restoreFocus)flyoutTrigger.focus();
  openFlyout=null;flyoutTrigger=null;
}
function toggleFlyout(trigger) {
  const panel=document.getElementById(trigger.getAttribute('aria-controls'));
  const wasOpen=openFlyout===panel;
  closeFlyout(false);
  if(wasOpen){trigger.focus();return;}
  openFlyout=panel;flyoutTrigger=trigger;panel.hidden=false;
  trigger.setAttribute('aria-expanded','true');positionFlyout();
  panel.querySelector('button,input:not([hidden])')?.focus();
}
for(const id of ['app-menu','document-menu','tool-menu','export-menu'])$(`#${id}`).onclick=event=>toggleFlyout(event.currentTarget);
$$('[data-close-flyout]').forEach(button=>button.onclick=()=>closeFlyout());
document.addEventListener('pointerdown',event=>{
  if(openFlyout&&!openFlyout.contains(event.target)&&!event.target.closest('.titlebar-trigger,.combo-menu'))closeFlyout(false);
});
document.addEventListener('keydown',event=>{
  if(!openFlyout||event.defaultPrevented||event.target.closest('.combo-menu'))return;
  if(event.key==='Escape'){event.preventDefault();event.stopImmediatePropagation();closeFlyout();return;}
  if(openFlyout.getAttribute('role')==='menu'&&['ArrowDown','ArrowUp','Home','End'].includes(event.key)) {
    const items=[...openFlyout.querySelectorAll('[role="menuitem"]')],index=items.indexOf(document.activeElement);
    const next=event.key==='Home'?0:event.key==='End'?items.length-1:(index+(event.key==='ArrowDown'?1:items.length-1))%items.length;
    event.preventDefault();items[next]?.focus();return;
  }
  if(event.key==='Tab') {
    const controls=[...openFlyout.querySelectorAll('button:not(:disabled),input:not(:disabled),[tabindex="0"]')].filter(control=>control.getClientRects().length);
    const first=controls[0],last=controls.at(-1);
    if(event.shiftKey&&document.activeElement===first){event.preventDefault();last?.focus();}
    else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first?.focus();}
  }
});
window.addEventListener('resize',positionFlyout);
window.addEventListener('blur',()=>closeFlyout(false));

function updateToolSummary() {
  $('#tool-summary').textContent=['brush','eraser'].includes(state.tool)?`${state.size} px`:'';
  $('#brush-square').style.width=$('#brush-square').style.height=`${state.size*6}px`;
  $('#brush-footprint-label').textContent=`方形 · ${state.size} × ${state.size} 像素`;
  updateCanvasCursor();
}
function compose(source=layers) {
  const output = new Uint8ClampedArray(documentOptions.size**2*4);
  for (const layer of source) {
    if (!layer.visible) continue;
    for(let i=0;i<output.length;i+=4) {
      const alpha=layer.pixels[i+3]/255*layer.opacity/100, under=output[i+3]/255, result=alpha+under*(1-alpha);
      if (!result) continue;
      for(let c=0;c<3;c++) output[i+c]=(layer.pixels[i+c]*alpha+output[i+c]*under*(1-alpha))/result;
      output[i+3]=result*255;
    }
  }
  return output;
}
function renderPixels() {
  const pixels=compose();
  model.setPixels(pixels); uv.setPixels(pixels);
}
function notify(message) {
  $('#editor-toast').textContent=message; $('#editor-toast').hidden=false;
  clearTimeout(toastTimer); toastTimer=setTimeout(()=>$('#editor-toast').hidden=true,3600);
}
function updateTarget() {
  $('#target-description').textContent=`${activeLayer().name} · ${state.surface==='base'?'底层':'外层'}`;
  $('#revision-status').textContent=`版本 ${state.revision}`;
  $('#metadata-revision').textContent=state.revision;
  $('#metadata-layers').textContent=`${layers.length} 个图层`;
  $('#metadata-size').textContent=`${dimensions(documentOptions.size)} px`;
  $('#metadata-model').textContent=modelLabel(documentOptions.model);
  $('#uv-dimensions').textContent=dimensions(documentOptions.size);
  $('#selection-status').textContent=state.selection ? `${parts[state.selection.part]} · ${faces[state.selection.face]} · ${state.selection.w} × ${state.selection.h} 选区` : '未选择区域';
  $('#undo').disabled=cursor===0; $('#redo').disabled=cursor===history.length;
  $('#brush-size').disabled=!['brush','eraser'].includes(state.tool);
}
function commit(before,label,source='manual') {
  const after=snapshot();
  const changed = before.options.model!==after.options.model || before.options.size!==after.options.size || before.layers.length!==after.layers.length || before.layers.some((layer,i)=>{
    const next=after.layers[i];
    return !next || layer.id!==next.id || layer.name!==next.name || layer.visible!==next.visible || layer.locked!==next.locked || layer.opacity!==next.opacity || layer.pixels.some((v,j)=>v!==next.pixels[j]);
  });
  if(!changed) return false;
  history.splice(cursor); history.push({before,after,label,source}); cursor=history.length;
  if(history.length>32) { history.shift();cursor--; }
  state.revision++; renderPixels(); renderLayers(); renderHistory(); updateTarget();
  $('#save-status').textContent='本页草稿已更新 · 刷新页面将重置';
  return true;
}
function restoreSnapshot(value) {
  configureDocument(value.options);
  layers=value.layers.map(layer=>({...layer,pixels:new Uint8ClampedArray(layer.pixels)})); activeId=value.activeId;
}
function configureDocument(options) {
  if(options.model===documentOptions.model&&options.size===documentOptions.size)return;
  documentOptions={...options};documentFrames=getSkinFrames(documentOptions);
  $('#export-menu').hidden=documentOptions.size!==128;
  closeFlyout(false);
  model.configure(documentOptions);uv.configure(documentOptions);
  hover=null;clearSelection();updateCanvasCursor();
}
function undo(redo=false) {
  if(stroke) return;
  if(redo ? cursor>=history.length : cursor===0) return;
  const step=history[redo?cursor:cursor-1]; restoreSnapshot(redo?step.after:step.before); cursor+=redo?1:-1;
  state.revision++; renderPixels();renderLayers();renderHistory();updateTarget();
  notify(`${redo?'已重做':'已撤销'}：${step.label}`);
}
function renderHistory() {
  $('#history-count').textContent=`${cursor} 次编辑`;
  $('#history-list').innerHTML=[...history].reverse().map((entry,index)=>{
    const step=history.length-index,reverted=step>cursor;
    return `<li class="${reverted?'reverted':''}"><button data-history="${step}" aria-current="${step===cursor?'step':'false'}"><i data-icon="${entry.source==='ai'?'spark':'brush'}"></i><span>${esc(entry.label)}<small>${reverted?'后续步骤 · 可恢复':entry.source==='ai'?'AI 建议 · 已接受':'手动编辑'} · ${step}</small></span></button></li>`;
  }).join('')+`<li><button data-history="0" aria-current="${cursor===0?'step':'false'}"><i data-icon="skin"></i><span>${history.length===32?'最早保留状态':`${esc(initialTemplate.name)} · 初始状态`}<small>点击回溯 · 新编辑前保留后续步骤</small></span></button></li>`;
  hydrate($('#history-list'));
}
function goToHistory(index) {
  if(stroke||index===cursor||index<0||index>history.length||!history.length)return;
  restoreSnapshot(index===0?history[0].before:history[index-1].after);
  cursor=index;state.revision++;renderPixels();renderLayers();renderHistory();updateTarget();
  $('#save-status').textContent='正在回溯历史 · 新编辑前保留后续步骤';
}
const layerPanel=createLayerPanel($('#layer-list'),{
  getLayers:()=>layers,getActiveId:()=>activeId,
  onSelect:id=>{activeId=id;renderLayers();},
  onToggleVisible:id=>{const layer=layers.find(item=>item.id===id);if(!layer||layer.base)return;const before=snapshot();layer.visible=!layer.visible;commit(before,`${layer.visible?'显示':'隐藏'}图层`);},
  onRename:(id,name)=>{const layer=layers.find(item=>item.id===id);if(!layer||!name.trim())return;const before=snapshot();layer.name=name.trim();commit(before,'重命名图层');},
  onReorder:ids=>{const before=snapshot();layers=ids.map(id=>layers.find(layer=>layer.id===id));commit(before,'调整图层顺序');},
  onExport:exportLayer,
  notify
});
function renderLayers() {
  layerPanel.render();updateTarget();
}
function chooseTool(tool) {
  state.tool=tool;
  $$('[data-tool]').forEach(button=>button.setAttribute('aria-pressed',button.dataset.tool===tool));
  const label=tool==='fill'?'填充':names[tool];
  $('#tool-name').innerHTML=`<i data-icon="${tool}"></i><span>${label}</span>`;hydrate($('#tool-name'));updateTarget();updateToolSummary();
  $('#tool-flyout-title').textContent=`${label}选项`;
  const brushTool=['brush','eraser'].includes(tool);
  for(const selector of ['.option-field','.brush-shape'])$(selector).hidden=!brushTool;
  $('#tool-description').hidden=brushTool;
  $('#tool-description').textContent={fill:'当前面 · 相同颜色的连通区域',picker:'取样所有可见图层的合成颜色',select:'在主平面或展开图中选择一个身体面内的像素'}[tool]||'';
  if(tool==='select'&&state.view==='3d'&&!state.uvVisible)toggleUv(true);
  if(tool==='fill') notify('填充范围：当前身体面内的同色连通区域');
  updateCanvasCursor();
}
function setColor(value) {
  if(!/^#[0-9a-f]{6}([0-9a-f]{2})?$/i.test(value)) { $('#color-error').hidden=false;$('#hex-color').setAttribute('aria-invalid','true');return; }
  state.color=value.toUpperCase();$('#color-error').hidden=true;$('#hex-color').removeAttribute('aria-invalid');
  $('#color-picker').value=value.slice(0,7);$('#hex-color').value=state.color;$('#rail-color').style.background=value;
  palette?.syncColor(state.color);
}
palette=createPalette($('#palette-editor'),{getColor:()=>state.color,onPick:setColor,notify});
setColor(state.color);
function setViewMode(mode) {
  const current=model.getView();viewTransforms[state.view]={zoom:current.zoom,panX:current.panX,panY:current.panY};
  state.view=mode;$('.canvas-layout').dataset.view=mode;
  $('#uv-toggle').hidden=mode!=='3d';$('#uv-window').hidden=mode!=='3d'||!state.uvVisible;
  $$('[data-view]').filter(el=>el.tagName==='BUTTON').forEach(button=>button.setAttribute('aria-pressed',button.dataset.view===mode));
  $('#main-view-label').textContent=mode==='3d'?'MODEL':'TEXTURE';
  $('#main-view-title').textContent=mode==='3d'?'立体绘制':'平面绘制';
  $('#main-view-hint').textContent=`左键绘制 · Ctrl 拖动平移 · Alt 拖动${mode==='3d'?'旋转':'平移'} · 滚轮缩放`;
  $('#model-canvas').setAttribute('aria-label',`${mode==='3d'?'3D':'平面'}皮肤画布，左键绘制，Ctrl拖动平移，Alt拖动${mode==='3d'?'旋转':'平移'}，滚轮缩放`);
  model.setView({mode,...viewTransforms[mode]});model.setBrushPreview(null);
  requestAnimationFrame(()=>{model.render();drawSelection();});updateCanvasCursor();
}
function toggleUv(visible=!state.uvVisible) {
  state.uvVisible=visible;$('#uv-window').hidden=state.view!=='3d'||!visible;$('#uv-toggle').setAttribute('aria-pressed',visible);
  if(!visible){uv.setBrushPreview(null);$('#uv-toggle').focus();}
  requestAnimationFrame(()=>{uv.render();drawSelection();});
}
function setSurface(surface) {
  state.surface=surface; clearSelection();
  if(surface==='outer') $('#outer-visible').checked=true;
  model.setView({layer:surface,outerVisible:$('#outer-visible').checked});uv.setView({layer:surface});
  $$('[data-surface]').forEach(button=>button.setAttribute('aria-pressed',button.dataset.surface===surface));
  $('#surface-help').textContent=surface==='base'?'底层构成人物本体，始终保持不透明。':'外层用于帽子与衣物，可在透明区域绘制。';
  updateTarget();
}
function clearSelection() { state.selection=null;$('#selection-rect').hidden=true;$('#main-selection-rect').hidden=true;$('#clear-selection').hidden=true;updateTarget(); }
function drawSelection() {
  for(const [engine,canvas,rect] of [[model,$('#model-canvas'),$('#main-selection-rect')],[uv,$('#uv-canvas'),$('#selection-rect')]]) {
    const s=state.selection,bounds=s&&engine.getUvRect(s.x,s.y,s.w,s.h);
    rect.hidden=!bounds;
    if(bounds)Object.assign(rect.style,{left:`${canvas.offsetLeft+bounds.left}px`,top:`${canvas.offsetTop+bounds.top}px`,width:`${bounds.width}px`,height:`${bounds.height}px`});
  }
}
function inSelection(x,y) { const s=state.selection;return !s || (x>=s.x&&x<s.x+s.w&&y>=s.y&&y<s.y+s.h); }
function paint(engine,hit) {
  if(!hit)return;
  if(state.tool==='picker') {const color=engine.getColor(hit);if(color?.length===7)setColor(color);return;}
  const layer=activeLayer();
  if(layer.locked||!layer.visible) {notify(layer.locked?'此图层已锁定，请解锁后绘制。':'此图层已隐藏，请显示后绘制。');return;}
  if(!['brush','eraser','fill'].includes(state.tool))return;
  if(state.tool==='eraser'&&layer.base&&state.surface==='base') {notify('基础皮肤底层保持不透明。请用颜色覆盖，或在新增图层上绘制。');return;}
  // Edit an isolated copy of the active paint layer, then composite it back.
  // This prevents a visible layer above it from leaking into the selected layer.
  engine.setPixels(layer.pixels);
  const before=engine.getPixels(),color=state.tool==='eraser'?'#00000000':state.color;
  if(state.tool==='fill') engine.fillAt(hit,color,inSelection);
  else engine.paintAt(hit,color,state.size,state.mirror);
  const after=engine.getPixels();
  for(let i=0;i<after.length;i+=4) {
    const x=(i/4)%documentOptions.size,y=Math.floor(i/4/documentOptions.size);
    if(!inSelection(x,y))continue;
    if(after[i]!==before[i]||after[i+1]!==before[i+1]||after[i+2]!==before[i+2]||after[i+3]!==before[i+3]) layer.pixels.set(after.slice(i,i+4),i);
  }
  renderPixels();
}
function beginManualEdit() {
}
function updateCanvasCursor() {
  for(const [canvas,engine] of [[$('#model-canvas'),model],[$('#uv-canvas'),uv]]) {
    const navigation=stroke?.engine===engine&&stroke.moveMode;
    const hit=hover?.canvas===canvas&&!state.alt&&!state.ctrl&&!navigation?engine.hitTest(hover.x,hover.y):null;
    const preview=hit&&['brush','eraser'].includes(state.tool);
    engine.setBrushPreview(preview?hit:null,state.size,state.mirror);
    canvas.style.cursor=navigation?'grabbing':state.alt||state.ctrl?'grab':preview?'none':'crosshair';
  }
}
function finishGesture() {
  const gesture=stroke;if(!gesture)return;
  stroke=null;lastHit=null;
  if(gesture.before)commit(gesture.before,gesture.label);
  if(gesture.canvas.hasPointerCapture(gesture.pointerId))gesture.canvas.releasePointerCapture(gesture.pointerId);
  updateCanvasCursor();
}
function wireCanvas(canvas,engine,isMini) {
  const isUv=()=>isMini||state.view==='uv';
  canvas.addEventListener('contextmenu',event=>event.preventDefault());
  canvas.addEventListener('pointerdown',event=>{
    if(stroke)return;
    if(event.button!==0&&event.button!==2)return;
    if(isUv()&&event.button!==0)return;
    canvas.focus();canvas.setPointerCapture(event.pointerId);
    state.alt=event.altKey;state.ctrl=event.ctrlKey;
    if(event.altKey||event.button===2||event.ctrlKey) {
      const view=engine.getView();
      stroke={engine,canvas,pointerId:event.pointerId,moveMode:isUv()||(event.button===0&&event.ctrlKey)?'pan':'orbit',x:event.clientX,y:event.clientY,...view};
      updateCanvasCursor();return;
    }
    const hit=engine.hitTest(event.clientX,event.clientY);
    if(['brush','eraser','fill'].includes(state.tool)&&state.selection&&(!hit||!inSelection(hit.x,hit.y))) {
      clearSelection();updateCanvasCursor();return;
    }
    if(!hit)return;
    if(state.tool==='select') {
      if(!isUv()){notify('请在平面视图或展开图内框选。Alt 拖动仍可旋转。');return;}
      stroke={engine,canvas,pointerId:event.pointerId,select:true,start:hit};state.selection={...hit,w:1,h:1};$('#clear-selection').hidden=false;drawSelection();updateTarget();return;
    }
    if(state.tool!=='picker')beginManualEdit();
    stroke={engine,canvas,pointerId:event.pointerId,before:snapshot(),label:names[state.tool],paint:true};lastHit=hit;
    paint(engine,hit);
  });
  canvas.addEventListener('pointermove',event=>{
    if(stroke&&stroke.pointerId!==event.pointerId)return;
    if(stroke&&event.buttons===0)finishGesture();
    hover={canvas,engine,x:event.clientX,y:event.clientY};state.alt=event.altKey;state.ctrl=event.ctrlKey;
    const hit=engine.hitTest(event.clientX,event.clientY);
    updateCanvasCursor();
    if(!stroke||stroke.engine!==engine)return;
    if(stroke.moveMode==='orbit') {engine.setView({yaw:stroke.yaw+(event.clientX-stroke.x)*.009,pitch:Math.max(-1.2,Math.min(1.2,stroke.pitch+(event.clientY-stroke.y)*.006))});return;}
    if(stroke.moveMode==='pan') {engine.setView({panX:stroke.panX+event.clientX-stroke.x,panY:stroke.panY+event.clientY-stroke.y});drawSelection();return;}
    if(stroke.select) {
      const start=stroke.start;
      if(hit&&hit.part===start.part&&hit.face===start.face&&hit.layer===start.layer) {
        state.selection={...start,x:Math.min(start.x,hit.x),y:Math.min(start.y,hit.y),w:Math.abs(hit.x-start.x)+1,h:Math.abs(hit.y-start.y)+1};drawSelection();updateTarget();
        $('#clear-selection').hidden=false;
      }
      return;
    }
    if(stroke.paint&&hit&&state.tool!=='fill') {
      if(lastHit&&hit.part===lastHit.part&&hit.face===lastHit.face&&hit.layer===lastHit.layer) {
        const steps=Math.max(Math.abs(hit.x-lastHit.x),Math.abs(hit.y-lastHit.y));
        for(let i=1;i<=steps;i++)paint(engine,{...hit,x:Math.round(lastHit.x+(hit.x-lastHit.x)*i/steps),y:Math.round(lastHit.y+(hit.y-lastHit.y)*i/steps)});
      } else paint(engine,hit);
      lastHit=hit;
    }
  });
  canvas.addEventListener('pointerleave',()=>{if(hover?.canvas===canvas)hover=null;updateCanvasCursor();});
  const finish=event=>{
    if(stroke&&(stroke.engine!==engine||stroke.pointerId!==event.pointerId))return;
    finishGesture();
    if(canvas.hasPointerCapture(event.pointerId))canvas.releasePointerCapture(event.pointerId);
    updateCanvasCursor();
  };
  canvas.addEventListener('pointerup',finish);canvas.addEventListener('pointercancel',finish);canvas.addEventListener('lostpointercapture',finish);
  canvas.addEventListener('wheel',event=>{event.preventDefault();engine.setView({zoom:Math.max(.5,Math.min(5,engine.getView().zoom-event.deltaY*.001))});drawSelection();updateCanvasCursor();},{passive:false});
}
wireCanvas($('#model-canvas'),model,false);wireCanvas($('#uv-canvas'),uv,true);
function tab(name) {
  state.tab=name;
  $$('[data-tab]').forEach(button=>{const active=button.dataset.tab===name;button.setAttribute('aria-selected',active);button.tabIndex=active?0:-1;});
  for(const panel of ['edit','ai','history'])$(`#panel-${panel}`).hidden=panel!==name;
  if(name==='ai')$('.unread-dot').hidden=true;
}
function dialog(title,html,actions=[{text:'知道了'}]) {
  closeFlyout();
  clearTemplatePreviews();
  $('#dialog-title').textContent=title;$('#dialog-body').innerHTML=html;$('#dialog-actions').replaceChildren();
  for(const action of actions) {const button=document.createElement('button');button.className=`e-button ${action.primary?'primary':''}`;button.textContent=action.text;button.onclick=()=>{if(action.run?.()!==false)$('#editor-dialog').close();};$('#dialog-actions').append(button);}
  if(!$('#editor-dialog').open)$('#editor-dialog').showModal();
}
function clearTemplatePreviews() {
  window.closeCombo();
  templatePreviews.forEach(preview=>preview.destroy());templatePreviews=[];
  $('#editor-dialog').classList.remove('template-dialog');
}
$('#editor-dialog').addEventListener('close',clearTemplatePreviews);
function modelField(id,value='classic') {
  return `<div class="document-option"><label for="${id}">手臂模型</label><select id="${id}" aria-label="手臂模型"><option value="classic" ${value==='classic'?'selected':''}>标准手臂 · Classic</option><option value="slim" ${value==='slim'?'selected':''}>细手臂 · Slim</option></select></div>`;
}
function newDocumentDialog() {
  const cards=SKIN_TEMPLATES.map((template,index)=>`<label class="template-card"><input type="radio" name="skin-template" id="template-${template.id}" value="${template.id}" ${index===0?'checked':''}><canvas data-template-preview="${template.id}" role="img" aria-label="${esc(template.name)}预览"></canvas><span class="template-caption"><strong>${esc(template.name)}</strong><small>${esc(template.description)}</small></span></label>`).join('');
  dialog('新建皮肤',`<p class="template-intro">选择一个人物底稿，继续画出自己的样子。</p><div class="template-grid" role="radiogroup" aria-label="基础模板">${cards}</div><div class="document-options">${modelField('new-model')}<div class="document-option"><label for="new-size">纹理尺寸</label><select id="new-size" aria-label="纹理尺寸"><option value="64">64 × 64</option><option value="128">128 × 128 · HD</option></select></div></div><p class="template-note" id="resolution-note">标准分辨率，可用于 Java 版账号皮肤。</p>${history.length?'<p class="template-note">新建将替换本页草稿，需要保留请先导出。</p>':''}`,[
    {text:'创建皮肤',primary:true,run:()=>startDocument($('input[name="skin-template"]:checked').value,{model:$('#new-model').value,size:Number($('#new-size').value)})},
    {text:'取消'}
  ]);
  $('#editor-dialog').classList.add('template-dialog');
  window.upgradeCombos($('#dialog-body'));
  $$('[data-template-preview]').forEach(canvas=>{
    const preview=createSkinCanvas(canvas,{template:canvas.dataset.templatePreview});
    preview.setPixels(createTemplateSkin(canvas.dataset.templatePreview));
    preview.setView({yaw:-.3,pitch:.04,zoom:1.05,grid:false});
    templatePreviews.push(preview);
  });
  let manualModel=false;
  const updatePreviews=()=>{
    const options={model:$('#new-model').value,size:Number($('#new-size').value)};
    templatePreviews.forEach((preview,index)=>{
      preview.configure(options);preview.setPixels(createTemplateSkin(SKIN_TEMPLATES[index].id,options));
    });
    $('#resolution-note').textContent=options.size===128?'HD 纹理保留更多细节；Java 版账号皮肤需使用 64 × 64。':'标准分辨率，可用于 Java 版账号皮肤。';
  };
  $$('input[name="skin-template"]').forEach(input=>input.onchange=()=>{
    if(!manualModel) {
      const select=$('#new-model');
      select.value=SKIN_TEMPLATES.find(template=>template.id===input.value).model;
      select.parentElement.querySelector('.combo-face > span').textContent=select.selectedOptions[0].textContent;
    }
    updatePreviews();
  });
  $('#new-model').onchange=()=>{manualModel=true;updatePreviews();};
  $('#new-size').onchange=updatePreviews;
  $('#template-classic').focus();
}
function startDocument(templateId,options) {
  if(skinAiActive)clearTimeout(skinAiActive.timer);
  skinAiActive=null;skinAiQueue.length=0;updateAiActivity();
  const pixels=createTemplateSkin(templateId,options);
  finishGesture();
  configureDocument(options);
  initialTemplate=SKIN_TEMPLATES.find(template=>template.id===templateId);
  layers=[{id:1,name:'基础皮肤',pixels,visible:true,locked:false,opacity:100,base:true}];
  activeId=1;nextId=2;history=[];cursor=0;
  state.revision=1;state.part='all';
  $('#ai-result').replaceChildren();$('#ai-result').hidden=false;$('#ai-empty').hidden=false;
  updateAiActivity();
  aiImages=[];renderAiImages();
  $$('[data-part]').forEach(button=>button.setAttribute('aria-pressed','false'));
  $$('[data-scenario]').forEach(button=>button.setAttribute('aria-pressed',button.dataset.scenario==='manual'));
  $$('[data-camera]').forEach(button=>button.setAttribute('aria-pressed',button.dataset.camera==='angle'));
  clearSelection();setSurface('base');setViewMode('3d');
  viewTransforms['3d']={zoom:1,panX:0,panY:0};viewTransforms.uv={zoom:1,panX:0,panY:0};
  model.setView({part:'all',yaw:-.48,pitch:.1,zoom:1,panX:0,panY:0});
  uv.setView({part:'all',zoom:1,panX:0,panY:0});
  renderPixels();renderLayers();renderHistory();updateTarget();tab('edit');
  $('#save-status').textContent='新建皮肤 · 修改仅保留在本页';
  notify(`已从「${initialTemplate.name}」创建皮肤`);
}
function exportDialog(size=documentOptions.size) {
  closeFlyout(false);
  let png=model.exportPng();
  if(size!==documentOptions.size) {
    const source=document.createElement('canvas');source.width=source.height=documentOptions.size;
    source.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(compose()),documentOptions.size,documentOptions.size),0,0);
    const target=document.createElement('canvas');target.width=target.height=size;
    const ctx=target.getContext('2d');ctx.imageSmoothingEnabled=false;ctx.drawImage(source,0,0,size,size);png=target.toDataURL('image/png');
  }
  dialog('导出 Minecraft 皮肤',`<div class="export-preview"><img src="${png}" alt="当前合成皮肤贴图"><div><strong>未命名皮肤.png</strong><small>${dimensions(size)} · ${modelLabel(documentOptions.model)} · PNG</small></div></div><p>导出当前已接受的可见图层。PNG 不包含绘图图层与操作历史。</p>${size===128?'<p>128 × 128 为 HD 纹理；Java 版账号皮肤需使用 64 × 64。</p>':''}<p>这一步不会更改 Minecraft 账号皮肤。</p>`,[{text:'导出 PNG',primary:true,run:()=>{const link=document.createElement('a');link.href=png;link.download='未命名皮肤.png';link.click();notify('已请求下载 PNG；账号皮肤未更改。');}},{text:'取消'}]);
}
let skinAiActive=null;
const skinAiQueue=[];
function updateAiActivity() {
  $('#ai-new-chat').hidden=$('#ai-result').childElementCount===0;
  let dots=$('#skin-ai-responding');
  if(!dots){dots=document.createElement('div');dots.id='skin-ai-responding';dots.className='skin-response-dots';dots.setAttribute('aria-label','正在响应');dots.innerHTML='<i></i><i></i><i></i>';$('.ai-composer-area').prepend(dots);}
  dots.hidden=!skinAiActive;
  let stop=$('#skin-ai-stop');
  if(!stop){stop=document.createElement('button');stop.id='skin-ai-stop';stop.className='e-button';stop.textContent='■';stop.setAttribute('aria-label','停止');stop.onclick=()=>stopSkinAi();$('#generate-proposal').before(stop);}
  stop.hidden=!skinAiActive;
  $('#generate-proposal').setAttribute('aria-label',skinAiActive?'排队发送':'发送');
}
$('#ai-new-chat').onclick=()=>dialog('新会话', '<p>开启新会话将清除当前工程的 AI 对话，并停止正在生成的回答。已保存的皮肤、图层和编辑历史会保留。</p>',[
  {text:'开启新会话',run:()=>{
    if(skinAiActive)clearTimeout(skinAiActive.timer);
    skinAiActive=null;skinAiQueue.length=0;
    $('#ai-result').replaceChildren();$('#ai-result').hidden=false;$('#ai-empty').hidden=false;
    $('#ai-prompt').value='';aiImages=[];renderAiImages();updateAiActivity();
  }},
  {text:'取消',primary:true}
]);
function stopSkinAi() {
  if(!skinAiActive)return;
  clearTimeout(skinAiActive.timer);
  skinAiActive.node.querySelector('.ai-queue-actions').textContent='已停止';
  skinAiActive=null;updateAiActivity();
  if(skinAiQueue.length)startSkinAi(skinAiQueue.shift());
}
function startSkinAi(job) {
  skinAiActive=job;job.node.querySelector('.ai-queue-actions').replaceChildren();updateAiActivity();
  job.timer=setTimeout(()=>{if(skinAiActive!==job)return;createProposal(job.prompt,job.images,job.node);skinAiActive=null;updateAiActivity();if(skinAiQueue.length)startSkinAi(skinAiQueue.shift());},5000);
}
function enqueueSkinAi() {
  const prompt=$('#ai-prompt').value.trim();if(!prompt)return;
  const node=document.createElement('section');node.className='ai-turn';
  const user=document.createElement('p');user.className='ai-user';user.textContent=prompt;node.append(user);
  const actions=document.createElement('div');actions.className='ai-queue-actions';node.append(actions);
  const job={prompt,images:[...aiImages],node,timer:null};
  $('#ai-result').append(node);$('#ai-result').hidden=false;$('#ai-empty').hidden=true;
  $('#ai-prompt').value='';aiImages=[];renderAiImages();
  if(skinAiActive){
    actions.append('排队中 ');
    const now=document.createElement('button');now.className='e-button';now.textContent='立即发送';
    now.onclick=()=>{const at=skinAiQueue.indexOf(job);if(at<0)return;skinAiQueue.splice(at,1);skinAiQueue.unshift(job);stopSkinAi();};
    const cancel=document.createElement('button');cancel.className='e-button';cancel.textContent='取消排队';
    cancel.onclick=()=>{const at=skinAiQueue.indexOf(job);if(at<0)return;skinAiQueue.splice(at,1);actions.textContent='已取消排队';};
    actions.append(now,cancel);skinAiQueue.push(job);
  }else startSkinAi(job);
}
function createProposal(prompt=$('#ai-prompt').value,images=aiImages,target=null) {
  if(!prompt.trim()) {notify('请先描述想修改的地方。');$('#ai-prompt').focus();return;}
  const current=compose(),delta=new Uint8ClampedArray(current.length);
  const shirtColors=new Set(SKIN_TEMPLATES.flatMap(template=>template.shirtColors).map(color=>parseInt(color.slice(1),16)));
  let count=0;
  // Fixed local color study. It is intentionally not represented as model output.
  for(let y=0;y<documentOptions.size;y++)for(let x=0;x<documentOptions.size;x++) {
    const i=(y*documentOptions.size+x)*4,r=current[i],g=current[i+1],b=current[i+2];
    const isShirtColor=shirtColors.has((r<<16)|(g<<8)|b);
    if(!inSelection(x,y)||current[i+3]!==255||!(isShirtColor||(g>r*1.14&&g>b*1.12)))continue;
    if(!matchesContext(x,y))continue;
    const shade=.7+(r*.2126+g*.7152+b*.0722)/255*.4;
    const color=[181,112,79].map(value=>Math.min(255,Math.round(value*shade))).concat(255);
    delta.set(color,i);count++;
  }
  if(!count) {recordAiTurn(prompt,'当前皮肤没有可用于本地配色演示的上衣色。',['读取躯干、左右手臂的底层可见颜色，未找到本地示例需要的上衣色。'],images,target);return;}
  const before=snapshot();
  layers.push({id:nextId++,name:'AI · 陶土配色',pixels:delta,visible:true,locked:false,opacity:100,base:false});activeId=layers.at(-1).id;
  tab('ai');
  commit(before,'陶土配色','ai');
  recordAiTurn(prompt,'已将上衣调整为陶土配色。',[
    '读取躯干、左右手臂的当前表面与所选区域，保留肤色及其余身体部位。',
    '新建“AI · 陶土配色”独立绘图层，不修改背景和原绘图层。',
    `在新层填入 ${count} 个上衣配色像素，整批保存为一个“陶土配色”历史步骤。`,
  ],images,target);
}
function recordAiTurn(prompt,answer,activity,images=aiImages,target=null) {
  const turn=target||document.createElement('section');turn.className='ai-turn';turn.replaceChildren();
  const user=document.createElement('p');user.className='ai-user';user.textContent=prompt;turn.append(user);
  if(images.length)turn.append(referenceImages(images));
  const appendTools=items=>{
    if(!items.length)return;
    const tools=document.createElement('details'),header=document.createElement('summary'),steps=document.createElement('ol');
    tools.className='ai-tools';header.textContent=`工具调用 · ${items.length} 步`;
    items.forEach(text=>{const item=document.createElement('li');item.textContent=text;steps.append(item);});
    tools.append(header,steps);turn.append(tools);
  };
  appendTools(activity.slice(0,1));
  const thinking=document.createElement('details'),summary=document.createElement('summary'),reason=document.createElement('p');
  summary.textContent='思考（演示）';reason.textContent='使用本页固定配色规则，保留肤色及不相关部位；没有连接模型。';
  thinking.append(summary,reason);turn.append(thinking);
  appendTools(activity.slice(1));
  const response=document.createElement('p');response.textContent=answer;turn.append(response);
  $('#ai-empty').hidden=true;$('#ai-result').hidden=false;if(!target)$('#ai-result').append(turn);updateAiActivity();
  turn.scrollIntoView({block:'end'});
}
// Match the canonical atlas rectangles, without inventing permissions from them.
function matchesContext(x,y) {
  return documentFrames.some(frame=>['body','rightArm','leftArm'].includes(frame.part)&&frame.layer===state.surface
    &&(state.part==='all'||state.part===frame.part)&&x>=frame.x&&x<frame.x+frame.w&&y>=frame.y&&y<frame.y+frame.h);
}

document.addEventListener('click',event=>{
  const b=event.target.closest('button');if(!b)return;
  if(b.dataset.tool)chooseTool(b.dataset.tool);
  if(b.dataset.view)setViewMode(b.dataset.view);
  if(b.dataset.history!==undefined)goToHistory(Number(b.dataset.history));
  if(b.dataset.surface)setSurface(b.dataset.surface);
  if(b.dataset.tab)tab(b.dataset.tab);
  if(b.dataset.part) {state.part=state.part===b.dataset.part?'all':b.dataset.part;clearSelection();model.setView({part:state.part});uv.setView({part:state.part});$$('[data-part]').forEach(x=>x.setAttribute('aria-pressed',x.dataset.part===state.part));updateTarget();}
  if(b.dataset.theme) {document.documentElement.dataset.theme=b.dataset.theme;$$('[data-theme]').filter(x=>x.tagName==='BUTTON').forEach(x=>x.setAttribute('aria-pressed',x===b));model.render();uv.render();}
  if(b.dataset.camera) {const yaw={front:0,back:Math.PI,angle:-.48}[b.dataset.camera];model.setView({yaw,pitch:b.dataset.camera==='angle'?.1:0});$$('[data-camera]').forEach(x=>x.setAttribute('aria-pressed',x===b));}
  if(b.dataset.prompt) {$('#ai-prompt').value=b.dataset.prompt;$('#ai-prompt').focus();}
  if(b.dataset.scenario) {
    $$('[data-scenario]').forEach(x=>x.setAttribute('aria-pressed',x===b));
    if(b.dataset.scenario==='manual') tab('edit');
    else {state.part='all';setSurface('base');model.setView({part:'all'});uv.setView({part:'all'});$$('[data-part]').forEach(x=>x.setAttribute('aria-pressed','false'));$('#ai-prompt').value='给躯干和手臂尝试陶土配色，保留头部和腿部。';createProposal();}
  }
});
$('#undo').onclick=()=>undo();$('#redo').onclick=()=>undo(true);
$('#brush-size').oninput=event=>{state.size=Number(event.target.value);$('#brush-size-value').textContent=`${state.size} px`;updateToolSummary();};
$('#mirror-toggle').onclick=()=>{state.mirror=!state.mirror;$('#mirror-toggle').setAttribute('aria-pressed',state.mirror);updateCanvasCursor();};
$('#color-picker').oninput=event=>setColor(event.target.value);
$('#hex-color').onchange=event=>setColor(event.target.value);
$('#hex-color').oninput=event=>{
  const value=event.target.value;
  if(value.length===7 || !/^#?[0-9a-f]*$/i.test(value))setColor(value);
};
createColorPicker($('#rail-color'),{getColor:()=>state.color,onChange:setColor});
$('#grid-toggle').onclick=()=>{const on=$('#grid-toggle').getAttribute('aria-pressed')!=='true';$('#grid-toggle').setAttribute('aria-pressed',on);model.setView({grid:on});uv.setView({grid:on});};
$('#reset-camera').onclick=()=>{model.setView({yaw:-.48,pitch:.1,zoom:1,panX:0,panY:0});drawSelection();updateCanvasCursor();$$('[data-camera]').forEach(x=>x.setAttribute('aria-pressed',x.dataset.camera==='angle'));};
$('#uv-toggle').onclick=()=>toggleUv();$('#close-uv').onclick=()=>toggleUv(false);
$('#reset-uv').onclick=()=>{uv.setView({zoom:1,panX:0,panY:0});drawSelection();};
$('#show-all').onclick=()=>{state.part='all';model.setView({part:'all'});uv.setView({part:'all'});$$('[data-part]').forEach(b=>b.setAttribute('aria-pressed','false'));clearSelection();};
$('#outer-visible').onchange=event=>{
  if(!event.target.checked&&state.surface==='outer')setSurface('base');
  model.setView({outerVisible:event.target.checked});uv.setView({outerVisible:event.target.checked});
};
$('#clear-selection').onclick=clearSelection;
$('#add-layer').onclick=()=>{const before=snapshot();layers.push({id:nextId++,name:`绘图图层 ${nextId-2}`,pixels:new Uint8ClampedArray(documentOptions.size**2*4),visible:true,locked:false,opacity:100,base:false});activeId=layers.at(-1).id;commit(before,'添加绘图图层');};
$('#generate-proposal').onclick=enqueueSkinAi;
let aiImages=[];
function referenceImages(images,removable=false) {
  const list=document.createElement('div');list.className='ai-reference-images';
  images.forEach((image,index)=>{
    const row=document.createElement('div');row.className='ai-reference-image';
    const preview=document.createElement('img');preview.src=image.data;preview.alt=image.name;
    const name=document.createElement('span');name.textContent=image.name;name.title=image.name;
    row.append(preview,name);
    if(removable) {
      const remove=document.createElement('button');remove.className='inline-icon';remove.textContent='×';remove.setAttribute('aria-label','移除参考图');
      remove.onclick=()=>{aiImages.splice(index,1);renderAiImages();};row.append(remove);
    }
    list.append(row);
  });
  return list;
}
function renderAiImages() {
  const host=$('#ai-reference-images');host.replaceChildren(referenceImages(aiImages,true));host.hidden=!aiImages.length;
  $('#ai-add-images').disabled=aiImages.length===3;
}
async function addAiImages(files) {
  if(files.length+aiImages.length>3)throw new Error('参考图最多3张，请先移除一张再添加。');
  if(files.some(file=>!['image/png','image/jpeg','image/webp'].includes(file.type)))throw new Error('请选择 PNG、JPG 或 WebP 图片。');
  const images=await Promise.all(files.map(file=>new Promise((resolve,reject)=>{
    const reader=new FileReader();reader.onerror=()=>reject(new Error(`无法读取 ${file.name}`));
    reader.onload=()=>resolve({name:file.name,data:reader.result});reader.readAsDataURL(file);
  })));
  if(images.length+aiImages.length>3)throw new Error('参考图最多3张。');
  aiImages.push(...images);renderAiImages();
}
$('#ai-add-images').onclick=()=>$('#ai-reference-files').click();
$('#ai-reference-files').onchange=async event=>{
  try {await addAiImages(Array.from(event.target.files));}catch(error){notify(error.message);}finally{event.target.value='';}
};
$('#ai-prompt').addEventListener('paste',event=>{
  const files=Array.from(event.clipboardData.files);if(!files.length)return;
  event.preventDefault();addAiImages(files).catch(error=>notify(error.message));
});
$('#ai-prompt').addEventListener('keydown',event=>{
  if(event.key==='Enter'&&!event.shiftKey&&!event.isComposing&&event.keyCode!==229) {event.preventDefault();enqueueSkinAi();}
});
$('#shortcuts').onclick=()=>dialog('快捷键',`<table class="shortcut-table"><tr><td>画笔 / 橡皮 / 填充 / 取色</td><td>B / E / G / I</td></tr><tr><td>矩形选区</td><td>M</td></tr><tr><td>撤销 / 重做</td><td>Ctrl+Z / Ctrl+Shift+Z</td></tr><tr><td>导出 PNG</td><td>Ctrl+Shift+E</td></tr><tr><td>3D 旋转 / 平面平移</td><td>Alt + 拖动</td></tr><tr><td>缩放</td><td>滚轮</td></tr><tr><td>取消选区</td><td>Esc</td></tr></table><p style="margin-top:14px">点击历史步骤可前后回溯，新的像素或图层编辑才会替换后续步骤。</p>`);
$('#dialog-close').onclick=()=>$('#editor-dialog').close();
$('#export-64').onclick=()=>exportDialog(64);
$('#export-document').onclick=()=>{
  exportDialog();
};
$('#new-document').onclick=newDocumentDialog;
$('#save-project').onclick=()=>{
  closeFlyout();
  dialog('工程另存为', '<p>原生窗口在文件保存选择器中另存 .rmcs 工程，保留图层和现有撤销/重做历史，不改变当前工程；不包含 AI 会话。</p><p>此 HTML 只示意入口，不生成产品工程文件。</p>', [{text:'关闭'}]);
};
$('#import-document').onclick=()=>{closeFlyout();$('#import-file').click();};
$('#import-layer').onclick=()=>$('#layer-png-file').click();
$('#layer-png-file').onchange=async event=>{
  const file=event.target.files[0];event.target.value='';if(!file)return;
  const revision=state.revision;
  let bitmap;
  try {
    bitmap=await createImageBitmap(file);
    const size=documentOptions.size;
    if(bitmap.width!==size||bitmap.height!==size)throw new Error(`图层 PNG 必须为 ${size} × ${size}，与当前工程尺寸一致。`);
    if(revision!==state.revision)throw new Error('工程已更改，请重新导入图层。');
    finishGesture();const before=snapshot();
    const canvas=document.createElement('canvas');canvas.width=canvas.height=size;
    const ctx=canvas.getContext('2d');ctx.drawImage(bitmap,0,0);
    layers.push({id:nextId++,name:file.name.replace(/\.png$/i,'')||'披肩',pixels:ctx.getImageData(0,0,size,size).data,visible:true,locked:false,opacity:100,base:false});
    activeId=layers.at(-1).id;commit(before,'导入 PNG 图层');
  } catch(error) {notify(error.message);} finally {bitmap?.close();}
};
function exportLayer(id) {
  const layer=layers.find(item=>item.id===id);if(!layer)return;
  const size=documentOptions.size,canvas=document.createElement('canvas');canvas.width=canvas.height=size;
  canvas.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(layer.pixels),size,size),0,0);
  const link=document.createElement('a');link.href=canvas.toDataURL('image/png');link.download=`${layer.name}.png`;link.click();
}
$('#import-file').onchange=async event=>{
  const file=event.target.files[0];if(!file)return;
  try {
    const bitmap=await createImageBitmap(file);
    if(![64,128].includes(bitmap.width)||bitmap.height!==bitmap.width) {bitmap.close();dialog('请选择 64 × 64 或 128 × 128 PNG','<p>此原型尚未实现旧 64 × 32 皮肤转换。当前工程没有被替换。</p>');return;}
    const size=bitmap.width,canvas=document.createElement('canvas');canvas.width=canvas.height=size;const ctx=canvas.getContext('2d');ctx.drawImage(bitmap,0,0);bitmap.close();
    const rawPixels=ctx.getImageData(0,0,size,size).data;
    dialog('导入皮肤',`<div class="export-preview"><canvas id="import-preview" role="img" aria-label="导入皮肤模型预览"></canvas><div><strong>${esc(file.name)}</strong><small>${dimensions(size)} · PNG</small></div></div><div class="document-options import-options">${modelField('import-model',documentOptions.model)}</div><p>选择与图片对应的手臂模型。导入将替换本页绘图层，可一次撤销恢复；原文件不会改写。</p>${size===128?'<p>128 × 128 为 HD 纹理；Java 版账号皮肤需使用 64 × 64。</p>':''}`,[{text:'导入',primary:true,run:()=>{
      const options={model:$('#import-model').value,size},pixels=normalizeBaseAlpha(rawPixels,options);
      finishGesture();const before=snapshot();configureDocument(options);
      layers=[{id:nextId++,name:'导入的皮肤',pixels,visible:true,locked:false,opacity:100,base:true}];activeId=layers[0].id;
      clearSelection();commit(before,'导入 PNG');
    }},{text:'取消'}]);
    window.upgradeCombos($('#dialog-body'));
    const preview=createSkinCanvas($('#import-preview'),{model:documentOptions.model,size});
    templatePreviews.push(preview);
    const updatePreview=()=>{const options={model:$('#import-model').value,size};preview.configure(options);preview.setPixels(normalizeBaseAlpha(rawPixels,options));preview.setView({yaw:-.3,pitch:.04,grid:false});};
    $('#import-model').onchange=updatePreview;updatePreview();
  } catch {dialog('无法读取这张图片','<p>请使用有效的 PNG 文件。当前工程保留，可以选择其他文件重试。</p>');}
  finally {event.target.value='';}
};
$('#close-window').onclick=()=>dialog('关闭编辑器','<p>这里是独立窗口的设计原型。修改只在本页保留，离开前可以导出 PNG。</p><p>原生窗口将显示真实工程保存状态；保存失败时提供重试和保留副本。</p>',[{text:'继续编辑'},{text:'导出 PNG',primary:true,run:()=>{setTimeout(()=>$('#export-document').click());}}]);
$('.inspector-tabs').addEventListener('keydown',event=>{
  if(!['ArrowLeft','ArrowRight','Home','End'].includes(event.key))return;
  const tabs=['edit','ai','history'],index=tabs.indexOf(state.tab);
  const next=event.key==='Home'?0:event.key==='End'?2:(index+(event.key==='ArrowRight'?1:2))%3;
  event.preventDefault();tab(tabs[next]);$(`#tab-${tabs[next]}`).focus();
});
document.addEventListener('keydown',event=>{
  if(event.defaultPrevented||openFlyout||$('dialog[open]')||$('[data-color-flyout]:not([hidden])')||event.target.closest('input,textarea,[role="combobox"],[role="listbox"]'))return;
  if(event.key==='Alt'){event.preventDefault();if(!state.alt)finishGesture();state.alt=true;updateCanvasCursor();return;}
  if(event.key==='Control'){state.ctrl=true;updateCanvasCursor();return;}
  const key=event.key.toLowerCase();
  if(event.ctrlKey||event.metaKey) {
    if(key==='z') {event.preventDefault();undo(event.shiftKey);}
    if(key==='y') {event.preventDefault();undo(true);}
    if(key==='e'&&event.shiftKey) {event.preventDefault();$('#export-document').click();}
    if(key==='s'&&event.shiftKey) {event.preventDefault();$('#save-project').click();}
    return;
  }
  const tool={b:'brush',e:'eraser',g:'fill',i:'picker',m:'select'}[key];
  if(tool) {event.preventDefault();chooseTool(tool);}
  if(key==='escape')clearSelection();
});
document.addEventListener('keyup',event=>{if(event.key==='Alt'){event.preventDefault();state.alt=false;updateCanvasCursor();}if(event.key==='Control'){state.ctrl=false;updateCanvasCursor();}});
window.addEventListener('blur',()=>{state.alt=false;state.ctrl=false;hover=null;finishGesture();updateCanvasCursor();});
let viewResizeFrame=0;
const syncCanvasGeometry=()=>{
  cancelAnimationFrame(viewResizeFrame);
  viewResizeFrame=requestAnimationFrame(()=>{model.render();uv.render();drawSelection();updateCanvasCursor();});
};
const viewResizeObserver=new ResizeObserver(syncCanvasGeometry);
viewResizeObserver.observe($('#model-canvas'));viewResizeObserver.observe($('#uv-canvas'));
window.addEventListener('resize',syncCanvasGeometry);
const params=new URLSearchParams(location.search);
if(['light','dark','contrast'].includes(params.get('theme')))document.querySelector(`button[data-theme="${params.get('theme')}"]`).click();
renderLayers();renderHistory();renderPixels();updateTarget();
if(params.get('scenario')==='ai')document.querySelector('[data-scenario="ai"]').click();
const splitter=$('#sidebar-splitter'),editorBody=$('.editor-body');
function resizeSidebar(width) {
  const next=Math.max(240,Math.min(640,editorBody.clientWidth-370,width));
  editorBody.style.setProperty('--inspector-width',`${next}px`);
  splitter.setAttribute('aria-valuenow',Math.round(next));
}
splitter.addEventListener('pointerdown',event=>{
  const origin=event.clientX,width=$('.inspector').getBoundingClientRect().width;
  splitter.setPointerCapture(event.pointerId);
  const move=next=>resizeSidebar(width+origin-next.clientX);
  const finish=()=>{splitter.removeEventListener('pointermove',move);splitter.removeEventListener('pointerup',finish);splitter.removeEventListener('pointercancel',finish);};
  splitter.addEventListener('pointermove',move);splitter.addEventListener('pointerup',finish);splitter.addEventListener('pointercancel',finish);
});
splitter.addEventListener('keydown',event=>{
  if(!['ArrowLeft','ArrowRight'].includes(event.key))return;
  event.preventDefault();resizeSidebar($('.inspector').getBoundingClientRect().width+(event.key==='ArrowLeft'?16:-16));
});
