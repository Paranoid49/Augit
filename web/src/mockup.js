const scene = document.body.dataset.scene || "main-project";
const app = document.getElementById("app");
const requestedTheme = new URLSearchParams(window.location.search).get("theme");

// 主框架字号对照只改变界面文字，正文等宽字号与图标尺寸保持独立。
async function applyTypographyPreview() {
  const codeSize = Number(new URLSearchParams(window.location.search).get("code-size"));
  if (Number.isFinite(codeSize) && codeSize >= 9 && codeSize <= 40) {
    const scale = window.devicePixelRatio || 1;
    document.querySelectorAll(".code-view, .diff-columns, .conflict-block, .terminal-view").forEach(view => {
      view.style.fontSize = `${codeSize}px`;
      view.style.lineHeight = `${Math.round(codeSize * 1.7 * scale) / scale}px`;
    });
  }
  const value = new URLSearchParams(window.location.search).get("ui-size");
  if (value === null) {
    await document.fonts.ready;
    measureCodeViews(); measureDocumentToolbar(); measureCommitPanels(); measureConflictResolver(); measureRemoteDialog(); measureWorktreeDialog(); measureStashDialog(); measureStashManagerDialog(); measureCloneDialog(); measureResetDialog(); measureRollbackDialog(); measurePushDialog(); measureTerminalHeaders(); measureSearchOverlay();
    document.body.dataset.typographyPreview = "ready";
    return;
  }
  const size = Number(value);
  if (!Number.isFinite(size) || size < 9 || size > 40) return;
  document.documentElement.style.fontSize = `${size}px`;
  await document.fonts.ready;
  const context = document.createElement("canvas").getContext("2d");
  const family = getComputedStyle(document.documentElement).fontFamily;
  let height = 0;
  for (const role of ["normal", "600", "italic"]) {
    context.font = `${role} ${size}px ${family}`;
    const metrics = context.measureText("国Ag");
    height = Math.max(height, Math.ceil(metrics.fontBoundingBoxAscent + metrics.fontBoundingBoxDescent));
  }
  const root = document.documentElement.style;
  const length = (name, pixels) => root.setProperty(`--augit-${name}`, `${pixels}px`);
  length("title-height", Math.max(44, height + 18));
  length("title-text-height", Math.max(30, height + 4));
  length("tab-height", Math.max(42, height + 14));
  length("project-header-height", Math.max(39, height + 10));
  length("tree-height", Math.ceil(Math.max(27, height + 8) / 2) * 2);
  length("document-toolbar-height", Math.max(36, height + 8));
  length("diff-toolbar-height", Math.max(39, height + 12));
  length("diff-filebar-height", Math.max(31, height + 12));
  length("diff-unified-row-height", Math.max(24, height + 4));
  length("diff-unified-filebar-height", Math.max(55, Math.max(24, height + 4) * 2 + 7));
  length("status-height", Math.max(22, height + 2));
  length("find-height", Math.max(42, height + 16));
  length("find-edit-height", Math.max(30, height + 4));
  length("history-header-height", Math.max(38, height + 14));
  length("history-tab-height", Math.max(24, height + 4));
  length("history-filter-height", Math.max(36, height + 14));
  length("history-input-height", Math.max(25, height + 2));
  length("history-row-height", Math.max(26, height + 6));
  length("history-file-row-height", Math.max(24, height + 4));
  length("history-toolbar-height", Math.max(39, height + 12));
  length("history-detail-title-height", Math.max(40, height * 2));
  length("history-detail-meta-height", Math.max(20, height));
  const bottomMinimum = Math.max(180, height * 4 + 80);
  length("bottom-min-height", bottomMinimum);
  root.setProperty("--augit-bottom-height", `clamp(${bottomMinimum}px, 31vh, ${Math.max(305, bottomMinimum)}px)`);
  // 只延长轨道的纵向连线，节点半径、横向轨距和笔画不随字号拉伸。
  document.querySelectorAll(".commit-graph-svg").forEach(svg => {
    svg.outerHTML = commitGraphSvg({ width: svg.viewBox.baseVal.width, rows: [JSON.parse(svg.dataset.graphRow)] }, 0, Math.max(26, height + 6));
  });
  measureCurrentFind();
  measureCodeViews();
  measureDocumentToolbar();
  measureCommitPanels();
  measureConflictResolver();
  measureRemoteDialog();
  measureWorktreeDialog();
  measureStashDialog();
  measureStashManagerDialog();
  measureCloneDialog();
  measureResetDialog();
  measureRollbackDialog();
  measurePushDialog();
  measureTerminalHeaders();
  measureSearchOverlay();
  document.body.dataset.typographyPreview = "ready";
}

// 终端标题只测量当前字体和名称；不启动真实 Shell，也不改变正文内容。
function measureTerminalHeaders() {
  for (const tool of document.querySelectorAll('.terminal-tool')) {
    const header = tool.querySelector('.terminal-header'), style = getComputedStyle(header);
    const context = document.createElement('canvas').getContext('2d');
    let height = 0;
    for (const role of ['normal', '600', 'italic']) {
      context.font = `${role} ${style.fontSize} ${style.fontFamily}`;
      const metrics = context.measureText('国Ag');
      height = Math.max(height, Math.ceil(metrics.fontBoundingBoxAscent + metrics.fontBoundingBoxDescent));
    }
    context.font = `600 ${style.fontSize} ${style.fontFamily}`;
    const titleWidth = Math.max(54, Math.ceil(context.measureText('终端').width) + 8);
    context.font = `normal ${style.fontSize} ${style.fontFamily}`;
    const label = tool.querySelector('.terminal-session');
    label.title = label.textContent;
    const sessionWidth = Math.min(Math.max(124, Math.ceil(context.measureText(label.textContent).width) + 16),
      Math.max(0, header.clientWidth - titleWidth - 12 - 96));
    tool.style.setProperty('--terminal-header-height', `${Math.max(38, height + 12)}px`);
    tool.style.setProperty('--terminal-text-height', `${Math.max(24, height + 4)}px`);
    tool.style.setProperty('--terminal-title-width', `${titleWidth}px`);
    tool.style.setProperty('--terminal-session-width', `${sessionWidth}px`);
  }
}
window.addEventListener('resize', measureTerminalHeaders);

// 搜索浮层与原生采用相同分组和字体度量；此处只演示布局与开关，不执行仓库查询。
function measureSearchOverlay() {
  const overlay = document.querySelector('.search-overlay');
  if (!overlay) return;
  const style = getComputedStyle(overlay), context = document.createElement('canvas').getContext('2d');
  let line = 0;
  for (const weight of ['normal', '600', 'italic']) {
    context.font = `${weight} ${style.fontSize} ${style.fontFamily}`;
    const metrics = context.measureText('国Ag');
    line = Math.max(line, Math.ceil(metrics.fontBoundingBoxAscent + metrics.fontBoundingBoxDescent));
  }
  context.font = `normal ${style.fontSize} ${style.fontFamily}`;
  const content = overlay.clientWidth - 20, header = Math.max(41, line + 20), button = Math.max(28, line + 8);
  const title = overlay.querySelector('.search-tabs strong'), shortcut = overlay.querySelector('.menu-shortcut');
  const titleWidth = context.measureText(title.textContent).width;
  let fieldTop = header;
  title.style.cssText = `left:10px;top:${(header - line) / 2}px;line-height:${line}px`;
  const options = overlay.querySelectorAll('.search-option');
  if (options.length) {
    const ignored = overlay.querySelector('.search-ignored'), ignoredWidth = Math.min(content, Math.ceil(context.measureText('包含忽略文件').width) + 22);
    const optionsWidth = 92 + 8 + ignoredWidth;
    let left = overlay.clientWidth - 10 - optionsWidth, top = (header - button) / 2;
    let ignoredLeft = left + 100, ignoredTop = top;
    if (titleWidth + 8 + optionsWidth > content) {
      left = 10; top = header; ignoredLeft = 110; ignoredTop = top;
      if (optionsWidth > content) { ignoredLeft = 10; ignoredTop += button + 8; }
      fieldTop = ignoredTop + button + 8;
    }
    options.forEach((option, index) => { option.style.cssText = `left:${left + index * 32}px;top:${top}px;width:28px;height:${button}px`; });
    ignored.style.cssText = `left:${ignoredLeft}px;top:${ignoredTop}px;width:${ignoredWidth}px;height:${button}px`;
  } else if (shortcut) {
    const below = titleWidth + 8 + context.measureText(shortcut.textContent).width > content;
    shortcut.style.cssText = `right:10px;top:${below ? header : (header - line) / 2}px;line-height:${line}px`;
    if (below) fieldTop += line + 4;
  }
  overlay.style.setProperty('--search-header', `${fieldTop}px`);
  overlay.style.setProperty('--search-field', `${Math.max(31, line + 8)}px`);
  overlay.style.setProperty('--search-row', `${Math.max(32, line + 8)}px`);
  overlay.style.setProperty('--search-line', `${line}px`);
  overlay.dataset.layoutReady = 'true';
}
window.addEventListener('resize', measureSearchOverlay);
document.addEventListener('click', event => {
  const option = event.target.closest('.search-option');
  if (option) option.setAttribute('aria-pressed', String(option.getAttribute('aria-pressed') !== 'true'));
});

// Clone 的字体适配只改变表单空间；固定动作栏不随失败或取消提示移动。
// Push 只模拟现有对话框状态，不执行 Git；所有延迟和监听在关闭时收尾。
// 外壳注入真实待推送信息时使用；结构、图标与样式与样例版一致。
function livePushDialogBody() {
  const mark = '<svg viewBox="0 0 16 16" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 8l4 4 7-8"/></svg>';
  const push = (window.__augitLive && window.__augitLive.push) || null;
  if (!push || !push.upstream) {
    return `<div class="management-content"><div class="management-list"><div class="tree-row push-summary selected"><strong>${escapeHtml(push ? push.branch : "HEAD")}</strong><a class="file-status-modified" href="remote.html">定义远端</a></div><div class="push-commits" role="listbox" aria-label="待推送提交" tabindex="0"></div></div><div class="management-detail" tabindex="0" aria-label="推送详情，可滚动阅读"><h2>无法生成推送预览</h2><p class="commit-meta">${escapeHtml(push && push.reason ? push.reason : "请先定义远端，然后重新打开推送。")}</p><div class="push-notice" role="status" hidden></div></div></div><div class="check-line push-tags"><input type="checkbox" disabled aria-label="推送标签">推送标签 <select class="select-field" disabled aria-label="标签范围"><option>全部</option></select></div>`;
  }

  const commits = push.commits || [];
  // ready=false 表示上游未配置或读取失败：此时不能断言「没有提交」，
  // 只能说数量未知（规格 §7.12 要求此时禁用推送并保留「定义远端」）。
  const unknown = push.ready === false;
  const rows = commits.length === 0
    ? `<p class="commit-meta" style="padding:6px">${unknown ? "领先提交超出已加载的历史范围" : "没有待推送的提交"}</p>`
    : commits.map((commit, index) => `<div class="push-commit" role="option" aria-selected="false" id="push-commit-${index}" data-push-hash="${escapeHtml(commit.hash)}">${mark}<span>${escapeHtml(commit.subject)}</span></div>`).join("");
  const detail = unknown
    ? `<h2>提交数量未知</h2><p class="push-target">目标：${escapeHtml(push.upstream)}</p><p class="commit-meta">${escapeHtml(push.reason || "领先提交数量未知。")}</p>`
    : `<h2>${commits.length} 个提交</h2><p class="push-target">目标：${escapeHtml(push.upstream)}</p><p class="commit-meta push-credentials">凭据由本机 Git 环境处理，Augit 不保存凭据。</p>`;
  return `<div class="management-content"><div class="management-list"><div class="tree-row push-summary selected"><strong>${escapeHtml(push.branch)} → ${escapeHtml(push.upstream)}</strong></div><div class="push-commits" role="listbox" aria-label="待推送提交" tabindex="0">${rows}</div></div><div class="management-detail" tabindex="0" aria-label="推送详情，可滚动阅读">${detail}<div class="push-notice" role="status" hidden></div></div></div>`;
}

function pushDialogBody(noRemote) {
  const mark = '<svg viewBox="0 0 16 16" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 8l4 4 7-8"/></svg>';
  return `<div class="management-content"><div class="management-list"><div class="tree-row push-summary selected"><strong>main →${noRemote ? '' : ' origin/main'}</strong>${noRemote ? '<a class="file-status-modified" href="remote.html">定义远端</a>' : ''}</div><div class="push-commits" role="listbox" aria-label="待推送提交" tabindex="0">${noRemote ? '' : ['fix: 精确恢复系统 PATH', 'fix: 避免强制更新 .NET 10'].map((text, index) => `<div class="push-commit" role="option" aria-selected="false" id="push-commit-${index}">${mark}<span>${text}</span></div>`).join('')}</div></div><div class="management-detail" tabindex="0" aria-label="推送详情，可滚动阅读">${noRemote ? '<div class="push-empty">没有选中的提交</div>' : '<h2>2 个提交</h2><p class="push-target">目标：origin/main</p><p class="commit-meta push-credentials">凭据由本机 Git 环境处理，Augit 不保存凭据。</p>'}<div class="push-notice" role="status" hidden></div></div></div>${noRemote ? '<div class="check-line push-tags"><input type="checkbox" disabled aria-label="推送标签">推送标签 <select class="select-field" disabled aria-label="标签范围"><option>全部</option></select></div>' : ''}`;
}

function measurePushDialog() {
  const dialog = document.querySelector('.push-dialog');
  if (!dialog) return;
  const canvas = document.createElement('canvas').getContext('2d'), style = getComputedStyle(dialog);
  let line = 0;
  for (const weight of ['normal', '600']) {
    canvas.font = `${weight} ${style.fontSize} ${style.fontFamily}`;
    const metrics = canvas.measureText('国Ag');
    line = Math.max(line, Math.ceil(metrics.fontBoundingBoxAscent + metrics.fontBoundingBoxDescent));
  }
  canvas.font = `${style.fontSize} ${style.fontFamily}`;
  const text = value => Math.ceil(canvas.measureText(value).width);
  const header = Math.max(45, line + 18), footer = Math.max(53, line + 25), row = Math.max(27, line + 8), button = Math.max(28, line + 8);
  const tags = dialog.classList.contains('push-no-remote') ? Math.max(30, row + 3) : 0;
  const width = Math.min(930, innerWidth - 80), define = Math.max(72, text('定义远端') + 16);
  const sidebar = tags ? Math.min(Math.max(260, 16 + text('main →') + 8 + define + 8), Math.max(260, (width - 34) / 2)) : 260;
  const values = { header, footer, row, button, tags, sidebar, height: header + 30 + 365 + footer + 1 + tags,
    cancel: Math.max(78, text('取消操作') + 24), push: Math.max(84, text('正在推送…') + 24) };
  for (const [name, value] of Object.entries(values)) dialog.style.setProperty(`--push-${name}`, `${value}px`);
}

function bindPushDialog() {
  const dialog = document.querySelector('.push-dialog');
  if (!dialog) return;
  dialog.querySelector('.footer-help')?.remove(); dialog.setAttribute('aria-modal', 'true');
  const list = dialog.querySelector('.push-commits'), rows = [...list.children], detail = dialog.querySelector('.management-detail');
  const push = dialog.querySelector('.primary-button'), cancel = dialog.querySelector('.secondary-button'), close = dialog.querySelector('.dialog-header a');
  const define = dialog.querySelector('.push-summary a'), notice = dialog.querySelector('.push-notice');
  close.innerHTML = '<svg viewBox="0 0 16 16" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" aria-hidden="true"><path d="M4.5 4.5l7 7m0-7-7 7"/></svg>';
  const query = new URLSearchParams(location.search), results = (query.get('push-result') || 'success').split(',');
  const controller = new AbortController(), options = { signal: controller.signal }, scrim = document.querySelector('.scrim');
  const background = [...dialog.parentElement.children].filter(node => node !== dialog && node !== scrim);
  const previousInert = background.map(node => node.inert), previousFocus = document.activeElement;
  background.forEach(node => node.inert = true);
  let running = false, cancelling = false, timer = null, attempt = 0, selected = -1;
  const showNotice = (text, error = false) => {
    notice.textContent = notice.title = text; notice.hidden = !text; notice.classList.toggle('error', error); detail.scrollTop = 0;
  };
  const select = index => {
    selected = index;
    rows.forEach((row, candidate) => { row.classList.toggle('selected', candidate === index); row.setAttribute('aria-selected', String(candidate === index)); });
    list.setAttribute('aria-activedescendant', rows[index]?.id || '');
  };
  const update = () => {
    push.disabled = running || rows.length === 0 || dialog.dataset.state === 'empty';
    list.setAttribute('aria-disabled', String(running)); close.setAttribute('aria-disabled', String(running));
  };
  const cleanup = () => {
    clearTimeout(timer); timer = null; controller.abort();
    background.forEach((node, index) => node.inert = previousInert[index]);
  };
  const dismiss = () => {
    cleanup(); dialog.remove(); scrim?.remove();
    if (previousFocus?.isConnected) previousFocus.focus({ preventScroll: true });
  };
  const finish = result => {
    if (!dialog.isConnected) return;
    clearTimeout(timer); timer = null;
    if (cancelling) result = 'cancelled';
    running = false; cancelling = false; dialog.dataset.state = result;
    if (result === 'success') { dismiss(); return; }
    update(); cancel.textContent = '取消'; push.textContent = '推送';
    const error = '远端拒绝推送，请先更新分支。';
    showNotice(result === 'cancelled' ? '操作已取消。' : query.has('long-error') ? Array(18).fill(error).join('\n') : error, result !== 'cancelled');
  };
  const start = () => {
    if (push.disabled || running) return;
    running = true; dialog.dataset.state = 'running'; update();
    cancel.textContent = '取消操作'; push.textContent = '正在推送…'; cancel.focus(); showNotice('正在推送…');
    const result = results[Math.min(attempt++, results.length - 1)];
    if (result !== 'pending') timer = setTimeout(() => finish(result), 180);
  };
  const cancelOrClose = () => {
    if (!running) { dismiss(); return; }
    if (cancelling) return;
    cancelling = true; showNotice('正在取消推送，等待 Git 停止…');
    clearTimeout(timer); timer = setTimeout(() => finish('cancelled'), 180);
  };
  push.addEventListener('click', start, options); cancel.addEventListener('click', cancelOrClose, options);
  close.addEventListener('click', event => { event.preventDefault(); if (!running) dismiss(); }, options);
  list.addEventListener('click', event => {
    const row = event.target.closest('.push-commit');
    if (row && !running) { list.focus(); select(rows.indexOf(row)); }
  }, options);
  dialog.addEventListener('keydown', event => {
    if (event.key === 'Escape') { event.preventDefault(); cancelOrClose(); }
    if (event.key === 'Enter') {
      if (event.target === define) return;
      event.preventDefault(); if (event.target === cancel || event.target === close) cancelOrClose(); else start();
    }
    if (event.target === list && !running && ['ArrowDown', 'ArrowUp'].includes(event.key)) {
      event.preventDefault(); select(Math.max(0, Math.min(rows.length - 1, selected + (event.key === 'ArrowDown' ? 1 : -1))));
      rows[selected]?.scrollIntoView({ block: 'nearest' });
    }
    if (event.key === 'Tab') {
      event.preventDefault();
      const order = [list, detail, define, cancel, push, close].filter(node => node && !node.disabled && node.getAttribute('aria-disabled') !== 'true');
      const index = order.indexOf(document.activeElement);
      order[(index + (event.shiftKey ? -1 : 1) + order.length) % order.length].focus();
    }
  }, options);
  const initial = query.get('push-state');
  if (initial === 'error' || initial === 'cancelled') finish(initial);
  if (initial === 'empty' && !define) {
    dialog.dataset.state = 'empty'; rows.forEach(row => row.hidden = true);
    dialog.querySelector('h2').textContent = '0 个提交'; showNotice('当前引用没有待推送提交。');
  }
  update(); list.focus({ preventScroll: true });
  window.addEventListener('resize', measurePushDialog, options);
  window.addEventListener('pagehide', cleanup, { ...options, once: true });
}

function measureCloneDialog() {
  const dialog = document.querySelector(".clone-dialog");
  if (!dialog) return;
  const context = document.createElement("canvas").getContext("2d"), style = getComputedStyle(dialog);
  let line = 0;
  for (const weight of ["normal", "600"]) {
    context.font = `${weight} ${style.fontSize} ${style.fontFamily}`;
    const metrics = context.measureText("国Ag");
    line = Math.max(line, Math.ceil(metrics.fontBoundingBoxAscent + metrics.fontBoundingBoxDescent));
  }
  context.font = `${style.fontSize} ${style.fontFamily}`;
  const text = value => Math.ceil(context.measureText(value).width);
  const header = Math.max(45, line + 16), field = Math.max(30, line + 10), footer = field + 23;
  const label = Math.max(110, ...["版本控制", "仓库 URL", "目录"].map(text));
  const cancel = Math.max(53, text("取消操作") + 24), create = Math.max(52, text("正在克隆…") + 24);
  const shallow = Math.max(180, text("浅克隆，历史截断为") + 23), depth = Math.max(58, text("1000") + 16), suffix = Math.max(80, text("个提交"));
  const width = Math.min(Math.max(930, label + 44 + shallow), innerWidth - 90);
  const wrapped = shallow + depth + suffix + 20 > width - 46 - label;
  const height = header + 191 + 4 * (field - 30) + (wrapped ? field + 8 : 0) + footer;
  for (const [name, value] of Object.entries({ header, field, footer, label, cancel, create, shallow, depth, suffix, width, height }))
    dialog.style.setProperty(`--clone-${name}`, `${value}px`);
}

function bindCloneDialog() {
  const dialog = document.querySelector(".clone-dialog");
  if (!dialog) return;
  dialog.querySelector(".footer-help")?.remove();
  dialog.setAttribute("aria-modal", "true");
  const versionControl = dialog.querySelector("#clone-version"), source = dialog.querySelector("#clone-source"), destination = dialog.querySelector("#clone-destination");
  const shallow = dialog.querySelector("#clone-shallow"), depth = dialog.querySelector("#clone-depth");
  const create = dialog.querySelector(".primary-button"), cancel = dialog.querySelector(".secondary-button"), close = dialog.querySelector(".dialog-header a");
  const notice = dialog.querySelector(".clone-notice"), body = dialog.querySelector(".dialog-body");
  close.innerHTML = '<svg viewBox="0 0 16 16" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" aria-hidden="true"><path d="M4.5 4.5l7 7m0-7-7 7"/></svg>';
  const fields = [versionControl, source, destination, shallow, depth], order = [...fields, cancel, create, close];
  const controller = new AbortController(), options = { signal: controller.signal };
  const background = document.querySelector(".editor-content"), scrim = document.querySelector(".scrim");
  const query = new URLSearchParams(location.search), results = (query.get("clone-result") || "success").split(",");
  let running = false, cancelling = false, composing = false, timer = null, request = 0, attempt = 0;
  // 只约束当前视觉稿背景；关闭后恢复原值，不留下全局键盘监听或模拟计时器。
  const backgroundNodes = [...dialog.parentElement.children].filter(node => node !== dialog && node !== scrim);
  const inertStates = backgroundNodes.map(node => node.inert);
  backgroundNodes.forEach(node => node.inert = true);
  const ensureVisible = node => {
    const rect = node.getBoundingClientRect(), viewport = body.getBoundingClientRect();
    if (rect.top < viewport.top || rect.height >= body.clientHeight) body.scrollTop += rect.top - viewport.top;
    else if (rect.bottom > viewport.bottom) body.scrollTop += rect.bottom - viewport.bottom;
  };
  const updateFields = () => {
    fields.forEach(field => field.disabled = running);
    depth.disabled = running || !shallow.checked;
    dialog.querySelector(".clone-depth-group").classList.toggle("disabled", depth.disabled);
    create.disabled = running; close.setAttribute("aria-disabled", String(running));
  };
  const showNotice = text => {
    notice.textContent = notice.title = text; notice.hidden = !text;
    if (text) ensureVisible(notice);
  };
  const cleanup = () => {
    request++; clearTimeout(timer); timer = null; controller.abort();
    backgroundNodes.forEach((node, index) => node.inert = inertStates[index]);
  };
  const dismiss = () => {
    cleanup(); dialog.remove(); scrim?.remove();
    if (background) { background.tabIndex = -1; background.focus({ preventScroll: true }); }
  };
  const finish = (result, expected) => {
    if (!dialog.isConnected || request !== expected) return;
    clearTimeout(timer); timer = null;
    if (cancelling) result = "cancelled";
    running = false; cancelling = false;
    if (result === "success") { dismiss(); return; }
    updateFields(); cancel.textContent = "取消"; create.textContent = "克隆";
    const error = "服务器暂时不可达，请检查仓库地址或网络后重试。";
    showNotice(result === "cancelled" ? "操作已取消。" : query.has("long-error") ? Array(12).fill(error).join("\n") : error);
    dialog.dataset.state = result;
  };
  const cancelOrClose = () => {
    if (!running) { dismiss(); return; }
    if (cancelling) return;
    cancelling = true; dialog.dataset.state = "cancelling";
    clearTimeout(timer);
    // 模拟 Git 确认结束前继续冻结表单，不能把请求取消立即当成已取消。
    timer = setTimeout(() => finish("cancelled", request), 180);
  };
  const start = () => {
    if (running) return;
    if (!source.value.trim() || !destination.value.trim()) {
      showNotice("请输入仓库地址和目标目录。");
      (!source.value.trim() ? source : destination).focus(); return;
    }
    if (shallow.checked && (!/^[0-9]+$/.test(depth.value.trim()) || Number(depth.value.trim()) < 1 || Number(depth.value.trim()) > 2147483647)) {
      showNotice("浅克隆深度必须是正整数。"); depth.focus(); return;
    }
    running = true; cancelling = false; const expected = ++request;
    updateFields(); create.textContent = "正在克隆…"; cancel.textContent = "取消操作"; cancel.focus();
    showNotice("正在克隆…"); dialog.dataset.state = "running";
    // 外壳注入真实克隆函数时执行真实 Git；否则保留视觉稿的模拟结果。
    if (typeof window.__augitCloneRequest === "function") {
      const depthValue = shallow.checked ? Number(depth.value.trim()) : null;
      window.__augitCloneRequest({
        source: source.value.trim(),
        destination: destination.value.trim(),
        depth: depthValue,
      }).then(result => {
        if (request !== expected) return;
        if (result && result.available) { finish("success", expected); return; }
        running = false; dialog.dataset.state = "idle"; updateFields();
        create.textContent = "克隆"; cancel.textContent = "取消";
        showNotice(result && result.reason ? result.reason : "克隆失败。");
        const field = result && result.field;
        (field === "source" ? source : field === "depth" ? depth : destination).focus();
      }).catch(error => {
        if (request !== expected) return;
        running = false; dialog.dataset.state = "idle"; updateFields();
        showNotice("克隆失败：" + String(error && error.message || error));
      });
      return;
    }

    const result = results[Math.min(attempt++, results.length - 1)];
    if (result !== "pending") timer = setTimeout(() => finish(result, expected), 180);
  };
  create.addEventListener("click", start, options);
  cancel.addEventListener("click", cancelOrClose, options);
  close.addEventListener("click", event => { event.preventDefault(); if (!running) dismiss(); }, options);
  shallow.addEventListener("change", updateFields, options);
  dialog.addEventListener("compositionstart", () => composing = true, options);
  dialog.addEventListener("compositionend", () => composing = false, options);
  body.addEventListener("focusin", event => ensureVisible(event.target), options);
  dialog.addEventListener("keydown", event => {
    if (composing || event.isComposing) return;
    if (event.key === "Escape") { event.preventDefault(); cancelOrClose(); }
    if (event.key === "Enter" && event.target !== versionControl) {
      event.preventDefault();
      if (event.target === cancel || event.target === close) cancelOrClose(); else start();
    }
    if (event.key !== "Tab") return;
    event.preventDefault();
    const available = order.filter(node => !node.disabled && node.getAttribute("aria-disabled") !== "true");
    const index = available.indexOf(document.activeElement);
    available[(index + (event.shiftKey ? -1 : 1) + available.length) % available.length].focus();
  }, options);
  window.addEventListener("resize", measureCloneDialog, options);
  window.addEventListener("pagehide", cleanup, { ...options, once: true });
  updateFields(); source.focus({ preventScroll: true });
}

// Stash 只有正文可以滚动；按钮预留进行态文字宽度，状态更新不改变表单几何。
function measureStashDialog() {
  const dialog = document.querySelector(".stash-dialog");
  if (!dialog) return;
  const context = document.createElement("canvas").getContext("2d"), style = getComputedStyle(dialog);
  let line = 0;
  for (const weight of ["normal", "600"]) {
    context.font = `${weight} ${style.fontSize} ${style.fontFamily}`;
    const m = context.measureText("国Ag");
    line = Math.max(line, Math.ceil(m.fontBoundingBoxAscent + m.fontBoundingBoxDescent));
  }
  context.font = `${style.fontSize} ${style.fontFamily}`;
  const text = value => Math.ceil(context.measureText(value).width);
  const header = Math.max(45, line + 16), field = Math.max(30, line + 10), branch = Math.max(24, line);
  const button = Math.max(30, line + 10), message = Math.max(78, line * 3 + 12), footer = Math.max(53, button + 23);
  const label = Math.max(110, ...["Git 根目录", "当前分支", "消息"].map(text));
  const cancel = Math.max(54, text("取消操作") + 24), create = Math.max(90, text("正在创建 Stash…") + 24);
  const body = 16 + field + 13 + branch + 5 + message + 13 + button + 16;
  const width = Math.max(620, cancel + create + 44, label + 69 + text("保留索引状态"));
  for (const [name, value] of Object.entries({ header, field, branch, button, message, footer, label, cancel, create, body, width,
    height: header + body + footer })) dialog.style.setProperty(`--stash-${name}`, `${value}px`);
}
window.addEventListener("resize", measureStashDialog);

// Stash 管理的标题、工具栏和底栏固定，右侧详情在正文视口内独立滚动。
function measureStashManagerDialog() {
  const dialog = document.querySelector('.stash-manager-dialog');
  if (!dialog) return;
  const context = document.createElement('canvas').getContext('2d'), style = getComputedStyle(dialog);
  context.font = `600 ${style.fontSize} ${style.fontFamily}`;
  const m = context.measureText('国Ag');
  const line = Math.ceil(m.fontBoundingBoxAscent + m.fontBoundingBoxDescent);
  const header = Math.max(45, line + 18), toolbar = Math.max(61, line + 28), button = Math.max(28, line + 8), footer = Math.max(53, button + 25);
  const height = Math.min(header + toolbar + 220 + footer, innerHeight - 88);
  for (const [name, value] of Object.entries({ header, toolbar, button, footer, height })) dialog.style.setProperty(`--stash-manager-${name}`, `${value}px`);
  dialog.querySelector('.footer-help')?.remove();
}
window.addEventListener("resize", measureStashManagerDialog);

function bindStashDialog() {
  const dialog = document.querySelector(".stash-dialog");
  if (!dialog) return;
  dialog.querySelector(".footer-help")?.remove();
  dialog.setAttribute("aria-modal", "true");
  const root = dialog.querySelector("#stash-root"), message = dialog.querySelector("#stash-message"), keep = dialog.querySelector("#stash-keep");
  const create = dialog.querySelector(".primary-button"), cancel = dialog.querySelector(".secondary-button"), close = dialog.querySelector(".dialog-header a");
  const notice = dialog.querySelector(".stash-notice"), body = dialog.querySelector(".dialog-body");
  close.innerHTML = '<svg viewBox="0 0 16 16" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" aria-hidden="true"><path d="M4.5 4.5l7 7m0-7-7 7"/></svg>';
  const fields = [root, message, keep], order = [...fields, cancel, create, close];
  const background = document.querySelector(".editor-content");
  let running = false, timer = null, version = 0, composing = false;
  const showNotice = text => {
    notice.textContent = notice.title = text; notice.hidden = !text;
    if (text) body.scrollTop = Math.max(0, notice.offsetTop - body.offsetTop);
  };
  const finish = (result, request) => {
    if (!dialog.isConnected || version !== request) return;
    clearTimeout(timer); timer = null; running = false;
    if (result === "success") { dismiss(); return; }
    fields.forEach(field => field.disabled = false); create.disabled = false; close.removeAttribute("aria-disabled");
    cancel.textContent = "取消"; create.textContent = "创建 Stash";
    const failure = "Git 锁文件被占用，请稍后重试。";
    showNotice(result === "cancelled" ? "操作已取消。" : new URLSearchParams(location.search).has("long-error") ? Array(12).fill(failure).join("\n") : failure);
    dialog.dataset.state = result;
  };
  const dismiss = () => {
    if (running) { version++; finish("cancelled", version); return; }
    version++; clearTimeout(timer); timer = null;
    dialog.remove(); document.querySelector(".scrim")?.remove();
    if (background) { background.tabIndex = -1; background.focus({ preventScroll: true }); }
  };
  create.addEventListener("click", () => {
    if (running) return;
    running = true; const request = ++version;
    fields.forEach(field => field.disabled = true); create.disabled = true; close.setAttribute("aria-disabled", "true");
    create.textContent = "正在创建 Stash…"; cancel.textContent = "取消操作"; cancel.focus();
    showNotice("正在创建 Stash…"); dialog.dataset.state = "running";
    const result = new URLSearchParams(location.search).get("stash-result") || "success";
    if (result !== "pending") timer = setTimeout(() => finish(result, request), 180);
  });
  cancel.addEventListener("click", dismiss);
  close.addEventListener("click", event => { event.preventDefault(); if (!running) dismiss(); });
  message.addEventListener("compositionstart", () => composing = true);
  message.addEventListener("compositionend", () => composing = false);
  dialog.addEventListener("keydown", event => {
    if (composing || event.isComposing) return;
    if (event.key === "Escape") { event.preventDefault(); dismiss(); }
    if (event.key !== "Tab") return;
    event.preventDefault();
    const available = order.filter(node => !node.disabled && node.getAttribute("aria-disabled") !== "true");
    const index = available.indexOf(document.activeElement), next = (index + (event.shiftKey ? -1 : 1) + available.length) % available.length;
    available[next].focus();
  });
  root.focus({ preventScroll: true });
}

