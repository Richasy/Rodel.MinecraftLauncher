/* Design fixtures only. No model, files, launcher commands or game processes are used. */
const AiRepairDesign = (() => {
  const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[c]));
  const scenarios = {
    conflict: {
      title:'模组冲突', confidence:'需要验证', summary:'先验证两个渲染模组的兼容性',
      explanation:'崩溃链中出现 View Tweaks 和 Frame Boost 的注入。它们是否冲突还没有定论，先停用一项做最小范围验证。',
      original:'Mixin apply failed: view_tweaks.mixins.json:WorldRendererMixin\nCaused by: InvalidInjectionException: target modified by frame_boost\nProcess exited with code 1',
      source:'本次运行 · latest.log: 1032–1045', target:'view-tweaks-demo.jar', before:'启用', after:'停用',
      reason:'保留文件，先验证渲染注入是否仍然失败。', restore:'恢复启用状态'
    },
    dependency: {
      title:'缺少依赖', confidence:'日志明确报告', summary:'补齐 Grove Routes 声明的依赖',
      explanation:'加载器明确报告缺少 Transit API。候选需同时匹配当前游戏、加载器及声明版本范围，不能直接安装搜索结果的最新版本。',
      original:'Mod grove_routes requires transit_api [demo.2, demo.3)\nNo matching dependency was found.\nProcess exited with code 1',
      source:'本次运行 · stderr: 38–44', target:'transit-api-demo.2.jar', before:'未安装', after:'安装匹配版本',
      reason:'补齐已声明的必需依赖；不更新其它模组。', restore:'撤回本次新增文件'
    },
    java: {
      title:'Java 版本不匹配', confidence:'日志明确报告', summary:'为这个实例切换已安装的 Java 21',
      explanation:'本次实际使用 Java 17，加载器要求 Java 21。只调整当前实例的运行环境，并保留原选择。',
      original:'Incompatible Java version\nRequired Java 21 or newer; current runtime: Java 17\nProcess exited with code 1',
      source:'本次运行 · stderr: 12–15', target:'实例运行环境', before:'Java 17', after:'已安装的 Java 21',
      reason:'使用本机已验证可用的运行时，不改全局默认。', restore:'恢复此实例原来的 Java 选择'
    },
    runtime: {
      title:'游戏期间内存不足', confidence:'日志明确报告', summary:'先核对内存占用与崩溃前的操作',
      explanation:'报告包含 Java heap space，但仅增加内存不能排除模组泄漏。先记录发生场景，再对这个实例做一次可回退的调整。',
      original:'java.lang.OutOfMemoryError: Java heap space\nCurrent maximum heap: 4096 MiB\nCrash report saved during the current session',
      source:'本次运行 · crash-report: 7–19', target:'实例最大内存', before:'4 GiB', after:'6 GiB（示例候选）',
      reason:'示例中已核对可用内存；保留本次运行记录，重试后继续比较。', restore:'恢复原来的 4 GiB'
    },
    unknown: {
      title:'原因尚不明确', confidence:'证据不足', summary:'当前记录不足以提出可靠的改动',
      explanation:'只读到异常退出，没有关联到可读取的崩溃报告。不依据日志里出现的模组名称批量停用，也不把未找到报告当成没有崩溃。',
      original:'Process exited with code 1\nNo readable crash report was associated with this session.', source:'本次运行 · 进程记录'
    }
  };
  const button = (label, action, kind = '') => `<button type="button" class="${kind === 'text-action' ? kind : `small-button ${kind}`}" data-assistant-action="repair-${action}">${label}</button>`;
  function create(kind, original, target) {
    const key = Object.hasOwn(scenarios, kind) ? kind : 'unknown';
    return { instanceName:target.name, loader:target.loader, gameVersion:target.version, modCount:target.mods, key, fixture:scenarios[key], original:original || scenarios[key].original, phase:'context', busy:false,
      attempt:0, candidateIndex:0, originSession:7, session:7, applied:false, notes:[], history:[], events:[], epoch:0,
      evidence:false, receiptOpen:false, rollback:new Map(), currentPlan:[], needRepro:false };
  }
  function header(r) {
    return `<div class="repair-context"><span role="status">${escape(labels[r.phase] || r.fixture.summary)}</span>${r.applied && !r.busy ? button('撤销','undo','text-action') : ''}</div>`;
  }
  function evidence(r) {
    return `<details class="repair-evidence" ${r.evidence ? 'open' : ''}><summary>故障依据 · 运行 #${String(r.originSession).padStart(2,'0')} · ${escape(r.fixture.source.replace('本次运行 · ',''))}</summary><pre>${escape(r.original)}</pre><dl><div><dt>模组清单</dt><dd>${escape(r.modCount)} 项 · 故障发生时的快照</dd></div><div><dt>运行环境</dt><dd>Java ${r.key === 'java' ? '17' : '21'} · 最大内存 4 GiB</dd></div><div><dt>处理范围</dt><dd>当前实例 · 可回退变更 · 自动重启与检查</dd></div></dl></details>`;
  }
  function changeRows(changes) {
    return changes.map(change => `<div class="repair-change"><span class="repair-change-symbol">${change.before === '未安装' ? '+' : '~'}</span><div><strong dir="ltr">${escape(change.target)}</strong><p>${escape(change.before)} → ${escape(change.after)}</p><small>${escape(change.reason)}</small></div><span class="repair-change-tag">${change.before === '未安装' ? '新增' : '调整'}</span></div>`).join('');
  }
  function plan(r) {
    if (!r.currentPlan.length) return '';
    return `<details class="repair-plan" ${['planning','applying'].includes(r.phase) ? 'open' : ''}><summary>第 ${r.attempt} 次方案 · ${r.currentPlan.length} 项改动</summary><div class="repair-changes">${changeRows(r.currentPlan)}</div><div class="repair-plan-footer"><span>先记录原状态，自动应用并验证；可随时停止。</span></div></details>`;
  }
  const labels = {
    context:'开始处理这次故障', analyzing:'正在分析故障依据', planning:'已冻结最小修复方案', applying:'正在应用可回退变更',
    launching:'正在重新启动验证', observing:'正在核对本次运行', verified:'启动检查通过', partial:'启动已恢复，原场景尚未验证',
    blocked:'需要补充诊断依据', paused:'自动修复已停止', uncertain:'已停止，操作结果待核对', restored:'已恢复修复前的状态',
    restoring:'正在恢复原状态', reproducing:'正在隔离副本中复现场景', reproduced:'本次复现检查通过'
  };
  function transcript(r, ready) {
    const prior = r.history.length ? `<details class="repair-history"><summary>此前 ${r.history.length} 次尝试 · 保留每次依据和变更</summary>${r.history.map(item => `<p>${escape(item)}</p>`).join('')}</details>` : '';
    const notes = r.notes.map(note => `<article class="message user"><header><strong>你</strong></header><div class="message-text">${escape(note.text)}</div>${note.images?.length ? `<div class="chat-references">${note.images.map(image => `<div class="chat-reference"><img src="${escape(image.url)}" alt="${escape(image.name)}"><span>${escape(image.name)}</span></div>`).join('')}</div>` : ''}${note.queued ? '<small>排队中</small>' : ''}</article>`).join('');
    const timeline = r.events.length ? `<details class="repair-events" ${r.busy ? 'open' : ''}><summary>自动处理记录 · ${r.attempt} 次方案</summary><ol class="repair-timeline">${r.events.map(event => `<li class="${event.failed ? 'is-failed' : ''}"><strong>${escape(event.title)}</strong><span>${escape(event.detail)}</span></li>`).join('')}</ol></details>` : '';
    let actions = '';
    if (r.phase === 'context') actions = `<p>将分析故障、应用当前实例内可回退的改动，并自动重启检查。</p>${ready ? button('开始自动修复','diagnose','primary') : '<button class="small-button primary" data-assistant-action="manage-services">选择模型</button>'}`;
    if (['paused','uncertain'].includes(r.phase)) actions = `<p>已停止继续操作，已有记录仍保留。继续时先核对已提交的任务结果。</p>${button('核对并继续修复','resume','primary')}${r.applied ? button('撤销本次修复','undo') : ''}`;
    if (r.phase === 'verified' || r.phase === 'reproduced') actions = `<div class="repair-checks"><p>✓ 加载器初始化完成</p><p>✓ 游戏窗口已出现</p><p>✓ 验证记录中未再出现原异常</p>${r.phase === 'reproduced' ? '<p>✓ 隔离副本中的指定操作已复现并检查</p>' : ''}</div><p class="repair-scope">验证范围：${r.phase === 'reproduced' ? '本次指定的复现场景' : '加载器初始化与启动'} · 运行 #${String(r.session).padStart(2,'0')}</p><div class="repair-actions">${button('查看变更记录','receipt')}${button('撤销本次修复','undo')}</div>`;
    if (r.phase === 'partial') actions = `<p>新运行已经完成启动检查。此前的内存不足发生在游戏期间，需要知道崩溃前的操作，才能在副本中继续复现。</p>${r.reproKind === 'action' ? '<p>在下方补充具体操作后，将继续复现检查。</p>' : `<section class="chat-user-question"><small>需要你的回答</small><div class="chat-question-body"><p>崩溃通常发生在什么时候？</p><div class="repair-actions">${button('进入世界时','repro-world')}${button('特定操作后','repro-action')}</div><span>也可以在下方补充具体操作。</span></div></section>`}`;
    if (r.phase === 'restored') actions = `<p>已恢复这轮修复前的状态，诊断记录仍然保留。</p>${button('再次分析此故障','diagnose')}`;
    if (r.phase === 'blocked') actions = '<p>现有记录没有足够依据，暂不改动文件。可以补充其它崩溃报告或发生前的具体操作。</p>';
    const receipt = r.receiptOpen ? `<section class="repair-result"><h3>本次变更与回退记录</h3>${changeRows(r.currentPlan)}<p>原状态按目标保存，重复试验不会覆盖最初的回退点。</p></section>` : '';
    const explanation = r.candidateIndex > 0 ? '前一次验证仍出现同一异常。已恢复上一项试验，改为验证另一个候选。' : r.fixture.explanation;
    return `<div class="repair-transcript">${prior}${evidence(r)}${notes}<article class="message assistant"><div class="repair-finding"><span class="repair-confidence">${r.busy ? '自动处理中' : ['verified','reproduced'].includes(r.phase) ? '已完成可验证检查' : escape(r.fixture.confidence)}</span><h2>${escape(labels[r.phase] || r.fixture.summary)}</h2>${['analyzing','planning','applying'].includes(r.phase) ? `<p>${escape(explanation)}</p>` : ''}</div>${timeline}${plan(r)}${actions}${receipt}</article></div>`;
  }
  function event(r, title, detail, failed = false) { r.events.push({title,detail,failed}); }
  // Timed fixture transitions illustrate proposed Core observations, not a second production agent loop.
  function step(r, notify, phase, next, delay = 1050) {
    const epoch = ++r.epoch;
    r.phase = phase; r.busy = true; notify();
    setTimeout(() => { if (r.epoch !== epoch) return; next(); }, delay);
  }
  function finish(r, notify, phase) {
    r.phase = phase; r.busy = false; r.notes.forEach(note => note.queued = false); notify();
  }
  function verify(r, notify) {
    event(r,'已启动验证进程',`运行 #${String(r.session).padStart(2,'0')}，继续读取本次日志。`);
    step(r, notify, 'observing', () => {
      if (r.key === 'conflict' && r.candidateIndex === 0) {
        event(r,'第一次验证未通过','新运行再次报告相同的渲染注入异常。',true);
        r.history.push(`运行 #${r.session}：停用 View Tweaks 后仍崩溃；原状态与日志已保留。`);
        r.candidateIndex = 1;
        diagnose(r, notify); return;
      }
      event(r,'启动检查通过',`运行 #${r.session} 的初始化记录、窗口与原错误签名检查通过。`);
      finish(r, notify, r.key === 'runtime' ? 'partial' : 'verified');
    },1400);
  }
  function launch(r, notify) {
    r.session++;
    step(r, notify, 'launching', () => {
      event(r,'游戏窗口已出现','继续检查加载器初始化与新错误，不据此直接宣布修复。');
      verify(r, notify);
    });
  }
  function apply(r, notify) {
    step(r, notify, 'applying', () => {
      for (const change of r.currentPlan) if (!r.rollback.has(change.target)) r.rollback.set(change.target,change.before);
      r.applied = true;
      event(r,`第 ${r.attempt} 次方案已应用`,`${r.currentPlan.length} 项变更，原状态与写后结果均已记录。`);
      launch(r, notify);
    });
  }
  function diagnose(r, notify) {
    step(r, notify, 'analyzing', () => {
      if (r.key === 'unknown') { finish(r, notify, 'blocked'); return; }
      r.attempt++;
      r.currentPlan = r.candidateIndex === 0 ? [r.fixture] : [
        {target:'view-tweaks-demo.jar',before:'停用',after:'启用',reason:'恢复上一次未通过的试验。'},
        {target:'frame-boost-demo.jar',before:'启用',after:'停用',reason:'以新运行的异常依据验证另一个候选。'}
      ];
      event(r,`第 ${r.attempt} 次分析完成`,r.candidateIndex === 0 ? r.fixture.summary : '撤回上一项试验，再做下一项最小变更。');
      step(r, notify, 'planning', () => apply(r, notify),1300);
    });
  }
  function act(r, action, notify) {
    if (action === 'evidence') { r.evidence = !r.evidence; notify(); return; }
    if (action === 'receipt') { r.receiptOpen = !r.receiptOpen; notify(); return; }
    if (action === 'stop') {
      const wasApplying = r.phase === 'applying'; r.epoch++; r.busy = false;
      r.notes.forEach(note => note.queued = false); r.phase = wasApplying ? 'uncertain' : 'paused'; notify(); return;
    }
    if (r.busy) return;
    switch (action) {
      case 'diagnose': diagnose(r,notify); break;
      case 'resume':
        if (r.applied || r.phase === 'uncertain') {
          for (const change of r.currentPlan) if (!r.rollback.has(change.target)) r.rollback.set(change.target,change.before);
          event(r,'已核对原操作结果','沿用原任务记录，未重新提交同一项改动。'); r.applied = true; launch(r,notify);
        } else diagnose(r,notify);
        break;
      case 'undo':
        step(r,notify,'restoring',() => { event(r,'已恢复原状态','只恢复本次目标，变更后状态需与记录一致。'); r.applied=false; finish(r,notify,'restored'); }); break;
      case 'repro-world':
      case 'repro-action':
        r.notes.push({text:action === 'repro-world' ? '进入世界时发生。' : '在特定操作后发生，需要补充具体操作。',queued:false});
        if (action === 'repro-action') { r.reproKind = 'action'; notify(); break; }
        step(r,notify,'reproducing',() => { event(r,'指定复现场景检查通过','示例验证在隔离副本中进入同一世界并核对新日志。'); finish(r,notify,'reproduced'); },1600); break;
    }
  }
  function ask(r, text, images, notify) {
    r.notes.push({text,images,queued:r.busy});
    if (r.phase === 'partial' && r.reproKind === 'action' && text.trim())
      step(r,notify,'reproducing',() => { event(r,'指定操作检查通过','示例验证在隔离副本中复现用户补充的操作并核对新日志。'); finish(r,notify,'reproduced'); },1600);
  }
  return {create,header,transcript,act,ask};
})();
