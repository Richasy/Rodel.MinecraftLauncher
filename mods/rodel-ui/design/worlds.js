const $ = selector => document.querySelector(selector);
const viewport = $('#viewport');
const dialog = $('#world-dialog');
const presets = {
  'minecraft:normal': ['默认', '原版的生物群系与自然地形。'],
  'minecraft:flat': ['超平坦', '由平坦的方块层组成，可自定义层与预设。'],
  'minecraft:flat_all_dimensions': ['全维度超平坦', '在各维度使用超平坦地形。'],
  'minecraft:large_biomes': ['巨型生物群系', '使用范围更大的生物群系。'],
  'minecraft:amplified': ['放大化', '生成高耸的山峰与起伏剧烈的地形。'],
  'minecraft:single_biome_surface': ['单一生物群系', '使用指定的单一生物群系生成世界。'],
};
const modes = { survival: '生存', creative: '创造', hardcore: '极限' };
const difficulties = { peaceful: '和平', easy: '简单', normal: '普通', hard: '困难' };
const modeDescriptions = {
  survival: '收集资源，建造并探索世界。',
  creative: '无限资源，自由飞行与建造。',
  hardcore: '只有一次生命，死亡后无法再次以玩家身份重生。',
};
let size = [1280, 720];
let worlds = [];
let selectedId = null;
let emptyReview = false;
let dataLoaded = false;
let page = new URL(location.href).searchParams.get('page') === 'create' ? 'create' : 'select';
let commandsOverridden = false;
let paused = matchMedia('(prefers-reduced-motion: reduce)').matches;
const requestedTheme = new URL(location.href).searchParams.get('theme');
let activeTheme = ['light', 'dark', 'contrast'].includes(requestedTheme) ? requestedTheme : 'dark';
let loadPanorama = null;
const preview = window.worldsDesign = { ready: false, backgroundReady: false, dataReady: false };
document.querySelectorAll('svg').forEach(svg => svg.setAttribute('aria-hidden', 'true'));

function message(text) {
  $('#review-message').textContent = text;
}

document.addEventListener('pointerdown', () => { document.documentElement.dataset.input = 'pointer'; }, true);
document.addEventListener('keydown', event => {
  if (!event.metaKey && !event.ctrlKey && !event.altKey) document.documentElement.dataset.input = 'keyboard';
}, true);

function fit() {
  const stage = $('.review-stage');
  const style = getComputedStyle(stage);
  const availableWidth = stage.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
  const availableHeight = Math.max(200, innerHeight - $('.review-toolbar').offsetHeight - $('.review-notes').offsetHeight - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom));
  const scale = Math.max(.1, Math.min(availableWidth / size[0], availableHeight / size[1], 1.5));
  $('#frame').style.width = `${size[0] * scale}px`;
  $('#frame').style.height = `${size[1] * scale}px`;
  viewport.style.width = `${size[0]}px`;
  viewport.style.height = `${size[1]}px`;
  viewport.style.transform = `scale(${scale})`;
}
$('#review-size').onchange = event => {
  size = event.target.value.split(',').map(Number);
  fit();
};
new ResizeObserver(fit).observe($('.review-toolbar'));
new ResizeObserver(fit).observe($('.review-notes'));
window.addEventListener('resize', fit);
fit();

function setPage(next, focus = false) {
  page = next;
  $('#select-page').hidden = next !== 'select';
  $('#create-page').hidden = next !== 'create';
  $('[data-review-page="select"]').setAttribute('aria-pressed', String(next === 'select'));
  $('[data-review-page="create"]').setAttribute('aria-pressed', String(next === 'create'));
  $('#review-empty').disabled = next !== 'select' || !dataLoaded;
  const url = new URL(location.href);
  url.searchParams.set('page', next);
  history.replaceState(null, '', url);
  if (focus) (next === 'create' ? $('#world-name') : $('#open-create').hidden ? $('#empty-create') : $('#open-create')).focus();
}
document.querySelectorAll('[data-review-page]').forEach(button => {
  button.onclick = () => setPage(button.dataset.reviewPage);
});
$('#open-create').onclick = $('#empty-create').onclick = () => setPage('create', true);
$('#create-back').onclick = () => setPage('select', true);
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && !dialog.open && page === 'create' && event.target.tagName !== 'SELECT') {
    event.preventDefault();
    setPage('select', true);
  }
});
setPage(page);