// Reset 的文字度量只改变窗口与正文空间；进行态、失败和取消不移动底栏。
function measureResetDialog() {
  const dialog = document.querySelector('.reset-dialog');
  if (!dialog) return;
  const context = document.createElement('canvas').getContext('2d'), style = getComputedStyle(dialog);
  let line = 0;
  for (const weight of ['normal', '600']) {
    context.font = `${weight} ${style.fontSize} ${style.fontFamily}`;
    const metrics = context.measureText('国Ag');
    line = Math.max(line, Math.ceil(metrics.fontBoundingBoxAscent + metrics.fontBoundingBoxDescent));
  }
  context.font = `${style.fontSize} ${style.fontFamily}`;
  const text = value => Math.ceil(context.measureText(value).width);
  const header = Math.max(45, line + 16), button = Math.max(30, line + 10), footer = 53 + button - 30;
  const label = Math.max(110, text('目标提交'), text('模式'));
  const cancel = Math.max(78, text('取消操作') + 26);
  const run = Math.max(116, ...['确认 Reset Hard', '执行 Reset', '正在执行 Reset…'].map(value => text(value) + 26));
  const mode = Math.max(...[...dialog.querySelector('select').options].map(option => text(option.textContent)));
  const width = Math.max(620, label + mode + 110, cancel + run + 42);
  for (const [name, value] of Object.entries({ header, button, footer, label, cancel, run, width, line }))
    dialog.style.setProperty(`--reset-${name}`, `${value}px`);
  const form = dialog.querySelector('.form-grid'), impact = dialog.querySelector('.reset-impact');
  const height = Math.max(288, header + 15 + form.offsetHeight + 16 + impact.offsetHeight + 17 + footer + 1);
  dialog.style.setProperty('--reset-height', `${height}px`);
}

// 回滚使用原比较视图；这里只度量确认窗口，不执行真实回滚。
function measureRollbackDialog() {
  const dialog = document.querySelector('.rollback-dialog');
  if (!dialog) return;
  const context = document.createElement('canvas').getContext('2d'), style = getComputedStyle(dialog);
  let line = 0;
  for (const weight of ['normal', '600']) {
    context.font = `${weight} ${style.fontSize} ${style.fontFamily}`;
    const metrics = context.measureText('国Ag');
    line = Math.max(line, Math.ceil(metrics.fontBoundingBoxAscent + metrics.fontBoundingBoxDescent));
  }
  context.font = `${style.fontSize} ${style.fontFamily}`;
  const text = value => Math.ceil(context.measureText(value).width);
  const header = Math.max(45, line + 16), button = Math.max(30, line + 10), footer = button + 23;
  const cancel = Math.max(78, text('取消') + 26), run = Math.max(116, text('回滚完整文件') + 26);
  const width = Math.max(930, cancel + run + 42);
  const comparison = Math.max(214, Math.max(39, line + 12) + Math.max(55, 2 * Math.max(24, line + 4) + 7) + 96);
  for (const [name, value] of Object.entries({ header, button, footer, cancel, run, width, line, comparison }))
    dialog.style.setProperty(`--rollback-${name}`, `${value}px`);
  dialog.style.setProperty('--rollback-height', `${Math.max(430, header + 15 + dialog.querySelector('.rollback-impact').offsetHeight + 14 + comparison + 8 + footer + 1)}px`);
}

function bindRollbackDialog() {
  const dialog = document.querySelector('.rollback-dialog');
  if (!dialog) return;
  dialog.querySelector('.footer-help')?.remove();
  dialog.querySelector('.dialog-header > span').title = dialog.querySelector('.dialog-header > span').textContent;
  dialog.setAttribute('aria-modal', 'true');
  const controller = new AbortController(), options = { signal: controller.signal };
  if (new URLSearchParams(location.search).has('recycle')) dialog.querySelector('.rollback-recycle').hidden = false;
  dialog.querySelector('.dialog-body').addEventListener('focusin', event => event.target.scrollIntoView({ block: 'nearest' }), options);
  dialog.addEventListener('keydown', event => {
    if (event.key !== 'Tab') return;
    const targets = [...dialog.querySelectorAll('.dialog-body button, .dialog-body [tabindex="0"], .dialog-footer a, .dialog-header a')]
      .filter(node => !node.disabled && node.getClientRects().length && node.getAttribute('aria-disabled') !== 'true');
    const index = targets.indexOf(document.activeElement), direction = event.shiftKey ? -1 : 1;
    event.preventDefault(); targets[(index + direction + targets.length) % targets.length]?.focus();
  }, options);
  window.addEventListener('resize', measureRollbackDialog, options);
  window.addEventListener('pagehide', () => controller.abort(), { ...options, once: true });
}

function bindResetDialog() {
  const dialog = document.querySelector('.reset-dialog');
  if (!dialog) return;
  dialog.querySelector('.footer-help')?.remove();
  dialog.setAttribute('aria-modal', 'true');
  const target = dialog.querySelector('#reset-target'), mode = dialog.querySelector('#reset-mode');
  const run = dialog.querySelector('.reset-run'), cancel = dialog.querySelector('.secondary-button');
  const close = dialog.querySelector('.dialog-header a'), body = dialog.querySelector('.dialog-body');
  const impact = dialog.querySelector('.reset-impact'), notice = dialog.querySelector('.reset-notice');
  const controller = new AbortController(), options = { signal: controller.signal };
  const params = new URLSearchParams(location.search);
  let running = false, composing = false, timer;
  const showNotice = value => {
    notice.hidden = !value; notice.textContent = value;
    if (value) notice.scrollIntoView({ block: 'nearest' });
  };
  const update = () => {
    const presentations = [
      ['仅移动 HEAD，索引和工作区保持不变', '已暂存和未暂存的本地改动都会保留。'],
      ['移动 HEAD 并重置索引，工作区保持不变', '已暂存改动会回到工作区，本地文件不会删除。'],
      ['将丢失 35 个已跟踪文件的本地改动', '未跟踪文件不会删除，操作不可由 Augit 自动撤销。'],
    ];
    impact.querySelector('strong').textContent = presentations[mode.selectedIndex][0];
    impact.querySelector('p').textContent = presentations[mode.selectedIndex][1];
    impact.classList.toggle('danger', mode.selectedIndex === 2);
    run.classList.toggle('danger-button', mode.selectedIndex === 2);
    run.classList.toggle('primary-button', mode.selectedIndex !== 2);
    run.textContent = running ? '正在执行 Reset…' : mode.selectedIndex === 2 ? '确认 Reset Hard' : '执行 Reset';
    cancel.textContent = running ? '取消操作' : '取消';
    for (const node of [target, mode, run]) node.disabled = running;
    run.setAttribute('aria-disabled', String(running)); close.setAttribute('aria-disabled', String(running));
  };
  const dismiss = () => {
    clearTimeout(timer); controller.abort(); dialog.remove(); document.querySelector('.scrim')?.remove();
  };
  const cancelAction = () => {
    if (!running) { dismiss(); return; }
    clearTimeout(timer); running = false; update(); showNotice('操作已取消。'); dialog.dataset.state = 'cancelled';
  };
  const start = () => {
    if (running) return;
    if (!target.value.trim()) { showNotice('请输入 Reset 目标提交。'); target.focus(); return; }
    running = true; update(); cancel.focus(); showNotice('正在执行 Reset…'); dialog.dataset.state = 'running';
    if (params.get('reset-result') === 'pending') return;
    timer = setTimeout(() => {
      running = false; update();
      if (params.get('reset-result') === 'failure') {
        const reason = '无法解析目标提交，请检查仓库中的引用名称。';
        showNotice(params.has('long-error') ? Array(12).fill(reason).join('\n') : reason);
        dialog.dataset.state = 'failure';
      } else dismiss();
    }, 160);
  };
  target.addEventListener('compositionstart', () => { composing = true; }, options);
  target.addEventListener('compositionend', () => { composing = false; }, options);
  mode.addEventListener('change', update, options);
  run.addEventListener('click', start, options); cancel.addEventListener('click', cancelAction, options);
  close.addEventListener('click', event => { event.preventDefault(); if (!running) dismiss(); }, options);
  body.addEventListener('focusin', event => event.target.scrollIntoView({ block: 'nearest' }), options);
  dialog.addEventListener('keydown', event => {
    if (composing || event.isComposing) return;
    if (event.key === 'Tab') {
      const items = [target, mode, cancel, run, close].filter(node => !node.disabled && node.getAttribute('aria-disabled') !== 'true');
      const at = items.indexOf(document.activeElement), direction = event.shiftKey ? -1 : 1;
      event.preventDefault(); items[(at + direction + items.length) % items.length]?.focus();
    } else if (event.key === 'Escape') { event.preventDefault(); cancelAction(); }
    else if (event.key === 'Enter' && event.target === target) { event.preventDefault(); if (!event.repeat) start(); }
  }, options);
  window.addEventListener('resize', measureResetDialog, options);
  window.addEventListener('pagehide', () => { clearTimeout(timer); controller.abort(); }, { ...options, once: true });
  update(); target.focus({ preventScroll: true });
}

// 远端管理保留双栏，受限高度只滚动详情，字号变化不裁切输入和固定操作栏。
function measureRemoteDialog() {
  const dialog = document.querySelector(".remote-dialog");
  if (!dialog) return;
  const context = document.createElement("canvas").getContext("2d"), style = getComputedStyle(dialog);
  let line = 0;
  for (const weight of ["normal", "600"]) {
    context.font = `${weight} ${style.fontSize} ${style.fontFamily}`;
    const metrics = context.measureText("国Ag");
    line = Math.max(line, Math.ceil(metrics.fontBoundingBoxAscent + metrics.fontBoundingBoxDescent));
  }
  context.font = `${style.fontSize} ${style.fontFamily}`;
  const header = Math.max(45, line + 16), field = Math.max(30, line + 12), button = Math.max(28, line + 12);
  const footer = Math.max(53, button + 25), toolbar = Math.max(61, line + 28), title = Math.max(30, line);
  const values = { header, field, button, footer, toolbar, title,
    label: Math.max(110, ...["名称", "获取 URL", "推送 URL"].map(text => Math.ceil(context.measureText(text).width))),
    height: Math.max(407, header + toolbar + 14 + title + 6 + 3 * field + 28 + 16 + button + 16 + footer) };
  for (const [name, value] of Object.entries(values)) dialog.style.setProperty(`--remote-${name}`, `${value}px`);
  dialog.querySelector(".footer-help")?.remove();
}

// Worktree 沿用远端的双栏骨架，但默认高度保持视觉稿的 365px；右侧详情独立滚动。
function measureWorktreeDialog() {
  const dialog = document.querySelector('.worktree-dialog');
  if (!dialog) return;
  const context = document.createElement('canvas').getContext('2d'), style = getComputedStyle(dialog);
  context.font = `${style.fontSize} ${style.fontFamily}`;
  const sample = context.measureText('国Ag');
  const line = Math.ceil(sample.fontBoundingBoxAscent + sample.fontBoundingBoxDescent);
  const header = Math.max(45, line + 16), footer = Math.max(53, line + 25), toolbar = Math.max(61, line + 28);
  const button = Math.max(31, line + 10);
  const body = 190 + Math.max(0, line - 31) + 3 * Math.max(0, line - 24) + Math.max(0, button - 31);
  const values = { header, footer, toolbar, title: Math.max(30, line), button,
    label: Math.max(110, context.measureText('新分支（可选）').width), height: Math.max(365, header + toolbar + body + footer) };
  for (const [name, value] of Object.entries(values)) dialog.style.setProperty(`--worktree-${name}`, `${value}px`);
}

// 三栏来源和操作行按界面字体度量，正文仍只跟随等宽字号。
function measureConflictResolver() {
  const page = document.querySelector(".conflict-page");
  if (!page) return;
  for (const body of page.querySelectorAll(".conflict-block")) {
    const codeSize = parseFloat(getComputedStyle(body).fontSize);
    body.style.setProperty("--conflict-line-height", `${Math.round(codeSize * 1.7 * devicePixelRatio) / devicePixelRatio}px`);
  }
  const dialog = page.closest(".dialog"), context = document.createElement("canvas").getContext("2d");
  const style = getComputedStyle(page);
  context.font = `${style.fontSize} ${style.fontFamily}`;
  const metrics = context.measureText("国Ag"), line = Math.ceil(metrics.fontBoundingBoxAscent + metrics.fontBoundingBoxDescent);
  const button = Math.max(28, line + 8), header = Math.max(45, line + 18);
  const contentHeader = Math.max(42, button + 14), footer = Math.max(53, button + 25);
  const measure = (text, minimum) => Math.max(minimum, Math.ceil(context.measureText(text).width) + 24);
  const accept = measure("接受两侧", 112), cancel = measure("取消", 80), save = measure("应用并标记已解决", 164);
  const navigation = Math.max(measure("上一处", 78), measure("下一处", 78));
  const count = Math.ceil(context.measureText(page.querySelector(".conflict-header .commit-meta").textContent).width) + 8;
  const title = dialog.querySelector(".conflict-file-title") || page.querySelector(".conflict-header strong");
  title.classList.add("conflict-file-title");
  const fullTitle = title.dataset.fullTitle ??= title.textContent;
  const fileName = fullTitle.slice(fullTitle.indexOf(" · ") + 3);
  context.font = `600 ${style.fontSize} ${style.fontFamily}`;
  const compact = Math.ceil(context.measureText(fullTitle).width) + 8 + count + 2 * (navigation + 8) > dialog.clientWidth - 34;
  dialog.classList.toggle("compact-conflict-header", compact);
  title.textContent = compact ? fileName : fullTitle;
  if (compact) dialog.querySelector(".dialog-header .grow").before(title);
  else page.querySelector(".conflict-header").prepend(title);
  context.font = `${style.fontSize} ${style.fontFamily}`;
  const titles = [...page.querySelectorAll(".conflict-column-title")];
  const split = titles.some(node => context.measureText(node.title).width + 12 > node.getBoundingClientRect().width);
  const column = Math.max(36, line * (split ? 2 : 1) + 12);
  for (const node of titles) {
    node.replaceChildren();
    if (split) for (const part of node.title.split(" · ")) {
      const label = document.createElement("span");
      label.textContent = part;
      label.style.height = `${line}px`;
      node.append(label);
    }
    else node.textContent = node.title;
  }
  const wrap = accept * 3 + cancel + save + 40 > dialog.clientWidth - 34;
  const actions = button * (wrap ? 2 : 1) + (wrap ? 28 : 20);
  dialog.style.height = `${Math.min(243 + header + contentHeader + column + actions + footer, innerHeight - 40)}px`;
  for (const [name, value] of Object.entries({ button, header, 'content-header': contentHeader, column, footer, actions, accept, cancel, save }))
    dialog.style.setProperty(`--conflict-${name}`, `${value}px`);
  page.classList.toggle("wrap-actions", wrap);
  page.querySelectorAll(".conflict-header button").forEach(button => { button.style.width = `${navigation}px`; });
  const notice = dialog.querySelector(".footer-help");
  notice.textContent = notice.title = "未处理冲突块：1，当前位置：1";
  dialog.querySelector(".dialog-footer > a").style.width = `${measure("返回冲突列表", 126)}px`;
}
window.addEventListener("resize", measureConflictResolver);

// 提交区只随尺寸与字体度量布局，反馈和勾选不改变区域分配。
function measureCommitPanels() {
  document.querySelectorAll(".changes-layout").forEach(layout => {
    const box = layout.querySelector(".commit-box");
    if (!box?.querySelector(".commit-amend")) return;
    const side = layout.closest(".side-tool");
    const context = document.createElement("canvas").getContext("2d");
    const style = getComputedStyle(box);
    context.font = `normal ${style.fontSize} ${style.fontFamily}`;
    const metrics = context.measureText("国Ag");
    const h = Math.ceil(metrics.fontBoundingBoxAscent + metrics.fontBoundingBoxDescent);
    const textWidth = text => Math.ceil(context.measureText(text).width);
    const width = side.clientWidth;
    const actionWidth = Math.max(0, width - 20);
    const commitWidth = Math.max(53, textWidth("提交") + 26);
    const pushWidth = Math.max(102, textWidth("提交并推送…") + 26);
    const wrap = commitWidth + pushWidth + 27 + 14 > actionWidth;
    const actionHeight = Math.max(30, h + 8);
    const actionsHeight = actionHeight * (wrap ? 2 : 1) + (wrap ? 7 : 0);
    const optionsHeight = Math.max(22, h + 4);
    const optionsBlock = Math.max(39, optionsHeight + 17);
    const feedback = Math.max(20, h + 4);
    const minimum = optionsBlock + Math.max(80, feedback + h + 12) + 13 + actionsHeight;
    const header = Math.max(39, h + 10), toolbar = Math.max(36, h + 8);
    const available = side.clientHeight - header;
    const commitHeight = Math.max(minimum, Math.min(Math.max(190, Math.round(available * .42)), available - toolbar - 155));
    side.style.gridTemplateRows = `${header}px minmax(0, 1fr)`;
    layout.style.gridTemplateRows = `${toolbar}px minmax(0, 1fr) ${commitHeight}px`;
    layout.style.setProperty("--commit-row-height", `${Math.max(27, h + 6)}px`);
    const empty = layout.querySelector(".empty-tool-state");
    if (empty) empty.classList.toggle("compact", available - toolbar - commitHeight < Math.max(24, h + 4) * 2 + 2);
    box.style.gridTemplateRows = `${optionsBlock}px minmax(0, 1fr) ${actionsHeight + 13}px`;
    box.style.setProperty("--commit-feedback-height", `${feedback}px`);
    const setRect = (selector, x, y, w, height) => Object.assign(box.querySelector(selector).style,
      { left: `${x}px`, top: `${y}px`, width: `${w}px`, height: `${height}px` });
    const amendWidth = Math.min(Math.max(76, textWidth("Amend") + 25), Math.max(0, width - 50));
    const remaining = Math.max(0, width - 16 - amendWidth - 7);
    const countWidth = Math.min(Math.max(86, textWidth("99 modified") + 4), Math.max(0, remaining - 34));
    const lastWidth = Math.max(0, remaining - countWidth - 7);
    setRect(".commit-amend", 0, 5, amendWidth, optionsHeight);
    setRect(".commit-last", amendWidth + 7, 5, lastWidth, optionsHeight);
    setRect(".commit-count", width - 16 - countWidth, 5, countWidth, optionsHeight);
    box.querySelector(".commit-last").classList.toggle("icon-only", lastWidth < textWidth("上一次提交") + 22);
    setRect(".commit-actions > .primary-button", 2, 8, Math.min(commitWidth, actionWidth - 34), actionHeight);
    setRect(".commit-actions > .secondary-button", wrap ? 2 : 2 + commitWidth + 7, wrap ? 8 + actionHeight + 7 : 8,
      Math.min(pushWidth, wrap ? actionWidth : actionWidth - commitWidth - 41), actionHeight);
    setRect(".commit-actions > .icon-button", width - 43, 8 + (actionHeight - 30) / 2, 27, 30);
  });
}
window.addEventListener("resize", measureCommitPanels);

// 文档工具栏共享文字度量，图标仍使用固定命中区。
function measureDocumentToolbar() {
  const context = document.createElement("canvas").getContext("2d");
  document.querySelectorAll(".document-path").forEach(path => { path.title = path.textContent; });
  const zoom = document.querySelector(".image-zoom-label");
  if (zoom) {
    const style = getComputedStyle(zoom);
    context.font = `600 ${style.fontSize} ${style.fontFamily}`;
    zoom.style.flexBasis = `${Math.max(48, Math.ceil(context.measureText("800%").width) + 8)}px`;
  }
  const documentView = document.querySelector(".document-view");
  if (documentView && new URLSearchParams(location.search).has("target") && !documentView.querySelector(".document-target")) {
    const target = document.createElement("div");
    target.className = "document-target";
    target.textContent = "符号链接目标：D:\\shared\\Augit\\配置与示例文件\\global.json";
    documentView.querySelector(".document-toolbar").after(target);
    documentView.classList.add("has-target");
    measureDocumentToolbar();
  }
}

if (requestedTheme === "dark") {
  document.body.dataset.theme = "dark";
}

// 视觉稿直接使用本地几何，不依赖联网后的第二次图标替换。
// 以下补齐项依据现有原生绘制；与原生一致不等于已取得 PyCharm 同场景实测。
const toolbarIconShapes = {
  "menu": '<path d="M2 3h12M2 8h12M2 13h12"/>',
  "window-minimize": '<path d="M2 11h12"/>',
  "window-maximize": '<rect x="3" y="3" width="10" height="10"/>',
  "window-close": '<path d="m3 3 10 10M13 3 3 13"/>',
  "folder": '<path d="M1 2h5l2 3h7v9H1Z"/>',
  "git-commit-horizontal": '<path d="M1 8h4.5M10.5 8H15"/><circle cx="8" cy="8" r="2.5"/>',
  "git-branch": '<circle cx="4" cy="3" r="2"/><circle cx="12" cy="5" r="2"/><path d="M4 5v10M12 7v1a4 4 0 0 1-4 4H4"/>',
  "search": '<circle cx="7" cy="7" r="5"/><path d="m11 11 3 3"/>',
  "history-search": '<circle cx="6.5" cy="6.5" r="4.5"/><path d="m10 10 5 5"/>',
  "history-back": '<path d="m12 2-6 6 6 6"/>',
  "arrow-up": '<path d="M8 14V2M4 6l4-4 4 4"/>',
  "arrow-down": '<path d="M8 2v12M4 10l4 4 4-4"/>',
  "arrow-left": '<path d="m10 4-4 4 4 4"/>',
  "arrow-right": '<path d="m6 4 4 4-4 4"/>',
  "chevron-left": '<path d="m10 4-4 4 4 4"/>',
  "chevron-right": '<path d="m6 4 4 4-4 4"/>',
  "chevron-up": '<path d="m4 10 4-4 4 4"/>',
  "plus": '<path d="M2 8h12M8 2v12"/>',
  "minus": '<path d="M3 8h10"/>',
  "trash-2": '<path d="M1 3h14M5 1h6"/><rect x="3" y="5" width="10" height="10"/>',
  "x": '<path stroke-width="1.2" d="m4.5 4.5 7 7m0-7-7 7"/>',
  "pilcrow": '<g stroke-width="1"><ellipse cx="6" cy="5" rx="4" ry="4"/><path d="M8 1v14M12 1v14"/></g>',
  "corner-down-right": '<path stroke-width="1" d="M2 2v9h11M9 7l4 4-4 4"/>',
  "history-expand": '<path d="M8 1v14M4 5l4-4 4 4M4 11l4 4 4-4"/>',
  "cloud": '<path d="m3 8 5-4 5 4-5 4Z"/><circle cx="3" cy="8" r="2.2"/><circle cx="8" cy="4" r="2.2"/><circle cx="13" cy="8" r="2.2"/><circle cx="8" cy="12" r="2.2"/>',
  "archive": '<g stroke-width="1"><rect x="2" y="5" width="12" height="9"/><path d="M2 5V2h12v3M6 8h4"/></g>',
  "folder-git-2": '<g stroke-width="1"><path d="M.5 4h5l2-2h8v12H.5Z M7.5 8h4v2"/><circle cx="7" cy="7.5" r="1.5"/><circle cx="12" cy="7.5" r="1.5"/><circle cx="12" cy="11.5" r="1.5"/></g>',
  // 分支菜单沿用当前原生实现，尚未计为 PyCharm 图形校准完成。
  "branch-update": '<path d="m3 4 8 8H7M11 12V8"/>',
  "branch-push": '<path d="m4 12 8-8H8M12 4v4"/>',
  "case-sensitive": '<g stroke-width="1"><path d="m1.5 12 3-8 3 8M2.5 9h4m7-1.5V12"/><ellipse cx="11.25" cy="9.75" rx="2.25" ry="2.25"/></g>',
  "whole-word": '<g stroke-width="1"><path d="M3 3H1v10h2M13 3h2v10h-2M7 7.5v4M9 4.5v7"/><ellipse cx="5.25" cy="9.5" rx="1.75" ry="2"/><ellipse cx="10.75" cy="9.5" rx="1.75" ry="2"/></g>',
  "regex": '<g stroke-width="1"><path d="M10.5 3v7m-3-5.25 6 3.5m-6 0 6-3.5"/><circle cx="3.25" cy="12.25" r=".75"/></g>',
  "settings": '<path d="M5.3000 3.3235L6.3313 2.8643L6.5446 1.1530L9.4554 1.1530L9.6687 2.8643L10.7000 3.3235L10.7000 3.3235L11.6133 3.9870L13.2020 3.3161L14.6574 5.8369L13.2820 6.8773L13.4000 8.0000L13.4000 8.0000L13.2820 9.1227L14.6574 10.1631L13.2020 12.6839L11.6133 12.0130L10.7000 12.6765L10.7000 12.6765L9.6687 13.1357L9.4554 14.8470L6.5446 14.8470L6.3313 13.1357L5.3000 12.6765L5.3000 12.6765L4.3867 12.0130L2.7980 12.6839L1.3426 10.1631L2.7180 9.1227L2.6000 8.0000L2.6000 8.0000L2.7180 6.8773L1.3426 5.8369L2.7980 3.3161L4.3867 3.9870L5.3000 3.3235Z"/><circle cx="8" cy="8" r="2.3"/>',
  "document-source": '<path stroke-width="1" d="M3 3.5h10M3 6.5h10M3 9.5h10M3 12.5h10"/>',
  "document-split": '<g stroke-width="1"><path d="M1.5 3.5H6M1.5 6.5H6M1.5 9.5H6M1.5 12.5H6"/><rect x="8.5" y="2.5" width="6" height="11" rx="1.5"/></g>',
  "document-preview": '<g stroke-width="1"><rect x="2.5" y="2.5" width="11" height="11" rx="2"/><circle cx="10" cy="6" r="1.5"/><path d="m2.5 9.5 2-2 6 6"/></g>',
  "document-formatted": '<g fill="none" stroke-width="1"><path d="M6.5 2.5C4.5 2.5 4.5 4.5 4.5 6c0 1.2-1 1.2-1 2s1 0.8 1 2c0 1.5 0 3.5 2 3.5M9.5 2.5c2 0 2 2 2 3.5 0 1.2 1 1.2 1 2s-1 .8-1 2c0 1.5 0 3.5-2 3.5"/></g>',
  "zoom-out": '<g stroke-width="1"><circle cx="7" cy="7" r="4.5"/><path d="m10.5 10.5 3.5 3.5M4.5 7h5"/></g>',
  "zoom-in": '<g stroke-width="1"><circle cx="7" cy="7" r="4.5"/><path d="m10.5 10.5 3.5 3.5M4.5 7h5M7 4.5v5"/></g>',
  "image-fit": '<path stroke-width="1" d="M2 6V2h4M10 2h4v4M2 10v4h4M10 14h4v-4"/>',
  "diff-unified": '<rect stroke-width="1" x="2.5" y="2.5" width="11" height="11" rx="2"/>',
  "diff-side-by-side": '<g stroke-width="1"><rect x="2.5" y="2.5" width="11" height="11" rx="2"/><path d="M8 2.5v11"/></g>',
  "diff-ignore-whitespace": '<path stroke-width="1" d="M9 2.5v11M12 2.5v11M14 2.5H6a3.5 3.5 0 0 0 0 7h3"/>',
  "ellipsis-vertical": '<g fill="currentColor" stroke="none"><circle cx="8" cy="4" r=".8"/><circle cx="8" cy="8" r=".8"/><circle cx="8" cy="12" r=".8"/></g>',

  "copy": '<path stroke-width="1" d="M5 2h7a2 2 0 0 1 2 2v7 M4 4h5a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2 M5 7h3M5 9.5h3M5 12h3"/>',
  "cherry": '<circle cx="3.5" cy="11.5" r="2.5"/><circle cx="11.5" cy="12.5" r="2.5"/><path d="M3.5 9C4 6 10 6 10 2 M11.5 10C13 6 10 5 10 2 M10 2C7 2 6 3 6 5"/>',
  "git-branch-plus": '<circle cx="4" cy="3" r="2"/><circle cx="4" cy="13" r="2"/><circle cx="12" cy="3" r="2"/><path d="M4 5v6M4 10C4 6 12 9 12 5M12 9v6M9 12h6"/>',
  "tag": '<path d="M1 3h7l7 7-6 5-8-7Z"/><circle cx="4.5" cy="6.5" r="1"/>',
  "external-link": '<path d="M2 4h5M2 4v10h10V9M7 9l7-7H9M14 2v5"/>',
  "list-tree": '<path d="M2 3v10M2 3h3M2 8h3M2 13h3M8 3h6M8 8h6M8 13h6"/>',
  "wrap-text": '<path d="M2 3h12M2 7h9a3 3 0 0 1 0 6H7l2-2M7 13l2 2M2 13h2"/>',
  "chevron-down": '<path d="m4 6 4 4 4-4"/>',
  "refresh-cw": '<path d="M1 8l1.83 1.88L4.7 8 M15 8l-1.83-1.88L11.3 8 M2.83 9.88A5.5 5.5 0 0 1 10.75 3.24 M13.17 6.12A5.5 5.5 0 0 1 5.25 12.76"/>',
  "git-compare-arrows": '<path d="M7 4h8 M10 1 7 4l3 3 M1 12h8 M6 9l3 3-3 3"/>',
  "undo-2": '<path d="M2 5h8a4 4 0 0 1 0 8H5 M5 2 2 5l3 3"/>',
  "download": '<path d="M1 8h3l1 3h6l1-3h3v6H1Z M8 1v7M5 5l3 3 3-3"/>',
  "eye": '<path d="M1 8C4.5 1.333 11.5 1.333 15 8C11.5 14.667 4.5 14.667 1 8Z"/><circle cx="8" cy="8" r="2.5"/>',
  "locate-fixed": '<circle cx="8" cy="8" r="6"/><path d="M1 8h4m6 0h4 M8 1v4m0 6v4"/>',
  "fold-vertical": '<path d="m4.5 2 3.5 3.5L11.5 2 M4.5 14 8 10.5l3.5 3.5"/>',
  "square-terminal": '<rect x="1" y="2" width="14" height="12"/><path d="m4 5 3 3-3 3m5 0h3"/>',
  "history": '<circle cx="8" cy="8" r="6"/><path d="M8 4.5V8l3.5 2 M1 8h3M1 8l2-2"/>',
  "rotate-ccw": '<path d="M2 5h8a4 4 0 0 1 0 8H5 M5 2 2 5l3 3"/>',
  "rename": '<path d="M3 13 5 5l8 8H3Zm2-8 3 3m1-7 4 4"/>',
  "clone": '<g stroke-width="1.2"><path d="M1.5 5h5l1.5 1.5h6.5v7h-13Z"/><circle cx="6" cy="9" r="1.4"/><circle cx="11" cy="11" r="1.4"/><path d="M7.2 9.8 9.8 10.4"/></g>',
  "conflict": '<g stroke-width="1.2"><path d="M3 1.5h7l3 3v10H3Z M10 1.5v3h3M8 7v4"/><circle cx="8" cy="13" r=".8"/></g>',
};
function icon(name) {
  if (name === "none") return '<span class="menu-icon-empty" aria-hidden="true"></span>';
  if (name === "folder-open") return treeFolderIcon();
  if (name === "lock") {
    return '<svg class="augit-toolbar-icon" viewBox="0 0 12 12" fill="none" stroke="currentColor" stroke-width="1" aria-hidden="true" data-augit-icon="lock"><path d="M3 5V3a3 3 0 0 1 6 0v2"/><rect x="1" y="5" width="10" height="7"/></svg>';
  }
  if (name === "file-warning") {
    return '<svg class="augit-toolbar-icon" viewBox="0 0 18 18" fill="none" stroke="currentColor" stroke-width="1" aria-hidden="true" data-augit-icon="file-warning"><path d="M3 1h8l4 4v12H3Z M11 1v4h4M9 7v4"/><circle cx="9" cy="14" r="1"/></svg>';
  }
  if (!Object.hasOwn(toolbarIconShapes, name)) throw new Error(`视觉稿未登记图标：${name}`);
  return `<svg class="augit-toolbar-icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" data-augit-icon="${name}">${toolbarIconShapes[name]}</svg>`;
}

function windowActionIcons() {
  return '<span class="window-dot" aria-label="最小化">' + icon("window-minimize")
    + '</span><span class="window-dot" aria-label="最大化">' + icon("window-maximize")
    + '</span><span class="window-dot" aria-label="关闭窗口">' + icon("window-close") + '</span>';
}

// 目录和引用是树节点图形，不套用工具栏的粗线空心文件夹或菜单标签。
function treeFolderIcon(workspaceRoot = false) {
  const folder = '<path d="M3 2.5h2.5C6 2.5 6.3 2.7 6.6 3L7.9 4.3C8.2 4.6 8.5 4.5 9 4.5h4c.83 0 1.5.67 1.5 1.5v6c0 .83-.67 1.5-1.5 1.5H3c-.83 0-1.5-.67-1.5-1.5V4c0-.83.67-1.5 1.5-1.5Z" fill="var(--augit-folder-fill)" stroke="var(--augit-folder-outline)" stroke-width="1"/>';
  const badge = workspaceRoot ? '<rect x="9" y="9" width="7" height="7" rx="2" fill="var(--augit-root-fill)" stroke="var(--augit-row-background, var(--augit-panel))" stroke-width="3"/><rect x="9.5" y="9.5" width="6" height="6" rx="1.5" fill="var(--augit-root-fill)" stroke="var(--augit-root-outline)"/>' : '';
  return `<svg class="tree-folder-icon" viewBox="0 0 16 16" aria-hidden="true">${folder}${badge}</svg>`;
}

function gitReferenceIcon(filled = true) {
  const outline = 'M8.6 1.5h4.9c.55 0 1 .45 1 1v4.9L7.9 14c-.39.39-1.01.39-1.4 0L2 9.5c-.39-.39-.39-1.01 0-1.4L8.6 1.5Z M12 5c0 .83-.67 1.5-1.5 1.5S9 5.83 9 5s.67-1.5 1.5-1.5S12 4.17 12 5Z';
  return `<svg class="git-reference-icon" viewBox="0 0 16 16" aria-hidden="true"><path d="${outline}" fill="${filled ? 'var(--augit-reference)' : 'none'}" fill-rule="evenodd" stroke="${filled ? 'none' : 'var(--augit-reference)'}" stroke-width="1" stroke-linejoin="round"/></svg>`;
}

