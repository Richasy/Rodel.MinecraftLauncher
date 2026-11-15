// Browser-only interaction reference. The native app uses Windows WIC, not this implementation.
export async function readPaletteReference(file) {
  const bitmap = await createImageBitmap(file);
  const source = document.createElement('canvas');
  source.width = bitmap.width; source.height = bitmap.height;
  const context = source.getContext('2d', {willReadFrequently: true});
  context.drawImage(bitmap, 0, 0); bitmap.close();
  const pixels = context.getImageData(0, 0, source.width, source.height).data;
  const histogram = new Map(), limit = 65536, samples = [];
  let seen = 0, randomState = 1;
  for (let offset = 0; offset < pixels.length; offset += 4) {
    if (!pixels[offset + 3]) continue;
    seen++;
    randomState = (Math.imul(randomState, 1664525) + 1013904223) >>> 0;
    const slot = seen <= limit ? seen - 1 : Math.floor(randomState / 4294967296 * seen);
    if (slot < limit) samples[slot] = [...pixels.slice(offset, offset + 3)];
  }
  for (const rgb of samples) {
    const key = rgb.join(','), entry = histogram.get(key);
    if (entry) entry.count++; else histogram.set(key, {rgb, count: 1});
  }
  const box = colors => {
    const ranges = [0, 1, 2].map(channel => {
      let minimum = 255, maximum = 0;
      for (const color of colors) { minimum = Math.min(minimum, color.rgb[channel]); maximum = Math.max(maximum, color.rgb[channel]); }
      return maximum - minimum;
    });
    const axis = ranges.indexOf(Math.max(...ranges));
    return {colors, axis, weight: colors.reduce((sum, color) => sum + color.count, 0), range: ranges[axis]};
  };
  const boxes = histogram.size ? [box([...histogram.values()])] : [];
  while (boxes.length < 16) {
    const split = boxes.filter(item => item.colors.length > 1).sort((a, b) => b.range * b.weight - a.range * a.weight)[0];
    if (!split) break;
    const sorted = split.colors.sort((a, b) => a.rgb[split.axis] - b.rgb[split.axis]);
    let position = 1, weight = sorted[0].count;
    while (position < sorted.length - 1 && weight < split.weight / 2) weight += sorted[position++].count;
    boxes.splice(boxes.indexOf(split), 1, box(sorted.slice(0, position)), box(sorted.slice(position)));
  }
  const hex = rgb => '#' + rgb.map(value => value.toString(16).padStart(2, '0')).join('').toUpperCase();
  const colors = boxes.map(item => {
    const mean = [0, 1, 2].map(channel => item.colors.reduce((sum, color) => sum + color.rgb[channel] * color.count, 0) / item.weight);
    const distance = color => color.rgb.reduce((sum, value, channel) => sum + (value - mean[channel]) ** 2, 0);
    return hex(item.colors.reduce((best, color) => distance(color) < distance(best) ? color : best).rgb);
  });
  return {name: file.name, source, pixels, colors: refinePalette([...new Set(colors)], samples)};
}

function refinePalette(colors, samples) {
  const oklab = rgb => {
    const [r, g, b] = rgb.map(component => {
      const value = component / 255;
      return value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4;
    });
    const l = Math.cbrt(.4122214708 * r + .5363325363 * g + .0514459929 * b);
    const m = Math.cbrt(.2119034982 * r + .6806995451 * g + .1073969566 * b);
    const s = Math.cbrt(.0883024619 * r + .2817188376 * g + .6299787005 * b);
    return [.2104542553 * l + .7936177850 * m - .0040720468 * s,
      1.9779984951 * l - 2.4285922050 * m + .4505937099 * s,
      .0259040371 * l + .7827717662 * m - .8086757660 * s];
  };
  const distance = (a, b) => a.reduce((sum, value, index) => sum + (value - b[index]) ** 2, 0);
  const candidates = colors.sort().map(value => {
    const lab = oklab([1, 3, 5].map(offset => parseInt(value.slice(offset, offset + 2), 16)));
    return {value, lab, population: 0, neutral: Math.hypot(lab[1], lab[2]) < .018, hue: (Math.atan2(lab[2], lab[1]) * 180 / Math.PI + 360) % 360};
  });
  if (!candidates.length) return [];
  for (const sample of samples) {
    const lab = oklab(sample);
    let best = candidates[0], nearest = Infinity;
    for (const candidate of candidates) {
      const next = distance(lab, candidate.lab);
      if (next < nearest) { best = candidate; nearest = next; }
    }
    best.population++;
  }
  const retained = [];
  for (const color of candidates.sort((a, b) => b.population - a.population || a.value.localeCompare(b.value))) {
    if (color.population && !retained.some(other => other.neutral === color.neutral && distance(other.lab, color.lab) < .05 ** 2)) retained.push(color);
  }
  const lightness = (a, b) => a.lab[0] - b.lab[0] || a.value.localeCompare(b.value);
  const result = retained.filter(color => color.neutral).sort(lightness), families = [];
  for (const color of retained.filter(color => !color.neutral).sort((a, b) => a.hue - b.hue || a.value.localeCompare(b.value))) {
    if (!families.length || color.hue - families.at(-1)[0].hue > 60) families.push([]);
    families.at(-1).push(color);
  }
  if (families.length > 1 && families[0].at(-1).hue + 360 - families.at(-1)[0].hue <= 60) families[0].push(...families.pop());
  for (const family of families) result.push(...family.sort(lightness));
  return result.map(color => color.value);
}

