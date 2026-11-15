/* Review-only pack maintenance. No files, providers, Java or game processes are accessed. */
function openPackMaintenanceReview(connect = null) {
  closeInstanceMenu(); closeCombo();
  const dialog = document.createElement('dialog');
  dialog.className = 'instance-dialog pack-maintenance-dialog';
  dialog.setAttribute('aria-labelledby', 'pack-maintenance-title');
  let reviewed = !!connect, current = 'one', target = 'two', source = true, confirmation = '';
  let hasOperation = false, detached = false;
  let applied = false;
  let noChanges = false;
  let large = false;
  function changesMarkup() {
    const changes = [
      { kind:'update', name:'Mod Menu', version:'11.0.4 → 11.0.5', path:'mods/modmenu-11.0.5.jar', mark:'~', label:'更新' },
      { kind:'add', name:'示例客户端模组', version:'1.0', path:'mods/example-client.jar', mark:'+', label:'新增' },
      { kind:'delete', name:'示例旧模组', version:'0.9', path:'mods/example-old.jar', mark:'−', label:'移除' },
      { kind:'files', name:'config/server-release.toml', version:'', path:'config/server-release.toml', mark:'~', label:'替换' },
    ];
    const rows = large ? Array.from({length:120}, (_, index) => ({...changes[index % changes.length], name:`${changes[index % changes.length].name} ${index + 1}`})) : changes;
    return `<p class="settings-note">模组更新 ${rows.filter(row=>row.kind==='update').length} · 新增 ${rows.filter(row=>row.kind==='add').length} · 移除 ${rows.filter(row=>row.kind==='delete').length} · 其他文件 ${rows.filter(row=>row.kind==='files').length}</p>
      ${rows.map(row=>`<div class="pack-change-line" data-change="${row.kind==='update'||row.kind==='files'?'replace':row.kind}"><strong aria-hidden="true">${row.mark}</strong><span><span dir="ltr">${escapeHtml(row.name)}</span>${row.version?`<small dir="ltr">${row.version}</small>`:''}</span><span class="pack-category-tag">${row.label}</span></div>`).join('')}`;
  }
  function close() { dialog.close(); dialog.remove(); restoreInstanceTrigger(); }
  function draw() {
    const footer = confirmation ? 'rollback' === confirmation ? '确认回退' : '确认解除分发源'
      : connect && applied ? '连接当前客户端' : reviewed ? '应用审核的更新' : source ? '检查分发更新' : '审核更新包';
    const form = confirmation ? `<section class="recovery-summary"><span class="recovery-icon">${vectorIcon('info')}</span><div><h3>${footer}</h3><p>${confirmation === 'rollback'
      ? '恢复当前事务之前的文件与运行配置，保留后续玩家修改。'
      : '停止检查当前分发源，保留客户端、存档及本地更新能力，不改变所属服务器。'}</p></div></section>`
      : `${!detached ? `<select aria-label="更新来源" data-maintenance-mode><option value="archive" ${source ? '' : 'selected'}>使用本地更新包</option><option value="source" ${source ? 'selected' : ''}>检查已保存的分发源</option></select>` : ''}
      ${source ? '<p class="settings-note" dir="ltr">https://distribution.example.net/client</p>'
        : '<label>更新包文件<div class="pack-path-input"><input data-maintenance-archive readonly placeholder="选择更新包"><button class="small-button subtle pack-path-browse" data-maintenance-browse aria-label="选择更新包">'+vectorIcon('folder')+'</button></div></label><label>本地补件目录<div class="pack-path-input"><input readonly placeholder="选择文件夹"><button class="small-button subtle pack-path-browse" data-maintenance-browse aria-label="选择补件目录">'+vectorIcon('folder')+'</button></div></label><label><input type="checkbox" checked> 联网下载可取得的文件</label>'}
      <label><input type="checkbox" checked> 同时准备游戏与 Java 运行环境</label>
      <p dir="ltr">${reviewed ? escapeHtml(current)+' → '+escapeHtml(target) : '当前版本：'+escapeHtml(current)}</p>
      ${reviewed ? changesMarkup() : ''}
      ${noChanges ? '<p class="settings-note">没有需要应用的更新。</p>' : ''}`;
    dialog.innerHTML = `<header><div><h2 id="pack-maintenance-title">整合包更新</h2><p class="settings-note">${escapeHtml(selected.name)}</p></div><div class="server-actions"><button class="small-button subtle" data-maintenance-report>操作详情</button><button class="pack-preparation-close" data-maintenance-more aria-label="整合包维护操作">${vectorIcon('more')}</button><button class="pack-preparation-close" data-maintenance-close aria-label="关闭">×</button></div></header>
      <div class="instance-dialog-body">${form}<aside class="server-review-tools">设计评审示意：不联网，不读取或更新真实客户端。<button class="text-button" data-maintenance-large>${large?'少量示例':'120项示例'}</button></aside></div>
      <footer><button class="small-button" data-maintenance-confirm>${footer}</button><button class="small-button subtle" data-maintenance-cancel>${confirmation ? '取消' : '关闭'}</button></footer>`;
    upgradeCombos(dialog);
  }
  dialog.addEventListener('change', event => {
    if (event.target.matches('[data-maintenance-mode]')) { source = event.target.value === 'source'; reviewed = false; noChanges = false; draw(); }
  });
  dialog.addEventListener('input', event => {
    if (event.target.matches('[data-maintenance-archive]')) reviewed = false;
  });
  dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
  dialog.addEventListener('click', event => {
    const button = event.target.closest('button'); if (!button) return;
    if (button.hasAttribute('data-maintenance-close')) close();
    else if (button.hasAttribute('data-maintenance-large')) { large = !large; reviewed = true; draw(); }
    else if (button.hasAttribute('data-maintenance-cancel')) { if (confirmation) { confirmation = ''; draw(); } else close(); }
    else if (button.hasAttribute('data-maintenance-browse')) showToast('设计示意：原生入口使用文件或目录选择器。');
    else if (button.hasAttribute('data-maintenance-report')) showToast(`设计示意：审核 ${current} → ${target}，完整操作和原始记录由原生详情提供。`);
    else if (button.hasAttribute('data-maintenance-more')) {
      const menu = document.createElement('div'); menu.className = 'server-menu'; menu.setAttribute('role', 'menu');
      menu.innerHTML = `${connect ? '<button role="menuitem" data-maintenance-connect>连接当前客户端</button>' : ''}<button role="menuitem" data-maintenance-rollback ${hasOperation ? '' : 'disabled'}>回退最近更新</button><button role="menuitem" data-maintenance-detach ${detached ? 'disabled' : ''}>解除分发源</button>`;
      dialog.append(menu); const bounds = button.getBoundingClientRect(), root = dialog.getBoundingClientRect();
      menu.style.left = `${bounds.left-root.left-menu.offsetWidth+32}px`; menu.style.top = `${bounds.bottom-root.top}px`;
    }
    else if (button.hasAttribute('data-maintenance-connect')) { close(); connect(); }
    else if (button.hasAttribute('data-maintenance-rollback') || button.hasAttribute('data-maintenance-detach')) {
      confirmation = button.hasAttribute('data-maintenance-rollback') ? 'rollback' : 'detach'; draw();
    }
    else if (button.hasAttribute('data-maintenance-confirm')) {
      if (confirmation === 'rollback') { current = 'one'; hasOperation = false; reviewed = false; applied = false; }
      else if (confirmation === 'detach') { detached = true; source = false; reviewed = false; applied = !!connect; }
      else if (connect && applied) { close(); connect(); return; }
      else if (reviewed) { current = target; reviewed = false; hasOperation = true; applied = true; }
      else if (source && current === target) { if (connect) { close(); connect(); return; } noChanges = true; }
      else { reviewed = true; noChanges = false; }
      confirmation = ''; draw();
    }
  });
  document.querySelector('.launcher').append(dialog); draw(); dialog.showModal();
}