// 文件类型图标与原生绘制使用相同的 16px 坐标；Git 状态只改变文件名颜色。
function fileTypeIcon(name) {
  // 容忍空值：无文档或数据未到达时仍可能渲染标签，缺名称不应导致整页异常。
  const safeName = String(name ?? "");
  const extension = safeName.toLowerCase().split(".").pop();
  let kind = "file";
  let shape = '<path d="M2 1h8.5L14 4.5V15H2Z M10.5 1v3.5H14 M4 6h8 M4 9h8 M4 12h6"/>';
  if (safeName.toLowerCase().split(/[\\/]/).pop() === ".gitignore") {
    kind = "ignored";
    shape = '<circle cx="8" cy="8" r="6"/><path d="m4 12 8-8"/>';
  } else if (["slnx", "props", "gitattributes"].includes(extension)) {
    kind = "text";
    shape = '<path d="M2 4h11 M2 8h11 M2 12h11"/>';
  } else if (["md", "markdown"].includes(extension)) {
    kind = "markdown";
    shape = '<path fill="currentColor" stroke="none" d="M1 4h2l2 4 2-4h2v8H7V7.5L5 11 3 7.5V12H1Z"/><path d="M13 4v8m-2-2 2 2 2-2"/>';
  } else if (["cs", "csproj"].includes(extension)) {
    kind = "csharp";
    shape = '<path d="M7.47 10.83A3.5 4 0 1 1 7.47 5.17 M11.5 4.5l-1 7 M14.5 4.5l-1 7 M9.5 6.5H15 M9 9.5h5.5"/>';
  } else if (["html", "htm", "xml"].includes(extension)) {
    kind = "markup";
    shape = '<path d="m8 4-3 4 3 4m2-8 3 4-3 4"/>';
  } else if (["json", "yaml", "yml"].includes(extension)) {
    kind = "structured";
    shape = '<path d="m7 4-2 2v4l2 2m4-8 2 2v4l-2 2"/>';
  } else if (["png", "jpg", "jpeg", "bmp"].includes(extension)) {
    kind = "image";
    shape = '<rect x="2" y="2" width="12" height="12"/><circle cx="10.5" cy="5.5" r="1"/><path d="m2 11 4-4 8 7"/>';
  }
  return `<svg class="file-type-icon file-type-${kind}" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${shape}</svg>`;
}

// 容忍 undefined / null：渲染函数可能在数据尚未到达或被拒绝时被调用，
// 缺一个字段不应该让整页渲染抛异常。
const escapeHtml = (value) => String(value ?? "")
  .replaceAll("&", "&amp;")
  .replaceAll("<", "&lt;")
  .replaceAll(">", "&gt;")
  .replaceAll('"', "&quot;");

// 工作区沿用历史样本的真实路径与重命名结果，列表、复选和导航共用完整集合。
const workspaceRenames = new Map(historySampleFiles("fix: 精确恢复安装前系统 PATH")
  .filter(file => file.original).map(file => [file.original, file.path]));
const changesSamples = [
  ...historySampleFiles("feat: 实现 Augit 阶段零至五功能").map(file => ({
    path: workspaceRenames.get(file.path) || file.path, group: "Changes", checked: true,
  })),
  ...["src/Augit.App/NativeToolTip.cs", "src/Augit.App/NativeModalScrim.cs", "src/Augit.App/NativeFocusNavigation.cs",
    "docs/ux-spec.md", "docs/ux-mockups/new-theme.css", "tests/Augit.App.Tests/VisualAuditFixtureTests.cs",
    "tools/xterm/terminal/README.md"].map(path => ({ path, group: "Unversioned Files", checked: false })),
].map(file => ({ ...file, name: file.path.split("/").at(-1), directory: file.path.split("/").slice(0, -1).join("/") }));
const changesNameOrder = new Intl.Collator("en", { numeric: true, sensitivity: "base" });
changesSamples.sort((a, b) => a.group.localeCompare(b.group)
  || changesNameOrder.compare(a.name, b.name) || changesNameOrder.compare(a.path, b.path));

const scenePages = {
  "main-project": ["项目与文件查看", "项目树、标签和只读正文的默认工作区"],
  "text-viewer": ["普通文本查看", "行号、只读文本、当前文件查找和跳转"],
  "markdown-preview": ["Markdown 预览", "原文、预览和左右对照模式"],
  "json-preview": ["JSON 格式化", "原文与只读格式化结果"],
  "image-preview": ["图片预览", "PNG、JPEG 与 BMP 的受限预览"],
  "file-limit": ["不可预览文件", "超限、非法 UTF-8、GIF、WebP 与二进制摘要"],
  "commit-changes": ["提交工具窗", "Changes、Unversioned Files、复选和提交信息"],
  "commit-diff": ["工作区 Diff", "选择变更文件后的稳定双栏预览"],
  "diff-boundary": ["Diff 文件边界", "再次同方向操作才进入相邻文件"],
  "git-history": ["Git 历史", "引用树、提交图、筛选、文件和元数据"],
  "file-history": ["文件历史与 Blame", "限定当前路径的提交历史与逐行归属"],
  "git-compare": ["引用比较", "分支、标签、提交和工作区比较"],
  "history-diff-loading": ["历史 Diff 加载", "延迟加载反馈与原位取消"],
  "history-diff-failure": ["历史 Diff 失败", "局部原因说明并保留历史上下文"],
  "history-diff-cancelled": ["历史 Diff 取消", "保留标签与列表选择，允许重新打开"],
  "branches": ["分支与标签", "搜索、创建、切换、推送、比较与危险操作"],
  "stash": ["Stash 管理", "创建、查看、应用、弹出和删除"],
  "reset": ["Reset", "Soft、Mixed、Hard 的影响预览与确认"],
  "worktrees": ["Worktree 管理", "创建、打开和安全移除"],
  "remote": ["远端管理", "增删改远端名称与 URL"],
  "clone": ["克隆仓库", "URL、目标目录和进行状态"],
  "push": ["Push", "提交列表、目标引用和错误反馈"],
  "conflict-list": ["冲突操作会话", "冲突文件、Continue、Skip 与 Abort"],
  "conflict-resolver": ["三栏冲突解决", "当前分支、可编辑结果和合入内容"],
  "quick-open": ["快速打开文件", "最多 100 项的键盘优先浮层"],
  "repository-search": ["全仓搜索", "查询开关、结果分组、截断与取消"],
  "terminal": ["内置终端", "底部按需单会话终端"],
  "settings": ["设置", "外观、Git、终端和字体"],
  "git-unavailable": ["Git 不可用", "缺失或版本过低时的降级状态"],
  "operation-result": ["Git 操作反馈", "进行中、成功、失败和可取消状态"],
  "workspace-open": ["打开工作区", "最近目录、选择目录和启动降级状态"],
  "repository-init": ["初始化仓库", "非 Git 目录的显式初始化确认"],
  "blame": ["Blame", "只读正文旁的逐行提交归属"],
  "smart-checkout": ["Smart Checkout", "覆盖风险、临时 Stash 和恢复结果"],
  "rollback": ["Rollback", "完整文件回滚与将丢失 Diff"],
  "operation-progress": ["Git 操作进行中", "进度、取消和防止重复触发"],
  "terminal-close": ["关闭运行中终端", "终止前台命令与子进程树确认"],
  "search-limited": ["搜索截断与超时", "1000 项上限、停止和已有结果保留"],
  "commit-empty": ["提交空态", "没有待提交更改时保留完整工具窗骨架"],
  "diff-loading": ["Diff 局部加载", "只更新临时 Diff 标签正文"],
  "push-no-remote": ["Push 无远端", "Define remote 与禁用的 Push 动作"],
  "stash-manager": ["Stash 管理", "已有 Stash 的查看、应用、弹出和删除"],
  "project-context-menu": ["项目右键菜单", "只保留 Augit 支持的文件操作"],
  "changes-context-menu": ["Changes 右键菜单", "Diff、回滚、历史和定位操作"],
  "git-history-menu": ["Git 历史右键菜单", "提交级操作与危险入口"],
  "quick-open-empty": ["快速打开空态", "弹层按内容高度增长"],
};

const sourceLines = [
  "# Augit 产品规格",
  "",
  "## 1. 产品定位",
  "",
  "Augit 是供 100% 外部 AI 协作开发场景使用的轻量桌面工具。",
  "",
  "轻量优先是 Augit 的最高产品原则。",
  "",
  "## 2. 支持环境与性能目标",
  "",
  "- 支持 Windows 10 22H2 x64 和 Windows 11 x64。",
  "- 冷启动到界面可操作优先争取不超过 1 秒。",
  "- 核心进程树 Working Set 总和优先控制在 100 MB 内。",
  "- 外部文件和本地 Git 状态变化在 500 毫秒内反映。",
  "- 后台空闲时不持续扫描仓库，不明显占用 CPU。",
  "",
  "## 3. 工作区与文件浏览",
  "",
  "一个窗口只打开一个目录；同一目录不重复开窗口。",
  "普通文件查看器不提供编辑和保存功能。",
  "",
  "## 4. Git 能力",
  "",
  "Git 操作界面和流程固定参考 IntelliJ IDEA 2026.2 New UI。",
];

const textViewerSourceLines = [
  "using System.Collections.Concurrent;",
  "using System.ComponentModel;",
  "using System.Runtime.InteropServices;",
  "using Augit.Core.Files;",
  "using Augit.Core.Git;",
  "using Augit.Infrastructure.Files;",
  "using Augit.Infrastructure.Git;",
  "using Augit.Infrastructure.Interop;",
  "using Augit.Infrastructure.Settings;",
  "using Microsoft.Web.WebView2.Core;",
  "",
  "namespace Augit.App;",
  "",
  "internal sealed class MainWindow : IDisposable",
  "{",
  '  private const string WindowClassName = "Augit.MainWindow.Native";',
  "  private static int MinimumWidth => S(1024);",
  "  private static int MinimumHeight => S(640);",
  "  private static int ToolbarHeight => S(44);",
  "",
  "  private bool _showingGitPanel;",
  "  private bool _showingHistoryPanel;",
  "  private bool _showingTerminalPanel;",
  "  private bool _mainMenuOpen;",
  "  private string _branchLabel = \"Git\";",
  "",
  "  internal MainWindow(SettingsStore settingsStore, ApplicationSettings settings)",
  "  {",
  "    _settingsStore = settingsStore;",
  "    _settings = settings;",
];

function codeLines(lines = sourceLines, active = 12) {
  return lines.map((line, index) => `<div class="code-line ${index + 1 === active ? "active" : ""}"><span class="line-number">${index + 1}</span><span>${line || " "}</span></div>`).join("");
}

function measureCodeViews() {
  const context = document.createElement("canvas").getContext("2d");
  const scale = window.devicePixelRatio || 1;
  document.querySelectorAll(".diff-columns").forEach(view => {
    view.style.setProperty("--code-line-height", `${Math.round(parseFloat(getComputedStyle(view).fontSize) * 1.7 * scale) / scale}px`);
    const layout = view.closest(".diff-layout");
    if (!view.classList.contains("diff-unified-body")) {
      const style = getComputedStyle(view);
      context.font = `${style.fontSize} ${style.fontFamily}`;
      const columns = [...view.querySelectorAll(".diff-gutter > div")];
      const digits = Math.max(3, ...columns.flatMap(column => [...column.childNodes]
        .filter(node => node.nodeType === Node.TEXT_NODE).map(node => node.textContent.trim().length)));
      const width = Math.max(84, Math.ceil(context.measureText(`${'9'.repeat(digits)}    ${'9'.repeat(digits)}`).width * scale) / scale + 14);
      layout.style.setProperty("--comparison-gutter-width", `${width}px`);
    }
  });
  document.querySelectorAll(".code-view").forEach(view => {
    const rows = [...view.querySelectorAll(":scope > .code-line, :scope > .code-lines > .code-line")];
    if (!rows.length) return;
    let lines = view.querySelector(":scope > .code-lines");
    if (!lines) {
      lines = document.createElement("div");
      lines.className = "code-lines";
      view.append(lines);
      lines.append(...rows);
    }
    const style = getComputedStyle(view);
    context.font = `${style.fontSize} ${style.fontFamily}`;
    const digits = Math.max(3, String(rows.length).length);
    const gutter = Math.round(context.measureText("9".repeat(digits)).width * scale) / scale
      + Math.round(28 * scale) / scale;
    view.style.setProperty("--code-gutter-width", `${gutter}px`);
    view.style.setProperty("--code-line-height", `${Math.round(parseFloat(style.fontSize) * 1.7 * scale) / scale}px`);
    view.closest(".blame-layout")?.style.setProperty("--code-line-height", view.style.getPropertyValue("--code-line-height"));
    const blame = view.closest(".blame-layout")?.querySelector(".blame-gutter");
    if (blame) {
      blame.style.fontSize = style.fontSize;
      const cells = [...blame.querySelectorAll(".blame-row")];
      const dateDigits = Math.max(9, ...cells.map(row => row.children[0].textContent.length));
      const numberDigits = Math.max(3, ...cells.map(row => row.children[2].textContent.length));
      const measure = text => Math.ceil(context.measureText(text).width * scale) / scale;
      const date = Math.max(72, measure("8".repeat(dateDigits)));
      const number = Math.max(24, measure("8".repeat(numberDigits)));
      const author = Math.max(24, measure("I49"));
      const layout = view.closest(".blame-layout");
      layout.style.setProperty("--blame-date-width", `${date}px`);
      layout.style.setProperty("--blame-number-width", `${number}px`);
      layout.style.setProperty("--blame-width", `${date + author + number + 22}px`);
    }
  });
}

function jsonView() {
  if (liveDocument()) return liveJsonDocument();
  const state = new URLSearchParams(window.location.search).get("json-state") || "formatted";
  const invalid = state === "invalid";
  return `<div class="document-view json-document ${invalid ? "json-invalid" : ""}" data-json-mode="${invalid || state === "source" ? "source" : "formatted"}"><div class="document-toolbar"><span class="document-path">Augit › global.json　只读</span><div class="segmented document-modes"><button class="segment" aria-label="原文" data-json-mode="source">${icon("document-source")}</button><button class="segment" aria-label="格式化" data-json-mode="formatted" ${invalid ? 'disabled title="JSON 格式错误，请查看原文中的错误位置"' : ""}>${icon("document-formatted")}</button></div><button class="icon-button" aria-label="更多">${icon("ellipsis-vertical")}</button></div>${invalid ? '<button class="json-error" aria-label="定位 JSON 错误">JSON 格式错误：第 4 行，第 19 列。 点击定位。</button>' : ""}<div class="code-view" tabindex="0" aria-label="JSON 只读正文"></div></div>`;
}

function bindJsonModes() {
  const view = document.querySelector(".json-document");
  if (!view) return;
  const source = '{"sdk":{"version":"10.0.201","rollForward":"latestPatch","allowPrerelease":false}}\n';
  const invalidSource = '{\n  "sdk": {\n    "version": "10.0.201",\n    "rollForward":}\n}\n';
  const code = view.querySelector(".code-view");
  const positions = new Map();
  let current;
  const update = mode => {
    if (mode === current || (mode === "formatted" && view.classList.contains("json-invalid"))) return;
    if (current) positions.set(current, [code.scrollLeft, code.scrollTop]);
    current = view.dataset.jsonMode = mode;
    view.querySelectorAll("button[data-json-mode]").forEach(button => {
      const active = button.dataset.jsonMode === mode;
      button.classList.toggle("active", active);
      button.setAttribute("aria-pressed", String(active));
    });
    const text = view.classList.contains("json-invalid") ? invalidSource
      : mode === "source" ? source : JSON.stringify(JSON.parse(source), null, 2);
    code.innerHTML = text.split("\n").map((line, index) => `<div class="code-line" data-line="${index + 1}"><span class="line-number">${index + 1}</span><span>${escapeHtml(line)}</span></div>`).join("");
    measureCodeViews();
    const [left, top] = positions.get(mode) || [0, 0];
    code.scrollTo(left, top);
    view.dispatchEvent(new Event('document-content-changed'));
  };
  view.querySelectorAll("button[data-json-mode]").forEach(button => button.addEventListener("click", () => update(button.dataset.jsonMode)));
  view.querySelector(".json-error")?.addEventListener("click", () => {
    const row = code.querySelector('[data-line="4"]');
    row.classList.add("active");
    row.scrollIntoView({ block: "nearest" });
    code.focus({ preventScroll: true });
  });
  update(view.dataset.jsonMode);
}

function diffFileHeader(source, target, title, path = "src/Augit.App/app.manifest") {
  return `<div class="diff-filebar reference-filebar" title="${escapeHtml(title)} · ${escapeHtml(path)}"><div class="reference-before"><span class="reference-lock">${icon("lock")}</span><span class="reference-source">${source}</span><span class="reference-path">${escapeHtml(path)}</span></div><div class="reference-after"><span class="reference-lock">${icon("lock")}</span><span class="reference-target">${target}</span></div></div>`;
}

function workspaceSampleOriginal(path) {
  const sample = changesSamples.find(file => file.path === path);
  if (sample?.group === "Unversioned Files") return "";
  // 与原生 VisualAuditHost 的固定仓库保持同文，不读取用户工作区或 Git。
  const original = [...workspaceRenames].find(([, target]) => target === path)?.[0] || path;
  const name = original.split("/").at(-1), stem = name.replace(/\.cs$/, "");
  const texts = {
    "THIRD-PARTY-NOTICES.md": "# 第三方组件声明\n\n视觉审计使用本地测试数据。\n",
    "README.md": "# Augit\n\n轻量 Git 编辑器视觉审计仓库。\n",
    "global.json": '{\n  "sdk": {\n    "version": "10.0.100"\n  }\n}\n',
    "Augit.slnx": '<Solution>\n  <Project Path="src/Augit.App/Augit.App.csproj" />\n</Solution>\n',
    "docs/architecture.md": "# 架构说明\n\n主界面使用 C#、.NET 10 和 Win32。\n",
    "docs/runtime-dependencies.md": "# 运行时依赖\n\n记录审计所需的本机运行时。\n",
    "docs/performance-report.md": "# 性能报告\n\n记录启动、内存和刷新测量。\n",
    "docs/roadmap.md": "# 实施路线\n\n按 UX 规格逐页完成视觉和交互验收。\n",
    "tests/Augit.App.Tests/Augit.App.Tests.csproj": '<Project Sdk="Microsoft.NET.Sdk" />\n',
    "src/Augit.App/AssemblyInfo.cs": 'using System.Runtime.CompilerServices;\n\n[assembly: AssemblyMetadata("Audit", "true")]\n',
    "tools/xterm/terminal/index.html": "<!doctype html>\n<title>终端</title>\n",
    "tools/xterm/terminal/terminal.js": "export function startTerminal() {}\n",
    "docs/product-spec.md": [
      "# Augit 产品规格", "", "## 1. 产品定位", "", "Augit 是供 100% 外部 AI 协作开发场景使用的轻量桌面工具。", "",
      "轻量优先是 Augit 的最高产品原则。", "", "## 2. 支持环境与性能目标", "",
      "- 支持 Windows 10 22H2 x64 和 Windows 11 x64。", "- 冷启动到界面可操作优先争取不超过 1 秒。",
      "- 核心进程树 Working Set 总和优先控制在 100 MB 内。", "- 外部文件和本地 Git 状态变化在 500 毫秒内反映。",
      "- 后台空闲时不持续扫描仓库，不明显占用 CPU。", "", "## 3. 工作区与文件浏览", "",
      "一个窗口只打开一个目录；同一目录不重复开窗口。", "普通文件查看器不提供编辑和保存功能。", "", "## 4. Git 能力", "",
      "Git 操作界面和流程固定参考 IntelliJ IDEA 2026.2 New UI。",
    ].join("\n") + "\n",
  };
  let text = texts[original];
  if (text === undefined) {
    if (original.startsWith("src/Augit.App/")) {
      text = "namespace Augit.App;\n\ninternal " + (stem === "NativeTheme" ? "static" : "sealed") + " class " + stem + "\n{\n}\n";
    } else if (original.startsWith("src/Augit.Infrastructure/Terminal/")) {
      text = "namespace Augit.Infrastructure.Terminal;\n\ninternal " + (stem === "ConPtyNativeMethods" ? "static" : "sealed") + " class " + stem + "\n{\n}\n";
    } else if (original.startsWith("src/Augit.Core/")) {
      const type = { ZAuditDiffModels: "ZAuditDiffDocument", ZAuditHistoryModels: "ZAuditHistoryEntry" }[stem] || stem;
      text = "namespace Augit.Core.Git;\n\npublic sealed record " + type + ";\n";
    } else {
      const area = original.split("/")[1], folder = original.split("/")[2];
      const namespace = area + (area === "Augit.Infrastructure" ? "." + folder : "");
      text = "namespace " + namespace + ";\n\npublic sealed " + (stem === "ApplicationSettings" ? "record " : "class ") + stem + ";\n";
    }
  }
  if (original === "src/Augit.App/MainWindow.cs") text += "\n阶段三：刷新窗口状态。\n";
  if (original === "src/Augit.App/NativeGitPanel.cs") text += "\n阶段三：保持 Changes 选择。\n";
  if (["docs/architecture.md", "docs/performance-report.md", "docs/roadmap.md", "docs/runtime-dependencies.md", "global.json", "Augit.slnx"].includes(original))
    text += "\n阶段四：避免不必要的运行时更新。\n";
  if (original === "docs/product-spec.md") text += "\n阶段五：精确恢复安装前系统 PATH。\n";
  return text;
}

function diffView(comparison = false, state = "ready", workspaceComparison = false, fileHistory = false, workspacePath = "src/Augit.App/app.manifest", historyHash = null) {
  let oldLines = [
    "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
    "<assembly manifestVersion=\"1.0\" xmlns=\"urn:schemas-microsoft-com:asm.v1\">",
    "  <assemblyIdentity version=\"1.0.0.0\" name=\"Augit.App\" />",
    "  <description>Augit</description>",
    "  <compatibility xmlns=\"urn:schemas-microsoft-com:compatibility.v1\">",
    "    <application>",
    "      <!-- Windows 10 and Windows 11 -->",
    "      <supportedOS Id=\"{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}\" />",
    "    </application>",
    "  </compatibility>",
    "  <application xmlns=\"urn:schemas-microsoft-com:asm.v3\">",
    "    <windowsSettings>",
    "      <dpiAware>true</dpiAware>",
    "      <longPathAware>true</longPathAware>",
    "    </windowsSettings>",
    "  </application>",
    "  <dependency>",
    "    <dependentAssembly>",
    "      <assemblyIdentity type=\"win32\"",
    "        name=\"Microsoft.Windows.Common-Controls\"",
    "        version=\"6.0.0.0\" processorArchitecture=\"*\" />",
    "    </dependentAssembly>",
    "  </dependency>",
    "</assembly>",
  ];
  let newLines = [
    "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
    "<assembly manifestVersion=\"1.0\" xmlns=\"urn:schemas-microsoft-com:asm.v1\">",
    "  <assemblyIdentity version=\"1.0.0.0\" name=\"Augit.App\" />",
    "  <description>Augit</description>",
    "  <compatibility xmlns=\"urn:schemas-microsoft-com:compatibility.v1\">",
    "    <application>",
    "      <!-- Windows 10 and Windows 11 -->",
    "      <supportedOS Id=\"{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}\" />",
    "    </application>",
    "  </compatibility>",
    "  <application xmlns=\"urn:schemas-microsoft-com:asm.v3\">",
    "    <windowsSettings>",
    "      <dpiAware>true/pm</dpiAware>",
    "      <dpiAwareness>PerMonitorV2</dpiAwareness>",
    "      <longPathAware>true</longPathAware>",
    "    </windowsSettings>",
    "  </application>",
    "  <dependency>",
    "    <dependentAssembly>",
    "      <assemblyIdentity type=\"win32\"",
    "        name=\"Microsoft.Windows.Common-Controls\"",
    "        version=\"6.0.0.0\" processorArchitecture=\"*\" />",
    "    </dependentAssembly>",
    "  </dependency>",
    "</assembly>",
  ];
  if (fileHistory) {
    oldLines = ["# Augit 产品规格", "", "## 产品定位", "", "Augit 是轻量桌面工具。", "", "普通文件只读。", "", "## 文件浏览", "", "外部修改后自动刷新。", "", "保留文件标签。", "", "仅支持 Windows 10 和 Windows 11。"];
    newLines = [...oldLines.slice(0, 12), "保留文件标签、选择和滚动位置。", "仅更新当前文件正文。", ...oldLines.slice(13)];
  }
  const appendOnly = !!historyHash || !comparison && workspacePath !== "src/Augit.App/app.manifest";
  if (appendOnly) {
    const text = workspaceSampleOriginal(workspacePath);
    oldLines = text ? text.replace(/\n$/, "").split("\n") : [];
    newLines = text ? [...oldLines, "", historyHash ? "历史修改：用于视觉审计的稳定变更。" : "工作区修改：用于视觉审计的稳定变更。"] : ["// 视觉审计样本文件"];
  }
  const oldChanged = appendOnly ? [] : [12];
  const newChanged = appendOnly ? newLines.map((_, index) => index).slice(oldLines.length) : [12, 13];
  const renderLines = (lines, changedLines, kind) => lines
    .map((line, index) => `<div class="diff-code-line ${changedLines.includes(index) ? kind : ""}">${escapeHtml(line)}</div>`)
    .join("");
  const unifiedRows = appendOnly ? [
    ...oldLines.map((line, index) => [index + 1, index + 1, "", line, ""]),
    ...newLines.slice(oldLines.length).map((line, index) => ["", oldLines.length + index + 1, "+", line, "added"]),
  ] : oldLines.flatMap((line, index) => index === 12
    ? [[13, "", "−", line, "removed"], ["", 13, "+", newLines[12], "added"], ["", 14, "+", newLines[13], "added"]]
    : [[index + 1, index < 12 ? index + 1 : index + 2, "", line, ""]]);
  const unifiedTemplate = `<template class="diff-unified-template">${unifiedRows.map(([oldNumber, newNumber, mark, line, kind]) =>
    `<div class="diff-code-line unified-diff-line ${kind}"><span class="unified-number">${oldNumber}</span><span class="unified-number">${newNumber}</span><span class="unified-marker">${mark}</span><span>${escapeHtml(line)}</span></div>`).join("")}</template>`;
  // 新版本多出一行时，旧版本与行号同时补空行；各类比较共用同一对齐样本。
  const alignedOldLines = appendOnly ? [...oldLines, ...newChanged.map(() => "")] : [...oldLines.slice(0, 13), "", ...oldLines.slice(13)];
  const oldNumbers = appendOnly ? [...oldLines.map((_, index) => index + 1), ...newChanged.map(() => "")]
    : [...oldLines.slice(0, 13).map((_, index) => index + 1), "", ...oldLines.slice(13).map((_, index) => index + 14)];
  if (comparison) {
    const hash = "dfe5c25a2e0d6378b44fbac8f4d74c8f3baa2601";
    const source = historyHash ? `${historyHash.slice(0, 8)}^` : workspaceComparison ? "HEAD" : "dfe5c25a^";
    const target = historyHash ? historyHash.slice(0, 8) : workspaceComparison ? "工作区" : "dfe5c25a";
    const referenceTitle = workspaceComparison ? "HEAD → 工作区" : `${hash}^ → ${hash}`;
    const disabled = state === "ready" ? "" : "disabled";
    let body = `<div class="diff-columns"><div class="diff-side">${renderLines(alignedOldLines, oldChanged, "removed")}</div><div class="diff-gutter"><div>${oldNumbers.map(number => `${number}<br>`).join("")}</div><div>${newLines.map((_, index) => `${index + 1}<br>`).join("")}</div></div><div class="diff-side">${renderLines(newLines, newChanged, "added")}</div></div>`;
    if (state === "loading") {
      const lines = [72, 46, 87, 61, 78, 39, 69, 54, 82, 48, 64, 43].map(width => `<span class="diff-loading-line" style="width:${width}%"></span>`).join("");
      const gutter = Array.from({ length: 12 }, () => '<span class="diff-loading-gutter-line"></span><span class="diff-loading-gutter-line"></span>').join("");
      body = `<div class="diff-columns diff-loading-columns"><div class="diff-loading-status"><span class="loading-mark"></span><span>正在生成 diff...</span></div><div class="diff-loading-side">${lines}</div><div class="diff-loading-gutter">${gutter}</div><div class="diff-loading-side">${lines}</div></div>`;
    } else if (state !== "ready") {
      body = `<div class="comparison-notice ${state === "failure" ? "failure" : ""}"><svg class="notice-mark" viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="7"/><path d="M8 5v4m0 2v.5"/></svg><span>${state === "failure" ? "无法读取提交中的文件：Git 查询失败。" : "比较已取消。"}</span></div>`;
    }
    return `<div class="diff-layout"><div class="diff-toolbar comparison-toolbar"><button class="toolbar-button" ${disabled} aria-label="上一处差异">${icon("arrow-up")}</button><button class="toolbar-button" ${disabled} aria-label="下一处差异">${icon("arrow-down")}</button><span class="toolbar-separator"></span><button class="toolbar-button" ${disabled} aria-label="查找">${icon("search")}</button><span class="grow"></span>${state === "loading" ? '<span class="comparison-loading-label">正在生成 diff...</span>' : state === "ready" ? '<span class="comparison-summary">1 处差异</span>' : ""}<button class="toolbar-button" aria-label="忽略空白">${icon("diff-ignore-whitespace")}</button><div class="segmented"><button class="segment active" aria-label="双栏">${icon("diff-side-by-side")}</button><button class="segment" aria-label="单栏">${icon("diff-unified")}</button></div><button class="icon-button" aria-label="设置">${icon("settings")}</button></div>${diffFileHeader(source, target, referenceTitle, fileHistory ? "docs/product-spec.md" : undefined)}${body}${state === "ready" ? unifiedTemplate : ""}</div>`;
  }
  return `<div class="diff-layout"><div class="diff-toolbar"><button class="toolbar-button" aria-label="上一处差异">${icon("arrow-up")}</button><button class="toolbar-button" aria-label="下一处差异">${icon("arrow-down")}</button><span class="toolbar-separator"></span><button class="toolbar-button" aria-label="查找">${icon("search")}</button><button class="toolbar-button" aria-label="上一个文件">${icon("arrow-left")}</button><span class="file-status-modified">1/42 个文件</span><button class="toolbar-button" aria-label="下一个文件">${icon("arrow-right")}</button><span class="grow"></span><span>1 处差异，0 个已包含</span><button class="toolbar-button" aria-label="忽略空白">${icon("diff-ignore-whitespace")}</button><div class="segmented"><button class="segment active" aria-label="双栏">${icon("diff-side-by-side")}</button><button class="segment" aria-label="单栏">${icon("diff-unified")}</button></div><button class="icon-button" aria-label="设置">${icon("settings")}</button></div>${diffFileHeader("HEAD", "当前版本", "HEAD → 当前版本", workspacePath)}<div class="diff-columns"><div class="diff-side">${renderLines(alignedOldLines, oldChanged, "removed")}</div><div class="diff-gutter"><div>${oldNumbers.map(number => `${number}<br>`).join("")}</div><div>${newLines.map((_, index) => `${index + 1}<br>`).join("")}</div></div><div class="diff-side">${renderLines(newLines, newChanged, "added")}</div></div>${unifiedTemplate}</div>`;
}

function emptyChangesSide() {
  return `<aside class="tool-window side-tool"><div class="tool-header"><span>提交</span><span class="grow"></span><button class="icon-button" aria-label="更多">${icon("ellipsis-vertical")}</button><button class="icon-button" aria-label="最小化">${icon("minus")}</button></div>
    <div class="changes-layout"><div class="toolbar"><button class="toolbar-button" aria-label="刷新">${icon("refresh-cw")}</button><button class="toolbar-button" disabled aria-label="回滚">${icon("undo-2")}</button><button class="toolbar-button" disabled aria-label="显示 Diff">${icon("git-compare-arrows")}</button></div>
    <div class="empty-tool-state"><div><strong>没有待提交的更改</strong><p>工作区与 HEAD 一致。</p></div></div>
    <div class="commit-box"><div class="commit-options"><span class="commit-amend"><span class="fake-check"></span><span>Amend</span></span><span class="commit-last" hidden></span><span class="commit-count" hidden></span></div>
    ${commitMessageBox(true)}
    <div class="commit-actions"><button class="primary-button" disabled>提交</button><button class="secondary-button" disabled>提交并推送…</button><a class="icon-button" href="settings.html" aria-label="提交设置">${icon("settings")}</a></div></div></div></aside>`;
}

function quickOpenEmpty() {
  return `<div class="search-overlay"><div class="search-tabs"><strong>快速打开文件</strong><span class="grow"></span><span class="menu-shortcut">Ctrl+P</span></div><div class="search-query"><input class="search-field" value="" placeholder="输入文件名" aria-label="搜索内容"></div></div>`;
}

function projectContextMenu() {
  return `<section class="popover context-menu"><a class="menu-item" href="main-project.html">${icon("copy")} 复制路径</a><a class="menu-item" href="main-project.html">${icon("external-link")} 在资源管理器中定位</a><a class="menu-item" href="terminal.html">${icon("square-terminal")} 在外部终端打开</a><div class="menu-separator"></div><a class="menu-item" href="main-project.html">${icon("refresh-cw")} 刷新</a><a class="menu-item" href="file-history.html">${icon("history")} 文件历史</a><a class="menu-item" href="blame.html">${icon("list-tree")} Blame</a></section>`;
}

function changesContextMenu() {
  return `<section class="popover context-menu changes-context"><a class="menu-item" href="commit-diff.html">${icon("git-compare-arrows")} 显示 Diff</a><a class="menu-item" href="rollback.html">${icon("undo-2")} 回滚…</a><div class="menu-separator"></div><a class="menu-item" href="file-history.html">${icon("history")} 文件历史</a><a class="menu-item" href="blame.html">${icon("list-tree")} Blame</a><div class="menu-separator"></div><a class="menu-item" href="commit-changes.html">${icon("copy")} 复制路径</a><a class="menu-item" href="commit-changes.html">${icon("external-link")} 在资源管理器中定位</a></section>`;
}

function gitLogContextMenu() {
  return `<section class="popover context-menu log-context"><a class="menu-item" href="git-history.html">${icon("copy")} 复制提交哈希</a><a class="menu-item" href="operation-progress.html">${icon("cherry")} Cherry-pick</a><div class="menu-separator"></div><a class="menu-item" href="git-compare.html">${icon("none")} 与工作区比较</a><a class="menu-item" href="reset.html">${icon("undo-2")} Reset 当前分支到此处…</a><a class="menu-item" href="operation-progress.html">${icon("none")} Revert Commit</a><div class="menu-separator"></div><a class="menu-item" href="branches.html">${icon("none")} 新建分支…</a><a class="menu-item" href="branches.html">${icon("none")} 新建标签…</a></section>`;
}

