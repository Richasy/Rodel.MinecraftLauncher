/* Design simulation only. No fetch, model requests or client execution. Picked browser images stay in memory. */
const Assistant = (() => {
  const clientArtworkBase = new URL('../../src/Rodel.MinecraftLauncher.App/Assets/AiClients/', document.currentScript.src);
const assistantIconPaths = {
  image: '<rect x="3" y="3" width="18" height="18" rx="1"/><circle cx="8" cy="8" r="2"/><path d="m4 19 6-7 4 4 3-4 4 5"/>',
  refresh: '<path d="M20 7v5h-5M4 17v-5h5"/><path d="M6 7a7 7 0 0 1 12-2l2 3M4 16l2 3a7 7 0 0 0 12-2"/>',
  edit: '<path d="m15 4 5 5M4 16 16 4a3 3 0 0 1 4 4L8 20H4Z"/>',
  trash: '<path d="M3 6h18M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7M14 10v7"/>',
  home: '<path d="m3 10 9-7 9 7v10H3Z"/><path d="M9 20v-7h6v7"/>',
  chat: '<path d="M4 4h16v12H9l-5 4Z"/><path d="M8 8h8M8 12h5"/>',
  settings: '<path d="M4 6h16M4 12h16M4 18h16M8 3v6M16 9v6M10 15v6"/>',
  spark: '<path d="m12 3 2.6 6.4L21 12l-6.4 2.6L12 21l-2.6-6.4L3 12l6.4-2.6Z"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  arrow: '<path d="M5 12h14m-6-6 6 6-6 6"/>',
  back: '<path d="M19 12H5m6-6-6 6 6 6"/>',
  close: '<path d="m6 6 12 12M6 18 18 6"/>',
  chevron: '<path d="m6 9 6 6 6-6"/>',
  file: '<path d="M5 3h9l5 5v13H5Z"/><path d="M14 3v6h5M8 13h8M8 17h6"/>',
  globe: '<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c5 5 5 13 0 18-5-5-5-13 0-18Z"/>',
  plug: '<path d="M8 3v5m8-5v5M6 8h12v4a6 6 0 0 1-12 0ZM12 18v3"/>',
  book: '<path d="M3 4h7l2 2 2-2h7v15h-7l-2 2-2-2H3ZM12 6v15"/>',
  external: '<path d="M13 3h8v8m0-8L11 13M9 5H3v16h16v-6"/>',
  history: '<path d="M3 10a9 9 0 1 1 2 8M3 4v6h6M12 7v5l4 2"/>',
  more: '<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>',
  copy: '<path d="M8 8h12v13H8ZM4 16H2V2h12v2"/>',
  check: '<path d="m4 12 5 5L20 6"/>',
  stop: '<path d="M6 6h12v12H6Z"/>',
  folder: '<path d="M3 5h7l2 3h9v12H3Z"/>',
  shield: '<path d="m12 3 8 3v6c0 5-8 9-8 9S4 17 4 12V6Z"/><path d="m8 12 3 3 5-6"/>',
  server: '<path d="M3 3h18v7H3ZM3 14h18v7H3ZM7 6h1m-1 11h1M12 6h5m-5 11h5"/>',
  download: '<path d="M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5"/>',
  warning: '<path d="m12 3 10 18H2ZM12 9v5m0 3v1"/>'
};
function assistantIcon(name) { return `<svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="square" stroke-linejoin="miter" aria-hidden="true">${assistantIconPaths[name] || assistantIconPaths.spark}</svg>`; }

  const e = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const $ = selector => document.querySelector('#assistant-modal-root')?.querySelector(selector) || document.querySelector('#assistant-content')?.querySelector(selector) || document.querySelector('#assistant-settings')?.querySelector(selector);
  let initialized = false;
  const expandedSettings = new Map();
  const btn = (label, action, extra = '', kind = '') => `<button type="button" class="${kind === 'text-action' ? 'text-action' : `small-button ${kind}`}" data-assistant-action="${action}" ${extra}>${label}</button>`;
  const iconBtn = (icon, label, action, extra = '') => `<button type="button" class="icon-button" data-assistant-action="${action}" aria-label="${e(label)}" title="${e(label)}" ${extra}>${assistantIcon(icon)}</button>`;
  const select = (name, label, options, value, disabled = false) => `<select name="${name}" aria-label="${e(label)}" ${disabled ? 'disabled' : ''}>${options.map(o => { const [v, text, description] = Array.isArray(o) ? o : [o, o]; return `<option ${description ? `data-description="${e(description)}"` : ''} value="${e(v)}" ${String(value) === String(v) ? 'selected' : ''}>${e(text)}</option>`; }).join('')}</select>`;
  const field = (label, name, value = '', placeholder = '', type = 'text', extra = '') => `<div class="form-field"><label for="field-${name}">${label}</label><input id="field-${name}" name="${name}" type="${type}" value="${e(value)}" placeholder="${e(placeholder)}" ${extra}></div>`;
  const protocols = [['openai-responses', 'OpenAI · Responses'], ['openai-chat', 'OpenAI · Chat Completions'], ['anthropic', 'Anthropic · Messages'], ['gemini', 'Gemini']];
  const serviceKinds = [['openai', 'OpenAI 服务及兼容 API', '兼容 OpenAI 接口的服务，如 DeepSeek, Azure OpenAI等'], ['anthropic', 'Anthropic 服务及兼容 API', '支持 Anthropic 服务及兼容其 API 的服务。'], ['gemini', 'Gemini 服务及兼容 API', '支持 Gemini 服务及兼容其 API 的服务。']];
  const serviceKind = protocol => protocol.startsWith('openai-') ? 'openai' : protocol;
  const instances = [['none', '不附加实例'], ['mist', '雾谷生存 · Fabric 1.21.1'], ['creative', '建筑练习 · 原版 1.21.1']];
  const integrationClients = {
    codex: ['Codex', '.agents', 'codex'],
    claude: ['Claude Code', '.claude', 'claude'],
    copilot: ['GitHub Copilot CLI', '.copilot', 'copilot'],
    'deepseek-harness': ['DeepSeek Harness', '.dsh', 'dsh']
  };
  const state = {
    view: 'chat', section: 'services', services: [], defaultMode: 'read', modeChosen: false, defaultBinding: '', threads: [], threadId: null, activeTurn: null, queued: [],
    draft: '', references: [], instance: 'none', mode: 'read', binding: '', historyOpen: false,
    accessDefaults: { files: true, network: true }, files: { enabled: true, roots: [] }, network: { enabled: true },
    skills: [{ id: 'builtin', name: '启动器助手', description: '实例诊断、设置与参数、内容审核、任务跟踪', source: '产品内置', enabled: true, valid: true }],
    mcps: [{ id: 'builtin-files', name: '文件工具', builtin: true, enabled: true, status: 'connected' }, { id: 'builtin-web', name: '网络工具', builtin: true, enabled: true, status: 'connected' }], integrations: {}, jobs: [], fault: 'none', serial: 0,
    followBottom: true, modal: null, modalTrigger: null, noticeTimer: null
  };
  const builtinMcp = name => state.mcps.find(m => m.id === `builtin-${name}` && m.enabled && m.status === 'connected');
  const nextId = prefix => `${prefix}-${++state.serial}`;
  const thread = () => state.threads.find(t => t.id === state.threadId);
  const currentBinding = () => thread()?.binding ?? state.binding;
  const binding = value => { const [serviceId, modelId] = (value ?? currentBinding()).split('/'); const service = state.services.find(s => s.id === serviceId); return { service, model: service?.models.find(m => m.id === modelId) }; };
  const contextLabel = id => instances.find(x => x[0] === id)?.[1] ?? '不附加实例';
  const activeHere = () => state.activeTurn?.threadId === state.threadId;
  const busy = () => !!state.activeTurn;
  const draft = () => thread()?.draft ?? state.draft;
  const draftReferences = () => thread() ? (thread().references ||= []) : state.references;
  function referenceMarkup(images, removable = false) {
    return images.length ? `<div class="chat-references">${images.map(image => `<div class="chat-reference"><img src="${e(image.url)}" alt="${e(image.name)}"><span>${e(image.name)}</span>${removable ? iconBtn('close', '移除图片', 'remove-reference', `data-id="${image.id}"`) : ''}</div>`).join('')}</div>` : '';
  }
  async function attachReferences(files) {
    const images = [...files].filter(file => ['image/png','image/jpeg','image/webp'].includes(file.type));
    if (!images.length) return;
    const target = draftReferences();
    if (target.length + images.length > 3) { notice('每轮最多添加 3 张图片。'); return; }
    const prepared = await Promise.all(images.map(file => new Promise((resolve, reject) => {
      const reader = new FileReader(); reader.onload = () => resolve({ id: nextId('image'), name: file.name || '粘贴的图片', url: reader.result });
      reader.onerror = reject; reader.readAsDataURL(file);
    })));
    if (target.length + prepared.length > 3) { notice('每轮最多添加 3 张图片。'); return; }
    target.push(...prepared); render();
  }
  function pendingQuestion() { return activeHere() ? thread()?.messages.find(m => m.id === state.activeTurn.messageId && m.prompt) : null; }
  function questionCard() {
    const m = pendingQuestion(), q = m?.prompt;
    if (!q) return '';
    return `<section class="chat-user-question" aria-label="需要你的回答"><small>需要你的回答</small><div class="chat-question-body"><p>${e(q.question)}</p><span>可选择一项，也可以直接输入</span>${q.options.map(option => `<label class="checkbox-label"><input type="radio" name="user-answer-option" value="${option.id}" ${q.selected === option.id ? 'checked' : ''}><span>${e(option.label)}</span></label>`).join('')}<input type="text" id="user-answer-text" placeholder="输入你的回答或补充说明" value="${e(q.text)}"></div><footer>${btn('提交回答', 'submit-answer', !q.selected && !q.text.trim() ? 'disabled' : '', 'primary')}${btn('跳过', 'skip-answer')}</footer></section>`;
  }
  const saveDraft = value => { if (thread()) thread().draft = value; else state.draft = value; };
  const optionValue = name => $(`[name="${name}"]`)?.value ?? '';
  function notice(text) { showToast(text); }
  function heading(title, description, actions = '') { return `<header class="page-heading assistant-heading"><div class="heading-copy">${state.view === 'chat' ? iconBtn('history', '打开对话历史', 'toggle-history').replace('class="icon-button"', 'class="icon-button mobile-history"') : ''}<h1>${title}</h1></div><div class="heading-actions">${actions}</div></header>`; }
  function decorate() { document.querySelectorAll('.assistant-content [data-glyph],.assistant-settings [data-glyph],.assistant-dialog [data-glyph]').forEach(n => n.innerHTML = assistantIcon(n.dataset.glyph)); document.querySelectorAll('.assistant-dialog .review-options>summary,.assistant-content .review-options>summary').forEach(summary => { if (!summary.querySelector('.disclosure-chevron')) summary.insertAdjacentHTML('beforeend', `<span class="disclosure-chevron">${assistantIcon('chevron')}</span>`); }); if (typeof upgradeCombos === 'function') upgradeCombos(); }
  function render() {
    const isSettings = currentPage === 'settings' && settingsSection === 'ai';
    const host = document.getElementById(isSettings ? 'assistant-settings' : 'assistant-content');
    if (!host || (!isSettings && currentPage !== 'ai')) return;
    host.querySelectorAll('.settings-expander[data-expander]').forEach(group => expandedSettings.set(group.dataset.expander, group.open));
    const previousTranscript = $('.transcript');
    const oldScroll = previousTranscript?.scrollTop || 0;
    if (previousTranscript && thread() && previousTranscript.dataset.threadId === thread().id) thread().scrollOffset = oldScroll;
    closeCombo();
    host.innerHTML = isSettings ? settingsContent() : chatPage();
    fitContent();
    decorate();
    const transcript = $('.transcript');
    if (transcript) {
      const current = thread();
      transcript.dataset.threadId = current?.id || '';
      transcript.scrollTop = !current?.messages.length && !current?.repair ? 0 : state.followBottom ? transcript.scrollHeight : current.scrollOffset ?? oldScroll;
      transcript.addEventListener('scroll', () => {
        if (!transcript.isConnected || thread() !== current) return;
        if (current) current.scrollOffset = transcript.scrollTop;
        state.followBottom = transcript.scrollHeight - transcript.scrollTop - transcript.clientHeight < 56;
        const b = $('[data-assistant-action="scroll-bottom"]'); if (b) b.hidden = state.followBottom;
      });
    }
  }
  function modelButton() {
    const value = currentBinding() || state.defaultBinding;
    return `<div class="model-choice"><select id="assistant-model" name="model-binding" aria-label="选择模型">${!value ? '<option value="" disabled selected>选择模型</option>' : ''}${state.services.map(service => `<optgroup label="${e(service.name)}${service.enabled ? '' : ' · 已禁用'}" ${service.enabled ? '' : 'disabled'}>${service.models.length ? service.models.map(model => `<option value="${e(`${service.id}/${model.id}`)}" ${value === `${service.id}/${model.id}` ? 'selected' : ''}>${e(model.name)}</option>`).join('') : '<option disabled>尚未添加模型</option>'}</optgroup>`).join('')}${!state.services.length ? '<option disabled>请先在设置中添加模型服务</option>' : ''}</select></div>`;
  }
  function threadBadge(t) {
    if (t.repair?.busy) return '响应中';
    if (t.repair && ['blocked','partial','uncertain','paused'].includes(t.repair.phase)) return '待回复';
    const active = state.activeTurn?.threadId === t.id ? findMessage(state.activeTurn.messageId).m : null;
    if (active?.status === 'awaiting_user' || active?.status === 'confirming') return '待回复';
    if (active) return '响应中';
    if (state.queued.some(item => item.t.id === t.id)) return '排队中';
    return t.unread ? '未读' : '';
  }
  function closeHistory() { state.historyOpen = false; $('.history-pane')?.classList.remove('is-open'); }
  function historyMarkup() { return `<aside class="history-pane ${state.historyOpen ? 'is-open' : ''}" aria-label="对话历史">${btn(`${assistantIcon('plus')} 新对话`, 'new-chat')}<div class="history-items">${state.threads.length ? state.threads.map(t => `<div class="history-item ${t.instance !== 'none' ? 'has-context' : ''}" aria-current="${t.id === state.threadId}"><button data-assistant-action="open-thread" data-id="${t.id}"><span class="history-name"><strong>${e(t.title)}</strong>${threadBadge(t) ? `<span class="chat-badge">${threadBadge(t)}</span>` : ''}</span>${t.instance === 'none' ? '' : `<small>${e(contextLabel(t.instance).split(' · ')[0])}</small>`}</button></div>`).join('') : '<p class="history-note">从一个问题开始。对话会留在这里。</p>'}</div></aside>`; }
  function chatPage() {
    const t = thread(), b = binding(), currentInstance = t?.instance ?? state.instance, mode = t?.mode ?? state.mode;
    const ready = !!b.service?.enabled && !!b.model;
    return heading('AI 助手', '一起整理世界，找到问题的线索。', iconBtn('settings', '打开设置中的 AI 分类', 'settings')) + `<div class="chat-layout">${historyMarkup()}<section class="conversation page-surface acrylic${t?.repair ? ' repair-active' : ''}" aria-label="助手对话">${t?.repair ? AiRepairDesign.header(t.repair) : `<div class="conversation-toolbar"><strong>${e(t?.title || '新对话')}</strong><span></span></div>`}<div class="transcript" role="log" aria-label="对话记录" aria-live="off">${t?.repair ? AiRepairDesign.transcript(t.repair, ready) : t?.messages.length ? t.messages.map(m => messageMarkup(m) + (pendingQuestion()?.id === m.id ? questionCard() : '')).join('') : welcome(ready)}</div>${btn('回到最新消息 ↓', 'scroll-bottom', state.followBottom ? 'hidden' : '', 'scroll-bottom')}${(t?.repair?.busy || (activeHere() && !['confirming','awaiting_user'].includes(findMessage(state.activeTurn.messageId).m.status))) ? '<div class="response-dots" role="status" aria-label="响应中"><i></i><i></i><i></i></div>' : ''}${approvalBanner()}<form id="chat-form" class="composer"><div class="compose-context">${t?.repair ? `<button type="button" class="repair-instance text-action" data-assistant-action="repair-instance" aria-label="查看关联实例：${e(t.repair.instanceName)}">${assistantIcon('home')}${e(t.repair.instanceName)}</button>` : `<div class="context-choice">${select('instance', '下一轮实例上下文', instances, currentInstance)}</div>`}<div class="mode-choice">${select('mode', '操作模式', [['read', '安全'], ['operate', '允许全部']], mode, !!t?.repair)}</div>${modelButton()}${iconBtn('image', '添加图片', 'add-reference', draftReferences().length >= 3 ? 'disabled' : '')}<input id="chat-image-picker" type="file" accept="image/png,image/jpeg,image/webp" multiple hidden></div>${referenceMarkup(draftReferences(), true)}<label class="visually-hidden" for="question">发送给助手的消息</label><textarea id="question" name="question" placeholder="描述你想做的事…" rows="2">${e(draft())}</textarea><div class="composer-bottom"><span class="composer-help">Enter 发送消息，Shift+Enter 换行</span><div class="compose-actions">${(t?.repair?.busy || activeHere()) ? btn(`${assistantIcon('stop')}`, t?.repair ? 'repair-stop' : 'stop', 'aria-label="停止" title="停止"') : ''}<button class="small-button primary" data-assistant-action="send" type="submit" ${!ready || (!draft().trim() && !draftReferences().length) ? 'disabled' : ''}>${assistantIcon('arrow')} ${activeHere() ? '排队发送' : '发送'}</button></div></div></form></section></div>`;
  }
  function welcome(ready) { return `<div class="welcome ${ready ? 'is-ready' : 'needs-service'}"><div class="welcome-icon">${assistantIcon('spark')}</div><span class="pixel-label">LET’S FIGURE IT OUT</span><h2>${ready ? '想为你的世界做些什么？' : '连接你的模型，从这里开始。'}</h2><p>${ready ? '可以聊一个问题，也可以让助手沿着明确的实例、文件与来源帮你处理事情。' : '使用你自己的在线模型服务。服务没有模型列表时，仍可手工填写。'}</p>${!ready ? btn('前往设置，添加模型服务', 'manage-services', '', 'text-action') : ''}${ready ? `<div class="suggestions">${[['看看这个实例的运行配置', '读取版本、Java 与内存，说明依据'], ['帮我查找适合这个实例的模组', '查找内容，审核后再安装'], ['读取我授权目录中的启动日志', '按需访问已选择的本地目录']].map(([q, note]) => `<button data-assistant-action="suggest" data-question="${e(q)}"><span><strong>${e(q)}</strong><small>${note}</small></span>${assistantIcon('arrow')}</button>`).join('')}</div>` : ''}</div>`; }
  function markdownSampleMarkup() {
    return '<h2>实例运行建议</h2><p>先保留当前配置，再根据实际表现逐项调整。</p><table><thead><tr><th>项目</th><th>当前值</th><th>建议与原因</th></tr></thead><tbody><tr><td>Java</td><td>25</td><td>使用与当前游戏版本匹配的运行时</td></tr><tr><td>内存</td><td>6 GiB</td><td>先观察真实占用，避免为所有实例配置同样的大数值</td></tr><tr><td>渲染距离</td><td>12 区块</td><td>根据帧率与世界复杂度决定是否调整</td></tr></tbody></table><h3>下一步</h3><ol><li>打开实例运行环境，核对 Java。</li><li>保存后运行一次，检查实际日志。</li></ol><blockquote>这里只展示模拟内容，用于核验排版。</blockquote>';
  }
  function messageMarkup(m) {
    if (m.status === 'incomplete' && m.capabilityNotice) return `<article class="capability-notice" role="status" data-message="${m.id}">${assistantIcon('plug')}<span>${e(m.capabilityNotice)}</span>${btn(m.recovery.label, m.recovery.action, `data-id="${m.id}"`, 'text-action')}</article>`;
    const visibleTools = (m.tools || []).filter(tool => tool.name !== '加载技能');
    const job = state.jobs.find(j => j.id === m.jobId);
    const tools = visibleTools.length ? `<details class="operation-process"><summary>${m.status === 'completed' ? '已完成' : '已记录'} ${visibleTools.length} 项操作<span>查看过程</span>${assistantIcon('chevron')}</summary><div class="process-content">${visibleTools.map(toolMarkup).join('')}${job?.state === 'completed' ? jobMarkup(job) : ''}</div></details>` : '';
    const retry = !m.jobId && ['failed', 'interrupted'].includes(m.status) ? btn('继续此目标', 'retry-message', `data-id="${m.id}"`, 'text-action') : '';
    return `<article class="message ${m.role}" data-message="${m.id}" tabindex="0"><header><strong>${m.role === 'user' ? '你' : 'AI 助手'}</strong><div class="message-actions">${iconBtn('copy', '复制这条消息', 'copy-message', `data-id="${m.id}"`)}</div></header>${m.answerReceipt ? `<section class="chat-user-question" aria-label="问答"><small>问题</small><div class="chat-question-body"><p>${e(m.answerReceipt.question)}</p>${m.answerReceipt.skipped ? '<small>已跳过</small>' : `<div class="chat-question-answer"><small>你的回答</small><p>${e(m.answerReceipt.answer)}</p></div>`}</div></section>` : ''}<div class="message-text ${m.markdownSample ? 'markdown-body' : ''}">${m.markdownSample && m.status === 'completed' ? markdownSampleMarkup() : e(m.text)}</div>${referenceMarkup(m.images || [])}${tools}${job && job.state !== 'completed' ? jobMarkup(job) : ''}${m.status && !['completed', 'incomplete', 'confirming', 'awaiting_user', 'generating'].includes(m.status) ? `<div class="status-line ${['failed', 'unknown'].includes(m.status) ? 'error' : ''}">${e(['failed', 'interrupted'].includes(m.status) ? '回复中断' : m.status === 'queued' ? '排队中' : (m.statusText || m.status))}</div>` : ''}${['failed', 'interrupted'].includes(m.status) && m.statusText ? `<details class="message-error-details"><summary>错误详情</summary><pre>${e(m.statusText)}</pre></details>` : ''}${m.recovery ? btn(m.recovery.label, m.recovery.action, `data-id="${m.id}"`, 'text-action') : ''}${m.status === 'queued' ? btn('立即发送', 'send-queued-now', `data-id="${m.id}"`, 'text-action') + btn('取消排队', 'cancel-queued', `data-id="${m.id}"`, 'text-action') : ''}${retry}</article>`;
  }
  function toolMarkup(tool) { return `<details class="tool-card"><summary>${assistantIcon(tool.icon || 'settings')}<span>${e(tool.name)}</span><small>${e(tool.state || '已完成')}</small><time>${e(tool.elapsed || '')}</time></summary><div class="tool-content"><p>${e(tool.description)}</p>${tool.path ? `<div class="path-block">${e(tool.path)}</div>` : ''}${tool.result ? `<pre>${e(tool.result)}</pre>` : ''}${tool.links ? `<div class="result-links">${tool.links.map(([label, url]) => `<a href="${e(url)}" target="_blank" rel="noopener noreferrer">${e(label)} ↗</a>`).join('')}</div><p>来源链接供设计评审；本页未请求这些站点。检索日期、摘录与版本为模拟。</p>` : ''}</div></details>`; }
  function pendingApproval() { return thread()?.messages.find(m => m.confirm && state.activeTurn?.messageId === m.id); }
  function approvalBanner() {
    const m = pendingApproval(); if (!m) return '';
    const c = m.confirm, permission = c.kind === 'permission';
    const approve = btn(c.stale ? '重新审核' : '批准', c.stale ? 'refresh-review' : 'confirm-operation', `data-id="${m.id}"`, 'primary');
    const allowTool = permission && !c.stale ? `<details class="approval-split-menu"><summary class="small-button primary" aria-label="批准选项">⌄</summary><div role="menu">${btn(`允许本会话内所有 ${e(c.tool)} 命令`, 'confirm-tool', `data-id="${m.id}" role="menuitem"`)}</div></details>` : '';
    return `<section class="approval-banner" aria-label="待审批"><div class="approval-copy"><strong>${e(c.title)}</strong><small>${e(c.tool || c.target)}</small></div><div class="approval-actions">${btn('查看详情', 'review-details', `data-id="${m.id}"`, 'text-action')}<div class="approval-split">${approve}${allowTool}</div>${btn('拒绝', 'deny-operation', `data-id="${m.id}"`)}</div></section>`;
  }
  function confirmationMarkup(m) { const c = m.confirm; return `<section class="operation-review"><div class="tool-content"><h3>${e(c.title)}</h3><p>${e(c.description)}</p><div class="path-block">${e(c.target)}</div><div class="diff"><div><small>当前值</small><pre>${e(c.before)}</pre></div><div><small>确认后</small><pre>${e(c.after)}</pre></div></div><p>${e(c.fingerprint)} · 变更前再次核对目标</p>${c.kind === 'permission' ? '<p>主按钮仅批准当前调用；菜单可允许本会话内所有同名工具，切换权限模式后失效。目标和目录范围仍会核对。</p>' : ''}${c.stale ? '<p class="field-error">目标已改变，当前审核已过期。重新读取后才能应用。</p>' : ''}<details class="review-options"><summary>设计验证</summary>${btn('模拟目标已变化', 'stale-review', `data-id="${m.id}"`)}</details></div></section>`; }
  function approvalDetails(id) {
    const { m } = findMessage(id); if (!m?.confirm) return;
    m.confirm.reviewed = !m.confirm.stale; render();
    const trigger = $(`[data-assistant-action="review-details"][data-id="${id}"]`);
    openDialog('查看详情', confirmationMarkup(m), btn('关闭', 'close-dialog'), { single: true, type: 'approval-details' });
    state.modalTrigger = trigger;
  }
  function jobMarkup(job) { if (!job) return ''; const running = job.state === 'running'; return `<section class="job-line" data-job="${job.id}"><div class="tool-content"><strong>${e(job.title.replace(' · 示例任务',''))}</strong><p>${running ? '下载与安装中' : e(job.stateLabel)}</p>${running ? `<div class="job-progress" role="progressbar" aria-label="任务进度" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${job.progress}"><span style="width:${job.progress}%"></span></div><p>${job.progress} / 100 MB</p>` : ''}<div class="inline-actions">${running ? btn('取消任务', 'cancel-job', `data-id="${job.id}"`, 'text-action') : job.state !== 'completed' ? btn('重试任务', 'retry-job', `data-id="${job.id}"`, 'text-action') : ''}${btn('查看详情', 'job-detail', `data-id="${job.id}"`, 'text-action')}</div>${running ? `<details class="review-options"><summary>设计验证</summary>${btn('模拟连接中断', 'fail-job', `data-id="${job.id}"`, 'text-action')}</details>` : ''}</div></section>`; }
  function settingsMarkup() { return '<div class="assistant-settings" id="assistant-settings"></div>'; }
  function settingsContent() {
    const sections = [['defaults',defaultsPage],['services',servicesPage],['capabilities',capabilitiesPage],['integrations',integrationsPage]];
    return `<div class="settings-content ai-unified-settings">${sections.map(([id,body]) => `<section class="ai-settings-section" id="ai-section-${id}" data-setting-id="ai.${id}">${body()}</section>`).join('')}</div>`;
  }
  function openSettings(section = 'services') {
    closeDialog(); state.section = section; navigate('settings','ai');
    requestAnimationFrame(() => document.getElementById(`ai-section-${section}`)?.scrollIntoView({block:'start'}));
  }
  function settingsExpander(id, title, description, body, commands = '', open = false, icon = '') {
    const expanded = expandedSettings.has(id) ? expandedSettings.get(id) : open;
    return `<details class="settings-expander" data-expander="${e(id)}" ${expanded ? 'open' : ''}><summary class="settings-expander-header">${icon?`<span class="settings-expander-icon">${assistantIcon(icon)}</span>`:""}<span class="settings-expander-copy"><span class="settings-expander-title">${e(title)}</span>${description ? `<small>${e(description)}</small>` : ''}</span><span class="settings-expander-commands">${commands}</span><span class="settings-expander-chevron">${assistantIcon('chevron')}</span></summary><div class="settings-expander-body">${body}</div></details>`;
  }

  function defaultsPage() { return `<header><h2>对话默认值</h2></header><div class="ai-setting-surface">${settingRow('默认模式', select('default-mode','默认模式',[['operate','允许全部'],['read','安全模式']],state.defaultMode), '用于新建普通对话；已有对话保留当前模式。')}</div>`; }

  function servicesPage() {
    const modelRows = service => service.models.map(model => `<li><div class="provider-model-copy"><span>${e(model.name)}</span>${service.protocol === 'inference' || model.name !== model.code ? `<small>${e(model.code)}</small>` : ''}</div><div class="provider-model-actions">${iconBtn('edit', `编辑模型 ${model.name}`, 'edit-model', `data-id="${service.id}" data-model="${model.id}"`)}${iconBtn('trash', `移除模型 ${model.name}`, 'remove-model', `data-id="${service.id}" data-model="${model.id}"`)}</div></li>`).join('');
    const locals = [];
    const localGroup = locals.length ? settingsExpander('local-models', '本地模型', '', `<ul class="provider-models">${locals.map(modelRows).join('')}</ul>`, btn('添加模型', 'add-local-model', '', 'small-button'), true, 'server') : '';
    const online = state.services.filter(service => service.protocol !== 'inference').map(service => settingsExpander(`provider-${service.id}`, service.name, service.url, `<ul class="provider-models">${modelRows(service)}</ul>`, `${btn('添加模型', 'edit-model', `data-id="${service.id}"`, 'small-button')}${iconBtn('refresh', `刷新 ${service.name} 的模型`, 'refresh-service-models', `data-id="${service.id}"`)}${iconBtn('edit', `编辑服务 ${service.name}`, 'open-service', `data-id="${service.id}"`)}${iconBtn('trash', `移除服务 ${service.name}`, 'remove-service', `data-id="${service.id}"`)}`, true, 'server')).join('');
    return `<header class="services-heading"><h2>模型服务</h2>${btn(`${assistantIcon('plus')} 添加服务`, 'open-service')}</header><div class="provider-list">${localGroup}${online}${!state.services.length ? '<div class="ai-setup-empty"><span>'+assistantIcon('server')+'</span><p>添加一个服务，选择要使用的模型。</p></div>' : ''}</div>`;
  }

  function settingsMenu(label, items) { return `<details class="service-options"><summary aria-label="${e(label)}">${assistantIcon('more')}</summary><div class="service-options-menu">${items}</div></details>`; }
  function settingRow(name, controls = '', secondary = '', attrs = '', artwork = '') { return `<div class="ai-preference-row" ${attrs}>${artwork}<div class="ai-preference-copy">${name}${secondary ? `<small>${secondary}</small>` : ''}</div><div class="ai-preference-actions">${controls}</div></div>`; }
  function capabilitiesPage() {
    const builtins = settingRow('应用包数据', '<span class="ai-setting-value">只读 · 内置</span>', '当前应用包的数据目录（含日志）') + settingRow('数据目录', '<span class="ai-setting-value">可读写 · 内置</span>', '当前启动器数据根');
    const directories = builtins + state.files.roots.map(root => settingRow(e(root.name), `<span class="ai-setting-value">${root.access === 'write' ? '可读写' : '只读'}</span>${settingsMenu(`管理目录 ${root.name}`, btn(root.access === 'write' ? '改为只读' : '允许写入', 'root-access', `data-id="${root.id}"`) + btn('撤销授权', 'remove-root', `data-id="${root.id}"`))}`, e(root.path), `data-root="${root.id}"`)).join('') || '<p class="ai-empty-note">选择助手可以访问的文件夹。</p>';
    return `<header><h2>文件与网络</h2></header><div class="ai-setting-surface">${settingRow('新对话开启文件访问', `<button class="toggle" role="switch" aria-label="默认开启文件访问" aria-checked="${state.accessDefaults.files}" data-assistant-action="toggle-default-files"></button>`,'新建对话可访问已授权的文件夹。')}</div><div class="ai-setting-surface">${settingRow('新对话开启网络访问', `<button class="toggle" role="switch" aria-label="默认开启网络访问" aria-checked="${state.accessDefaults.network}" data-assistant-action="toggle-default-network"></button>`,'新建对话可检索网页并读取网址。')}</div>${settingsExpander('file-roots','可访问的文件夹', `${state.files.roots.length + 2} 个文件夹`, directories, btn('添加文件夹','add-root','','small-button'), false)}`;
  }

  function integrationsPage() {
    return `<header><h2>外部 AI 工具</h2></header>${Object.entries(integrationClients).map(([id,[name]]) => {
      const item = state.integrations[id];
      return `<div class="ai-setting-surface">${settingRow(item ? btn(name, 'integration-details', `data-client="${id}"`, 'preference-name') : e(name), item ? settingsMenu(`管理 ${name} 接入`, btn('检查与更新', 'integration-plan', `data-client="${id}"`) + (item.status === 'partial' ? btn('重试接入', 'retry-integration', `data-client="${id}"`) : '') + btn('移除接入', 'integration-remove', `data-client="${id}"`)) : btn('安装', 'integration-plan', `data-client="${id}"`, 'text-action'), item?.status === 'partial' ? '<span class="field-error">接入未完成</span>' : item?.status === 'preserved' ? '用户修改已保留' : `安装启动器技能与 MCP，供 ${name} 操作启动器。`, `data-integration="${id}"`, `<span class="ai-client-icon" aria-hidden="true" style="--client-logo:url('${new URL(id + '.svg', clientArtworkBase).href}')"></span>`)}</div>`;
    }).join('')}`;
  }
  const integrationPath = client => `%USERPROFILE%/${integrationClients[client][1]}/skills/rodel-minecraft/`;
  function openDialog(title, body, footer, options = {}) {
    closeCombo();
    if (!state.modal) state.modalTrigger = document.activeElement;
    document.getElementById('assistant-modal-root').innerHTML = `<dialog class="assistant-dialog" aria-labelledby="dialog-title"><div class="dialog-frame"><header class="dialog-header"><h2 id="dialog-title">${title}</h2>${options.header || ''}</header><div class="dialog-body">${body}</div><footer class="dialog-footer ${options.single ? 'single' : ''}">${footer}</footer></div></dialog>`;
    state.modal = options.type || title;
    const d = $('dialog'); d.addEventListener('cancel', event => { event.preventDefault(); serviceDraft = null; closeDialog(); }); d.showModal(); decorate();
    const focus = d.querySelector('[autofocus]') || d.querySelector('input:not([type=checkbox]),textarea') || d.querySelector('.dialog-footer button'); focus?.focus();
  }
  function closeDialog() { closeCombo(); $('dialog')?.close(); document.getElementById('assistant-modal-root').innerHTML = ''; state.modal = null; const target = state.modalTrigger; if (target?.isConnected) target.focus(); else document.querySelector('#assistant-content button')?.focus(); }
  const cancelButton = () => btn('取消', 'close-dialog');
  let serviceDraft = null;
  const serviceDefaults = protocol => ({ 'openai-responses': 'https://api.example.com/v1', 'openai-chat': 'https://api.example.com/v1', anthropic: 'https://api.anthropic.example', gemini: 'https://generativelanguage.example', inference: '' }[protocol]);
  function serviceDialog(id = '', back = false) {
    const existing = state.services.find(x => x.id === id);
    if (!back) serviceDraft = { id, protocol: existing?.protocol || 'openai-responses', url: existing?.url || serviceDefaults('openai-responses'), name: existing?.name || '', credential: existing?.credential || 'example', secret: '', enabled: existing?.enabled ?? true, scenario: 'success', selected: ['rodel-chat'], manual: '', manualName: '', manualMode: false };
    const d = serviceDraft;
    openDialog(existing ? '编辑模型服务' : '添加服务', `<form id="service-form" data-step="connection"><div class="service-fields">${field('显示名称', 'service-name', d.name, '留空使用服务名称', 'text', 'autofocus')}<div class="form-field"><label>模型服务</label>${select('service-kind', '模型服务类型', serviceKinds, serviceKind(d.protocol))}</div><div class="form-field" data-openai-api ${serviceKind(d.protocol) === 'openai' ? '' : 'hidden'}><label>OpenAI API 类型</label>${select('openai-api', 'OpenAI API 类型', [['openai-responses','Responses API'],['openai-chat','Chat Completions API']], d.protocol.startsWith('openai-') ? d.protocol : 'openai-responses')}</div>${field('API 密钥', 'credential-value', d.secret, '原型勿填真实密钥', 'password', 'autocomplete="off"')}<div class="form-field" data-custom-endpoint><label for="custom-service-url">${d.protocol === 'inference' ? '模型文件' : '服务地址'}</label><div class="ai-path-input"><input id="custom-service-url" name="custom-service-url" placeholder="${d.protocol === 'inference' ? '选择 GGUF 模型文件' : ''}" value="${e(d.url)}" ${d.protocol === 'inference' ? 'readonly' : ''}>${iconBtn('folder', '选择模型文件', 'pick-local-model', d.protocol === 'inference' ? '' : 'hidden')}</div></div></div><div id="service-error" class="service-infobar" role="alert"></div></form>`, `${cancelButton()}<button type="submit" form="service-form" class="small-button primary">${existing ? '保存' : d.protocol === 'inference' ? '添加' : '下一步'}</button>`, { type: 'service' });
    $('[name="credential-value"]').disabled = d.protocol === 'inference' || d.credential === 'none';
    $('[name="credential-value"]').parentElement.hidden = d.protocol === 'inference' || d.credential === 'none';
  }
  function captureServiceConnection(form) {
    const data = new FormData(form), kind = String(data.get('service-kind')), protocol = kind === 'openai' ? String(data.get('openai-api')) : kind;
    const url = String(data.get('custom-service-url')).trim();
    let parsed; try { parsed = kind === 'inference' ? null : new URL(url); } catch { $('#service-error').textContent = '请输入完整的 http 或 https 地址。'; return false; }
    if (parsed && (!['http:', 'https:'].includes(parsed.protocol) || parsed.username || parsed.password)) { $('#service-error').textContent = '地址须为 HTTP(S)，且不能内嵌账号或口令。'; return false; }
    Object.assign(serviceDraft, { protocol, url, name: String(data.get('service-name')).trim() || serviceKinds.find(x => x[0] === serviceKind(protocol))[1], credential: protocol === 'inference' ? 'none' : data.get('credential-value') ? 'example' : serviceDraft.credential, secret: String(data.get('credential-value') || '') });
    return true;
  }
  function serviceModelsStep(loading = false) {
    const d = serviceDraft;
    const message = { auth: '密钥验证未通过，可以返回修改，或手动填写后保存。', offline: '暂时无法连接，可以返回重试，或手动填写模型后保存。', empty: '服务没有返回模型列表，可手动填写模型标识。' }[d.scenario];
    if (d.protocol === 'inference' && d.url && !d.selected.includes(d.url)) d.selected = [d.url];
    const choices = (d.protocol === 'inference' ? [[d.url, d.url.split(/[\\/]/).pop() || '本地模型']] : [['text-preview', 'Text Preview'], ['rodel-chat', 'Rodel Chat']]).sort((a,b) => a[1].localeCompare(b[1]) || a[0].localeCompare(b[0]));
    openDialog(d.refresh ? '选择模型' : '添加服务', `<form id="service-models-form"><p>${e(d.name)}</p>${loading ? '<p role="status">正在获取模型列表…</p>' : `${message ? `<div class="${d.scenario === 'empty' ? 'service-notice' : 'service-infobar'}" role="${d.scenario === 'empty' ? 'status' : 'alert'}">${message}</div>` : `<div class="discovered-models">${choices.map(([code,name]) => `<label class="discovered-model"><input type="checkbox" name="discovered-model" value="${code}" ${d.selected.includes(code) ? 'checked' : ''}><span>${name}</span></label>`).join('')}</div>`}${!message ? `<div class="manual-model-entry" ${d.manualMode ? 'hidden' : ''}>${btn(`${assistantIcon('plus')} 手动添加模型`, 'show-manual-model', 'aria-controls="manual-model-input"', 'inline-add')}</div>` : ''}<div class="manual-model" id="manual-model-input" ${d.manualMode || message ? '' : 'hidden'}><div class="manual-model-field">${field('显示名称', 'model-display-name', d.manualName || '', '留空使用模型标识')}${d.protocol === 'inference' ? `<div class="form-field"><label for="field-model-code">模型文件</label><div class="ai-path-input"><input id="field-model-code" name="model-code" value="${e(d.manual)}" readonly placeholder="选择 GGUF 模型文件">${iconBtn('folder','选择模型文件','pick-local-model-code')}</div></div>` : field('模型标识', 'model-code', d.manual, '模型 ID、部署名称或 Profile', 'text', message ? 'required' : '')}</div>${!message ? iconBtn('trash', '移除自定义模型输入', 'hide-manual-model') : ''}</div><div id="service-error" class="service-infobar" role="alert"></div>`}</form>`, `${d.refresh ? cancelButton() : btn('上一步', 'service-back')}<button type="submit" form="service-models-form" class="small-button primary" ${loading ? 'disabled' : ''}>${d.refresh ? '保存' : '添加'}</button>`, { type: 'service-models', header: btn('刷新模型', 'reload-service-models', loading ? 'disabled' : '') });
    if (!loading) { const first = $('[name="discovered-model"]') || $('[name="model-code"]'); first?.focus(); }
    if (loading) { const dialog = $('dialog'); setTimeout(() => { if (dialog?.isConnected && serviceDraft === d) serviceModelsStep(); }, 650); }
  }
  function saveService(form) {
    if (!captureServiceConnection(form)) return;
    const d = serviceDraft, existing = state.services.find(x => x.id === d.id);
    if (existing) { Object.assign(existing, { name:d.name, protocol:d.protocol, url:d.url, credential:d.credential, enabled:d.enabled, test:'unknown' }); serviceDraft = null; closeDialog(); render(); notice('服务连接已保存，已有模型保持不变。'); return; }
    if (d.protocol === 'inference') {
      if (!d.url) { $('#service-error').textContent = '请选择 GGUF 模型文件。'; return; }
      let local = state.services.find(service => service.protocol === 'inference' && service.enabled);
      if (local?.models.some(model => model.code === d.url)) { $('#service-error').textContent = '此模型文件已添加。'; return; }
      if (!local) { local = { id:nextId('service'), name:'本地模型', protocol:'inference', url:'', credential:'none', enabled:true, models:[] }; state.services.push(local); }
      const model = { id:nextId('model'), code:d.url, name:d.name === '本地模型' ? d.url.split(/[\\/]/).pop().replace(/\.gguf$/i, '') : d.name, source:'custom' };
      local.models.push(model);
      const key = `${local.id}/${model.id}`;
      if (!state.defaultBinding) state.defaultBinding = key;
      if (!currentBinding()) state.binding = key;
      serviceDraft = null; closeDialog(); render(); notice('本地模型已添加。'); return;
    }
    serviceModelsStep(true);
  }
  function refreshServiceModels(serviceId) {
    const service = state.services.find(s => s.id === serviceId);
    serviceDraft = { id: serviceId, refresh: true, name:service.name, protocol:service.protocol, url:service.url, scenario:'success', selected:service.models.map(m => m.code), manual:'', manualName:'', manualMode:false };
    serviceModelsStep(true);
  }
  function captureModelChoices() {
    const form = $('#service-models-form');
    if (!form?.querySelector('.manual-model')) return;
    serviceDraft.selected = [...form.querySelectorAll('[name="discovered-model"]:checked')].map(x => x.value);
    serviceDraft.manual = String(form.querySelector('[name="model-code"]')?.value || '').trim();
    serviceDraft.manualName = String(form.querySelector('[name="model-display-name"]')?.value || '').trim();
    serviceDraft.manualMode = !form.querySelector('.manual-model').hidden;
  }
  function saveServiceModels() {
    captureModelChoices(); const d = serviceDraft;
    const codes = [...new Set([...(['auth','offline','empty'].includes(d.scenario) ? [] : d.selected), ...(d.manual ? [d.manual] : [])])];
    if (d.manualMode && !d.manual && d.manualName) { $('#service-error').textContent = '请填写自定义模型的标识。'; $('[name="model-code"]').focus(); return; }
    const existing = d.refresh ? state.services.find(s => s.id === d.id) : null;
    if (!codes.length && !existing?.models.some(model => model.source === 'custom' || !['rodel-chat','text-preview'].includes(model.code))) { $('#service-error').textContent = '选择一个模型，或手动填写模型标识。'; return; }
    const models = codes.map(code => existing?.models.find(model => model.code === code) || ({ id:nextId('model'), code, name:code === d.manual ? d.manualName || code : code === 'rodel-chat' ? 'Rodel Chat' : code === 'text-preview' ? 'Text Preview' : code, source:code === d.manual ? 'custom' : 'discovered' }));
    if (existing) {
      for (const model of existing.models) if ((model.source === 'custom' || !['rodel-chat','text-preview'].includes(model.code)) && !models.some(m => m.code === model.code)) models.push(model);
      existing.models = models;
      if (state.defaultBinding.startsWith(`${existing.id}/`) && !models.some(model => state.defaultBinding === `${existing.id}/${model.id}`)) state.defaultBinding = '';
      serviceDraft = null; closeDialog(); render(); notice('模型列表已更新。'); return;
    }
    const item = { id:nextId('service'), name:d.name, protocol:d.protocol, url:d.url, credential:d.credential, enabled:d.enabled, test:['auth','offline'].includes(d.scenario) ? d.scenario : 'unknown', models };
    state.services.push(item); const key = `${item.id}/${models[0].id}`;
    if (!state.defaultBinding && item.enabled) state.defaultBinding = key;
    if (!currentBinding() && item.enabled) { if (thread()) thread().binding = key; else state.binding = key; }
    serviceDraft = null; closeDialog(); render(); notice(`已添加 ${models.length} 个模型。`);
  }
  function newThread() { state.threadId = null; state.draft = ''; state.references = []; state.instance = 'none'; state.mode = state.defaultMode; state.modeChosen = false; state.binding = state.defaultBinding; state.files.enabled = state.accessDefaults.files; state.network.enabled = state.accessDefaults.network; state.historyOpen = false; state.followBottom = true; state.view = 'chat'; render(); $('#question')?.focus(); }
  function ensureThread(question) { let t = thread(); if (!t) { t = { id: nextId('chat'), title: question.slice(0, 24), instance: state.instance, mode: state.mode, binding: state.binding, access: { files:state.files.enabled, network:state.network.enabled }, draft: '', references: state.references, messages: [] }; state.threads.unshift(t); state.threadId = t.id; } return t; }
  function addTool(m, name, description, result, options = {}) { m.tools ||= []; if (m.tools.some(t => t.name === name && t.result === result && t.path === options.path)) return; m.tools.push({ name, description, result, elapsed: '0.0 秒', ...options }); }
  function finish(m, status = 'completed', text = '') { if (state.activeTurn?.messageId !== m.id) return; if (state.activeTurn.timer) clearTimeout(state.activeTurn.timer); m.status = status; if (text) m.statusText = text; const t = findMessage(m.id).t; t.unread = t.id !== state.threadId || document.hidden; state.activeTurn = null; render(); queueMicrotask(() => { if (!busy() && state.queued.length) { const next = state.queued.shift(); startTurn(next.t, next.m, next.fault); } }); }
  function stream(m, text, final = 'completed') {
    if (state.activeTurn?.messageId !== m.id) return;
    // Simulated model-request boundary after the current scenario's completed tool batch.
    const queued = m.tools?.length && state.queued.find(item => item.t.id === state.activeTurn.threadId);
    if (queued) { state.queued = [queued, ...state.queued.filter(item => item !== queued)]; finish(m); return; }
    m.status = 'generating'; m.statusText = '正在生成回答…'; let pos = 0;
    const step = () => {
      if (state.activeTurn?.messageId !== m.id) return;
      pos = Math.min(pos + 7, text.length); m.text = text.slice(0, pos);
      const node = document.querySelector(`[data-message="${m.id}"] .message-text`); if (node) { node.textContent = m.text; if (state.followBottom) { const ts = $('.transcript'); if (ts) ts.scrollTop = ts.scrollHeight; } }
      if (pos < text.length) state.activeTurn.timer = setTimeout(step, 45); else finish(m, final);
    };
    render(); step();
  }
  function send(questionOverride = '') {
    const question = (questionOverride || draft()).trim(), b = binding();
    if ((!question && !draftReferences().length) || !b.service?.enabled || !b.model) return;
    if (thread()?.repair) { AiRepairDesign.ask(thread().repair, question, [...draftReferences()], render); thread().draft = ''; thread().references = []; state.references = []; render(); return; }
    const images = [...draftReferences()];
    const t = ensureThread(question || '图片'); t.draft = ''; t.references = []; state.draft = ''; state.references = [];
    const snapshot = { service: b.service.name, model: b.model.name, instance: t.instance, mode: t.mode, fileEnabled: state.files.enabled, networkEnabled: state.network.enabled, skills: state.skills.filter(s => s.enabled && s.valid).map(s => s.name) };
    const user = { id: nextId('message'), role: 'user', text: question, snapshot, images };
    const m = { id: nextId('message'), role: 'assistant', text: '', snapshot, question, status: 'generating', statusText: '正在连接示例服务…', tools: [] };
    t.messages.push(user, m); const fault = state.fault; state.fault = 'none'; if (busy()) { m.status = 'queued'; state.queued.push({ t, m, fault }); state.followBottom = true; render(); return; } startTurn(t, m, fault);
  }
  function startTurn(t, m, fault = 'none') {
    const question = m.question; m.status = 'generating'; state.activeTurn = { id: nextId('turn'), threadId: t.id, messageId: m.id, timer: null }; state.followBottom = true; render(); $('#question')?.focus();
    state.activeTurn.timer = setTimeout(() => {
      if (!state.activeTurn || state.activeTurn.messageId !== m.id) return;
      if (fault === 'network' || question.includes('模拟断流')) { m.text = '我已收到你的问题，正在核对现有信息。'; finish(m, 'failed', '连接在回答中断开。保留已有内容，可以重试；未执行任何工具。'); return; }
      if (fault === 'interrupted') { finish(m, 'interrupted', '宿主意外退出。本轮已中断，未自动重放操作。'); return; }
      if (fault === 'storage') { m.text = '记录写入失败，本轮已停止后续工具调度。已有记录仍可查看。'; finish(m, 'failed', '检查数据目录可写状态后再重试。本示例未提交任何工具。'); return; }
      if (fault === 'unknown') {
        addTool(m, '恢复待核对的操作', '示例重启恢复 · 操作意图已持久化，返回结果尚未确认', 'intent: committed\nrecoveryKey: demo-job-42\n状态：unknown_effect\n禁止自动重新提交', { icon: 'warning', state: '效果未知', open: true });
        m.text = '原操作已提交，但响应丢失。需要先查询实际状态，再决定剩余工作，不能直接重新执行。';
        m.recovery = { label: '核对原操作状态', action: 'reconcile' };
        finish(m, 'unknown', '保留目标与恢复查询键；等待核对。'); return;
      }
      runScenario(m);
    }, 900);
  }
  function resumePending(id = '') {
    if (busy()) return;
    const pair = id ? findMessage(id) : { t: thread(), m: thread()?.messages.at(-1) };
    const { t, m } = pair;
    if (!t || !m || m.role !== 'assistant' || m.status !== 'incomplete' || t.messages.at(-1) !== m) return;
    const b = binding(t.binding), q = m.question;
    if (!b.service?.enabled || !b.model) return;
    const wantsFile = /文件|日志|目录/.test(q), writesFile = /写入|修改文件|保存到|创建文件/.test(q);
    const wantsWeb = /更新|网页|网址|https?:|网络|检索/.test(q) && !/安装模组/.test(q);
    const wantsInstall = /模组|安装/.test(q), wantsSettings = /内存|参数|配置/.test(q);
    if (wantsFile) {
      if (!builtinMcp('files') || !state.files.enabled || !state.files.roots.length) return;
      if (writesFile && state.files.roots[0].access !== 'write') return;
      if (writesFile && t.mode !== 'operate' && m.recovery?.action === 'allow-retry') return;
    } else if (wantsWeb) {
      if (!state.network.enabled || !builtinMcp('web')) return;
      const urls = (q.match(/https?:\/\/[^\s<>"，。]+/g) || []).map(v => { try { return new URL(v); } catch { return null; } }).filter(Boolean);
      if (urls.some(url => ['localhost','127.0.0.1','[::1]'].includes(url.hostname) && !(m.allowedLocalOrigins || []).includes(url.origin))) return;
    } else if (wantsInstall || wantsSettings) {
      if (t.instance === 'none' || (wantsInstall && !state.network.enabled)) return;
      if ((wantsInstall || /改|调整|设置为/.test(q)) && t.mode !== 'operate' && m.recovery?.action === 'allow-retry') return;
    }
    m.authorizationRevision = (m.authorizationRevision || 0) + 1;
    m.snapshot = { ...m.snapshot, authorizationRevision: m.authorizationRevision, service: b.service.name, model: b.model.name, instance: t.instance, mode: t.mode, fileEnabled: state.files.enabled, networkEnabled: state.network.enabled };
    m.recovery = null; m.capabilityNotice = null; m.text = ''; m.status = 'generating'; m.statusText = '正在继续处理…';
    state.activeTurn = { id: nextId('turn'), threadId: t.id, messageId: m.id, timer: null };
    render();
    state.activeTurn.timer = setTimeout(() => { if (state.activeTurn?.messageId === m.id) runScenario(m); }, 250);
  }
  function requireCapability(m, message, label, action) { m.text = message; m.capabilityNotice = action === 'focus-instance' ? '请先选择目标实例' : action === 'choose-model' ? '当前模型不支持工具调用' : action === 'capabilities' ? (/文件|目录/.test(message) ? '需要设置文件访问范围' : '需要设置网络访问范围') : action === 'allow-retry' ? '此操作需要写入权限' : message; m.recovery = { label, action }; finish(m, 'incomplete', '等待所需范围配置。'); }
  function requestPermission(m, tool) {
    const { t } = findMessage(m.id);
    if (m.snapshot.mode === 'operate' || t.operationGrant || t.operationToolGrants?.includes(tool) || m.operationApproved) return false;
    m.text = '操作已准备好，等待你批准。';
    m.confirm = { kind: 'permission', tool, title: '操作权限请求', description: '批准后继续本次操作，目标和目录范围仍会核对。', target: m.snapshot.instance === 'none' ? '全局设置' : contextLabel(m.snapshot.instance), before: '尚未执行', after: `允许 ${tool}`, fingerprint: `permission-${m.id}`, scope: 'once' };
    m.status = 'confirming'; render(); return true;
  }
  function runScenario(m) {
    const q = m.question, snap = m.snapshot;
    if (/markdown|排版|表格/i.test(q)) {
      m.markdownSample = true;
      stream(m, '实例运行建议\n\n先保留当前配置，再根据实际表现逐项调整。\n\n项目 | 当前值 | 建议与原因\nJava | 25 | 使用与当前游戏版本匹配的运行时\n内存 | 6 GiB | 根据真实占用调整\n渲染距离 | 12 区块 | 根据帧率与世界复杂度判断\n\n下一步\n1. 核对运行环境。\n2. 保存后检查实际日志。');
      return;
    }
    if (/ask_user|问我|询问.*偏好/.test(q)) {
      m.prompt = { question: '你希望这次采用哪种风格？', options: [{id:'simple',label:'简洁，保留清晰轮廓'},{id:'detailed',label:'细节丰富，增加层次'}], selected: '', text: '' };
      m.status = 'awaiting_user'; m.statusText = ''; render(); return;
    }
    if (q.startsWith('核对已有任务状态，不重复操作：')) {
      addTool(m, '查询已有操作结果', '按原结果引用核对状态，不重新提交（模拟）', '已完成的设置或文件变更保持原结果。\n已有后台任务按其当前状态继续显示。', { icon: 'check' });
      stream(m, '已核对示例记录，未重放原来的修改或安装。可以继续说明尚未完成的具体部分，或在原任务卡查看进度。'); return;
    }
    const wantsWeb = /更新|网页|网址|https?:|网络|检索/.test(q) && !/安装模组/.test(q);
    const wantsFile = /文件|日志|目录/.test(q), wantsWrite = /写入|修改文件|保存到|创建文件/.test(q);
    const wantsInstall = /模组|安装/.test(q), wantsSettings = /内存|参数|配置/.test(q);
    const needsTool = wantsWeb || wantsFile || wantsInstall || wantsSettings;
    if (needsTool) addTool(m, '加载技能', '产品内置 · 启动器助手 · 本轮版本 1.1（模拟）', snap.skills.join('\n'), { icon: 'book' });
    if (wantsFile) {
      if (!builtinMcp('files')) { requireCapability(m, '内置文件工具暂不可用', '重试', 'retry-builtin-files'); return; }
      if (!state.files.enabled || !state.files.roots.length) { requireCapability(m, '文件访问尚未开放。请先选择允许访问的目录；助手只按需读取，不扫描整台电脑。', '选择文件范围', 'capabilities'); return; }
      const root = state.files.roots[0];
      if (wantsWrite) {
        if (root.access !== 'write') { requireCapability(m, `「${root.name}」目前没有可用的写入授权。需要目录允许写入，并将此会话切换为允许操作。`, '调整文件范围', 'capabilities'); return; }
        if (requestPermission(m, 'file_write')) return;
        addTool(m, '读取待修改文件', '范围：已授权目录；仅读取目标文件（模拟）', 'renderDistance=12\nentityDistance=100', { icon: 'file', path: `${root.path}/launcher-notes.txt` });
        m.text = '已准备小范围修改，其他行保持原样。请核对目标文件与差异。';
        m.confirm = { kind: 'file', title: '修改文件前核对', description: '替换一个已确认字段；写入前再次核对文件内容指纹。', target: `${root.path}/launcher-notes.txt`, before: 'renderDistance=12', after: 'renderDistance=10', fingerprint: '文件 sha256: demo-12 / 范围 1 行', rootId: root.id };
        m.confirm.reviewed = true; confirmOperation(m.id); return;
      }
      addTool(m, '读取本地文件', '文件访问 · 已授权目录 · 只读（模拟）', '[ERROR] Example dependency is missing\n此片段由原型生成，不来自本机日志。', { icon: 'file', path: `${root.path}/logs/latest.log` });
      stream(m, '日志出现一条缺少依赖的错误。可以先在内容页核对依赖清单，再决定是否补全。'); return;
    }
    if (wantsWeb) {
      if (!state.network.enabled) { requireCapability(m, '本会话尚未启用网络访问。开启后可以检索公开网页，或读取你提供的明确网址。', '开启网络访问', 'capabilities'); return; }
      const requestedUrls = (q.match(/https?:\/\/[^\s<>"，。]+/g) || []).map(value => { try { return new URL(value); } catch { return null; } }).filter(Boolean);
      if (requestedUrls.some(url => ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname) && !(m.allowedLocalOrigins || []).includes(url.origin))) { m.localOrigin = requestedUrls.find(url => ['localhost','127.0.0.1','[::1]'].includes(url.hostname) && !(m.allowedLocalOrigins || []).includes(url.origin)).origin; requireCapability(m, '此请求需要访问本机服务', '查看请求', 'review-local-origin'); return; }
      if (!builtinMcp('web')) { requireCapability(m, '内置网络工具暂不可用', '重试', 'retry-builtin-web'); return; }
      const fetching = /https?:\/\//.test(q);
      addTool(m, fetching ? '读取明确网址' : '检索服务更新', `${fetching ? '网页读取' : builtinMcp('web').name} · 查询时间 2026-10-18 14:32（模拟）`, '示例结果：发现版本说明和兼容性备注。\n未实际获取网页，不能据此判断最新版本。', { icon: 'globe', links: [['Ollama 官方发布记录 · 示例来源', 'https://github.com/ollama/ollama/releases'], ['Modrinth · 示例内容来源', 'https://modrinth.com/']] });
      stream(m, '发布说明列出了版本变化与兼容性提醒，来源已附在查询详情中。可以先核对你正在使用的版本，再决定是否更新。'); return;
    }
    if (wantsInstall || wantsSettings) {
      if (snap.instance === 'none' && (wantsInstall || /这个实例|当前实例/.test(q))) { requireCapability(m, '这项请求需要一个明确的实例。请在输入框上方选择目标后接续；普通问答无需选择实例。', '选择实例上下文', 'focus-instance'); return; }
      if (wantsInstall) {
        if (!state.network.enabled) { requireCapability(m, '查找 Modrinth 内容需要联网。请先启用网络访问，之后仍按实例版本与加载器筛选。', '开启网络访问', 'capabilities'); return; }
        addTool(m, '搜索 Modrinth 内容', '专用内容工具 · 按本轮实例版本与加载器筛选（模拟）', 'Sodium · Fabric 1.21.1\n候选文件：sodium-example.jar\n来源与依赖清单待审核', { icon: 'globe', links: [['Sodium · Modrinth 项目', 'https://modrinth.com/mod/sodium']] });
        if (requestPermission(m, 'content_apply_review')) return;
        m.text = '已生成安装清单。确认后交给后台任务下载与安装，任务结果会在这里持续显示。';
        m.confirm = { kind: 'install', title: '安装内容审核', description: '添加 1 个示例文件，无替换和删除；目标与依赖在提交前重新核对。', target: `${contextLabel(snap.instance)} / game/mods`, before: '未安装 Sodium', after: '+ sodium-example.jar\n来源：Modrinth · 示例版本', fingerprint: 'review-demo-01 / 实例修订 4' };
        m.confirm.reviewed = true; confirmOperation(m.id); return;
      }
      const scope = snap.instance === 'none' ? '全局' : '实例';
      addTool(m, `读取${scope}配置`, 'Core · 当前配置与继承来源（模拟）', `最大内存 4096 MiB\n来源：${scope}覆盖；其余参数保持不变`, { icon: 'settings' });
      if (/改|调整|设置为/.test(q)) {
        if (requestPermission(m, 'config_patch')) return;
        addTool(m, '提交设置 patch', '已获授权 · 本次明确目标 · 仅修改最大内存（模拟）', `maxMemoryMb: 4096 → 6144\n其他字段与继承关系保留\n回读：6144 MiB（${scope}覆盖）\n生效：下次启动`, { icon: 'check' });
        stream(m, '最大内存已调整为 6144 MiB，其他参数与继承关系保持不变。将在下次启动生效。'); return;
      }
      stream(m, '这个实例使用 Minecraft 1.21.1、Fabric 与 Java 21，最大内存为 4096 MiB，来自实例覆盖。\n\n当前没有足够依据认为必须调整。若有具体卡顿或启动错误，可以继续查看相关日志，再决定修改哪些参数。'); return;
    }
    stream(m, /你好|hello|hi/i.test(q) ? '你好！想聊聊 Minecraft，还是处理某个实例的问题？' : '可以，我们从你当前的目标开始。你也可以继续补充具体需求或已有信息。');
  }
  function findMessage(id) { for (const t of state.threads) { const m = t.messages.find(x => x.id === id); if (m) return { t, m }; } return {}; }
  function stopTurn() { const a = state.activeTurn; if (!a) return; const { m } = findMessage(a.messageId); clearTimeout(a.timer); m.confirm = null; finish(m, 'canceled', m.jobId ? '已停止模型回复。独立后台任务仍在运行，请在任务卡单独取消。' : '已停止本轮，保留已产生内容；不会继续调用工具。'); }
  function startJob(m) {
    const job = { id: nextId('job'), title: '安装 Sodium · 示例任务', instance: m.snapshot.instance, state: 'running', progress: 12, stateLabel: '', timer: null, messageId: m.id };
    state.jobs.push(job); m.jobId = job.id; tickJob(job); return job;
  }
  function tickJob(job) {
    clearTimeout(job.timer);
    job.timer = setTimeout(() => {
      if (job.state !== 'running') return;
      job.progress = Math.min(100, job.progress + 17);
      if (job.progress === 100) { job.state = 'completed'; job.stateLabel = '安装完成 · 已回读示例文件'; }
      const node = document.querySelector(`[data-job="${job.id}"]`); if (node) node.outerHTML = jobMarkup(job);
      if (job.state === 'running') tickJob(job); else {
        const { m } = findMessage(job.messageId);
        if (m && state.activeTurn?.messageId === m.id) { addTool(m, '核对安装结果', '后台任务完成后回读目标文件', 'sodium-example.jar 已登记，依赖检查通过。', { icon: 'check' }); stream(m, 'Sodium 已安装，文件与依赖已核对。下次启动这个实例时生效。'); }
        else notice('后台任务已完成，结果保留在原对话。');
      }
    }, 1600);
  }
  function confirmOperation(id) {
    const { t, m } = findMessage(id); if (!m?.confirm || m.confirm.stale || state.activeTurn?.messageId !== m.id) return;
    if (m.confirm.kind === 'permission') {
      if (m.confirm.scope === 'tool') (t.operationToolGrants ||= []).push(m.confirm.tool);
      m.operationApproved = true; m.confirm = null; m.status = 'generating'; runScenario(m); return;
    }
    if (!m.confirm.reviewed) return;
    if (t.mode !== 'operate' && !t.operationGrant && !m.operationApproved) { m.confirm = null; requireCapability(m, '操作授权已失效，未提交这次变更。', '返回对话', 'back-chat'); return; }
    if (m.confirm.kind === 'install' && !state.network.enabled) { m.confirm = null; requireCapability(m, '网络访问已关闭，未分派下载或安装任务。重新开启后可重新审核。', '检查网络范围', 'capabilities'); return; }
    if (m.confirm.kind === 'file' && (!state.files.enabled || !state.files.roots.some(r => r.id === m.confirm.rootId && r.access === 'write'))) { m.confirm = null; requireCapability(m, '文件授权已撤销，未执行写入。', '检查文件范围', 'capabilities'); return; }
    const c = m.confirm; m.confirm = null;
    if (c.kind === 'install') { addTool(m, '提交安装任务', '本次审核已确认 · 目标快照再次核对（模拟）', '已创建后台任务；queued 不表示安装完成。', { icon: 'download' }); startJob(m); m.text = '正在下载和安装 Sodium，完成后会继续核对结果。'; m.status = 'waiting'; m.statusText = '等待后台任务…'; render(); }
    else { addTool(m, '应用文件 patch', '再次核对授权与内容指纹，再写入并回读（模拟）', '已替换 1 行；其他内容保留。\n回读：renderDistance=10\n原文件可恢复备份：demo-backup-01', { icon: 'check', path: c.target }); stream(m, '文件已修改并回读。只改变审核中的一行，备份引用保留在工具详情中。'); }
  }
  function rootDialog() { openDialog('选择授权目录', `<p>正式产品使用系统目录选择器。这里选择一份虚构目录，不打开或读取本机文件。</p><div class="form-field"><label>示例目录</label>${select('root-choice', '授权目录候选', [['logs', '游戏诊断 · 示例日志目录'], ['notes', '我的游戏笔记 · 示例文档目录']], 'logs')}</div><div class="path-block" id="root-path">C:/Demo/Rodel/diagnostics</div><div class="form-field"><label>访问范围</label>${select('root-access', '目录访问范围', [['read', '只读'], ['write', '读取与小范围写入']], 'read')}</div><p style="margin-top:14px">读取到的内容可能随相关请求发送给当前模型服务。选择此目录不意味着自动上传其全部内容。</p>`, `${cancelButton()}${btn('授权此目录', 'save-root', '', 'primary')}`, { type: 'root' }); }
  function skillDialog() { openDialog('添加技能目录', `<p>添加一个明确的目录，仅索引其中 SKILL.md。这里使用虚构路径。</p><form id="skill-form"><div class="form-grid">${field('技能名称', 'skill-name', '建筑规划助手', '', 'text', 'required')}${field('目录路径', 'skill-path', 'C:/Demo/skills/building-helper', '', 'text', 'required')}<div class="form-field full"><label>说明</label><input name="skill-description" value="整理建筑计划、材料清单与分步核对"></div></div><details class="review-options"><summary>设计验证：目录状态</summary>${select('skill-scenario', '模拟技能目录状态', [['valid', '有效技能'], ['broken', 'SKILL.md 无法读取'], ['duplicate', '与内置技能同名']], 'valid')}</details></form>`, `${cancelButton()}<button type="submit" form="skill-form" class="small-button primary">添加目录</button>`, { type: 'skill' }); }
  function mcpDialog(id = '') { const m = state.mcps.find(x => x.id === id); openDialog(m ? '编辑第三方 MCP' : '添加第三方 MCP', `<form id="mcp-form" data-id="${e(id)}"><div class="form-grid">${field('显示名称', 'mcp-name', m?.name || '本地资料工具', '', 'text', 'required')}${field('可执行文件', 'mcp-executable', m?.executable || 'C:/Demo/tools/reference-mcp.exe', '', 'text', 'required')}<div class="form-field full"><label for="mcp-args">独立参数 · 每行一个</label><textarea id="mcp-args" name="mcp-args">${e(m?.args || '--stdio\n--catalog\nC:/Demo/reference')}</textarea></div>${field('工作目录', 'mcp-directory', m?.directory || 'C:/Demo/tools')}${field('凭据引用 · 可选', 'mcp-secret-ref', '', '例如环境变量名')}<div class="form-field full"><label>非秘密环境变量 · KEY=VALUE，每行一个</label><textarea name="mcp-env" placeholder="LANG=zh_CN" rows="2"></textarea></div></div><div class="status-line warning">程序拥有当前用户的系统权限。仅添加你信任的本机服务；不自动安装依赖、不通过 shell 拼接参数。</div><label class="checkbox-label"><input type="checkbox" name="mcp-enabled" ${m?.enabled ? 'checked' : ''}><span>保存后启用此服务<small>首次使用工具时连接。启用不代表所有工具都在只读模式下可调用。</small></span></label></form>`, `${cancelButton()}<button type="submit" form="mcp-form" class="small-button primary">保存 MCP</button>`, { type: 'mcp' }); }
  function integrationPlan(client, values = {}) {
    const [name, , executable] = integrationClients[client], prior = state.integrations[client];
    openDialog(`${prior ? '检查与更新' : '安装到'} ${name}`, `<form id="integration-form" data-client="${client}"><p>预览本产品将管理的内容。以下位置与客户端状态均为模拟。</p><div class="form-grid">${field('客户端可执行入口', 'client-path', values.path || executable, '允许手工指定')}<div class="form-field"><label>绑定数据根</label><input value="C:/Demo/.rmcl" readonly aria-label="绑定数据根"></div></div><label class="checkbox-label"><input type="checkbox" name="install-skill" checked disabled><span>启动器技能<small>用户级目录 · 仅管理 rodel-minecraft</small></span></label><div class="path-block">${e(integrationPath(client))}</div><label class="checkbox-label"><input type="checkbox" name="install-mcp"><span>MCP 服务<small>条目 rodel-minecraft · 选择独立安装的 CLI · 程序保留在原目录</small></span></label>${field('独立 CLI（rodel-mc.exe）', 'launcher-path', values.launcher || '', '例如 C:/Tools/RodelCli/rodel-mc.exe')}<p>选择 MCP 时需要先安装独立 CLI。App 不携带或复制该程序；这里只模拟选择结果。</p><div id="integration-status" class="notice-result">${prior ? '已检测到本产品安装记录；更新前比较内容指纹。' : '未检测到已安装内容。'}</div><details class="review-options"><summary>设计验证：客户端与配置状态</summary>${select('integration-scenario', '模拟外部安装状态', [['normal', '客户端可用 · 无冲突'], ['missing', 'CLI 不存在 · 可独立安装技能'], ['conflict', '同名 MCP 已被用户修改'], ['invalid', '客户端配置解析失败'], ['partial', '技能成功 · MCP 登记失败']], values.scenario || 'normal')}</details></form>`, `${cancelButton()}${btn(prior ? '重新安装' : '安装所选内容', 'apply-integration', `data-client="${client}"`, 'primary')}`, { type: 'integration' });
  }
  function applyIntegration(client, replacing = false) {
    const form = $('#integration-form'), scenario = form ? optionValue('integration-scenario') : state.pendingIntegration?.scenario;
    const chosen = form ? { skill: true, mcp: form.elements['install-mcp'].checked } : state.pendingIntegration.chosen;
    if (chosen.mcp && form && !form.elements['launcher-path'].value.trim()) { $('#integration-status').textContent = '请先选择独立安装的 rodel-mc.exe，或仅安装技能。'; return; }
    if (!chosen.skill && !chosen.mcp) { $('#integration-status').textContent = '至少选择一项要安装的内容。'; return; }
    if (scenario === 'conflict' && !replacing) {
      state.pendingIntegration = { client, scenario, chosen };
      openDialog('同名配置已被修改', `<p>rodel-minecraft 条目与本产品记录不匹配。核对差异后决定是否替换；其他 MCP 与设置保留。</p><div class="diff"><div><small>当前条目</small><pre>command: custom-rodel.exe\nargs: ["--custom-root"]</pre></div><div><small>建议条目</small><pre>command: C:/Tools/RodelCli/rodel-mc.exe\nargs: ["--data-dir", "C:/Demo/.rmcl", "mcp", "serve", "--stdio", "--tool-profile", "discovery"]</pre></div></div>`, `${btn('保留现有', 'close-dialog')}${btn('备份后替换此条目', 'replace-integration', `data-client="${client}"`, 'primary')}`, { type: 'conflict' }); return;
    }
    if (scenario === 'invalid') {
      openDialog('配置无法解析', `<p>未覆盖或改写客户端配置。可修复后重试，或复制本产品的独立配置片段后手工合并。</p><pre id="manual-mcp">${e(JSON.stringify({ 'rodel-minecraft': { command: form?.elements['launcher-path']?.value || 'C:/Tools/RodelCli/rodel-mc.exe', args: ['--data-dir', 'C:/Demo/.rmcl', 'mcp', 'serve', '--stdio'] } }, null, 2))}</pre>`, `${btn('返回修改', 'integration-plan', `data-client="${client}"`)}${btn('复制配置片段', 'copy-manual-mcp')}`, { type: 'integration-error' }); return;
    }
    const hasCli = scenario !== 'missing', mcpOk = chosen.mcp && hasCli && scenario !== 'partial';
    state.integrations[client] = { skill: chosen.skill, mcp: mcpOk, status: chosen.mcp && !mcpOk ? 'partial' : 'installed', modified: false };
    if (!chosen.mcp || mcpOk) { closeDialog(); render(); notice(`${integrationClients[client][0]} 启动器接入已安装（模拟）。`); return; }
    openDialog('安装结果 · 模拟', `<div class="status-line">${chosen.skill ? '技能：已写入本产品专属目录。' : '技能：未选择。'}<br>${!chosen.mcp ? 'MCP：未选择。' : mcpOk ? 'MCP：已登记，并完成示例连接检查。' : hasCli ? 'MCP：登记失败。技能已保留，可以仅重试失败项。' : 'MCP：CLI 不存在，尚未登记。可导出配置后手工安装。'}</div><p>其他客户端配置与人工文件保留。模拟步骤记录已写入本页内存，未调用实际客户端。</p>`, `${btn('返回设置', 'close-render')}${chosen.mcp && !mcpOk ? btn(hasCli ? '重试 MCP 登记' : '查看手工配置', hasCli ? 'retry-integration' : 'integration-manual', `data-client="${client}"`, 'primary') : btn('完成', 'close-render', '', 'primary')}`, { type: 'integration-result' });
  }
  let historyContext;
  function showHistoryContext(target, x, y) {
    historyContext?.remove();
    const id = target.dataset.id;
    const menu = document.createElement('div');
    menu.className = 'assistant-content history-context-menu';
    menu.setAttribute('role', 'menu');
    menu.innerHTML = `${btn(`${assistantIcon('edit')} 重命名`, 'thread-menu', `data-id="${id}"`)}${btn(`${assistantIcon('download')} 导出对话`, 'export-thread', `data-id="${id}"`)}${btn(`${assistantIcon('trash')} 删除对话`, 'delete-thread', `data-id="${id}"`, 'history-delete')}`;
    document.body.append(menu);
    menu.style.left = `${Math.max(8, Math.min(x, innerWidth - menu.offsetWidth - 8))}px`;
    menu.style.top = `${Math.max(8, Math.min(y, innerHeight - menu.offsetHeight - 8))}px`;
    historyContext = menu;
    menu.querySelector('button').focus();
  }
  function threadMenu(id) {
    const t = state.threads.find(x => x.id === id);
    openDialog('重命名对话', `<div class="form-field"><label for="thread-title">对话名称</label><input id="thread-title" name="thread-title" value="${e(t.title)}" maxlength="80"></div>`, `${btn('保存', 'rename-thread', `data-id="${id}"`, 'primary')}${cancelButton()}`, { type: 'thread-menu' });
  }
  async function copy(text) { try { await navigator.clipboard.writeText(text); notice('已复制模拟内容。'); } catch { openDialog('可复制的文本', `<textarea readonly aria-label="待复制内容" style="min-height:180px">${e(text)}</textarea><p>浏览器未允许直接写入剪贴板；可选中后复制。</p>`, btn('关闭', 'close-dialog'), { single: true }); } }
  function demo() {
    if (!state.services.length) { state.services.push({ id: nextId('service'), name: '演示模型服务', protocol: 'openai-chat', url: 'https://api.example.com/v1', credential: 'example', enabled: true, test: 'success', models: [{ id: nextId('model'), code: 'rodel-chat', name: 'Rodel Chat' }, { id: nextId('model'), code: 'text-preview', name: 'Text Preview' }] }); }
    const s = state.services[0]; state.defaultBinding = `${s.id}/${s.models[0].id}`; state.binding = state.defaultBinding; if (thread() && !thread().binding) thread().binding = state.defaultBinding;
    render(); notice('已载入示例模型。内置工具已就绪，文件访问仍限于你选择的目录。');
  }
  function openRepair(kind = 'conflict', original = '', target = null) {
    target ||= typeof selected !== 'undefined' ? selected : { id:'mist', name:'雾谷生存', loader:'Fabric', version:'1.21.1', mods:102 };
    const key = 'repair-' + target.id + '-' + kind + '-run-07';
    if (!instances.some(item => item[0] === target.id)) instances.push([target.id, `${target.name} · ${target.loader} ${target.version}`]);
    let current = state.threads.find(item => item.repairKey === key);
    if (!current) {
      current = { id:nextId('chat'), title:'修复 · ' + target.name, instance:target.id, mode:'read', binding:state.defaultBinding,
        access:{ files:true, network:true }, draft:'', references:[], messages:[], repairKey:key, repair:AiRepairDesign.create(kind, original, target) };
      state.threads.unshift(current);
    }
    state.threadId = current.id; state.view = 'chat'; state.followBottom = true;
    navigate('ai'); render();
    const selectedBinding = binding();
    if (current.repair.phase === 'context' && selectedBinding.service?.enabled && selectedBinding.model)
      AiRepairDesign.act(current.repair, 'diagnose', render);
  }
  function manageModels() { closeDialog(); openSettings('services'); }
  function editModel(serviceId, modelId) {
    const s = state.services.find(x => x.id === serviceId), m = s.models.find(x => x.id === modelId);
    openDialog(m ? '编辑模型' : '添加模型', `<form id="model-form" data-service="${s.id}" data-model="${e(modelId || '')}"><div class="service-fields">${field('模型名称', 'binding-name', m?.name || '', s.protocol === 'inference' ? '留空使用文件名' : '留空使用模型标识')}${s.protocol === 'inference' ? `<div class="form-field"><label>模型文件</label><div class="ai-path-input"><input name="binding-code" value="${e(m?.code || '')}" readonly placeholder="选择 GGUF 模型文件">${iconBtn('folder', '选择模型文件', 'pick-local-binding')}</div></div>` : field('模型标识', 'binding-code', m?.code || '', '填写服务提供的标识', 'text', 'required')}</div><label class="checkbox-label"><input type="checkbox" name="binding-default" ${state.defaultBinding === `${s.id}/${m?.id}` ? 'checked' : ''}><span>设为新对话默认模型</span></label></form>`, `${m ? btn('移除模型', 'remove-model', `data-id="${s.id}" data-model="${m.id}"`) : cancelButton()}<button class="small-button primary" type="submit" form="model-form">保存</button>`, { type: 'model-edit' });
  }
  function faultDialog() {
    openDialog('异常与恢复 · 设计验证', `<p>只影响下一次示例发送。用于审阅中断、未知效果和恢复入口；没有网络或文件故障注入。</p><div class="form-field"><label>下一轮模拟状态</label>${select('fault-choice', '下一轮模拟状态', [['none', '正常回复'], ['network', '回答中断 · 保留部分内容'], ['interrupted', '宿主退出 · 不自动重放'], ['unknown', '已提交操作但未获得结果'], ['storage', '记录不可写 · 停止后续工具']], state.fault)}</div>`, `${cancelButton()}${btn('应用到下一轮', 'set-fault', '', 'primary')}`, { type: 'fault' });
  }
  function handleAction(action, b) {
    const id = b.dataset.id, client = b.dataset.client;
    if (action.startsWith('repair-') && thread()?.repair) {
      if (action === 'repair-instance') { const r = thread().repair; openDialog('关联实例', `<h3>${e(r.instanceName)}</h3><p>${e(r.gameVersion)} · ${e(r.loader)}</p>`, btn('关闭', 'close-dialog'), { single:true }); return; }
      if (action === 'repair-back') { navigate('instances'); return; }
      AiRepairDesign.act(thread().repair, action.slice(7), render); return;
    }
    switch (action) {
      case 'demo': demo(); break;
      case 'add-reference': $('#chat-image-picker')?.click(); break;
      case 'remove-reference': { const images = draftReferences(), index = images.findIndex(image => image.id === id); if (index >= 0) images.splice(index, 1); render(); break; }
      case 'submit-answer':
      case 'skip-answer': {
        const m = pendingQuestion(); if (!m) break;
        const q = m.prompt, skip = action === 'skip-answer';
        if (!skip && !q.selected && !q.text.trim()) break;
        const answer = skip ? '已跳过' : [q.options.find(option => option.id === q.selected)?.label, q.text].filter(Boolean).join(' · ');
        m.answerReceipt = { question:q.question, answer, skipped:skip };
        m.prompt = null; stream(m, skip ? '已记录跳过，将依据已有信息继续。' : '已收到你的回答，将按这一偏好继续。'); break;
      }
      case 'fault-scenario': faultDialog(); break;
      case 'set-fault': state.fault = optionValue('fault-choice'); closeDialog(); notice('下一轮将演示所选状态；未触发真实请求。'); break;
      case 'reconcile': {
        const { m } = findMessage(id);
        addTool(m, '核对已提交操作', '使用记录的任务键查询状态，未重新提交（模拟）', 'recoveryKey: demo-job-42\n实际终态：已完成\n回读结果：目标文件存在，版本一致', { icon: 'check' });
        m.status = 'completed'; m.statusText = ''; m.recovery = null; m.text = '已通过示例恢复查询确认原任务完成。没有重新执行写操作。'; render(); break;
      }
      case 'reset': openDialog('重置原型演示', '<p>清空本页模拟服务、对话与安装状态，回到首次打开。不会影响启动器、其他页面或真实文件。</p>', `${cancelButton()}${btn('清空演示并重新开始', 'reset-confirm', '', 'primary')}`); break;
      case 'reset-confirm': location.reload(); break;
      case 'new-chat': newThread(); break;
      case 'settings': openSettings(); break;
      case 'back-chat': navigate('ai'); break;
      case 'refresh-service-models': refreshServiceModels(id); break;
      case 'manage-services': openSettings(); break;
      case 'capabilities': openSettings('capabilities'); break;
      case 'builtin-details': { const m = state.mcps.find(x => x.id === id); openDialog(m.name, `<p>启动器内置，自动连接并随应用更新；无需安装运行环境、填写命令或服务密钥。</p><p>${m.id === 'builtin-files' ? '只访问你选择的目录，修改仍受会话操作模式与确认约束。' : '默认提供公开网页检索与读取；不要求配置搜索服务。网络不可用时仍可普通聊天。'}</p><details class="review-options"><summary>设计验证：工具连接故障</summary>${btn('模拟暂时不可用', 'fail-builtin', `data-id="${m.id}"`)}</details>`, btn('关闭', 'close-dialog'), { single: true }); break; }
      case 'fail-builtin': state.mcps.find(m => m.id === id).status = 'failed'; closeDialog(); render(); break;
      case 'retry-builtin-files':
      case 'retry-builtin-web': { const kind = action.endsWith('files') ? 'files' : 'web'; state.mcps.find(m => m.id === `builtin-${kind}`).status = 'connected'; render(); resumePending(); break; }
      case 'mcp-settings': openSettings('mcp'); break;
      case 'toggle-history': state.historyOpen = !state.historyOpen; render(); break;
      case 'open-thread': state.threadId = id; thread().unread = false; state.files.enabled = thread().access?.files ?? state.accessDefaults.files; state.network.enabled = thread().access?.network ?? state.accessDefaults.network; state.historyOpen = false; state.followBottom = thread().scrollOffset == null; render(); break;
      case 'thread-menu': threadMenu(id); break;
      case 'rename-thread': state.threads.find(x => x.id === id).title = optionValue('thread-title').trim() || '未命名对话'; closeDialog(); render(); break;
      case 'delete-thread': {
        const t = state.threads.find(x => x.id === id), jobs = t.messages.flatMap(m => state.jobs.filter(j => j.id === m.jobId && j.state === 'running'));
        openDialog('删除这段对话', `<p>删除「${e(t.title)}」的本页模拟历史。${state.activeTurn?.threadId === id ? '活动回合会先停止。' : ''}</p>${jobs.length ? '<div class="status-line warning">这段对话仍有独立后台任务。删除历史不会自动取消任务；先返回任务卡处理，再删除记录。</div>' : ''}`, `${cancelButton()}${btn(jobs.length ? '返回任务' : '删除对话', jobs.length ? 'return-thread' : 'delete-thread-confirm', `data-id="${id}"`, 'primary')}`); break;
      }
      case 'return-thread': closeDialog(); state.threadId = id; state.view = 'chat'; render(); break;
      case 'delete-thread-confirm': state.queued = state.queued.filter(item => item.t.id !== id); if (state.activeTurn?.threadId === id) stopTurn(); state.threads = state.threads.filter(t => t.id !== id); if (state.threadId === id) state.threadId = null; closeDialog(); render(); break;
      case 'export-thread': { const t = state.threads.find(x => x.id === id); const text = `设计模拟 · ${t.title}\n\n${t.messages.map(m => `${m.role === 'user' ? '你' : '助手'}\n${m.text}\n${(m.tools || []).map(tool => `${tool.name}\n${tool.result || tool.description}`).join('\n')}`).join('\n\n')}`; openDialog('导出预览', `<p>本页只展示导出内容，可复制后另存。</p><textarea readonly aria-label="导出内容" style="min-height:220px">${e(text)}</textarea>`, `${btn('关闭', 'close-dialog')}${btn('复制全文', 'copy-export', '', 'primary')}`); break; }
      case 'copy-export': copy($('dialog textarea').value); break;
      case 'suggest': saveDraft(b.dataset.question); render(); $('#question')?.focus(); break;
      case 'send': break; // Native form submit owns Enter and click.
      case 'send-queued-now': { const item = state.queued.find(item => item.m.id === id); if (!item) break; state.followBottom = true; state.queued = [item, ...state.queued.filter(other => other !== item)]; if (state.activeTurn?.threadId === item.t.id) stopTurn(); else if (!busy()) { const next = state.queued.shift(); startTurn(next.t, next.m, next.fault); } render(); break; }
      case 'cancel-queued': { const item = state.queued.find(item => item.m.id === id); if (item) { item.m.status = 'canceled'; item.m.statusText = '已取消排队'; state.queued = state.queued.filter(other => other !== item); render(); } break; }
      case 'stop': state.queued = state.queued.filter(item => { if (item.t.id !== state.threadId) return true; item.m.status = 'canceled'; item.m.statusText = '已取消排队'; return false; }); stopTurn(); break;
      case 'scroll-bottom': state.followBottom = true; $('.transcript').scrollTop = $('.transcript').scrollHeight; b.hidden = true; break;
      case 'choose-model': { const trigger = $('[name="model-binding"]')?.parentElement.querySelector('.combo-face'); if (trigger) openCombo(trigger); break; }
      case 'open-service': serviceDialog(id); break;
      case 'add-local-model': { const local = state.services.find(service => service.protocol === 'inference' && service.enabled); if (local) editModel(local.id); else serviceDialog(); break; }
      case 'manage-models': manageModels(id); break;
      case 'edit-model': editModel(id, b.dataset.model); break;
      case 'remove-model': { const s = state.services.find(x => x.id === id); const m = s.models.find(x => x.id === b.dataset.model); openDialog('移除此模型', `<p>移除 ${e(m.name)} 的可选绑定，历史和活动回合保留原模型快照；后续需要另选模型。</p>`, `${cancelButton()}${btn('移除模型', 'remove-model-confirm', `data-id="${id}" data-model="${m.id}"`, 'primary')}`); break; }
      case 'remove-model-confirm': { const s = state.services.find(x => x.id === id); s.models = s.models.filter(x => x.id !== b.dataset.model); if (state.defaultBinding === `${s.id}/${b.dataset.model}`) state.defaultBinding = ''; manageModels(id); break; }
      case 'save-service': break;
      case 'pick-local-binding': { $('[name="binding-code"]').value = 'C:/Demo/models/another-model.gguf'; notice('设计示例路径；正式应用打开系统文件选择器。'); break; }
      case 'pick-local-model-code': { $('[name="model-code"]').value = 'C:/Demo/models/model.gguf'; notice('设计示例路径；正式应用打开系统文件选择器。'); break; }
      case 'pick-local-model': { $('[name="custom-service-url"]').value = 'C:/Demo/models/model.gguf'; if (!$('[name="service-name"]').value.trim()) $('[name="service-name"]').value = 'model'; notice('设计示例路径；正式应用打开系统文件选择器。'); break; }
      case 'show-manual-model': serviceDraft.manualMode = true; $('.manual-model-entry').hidden = true; $('.manual-model').hidden = false; $('[name="model-code"]').focus(); break;
      case 'hide-manual-model': $('#service-error').textContent = ''; serviceDraft.manualMode = false; serviceDraft.manual = ''; serviceDraft.manualName = ''; $('[name="model-code"]').value = ''; $('[name="model-display-name"]').value = ''; $('.manual-model').hidden = true; $('.manual-model-entry').hidden = false; $('[data-assistant-action="show-manual-model"]').focus(); break;
      case 'service-back': captureModelChoices(); serviceDialog(serviceDraft.id, true); break;
      case 'reload-service-models': captureModelChoices(); serviceModelsStep(true); break;
      case 'test-saved-service': { const s = state.services.find(x => x.id === id); openDialog('测试连接', `<p>${e(s.name)} · ${e(s.url)}</p><div class="form-field"><label>模拟响应</label>${select('saved-service-scenario', '模拟连接结果', [['success', '成功'], ['failed', '鉴权失败 / 不可达']], 'success')}</div><div id="saved-test-result" class="notice-result"></div>`, `${btn('关闭', 'close-render')}${btn('运行连接测试', 'run-saved-test', `data-id="${id}"`, 'primary')}`); break; }
      case 'run-saved-test': { const s = state.services.find(x => x.id === id); s.test = optionValue('saved-service-scenario'); $('#saved-test-result').textContent = s.test === 'success' ? '连接成功（模拟）；模型与工具能力仍需分别验证。' : '服务连接失败（模拟）。请检查地址和凭据；其他服务仍可用。'; break; }
      case 'toggle-service': { const s = state.services.find(x => x.id === id); s.enabled = !s.enabled; render(); notice('服务启用状态只影响后续发送。'); break; }
      case 'remove-service': openDialog('移除模型服务', '<p>移除示例配置。已有历史保留服务显示名与模型，活动回合使用原快照；后续发送需要重新选择模型。</p>', `${cancelButton()}${btn('移除服务', 'remove-service-confirm', `data-id="${id}"`, 'primary')}`); break;
      case 'remove-service-confirm': state.services = state.services.filter(s => s.id !== id); if (state.defaultBinding.startsWith(`${id}/`)) state.defaultBinding = ''; closeDialog(); render(); break;
      case 'toggle-default-files': state.accessDefaults.files = !state.accessDefaults.files; if (!thread()) state.files.enabled = state.accessDefaults.files; render(); break;
      case 'toggle-default-network': state.accessDefaults.network = !state.accessDefaults.network; if (!thread()) state.network.enabled = state.accessDefaults.network; render(); break;
      case 'toggle-files': state.files.enabled = !state.files.enabled; if (thread()) thread().access = { files:state.files.enabled, network:state.network.enabled }; render(); notice(state.files.enabled ? '已开启文件能力，请选择授权目录。' : '已关闭文件能力，后续文件调用停止。'); break;
      case 'toggle-network': state.network.enabled = !state.network.enabled; if (thread()) thread().access = { files:state.files.enabled, network:state.network.enabled }; render(); notice(state.network.enabled ? '已开启网页访问，可配置检索或提供明确 URL。' : '已关闭网络能力，后续网络调用停止。'); break;
      case 'add-root': rootDialog(); break;
      case 'save-root': { const kind = optionValue('root-choice'); if (!state.files.roots.some(r => r.kind === kind)) state.files.roots.push({ id: nextId('root'), kind, name: kind === 'logs' ? '游戏诊断' : '我的游戏笔记', path: kind === 'logs' ? 'C:/Demo/Rodel/diagnostics' : 'C:/Demo/Minecraft-notes', access: optionValue('root-access') }); closeDialog(); render(); notice('示例目录已授权，未扫描任何文件。'); break; }
      case 'root-access': { const r = state.files.roots.find(x => x.id === id); if (r.access === 'write') { r.access = 'read'; render(); } else openDialog('允许此目录写入', `<p>允许助手在此目录新建或小范围修改文件。覆盖已有内容时显示目标和差异，写入前核对内容指纹。</p><div class="path-block">${e(r.path)}</div><p>聊天仍需处于允许操作模式。不会开放任意 shell 或执行文件。</p>`, `${cancelButton()}${btn('允许此范围写入', 'root-write-confirm', `data-id="${id}"`, 'primary')}`); break; }
      case 'root-write-confirm': state.files.roots.find(x => x.id === id).access = 'write'; closeDialog(); render(); break;
      case 'remove-root': state.files.roots = state.files.roots.filter(r => r.id !== id); render(); notice('目录授权已撤销，后续调用不可再访问。'); break;
      case 'review-local-origin': { const {m} = findMessage(id); openDialog('访问本机服务', `<p>仅本次对话请求访问：</p><p>${e(m.localOrigin)}</p>`, `${cancelButton()}${btn('允许本次请求','allow-local-origin',`data-id="${id}"`,'primary')}`); break; }
      case 'allow-local-origin': { const {m} = findMessage(id); m.allowedLocalOrigins ||= []; if (!m.allowedLocalOrigins.includes(m.localOrigin)) m.allowedLocalOrigins.push(m.localOrigin); closeDialog(); render(); resumePending(id); break; }
      case 'add-skill': skillDialog(); break;
      case 'toggle-skill': { const s = state.skills.find(x => x.id === id); s.enabled = !s.enabled; render(); notice('技能启用变化在下一轮生效。'); break; }
      case 'inspect-skill': { const s = state.skills.find(x => x.id === id); openDialog('技能内容', `<p>${e(s.name)}</p>${s.path ? `<p>${e(s.path)}</p>` : ''}<pre># ${e(s.name)}\n\n${e(s.description)}\n\n先查询明确目标，再使用已开放工具。\n展示来源与实际结果；变更后回读。\n技能不能扩大权限，不执行附属脚本。</pre>${s.id === 'builtin' ? '<p>核心技能每轮加载；扩展技能仅在适用时读取正文。</p>' : ''}`, btn('关闭', 'close-dialog'), { single: true }); break; }
      case 'check-skill': { const s = state.skills.find(x => x.id === id); s.valid = true; render(); notice('模拟目录已恢复，技能可在下一轮加载。'); break; }
      case 'remove-skill': state.skills = state.skills.filter(s => s.id !== id); render(); break;
      case 'open-mcp': if (!state.mcps.find(m => m.id === id)?.builtin) mcpDialog(id); break;
      case 'toggle-mcp': { const m = state.mcps.find(x => x.id === id); if (m.builtin) break; if (m.enabled) { m.enabled = false; render(); notice('已禁用，后续不再分派此 MCP 的调用。'); } else openDialog('启用本机 MCP', `<p>${e(m.name)} 将作为当前用户启动，拥有该用户的系统权限。只读模式控制助手调度，不是 OS 沙箱。</p><div class="path-block">${e(m.executable)}</div>`, `${cancelButton()}${btn('启用此服务', 'enable-mcp-confirm', `data-id="${id}"`, 'primary')}`); break; }
      case 'enable-mcp-confirm': state.mcps.find(x => x.id === id).enabled = true; closeDialog(); render(); break;
      case 'test-mcp': openDialog('MCP 连接检查', `<p>握手、目录解析与工具可用性分别显示。本次不启动真实进程。</p><div class="form-field"><label>模拟结果</label>${select('mcp-test-scenario', '模拟 MCP 连接结果', [['connected', '正常连接'], ['failed', '启动 / 握手失败']], 'connected')}</div><div id="mcp-test-result" class="notice-result"></div>`, `${btn('关闭', 'close-render')}${btn('执行检查', 'run-mcp-test', `data-id="${id}"`, 'primary')}`); break;
      case 'run-mcp-test': { const m = state.mcps.find(x => x.id === id); m.status = optionValue('mcp-test-scenario'); $('#mcp-test-result').textContent = m.status === 'connected' ? '示例握手成功。发现 3 个工具，其中 1 个 schema 不支持而被单独禁用。' : '握手超时。检查可执行文件和独立参数后重试；聊天和其他扩展仍可使用。'; break; }
      case 'mcp-tools': { const m = state.mcps.find(x => x.id === id); openDialog('扩展工具目录', `<p>${e(m.name)} · 来源命名空间 ${e(m.id)} · 目录修订 demo-1</p>${[['search_notes', '可读取目录 · 由宿主核对执行范围'], ['write_note', '有副作用 · 需要允许操作模式'], ['legacy_tool', '不支持的 schema · 此工具不可用']].map(([name, note]) => `<div class="settings-row"><div><h3>${e(m.id)} / ${name}</h3><p>${note}</p></div></div>`).join('')}<p>第三方标注只作提示，不能仅凭其自称只读开放调用。</p>`, btn('关闭', 'close-dialog'), { single: true }); break; }
      case 'remove-mcp': state.mcps = state.mcps.filter(m => m.id !== id || m.builtin); render(); notice('示例 MCP 配置已移除。'); break;
      case 'integration-details': { const item = state.integrations[client]; openDialog(`${integrationClients[client][0]} 接入`, `<p>${item.skill ? '已安装技能' : '未安装技能'} · ${item.mcp ? '已登记 MCP' : '未登记 MCP'}</p><p>${e(integrationPath(client))}</p>`, btn('关闭', 'close-dialog'), { single:true }); break; }
      case 'integration-plan': integrationPlan(client); break;
      case 'apply-integration': applyIntegration(client); break;
      case 'replace-integration': applyIntegration(client, true); break;
      case 'copy-manual-mcp': copy($('#manual-mcp').textContent); break;
      case 'retry-integration': state.integrations[client].mcp = true; state.integrations[client].status = 'installed'; closeDialog(); render(); notice('仅重试 MCP 登记：模拟成功；技能未重复覆盖。'); break;
      case 'integration-manual': openDialog('手工 MCP 配置', '<p>CLI 不存在，因此未注册 MCP。安装客户端后可返回检查，或复制以下配置自行添加。</p><pre id="manual-mcp">command: C:/Tools/RodelCli/rodel-mc.exe\nargs: [--data-dir, C:/Demo/.rmcl, mcp, serve, --stdio, --tool-profile, discovery]</pre>', `${btn('关闭', 'close-render')}${btn('复制配置', 'copy-manual-mcp')}`); break;
      case 'integration-remove': openDialog('移除启动器接入', `<p>只移除仍匹配安装记录的 rodel-minecraft 内容。其他技能、MCP 与用户设置保留。</p><label class="checkbox-label"><input type="checkbox" name="modified-install"><span>设计验证：技能文件已被用户修改<small>此时保留该文件，仅移除仍属于本产品的 MCP 条目。</small></span></label>`, `${cancelButton()}${btn('移除本产品内容', 'integration-remove-confirm', `data-client="${client}"`, 'primary')}`); break;
      case 'integration-remove-confirm': if ($('[name="modified-install"]').checked) { state.integrations[client] = { skill: true, mcp: false, status: 'preserved', modified: true }; closeDialog(); render(); notice('MCP 已移除，用户修改的技能文件保留。'); } else { state.integrations[client] = null; closeDialog(); render(); notice('本产品管理的示例内容已移除，无关配置保留。'); } break;
      case 'copy-message': copy(findMessage(id).m.text); break;
      case 'retry-message': { const { t, m } = findMessage(id); if (busy()) break; state.threadId = t.id; send(m.tools?.some(tool => /提交|应用/.test(tool.name)) ? `核对已有任务状态，不重复操作：${m.question}` : m.question); break; }
      case 'allow-retry': { const { t } = findMessage(id); if (busy()) break; t.mode = 'operate'; state.threadId = t.id; resumePending(id); break; }
      case 'focus-instance': state.view = 'chat'; render(); $('[name="instance"]')?.parentElement.querySelector('button')?.focus(); break;
      case 'review-details': approvalDetails(id); break;
      case 'confirm-operation': confirmOperation(id); break;
      case 'confirm-tool': { const { m } = findMessage(id); if (m?.confirm?.kind === 'permission') { m.confirm.scope = 'tool'; confirmOperation(id); } break; }
      case 'deny-operation': { const { m } = findMessage(id); m.confirm = null; addTool(m, '执行确认', '用户拒绝了本次变更', '未提交操作。', { icon: 'shield' }); stream(m, '已拒绝这次操作，目标没有因为这份审核被修改。你可以继续查看信息或重新说明需求。'); break; }
      case 'stale-review': findMessage(id).m.confirm.stale = true; render(); break;
      case 'refresh-review': { const c = findMessage(id).m.confirm; c.stale = false; c.reviewed = false; c.scope = 'once'; c.fingerprint = 'review-demo-02 / 已重新读取目标'; c.before += '\n外部改动已纳入本次审核'; render(); break; }
      case 'cancel-job': { const j = state.jobs.find(x => x.id === id); clearTimeout(j.timer); j.state = 'canceled'; j.stateLabel = '已取消 · 保留完成的下载'; const { m } = findMessage(j.messageId); if (m && state.activeTurn?.messageId === m.id) finish(m, 'canceled', '安装已取消，保留已下载内容。'); else render(); break; }
      case 'fail-job': { const j = state.jobs.find(x => x.id === id); clearTimeout(j.timer); j.state = 'failed'; j.stateLabel = '下载中断 · 可从已完成部分重试'; const { m } = findMessage(j.messageId); if (m && state.activeTurn?.messageId === m.id) finish(m, 'failed', '下载中断，可以重试此任务。'); else render(); break; }
      case 'retry-job': { const j = state.jobs.find(x => x.id === id); if (!state.network.enabled) { notice('网络访问已关闭，请开启后重试。'); break; } j.state = 'running'; const { t, m } = findMessage(j.messageId); if (m && !busy()) { state.activeTurn = { id: nextId('turn'), threadId: t.id, messageId: m.id, timer: null }; m.status = 'waiting'; m.statusText = '等待后台任务…'; } tickJob(j); render(); break; }
      case 'job-detail': { const j = state.jobs.find(x => x.id === id); openDialog('后台任务详情', `<p>${e(j.title)} · ${e(j.id)}</p><div class="path-block">目标：${e(contextLabel(j.instance))}\n阶段：${e(j.state)}\n进度：${j.progress}%（模拟）\n${j.state === 'completed' ? '已回读目标文件 · 示例校验通过' : '尚未确认安装完成'}</div><p>模型回复与后台任务各自结束；切换实例不会改变这个任务的目标。</p>`, btn('关闭', 'close-dialog'), { single: true }); break; }
      case 'close-render': closeDialog(); render(); break;
      case 'close-dialog': serviceDraft = null; closeDialog(); break;
    }
  }
  function fitContent() { const host = document.getElementById('assistant-content'); if (!host) return; host.style.setProperty('--assistant-top', `${Math.round(host.getBoundingClientRect().top)}px`); }
  function mount() { render(); if (currentPage === 'ai') resumePending(); }
  function start() {
    if (initialized) return;
    initialized = true;
    const params = new URLSearchParams(location.search);
    if (params.get('demo') === '1') demo();
    window.addEventListener('resize', () => { if (window.innerWidth > 900) closeHistory(); fitContent(); });
    const syncReview = () => {
      if (currentPage === 'ai') { document.querySelector('.review-states').hidden = true; fitContent(); document.querySelector('.review-bar strong').textContent = '08 / ASSISTANT'; document.querySelector('.review-note').textContent = 'AI流程设计 · 全部模拟'; }
      else state.followBottom = false;
    };
    window.addEventListener('prototype-page-changed', syncReview);
    window.addEventListener('prototype-theme-changed', syncReview);
    document.addEventListener('contextmenu', event => {
      const target = event.target.closest('.history-item [data-assistant-action="open-thread"]');
      if (target) { event.preventDefault(); showHistoryContext(target, event.clientX, event.clientY); }
    });
    document.addEventListener('paste', event => { if (event.target.id !== 'question') return; const images = [...(event.clipboardData?.files || [])].filter(file => file.type.startsWith('image/')); if (images.length) { event.preventDefault(); attachReferences(images).catch(() => notice('图片读取失败。')); } });
    document.addEventListener('keydown', event => {
      const target = event.target.closest('.history-item [data-assistant-action="open-thread"]');
      if (target && (event.key === 'ContextMenu' || event.shiftKey && event.key === 'F10')) { event.preventDefault(); const box = target.getBoundingClientRect(); showHistoryContext(target, box.left, box.bottom); }
      if (event.key === 'Escape') { historyContext?.remove(); closeHistory(); }
    });
    document.addEventListener('click', event => {
      if (historyContext && !historyContext.contains(event.target)) historyContext.remove();
      if (state.historyOpen && !event.target.closest('.history-pane,.mobile-history')) closeHistory();
      document.querySelectorAll('.service-options[open]').forEach(menu => { if (!menu.contains(event.target)) menu.open = false; });
      const target = event.target.closest('button'); if (!target || !target.closest('.assistant-content,.assistant-settings,.assistant-dialog,.review-assistant-states')) return;
      if (target.dataset.assistantView) { openSettings(target.dataset.assistantView); return; }
      if (target.dataset.assistantAction) { if (target.closest('.settings-expander-header')) event.preventDefault(); target.closest('.service-options')?.removeAttribute('open'); handleAction(target.dataset.assistantAction, target); historyContext?.remove(); if (['toggle-files','toggle-network','save-root','root-write-confirm','enable-mcp-confirm','test-mcp'].includes(target.dataset.assistantAction)) resumePending(); }
    });
    document.addEventListener('input', event => {
      if (!event.target.closest('.assistant-content,.assistant-settings,.assistant-dialog')) return;
      if (event.target.id === 'user-answer-text' && pendingQuestion()) { const q = pendingQuestion().prompt; q.text = event.target.value; $('[data-assistant-action="submit-answer"]').disabled = !q.selected && !q.text.trim(); }
      if (event.target.name === 'model-code' && event.target.value.trim() && $('#service-error')) $('#service-error').textContent = '';
      if (event.target.id === 'question') { saveDraft(event.target.value); const b = $('[data-assistant-action="send"]'); if (b) { const x = binding(); b.disabled = (!event.target.value.trim() && !draftReferences().length) || !x.service?.enabled || !x.model; } }
    });
    document.addEventListener('change', event => {
      if (!event.target.closest('.assistant-content,.assistant-settings,.assistant-dialog')) return;
      const target = event.target;
      if (target.name === 'default-mode') { state.defaultMode = target.value; if (!thread() && !state.modeChosen && !draft().trim() && draftReferences().length === 0) state.mode = state.defaultMode; return; }
      if (target.id === 'chat-image-picker') { attachReferences(target.files).catch(() => notice('图片读取失败。')); return; }
      if (target.name === 'user-answer-option' && pendingQuestion()) { pendingQuestion().prompt.selected = target.value; render(); return; }
      if (target.name === 'model-binding') { if (thread()) thread().binding = target.value; else state.binding = target.value; render(); $('[name="model-binding"]')?.parentElement.querySelector('.combo-face')?.focus(); if (!busy()) resumePending(); }
      if (target.name === 'instance' || target.name === 'mode') {
        if (target.name === 'mode' && !thread()) state.modeChosen = true;
        if (target.name === 'mode' && thread()) {
          thread().operationGrant = false;
          thread().operationToolGrants = [];
          const pending = pendingApproval();
          if (pending) { pending.confirm = null; pending.operationApproved = false; finish(pending, 'interrupted', '权限模式已变化，请重新提交目标。'); }
        }
        if (thread()) thread()[target.name] = target.value; else state[target.name] = target.value;
        if (busy()) notice('已为下一轮更新选择；进行中的回合保留原目标。'); else resumePending();
      }

      if (target.name === 'root-choice') $('#root-path').textContent = target.value === 'logs' ? 'C:/Demo/Rodel/diagnostics' : 'C:/Demo/Minecraft-notes';
      if (target.name === 'service-kind') { const protocol = target.value === 'openai' ? $('[name="openai-api"]').value : target.value; $('[name="custom-service-url"]').value = serviceDefaults(protocol); $('[data-openai-api]').hidden = target.value !== 'openai'; const input = $('[name="credential-value"]'); input.disabled = target.value === 'inference'; input.parentElement.hidden = input.disabled; $('button[form="service-form"]').textContent = serviceDraft.id ? '保存' : target.value === 'inference' ? '添加' : '下一步'; const path = $('[name="custom-service-url"]'); path.readOnly = target.value === 'inference'; path.placeholder = path.readOnly ? '选择 GGUF 模型文件' : ''; $('[data-custom-endpoint] label').textContent = path.readOnly ? '模型文件' : '服务地址'; $('[data-assistant-action="pick-local-model"]').hidden = !path.readOnly; }
    });
    document.addEventListener('keydown', event => { if (event.key === 'Escape') { const menu = event.target.closest('.service-options[open]'); if (menu) { menu.open = false; menu.querySelector('summary').focus(); } } if (event.target.id === 'user-answer-text' && event.key === 'Enter' && !event.shiftKey && !event.ctrlKey && !event.altKey && !event.metaKey && !event.isComposing && event.keyCode !== 229) { event.preventDefault(); if (!event.repeat) $('[data-assistant-action="submit-answer"]:not(:disabled)')?.click(); return; } if (event.target.id === 'question' && event.key === 'Enter' && !event.shiftKey && !event.isComposing && event.keyCode !== 229) { event.preventDefault(); send(); } });
    document.addEventListener('submit', event => {
      if (!event.target.closest('.assistant-content,.assistant-settings,.assistant-dialog')) return;
      if (event.target.id === 'chat-form') { event.preventDefault(); send(); }
      if (event.target.id === 'service-form') { event.preventDefault(); saveService(event.target); }
      if (event.target.id === 'service-models-form') { event.preventDefault(); saveServiceModels(); }
      if (event.target.id === 'model-form') { event.preventDefault(); const form = event.target, data = new FormData(form), s = state.services.find(x => x.id === form.dataset.service); const m = s.models.find(x => x.id === form.dataset.model); const code = String(data.get('binding-code')).trim(); if (!code) return; const model = { id: m?.id || nextId('model'), code, name: String(data.get('binding-name')).trim() || (s.protocol === 'inference' ? code.split(/[\\/]/).pop().replace(/\.gguf$/i, '') : code), source:m?.source || 'custom' }; if (m) Object.assign(m, model); else s.models.push(model); if (data.get('binding-default')) state.defaultBinding = `${s.id}/${model.id}`; manageModels(s.id); }
      if (event.target.id === 'skill-form') { event.preventDefault(); const data = new FormData(event.target), scenario = data.get('skill-scenario'); const duplicate = scenario === 'duplicate'; state.skills.push({ id: nextId('skill'), name: duplicate ? '启动器助手' : String(data.get('skill-name')), path: String(data.get('skill-path')), description: String(data.get('skill-description')), source: duplicate ? '用户目录 · 同名独立来源' : '用户目录', enabled: scenario !== 'broken', valid: scenario !== 'broken' }); closeDialog(); render(); notice(duplicate ? '同名技能以独立来源记录，未覆盖内置技能。' : '技能目录已添加（模拟）。'); }
      if (event.target.id === 'mcp-form') { event.preventDefault(); const data = new FormData(event.target), id = event.target.dataset.id; const existing = state.mcps.find(x => x.id === id); const item = { id: id || nextId('mcp'), name: String(data.get('mcp-name')), executable: String(data.get('mcp-executable')), args: String(data.get('mcp-args')), directory: String(data.get('mcp-directory')), enabled: !!data.get('mcp-enabled'), status: 'unknown' }; if (existing) Object.assign(existing, item); else state.mcps.push(item); closeDialog(); render(); notice('MCP 示例配置已保存，未启动实际程序。'); }
      if (event.target.id === 'integration-form') event.preventDefault();
    });
    const requestedPage = params.get('page');
    if (['ai','content','instances','settings','home'].includes(requestedPage)) navigate(requestedPage);
    if (params.has('repair')) openRepair(params.get('repair'));
  }
  return { start, mount, settingsMarkup, openSettings, openRepair };
})();
