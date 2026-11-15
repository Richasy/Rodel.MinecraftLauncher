/* Browser-only design state; no real game or launcher data is accessed. */
function gameBridgeDesignGlobalEnabled() {
  return (reviewControlValues.get('settings:global:launch:gameBridgeEnabled') ?? preferenceFields.find(field => field.key === 'gameBridgeEnabled').value) === 'true';
}
function openGameBridgeDesign(instance, trigger) {
  closeInstanceMenu();
  const mode = new URLSearchParams(location.search).get('bridge') || 'local';
  const dialog = document.createElement('dialog');
  dialog.className = 'instance-dialog game-bridge-dialog';
  dialog.setAttribute('aria-labelledby', 'game-bridge-title');
  const status = mode === 'local' ? '本地世界 · 可读取和修改' : mode === 'remote' ? '服务器 · 只读' : '本次游戏未启用游戏桥。';
  dialog.innerHTML = `<header><div><h2 id="game-bridge-title">游戏桥</h2><div class="instance-dialog-context">${escapeHtml(instance.name)}</div></div></header>
    <div class="instance-dialog-body"><label class="bridge-enable">下次启动时启用<input type="checkbox" ${(instance.gameBridgeEnabled ?? gameBridgeDesignGlobalEnabled()) ? 'checked' : ''}></label>
      <section class="bridge-observation"><div>${status}</div>${mode === 'offline' ? '' : '<p>维度：minecraft:overworld<br>坐标：16.50，64.00，−8.50<br>生命值：20.0 · 时间：1000 tick</p>'}</section>
      ${mode === 'local' ? '<div class="bridge-edit"><label>修改本地世界<select data-bridge-kind><option value="time">世界时间（tick，0 或更大）</option><option value="health">玩家生命值（1–20）</option></select></label><input data-bridge-value value="1000" aria-label="修改值" inputmode="decimal"><button class="small-button dialog-primary" data-bridge-apply>应用本地修改</button></div>' : ''}
      <p data-bridge-receipt hidden aria-live="polite"></p></div><footer><button class="small-button subtle" data-bridge-close>关闭</button></footer>`;
  document.querySelector('.launcher').append(dialog);
  dialog.querySelector('.bridge-enable input').addEventListener('change', event => { instance.gameBridgeEnabled = event.target.checked; });
  const input = dialog.querySelector('[data-bridge-value]'), kind = dialog.querySelector('[data-bridge-kind]'), apply = dialog.querySelector('[data-bridge-apply]');
  const valid = () => { if (apply) { const n = Number(input.value); apply.disabled = !input.value.trim() || !Number.isFinite(n) || (kind.value === 'time' ? !Number.isSafeInteger(n) || n < 0 : n < 1 || n > 20); } };
  kind?.addEventListener('change', () => { input.value = kind.value === 'time' ? '1000' : '13'; valid(); });
  input?.addEventListener('input', valid);
  apply?.addEventListener('click', () => { const receipt = dialog.querySelector('[data-bridge-receipt]'); receipt.textContent = `已应用 · 回读值：${input.value}（设计模拟）`; receipt.hidden = false; });
  dialog.querySelector('[data-bridge-close]').addEventListener('click', () => dialog.close());
  dialog.addEventListener('close', () => { dialog.remove(); trigger?.focus(); }, { once: true });
  dialog.showModal();
}
if (new URLSearchParams(location.search).has('bridge')) requestAnimationFrame(() => openGameBridgeDesign(selected));
