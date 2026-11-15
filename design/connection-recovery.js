/* Design-only examples of original error text; no network, files or game control. */
iconPaths.info = 'M12 3 A9 9 0 1 0 12 21 A9 9 0 0 0 12 3 M12 7 V8 M12 11 V17';
iconPaths.error = 'M12 3 A9 9 0 1 0 12 21 A9 9 0 0 0 12 3 M9 9 L15 15 M15 9 L9 15';
const recoverySamples = {
  java: ['Incompatible mods found!', "Some of your mods are incompatible with the game or each other!\n确定了一种可能的解决方法，这样做可能会解决你的问题：\n - 将 'OpenJDK 64-Bit Server VM' (java) 17 替换为 21 及以上版本。\n更多信息：\n - 模组 'Fabric API' (fabric-api) 需要 Java 21 及以上版本，但当前为 17。"],
  content: ['Received unknown remote registry entries from server', 'Registry entry (fwaystones:waystone) is missing from local registry (minecraft:item)'],
  closed: ['Client disconnected with reason: Test server closed the connection', ''],
  unknown: ['尚未读取到错误原文', '当前客户端日志中没有可读取的明确错误记录。可查看完整记录；不会用推测的原因替代原文。']
};
function openRecoveryReview(server, kind = 'java') {
  if (document.querySelector('dialog[open]')) return;
  const sample = recoverySamples[kind];
  const dialog = document.createElement('dialog');
  dialog.className = 'server-dialog acrylic'; dialog.id = 'connection-recovery-dialog';
  dialog.innerHTML = `<header class="recovery-heading"><h2>错误信息</h2><button class="text-button" data-recovery-details>完整记录</button></header>
    <div class="server-dialog-body recovery-body">
    <section class="recovery-context"><small>诊断对象</small><strong>${escapeHtml(server?.name ?? '当前实例')} · 关联客户端</strong>
    ${server ? `<p dir="ltr">${escapeHtml(server.address)}</p>` : ''}</section>
    <section class="recovery-summary${kind === 'unknown' ? '' : ' critical'}">
    <span class="recovery-icon">${vectorIcon(kind === 'unknown' ? 'info' : 'error')}</span>
    <div><h3>${escapeHtml(sample[0])}</h3><p class="recovery-original">${escapeHtml(sample[1])}</p></div></section>
    <aside class="server-review-tools">设计示意 · 非真实游戏记录
    ${Object.keys(recoverySamples).map(key => `<button data-recovery-sample="${key}">${key}</button>`).join('')}</aside></div>
    <footer><button class="small-button subtle" data-recovery-close>关闭</button><button class="small-button primary" data-recovery-ai>AI 分析并修复</button></footer>`;
  document.querySelector('.launcher').append(dialog); dialog.showModal();
  const position = () => {
    const bounds = document.querySelector('.launcher').getBoundingClientRect();
    const size = dialog.getBoundingClientRect();
    dialog.style.left = `${Math.min(innerWidth - size.width / 2 - 24, Math.max(size.width / 2 + 24, bounds.left + bounds.width / 2))}px`;
    dialog.style.top = `${Math.min(innerHeight - size.height / 2 - 24, Math.max(size.height / 2 + 24, bounds.top + bounds.height / 2))}px`;
  };
  position(); window.addEventListener('resize', position);
  const close = () => { window.removeEventListener('resize', position); dialog.close(); dialog.remove(); document.querySelector('[data-home-recovery]')?.focus(); };
  dialog.querySelector('[data-recovery-close]').onclick = close;
  dialog.querySelector('[data-recovery-ai]').onclick = () => { close(); Assistant.openRepair(kind, sample.join('\n\n')); };
  dialog.querySelectorAll('[data-recovery-sample]').forEach(button => button.onclick = () => { close(); openRecoveryReview(server, button.dataset.recoverySample); });
  dialog.querySelector('[data-recovery-details]').onclick = () => {
    const body = dialog.querySelector('.server-dialog-body');
    const previous = [...body.childNodes];
    dialog.style.height = `${dialog.getBoundingClientRect().height}px`;
    const text = document.createElement('p');
    text.className = 'recovery-original';
    text.textContent = sample.join('\n\n');
    const back = document.createElement('button');
    back.className = 'small-button subtle';
    back.textContent = '返回错误信息';
    back.onclick = () => { body.replaceChildren(...previous); dialog.querySelector('[data-recovery-details]').focus(); };
    body.replaceChildren(text, back); back.focus();
  };
  dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
}
function renderRecoveryEntry() {
  const area = document.querySelector('#launch-area');
  if (!area || !homeServerMode || area.querySelector('[data-home-recovery]')) return;
  if (homeServerAttempts.get(homeServerId) === 'failed') {
    const feedback = area.querySelector('.home-server-feedback');
    if (feedback) {
      feedback.classList.add('recovery-home-error');
      feedback.innerHTML = `<strong>${escapeHtml(recoverySamples.java[0])}</strong>
        <button class="server-more" data-home-recovery aria-label="展开错误详情" title="展开错误详情">${vectorIcon('expand')}</button>
        <p>${escapeHtml(recoverySamples.java[1])}</p>`;
      return;
    }
  }
  area.querySelector('.launch-actions')?.insertAdjacentHTML('beforeend',
    `<button class="secondary-button surface" data-home-recovery aria-label="查看错误信息" title="查看错误信息">${vectorIcon('info')}</button>`);
}
document.addEventListener('click', event => {
  if (!event.target.closest('[data-home-recovery]')) return;
  openRecoveryReview(serverFavorites.find(item => item.id === homeServerId));
});
window.addEventListener('prototype-page-changed', renderRecoveryEntry);
const previousRenderHomeTargets = window.renderHomeTargets;
window.renderHomeTargets = function (...args) { previousRenderHomeTargets?.(...args); renderRecoveryEntry(); };
renderRecoveryEntry();
