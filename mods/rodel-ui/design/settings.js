// Withdrawn content draft: these rows are layout references, not a settings schema.
const topics = [
  ['interface', '界面', 'icon-theme', '菜单、缩放与显示风格。'],
  ['video', '画面', 'display', '画面质量、视距与渲染偏好。'],
  ['audio', '声音', 'sound', '分别调整音乐、环境与游戏音效。'],
  ['controls', '操作', 'keyboard', '鼠标、移动与按键绑定。'],
  ['language', '语言', 'world', '界面语言与文字显示。'],
  ['accessibility', '辅助功能', 'accessibility', '让画面与信息反馈更易感知。'],
  ['chat', '聊天', 'chat', '消息显示、链接与聊天布局。'],
  ['resources', '资源包', 'layers', '查看当前配置启用的资源包。'],
  ['online', '隐私与在线', 'shield', '多人游戏中的公开信息与隐私偏好。'],
  ['about', '关于', 'info', '版本、鸣谢与版权说明。'],
];

const choose = (key, label, description, choices) => ({ kind: 'select', key, label, description, choices });
const toggle = (key, label, description) => choose(key, label, description, [[true, '开启'], [false, '关闭']]);
const range = (key, label, description, min = 0, max = 1, step = .01, unit = '%') =>
  ({ kind: 'range', key, label, description, min, max, step, unit });
const fixed = (label, value, description = '') => ({ kind: 'fixed', label, value, description });
const action = (label, description, target = label) => ({ kind: 'action', label, description, target });
const group = (title, rows) => ({ title, rows });