export function mountPaletteReference(parent, reference, add) {
  const section = document.createElement('section'); section.className = 'palette-reference';
  const tools = document.createElement('div'); tools.className = 'palette-reference-tools';
  const label = document.createElement('span'); label.textContent = reference.name; label.title = reference.name;
  tools.append(label);
  const canvas = document.createElement('canvas'); canvas.tabIndex = 0; canvas.setAttribute('aria-label', '参考图：缩放、平移和精确取色');
  const value = document.createElement('output'); value.className = 'palette-reference-value';
  const hint = document.createElement('p'); hint.className = 'palette-help';
  hint.textContent = '滚轮缩放，拖动平移。取色时单击加入颜色，Ctrl+拖动平移，方向键逐像素微调，Enter 取色，Esc 退出取色。';
  const paths = {pick: 'M14 3 L21 10 M16 5 L4 17 L3 22 L8 21 L20 9 M10 11 L14 15', minus: 'M5 12 H19', plus: 'M5 12 H19 M12 5 V19', reset: 'M20 10 A8 8 0 1 0 19 17 M20 4 V10 H14'};
  const command = (title, icon, run) => {
    const button = document.createElement('button'); button.type = 'button'; button.className = 'palette-icon';
    button.title = title; button.setAttribute('aria-label', title);
    button.innerHTML = `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${paths[icon]}"/></svg>`;
    button.onclick = run; tools.append(button); return button;
  };
  let zoom = 1, panX = 0, panY = 0, width = 560, height = 260;
  let picking = false, sample = null, pointer = null, gesture = null;
  const fit = () => Math.min(1, width / reference.source.width, height / reference.source.height);
  const scale = () => fit() * zoom;
  const left = () => (width - reference.source.width * scale()) / 2 + panX;
  const top = () => (height - reference.source.height * scale()) / 2 + panY;
  const clamp = () => {
    const x = Math.max(0, (reference.source.width * scale() - width) / 2), y = Math.max(0, (reference.source.height * scale() - height) / 2);
    panX = Math.max(-x, Math.min(x, panX)); panY = Math.max(-y, Math.min(y, panY));
  };
  const sampleAt = point => {
    pointer = point;
    const x = Math.floor((point.x - left()) / scale()), y = Math.floor((point.y - top()) / scale());
    sample = x >= 0 && y >= 0 && x < reference.source.width && y < reference.source.height ? {x, y} : null;
  };
  const sampleHex = () => {
    if (!sample) return '';
    const offset = (sample.y * reference.source.width + sample.x) * 4;
    return '#' + [...reference.pixels.slice(offset, offset + 4)].map(channel => channel.toString(16).padStart(2, '0')).join('').toUpperCase();
  };
  const setPicking = enabled => {
    picking = enabled; sample = null;
    pick.setAttribute('aria-pressed', String(enabled)); canvas.classList.toggle('picking', enabled);
    if (enabled) { canvas.focus(); sampleAt({x: width / 2, y: height / 2}); }
    draw();
  };
  const pick = command('从参考图取色', 'pick', () => setPicking(!picking)); pick.setAttribute('aria-pressed', 'false');
  const changeZoom = (factor, point = {x: width / 2, y: height / 2}) => {
    const before = zoom; zoom = Math.max(.25, Math.min(Math.max(32, 16 / fit()), zoom * factor));
    panX = point.x - width / 2 - (point.x - width / 2 - panX) * zoom / before;
    panY = point.y - height / 2 - (point.y - height / 2 - panY) * zoom / before;
    clamp(); if (picking) sampleAt(point); draw();
  };
  command('缩小', 'minus', () => changeZoom(.8));
  command('放大', 'plus', () => changeZoom(1.25));
  command('复位视图', 'reset', () => { zoom = 1; panX = panY = 0; sample = null; draw(); });
  section.append(tools, canvas, value, hint); parent.append(section);
  function draw() {
    const context = canvas.getContext('2d'), ratio = window.devicePixelRatio || 1;
    context.setTransform(ratio, 0, 0, ratio, 0, 0); context.clearRect(0, 0, width, height);
    context.imageSmoothingEnabled = scale() < 1;
    context.drawImage(reference.source, left(), top(), reference.source.width * scale(), reference.source.height * scale());
    value.textContent = picking ? sampleHex() : ''; value.hidden = !value.textContent;
    if (!picking || !sample || !pointer) return;
    const lx = Math.max(0, Math.min(width - 112, pointer.x + 136 <= width ? pointer.x + 24 : pointer.x - 136));
    const ly = Math.max(0, Math.min(height - 112, pointer.y - 136));
    context.fillStyle = '#222'; context.fillRect(lx, ly, 112, 112);
    for (let y = 0; y < 11; y++) for (let x = 0; x < 11; x++) {
      context.fillStyle = (x + y) % 2 ? '#aaa' : '#ddd'; context.fillRect(lx + 1 + x * 10, ly + 1 + y * 10, 10, 10);
    }
    context.imageSmoothingEnabled = false;
    context.drawImage(reference.source, sample.x - 5, sample.y - 5, 11, 11, lx + 1, ly + 1, 110, 110);
    context.strokeStyle = '#0005'; context.lineWidth = 1;
    for (let index = 0; index <= 11; index++) {
      context.beginPath(); context.moveTo(lx + 1 + index * 10, ly + 1); context.lineTo(lx + 1 + index * 10, ly + 111); context.stroke();
      context.beginPath(); context.moveTo(lx + 1, ly + 1 + index * 10); context.lineTo(lx + 111, ly + 1 + index * 10); context.stroke();
    }
    context.strokeStyle = '#000'; context.lineWidth = 3; context.strokeRect(lx + 51, ly + 51, 10, 10);
    context.strokeStyle = '#fff'; context.lineWidth = 1; context.strokeRect(lx + 51, ly + 51, 10, 10);
  }
  const point = event => { const bounds = canvas.getBoundingClientRect(); return {x: event.clientX - bounds.left, y: event.clientY - bounds.top}; };
  canvas.oncontextmenu = event => event.preventDefault();
  canvas.addEventListener('wheel', event => { event.preventDefault(); changeZoom(event.deltaY < 0 ? 1.25 : .8, point(event)); }, {passive: false});
  canvas.onpointerdown = event => {
    if (![0, 2].includes(event.button)) return;
    event.preventDefault(); canvas.focus(); canvas.setPointerCapture(event.pointerId);
    gesture = {pan: !picking || event.ctrlKey || event.button === 2, previous: point(event)};
    if (!gesture.pan) sampleAt(gesture.previous); draw();
  };
  canvas.onpointermove = event => {
    const position = point(event);
    if (gesture?.pan) { panX += position.x - gesture.previous.x; panY += position.y - gesture.previous.y; clamp(); gesture.previous = position; sample = null; }
    else if (picking) sampleAt(position);
    draw();
  };
  canvas.onpointerup = event => {
    if (!gesture) return;
    if (!gesture.pan && picking) { sampleAt(point(event)); if (sample) add(sampleHex()); setPicking(false); }
    gesture = null; canvas.releasePointerCapture(event.pointerId); draw();
  };
  canvas.onpointercancel = canvas.onlostpointercapture = () => { gesture = null; };
  canvas.onpointerleave = () => { if (!gesture) { sample = null; draw(); } };
  canvas.onkeydown = event => {
    if (!picking) return;
    if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); setPicking(false); return; }
    if (event.key === 'Enter' && sample) { event.preventDefault(); add(sampleHex()); setPicking(false); return; }
    const delta = {ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1]}[event.key];
    if (!delta) return;
    event.preventDefault(); sample ||= {x: Math.floor(reference.source.width / 2), y: Math.floor(reference.source.height / 2)};
    sample.x = Math.max(0, Math.min(reference.source.width - 1, sample.x + delta[0]));
    sample.y = Math.max(0, Math.min(reference.source.height - 1, sample.y + delta[1]));
    pointer = {x: left() + (sample.x + .5) * scale(), y: top() + (sample.y + .5) * scale()};
    if (pointer.x < 0 || pointer.x >= width || pointer.y < 0 || pointer.y >= height) {
      panX += width / 2 - pointer.x; panY += height / 2 - pointer.y; clamp();
      pointer = {x: left() + (sample.x + .5) * scale(), y: top() + (sample.y + .5) * scale()};
    }
    draw();
  };
  const resize = new ResizeObserver(() => {
    width = canvas.clientWidth; height = canvas.clientHeight;
    canvas.width = Math.round(width * (window.devicePixelRatio || 1)); canvas.height = Math.round(height * (window.devicePixelRatio || 1));
    clamp(); draw();
  });
  resize.observe(canvas);
  return () => resize.disconnect();
}