function managementPage(kind) {
  const configs = {
    stash: ["Stash", ["stash@{0} 工作区切换前", "stash@{1} 调整安装器"], `<h2>stash@{0} · 工作区切换前</h2><p class="commit-meta">main · I49 · 2 分钟前</p><div class="button-row" style="justify-content:flex-start"><button class="primary-button">应用</button><button class="secondary-button">弹出</button><button class="secondary-button">查看内容</button><button class="danger-button">删除</button></div><h3>包含 3 个文件</h3><div class="tree-row"><span>${fileTypeIcon("NativeGitPanel.cs")}</span> NativeGitPanel.cs</div><div class="tree-row"><span>${fileTypeIcon("product-spec.md")}</span> product-spec.md</div>`],
    worktrees: ["Worktree", ["main · D:\\github\\Augit", "feature/ux · D:\\github\\Augit-ux"], `<h2>feature/ux</h2><div class="form-grid"><span>路径</span><span>D:\\github\\Augit-ux</span><span>状态</span><span class="file-status-new">干净，可安全移除</span><span>终端会话</span><span>无运行中的内置终端</span></div><div class="button-row" style="justify-content:flex-start"><button class="primary-button">打开窗口</button><button class="secondary-button">新建 Worktree</button><button class="danger-button">移除…</button></div>`],
    remote: ["远端", ["origin", "backup"], `<h2>origin</h2><div class="form-grid"><label>名称</label><input class="text-field" value="origin"><label>获取 URL</label><input class="text-field" value="https://example.com/team/Augit.git"><label>推送 URL</label><input class="text-field" value="https://example.com/team/Augit.git"></div><div class="button-row"><button class="secondary-button">删除</button><button class="primary-button">保存</button></div>`],
  };
  const [title, entries, detail] = configs[kind];
  const selectedIndex = kind === "worktrees" ? 1 : 0;
  return `<div class="history-page"><div class="toolbar"><button class="toolbar-button">${icon("plus")}</button><button class="toolbar-button">${icon("trash-2")}</button><button class="toolbar-button">${icon("refresh-cw")}</button><span class="toolbar-separator"></span><strong>${title} 管理</strong></div><div class="management-content"><div class="management-list">${entries.map((entry, index) => `<div class="tree-row ${index === selectedIndex ? "selected" : ""}">${icon(kind === "worktrees" ? "folder-git-2" : kind === "remote" ? "cloud" : "archive")}<span class="tree-name">${entry}</span></div>`).join("")}</div><div class="management-detail">${detail}</div></div></div>`;
}

// 外壳注入真实数据时的管理页：结构、图标与样式与样例版一致。
function liveManagementPage(kind) {
  const live = window.__augitLive || {};
  const configs = {
    remote: () => {
      const remotes = (live.remotes && live.remotes.remotes) || [];
      const entries = remotes.map(remote => remote.name);
      const first = remotes[0];
      const detail = first ? `<h2>${escapeHtml(first.name)}</h2><div class="form-grid"><label>名称</label><input class="text-field" value="${escapeHtml(first.name)}" readonly><label>获取 URL</label><input class="text-field" value="${escapeHtml(first.fetchUrl)}" readonly><label>推送 URL</label><input class="text-field" value="${escapeHtml(first.pushUrl)}" readonly></div>` : `<p class="commit-meta">没有配置远端</p>`;
      return ["远端", entries, detail];
    },
    worktrees: () => {
      const worktrees = (live.worktrees && live.worktrees.worktrees) || [];
      const entries = worktrees.map(worktree => `${worktree.branch || "(detached)"} · ${worktree.path}`);
      const first = worktrees[0];
      const detail = first ? `<h2>${escapeHtml(first.branch || "(detached)")}</h2><div class="form-grid"><span>路径</span><span>${escapeHtml(first.path)}</span><span>状态</span><span>${first.isLocked ? "已锁定" : first.isPrunable ? "可清理" : "干净，可安全移除"}</span></div>` : `<p class="commit-meta">没有 Worktree</p>`;
      return ["Worktree", entries, detail];
    },
    stash: () => {
      const stashes = (live.stashes && live.stashes.stashes) || [];
      const entries = stashes.map(stash => `${stash.reference} ${stash.message}`);
      const first = stashes[0];
      const detail = first ? `<h2>${escapeHtml(first.reference)} · ${escapeHtml(first.message)}</h2><p class="commit-meta">${escapeHtml(first.branch)} · ${escapeHtml(first.date)}</p>` : `<p class="commit-meta">没有 Stash</p>`;
      return ["Stash", entries, detail];
    },
  };
  const build = configs[kind];
  if (!build) return managementPage(kind);
  const [title, entries, detail] = build();
  const iconName = kind === "worktrees" ? "folder-git-2" : kind === "remote" ? "cloud" : "archive";
  const list = entries.length === 0
    ? `<p class="commit-meta">空</p>`
    : entries.map((entry, index) => `<div class="tree-row ${index === 0 ? "selected" : ""}">${icon(iconName)}<span class="tree-name">${escapeHtml(entry)}</span></div>`).join("");
  return `<div class="history-page"><div class="toolbar"><button class="toolbar-button">${icon("plus")}</button><button class="toolbar-button">${icon("trash-2")}</button><button class="toolbar-button">${icon("refresh-cw")}</button><span class="toolbar-separator"></span><strong>${escapeHtml(title)} 管理</strong></div><div class="management-content"><div class="management-list">${list}</div><div class="management-detail">${detail}</div></div></div>`;
}

// 外壳注入真实冲突文档时使用；三栏结构与样例版一致，结果栏是唯一可编辑区域。
function liveConflictResolver() {
  const live = window.__augitLive || {};
  const document_ = live.conflict;
  if (!document_) return conflictResolver();
  const lines = (text) => String(text ?? "").replace(/\r\n?/g, "\n").split("\n");
  const render = (items, extraClass) => items.map((text, index) =>
    `<span class="conflict-line${extraClass ? ` ${extraClass}` : ""}" data-line="${index + 1}">${escapeHtml(text) || "&nbsp;"}</span>`).join("");

  const yoursLines = lines(document_.yoursText);
  const theirsLines = lines(document_.theirsText);
  const resultLines = lines(document_.resultText);
  // 冲突块在结果正文里的行范围，用于标出「未处理冲突」。
  const marked = new Set();
  for (const block of document_.blocks || []) {
    for (let index = block.start; index < block.start + block.length; index += 1) {
      marked.add(index);
    }
  }

  const column = (title, body, editable = false) => `<section class="conflict-column${editable ? " result" : ""}"><div class="conflict-column-title" title="${escapeHtml(title)}">${escapeHtml(title)}</div><div class="conflict-block"${editable ? ' contenteditable="plaintext-only" role="textbox" aria-label="最终结果" aria-multiline="true" spellcheck="false"' : ""}>${body}</div></section>`;
  const count = (document_.blocks || []).length;
  return `<div class="conflict-page">
    <div class="conflict-header"><strong title="${escapeHtml(document_.path)}">解决冲突 · ${escapeHtml(document_.path.split("/").at(-1))} · ${escapeHtml(document_.operation)}</strong><span class="grow"></span><span class="commit-meta">${count} 个未处理冲突</span><button class="secondary-button">上一处</button><button class="secondary-button">下一处</button></div>
    <div class="conflict-columns">${column(document_.yoursLabel, render(yoursLines, ""))}${column("最终结果 · 可编辑", resultLines.map((text, index) => `<span class="conflict-line${marked.has(index) ? " conflict-result" : ""}" data-line="${index + 1}">${escapeHtml(text) || "&nbsp;"}</span>`).join(""), true)}${column(document_.theirsLabel, render(theirsLines, "conflict-side"))}</div>
    <div class="conflict-footer"><div class="conflict-accept-actions"><button class="secondary-button">接受左侧</button><button class="secondary-button">接受两侧</button><button class="secondary-button">接受右侧</button></div><div class="conflict-save-actions"><button class="secondary-button">取消</button><button class="primary-button" type="button" data-conflict-save>应用并标记已解决</button></div></div>
  </div>`;
}

// 外壳注入真实设置时使用；结构与样例版一致，字段值来自设置文件。
function settingsBodyFallback() {
  return `<div class="settings-layout"><nav class="settings-nav"><input class="search-field" placeholder="搜索设置" style="width:100%;margin-bottom:10px"><div class="tree-row selected">外观与行为</div><div class="tree-row depth-1">外观</div><div class="tree-row">文件查看</div><div class="tree-row">Git</div><div class="tree-row">终端</div></nav><div class="settings-page"><h2>外观</h2><section class="settings-group"><div class="form-grid"><label>主题</label><select class="select-field"><option>跟随 Windows</option></select><label></label><span class="commit-meta">主题覆盖主界面和预览；字体设置只改变显示，不会修改文件。</span></div></section><section class="settings-group"><div class="form-grid"><label>界面字体</label><div class="font-setting"><input class="text-field" value="Microsoft YaHei UI"><label for="ui-font-size">字号</label><input id="ui-font-size" type="number" min="9" max="40" class="text-field" value="13"></div><label>等宽字体</label><div class="font-setting"><input class="text-field" value="Cascadia Mono"><label for="code-font-size">字号</label><input id="code-font-size" type="number" min="9" max="40" class="text-field" value="13"></div><label></label><span class="commit-meta">字体只改变显示，不会修改文件。</span></div></section><section class="settings-group"><p class="commit-meta">启动时恢复上次打开的目录和标签。</p></section></div></div>`;
}

function liveSettingsBody() {
  const settings = (window.__augitLive && window.__augitLive.settings) || null;
  if (!settings) return settingsBodyFallback();
  const themeOption = (value, label) => `<option value="${value}"${settings.theme === value ? " selected" : ""}>${label}</option>`;
  const shellOption = (value, label) => `<option value="${value}"${settings.terminalShell === value ? " selected" : ""}>${label}</option>`;
  return `<div class="settings-layout live-settings"><nav class="settings-nav"><input class="search-field" placeholder="搜索设置" style="width:100%;margin-bottom:10px"><div class="tree-row selected">外观与行为</div><div class="tree-row depth-1">外观</div><div class="tree-row">文件查看</div><div class="tree-row">Git</div><div class="tree-row">终端</div></nav><div class="settings-page"><h2>外观</h2><section class="settings-group"><div class="form-grid"><label>主题</label><select class="select-field" data-setting="theme">${themeOption("System", "跟随 Windows")}${themeOption("Light", "浅色")}${themeOption("Dark", "深色")}</select><label></label><span class="commit-meta">主题覆盖主界面和预览；字体设置只改变显示，不会修改文件。</span></div></section><section class="settings-group"><div class="form-grid"><label>界面字体</label><div class="font-setting"><input class="text-field" data-setting="textFontFamily" value="${escapeHtml(settings.textFontFamily)}"><label for="ui-font-size">字号</label><input id="ui-font-size" type="number" min="9" max="40" class="text-field" data-setting="fontSize" value="${escapeHtml(String(settings.fontSize))}"></div><label>等宽字体</label><div class="font-setting"><input class="text-field" data-setting="monospaceFontFamily" value="${escapeHtml(settings.monospaceFontFamily)}"><label for="code-font-size">字号</label><input id="code-font-size" type="number" min="9" max="40" class="text-field" data-setting="codeFontSize" value="${escapeHtml(String(settings.codeFontSize))}"></div><label></label><span class="commit-meta">字体只改变显示，不会修改文件。</span></div></section><section class="settings-group"><h2>终端</h2><div class="form-grid"><label for="terminal-shell">Shell</label><select id="terminal-shell" class="select-field" data-setting="terminalShell">${shellOption("WindowsPowerShell", "Windows PowerShell")}${shellOption("PowerShell7", "PowerShell 7")}${shellOption("CommandPrompt", "CMD")}${shellOption("GitBash", "Git Bash")}${shellOption("Wsl", "WSL")}${shellOption("Custom", "自定义命令")}</select><label></label><span class="commit-meta">配置失效时会明确报错，不会静默切换 Shell。</span></div></section><section class="settings-group"><h2>Git</h2><div class="form-grid"><label for="git-path">git.exe</label><input id="git-path" class="text-field" data-setting="gitExecutablePath" value="${escapeHtml(settings.gitExecutablePath || "")}" placeholder="留空则从系统 PATH 查找"><label></label><span class="commit-meta">设置缺失时 Git 功能不可用，文件浏览仍可使用。</span></div></section><section class="settings-group"><h2>最近目录</h2><p class="commit-meta">共 ${(settings.recentWorkspaces || []).length} 个记录。</p></section></div></div>`;
}

// 外壳注入真实数据时的 Reset 对话框：目标提交取当前 HEAD，模式沿用规格说明。
function liveResetBody() {
  const live = window.__augitLive || {};
  const head = (live.history && live.history.head) ? live.history.head.slice(0, 7) : "HEAD";
  return `<div class="form-grid"><label for="reset-target">目标提交</label><input id="reset-target" class="text-field" value="${escapeHtml(head)}" readonly><label for="reset-mode">模式</label><select id="reset-mode" class="select-field"><option>Soft · 仅移动 HEAD</option><option>Mixed · 同时重置索引</option><option selected>Hard · 重置索引和工作区</option></select></div><div class="inline-alert reset-impact danger"><strong></strong><p class="commit-meta"></p></div><div class="reset-notice" role="status" hidden></div>`;
}

// 外壳注入真实改动文件时的回滚对话框：路径来自当前选中的改动文件。
function liveRollbackBody() {
  const live = window.__augitLive || {};
  const files = (live.status && live.status.files) || [];
  const file = files.find((item) => item.group === "Changes") || files[0] || null;
  if (!file) {
    return `<div class="inline-alert danger rollback-impact"><strong>没有可回滚的改动</strong><p class="commit-meta">当前工作区没有已跟踪文件的改动。</p></div>`;
  }

  return `<div class="inline-alert danger rollback-impact"><strong>将丢失此文件的全部本地改动</strong><p class="commit-meta">回滚完整文件，不能只回滚选中的差异块。</p><p class="rollback-recycle" hidden>未跟踪或新增文件将移入 Windows 回收站。</p></div><div class="form-grid"><span>文件</span><span>${escapeHtml(file.path)}</span><span>变更</span><span class="live-file-status-${escapeHtml(file.kind)}">${escapeHtml(file.kind)}</span></div>`;
}

// 外壳注入真实差异时使用：按「旧行 / 行号槽 / 新行」三列渲染结构化行。
function liveDiffView() {
  const live = window.__augitLive || {};
  const diff = live.diff;
  if (!diff) return diffView();
  const rows = diff.rows || [];
  // 文件栏的双方引用（规格 §7.8/§7.9）：
  // 历史比较显示 `<hash>^ → <hash>`，引用比较显示该引用 → 工作区，其余按 HEAD → 工作区。
  const comparison = live.historyComparison;
  const historyActive = comparison && comparison.status !== "closed"
    && comparison.path === diff.path && comparison.commit;
  const barSource = historyActive ? `${String(comparison.commit).slice(0, 8)}^` : "HEAD";
  const barTarget = historyActive ? String(comparison.commit).slice(0, 8) : "工作区";
  const barFilebar = diffFileHeader(barSource, barTarget, diff.path, diff.path);
  if (rows.length === 0) {
    const reason = diff.status && diff.status !== "Ready"
      ? `该文件无法显示文本差异（${escapeHtml(diff.status)}）。`
      : "该文件当前没有文本差异。";
    return `<div class="diff-layout"><div class="diff-toolbar"><button class="toolbar-button" aria-label="上一处差异">${icon("arrow-up")}</button><button class="toolbar-button" aria-label="下一处差异">${icon("arrow-down")}</button><span class="grow"></span><span>${reason}</span><div class="segmented"><button class="segment active" aria-label="双栏">${icon("diff-side-by-side")}</button><button class="segment" aria-label="单栏">${icon("diff-unified")}</button></div></div>${barFilebar}<div class="diff-columns"><div class="diff-side"></div><div class="diff-gutter"></div><div class="diff-side"></div></div></div>`;
  }

  // 差异行内的字符级高亮：把 span 区间切成普通片段与标记片段。
  const marked = (text, spans) => {
    const source = String(text ?? "");
    if (!spans || spans.length === 0) return escapeHtml(source) || "&nbsp;";
    let cursor = 0;
    let html = "";
    for (const span of [...spans].sort((a, b) => a.start - b.start)) {
      const start = Math.max(0, Math.min(span.start, source.length));
      const end = Math.max(start, Math.min(span.start + span.length, source.length));
      if (start > cursor) html += escapeHtml(source.slice(cursor, start));
      html += `<mark>${escapeHtml(source.slice(start, end))}</mark>`;
      cursor = end;
    }

    if (cursor < source.length) html += escapeHtml(source.slice(cursor));
    return html || "&nbsp;";
  };
  const cssKind = (kind) => kind === "Added" ? "added" : kind === "Removed" ? "removed" : kind === "Modified" ? "changed" : "";
  const cell = (row, side) => {
    const kind = row.kind === "Context" ? "" : cssKind(row.kind);
    const lineNumber = side === "old" ? row.oldLine : row.newLine;
    const text = side === "old" ? row.oldText : row.newText;
    const spans = side === "old" ? row.oldChanges : row.newChanges;
    const content = text === null || text === undefined
      ? "&nbsp;"
      : marked(text, spans);
    return { kind, lineNumber, content };
  };
  // 元数据行（diff --git / index / --- / +++）是补丁头部，不是内容，必须剔除；
  // 否则差异区看起来像在转储原始补丁，而不是一份差异。
  const content = rows.filter(row => row.kind !== "Metadata");
  const isSeparator = (row) => row.kind === "HunkHeader";
  const cellOf = (row, side) => {
    if (isSeparator(row)) return { kind: "hunk", lineNumber: null, content: escapeHtml(row.newText || row.oldText || "") };
    return cell(row, side);
  };
  const oldCells = content.map(row => cellOf(row, "old"));
  const newCells = content.map(row => cellOf(row, "new"));
  const oldSide = content.map((row, index) => `<div class="diff-code-line ${oldCells[index].kind}" data-line="${oldCells[index].lineNumber ?? ""}">${oldCells[index].content}</div>`).join("");
  const newSide = content.map((row, index) => `<div class="diff-code-line ${newCells[index].kind}" data-line="${newCells[index].lineNumber ?? ""}">${newCells[index].content}</div>`).join("");
  const gutter = content.map((row, index) => `<div>${oldCells[index].lineNumber ?? ""}</div><div>${newCells[index].lineNumber ?? ""}</div>`).join("");
  // 单栏视图：同一份补丁的另一种排版，切换时不查询 Git（规格 §6.3）。
  const unified = content.map((row, index) => {
    const kind = row.kind === "HunkHeader" ? "hunk"
      : row.kind === "Added" ? "added"
        : row.kind === "Removed" ? "removed"
          : row.kind === "Modified" ? "changed" : "";
    const lineNumber = newCells[index].lineNumber ?? oldCells[index].lineNumber ?? "";
    const text = newCells[index].content !== "&nbsp;" ? newCells[index].content : oldCells[index].content;
    return `<div class="diff-code-line ${kind}" data-line="${lineNumber}">${text}</div>`;
  }).join("");
  const unifiedTemplate = `<template class="diff-unified-template"><div class="diff-code-line hunk">${escapeHtml(diff.path)}</div>${unified}</template>`;
  return `<div class="diff-layout"><div class="diff-toolbar"><button class="toolbar-button" aria-label="上一处差异">${icon("arrow-up")}</button><button class="toolbar-button" aria-label="下一处差异">${icon("arrow-down")}</button><span class="grow"></span><span>${content.length} 行</span><div class="segmented"><button class="segment active" aria-label="双栏">${icon("diff-side-by-side")}</button><button class="segment" aria-label="单栏">${icon("diff-unified")}</button></div></div>${barFilebar}${unifiedTemplate}<div class="diff-columns"><div class="diff-side">${oldSide}</div><div class="diff-gutter">${gutter}</div><div class="diff-side">${newSide}</div></div></div>`;
}

// 外壳注入真实设置时的 Clone 表单：目标目录用最近目录预填，浅克隆默认不勾选且深度禁用。
function liveCloneBody() {
  const settings = (window.__augitLive && window.__augitLive.settings) || {};
  const recent = (settings.recentWorkspaces || [])[0] || "";
  return `<div class="form-grid"><label for="clone-version">版本控制</label><select id="clone-version" class="select-field"><option>Git</option></select><label for="clone-source">仓库 URL</label><input id="clone-source" class="text-field" placeholder="https://example.com/team/repository.git" autofocus><label for="clone-destination">目录</label><input id="clone-destination" class="text-field" placeholder="D:\\projects\\repository" value="${escapeHtml(recent)}"><span></span><div class="clone-shallow-row"><label class="check-line"><input id="clone-shallow" type="checkbox">浅克隆，历史截断为</label><div class="clone-depth-group disabled"><input id="clone-depth" class="text-field" value="1" inputmode="numeric" aria-label="浅克隆深度" disabled><span>个提交</span></div></div></div><div class="clone-notice" role="status" hidden></div>`;
}

function conflictResolver() {
  const code = lines => lines.map(([text, kind]) => kind === "gap"
    ? `<span class="conflict-spacer" aria-hidden="true" style="height:calc(${text} * var(--conflict-line-height, 1.7em))"></span>`
    : `<span class="conflict-line${kind ? ` ${kind}` : ""}">${text || "&nbsp;"}</span>`).join("");
  let left = code([["public async Task RefreshAsync()"], ["{"], ["    var snapshot = await LoadStatusAsync();", "conflict-side"], ["    if (snapshot != _snapshot)", "conflict-side"], ["        ShowSelectedDiff();", "conflict-side"], [2, "gap"], ["}"]]);
  let result = code([["public async Task RefreshAsync()"], ["{"], ["    var snapshot = await LoadStatusAsync();", "conflict-result"], ["    if (snapshot != _snapshot)", "conflict-result"], ["        ShowSelectedDiff();", "conflict-result"], ["    var status = await LoadStatusAsync();", "conflict-result"], ["    Render(status);", "conflict-result"], ["}"]]);
  let right = code([["public async Task RefreshAsync()"], ["{"], ["    var status = await LoadStatusAsync();", "conflict-side"], ["    Render(status);", "conflict-side"], [3, "gap"], ["}"]]);
  const emptySide = new URLSearchParams(location.search).get("empty-side");
  if (emptySide === "left" || emptySide === "right") {
    const common = [["public async Task RefreshAsync()"], ["{"], ["    await LoadStatusAsync();"], ["}"]];
    const inserted = kind => [["// 新增说明一", kind], ["// 新增说明二", kind], ...common];
    left = code(emptySide === "left" ? [[2, "gap"], ...common] : inserted("conflict-side"));
    right = code(emptySide === "right" ? [[2, "gap"], ...common] : inserted("conflict-side"));
    result = code(inserted("conflict-result"));
  }
  const column = (title, body, editable = false) => `<section class="conflict-column${editable ? " result" : ""}"><div class="conflict-column-title" title="${title}">${title}</div><div class="conflict-block"${editable ? ' contenteditable="plaintext-only" role="textbox" aria-label="最终结果" aria-multiline="true" spellcheck="false"' : ""}>${body}</div></section>`;
  return `<div class="conflict-page">
    <div class="conflict-header"><strong title="src/Augit.App/NativeGitPanel.cs">解决冲突 · NativeGitPanel.cs</strong><span class="grow"></span><span class="commit-meta">1 个未处理冲突</span><button class="secondary-button">上一处</button><button class="secondary-button">下一处</button></div>
    <div class="conflict-columns">${column("当前分支 · main", left)}${column("最终结果 · 可编辑", result, true)}${column("合入内容", right)}</div>
    <div class="conflict-footer"><div class="conflict-accept-actions"><button class="secondary-button">接受左侧</button><button class="secondary-button">接受两侧</button><button class="secondary-button">接受右侧</button></div><div class="conflict-save-actions"><button class="secondary-button">取消</button><a class="primary-button" href="conflict-list.html">应用并标记已解决</a></div></div>
  </div>`;
}

/* 第二轮共享骨架：只复用 PyCharm 的界面组织与交互语言，不复制品牌资产。 */
function titlebar() {
  return `
    <header class="titlebar">
      <div class="brand-mark" aria-label="Augit">A</div>
      <button class="top-button" aria-label="主菜单" data-action="menu">${icon("menu")}</button>
      <a class="top-chip workspace-chip" href="workspace-open.html" title="切换工作区"><span class="brand-mark">A</span> ${escapeHtml((window.__augitLive && window.__augitLive.workspaceName) || "Augit")} ${icon("chevron-down")}</a>
      <a class="top-chip branch-chip" href="branches.html" title="分支与标签">${icon("git-branch")} ${escapeHtml((window.__augitLive && window.__augitLive.branch) || "main")} ${icon("chevron-down")}</a>
      <span></span>
      <a class="titlebar-context" href="quick-open.html" title="快速打开文件">${escapeHtml((window.__augitLive && window.__augitLive.document && window.__augitLive.document.name) || "当前文件")} ${icon("chevron-down")}</a>
      <span></span>
      <nav class="window-actions" aria-label="窗口工具">
        <a class="top-button" href="quick-open.html" aria-label="搜索">${icon("search")}</a>
        <a class="top-button" href="settings.html" aria-label="设置">${icon("settings")}</a>
        ${windowActionIcons()}
      </nav>
    </header>`;
}

function rail(active) {
  return `
    <nav class="tool-rail" aria-label="工具窗口">
      <a class="rail-button ${active === "project" ? "active" : ""}" href="main-project.html" aria-label="项目">${icon("folder")}</a>
      <a class="rail-button ${active === "commit" ? "active" : ""}" href="commit-changes.html" aria-label="提交">${icon("git-commit-horizontal")}</a>
      <a class="rail-button ${active === "search" ? "active" : ""}" href="repository-search.html" aria-label="搜索">${icon("search")}</a>
      <div class="rail-spacer"></div>
      <a class="rail-button ${active === "terminal" ? "active" : ""}" href="terminal.html" aria-label="终端">${icon("square-terminal")}</a>
      <a class="rail-button ${active === "history" ? "active" : ""}" href="git-history.html" aria-label="Git 历史">${icon("git-branch")}</a>
    </nav>`;
}

// 外壳注入真实工作区数据时使用：结构与样例树一致，保证同一套 CSS 与交互绑定。
function liveProjectTree(selected, live) {
  const rows = live.tree.map((entry) => {
    const depth = entry.depth === 0 ? "root-row" : `depth-${entry.depth}`;
    const hasChildren = entry.isDirectory && entry.hasChildren;
    const chevron = hasChildren ? (entry.expanded ? "chevron-down" : "chevron-right") : "";
    const expanded = entry.expanded ? "true" : "false";
    return `<div class="tree-row ${depth} ${entry.name === selected ? "selected" : ""}" data-tree-path="${escapeHtml(entry.path)}" data-tree-directory="${entry.isDirectory}" role="treeitem" aria-level="${entry.depth + 1}" aria-expanded="${entry.isDirectory ? expanded : "false"}" tabindex="-1"><span class="chevron">${chevron ? icon(chevron) : ""}</span><span class="${entry.isDirectory ? "folder-icon" : "file-icon"}">${entry.isDirectory ? treeFolderIcon(entry.depth === 0) : fileTypeIcon(entry.name)}</span><span class="tree-name">${escapeHtml(entry.name)}</span>${entry.depth === 0 && live.root ? `<span class="tree-path">${escapeHtml(live.root)}</span>` : ""}</div>`;
  }).join("");
  return `
    <aside class="tool-window side-tool">
      <div class="tool-header"><span>项目</span><span>${icon("chevron-down")}</span><span class="grow"></span><span class="header-actions"><button class="icon-button" aria-label="定位当前文件">${icon("locate-fixed")}</button><button class="icon-button" aria-label="折叠项目树">${icon("fold-vertical")}</button><button class="icon-button" aria-label="更多">${icon("ellipsis-vertical")}</button><button class="icon-button" aria-label="最小化">${icon("minus")}</button></span></div>
      <div class="side-content tree" role="tree" aria-label="项目文件">
        ${rows}
      </div>
    </aside>`;
}

function projectTree(selected = "product-spec.md", liveRows = null) {
  if (liveRows) return liveProjectTree(selected, liveRows);
  const rows = [
    ["root-row", "chevron-down", "folder", "Augit", "D:\\github\\Augit", ""],
    ["depth-1", "chevron-right", "folder", "artifacts", "", ""],
    ["depth-1", "chevron-down", "folder", "docs", "", ""],
    ["depth-2", "chevron-right", "folder", "ux-mockups", "", ""],
    ["depth-2", "", "file-text", "architecture.md", "", "markdown-preview.html"],
    ["depth-2", "", "file-text", "performance-report.md", "", "markdown-preview.html"],
    ["depth-2", "", "file-text", "product-spec.md", "", "markdown-preview.html"],
    ["depth-2", "", "file-text", "roadmap.md", "", "markdown-preview.html"],
    ["depth-2", "", "file-text", "runtime-dependencies.md", "", "markdown-preview.html"],
    ["depth-2", "", "file-text", "ux-spec.md", "", "markdown-preview.html"],
    ["depth-1", "chevron-right", "folder", "licenses", "", ""],
    ["depth-1", "chevron-down", "folder", "src", "", ""],
    ["depth-2", "chevron-down", "folder", "Augit.App", "", ""],
    ["depth-3", "chevron-right", "folder", "bin", "", ""],
    ["depth-3", "chevron-right", "folder", "obj", "", ""],
    ["depth-3", "", "file-code-2", "app.manifest", "", "text-viewer.html"],
    ["depth-3", "", "file-code-2", "AssemblyInfo.cs", "", "text-viewer.html"],
    ["depth-3", "", "file-code-2", "MainWindow.cs", "", "text-viewer.html"],
    ["depth-3", "", "file-code-2", "NativeGitPanel.cs", "", "text-viewer.html"],
    ["depth-1", "", "file-code-2", "global.json", "", "json-preview.html"],
  ];
  return `
    <aside class="tool-window side-tool">
      <div class="tool-header"><span>项目</span><span>${icon("chevron-down")}</span><span class="grow"></span><span class="header-actions"><button class="icon-button" aria-label="定位当前文件">${icon("locate-fixed")}</button><button class="icon-button" aria-label="折叠项目树">${icon("fold-vertical")}</button><button class="icon-button" aria-label="更多">${icon("ellipsis-vertical")}</button><button class="icon-button" aria-label="最小化">${icon("minus")}</button></span></div>
      <div class="side-content tree">
        ${rows.map(([depth, chevron, fileIcon, name, path, href]) => `${href ? `<a href="${href}"` : `<div`} class="tree-row ${depth} ${name === selected ? "selected" : ""}"><span class="chevron">${chevron ? icon(chevron) : ""}</span><span class="${fileIcon === "folder" ? "folder-icon" : "file-icon"}">${fileIcon === "folder" ? treeFolderIcon(depth === "root-row") : fileTypeIcon(name)}</span><span class="tree-name">${name}</span>${path ? `<span class="tree-path">${path}</span>` : ""}${href ? "</a>" : "</div>"}`).join("")}
      </div>
    </aside>`;
}

function commitMessageBox(disabled = false) {
  const state = disabled ? null : new URLSearchParams(location.search).get("commit-state");
  const error = state === "validation" ? "提交信息不能为空。"
    : state === "hook-failure" ? "commit-msg hook 拒绝提交。请检查仓库提交规则。" : "";
  return `<div class="commit-message-box"><div class="commit-feedback ${error ? "error" : ""}" role="status" title="${escapeHtml(error || "提交信息")}">${escapeHtml(error || "提交信息")}</div><textarea class="message-field" aria-label="提交信息" ${disabled ? "disabled" : ""}>${state === "hook-failure" ? "fix: 保留失败草稿" : ""}</textarea></div>`;
}

// 外壳注入真实 Git 状态时使用；结构与样例版一致，复用同一套样式与交互绑定。
function liveChangesSide(selected) {
  const status = window.__augitLive.status;
  const groups = [["Changes", "Changes"], ["UnversionedFiles", "Unversioned Files"]];
  // 无改动时复用样例版的空状态：文案与规格 §10.1 一致（「没有待提交的更改」），
  // 并保留提交工具窗口骨架（工具栏、提交框与禁用的动作）。
  if (status.files.length === 0) {
    return emptyChangesSide();
  }

  const changeRows = groups.map(([key, label]) => {
      const groupFiles = status.files.filter(file => file.group === key);
      if (groupFiles.length === 0) return "";
      const state = groupFiles.every(file => file.checked) ? "true" : groupFiles.some(file => file.checked) ? "mixed" : "false";
      const checkClass = state === "true" ? " checked" : state === "mixed" ? " mixed" : "";
      const rows = groupFiles.map(file => `
      <div class="check-row change-file-row ${selected === file.name ? "selected" : ""}" data-file="${escapeHtml(file.name)}" data-path="${escapeHtml(file.path)}" data-group="${escapeHtml(label)}" role="treeitem" aria-level="2" aria-selected="${selected === file.name}" data-change-path="${escapeHtml(file.path)}" tabindex="-1">
        <button class="fake-check${file.checked ? " checked" : ""}" type="button" role="checkbox" tabindex="-1" aria-checked="${file.checked}" aria-label="选择 ${escapeHtml(file.name)}"></button>
        <span class="file-icon">${fileTypeIcon(file.name)}</span><span class="tree-name live-file-status-${escapeHtml(file.kind)}">${escapeHtml(file.name)}</span><span class="tree-path">${escapeHtml(file.directory)}</span>
      </div>`).join("");
      return `<div class="check-row check-group-row" data-group="${escapeHtml(label)}" role="treeitem" aria-level="1" aria-expanded="true" aria-selected="false"><button class="change-chevron" type="button" tabindex="-1" aria-label="折叠 ${escapeHtml(label)}" aria-expanded="true">${icon("chevron-down")}</button><button class="fake-check${checkClass}" type="button" role="checkbox" tabindex="-1" aria-checked="${state}" aria-label="选择全部 ${escapeHtml(label)}"></button><strong>${escapeHtml(label)}</strong><span class="commit-meta">${groupFiles.length} 个文件</span></div>${rows}`;
    }).join("");
  const changed = status.files.filter(file => file.group === "Changes").length;
  return `
    <aside class="tool-window side-tool">
      <div class="tool-header"><span>提交</span><span class="grow"></span><span class="header-actions"><button class="icon-button" aria-label="更多">${icon("ellipsis-vertical")}</button><button class="icon-button" aria-label="最小化">${icon("minus")}</button></span></div>
      <div class="changes-layout">
        <div class="toolbar"><button class="toolbar-button" aria-label="刷新">${icon("refresh-cw")}</button><button class="toolbar-button" disabled title="回滚在当前上下文不可用。" aria-label="回滚">${icon("undo-2")}</button><button class="toolbar-button" data-action="show-change-diff" aria-label="显示 Diff"${window.__augitLive && window.__augitLive.selectedChangePath ? "" : ' disabled title="先在改动列表里选择一个文件。"'}>${icon("git-compare-arrows")}</button><button class="toolbar-button" aria-label="展开全部">${icon("download")}</button><button class="toolbar-button" aria-label="预览">${icon("eye")}</button></div>
        <div class="changes-list" role="tree" aria-label="待提交文件" tabindex="0">
          ${changeRows}
        </div>
        <div class="commit-box">
          <div class="commit-options"><span class="commit-amend"><button class="fake-check" type="button" role="checkbox" aria-checked="false" aria-label="Amend"></button><span>Amend</span></span><span class="commit-last"><span>${escapeHtml(status.branch || "HEAD")}</span></span><span class="commit-count file-status-modified" title="${changed} modified">${changed} modified</span></div>
          ${commitMessageBox()}
          <div class="commit-actions"><a class="primary-button" href="operation-result.html">提交</a><a class="secondary-button" href="push.html">提交并推送…</a><span class="grow"></span><a class="icon-button" href="settings.html" aria-label="提交设置">${icon("settings")}</a></div>
        </div>
      </div>
    </aside>`;
}