function setTheme(theme) {
  activeTheme = theme;
  viewport.dataset.theme = theme;
  $('#select-page .page-header a').href = `index.html?theme=${theme === 'light' ? 'light' : 'dark'}`;
  document.querySelectorAll('[data-theme]').forEach(button => {
    if (button.tagName === 'BUTTON') button.setAttribute('aria-pressed', String(button.dataset.theme === theme));
  });
  const url = new URL(location.href);
  url.searchParams.set('theme', theme);
  history.replaceState(null, '', url);
  if (loadPanorama && theme !== 'contrast') loadPanorama(theme);
}
document.querySelectorAll('button[data-theme]').forEach(button => { button.onclick = () => setTheme(button.dataset.theme); });
setTheme(activeTheme);
function updateMotion() {
  $('#review-motion').textContent = paused ? '继续全景' : '暂停全景';
  $('#review-motion').setAttribute('aria-pressed', String(paused));
}
$('#review-motion').onclick = () => { paused = !paused; updateMotion(); };
updateMotion();
matchMedia('(prefers-reduced-motion: reduce)').addEventListener('change', event => {
  paused = event.matches;
  updateMotion();
});

function icon(name) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  const use = document.createElementNS(svg.namespaceURI, 'use');
  use.setAttribute('href', `#i-${name}`);
  svg.setAttribute('aria-hidden', 'true');
  svg.append(use);
  return svg;
}
function paragraph(text) {
  const element = document.createElement('p');
  element.textContent = text;
  return element;
}
function openDialog(title, body, actions) {
  $('#dialog-title').textContent = title;
  $('#dialog-body').replaceChildren(...body);
  $('#dialog-actions').replaceChildren(...actions.map(({ text, run, critical, disabled }) => {
    const button = document.createElement('button');
    button.className = `button${critical ? ' critical' : ''}`;
    button.textContent = text;
    button.disabled = !!disabled;
    button.onclick = run || (() => dialog.close());
    return button;
  }));
  dialog.showModal();
}
function nativeEntry(title, detail) {
  openDialog(title, [
    paragraph(detail),
    paragraph('本轮只评审世界选择与新建主页面；此入口后续衔接原版子页面，HTML 不执行游戏或文件操作。'),
  ], [{ text: '返回' }]);
}

