/* Interactive design only: fictional profiles and explicit review outcomes. No authentication requests. */
(() => {
  const esc = escapeHtml;
  const accounts = window.rodelAccountsReview;
  const review = new URLSearchParams(location.search).get('accounts') === '1';
  const paths = {
    globe: 'M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0M3 12h18M12 3a19 19 0 0 1 0 18 19 19 0 0 1 0-18',
    back: 'M19 12H5m6-6-6 6 6 6', close: 'M6 6l12 12M18 6L6 18',
    arrow: 'M9 5l7 7-7 7', alert: 'M12 3L2 21h20L12 3zM12 9v5m0 3v1',
    link: 'M14 3h7v7M21 3L10 14M10 5H4v15h15v-6',
    eye: 'M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0',
    copy: 'M9 9h12v12H9zM15 9V3H3v12h6', file: 'M14 2H5v20h14V7zM14 2v6h5',
    check: 'M5 12l4 4L19 6', info: 'M12 11v6m0-10v1M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0'
  };
  const icon = name => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${paths[name] || paths.globe}"/></svg>`;
  const skinMark = '<span class="external-skin-mark" aria-hidden="true"><img src="assets/littleskin.png" alt="" width="28" height="28"></span>';
  const littleSkin = { name: 'LittleSkin', endpoint: 'https://littleskin.cn/api/yggdrasil', builtin: true };
  let panel, page = 'sources', provider = { ...littleSkin }, target = null;
  let draft = {}, phase = 'idle', fault = null, selectedRole = null, reveal = false;
  let details = false, hasCache = true, resume = 'login', generation = 0, passwordMode = false;
  let codeSequence = 0, userCode = '', pageOpened = false;
  let copyState = 'idle', copyRequest = 0;
  let reviewSelection = '';
  const reviewScenes = [
    ['入口', [['sources', '登录来源选择']]],
    ['LittleSkin', [
      ['ls-device', '等待授权'], ['ls-opened', '已打开授权网页'], ['ls-code', '授权码过期'],
      ['ls-denied', '拒绝授权'], ['ls-browser', '浏览器未打开'], ['ls-client', '设备码未开通 · 密码登录'], ['ls-password-error', '密码登录失败'],
      ['ls-network', '连接超时'], ['ls-tls', 'TLS 证书错误'], ['ls-malformed', '返回内容错误'],
      ['ls-remote', '服务端自定义错误'], ['ls-rate', '请求过多'], ['ls-roles', '选择角色'],
      ['ls-no-roles', '没有角色'], ['ls-reauth', '已有账户重新登录']
    ]],
    ['其他认证服务', [
      ['custom-form', '填写登录信息'], ['custom-filled', '已填写登录信息'], ['custom-http', 'HTTP 地址登录'],
      ['custom-waiting', '正在登录'], ['custom-password', '账号或密码错误'], ['custom-network', '连接超时'],
      ['custom-tls', 'TLS 证书错误'], ['custom-malformed', '返回内容错误'], ['custom-remote', '服务端自定义错误'],
      ['custom-details', '错误详情 Flyout'], ['custom-rate', '请求过多'], ['custom-roles', '选择角色'],
      ['custom-selected', '角色已选择'], ['custom-no-roles', '没有角色'], ['custom-deleted', '原角色已删除'],
      ['custom-reauth', '已有账户重新登录']
    ]],
    ['启动与组件', [
      ['refresh', '启动前网络失败'], ['no-cache', '启动前无缓存'], ['refresh-waiting', '正在验证会话'],
      ['expired', '登录凭据失效'], ['agent', '组件准备失败'], ['agent-pending', '正在准备组件']
    ]]
  ];
  let lastReviewEvent = '';
  const roles = [
    { role: 'meadow', name: 'MeadowFox', avatar: { kind: 'preset', id: 'fox' } },
    { role: 'cloud', name: 'CloudWalker', avatar: { kind: 'preset', id: 'young-boy' } }
  ];
  const button = (label, action, primary = false, disabled = false) => `<button type="button" class="small-button ${primary ? 'account-primary' : 'subtle'}" data-external-action="${action}"${disabled ? ' disabled' : ''}>${label}</button>`;
  const textButton = (label, action) => `<button type="button" class="external-text-button" data-external-action="${action}">${label}</button>`;
  const summary = () => `<div class="external-service">${provider.builtin ? skinMark : icon('globe')}<div><strong>${esc(provider.name)}</strong><span dir="ltr">${esc(provider.builtin ? 'littleskin.cn' : provider.endpoint)}</span></div></div>`;
  const cancelButton = label => `<button type="button" class="external-text-button external-inline-cancel" data-external-action="cancel" aria-label="${label}">取消</button>`;
  const pending = (label, context, cancelLabel = '取消登录') => `<div class="external-pending"><span class="external-spinner" aria-hidden="true"></span><div role="status"><p class="external-pending-label">${esc(label)}</p><p class="external-pending-context">${esc(context)}</p></div>${cancelButton(cancelLabel)}</div>`;
  const faults = {
    password: { title: '账号或密码不正确', text: '请检查登录信息后重试。', raw: 'HTTP 403 Forbidden\nForbiddenOperationException\nInvalid username or password.' },
    network: { title: '暂时无法连接认证服务', text: '连接超时，请检查网络后重试。', raw: 'Connection timed out (connect)\n请求阶段：连接认证服务' },
    tls: { title: '无法建立安全连接', text: '认证服务的证书验证失败，请核对地址或联系服务管理员。', raw: 'SSLHandshakeException\nNo subject alternative DNS name matching skin.example.com found.' },
    malformed: { title: '认证服务返回了无法识别的内容', text: '该地址可能不是认证接口，或服务暂时异常。', raw: 'HTTP 200 OK\nContent-Type: text/html\nExpected a Yggdrasil JSON response; received an HTML page.' },
    remote: { title: '认证服务拒绝了此次登录', text: '此账户尚未完成邮箱验证，请先验证邮箱。', raw: 'HTTP 403 Forbidden\nForbiddenOperationException\n此账户尚未完成邮箱验证，请先验证邮箱。' },
    rate: { title: '登录请求过于频繁', text: '认证服务暂时限制了请求，请稍后重试。', raw: 'HTTP 429 Too Many Requests\nRetry-After: 30\nToo many authentication attempts.' },
    expired: { title: '登录凭据已失效', text: '请重新登录此账户后继续。', raw: 'HTTP 403 Forbidden\nForbiddenOperationException\nInvalid token.' },
    agent: { title: '无法准备外置登录组件', text: '登录组件下载失败，游戏尚未启动。可以重试，或选择已有的组件文件。', raw: 'AuthlibInjectorDownloadException\nFailed to download authlib-injector.\nCaused by: connection timed out.' },
    code: { title: '授权码已过期', text: '重新获取授权码后即可继续登录。', raw: 'OAuth error: expired_token\nThe device code has expired.' },
    denied: { title: '未获得登录授权', text: '你已在网页中拒绝授权。需要使用此账户时，可以重新登录。', raw: 'OAuth error: access_denied\nThe user denied the authorization request.' },
    browser: { title: '未能打开浏览器', text: '可以重试打开，或在其他设备访问下方网页并输入授权码。', raw: 'Unable to open the default browser.' },
  };
  function rawError() {
    if (!fault?.raw) return '';
    return `<div id="external-error-flyout" class="external-error-flyout" popover="auto" role="region" aria-label="错误详情"><div class="external-error-heading"><strong>错误详情</strong><button type="button" class="external-error-copy" data-external-action="copy-error" aria-label="复制错误详情" title="复制错误详情">${icon('copy')}</button></div><pre tabindex="0" aria-label="原始错误">${esc(fault.raw)}</pre></div>`;
  }
  function notice() {
    return fault ? `<div class="external-notice" role="alert">${icon('alert')}<div class="external-notice-content"><div class="external-notice-title"><h3>${esc(fault.title)}</h3>${fault.raw ? `<button type="button" class="external-info-button" popovertarget="external-error-flyout" aria-controls="external-error-flyout" aria-expanded="false" aria-label="查看错误详情" title="错误详情">${icon('info')}</button>` : ''}</div><p>${esc(fault.text)}</p></div>${rawError()}</div>` : '';
  }
  function positionErrorFlyout() {
    const flyout = panel?.querySelector('#external-error-flyout');
    const trigger = panel?.querySelector('.external-info-button');
    if (!flyout?.matches(':popover-open') || !trigger) return;
    const anchor = trigger.getBoundingClientRect(), bounds = flyout.getBoundingClientRect();
    flyout.style.left = `${Math.max(12, Math.min(anchor.right - bounds.width, innerWidth - bounds.width - 12))}px`;
    const below = anchor.bottom + 8;
    flyout.style.top = `${below + bounds.height <= innerHeight - 12 ? below : Math.max(12, anchor.top - bounds.height - 8)}px`;
    flyout.style.visibility = 'visible';
  }
  function copyDeviceCode() {
    if (!panel || page !== 'device' || fault === faults.code || fault === faults.denied) return;
    const code = userCode, request = ++copyRequest;
    const result = navigator.clipboard?.writeText(code) || Promise.reject(new Error('Clipboard unavailable'));
    result.then(() => update('copied'), () => update('failed'));
    function update(state) {
      if (!panel || page !== 'device' || userCode !== code || copyRequest !== request || fault === faults.code || fault === faults.denied) return;
      copyState = state;
      const control = panel.querySelector('[data-external-action="copy-code"]');
      control.title = state === 'copied' ? '已复制，点击再次复制' : '点击复制授权码';
      control.querySelector('.external-code-copy-icon').innerHTML = icon(state === 'copied' ? 'check' : 'copy');
      panel.querySelector('.external-copy-feedback').textContent = state === 'copied' ? '已复制' : '未能复制，请点击重试';
    }
  }
  function reviewControls() {
    if (!review) return '';
    return `<aside class="account-simulation external-simulation" aria-label="外置账户设计评审工具"><span>设计评审 · 选择即预览，请勿输入真实凭据</span><div class="external-review-controls"><select data-external-preview aria-label="预览流程状态"><option value="" disabled${!reviewSelection ? ' selected' : ''}>选择流程状态…</option>${reviewScenes.map(([group, scenes]) => `<optgroup label="${group}">${scenes.map(([value, label]) => `<option value="${value}"${value === reviewSelection ? ' selected' : ''}>${label}</option>`).join('')}</optgroup>`).join('')}</select><button data-theme-choice="light">浅色</button><button data-theme-choice="dark">深色</button><button data-theme-choice="contrast">高对比</button></div></aside>`;
  }
  function mount() {
    if (panel) return;
    accounts.close();
    panel = document.createElement('dialog');
    panel.className = 'account-popup external-account-popup';
    panel.setAttribute('aria-labelledby', 'external-title');
    panel.setAttribute('aria-modal', 'true');
    document.querySelector('.launcher').append(panel);
    panel.addEventListener('cancel', event => { event.preventDefault(); close(); accounts.open(); });
    panel.addEventListener('click', event => {
      const role = event.target.closest('[data-external-role]');
      if (role) { selectedRole = role.dataset.externalRole; show('roles'); panel.querySelector(`[data-external-role="${selectedRole}"]`).focus(); return; }
      const control = event.target.closest('[data-external-action]');
      if (control) { event.preventDefault(); act(control.dataset.externalAction); }
    });
    panel.addEventListener('input', event => {
      if (event.target.name) draft[event.target.name] = event.target.value;
      if (page === 'password') {
        panel.querySelector('[data-external-action="password-login"]').disabled = !draft.username?.trim() || !draft.password || (!target && !draft.address?.trim());
      }
    });
    panel.addEventListener('change', event => {
      if (event.target.matches('[data-external-preview]')) previewScene(event.target.value);
    });
    panel.addEventListener('keydown', event => {
      const flyout = panel.querySelector('#external-error-flyout');
      if (event.key === 'Escape' && flyout?.matches(':popover-open')) {
        event.preventDefault(); event.stopPropagation(); flyout.hidePopover();
        panel.querySelector('.external-info-button')?.focus(); return;
      }
      if (event.target.closest('.external-role-list') && ['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
        event.preventDefault();
        const index = roles.findIndex(role => role.role === event.target.closest('[data-external-role]')?.dataset.externalRole);
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? roles.length - 1 : (index + (event.key === 'ArrowDown' ? 1 : -1) + roles.length) % roles.length;
        selectedRole = roles[next].role; show('roles');
      }
    });
    panel.addEventListener('submit', event => {
      event.preventDefault();
      if (page === 'password') act('password-login');
    });
    panel.addEventListener('scroll', event => {
      if (event.target.classList?.contains('account-body')) {
        const flyout = panel.querySelector('#external-error-flyout');
        if (flyout?.matches(':popover-open')) flyout.hidePopover();
      }
    }, true);
    panel.showModal();
  }
  function close() {
    generation++; copyRequest++;
    phase = 'idle'; draft = {}; reveal = false;
    panel?.close(); panel?.remove(); panel = null;
  }
  function reset(account = null) {
    generation++; target = account; draft = {}; fault = null; details = false;
    reviewSelection = '';
    selectedRole = null; phase = 'idle'; hasCache = true; resume = 'login'; passwordMode = false;
    provider = account ? { name: account.source, endpoint: account.endpoint, builtin: account.source === 'LittleSkin' } : { ...littleSkin };
    if (account) draft.address = account.endpoint;
  }
  function show(next, focusSelector = '') {
    mount(); page = next; panel.dataset.view = `external-${next}`;
    let title = '添加外置账户', body = '', footer = '', back = 'sources';
    if (next === 'sources') {
      body = `<div class="external-provider-list"><button class="account-choice" data-external-action="littleskin">${skinMark}<span><strong>LittleSkin</strong><small>登录并选择你的游戏角色</small></span>${icon('arrow')}</button><button class="account-choice" data-external-action="custom">${icon('globe')}<span><strong>其他认证服务</strong><small>填写认证地址和账号密码</small></span>${icon('arrow')}</button></div>`;
      back = 'list';
    } else if (next === 'device') {
      title = target ? '重新登录 LittleSkin' : '登录 LittleSkin'; back = 'sources';
      const ended = fault === faults.code || fault === faults.denied;
      body = `${notice()}${!fault ? '<p class="external-device-intro">打开授权网页，确认本次登录。</p>' : ''}<button type="button" class="external-device-code${ended ? ' is-expired' : ''}" data-external-action="copy-code" aria-label="复制授权码" aria-describedby="external-user-code" title="${copyState === 'copied' && !ended ? '已复制，点击再次复制' : '点击复制授权码'}"${ended ? ' disabled' : ''}><code id="external-user-code" dir="ltr">${[...userCode].map(character => `<span>${esc(character)}</span>`).join('')}</code><span class="external-code-copy-icon" aria-hidden="true">${icon(copyState === 'copied' && !ended ? 'check' : 'copy')}</span></button><div class="external-device-meta"><span class="external-verification-address" dir="ltr">open.littleskin.cn/oauth/link</span><span class="external-copy-feedback" role="status">${ended ? '' : copyState === 'copied' ? '已复制' : copyState === 'failed' ? '未能复制，请点击重试' : ''}</span></div>${!ended ? `<div class="external-device-status"><span class="external-status-dot${fault ? ' is-paused' : ''}" aria-hidden="true"></span><span role="status">${fault ? '授权码仍可使用' : '等待授权，完成后自动继续'}</span></div>` : ''}`;
      footer = button(ended ? fault === faults.code ? '重新获取授权码' : '重新登录' : fault === faults.network ? '重试连接' : pageOpened || fault === faults.browser ? '重新打开网页' : '打开授权网页', ended ? 'restart' : fault === faults.network ? 'retry-device' : 'open-device', true) + (!ended ? button('取消', 'cancel') : '');
    } else if (next === 'password') {
      title = provider.builtin ? '登录 LittleSkin' : target ? '重新登录' : '添加外置账户'; back = target ? 'list' : 'sources';
      body = `${target ? summary() : ''}<form id="external-password-form">${notice()}${!target && !provider.builtin ? `<label for="external-address">认证地址</label><input id="external-address" name="address" dir="ltr" autocomplete="off" placeholder="可粘贴网站或 Yggdrasil API 地址。" value="${esc(draft.address || '')}" aria-invalid="${!!fault?.address}">` : ''}<label for="external-username">${provider.builtin ? '邮箱或角色名' : '邮箱或用户名'}</label><input id="external-username" name="username" autocomplete="off" placeholder="${provider.builtin ? '输入邮箱或游戏角色名' : '输入邮箱或用户名'}" value="${esc(draft.username || '')}" aria-invalid="${fault === faults.password}"><label for="external-password">密码</label><div class="external-password"><input id="external-password" name="password" autocomplete="off" placeholder="输入密码" type="${reveal ? 'text' : 'password'}" value="${esc(draft.password || '')}" aria-invalid="${fault === faults.password}"><button type="button" data-external-action="reveal" aria-label="${reveal ? '隐藏' : '显示'}密码" aria-pressed="${reveal}" title="${reveal ? '隐藏' : '显示'}密码">${icon('eye')}</button></div></form>`;
      footer = button('登录', 'password-login', true, !draft.username?.trim() || !draft.password || (!target && !draft.address?.trim())).replace('type="button"', 'type="submit" form="external-password-form"');
    } else if (next === 'waiting') {
      title = resume === 'roles' ? '读取游戏角色' : resume === 'refresh' ? '验证账户' : target ? '重新登录' : '登录外置账户';
      body = pending(resume === 'roles' ? '正在读取角色…' : resume === 'refresh' ? '正在验证会话…' : '正在登录…', target ? `${target.name} · ${provider.name}` : provider.name);
    } else if (next === 'roles') {
      title = '选择游戏角色'; back = 'cancel';
      body = `${summary()}<div class="external-role-list" role="radiogroup" aria-label="选择要添加的游戏角色">${roles.map(role => `<button role="radio" aria-checked="${selectedRole === role.role}" tabindex="${(selectedRole || roles[0].role) === role.role ? 0 : -1}" class="external-role" data-external-role="${role.role}"><span class="account-face">${window.rodelAvatarPicker.image(role.avatar)}</span><strong>${role.name}</strong><span class="external-radio" aria-hidden="true"></span></button>`).join('')}</div>`;
      footer = button('添加账户', 'add-role', true, !selectedRole);
    } else if (next === 'no-roles') {
      title = '尚未创建游戏角色'; back = 'cancel';
      body = `${summary()}<div class="external-empty"><h3>登录成功，但还没有角色</h3><p>请先在 ${esc(provider.name)} 创建游戏角色，再重新读取。</p></div><div class="external-inline-actions">${textButton(`前往管理角色 ${icon('link')}`, 'open-web')}</div>`;
      footer = button('重新读取角色', 'read-roles', true);
    } else if (next === 'deleted') {
      title = '游戏角色已不存在'; back = 'list';
      body = `${summary()}<div class="external-notice" role="alert">${icon('alert')}<div><h3>${esc(target?.name || '原角色')} 已被删除或不再属于此账户</h3><p>请在认证服务中确认角色状态，也可以另行添加其他角色。</p></div></div>`;
      footer = button('重新读取', 'read-roles', true) + button('添加其他角色', 'other-role');
    } else if (next === 'refresh') {
      title = '启动前验证未完成'; back = 'cancel';
      body = `${summary()}${notice()}<p class="external-cache-note">${hasCache ? '可以沿用此角色的缓存会话尝试本次启动。联机认证和皮肤加载仍可能失败。' : '此角色没有可用的缓存会话，请恢复连接后重试。'}</p>`;
      footer = button('重试验证', 'retry', true) + (hasCache ? button('使用缓存尝试', 'use-cache') : '');
    } else if (next === 'agent') {
      title = '外置登录组件未就绪'; back = 'cancel';
      body = `${summary()}${notice()}<p class="external-field-hint">此组件由启动器加载，无需安装皮肤 Mod。</p><input type="file" id="external-agent-file" accept=".jar" hidden>`;
      footer = button('重试准备', 'retry-agent', true) + button('选择本地文件', 'choose-agent');
    } else if (next === 'agent-check') {
      title = '准备登录组件';
      body = pending('正在准备并校验组件…', draft.artifact || 'authlib-injector.jar', '取消准备');
    } else if (next === 'error') {
      title = '登录未完成'; back = 'cancel';
      body = `${summary()}${notice()}`;
      if (fault === faults.expired) footer = button('重新登录', 'restart', true);
      else footer = button('重试', 'retry', true) + (provider.builtin ? '' : button('返回修改', 'edit'));
    }
    panel.setAttribute('role', 'dialog');
    const inlineCancel = ['waiting', 'agent-check'].includes(next) || (next === 'device' && fault !== faults.code && fault !== faults.denied);
    panel.innerHTML = `<header>${inlineCancel && next !== 'device' ? '' : `<button class="account-back" data-external-action="${back}" aria-label="${back === 'list' ? '返回账户管理' : back === 'sources' ? '返回登录来源选择' : '返回'}">${icon('back')}</button>`}<h2 id="external-title">${title}</h2>${inlineCancel ? '' : `<button class="account-close" data-external-action="cancel" aria-label="关闭外置账户面板" title="关闭外置账户面板">${icon('close')}</button>`}</header><div class="account-body" tabindex="0" aria-label="外置账户内容"><div class="account-body-inner">${body}</div></div>${footer ? `<footer>${footer}</footer>` : ''}${reviewControls()}`;
    const errorFlyout = panel.querySelector('#external-error-flyout');
    const errorTrigger = panel.querySelector('.external-info-button');
    if (errorFlyout) {
      errorFlyout.addEventListener('beforetoggle', event => { if (event.newState === 'open') errorFlyout.style.visibility = 'hidden'; });
      errorFlyout.addEventListener('toggle', event => {
        details = event.newState === 'open'; errorTrigger.setAttribute('aria-expanded', String(details));
        if (details) { positionErrorFlyout(); errorFlyout.querySelector('pre').focus({ preventScroll: true }); }
      });
    }
    if (next === 'agent') panel.querySelector('#external-agent-file').addEventListener('change', event => {
      const file = event.target.files[0]; if (!file) return;
      draft.artifact = file.name; phase = 'pending'; show('agent-check');
    });
    const expectedPage = page;
    requestAnimationFrame(() => {
      if (!panel || page !== expectedPage) return;
      (focusSelector ? panel.querySelector(focusSelector) : next === 'roles' ? panel.querySelector(`[data-external-role="${selectedRole || roles[0].role}"]`) : ['waiting', 'agent-check'].includes(next) ? panel.querySelector('.external-inline-cancel') : panel.querySelector('#external-address, #external-username') || panel.querySelector('footer .account-primary:not(:disabled)') || panel.querySelector('.account-back, .account-close'))?.focus();
      if (details && errorFlyout?.isConnected) errorFlyout.showPopover();
    });
  }
  function beginLogin() {
    generation++; fault = null; details = false; resume = 'login'; passwordMode = false;
    if (provider.builtin) {
      userCode = ['RDLX-7QMP', 'NQPW-8ZRK', 'FCTB-6VMH'][codeSequence++ % 3];
      copyState = 'idle'; phase = 'pending'; pageOpened = false; show('device'); copyDeviceCode();
    } else { phase = 'idle'; show('password'); }
  }
  function commit(role) {
    const record = target ? { ...target, status: 'ready' } : {
      ...role, id: `external-${encodeURIComponent(provider.endpoint)}-${role.role}`,
      source: provider.name, endpoint: provider.endpoint, kind: 'external', status: 'ready'
    };
    close(); accounts.addExternal(record);
  }
  function settle(outcome) {
    if (!panel) return;
    details = false;
    if (outcome === 'client') {
      generation++; fault = null; phase = 'idle'; resume = 'login'; passwordMode = true;
      draft.address = provider.endpoint; draft.password = ''; show('password'); return;
    }
    if (outcome === 'success' || outcome === 'single') {
      if (!provider.builtin && !target && resume === 'login' && phase === 'pending') {
        const address = new URL(provider.endpoint);
        provider.name = address.hostname === 'skin.example.com' ? '星河社区' : address.hostname;
        if (address.hostname === 'skin.example.com' && address.pathname === '/') provider.endpoint += '/api/yggdrasil';
      }
      if (phase !== 'pending') { showToast('请先开始登录、读取角色或准备组件，再模拟成功返回。'); return; }
      phase = 'idle'; fault = null;
      if (page === 'agent-check' || resume === 'refresh') { lastReviewEvent = '模拟：已将原角色交回本次启动流程；默认身份未改变。'; close(); accounts.open(); return; }
      if (target) { commit(target); return; }
      if (outcome === 'single') { commit(roles[0]); return; }
      selectedRole = null; show('roles'); return;
    }
    phase = 'idle';
    if (outcome === 'no-roles') { show('no-roles'); return; }
    if (outcome === 'deleted') { if (target) accounts.updateExternal(target.id, { status: 'missing' }); show('deleted'); return; }
    fault = faults[outcome] || faults.remote;
    if (outcome === 'expired' && target) accounts.updateExternal(target.id, { status: 'expired' });
    if (outcome === 'agent') { show('agent'); return; }
    if (page === 'device' && ['code', 'denied', 'browser', 'network'].includes(outcome)) {
      phase = outcome === 'browser' ? 'pending' : outcome === 'network' ? 'paused' : 'idle';
      show('device'); return;
    }
    if ((!provider.builtin || passwordMode) && resume === 'login') { show('password'); return; }
    show(resume === 'refresh' && outcome !== 'expired' ? 'refresh' : 'error');
  }
  function act(action) {
    if (!['copy-code', 'copy-error'].includes(action)) reviewSelection = '';
    if (action === 'list' || action === 'cancel') { close(); accounts.open(); return; }
    if (action === 'sources') { generation++; copyRequest++; draft = {}; passwordMode = false; fault = null; phase = 'idle'; show('sources'); return; }
    if (action === 'littleskin') { provider = { ...littleSkin }; target = null; draft = {}; settle('client'); return; }
    if (action === 'custom') { provider = { name: '认证服务', endpoint: '', builtin: false }; target = null; draft = {}; fault = null; show('password'); return; }
    if (action === 'password-login') {
      if (!draft.username?.trim() || !draft.password) return;
      const value = (target ? provider.endpoint : draft.address)?.trim();
      if (!value) return;
      try {
        const url = new URL(/^[a-z][a-z\d+.-]*:/i.test(value) ? value : `https://${value}`);
        if (!['http:', 'https:'].includes(url.protocol) || !url.hostname || url.username || url.password) throw new Error();
        provider.endpoint = url.href.replace(/\/$/, '');
        if (!target && !provider.builtin) provider.name = url.hostname;
      } catch { fault = { title: '认证地址格式不正确', text: '请输入 HTTP 或 HTTPS 网站地址。', raw: 'Invalid authentication service URL.', address: true }; show('password'); return; }
      fault = null; phase = 'pending'; generation++; show('waiting'); return;
    }
    if (action === 'reveal') { reveal = !reveal; const field = panel.querySelector('#external-password'); field.type = reveal ? 'text' : 'password'; const control = panel.querySelector('[data-external-action="reveal"]'); control.setAttribute('aria-pressed', String(reveal)); control.setAttribute('aria-label', `${reveal ? '隐藏' : '显示'}密码`); control.title = `${reveal ? '隐藏' : '显示'}密码`; return; }
    if (action === 'restart') { beginLogin(); return; }
    if (action === 'retry') {
      if (provider.builtin && !passwordMode && resume === 'login') { beginLogin(); return; }
      fault = null; phase = 'pending'; generation++;
      if (resume !== 'refresh' && resume !== 'roles' && (!draft.username?.trim() || !draft.password)) { phase = 'idle'; show('password'); return; }
      show('waiting'); return;
    }
    if (action === 'retry-device') { fault = null; phase = 'pending'; show('device'); return; }
    if (action === 'open-device') { pageOpened = true; if (fault === faults.browser) fault = null; show('device'); showToast('原型：已模拟打开 LittleSkin 授权网页。'); return; }
    if (action === 'edit') { fault = null; phase = 'idle'; show('password'); return; }
    if (action === 'open-web') { showToast('原型：已模拟打开对应网页。请用评审工具返回结果。'); return; }
    if (action === 'read-roles') { fault = null; phase = 'pending'; resume = 'roles'; show('waiting'); return; }
    if (action === 'other-role') { target = null; selectedRole = null; phase = 'idle'; show('roles'); return; }
    if (action === 'add-role') { const role = roles.find(item => item.role === selectedRole); if (role) commit(role); return; }
    if (action === 'use-cache') { if (!hasCache) return; lastReviewEvent = '模拟：沿用原 UUID 和缓存会话尝试本次启动；未切换默认身份。'; close(); accounts.open(); showToast('原型：使用缓存继续本次启动，默认账户未改变。'); return; }
    if (action === 'retry-agent') { fault = null; phase = 'pending'; show('agent-check'); return; }
    if (action === 'choose-agent') { panel.querySelector('#external-agent-file').click(); return; }
    if (action === 'copy-code') { copyDeviceCode(); return; }
    if (action === 'copy-error') navigator.clipboard.writeText(fault.raw).then(() => showToast('已复制'), () => showToast('未能复制，请选中文本后手动复制。'));
  }
  // Review fixtures enter complete states directly; they never authenticate or store credentials.
  function previewScene(value) {
    close(); reset(); reviewSelection = value;
    if (value === 'sources') { show('sources'); return; }
    const custom = value.startsWith('custom-');
    if (custom) {
      provider = { name: '星河社区', endpoint: 'https://skin.example.com/api/yggdrasil', builtin: false };
      draft = { address: 'skin.example.com', username: 'demo@example.com', password: 'fictional-demo-only' };
      const state = value.slice(7);
      if (state === 'form') { provider = { name: '认证服务', endpoint: '', builtin: false }; draft = {}; show('password'); }
      else if (['filled', 'http'].includes(state)) { if (state === 'http') draft.address = 'http://skin.example.com'; show('password'); }
      else if (state === 'reauth' || state === 'deleted') {
        target = { id: 'review-river', name: 'RiverStone', source: provider.name, endpoint: provider.endpoint, kind: 'external', status: 'expired' };
        show(state === 'deleted' ? 'deleted' : 'password');
      } else if (state === 'waiting') { phase = 'pending'; show('waiting'); }
      else if (state === 'roles' || state === 'selected') { selectedRole = state === 'selected' ? roles[0].role : null; show('roles'); }
      else if (state === 'no-roles') show('no-roles');
      else { fault = faults[state === 'details' ? 'remote' : state] || faults.remote; details = state === 'details'; show('password'); }
    } else if (value.startsWith('ls-')) {
      if (value === 'ls-reauth') target = { id: 'review-north', name: 'NorthWind', source: littleSkin.name, endpoint: littleSkin.endpoint, kind: 'external', status: 'expired' };
      beginLogin();
      const state = value.slice(3);
      if (state === 'password-error') {
        settle('client'); draft.username = 'demo@example.com'; draft.password = 'fictional-demo-only';
        fault = faults.password; show('password'); return;
      }
      if (state === 'opened') { pageOpened = true; show('device'); }
      else if (state === 'roles' || state === 'no-roles') settle(state === 'roles' ? 'success' : 'no-roles');
      else if (!['device', 'reauth'].includes(state)) settle(state);
    } else {
      target = { id: 'review-north', name: 'NorthWind', source: littleSkin.name, endpoint: littleSkin.endpoint, kind: 'external', status: 'ready' };
      if (value === 'refresh' || value === 'no-cache') { resume = 'refresh'; hasCache = value !== 'no-cache'; fault = faults.network; show('refresh'); }
      else if (value === 'refresh-waiting') { resume = 'refresh'; phase = 'pending'; show('waiting'); }
      else if (value === 'expired') { fault = faults.expired; show('error'); }
      else if (value === 'agent') { fault = faults.agent; show('agent'); }
      else if (value === 'agent-pending') { phase = 'pending'; show('agent-check'); }
    }
    const renderedPanel = panel;
    requestAnimationFrame(() => { if (panel === renderedPanel) panel?.querySelector('[data-external-preview]')?.focus(); });
  }
  function demo(name) {
    close(); accounts.close();
    const account = accounts.getState().accounts.find(item => item.id === 'external-littleskin-north');
    reset(account || { id: 'demo-only', name: 'NorthWind', source: 'LittleSkin', endpoint: littleSkin.endpoint, kind: 'external', status: 'ready' });
    if (name === 'refresh' || name === 'no-cache') { resume = 'refresh'; hasCache = name !== 'no-cache'; fault = faults.network; show('refresh'); }
    else if (name === 'expired') { fault = faults.expired; if (account) accounts.updateExternal(account.id, { status: 'expired' }); show('error'); }
    else if (name === 'agent') { fault = faults.agent; show('agent'); }
  }
  window.rodelExternalAccounts = {
    open: () => { close(); reset(); show('sources'); },
    reauthenticate: account => { reset(account); beginLogin(); },
    demo,
    getState: () => ({ page, phase, provider: { ...provider }, targetId: target?.id, generation, lastReviewEvent })
  };
  window.addEventListener('resize', positionErrorFlyout);
  if (review) {
    const toolbar = document.createElement('div'); toolbar.className = 'account-review external-top-review';
    toolbar.innerHTML = `<button data-external-demo="new">外置账户</button><button data-external-demo="refresh">启动前网络失败</button><button data-external-demo="no-cache">无缓存</button><button data-external-demo="expired">凭据过期</button><button data-external-demo="agent">组件失败</button>`;
    document.querySelector('.review-footer').prepend(toolbar);
    toolbar.addEventListener('click', event => { const name = event.target.closest('[data-external-demo]')?.dataset.externalDemo; if (name === 'new') window.rodelExternalAccounts.open(); else if (name) demo(name); });
    document.querySelector('.review-note').textContent = '外置账户设计 · 模拟认证与角色';
    window.addEventListener('prototype-theme-changed', () => { document.querySelector('.review-note').textContent = '外置账户设计 · 模拟认证与角色'; });
    document.querySelector('.review-footer > .account-review:not(.external-top-review)')?.remove();
    if (new URLSearchParams(location.search).get('external') === '1') window.rodelExternalAccounts.open();
  }
})();