function changesSide(selected = "app.manifest") {
  const groups = ["Changes", "Unversioned Files"];
  const changeRows = groups.map(group => {
    const groupFiles = changesSamples.filter(file => file.group === group);
    const checked = groupFiles.filter(file => file.checked).length;
    const state = checked === 0 ? "false" : checked === groupFiles.length ? "true" : "mixed";
    const checkClass = state === "true" ? " checked" : state === "mixed" ? " mixed" : "";
    const rows = groupFiles.map(file => `
      <div class="check-row change-file-row ${selected === file.name ? "selected" : ""}" data-file="${escapeHtml(file.name)}" data-path="${escapeHtml(file.path)}" data-group="${group}" role="treeitem" aria-level="2" aria-selected="${selected === file.name}" id="change-file-${changesSamples.indexOf(file)}" tabindex="-1">
        <button class="fake-check${file.checked ? " checked" : ""}" type="button" role="checkbox" tabindex="-1" aria-checked="${file.checked}" aria-label="选择 ${escapeHtml(file.name)}"></button>
        <span class="file-icon">${fileTypeIcon(file.name)}</span><span class="tree-name file-status-modified">${escapeHtml(file.name)}</span><span class="tree-path">${escapeHtml(file.directory)}</span>
      </div>`).join("");
    return `<div class="check-row check-group-row" data-group="${group}" role="treeitem" aria-level="1" aria-expanded="true" aria-selected="false" id="change-group-${groups.indexOf(group)}"><button class="change-chevron" type="button" tabindex="-1" aria-label="折叠 ${group}" aria-expanded="true">${icon("chevron-down")}</button><button class="fake-check${checkClass}" type="button" role="checkbox" tabindex="-1" aria-checked="${state}" aria-label="选择全部 ${group}"></button><strong>${group}</strong><span class="commit-meta">${groupFiles.length} 个文件</span></div>${rows}`;
  }).join("");
  return `
    <aside class="tool-window side-tool">
      <div class="tool-header"><span>提交</span><span class="grow"></span><span class="header-actions"><button class="icon-button" aria-label="更多">${icon("ellipsis-vertical")}</button><button class="icon-button" aria-label="最小化">${icon("minus")}</button></span></div>
      <div class="changes-layout">
        <div class="toolbar"><button class="toolbar-button" aria-label="刷新">${icon("refresh-cw")}</button><button class="toolbar-button" aria-label="回滚">${icon("undo-2")}</button><button class="toolbar-button" data-action="show-change-diff" aria-label="显示 Diff">${icon("git-compare-arrows")}</button><button class="toolbar-button" aria-label="展开全部">${icon("download")}</button><button class="toolbar-button" aria-label="预览">${icon("eye")}</button></div>
        <div class="changes-list" role="tree" aria-label="待提交文件" tabindex="0">
          ${changeRows}
        </div>
        <div class="commit-box">
          <div class="commit-options"><span class="commit-amend"><button class="fake-check" type="button" role="checkbox" aria-checked="false" aria-label="Amend"></button><span>Amend</span></span><a class="commit-last file-status-modified" href="git-history.html" title="上一次提交"><span>上一次提交</span>${icon("history")}</a><span class="commit-count file-status-modified" title="${changesSamples.filter(file => file.group === "Changes").length} modified">${changesSamples.filter(file => file.group === "Changes").length} modified</span></div>
          ${commitMessageBox()}
          <div class="commit-actions"><a class="primary-button" href="operation-result.html">提交</a><a class="secondary-button" href="push.html">提交并推送…</a><span class="grow"></span><a class="icon-button" href="settings.html" aria-label="提交设置">${icon("settings")}</a></div>
        </div>
      </div>
    </aside>`;
}

function editorTabs(active, extra = "") {
  const live = window.__augitLive;
  // 实时外壳：标签来自 live.tabs（规格 §5.2）。临时预览标签用 preview 类区分，
  // 关闭叉与中键都走同一套“不抢焦点”的关闭流程。
  if (live && Array.isArray(live.tabs) && live.tabs.length > 0) {
    const markup = live.tabs.map(tab => {
      const cls = ["editor-tab"];
      if (tab.id === live.activeTabId) cls.push("active");
      if (tab.preview) cls.push("preview");
      if (tab.kind === "comparison") cls.push("comparison-tab");
      const label = tab.title || tab.path || "未命名";
      const glyph = tab.kind === "comparison" ? icon("git-compare-arrows") : fileTypeIcon(label);
      return `<a class="${cls.join(" ")}" href="#" data-tab-id="${escapeHtml(tab.id)}" title="${escapeHtml(label)}"${tab.id === live.activeTabId ? ' aria-current="true"' : ""}>${glyph} ${escapeHtml(label)}<span class="tab-close" role="button" aria-label="关闭标签">${icon("x")}</span></a>`;
    }).join("");
    return `
    <div class="editor-tabs">
      ${markup}
      <span style="flex:1"></span><button class="icon-button" aria-label="标签选项">${icon("ellipsis-vertical")}</button>
    </div>`;
  }

  if (live && live.document) {
    const name = live.document.name || live.document.path;
    return `
    <div class="editor-tabs">
      <a class="editor-tab active" href="#">${fileTypeIcon(name)} ${escapeHtml(name)}<span class="tab-close" aria-label="关闭文件">${icon("x")}</span></a>
      <span style="flex:1"></span><button class="icon-button" aria-label="标签选项">${icon("ellipsis-vertical")}</button>
    </div>`;
  }

  return `
    <div class="editor-tabs">
      <a class="editor-tab ${active === "third" ? "active" : ""}" href="text-viewer.html">${fileTypeIcon("THIRD-PARTY-NOTICES.md")} THIRD-PARTY-NOTICES.md</a>
      <a class="editor-tab ${active === "roadmap" ? "active" : ""}" href="markdown-preview.html">${fileTypeIcon("roadmap.md")} roadmap.md</a>
      <a class="editor-tab ${active === "product" ? "active" : ""}" href="markdown-preview.html">${fileTypeIcon("product-spec.md")} product-spec.md</a>
      ${extra}<span style="flex:1"></span><button class="icon-button" aria-label="标签选项">${icon("ellipsis-vertical")}</button>
    </div>`;
}

function markdownView(mode = "preview") {
  if (liveDocument()) return liveMarkdownDocument(new URLSearchParams(location.search).get('markdown-mode') || mode);
  const params = new URLSearchParams(location.search);
  mode = params.get('markdown-mode') || mode;
  if (!['source', 'split', 'preview'].includes(mode)) mode = 'preview';
  const sections = [
    ['1. 产品定位', 'Augit 是供外部 AI 协作开发场景使用的轻量桌面工具。它不接入任何 AI，也不负责生成代码。', '轻量优先是 Augit 的最高产品原则。'],
    ['2. 文件浏览', '在项目树中选择文件，双击或按 Enter 打开只读标签。', '普通文件查看器不提供编辑和保存。'],
    ['3. 查看变更', 'Changes 展示文件变化与完整文件提交选择。', '打开 Diff 后在比较区域阅读磁盘真实文本变化。'],
    ['4. 阅读 Markdown', '从文件树打开时默认预览，可以切换原文和左右对照。', '切换模式保留阅读位置，文件链接在 Augit 只读标签中打开。'],
    ['5. 轻量与响应', '外部文件和本地 Git 状态变化及时反映。', '后台空闲时不持续扫描仓库，不明显占用 CPU。'],
  ];
  const sourceLines = ['# Augit 产品规格', '', ...sections.flatMap(([title, ...paragraphs]) => [`## ${title}`, '', ...paragraphs.flatMap(p => [p, ''])]), '[返回产品定位](#section-0)', '', '[UX 规格](ux-spec.md)', '', '[不可用资源](missing.md)'];
  const toolbar = `<div class="document-toolbar"><span class="document-path">Augit › docs › product-spec.md　只读</span><div class="segmented document-modes">${[['source','原文','document-source'],['split','左右对照','document-split'],['preview','预览','document-preview']].map(([value,label,glyph]) => `<button class="segment" data-markdown-mode="${value}" aria-label="${label}">${icon(glyph)}</button>`).join('')}</div><button class="icon-button" aria-label="更多">${icon('ellipsis-vertical')}</button></div>`;
  const source = `<div class="markdown-source code-view" tabindex="0" aria-label="Markdown 只读原文">${codeLines(sourceLines, 0)}</div>`;
  const preview = `<article class="markdown-preview" tabindex="0" aria-label="Markdown 预览正文"><h1>Augit 产品规格</h1>${sections.map(([title,...paragraphs],i) => `<h2 id="section-${i}">${title}</h2>${paragraphs.map(p => `<p>${p}</p>`).join('')}`).join('')}<p><a href="#section-0">返回产品定位</a></p><p><a href="markdown-preview.html">UX 规格</a></p><p><span class="markdown-blocked" title="missing.md">不可用资源（链接已阻止：路径不存在、越出工作区或协议不支持）</span></p></article>`;
  return `<div class="document-view markdown-document" data-markdown-mode="${mode}" data-markdown-state="${params.get('markdown-state') === 'failure' ? 'failure' : params.get('markdown-state') === 'loading' ? 'loading' : 'ready'}">${toolbar}<div class="markdown-panes">${source}<div class="markdown-divider" role="separator" aria-label="调整 Markdown 对照宽度" aria-orientation="vertical" tabindex="0"></div><div class="markdown-preview-region">${preview}<div class="markdown-feedback" role="status"></div></div></div></div>`;
}

function bindMarkdownModes() {
  bindMarkdownModes.dispose?.();
  const view = document.querySelector('.markdown-document');
  if (!view) return;
  const lifetime = new AbortController();
  // 容忍选择器未命中的情况：可选元素（例如预览区里的锚点）在实时数据下可能不存在，
  // 一个缺失元素不该让整条绑定链抛异常并中断后续渲染。
  const on = (element, type, handler, options = {}) => element && element.addEventListener(type, handler, { ...options, signal: lifetime.signal });
  const panes = view.querySelector('.markdown-panes');
  const source = view.querySelector('.markdown-source');
  const preview = view.querySelector('.markdown-preview');
  const divider = view.querySelector('.markdown-divider');
  const positions = new Map();
  let ratio = Number(panes.style.getPropertyValue('--markdown-source-ratio')) || .5;
  let drag = null;
  const setRatio = value => {
    const available = Math.max(0, panes.clientWidth - 6);
    if (!available) return;
    const minimum = Math.min(available / 2, 240);
    const width = Math.max(minimum, Math.min(available - minimum, Math.round(available * value)));
    const next = width / available;
    if (next === ratio && panes.style.getPropertyValue('--markdown-source-ratio')) return;
    ratio = next;
    panes.style.setProperty('--markdown-source-ratio', String(ratio));
    divider.setAttribute('aria-valuenow', String(Math.round(ratio * 100)));
  };
  const cancelDrag = () => {
    const previous = drag;
    drag = null;
    if (previous && divider.hasPointerCapture(previous.id)) divider.releasePointerCapture(previous.id);
  };
  bindMarkdownModes.dispose = () => { cancelDrag(); lifetime.abort(); };
  // 该绑定把监听挂在 document/window 上，所有者是本区域节点；登记以便区域替换时释放。
  registerRegionDisposer(bindMarkdownModes.dispose);
  const setMode = mode => {
    if (view.dataset.markdownMode === mode && view.dataset.markdownBound) return;
    if (mode !== 'split') cancelDrag();
    for (const element of [source, preview]) if (element.clientWidth) positions.set(element, [element.scrollLeft, element.scrollTop]);
    view.dataset.markdownMode = mode;
    view.dataset.markdownBound = 'true';
    view.querySelectorAll('button[data-markdown-mode]').forEach(button => {
      const active = button.dataset.markdownMode === mode;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', String(active));
    });
    for (const [element, [x,y]] of positions) if (element.clientWidth) element.scrollTo(x,y);
    view.dispatchEvent(new Event('document-content-changed'));
  };
  view.querySelectorAll('button[data-markdown-mode]').forEach(button => on(button, 'click', () => setMode(button.dataset.markdownMode)));
  on(divider, 'pointerdown', event => {
    if (event.button !== 0 || drag || view.dataset.markdownMode !== 'split' || !view.checkVisibility()) return;
    drag = { id: event.pointerId, offset: event.clientX - divider.getBoundingClientRect().left };
    divider.setPointerCapture(event.pointerId);
    event.preventDefault();
  });
  const moveDrag = event => {
    if (!drag || drag.id !== event.pointerId) return;
    if (!divider.hasPointerCapture(event.pointerId) || view.dataset.markdownMode !== 'split' || !view.checkVisibility()) {
      cancelDrag(); return;
    }
    const rect = panes.getBoundingClientRect();
    const available = panes.clientWidth - 6;
    if (available <= 480) return;
    const width = Math.max(Math.min(available / 2, 240), Math.min(available - Math.min(available / 2, 240),
      Math.round(event.clientX - rect.left - drag.offset)));
    if (Math.abs(width - source.getBoundingClientRect().width) < .5) return;
    setRatio(width / available);
  };
  on(divider, 'pointermove', moveDrag);
  on(divider, 'pointerup', event => {
    if (drag?.id !== event.pointerId) return;
    moveDrag(event); cancelDrag();
  });
  on(divider, 'pointercancel', event => { if (drag?.id === event.pointerId) cancelDrag(); });
  on(divider, 'lostpointercapture', event => { if (drag?.id === event.pointerId) drag = null; });
  on(window, 'blur', cancelDrag);
  on(document, 'visibilitychange', () => { if (document.hidden) cancelDrag(); });
  on(document, 'keydown', event => {
    if (event.key === 'Escape' && drag) { cancelDrag(); event.preventDefault(); event.stopImmediatePropagation(); }
  }, { capture: true });
  on(divider, 'keydown', event => {
    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') { setRatio(ratio + (event.key === 'ArrowLeft' ? -.02 : .02)); event.preventDefault(); }
  });
  const feedback = view.querySelector('.markdown-feedback');
  feedback.textContent = view.dataset.markdownState === 'loading' ? '正在生成 Markdown 预览…' : view.dataset.markdownState === 'failure' ? '预览失败：文件读取失败。' : '';
  on(view.querySelector('.markdown-preview a[href^="#"]'), 'click', event => {
    event.preventDefault();
    preview.scrollTo(0, preview.querySelector('#section-0').offsetTop - preview.offsetTop - 24);
  });
  setRatio(ratio);
  setMode(view.dataset.markdownMode);
}

function measureCurrentFind() {
  const bar = document.querySelector('.current-find');
  if (!bar) return;
  const status = bar.querySelector('.find-status');
  const canvas = document.createElement('canvas').getContext('2d');
  canvas.font = getComputedStyle(bar).font;
  const width = Math.max(48, Math.ceil(canvas.measureText(status.textContent).width) + 4);
  bar.style.setProperty('--find-status-width', `${width}px`);
  bar.style.setProperty('--find-width', `${471 + width - 48}px`);
}

// 外壳打开真实文档时使用的视图构建器；没有真实文档时继续用视觉稿样例内容。
function liveDocument() {
  const live = window.__augitLive;
  return live && live.document ? live.document : null;
}

function liveLineViews(text) {
  const lines = String(text ?? "").replace(/\r\n?/g, "\n").split("\n");
  return lines.map((line, index) => `<div class="code-line"><span class="line-number">${index + 1}</span><span>${escapeHtml(line) || " "}</span></div>`).join("");
}

function liveTextDocument(find) {
  const document_ = liveDocument();
  const name = document_.name || document_.path;
  const findBox = find ? currentFindBox() : "";
  return `<div class="document-view"><div class="document-toolbar"><span class="document-path">${escapeHtml(document_.path)}\u3000只读</span><button class="icon-button" aria-label="自动换行">${icon("wrap-text")}</button><button class="icon-button" aria-label="显示空白">${icon("pilcrow")}</button><button class="icon-button" aria-label="当前文件搜索">${icon("search")}</button><button class="icon-button" aria-label="跳转行">${icon("corner-down-right")}</button></div>${findBox}<div class="code-view" tabindex="0" aria-label="${escapeHtml(name)} 只读正文">${liveLineViews(document_.text)}</div></div>`;
}

// 当前文件查找条：结构与视觉稿一致，初始为空状态。
function currentFindBox() {
  return `<div class="current-find"><input class="search-field" value="" aria-label="当前文件查找" placeholder="查找"><button class="icon-button" aria-label="区分大小写">${icon("case-sensitive")}</button><button class="icon-button" aria-label="全字匹配">${icon("whole-word")}</button><button class="icon-button" aria-label="正则表达式">${icon("regex")}</button><span class="find-status"></span><button class="icon-button" aria-label="上一项">${icon("chevron-up")}</button><button class="icon-button" aria-label="下一项">${icon("chevron-down")}</button><button class="icon-button" aria-label="关闭查找">${icon("x")}</button></div>`;
}

function liveMarkdownDocument(mode) {
  const document_ = liveDocument();
  const toolbar = `<div class="document-toolbar"><span class="document-path">${escapeHtml(document_.path)}\u3000只读</span><div class="segmented document-modes">${[['source','原文','document-source'],['split','左右对照','document-split'],['preview','预览','document-preview']].map(([value,label,glyph]) => `<button class="segment" data-markdown-mode="${value}" aria-label="${label}">${icon(glyph)}</button>`).join('')}</div><button class="icon-button" aria-label="更多">${icon('ellipsis-vertical')}</button></div>`;
  const source = `<div class="markdown-source code-view" tabindex="0" aria-label="Markdown 只读原文">${liveLineViews(document_.text)}</div>`;
  const preview = `<article class="markdown-preview" tabindex="0" aria-label="Markdown 预览正文">${document_.preview || ""}</article>`;
  return `<div class="document-view markdown-document" data-markdown-mode="${mode}" data-markdown-state="ready">${toolbar}<div class="markdown-panes">${source}<div class="markdown-divider" role="separator" aria-label="调整 Markdown 对照宽度" aria-orientation="vertical" tabindex="0"></div><div class="markdown-preview-region">${preview}<div class="markdown-feedback" role="status"></div></div></div></div>`;
}

function liveJsonDocument() {
  const document_ = liveDocument();
  const formatted = document_.formatted || document_.text || "";
  return `<div class="document-view json-document" data-json-mode="formatted"><div class="document-toolbar"><span class="document-path">${escapeHtml(document_.path)}\u3000只读</span><div class="segmented document-modes"><button class="segment" aria-label="原文" data-json-mode="source">${icon("document-source")}</button><button class="segment" aria-label="格式化" data-json-mode="formatted">${icon("document-formatted")}</button></div><button class="icon-button" aria-label="更多">${icon("ellipsis-vertical")}</button></div><div class="code-view" tabindex="0" aria-label="JSON 只读正文" data-json-source="${escapeHtml(document_.text || "")}">${liveLineViews(formatted)}</div></div>`;
}

function liveImageDocument() {
  const document_ = liveDocument();
  const size = document_.pixelWidth && document_.pixelHeight ? `${document_.pixelWidth} × ${document_.pixelHeight}` : "";
  const bytes = document_.fileSize ? `${(document_.fileSize / 1024).toFixed(1)} KB` : "";
  const label = [size, document_.typeName, bytes].filter(Boolean).join(" · ");
  return `<div class="document-view"><div class="document-toolbar image-toolbar"><button class="icon-button" aria-label="缩小">${icon("zoom-out")}</button><span class="image-zoom-label">100%</span><button class="icon-button" aria-label="放大">${icon("zoom-in")}</button><button class="icon-button" aria-label="适应区域">${icon("image-fit")}</button><span class="image-size-label" title="${escapeHtml(label)}"><span class="image-size-content">${escapeHtml(label)}</span></span></div><div class="image-stage" tabindex="0" aria-label="只读图片"><img src="${escapeHtml(document_.dataUrl || "")}" alt="${escapeHtml(document_.name || "")}" draggable="false"></div></div>`;
}

function liveUnavailableDocument() {
  const document_ = liveDocument();
  return `<div class="info-state"><div class="info-block"><span style="color:var(--augit-orange)">${icon("file-warning")}</span><h2>无法在 Augit 中预览此文件</h2><p>${escapeHtml(document_.name || "")}${document_.typeName ? " · " + escapeHtml(document_.typeName) : ""}</p><p>${escapeHtml(document_.path)}</p><p>${escapeHtml(document_.message || "该文件不能以只读文本方式查看。")}</p></div></div>`;
}

function textView(find = false) {
  if (liveDocument()) return liveTextDocument(find);
  const state = new URLSearchParams(window.location.search).get('find-state');
  const query = state === 'invalid' ? '[' : state === 'timeout' ? '(a+)+$' : state === 'empty' ? '' : state === 'no-match' ? '不存在的文字' : 'Git';
  const result = state === 'loading' ? '正在搜索…' : state === 'timeout' ? '查找超时' : state === 'invalid' ? '正则表达式无效' : '';
  const findBox = find ? `<div class="current-find"><input class="search-field" value="${query}" aria-label="当前文件查找"><button class="icon-button" aria-label="区分大小写">${icon("case-sensitive")}</button><button class="icon-button" aria-label="全字匹配">${icon("whole-word")}</button><button class="icon-button" aria-label="正则表达式" aria-pressed="${state === 'invalid' || state === 'timeout'}">${icon("regex")}</button><span class="find-status" title="${result}">${result}</span><button class="icon-button" aria-label="上一项">${icon("chevron-up")}</button><button class="icon-button" aria-label="下一项">${icon("chevron-down")}</button><button class="icon-button" aria-label="关闭查找">${icon("x")}</button></div>` : "";
  return `<div class="document-view"><div class="document-toolbar"><span class="document-path">Augit › src › Augit.App › MainWindow.cs　只读</span><button class="icon-button" aria-label="自动换行">${icon("wrap-text")}</button><button class="icon-button" aria-label="显示空白">${icon("pilcrow")}</button><button class="icon-button" aria-label="当前文件搜索">${icon("search")}</button><button class="icon-button" aria-label="跳转行">${icon("corner-down-right")}</button></div>${findBox}<div class="code-view" tabindex="0">${codeLines(textViewerSourceLines, 23)}</div></div>`;
}

// 外壳注入真实 Blame 时使用：行高由既有 measureCodeViews 统一校准，槽位结构与样例一致。
function liveBlameView() {
  const blame = window.__augitLive.blame;
  const lines = blame.lines || [];
  return `<div class="document-view blame-document"><div class="document-toolbar"><span class="document-path">${escapeHtml(blame.path || "")}\u3000只读</span><span class="grow"></span><span class="commit-meta">${lines.length} 行归属</span><button class="icon-button" aria-label="关闭 Blame">${icon("x")}</button></div><div class="blame-layout"><div class="blame-gutter">${lines.map(line => `<a href="#blame-commit" data-blame-commit="${escapeHtml(line.hash)}" aria-label="定位第 ${line.number} 行的提交" class="blame-row"><span>${escapeHtml(line.date)}</span><span>${escapeHtml(line.author)}</span><span>${line.number}</span></a>`).join("")}</div><div class="code-view" tabindex="0" aria-label="Blame 只读正文">${lines.map(line => `<div class="code-line"><span class="line-number">${line.number}</span><span>${escapeHtml(line.content) || " "}</span></div>`).join("")}</div></div></div>`;
}

function blameView() {
  const blame = sourceLines.map((_, index) => ["2026/8/28", "I49", index + 1]);
  return `<div class="document-view blame-document"><div class="document-toolbar"><span class="grow"></span><span class="commit-meta">${blame.length} 行归属</span><button class="icon-button" aria-label="关闭 Blame">${icon("x")}</button></div><div class="blame-layout"><div class="blame-gutter">${blame.map((item, index) => `<a href="#blame-commit" data-blame-commit="commit-4" aria-label="定位第 ${item[2]} 行的提交" class="blame-row ${index === 18 ? "selected" : ""}"><span>${item[0]}</span><span>${item[1]}</span><span>${item[2]}</span></a>`).join("")}</div><div class="code-view" tabindex="0" aria-label="Blame 只读正文">${codeLines(sourceLines, 19)}</div></div></div>`;
}

function bindBlame() {
  document.querySelectorAll(".blame-document").forEach(documentView => {
    const body = documentView.querySelector(".code-view"), gutter = documentView.querySelector(".blame-gutter");
    const sync = () => { gutter.scrollTop = body.scrollTop; };
    body.addEventListener("scroll", sync, { passive: true });
    gutter.addEventListener("click", event => {
      const row = event.target.closest(".blame-row");
      if (!row) return;
      event.preventDefault();
      gutter.querySelectorAll(".blame-row").forEach(item => item.classList.toggle("selected", item === row));
      const workspace = documentView.closest(".workspace");
      let bottom = workspace.querySelector(".bottom-tool");
      // 固定样本来自文件历史中的根提交；定位只更新底部日志，正文和标签保持原位。
      const hash = row.dataset.blameCommit;
      if (bottom.dataset.blameCommit !== hash) {
        const template = document.createElement("template");
        template.innerHTML = gitLog(false);
        const log = template.content.firstElementChild;
        const commit = log.querySelector(`[data-hash="${hash}"]`);
        log.querySelector(".commit-list").replaceChildren(commit);
        commit.querySelector(".commit-graph-svg").outerHTML = commitGraphSvg(buildCommitGraph([{ hash, parents: [] }]), 0);
        log.querySelector('[aria-label="文本或哈希"]').value = hash;
        log.dataset.blameCommit = hash;
        bottom.replaceWith(log);
        bottom = log;
        bindSelectionFocus(log);
        bindHistoryToolbar(log);
        bindHistoryLayout(log);
        bindHistoryDetails(log);
      }
      bottom.querySelector(`[data-hash="${hash}"]`).click();
      sync();
    });
    gutter.addEventListener("wheel", event => {
      event.preventDefault();
      body.scrollTop += event.deltaY * (event.deltaMode === 1 ? parseFloat(getComputedStyle(body).lineHeight) : event.deltaMode === 2 ? body.clientHeight : 1);
      sync();
    }, { passive: false });
    documentView.querySelector('[aria-label="关闭 Blame"]').addEventListener("click", () => {
      const top = body.scrollTop, left = body.scrollLeft;
      const template = document.createElement("template");
      template.innerHTML = markdownView("source");
      const replacement = template.content.firstElementChild;
      const source = replacement.querySelector(".markdown-source");
      body.classList.add("markdown-source");
      body.setAttribute("aria-label", "Markdown 只读原文");
      source.replaceWith(body);
      documentView.replaceWith(replacement);
      body.removeEventListener("scroll", sync);
      bindMarkdownModes();
      if (typeof bindCurrentFind === 'function') bindCurrentFind();
      measureCodeViews();
      measureDocumentToolbar();
      body.scrollTo(left, top);
      body.focus({ preventScroll: true });
    });
  });
}

function buildCommitGraph(entries) {
  const positions = new Map(entries.map((entry, index) => [entry.hash, index]));
  const lanes = [], rows = [];
  let nextColor = 0, columnCount = 1;
  entries.forEach((entry, rowIndex) => {
    let column = lanes.findIndex(lane => lane.hash === entry.hash);
    const incoming = column >= 0;
    if (!incoming) { column = lanes.length; lanes.push({ hash: entry.hash, color: nextColor++ }); }
    const node = lanes[column], before = [...lanes];
    lanes.splice(column, 1);
    const parents = [...new Set(entry.parents)];
    const visible = parents.filter(hash => positions.get(hash) > rowIndex);
    const missingColors = [];
    let insertion = column;
    parents.forEach((hash, index) => {
      if (lanes.some(lane => lane.hash === hash)) return;
      const color = index === 0 ? node.color : nextColor++;
      if (positions.get(hash) > rowIndex) lanes.splice(Math.min(insertion++, lanes.length), 0, { hash, color });
      else missingColors.push(color);
    });
    const segments = [];
    before.forEach((lane, index) => {
      if (index === column) {
        if (incoming) segments.push([index, 0, index, 0.5, node.color]);
      } else {
        const destination = lanes.findIndex(next => next.hash === lane.hash);
        segments.push([index, 0, index, 0.5, lane.color], [index, 0.5, destination, 1, lane.color]);
      }
    });
    visible.forEach(hash => {
      const destination = lanes.findIndex(lane => lane.hash === hash);
      segments.push([column, 0.5, destination, 1, lanes[destination].color]);
    });
    let missingColumn = Math.max(before.length, lanes.length);
    missingColors.forEach(color => {
      const destination = visible.length === 0 && missingColors.length === 1 ? column : missingColumn++;
      segments.push([column, 0.5, destination, 0.92, color, true]);
      columnCount = Math.max(columnCount, destination + 1);
    });
    columnCount = Math.max(columnCount, before.length, lanes.length);
    rows.push({ column, color: node.color, head: !!entry.head, segments });
  });
  return { rows, width: 29 + (columnCount - 1) * 16 };
}

function commitGraphSvg(graph, index, rowHeight = 26) {
  const row = graph.rows[index];
  const colors = ["var(--augit-graph)", "var(--augit-graph-secondary)", "var(--augit-graph-third)", "var(--augit-graph-fourth)"];
  const paths = row.segments.map(([fromColumn, fromY, toColumn, toY, color, dashed]) => {
    const x1 = 15 + fromColumn * 16, x2 = 15 + toColumn * 16;
    const y1 = fromY * rowHeight, y2 = toY * rowHeight;
    const d = dashed
      ? `M${x1} ${y1}L${x1 + (x2 - x1) * 0.42} ${y1 + (y2 - y1) * 0.42}M${x1 + (x2 - x1) * 0.68} ${y1 + (y2 - y1) * 0.68}L${x2} ${y2}`
      : `M${x1} ${y1}L${x2} ${y2}`;
    return `<path class="graph-line" stroke="${colors[color % 4]}" d="${d}"/>`;
  }).join("");
  const x = 15 + row.column * 16, color = colors[row.color % 4];
  const node = `${row.head ? `<circle class="graph-head-ring" style="stroke:${color}" cx="${x}" cy="${rowHeight / 2}" r="5.5"/>` : ""}<circle fill="${color}" cx="${x}" cy="${rowHeight / 2}" r="${row.head ? 2 : 4}"/>`;
  return `<svg class="commit-graph-svg" data-graph-row='${JSON.stringify(row)}' viewBox="0 0 ${graph.width} ${rowHeight}" aria-hidden="true">${paths}${node}</svg>`;
}

// 外壳注入真实历史时使用：沿用与样例版一致的提交行、分支标签与图形结构。
function liveGitLog(history, selected, cancelComparison) {
  const commits = history.commits.map(commit => [commit.subject, (commit.references || []).join(" ") , commit.author, commit.date]);
  const entries = history.commits.map(commit => ({
    hash: commit.hash,
    head: commit.fullHash === history.head,
    parents: commit.parents || [],
  }));
  const fullByShort = new Map(history.commits.map(commit => [commit.hash, commit.fullHash]));
  for (const entry of entries) {
    entry.parents = (entry.parents || []).map(parent => fullByShort.get(parent) || parent);
  }

  const graph = buildCommitGraph(entries);
  const filters = ["分支", "用户", "日期", "路径"];
  const branchLabel = (commit) => (commit.references || []).filter(name => name !== "HEAD").join(" ");
  const branches = new Set();
  for (const commit of history.commits) {
    for (const name of commit.references || []) {
      if (name !== "HEAD" && name !== "origin/HEAD") branches.add(name);
    }
  }
  const branchRows = [...branches].sort().map(name =>
    `<div class="tree-row depth-1${name === history.branch ? " selected" : ""}">${gitReferenceIcon()} ${escapeHtml(name)}</div>`).join("");
  // 无历史文案（规格 §10.1）：保留引用树与筛选栏，只替换提交列表内容。
  const emptyRow = `<div class="empty-tool-state"><div><strong>仓库还没有提交</strong><p>提交后会显示在这里。</p></div></div>`;
  return `<section class="bottom-tool">
    <div class="bottom-header"><span class="bottom-title">Git</span><button class="tool-tab active">日志</button><span class="grow"></span>${cancelComparison ? "" : ""}<button class="icon-button">${icon("ellipsis-vertical")}</button><button class="icon-button">${icon("minus")}</button></div>
    <div class="git-toolbar-layout">
      <nav class="git-side-toolbar" aria-label="Git 日志工具"><button class="toolbar-button" aria-label="返回">${icon("history-back")}</button><span class="rail-separator"></span><button class="toolbar-button" aria-label="新建引用">${icon("plus")}</button><button class="toolbar-button" aria-label="删除引用">${icon("trash-2")}</button><button class="toolbar-button" aria-label="刷新">${icon("refresh-cw")}</button><button class="toolbar-button" aria-label="搜索">${icon("history-search")}</button><button class="toolbar-button" aria-label="比较">${icon("git-compare-arrows")}</button><button class="toolbar-button" aria-label="定位 HEAD">${icon("locate-fixed")}</button></nav>
      <div class="git-log">
        <div class="log-ref-panel"><div class="log-filterbar"><label class="history-search">${icon("search")}<input class="search-field" placeholder="分支或标签" aria-label="分支或标签"></label></div><div class="tree"><div class="tree-row">HEAD（当前分支）</div><div class="tree-row"><span>${icon("chevron-down")}</span><strong>本地</strong></div>${branchRows}</div></div>
        <div class="log-list-panel">
          <div class="log-filterbar history-filters">
            <label class="history-search">${icon("search")}<input class="search-field" placeholder="文本或哈希" aria-label="文本或哈希"></label>
            ${filters.map((label, index) => `<button class="toolbar-button history-filter" data-history-filter="${index}"><span>${label}</span>${icon("chevron-down")}</button>`).join("")}
            <details class="history-filter-overflow" hidden><summary class="toolbar-button" aria-label="更多历史筛选">${icon("chevron-right")}</summary><div class="history-filter-menu">${filters.map((label, index) => `<button data-history-filter="${index}">${icon("search")}${label}</button>`).join("")}</div></details>
            <span class="grow"></span><button class="toolbar-button history-utility" aria-label="显示提交详情">${icon("eye")}</button><button class="toolbar-button history-utility" aria-label="搜索提交">${icon("search")}</button>
          </div>
          <div class="commit-list commit-list-graph" style="--augit-graph-width:${graph.width}px">${history.commits.length === 0 ? emptyRow : history.commits.map((commit, index) => `<div class="commit-row ${index === 0 ? "selected" : ""}" role="option" aria-selected="${index === 0}" data-hash="${escapeHtml(commit.hash)}" data-full-hash="${escapeHtml(commit.fullHash)}">${commitGraphSvg(graph, index)}<span class="commit-subject">${escapeHtml(commit.subject)}</span><span class="branch-label">${branchLabel(commit) ? `${gitReferenceIcon(false)} ${escapeHtml(branchLabel(commit))}` : ""}</span><span class="commit-meta commit-author">${escapeHtml(commit.author)}</span><time class="commit-meta commit-date" data-full="${escapeHtml(commit.date)}" data-compact="${escapeHtml(commit.date.slice(5, 10))}">${escapeHtml(commit.date)}</time></div>`).join("")}</div>
        </div>
        <div class="log-detail-panel"><div class="changed-files" data-live-changed-files><p class="commit-meta">正在读取变更…</p></div><div class="commit-detail" data-live-commit-detail><h3>${history.commits.length === 0 ? "提交详情" : escapeHtml(history.commits[0].subject)}</h3><div>${history.commits.length === 0 ? "" : `${escapeHtml(history.commits[0].hash)} · ${escapeHtml(history.commits[0].author)} · ${escapeHtml(history.commits[0].date)}`}</div></div></div>
      </div>
    </div>
  </section>`;
}