function worldMode(world) {
  return world.hardcore ? '极限模式' : ({ 0: '生存模式', 1: '创造模式', 2: '冒险模式', 3: '旁观模式' }[world.gameType] || '未知模式');
}
function filteredWorlds() {
  if (emptyReview) return [];
  const query = $('#world-search').value.trim().toLocaleLowerCase();
  return worlds.filter(world => `${world.name}\n${world.folder}`.toLocaleLowerCase().includes(query));
}
function selectedWorld() {
  return worlds.find(world => world.id === selectedId);
}
function updateActions() {
  const enabled = !!selectedWorld() && filteredWorlds().some(world => world.id === selectedId);
  for (const id of ['edit-world', 'recreate-world', 'delete-world', 'play-world']) $(`#${id}`).disabled = !enabled;
}
function selectWorld(id, focus = false) {
  selectedId = id;
  const rows = [...$('#world-list').children];
  rows.forEach(row => {
    const selected = row.dataset.world === id;
    row.setAttribute('aria-selected', String(selected));
    row.tabIndex = selected ? 0 : -1;
    if (selected && focus) {
      row.focus({ preventScroll: true });
      row.scrollIntoView({ block: 'nearest' });
    }
  });
  updateActions();
}
function worldPlaceholder() {
  const placeholder = document.createElement('div');
  placeholder.className = 'world-icon world-icon-placeholder';
  placeholder.setAttribute('aria-hidden', 'true');
  placeholder.append(icon('world'));
  return placeholder;
}
function renderWorlds() {
  const list = $('#world-list');
  const visible = filteredWorlds();
  if (!visible.some(world => world.id === selectedId)) selectedId = visible[0]?.id ?? null;
  list.replaceChildren(...visible.map(world => {
    const row = document.createElement('div');
    row.className = 'world-row';
    row.dataset.world = world.id;
    row.setAttribute('role', 'option');
    row.setAttribute('aria-selected', String(world.id === selectedId));
    row.tabIndex = world.id === selectedId ? 0 : -1;
    let image = worldPlaceholder();
    if (world.icon) {
      image = document.createElement('img');
      image.className = 'world-icon';
      image.src = world.icon;
      image.alt = '';
      image.width = image.height = 64;
      image.onerror = () => {
        image.replaceWith(worldPlaceholder());
        console.error(`World icon could not be loaded: ${world.id}`);
        message('有一张世界缩略图未能读取，已明确显示通用图标；存档未被修改。');
      };
    }
    const copy = document.createElement('div');
    copy.className = 'world-copy';
    const name = document.createElement('strong');
    name.className = 'world-name';
    name.textContent = world.name;
    name.onmouseenter = () => {
      if (name.scrollWidth > name.clientWidth) name.title = world.name;
      else name.removeAttribute('title');
    };
    const metadata = document.createElement('div');
    metadata.className = 'world-metadata';
    [worldMode(world), world.version, ...(world.commands ? ['命令已启用'] : [])].forEach(text => {
      const span = document.createElement('span');
      span.textContent = text;
      metadata.append(span);
    });
    copy.append(name, metadata);
    const played = document.createElement('div');
    played.className = 'world-last-played';
    const label = document.createElement('span');
    label.textContent = '上次游玩';
    const time = document.createElement('time');
    const date = new Date(world.lastPlayed);
    time.dateTime = date.toISOString();
    time.textContent = new Intl.DateTimeFormat('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false }).format(date);
    played.append(label, time);
    row.append(image, copy, played);
    row.onclick = () => selectWorld(world.id, true);
    row.ondblclick = () => { selectWorld(world.id); playWorld(); };
    row.onkeydown = event => {
      const index = visible.findIndex(item => item.id === world.id);
      let next;
      if (event.key === 'ArrowDown') next = Math.min(index + 1, visible.length - 1);
      else if (event.key === 'ArrowUp') next = Math.max(index - 1, 0);
      else if (event.key === 'Home') next = 0;
      else if (event.key === 'End') next = visible.length - 1;
      else if (event.key === 'Enter') { event.preventDefault(); playWorld(); return; }
      else if (event.key === ' ') { event.preventDefault(); selectWorld(world.id); return; }
      else return;
      event.preventDefault();
      selectWorld(visible[next].id, true);
    };
    return row;
  }));
  $('#world-count').textContent = !dataLoaded ? '正在读取本机存档…' : $('#world-search').value.trim() && !emptyReview ? `${visible.length} / ${worlds.length} 个世界` : `${visible.length} 个世界`;
  const noResults = !emptyReview && worlds.length > 0;
  $('#list-empty').hidden = visible.length > 0 || !dataLoaded;
  $('#empty-title').textContent = noResults ? '没有找到世界' : '还没有世界';
  $('#empty-description').textContent = noResults ? '尝试其他名称或清空搜索。' : '创建一个世界，开始新的冒险。';
  $('#empty-create').hidden = noResults;
  $('#open-create').hidden = dataLoaded && !visible.length && !noResults;
  updateActions();
}
$('#world-search').oninput = renderWorlds;
$('#review-empty').onclick = () => {
  emptyReview = !emptyReview;
  $('#review-empty').setAttribute('aria-pressed', String(emptyReview));
  $('#world-search').value = '';
  renderWorlds();
  message(emptyReview ? '正在评审空列表布局；真实世界摘要仍保留在浏览器内存中。' : '已恢复只读加载的真实世界列表。');
};
function playWorld() {
  const world = selectedWorld();
  if (world) nativeEntry(`进入“${world.name}”`, '实际游戏将使用原版世界加载流程，继续原有的版本检查、备份提示与数据包加载。');
}
$('#play-world').onclick = playWorld;
$('#recreate-world').onclick = () => {
  const world = selectedWorld();
  if (world) nativeEntry('重新创建世界', `实际游戏会读取“${world.name}”的原生生成设置与数据包；不能从列表摘要推测种子或完整创建参数。`);
};
$('#edit-world').onclick = () => {
  const world = selectedWorld();
  if (!world) return;
  const label = document.createElement('label');
  label.htmlFor = 'rename-world';
  label.textContent = '世界名称';
  const input = document.createElement('input');
  input.id = 'rename-world';
  input.name = 'rename-world';
  input.value = world.name;
  input.autocomplete = 'off';
  input.spellcheck = false;
  const error = paragraph('请输入世界名称。');
  error.id = 'rename-error';
  error.className = 'field-error';
  error.setAttribute('aria-live', 'polite');
  error.hidden = true;
  input.setAttribute('aria-describedby', error.id);
  input.oninput = () => { error.hidden = true; input.removeAttribute('aria-invalid'); };
  openDialog('编辑世界', [label, input, error, paragraph('此稿只预览重命名。实际游戏的备份、文件夹、图标重置与优化入口仍由原版编辑页负责。')], [
    { text: '取消' },
    { text: '预览重命名', run: () => {
      if (!input.value.trim()) {
        error.hidden = false;
        input.setAttribute('aria-invalid', 'true');
        input.focus();
        return;
      }
      world.name = input.value.trim();
      dialog.close();
      renderWorlds();
      if (filteredWorlds().some(item => item.id === world.id)) selectWorld(world.id, true);
      else $('#world-search').focus();
      message('仅修改了浏览器中的名称，未写回存档；刷新页面可恢复原始摘要。');
    } },
  ]);
};
$('#delete-world').onclick = () => {
  const world = selectedWorld();
  if (!world) return;
  openDialog('删除这个世界？', [
    paragraph(`“${world.name}”在实际游戏中删除后无法撤销。`),
    paragraph('当前为 HTML 设计稿，确认只移除此页中的条目，不会删除或修改任何真实文件。'),
  ], [
    { text: '取消' },
    { text: '预览删除', critical: true, run: () => {
      worlds = worlds.filter(item => item.id !== world.id);
      dialog.close();
      renderWorlds();
      ($('#world-list').firstElementChild || $('#empty-create')).focus();
      message('已从浏览器列表中移除该条目，真实世界未删除；刷新可重新读取。');
    } },
  ]);
};

function setTab(name, focus = false) {
  document.querySelectorAll('[data-tab]').forEach(tab => {
    const selected = tab.dataset.tab === name;
    tab.setAttribute('aria-selected', String(selected));
    tab.tabIndex = selected ? 0 : -1;
    $(`#panel-${tab.dataset.tab}`).hidden = !selected;
    if (selected && focus) tab.focus();
  });
  $('.form-scroll').scrollTop = 0;
}
const tabs = [...document.querySelectorAll('[data-tab]')];
tabs.forEach((tab, index) => {
  tab.onclick = () => setTab(tab.dataset.tab);
  tab.onkeydown = event => {
    let next;
    if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
    else if (event.key === 'ArrowLeft') next = (index + tabs.length - 1) % tabs.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = tabs.length - 1;
    else return;
    event.preventDefault();
    setTab(tabs[next].dataset.tab, true);
  };
});
document.querySelectorAll('.switch').forEach(button => {
  button.onclick = () => {
    button.setAttribute('aria-checked', String(button.getAttribute('aria-checked') !== 'true'));
    if (button.id === 'allow-commands') commandsOverridden = true;
    updateForm();
  };
});
$('#game-mode').onchange = () => {
  const mode = $('#game-mode').value;
  if (mode === 'hardcore') {
    $('#difficulty').value = 'hard';
    $('#allow-commands').setAttribute('aria-checked', 'false');
    $('#bonus-chest').setAttribute('aria-checked', 'false');
  } else if (!commandsOverridden) {
    $('#allow-commands').setAttribute('aria-checked', String(mode === 'creative'));
  }
  updateForm();
};
function updateForm() {
  const mode = $('#game-mode').value;
  const hardcore = mode === 'hardcore';
  $('#game-mode-description').textContent = modeDescriptions[mode];
  $('#difficulty').disabled = hardcore;
  $('#allow-commands').disabled = hardcore;
  $('#bonus-chest').disabled = hardcore;
  $('#difficulty-description').textContent = hardcore ? '极限模式始终使用困难难度。' : '影响敌对生物、伤害和生存挑战。';
  $('#commands-description').textContent = hardcore ? '极限模式不允许使用命令。' : '允许使用传送、调整时间等游戏命令。';
  $('#bonus-description').textContent = hardcore ? '极限模式不提供奖励箱。' : '在出生点附近放置装有基础物资的箱子。';
  const preset = $('#world-type').value;
  $('#world-type-description').textContent = presets[preset]?.[1] || '正在读取原版世界类型…';
  $('#customize-world').hidden = !['minecraft:flat', 'minecraft:single_biome_surface'].includes(preset);
  $('#create-summary').textContent = `${modes[mode]} · ${difficulties[$('#difficulty').value]} · ${presets[preset]?.[0] || '读取世界类型中'}`;
  $('#create-world').disabled = !dataLoaded || !$('#world-name').value.trim();
}
$('#difficulty').onchange = $('#world-type').onchange = updateForm;
$('#world-name').oninput = () => {
  $('#name-error').hidden = true;
  $('#world-name').removeAttribute('aria-invalid');
  updateForm();
};
$('#world-name').onblur = () => {
  const invalid = !$('#world-name').value.trim();
  $('#name-error').hidden = !invalid;
  if (invalid) $('#world-name').setAttribute('aria-invalid', 'true');
};
$('#customize-world').onclick = () => nativeEntry(
  $('#world-type').value === 'minecraft:flat' ? '自定义超平坦世界' : '选择生物群系',
  $('#world-type').value === 'minecraft:flat' ? '后续保留原版层编辑与预设流程，不在 HTML 中另造地形配置系统。' : '后续使用当前游戏注册表提供的真实生物群系候选，不手写生物群系清单。',
);
document.querySelectorAll('[data-native-page]').forEach(button => {
  button.onclick = () => nativeEntry(button.dataset.nativePage, '保留当前新建世界草稿，返回后继续原版创建流程。');
});
$('#create-world').onclick = () => {
  if (!$('#world-name').value.trim() || !dataLoaded) return;
  openDialog('创建操作预览', [
    paragraph(`世界名称：${$('#world-name').value.trim()}`),
    paragraph($('#create-summary').textContent),
    paragraph('实际游戏点击后会直接执行原版创建流程，不额外加入本确认框。本设计稿不生成地形、不创建文件，也不会向真实列表追加虚构世界。'),
  ], [{ text: '返回设计稿' }]);
};
updateForm();

async function loadWorlds() {
  try {
    const response = await fetch('../../../.local/game-ui-design/worlds.json', { cache: 'no-store' });
    if (!response.ok) throw new Error(`世界摘要 HTTP ${response.status}`);
    const data = await response.json();
    if (!data.readOnly || !Array.isArray(data.worlds) || !Array.isArray(data.worldPresets)) throw new Error('世界摘要格式不正确');
    for (const preset of data.worldPresets) {
      if (!presets[preset]) throw new Error(`尚未设计的原版世界类型：${preset}`);
    }
    if (!data.worldPresets.length) throw new Error('原版世界类型列表为空');
    for (const world of data.worlds) {
      if (typeof world.id !== 'string' || typeof world.name !== 'string' || typeof world.folder !== 'string'
        || typeof world.version !== 'string' || !Number.isFinite(world.lastPlayed) || !Number.isInteger(world.gameType)
        || !Number.isFinite(new Date(world.lastPlayed).getTime())) throw new Error('世界摘要条目不完整');
    }
    worlds = data.worlds;
    $('#world-type').replaceChildren(...data.worldPresets.map(id => {
      const option = document.createElement('option');
      option.value = id;
      option.textContent = presets[id][0];
      return option;
    }));
    dataLoaded = preview.dataReady = true;
    $('#review-empty').disabled = page !== 'select';
    $('#source-note').textContent = `${data.clientVersion} · 只读加载本机 ${worlds.length} 个世界；名称、模式、版本、时间与缩略图均来自实际存档。`;
    renderWorlds();
    updateForm();
  } catch (error) {
    console.error(error);
    $('#world-count').textContent = '世界摘要读取失败';
    $('#list-empty').hidden = false;
    $('#empty-title').textContent = '未能读取本机世界';
    $('#empty-description').textContent = error.message;
    $('#empty-create').hidden = true;
    message('未填入示例世界或伪造候选；需要重新导出本地只读摘要。');
  }
}

async function startPanorama() {
  try {
    const THREE = await import('../../../.local/design-tools/node_modules/three/build/three.module.js');
    const renderer = new THREE.WebGLRenderer({ canvas: $('#panorama'), antialias: true });
    renderer.setPixelRatio(Math.min(devicePixelRatio, 1.5));
    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(80, size[0] / size[1], .1, 10);
    let generation = 0;
    let renderedSize = '';
    loadPanorama = async theme => {
      const request = ++generation;
      try {
        const paths = [1, 3, 4, 5, 0, 2].map(face => `../../../.local/game-ui-design/panorama_${face}.png`);
        const texture = await new THREE.CubeTextureLoader().loadAsync(paths);
        if (request !== generation) { texture.dispose(); return; }
        texture.colorSpace = THREE.SRGBColorSpace;
        const previous = scene.background;
        scene.background = texture;
        previous?.dispose();
        preview.backgroundReady = true;
        $('#resource-error').hidden = true;
      } catch (error) {
        if (request !== generation) return;
        console.error(error);
        $('#resource-error').textContent = `全景加载失败：${error.message}。未使用静态图冒充动态背景。`;
        $('#resource-error').hidden = false;
      }
    };
    await loadPanorama(activeTheme);
    let angle = .55;
    let last = performance.now();
    renderer.setAnimationLoop(now => {
      const delta = Math.min((now - last) / 1000, .1);
      last = now;
      if (document.hidden || activeTheme === 'contrast') return;
      if (!paused) angle += delta * .018;
      const currentSize = size.join(',');
      if (currentSize !== renderedSize) {
        renderedSize = currentSize;
        renderer.setSize(size[0], size[1], false);
        camera.aspect = size[0] / size[1];
        camera.updateProjectionMatrix();
      }
      camera.rotation.set(-.12, angle, 0, 'YXZ');
      renderer.render(scene, camera);
    });
    window.addEventListener('pagehide', () => {
      generation++;
      renderer.setAnimationLoop(null);
      scene.background?.dispose();
      renderer.dispose();
    }, { once: true });
  } catch (error) {
    console.error(error);
    $('#resource-error').textContent = `全景预览不可用：${error.message}。需要本机 Three.js 与 WebGL。`;
    $('#resource-error').hidden = false;
  }
}
await Promise.all([loadWorlds(), startPanorama()]);
preview.ready = true;
