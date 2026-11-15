let pickerSerial = 0;
const clamp = (value, min, max) => Math.max(min, Math.min(max, value));

function fromHex(hex) {
  const rgb = [1, 3, 5].map(index => parseInt(hex.slice(index, index + 2), 16) / 255);
  const max = Math.max(...rgb), min = Math.min(...rgb), delta = max - min;
  let hue = 0;
  if (delta) {
    const [red, green, blue] = rgb;
    hue = max === red ? ((green - blue) / delta) % 6 : max === green ? (blue - red) / delta + 2 : (red - green) / delta + 4;
    hue = (hue * 60 + 360) % 360;
  }
  return {h: hue, s: max ? delta / max : 0, v: max};
}

function toHex({h, s, v}) {
  const chroma = v * s, x = chroma * (1 - Math.abs((h / 60) % 2 - 1)), m = v - chroma;
  const rgb = h < 60 ? [chroma, x, 0] : h < 120 ? [x, chroma, 0] : h < 180 ? [0, chroma, x]
    : h < 240 ? [0, x, chroma] : h < 300 ? [x, 0, chroma] : [chroma, 0, x];
  return `#${rgb.map(value => Math.round((value + m) * 255).toString(16).padStart(2, '0')).join('').toUpperCase()}`;
}

/** Open a theme-aware, keyboard-accessible HSV flyout beside the foreground button. */
export function createColorPicker(button, {getColor, onChange}) {
  const id = ++pickerSerial;
  let hsv = fromHex('#638B4D'), alpha = '', dragging = false;
  const popup = document.createElement('section');
  popup.className = 'foreground-picker'; popup.dataset.colorFlyout = '';
  popup.id = `foreground-picker-${id}`; popup.hidden = true; popup.tabIndex = -1;
  popup.setAttribute('role', 'dialog'); popup.setAttribute('aria-labelledby', `foreground-picker-title-${id}`);
  popup.innerHTML = `<div class="foreground-picker-heading"><h2 id="foreground-picker-title-${id}">前景色</h2><button type="button" class="foreground-picker-close" aria-label="关闭颜色选择器"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 5l14 14 M19 5 5 19"/></svg></button></div>
    <div class="foreground-sv" role="group" aria-roledescription="二维颜色选择器" aria-label="饱和度和明度" aria-describedby="foreground-sv-hint-${id}" tabindex="0"><span class="foreground-sv-thumb" aria-hidden="true"></span></div>
    <p class="foreground-picker-hint" id="foreground-sv-hint-${id}">左右调整饱和度，上下调整明度</p>
    <div class="foreground-hue-row"><label for="foreground-hue-${id}">色相</label><input id="foreground-hue-${id}" type="range" min="0" max="359" step="1" value="90"><output class="foreground-hue-value" for="foreground-hue-${id}">90°</output></div>
    <div class="foreground-hex-row"><span class="foreground-preview" aria-hidden="true"></span><label for="foreground-hex-${id}">HEX</label><input id="foreground-hex-${id}" type="text" maxlength="9" autocomplete="off" spellcheck="false" aria-describedby="foreground-picker-error-${id}"></div>
    <p class="foreground-picker-error" id="foreground-picker-error-${id}" role="status" hidden>请输入 6 位 RGB 或 8 位 RGBA HEX，例如 #638B4DFF。</p>`;
  document.body.append(popup);
  const sv = popup.querySelector('.foreground-sv'), thumb = popup.querySelector('.foreground-sv-thumb');
  const hue = popup.querySelector('input[type="range"]'), hex = popup.querySelector('input[type="text"]');
  const error = popup.querySelector('.foreground-picker-error');
  button.setAttribute('aria-haspopup', 'dialog'); button.setAttribute('aria-expanded', 'false'); button.setAttribute('aria-controls', popup.id);
  function position() {
    if (popup.hidden) return;
    const anchor = button.getBoundingClientRect(), width = popup.offsetWidth, height = popup.offsetHeight;
    const right = anchor.right + 12;
    const x = right + width <= innerWidth - 12 ? right : anchor.left - width - 12;
    popup.style.left = `${clamp(x, 12, Math.max(12, innerWidth - width - 12))}px`;
    popup.style.top = `${clamp(anchor.bottom - height, 12, Math.max(12, innerHeight - height - 12))}px`;
  }
  function render(emit = false) {
    const color = toHex(hsv) + alpha, saturation = Math.round(hsv.s * 100), brightness = Math.round(hsv.v * 100);
    sv.style.setProperty('--picker-hue', `hsl(${hsv.h} 100% 50%)`);
    thumb.style.left = `${hsv.s * 100}%`; thumb.style.top = `${(1 - hsv.v) * 100}%`;
    thumb.style.backgroundColor = color;
    sv.setAttribute('aria-label', `饱和度 ${saturation}%，明度 ${brightness}%`);
    const hueDegrees = Math.round(hsv.h) % 360;
    hue.value = String(hueDegrees); hue.setAttribute('aria-valuetext', `${hueDegrees} 度`);
    popup.querySelector('.foreground-hue-value').textContent = `${hueDegrees}°`;
    popup.querySelector('.foreground-preview').style.backgroundColor = color;
    hex.value = color; hex.removeAttribute('aria-invalid'); error.hidden = true;
    if (emit) onChange(color);
    position();
  }
  function open() {
    const color = getColor();
    if (/^#[0-9a-f]{6}([0-9a-f]{2})?$/i.test(color)) {
      hsv = fromHex(color); alpha = color.length === 9 ? color.slice(7).toUpperCase() : '';
    }
    popup.hidden = false; button.setAttribute('aria-expanded', 'true'); render(); sv.focus();
  }
  function close(restoreFocus = true) {
    if (popup.hidden) return;
    popup.hidden = true; dragging = false; button.setAttribute('aria-expanded', 'false');
    if (restoreFocus && button.isConnected) button.focus();
  }
  function sample(event) {
    const bounds = sv.getBoundingClientRect();
    hsv.s = clamp((event.clientX - bounds.left) / bounds.width, 0, 1);
    hsv.v = 1 - clamp((event.clientY - bounds.top) / bounds.height, 0, 1);
    render(true);
  }
  button.addEventListener('click', () => popup.hidden ? open() : close());
  popup.querySelector('.foreground-picker-close').addEventListener('click', () => close());
  sv.addEventListener('pointerdown', event => {
    if (event.button !== 0) return;
    event.preventDefault(); sv.focus(); dragging = true; sv.setPointerCapture(event.pointerId); sample(event);
  });
  sv.addEventListener('pointermove', event => { if (dragging) sample(event); });
  sv.addEventListener('pointerup', () => { dragging = false; });
  sv.addEventListener('pointercancel', () => { dragging = false; });
  sv.addEventListener('lostpointercapture', () => { dragging = false; });
  sv.addEventListener('keydown', event => {
    if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault(); const step = event.shiftKey ? .1 : .01;
    if (event.key === 'ArrowLeft') hsv.s -= step;
    if (event.key === 'ArrowRight') hsv.s += step;
    if (event.key === 'ArrowUp') hsv.v += step;
    if (event.key === 'ArrowDown') hsv.v -= step;
    hsv.s = clamp(hsv.s, 0, 1); hsv.v = clamp(hsv.v, 0, 1); render(true);
  });
  hue.addEventListener('input', () => { hsv.h = Number(hue.value); render(true); });
  hex.addEventListener('input', () => {
    const value = hex.value.trim();
    if (!/^#?[0-9a-f]{6}([0-9a-f]{2})?$/i.test(value)) { hex.setAttribute('aria-invalid', 'true'); error.hidden = false; position(); return; }
    const normalized = value.startsWith('#') ? value : `#${value}`;
    hsv = fromHex(normalized); alpha = normalized.length === 9 ? normalized.slice(7).toUpperCase() : ''; render(true);
  });
  popup.addEventListener('keydown', event => {
    if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); close(); }
    if (event.key === 'Tab') {
      const focusable = [...popup.querySelectorAll('button,input,[tabindex="0"]')];
      const first = focusable[0], last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    }
  });
  document.addEventListener('pointerdown', event => {
    if (!popup.hidden && !popup.contains(event.target) && !button.contains(event.target)) close();
  });
  window.addEventListener('resize', position);
  window.addEventListener('blur', () => close(false));
  document.addEventListener('visibilitychange', () => { if (document.hidden) close(false); });
  return {close};
}