export function mountSettings({ screen, viewport, setTheme, showDialog }) {
  const root = document.createElement('section');
  root.className = 'settings-page';
  root.hidden = true;
  root.setAttribute('aria-label', '游戏设置');
  root.innerHTML = `
    <header class="settings-header">
      <button class="control settings-back" aria-label="返回主菜单" title="返回主菜单"><svg aria-hidden="true"><use href="#arrow"/></svg></button>
      <div><h2>设置</h2><p>让熟悉的世界，更合自己的习惯。</p></div>
    </header>
    <div class="settings-layout">
      <nav class="settings-sidebar" aria-label="游戏设置分类"></nav>
      <section class="settings-details" aria-labelledby="settings-heading">
        <header class="settings-section-header"><h3 id="settings-heading"></h3><p></p></header>
        <div class="settings-scroll" tabindex="0" aria-label="设置详情"></div>
        <footer class="settings-snapshot"><span class="snapshot-source"></span><span class="snapshot-changes" aria-live="polite"></span></footer>
      </section>
    </div>`;
  screen.append(root);
  const nav = root.querySelector('nav');
  const body = root.querySelector('.settings-scroll');
  const header = root.querySelector('.settings-section-header');
  const values = {};
  const changes = new Map();
  let snapshot = null;
  let failure = null;
  let selected = 'interface';
  let returnFocus = null;
  let active = false;

  function node(tag, text, className) {
    const element = document.createElement(tag);
    if (text !== undefined) element.textContent = text;
    if (className) element.className = className;
    return element;
  }

  function groups() {
    switch (selected) {
      case 'interface': return [
        group('显示风格', [
          choose('rodel_theme', '界面主题', '只改变菜单材质，保留原版动态全景。', [['dark', '深色'], ['light', '浅色']]),
          choose('guiScale', 'GUI 缩放', '原生游戏界面大小；Rodel UI 的文字与控件保持整体比例。', [[0, '自动'], ...[1, 2, 3, 4, 5, 6].map(value => [value, `${value} 倍`])]),
          toggle('highContrast', '高对比度', '用更清晰的边界和对比度区分界面元素。'),
        ]),
        group('文字', [
          fixed('中文字体', '微软雅黑 · 粗体', '直接读取 Windows 系统字体，不随模组分发。'),
          fixed('英文与数字', 'Minecraft 原版像素字', '保留原有的风格化字形。'),
        ]),
      ];
      case 'video': return [
        group('显示', [
          choose('preferredGraphicsBackend', '渲染后端', '更改后需重启游戏；启动参数可能覆盖此偏好。', [['default', '由游戏选择'], ['vulkan', 'Vulkan'], ['opengl', 'OpenGL']]),
          toggle('fullscreen', '全屏', '在窗口与全屏显示之间切换。'),
          toggle('enableVsync', '垂直同步', '将画面呈现与显示器刷新节奏同步。'),
          choose('maxFps', '帧率上限', '平衡画面流畅度与设备负载。', [30, 60, 120, 144, 165, 240, 260].map(value => [value, `${value} FPS`])),
        ]),
        group('世界画面', [
          range('renderDistance', '渲染距离', '决定远处区块的可见范围。', 2, 32, 1, ' 区块'),
          range('simulationDistance', '模拟距离', '决定附近区块运行游戏逻辑的范围。', 5, 32, 1, ' 区块'),
          range('gamma', '亮度', '调整暗处的可见程度。'),
          toggle('ao', '平滑光照', '让方块交界处的明暗过渡更自然。'),
          action('完整视频设置', '实机中进入原版页面，保留高级画面选项。'),
        ]),
      ];
      case 'audio': return [
        group('主音量', [range('soundCategory_master', '主音量', '控制游戏整体音量。')]),
        group('分类音量', [
          range('soundCategory_music', '音乐', '游戏与菜单背景音乐。'),
          range('soundCategory_ui', '界面音效', '按钮与其他界面操作的声音。'),
          range('soundCategory_ambient', '环境', '周围环境的氛围声音。'),
          range('soundCategory_weather', '天气', '雨、雷与其他天气声音。'),
          range('soundCategory_block', '方块', '放置、破坏与方块交互。'),
          range('soundCategory_hostile', '敌对生物', '敌对生物发出的声音。'),
          range('soundCategory_neutral', '友好生物', '友好与中立生物发出的声音。'),
          range('soundCategory_player', '玩家', '玩家动作与相关声音。'),
        ]),
      ];
      case 'controls': return [
        group('鼠标', [
          range('mouseSensitivity', '鼠标灵敏度', '调整视角转动的响应速度。'),
          toggle('invertYMouse', '反转鼠标 Y 轴', '交换向上、向下移动鼠标的视角方向。'),
        ]),
        group('移动', [
          toggle('autoJump', '自动跳跃', '靠近适合跨越的方块时自动起跳。'),
          toggle('toggleSprint', '切换式疾跑', '用切换状态代替持续按住疾跑键。'),
        ]),
        group('按键', [
          fixed('前进', binding('key_key.forward'), '来自当前实例的真实按键配置。'),
          fixed('跳跃', binding('key_key.jump')),
          fixed('物品栏', binding('key_key.inventory')),
          action('按键绑定', '实机中进入原版按键页面，保留冲突检测与重置操作。'),
        ]),
      ];
      case 'language': return [
        group('语言与文字', [
          choose('lang', '界面语言', '实机中切换语言会重新加载语言资源。', [['zh_cn', '简体中文'], ['zh_tw', '繁體中文'], ['en_us', 'English']]),
          fixed('中文字体', '微软雅黑 · 粗体'),
          fixed('英文与数字', 'Minecraft 原版像素字'),
          action('全部语言', '实机中进入完整的原版语言列表。'),
        ]),
      ];
      case 'accessibility': return [
        group('信息反馈', [
          choose('narrator', '旁白', '选择需要朗读的信息范围。', [[0, '关闭'], [1, '朗读全部'], [2, '仅聊天'], [3, '仅系统']]),
          toggle('showSubtitles', '声音字幕', '用字幕提示附近声音及其方向。'),
          toggle('highContrast', '高对比度', '增强界面元素的区分度。'),
        ]),
        group('视觉效果', [
          range('screenEffectScale', '屏幕效果', '调整传送门等屏幕效果的强度。'),
          range('fovEffectScale', '视场变化效果', '调整疾跑等状态对视场的影响。'),
          action('完整辅助功能设置', '实机中保留原版其余辅助功能选项。'),
        ]),
      ];
      case 'chat': return [
        group('消息与链接', [
          choose('chatVisibility', '聊天显示', '决定聊天窗口显示哪些消息。', [[0, '显示全部'], [1, '仅命令'], [2, '隐藏']]),
          toggle('chatColors', '聊天颜色', '显示消息中的颜色格式。'),
          toggle('chatLinks', '网页链接', '允许从聊天消息访问网页链接。'),
          toggle('chatLinksPrompt', '打开链接前确认', '访问外部网页前显示确认提示。'),
        ]),
        group('聊天布局', [
          range('chatScale', '聊天缩放', '单独调整聊天窗口的显示大小。'),
          action('完整聊天设置', '实机中继续访问原版聊天布局选项。'),
        ]),
      ];
      case 'resources': return [
        group('当前配置', [fixed('启用的资源包', Array.isArray(values.resourcePacks) ? `${values.resourcePacks.length} 个额外资源包` : '未读取', '只展示配置中启用的资源包，不代表磁盘中安装的全部内容。')]),
        group('资源管理', [action('管理资源包', '实机中进入原版资源包页面，保留启用顺序与兼容性提示。')]),
      ];
      case 'online': return [
        group('多人游戏隐私', [
          toggle('allowServerListing', '服务器公开列表', '允许服务器在公开列表中显示你的在线信息。'),
          toggle('onlyShowSecureChat', '仅显示安全聊天', '选择是否只显示可验证的聊天消息。'),
          toggle('telemetryOptInExtra', '可选遥测数据', '选择是否额外发送可选的游戏使用数据。'),
        ]),
        group('在线选项', [action('完整在线设置', '实机中使用原版账号与在线选项，不在此伪造连接状态。')]),
      ];
      case 'about': return [
        group('版本', [fixed('Minecraft', '26.3'), fixed('模组', 'Rodel UI'), fixed('加载器', 'Fabric')]),
        group('鸣谢与版权', [
          action('© Mojang AB · 鸣谢与版权说明', '实机中进入原版鸣谢与版权页面。', 'copyright'),
          fixed('系统字体', 'Microsoft YaHei Bold', '仅从本机 Windows 读取，不包含在模组分发包中。'),
        ]),
      ];
      default: throw new Error(`Unknown settings category: ${selected}`);
    }
  }

  function binding(key) {
    const value = values[key];
    if (typeof value !== 'string') return '未读取';
    if (value === 'key.keyboard.space') return '空格';
    return value.startsWith('key.keyboard.') ? value.slice('key.keyboard.'.length).toUpperCase() : value;
  }

  function reportChanges() {
    root.querySelector('.snapshot-source').textContent = '布局草图 · 手写设置目录已撤回';
    root.querySelector('.snapshot-changes').textContent = '等待接入游戏原生设置控件';
  }

  function changed(key, value) {
    values[key] = value;
    if (snapshot && JSON.stringify(value) !== JSON.stringify(snapshot.values[key])) changes.set(key, value);
    else changes.delete(key);
    if (key === 'rodel_theme') setTheme(value);
    reportChanges();
  }

  function field(item) {
    const container = node('div', undefined, 'settings-field');
    if (item.kind === 'fixed') {
      container.append(node('span', item.value, 'settings-static'));
      return container;
    }
    if (item.kind === 'action') {
      const button = node('button', item.label, 'control');
      button.type = 'button';
      button.onclick = () => showDialog(item.target === 'copyright' ? '鸣谢与版权说明' : item.label,
        node('p', item.target === 'copyright'
          ? '© Mojang AB。正式游戏进入原版鸣谢与版权页面；这里仅演示页面层级。'
          : '正式游戏继续使用对应的原版选项与处理逻辑。本页只评审设置布局，不执行游戏操作。'));
      container.append(button);
      return container;
    }
    const current = values[item.key];
    const missing = current === undefined;
    if (item.kind === 'select') {
      const select = node('select', undefined, 'control settings-select');
      select.id = `setting-${item.key}`;
      select.dataset.setting = item.key;
      select.setAttribute('aria-label', item.label);
      const choices = item.choices.slice();
      if (missing) choices.unshift([undefined, '未读取']);
      else if (!choices.some(([value]) => value === current)) choices.push([current, String(current)]);
      for (const [index, [value, label]] of choices.entries()) {
        const option = node('option', label);
        option.value = String(index);
        option.dataset.settingValue = String(value);
        option.selected = value === current;
        select.append(option);
      }
      select.disabled = missing;
      select.onchange = () => changed(item.key, choices[Number(select.value)][0]);
      container.append(select);
    } else {
      const wrap = node('div', undefined, 'settings-range');
      const input = document.createElement('input');
      input.type = 'range';
      input.id = `setting-${item.key}`;
      input.setAttribute('aria-label', item.label);
      input.min = String(item.min);
      input.max = String(item.max);
      input.step = String(item.step);
      const valid = !missing && typeof current === 'number' && Number.isFinite(current);
      input.disabled = !valid;
      if (valid) {
        input.min = String(Math.min(item.min, current));
        input.max = String(Math.max(item.max, current));
        input.value = String(current);
        input.style.setProperty('--progress', `${(current - Number(input.min)) / (Number(input.max) - Number(input.min)) * 100}%`);
      }
      const output = node('output', valid ? display(current, item.unit) : '未读取');
      output.htmlFor = input.id;
      input.oninput = () => {
        const value = Number(input.value);
        output.textContent = display(value, item.unit);
        input.style.setProperty('--progress', `${(value - Number(input.min)) / (Number(input.max) - Number(input.min)) * 100}%`);
        changed(item.key, value);
      };
      wrap.append(input, output);
      container.append(wrap);
    }
    return container;
  }

  function display(value, unit) {
    return unit === '%' ? `${Math.round(value * 100)}%` : `${value}${unit}`;
  }

  function render() {
    const topic = topics.find(([id]) => id === selected);
    header.querySelector('h3').textContent = topic[1];
    header.querySelector('p').textContent = topic[3];
    nav.querySelectorAll('button').forEach(button => {
      if (button.dataset.topic === selected) button.setAttribute('aria-current', 'page');
      else button.removeAttribute('aria-current');
    });
    body.replaceChildren();
    body.scrollTop = 0;
    body.append(node('div',
      '此页只保留布局参照，手写的设置目录和控件已停用。正式实现直接覆盖原版真实设置页面与控件；第三方使用其注册的配置界面，不以 options.txt 快照或本页列表生成设置。',
      'settings-notice'));
    if (failure) {
      const notice = node('div', `无法读取真实设置快照：${failure}`, 'settings-notice error');
      const retry = node('button', '重新读取', 'control');
      retry.onclick = load;
      notice.append(document.createElement('br'), retry);
      body.append(notice);
    }
    for (const section of groups()) {
      const groupNode = node('section', undefined, 'settings-group');
      groupNode.append(node('h4', section.title));
      for (const item of section.rows) {
        const row = node('div', undefined, 'settings-row');
        const label = node('div', undefined, 'settings-row-label');
        const title = node(item.key ? 'label' : 'strong', item.label);
        if (item.key) title.htmlFor = `setting-${item.key}`;
        label.append(title);
        if (item.description) label.append(node('p', item.description));
        row.append(label, field(item));
        groupNode.append(row);
      }
      body.append(groupNode);
    }
    if (selected === 'resources' && Array.isArray(values.resourcePacks) && values.resourcePacks.length) {
      const list = node('ul', undefined, 'settings-pack-list');
      values.resourcePacks.forEach(pack => list.append(node('li', String(pack))));
      body.append(list);
    }
    body.querySelectorAll('select, input, button').forEach(control => { control.disabled = true; });
    reportChanges();
  }

  for (const [id, label, icon] of topics) {
    const button = node('button', undefined, 'settings-category');
    button.type = 'button';
    button.dataset.topic = id;
    button.innerHTML = `<svg aria-hidden="true"><use href="#${icon}"/></svg>`;
    button.append(node('span', label));
    button.onclick = () => { selected = id; render(); };
    nav.append(button);
  }
  nav.onkeydown = event => {
    if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const buttons = [...nav.querySelectorAll('button')];
    const current = buttons.indexOf(document.activeElement);
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1
      : (current + (event.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length;
    buttons[next].focus();
    buttons[next].click();
    buttons[next].scrollIntoView({ block: 'nearest' });
  };
  root.querySelector('.settings-back').onclick = close;
  root.addEventListener('keydown', event => {
    if (event.key === 'Escape' && event.target.tagName !== 'SELECT') {
      event.preventDefault();
      close();
    }
  });

  async function load() {
    failure = null;
    try {
      const response = await fetch('../../../.local/game-ui-design/settings.json', { cache: 'no-store' });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const data = await response.json();
      if (data.schemaVersion !== 1 || !data.values || typeof data.values !== 'object') throw new Error('配置快照格式无效');
      snapshot = data;
      Object.assign(values, data.values);
      changes.forEach((value, key) => { values[key] = value; });
      values.rodel_theme = screen.dataset.theme;
    } catch (error) {
      failure = error.message;
      console.error('Rodel UI settings snapshot:', error);
    }
    if (active) render();
  }

  function open(topic = selected) {
    selected = topics.some(([id]) => id === topic) ? topic : 'interface';
    returnFocus = document.activeElement;
    active = true;
    root.hidden = false;
    viewport.dataset.page = 'settings';
    screen.querySelector('.hero').inert = true;
    screen.querySelector('.panel').inert = true;
    screen.querySelector('.footer').inert = true;
    values.rodel_theme = screen.dataset.theme;
    render();
    nav.querySelector(`[data-topic="${selected}"]`).focus();
  }

  function close() {
    active = false;
    root.hidden = true;
    viewport.dataset.page = 'menu';
    screen.querySelector('.hero').inert = false;
    screen.querySelector('.panel').inert = false;
    screen.querySelector('.footer').inert = false;
    if (returnFocus?.isConnected) returnFocus.focus();
  }
  screen.addEventListener('rodel-theme-change', () => {
    values.rodel_theme = screen.dataset.theme;
    if (snapshot && values.rodel_theme !== snapshot.values.rodel_theme) changes.set('rodel_theme', values.rodel_theme);
    else changes.delete('rodel_theme');
    root.querySelectorAll('[data-setting="rodel_theme"] option').forEach(option => {
      option.selected = option.dataset.settingValue === values.rodel_theme;
    });
    reportChanges();
  });
  void load();
  return { open, close, get active() { return active; }, get ready() { return snapshot !== null; } };
}