function gitLog(selected = true, complexGraph = false, cancelComparison = false) {
  const liveHistory = window.__augitLive && window.__augitLive.history;
  // 复杂泳道图同样使用真实历史：结构化行已带父子关系，
  // 泳道由 buildCommitGraph 推导，不需要另用样例数据。
  if (liveHistory) return liveGitLog(liveHistory, selected, cancelComparison);
  let commits = complexGraph ? [
    ["merge: 合并历史界面调整", "main", "I49", "2026/8/29 11:00"],
    ["fix: 修正工具栏图标", "", "I49", "2026/8/29 10:00"],
    ["feat: 调整历史筛选布局", "feature/graph", "I49", "2026/8/29 9:00"],
    ["fix: 精确恢复安装前系统 PATH", "", "I49", "2026/8/28 22:55"],
    ["fix: 避免强制更新兼容的 .NET 10", "", "I49", "2026/8/28 21:32"],
    ["fix: 提升安装卸载与 Git 取消可靠性", "", "I49", "2026/8/28 21:25"],
    ["fix: 补充原生 Windows 应用清单", "", "I49", "2026/8/28 8:45"],
    ["feat: 实现 Augit 阶段零至五功能", "", "I49", "2026/8/28 8:25"],
  ] : [
    ["fix: 精确恢复安装前系统 PATH", "main", "I49", "2026/8/28 22:55"],
    ["fix: 避免强制更新兼容的 .NET 10", "", "I49", "2026/8/28 21:32"],
    ["fix: 提升安装卸载与 Git 取消可靠性", "", "I49", "2026/8/28 21:25"],
    ["fix: 补充原生 Windows 应用清单", "", "I49", "2026/8/28 8:45"],
    ["feat: 实现 Augit 阶段零至五功能", "", "I49", "2026/8/28 8:25"],
  ];
  let entries = commits.map((item, index) => ({ hash: `commit-${index}`, head: index === 0,
    parents: index < commits.length - 1 ? [`commit-${index + 1}`] : [] }));
  let wideGraph = false;
  if (complexGraph) {
    entries[0].parents = ["commit-1", "commit-2"];
    entries[1].parents = ["commit-3"];
    // 场景参数仅用于视觉验收，不增加产品筛选或操作入口。
    const variant = new URLSearchParams(location.search).get("graph");
    if (variant === "wide") {
      wideGraph = true;
      const baseCommits = commits.slice(3);
      const branches = Array.from({ length: 12 }, (_, index) => `branch-${index}`);
      entries = [{ hash: "merge", head: true, parents: branches },
        ...branches.map(hash => ({ hash, parents: ["base-0"] })),
        ...baseCommits.map((_, index) => ({ hash: `base-${index}`, parents: index < baseCommits.length - 1 ? [`base-${index + 1}`] : [] }))];
      commits = [["merge: 合并十二条分支", "main", "I49", "2026/8/29 11:00"],
        ...branches.map((hash, index) => [`feat: ${hash}`, index === 0 ? "" : `feature/branch-${index}`, "I49",
          index === 0 ? "2026/8/29 10:00" : `2026/8/29 9:${String(11 - index).padStart(2, "0")}`]), ...baseCommits];
    } else if (variant === "filtered" || variant === "page") {
      const indices = variant === "page" ? [0, 1] : [0, 1, 3, 4, 5, 6, 7];
      entries = indices.map(index => entries[index]);
      commits = indices.map(index => commits[index]);
    }
  }
  const graph = buildCommitGraph(entries);
  const filters = ["分支", "用户", "日期", "路径"];
  const branchRows = wideGraph
    ? `${Array.from({ length: 11 }, (_, index) => `<a class="tree-row depth-1" href="branches.html">${gitReferenceIcon()} feature/branch-${index + 1}</a>`).join("")}<a class="tree-row depth-1 selected" href="branches.html">${gitReferenceIcon()} main</a>`
    : complexGraph
    ? `<a class="tree-row depth-1" href="branches.html">${gitReferenceIcon()} feature/graph</a><a class="tree-row depth-1 selected" href="branches.html">${gitReferenceIcon()} main</a>`
    : `<a class="tree-row depth-1 selected" href="branches.html">${gitReferenceIcon()} main</a>`;
  const detailTitle = commits[1]?.[0] ?? "提交详情";
  const detailMeta = complexGraph ? "cb20f43 · I49 · 2026/8/29 10:00" : "dfe5c25a · I49 · 2026/8/28 21:32";
  const detailBody = complexGraph ? "" : `<p class="commit-meta">安装器检测到兼容运行时后保持当前环境，不重复更新系统组件。</p>`;
  return `<section class="bottom-tool">
    <div class="bottom-header"><span class="bottom-title">Git</span><button class="tool-tab active">日志</button><span class="grow"></span>${cancelComparison ? '<a class="toolbar-button" href="history-diff-cancelled.html">取消比较</a>' : ""}<button class="icon-button">${icon("ellipsis-vertical")}</button><button class="icon-button">${icon("minus")}</button></div>
    <div class="git-toolbar-layout">
      <nav class="git-side-toolbar" aria-label="Git 日志工具"><button class="toolbar-button" aria-label="返回">${icon("history-back")}</button><span class="rail-separator"></span><button class="toolbar-button" aria-label="新建引用">${icon("plus")}</button><button class="toolbar-button" aria-label="删除引用">${icon("trash-2")}</button><button class="toolbar-button" aria-label="刷新">${icon("refresh-cw")}</button><button class="toolbar-button" aria-label="搜索">${icon("history-search")}</button><button class="toolbar-button" aria-label="比较">${icon("git-compare-arrows")}</button><button class="toolbar-button" aria-label="定位 HEAD">${icon("locate-fixed")}</button></nav>
      <div class="git-log">
        <div class="log-ref-panel"><div class="log-filterbar"><label class="history-search">${icon("search")}<input class="search-field" placeholder="分支或标签" aria-label="分支或标签"></label></div><div class="tree"><div class="tree-row">HEAD（当前分支）</div><div class="tree-row"><span>${icon("chevron-down")}</span><strong>本地</strong></div>${branchRows}</div></div>
        <div class="log-list-panel">
          <div class="log-filterbar history-filters">
            <label class="history-search">${icon("search")}<input class="search-field" placeholder="文本或哈希" aria-label="文本或哈希"></label>
            ${filters.map((label, index) => `<button class="toolbar-button history-filter" data-history-filter="${index}"><span>${label}</span>${icon("chevron-down")}</button>`).join("")}
            <details class="history-filter-overflow" hidden><summary class="toolbar-button" aria-label="更多历史筛选">${icon("chevron-right")}</summary><div class="history-filter-menu">${filters.map((label, index) => `<button data-history-filter="${index}">${icon("search")}${label}</button>`).join("")}</div></details>
            <span class="grow"></span><button class="toolbar-button history-utility" aria-label="显示提交详情">${icon("eye")}</button><button class="toolbar-button history-utility" aria-label="搜索提交">${icon("search")}</button>
          </div>
          <div class="commit-list commit-list-graph" style="--augit-graph-width:${graph.width}px">${commits.map((item, index) => `<div class="commit-row ${selected && index === 1 ? "selected" : ""}" role="option" aria-selected="${selected && index === 1}" data-hash="${entries[index].hash}">${commitGraphSvg(graph, index)}<span class="commit-subject">${item[0]}</span><span class="branch-label">${item[1] ? `${gitReferenceIcon(false)} ${item[1]}` : ""}</span><span class="commit-meta commit-author">${item[2]}</span><time class="commit-meta commit-date" data-full="${item[3]}" data-compact="${item[3].includes('8/29') ? '08-29' : '08-28'}">${item[3]}</time></div>`).join("")}</div>
        </div>
        <div class="log-detail-panel"><div class="changed-files">${selected ? complexGraph ? '<div class="tree-row">0 个文件</div>' : `<div class="tree-row"><span>${icon("chevron-down")}</span><span>${treeFolderIcon()}</span><strong>6 个文件</strong></div><div class="tree-row depth-1"><span>${icon("chevron-down")}</span><span>${treeFolderIcon()}</span> docs <span class="commit-meta">4 个文件</span></div>${["architecture.md", "performance-report.md", "roadmap.md", "runtime-dependencies.md"].map(name => `<div class="tree-row depth-2 file-status-modified" data-history-path="docs/${name}">${fileTypeIcon(name)}${name}</div>`).join("")}` : `<div class="empty-state">选择提交以查看变更</div>`}</div><div class="commit-detail">${selected ? `<h3>${detailTitle}</h3><div>${detailMeta}</div>${detailBody}` : `<div class="empty-state">提交详情</div>`}</div></div>
      </div>
    </div>
  </section>`;
}

function historySampleFiles(subject) {
  // 文件集合对应原生视觉审计隔离仓库的固定提交，不沿用上一提交的详情。
  const baseline = ["THIRD-PARTY-NOTICES.md", "README.md", "global.json", "Augit.slnx",
    ...["architecture.md", "product-spec.md", "roadmap.md", "runtime-dependencies.md", "performance-report.md"].map(name => `docs/${name}`),
    ...["MainWindow.cs", "NativeGitPanel.cs", "app.manifest", "AssemblyInfo.cs", "NativeTheme.cs", "NativeDocumentView.cs",
      "NativeGitHistoryPanel.cs", "NativeSearchPanel.cs", "NativeTerminalPanel.cs", "NativeConflictResolverDialog.cs"].map(name => `src/Augit.App/${name}`),
    ...["ConPtyNativeMethods.cs", "ConPtyTerminalSession.cs"].map(name => `src/Augit.Infrastructure/Terminal/${name}`),
    ...["ZAuditStatusSnapshot.cs", "ZAuditDiffModels.cs", "ZAuditHistoryModels.cs"].map(name => `src/Augit.Core/${name}`),
    ...["ZAuditStatusService.cs", "ZAuditDiffService.cs", "ZAuditHistoryService.cs"].map(name => `src/Augit.Infrastructure/Git/${name}`),
    "src/Augit.Infrastructure/Settings/ApplicationSettings.cs", "src/Augit.Infrastructure/Settings/ZAuditSettingsStore.cs",
    "tests/Augit.App.Tests/Augit.App.Tests.csproj", "tests/Augit.App.Tests/VisualAuditHostTests.cs",
    "tests/Augit.Core.Tests/ZAuditGitFileSelectionTests.cs", "tests/Augit.Infrastructure.Tests/ZAuditGitStatusServiceTests.cs",
    "tools/xterm/terminal/index.html", "tools/xterm/terminal/terminal.js"];
  if (subject === "feat: 实现 Augit 阶段零至五功能") return baseline.map(path => ({ path, status: "added" }));
  if (subject === "fix: 精确恢复安装前系统 PATH") {
    const renamed = [["global.json", "ZAuditGlobal.json"], ["docs/architecture.md", "docs/ZAuditArchitecture.md"],
      ...["NativeConflictResolverDialog.cs", "NativeDocumentView.cs", "NativeGitHistoryPanel.cs", "NativeSearchPanel.cs", "NativeTerminalPanel.cs"]
        .map(name => [`src/Augit.App/${name}`, `src/Augit.App/ZAudit${name.slice(6)}`]),
      ["tools/xterm/terminal/index.html", "tools/xterm/terminal/ZAuditIndex.html"]];
    return [...renamed.map(([original, path]) => ({ path, original, status: "renamed" })), { path: "docs/product-spec.md", status: "modified" }];
  }
  const files = subject === "fix: 避免强制更新兼容的 .NET 10"
    ? ["docs/architecture.md", "docs/performance-report.md", "docs/roadmap.md", "docs/runtime-dependencies.md", "global.json", "Augit.slnx"]
    : subject === "fix: 提升安装卸载与 Git 取消可靠性" ? ["src/Augit.App/MainWindow.cs", "src/Augit.App/NativeGitPanel.cs"]
      : subject === "fix: 补充原生 Windows 应用清单" ? ["src/Augit.App/app.manifest"] : [];
  return files.map(path => ({ path, status: "modified" }));
}

function historySampleFilesHtml(subject) {
  const files = historySampleFiles(subject);
  const render = (items, prefix = "", depth = 1) => {
    const groups = new Map(), leaves = [];
    items.forEach(item => {
      const relative = item.path.slice(prefix.length), slash = relative.indexOf("/");
      if (slash < 0) leaves.push(item);
      else {
        const group = relative.slice(0, slash);
        if (!groups.has(group)) groups.set(group, []);
        groups.get(group).push(item);
      }
    });
    const indent = `style="padding-left:${8 + depth * 18}px"`;
    return [...groups].sort(([a], [b]) => a.localeCompare(b)).map(([name, children]) =>
      `<div class="tree-row" ${indent}>${icon("chevron-down")}${treeFolderIcon()}${name}<span class="commit-meta">${children.length} 个文件</span></div>${render(children, `${prefix}${name}/`, depth + 1)}`).join("")
      + leaves.sort((a, b) => a.path.localeCompare(b.path)).map(file =>
        `<div class="tree-row file-status-${file.status}" data-history-path="${escapeHtml(file.path)}" ${indent}>${fileTypeIcon(file.path)}${file.path.slice(prefix.length)}${file.original ? ` ← ${file.original.split("/").at(-1)}` : ""}</div>`).join("");
  };
  return `<div class="tree-row">${icon("chevron-down")}${treeFolderIcon()}<strong>${files.length} 个文件</strong></div>${render(files)}`;
}

// 外壳注入真实文件历史时使用；结构与样例版一致，复用同一套样式。
function liveFileHistoryTool() {
  const fileHistory = window.__augitLive.fileHistory;
  const commits = fileHistory.commits || [];
  const rows = commits.length === 0
    ? `<p class="commit-meta">没有历史</p>`
    : commits.map((commit, index) => `<div class="history-row ${index === 0 ? "selected" : ""}" data-history-hash="${escapeHtml(commit.hash)}"><span>${escapeHtml(commit.author)}</span><span>${escapeHtml(commit.date)}</span><span>${escapeHtml(commit.subject)}</span></div>`).join("");
  const head = commits[0];
  return `<section class="bottom-tool"><div class="bottom-header"><span class="bottom-title">Git</span><a class="tool-tab" href="git-history.html">日志</a><button class="tool-tab active">历史: ${escapeHtml(fileHistory.path || "")}</button><span class="grow"></span><button class="icon-button">${icon("ellipsis-vertical")}</button><button class="icon-button">${icon("minus")}</button></div><div class="history-tool-content"><div class="history-list-pane"><div class="history-toolbar"><span>分支: HEAD</span><button class="icon-button">${icon("x")}</button><span class="toolbar-separator"></span><button class="icon-button">${icon("refresh-cw")}</button><button class="icon-button">${icon("git-compare-arrows")}</button><button class="icon-button">${icon("history-expand")}</button><button class="icon-button">${icon("eye")}</button></div><div class="history-rows">${rows}</div></div><div class="history-detail-pane"><div class="commit-detail">${head ? `<h3>${escapeHtml(head.subject)}</h3><div>${escapeHtml(head.hash)} · ${escapeHtml(head.author)} · ${escapeHtml(head.date)}</div>` : `<p class="commit-meta">选择提交以查看变更</p>`}</div></div></div></section>`;
}

function fileHistoryTool() {
  return `<section class="bottom-tool"><div class="bottom-header"><span class="bottom-title">Git</span><a class="tool-tab" href="git-history.html">日志</a><button class="tool-tab active">历史: product-spec.md</button><span class="grow"></span><button class="icon-button">${icon("ellipsis-vertical")}</button><button class="icon-button">${icon("minus")}</button></div><div class="history-tool-content"><div class="history-list-pane"><div class="history-toolbar"><span>分支: HEAD</span><button class="icon-button">${icon("x")}</button><span class="toolbar-separator"></span><button class="icon-button">${icon("refresh-cw")}</button><button class="icon-button">${icon("git-compare-arrows")}</button><button class="icon-button">${icon("history-expand")}</button><button class="icon-button">${icon("eye")}</button></div><div class="history-rows"><div class="history-row selected"><span>I49</span><span>2026/8/28 8:25</span><span>feat: 实现 Augit 阶段零至五功能</span></div></div></div><div class="history-detail-pane">${diffView(true, new URLSearchParams(location.search).get("history-state") || "ready", false, true)}</div></div></section>`;
}

function terminalTool() {
  const loading = new URLSearchParams(location.search).get("terminal-state") === "loading";
  const name = `Windows PowerShell${loading ? " · 正在启动…" : ""}`;
  const output = loading ? "" : `<div><span class="terminal-prompt">PS </span><span class="terminal-path">D:\\github\\Augit</span>&gt; <span class="terminal-command">dotnet test Augit.slnx -c Release</span></div><div>已通过! - 失败: 0，通过: 183，跳过: 0，总计: 183</div><div><span class="terminal-prompt">PS </span><span class="terminal-path">D:\\github\\Augit</span>&gt; <span class="terminal-command">git status --short</span></div><div class="file-status-modified"> M docs/product-spec.md</div><div class="file-status-modified"> M src/Augit.App/NativeGitPanel.cs</div><div><span class="terminal-prompt">PS </span><span class="terminal-path">D:\\github\\Augit</span>&gt; <span class="terminal-command">_</span></div>`;
  return `<section class="bottom-tool terminal-tool"><div class="bottom-header terminal-header"><span class="bottom-title terminal-title">终端</span><span class="terminal-session" title="${name}">${name}</span><button class="icon-button terminal-session-close" aria-label="关闭终端">${icon("x")}</button><button class="icon-button terminal-more" aria-label="更多操作">${icon("ellipsis-vertical")}</button><button class="icon-button terminal-hide" aria-label="隐藏终端">${icon("minus")}</button></div><div class="terminal-view" aria-busy="${loading}">${output}</div></section>`;
}

function shell({ activeRail = "project", side = "project", editor = "markdown", bottom = "", overlay = "", toast = "", selectedFile = "product-spec.md", complexGraph = false, comparisonState = "ready", workspaceComparison = false, diffBoundary = false } = {}) {
  const live = window.__augitLive || null;
  // 实时外壳下，工具窗口由用户操作驱动（规格 §5.1）：场景只提供初始布局，
  // 之后以 live.layout 为准。视觉稿单独打开时没有 live，行为完全不变，
  // 因此逐场景静态浏览与既有一致性不受影响。
  // 只有在用户实际操作过工具窗口之后才覆盖场景值。
  // 场景可以省略 activeRail/side/bottom 而依赖 shell() 的默认参数，
  // 若一开始就用 DOM 读出的值覆盖，会把场景依赖的默认值抹掉。
  if (live && live.layout && live.layout.userDriven) {
    if (live.layout.activeRail) activeRail = live.layout.activeRail;
    side = live.layout.collapsed === "side" ? "" : live.layout.side;
    bottom = live.layout.collapsed === "bottom" ? "" : live.layout.bottom;
  }

  // 实时外壳下由用户操作打开的弹层覆盖场景自带的那个（规格 §5.3）。
  // 只在确有实时弹层时覆盖，否则保留场景值，逐场景静态浏览与既有场景行为不变。
  if (live && live.overlay) {
    overlay = live.overlay;
  }

  // 比较标签（工作区 Diff、引用比较、历史比较）没有普通文档，但同样有明确视图：
  // 只看 live.document 会让比较正文渲染不出来（表现为 editor 已是 diff 而 DOM 仍是文档）。
  if (live && live.editor && (live.document || live.editor === "diff")) editor = live.editor;
  if (live && live.document) selectedFile = live.document.name || selectedFile;
  // 折叠状态（side 为空）不渲染侧栏，把整块宽度还给编辑区。
  const sideHtml = side === "" ? "" : side === "commit-empty"
    ? emptyChangesSide()
    : side === "commit"
      ? (live && live.status ? liveChangesSide(editor === "diff" || editor === "diff-loading" ? "app.manifest" : "") : changesSide(editor === "diff" || editor === "diff-loading" ? "app.manifest" : ""))
      : projectTree(selectedFile, live && side === "project" ? live : null);
  let editorExtra = "";
  let editorBody = markdownView();
  if (editor === "text") editorBody = textView(false);
  if (editor === "text-find") editorBody = textView(true);
  if (editor === "json") editorBody = jsonView();
  if (editor === "image" && liveDocument()) editorBody = liveImageDocument();
  else if (editor === "image") editorBody = `<div class="document-view"><div class="document-toolbar image-toolbar"><button class="icon-button" aria-label="缩小">${icon("zoom-out")}</button><span class="image-zoom-label">100%</span><button class="icon-button" aria-label="放大">${icon("zoom-in")}</button><button class="icon-button" aria-label="适应区域">${icon("image-fit")}</button><span class="image-size-label" title="1920 × 1200 · PNG · 52.5 KB"><span class="image-size-content">1920 × 1200 · PNG · 52.5 KB</span></span></div><div class="image-stage" tabindex="0" aria-label="只读图片"><img src="assets/image-sample.png" alt="带透明边缘的山景样图" draggable="false"></div></div>`;
  if (editor === "file-limit" && liveDocument()) editorBody = liveUnavailableDocument();
  else if (editor === "file-limit") editorBody = `<div class="info-state"><div class="info-block"><span style="color:var(--augit-orange)">${icon("file-warning")}</span><h2>无法在 Augit 中预览此文件</h2><p>animation.webp · WebP 图片 · 4.8 MB</p><p>D:\\github\\Augit\\docs\\assets\\animation.webp</p><p>Augit 不支持 GIF、WebP 或其他二进制图片格式。</p><div class="button-row" style="justify-content:center"><button class="secondary-button">使用系统默认程序打开</button></div></div></div>`;
  if (editor === "blame") editorBody = (live && live.blame) ? liveBlameView() : blameView();
  if (editor === "diff") {
    // 标签优先用真实差异路径；差异尚未到达时用启动参数里的目标路径，
    // 避免短暂显示样例文件名。
    const diffPath = (live && live.diff && live.diff.path)
      || (new URLSearchParams(location.search).get("diff"))
      || "app.manifest";
    editorExtra = `<a class="editor-tab active" href="#" data-workspace-diff-tab="true">${icon("git-compare-arrows")} <span class="change-tab-caption">提交: ${escapeHtml(diffPath)}</span><button type="button" class="tab-close" aria-label="关闭比较">${icon("x")}</button></a>`;
    editorBody = (live && live.diff) ? liveDiffView() : diffView();
    if (diffBoundary) {
      editorBody = editorBody.replace('<div class="diff-columns">', '<div class="diff-columns diff-boundary-columns"><div class="diff-boundary-hint" role="status">再次点击可进入下一个文件</div>');
    }
  }
  if (editor === "comparison") {
    editorExtra = `<a class="editor-tab active comparison-tab" href="git-compare.html">${icon("git-compare-arrows")}<span class="comparison-caption"><span class="comparison-file">比较: app.manifest</span><span> · </span><span class="comparison-revision">${workspaceComparison ? "HEAD" : "dfe5c25a^"}</span><span> → </span><span class="comparison-revision">${workspaceComparison ? "工作区" : "dfe5c25a"}</span></span><span class="tab-close" aria-label="关闭比较">${icon("x")}</span></a>`;
    editorBody = diffView(true, comparisonState, workspaceComparison);
  }
  if (editor === "diff-loading") {
    editorExtra = `<a class="editor-tab active" href="diff-loading.html">${icon("git-compare-arrows")} 提交: app.manifest</a>`;
    const loadingLineWidths = [72, 46, 87, 61, 78, 39, 69, 54, 82, 48, 64, 43];
    const loadingLines = (offset = 0) => loadingLineWidths
      .map((width, index) => `<span class="diff-loading-line" style="width:${loadingLineWidths[(index + offset) % loadingLineWidths.length]}%"></span>`)
      .join("");
    const loadingGutterLines = loadingLineWidths
      .map(() => `<span class="diff-loading-gutter-line"></span><span class="diff-loading-gutter-line"></span>`)
      .join("");
    editorBody = `<div class="diff-layout"><div class="diff-toolbar"><button class="toolbar-button" aria-label="上一处差异">${icon("arrow-up")}</button><button class="toolbar-button" aria-label="下一处差异">${icon("arrow-down")}</button><span class="grow"></span><span>正在计算差异…</span><div class="segmented"><button class="segment active" aria-label="双栏">${icon("diff-side-by-side")}</button><button class="segment" aria-label="单栏">${icon("diff-unified")}</button></div></div>${diffFileHeader("HEAD", "当前版本", "HEAD → 当前版本")}<div class="diff-columns diff-loading-columns"><div class="diff-loading-status"><span class="loading-mark"></span><span>正在加载 app.manifest 的差异</span></div><div class="diff-loading-side">${loadingLines()}</div><div class="diff-loading-gutter">${loadingGutterLines}</div><div class="diff-loading-side">${loadingLines(3)}</div></div></div>`;
  }
  if (editor === "empty") editorBody = `<div class="empty-state">选择文件以查看内容</div>`;
  const tabs = editor === "image"
    ? `<div class="editor-tabs"><a class="editor-tab active" href="image-preview.html">${fileTypeIcon("image-sample.png")}<span class="json-tab-caption">image-sample.png</span><span class="tab-close" aria-label="关闭文件">${icon("x")}</span></a><span class="grow"></span><button class="icon-button" aria-label="标签选项">${icon("ellipsis-vertical")}</button></div>`
    : editor === "json"
    ? `<div class="editor-tabs"><a class="editor-tab active" href="json-preview.html">${fileTypeIcon("global.json")}<span class="json-tab-caption">global.json</span><span class="tab-close" aria-label="关闭文件">${icon("x")}</span></a><span class="grow"></span><button class="icon-button" aria-label="标签选项">${icon("ellipsis-vertical")}</button></div>`
    : scene === "go-to-line" || scene === "text-viewer"
    ? `<div class="editor-tabs"><a class="editor-tab active" href="text-viewer.html">${fileTypeIcon("MainWindow.cs")} MainWindow.cs<span class="tab-close" aria-label="关闭文件">${icon("x")}</span></a><span class="grow"></span><button class="icon-button" aria-label="标签选项">${icon("ellipsis-vertical")}</button></div>`
    : editor === "diff" || editor === "diff-loading" || editor === "comparison"
    ? editorTabs("", editorExtra)
    : editorTabs(editor === "markdown" || editor === "blame" ? "product" : "third", editorExtra);
  const bottomHtml = bottom === "git" ? gitLog(true, complexGraph, comparisonState === "loading") : bottom === "terminal" ? terminalTool() : bottom === "file-history" ? ((live && live.fileHistory) ? liveFileHistoryTool() : fileHistoryTool()) : "";
  return `<div class="augit-window">${titlebar()}<main class="app-main">${rail(activeRail)}${sideHtml}<section class="workspace ${bottom ? "with-bottom" : ""}"><article class="editor-area">${tabs}<div class="editor-content">${editorBody}</div></article>${bottomHtml}</section></main>${statusBar(editor, selectedFile)}${overlay}${toast}</div>`;
}

// 状态栏描述活动视图；比较补丁不提供源文件编码与换行事实。
function statusBar(editor, selectedFile) {
  const live = liveDocument();
  if (live) {
    const fields = [];
    if (live.encoding) fields.push(live.encoding);
    if (live.lineEndings) fields.push(live.lineEndings);
    fields.push("只读");
    const location = [live.workspaceName || "Augit", ...String(live.path).split("/")].filter(Boolean).join("  ›  ");
    return `<footer class="statusbar" aria-label="文件状态"><span class="status-path" title="${escapeHtml(live.fullPath || live.path)}">${escapeHtml(location)}</span><div class="status-fields">${fields.map(text => `<span>${escapeHtml(text)}</span>`).join('')}</div></footer>`;
  }

  const comparison = ["diff", "diff-loading", "comparison"].includes(editor);
  // 外壳存在但没有文档时，状态栏显示工作区路径（规格 §4.1），
  // 而不是视觉稿里写死的工作区名与路径。
  if (window.__augitLive && !liveDocument()) {
    const workspace = window.__augitLive;
    const rootPath = workspace.root || "";
    const folder = rootPath ? rootPath.replaceAll("\\", "/").split("/").filter(Boolean).at(-1) : null;
    const name = workspace.workspaceName || folder || "工作区";
    const title = rootPath ? `${rootPath}\\${workspace.name || folder || ""}` : name;
    return `<footer class="statusbar" aria-label="文件状态"><span class="status-path" title="${escapeHtml(title)}">${escapeHtml(name)}</span><div class="status-fields"></div></footer>`;
  }

  const hasDocument = editor !== "empty";
  const isText = hasDocument && !comparison && !["image", "file-limit"].includes(editor);
  const file = comparison ? "app.manifest" : selectedFile;
  const parent = comparison || ["text", "text-find"].includes(editor) ? "src/Augit.App"
    : editor === "json" ? "" : editor === "image" || editor === "file-limit" ? "docs/assets" : "docs";
  const relative = hasDocument ? [parent, file].filter(Boolean).join('/') : "";
  const fullPath = 'D:\\github\\Augit' + (relative ? '\\' + relative.replaceAll('/', '\\') : '');
  const location = ["Augit", ...relative.split('/').filter(Boolean)].join('  ›  ');
  const requestedEnding = new URLSearchParams(window.location.search).get('eol');
  const ending = ["LF", "CRLF", "CR", "混合换行", "无换行"].includes(requestedEnding) ? requestedEnding : "LF";
  const fields = isText ? ["UTF-8", ending, "只读"] : hasDocument ? ["只读"] : [];
  return `<footer class="statusbar" aria-label="文件状态"><span class="status-path" title="${escapeHtml(fullPath)}">${escapeHtml(location)}</span><div class="status-fields">${fields.map(text => `<span>${text}</span>`).join('')}</div></footer>`;
}

// 外壳注入真实搜索结果时使用；结构与样例版一致（浮层、开关、结果行、提示）。
function liveSearchOverlay(kind) {
  const live = window.__augitLive || {};
  const search = live.search;
  const repository = kind === "repository";
  const header = repository
    ? `<strong>全仓搜索</strong>${[['case-sensitive', '区分大小写', 'matchCase'], ['whole-word', '全字匹配', 'matchWholeWord'], ['regex', '正则表达式', 'useRegularExpression']].map(([name, label, key]) => `<button class="icon-button search-option" aria-label="${label}" title="${label}" aria-pressed="${search && search.options && search.options[key] ? 'true' : 'false'}">${icon(name)}</button>`).join('')}<label class="search-ignored"><input type="checkbox" aria-label="包含忽略文件"${search && search.options && search.options.includeIgnoredFiles ? ' checked' : ''}>包含忽略文件</label>`
    : `<strong>快速打开文件</strong><span class="grow"></span><span class="menu-shortcut">Ctrl+P</span>`;
  const query = (search && search.query) || "";
  const matches = (search && search.matches) || [];
  const rows = matches.map((match, index) => repository
    ? `<a class="search-result${index === 0 ? ' selected' : ''}" href="#" data-search-path="${escapeHtml(match.path)}" data-search-line="${match.line}">${fileTypeIcon(match.name)}<span>${escapeHtml(match.name)} <span class="commit-meta">${escapeHtml(String(match.line))}: ${escapeHtml(match.text)}</span></span><span class="commit-meta">${escapeHtml(match.directory)}</span></a>`
    : `<a class="search-result${index === 0 ? ' selected' : ''}" href="#" data-search-path="${escapeHtml(match.path)}">${fileTypeIcon(match.name)}<span>${escapeHtml(match.name)}</span><span class="commit-meta">${escapeHtml(match.directory)}</span></a>`).join("");
  // 搜索无结果（规格 §10.1）：显示「未找到结果」，输入框与查询保持不动。
  // 仅在**确实查询过**（有查询串）且没有命中时显示，避免打开浮层就报「未找到」。
  const results = matches.length > 0
    ? `<div class="search-results">${rows}</div>`
    : (query.length > 0 ? `<div class="search-results"><div class="empty-tool-state"><div><strong>未找到结果</strong><p>换个关键词或调整筛选。</p></div></div></div>` : "");
  const noticeText = search && search.notice ? search.notice : "";
  const notice = noticeText
    ? `<div class="search-notice" role="status" tabindex="0">${escapeHtml(noticeText)}</div>`
    : "";
  return `<div class="search-overlay ${repository ? 'repository-mode' : ''}" data-augit-overlay><div class="search-tabs">${header}</div><div class="search-query"><input class="search-field" value="${escapeHtml(query)}" aria-label="搜索内容"></div>${results}${notice}</div>`;
}

function searchOverlay(kind) {
  const repository = kind === "repository";
  const searchHeader = repository
    ? `<strong>全仓搜索</strong>${[['case-sensitive', '区分大小写'], ['whole-word', '全字匹配'], ['regex', '正则表达式']].map(([name, label]) => `<button class="icon-button search-option" aria-label="${label}" title="${label}" aria-pressed="false">${icon(name)}</button>`).join('')}<label class="search-ignored"><input type="checkbox" aria-label="包含忽略文件">包含忽略文件</label>`
    : `<strong>快速打开文件</strong><span class="grow"></span><span class="menu-shortcut">Ctrl+P</span>`;
  const state = new URLSearchParams(location.search).get('search-state') || (scene === 'search-limited' ? 'limited' : 'ready');
  const notices = { ready: '3 条结果', limited: '结果超过 1000 条，已停止搜索，请缩小范围或使用更准确的关键词。', timeout: '搜索超时，已保留完成的结果。', error: '搜索表达式无效，请检查括号后重试。\n'.repeat(12).trim(), empty: '' };
  const rows = repository ? [['NativeGitPanel.cs', 'src/Augit.App', '214: Git 状态已刷新。'], ['architecture.md', 'docs', 'Git 状态以本地事实为准'], ['Win32SmokeTests.cs', 'tests', 'Assert.Contains("Git 状态")']]
    : [['NativeGitPanel.cs', 'src/Augit.App'], ['NativeGitPanelRenderingTests.cs', 'tests/Augit.App.Tests'], ['product-spec.md', 'docs']];
  const results = state === 'empty' || state === 'error' ? '' : `<div class="search-results">${rows.map(([name, directory, match], index) => `<a class="search-result${index === 0 ? ' selected' : ''}" href="text-viewer.html">${fileTypeIcon(name)}<span>${name}${match ? ` <span class="commit-meta">${match}</span>` : ''}</span><span class="commit-meta">${directory}</span></a>`).join('')}</div>`;
  const notice = repository && notices[state] ? `<div class="search-notice" role="status" tabindex="0">${notices[state]}</div>` : '';
  return `<div class="search-overlay ${repository ? 'repository-mode' : ''}" data-augit-overlay><div class="search-tabs">${searchHeader}</div><div class="search-query"><input class="search-field" value="${state === 'empty' ? '' : repository ? 'Git 状态' : 'NativeGitPanel'}" aria-label="搜索内容"></div>${results}${notice}</div>`;
}

