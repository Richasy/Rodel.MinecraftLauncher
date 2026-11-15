/* HTML-only avatar draft/crop workflow. No account, skin or filesystem writes. */
(() => {
  const {resolve} = window.rodelAvatarAssets;
  const esc = value => String(value).replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
  const image = (avatar, alt = '') => `<img class="account-avatar-image${avatar?.kind === 'custom' ? ' custom' : ''}" src="${esc(resolve(avatar).src)}" alt="${esc(alt)}" draggable="false">`;
  const uploadIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><path d="M4 15v6h16v-6M12 16V3m-5 5 5-5 5 5"/></svg>';
  let active;
  function choose(account, complete, instance = false) {
    const assets = instance ? window.rodelInstanceIconAssets : window.rodelAvatarAssets;
    const {presets, resolve} = assets;
    const fallback = instance ? 'grass' : 'steve';
    const title = instance ? '更换图标' : '更换头像';
    const noun = instance ? '图标' : '头像';
    const image = value => instance ? window.rodelInstanceIconAssets.image(value) : window.rodelAvatarPicker.image(value);
    active?.cancel();
    const origin = document.activeElement;
    const original = account.avatar || {kind:'preset', id:fallback};
    let draft = {...original}, tab = original.kind === 'custom' ? 'custom' : 'presets';
    let bitmap, zoom = 1, panX = 0, panY = 0, drag, ticket = 0, busy = false, error = '', closed = false;
    const picker = document.createElement('dialog');
    picker.className = 'account-popup avatar-picker';
    picker.setAttribute('aria-labelledby', 'avatar-picker-title');
    picker.setAttribute('aria-modal', 'true');
    document.querySelector('.launcher').append(picker);
    function close(commit) {
      if(closed) return;
      closed = true; ticket++; bitmap?.close(); picker.close(); picker.remove(); active = null;
      complete(commit ? {...draft} : null);
      if(origin?.isConnected) origin.focus();
    }
    active = {cancel:() => close(false)};
    function same() { return JSON.stringify(draft) === JSON.stringify(original); }
    function updateSummary() {
      picker.querySelector('.avatar-summary-image').innerHTML = image(draft);
      picker.querySelector('.avatar-summary-copy span').textContent = draft.kind === 'preset' && draft.id === fallback ? resolve(draft).name + ' · 默认' : resolve(draft).name;
      picker.querySelector('.avatar-save').disabled = busy || same();
      picker.querySelector('.avatar-reset').hidden = draft.kind === 'preset' && draft.id === fallback;
      picker.querySelectorAll('[data-avatar-preset]').forEach(node => {
        const selected = draft.kind === 'preset' && node.dataset.avatarPreset === draft.id;
        node.setAttribute('aria-checked', String(selected));
        node.tabIndex = selected ? 0 : -1;
      });
      if(draft.kind !== 'preset') picker.querySelector('[data-avatar-preset]')?.setAttribute('tabindex','0');
    }
    function render(focusTab = false) {
      picker.innerHTML = `<header><h2 id="avatar-picker-title">${title}</h2></header>
        <div class="avatar-picker-body">
          <div class="avatar-summary"><div class="avatar-summary-image"></div><div class="avatar-summary-copy"><strong>${esc(account.name)}</strong><span></span></div><button class="avatar-reset" type="button">恢复默认</button></div>
          <div class="avatar-tabs instance-display-segmented" role="tablist" aria-label="${noun}来源"><button id="avatar-tab-presets" role="tab" aria-selected="${tab === 'presets'}" aria-controls="avatar-pane" tabindex="${tab === 'presets' ? 0 : -1}" data-avatar-tab="presets">预制${noun}</button><button id="avatar-tab-custom" role="tab" aria-selected="${tab === 'custom'}" aria-controls="avatar-pane" tabindex="${tab === 'custom' ? 0 : -1}" data-avatar-tab="custom">自定义图片</button></div>
          <div id="avatar-pane" role="tabpanel" aria-labelledby="avatar-tab-${tab}">
            ${tab === 'presets' ? `<div class="avatar-grid" role="radiogroup" aria-label="28 个预制${noun}">${presets.map(preset => `<button type="button" class="avatar-option" role="radio" data-avatar-preset="${preset.id}" aria-label="${esc(preset.name)}${preset.id === fallback ? '，默认' + noun : ''}" title="${esc(preset.name)}">${image({kind:'preset',id:preset.id})}</button>`).join('')}</div>` : `${bitmap ? '<div class="avatar-crop-frame"><canvas width="472" height="472" tabindex="0" aria-label="裁切图片，可拖动或使用方向键移动图片"></canvas></div><div class="avatar-crop-controls"><label for="avatar-zoom">缩放</label><input id="avatar-zoom" type="range" min="1" max="4" step="0.01" value="'+zoom+'"><button type="button" data-avatar-file>重新选择</button></div>' : draft.kind === 'custom' ? `<div class="avatar-crop-frame">${image(draft)}</div><div class="avatar-crop-controls"><button type="button" data-avatar-file>选择其他图片</button></div>` : `<button class="avatar-upload" type="button" data-avatar-file>${uploadIcon}<strong>选择图片或拖到这里</strong><small>PNG、JPG、WebP</small></button>`}`}
          </div>
          <p class="avatar-error" role="alert" ${error ? '' : 'hidden'}>${esc(error)}</p><div class="avatar-processing" role="status" ${busy ? '' : 'hidden'}>正在读取图片…</div>
          ${instance ? '' : '<div class="avatar-scope">仅更改启动器头像，不更改游戏皮肤。</div>'}
          <input type="file" accept="image/png,image/jpeg,image/webp" hidden data-avatar-input>
        </div>
        <footer><button class="small-button account-primary avatar-save" data-avatar-save>保存</button><button class="small-button subtle" data-avatar-cancel>取消</button></footer>`;
      updateSummary();
      if(bitmap && tab === 'custom') draw();
      if(focusTab) picker.querySelector(`[data-avatar-tab="${tab}"]`).focus();
    }
    function draw() {
      const canvas = picker.querySelector('canvas');
      if(!canvas || !bitmap) return;
      const side = canvas.width;
      const scale = Math.max(side / bitmap.width, side / bitmap.height) * zoom;
      const width = bitmap.width * scale, height = bitmap.height * scale;
      panX = Math.max(-(width - side)/2, Math.min((width-side)/2, panX));
      panY = Math.max(-(height-side)/2, Math.min((height-side)/2, panY));
      const context = canvas.getContext('2d');
      context.clearRect(0,0,side,side);
      context.drawImage(bitmap,(side-width)/2+panX,(side-height)/2+panY,width,height);
      draft = {kind:'custom',src:canvas.toDataURL('image/png')};
      updateSummary();
    }
    async function load(file) {
      if(!file) return;
      const thisTicket = ++ticket;
      if(!['image/png','image/jpeg','image/webp'].includes(file.type)) {
        busy = false;error = '请选择 PNG、JPG 或 WebP 图片。'; render(); return;
      }
      busy = true; error = ''; render();
      try {
        const next = await createImageBitmap(file);
        if(closed || ticket !== thisTicket) { next.close(); return; }
        bitmap?.close(); bitmap = next; zoom = 1; panX = 0; panY = 0;
      } catch {
        if(!closed && ticket === thisTicket) error = '无法读取这张图片，请选择其他图片。';
      } finally {
        if(!closed && ticket === thisTicket) { busy = false; render(); }
      }
    }
    picker.addEventListener('cancel',event => { event.preventDefault();close(false); });
    picker.addEventListener('click',event => {
      const control = event.target.closest('button');
      if(!control) return;
      if(control.hasAttribute('data-avatar-cancel')) { close(false);return; }
      if(control.hasAttribute('data-avatar-save')) { if(!busy && !same()) close(true);return; }
      if(control.matches('.avatar-reset')) { draft = {kind:'preset',id:fallback};tab = 'presets';error = '';render();return; }
      if(control.dataset.avatarPreset) { draft = {kind:'preset',id:control.dataset.avatarPreset};error = '';updateSummary();return; }
      if(control.dataset.avatarTab) { tab = control.dataset.avatarTab;error = '';render(true);return; }
      if(control.hasAttribute('data-avatar-file')) picker.querySelector('[data-avatar-input]').click();
    });
    picker.addEventListener('change',event => { if(event.target.matches('[data-avatar-input]')) load(event.target.files[0]); });
    picker.addEventListener('input',event => { if(event.target.id === 'avatar-zoom') { zoom = +event.target.value;draw(); } });
    picker.addEventListener('dragover',event => { if(tab !== 'custom') return;event.preventDefault();picker.querySelector('.avatar-upload')?.classList.add('drag-over'); });
    picker.addEventListener('dragleave',event => { if(!picker.contains(event.relatedTarget)) picker.querySelector('.avatar-upload')?.classList.remove('drag-over'); });
    picker.addEventListener('drop',event => { event.preventDefault();if(tab === 'custom') load(event.dataTransfer.files[0]); });
    picker.addEventListener('pointerdown',event => {
      if(event.target.tagName !== 'CANVAS') return;
      drag = {x:event.clientX,y:event.clientY,panX,panY};event.target.setPointerCapture(event.pointerId);
    });
    picker.addEventListener('pointermove',event => {
      if(!drag || event.target.tagName !== 'CANVAS') return;
      const factor = event.target.width/event.target.getBoundingClientRect().width;
      panX = drag.panX + (event.clientX-drag.x)*factor;panY = drag.panY + (event.clientY-drag.y)*factor;draw();
    });
    picker.addEventListener('pointerup',() => {drag = null;});
    picker.addEventListener('pointercancel',() => {drag = null;});
    picker.addEventListener('keydown',event => {
      const option = event.target.closest('[data-avatar-preset]');
      if(option && ['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End'].includes(event.key)) {
        event.preventDefault();event.stopPropagation();
        const index = presets.findIndex(item => item.id === option.dataset.avatarPreset);
        const offset = {ArrowLeft:-1,ArrowRight:1,ArrowUp:-7,ArrowDown:7}[event.key] || 0;
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? 27 : Math.max(0,Math.min(27,index+offset));
        draft = {kind:'preset',id:presets[next].id};updateSummary();picker.querySelector(`[data-avatar-preset="${draft.id}"]`).focus();
      }
      if(event.target.matches('[data-avatar-tab]') && ['ArrowLeft','ArrowRight','Home','End'].includes(event.key)) {
        event.preventDefault();tab = event.key === 'Home' ? 'presets' : event.key === 'End' ? 'custom' : tab === 'presets' ? 'custom' : 'presets';render(true);
      }
      if(event.target.tagName === 'CANVAS' && ['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key)) {
        event.preventDefault();panX += {ArrowLeft:-8,ArrowRight:8}[event.key] || 0;panY += {ArrowUp:-8,ArrowDown:8}[event.key] || 0;draw();
      }
    });
    render();picker.showModal();picker.querySelector(`[data-avatar-tab="${tab}"]`).focus();
  }
  window.rodelAvatarPicker = {image,choose, chooseInstance:(instance, complete) => choose({name:instance.name,avatar:window.rodelInstanceIconAssets.choice(instance.icon)},complete,true)};
})();