function dialog(title, body, footer, wide = false, extraClass = "") {
  // scrim 与对话框作为同一个覆盖层整体替换，避免局部刷新后残留其一。
  return `<div class="overlay-layer" data-augit-overlay><div class="scrim"></div><section class="dialog ${wide ? "wide" : ""} ${extraClass}" role="dialog" aria-label="${title}"><div class="dialog-header"><span>${title}</span><span class="grow"></span><a class="icon-button" href="main-project.html" aria-label="关闭">${icon("x")}</a></div><div class="dialog-body">${body}</div><div class="dialog-footer"><span class="footer-help"></span>${footer}</div></section></div>`;
}

/**
 * 紧凑单行输入窗口（规格 §5.3）。
 *
 * 跳转行、检出引用、创建跟踪分支和重命名共用本结构：
 * 打开后输入框获得焦点；Tab 按「输入框 → 取消 → 确定 → 标题栏关闭」循环，Shift+Tab 反向；
 * 输入框或确定按钮上的 Enter 确认；取消或关闭按钮上的 Enter、Esc 与标题栏关闭均取消。
 * 按钮用 type="button" 明确语义，避免被当作链接触发页面跳转。
 */
function compactInputDialog(title, label, value, confirmLabel) {
  return `<div class="overlay-layer" data-augit-overlay><div class="scrim"></div><section class="dialog compact-input" role="dialog" aria-modal="true" aria-label="${escapeHtml(title)}" data-compact-dialog="${escapeHtml(title)}"><div class="dialog-header"><span>${escapeHtml(title)}</span><span class="grow"></span><button type="button" class="icon-button" data-compact-action="close" aria-label="关闭">${icon("x")}</button></div><div class="dialog-body"><label for="compact-input-field">${escapeHtml(label)}</label><input id="compact-input-field" class="text-field" aria-label="${escapeHtml(label)}" value="${escapeHtml(value || "")}" data-compact-field></div><div class="dialog-footer"><span class="footer-help"></span><button type="button" class="secondary-button" data-compact-action="cancel">取消</button><button type="button" class="primary-button" data-compact-action="confirm">${escapeHtml(confirmLabel)}</button></div></section></div>`;
}

// 外壳注入真实引用时使用：分组、当前分支标记与二级动作沿用样例版结构。
function liveBranchesPopover() {
  const references = window.__augitLive.references;
  const branches = references.branches || [];
  const tags = references.tags || [];
  const local = branches.filter(branch => !branch.isRemote);
  const remote = branches.filter(branch => branch.isRemote);
  const row = (branch, selected) => `<div class="menu-item${selected ? " selected" : ""}" data-branch="${escapeHtml(branch.name)}" data-branch-kind="${branch.isRemote ? "remote" : "branch"}">${gitReferenceIcon(branch.isRemote)} ${escapeHtml(branch.name)}${branch.isCurrent ? ' <span class="commit-meta">当前</span>' : ''}${branch.upstream ? ` <span class="commit-meta">→ ${escapeHtml(branch.upstream)}</span>` : ''}<span class="grow"></span>${icon("chevron-right")}</div>`;
  const group = (label, items) => items.length === 0
    ? ""
    : `<div class="menu-item"><span>${icon("chevron-down")}</span><strong>${label}</strong></div>${items.join("")}`;
  const checkoutError = window.__augitCheckoutError
    ? `<div class="inline-alert danger" role="alert">${escapeHtml(window.__augitCheckoutError)}</div>`
    : "";
  const quick = `<input class="search-field" placeholder="搜索分支和操作" aria-label="搜索分支和操作">${checkoutError}<a class="menu-item" href="operation-result.html" data-popover-action="fetch">${icon("branch-update")} 更新项目…</a><a class="menu-item" href="commit-changes.html" data-popover-action="commit">${icon("git-commit-horizontal")} 提交…</a><a class="menu-item" href="push.html" data-popover-action="push">${icon("branch-push")} 推送…</a><div class="menu-separator"></div><a class="menu-item" href="branches.html" data-popover-action="create-branch">${icon("plus")} 新建分支…</a><a class="menu-item" href="git-compare.html" data-popover-action="checkout-revision">${icon("git-compare-arrows")} 检出标签或版本…</a><div class="menu-separator"></div>`;
  const groups = group("本地", local.map(branch => row(branch, branch.isCurrent)))
    + group("远程", remote.map(branch => row(branch, false)))
    + group("标签", tags.map(tag => `<div class="menu-item" data-branch="${escapeHtml(tag.name)}" data-branch-kind="tag">${gitReferenceIcon(false)} ${escapeHtml(tag.name)}<span class="grow"></span>${icon("chevron-right")}</div>`));
  const current = local.find(branch => branch.isCurrent);
  const actions = current
    ? `<section class="popover branch-actions"><a class="menu-item" href="smart-checkout.html" data-branch-action="create">${icon("plus")} 从 ${escapeHtml(current.name)} 新建分支…</a><a class="menu-item" href="git-compare.html" data-popover-action="compare-workspace">${icon("git-compare-arrows")} 与工作区比较</a><a class="menu-item" href="worktrees.html" data-popover-action="create-worktree">${icon("folder-git-2")} 新建 Worktree…</a><div class="menu-separator"></div><a class="menu-item" href="push.html">${icon("branch-push")} 推送…</a><a class="menu-item" href="branches.html" data-branch-action="rename">${icon("rename")} 重命名…</a></section>`
    : "";
  return `<div class="overlay-layer" data-augit-overlay><section class="popover">${quick}${groups || '<p class="commit-meta">没有引用</p>'}</section>${actions}</div>`;
}

function branchesPopover() {
  return `<div class="overlay-layer" data-augit-overlay><section class="popover"><input class="search-field" placeholder="搜索分支和操作" aria-label="搜索分支和操作"><a class="menu-item" href="operation-result.html">${icon("branch-update")} 更新项目…</a><a class="menu-item" href="commit-changes.html">${icon("git-commit-horizontal")} 提交…</a><a class="menu-item" href="push.html">${icon("branch-push")} 推送…</a><div class="menu-separator"></div><a class="menu-item" href="branches.html">${icon("plus")} 新建分支…</a><a class="menu-item" href="git-compare.html">${icon("git-compare-arrows")} 检出标签或版本…</a><div class="menu-separator"></div><div class="menu-item"><span>${icon("chevron-down")}</span><strong>本地</strong></div><div class="menu-item selected">${gitReferenceIcon()} main <span class="grow"></span>${icon("chevron-right")}</div></section><section class="popover branch-actions"><a class="menu-item" href="smart-checkout.html">${icon("plus")} 从 main 新建分支…</a><a class="menu-item" href="git-compare.html">${icon("git-compare-arrows")} 与工作区比较</a><a class="menu-item" href="worktrees.html">${icon("folder-git-2")} 新建 Worktree…</a><div class="menu-separator"></div><a class="menu-item" href="push.html">${icon("branch-push")} 推送…</a><a class="menu-item" href="branches.html">${icon("rename")} 重命名…</a></section></div>`;
}

function renderScene() {
  const settingsBody = `<div class="settings-layout"><nav class="settings-nav"><input class="search-field" placeholder="搜索设置" style="width:100%;margin-bottom:10px"><div class="tree-row selected">外观与行为</div><div class="tree-row depth-1">外观</div><div class="tree-row">文件查看</div><div class="tree-row">Git</div><div class="tree-row">终端</div></nav><div class="settings-page"><h2>外观</h2><section class="settings-group"><div class="form-grid"><label>主题</label><select class="select-field"><option>跟随 Windows</option></select><label></label><span class="commit-meta">主题覆盖主界面和预览；字体设置只改变显示，不会修改文件。</span></div></section><section class="settings-group"><div class="form-grid"><label>界面字体</label><div class="font-setting"><input class="text-field" value="Microsoft YaHei UI"><label for="ui-font-size">字号</label><input id="ui-font-size" type="number" min="9" max="40" class="text-field" value="13"></div><label>等宽字体</label><div class="font-setting"><input class="text-field" value="Cascadia Mono"><label for="code-font-size">字号</label><input id="code-font-size" type="number" min="9" max="40" class="text-field" value="13"></div><label></label><span class="commit-meta">字体只改变显示，不会修改文件。</span></div></section><section class="settings-group"><p class="commit-meta">启动时恢复上次打开的目录和标签。</p></section></div></div>`;
  const stashBody = `<div class="form-grid"><label for="stash-root">Git 根目录</label><select id="stash-root" class="select-field"><option>D:\\github\\Augit</option></select><label>当前分支</label><span class="stash-branch">main</span><label for="stash-message">消息</label><textarea id="stash-message" class="message-field"></textarea><span></span><label class="check-line"><input id="stash-keep" type="checkbox">保留索引状态</label></div><div class="stash-notice" role="status" hidden></div>`;
  const cloneBody = `<div class="form-grid"><label for="clone-version">版本控制</label><select id="clone-version" class="select-field"><option>Git</option></select><label for="clone-source">仓库 URL</label><input id="clone-source" class="text-field" placeholder="https://example.com/team/repository.git"><label for="clone-destination">目录</label><input id="clone-destination" class="text-field" placeholder="D:\\projects\\repository"><span></span><div class="clone-shallow-row"><label class="check-line"><input id="clone-shallow" type="checkbox">浅克隆，历史截断为</label><div class="clone-depth-group disabled"><input id="clone-depth" class="text-field" value="1" inputmode="numeric" aria-label="浅克隆深度" disabled><span>个提交</span></div></div></div><div class="clone-notice" role="status" hidden></div>`;
  switch (scene) {
    case "main-project": return shell({ activeRail: "project", side: "project", editor: "markdown", bottom: "git" });
    case "text-viewer": return shell({ activeRail: "project", side: "project", editor: "text-find", selectedFile: "MainWindow.cs" });
    case "go-to-line": return shell({ activeRail: "project", side: "project", editor: "text", selectedFile: "MainWindow.cs", overlay: `<div class="scrim"></div><section class="dialog compact-input" role="dialog" aria-modal="true" aria-label="跳转行"><div class="dialog-header"><span>跳转行</span><span class="grow"></span><a class="icon-button" href="text-viewer.html" aria-label="关闭">${icon("x")}</a></div><div class="dialog-body"><label for="prompt-line">行号</label><input id="prompt-line" class="text-field" inputmode="numeric" aria-label="行号" autofocus></div><div class="dialog-footer"><a class="secondary-button" href="text-viewer.html">取消</a><a class="primary-button" href="text-viewer.html">确定</a></div></section>` });
    case "markdown-preview": return shell({ activeRail: "project", side: "project", editor: "markdown" });
    case "json-preview": return shell({ activeRail: "project", side: "project", editor: "json", selectedFile: "global.json" });
    case "image-preview": return shell({ activeRail: "project", side: "project", editor: "image", selectedFile: "image-sample.png" });
    case "file-limit": return shell({ activeRail: "project", side: "project", editor: "file-limit", selectedFile: "animation.webp" });
    case "commit-changes": return shell({ activeRail: "commit", side: "commit", editor: "markdown" });
    case "commit-diff": return shell({ activeRail: "commit", side: "commit", editor: "diff", bottom: "git", selectedFile: "app.manifest" });
    case "diff-boundary": return shell({ activeRail: "commit", side: "commit", editor: "diff", bottom: "git", selectedFile: "app.manifest", diffBoundary: true });
    case "git-history": return shell({ activeRail: "history", side: "project", editor: "markdown", bottom: "git" });
    case "git-history-graph": return shell({ activeRail: "history", side: "project", editor: "markdown", bottom: "git", complexGraph: true });
    case "file-history": return shell({ activeRail: "history", side: "project", editor: "text", bottom: "file-history", selectedFile: "product-spec.md" });
    case "git-compare": return shell({ activeRail: "history", side: "project", editor: "comparison", bottom: "git", selectedFile: "app.manifest", workspaceComparison: true });
    case "history-diff-loading":
    case "history-diff-failure":
    case "history-diff-cancelled": return shell({ activeRail: "history", side: "project", editor: "comparison", bottom: "git", selectedFile: "app.manifest", comparisonState: scene.slice("history-diff-".length) });
    case "branches": return shell({ activeRail: "commit", side: "commit", editor: "diff", bottom: "git", overlay: (window.__augitLive && window.__augitLive.references) ? liveBranchesPopover() : branchesPopover(), selectedFile: "app.manifest" });
    case "stash": return shell({ activeRail: "commit", side: "commit", editor: "diff", bottom: "git", overlay: dialog("Stash", stashBody, `<button class="secondary-button">取消</button><button class="primary-button">创建 Stash</button>`, false, "stash-dialog") });
    case "worktrees": return shell({ activeRail: "history", side: "project", editor: "text", bottom: "git", overlay: dialog("Worktree 管理", (window.__augitLive && window.__augitLive.worktrees) ? liveManagementPage("worktrees") : managementPage("worktrees"), `<a class="secondary-button" href="git-history.html">关闭</a>`, true, "worktree-dialog") });
    case "remote": return shell({ activeRail: "history", side: "project", editor: "text", bottom: "git", overlay: dialog("远端管理", (window.__augitLive && window.__augitLive.remotes) ? liveManagementPage("remote") : managementPage("remote"), `<a class="secondary-button" href="git-history.html">关闭</a>`, true, "remote-dialog") });
    case "reset": return shell({ activeRail: "history", side: "project", editor: "text", bottom: "git", overlay: dialog("Reset 当前分支", (window.__augitLive && window.__augitLive.history) ? liveResetBody() : `<div class="form-grid"><label for="reset-target">目标提交</label><input id="reset-target" class="text-field" value="dfe5c25a"><label for="reset-mode">模式</label><select id="reset-mode" class="select-field"><option>Soft · 仅移动 HEAD</option><option>Mixed · 同时重置索引</option><option selected>Hard · 重置索引和工作区</option></select></div><div class="inline-alert reset-impact danger"><strong></strong><p class="commit-meta"></p></div><div class="reset-notice" role="status" hidden></div>`, `<button class="secondary-button" type="button">取消</button><button class="reset-run danger-button" type="button">确认 Reset Hard</button>`, false, "reset-dialog") });
    case "clone": return shell({ activeRail: "project", side: "project", editor: "empty", overlay: dialog("克隆仓库", (window.__augitLive && window.__augitLive.settings) ? liveCloneBody() : cloneBody, `<button class="secondary-button">取消</button><button class="primary-button">克隆</button>`, true, "clone-dialog") });
    case "push": return shell({ activeRail: "commit", side: "commit", editor: "diff", overlay: dialog("推送提交到 Augit", (window.__augitLive && window.__augitLive.push) ? livePushDialogBody() : pushDialogBody(false), `<button class="secondary-button">取消</button><button class="primary-button">推送</button>`, true, "push-dialog") });
    case "quick-open": return shell({ activeRail: "project", side: "project", editor: "text", overlay: (window.__augitLive && window.__augitLive.search) ? liveSearchOverlay("quick") : searchOverlay("quick"), selectedFile: "MainWindow.cs" });
    case "repository-search": return shell({ activeRail: "search", side: "project", editor: "text", overlay: (window.__augitLive && window.__augitLive.search) ? liveSearchOverlay("repository") : searchOverlay("repository"), selectedFile: "NativeGitPanel.cs" });
    case "terminal": return shell({ activeRail: "terminal", side: "project", editor: "text", bottom: "terminal", selectedFile: "app.manifest" });
    case "settings": return shell({ activeRail: "project", side: "project", editor: "markdown", overlay: dialog("设置 — Augit", (window.__augitLive && window.__augitLive.settings) ? liveSettingsBody() : settingsBody, `<a class="secondary-button" href="main-project.html">取消</a><button class="secondary-button">应用</button><a class="primary-button" href="main-project.html">确定</a>`, true, "dialog-xl") });
    case "git-unavailable": return shell({ activeRail: "project", side: "project", editor: "empty", toast: `<div class="toast error"><div class="toast-title">Git 不可用</div><div>未找到 Git for Windows 2.40 或更高版本，文件浏览仍可使用。</div><div class="button-row"><a class="secondary-button" href="settings.html">配置 git.exe</a></div></div>` });
    case "operation-result": return shell({ activeRail: "commit", side: "commit", editor: "diff", toast: `<div class="toast error"><div class="toast-title">推送失败</div><div>当前仓库未配置远端，Git 没有修改本地提交或工作区。</div><div class="commit-meta" style="margin-top:5px">关闭提示后不保留命令输出或操作历史。</div></div>` });
    case "workspace-open": return shell({ activeRail: "project", side: "project", editor: "empty", overlay: dialog("打开工作区", `<div class="management-content" style="height:350px"><div class="management-list"><div class="tree-row selected">${icon("history")} 最近目录</div><div class="tree-row">${icon("folder-open")} 选择目录…</div><div class="tree-row">${icon("clone")} 克隆仓库…</div></div><div class="management-detail"><h2>最近目录</h2><a class="tree-row selected" href="main-project.html"><span>${treeFolderIcon(true)}</span><span class="tree-name">Augit</span><span class="tree-path">D:\\github\\Augit</span></a><p class="commit-meta">同一目录已经打开时激活原窗口。</p></div></div>`, `<a class="secondary-button" href="main-project.html">取消</a><button class="primary-button">打开</button>`, true) });
    case "repository-init": return shell({ activeRail: "project", side: "project", editor: "empty", overlay: dialog("初始化 Git 仓库", `<div class="info-block" style="width:auto;text-align:left"><h2>D:\\projects\\notes 不是 Git 仓库</h2><p>初始化会创建 .git 元数据，不会提交或修改现有文件。</p></div>`, `<a class="secondary-button" href="main-project.html">继续仅浏览</a><a class="primary-button" href="operation-progress.html">初始化仓库</a>`) });
    case "blame": return shell({ activeRail: "history", side: "project", editor: "blame", bottom: "file-history", selectedFile: "product-spec.md" });
    case "smart-checkout": return shell({ activeRail: "commit", side: "commit", editor: "diff", overlay: dialog("切换到 feature/ux", `<div class="inline-alert"><strong>当前改动会被目标分支覆盖</strong><p class="commit-meta">Augit 可以临时 Stash 当前改动，切换后再恢复。</p></div><div class="form-grid" style="margin-top:16px"><span>当前分支</span><span>main</span><span>目标分支</span><span>feature/ux</span><span>将暂存</span><span>8 个已跟踪文件和 2 个未跟踪文件</span></div>`, `<a class="secondary-button" href="branches.html">取消</a><a class="primary-button" href="operation-progress.html">Smart Checkout</a>`) });
    case "rollback": {
      const liveStatus = window.__augitLive && window.__augitLive.status;
      const rollbackFile = liveStatus && liveStatus.files ? (liveStatus.files.find(f => f.group === "Changes") || liveStatus.files[0]) : null;
      const title = rollbackFile ? `回滚文件 · ${rollbackFile.path}` : "回滚文件 · src/Augit.App/app.manifest";
      const body = rollbackFile ? liveRollbackBody() : `<div class="inline-alert danger rollback-impact"><strong>将丢失此文件的全部本地改动</strong><p class="commit-meta">回滚完整文件，不能只回滚选中的差异块。</p><p class="rollback-recycle" hidden>未跟踪或新增文件将移入 Windows 回收站。</p></div><div class="rollback-comparison">${diffView(true, "ready", true)}</div>`;
      return shell({ activeRail: "commit", side: "commit", editor: "diff", overlay: dialog(title, body, `<a class="secondary-button" href="commit-diff.html">取消</a><a class="danger-button" href="operation-progress.html">回滚完整文件</a>`, true, "rollback-dialog") });
    }
    case "operation-progress": return shell({ activeRail: "commit", side: "commit", editor: "diff", toast: `<div class="toast"><div class="toast-title">正在执行 Smart Checkout…</div><div class="commit-meta">正在恢复临时 Stash</div><div class="progress-track"><div class="progress-value"></div></div><div class="button-row"><a class="secondary-button" href="operation-result.html">取消</a></div></div>` });
    case "terminal-close": return shell({ activeRail: "terminal", side: "project", editor: "text", bottom: "terminal", overlay: dialog("关闭终端", `<div class="info-block" style="width:auto;text-align:left"><h2>终端中仍有命令正在运行</h2><p><code>dotnet test Augit.slnx -c Release</code></p><p>继续将结束前台命令、Shell 及其整个子进程树。</p></div>`, `<a class="secondary-button" href="terminal.html">保留终端</a><a class="danger-button" href="main-project.html">结束命令并关闭</a>`) });
    case "search-limited": return shell({ activeRail: "search", side: "project", editor: "text", overlay: searchOverlay("repository"), selectedFile: "NativeGitPanel.cs" });
    case "conflict-list": return shell({ activeRail: "commit", side: "commit", editor: "empty", overlay: dialog("Rebase 冲突", `<div class="toolbar"><strong>2 个冲突文件</strong><span class="grow"></span><span class="commit-meta">当前步骤 2/4</span></div><div class="changes-list"><a class="check-row selected" href="conflict-resolver.html"><span style="color:var(--augit-red)">${icon("conflict")}</span><span>NativeGitPanel.cs</span><span class="tree-path">内容冲突</span></a><a class="check-row" href="conflict-resolver.html"><span style="color:var(--augit-red)">${icon("conflict")}</span><span>MainWindow.cs</span><span class="tree-path">内容冲突</span></a></div>`, `<a class="secondary-button" href="main-project.html">Abort Rebase</a><button class="secondary-button">Skip</button><button class="primary-button" disabled>Continue Rebase</button>`, true) });
    case "conflict-resolver": return shell({ activeRail: "commit", side: "commit", editor: "empty", overlay: dialog("解决冲突", (window.__augitLive && window.__augitLive.conflict) ? liveConflictResolver() : conflictResolver(), `<a class="secondary-button" href="conflict-list.html">返回冲突列表</a>`, true, "dialog-xl") });
    case "commit-empty": return shell({ activeRail: "commit", side: "commit-empty", editor: "markdown" });
    case "diff-loading": return shell({ activeRail: "commit", side: "commit", editor: "diff-loading", bottom: "git", selectedFile: "app.manifest" });
    case "push-no-remote": return shell({ activeRail: "commit", side: "commit", editor: "diff", overlay: dialog("推送提交到 Augit", pushDialogBody(true), `<button class="secondary-button">取消</button><button class="primary-button" disabled>推送</button>`, true, "push-dialog push-no-remote") });
    case "stash-manager": return shell({ activeRail: "history", side: "project", editor: "text", bottom: "git", overlay: dialog("Stash 管理", (window.__augitLive && window.__augitLive.stashes) ? liveManagementPage("stash") : managementPage("stash"), `<a class="secondary-button" href="git-history.html">关闭</a>`, true, "stash-manager-dialog") });
    case "project-context-menu": return shell({ activeRail: "project", side: "project", editor: "markdown", bottom: "terminal", overlay: projectContextMenu() });
    case "changes-context-menu": return shell({ activeRail: "commit", side: "commit", editor: "diff", bottom: "git", overlay: changesContextMenu(), selectedFile: "app.manifest" });
    case "git-history-menu": return shell({ activeRail: "history", side: "project", editor: "markdown", bottom: "git", overlay: gitLogContextMenu() });
    case "quick-open-empty": return shell({ activeRail: "project", side: "project", editor: "text", overlay: quickOpenEmpty(), selectedFile: "MainWindow.cs" });
    default: return shell();
  }
}

function bindHistoryToolbar(root = document) {
  root.querySelectorAll(".git-side-toolbar").forEach(toolbar => {
    const buttons = [...toolbar.querySelectorAll("button")];
    const trigger = document.createElement("button");
    trigger.className = "toolbar-button history-tools-more";
    trigger.setAttribute("aria-label", "更多历史工具");
    trigger.setAttribute("aria-expanded", "false");
    trigger.innerHTML = icon("chevron-right");
    toolbar.append(trigger);
    const popup = document.createElement("div");
    popup.className = "history-tools-popup";
    popup.setAttribute("popover", "auto");
    popup.setAttribute("role", "toolbar");
    popup.setAttribute("aria-label", "历史工具");
    toolbar.append(popup);
    const layout = () => {
      let focus = document.activeElement;
      if (popup.contains(focus)) focus = trigger;
      if (popup.matches(":popover-open")) popup.hidePopover();
      const filterHeight = parseFloat(getComputedStyle(toolbar).getPropertyValue("--augit-history-filter-height")) || 36;
      const tops = [4, ...Array.from({ length: 6 }, (_, index) => filterHeight + 13 + index * 30)];
      const fitting = tops.filter(top => top + 28 <= toolbar.clientHeight - 4).length;
      const visible = fitting === buttons.length ? fitting : fitting === 0 ? 0 : Math.max(1, fitting - 1);
      const overflowTop = Math.min(tops[visible] ?? 0, toolbar.clientHeight - 32);
      const showOverflow = visible > 0 && visible < buttons.length && overflowTop >= tops[visible - 1] + 30;
      buttons.forEach((button, index) => {
        button.hidden = index >= visible;
        button.style.top = `${tops[index]}px`;
      });
      toolbar.querySelector(".rail-separator").style.top = `${filterHeight + 1}px`;
      trigger.hidden = !showOverflow;
      trigger.style.top = `${overflowTop}px`;
      if ((buttons.includes(focus) && focus.hidden) || focus === trigger) {
        const target = showOverflow ? trigger : buttons.slice().reverse().find(button => !button.hidden && !button.disabled);
        target?.focus();
      }
    };
    new ResizeObserver(layout).observe(toolbar);
    layout();
    trigger.addEventListener("click", () => {
      if (popup.matches(":popover-open")) { popup.hidePopover(); return; }
      popup.replaceChildren(...buttons.slice(1).map(button => {
        const copy = button.cloneNode(true);
        copy.hidden = false;
        copy.removeAttribute("style");
        copy.addEventListener("click", () => {
          popup.hidePopover();
          trigger.focus();
          if (copy.getAttribute("aria-label") === "搜索") {
            toolbar.closest(".git-toolbar-layout").querySelector(".history-filters input").focus();
          } else button.click();
        });
        return copy;
      }));
      const anchor = trigger.getBoundingClientRect();
      popup.showPopover();
      popup.style.left = `${Math.max(8, Math.min(anchor.left, innerWidth - popup.offsetWidth - 8))}px`;
      popup.style.top = `${Math.max(8, Math.min(anchor.top, innerHeight - popup.offsetHeight - 8))}px`;
      popup.querySelector("button:not(:disabled)")?.focus();
    });
    popup.addEventListener("toggle", () => trigger.setAttribute("aria-expanded", String(popup.matches(":popover-open"))));
    popup.addEventListener("keydown", event => {
      if (event.key === "ArrowLeft" || event.key === "ArrowRight" || event.key === "Tab") {
        event.preventDefault();
        const available = [...popup.querySelectorAll("button:not(:disabled)")];
        const index = available.indexOf(document.activeElement);
        const backwards = event.key === "ArrowLeft" || event.key === "Tab" && event.shiftKey;
        available[(index + (backwards ? -1 : 1) + available.length) % available.length]?.focus();
      }
      if (event.key === "Escape") { event.preventDefault(); popup.hidePopover(); trigger.focus(); }
    });
  });
}

function bindHistoryLayout(root = document) {
  const lifetime = new AbortController();
  const signal = lifetime.signal;
  registerRegionDisposer(() => lifetime.abort());
  root.querySelectorAll(".log-list-panel").forEach(panel => {
    const bar = panel.querySelector(".history-filters");
    const buttons = [...bar.querySelectorAll(".history-filter")];
    const overflow = bar.querySelector("details");
    const menuButtons = [...overflow.querySelectorAll("button")];
    const input = bar.querySelector("input");
    const list = panel.querySelector(".commit-list");
    const rows = [...panel.querySelectorAll(".commit-row")];
    const context = document.createElement("canvas").getContext("2d");
    const layout = () => {
      const focus = document.activeElement;
      const trigger = overflow.querySelector("summary");
      const style = getComputedStyle(bar);
      context.font = `${style.fontSize} ${style.fontFamily}`;
      const measure = text => Math.ceil(context.measureText(text).width);
      const buttonWidth = Math.max(48, Math.ceil((Math.max(...buttons.map(button => measure(button.textContent))) + 24) / 4) * 4);
      const available = Math.max(0, panel.clientWidth - 76);
      let visible = 4;
      const slots = () => visible * (buttonWidth + 4) + (visible < 4 ? 32 : 0);
      while (visible > 0 && available - slots() - 4 < 110) visible--;
      buttons.forEach((button, index) => {
        button.hidden = index >= visible;
        button.style.width = `${buttonWidth}px`;
        menuButtons[index].hidden = index < visible;
      });
      overflow.hidden = visible === 4;
      if (overflow.hidden) overflow.open = false;
      if ((buttons.includes(focus) && focus.hidden) || focus === trigger && overflow.hidden) {
        (overflow.hidden ? buttons.slice().reverse().find(button => !button.hidden && !button.disabled) : trigger)?.focus();
      }
      bar.querySelector(".history-search").style.width = `${Math.min(230, Math.max(0, available - slots() - 4))}px`;

      const authorWidth = Math.min(96, Math.max(...rows.map(row => measure(row.querySelector(".commit-author").textContent))));
      const referenceWidth = Math.min(128, Math.max(...rows.map(row => {
        const text = row.querySelector(".branch-label").textContent.trim();
        return text ? measure(text) + 20 : 0;
      })));
      const fullWidth = Math.max(...rows.map(row => measure(row.querySelector("time").dataset.full)));
      const compactWidth = Math.max(...rows.map(row => measure(row.querySelector("time").dataset.compact)));
      // 复杂提交图占用完整的多轨宽度，不能沿用单轨圆点宽度而让连线覆盖标题。
      const graph = panel.querySelector(".commit-graph-svg");
      const graphWidth = graph ? Number(graph.viewBox.baseVal.width) : 29;
      const rowWidth = Math.max(list.clientWidth, graphWidth + 120 + 8
        + (authorWidth > 0 ? Math.min(authorWidth, 48) + 8 : 0) + (compactWidth > 0 ? compactWidth + 8 : 0));
      const contentWidth = Math.max(0, rowWidth - graphWidth - 8);
      const budget = Math.max(0, contentWidth - Math.min(contentWidth, 120) - 8);
      const compact = fullWidth + Math.min(authorWidth, 48) + 8 > budget;
      const date = Math.min(budget, compact ? compactWidth : fullWidth);
      const author = Math.min(authorWidth, Math.max(0, budget - date - 8));
      const referenceBudget = Math.max(0, budget - date - (author ? author + 8 : 0) - 8);
      const reference = referenceBudget >= Math.min(referenceWidth, 64) ? Math.min(referenceWidth, referenceBudget) : 0;
      rows.forEach(row => {
        const columns = [`${graphWidth}px`, "minmax(0, 1fr)"];
        [[".branch-label", reference], [".commit-author", author], [".commit-date", date]].forEach(([selector, width]) => {
          row.querySelector(selector).hidden = width === 0;
          if (width) columns.push(`${width + 8}px`);
        });
        row.style.width = `${rowWidth}px`;
        row.style.gridTemplateColumns = columns.join(" ");
        const time = row.querySelector("time");
        time.textContent = compact ? time.dataset.compact : time.dataset.full;
      });
    };
    const observer = new ResizeObserver(layout);
    observer.observe(panel);
    observer.observe(list);
    layout();
    list.setAttribute("role", "listbox");
    list.setAttribute("aria-label", "提交历史");
    const select = index => {
      if (index < 0 || index >= rows.length) return;
      list.focus({ preventScroll: true });
      if (rows[index].getAttribute("aria-selected") === "true") return;
      rows.forEach((row, current) => {
        row.classList.toggle("selected", current === index);
        row.classList.toggle("inactive", false);
        row.setAttribute("aria-selected", String(current === index));
      });
      const row = rows[index];
      const detail = panel.parentElement.querySelector(".commit-detail");
      detail.replaceChildren();
      const title = document.createElement("h3"), meta = document.createElement("div");
      title.textContent = row.querySelector(".commit-subject").textContent;
      meta.textContent = `${row.dataset.hash} · ${row.querySelector(".commit-author").textContent} · ${row.querySelector("time").dataset.full}`;
      detail.append(title, meta);
      detail.scrollTo({ top: 0, behavior: "instant" });
      panel.parentElement.querySelector(".changed-files").innerHTML = historySampleFilesHtml(title.textContent);
      panel.dispatchEvent(new CustomEvent("history-commit-selected", { bubbles: true }));
      if (row.offsetTop < list.scrollTop) list.scrollTop = row.offsetTop;
      else if (row.offsetTop + row.offsetHeight > list.scrollTop + list.clientHeight)
        list.scrollTop = row.offsetTop + row.offsetHeight - list.clientHeight;
    };
    list.addEventListener("click", event => {
      const row = event.target.closest(".commit-row");
      if (row) select(rows.indexOf(row));
    });
    list.addEventListener("keydown", event => {
      if (!["ArrowUp", "ArrowDown", "Home", "End"].includes(event.key)) return;
      event.preventDefault();
      const current = rows.findIndex(row => row.classList.contains("selected"));
      select(event.key === "Home" ? 0 : event.key === "End" ? rows.length - 1
        : Math.max(0, Math.min(rows.length - 1, current + (event.key === "ArrowUp" ? -1 : 1))));
    });
    overflow.addEventListener("toggle", () => {
      if (!overflow.open) return;
      const anchor = overflow.getBoundingClientRect();
      const menu = overflow.querySelector(".history-filter-menu");
      menu.style.left = `${Math.max(8, Math.min(anchor.left, innerWidth - menu.offsetWidth - 8))}px`;
      menu.style.top = `${Math.max(8, Math.min(anchor.bottom, innerHeight - menu.offsetHeight - 8))}px`;
    });
    bar.addEventListener("click", event => {
      const button = event.target.closest("[data-history-filter]");
      if (!button) return;
      input.placeholder = ["分支", "用户", "日期", "路径"][Number(button.dataset.historyFilter)];
      overflow.open = false;
      input.focus();
    });
    document.addEventListener("click", event => {
      // 该监听由本区域的筛选菜单持有，区域替换时随 signal 一并释放。
      if (!overflow.contains(event.target)) overflow.open = false;
    }, { signal });
    overflow.addEventListener("keydown", event => {
      if (event.key === "Escape") {
        overflow.open = false;
        overflow.querySelector("summary").focus();
      }
    });
  });
}

function bindHistoryDetails(root = document) {
  root.querySelectorAll(".log-detail-panel").forEach(panel => {
    const detail = panel.querySelector(".commit-detail");
    const files = panel.querySelector(".changed-files");
    detail.tabIndex = 0;
    detail.setAttribute("aria-label", "提交详情");
    detail.addEventListener("keydown", event => {
      const line = parseFloat(getComputedStyle(detail).fontSize) * 1.55;
      const page = Math.max(line, detail.clientHeight - line);
      const next = { Home: 0, End: detail.scrollHeight, ArrowUp: detail.scrollTop - line,
        ArrowDown: detail.scrollTop + line, PageUp: detail.scrollTop - page, PageDown: detail.scrollTop + page }[event.key];
      if (next === undefined) return;
      event.preventDefault();
      detail.scrollTo({ top: next, behavior: "instant" });
    });
    const actions = document.createElement("div");
    actions.className = "history-detail-actions";
    actions.innerHTML = '<strong>提交详情</strong><a class="toolbar-button" href="file-history.html">文件历史</a><a class="toolbar-button" href="blame.html">Blame</a>';
    panel.insertBefore(actions, detail);
    const arrange = () => {
      const context = document.createElement("canvas").getContext("2d");
      const style = getComputedStyle(panel);
      context.font = `${style.fontSize} ${style.fontFamily}`;
      const required = Math.max(280, Math.max(84, context.measureText("提交详情").width + 13)
        + Math.max(82, context.measureText("文件历史").width + 16)
        + Math.max(66, context.measureText("Blame").width + 16) + 30);
      const visible = panel.clientWidth >= required && panel.clientHeight >= 150;
      if (!visible && actions.contains(document.activeElement)) files.focus({ preventScroll: true });
      actions.hidden = !visible;
      panel.style.setProperty("--detail-actions-height", visible ? "var(--augit-history-tab-height, 24px)" : "0px");
    };
    const observer = new ResizeObserver(arrange);
    observer.observe(panel);
    arrange();
    const normalize = () => {
      const title = detail.querySelector("h3");
      if (title) title.title = title.textContent;
      const meta = title?.nextElementSibling;
      if (meta) { meta.classList.add("commit-detail-meta"); meta.title = meta.textContent; }
      detail.querySelectorAll("p").forEach(body => { body.className = "commit-detail-body"; });
    };
    normalize();
    new MutationObserver(normalize).observe(detail, { childList: true });
    if (new URLSearchParams(location.search).get("details") === "long") {
      const body = document.createElement("p");
      body.className = "commit-detail-body";
      body.textContent = Array.from({ length: 200 }, (_, index) => `第 ${String(index).padStart(3, "0")} 段：这是一段完整提交说明，用于检查窄栏自然换行。`).join("\n") + "\n最后一段必须可见。";
      detail.append(body);
    }
    const selectFile = row => {
      files.querySelectorAll(".selected").forEach(previous => previous.classList.remove("selected", "inactive"));
      row.classList.add("selected");
      files.focus({ preventScroll: true });
    };
    files.addEventListener("click", event => {
      const row = event.target.closest(".file-status-modified, .file-status-added, .file-status-deleted");
      if (row) selectFile(row);
    });
    files.addEventListener("keydown", event => {
      if (event.key !== "ContextMenu" && !(event.shiftKey && event.key === "F10")) return;
      const row = files.querySelector(".selected") || files.querySelector(".file-status-modified, .file-status-added, .file-status-deleted");
      if (!row) return;
      event.preventDefault();
      row.scrollIntoView({ block: "nearest" });
      const bounds = row.getBoundingClientRect();
      row.dispatchEvent(new MouseEvent("contextmenu", { bubbles: true, cancelable: true,
        clientX: bounds.left, clientY: bounds.bottom }));
    });
    files.addEventListener("contextmenu", event => {
      const row = event.target.closest(".file-status-modified, .file-status-added, .file-status-deleted");
      if (!row) return;
      event.preventDefault();
      selectFile(row);
      document.querySelector(".history-files-menu")?.remove();
      const menu = document.createElement("div");
      menu.className = "popover context-menu history-files-menu";
      menu.setAttribute("role", "menu");
      menu.innerHTML = '<a class="menu-item" href="git-compare.html">显示 Diff</a><a class="menu-item" href="file-history.html">文件历史</a><a class="menu-item" href="blame.html">Blame</a>';
      document.body.append(menu);
      menu.style.position = "fixed";
      menu.style.left = `${Math.max(0, Math.min(event.clientX, innerWidth - menu.offsetWidth))}px`;
      menu.style.top = `${Math.max(0, Math.min(event.clientY, innerHeight - menu.offsetHeight))}px`;
      const links = [...menu.querySelectorAll("a")];
      links[0].focus();
      const close = () => { menu.remove(); files.focus({ preventScroll: true }); };
      menu.addEventListener("keydown", input => {
        if (input.key === "Escape") { input.preventDefault(); close(); }
        if (["ArrowDown", "ArrowUp"].includes(input.key)) {
          input.preventDefault();
          links[(links.indexOf(document.activeElement) + (input.key === "ArrowDown" ? 1 : 2)) % 3].focus();
        }
      });
      menu.addEventListener("focusout", () => queueMicrotask(() => { if (!menu.contains(document.activeElement)) menu.remove(); }));
    });
  });
}

// 历史比较只维护一个跟随标签；普通文档在前台时后台更新，返回比较才激活。
function bindHistoryComparisonFollow(root = document) {
  if (scene !== "git-history") return;
  const log = root.querySelector(".git-log"), files = log?.querySelector(".changed-files");
  const tabs = root.querySelector(".editor-tabs"), content = root.querySelector(".editor-content");
  if (!files || !tabs || !content) return;
  const ordinaryTabs = [...tabs.querySelectorAll(".editor-tab")];
  let documentTab = ordinaryTabs.find(tab => tab.classList.contains("active"));
  let comparison = null, comparisonActive = false;
  const documents = new Map([[documentTab, content.firstElementChild]]);
  const currentFile = () => files.querySelector("[data-history-path].selected");
  const currentHash = () => log.querySelector('.commit-row[aria-selected="true"]')?.dataset.hash || "";
  const show = (tab, view) => {
    tabs.querySelectorAll(".editor-tab").forEach(item => item.classList.toggle("active", item === tab));
    [...content.children].forEach(item => { item.hidden = item !== view; });
  };
  const showDocument = tab => {
    documentTab = tab; comparisonActive = false;
    let view = documents.get(tab);
    if (!view) {
      const template = document.createElement("template"), name = tab.textContent.trim();
      template.innerHTML = `<div class="document-view"><div class="document-toolbar"><span class="document-path">${escapeHtml(name)}</span></div><div class="code-view" tabindex="0">${codeLines([`# ${name}`, "", "只读文档视觉审计样本。"], 0)}</div></div>`;
      view = template.content.firstElementChild; content.append(view); documents.set(tab, view);
      measureCodeViews();
    }
    show(tab, view);
  };
  const activate = () => {
    if (!comparison) return;
    comparisonActive = true; show(comparison.tab, comparison.view);
    comparison.view.focus({ preventScroll: true });
  };
  const close = () => {
    const active = comparisonActive;
    comparison.tab.remove(); comparison.view.remove(); comparison = null; comparisonActive = false;
    if (active) { showDocument(documentTab); files.focus({ preventScroll: true }); }
  };
  const update = row => {
    if (!comparison) return;
    const path = row?.dataset.historyPath || "", hash = currentHash();
    if (comparison.path === path && comparison.hash === hash) return;
    comparison.path = comparison.tab.dataset.path = path;
    comparison.hash = comparison.tab.dataset.commit = hash;
    comparison.tab.querySelector(".comparison-caption").textContent = `比较: ${path.split("/").at(-1) || "选择文件"} · ${hash.slice(0, 8)}^ → ${hash.slice(0, 8)}`;
    comparison.tab.title = path;
    const mode = comparison.view.querySelector(".diff-layout")?.dataset.diffMode;
    comparison.view.innerHTML = path ? diffView(true, "ready", false, false, path, hash)
      : '<div class="comparison-notice">选择提交中的文件以查看差异。</div>';
    const header = comparison.view.querySelector(".diff-filebar");
    if (header) header.outerHTML = diffFileHeader(`${hash.slice(0, 8)}^`, hash.slice(0, 8), `${hash}^ → ${hash}`, path);
    bindDiffModes(comparison.view);
    if (mode === "unified") comparison.view.querySelector('[aria-label="单栏"]')?.click();
  };
  const open = row => {
    if (!row) return;
    if (!comparison) {
      const tab = document.createElement("a"), view = document.createElement("div");
      tab.className = "editor-tab comparison-tab"; tab.href = "#"; tab.dataset.historyComparison = "true";
      tab.innerHTML = `${icon("git-compare-arrows")}<span class="comparison-caption"></span><button type="button" class="tab-close" aria-label="关闭比较">${icon("x")}</button>`;
      tabs.querySelector(":scope > span").before(tab);
      view.className = "document-view history-follow-view"; view.tabIndex = 0; content.append(view);
      comparison = { tab, view, path: null, hash: null };
      tab.addEventListener("click", event => { event.preventDefault(); if (!event.target.closest(".tab-close")) activate(); });
      tab.querySelector(".tab-close").addEventListener("pointerdown", event => event.preventDefault());
      tab.querySelector(".tab-close").addEventListener("click", event => { event.preventDefault(); event.stopPropagation(); close(); });
    }
    update(row); activate();
  };
  ordinaryTabs.forEach(tab => tab.addEventListener("click", event => { event.preventDefault(); showDocument(tab); }));
  files.addEventListener("click", event => {
    const row = event.target.closest("[data-history-path]");
    if (!row) return;
    files.querySelectorAll(".selected").forEach(item => item.classList.remove("selected"));
    row.classList.add("selected"); update(row);
  });
  files.addEventListener("dblclick", event => open(event.target.closest("[data-history-path]")));
  files.addEventListener("keydown", event => {
    if (event.key === "Enter") { event.preventDefault(); open(currentFile()); }
  });
  log.addEventListener("history-commit-selected", () => {
    if (!comparison) return;
    const row = [...files.querySelectorAll("[data-history-path]")].find(item => item.dataset.historyPath === comparison.path)
      || files.querySelector("[data-history-path]");
    row?.classList.add("selected"); update(row);
  });
  document.addEventListener("click", event => {
    const link = event.target.closest(".history-files-menu .menu-item");
    if (link?.textContent === "显示 Diff") { event.preventDefault(); link.closest(".history-files-menu").remove(); open(currentFile()); }
  });
}

function bindSelectionFocus(root = document) {
  // 与原生工具列表一致：焦点只改变选中背景，不取消选择或执行链接。
  root.querySelectorAll(".side-content.tree, .changes-layout > .changes-list, .log-ref-panel > .tree, .commit-list, .log-detail-panel > .changed-files, .history-row").forEach(region => {
    region.tabIndex = 0;
    region.dataset.selectionRegion = "true";
    const update = () => {
      const inactive = !region.contains(document.activeElement);
      const rows = region.matches(".selected") ? [region] : region.querySelectorAll(".selected");
      rows.forEach(row => row.classList.toggle("inactive", inactive));
    };
    region.addEventListener("focusin", update);
    region.addEventListener("focusout", () => queueMicrotask(update));
    update();
  });
}

function bindDiffModes(root = document) {
  root.querySelectorAll(".diff-layout").forEach(layout => {
    if (layout.dataset.modesBound) return;
    layout.dataset.modesBound = "true";
    const body = layout.querySelector(":scope > .diff-columns");
    const template = layout.querySelector(".diff-unified-template");
    const split = body?.innerHTML;
    const buttons = [...layout.querySelectorAll(".diff-toolbar .segmented button")];
    let boundaryDismissed = false;
    const update = unified => {
      const mode = unified ? "unified" : "side-by-side";
      if (layout.dataset.diffMode === mode) return;
      boundaryDismissed ||= !!layout.dataset.diffMode;
      layout.dataset.diffMode = mode;
      buttons.forEach(button => {
        const active = button.getAttribute("aria-label") === (unified ? "单栏" : "双栏");
        button.classList.toggle("active", active);
        button.setAttribute("aria-pressed", String(active));
      });
      if (body && template) {
        body.classList.toggle("diff-unified-body", unified);
        body.innerHTML = unified ? template.innerHTML : split;
        if (boundaryDismissed) body.querySelector(".diff-boundary-hint")?.remove();
      }
    };
    buttons.forEach(button => button.addEventListener("click", () => {
      const unified = button.getAttribute("aria-label") === "单栏";
      update(unified);
      // 外壳存在时同步显示模式：相同内容只重新排版，不重新查询 Git（§6.3）。
      if (window.__augitLive && window.__augitLive.diff && typeof window.__augitLoadDiffMode === "function") {
        window.__augitLoadDiffMode(unified ? "unified" : "side-by-side");
      }
    }));
    update(new URLSearchParams(window.location.search).get("diffMode") === "unified");
  });
}

// Changes 工作流：行选择、提交勾选和打开 Diff 是三个互不混用的动作。
function bindChangesWorkflow() {
  const list = document.querySelector(".changes-layout > .changes-list");
  if (!list) return;
  const side = list.closest(".side-tool"), tabs = document.querySelector(".editor-tabs");
  const content = document.querySelector(".editor-content"), status = document.querySelector(".statusbar");
  const rows = [...list.querySelectorAll(".change-file-row")];
  const groups = [...list.querySelectorAll(".check-group-row")];
  const allRows = [...list.querySelectorAll(".check-row")];
  const ordinaryTabs = [...tabs.querySelectorAll(".editor-tab")].filter(tab => !tab.textContent.trim().startsWith("提交:"));
  const documents = new Map();
  let documentTab = ordinaryTabs.find(tab => tab.classList.contains("active")) || ordinaryTabs.at(-1);
  let activeComparison = null, preview = null;
  if (!content.querySelector(".diff-layout")) documents.set(documentTab, content.firstElementChild);
  const groupFor = row => groups.find(group => group.dataset.group === row.dataset.group);
  const selected = () => list.querySelector(".check-row.selected");
  const selectedFile = () => list.querySelector(".change-file-row.selected:not([hidden])");
  const updateActions = () => {
    side.querySelectorAll('[data-action="show-change-diff"], .toolbar [aria-label="回滚"]').forEach(button => {
      button.disabled = !selectedFile();
    });
  };
  const setStatus = (path, comparison) => {
    const label = status.querySelector(".status-path");
    label.textContent = ["Augit", ...path.split("/")].join("  ›  ");
    label.title = "D:\\github\\Augit\\" + path.replaceAll("/", "\\");
    status.querySelector(".status-fields").innerHTML = (comparison ? ["只读"] : ["UTF-8", "LF", "只读"])
      .map(text => "<span>" + text + "</span>").join("");
  };
  const activate = (tab, view) => {
    tabs.querySelectorAll(".editor-tab").forEach(item => item.classList.toggle("active", item === tab));
    [...content.children].forEach(item => { item.hidden = item !== view; });
  };
  const showDocument = tab => {
    documentTab = tab;
    activeComparison = null;
    let view = documents.get(tab);
    const name = tab.textContent.trim();
    if (!view) {
      const holder = document.createElement("template");
      holder.innerHTML = name === "product-spec.md" ? markdownView()
        : '<div class="document-view"><div class="document-toolbar"><span class="document-path">'
          + escapeHtml(name) + '　只读</span></div><div class="code-view" tabindex="0">'
          + codeLines(name === "roadmap.md" ? ["# 实施路线", "", "按 UX 规格逐页完成视觉和交互验收。"]
            : ["# 第三方组件声明", "", "视觉审计使用本地测试数据。"], 0) + "</div></div>";
      view = holder.content.firstElementChild;
      content.append(view);
      documents.set(tab, view);
      if (name === "product-spec.md") bindMarkdownModes();
      measureCodeViews();
    }
    activate(tab, view);
    setStatus(name === "THIRD-PARTY-NOTICES.md" ? name : "docs/" + name, false);
  };
  const updateComparison = (comparison, row) => {
    if (comparison.path === row.dataset.path) return;
    comparison.path = comparison.tab.dataset.path = row.dataset.path;
    comparison.tab.querySelector(".change-tab-caption").textContent = "提交: " + row.dataset.file;
    comparison.tab.title = comparison.path;
    const holder = document.createElement("template");
    holder.innerHTML = diffView(false, "ready", false, false, comparison.path);
    const view = holder.content.firstElementChild, previous = comparison.view;
    const mode = previous?.dataset.diffMode;
    view.hidden = activeComparison !== comparison;
    if (previous) previous.replaceWith(view); else content.append(view);
    comparison.view = view;
    bindDiffModes(content);
    if (mode === "unified") view.querySelector('[aria-label="单栏"]').click();
    view.querySelector(".diff-toolbar > .file-status-modified").textContent =
      (changesSamples.findIndex(file => file.path === comparison.path) + 1) + "/" + changesSamples.length + " 个文件";
    const position = changesSamples.findIndex(file => file.path === comparison.path);
    for (const [label, offset] of [["上一个文件", -1], ["下一个文件", 1]]) {
      const button = view.querySelector('[aria-label="' + label + '"]');
      button.disabled = !changesSamples[position + offset];
      button.addEventListener("click", () => {
        const file = changesSamples[position + offset];
        if (!file) return;
        const target = rows.find(item => item.dataset.path === file.path);
        if (target.hidden) toggleCollapsed(groupFor(target));
        openDiff(target);
        target.scrollIntoView({ block: "nearest" });
      });
    }
    if (activeComparison === comparison) setStatus(comparison.path, true);
    measureCodeViews();
  };
  const activateComparison = comparison => {
    activeComparison = comparison;
    if (!comparison.tab) return;
    activate(comparison.tab, comparison.view);
    setStatus(comparison.path, true);
  };
  const closeComparison = comparison => {
    const active = activeComparison === comparison;
    if (!comparison.tab) return;
    const heldFocus = comparison.tab.contains(document.activeElement) || comparison.view.contains(document.activeElement);
    if (preview === comparison) preview = null;
    comparison.tab.remove();
    comparison.view.remove();
    if (active) showDocument(documentTab);
    if (heldFocus) list.focus({ preventScroll: true });
  };
  const createComparison = (row, existingTab = null, existingView = null) => {
    // 实时外壳下，标签栏由 live.tabs 渲染（规格 §5.2），
    // 这里不再另建一个标签节点，否则两套标签会互相覆盖：
    // 区域刷新替换 .editor-tabs 后，这里持有的旧引用会往游离节点里插标签，
    // 界面上就出现「比较标签突然消失」。
    if (window.__augitLive && Array.isArray(window.__augitLive.tabs) && window.__augitLive.tabs.length > 0) {
      return { tab: null, view: existingView || null, path: row.dataset.path, live: true };
    }

    const tab = existingTab || document.createElement("a");
    tab.className = "editor-tab";
    tab.href = "#";
    tab.dataset.workspaceDiffTab = "true";
    tab.innerHTML = icon("git-compare-arrows") + '<span class="change-tab-caption">提交: '
      + escapeHtml(row.dataset.file) + '</span><button type="button" class="tab-close" aria-label="关闭比较">'
      + icon("x") + "</button>";
    if (!existingTab) tabs.querySelector(":scope > span").before(tab);
    const comparison = { tab, view: existingView, path: existingView ? row.dataset.path : null };
    tab.addEventListener("click", event => {
      event.preventDefault();
      if (!event.target.closest(".tab-close")) activateComparison(comparison);
    });
    const close = tab.querySelector(".tab-close");
    close.addEventListener("pointerdown", event => { if (event.button === 0) event.preventDefault(); });
    close.addEventListener("click", event => {
      event.preventDefault(); event.stopPropagation(); closeComparison(comparison);
    });
    if (!existingView) updateComparison(comparison, row);
    else { tab.dataset.path = comparison.path; tab.title = comparison.path; }
    return comparison;
  };
  const select = row => {
    const changed = selected() !== row;
    allRows.forEach(item => {
      item.classList.toggle("selected", item === row);
      item.classList.remove("inactive");
      item.setAttribute("aria-selected", String(item === row));
    });
    list.setAttribute("aria-activedescendant", row.id);
    list.focus({ preventScroll: true });
    updateActions();
    if (changed && row.matches(".change-file-row") && preview) {
      const foreground = !!activeComparison;
      updateComparison(preview, row);
      if (foreground) activateComparison(preview);
    }
  };
  const openDiff = row => {
    if (!row) return;
    select(row);
    const comparison = preview || createComparison(row);
    preview = comparison;
    updateComparison(comparison, row);
    activateComparison(comparison);
  };
  const setCheck = (checkbox, state) => {
    checkbox.setAttribute("aria-checked", state);
    checkbox.classList.toggle("checked", state === "true");
    checkbox.classList.toggle("mixed", state === "mixed");
  };
  const updateChecks = () => {
    groups.forEach(group => {
      const items = rows.filter(row => row.dataset.group === group.dataset.group);
      const checked = items.filter(row => row.querySelector(".fake-check").getAttribute("aria-checked") === "true").length;
      setCheck(group.querySelector(".fake-check"), checked === 0 ? "false" : checked === items.length ? "true" : "mixed");
    });
    const included = rows.filter(row => row.querySelector(".fake-check").getAttribute("aria-checked") === "true");
    const modified = included.filter(row => row.dataset.group === "Changes").length;
    const count = side.querySelector(".commit-count");
    count.textContent = count.title = modified + " modified" + (included.length > modified ? ", " + (included.length - modified) + " added" : "");
  };
  const toggleCheck = row => {
    const checkbox = row.querySelector(".fake-check");
    const checked = checkbox.getAttribute("aria-checked") !== "true";
    if (row.matches(".check-group-row")) {
      rows.filter(item => item.dataset.group === row.dataset.group)
        .forEach(item => setCheck(item.querySelector(".fake-check"), String(checked)));
    } else setCheck(checkbox, String(checked));
    updateChecks();
  };
  const toggleCollapsed = group => {
    const collapsed = group.getAttribute("aria-expanded") === "true";
    group.setAttribute("aria-expanded", String(!collapsed));
    const chevron = group.querySelector(".change-chevron");
    chevron.setAttribute("aria-expanded", String(!collapsed));
    chevron.setAttribute("aria-label", (collapsed ? "展开 " : "折叠 ") + group.dataset.group);
    rows.filter(row => row.dataset.group === group.dataset.group).forEach(row => { row.hidden = collapsed; });
    if (collapsed && selected()?.hidden) select(group);
  };
  ordinaryTabs.forEach(tab => tab.addEventListener("click", event => { event.preventDefault(); showDocument(tab); }));
  const initialTab = [...tabs.querySelectorAll(".editor-tab")].find(tab => tab.textContent.trim().startsWith("提交:"));
  // 已有「提交:」标签但没有选中的文件行时不能建立比较视图：
  // createComparison 需要文件行提供路径。此时保留标签外观，等待用户选择文件。
  const initialRow = selectedFile();
  if (initialTab && initialRow) {
    preview = createComparison(initialRow, initialTab, content.querySelector(".diff-layout"));
    activateComparison(preview);
  }
  updateActions();
  list.addEventListener("pointerdown", event => {
    if (event.button === 0 && event.target.closest(".fake-check, .change-chevron")) event.preventDefault();
  });
  list.addEventListener("click", event => {
    const row = event.target.closest(".check-row");
    if (!row) return;
    event.preventDefault();
    if (event.target.closest(".fake-check")) { if (event.detail < 2) toggleCheck(row); }
    else if (event.target.closest(".change-chevron")) { if (event.detail < 2) toggleCollapsed(row); }
    else select(row);
  });
  list.addEventListener("dblclick", event => {
    const row = event.target.closest(".check-row");
    if (!row || event.target.closest(".fake-check, .change-chevron")) return;
    event.preventDefault();
    if (row.matches(".check-group-row")) toggleCollapsed(row); else openDiff(row);
  });
  list.addEventListener("keydown", event => {
    const row = selected();
    const visible = allRows.filter(item => !item.hidden);
    if (["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) {
      event.preventDefault();
      const current = visible.indexOf(row);
      const index = event.key === "Home" ? 0 : event.key === "End" ? visible.length - 1
        : Math.max(0, Math.min(visible.length - 1, current + (event.key === "ArrowDown" ? 1 : -1)));
      select(visible[index]);
      visible[index].scrollIntoView({ block: "nearest" });
    } else if (row && event.key === " ") {
      event.preventDefault(); toggleCheck(row);
    } else if (row && event.key === "Enter") {
      event.preventDefault();
      if (row.matches(".change-file-row")) openDiff(row); else toggleCollapsed(row);
    } else if (row?.matches(".check-group-row") && ["ArrowLeft", "ArrowRight"].includes(event.key)) {
      event.preventDefault();
      if ((event.key === "ArrowLeft") === (row.getAttribute("aria-expanded") === "true")) toggleCollapsed(row);
    }
  });
  side.querySelector('[data-action="show-change-diff"]').addEventListener("click", () => openDiff(selectedFile()));
  side.querySelector('[aria-label="展开全部"]').addEventListener("click", () => {
    groups.filter(group => group.getAttribute("aria-expanded") === "false").forEach(toggleCollapsed);
  });
  // 样本没有后台 Git 查询；无变化刷新不重建任何列表或正文。
  side.querySelector('[aria-label="刷新"]').addEventListener("click", updateActions);
  const message = side.querySelector(".message-field"), amend = side.querySelector('.commit-amend .fake-check');
  let originalDraft = message.value;
  amend.addEventListener("click", () => {
    const checked = amend.getAttribute("aria-checked") !== "true";
    if (checked) originalDraft = message.value;
    setCheck(amend, String(checked));
    message.value = checked ? "fix: 精确恢复安装前系统 PATH" : originalDraft;
    message.dispatchEvent(new Event("input", { bubbles: true }));
  });
  let menu = null;
  const closeMenu = (restore = true) => {
    if (!menu) return;
    const ownedFocus = menu.contains(document.activeElement);
    menu.remove(); menu = null;
    if (restore && ownedFocus) list.focus({ preventScroll: true });
  };
  const showMenu = (row, x, y) => {
    closeMenu(false);
    const holder = document.createElement("template");
    holder.innerHTML = changesContextMenu();
    menu = holder.content.firstElementChild;
    menu.classList.add("changes-workflow-menu");
    menu.setAttribute("role", "menu");
    document.body.append(menu);
    Object.assign(menu.style, { position: "fixed", left: Math.max(0, Math.min(x, innerWidth - menu.offsetWidth)) + "px",
      top: Math.max(0, Math.min(y, innerHeight - menu.offsetHeight)) + "px" });
    const items = [...menu.querySelectorAll(".menu-item")];
    items.forEach(item => item.setAttribute("role", "menuitem"));
    items[0].addEventListener("click", event => {
      event.preventDefault(); closeMenu(); openDiff(row);
    });
    menu.addEventListener("keydown", event => {
      if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); closeMenu(); }
      else if (["ArrowDown", "ArrowUp"].includes(event.key)) {
        event.preventDefault();
        items[(items.indexOf(document.activeElement) + (event.key === "ArrowDown" ? 1 : items.length - 1)) % items.length].focus();
      }
    });
    items[0].focus({ preventScroll: true });
  };
  list.addEventListener("contextmenu", event => {
    const row = event.target.closest(".change-file-row");
    if (!row) return;
    event.preventDefault();
    showMenu(row, event.clientX, event.clientY);
  });
  list.addEventListener("keydown", event => {
    if ((event.key !== "ContextMenu" && !(event.shiftKey && event.key === "F10")) || !selectedFile()) return;
    event.preventDefault();
    const row = selectedFile();
    row.scrollIntoView({ block: "nearest" });
    const bounds = row.getBoundingClientRect();
    showMenu(row, bounds.left, bounds.bottom);
  });
  document.addEventListener("pointerdown", event => { if (menu && !menu.contains(event.target)) closeMenu(false); });
  document.addEventListener("keydown", event => {
    if (event.ctrlKey && event.key.toLowerCase() === "w" && activeComparison) {
      event.preventDefault(); closeComparison(activeComparison);
    }
  });
}

function bindInteractions() {
  bindStashDialog();
  bindCloneDialog();
  bindResetDialog();
  bindRollbackDialog();
  bindPushDialog();
  document.querySelectorAll(".commit-message-box").forEach(box => {
    const input = box.querySelector("textarea");
    const feedback = box.querySelector(".commit-feedback");
    const showError = error => {
      feedback.textContent = error || "提交信息";
      feedback.title = feedback.textContent;
      feedback.classList.toggle("error", !!error);
    };
    input.addEventListener("input", () => showError(""));
    box.closest(".commit-box").querySelectorAll(".commit-actions > .primary-button, .commit-actions > .secondary-button").forEach(button => {
      button.addEventListener("click", event => {
        if (input.value.trim()) return;
        event.preventDefault();
        showError("提交信息不能为空。");
        input.focus({ preventScroll: true });
      });
    });
  });
  bindDiffModes();
  bindChangesWorkflow();
  bindJsonModes();
  bindMarkdownModes();
  if (typeof bindCurrentFind === 'function') bindCurrentFind();
  bindBlame();
  if (typeof bindImagePreview === "function") bindImagePreview();
  measureCodeViews();
  bindSelectionFocus();
  bindHistoryToolbar();
  bindHistoryLayout();
  bindHistoryDetails();
  bindHistoryComparisonFollow();
  document.querySelectorAll("[aria-label]").forEach((element) => {
    if (!element.title && (element.matches("button") || element.matches("a"))) {
      element.title = element.getAttribute("aria-label");
    }
  });

  document.addEventListener("click", (event) => {
    const button = event.target.closest("[data-action='menu']");
    if (!button) return;

    const title = document.querySelector(".titlebar");
    if (title.querySelector(".main-menu-bar")) {
      title.outerHTML = titlebar();
    } else {
      title.innerHTML = `<div class="brand-mark" aria-label="Augit">A</div><button class="top-button" aria-label="关闭主菜单" data-action="menu">${icon("menu")}</button><nav class="main-menu-bar"><a class="main-menu-entry" href="workspace-open.html">文件</a><a class="main-menu-entry" href="main-project.html">视图</a><a class="main-menu-entry" href="git-history.html">Git</a><a class="main-menu-entry" href="terminal.html">终端</a><a class="main-menu-entry" href="settings.html">设置</a></nav><span></span><span></span><nav class="window-actions" aria-label="窗口工具">${windowActionIcons()}</nav>`;
    }
  });

  document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") return;
    // 内嵌主菜单打开时，Esc 关闭菜单并恢复标准标题栏（规格 §5.1）。
    // 组词期间交给输入法，不抢占按键。
    if (event.isComposing || event.keyCode === 229) return;
    const title = document.querySelector(".titlebar");
    if (title && title.querySelector(".main-menu-bar")) {
      event.preventDefault();
      title.outerHTML = titlebar();
      return;
    }

    if (scene === "quick-open" || scene === "quick-open-empty") {
      window.location.href = "main-project.html";
    }
  });
}

// 供外壳的真实数据加载器复用同一套图形与动作绑定。
// 区域替换前的清理登记表。
//
// 背景：部分绑定把监听挂在 document / window 上，但它的「所有者」是某个区域节点
// （例如 .document-view 或日志面板）。区域刷新会换掉那个节点，于是下次绑定
// 又挂一份新的，而旧监听仍留在 document 上——表现为「越用越慢」，
// 且因为每次操作看起来都对而极难察觉（实测 20 次刷新把 document 监听从 24 涨到 164）。
//
// 这里让这类绑定登记一个清理函数；替换区域前统一调用，再清空登记表。
const regionDisposers = new Set();

function registerRegionDisposer(dispose) {
  regionDisposers.add(dispose);
}

function disposeRegionBindings() {
  for (const dispose of regionDisposers) {
    try {
      dispose();
    } catch {
      // 清理失败不应阻断渲染。
    }
  }

  regionDisposers.clear();
}

window.__augitDisposeRegionBindings = disposeRegionBindings;

window.__augitBuildCommitGraph = buildCommitGraph;
window.__augitRender = () => {
  if (!app) return;
  app.innerHTML = renderScene();
  bindInteractions();
  if (scene === "go-to-line") document.querySelector("#prompt-line")?.focus();
  if (scene === "quick-open" || scene === "quick-open-empty") {
    document.querySelector(".search-overlay input")?.focus();
  }
  void applyTypographyPreview();
};

// 各区域模板的取法：把整页 HTML 解析一次，再按类名取回对应片段。
function renderRegions() {
  const template = document.createElement("template");
  template.innerHTML = renderScene();
  return template.content;
}

// 只在这些区域上做定点替换。每个键对应一个选择器与它的取样器。
const REGION_SELECTORS = {
  titlebar: ".titlebar",
  rail: ".tool-rail",
  side: ".side-tool",
  editorTabs: ".editor-tabs",
  editorContent: ".editor-content",
  statusbar: ".statusbar",
  bottomTool: ".bottom-tool",
  overlay: "[data-augit-overlay]",
  toast: ".toast",
};

const REGION_SOURCES = {
  titlebar: fragment => fragment.querySelector(".titlebar"),
  rail: fragment => fragment.querySelector(".tool-rail"),
  side: fragment => fragment.querySelector(".side-tool"),
  editorTabs: fragment => fragment.querySelector(".editor-tabs"),
  editorContent: fragment => fragment.querySelector(".editor-content"),
  statusbar: fragment => fragment.querySelector(".statusbar"),
  bottomTool: fragment => fragment.querySelector(".bottom-tool"),
  overlay: fragment => fragment.querySelector("[data-augit-overlay]"),
  toast: fragment => fragment.querySelector(".toast"),
};

/**
 * 局部更新：只替换指定区域的内容，保留其余区域的 DOM 实例。
 * 整页重绘会丢掉焦点、滚动位置、展开状态与已建立的组件实例
 * （终端、搜索输入框等），规格 §6 要求局部状态变化不得重建全局结构。
 */
window.__augitRenderRegions = (...names) => {
  if (!app || names.length === 0) return;
  // 先释放由即将被替换的节点持有的 document/window 监听，避免累积。
  disposeRegionBindings();
  const fragment = renderRegions();
  for (const name of names) {
    const selector = REGION_SELECTORS[name];
    const source = REGION_SOURCES[name];
    if (!selector || !source) continue;
    const target = app.querySelector(selector);
    const replacement = source(fragment);
    if (target && replacement) {
      // 只在两侧都存在时替换：该场景没有这个区域时跳过，
      // 不要因为一个区域缺失就退化成整页重绘。
      target.replaceWith(replacement);
    }
  }

  bindInteractions();
  void applyTypographyPreview();
};
window.__augitScene = () => scene;
window.__augitIcon = icon;
window.__augitFolderIcon = treeFolderIcon;
window.__augitFileIcon = fileTypeIcon;
window.__augitBind = bindInteractions;

if (app) {
  app.innerHTML = renderScene();
  bindInteractions();
  if (scene === "go-to-line") document.querySelector("#prompt-line")?.focus();
  if (scene === "quick-open" || scene === "quick-open-empty") {
    document.querySelector(".search-overlay input")?.focus();
  }
  void applyTypographyPreview();
}
