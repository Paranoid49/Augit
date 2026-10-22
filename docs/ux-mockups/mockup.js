const scene = document.body.dataset.scene || "main-project";
const app = document.getElementById("app");

// 主题查询值大小写不敏感：外壳曾下发首字母大写的 theme=Dark，而这里只认小写，
// 导致设置里选了“深色”却仍是浅色界面（审计与视觉稿一律用小写参数，掩盖了它）。
const requestedTheme = (new URLSearchParams(window.location.search).get("theme") || "").trim().toLowerCase();

// 字体设置只改变显示：界面字号/字族与正文等宽字号/字族相互独立（规格 §7.14 与设置页）。
// 预览参数（ui-size / code-size）优先于真实设置，便于视觉稿做字号对照。
function typographyFamily(name, fallback) {
  const cleaned = String(name ?? "").replace(/["\'\\;{}()]/g, "").replace(/\s+/g, " ").trim();
  return cleaned.length > 0 ? `"${cleaned}", ${fallback}` : null;
}

// 随包字体是否可用由浏览器实际加载结果判定，不能依赖本机字体枚举冒充加载成功。
let bundledCodeFontRequest = null;
function loadBundledCodeFont() {
  return bundledCodeFontRequest ??= document.fonts.load('13px "JetBrains Mono"', 'Ag 0123456789')
    .then(faces => faces.length > 0 && document.fonts.check('13px "JetBrains Mono"'))
    .catch(() => false);
}

// 规格 §154：调整界面字号时保持"原文档控件、树选择和首个可见节点、正文选择与滚动位置"。
// 行高由界面字号推导（`--augit-tree-height` 等），而 `scrollTop` 是像素值：
// 只改字号不回填，列表会停到别的节点上 —— 实测（tools/audit/live-shell.spec.cjs）
// 行高 28px→34px、`scrollTop` 仍为 260 时，树的首个可见行从 `docs/bulk-006.txt`
// 变成了 `docs/bulk-004.txt`。这里在改字号前记录每个可滚动容器中首个可见子节点的
// 视口偏移，改完后按**同一节点**回填，而不是按比例缩放像素。
function captureScrollAnchors() {
  const anchors = [];
  for (const node of document.querySelectorAll(".augit-window *")) {
    if (node.clientHeight <= 0 || node.scrollHeight <= node.clientHeight + 1) continue;
    const box = node.getBoundingClientRect();
    const first = [...node.children].find((child) => child.getBoundingClientRect().bottom > box.top + 1) || null;
    anchors.push({ node, first, offset: first ? first.getBoundingClientRect().top - box.top : 0, scrollTop: node.scrollTop });
  }
  return anchors;
}

function restoreScrollAnchors(anchors) {
  for (const anchor of anchors) {
    if (!anchor.node.isConnected) continue;
    // 锚点节点消失（列表被替换）时退回保存的像素位置，至少不引入额外跳动。
    if (!anchor.first || !anchor.first.isConnected) {
      if (Math.abs(anchor.node.scrollTop - anchor.scrollTop) > 0.5) anchor.node.scrollTop = anchor.scrollTop;
      continue;
    }
    const box = anchor.node.getBoundingClientRect();
    const delta = (anchor.first.getBoundingClientRect().top - box.top) - anchor.offset;
    if (Math.abs(delta) > 0.5) anchor.node.scrollTop += delta;
  }
}

let typographyGeneration = 0;
async function applyTypography({ uiSize = null, codeSize = null, uiFamily = null, monospaceFamily = null } = {}) {
  const generation = ++typographyGeneration;
  const scrollAnchors = captureScrollAnchors();
  const root = document.documentElement.style;
  const font = typographyFamily(uiFamily, '"Microsoft YaHei UI", "Segoe UI", sans-serif');
  if (font) root.setProperty("--augit-font", font);
  const code = typographyFamily(monospaceFamily, '"Cascadia Mono", Consolas, "Courier New", monospace');
  if (code) root.setProperty("--augit-code", code);

  const size = Number(uiSize);
  if (Number.isFinite(size) && size >= 9 && size <= 40) root.fontSize = `${size}px`;
  const codePixels = Number(codeSize);
  if (Number.isFinite(codePixels) && codePixels >= 9 && codePixels <= 40) {
    root.setProperty("--augit-code-size", `${codePixels}px`);
  }

  const codeStyle = getComputedStyle(document.documentElement);
  const effectiveCodeSize = parseFloat(codeStyle.getPropertyValue('--augit-code-size')) || 13;
  const effectiveCodeFamily = codeStyle.getPropertyValue('--augit-code');
  // 损坏或缺失的字体资源走既有回退链，不能阻止整页完成布局。
  await document.fonts.load(`${effectiveCodeSize}px ${effectiveCodeFamily}`, 'Ag 0123456789').catch(() => []);
  await document.fonts.ready;
  // 初始化、预览和设置应用可能并发；旧字体的迟到结果不能覆盖最新行高和滚动锚点。
  if (generation !== typographyGeneration) return;
  // 静态终端与实时网格使用相同的参考字体基础高度。
  const terminalFontLoaded = [...document.fonts].some(face => face.family.replaceAll(String.fromCharCode(34), '') === 'JetBrains Mono' && face.style === 'normal' && face.weight === '400' && face.status === 'loaded');
  const terminalFirstFont = effectiveCodeFamily.split(',')[0].trim().replaceAll(String.fromCharCode(34), '');
  root.setProperty('--terminal-line-height', terminalFirstFont === 'JetBrains Mono' && terminalFontLoaded ? String(Math.ceil(effectiveCodeSize * 1.02) + Math.ceil(effectiveCodeSize * .3)) + 'px' : 'normal');
  // 字体加载完成后才测量正文，避免首次布局使用回退字体的字宽和行高。
  root.setProperty('--code-line-height', `${codeLineHeight(effectiveCodeFamily, effectiveCodeSize)}px`);
  const context = document.createElement("canvas").getContext("2d");
  const family = getComputedStyle(document.documentElement).fontFamily;
  const uiPixels = Number.parseFloat(getComputedStyle(document.documentElement).fontSize) || 12;
  // 用户字号缩放先按 0.25 档量化，再应用各组件自己的尺寸；不能把字形框加固定留白
  // 当作 Tree/List 的行高补丁（JBUIScale.discreteScale、LafManagerImpl.patchRowHeight）。
  const uiScale = Math.max(0.75, Math.round(uiPixels / 12 * 4) / 4);
  root.setProperty("--augit-ui-scale", uiScale);
  let height = 0;
  for (const role of ["normal", "600", "italic"]) {
    context.font = `${role} ${uiPixels}px ${family}`;
    const metrics = context.measureText("国Ag");
    height = Math.max(height, Math.ceil(metrics.fontBoundingBoxAscent + metrics.fontBoundingBoxDescent));
  }
  const length = (name, pixels) => root.setProperty(`--augit-${name}`, `${pixels}px`);
  for (const [name, base] of [['icon-size',16], ['compact-icon-size',13], ['rail-icon-size',20], ['icon-hit',28], ['mode-hit',26], ['rail-width',37], ['rail-step',40], ['rail-inset',5], ['rail-right-inset',2], ['rail-radius',6]]) {
    length(name, Math.round(base * uiScale));
  }
  // 复选框画布、外接绘制区和描边分别度量，不能把15px中心路径当作外框。
  for (const [name, base] of [['check-canvas',24], ['check-visible',16], ['check-focus',18], ['check-selected-focus',22], ['check-stroke',1], ['change-inset',12]]) {
    length(name, name === 'check-stroke' ? base * uiScale : Math.round(base * uiScale));
  }
  length("title-height", Math.max(Math.round(44 * uiScale), height + 18));
  length("title-text-height", Math.max(30, height + 4));
  // IntelliJ TabbedPane.tabHeight=40 是默认下限；EditorTabs 上下 insets 各 6px，
  // 放大字号时容器按字高加 12px，保持标签正文两侧留白对称。
  length("tab-height", Math.max(Math.round(40 * uiScale), height + Math.round(12 * uiScale)));
  length("project-header-height", Math.max(Math.round(39 * uiScale), height + 10));
  // 文件树行高按24px基准离散缩放，并以实际字形高度保底。
  length("tree-height", Math.max(Math.round(24 * uiScale), height));
  // ClassicPainter左缩进不小于16px展开图标画布的一半，整数除法再加右缩进。
  // 默认为max(7,16/2)+11=19；只相加7+11会比参考少一个逻辑像素。
  length("tree-indent", Math.max(Math.round(7 * uiScale), Math.floor(Math.round(16 * uiScale) / 2)) + Math.round(11 * uiScale));
  length("tree-inset", Math.round(4 * uiScale));
  length("tree-control", Math.round(8 * uiScale));
  length("tree-control-gap", Math.round(4 * uiScale));
  length("tree-icon-slot", Math.max(16, Math.round(16 * uiScale)));
  length("history-date-width", Math.round(504 * uiScale));
  length("history-date-field-height", Math.max(28, height + 8));
  length("history-date-title-height", Math.max(34, height + 10));
  length("document-toolbar-height", Math.max(Math.round(36 * uiScale), height + 8));
  length("diff-toolbar-height", Math.max(Math.round(39 * uiScale), height + 12));
  length("diff-filebar-height", Math.max(31, height + 12));
  length("diff-unified-row-height", Math.max(24, height + 4));
  length("diff-unified-filebar-height", Math.max(55, Math.max(24, height + 4) * 2 + 7));
  // 状态栏的 New UI Widget.border 垂直 insets 共 12px；默认高度 28，大字号时随文本高度扩展。
  // 参考默认状态栏为 28px；Chromium 的字体包围盒不包含 AWT leading，
  // 因此保留实测基准下限，再按组件字号档位与实际字形扩展。
  const statusHeight = Math.max(Math.round(28 * uiScale), height + Math.round(12 * uiScale));
  length("status-height", statusHeight);
  // 终端打开时参考状态栏比普通页面高约 7 物理px（4 CSS）；保留基准供终端态叠加。
  length("status-height-base", statusHeight);
  length("find-height", Math.max(42, height + 16, Math.round(28 * uiScale) + 14));
  length("find-edit-height", Math.max(30, height + 4, Math.round(28 * uiScale) + 2));
  length("history-header-height", Math.max(38, height + 14));
  length("history-tab-height", Math.max(24, height + 4));
  length("history-filter-height", Math.max(36, height + 14));
  length("history-input-height", Math.max(25, height + 2));
  length("history-row-height", Math.max(Math.round(26 * uiScale), height + Math.round(2 * uiScale)));
  // 文件历史列表表头：权威的表头高度默认 25（`TableHeader.height`，DarculaTableHeaderUI.java:115），
  // 字号变大时按文本高同步增长，避免表头文字被固定高度裁切。
  length("history-columns-height", Math.max(25, height + 6));
  length("history-file-row-height", Math.max(Math.round(24 * uiScale), height));
  length("history-toolbar-height", Math.max(39, height + 12));
  length("history-detail-title-height", Math.max(40, height * 2));
  length("history-detail-meta-height", Math.max(20, height));
  const bottomMinimum = Math.max(180, height * 4 + 80);
  length("bottom-min-height", bottomMinimum);
  // 实时界面的底部面板高度由保存值或拖拽决定，只有预览才用推导值（否则会覆盖用户尺寸）。
  const settings = window.__augitLive && window.__augitLive.settings;
  const savedBottom = settings ? Number(settings.bottomPanelHeight) : Number.NaN;
  if (!(Number.isFinite(savedBottom) && savedBottom > 0)) {
    root.setProperty("--augit-bottom-height", `clamp(${bottomMinimum}px, 31vh, ${Math.max(305, bottomMinimum)}px)`);
  }
  // 字号改变后重新把保存尺寸映射到当前视口；不覆盖磁盘中的首选尺寸。
  if (typeof window.__augitApplyPanelSizes === 'function') window.__augitApplyPanelSizes(settings || {});
  // 提交图的几何按**实际行高**重算：权威 `PaintParameters.scaleWithRowHeight` 让节点半径、轨距、
  // 线宽与图形文字间距都跟着行高走（design-system.md §8.3.2）。行高本身由上面的
  // `history-row-height` 写入 `:root`，因此这里只需按新行高重绘一次。
  refreshCommitGraphGeometry();
  measureCurrentFind();
  measureCodeViews();
  measureDocumentToolbar();
  measureCommitPanels();
  measureWorkbenchSpace();
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
  measureTitlebarGlow();
  measureTabFade();
  restoreScrollAnchors(scrollAnchors);
  document.body.dataset.typographyPreview = "ready";
}

async function applyTypographyPreview() {
  const query = new URLSearchParams(window.location.search);
  const previewSize = (name) => {
    const raw = query.get(name);
    if (raw === null) return null;
    const value = Number(raw);
    return Number.isFinite(value) && value >= 9 && value <= 40 ? value : null;
  };
  // 字体族也可以按对照需要临时覆盖：实时外壳用用户保存的字体，视觉稿用设计基线字体，
  // 逐页对照必须先把两侧字体与字号设成相同（规格 §5.4），否则量到的几何差异全是字体造成的。
  const previewFamily = (name) => {
    const raw = query.get(name);
    if (raw === null) return null;
    const cleaned = raw.replace(/["'\\;{}()]/g, "").replace(/\s+/g, " ").trim();
    return cleaned.length > 0 && cleaned.length <= 60 ? cleaned : null;
  };
  const settings = window.__augitLive && window.__augitLive.settings ? window.__augitLive.settings : null;
  await applyTypography({
    uiSize: previewSize("ui-size") ?? (settings ? settings.fontSize : null),
    codeSize: previewSize("code-size") ?? (settings ? settings.codeFontSize : null),
    uiFamily: previewFamily("ui-family") ?? (settings ? settings.textFontFamily : null),
    monospaceFamily: previewFamily("code-family") ?? (settings ? settings.monospaceFontFamily : null),
  });
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
    const titleWidth = Math.max(54, Math.ceil(context.measureText('Terminal').width) + 8);
    context.font = `normal ${style.fontSize} ${style.fontFamily}`;
    const label = tool.querySelector('.terminal-session');
    // 2026-10-18 参考：Local 是紧凑标签（约 64.6 CSS 宽、28 CSS 高），文字左对齐、关闭叉在同一卡片内；
    // 不再用 124px 的会话下拉框宽度。sessionWidth 只承担文字+左右内距，20px 关闭段由紧随其后的按钮承担。
    const sessionWidth = Math.min(Math.max(44, Math.ceil(context.measureText(label.textContent).width) + 12),
      Math.max(0, header.clientWidth - titleWidth - 22 - 96));
    // 大字号时 icon 命中区会大于 header 的文本高度（40 号 UI 实测 91px）；
    // 只有放大 header 并让动作区按命中区定位，More/Hide 才不会被裁切或互相覆盖。
    const iconHit = parseFloat(getComputedStyle(tool).getPropertyValue('--augit-icon-hit')) || 28;
    const largeActions = iconHit > 32;
    tool.classList.toggle('terminal-large-actions', largeActions);
    tool.style.setProperty('--terminal-header-height', `${Math.max(38, height + 12, largeActions ? iconHit + 8 : 0)}px`);
    tool.style.setProperty('--terminal-text-height', `${Math.max(24, height + 4)}px`);
    if (largeActions) tool.style.setProperty('--terminal-session-height', `${Math.max(28, height - 2)}px`);
    else tool.style.removeProperty('--terminal-session-height');
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
  const uiScale = Math.max(.75, Math.round(parseFloat(style.fontSize) / 12 * 4) / 4);
  const optionWidth = Math.round(28 * uiScale), optionGap = Math.round(4 * uiScale);
  const content = overlay.clientWidth - 20, button = Math.max(optionWidth, line + 8);
  const options = overlay.querySelectorAll('.search-option');
  const header = Math.max(41, line + 20, options.length ? button + 8 : 0);
  const title = overlay.querySelector('.search-tabs strong'), shortcut = overlay.querySelector('.menu-shortcut');
  const titleWidth = context.measureText(title.textContent).width;
  let fieldTop = header;
  title.style.cssText = `left:10px;top:${(header - line) / 2}px;line-height:${line}px`;
  if (options.length) {
    const ignored = overlay.querySelector('.search-ignored'), ignoredWidth = Math.min(content, Math.ceil(context.measureText('Include ignored files').width) + 22);
    const groupWidth = options.length * optionWidth + (options.length - 1) * optionGap;
    const optionsWidth = groupWidth + 8 + ignoredWidth;
    let left = overlay.clientWidth - 10 - optionsWidth, top = (header - button) / 2;
    let ignoredLeft = left + groupWidth + 8, ignoredTop = top;
    let columns = options.length;
    if (titleWidth + 8 + optionsWidth > content) {
      left = 10; top = header; ignoredLeft = left + groupWidth + 8; ignoredTop = top;
      columns = Math.max(1, Math.min(options.length, Math.floor((content + optionGap) / (optionWidth + optionGap))));
      if (optionsWidth > content) { ignoredLeft = 10; ignoredTop += Math.ceil(options.length / columns) * (button + 8); }
      fieldTop = ignoredTop + button + 8;
    }
    options.forEach((option, index) => { option.style.cssText = `left:${left + index % columns * (optionWidth + optionGap)}px;top:${top + Math.floor(index / columns) * (button + 8)}px;width:${optionWidth}px;height:${button}px`; });
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
window.addEventListener('resize', measureTitlebarGlow);

/**
 * 细线厚度的设备像素对齐（`07-theme-dpi-dialogs.md` §2：分隔线粗细必须按 DPI 缩放）。
 *
 * 权威把"1 单位的线"按 `JBUIScale.scale(1) = round(userScaleFactor)` 取整到**整设备像素**
 * （`JBValue.get()` 也是 `scale(...)`，`JBValue.java:65-78`；§2.4 的 `alignIntToInt()` 同义），
 * 在 CSS 侧的等价写法是 `round(dpr) / dpr` px —— 这里只负责把两个变量写到 `:root`，
 * 规则在 `--augit-hairline` 里（未运行 JS 时回落 1px）。
 *
 * 换显示器会让 `devicePixelRatio` 变化，`resize` 之后重算一次即可（跨屏拖动必然触发）。
 */
function applyDeviceScale() {
  const scale = window.devicePixelRatio || 1;
  const root = document.documentElement;
  root.style.setProperty('--augit-dpr', String(scale));
  root.style.setProperty('--augit-hairline-device', String(Math.max(1, Math.round(scale))));
}

applyDeviceScale();
window.addEventListener('resize', applyDeviceScale);
// 提交图几何按 DPI 在设备空间对齐（权威 `PaintUtil.alignToInt`）：跨屏拖动改变
// `devicePixelRatio` 后线宽与节点尺寸都要重算，否则整条图形比设计值粗/细半个设备像素。
window.addEventListener('resize', refreshCommitGraphGeometry);
window.addEventListener('DOMContentLoaded', applyDeviceScale);
window.addEventListener('resize', measureTabFade);
// 标签栏的滚动事件不冒泡，用捕获阶段在 document 上收；只处理标签栏自身的滚动。
document.addEventListener('scroll', (event) => {
  const target = event.target;
  if (target && target.classList && target.classList.contains('editor-tabs')) measureTabFade();
}, true);
document.addEventListener('click', event => {
  const option = event.target.closest('.search-option');
  if (option) option.setAttribute('aria-pressed', String(option.getAttribute('aria-pressed') !== 'true'));
});

// Clone 的字体适配只改变表单空间；固定动作栏不随失败或取消提示移动。
// Push 只模拟现有对话框状态，不执行 Git；所有延迟和监听在关闭时收尾。
// 外壳注入真实待推送信息时使用；结构、图标与样式与样例版一致。
function livePushDialogBody() {
  const mark = '<svg viewBox="0 0 16 16" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2.5 8.25L6 11.75L13.5 4.25"/></svg>';
  const push = (window.__augitLive && window.__augitLive.push) || null;
  if (!push || !push.upstream) {
    return `<div class="management-content"><div class="management-list"><div class="tree-row push-summary selected"><strong>${escapeHtml(push ? push.branch : "HEAD")}</strong><a class="file-status-modified" href="remote.html">Define remote</a></div><div class="push-commits" role="listbox" aria-label="Commits to push" tabindex="0"></div></div><div class="management-detail" tabindex="0" aria-label="Push details; scroll to read"><h2>Unable to load push preview</h2><p class="commit-meta">${escapeHtml(push && push.reason ? push.reason : "Define a remote, then reopen Push.")}</p><div class="push-notice" role="status" hidden></div></div></div><div class="check-line push-tags"><input type="checkbox" disabled aria-label="Push tags">Push tags <select class="select-field" disabled aria-label="Tag scope"><option>All</option></select></div>`;
  }

  const commits = push.commits || [];
  // ready=false 表示上游未配置或读取失败：此时不能断言「没有提交」，
  // 只能说数量未知（规格 §7.12 要求此时禁用推送并保留「定义远端」）。
  const unknown = push.ready === false;
  const rows = commits.length === 0
    ? `<p class="commit-meta" style="padding:6px">${unknown ? "Outgoing commits are outside the loaded history" : "No commits to push"}</p>`
    : commits.map((commit, index) => `<div class="push-commit" role="option" aria-selected="false" id="push-commit-${index}" data-push-hash="${escapeHtml(commit.hash)}">${mark}<span>${escapeHtml(commit.subject)}</span></div>`).join("");
  const detail = unknown
    ? `<h2>Commit count unknown</h2><p class="push-target">Target: ${escapeHtml(push.upstream)}</p><p class="commit-meta">${escapeHtml(push.reason || "The outgoing commit count is unknown.")}</p>`
    : `<h2>${commits.length} commits</h2><p class="push-target">Target: ${escapeHtml(push.upstream)}</p><p class="commit-meta push-credentials">Credentials are handled by local Git; Augit does not store them.</p>`;
  return `<div class="management-content"><div class="management-list"><div class="tree-row push-summary selected"><strong>${escapeHtml(push.branch)} → ${escapeHtml(push.upstream)}</strong></div><div class="push-commits" role="listbox" aria-label="Commits to push" tabindex="0">${rows}</div></div><div class="management-detail" tabindex="0" aria-label="Push details; scroll to read">${detail}<div class="push-notice" role="status" hidden></div></div></div>`;
}

/**
 * Push 对话框的标题（权威）。
 *
 * 权威 `DvcsBundle.properties:54-55`：
 *   单仓库 = `push.dialog.push.commits.to.title` = **"Push Commits to {0}"**（{0} 是**仓库短名**，
 *   见 `VcsPushDialog.java:134-137` 的 `DvcsUtil.getShortRepositoryName(...)`）；
 *   多仓库 = `push.dialog.push.commits.title` = "Push Commits"。
 * Augit 一个工作区就是一个仓库 ⇒ 取**工作区名**，与标题栏 chip 用同一个来源（`workspaceName`）。
 *
 * 原实现把"Augit"硬编码进标题 —— 推送的目标是**远端**而不是产品名，属语义错误（第 140 轮按权威改正）。
 */
function pushDialogTitle() {
  const name = (window.__augitLive && window.__augitLive.workspaceName) || "Augit";
  return `Push Commits to ${name}`;
}

function pushDialogBody(noRemote) {
  const mark = '<svg viewBox="0 0 16 16" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2.5 8.25L6 11.75L13.5 4.25"/></svg>';
  return `<div class="management-content"><div class="management-list"><div class="tree-row push-summary selected"><strong>main →${noRemote ? '' : ' origin/main'}</strong>${noRemote ? '<a class="file-status-modified" href="remote.html">Define remote</a>' : ''}</div><div class="push-commits" role="listbox" aria-label="Commits to push" tabindex="0">${noRemote ? '' : ['fix: 精确恢复系统 PATH', 'fix: 避免强制更新 .NET 10'].map((text, index) => `<div class="push-commit" role="option" aria-selected="false" id="push-commit-${index}">${mark}<span>${text}</span></div>`).join('')}</div></div><div class="management-detail" tabindex="0" aria-label="Push details; scroll to read">${noRemote ? '<div class="push-empty">No selected commits</div>' : '<h2>2 commits</h2><p class="push-target">Target: origin/main</p><p class="commit-meta push-credentials">Credentials are handled by local Git; Augit does not store them.</p>'}<div class="push-notice" role="status" hidden></div></div></div>${noRemote ? '<div class="check-line push-tags"><input type="checkbox" disabled aria-label="Push tags">Push tags <select class="select-field" disabled aria-label="Tag scope"><option>All</option></select></div>' : ''}`;
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
  const header = Math.max(45, line + 18),
    // 底栏高度必须容得下按钮（`button` = `max(37, line + 17)`）+ 底栏顶部内距 12；
    // 原 `line + 25` 在大字号下会比按钮矮 4px，导致按钮溢出对话框底边（verify-ux-push 实测 size=40 时溢出 3px）。
    footer = Math.max(53, line + 29), row = Math.max(27, line + 8),
    // 按钮高度：权威 `Button.minimumSize` = 72×28 是下限；2026 参考图实测 37（该图缩放已复核为 1.5x），故下限取 37。
    button = Math.max(34, line + 12);
  // 判据取"对话框里真的有标签选项行"，而不是 `push-no-remote` 这个类名：从分支弹层打开的
  // **实时** Push 对话框用的是同一个 `push-dialog` 类，无上游时同样渲染 `.push-tags`，
  // 但它没有 `push-no-remote` 类 ⇒ 旧判据给它 0px，标签行不占网格行、直接压到底栏上
  //（第 274 轮实测：`--push-tags: 0px`、标签行底边在底栏之下）。静态 `push-no-remote` 场景
  // 两种判据一致，改后行为不变。
  const tags = dialog.querySelector('.push-tags') ? Math.max(30, row + 3) : 0;
  const width = Math.min(930, innerWidth - 80), define = Math.max(72, text('Define remote') + 16);
  const sidebar = tags ? Math.min(Math.max(260, 16 + text('main →') + 8 + define + 8), Math.max(260, (width - 34) / 2)) : 260;
  const values = { header, footer, row, button, tags, sidebar, height: header + 30 + 365 + footer + 1 + tags,
    cancel: Math.max(78, text('Cancel') + 24), push: Math.max(84, text('Pushing…') + 24) };
  for (const [name, value] of Object.entries(values)) dialog.style.setProperty(`--push-${name}`, `${value}px`);
}

function bindPushDialog() {
  const dialog = document.querySelector('.push-dialog');
  if (!dialog) return;
  dialog.querySelector('.footer-help')?.remove(); dialog.setAttribute('aria-modal', 'true');
  const list = dialog.querySelector('.push-commits'), rows = [...list.children], detail = dialog.querySelector('.management-detail');
  const push = dialog.querySelector('.primary-button'), cancel = dialog.querySelector('.secondary-button'), close = dialog.querySelector('.dialog-header a');
  const define = dialog.querySelector('.push-summary a'), notice = dialog.querySelector('.push-notice');
  close.innerHTML = '<svg viewBox="0 0 16 16" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1" stroke-linecap="round" aria-hidden="true"><path d="M3.5 12.5l9-9m0 9-9-9"/></svg>';
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
    update(); cancel.textContent = 'Cancel'; push.textContent = 'Push';
    const error = 'The remote rejected the push; update the branch first.';
    showNotice(result === 'cancelled' ? 'Operation cancelled.' : query.has('long-error') ? Array(18).fill(error).join('\n') : error, result !== 'cancelled');
  };
  const start = () => {
    if (push.disabled || running) return;
    running = true; dialog.dataset.state = 'running'; update();
    cancel.textContent = 'Cancel'; push.textContent = 'Pushing…'; cancel.focus(); showNotice('Pushing…');
    const result = results[Math.min(attempt++, results.length - 1)];
    if (result !== 'pending') timer = setTimeout(() => finish(result), 180);
  };
  const cancelOrClose = () => {
    if (!running) { dismiss(); return; }
    if (cancelling) return;
    cancelling = true; showNotice('Cancelling push; waiting for Git…');
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
    dialog.querySelector('h2').textContent = '0 commits'; showNotice('No outgoing commits for this reference.');
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
  // 按钮此前借用了 `--clone-field`（字段高度），本轮改为独立的 `--clone-button`。
  const button = Math.max(34, line + 12);
  const label = Math.max(110, ...["Version control", "Repository URL", "Directory"].map(text));
  const cancel = Math.max(53, text("Cancel") + 24), create = Math.max(52, text("Cloning…") + 24);
  const shallow = Math.max(180, text("Shallow clone, history truncated to") + 23), depth = Math.max(58, text("1000") + 16), suffix = Math.max(80, text("commits"));
  const width = Math.min(Math.max(930, label + 44 + shallow), innerWidth - 90);
  const wrapped = shallow + depth + suffix + 20 > width - 46 - label;
  const height = header + 191 + 4 * (field - 30) + (wrapped ? field + 8 : 0) + footer;
  for (const [name, value] of Object.entries({ header, field, footer, button, label, cancel, create, shallow, depth, suffix, width, height }))
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
  close.innerHTML = '<svg viewBox="0 0 16 16" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1" stroke-linecap="round" aria-hidden="true"><path d="M3.5 12.5l9-9m0 9-9-9"/></svg>';
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
    // 克隆按钮的可用性取权威 `DvcsCloneDialogComponent.kt:127`：
    //   `isOkActionEnabled() = getUrl().isNotBlank()` ⇒ **URL 为空即禁用**。
    // 原实现是"按钮始终可用、点击后才提示"，属旧交互（第 144 轮按权威改正）。
    // 目录仍走点击时校验：`ux-spec:541` 要求"提示原因并聚焦对应输入"，且 Augit 的目录**初始为空**
    //（权威是随 URL 自动派生目录），所以"目录为空"这一档只能由点击时的提示覆盖。
    const hasUrl = !!source.value.trim();
    create.disabled = running || !hasUrl;
    create.setAttribute("aria-disabled", String(create.disabled));
    if (!running && !hasUrl) create.title = "Enter a repository URL first.";
    else create.removeAttribute("title");
    close.setAttribute("aria-disabled", String(running));
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
    updateFields(); cancel.textContent = "Cancel"; create.textContent = "Clone";
    const error = "The server is unavailable. Check the repository URL or network and try again.";
    showNotice(result === "cancelled" ? "Operation cancelled." : query.has("long-error") ? Array(12).fill(error).join("\n") : error);
    dialog.dataset.state = result;
  };
  const cancelOrClose = () => {
    if (!running) { dismiss(); return; }
    if (cancelling) return;
    cancelling = true; dialog.dataset.state = "cancelling";
    clearTimeout(timer);
    // 真实外壳：取消必须**先请求宿主结束 Git**（规格 §7.12 第 5 条），然后继续冻结表单，
    // 等 `__augitCloneRequest` 的 promise 收尾 —— 不能用模拟计时器把取消直接当完成，
    // 否则宿主里的 Git 进程还在跑而界面已经解除冻结（第 273 轮接线）。
    if (typeof window.__augitCloneRequest === "function") {
      if (typeof window.__augitCloneCancel === "function") void window.__augitCloneCancel();
      return;
    }
    // 视觉稿：模拟 Git 确认结束前继续冻结表单，不能把请求取消立即当成已取消。
    timer = setTimeout(() => finish("cancelled", request), 180);
  };
  const start = () => {
    if (running) return;
    if (!source.value.trim() || !destination.value.trim()) {
      showNotice("Enter a repository URL and destination.");
      (!source.value.trim() ? source : destination).focus(); return;
    }
    if (shallow.checked && (!/^[0-9]+$/.test(depth.value.trim()) || Number(depth.value.trim()) < 1 || Number(depth.value.trim()) > 2147483647)) {
      showNotice("Shallow clone depth must be a positive integer."); depth.focus(); return;
    }
    running = true; cancelling = false; const expected = ++request;
    updateFields(); create.textContent = "Cloning…"; cancel.textContent = "Cancel"; cancel.focus();
    showNotice("Cloning…"); dialog.dataset.state = "running";
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
        create.textContent = "Clone"; cancel.textContent = "Cancel";
        showNotice(result && result.reason ? result.reason : "Clone failed.");
        const field = result && result.field;
        (field === "source" ? source : field === "depth" ? depth : destination).focus();
      }).catch(error => {
        if (request !== expected) return;
        running = false; dialog.dataset.state = "idle"; updateFields();
        showNotice("Clone failed:" + String(error && error.message || error));
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
  // URL 变化要即时反映到克隆按钮的可用性（权威 `isOkActionEnabled()` 只看 URL）。
  source.addEventListener("input", updateFields, options);
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
  const button = Math.max(34, line + 12), message = Math.max(78, line * 3 + 12), footer = Math.max(53, button + 23);
  const label = Math.max(110, ...["Git root", "Current branch", "Message"].map(text));
  const cancel = Math.max(54, text("Cancel") + 24), create = Math.max(90, text("Creating Stash…") + 24);
  const body = 16 + field + 13 + branch + 5 + message + 13 + button + 16;
  const width = Math.max(620, cancel + create + 44, label + 69 + text("Keep index"));
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
  const header = Math.max(45, line + 18), toolbar = Math.max(61, line + 28), button = Math.max(34, line + 12), footer = Math.max(53, button + 25);
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
  // 权威 `GitStashDialog.kt:48-56`：保留索引与「包含未跟踪文件」同处一行 ⇒ Tab 顺序里紧挨着。
  const include = dialog.querySelector("#stash-include-untracked");
  const create = dialog.querySelector(".primary-button"), cancel = dialog.querySelector(".secondary-button"), close = dialog.querySelector(".dialog-header a");
  const notice = dialog.querySelector(".stash-notice"), body = dialog.querySelector(".dialog-body");
  close.innerHTML = '<svg viewBox="0 0 16 16" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1" stroke-linecap="round" aria-hidden="true"><path d="M3.5 12.5l9-9m0 9-9-9"/></svg>';
  const fields = [root, message, keep, include], order = [...fields, create, cancel, close];
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
    cancel.textContent = "Cancel"; create.textContent = "Create Stash";
    const failure = "A Git lock file is in use. Try again later.";
    showNotice(result === "cancelled" ? "Operation cancelled." : new URLSearchParams(location.search).has("long-error") ? Array(12).fill(failure).join("\n") : failure);
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
    create.textContent = "Creating Stash…"; cancel.textContent = "Cancel"; cancel.focus();
    showNotice("Creating Stash…"); dialog.dataset.state = "running";
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
  const header = Math.max(45, line + 16), button = Math.max(34, line + 12), footer = 53 + button - 34;
  // 表单控件高度必须随字号缩放（否则大字号下 clientHeight < fontSize，被判为裁剪）；与 clone/stash 的 field 同式。
  const field = Math.max(30, line + 10);
  const label = Math.max(110, text('Target commit'), text('Mode'));
  const cancel = Math.max(78, text('Cancel') + 26);
  const run = Math.max(116, ...['Reset Hard', 'Reset', 'Resetting…'].map(value => text(value) + 26));
  const mode = Math.max(...[...dialog.querySelector('select').options].map(option => text(option.textContent)));
  const width = Math.max(620, label + mode + 110, cancel + run + 42);
  for (const [name, value] of Object.entries({ header, button, footer, label, cancel, run, width, line, field }))
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
  const header = Math.max(45, line + 16), button = Math.max(34, line + 12), footer = button + 23;
  const cancel = Math.max(78, text('Cancel') + 26), run = Math.max(116, text('Rollback File') + 26);
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

/**
 * Reset 的"将丢失 N 个已跟踪文件"里的 N。
 *
 * 视觉稿页面没有宿主（`__augitLive` 不存在），沿用样例基线值 35；
 * 实时外壳必须显示**真实**已跟踪改动文件数（规格 §10.4：危险操作要显示具体影响），
 * 否则用户看到的是一句与仓库无关的说明。
 */
function trackedChangeCount() {
  const live = window.__augitLive;
  const files = live && live.status && Array.isArray(live.status.files) ? live.status.files : null;
  if (!files) return 35;
  return files.filter((file) => file.group === "Changes"
    && file.kind !== "Untracked" && file.kind !== "Added").length;
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
    // 顺序必须与 `#reset-mode` 的 option 顺序一致（权威 `GitResetDialog.java:157-159`：MIXED → SOFT → HARD），
    // 因为下面用 `mode.selectedIndex` 索引它。危险档仍是索引 2。
    const presentations = [
      ['Move HEAD and reset the index; keep working tree changes', 'Staged changes return to the working tree; local files are kept.'],
      ['Move HEAD only; keep index and working tree changes', 'Staged and unstaged changes are kept.'],
      [`Discard changes in ${trackedChangeCount()} tracked files`, 'Untracked files are kept. Augit cannot undo this operation automatically.'],
    ];
    impact.querySelector('strong').textContent = presentations[mode.selectedIndex][0];
    impact.querySelector('p').textContent = presentations[mode.selectedIndex][1];
    impact.classList.toggle('danger', mode.selectedIndex === 2);
    run.classList.toggle('danger-button', mode.selectedIndex === 2);
    run.classList.toggle('primary-button', mode.selectedIndex !== 2);
    run.textContent = running ? 'Resetting…' : mode.selectedIndex === 2 ? 'Reset Hard' : 'Reset';
    cancel.textContent = running ? 'Cancel' : 'Cancel';
    for (const node of [target, mode, run]) node.disabled = running;
    run.setAttribute('aria-disabled', String(running)); close.setAttribute('aria-disabled', String(running));
  };
  const dismiss = () => {
    // 实时外壳的对话框外面还有一层 [data-augit-overlay]：只删 dialog 会留下空层，
    // 背景会一直被 syncModalBackground 当成"有模态"而保持禁用。
    const owner = dialog.closest('[data-augit-overlay]');
    clearTimeout(timer); controller.abort(); dialog.remove(); document.querySelector('.scrim')?.remove();
    if (owner) owner.remove();
    // 规格 §5.3：关闭后恢复打开前焦点（只有实时外壳提供这个栈，视觉稿是静态页面）。
    if (typeof window.__augitResetClosed === 'function') window.__augitResetClosed();
  };
  const cancelAction = () => {
    if (!running) { dismiss(); return; }
    // 实时外壳：取消必须真的请求宿主停止写操作，并等宿主确认（规格 §9.3）。
    if (typeof window.__augitResetCancel === 'function') {
      showNotice('Cancelling Reset; waiting for Git…');
      void window.__augitResetCancel().then(outcome => {
        running = false; update();
        showNotice(outcome && outcome.stopped ? 'Reset cancelled.' : 'Reset finished; no operation is available to cancel.');
        dialog.dataset.state = 'cancelled';
      });
      return;
    }
    clearTimeout(timer); running = false; update(); showNotice('Operation cancelled.'); dialog.dataset.state = 'cancelled';
  };
  const start = () => {
    if (running) return;
    if (!target.value.trim()) { showNotice('Enter a target commit for Reset.'); target.focus(); return; }
    running = true; update(); cancel.focus(); showNotice('Resetting…'); dialog.dataset.state = 'running';
    // 实时外壳：交给宿主执行真实 Reset（三种模式与目标由界面收集，宿主讲明失败原因）。
    if (typeof window.__augitResetRequest === 'function') {
      void window.__augitResetRequest({
        target: target.value.trim(),
        mode: mode.selectedIndex === 0 ? 'Soft' : mode.selectedIndex === 1 ? 'Mixed' : 'Hard',
      }).then(outcome => {
        if (outcome && outcome.ok) { dismiss(); return; }
        running = false; update();
        showNotice((outcome && outcome.reason) || 'Reset did not complete.');
        dialog.dataset.state = 'failure';
      });
      return;
    }
    if (params.get('reset-result') === 'pending') return;
    timer = setTimeout(() => {
      running = false; update();
      if (params.get('reset-result') === 'failure') {
        const reason = 'Cannot resolve the target commit. Check the reference name.';
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
      const items = [target, mode, run, cancel, close].filter(node => !node.disabled && node.getAttribute('aria-disabled') !== 'true');
      const at = items.indexOf(document.activeElement), direction = event.shiftKey ? -1 : 1;
      event.preventDefault(); items[(at + direction + items.length) % items.length]?.focus();
    } else if (event.key === 'Escape') { event.preventDefault(); cancelAction(); }
    else if (event.key === 'Enter' && event.target === target) { event.preventDefault(); if (!event.repeat) start(); }
  }, options);
  window.addEventListener('resize', measureResetDialog, options);
  window.addEventListener('pagehide', () => { clearTimeout(timer); controller.abort(); }, { ...options, once: true });
  update(); target.focus({ preventScroll: true });
}

// 远端管理改为 Name/URL 单列表格（+ Define Remote 次模态）：受限高度只滚动表格正文，
// 工具条与底栏固定。宽度按权威 `GitConfigureRemotesDialog.updateTableWidth()` 的内容驱动公式
// （名称列 = max(30, 名称字宽) + REMOTE_PADDING(30) + DEFAULT_HGAP(10)；URL 列最小 250），
// 默认尺寸按2026-10-17用户批准的原始实机图：Remotes 624×385物理像素（175%）。
// 本函数只量远端自己的窗口（主表 + Define）。
function remoteNameColumnWidth(width, name, url) {
  // JTable无拖动时从preferredWidth分配余量；收缩按各列距minWidth的余量分配。
  const minimum = 15, maximum = 2147483647;
  const delta = Math.max(minimum * 2, Math.round(width)) - name - url;
  const capacity = delta >= 0 ? maximum - name : name - minimum;
  const total = delta >= 0 ? maximum * 2 - name - url : name + url - minimum * 2;
  return Math.max(minimum, Math.round(name + delta * capacity / total));
}
function measureRemoteDialog() {
  const context = document.createElement("canvas").getContext("2d");
  const lineOf = (style, weight) => {
    context.font = `${weight} ${style.fontSize} ${style.fontFamily}`;
    const metrics = context.measureText("国Ag");
    return Math.ceil(metrics.fontBoundingBoxAscent + metrics.fontBoundingBoxDescent);
  };
  const dialog = document.querySelector(".remote-dialog");
  if (dialog) {
    const style = getComputedStyle(dialog);
    const line = Math.max(lineOf(style, "normal"), lineOf(style, "600"));
    const header = Math.max(40, line + 18), footer = Math.max(50, line + 25);
    const toolbar = Math.max(24, line + 4), row = Math.max(24, line + 6), button = Math.max(34, line + 12);
    const rows = [...dialog.querySelectorAll(".remote-row")];
    let nameWidth = 30, urlWidth = 250;
    context.font = `600 ${style.fontSize} ${style.fontFamily}`;
    for (const node of rows) {
      const name = node.querySelector(".remote-name"), url = node.querySelector(".remote-url");
      nameWidth = Math.max(nameWidth, Math.ceil(context.measureText(name ? name.textContent : "").width));
      // 权威表格按粗体预留URL列宽；正文renderer仍以普通字重显示。
      urlWidth = Math.max(urlWidth, Math.ceil(context.measureText(url ? url.textContent : "").width));
    }
    // 权威 `REMOTE_PADDING = 30`、`DEFAULT_HGAP = 10`；表格首行是表头，`visibleRowCount = min(nodes + 3, 8)`。
    const nameColumn = nameWidth + 30 + 10;
    const visibleRows = Math.min(rows.length + 3, 8);
    const height = Math.max(220, header + toolbar + row * visibleRows + footer + 16);
    dialog.style.setProperty("--remote-width", `${Math.max(624 / 1.75, nameColumn + urlWidth + 10 + 24)}px`);
    for (const [name, value] of Object.entries({ header, footer, toolbar, row, button, height })) {
      dialog.style.setProperty(`--remote-${name}`, `${value}px`);
    }
    dialog.querySelector(".footer-help")?.remove();
    const table = dialog.querySelector('.remote-table');
    if (table) dialog.style.setProperty('--remote-name-width', `${remoteNameColumnWidth(table.clientWidth, nameColumn, urlWidth)}px`);
  }
  // Remove Remote 确认复用 Define 的尺寸骨架，但不按三行表单重量。
  const define = document.querySelector(".remote-define-dialog:not(.remote-confirm-dialog)");
  if (define) {
    const style = getComputedStyle(define);
    const line = Math.max(lineOf(style, "normal"), lineOf(style, "600"));
    const header = Math.max(40, line + 18), footer = Math.max(50, line + 25), button = Math.max(34, line + 12);
    const field = Math.max(30, line + 12), row = Math.max(24, line + 6);
    const label = Math.max(...["Name:", "URL:", "Push URL:"].map((text) => {
      context.font = `${style.fontSize} ${style.fontFamily}`;
      return Math.ceil(context.measureText(text).width);
    }));
    // 参考 207 高只含 Name/URL 两行与 Fetch 复选框；Push URL 是本产品额外一行，
    // 按行高追加，因此这里是 207 加一行而不是声称与参考 207 精确相同。
    for (const [name, value] of Object.entries({ header, footer, button, field, row, label })) {
      define.style.setProperty(`--remote-${name}`, `${value}px`);
    }
    // 量实际表单，包含随字号放大的复选框；border-box的上下边框不能漏算。
    const bodyStyle = getComputedStyle(define.querySelector('.dialog-body'));
    const content = define.querySelector('.remote-define-form').getBoundingClientRect().height;
    const borders = parseFloat(style.borderTopWidth) + parseFloat(style.borderBottomWidth);
    const padding = parseFloat(bodyStyle.paddingTop) + parseFloat(bodyStyle.paddingBottom);
    const height = Math.max(207 + row + 8, header + footer + content + padding + borders);
    define.style.setProperty("--remote-define-height", `${height}px`);
  }
}

window.addEventListener('resize', measureRemoteDialog);

// Worktree 沿用远端的双栏骨架，但默认高度保持视觉稿的 365px；右侧详情独立滚动。
function measureWorktreeDialog() {
  const dialog = document.querySelector('.worktree-dialog');
  if (!dialog) return;
  const context = document.createElement('canvas').getContext('2d'), style = getComputedStyle(dialog);
  context.font = `${style.fontSize} ${style.fontFamily}`;
  const sample = context.measureText('国Ag');
  const line = Math.ceil(sample.fontBoundingBoxAscent + sample.fontBoundingBoxDescent);
  const header = Math.max(45, line + 16), footer = Math.max(53, line + 25), toolbar = Math.max(61, line + 28);
  const button = Math.max(34, line + 12);
  const body = 190 + Math.max(0, line - 31) + 3 * Math.max(0, line - 24) + Math.max(0, button - 31);
  const values = { header, footer, toolbar, title: Math.max(30, line), button,
    label: Math.max(110, context.measureText('New branch (optional)').width), height: Math.max(365, header + toolbar + body + footer) };
  for (const [name, value] of Object.entries(values)) dialog.style.setProperty(`--worktree-${name}`, `${value}px`);
}


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
    const uiScale = Math.max(0.75, Math.round(parseFloat(style.fontSize) / 12 * 4) / 4);
    const settingsWidth = Math.round(28 * uiScale), settingsHeight = Math.round(30 * uiScale);
    const textWidth = text => Math.ceil(context.measureText(text).width);
    const width = side.clientWidth;
    const actionWidth = Math.max(0, width - 20);
    const commitWidth = Math.max(78, textWidth(box.querySelector('.commit-actions > .primary-button').textContent) + 34);
    const pushWidth = Math.max(102, textWidth(box.querySelector('.commit-actions > .secondary-button').textContent) + 34);
    const wrap = commitWidth + pushWidth + settingsWidth + 14 > actionWidth;
    const actionHeight = Math.max(34, settingsHeight, h + 12);
    const actionsHeight = actionHeight * (wrap ? 2 : 1) + (wrap ? 7 : 0);
    const optionsHeight = Math.max(Math.round(24 * uiScale), h + 4);
    const lastWidth = Math.min(textWidth('last commit') + 4, actionWidth);
    const amendWidth = textWidth('Amend') + Math.round(24 * uiScale) + Math.round(4 * uiScale);
    const wrapAmend = amendWidth + textWidth(' ') + lastWidth > actionWidth;
    const optionsBlock = Math.max(39, optionsHeight * (wrapAmend ? 2 : 1) + 17);
    const feedback = Math.max(20, h + 4);
    const minimum = optionsBlock + Math.max(80, feedback + h + 12) + 13 + actionsHeight;
    const header = Math.max(Math.round(39 * uiScale), h + 10), toolbar = Math.max(Math.round(36 * uiScale), h + 8);
    const available = side.clientHeight - header;
    const commitHeight = Math.max(minimum, Math.min(Math.max(190, Math.round(available * .42)), available - toolbar - 155));
    side.style.gridTemplateRows = `${header}px minmax(0, 1fr)`;
    layout.style.gridTemplateRows = `${toolbar}px minmax(0, 1fr) ${commitHeight}px`;
    layout.style.setProperty("--commit-row-height", `${Math.max(Math.round(24 * uiScale), h)}px`);
    layout.style.setProperty('--commit-minimum', `${minimum}px`);
    layout.style.setProperty('--commit-toolbar-height', `${toolbar}px`);
    const empty = layout.querySelector(".empty-tool-state");
    if (empty) empty.classList.toggle("compact", available - toolbar - commitHeight < Math.max(24, h + 4) * 2 + 2);
    box.style.gridTemplateRows = `${optionsBlock}px minmax(0, 1fr) ${actionsHeight + 13}px`;
    box.style.setProperty("--commit-feedback-height", `${feedback}px`);
    const setRect = (selector, x, y, w, height) => Object.assign(box.querySelector(selector).style,
      { left: `${x}px`, top: `${y}px`, width: `${w}px`, height: `${height}px` });
    setRect(".commit-amend", 0, 5, Math.min(amendWidth, actionWidth), optionsHeight);
    setRect(".commit-last", wrapAmend ? 0 : amendWidth + textWidth(' '), wrapAmend ? 5 + optionsHeight : 5, lastWidth, optionsHeight);
    // 文件数量由 Changes/Unversioned 分组呈现，不在消息输入栏重复绘制。
    box.querySelector('.commit-count').hidden = true;
    setRect(".commit-actions > .primary-button", 2, 8, Math.min(commitWidth, actionWidth - settingsWidth - 7), actionHeight);
    setRect(".commit-actions > .secondary-button", wrap ? 2 : 2 + commitWidth + 7, wrap ? 8 + actionHeight + 7 : 8,
      Math.min(pushWidth, wrap ? actionWidth : actionWidth - commitWidth - settingsWidth - 14), actionHeight);
    setRect(".commit-actions > .icon-button", width - settingsWidth - 16, 8 + (actionHeight - settingsHeight) / 2, settingsWidth, settingsHeight);
  });
  measureWorkbenchSpace();
}
window.addEventListener("resize", measureCommitPanels);

// 字号与宿主可用空间共同约束上下工具区；受限工具窗在自身内部滚动。
// 不改变保存的首选尺寸，也不折叠用户已打开的面板。
function measureWorkbenchSpace() {
  const main = document.querySelector('.app-main');
  if (!main) return;
  const style = getComputedStyle(document.documentElement);
  const value = (name, fallback) => parseFloat(style.getPropertyValue(`--augit-${name}`)) || fallback;
  const content = document.querySelector('.editor-content');
  const view = content?.querySelector(':scope > :not([hidden])');
  const rows = view && getComputedStyle(view).display === 'grid'
    ? getComputedStyle(view).gridTemplateRows.split(' ').slice(0, -1).reduce((sum, row) => sum + (parseFloat(row) || 0), 0) : 0;
  const editorMinimum = value('tab-height', 40) + Math.max(value('document-toolbar-height', 36), rows) + 24;
  main.style.setProperty('--workbench-editor-minimum', `${editorMinimum}px`);
  document.documentElement.style.setProperty('--augit-editor-min-height', `${editorMinimum}px`);
  main.classList.toggle('space-constrained', main.classList.contains('bottom-open')
    && main.clientHeight < editorMinimum + value('bottom-min-height', 180) + 4);
  document.querySelectorAll('.changes-layout').forEach(layout => {
    const side = layout.closest('.side-tool');
    const available = side.clientHeight - side.querySelector('.tool-header').getBoundingClientRect().height;
    const cramped = available < parseFloat(layout.style.getPropertyValue('--commit-minimum'))
      + parseFloat(layout.style.getPropertyValue('--commit-toolbar-height'))
      + parseFloat(layout.style.getPropertyValue('--commit-row-height')) + 10;
    layout.classList.toggle('commit-scroll-layout', cramped);
    side.classList.toggle('commit-scroll-tool', cramped);
  });
  const settings = window.__augitLive?.settings;
  if (settings?.bottomPanelHeight > 0) window.__augitApplyPanelSizes?.(settings);
}
window.addEventListener('resize', measureWorkbenchSpace);

// 文档工具栏共享文字度量，图形与命中区按所属UI图标族缩放。
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
    target.tabIndex = 0;
    target.textContent = "符号链接目标：D:\\shared\\Augit\\配置与示例文件\\global.json";
    const toolbar = documentView.querySelector(".document-toolbar"); if (toolbar) toolbar.after(target); else documentView.prepend(target);
    documentView.classList.add("has-target");
    measureDocumentToolbar();
  }
}

if (requestedTheme === "dark") {
  document.body.dataset.theme = "dark";
}

// 视觉稿直接使用本地几何，不依赖联网后的第二次图标替换。
// 以下补齐项依据现有原生绘制；与原生一致不等于已取得 PyCharm 同场景实测。
// Stash/Shelve 托盘箭头：`platform/vcs-impl/resources/icons/new/stash.svg` 与
// `platform/icons/src/expui/vcs/shelve.svg` 属于同一份几何。提交工具栏托盘按钮与
// Stash 管理列表共用该图形但语义不同，因此分别登记、共用同一份几何以免漂移。
const trayArrowShape = '<g fill="currentColor" stroke="none"><path d="M4.64645 5.14645C4.45118 5.34171 4.45118 5.65829 4.64645 5.85355L7.64645 8.85355C7.84171 9.04882 8.15829 9.04882 8.35355 8.85355L11.3536 5.85355C11.5488 5.65829 11.5488 5.34171 11.3536 5.14645C11.1583 4.95118 10.8417 4.95118 10.6464 5.14645L8.5 7.29289V1.5C8.5 1.22386 8.27614 1 8 1C7.72386 1 7.5 1.22386 7.5 1.5V7.29289L5.35355 5.14645C5.15829 4.95118 4.84171 4.95118 4.64645 5.14645Z"/><path fill-rule="evenodd" clip-rule="evenodd" d="M4.77639 8.55279L5.5 10H10.5L11.2236 8.55279C11.393 8.214 11.7393 8 12.118 8H14C14.5523 8 15 8.44772 15 9V13C15 13.5523 14.5523 14 14 14H2C1.44772 14 1 13.5523 1 13V9C1 8.44772 1.44772 8 2 8H3.88197C4.26074 8 4.607 8.214 4.77639 8.55279ZM3.88197 9L4.88197 11H11.118L12.118 9H14V13H2V9H3.88197Z"/></g>';

const toolbarIconShapes = {
  // 菜单（汉堡）：权威 expui/general/menu.svg 是 3 条实心条，中线 y=3.5/7.5/11.5、x2..14（平头）。
  "menu": '<path stroke-linecap="butt" d="M2 3.5h12M2 7.5h12M2 11.5h12"/>',
  "window-minimize": '<rect x="3" y="8" width="10" height="1" fill="currentColor" stroke="none"/>',
  "window-maximize": '<rect x="3.5" y="3.5" width="9" height="9" fill="none"/>',
  "window-restore": '<g fill="currentColor" stroke="none"><path fill-rule="evenodd" d="M5 3H13V11H5ZM6 4V10H12V4Z"/><path fill-rule="evenodd" d="M3 5H11V13H3ZM4 6V12H10V6Z"/></g>',
  "window-close": '<g fill="currentColor" stroke="none"><rect x="1.5" y="7.5" width="13" height="1" transform="rotate(45 8 8)"/><rect x="1.5" y="7.5" width="13" height="1" transform="rotate(-45 8 8)"/></g>',
  // 工具窗口项目图标复用 expui/toolwindows/project.svg 的折角与圆角路径。
  "folder": '<path d="M8.15132 4.35836 8.29689 4.5H8.5h4.5c.8284 0 1.5.67157 1.5 1.5v6.1333c0 .7857-.5896 1.3667-1.25 1.3667H2.75c-.66045 0-1.25-.581-1.25-1.3667V3.86667C1.5 3.08099 2.08955 2.5 2.75 2.5h3.28823c.13024 0 .25534.05082.34868.14164z"/>',
  "git-commit-horizontal": '<g fill="currentColor" stroke="none"><path fill-rule="evenodd" d="M8 5a3 3 0 1 0 0 6 3 3 0 0 0 0-6ZM8 6a2 2 0 1 1 0 4 2 2 0 0 1 0-4Z"/><rect x="0" y="7.5" width="5.5" height="1" rx="0.5"/><rect x="10.5" y="7.5" width="5.5" height="1" rx="0.5"/></g>',
  "git-branch": '<path fill="currentColor" stroke="none" fill-rule="evenodd" d="M12.1232 2.67714C11.7828 2.33692 11.3696 2.16676 10.8834 2.16676C10.3975 2.16676 9.98427 2.33682 9.64395 2.67714C9.30375 3.01737 9.13366 3.43051 9.13366 3.91672C9.13366 4.23268 9.21242 4.52588 9.37041 4.79626C9.52838 5.06666 9.74087 5.27787 10.0084 5.42971C10.0084 5.74567 9.98273 6.02377 9.93118 6.26375C9.87939 6.50373 9.79441 6.71478 9.67597 6.8972C9.55721 7.07953 9.43442 7.23436 9.30672 7.36203C9.17899 7.4897 8.99973 7.60961 8.76885 7.72208C8.538 7.83449 8.32366 7.92857 8.12624 8.00464C7.92881 8.08047 7.66288 8.17008 7.32869 8.27342C6.72097 8.46187 6.25317 8.63491 5.92501 8.793V4.26292C6.19238 4.11111 6.40507 3.89993 6.56303 3.62953C6.72097 3.35912 6.79995 3.06589 6.79995 2.74993C6.79995 2.26388 6.62988 1.85064 6.28956 1.51042C5.94937 1.17022 5.53623 1 5.05005 1C4.56383 1 4.15069 1.1701 3.81037 1.51042C3.47018 1.85064 3.29999 2.26388 3.29999 2.74993C3.29999 3.06589 3.37897 3.35912 3.5369 3.62953C3.69487 3.89993 3.90752 4.11111 4.17492 4.26292V11.7371C3.90752 11.8888 3.69487 12.1002 3.5369 12.3705C3.37897 12.641 3.29999 12.9343 3.29999 13.2501C3.29999 13.7362 3.47005 14.1494 3.81037 14.4896C4.15069 14.8298 4.56393 15 5.05005 15C5.53614 15 5.94937 14.8298 6.28956 14.4896C6.62979 14.1494 6.79995 13.7362 6.79995 13.2501C6.79995 12.9343 6.72097 12.641 6.56303 12.3705C6.40504 12.1002 6.19238 11.8888 5.92501 11.7371V11.5001C5.92501 11.0809 6.05109 10.7772 6.30323 10.5888C6.55537 10.4003 7.07035 10.1846 7.84823 9.94163C8.66867 9.68029 9.28553 9.43405 9.6987 9.20307C11.0598 8.43155 11.7464 7.17367 11.7585 5.42968C12.0261 5.27787 12.2386 5.06663 12.3965 4.79622C12.5544 4.52582 12.6335 4.23265 12.6335 3.91669C12.6336 3.4306 12.4634 3.01746 12.1232 2.67714Z"/>',
  "search": '<circle cx="7" cy="7" r="4.5"/><path d="M10.1992 10.2002 13.4992 13.4961"/>',
  // expui/inline/searchHistory.svg：空心环、斜向手柄和右上小三角。
  "history-search": '<g fill="currentColor" stroke="none"><path d="M8.85355 10.1432c-.19526-.19531-.51184-.19531-.7071 0-.19527.1953-.19527.5118 0 .7071l2.99925 2.9992c.1952.1953.5118.1953.7071 0 .1952-.1952.1952-.5118 0-.7071zM9 7c0 2.20914-1.79086 4-4 4v1c2.76142 0 5-2.23858 5-5zm-4 4c-2.20914 0-4-1.79086-4-4H0c0 2.76142 2.23858 5 5 5zm-4-4c0-2.20914 1.79086-4 4-4V2C2.23858 2 0 4.23858 0 7zm4-4c2.20914 0 4 1.79086 4 4h1c0-2.76142-2.23858-5-5-5z"/><path d="M11 7l2.5 2.5L16 7z"/></g>',
  "history-back": '<path d="m12 2-6 6 6 6"/>',
  "arrow-up": '<path d="M8 14V3.9M3.65 6.35 8 2l4.35 4.35"/>',
  "arrow-down": '<path d="M8 2v10.1M3.65 9.65 8 14l4.35-4.35"/>',
  "arrow-left": '<path d="M14 8H3.9M6.35 3.65 2 8l4.35 4.35"/>',
  "arrow-right": '<path d="M2 8h10.1M9.65 3.65 14 8l-4.35 4.35"/>',
  "chevron-left": '<path d="m10 4.5-3.5 3.5 3.5 3.5"/>',
  "chevron-right": '<path d="m6 4.5 3.5 3.5-3.5 3.5"/>',
  "chevron-up": '<path d="m4.5 9.75 3.5-3.5 3.5 3.5"/>',
  "plus": '<path fill="currentColor" stroke="none" fill-rule="evenodd" d="M7 1H8V7H14V8H8V14H7V8H1V7H7Z"/>',
  "minus": '<path fill="currentColor" stroke="none" fill-rule="evenodd" d="M1 7H14V8H1Z"/>',
  // 删除：权威 expui/general/delete.svg —— 盖 x2..14、桶体 x4..12（底角圆角）、把手为弧形，
  // 且桶内**有两条竖线**（x6.5／x9.5，y7..12，这是 trash-2 与普通垃圾桶的区别）。
  "trash-2": '<path d="M2.5 4.5h11"/><path d="M6.5 4.5V3.3a1 1 0 0 1 1-1h1a1 1 0 0 1 1 1v1.2"/><path d="M4.5 6.5h7v6a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1z"/><path d="M6.5 8v3.5M9.5 8v3.5"/>',
  "x": '<path stroke-width="1" d="m3.5 12.5 9-9m0 9-9-9"/>',
  // 标签关闭：权威 expui/general/closeSmall.svg 是填充叉、可见约 7 单位、固定色 #A8ADBD。
  // 与对话框/搜索用的 16px `x` 描边叉分开登记，避免改一处影响通用关闭动作。
  "close-small": '<path fill="currentColor" stroke="none" fill-rule="evenodd" clip-rule="evenodd" d="M11.4939 4.48784C11.3002 4.28007 10.9724 4.27548 10.7729 4.47775L8.00074 7.28849L5.22871 4.47788C5.02922 4.27561 4.70143 4.2802 4.50768 4.48797C4.32506 4.68382 4.32933 4.98882 4.51736 5.17947L7.29908 7.99991L4.51756 10.8201C4.32953 11.0108 4.32526 11.3158 4.50788 11.5116C4.70163 11.7194 5.02942 11.724 5.22892 11.5217L8.00074 8.71133L10.7727 11.5219C10.9722 11.7241 11.3 11.7196 11.4937 11.5118C11.6764 11.3159 11.6721 11.0109 11.484 10.8203L8.7024 7.99991L11.4843 5.17934C11.6723 4.98869 11.6766 4.68368 11.4939 4.48784Z"/>',
  "pilcrow": '<g stroke-width="1"><ellipse cx="6" cy="5" rx="4" ry="4"/><path d="M8 1v14M12 1v14"/></g>',
  "corner-down-right": '<path stroke-width="1" d="M2 2v9h11M9 7l4 4-4 4"/>',
  "history-expand": '<path d="M8 1v14M4 5l4-4 4 4M4 11l4 4 4-4"/>',
  "cloud": '<path d="m3 8 5-4 5 4-5 4Z"/><circle cx="3" cy="8" r="2.2"/><circle cx="8" cy="4" r="2.2"/><circle cx="13" cy="8" r="2.2"/><circle cx="8" cy="12" r="2.2"/>',
  "stash": trayArrowShape,
  "folder-git-2": '<g stroke-width="1"><path d="M.5 4h5l2-2h8v12H.5Z M7.5 8h4v2"/><circle cx="7" cy="7.5" r="1.5"/><circle cx="12" cy="7.5" r="1.5"/><circle cx="12" cy="11.5" r="1.5"/></g>',
  // 分支菜单的更新项目与推送按 `expui/vcs/update.svg`、`expui/vcs/push.svg` 校准：
  // 两者互为 180° 旋转，更新项目朝左下、推送朝右上。其余分支菜单图标尚未计为校准完成。
  "branch-update": '<path d="m12 4-8 8H8M4 12V8"/>',
  "branch-push": '<path d="m4 12 8-8H8M12 4v4"/>',
  // 区分大小写：权威 expui/inline/matchCase.svg 是**填充的 "Cc" 字形对**（字体轮廓）。
  // 按 §7.0 不复制字体轮廓，这里按官方渲染比例自绘等效版本：大 C（中心 5.2,7.4、半径 4.6）
  // + 右下小 c（中心 11.7,11.3、半径 3.0），开口都朝右。
  "case-sensitive": '<path d="M8.16 3.88A4.6 4.6 0 1 0 8.16 10.92"/><path d="M13.63 9A3 3 0 1 0 13.63 13.6"/>',
  // 查找面板「全字匹配」：权威 expui/inline/exactWords.svg 是**填充的字母 W**（描边宽约 1.33px），
  // 不是旧版的 |ab| 括号形。这里按官方 W 的中心线自绘描边等效版本（官方外缘 x1.30..14.51、y3.9..13）。
  "whole-word": '<path d="M2.1 4.6 5 12.6 7.9 5.5 10.8 12.6 13.7 4.6"/>',
  // 查找面板「正则」：权威 expui/inline/regex.svg 的星号与**实心方点**（x2.9675 y12.35 1.65×1.65），
  // 不是圆点。星号保持现有的描边三线形式（与官方填充字形渲染等效）。
  "regex": '<g stroke-width="1"><path d="M10.5 3v7m-3-5.25 6 3.5m-6 0 6-3.5"/><rect x="2.9675" y="12.35" width="1.65" height="1.65" fill="currentColor" stroke="none"/></g>',
  // 齿轮按官方 expui/general/settings.svg 的外缘实测参数自绘：瓣尖是绕中心 r=6.5 的圆弧（跨瓣尖中心 ±9.1°）、
  // 瓣谷是 r=1.073 的凹弧（中心在谷射线 6.013 处）、瓣侧是连接两者的直线（中心线到中心垂距 4.186 / 法线 −29°）。
  // 中心孔为 r=2.151 的 1px 环（外 2.651 / 内 1.651）。逐角外缘偏差 ≤0.04px。
  "settings": '<path d="M14.072 5.681A6.5 6.5 0 0 0 13.044 3.901L10.988 3.865A1.073 1.073 0 0 1 10.087 3.345L9.028 1.582A6.5 6.5 0 0 0 6.972 1.582L5.913 3.345A1.073 1.073 0 0 1 5.012 3.865L2.956 3.901A6.5 6.5 0 0 0 1.928 5.681L2.925 7.48A1.073 1.073 0 0 1 2.925 8.52L1.928 10.319A6.5 6.5 0 0 0 2.956 12.099L5.012 12.135A1.073 1.073 0 0 1 5.913 12.655L6.972 14.418A6.5 6.5 0 0 0 9.028 14.418L10.087 12.655A1.073 1.073 0 0 1 10.988 12.135L13.044 12.099A6.5 6.5 0 0 0 14.072 10.319L13.075 8.52A1.073 1.073 0 0 1 13.075 7.48L14.072 5.681Z"/><circle cx="8" cy="8" r="2.151"/>',
  "document-source": '<path stroke-width="1" d="M3 3.5h10M3 6.5h10M3 9.5h10M3 12.5h10"/>',
  "document-split": '<g stroke-width="1"><path d="M1.5 3.5H6M1.5 6.5H6M1.5 9.5H6M1.5 12.5H6"/><rect x="8.5" y="2.5" width="6" height="11" rx="1.5"/></g>',
  // 预览模式：权威 expui/general/previewOnly.svg = 圆角框（rx1.5）+ 圆点(10,6) r1.5 + 山形（x2.36→12.0）。
  "document-preview": '<g stroke-width="1"><rect x="2.5" y="2.5" width="11" height="11" rx="1.5"/><circle cx="10" cy="6" r="1.5"/><path d="M2.36 9.5 4.18 7.67 12 13.5"/></g>',
  "document-formatted": '<g fill="none" stroke-width="1"><path d="M6.5 2.5C4.5 2.5 4.5 4.5 4.5 6c0 1.2-1 1.2-1 2s1 0.8 1 2c0 1.5 0 3.5 2 3.5M9.5 2.5c2 0 2 2 2 3.5 0 1.2 1 1.2 1 2s-1 .8-1 2c0 1.5 0 3.5-2 3.5"/></g>',
  // 缩小：同圆，只有减号（横条 y=8 跨 x4..12）。
  "zoom-out": '<circle cx="8" cy="8" r="6.5"/><rect x="4" y="7.5" width="8" height="1" rx="0.5" fill="currentColor" stroke="none"/>',
  // 放大：权威 expui/image/zoomIn.svg = **无手柄的圆**（中心 8,8、r6.5）+ 贯穿圆心的加号
  // （竖条 x=8 跨 y4..12、横条 y=8 跨 x4..12，官方是 rx0.5 的实心条，这里用圆头 1px 线等效）。
  "zoom-in": '<circle cx="8" cy="8" r="6.5"/><g fill="currentColor" stroke="none"><rect x="7.5" y="4" width="1" height="8" rx="0.5"/><rect class="image-zoom-crossbar" x="12" y="7.5" width="1" height="8" rx="0.5" transform="rotate(90 12 7.5)"/></g>',
  // 适应区域：权威 expui/image/fitContent.svg = 圆角外框（1.5,2.5 13×11 rx1.5）+ 两组内角线
  // （左上：竖 x4.5 跨 y5..9、横 y5.5 跨 x4..8；右下：竖 x11.5 跨 y7..11、横 y10.5 跨 x8..12）。
  "image-fit": '<rect x="1.5" y="2.5" width="13" height="11" rx="1.5"/><g fill="currentColor" stroke="none"><rect x="11" y="7" width="1" height="4" rx="0.5"/><rect x="12" y="10" width="1" height="4" rx="0.5" transform="rotate(90 12 10)"/><rect x="4" y="5" width="1" height="4" rx="0.5"/><rect x="8" y="5" width="1" height="4" rx="0.5" transform="rotate(90 8 5)"/></g>',
  // 单栏：权威 expui/diff/unified.svg —— 外缘 2..14、外圆角 2／内圆角 1 ⇒ 中心线圆角 1.5。
  "diff-unified": '<rect stroke-width="1" x="2.5" y="2.5" width="11" height="11" rx="1.5"/>',
  // 双栏：同一外框（中心线圆角 1.5）+ 中缝。官方两栏内缘在 y3..13，故中缝取 `M8 3v10`，
  // 而不是画到外框中心线（2.5..13.5）——那样会穿过上下边线。
  "diff-side-by-side": '<g stroke-width="1"><rect x="2.5" y="2.5" width="11" height="11" rx="1.5"/><path d="M8 3v10"/></g>',
  "diff-ignore-whitespace": '<path stroke-width="1" d="M9 2.5v11M12 2.5v11M14 2.5H6a3.5 3.5 0 0 0 0 7h3"/>',
  "ellipsis-vertical": '<g fill="currentColor" stroke="none"><circle cx="8" cy="3" r="1"/><circle cx="8" cy="8" r="1"/><circle cx="8" cy="13" r="1"/></g>',

  // expui/general/copy.svg：前页为 9×10、1.5 圆角，后页保留完整右侧轮廓与三条横线。
  "copy": '<rect x="2.5" y="3.5" width="9" height="10" rx="1.5" stroke="currentColor" stroke-width="1"/><g fill="currentColor" stroke="none"><rect x="5" y="6" width="4" height="1" rx="0.5"/><rect x="5" y="8" width="4" height="1" rx="0.5"/><rect x="5" y="10" width="4" height="1" rx="0.5"/><path fill-rule="evenodd" clip-rule="evenodd" d="M11.0017 2H11.5998C12.373 2 12.9998 2.6268 12.9998 3.4V3.91081C13.0011 3.94038 13.0017 3.97011 13.0017 4V11.5482C13.6063 11.1124 13.9998 10.4021 13.9998 9.6V3.4C13.9998 2.07452 12.9253 1 11.5998 1H6.39978C5.59677 1 4.88587 1.39437 4.4502 2H6.39978H11.0017Z"/></g>',
  "cherry": '<circle cx="3.5" cy="11.5" r="2.5"/><circle cx="11.5" cy="12.5" r="2.5"/><path d="M3.5 9C4 6 10 6 10 2 M11.5 10C13 6 10 5 10 2 M10 2C7 2 6 3 6 5"/>',
  "git-branch-plus": '<circle cx="4" cy="3" r="2"/><circle cx="4" cy="13" r="2"/><circle cx="12" cy="3" r="2"/><path d="M4 5v6M4 10C4 6 12 9 12 5M12 9v6M9 12h6"/>',
  "tag": '<path d="M1 3h7l7 7-6 5-8-7Z"/><circle cx="4.5" cy="6.5" r="1"/>',
  // 收藏：权威 `platform/icons/src/nodes/favorite.svg` 是五角星多边形（固定填充 #F4AF3D）。
  // Augit 的图标一律跟随 currentColor（这样禁用/深色/浅色都能正确着色），故同形但用 currentColor 填充；
  // 色值差异登记在轮次记录里。
  "star": '<polygon fill="currentColor" stroke="none" points="8 11.5 3.7 14.3 5 9.3 1 6.1 6.1 5.8 8 1 9.9 5.8 15 6.1 11 9.3 12.3 14.3"/>',
  // 外部打开／在资源管理器中定位：权威 expui/ide/externalLink.svg 是**角 + 斜向箭头、没有方框**
  // （旧版那个带方框的 "在新窗口打开" 图形不是 New UI 的形态）。
  "external-link": '<path d="M5.5 5.5H10.5V10.5M9.65 6.35 4.5 11.5"/>',
  "list-tree": '<path d="M2 3v10M2 3h3M2 8h3M2 13h3M8 3h6M8 8h6M8 13h6"/>',
  "wrap-text": '<path d="M2 3h12M2 7h9a3 3 0 0 1 0 6H7l2-2M7 13l2 2M2 13h2"/>',
  "chevron-down": '<path d="m4.5 6.25 3.5 3.5 3.5-3.5"/>',
  "refresh-cw": '<path d="M1 8l1.83 1.88L4.7 8 M15 8l-1.83-1.88L11.3 8 M2.83 9.88A5.5 5.5 0 0 1 10.75 3.24 M13.17 6.12A5.5 5.5 0 0 1 5.25 12.76"/>',
  "git-compare-arrows": '<path d="M7 4h8 M10 1 7 4l3 3 M1 12h8 M6 9l3 3-3 3"/>',
  "undo-2": '<path d="M3 5H10.5a4 4 0 0 1 0 8H5.5M5.5 1.5 1.8 5l3.7 3.5"/>',
  "download": trayArrowShape,
  "eye": '<circle cx="8" cy="8" r="1.5"/><path d="M8 11.5C5.62351 11.5 3.2737 9.94494 2.53088 8C3.2737 6.05506 5.62351 4.5 8 4.5C10.3765 4.5 12.7263 6.05506 13.4691 8C12.7263 9.94494 10.3765 11.5 8 11.5Z"/>',
  "locate-fixed": '<path fill="currentColor" stroke="none" fill-rule="evenodd" clip-rule="evenodd" d="M8.5 5V2.02054C11.4149 2.26101 13.739 4.5851 13.9795 7.5H11C10.7239 7.5 10.5 7.72386 10.5 8S10.7239 8.5 11 8.5H13.9795C13.739 11.4149 11.4149 13.739 8.5 13.9795V11C8.5 10.7239 8.27614 10.5 8 10.5S7.5 10.7239 7.5 11V13.9795C4.5851 13.739 2.26101 11.4149 2.02054 8.5H5C5.27614 8.5 5.5 8.27614 5.5 8S5.27614 7.5 5 7.5H2.02054C2.26101 4.5851 4.5851 2.26101 7.5 2.02054V5C7.5 5.27614 7.72386 5.5 8 5.5S8.5 5.27614 8.5 5ZM1 8C1 4.13401 4.13401 1 8 1S15 4.13401 15 8 11.866 15 8 15 1 11.866 1 8Z"/>',
  "fold-vertical": '<path d="M4.5 2.5 8 6l3.5-3.5M4.5 13.5 8 10l3.5 3.5"/>',
  // expui/fileTypes/shell.svg 的圆角外框与 1.2px 提示符/下划线几何。
  "square-terminal": '<rect x="1.5" y="2.5" width="13" height="11" rx="1.5"/><path d="M7.5 10.5h3M4.5 9.5 6.5 7.5 4.5 5.5"/>',
  // 历史与 Git 历史是两个不同图形：前者是「时钟」（general/history.svg），后者是 VCS 工具窗口的「两空心节点分支图」
  // （toolwindows/vcs.svg，design-system §7.2 的「Git 历史」一行）；标题栏分支芯片用的是 vcs/branch.svg 的三实心圆盘。
  "history": '<circle cx="8" cy="8" r="6.5"/><path d="M8 5V8L10.5 9.5"/>',
  "git-history": '<circle cx="4.5" cy="4" r="2"/><path d="M4.5 11.5H8.5C9.60457 11.5 10.5 10.6046 10.5 9.5V8"/><path d="M4.5 6.5V14.5"/><circle cx="10.5" cy="6" r="2"/>',
  "rename": '<path d="M3 13 5 5l8 8H3Zm2-8 3 3m1-7 4 4"/>',
  "clone": '<g stroke-width="1"><path d="M1.5 5h5l1.5 1.5h6.5v7h-13Z"/><circle cx="6" cy="9" r="1.4"/><circle cx="11" cy="11" r="1.4"/><path d="M7.2 9.8 9.8 10.4"/></g>',
  "conflict": '<g stroke-width="1"><path d="M3 1.5h7l3 3v10H3Z M10 1.5v3h3M8 7v4"/><circle cx="8" cy="13" r=".8"/></g>',
};
// 工具轨采用独立的 20px 资源几何；不能把 16px 图形放大后当作同一图标。
const railIconShapes = {
  "search": '<path fill-rule="evenodd" d="M16 9C16 10.6478 15.4307 12.1625 14.478 13.3584C14.5086 13.3812 14.5379 13.4065 14.5657 13.4343L18.5657 17.4343C18.8781 17.7467 18.8781 18.2533 18.5657 18.5657S17.7467 18.8781 17.4343 18.5657L13.4343 14.5657C13.4065 14.5379 13.3812 14.5086 13.3584 14.478C12.1625 15.4307 10.6478 16 9 16C5.13401 16 2 12.866 2 9S5.13401 2 9 2 16 5.13401 16 9ZM14.5 9C14.5 12.0376 12.0376 14.5 9 14.5S3.5 12.0376 3.5 9 5.96243 3.5 9 3.5 14.5 5.96243 14.5 9Z"/>',
  "square-terminal": '<rect x="1.75" y="2.75" width="16.5" height="14.5" rx="1.25" fill="none" stroke="currentColor"/><path d="M5.75 11.75 8.5 9 5.75 6.25" fill="none" stroke="currentColor"/><path d="M9 13.2788C9 12.8646 9.33579 12.5288 9.75 12.5288L14.25 12.5a.75.75 0 0 1 0 1.5L9.75 14.0288C9.33579 14.0288 9 13.693 9 13.2788Z"/>',
  "folder": '<path d="M10.5199 5.57617 10.7285 5.75H17a1.25 1.25 0 0 1 1.25 1.25v8.1667c0 .9004-.697 1.5833-1.5 1.5833H3.25c-.803 0-1.5-.6829-1.5-1.5833V4.83333c0-.90039.69705-1.58333 1.5-1.58333h4.38795c.05848 0 .11512.0205.16005.05794Z" fill="none" stroke="currentColor"/>',
  "git-commit-horizontal": '<path fill-rule="evenodd" d="M19.25 9.25C19.6642 9.25 20 9.58579 20 10S19.6642 10.75 19.25 10.75H13.9298C13.5787 12.6006 11.9528 14 10 14S6.42125 12.6006 6.0702 10.75H.75C.335787 10.75 0 10.4142 0 10S.335787 9.25 .75 9.25H6.0702C6.42125 7.39935 8.04721 6 10 6S13.5787 7.39935 13.9298 9.25ZM10 12.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Z"/>',
  "git-history": '<path fill-rule="evenodd" d="M7.5 4.5C7.5 5.60457 6.60457 6.5 5.5 6.5S3.5 5.60457 3.5 4.5 4.39543 2.5 5.5 2.5 7.5 3.39543 7.5 4.5ZM9 4.5C9 6.17556 7.82259 7.57612 6.25 7.91946V13.25H9.5C10.2092 13.25 10.7035 13.2496 11.0904 13.2232 11.4706 13.1973 11.692 13.1487 11.861 13.0787 12.4124 12.8504 12.8504 12.4124 13.0787 11.861 13.1487 11.692 13.1973 11.4706 13.2232 11.0904 13.244 10.7848 13.2487 10.4121 13.2497 9.91939C11.6773 9.57594 10.5 8.17546 10.5 6.5C10.5 4.567 12.067 3 14 3S17.5 4.567 17.5 6.5C17.5 8.17565 16.3225 9.57626 14.7498 9.91951C14.7488 10.4185 14.7439 10.8379 14.7197 11.1925 14.6886 11.6491 14.6229 12.0528 14.4645 12.4351 14.0839 13.3539 13.3539 14.0839 12.4351 14.4645 12.0528 14.6229 11.6491 14.6886 11.1925 14.7197 10.7485 14.75 10.203 14.75 9.52664 14.75H6.25V18a.75.75 0 0 1-1.5 0V7.91946C3.17741 7.57612 2 6.17556 2 4.5C2 2.567 3.567 1 5.5 1S9 2.567 9 4.5ZM16 6.5a2 2 0 1 0-4 0 2 2 0 0 0 4 0Z"/>',
};
function icon(name, rail = false) {
  if (rail && Object.hasOwn(railIconShapes, name)) {
    return `<svg class="augit-toolbar-icon" viewBox="0 0 20 20" fill="currentColor" stroke="none" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" data-augit-icon="${name}">${railIconShapes[name]}</svg>`;
  }
  if (name === "none") return '<span class="menu-icon-empty" aria-hidden="true"></span>';
  if (name === "folder-open") return treeFolderIcon();
  if (name === "lock") {
    return '<svg class="augit-toolbar-icon" viewBox="0 0 12 12" fill="none" stroke="currentColor" stroke-width="1" aria-hidden="true" data-augit-icon="lock"><path d="M3 5V3a3 3 0 0 1 6 0v2"/><rect x="1" y="5" width="10" height="7"/></svg>';
  }
  if (name === "file-warning") {
    return '<svg class="augit-toolbar-icon" viewBox="0 0 18 18" fill="none" stroke="currentColor" stroke-width="1" aria-hidden="true" data-augit-icon="file-warning"><path d="M3 1h8l4 4v12H3Z M11 1v4h4M9 7v4"/><circle cx="9" cy="14" r="1"/></svg>';
  }
  if (!Object.hasOwn(toolbarIconShapes, name)) throw new Error(`视觉稿未登记图标：${name}`);
  return `<svg class="augit-toolbar-icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" data-augit-icon="${name}">${toolbarIconShapes[name]}</svg>`;
}

// 窗口按钮：视觉稿里它们是纯标记（静态稿不描述真实的窗口状态）。
// 实时外壳把这三点接到宿主窗口命令上（见 web/src/live-data.js），并在最大化时
// 换成还原图形——这是 Windows 窗口按钮的既有语义，视觉稿只画了还原态。
function windowActionIcons() {
  const maximized = !!(window.__augitLive && window.__augitLive.windowMaximized);
  return '<span class="window-dot" data-window-action="minimize" aria-label="Minimize" title="Minimize">' + icon("window-minimize")
    + '</span><span class="window-dot" data-window-action="maximize" aria-label="' + (maximized ? "Restore Down" : "Maximize")
    + '" title="' + (maximized ? "Restore Down" : "Maximize") + '">' + icon(maximized ? "window-restore" : "window-maximize")
    + '</span><span class="window-dot" data-window-action="close" aria-label="Close window" title="Close window">' + icon("window-close") + '</span>';
}

// 目录和引用是树节点图形，不套用工具栏的粗线空心文件夹或菜单标签。
function treeFolderIcon(workspaceRoot = false) {
  // expui/nodes/folder.svg 的 13×11 外框，圆角只由原生路径体现，不用直角简化图形。
  const folder = '<path d="M8.10584 4.34613 8.25344 4.5H8.46667H13c.8284 0 1.5.67157 1.5 1.5v6.1333c0 .8196-.568 1.3667-1.1333 1.3667H2.63333C2.06804 13.5 1.5 12.9529 1.5 12.1333V3.86667C1.5 3.04707 2.06804 2.5 2.63333 2.5H6.1217c.13622 0 .26654.05557.36083.15387z" fill="var(--augit-folder-fill)" stroke="var(--augit-folder-outline)" stroke-width="1"/>';
  const badge = workspaceRoot ? '<rect x="9" y="9" width="7" height="7" rx="2" fill="var(--augit-root-fill)" stroke="var(--augit-row-background, var(--augit-panel))" stroke-width="3"/><rect x="9.5" y="9.5" width="6" height="6" rx="1.5" fill="var(--augit-root-fill)" stroke="var(--augit-root-outline)"/>' : '';
  return `<svg class="tree-folder-icon" viewBox="0 0 16 16" aria-hidden="true">${folder}${badge}</svg>`;
}

// Git 引用标签图标：形状沿用现有标签形，颜色按分组取权威色（本地分支绿／远程紫／标签灰／HEAD 黄）。
// 当前分支（HEAD）按参考图取两色：描边用本地分支绿、内点用 HEAD 黄（main 标签就是这个样子）。
// 日志/引用面板里的引用串分组：HEAD 当前分支、`tag: ` 前缀为标签，其余按是否落在远程名前缀上区分。
function refKindOf(refs) {
  const list = Array.isArray(refs) ? refs : String(refs || "").split(" ");
  if (list.includes("HEAD")) return "head";
  const first = list.find((name) => name && name !== "HEAD" && name !== "->") || "";
  if (first.startsWith("tag:")) return "tag";
  // 实时外壳里 `live.remotes` 是 `{available, remotes: [...]}` 而不是数组（见 live-data.js 的既有用法），
  // 静态视觉稿没有该对象 ⇒ 回落到 local。
  const remotes = (window.__augitLive && window.__augitLive.remotes && window.__augitLive.remotes.remotes) || [];
  return remotes.some((remote) => first.startsWith(`${(remote && remote.name) || remote}/`)) ? "remote" : "local";
}
// 引用显示名去掉 `tag:` 前缀与 HEAD/箭头。
function refDisplayName(refs) {
  const list = Array.isArray(refs) ? refs : String(refs || "").split(" ");
  return list.filter((token) => token && token !== "HEAD" && token !== "->")
    .map((token) => token.replace(/^tag:/, "")).join(" ");
}

function gitReferenceIcon(kind = "local") {
  const outline = 'M8.6 1.5h4.9c.55 0 1 .45 1 1v4.9L7.9 14c-.39.39-1.01.39-1.4 0L2 9.5c-.39-.39-.39-1.01 0-1.4L8.6 1.5Z M12 5c0 .83-.67 1.5-1.5 1.5S9 5.83 9 5s.67-1.5 1.5-1.5S12 4.17 12 5Z';
  if (kind === "head") {
    return `<svg class="git-reference-icon" viewBox="0 0 16 16" aria-hidden="true"><path d="${outline}" fill="none" fill-rule="evenodd" stroke="var(--augit-ref-branch)" stroke-width="1" stroke-linejoin="round"/><circle cx="10.5" cy="5" r="1.5" fill="var(--augit-ref-head)"/></svg>`;
  }
  const color = { local: 'var(--augit-ref-branch)', remote: 'var(--augit-ref-remote)', tag: 'var(--augit-ref-tag)' }[kind] || 'var(--augit-ref-branch)';
  return `<svg class="git-reference-icon" viewBox="0 0 16 16" aria-hidden="true"><path d="${outline}" fill="${color}" fill-rule="evenodd" stroke="none"/></svg>`;
}

/** 日志引用按名称独立保留；HEAD 与同一提交的首个分支用重叠色标表达，不合成单色字符串。 */
function logReferenceLabels(refs, atHead = false) {
  const values = (Array.isArray(refs) ? refs : []).filter(name => name && name !== '->');
  if (atHead && !values.includes('HEAD')) values.unshift('HEAD');
  const names = values.filter(name => name !== 'HEAD');
  const hasHead = values.includes('HEAD');
  if (!names.length && hasHead) names.push('HEAD');
  const colors = { head: 'var(--augit-ref-head)', local: 'var(--augit-ref-branch)', remote: 'var(--augit-ref-remote)', tag: 'var(--augit-ref-tag)' };
  const outline = 'M13.4495 7.5001 7.01028 13.9394C6.81502 14.1347 6.49843 14.1347 6.30317 13.9394L2.06053 9.69679C1.86527 9.50152 1.86527 9.18494 2.06053 8.98968L8.49978 2.55035C8.61106 2.43907 8.76824 2.38667 8.92404 2.40893L12.6363 2.93926C12.8563 2.97069 13.0292 3.14354 13.0606 3.36352L13.5909 7.07584C13.6132 7.23163 13.5608 7.38882 13.4495 7.5001Z';
  return names.map((name, index) => {
    const kind = name === 'HEAD' ? 'head' : refKindOf([name]);
    const layers = hasHead && index === 0 && kind !== 'head' ? ['head', kind] : [kind];
    const width = Math.round(16 + (layers.length - 1) * 5.12);
    const tags = [...layers].reverse().map((layer, i) => `<g transform="translate(${i * 5.12} 0)"><path d="${outline}" fill="var(--augit-row-background, var(--augit-panel))" stroke="${colors[layer]}"/>${i === layers.length - 1 ? `<circle cx="10.5" cy="5.5" r="1" fill="${colors[layer]}"/>` : ''}</g>`).join('');
    return `<span class="log-reference-label" data-log-reference="${escapeHtml(name)}" title="${escapeHtml(name)}"><svg class="log-reference-icon" data-reference-layers="${layers.join(' ')}" viewBox="0 0 ${width} 16" style="width:${width}px" stroke-width="1" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${tags}</svg><span>${escapeHtml(name.replace(/^tag:\s*/, ''))}</span></span>`;
  }).join('');
}

// 文件类型图标与原生绘制使用相同的 16px 坐标；Git 状态只改变文件名颜色。
/**
 * 变更状态符号。权威 `GitChangeType`
 *（`plugins/git4idea/backend/src/history/GitChangeType.java:11-18`）给出的就是 Git 的单字母
 * `MODIFIED('M')`／`ADDED('A')`／`COPIED('C')`／`DELETED('D')`／`RENAMED('R')`／
 * `UNRESOLVED('U')`／`TYPE_CHANGED('T')`，`toString()` 返回大写字母。
 * `Untracked` 不在该枚举里（Git porcelain 用 `??`，权威没有对应字母）⇒ 不给符号；
 * Augit 把未跟踪文件放在「Unversioned Files」分组里，分组本身说明了状态。
 */
const changeStatusSymbols = {
  modified: "M", added: "A", copied: "C", deleted: "D",
  renamed: "R", unmerged: "U", typechanged: "T",
};

/** 两列 porcelain 状态保留为说明，不能用一种文件名颜色冒充两个快照。 */
function gitStatusDescription(file) {
  if (!file || (typeof file.indexStatus !== "string" && typeof file.workTreeStatus !== "string")) return "";
  const labels = { " ": "Unchanged", M: "Modified", A: "Added", D: "Deleted", R: "Renamed", C: "Copied", T: "Type Changed", U: "Unmerged", "?": "Unversioned" };
  const label = value => labels[value || " "] || "Unknown";
  return `Index: ${label(file.indexStatus)}; Working tree: ${label(file.workTreeStatus)}`;
}

/**
 * 变更行的辅助技术名称：状态符号（有则加）＋ 文件名。
 * 规格 §7.8：**原生列表的辅助技术名称同时保留状态符号与文件名**——视觉上状态只由文件名颜色表达，
 * 读屏拿不到颜色，因此把符号写进名称。
 */
function changeAccessibleName(kind, text, file = null) {
  const symbol = changeStatusSymbols[String(kind || "").toLowerCase()] || "";
  const composite = file && file.indexStatus === "D" && file.workTreeStatus === "?"
    ? ", staged deletion; an untracked file exists at the same path"
    : "";
  const detail = gitStatusDescription(file);
  return `${symbol ? `${symbol} ` : ""}${text}${composite}${detail ? `, ${detail}` : ""}`;
}

// 文件类型来自实际 FileType 与 PlatformIconMappings 调用路径；不按图标文件名猜测语义。
const sceneFileIconShapes = {
  javascript: '<path d="M14 4C14 2.895 13.105 2 12 2H4C2.895 2 2 2.895 2 4V12C2 13.105 2.895 14 4 14H12C13.105 14 14 13.105 14 12V4Z" fill="currentColor"/><path d="M9.554 11.859C9.882 12.013 10.258 12.09 10.683 12.09C11.105 12.09 11.479 12.012 11.807 11.855C12.135 11.698 12.39 11.483 12.572 11.21C12.755 10.933 12.846 10.624 12.846 10.282C12.846 10 12.783 9.733 12.658 9.482C12.533 9.232 12.354 9.022 12.124 8.854C11.896 8.686 11.635 8.576 11.341 8.525L10.345 8.362C10.143 8.328 9.979 8.241 9.854 8.102C9.728 7.959 9.665 7.791 9.665 7.597C9.665 7.432 9.708 7.286 9.794 7.161C9.879 7.036 9.999 6.939 10.153 6.87C10.307 6.802 10.485 6.768 10.687 6.768C10.89 6.768 11.068 6.803 11.222 6.875C11.376 6.946 11.495 7.047 11.581 7.178C11.666 7.306 11.709 7.452 11.709 7.614H12.722C12.719 7.281 12.631 6.984 12.457 6.725C12.283 6.466 12.041 6.263 11.73 6.118C11.422 5.973 11.07 5.9 10.674 5.9C10.287 5.9 9.939 5.975 9.631 6.126C9.323 6.275 9.083 6.481 8.909 6.746C8.738 7.009 8.652 7.305 8.652 7.636C8.652 7.912 8.709 8.167 8.823 8.401C8.94 8.632 9.105 8.824 9.319 8.978C9.533 9.132 9.785 9.234 10.076 9.286L11.089 9.457C11.311 9.494 11.489 9.594 11.623 9.756C11.757 9.916 11.824 10.107 11.824 10.329C11.824 10.505 11.777 10.662 11.683 10.799C11.592 10.933 11.461 11.037 11.29 11.111C11.119 11.185 10.921 11.222 10.696 11.222C10.465 11.222 10.26 11.182 10.08 11.103C9.903 11.02 9.765 10.905 9.665 10.756C9.566 10.608 9.516 10.44 9.516 10.252H8.503C8.506 10.611 8.6 10.93 8.785 11.21C8.97 11.486 9.227 11.703 9.554 11.859Z" fill="var(--augit-js-ink)"/><path d="M5.222 11H4.213V12H5.32C5.648 12 5.939 11.932 6.192 11.795C6.446 11.655 6.644 11.461 6.786 11.213C6.929 10.965 7 10.68 7 10.358L7 6H6L6 10.222C6 10.376 5.967 10.513 5.902 10.632C5.839 10.749 5.748 10.84 5.628 10.906C5.511 10.969 5.376 11 5.222 11Z" fill="var(--augit-js-ink)"/>',
  typescript: '<path d="M14 4C14 2.895 13.105 2 12 2H4C2.895 2 2 2.895 2 4V12C2 13.105 2.895 14 4 14H12C13.105 14 14 13.105 14 12V4Z" fill="currentColor"/><path d="M3.135 6H7.872V7H6V12H5V7H3.135V6Z" fill="white"/><path d="M10.683 12.09C10.259 12.09 9.883 12.013 9.555 11.859C9.227 11.703 8.97 11.486 8.785 11.21C8.6 10.93 8.506 10.611 8.503 10.252H9.516C9.516 10.44 9.566 10.608 9.666 10.756C9.766 10.905 9.904 11.02 10.081 11.103C10.26 11.182 10.465 11.222 10.696 11.222C10.921 11.222 11.119 11.185 11.29 11.111C11.461 11.037 11.592 10.933 11.684 10.799C11.778 10.662 11.825 10.506 11.825 10.329C11.825 10.107 11.758 9.916 11.624 9.756C11.49 9.594 11.312 9.494 11.089 9.457L10.076 9.286C9.786 9.235 9.533 9.132 9.32 8.978C9.106 8.824 8.941 8.632 8.824 8.401C8.71 8.167 8.653 7.912 8.653 7.636C8.653 7.305 8.738 7.009 8.909 6.746C9.083 6.481 9.324 6.275 9.632 6.127C9.94 5.976 10.287 5.9 10.675 5.9C11.071 5.9 11.423 5.973 11.731 6.118C12.041 6.263 12.284 6.466 12.457 6.725C12.631 6.984 12.72 7.281 12.723 7.614H11.709C11.709 7.452 11.667 7.306 11.581 7.178C11.496 7.047 11.376 6.946 11.222 6.875C11.068 6.803 10.89 6.768 10.688 6.768C10.485 6.768 10.307 6.802 10.153 6.87C9.999 6.939 9.88 7.036 9.794 7.161C9.709 7.287 9.666 7.432 9.666 7.597C9.666 7.791 9.729 7.959 9.854 8.102C9.979 8.241 10.143 8.328 10.346 8.362L11.342 8.525C11.635 8.576 11.896 8.686 12.124 8.854C12.355 9.022 12.533 9.232 12.658 9.482C12.784 9.733 12.847 10 12.847 10.282C12.847 10.624 12.755 10.933 12.573 11.21C12.39 11.483 12.135 11.698 11.808 11.855C11.48 12.012 11.105 12.09 10.683 12.09Z" fill="white"/>',
  css: '<path d="M7.992 15L2.941 13.601L1.815 1H14.185L13.058 13.598L7.992 15Z" fill="currentColor"/><path d="M4.257 5.121L4.117 3.576H11.874L11.594 6.704L11.556 7.119L11.169 11.448L8 12.325L7.993 12.327L4.82 11.448L4.604 9.022H6.158L6.268 10.254L7.993 10.719L7.995 10.719L9.722 10.253L9.902 8.249L4.534 8.249L4.396 6.704H10.036L10.177 5.121H4.257Z" fill="white"/>',
  python: '<path fill-rule="evenodd" clip-rule="evenodd" d="M8 1C11 1 11 2 11 4L11 6.5C11 7.328 10.328 8 9.5 8H6.5C5.119 8 4 9.119 4 10.5V11C2 11 1 11 1 8C1 5 2 5 4 5L7.5 5C7.776 5 8 4.776 8 4.5C8 4.224 7.776 4 7.5 4H5C5 2 5 1 8 1ZM6.5 3C6.776 3 7 2.776 7 2.5C7 2.224 6.776 2 6.5 2C6.224 2 6 2.224 6 2.5C6 2.776 6.224 3 6.5 3Z" fill="var(--augit-python-blue)"/><path fill-rule="evenodd" clip-rule="evenodd" d="M12 5V6.5C12 7.881 10.881 9 9.5 9H6.5C5.672 9 5 9.672 5 10.5L5 12C4.999 14 5 15 8 15C11 15 11 14 11 12L8.5 12C8.224 12 8 11.776 8 11.5C8 11.224 8.224 11 8.5 11L12 11C14 11.001 15 11 15 8C15 5 14 5 12 5ZM9.5 14C9.776 14 10 13.776 10 13.5C10 13.224 9.776 13 9.5 13C9.224 13 9 13.224 9 13.5C9 13.776 9.224 14 9.5 14Z" fill="var(--augit-python-yellow)"/>',
  editorconfig: '<path fill-rule="evenodd" clip-rule="evenodd" d="M1.549 5.289C1.528 5.338 1.507 5.389 1.487 5.439C1.405 5.649 1.432 5.885 1.547 6.079L2.449 7.604C2.594 7.848 2.594 8.152 2.449 8.396L1.547 9.921C1.432 10.115 1.405 10.351 1.487 10.561C1.507 10.611 1.527 10.661 1.548 10.711M1.549 5.289C1.779 4.742 2.076 4.23 2.431 3.764C2.463 3.721 2.497 3.679 2.53 3.637C2.671 3.46 2.889 3.366 3.114 3.368L4.884 3.388C4.901 3.388 4.919 3.387 4.937 3.386C5.2 3.371 5.439 3.223 5.57 2.991L6.438 1.449C6.438 1.449 6.438 1.449 6.438 1.449C6.549 1.252 6.74 1.11 6.964 1.077C7.017 1.069 7.071 1.061 7.125 1.055C7.413 1.019 7.706 1 8.004 1C8.302 1 8.595 1.019 8.883 1.055C8.883 1.055 8.884 1.055 8.883 1.055C8.937 1.061 8.991 1.069 9.044 1.077C9.268 1.11 9.458 1.251 9.569 1.448C9.569 1.448 9.57 1.448 9.57 1.449L10.438 2.991C10.577 3.239 10.841 3.391 11.124 3.388L12.894 3.368C12.894 3.368 12.894 3.368 12.894 3.368C13.119 3.366 13.337 3.46 13.478 3.637C13.511 3.678 13.544 3.72 13.576 3.763C13.577 3.763 13.577 3.764 13.577 3.764C13.932 4.23 14.229 4.741 14.46 5.288C14.459 5.288 14.46 5.289 14.46 5.288C14.48 5.338 14.501 5.389 14.521 5.439C14.603 5.649 14.576 5.884 14.461 6.079C14.462 6.079 14.461 6.079 14.461 6.079L13.559 7.604C13.414 7.848 13.414 8.152 13.559 8.396L14.461 9.921C14.461 9.921 14.461 9.921 14.461 9.921C14.576 10.115 14.603 10.351 14.521 10.561C14.501 10.611 14.481 10.66 14.461 10.709C14.46 10.71 14.46 10.711 14.46 10.711C14.229 11.259 13.932 11.77 13.577 12.236C13.545 12.279 13.512 12.321 13.478 12.363C13.337 12.54 13.12 12.634 12.894 12.632L11.124 12.612C11.107 12.612 11.089 12.613 11.072 12.614C10.808 12.629 10.569 12.777 10.438 13.008L9.57 14.551C9.57 14.551 9.57 14.552 9.57 14.551C9.459 14.748 9.268 14.89 9.044 14.923C8.991 14.931 8.937 14.939 8.883 14.945C8.595 14.981 8.302 15 8.004 15C7.706 15 7.413 14.981 7.125 14.945C7.124 14.945 7.124 14.945 7.123 14.945C7.07 14.938 7.017 14.931 6.964 14.923C6.74 14.89 6.549 14.748 6.438 14.551L5.57 13.008C5.431 12.761 5.168 12.609 4.884 12.612L3.114 12.632C2.889 12.634 2.671 12.54 2.53 12.363C2.497 12.321 2.463 12.279 2.431 12.236C2.076 11.77 1.778 11.258 1.548 10.711M3.227 4.37C3.21 4.392 3.193 4.414 3.176 4.437C2.894 4.818 2.656 5.234 2.47 5.676L3.31 7.094C3.641 7.653 3.641 8.347 3.31 8.906L2.47 10.324C2.482 10.35 2.493 10.377 2.504 10.403C2.696 10.842 2.94 11.254 3.227 11.63L4.873 11.612C5.522 11.605 6.123 11.952 6.441 12.518L7.249 13.953C7.258 13.954 7.267 13.955 7.276 13.956C7.514 13.985 7.757 14 8.004 14C8.26 14 8.512 13.984 8.759 13.953L9.567 12.518C9.885 11.952 10.486 11.605 11.135 11.612L12.782 11.63C12.799 11.608 12.816 11.585 12.832 11.563C13.114 11.182 13.352 10.766 13.538 10.324L12.698 8.906C12.657 8.836 12.621 8.764 12.59 8.69C12.373 8.176 12.409 7.583 12.698 7.094L13.538 5.676C13.527 5.65 13.515 5.623 13.504 5.597C13.312 5.158 13.069 4.747 12.782 4.37L11.135 4.388C10.486 4.395 9.885 4.048 9.567 3.482L8.759 2.047C8.75 2.046 8.741 2.045 8.733 2.044C8.494 2.015 8.251 2 8.004 2C7.748 2 7.496 2.016 7.249 2.047L6.441 3.482C6.123 4.048 5.522 4.395 4.873 4.388L3.227 4.37ZM8.004 9.651C8.916 9.651 9.655 8.912 9.655 8C9.655 7.088 8.916 6.349 8.004 6.349C7.092 6.349 6.353 7.088 6.353 8C6.353 8.912 7.092 9.651 8.004 9.651ZM10.655 8C10.655 9.464 9.468 10.651 8.004 10.651C6.54 10.651 5.353 9.464 5.353 8C5.353 6.536 6.54 5.349 8.004 5.349C9.468 5.349 10.655 6.536 10.655 8Z" fill="currentColor"/>',
  gitignore: '<circle cx="8" cy="8" r="6.5" fill="var(--augit-ignore-paper)" stroke="currentColor"/><path d="M3.5 12.5 12.5 3.5" stroke-linecap="butt"/>',
};

// 分支树状态图形与日志引用色标分开，当前/收藏优先于普通分支或标签。
const branchTreeIconShapes = {
  current: '<path fill-rule="evenodd" clip-rule="evenodd" d="M7.707 2.293L1.295 8.705C0.904 9.096 0.904 9.729 1.295 10.119L5.881 14.705C6.271 15.095 6.904 15.095 7.295 14.705L13.707 8.293C13.895 8.105 14 7.851 14 7.586V3C14 2.448 13.552 2 13 2H8.414C8.149 2 7.895 2.105 7.707 2.293ZM2.002 9.412L8.414 3L13 3V7.586L6.588 13.998L2.002 9.412Z" fill="currentColor"/><circle cx="10" cy="6" r="1.5" stroke="currentColor"/>',
  currentFavorite: '<path fill-rule="evenodd" clip-rule="evenodd" d="M7.707 2.293L1.295 8.705C0.904 9.096 0.904 9.729 1.295 10.119L5.881 14.705C6.271 15.095 6.904 15.095 7.295 14.705L13.707 8.293C13.895 8.105 14 7.851 14 7.586V3C14 2.448 13.552 2 13 2H8.414C8.149 2 7.895 2.105 7.707 2.293ZM10 8C11.105 8 12 7.105 12 6C12 4.895 11.105 4 10 4C8.895 4 8 4.895 8 6C8 7.105 8.895 8 10 8Z" fill="currentColor"/>',
  tag: '<path fill-rule="evenodd" clip-rule="evenodd" d="M7.707 2.293L1.295 8.705C0.904 9.096 0.904 9.729 1.295 10.119L5.881 14.705C6.271 15.095 6.904 15.095 7.295 14.705L13.707 8.293C13.895 8.105 14 7.851 14 7.586V3C14 2.448 13.552 2 13 2H8.414C8.149 2 7.895 2.105 7.707 2.293ZM2.002 9.412L8.414 3L13 3V7.586L6.588 13.998L2.002 9.412Z" fill="currentColor"/><circle cx="10" cy="6" r="1.5" stroke="currentColor"/>',
  favorite: '<polygon points="7.998 11.522 3.673 14.311 4.989 9.336 1 6.084 6.138 5.798 7.998 1 9.858 5.798 14.996 6.084 11.007 9.336 12.323 14.311" fill="currentColor"/>',
};

function fileTypeIcon(name) {
  // 容忍空值：无文档或数据未到达时仍可能渲染标签，缺名称不应导致整页异常。
  const safeName = String(name ?? "");
  const extension = safeName.toLowerCase().split(".").pop();
  let kind = "file";
  let shape = '<path d="M2 1h8.5L14 4.5V15H2Z M10.5 1v3.5H14 M4 6h8 M4 9h8 M4 12h6"/>';
  const leafName = safeName.toLowerCase().split(/[\\/]/).pop();
  const sceneKind = leafName === '.gitignore' ? 'gitignore' : leafName === '.editorconfig' ? 'editorconfig'
    : ['js', 'mjs', 'cjs', 'jsx'].includes(extension) ? 'javascript'
      : ['ts', 'mts', 'cts', 'tsx'].includes(extension) ? 'typescript'
        : extension === 'css' ? 'css' : ['py', 'pyw', 'pyi'].includes(extension) ? 'python' : null;
  if (sceneKind) {
    kind = sceneKind;
    shape = `<g stroke="none" fill="currentColor">${sceneFileIconShapes[sceneKind]}</g>`;
    // 斜线圆为描边图形，不继承文件徽标的无描边组。
    if (sceneKind === 'gitignore') shape = sceneFileIconShapes.gitignore;
  } else if (extension === "txt") {
    // 权威 expui/fileTypes/text.svg：四条 1 单位实心横线，长/短交替（y=3/6/9/12，x2 起，长 12、短 8）。
    kind = "textfile";
    shape = '<g fill="currentColor" stroke="none"><rect x="2" y="3" width="12" height="1" rx="0.5"/><rect x="2" y="6" width="8" height="1" rx="0.5"/><rect x="2" y="9" width="12" height="1" rx="0.5"/><rect x="2" y="12" width="8" height="1" rx="0.5"/></g>';
  } else if (["slnx", "props", "gitattributes"].includes(extension)) {
    kind = "text";
    shape = '<path d="M2 4h11 M2 8h11 M2 12h11"/>';
  } else if (["md", "markdown"].includes(extension)) {
    kind = "markdown";
    shape = '<path fill="currentColor" stroke="none" fill-rule="evenodd" d="M0.5 4.70001h2.44558l1.70827 4.44462.10903.45692.09347-.45692 1.65634-4.44462h2.47154v7.26919H7.14096V7.59732l.03116-.4725-1.8277 4.84442H4.08269L2.31212 7.17155l.03115.42577v4.37188H0.5zM12.5929 9.9438V4.70001h1.2v5.24378l1.2834-1.28342.8485.84853-2.7319 2.732-2.7319-2.732.8486-.84853z"/>';
  } else if (["cs", "csproj"].includes(extension)) {
    kind = "csharp";
    shape = '<path d="M7.47 10.83A3.5 4 0 1 1 7.47 5.17 M11.5 4.5l-1 7 M14.5 4.5l-1 7 M9.5 6.5H15 M9 9.5h5.5"/>';
  } else if (["html", "htm"].includes(extension)) {
    kind = "html";
    shape = '<path d="m8 4-3 4 3 4m2-8 3 4-3 4"/>';
  } else if (extension === "xml") {
    kind = "xml";
    shape = '<path d="m8 4-3 4 3 4m2-8 3 4-3 4"/>';
  } else if (extension === "json") {
    kind = "json";
    shape = '<g fill="currentColor" stroke="none"><path d="M3.98755 9.77751C3.99755 9.39251 3.87005 9.08501 3.60505 8.85501C3.34005 8.62501 2.98255 8.51001 2.53255 8.51001H2V7.43001H2.53255C2.98255 7.43001 3.34005 7.31501 3.60505 7.08501C3.87005 6.85501 3.99755 6.54751 3.98755 6.16251L3.95755 4.58C3.94755 4.06 4.05505 3.605 4.28005 3.215C4.51005 2.825 4.83755 2.525 5.26255 2.315C5.68755 2.105 6.18755 2 6.76255 2H7.5V3.005H6.77005C6.25005 3.005 5.83755 3.15 5.53255 3.44C5.23255 3.725 5.08505 4.1125 5.09005 4.6025L5.12005 6.17001C5.13005 6.68001 4.99255 7.09751 4.70755 7.42251C4.42255 7.74251 4.03755 7.92001 3.55255 7.95501C4.03755 8.04501 4.42255 8.25501 4.70755 8.58501C4.99255 8.91001 5.13005 9.30501 5.12005 9.77001L5.09005 11.5475C5.08005 11.9875 5.21255 12.3375 5.48755 12.5975C5.76755 12.8625 6.14505 12.995 6.62005 12.995H7.5V14H6.61255C6.07755 14 5.60755 13.9 5.20255 13.7C4.80255 13.505 4.49255 13.2225 4.27255 12.8525C4.05755 12.4875 3.95255 12.06 3.95755 11.57L3.98755 9.77751Z"/><path d="M12.0145 9.77751C12.0045 9.39251 12.132 9.08501 12.397 8.85501C12.662 8.62501 13.0195 8.51001 13.4695 8.51001H14V7.43001H13.4695C13.0195 7.43001 12.662 7.31501 12.397 7.08501C12.132 6.85501 12.0045 6.54751 12.0145 6.16251L12.0445 4.58C12.0545 4.06 11.9445 3.605 11.7145 3.215C11.4895 2.825 11.1645 2.525 10.7395 2.315C10.3145 2.105 9.81455 2 9.23955 2H8.5V3.005H9.23205C9.75205 3.005 10.162 3.15 10.462 3.44C10.767 3.725 10.917 4.1125 10.912 4.6025L10.882 6.17001C10.872 6.68001 11.0095 7.09751 11.2945 7.42251C11.5795 7.74251 11.9645 7.92001 12.4495 7.95501C11.9645 8.04501 11.5795 8.25501 11.2945 8.58501C11.0095 8.91001 10.872 9.30501 10.882 9.77001L10.912 11.5475C10.922 11.9875 10.787 12.3375 10.507 12.5975C10.232 12.8625 9.85705 12.995 9.38205 12.995H8.5V14H9.38955C9.92455 14 10.392 13.9 10.792 13.7C11.197 13.505 11.507 13.2225 11.722 12.8525C11.942 12.4875 12.0495 12.06 12.0445 11.57L12.0145 9.77751Z"/></g>';
  } else if (["yaml", "yml"].includes(extension)) {
    kind = "yaml";
    // expui/fileTypes/yaml.svg：纸张、轮廓与 Y 分色，轮廓保留折角挖孔。
    shape = '<g stroke="none"><path d="M11 2C11.5523 2 12 2.44772 12 3V9H11C10.4477 9 10 9.44772 10 10V14H5C4.44772 14 4 13.5523 4 13V6H6C7.10457 6 8 5.10457 8 4V2H11Z" fill="var(--augit-yaml-paper)"/><path d="M7 4C7 4.06893 6.99277 4.13617 6.97949 4.20117C6.88625 4.65686 6.48325 5 6 5H4.41406L7 2.41406V4Z" fill="var(--augit-yaml-paper)"/><path fill-rule="evenodd" clip-rule="evenodd" d="M11 1C12.1046 1 13 1.89543 13 3V9H12V3C12 2.44772 11.5523 2 11 2H8V4C8 5.10457 7.10457 6 6 6H4V13C4 13.5523 4.44772 14 5 14H10V15H5C3.89543 15 3 14.1046 3 13V5.41406C3.00004 5.1489 3.10547 4.89453 3.29297 4.70703L6.70703 1.29297C6.89453 1.10547 7.1489 1.00004 7.41406 1H11ZM4.41406 5H6C6.55228 5 7 4.55228 7 4V2.41406L4.41406 5Z" fill="currentColor"/><path d="M12.4018 16V13.827L10.647 10.014H11.7212L12.656 12.0886C12.727 12.2526 12.7872 12.4139 12.8364 12.5724C12.8856 12.7309 12.9184 12.8539 12.9348 12.9414C12.9566 12.8539 12.9894 12.7309 13.0332 12.5724C13.0824 12.4139 13.1425 12.2526 13.2136 12.0886L14.1238 10.014H15.1898L13.435 13.827V16H12.4018Z" class="yaml-letter" fill="var(--augit-yaml-letter, var(--augit-icon))"/></g>';
  } else if (["png", "jpg", "jpeg", "bmp"].includes(extension)) {
    kind = "image";
    shape = '<rect x="2" y="2" width="12" height="12"/><circle cx="10.5" cy="5.5" r="1"/><path d="m2 11 4-4 8 7"/>';
  }
  return `<svg class="file-type-icon file-type-${kind}" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${shape}</svg>`;
}

function branchIsFavorite(item = {}, kind = 'branch') {
  if (typeof item.isFavorite === 'boolean') return item.isFavorite;
  // GitBranchManager 的默认收藏名；明确的状态可覆盖默认，不读取其它 IDE 的用户偏好。
  if (kind === 'tag') return false;
  return (kind === 'remote' || item.isRemote ? ['origin/main', 'origin/master'] : ['main', 'master']).includes(item.name);
}

function branchTreeIcon(item = {}, kind = 'branch') {
  const current = !!item.isCurrent, favorite = branchIsFavorite(item, kind);
  const state = current ? (favorite ? 'currentFavorite' : 'current') : favorite ? 'favorite' : kind === 'tag' ? 'tag' : 'branch';
  const color = state === 'favorite' ? 'var(--augit-favorite-icon)' : current ? 'var(--augit-current-branch-icon)' : 'var(--augit-icon)';
  const darkBranch = '<circle cx="4.5" cy="4" r="2"/><path d="M4.5 11H8.5a2 2 0 0 0 2-2V7.5" stroke-linecap="butt"/><path d="M4.5 6.5V14.5"/><circle cx="10.5" cy="6" r="2"/>';
  const drawing = state === 'branch' ? (document.body.dataset.theme === 'dark' ? darkBranch : toolbarIconShapes['git-history']) : branchTreeIconShapes[state];
  return `<svg class="git-branch-tree-icon" data-branch-icon="${state}" viewBox="0 0 16 16" fill="none" stroke="${state === 'branch' ? color : 'none'}" stroke-width="1" stroke-linecap="round" stroke-linejoin="round" style="color:${color}" aria-hidden="true">${drawing}</svg>`;
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
  "main-project": ["Project and Files", "Project tree, tabs, and read-only content"],
  "text-viewer": ["Text Viewer", "Line numbers, read-only text, find, and Go to Line"],
  "markdown-preview": ["Markdown Preview", "Raw, Preview, and Split View"],
  "json-preview": ["JSON Formatting", "Raw and read-only formatted output"],
  "image-preview": ["Image Preview", "Constrained PNG, JPEG, and BMP preview"],
  "file-limit": ["File Not Previewable", "Oversized, invalid UTF-8, GIF, WebP, and binary summaries"],
  "commit-changes": ["Commit Tool Window", "Changes, Unversioned Files, selection, and commit message"],
  "commit-diff": ["Workspace Diff", "Stable side-by-side preview after selecting a changed file"],
  "diff-status": ["Diff Summary", "Final explanations for binary, oversized, unchanged, and failed states"],
  "diff-boundary": ["Diff File Boundary", "Repeat the action in the same direction to open the adjacent file"],
  "git-history": ["Git Log", "Reference tree, commit graph, filters, files, and metadata"],
  "git-history-empty": ["Empty Git Log", "Empty repository state with the reference tree and filters"],
  "file-history": ["File History and Blame", "History and line attribution for the current path"],
  "git-compare": ["Reference Comparison", "Branches, tags, commits, and workspace comparison"],
  "history-diff-loading": ["History Diff Loading", "Local loading feedback and cancellation"],
  "history-diff-failure": ["History Diff Failed", "Local failure reason while preserving history context"],
  "history-diff-cancelled": ["History Diff Cancelled", "Preserve tabs and list selection for reopening"],
  "branches": ["Branches and Tags", "Search, create, checkout, push, compare, and dangerous actions"],
  "stash": ["Stash", "Create, inspect, apply, pop, and delete"],
  "reset": ["Reset", "Soft, Mixed, and Hard impact preview and confirmation"],
  "worktrees": ["Worktrees", "Create, open, and safely remove"],
  "remote": ["Remotes", "Add, edit, and delete remote names and URLs"],
  "clone": ["Clone Repository", "URL, destination, and progress states"],
  "push": ["Push", "Commit list, target reference, and error feedback"],
  "conflict-list": ["Conflict Session", "Conflicted files, Continue, Skip, and Abort"],
  "quick-open": ["Open File", "Keyboard-first overlay with up to 30 results"],
  "repository-search": ["Find in Files", "Query options, grouped results, limits, and cancellation"],
  "terminal": ["Terminal", "On-demand single-session terminal"],
  "settings": ["Settings", "Appearance, Git, terminal, and fonts"],
  "settings-save-failure": ["Settings Save Failed", "Keep user input while showing a local failure reason"],
  "git-unavailable": ["Git Unavailable", "Degraded state when Git is missing or too old"],
  "operation-result": ["Git Operation Result", "Running, success, failure, and cancellation states"],
  "workspace-open": ["Open Workspace", "Recent workspaces, directory selection, and fallback states"],
  "repository-init": ["Initialize Repository", "Explicit initialization confirmation for non-Git directories"],
  "blame": ["Blame", "Line attribution beside read-only content"],
  "smart-checkout": ["Smart Checkout", "覆盖风险、临时 Stash 和恢复结果"],
  "rollback": ["Rollback", "Rollback File and Discarded Diff"],
  "operation-progress": ["Git Operation in Progress", "进度、取消和防止重复触发"],
  "terminal-close": ["Close Running Terminal", "终止前台命令与子进程树确认"],
  "search-limited": ["Search Limit and Timeout", "1000 项上限、停止和已有结果保留"],
  "commit-empty": ["No Changes", "没有待提交更改时保留完整工具窗骨架"],
  "diff-loading": ["Diff Loading", "只更新临时 Diff 标签正文"],
  "push-no-remote": ["Push Without Remote", "Define remote 与禁用的 Push 动作"],
  "stash-manager": ["Stash", "已有 Stash 的查看、Apply、弹出和删除"],
  "project-context-menu": ["Project Context Menu", "只保留 Augit 支持的文件操作"],
  "changes-context-menu": ["Changes Context Menu", "Diff、Rollback、历史和定位操作"],
  "git-history-menu": ["Git Log Context Menu", "提交级操作与危险入口"],
  "quick-open-empty": ["Empty Open File", "弹层按内容高度增长"],
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

// 编辑器的 1.2 行距乘数作用于字体度量，而不是字号（EditorView.getLineHeight）。
// 随包JetBrains Mono的hhea度量为1020/-300/0，unitsPerEm=1000。
// JBR分别向上取整ascent/descent后乘1.2；浏览器先舍入包围盒会把13px正文压到20.57px。
// 其他字体继续使用浏览器度量；资源失败时不能套用未实际加载的字体常量。
function codeLineHeight(family, size, scale = window.devicePixelRatio || 1) {
  const firstFamily = String(family).split(',')[0].trim().replaceAll('"', '').replaceAll("'", '');
  const bundledLoaded = [...document.fonts].some(face => face.family.replaceAll('"', '') === 'JetBrains Mono' && face.style === 'normal' && face.weight === '400' && face.status === 'loaded');
  if (firstFamily === 'JetBrains Mono' && bundledLoaded) {
    // Java float乘法在18/37字号恰好越过整数边界，须保留单精度语义。
    return Math.ceil(Math.fround((Math.ceil(size * 1.02) + Math.ceil(size * .3)) * Math.fround(1.2)));
  }
  const context = document.createElement("canvas").getContext("2d");
  context.font = `${size}px ${family}`;
  const metrics = context.measureText("Ag");
  const height = metrics.fontBoundingBoxAscent + metrics.fontBoundingBoxDescent;
  return Math.ceil(Math.ceil(height * scale) * 1.2) / scale;
}

// 对齐JBR的EditorView ascent；仅移动墨迹，不改变行盒、背景、选区或滚动步长。
function codeBaselineShift(family, size, scale = window.devicePixelRatio || 1) {
  const firstFamily = String(family).split(',')[0].trim().replaceAll('"', '').replaceAll("'", '');
  const loaded = [...document.fonts].some(face => face.family.replaceAll('"', '') === 'JetBrains Mono' && face.style === 'normal' && face.weight === '400' && face.status === 'loaded');
  if (firstFamily !== 'JetBrains Mono' || !loaded) return 0;
  const height = codeLineHeight(family, size, scale);
  const ascent = Math.ceil(size * 1.02), descent = Math.ceil(size * .3);
  const referenceBaseline = height - descent - Math.floor((height - ascent - descent) / 2);
  // WebView2会在真实DPI下舍入字体leading，Canvas包围盒不足以推导实际行内基线。
  const row = document.createElement('div'), probe = document.createElement('i');
  row.style.cssText = 'position:fixed;left:0;top:0;visibility:hidden;pointer-events:none;white-space:pre';
  Object.assign(row.style, {fontFamily:family,fontSize:`${size}px`,fontWeight:'400',lineHeight:`${height}px`});
  probe.style.cssText = 'display:inline-block;width:0;height:0;vertical-align:baseline';
  row.append('Ag', probe); document.body.append(row);
  const browserBaseline = probe.getBoundingClientRect().top - row.getBoundingClientRect().top;
  row.remove();
  return referenceBaseline - browserBaseline;
}

// Windows参考Subpixel模式关闭分数度量：先取整设备字号，再取整0.6em字宽。
// 参考JBR的32字号×8比例独立测量覆盖此规则；仅实际加载的随包字体适用。
function codeLetterSpacing(family, size, scale = window.devicePixelRatio || 1) {
  const first = String(family).split(',')[0].trim().replaceAll('"', '').replaceAll("'", '');
  const loaded = [...document.fonts].some(face => face.family.replaceAll('"', '') === 'JetBrains Mono' && face.style === 'normal' && face.weight === '400' && face.status === 'loaded');
  if (first !== 'JetBrains Mono' || !loaded) return 0;
  const context = document.createElement('canvas').getContext('2d');
  context.font = `${size}px ${family}`;
  return Math.round(Math.round(size * scale) * .6) / scale - context.measureText('0').width;
}

function measureCodeViews() {
  const context = document.createElement("canvas").getContext("2d");
  const scale = window.devicePixelRatio || 1;
  document.querySelectorAll(".diff-columns").forEach(view => {
    const typography = getComputedStyle(view);
    const spacing = codeLetterSpacing(typography.fontFamily, parseFloat(typography.fontSize), scale);
    view.style.setProperty('--code-letter-spacing', `${spacing}px`);
    view.style.setProperty("--code-line-height", `${codeLineHeight(typography.fontFamily, parseFloat(typography.fontSize), scale)}px`);
    view.style.setProperty('--code-baseline-shift', `${codeBaselineShift(typography.fontFamily, parseFloat(typography.fontSize), scale)}px`);
    // 双栏正文/实时单栏保留行级背景，把文字放入独立内联墨迹层。
    for (const row of view.querySelectorAll('.diff-code-line:not(.unified-diff-line)')) {
      if (row.firstElementChild?.classList.contains('code-glyph')) continue;
      const glyph = document.createElement('span'); glyph.className = 'code-glyph';
      glyph.append(...row.childNodes); row.append(glyph);
    }
    const layout = view.closest(".diff-layout");
    if (!view.classList.contains("diff-unified-body")) {
      const style = getComputedStyle(view);
      context.font = `${style.fontSize} ${style.fontFamily}`;
      const columns = [...view.querySelectorAll(".diff-gutter > div")];
      const digits = Math.max(3, ...columns.flatMap(column => [...column.childNodes]
        .filter(node => node.nodeType === Node.TEXT_NODE).map(node => node.textContent.trim().length)));
      const sample = `${'9'.repeat(digits)}    ${'9'.repeat(digits)}`;
      const width = Math.max(84, Math.ceil((context.measureText(sample).width + sample.length * spacing) * scale) / scale + 14);
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
    const spacing = codeLetterSpacing(style.fontFamily, parseFloat(style.fontSize), scale);
    view.style.setProperty('--code-letter-spacing', `${spacing}px`);
    const gutter = Math.round((context.measureText("9".repeat(digits)).width + digits * spacing) * scale) / scale
      + Math.round(28 * scale) / scale;
    view.style.setProperty("--code-gutter-width", `${gutter}px`);
    view.style.setProperty("--code-line-height", `${codeLineHeight(style.fontFamily, parseFloat(style.fontSize), scale)}px`);
    view.style.setProperty('--code-baseline-shift', `${codeBaselineShift(style.fontFamily, parseFloat(style.fontSize), scale)}px`);
    view.closest(".blame-layout")?.style.setProperty("--code-line-height", view.style.getPropertyValue("--code-line-height"));
    const blame = view.closest(".blame-layout")?.querySelector(".blame-gutter");
    if (blame) {
      blame.style.fontSize = style.fontSize;
      blame.style.letterSpacing = `${spacing}px`;
      blame.style.setProperty('--code-letter-spacing', `${spacing}px`);
      const cells = [...blame.querySelectorAll(".blame-row")];
      const dateDigits = Math.max(9, ...cells.map(row => row.children[0].textContent.length));
      const numberDigits = Math.max(3, ...cells.map(row => row.children[2].textContent.length));
      const measure = text => Math.ceil((context.measureText(text).width + text.length * spacing) * scale) / scale;
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
  return `<div class="document-view json-document ${invalid ? "json-invalid" : ""}" data-json-mode="${invalid || state === "source" ? "source" : "formatted"}"><div class="document-toolbar"><span class="document-path">Augit › global.json　Read-only</span><div class="segmented document-modes"><button class="segment" aria-label="Raw" data-json-mode="source">${icon("document-source")}</button><button class="segment" aria-label="Formatted" data-json-mode="formatted" ${invalid ? 'disabled title="Invalid JSON. See the error location in Raw view"' : ""}>${icon("document-formatted")}</button></div><button class="icon-button" aria-label="More">${icon("ellipsis-vertical")}</button></div>${invalid ? '<button class="json-error" data-json-error-line="4" aria-label="Locate JSON Error">Invalid JSON: line 4, column 19. Click to locate.</button>' : ""}<div class="code-view" tabindex="0" aria-label="Read-only JSON"></div></div>`;
}

function bindJsonModes() {
  const view = document.querySelector(".json-document");
  if (!view) return;
  // 实时层会**就地**复用同一个 `.json-document`（规格 §7.4：外部更新复用原文与格式化正文），
  // 因此按节点做一次绑定守卫：`bindInteractions()` 每次区域刷新都会跑，没有守卫会在同一批按钮上
  // 叠加多个闭包（各自的"当前模式"还会互相打架）。
  if (view.dataset.jsonBound === "true") return;
  view.dataset.jsonBound = "true";
  const sampleSource = '{"sdk":{"version":"10.0.201","rollForward":"latestPatch","allowPrerelease":false}}\n';
  const sampleInvalidSource = '{\n  "sdk": {\n    "version": "10.0.201",\n    "rollForward":}\n}\n';
  const code = view.querySelector(".code-view");
  // 正文来源**每次现读**：实时层就地更新后会改写 `data-json-source`／`data-json-formatted`／
  // `data-json-error-line`，闭包捕获的旧值会让"原文"退回上一版本（第 292 轮实测踩到）。
  // 实时外壳把真实原文放在 `.code-view[data-json-source]` 上（`liveJsonDocument()` 渲染时写入）；
  // 视觉稿页面没有该属性时退回样例。只有实时文档的原文才可能是那份无效文本：视觉稿场景
  // `json-preview.html?json-state=invalid` 的 `source` 是回退后的**有效**样例（单行），
  // 用它渲染后根本没有第 4 行，"定位 JSON 错误"会退化成选中第一行（`verify-ux-json` 实测 `'1' !== '4'`）。
  const liveSource = () => (code && code.dataset ? code.dataset.jsonSource : null);
  const hasLiveSource = () => typeof liveSource() === "string" && liveSource().length > 0;
  const source = () => (hasLiveSource() ? liveSource() : sampleSource);
  // 宿主给出的"格式化后正文"（规格 §7.4）：实时文档直接用它，视觉稿页面没有该属性时才用
  // `JSON.stringify` 重新序列化样例。两条路径都必须两空格缩进并保持属性顺序。
  const formattedSource = () => {
    const value = code && code.dataset ? code.dataset.jsonFormatted : null;
    return typeof value === "string" && value.length > 0 ? value : null;
  };
  // 出错行来自渲染时写入的 `data-json-error-line`（宿主按 Unicode 标量计列的行号）；
  // 视觉稿样例没有该属性，退回样例里固定的第 4 行。
  const errorLine = () => Number(code && code.dataset ? code.dataset.jsonErrorLine : NaN) || 4;
  const positions = new Map();
  let current;
  const syncButtons = () => {
    view.querySelectorAll("button[data-json-mode]").forEach(button => {
      const active = button.dataset.jsonMode === current;
      button.classList.toggle("active", active);
      button.setAttribute("aria-pressed", String(active));
    });
  };
  const update = (mode, options = {}) => {
    if (mode === current || (mode === "formatted" && view.classList.contains("json-invalid"))) return;
    if (current) positions.set(current, [code.scrollLeft, code.scrollTop]);
    current = view.dataset.jsonMode = mode;
    syncButtons();
    const invalid = view.classList.contains("json-invalid");
    const text = invalid ? (hasLiveSource() ? source() : sampleInvalidSource)
      : mode === "source" ? source() : formattedSource() != null ? formattedSource() : JSON.stringify(JSON.parse(source()), null, 2);
    code.innerHTML = text.split("\n").map((line, index) => `<div class="code-line" data-line="${index + 1}"><span class="line-number">${index + 1}</span><span>${escapeHtml(line)}</span></div>`).join("");
    measureCodeViews();
    const [left, top] = positions.get(mode) || [0, 0];
    code.scrollTo(left, top);
    // `preserveMatch`：正文被**外部更新**时就地重算时，查找从原匹配位置继续（规格 §7.4：保留查找状态）；
    // 用户主动切换模式时按规格 §7.4 第八条"从新显示文本的起点继续"，不带这个意图。
    view.dispatchEvent(new CustomEvent('document-content-changed', {
      bubbles: true,
      detail: { preserveMatch: !!options.preserveMatch },
    }));
  };
  // 实时层就地更新正文后用它把闭包里"当前模式"与按钮状态对齐（不重排正文、不触发查找重算）。
  view.__augitJsonSync = () => { current = view.dataset.jsonMode; syncButtons(); };
  view.querySelectorAll("button[data-json-mode]").forEach(button => button.addEventListener("click", () => update(button.dataset.jsonMode)));
  // 错误条用**委托**挂在本视图上：实时层就地更新时会按最新行列重建错误条节点，
  // 挂在节点自己的监听会随着被替换掉而失效（第 292 轮实测：点击不再定位）。
  view.addEventListener("click", event => {
    const error = event.target && event.target.closest ? event.target.closest(".json-error") : null;
    if (!error || !view.contains(error)) return;
    const body = view.querySelector(":scope > .code-view") || code;
    const row = body.querySelector(`[data-line="${errorLine()}"]`) || body.querySelector(".code-line");
    if (!row) return;
    row.classList.add("active");
    row.scrollIntoView({ block: "nearest" });
    // 定位后焦点进正文（规格 §7.4）；错误条是原生 button，因此 Enter/Space 与单击同效。
    body.focus({ preventScroll: true });
  });
  update(view.dataset.jsonMode);
}

function diffFileHeader(source, target, title, path = "src/Augit.App/app.manifest", options = {}) {
  // 加载提示挂在**文件标题行**（规格 §6.5：有旧正文时在该 Diff 的文件标题行提示加载）。
  // 由 live.diffLoading 驱动：区域刷新会重建正文，注入的节点会被抹掉，
  // 只有随渲染一起产生才能在整个加载窗口内保持可见。
  // 文件历史预览有自己的加载态（工具条摘要 + 空栏），调用方传 `loading: false` 关掉这里的提示，
  // 否则编辑器正文在加载时会把"正在生成 diff…"画到预览的文件栏上（两份正文可以同时开着）。
  const showLoading = options.loading === undefined
    ? !!(window.__augitLive && window.__augitLive.diffLoading)
    : !!options.loading;
  const loading = showLoading
    ? `<span class="diff-loading-status diff-filebar-loading" role="status"><span class="loading-mark"></span>Generating diff…</span>`
    : "";
  const ending = value => {
    const label = ({"LF":"LF","CRLF":"CRLF","CR":"CR","混合换行":"Mixed line endings","Mixed line endings":"Mixed line endings","无换行":"No line endings","No line endings":"No line endings"})[value];
    const kind = ["LF", "CRLF", "CR"].includes(value) ? value.toLowerCase() : "other";
    return label ? `<span class="diff-line-ending" data-line-ending="${kind}">${label}</span>` : "";
  };
  return `<div class="diff-filebar reference-filebar" title="${escapeHtml(title)} · ${escapeHtml(path)}"><div class="reference-before"><span class="reference-lock">${icon("lock")}</span><span class="reference-source">${source}</span><span class="reference-path">${escapeHtml(path)}</span>${ending(options.oldLineEndings)}</div>${loading}<div class="reference-after"><span class="reference-lock">${icon("lock")}</span><span class="reference-target">${target}</span>${ending(options.newLineEndings)}</div></div>`;
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
    "tools/xterm/terminal/index.html": "<!doctype html>\n<title>Terminal</title>\n",
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
  if (original === "src/Augit.App/NativeGitPanel.cs") text += "\n阶段三：保持 Changes Choose。\n";
  if (["docs/architecture.md", "docs/performance-report.md", "docs/roadmap.md", "docs/runtime-dependencies.md", "global.json", "Augit.slnx"].includes(original))
    text += "\n阶段四：避免不必要的运行时更新。\n";
  if (original === "docs/product-spec.md") text += "\n阶段五：精确恢复安装前系统 PATH。\n";
  return text;
}

/**
 * Diff 的最终说明（规格 §6.5 + §10.2）。
 *
 * 二进制 / 超限 / 无文本差异 / 查询失败都必须给出三件事：
 * **发生了什么、哪些状态没有改变、可以做什么**；此前实时侧只把一句
 * `（Binary）` 塞在工具栏里，既不完整也和加载提示挤在同一行。
 * 复用设计系统里既有的 `.comparison-notice`（局部状态说明）组件，不新增视觉语言。
 * 文案保留"无法显示文本差异 / 没有文本差异"的既有措辞：harness 的 §6.5 断言依赖它。
 */
function diffStatusNotice(status) {
  const unavailable = Boolean(status) && status !== "Ready";
  const reason = unavailable
    ? `Text diff is unavailable for this file (${status}).`
    : "This file has no text differences.";
  const unchanged = "File content, changes, other tabs, and the current selection are unchanged.";
  const action = unavailable
    ? "Close this comparison or open the file in its default application."
    : "After changing the file, double-click the row to refresh.";
  const tone = status === "Failed" ? " failure" : "";
  return `<div tabindex="0" class="comparison-notice diff-status-notice${tone}" role="status">`
    + `<svg class="notice-mark" viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="7"/><path d="M8 5v4m0 2v.5"/></svg>`
    + `<div><strong>${escapeHtml(reason)}</strong>`
    + `<p class="commit-meta">${escapeHtml(unchanged)}</p>`
    + `<p class="commit-meta">${escapeHtml(action)}</p></div></div>`;
}

/**
 * 危险操作的「影响确认」正文（规格 §10.4）。
 *
 * 视觉稿与实时侧共用同一份结构，避免"基线画一套、实现写另一套"：
 * 对话框内的 `.info-block` 用标题（受影响对象）+ 若干影响条目；
 * 确认按钮由调用方给出，且必须是**动作名**（例如"删除 stash@{0}"、"移除 Worktree"），
 * 不能只写"确定"。
 */
function dangerConfirmBody(title, lines) {
  const items = (lines || [])
    .filter((line) => line !== null && line !== undefined && String(line).length > 0)
    .map((line) => `<p>${escapeHtml(String(line))}</p>`)
    .join("");
  return `<div class="info-block" style="width:auto;text-align:left"><h2>${escapeHtml(String(title))}</h2>${items}</div>`;
}

/**
 * 引用比较的"摘要"说明（规格 §7.9：摘要页清空变更导航，正文说明留在比较区域）。
 *
 * 与工作区 Diff 的最终说明分开措辞：工作区那句"改动文件后重新双击该行即可刷新"
 * 对"这个提交没有改这个文件"是误导 —— 用户要换的是提交或文件，不是去改文件。
 * 两者共用 `.comparison-notice` 组件，只是文案按比较语义切换。
 */
function comparisonSummaryNotice() {
  const reason = "These references have no text differences in this file.";
  const unchanged = "File content, changes, other tabs, and the current selection are unchanged.";
  const action = "Select another commit or file to compare, or close this tab.";
  return `<div tabindex="0" class="comparison-notice diff-status-notice" role="status">`
    + `<svg class="notice-mark" viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="7"/><path d="M8 5v4m0 2v.5"/></svg>`
    + `<div><strong>${escapeHtml(reason)}</strong>`
    + `<p class="commit-meta">${escapeHtml(unchanged)}</p>`
    + `<p class="commit-meta">${escapeHtml(action)}</p></div></div>`;
}

function diffView(comparison = false, state = "ready", workspaceComparison = false, fileHistory = false, workspacePath = "src/Augit.App/app.manifest", historyHash = null, finalStatus = null) {
  // 最终说明视觉稿（规格 §6.5/§10.2）：与 liveDiffView 的同一分支**同构**，
  // 否则像素对照比的就不是同一件事（实测漏写这个分支时，视觉稿侧仍在渲染样例 diff 行）。
  if (finalStatus) {
    const bar = diffFileHeader("HEAD", "Workspace", workspacePath, workspacePath);
    return `<div class="diff-layout"><div class="diff-toolbar"><button class="toolbar-button" aria-label="Previous Difference">${icon("arrow-up")}</button><button class="toolbar-button" aria-label="Next Difference">${icon("arrow-down")}</button><span class="grow"></span><div class="segmented"><button class="segment active" aria-label="Side-by-side">${icon("diff-side-by-side")}</button><button class="segment" aria-label="Unified">${icon("diff-unified")}</button></div></div>${bar}${diffStatusNotice(finalStatus)}</div>`;
  }
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
    const target = historyHash ? historyHash.slice(0, 8) : workspaceComparison ? "Workspace" : "dfe5c25a";
    const referenceTitle = workspaceComparison ? "HEAD → Working Tree" : `${hash}^ → ${hash}`;
    const disabled = state === "ready" ? "" : "disabled";
    let body = `<div class="diff-columns"><div class="diff-side">${renderLines(alignedOldLines, oldChanged, "removed")}</div><div class="diff-gutter"><div>${oldNumbers.map(number => `${number}<br>`).join("")}</div><div>${newLines.map((_, index) => `${index + 1}<br>`).join("")}</div></div><div class="diff-side">${renderLines(newLines, newChanged, "added")}</div></div>`;
    if (state === "loading") {
      body = `<div class="diff-columns diff-loading-columns"><div class="diff-loading-message" role="status">Loading...</div></div>`;
    } else if (state === "summary") {
      // 无文本差异：清空正文变更导航（按钮禁用但保留），正文只留比较语义的摘要说明。
      body = comparisonSummaryNotice();
    } else if (state !== "ready") {
      body = `<div class="comparison-notice ${state === "failure" ? "failure" : ""}"><svg class="notice-mark" viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="7"/><path d="M8 5v4m0 2v.5"/></svg><span>${state === "failure" ? "Cannot read the file at this commit: Git query failed." : "Comparison cancelled."}</span></div>`;
    }
    return `<div class="diff-layout"><div class="diff-toolbar comparison-toolbar"><button class="toolbar-button" ${disabled} aria-label="Previous Difference">${icon("arrow-up")}</button><button class="toolbar-button" ${disabled} aria-label="Next Difference">${icon("arrow-down")}</button><span class="toolbar-separator"></span><button class="toolbar-button" ${disabled} aria-label="Find">${icon("search")}</button><span class="grow"></span>${state === "loading" ? diffLoadingProgress() : state === "ready" ? '<span class="comparison-summary">1 difference</span>' : ""}<button class="toolbar-button" aria-label="Ignore whitespace">${icon("diff-ignore-whitespace")}</button><div class="segmented"><button class="segment active" aria-label="Side-by-side">${icon("diff-side-by-side")}</button><button class="segment" aria-label="Unified">${icon("diff-unified")}</button></div><button class="icon-button" aria-label="Settings">${icon("settings")}</button></div>${diffFileHeader(source, target, referenceTitle, fileHistory ? "docs/product-spec.md" : undefined)}${body}${state === "ready" ? unifiedTemplate : ""}</div>`;
  }
  return `<div class="diff-layout"><div class="diff-toolbar"><button class="toolbar-button" aria-label="Previous Difference">${icon("arrow-up")}</button><button class="toolbar-button" aria-label="Next Difference">${icon("arrow-down")}</button><span class="toolbar-separator"></span><button class="toolbar-button" aria-label="Find">${icon("search")}</button><button class="toolbar-button" aria-label="Previous File">${icon("arrow-left")}</button><span class="file-status-modified">1/42 files</span><button class="toolbar-button" aria-label="Next File">${icon("arrow-right")}</button><span class="grow"></span><span>1 difference, 0 included</span><button class="toolbar-button" aria-label="Ignore whitespace">${icon("diff-ignore-whitespace")}</button><div class="segmented"><button class="segment active" aria-label="Side-by-side">${icon("diff-side-by-side")}</button><button class="segment" aria-label="Unified">${icon("diff-unified")}</button></div><button class="icon-button" aria-label="Settings">${icon("settings")}</button></div>${diffFileHeader("HEAD", "Current Version", "HEAD → Current Version", workspacePath)}<div class="diff-columns"><div class="diff-side">${renderLines(alignedOldLines, oldChanged, "removed")}</div><div class="diff-gutter"><div>${oldNumbers.map(number => `${number}<br>`).join("")}</div><div>${newLines.map((_, index) => `${index + 1}<br>`).join("")}</div></div><div class="diff-side">${renderLines(newLines, newChanged, "added")}</div></div>${unifiedTemplate}</div>`;
}

function emptyChangesSide() {
  return `<aside class="tool-window side-tool"><div class="tool-header"><span>Commit</span><span class="grow"></span><button class="icon-button" aria-label="More">${icon("ellipsis-vertical")}</button><button class="icon-button" aria-label="Minimize">${icon("minus")}</button></div>
    <div class="changes-layout"><div class="toolbar"><button class="toolbar-button" aria-label="Refresh">${icon("refresh-cw")}</button><button class="toolbar-button" disabled aria-label="Rollback">${icon("undo-2")}</button><button class="toolbar-button" disabled aria-label="Show Diff">${icon("git-compare-arrows")}</button></div>
    ${statusRefreshNotice()}
    <div class="empty-tool-state"><div><strong>No changes to commit</strong><p>The working tree matches HEAD.</p></div></div>
    <div class="commit-box"><div class="commit-options"><span class="commit-amend" title="Amend this commit into the previous commit"><span class="fake-check"></span><span>Amend</span></span><a class="commit-last" href="git-history.html" title="View the last commit in Git Log">last commit</a><span class="commit-count" hidden></span></div>
    ${commitMessageBox(true)}
    <div class="commit-actions"><button class="primary-button" disabled>Commit</button><button class="secondary-button" disabled>Commit and Push…</button><a class="icon-button" href="settings.html" aria-label="Commit Settings">${icon("settings")}</a></div></div></div></aside>`;
}

/**
 * 改动列表的"正在读取"状态。
 *
 * Git 状态在首屏之后才到达（实测约 15 秒）。此前 `live` 已存在但 `live.status`
 * 为空时会退化到样例 `changesSide()`，于是**首屏显示视觉稿里的 42 个伪造文件**——
 * 用户看到工作区里并不存在的文件。这里改为如实说明正在读取，
 * 骨架与空状态保持一致（规格 §6.5 的加载反馈、§10.1 的稳定空状态）。
 */
function loadingChangesSide() {
  return `<aside class="tool-window side-tool"><div class="tool-header"><span>Commit</span><span class="grow"></span><button class="icon-button" aria-label="More">${icon("ellipsis-vertical")}</button><button class="icon-button" aria-label="Minimize">${icon("minus")}</button></div>
    <div class="changes-layout"><div class="toolbar"><button class="toolbar-button" aria-label="Refresh">${icon("refresh-cw")}</button><button class="toolbar-button" disabled aria-label="Rollback">${icon("undo-2")}</button><button class="toolbar-button" disabled aria-label="Show Diff">${icon("git-compare-arrows")}</button></div>
    <div class="empty-tool-state" role="status"><div><strong>Loading changes…</strong><p>Reading working tree status from Git.</p></div></div>
    <div class="commit-box"><div class="commit-options"><span class="commit-amend" title="Amend this commit into the previous commit"><span class="fake-check"></span><span>Amend</span></span><a class="commit-last" href="git-history.html" title="View the last commit in Git Log">last commit</a><span class="commit-count" hidden></span></div>
    ${commitMessageBox(true)}
    <div class="commit-actions"><button class="primary-button" disabled>Commit</button><button class="secondary-button" disabled>Commit and Push…</button><a class="icon-button" href="settings.html" aria-label="Commit Settings">${icon("settings")}</a></div></div></div></aside>`;
}

function quickOpenEmpty() {
  return `<div class="search-overlay"><div class="search-tabs"><strong>Open File</strong><span class="grow"></span><span class="menu-shortcut">Ctrl+P</span></div><div class="search-query"><input class="search-field" value="" placeholder="Type a file name" aria-label="Search"></div></div>`;
}

function projectContextMenu() {
  return `<section class="popover context-menu"><a class="menu-item" href="main-project.html">${icon("copy")} Copy Path</a><a class="menu-item" href="main-project.html">${icon("external-link")} Show in Explorer</a><a class="menu-item" href="terminal.html">${icon("square-terminal")} Open in External Terminal</a><div class="menu-separator"></div><a class="menu-item" href="main-project.html">${icon("refresh-cw")} Refresh</a><a class="menu-item" href="file-history.html">${icon("history")} File History</a><a class="menu-item" href="blame.html">${icon("list-tree")} Blame</a></section>`;
}

function changesContextMenu() {
  // 项的**相对顺序**取权威 `ChangesViewPopupMenu`（`platform/vcs-impl/resources/META-INF/VcsActions.xml:186-219`）：
  //   CheckinFiles → **ChangesView.Revert / .RevertFiles** → Move → **Diff.ShowDiff** → EditSource →
  //   CopyReferencePopupGroup → … → Refresh → **VersionControlsGroup**
  // 而 `VersionControlsGroup` 经 `VcsFileGroupPopup`（`VcsActions.xml:56-58`）收编各 VCS 的文件级动作，
  // git 侧是 `Git.FileActions`（`intellij.vcs.git.backend.xml:162-174`）：
  //   CheckinFiles → Git.Add → **Annotate（Blame）** → Compare.* → **Vcs.ShowTabbedFileHistory（文件历史）** → …
  // 因此权威的相对顺序是：**回滚在显示 Diff 之前**、**Blame 在文件历史之前**。
  // 原实现两处都反了（第 138 轮按权威改正）。
  // 注：`在资源管理器中定位` 在 `ChangesViewPopupMenu` 里**未定位到对应动作** ⇒ 保留现状（不删既有入口），
  // 记为待核，见 docs/nui-behavior/12-commit-changes.md §10。
  return `<section class="popover context-menu changes-context"><a class="menu-item" href="rollback.html">${icon("undo-2")} Rollback…</a><a class="menu-item" href="commit-diff.html">${icon("git-compare-arrows")} Show Diff</a><div class="menu-separator"></div><a class="menu-item" href="blame.html">${icon("list-tree")} Blame</a><a class="menu-item" href="file-history.html">${icon("history")} File History</a><div class="menu-separator"></div><a class="menu-item" href="commit-changes.html">${icon("copy")} Copy Path</a><a class="menu-item" href="commit-changes.html">${icon("external-link")} Show in Explorer</a></section>`;
}

/**
 * 日志提交行的右键菜单（权威 `Vcs.Log` 的提交弹出菜单同族：复制修订号／Cherry-pick／
 * 与工作区比较／Reset 到此处／Revert／新建分支/标签）。
 *
 * `live` 为真时改用 `data-log-action` 表达动作，并把**没有能力**的项标成禁用并写明原因
 * （规格 §7.8 第 460 行的同一口径："禁用时保留位置并显示原因"）；静态场景保持设计基线的链接不变。
 */
function gitLogContextMenu(live = false) {
  const staticHref = {
    "copy-hash": "git-history.html", "cherry-pick": "operation-progress.html",
    "compare-workspace": "git-compare.html", "reset-here": "reset.html", "revert": "operation-progress.html",
    "new-branch": "branches.html", "new-tag": "branches.html",
  };
  const entry = (action, iconName, label, reason) => {
    if (!live) return `<a class="menu-item" href="${staticHref[action]}">${icon(iconName)} ${label}</a>`;
    if (reason) {
      return `<span class="menu-item disabled" aria-disabled="true" title="${escapeHtml(reason)}">${icon(iconName)} ${label}</span>`;
    }
    return `<a class="menu-item" href="#" data-log-action="${action}">${icon(iconName)} ${label}</a>`;
  };
  return `<section class="popover context-menu log-context">`
    + entry("copy-hash", "copy", "Copy Commit Hash")
    + entry("cherry-pick", "cherry", "Cherry-pick", "Cherry-pick is unavailable.")
    + `<div class="menu-separator"></div>`
    + entry("compare-workspace", "none", "Compare with Workspace", "Commit-to-workspace comparison is unavailable.")
    + entry("reset-here", "undo-2", "Reset Current Branch Here…", "Reset is limited to the current HEAD.")
    + entry("revert", "none", "Revert Commit", "Revert is unavailable.")
    + `<div class="menu-separator"></div>`
    + entry("new-branch", "none", "New Branch…")
    + entry("new-tag", "none", "New Tag…")
    + `</section>`;
}

// 远端管理的 Name/URL 表格骨架：静态视觉稿与实时外壳共用同一份标记（规格 §7.11）。
// 权威 `GitConfigureRemotesDialog.kt:57-95`：两列表格（Name／URL）、`SINGLE_SELECTION`、
// `ToolbarDecorator` 只保留 Add/Remove/Edit（上移/下移禁用），双击行进入编辑。
// 产品额外的 Push URL 不在表格里，放在独立 Define Remote 窗口（差异登记见 ux-spec §7.11）。
function remoteManagerTable(rows, selectedIndex) {
  const hasSelection = Number.isInteger(selectedIndex) && selectedIndex >= 0 && selectedIndex < rows.length;
  const toolbar = `<div class="toolbar remote-toolbar">`
    + `<button class="toolbar-button" type="button" data-remote-action="add" aria-label="Add" title="Add">${icon("plus")}</button>`
    + `<button class="toolbar-button" type="button" data-remote-action="remove" aria-label="Remove" title="Remove"${hasSelection ? "" : " disabled"}>${icon("minus")}</button>`
    // ToolbarDecorator 的 Edit 使用 New UI 斜向铅笔；这里只绘制远端动作。
    + `<button class="toolbar-button" type="button" data-remote-action="edit" aria-label="Edit" title="Edit"${hasSelection ? "" : " disabled"}><svg class="augit-toolbar-icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1" aria-hidden="true" data-augit-icon="remote-edit"><path d="M11.6 7.65 13.69 5.56a1.04 1.04 0 0 0 0-1.47l-1.66-1.78a1.06 1.06 0 0 0-1.49 0L8.39 4.44M11.6 7.65 8.39 4.44M11.6 7.65 5.74 13.5H2.5v-3.18l5.89-5.88"/></svg></button>`
    + `</div>`;
  const head = `<div class="remote-table-head" role="presentation"><span>Name</span><span>URL</span></div>`;
  const body = rows.length === 0
    ? `<p class="remote-empty commit-meta"><span>Nothing to show</span></p>`
    : rows.map((remote, index) => {
      const selected = index === selectedIndex;
      return `<div class="remote-row${selected ? " selected" : ""}" role="option" aria-selected="${selected}" data-remote-index="${index}" data-remote-entry="${escapeHtml(remote.name)}">`
        + `<span class="remote-name" title="${escapeHtml(remote.name)}">${escapeHtml(remote.name)}</span>`
        + `<span class="remote-url" title="${escapeHtml(remote.url)}">${escapeHtml(remote.url)}</span></div>`;
    }).join("");
  return `<div class="history-page remote-page">${toolbar}<div class="remote-content"><div class="remote-table" role="listbox" aria-label="Remotes" tabindex="0">${head}${body}</div><p class="remote-notice commit-meta" role="status" hidden></p></div></div>`;
}

/** 静态 `remote` 场景的样例表格数据（真实形状：名称 + Fetch URL）。 */
function remoteSampleRows() {
  return [
    { name: "origin", url: "https://example.com/team/Augit.git" },
    { name: "backup", url: "https://example.com/team/Augit-backup.git" },
  ];
}

/**
 * Define Remote 次模态正文（权威 `GitDefineRemoteDialog.kt:69-85`）：
 * Name／URL 两行 + `Fetch remote` 复选框（参考默认勾选）；Push URL 是本产品额外一行。
 */
function remoteDefineBody(draft, { busy = false, error = "" } = {}) {
  const disabled = busy ? " disabled" : "";
  return `<div class="remote-define-form"><div class="form-grid">`
    + `<label for="remote-define-name">Name:</label>`
    + `<input id="remote-define-name" class="text-field" data-remote-field="name" autocomplete="off" spellcheck="false" value="${escapeHtml(draft.name)}"${disabled}>`
    + `<label for="remote-define-url">URL:</label>`
    + `<input id="remote-define-url" class="text-field" data-remote-field="fetchUrl" autocomplete="off" spellcheck="false" value="${escapeHtml(draft.fetchUrl)}"${disabled}>`
    + `<label for="remote-define-push">Push URL:</label>`
    + `<input id="remote-define-push" class="text-field" data-remote-field="pushUrl" autocomplete="off" spellcheck="false" value="${escapeHtml(draft.pushUrl || "")}"${disabled}>`
    + `</div><div class="check-line"><input type="checkbox" id="remote-define-fetch" data-remote-field="fetch"${draft.fetch === false ? "" : " checked"}${disabled}><label for="remote-define-fetch">Fetch remote</label></div>`
    + `<p class="remote-notice commit-meta" role="status"${error ? "" : " hidden"}>${escapeHtml(error)}</p></div>`;
}

function managementPage(kind) {
  if (kind === "remote") return remoteManagerTable(remoteSampleRows(), 0);
  const configs = {
    stash: ["Stash", ["stash@{0} 工作区切换前", "stash@{1} 调整安装器"], `<h2>stash@{0} · 工作区切换前</h2><p class="commit-meta">main · I49 · 2 分钟前</p><div class="button-row" style="justify-content:flex-start"><button class="primary-button" title="Apply and keep this Stash">Apply</button><button class="secondary-button" title="Apply and pop this Stash">Pop</button><button class="secondary-button">View Content</button><button class="danger-button">Delete</button></div><h3>Contains 3 files</h3><div class="tree-row"><span>${fileTypeIcon("NativeGitPanel.cs")}</span> NativeGitPanel.cs</div><div class="tree-row"><span>${fileTypeIcon("product-spec.md")}</span> product-spec.md</div>`],
    worktrees: ["Worktree", ["main · D:\\github\\Augit", "feature/ux · D:\\github\\Augit-ux"], `<h2>feature/ux</h2><div class="form-grid"><span>Path</span><span>D:\\github\\Augit-ux</span><span>Status</span><span class="file-status-new">Clean; safe to remove</span><span>Terminal session</span><span>No running embedded terminal</span></div><div class="button-row" style="justify-content:flex-start"><button class="primary-button">Open Window</button><button class="secondary-button">New Worktree</button><button class="danger-button">Remove…</button></div>`],
  };
  const [title, entries, detail] = configs[kind];
  const selectedIndex = kind === "worktrees" ? 1 : 0;
  const entryIcon = icon(kind === "worktrees" ? "folder-git-2" : "stash");
  return `<div class="history-page"><div class="toolbar"><button class="toolbar-button">${icon("plus")}</button><button class="toolbar-button">${icon("trash-2")}</button><button class="toolbar-button">${icon("refresh-cw")}</button><span class="toolbar-separator"></span><strong>${title} Manager</strong></div><div class="management-content"><div class="management-list">${entries.map((entry, index) => `<div class="tree-row ${index === selectedIndex ? "selected" : ""}">${entryIcon}<span class="tree-name">${entry}</span></div>`).join("")}</div><div class="management-detail">${detail}</div></div></div>`;
}

// 外壳注入真实数据时的管理页：结构、图标与样式与样例版一致。
function liveManagementPage(kind) {
  const live = window.__augitLive || {};
  // 远端管理（规格 §7.11）：Name/URL 表格 + 顶端 Add/Remove/Edit；表格骨架与静态稿共用。
  if (kind === "remote") {
    const remotes = (live.remotes && live.remotes.remotes) || [];
    const rows = remotes.map((remote) => ({ name: remote.name, url: remote.fetchUrl || remote.url || "" }));
    const index = Number.isInteger(live.selectedRemoteIndex) && live.selectedRemoteIndex >= 0 && live.selectedRemoteIndex < rows.length
      ? live.selectedRemoteIndex : null;
    return remoteManagerTable(rows, index);
  }
  const configs = {
    // Worktree 管理（视觉稿的 worktrees）：状态、终端会话与动作行。
    // 「移除…」只有干净且没有运行中的内置终端会话时才可用，禁用时把原因写在悬停说明里（§10.3）。
    worktrees: () => {
      const worktrees = (live.worktrees && live.worktrees.worktrees) || [];
      const entries = worktrees.map(worktree => `${worktree.branch || "(detached)"} · ${worktree.path}`);
      const index = Number.isInteger(live.selectedWorktreeIndex) ? live.selectedWorktreeIndex : 0;
      const current = worktrees[index] || null;
      const readiness = (live.worktreeRemoval && live.worktreeRemoval.path === (current && current.path))
        ? live.worktreeRemoval : null;
      const status = !current ? ""
        : current.isMain ? "Main worktree; cannot be removed"
          : current.isLocked ? "Locked"
            : current.isPrunable ? "Prunable"
              : readiness ? (readiness.isClean ? "Clean; safe to remove" : (readiness.reason || "Local changes or untracked files exist"))
                : "Checking…";
      const terminal = !readiness ? "Checking…"
        : readiness.hasActiveTerminal ? "Embedded terminal is running" : "No running embedded terminal";
      // 权威 `RemoveWorkingTreeAction.isEnabledFor()`：主工作树不可移除（`!it.isMain`），
      // 因此禁用按钮时优先说明"主工作树"，而不是等 Git 拒绝后报错。
      const canRemove = !!(readiness && readiness.canRemove) && !current.isMain;
      const removeTitle = canRemove ? ""
        : ` title="${escapeHtml(current.isMain ? "The main worktree cannot be removed." : ((readiness && readiness.reason) || "Checking whether removal is safe."))}"`;
      const detail = current
        ? `<h2>${escapeHtml(current.branch || "(detached)")}</h2>`
          + `<div class="form-grid"><span>Path</span><span>${escapeHtml(current.path)}</span>`
          + `<span>Status</span><span>${escapeHtml(status)}</span>`
          + `<span>Terminal session</span><span>${escapeHtml(terminal)}</span></div>`
          + '<div class="button-row" style="justify-content:flex-start">'
          + '<button class="primary-button" type="button" data-wtm-action="open">Open Window</button>'
          + '<button class="secondary-button" type="button" data-wtm-action="create">New Worktree</button>'
          + `<button class="danger-button" type="button" data-wtm-action="remove"${canRemove ? "" : ` disabled${removeTitle}`}>Remove…</button>`
          + "</div>"
          + '<p class="worktree-notice commit-meta" role="status" hidden></p>'
        : `<p class="commit-meta">No Worktrees</p>`;
      return ["Worktree", entries, detail];
    },
    // Stash 管理（视觉稿的 stash-manager）：详情带动作行与"包含 N 个文件"。
    // 选中项进状态（区域刷新会重建节点，把选中写死在 DOM 上会被丢掉）。
    stash: () => {
      const stashes = (live.stashes && live.stashes.stashes) || [];
      const entries = stashes.map(stash => `${stash.reference} ${stash.message}`);
      const index = Number.isInteger(live.selectedStashIndex) ? live.selectedStashIndex : 0;
      const current = stashes[index] || null;
      const files = (live.stashFiles && live.stashFiles.files) || [];
      const detail = current
        ? `<h2>${escapeHtml(current.reference)} · ${escapeHtml(current.message)}</h2>`
          + `<p class="commit-meta">${escapeHtml(current.branch)} · ${escapeHtml(current.date)}</p>`
          + '<div class="button-row" style="justify-content:flex-start">'
          + '<button class="primary-button" type="button" data-stash-action="apply" title="Apply and keep this Stash">Apply</button>'
          + '<button class="secondary-button" type="button" data-stash-action="pop" title="Apply and pop this Stash">Pop</button>'
          + `<button class="secondary-button" type="button" data-stash-action="view"${files.length > 0 ? "" : ' disabled title="This Stash has no files to view."'}>View Content</button>`
          + '<button class="danger-button" type="button" data-stash-action="drop">Delete</button>'
          + "</div>"
          + `<h3>Contains ${files.length} files</h3>`
          + (files.length > 0
            ? files.map(file => `<div class="tree-row" title="${escapeHtml(file.path)}"><span>${fileTypeIcon(file.path.split("/").at(-1))}</span> <span class="tree-name">${escapeHtml(file.path.split("/").at(-1))}</span></div>`).join("")
            : '<p class="commit-meta">No files.</p>')
          + '<p class="stash-notice commit-meta" role="status" hidden></p>'
        : `<p class="commit-meta">No Stashes</p>`;
      return ["Stash", entries, detail];
    },
  };
  const build = configs[kind];
  if (!build) return managementPage(kind);
  const [title, entries, detail] = build();
  const iconName = kind === "worktrees" ? "folder-git-2" : "stash";
  const selectedIndex = kind === "stash" && Number.isInteger(live.selectedStashIndex) ? live.selectedStashIndex
    : kind === "worktrees" && Number.isInteger(live.selectedWorktreeIndex) ? live.selectedWorktreeIndex : 0;
  const list = entries.length === 0
    ? `<p class="commit-meta">Empty</p>`
    : entries.map((entry, index) => {
      const hook = kind === "stash" ? ` data-stash-index="${index}"`
        : kind === "worktrees" ? ` data-worktree-index="${index}"` : "";
      return `<div class="tree-row ${index === selectedIndex ? "selected" : ""}"${hook}>${iconName ? icon(iconName) : ""}<span class="tree-name">${escapeHtml(entry)}</span></div>`;
    }).join("");
  return `<div class="history-page"><div class="toolbar"><button class="toolbar-button" data-mgmt-action="new" aria-label="New">${icon("plus")}</button><button class="toolbar-button" data-mgmt-action="delete" aria-label="Delete">${icon("trash-2")}</button><button class="toolbar-button" data-mgmt-action="refresh" aria-label="Refresh">${icon("refresh-cw")}</button><span class="toolbar-separator"></span><strong>${escapeHtml(title)} Manager</strong></div><div class="management-content"><div class="management-list">${list}</div><div class="management-detail">${detail}</div></div></div>`;
}

// 外壳注入真实设置时使用；结构与样例版一致，字段值来自设置文件。
function settingsBodyFallback() {
  return settingsLayoutHtml("appearance", null, null);
}

/**
 * 分类 → 该分类的字段（与 `settingsPageHtml` 里的 `data-setting` 一一对应）。
 * 用于"未保存修改"标记；写成常量而不是去 DOM 里反查，是因为标记要在
 * **不重绘右页**的前提下更新（重绘会打断用户正在输入的光标）。
 */
const SETTINGS_PAGE_KEYS = {
  "appearance": ["theme", "textFontFamily", "fontSize"],
  "file-view": ["monospaceFontFamily", "codeFontSize"],
  "git": ["gitExecutablePath"],
  "terminal": ["terminalShell", "terminalCustomCommand"],
};

// 参考外观只写入本次设置草稿；用户点击应用或确定之后才持久化。
const REFERENCE_APPEARANCE = Object.freeze({
  theme: "Light", textFontFamily: "Microsoft YaHei UI", fontSize: 12,
  monospaceFontFamily: "JetBrains Mono", codeFontSize: 13,
});

/**
 * 哪些分类有**未保存的修改**（规格 §7.17 的适配，依据 PyCharm 实测的实心圆点）。
 *
 * 判定口径是"草稿值与该分类的磁盘值不同"，而不是"碰过该分类的字段"：
 * 改回原值后标记应当消失，否则标记会骗人。数值字段按数字比较，
 * 避免 `13`（磁盘）与 `"13"`（输入框字符串）被判成不同。
 */
function settingsDirtyPages(live) {
  const settings = (live && live.settings) || null;
  const draft = (live && live.settingsDraft) || {};
  const dirty = new Set();
  if (!settings) return dirty;
  for (const page of Object.keys(SETTINGS_PAGE_KEYS)) {
    for (const key of SETTINGS_PAGE_KEYS[page]) {
      if (!Object.prototype.hasOwnProperty.call(draft, key)) continue;
      const next = draft[key];
      const current = settings[key];
      const numeric = typeof next === "number" || typeof current === "number";
      // 可选文本的"未设置"可能是 null/undefined，输入框读回是空字符串；两者语义相同，
      // 不能因此把 Terminal 页在用户未改任何值时标成脏（Apply 初始可用 + 蓝点）。
      const text = (value) => value == null ? "" : String(value);
      const same = numeric ? Number(next) === Number(current) : text(next) === text(current);
      if (!same) { dirty.add(page); break; }
    }
  }
  return dirty;
}

/**
 * 设置分类（规格 §7.17）：四个分类各自一个页面，导航点击真正切页。
 * 静态视觉稿与实时外壳**共用同一份结构**，区别只有两点：
 * 取值来源（样例 vs 设置文件）与是否带 `data-setting`（决定能否保存）。
 */
const SETTINGS_PAGES = [
  { id: "appearance", title: "Appearance", depth: 1 },
  { id: "file-view", title: "File View", depth: 1 },
  { id: "git", title: "Git", depth: 0 },
  { id: "terminal", title: "Terminal", depth: 0 },
];

function settingsNavHtml(page, live, dirty) {
  const dirtyPages = dirty || new Set();
  const row = (id, label, depth) => {
    const selected = page === id ? " selected" : "";
    const depthClass = depth > 0 ? ` depth-${depth}` : "";
    const attr = live ? ` data-settings-page="${id}"` : "";
    // 未保存修改标记：复用既有令牌 `--augit-blue`，只加一个点，不新增视觉语言。
    const mark = dirtyPages.has(id)
      ? `<span class="settings-dirty" role="status" aria-label="Unsaved changes" title="Unsaved changes"></span>`
      : "";
    return `<div class="tree-row${depthClass}${selected}"${attr}>${escapeHtml(label)}${mark}</div>`;
  };
  // 搜索框：搜索词与"无命中"态都记在 `live` 上（`live-data.js` 的搜索实现负责写），
  // 因此整块重绘（例如搜索驱动的切页）后这两者都能回填。静态视觉稿没有 live ⇒ 原样输出。
  const filterLive = live ? (window.__augitLive || {}) : {};
  const filterQuery = live ? String(filterLive.settingsFilter || "") : "";
  const noHits = live && filterLive.settingsFilterNoHits ? " no-hits" : "";
  const filter = live ? ` data-settings-filter="true" aria-label="Search settings" value="${escapeHtml(filterQuery)}"` : "";
  return `<nav class="settings-nav">`
    + `<input class="search-field${noHits}" placeholder="Search settings" style="width:100%;margin-bottom:10px"${filter}>`
    + `<div class="settings-nav-tree"><div class="tree-row settings-nav-group">Appearance & Behavior</div>`
    + SETTINGS_PAGES.slice(0, 2).map((item) => row(item.id, item.title, item.depth)).join("")
    + SETTINGS_PAGES.slice(2).map((item) => row(item.id, item.title, item.depth)).join("")
    + `</div></nav>`;
}

/** 单页正文。`live` 为真时带 `data-setting`（可保存），否则是纯展示的视觉稿。 */
function settingsPageHtml(page, values, live) {
  const attr = (key) => (live && key ? ` data-setting="${key}"` : "");
  const value = (key, fallback) => escapeHtml(String(values && values[key] !== undefined && values[key] !== null
    ? values[key] : fallback));
  const themeOption = (option, label) => `<option value="${option}"${values && values.theme === option ? " selected" : ""}>${label}</option>`;
  const shellOption = (option, label) => `<option value="${option}"${values && values.terminalShell === option ? " selected" : ""}>${label}</option>`;
  const fontSelect=(key,fallback,label)=>{
    const current=String(values?.[key]??fallback);
    const catalog=window.__augitLive?.fontFamilies||['JetBrains Mono','Cascadia Mono','Consolas','Courier New','Microsoft YaHei UI','Segoe UI','Tahoma'];
    const fonts=[...new Set([current,...catalog])].sort((a,b)=>a.localeCompare(b,'en',{sensitivity:'base'}));
    return `<select class="select-field font-family-select" aria-label="${label}"${attr(key)}>${fonts.map(name=>`<option value="${escapeHtml(name)}"${name===current?' selected':''}>${escapeHtml(name)}</option>`).join('')}</select>`;
  };

  if (page === "file-view") {
    return `<section class="settings-group"><div class="form-grid">`
      + `<label>Monospaced font</label><div class="font-setting" data-settings-search="Monospaced font JetBrains Mono Size 13"><span data-fixed-code-font>JetBrains Mono</span><label>Size</label><span data-fixed-code-size>13</span></div>`
      + `<label></label><span class="commit-meta">The monospaced font applies to read-only text, Diff, conflicts, and the terminal; Markdown code blocks follow it.</span>`
      + `<label></label><span class="commit-meta">Fonts only affect display; files are not modified.</span>`
      + `</div></section>`;
  }

  if (page === "git") {
    const detection = values && values.gitDetection;
    const state = !detection
      ? "Detecting git.exe…"
      : detection.available
        ? `Detected:${detection.path}${detection.version ? `（${detection.version}）` : ""}`
        : `${detection.reason || "Git not found"}`;
    const tone = detection && !detection.available ? " error" : "";
    return `<section class="settings-group"><div class="form-grid">`
      + `<label for="git-path">git.exe</label><input id="git-path" class="text-field"${attr("gitExecutablePath")} value="${value("gitExecutablePath", "")}" placeholder="Leave empty to search the system PATH">`
      + `<label></label><span class="commit-meta">Git is unavailable without a valid configuration; file browsing remains available.</span>`
      + `<label>Detection result</label><span class="settings-detection${tone}" data-git-detection>${escapeHtml(state)}</span>`
      + `<label>Minimum version</label><span class="commit-meta">Git for Windows ${value("gitMinimum", "2.40")} or newer; older versions disable Git.</span>`
      + `</div></section>`;
  }

  if (page === "terminal") {
    const custom = values && values.terminalShell === "Custom";
    return `<section class="settings-group"><div class="form-grid">`
      + `<label for="terminal-shell">Shell</label><select id="terminal-shell" class="select-field"${attr("terminalShell")}>`
      + shellOption("WindowsPowerShell", "Windows PowerShell") + shellOption("PowerShell7", "PowerShell 7")
      + shellOption("CommandPrompt", "CMD") + shellOption("GitBash", "Git Bash")
      + shellOption("Wsl", "WSL") + shellOption("Custom", "Custom command")
      + `</select><label></label><span class="commit-meta">Invalid configuration reports an error; the shell is not changed silently.</span>`
      + `<label for="terminal-command">Custom startup command</label><input id="terminal-command" class="text-field"${attr("terminalCustomCommand")} value="${value("terminalCustomCommand", "")}" placeholder="For example, wsl.exe -d Ubuntu"${live && !custom ? " disabled" : ""}>`
      + `<label></label><span class="commit-meta">Used when Custom command is selected; an empty command reports an error.</span>`
      + `</div></section>`;
  }

  return `<section class="settings-group"><div class="form-grid">`
    + `<label>Theme</label><select class="select-field"${attr("theme")}>${themeOption("System", "Sync with Windows")}${themeOption("Light", "Light")}${themeOption("Dark", "Dark")}</select>`
    + `<label></label><span class="commit-meta">The theme covers the main UI and previews; fonts only affect display.</span>`
      + `<label>UI font</label><div class="font-setting">${fontSelect('textFontFamily','Microsoft YaHei UI','UI font')}<label for="ui-font-size">Size</label><input id="ui-font-size" type="number" min="9" max="40" class="text-field"${attr("fontSize")} value="${value("fontSize", 12)}"></div>`
    + `<label></label><span class="commit-meta">The UI font applies to the tree, tabs, tool windows, menus, buttons, status bar, and Markdown body.</span>`
    + `<label></label><span class="commit-meta">Restore the last workspace and tabs at startup.</span>`
    + `</div><div class="button-row"><button type="button" class="secondary-button" data-restore-reference>Restore Reference Appearance</button></div></section>`;
}

/** 设置窗口的完整正文：左侧分类导航 + 右侧**只显示当前分类**的页面。 */
function settingsLayoutHtml(page, values, live, dirty) {
  const active = SETTINGS_PAGES.some((item) => item.id === page) ? page : "appearance";
  const title = (SETTINGS_PAGES.find((item) => item.id === active) || {}).title || "Appearance";
  return `<div class="settings-layout${live ? " live-settings" : ""}">`
    + settingsNavHtml(active, live, dirty)
    + `<div class="settings-page" data-settings-page-body="${active}">`
    + `<div class="settings-page-header"><nav class="settings-breadcrumbs" aria-label="Settings location">${['appearance','file-view'].includes(active)?'<span>Appearance &amp; Behavior</span><span class="breadcrumb-arrow">'+icon('chevron-right')+'</span>':''}<span>${escapeHtml(title)}</span></nav><div class="settings-navigation"><button type="button" class="icon-button" data-settings-nav="back" aria-label="Back"${!live||!(window.__augitLive?.settingsBack?.length)?' disabled':''}>${icon('arrow-left')}</button><button type="button" class="icon-button" data-settings-nav="forward" aria-label="Forward"${!live||!(window.__augitLive?.settingsForward?.length)?' disabled':''}>${icon('arrow-right')}</button></div></div>`
    + `<div class="settings-content">${settingsPageHtml(active, values, live)}</div>`
    + `</div></div>`;
}

function liveSettingsBody(page) {
  const settings = (window.__augitLive && window.__augitLive.settings) || null;
  if (!settings) return settingsBodyFallback();
  const live = window.__augitLive;
  // 值来源优先级：**未保存草稿** > 设置文件 > Git 检测结果（`git/detect`）。
  // 草稿必须参与渲染，否则切到别的分类再切回来会看到磁盘上的旧值——
  // 用户会以为刚改的内容丢了（规格 §7.17「切页保留未保存编辑」）。
  const values = Object.assign({}, settings, (live && live.settingsDraft) || {}, {
    gitDetection: live ? live.gitDetection || null : null,
    gitMinimum: live && live.gitDetection ? live.gitDetection.minimumVersion : "2.40",
  });
  return settingsLayoutHtml(page || (live && live.settingsPage) || "appearance", values, true, settingsDirtyPages(live));
}

// 外壳注入真实数据时的 Reset 对话框：目标提交取当前 HEAD，模式沿用规格说明。
/**
 * 冲突操作会话（规格 §7.13）：窗口显示操作类型、当前步骤、冲突文件与**实际可用动作**。
 * 只渲染宿主判定为可用的动作——无效动作直接不显示，而不是仅禁用（§10.3）。
 * Continue 若只是"前置条件未满足"（仍有冲突未解决）则保留并禁用，并说明还有多少冲突。
 */
function liveConflictSessionTitle(session) {
  const kind = session && session.kind && session.kind !== "None" ? session.kind : "Git";
  return `${kind} Conflicts`;
}

function liveConflictSessionBody(session) {
  if (!session || !session.inProgress) {
    return `<div class="empty-tool-state"><div><strong>No Git operation in progress</strong><p>No operation session needs attention in this workspace.</p></div></div>`;
  }

  const conflicts = session.conflicts || [];
  const unresolved = conflicts.length;
  const rows = conflicts.map(file => {
    const name = file.path.split("/").at(-1);
    const directory = file.path.split("/").slice(0, -1).join("/");
    return `<div class="check-row" data-conflict-path="${escapeHtml(file.path)}" role="treeitem" tabindex="-1" title="Open read-only diff"><span style="color:var(--augit-red)">${icon("conflict")}</span><span>${escapeHtml(name)}</span><span class="tree-path">${escapeHtml(directory)}</span></div>`;
  }).join("");
  const step = session.currentStep && session.totalSteps
    ? `<span class="commit-meta">Current step ${session.currentStep}/${session.totalSteps}</span>`
    : "";
  const summary = unresolved > 0 ? `${unresolved} conflicting file${unresolved === 1 ? "" : "s"}` : "No unresolved conflicts";
  return `<div class="toolbar"><strong>${summary}</strong><span class="grow"></span>${step}</div><div class="changes-list">${rows}</div>`;
}

function liveConflictSessionFooter(session) {
  if (!session || !session.inProgress) {
    return `<button type="button" class="secondary-button" data-operation-action="close">Close</button>`;
  }

  const kind = session.kind || "Git";
  const buttons = [];
  if (session.canAbort) {
    buttons.push(`<button type="button" class="secondary-button" data-operation-action="abort">Abort ${escapeHtml(kind)}</button>`);
  }

  if (session.canSkip) {
    buttons.push(`<button type="button" class="secondary-button" data-operation-action="skip">Skip</button>`);
  }

  const unresolved = (session.conflicts || []).length;
  if (session.canContinue) {
    buttons.push(`<button type="button" class="primary-button" data-operation-action="continue">Continue ${escapeHtml(kind)}</button>`);
  } else if (session.supportsContinue) {
    // 前置条件未满足：保留按钮但禁用，并在附近说明还差什么（规格 §7.13）。
    const reason = unresolved > 0
      ? `Remaining: ${unresolved} unresolved conflict${unresolved === 1 ? "" : "s"}`
      : "No staged content to commit";
    buttons.push(`<button type="button" class="primary-button" disabled title="${escapeHtml(reason)}。">Continue ${escapeHtml(kind)}</button>`);
    buttons.push(`<span class="commit-meta conflict-blocked">${escapeHtml(reason)}</span>`);
  }

  if (buttons.length === 0) {
    buttons.push(`<button type="button" class="secondary-button" data-operation-action="close">Close</button>`);
  }

  return buttons.join("");
}

function liveResetBody() {
  const live = window.__augitLive || {};
  const head = (live.history && live.history.head) ? live.history.head.slice(0, 7) : "HEAD";
  // 目标可编辑：与视觉稿样例一致（`Reset 目标提交` 是一个输入框），
  // 任何不存在的引用由宿主校验并给出原因，界面不自行判断。
  return `<div class="form-grid"><label for="reset-target">Target commit</label><input id="reset-target" class="text-field" value="${escapeHtml(head)}" aria-label="Reset target commit"><label for="reset-mode">Mode</label><select id="reset-mode" class="select-field"><option value="mixed" selected>Mixed · Reset index</option><option value="soft">Soft · Move HEAD only</option><option value="hard">Hard · Reset index and working tree</option></select></div><div class="inline-alert reset-impact danger"><strong></strong><p class="commit-meta"></p></div><div class="reset-notice" role="status" hidden></div>`;
}

// 外壳注入真实改动文件时的回滚对话框：目标是右键选中的那个文件
// （没有显式目标时退回视觉稿原有行为：第一个已跟踪改动）。
function liveRollbackBody() {
  const live = window.__augitLive || {};
  const files = (live.status && live.status.files) || [];
  const file = live.rollback && live.rollback.path
    ? files.find((item) => item.path === live.rollback.path) || null
    : files.find((item) => item.group === "Changes") || files[0] || null;
  if (!file) {
    return `<div class="inline-alert danger rollback-impact"><strong>No changes to roll back</strong><p class="commit-meta">No tracked file changes in this workspace.</p></div><div class="rollback-notice" role="status" hidden></div>`;
  }

  // 规格 §10.4：危险操作必须显示**具体影响**，且未跟踪文件删除必须明确说明进入
  // Windows 回收站。两种身份的后果不同，不能共用一句笼统说明：
  // 已跟踪文件是恢复到 HEAD，未跟踪/新增文件没有可恢复版本、只是被移出工作区。
  const kind = String(file.kind || "");
  const recycled = file.group !== "Changes" || kind === "Untracked" || kind === "Added" || file.workTreeStatus === "?";
  const impact = recycled
    ? `<strong>This file will be removed from the working tree</strong><p class="commit-meta">This file is untracked or new; no HEAD version can be restored.</p>`
    : `<strong>All local changes in this file will be discarded</strong><p class="commit-meta">Rollback affects the entire file, including all local changes.</p>`;
  return `<div class="inline-alert danger rollback-impact">${impact}<p class="rollback-recycle"${recycled ? "" : " hidden"}>Untracked or new files are moved to the Windows Recycle Bin.</p></div><div class="form-grid"><span>File</span><span>${escapeHtml(file.path)}</span><span>Changes</span><span class="file-status-${escapeHtml(String(file.kind).toLowerCase())}">${escapeHtml(file.kind)}</span></div><div class="rollback-notice" role="status" hidden></div>`;
}

/**
 * Diff 加载指示：参考 2026.2.1 的 `DiffRequestProcessor` 在头部放一个不定进度条
 * （`MyProgressBar extends JProgressBar`，`setIndeterminate(true)`），正文同时是
 * `LoadingDiffRequest` 的 "Loading..." 消息面板；不使用骨架屏，也不新造独立加载页。
 */
function diffLoadingProgress() {
  return '<span class="diff-progress" role="progressbar" aria-label="Generating diff"></span>';
}

/**
 * 实时 Diff 工具栏（规格 `ux-spec.md:439`）。
 *
 * 左侧依次为上一处、下一处、搜索、上一个文件、文件计数、下一个文件；右侧为差异摘要、
 * 忽略空白、双栏/单栏、设置。文件箭头围绕文件计数分组（`fileNav` 由调用方给出），
 * DOM 顺序即视觉顺序，键盘 Tab 顺序随之（验收套件按真实 Tab 核对）。
 *
 * 视觉稿里「搜索 / 忽略空白 / 设置」是静态控件；实时外壳里「忽略空白」有真实行为
 * （宿主 `git/diff` 收 `ignoreWhitespace`）、「设置」沿用应用既有约定打开设置对话框，
 * 而 **Diff 正文内的查找尚未实现**（共享查找条只服务 `.document-view > .code-view`）
 * ⇒ 按「未接线先禁用并写明原因」的既有做法渲染，不留死入口。
 */
function liveDiffToolbar({ fileNav = "", arrowsDisabled = false, summary = "", busy = false, ignoreWhitespace: ignoreOption, unified: unifiedOption } = {}) {
  const live = window.__augitLive || {};
  const disabled = arrowsDisabled || busy;
  const arrow = (label, name) => disabled
    ? `<button class="toolbar-button" disabled title="Generating diff; it will be available shortly." aria-label="${label}">${icon(name)}</button>`
    : `<button class="toolbar-button" aria-label="${label}">${icon(name)}</button>`;
  // 这两个选项默认取编辑器正文的会话状态；文件历史预览显式传入 —— 两边可以同时开着、
  // 各自处于不同的显示模式与忽略空白选项，不能互相污染。
  const ignoreWhitespace = ignoreOption === undefined ? !!(live.diffOptions && live.diffOptions.ignoreWhitespace) : !!ignoreOption;
  const unified = unifiedOption === undefined ? live.diffMode === "unified" : !!unifiedOption;
  const segment = (label, name, active) =>
    `<button class="segment${active ? " active" : ""}" aria-label="${label}">${icon(name)}</button>`;
  return `<div class="diff-toolbar">`
    + arrow("Previous Difference", "arrow-up") + arrow("Next Difference", "arrow-down")
    + '<span class="toolbar-separator"></span>'
    // 查找：Diff 正文查找按权威实现（第 308 轮）。加载态下正文是占位块 ⇒ 与差异箭头一样禁用并写明原因。
    + (disabled
      ? `<button class="toolbar-button" disabled title="Generating diff; it will be available shortly." aria-label="Find">${icon("search")}</button>`
      : `<button class="toolbar-button" aria-label="Find">${icon("search")}</button>`)
    + fileNav
    + '<span class="grow"></span>'
    + summary
    + `<button class="toolbar-button${ignoreWhitespace ? " active" : ""}" aria-pressed="${ignoreWhitespace ? "true" : "false"}" aria-label="Ignore whitespace">${icon("diff-ignore-whitespace")}</button>`
    + '<div class="segmented">'
    + segment("Side-by-side", "diff-side-by-side", !unified) + segment("Unified", "diff-unified", unified)
    + '</div>'
    + `<button class="icon-button" aria-label="Settings">${icon("settings")}</button>`
    + (busy ? '<button class="toolbar-button" aria-label="Cancel Comparison">Cancel Comparison</button>' : "")
    + '</div>';
}

// 外壳注入真实差异时使用：按「旧行 / 行号槽 / 新行」三列渲染结构化行。
// 冲突文件的只读提示（2026-10-17 用户批准移除内置三栏编辑器）。
// 只在当前 Diff 目标仍是未合并索引项时显示；Augit 不提供编辑、保存或整侧接受。
function liveConflictNotice(path) {
  const live = window.__augitLive || {};
  const file = ((live.status && live.status.files) || []).find((item) => item.path === path);
  if (!live.status || !live.status.hasConflicts || !file || file.kind !== "Unmerged") return "";
  return `<div class="inline-alert conflict-readonly-notice" role="status"><strong>Unresolved conflict</strong><p>This file still contains conflict markers. Resolve it with your external editor or terminal; Augit refreshes Git state automatically and does not provide a built-in merge editor.</p><button type="button" class="secondary-button" data-open-conflict-session>Show Conflicts</button></div>`;
}

// 宿主对超大补丁只回传前 2000 行；没有这句话时工具栏会把"前 2000 行无差异"
// 显示成 "0 differences"，而文件末尾的真实改动根本不在正文里（2026-10-19 8.7MB 实测）。
function liveDiffTruncationNotice() {
  const diff = window.__augitLive && window.__augitLive.diff;
  if (!diff || diff.truncated !== true) return "";
  return `<div class="inline-alert diff-truncated-notice" role="status"><strong>Diff truncated</strong><p>Showing the first 2000 rows. Changes in the rest of the file are not shown in this comparison.</p></div>`;
}

function liveDiffView() {
  const live = window.__augitLive || {};
  const diff = live.diff;
  if (!diff) {
    // 真实请求尚未返回时只显示目标与空正文，不能闪现视觉稿示例文件。
    const tab = (live.tabs || []).find(item => item.id === live.activeTabId);
    const path = tab?.path || new URLSearchParams(location.search).get("diff") || "";
    return `<div class="diff-layout">${liveDiffToolbar({ arrowsDisabled: true, busy: !!live.diffLoading })}${liveConflictNotice(path)}${diffFileHeader("HEAD", "Workspace", path, path)}<div class="diff-columns" tabindex="0" aria-label="Diff Content"><div class="diff-side"></div><div class="diff-gutter"></div><div class="diff-side"></div></div>`;
  }
  const rows = diff.rows || [];
  // 规格 `ux-spec.md:438/441/442`：工作区 Diff 工具条要含"上一个文件 / 文件计数 / 下一个文件"，
  // 且**跨文件查询期间差异/文件箭头禁用**。静态变体（`diffView(false)`）本来就有；
  // **live 变体此前完全没有**（第 114 轮 grep 零命中），导致 live 里跨文件导航根本不存在。
  const changedFiles = ((live.status && live.status.files) || []).map((file) => file.path);
  const currentFileIndex = changedFiles.indexOf(diff.path);
  // 规格 `ux-spec.md:438` 的文件导航属于**工作区 Diff**；`:489` 明确**比较视图的工具条没有**它
  // （上一处/下一处/查找/差异计数/忽略空白/双栏/单栏/设置）。第 115 轮重测暴露：不加这个门控时
  // 文件导航会**泄漏进比较视图**（commit-diff 1.59→1.80、diff-status 1.29→1.45）。
  const hasFileNav = live.workspaceDiff === true && currentFileIndex >= 0 && changedFiles.length > 1;
  const fileCountText = (currentFileIndex + 1) + '/' + changedFiles.length + ' files';
  // 文件计数是工具栏里唯一可让位的文字项：空间不足时省略显示，完整值留在 title
  // （摘要按 ux-spec §7.9 不允许被裁切）。
  const fileNavCount = '<span class="file-status-modified diff-file-count" title="' + fileCountText + '">' + fileCountText + '</span>';
  const fileNav = hasFileNav
    ? '<button class="toolbar-button" aria-label="Previous File">' + icon("arrow-left") + '</button>' + fileNavCount
      + '<button class="toolbar-button" aria-label="Next File">' + icon("arrow-right") + '</button>'
    : "";
  // 加载中优先显示加载提示：此时 rows 可能是上一轮的空值，
  // 直接走"没有文本差异"会把加载中的文件误报成无差异。
  if (live.diffLoading) {
    return `<div class="diff-layout" data-augit-loading="true">${liveDiffToolbar({
      // 规格 §7.7 第 9 条：「跨文件查询/排版期间禁用差异箭头，文件箭头和 Changes 仍允许改选」
      // ⇒ 文件箭头（`fileNav`）**不**禁用，只有上一处/下一处交给 `arrowsDisabled`。
      fileNav, arrowsDisabled: true, busy: true, summary: diffLoadingProgress(),
    })}${liveConflictNotice(diff.path)}${diffFileHeader("HEAD", "Workspace", diff.path, diff.path)}<div class="diff-columns diff-loading-columns" tabindex="0" aria-label="Diff Content"><div class="diff-loading-message" role="status">Loading...</div></div></div>`;
  }

  // 文件栏的双方引用（规格 §7.8/§7.9）：
  // 历史比较显示 `<hash>^ → <hash>`，引用比较显示该引用 → 工作区，其余按 HEAD → 工作区。
  const comparison = live.historyComparison;
  const historyActive = comparison && comparison.status !== "closed"
    && comparison.path === diff.path && comparison.commit;
  // 引用比较（与工作区比较）同样要在文件栏显示**双方引用**：来源是实际查询用的修订（命名引用保持原名），
  // 目标是工作区。此前这两种比较之外一律写 HEAD → 工作区，引用比较的文件栏因此与查询不符。
  const reference = live.referenceComparison;
  const referenceActive = !historyActive && reference && reference.path === diff.path && reference.revision;
  const barSource = historyActive ? `${String(comparison.commit).slice(0, 8)}^`
    : referenceActive ? String(reference.revision) : "HEAD";
  const barTarget = historyActive ? String(comparison.commit).slice(0, 8) : "Workspace";
  // 悬停说明保留**完整**引用与相对路径（规格 §7.9 第十七条）：显示值可以是前 8 位，查询与悬停用完整值。
  const hoverSource = historyActive ? `${comparison.commit}^` : referenceActive ? String(reference.revision) : "HEAD";
  const hoverTarget = historyActive ? String(comparison.commit) : "Workspace";
  const barFilebar = diffFileHeader(barSource, barTarget, `${hoverSource} → ${hoverTarget}`, diff.path, diff);
  if (rows.length === 0) {
    // 最终说明放到正文区（§6.5：持续可见；§10.2：三要素齐备），工具栏只留显示模式。
    // 历史/引用比较要按比较语义措辞（§7.9 摘要页）：工作区那句"改动文件后重新双击该行"
    // 对"这个提交没有改这个文件"是误导 —— 可换的是提交或文件。
    const emptyNotice = historyActive && (!diff.status || diff.status === "Ready")
      ? comparisonSummaryNotice()
      : diffStatusNotice(diff.status && diff.status !== "Ready" ? diff.status : "Ready");
    return `<div class="diff-layout">${liveDiffToolbar({ fileNav, arrowsDisabled: true })}${liveConflictNotice(diff.path)}${barFilebar}${emptyNotice}</div>`;
  }

  const body = diffBodyParts(diff);
  // 截断时不能沿用前 2000 行的差异计数；工具栏只显示 Truncated，具体边界由正文顶部说明。
  const summary = diff.truncated === true ? '<span class="diff-truncated-label">Truncated</span>' : body.summary;
  return `<div class="diff-layout">${liveDiffToolbar({ fileNav, summary })}${liveConflictNotice(diff.path)}${barFilebar}${liveDiffTruncationNotice()}${body.unifiedTemplate}${body.columns}</div>`;
}

/**
 * 由一份补丁构造差异**正文**与摘要（工具栏与文件栏由调用方各自拼装）。
 *
 * 提取出来的原因：文件历史右侧的只读比较视图（规格 §7.9 第六条「正文与引用比较同构」）
 * 必须与编辑器里的历史/引用比较使用**同一份**排版实现 —— 两份实现迟早会漂移
 *（行号、变更高亮、单栏模板、摘要块数都各有口径）。`liveDiffView()` 仍是编辑器正文的唯一入口。
 *
 * 输入形状与 `live.diff` 一致：`{ path, status, rows }`，行是宿主给的
 * `{ kind: Context|Added|Removed|Modified|HunkHeader|Metadata, oldLine, newLine, oldText, newText, oldChanges, newChanges }`。
 */
function diffBodyParts(diff) {
  const rows = (diff && diff.rows) || [];
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
    const lineNumber = side === "old" ? row.oldLine : row.newLine;
    // 缺失侧只是对齐占位，不能涂成实际变更行；有行号的空文本仍是实际空行。
    const kind = row.kind === "Context" || lineNumber == null ? "" : cssKind(row.kind);
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
  // 从文件开头开始的首个片段没有省略前文，补丁范围头不能占用正文行。
  // 非首个片段及从中段开始的片段仍保留分隔，避免把省略的上下文拼成连续文本。
  if (content[0]?.kind === "HunkHeader") {
    const range = /^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@/.exec(content[0].newText || content[0].oldText || "");
    if (range && Number(range[1]) <= 1 && Number(range[2]) <= 1) content.shift();
  }
  const isSeparator = (row) => row.kind === "HunkHeader";
  const cellOf = (row, side) => {
    if (isSeparator(row)) return { kind: "hunk", lineNumber: null, content: escapeHtml(row.newText || row.oldText || "") };
    return cell(row, side);
  };
  const oldCells = content.map(row => cellOf(row, "old"));
  const newCells = content.map(row => cellOf(row, "new"));
  // 空侧在变更块起点画双像素边界，连续占位不重复画线。
  const emptyBoundary = (index, side) => {
    const kind = side === "old" ? "Added" : "Removed";
    return content[index].kind === kind && (index === 0 || content[index - 1].kind !== kind)
      ? ` empty-${kind.toLowerCase()}` : "";
  };
  const oldSide = content.map((row, index) => `<div class="diff-code-line ${oldCells[index].kind}${emptyBoundary(index, "old")}" data-line="${oldCells[index].lineNumber ?? ""}">${oldCells[index].content}</div>`).join("");
  const newSide = content.map((row, index) => `<div class="diff-code-line ${newCells[index].kind}${emptyBoundary(index, "new")}" data-line="${newCells[index].lineNumber ?? ""}">${newCells[index].content}</div>`).join("");
  // ---- 中栏（行号槽 ＋ 变更连接区，规格 §7.7 第 11 条）----
  // 权威：`DiffLineMarkerRenderer.drawMarker()` 用**该变更块的类型色**（全强度 `DIFF_*.BACKGROUND`）
  // 填充行号槽，`DiffDividerDrawUtil.DividerPolygon` 用同色、**无边框**画连接两侧行范围的梯形。
  // 块类型由块内两侧是否都有行决定：只有新增行 ⇒ INSERTED、只有删除行 ⇒ DELETED、两侧都有 ⇒ MODIFIED
  //（一次替换在解析后可能是相邻的 Removed+Added 两行，参考图里这种块的**两个槽都是修改色**）。
  const isChangedKind = (row) => !!row && (row.kind === "Added" || row.kind === "Removed" || row.kind === "Modified");
  const changedBlocks = [];
  for (let index = 0; index < content.length; index += 1) {
    if (!isChangedKind(content[index])) continue;
    const last = changedBlocks[changedBlocks.length - 1];
    if (last && last.end === index - 1) last.end = index;
    else changedBlocks.push({ start: index, end: index });
  }
  const hasOldLine = (row) => row.oldLine !== null && row.oldLine !== undefined;
  const hasNewLine = (row) => row.newLine !== null && row.newLine !== undefined;
  const rowBlockKind = new Array(content.length).fill("");
  const blockShapes = changedBlocks.map((block) => {
    const oldRows = [];
    const newRows = [];
    for (let index = block.start; index <= block.end; index += 1) {
      if (hasOldLine(content[index])) oldRows.push(index - block.start);
      if (hasNewLine(content[index])) newRows.push(index - block.start);
    }
    const kind = oldRows.length && newRows.length ? "changed" : newRows.length ? "added" : "removed";
    for (let index = block.start; index <= block.end; index += 1) rowBlockKind[index] = kind;
    return { ...block, kind, oldRows, newRows, rows: block.end - block.start + 1 };
  });
  const slotClass = (row, index, side) => {
    if (!rowBlockKind[index]) return "";
    return (side === "old" ? hasOldLine(row) : hasNewLine(row)) ? rowBlockKind[index] : "";
  };
  // 逐行两格（旧侧／新侧）＋中间连接区列；行高由 `grid-auto-rows` 对齐正文（此前是每格 8px 内距的
  // 自动行 ⇒ 实时差异的行号与正文差了一倍多，见第 306 轮）。
  const gutter = content.map((row, index) => {
    const oldNumber = oldCells[index].lineNumber ?? "";
    const newNumber = newCells[index].lineNumber ?? "";
    return `<div class="diff-gutter-cell old ${slotClass(row, index, "old")}" data-line="${oldNumber}">${oldNumber}</div>`
      + '<div class="diff-gutter-gap"></div>'
      + `<div class="diff-gutter-cell new ${slotClass(row, index, "new")}" data-line="${newNumber}">${newNumber}</div>`;
  }).join("");
  // 连接区：每个变更块一个梯形，左边缘 = 旧侧行范围、右边缘 = 新侧行范围；某一侧没有行时那一侧
  // 收成**块高度的中点**尖角（权威 `startY1..endY1`／`startY2..endY2` 的一侧空范围）。
  // 百分比相对连接区自身高度 ⇒ 中点恒为 50%，与字号/DPI 无关。
  const percent = (value, total) => `${Math.round((value / Math.max(1, total)) * 10000) / 100}%`;
  const edge = (rows, total) => {
    if (!rows.length) return ["50%", "50%"];
    return [percent(rows[0], total), percent(rows[rows.length - 1] + 1, total)];
  };
  const connectors = blockShapes.map((block, index) => {
    const [leftTop, leftBottom] = edge(block.oldRows, block.rows);
    const [rightTop, rightBottom] = edge(block.newRows, block.rows);
    return `<div class="diff-gutter-connector ${block.kind}" data-block="${index}"`
      + ` style="top:${percent(block.start, content.length)};height:${percent(block.rows, content.length)};`
      + `clip-path:polygon(0 ${leftTop},100% ${rightTop},100% ${rightBottom},0 ${leftBottom})"></div>`;
  }).join("");
  // 单栏视图：同一份补丁的另一种排版，切换时不查询 Git（规格 §6.3）。
  const unified = content.map((row, index) => {
    const kind = row.kind === "HunkHeader" ? "hunk"
      : row.kind === "Added" ? "added"
        : row.kind === "Removed" ? "removed"
          : row.kind === "Modified" ? "changed" : "";
    const oldNumber = oldCells[index].lineNumber ?? "";
    const newNumber = newCells[index].lineNumber ?? "";
    const lineNumber = newNumber || oldNumber;
    const text = newCells[index].content !== "&nbsp;" ? newCells[index].content : oldCells[index].content;
    return `<div class="diff-code-line unified-diff-line ${kind}" data-line="${lineNumber}"><span class="unified-number">${oldNumber}</span><span class="unified-number">${newNumber}</span><span class="unified-marker"></span><span>${text}</span></div>`;
  }).join("");
  const unifiedTemplate = `<template class="diff-unified-template"><div class="diff-code-line hunk">${escapeHtml((diff && diff.path) || "")}</div>${unified}</template>`;
  return {
    // 正文是**可聚焦的正文位置**（规格 §7.9 第七/十九条：Tab 从工具条进入正文；加载/失败也保留一个正文位置，
    // 用户可据此从比较区回到标签栏）。编辑器正文与文件历史预览共用这一段 ⇒ 两处都有。
    columns: `<div class="diff-columns" tabindex="0" aria-label="Diff Content"><div class="diff-side">${oldSide}</div>`
      + `<div class="diff-gutter diff-gutter-live"><div class="diff-gutter-connectors" style="height:calc(${content.length} * var(--code-line-height, 22px))" aria-hidden="true">${connectors}</div>${gutter}</div>`
      + `<div class="diff-side">${newSide}</div></div>`,
    unifiedTemplate,
    summary: `<span class="diff-summary">${changedBlocks.length} ${changedBlocks.length === 1 ? "difference" : "differences"}</span>`,
    blockCount: changedBlocks.length,
  };
}

/**
 * 文件历史右侧的**只读比较视图**（规格 §7.9 第五条/第六条）。
 *
 * 数据来自 `live.fileHistoryPreview`（按"提交 + 路径"加载的补丁），排版与编辑器里的
 * 历史/引用比较共用 `diffBodyParts()` 与 `liveDiffToolbar()`：工具栏顺序、文件栏的
 * 父版本·提交版本·路径、行号与变更高亮因此逐项同构。文件栏与正文都**不**进入 Tab 顺序
 * （规格 §7.9 第十七条：文件栏不进入 Tab 顺序）。
 */
function liveFileHistoryPreviewView() {
  const live = window.__augitLive || {};
  const preview = live.fileHistoryPreview;
  const path = (preview && preview.path) || (live.fileHistory && live.fileHistory.path) || "";
  const short = (value) => String(value || "").slice(0, 8);
  // 文件栏：父版本（提交^）· 提交版本 · 相对路径 —— 与历史比较的编辑器正文同一口径。
  const filebar = diffFileHeader(`${short(preview && preview.commit)}^`, short(preview && preview.commit), path, path, { loading: false });
  // 工具条的显示模式与忽略空白取**预览自己的状态**（不是编辑器正文的）：两边可以同时开着。
  const options = {
    ignoreWhitespace: !!(preview && preview.ignoreWhitespace),
    unified: !!(preview && preview.mode === "unified"),
  };
  if (!preview || preview.loading) {
    return `<div class="diff-layout" data-augit-loading="true" data-live-file-history-preview>${liveDiffToolbar({
      ...options, arrowsDisabled: true, summary: diffLoadingProgress(),
    })}${filebar}<div class="diff-columns diff-loading-columns" tabindex="0" aria-label="Diff Content"><div class="diff-loading-message" role="status">Loading...</div></div></div>`;
  }
  const rows = (preview.diff && preview.diff.rows) || [];
  if (rows.length === 0) {
    const notice = diffStatusNotice(preview.diff && preview.diff.status && preview.diff.status !== "Ready" ? preview.diff.status : "Ready");
    return `<div class="diff-layout" data-live-file-history-preview>${liveDiffToolbar({ ...options, arrowsDisabled: true })}${filebar}${notice}</div>`;
  }
  const body = diffBodyParts(preview.diff);
  return `<div class="diff-layout" data-live-file-history-preview data-diff-total="${body.blockCount}">${liveDiffToolbar({ ...options, summary: body.summary })}${filebar}${body.unifiedTemplate}${body.columns}</div>`;
}

// 外壳注入真实设置时的 Clone 表单：目标目录用最近目录预填，浅克隆默认不勾选且深度禁用。
// 浅克隆行取权威 `GitShallowCloneComponentFactory`（`GitShallowCloneViewModel.kt:29-45`）：
//   复选 `clone.dialog.shallow.clone`（"Shallow clone with a history truncated to"）→ 数字框（1..Int.MAX_VALUE，默认 1，
//   `.enabledIf(shallowCloneCheckbox.selected)`）→ 标签 `clone.dialog.shallow.clone.depth`（"commits"）；
//   **数字框的 tooltip 是字面量 `--depth`**（同文件 `:41,48`）。
function liveCloneBody() {
  const settings = (window.__augitLive && window.__augitLive.settings) || {};
  const recent = (settings.recentWorkspaces || [])[0] || "";
  return `<div class="form-grid"><label for="clone-version">Version control</label><select id="clone-version" class="select-field"><option>Git</option></select><label for="clone-source">Repository URL</label><input id="clone-source" class="text-field" placeholder="https://example.com/team/repository.git" autofocus><label for="clone-destination">Directory</label><input id="clone-destination" class="text-field" placeholder="D:\\projects\\repository" value="${escapeHtml(recent)}"><span></span><div class="clone-shallow-row"><label class="check-line"><input id="clone-shallow" type="checkbox">Shallow clone, history truncated to</label><div class="clone-depth-group disabled"><input id="clone-depth" class="text-field" value="1" inputmode="numeric" aria-label="Shallow clone depth" title="--depth" disabled><span>commits</span></div></div></div><div class="clone-notice" role="status" hidden></div>`;
}

/* 第二轮共享骨架：只复用 PyCharm 的界面组织与交互语言，不复制品牌资产。 */
// 项目配色与参考一样保存索引、按序分配；存入 WebView2 的本地应用数据，不读取或修改 .idea。
// 首次以本次参考的蓝色开始；切换主题、路径大小写或分隔符不重新分配。
function projectColorIndex() {
  const live = window.__augitLive || {};
  const key = String(live.rootPath || "").replace(/\\/g, "/").replace(/\/+$/, "").toLowerCase();
  if (!key) return 4;
  try {
    const saved = JSON.parse(localStorage.getItem("augit.project.colors") || "null");
    const colors = Object.fromEntries(Object.entries(saved?.colors || {}).filter(([path, value]) =>
      path && Number.isInteger(value) && value >= 1 && value <= 9));
    if (Object.hasOwn(colors, key)) return colors[key];
    const last = Number.isInteger(saved?.last) && saved.last >= 1 && saved.last <= 9 ? saved.last : 3;
    const index = last % 9 + 1;
    colors[key] = index;
    localStorage.setItem("augit.project.colors", JSON.stringify({ colors, last: index }));
    return index;
  } catch {
    // 存储不可用时仍提供参考蓝色，不能让标题栏失败或改写其他用户设置。
    return 4;
  }
}

// 渐变中心 = 项目部件图标（工作区入口里的品牌方块）中心到标题栏左缘的距离。
function measureTitlebarGlow() {
  const bar = document.querySelector(".titlebar");
  if (!bar) return;
  const mark = bar.querySelector(".workspace-chip .brand-mark");
  if (!mark) return;
  const barRect = bar.getBoundingClientRect();
  const markRect = mark.getBoundingClientRect();
  if (!markRect.width) return;
  const center = markRect.left - barRect.left + markRect.width / 2;
  bar.style.setProperty("--augit-title-glow-center", `${Math.round(center)}px`);
  const current = bar.querySelector('.titlebar-context');
  const branch = bar.querySelector('.branch-chip');
  const actions = bar.querySelector('.window-actions');
  if (current && branch && actions) {
    const left = branch.getBoundingClientRect().right - barRect.left + 8;
    const right = actions.getBoundingClientRect().left - barRect.left - 8;
    const width = Math.max(0, Math.min(350, right - left));
    current.style.maxWidth = `${width}px`;
    const actual = current.getBoundingClientRect().width;
    current.style.left = `${Math.max(left + actual / 2, Math.min(barRect.width / 2, right - actual / 2))}px`;
  }
}

// 标签栏边缘渐隐：按实际滚动位置决定两端各留多少渐隐宽度（0 或 10px）。
// 权威值 ide.editor.tabs.fadeout.width = 10；语义是"标签被滚动到部分移出可见区时"
// 才在可见边缘渐隐——静止在两端时不渐隐，所以两端分别是 0。
function measureTabFade() {
  const fade = 10;
  document.querySelectorAll(".editor-tabs").forEach((strip) => {
    const maximum = strip.scrollWidth - strip.clientWidth;
    if (maximum <= 1) {
      strip.style.setProperty("--tab-fade-start", "0px");
      strip.style.setProperty("--tab-fade-end", "0px");
      return;
    }
    strip.style.setProperty("--tab-fade-start", strip.scrollLeft > 1 ? `${fade}px` : "0px");
    strip.style.setProperty("--tab-fade-end", strip.scrollLeft < maximum - 1 ? `${fade}px` : "0px");
  });
}

window.__augitMeasureTabFade = measureTabFade;
// 终端会话名宽度按实际字宽推导；实时层改标题文案（" · 正在启动…"）后要重新量一次。
window.__augitMeasureTerminalHeaders = measureTerminalHeaders;

function titlebar() {
  return `
    <header class="titlebar" data-project-color="${projectColorIndex()}">
      <div class="brand-mark" aria-label="Augit">A</div>
      <button class="top-button" aria-label="Main Menu" data-action="menu">${icon("menu")}</button>
      <a class="top-chip workspace-chip" href="workspace-open.html" title="Switch Workspace"><span class="brand-mark">A</span> ${escapeHtml((window.__augitLive && window.__augitLive.workspaceName) || "Augit")} ${icon("chevron-down")}</a>
      <a class="top-chip branch-chip" href="branches.html" title="Branches and Tags">${icon("git-branch")} ${escapeHtml((window.__augitLive && window.__augitLive.branch) || "main")} ${icon("chevron-down")}</a>
      <span></span>
      <a class="titlebar-context" href="quick-open.html" title="Open File"><span class="titlebar-caption">${escapeHtml((window.__augitLive && window.__augitLive.document && window.__augitLive.document.name) || "Current File")}</span> ${icon("chevron-down")}</a>
      <span></span>
      <nav class="window-actions" aria-label="Window Tools">
        <a class="top-button" href="quick-open.html" aria-label="Search">${icon("search")}</a>
        <a class="top-button" href="settings.html" aria-label="Settings">${icon("settings")}</a>
        ${windowActionIcons()}
      </nav>
    </header>`;
}

/**
 * 工具窗口轨。`gitUnavailableReason` 非空时，**提交**与 **Git 历史**入口按 ux-spec §7.18
 * 禁用并给出悬停原因（权威在缺 Git 时隐藏/禁用 VCS 入口，Augit 用显式降级页表达该状态）。
 * 项目树、搜索（ripgrep）与终端都不依赖 Git，保持可用。
 */
function rail(active, gitUnavailableReason = "", activeBottom = "", activeRail = "") {
  const disabled = typeof gitUnavailableReason === "string" && gitUnavailableReason.length > 0;
  const button = (railName, label, href, iconName) => {
    if (disabled && (railName === "commit" || railName === "history")) {
      return `<a class="rail-button" data-tool="${railName}" href="${href}" aria-label="${label}" aria-disabled="true" data-help-description="${escapeHtml(gitUnavailableReason)}">${icon(iconName, true)}</a>`;
    }
    const visible = railName === active
      || (railName === "terminal" && activeBottom === "terminal")
      || (railName === "history" && ["git", "file-history", "branch-compare"].includes(activeBottom));
    // 可见工具窗可以有两个，但活动归属还要覆盖已收起的工具窗；入口键盘焦点不改变活动归属。
    const isActive = railName === activeRail;
    return `<a class="rail-button ${visible ? "visible" : ""} ${isActive ? "active" : ""}" data-tool="${railName}" data-help-shortcut="${railName === 'search' ? 'Ctrl+Shift+F' : ''}" href="${href}" aria-label="${label}">${icon(iconName, true)}</a>`;
  };
  return `
    <nav class="tool-rail" aria-label="Tool Window">
      ${button("project", "Project", "main-project.html", "folder")}
      ${button("commit", "Commit", "commit-changes.html", "git-commit-horizontal")}
      ${button("search", "Search", "repository-search.html", "search")}
      <div class="rail-spacer"></div>
      ${button("terminal", "Terminal", "terminal.html", "square-terminal")}
      ${button("history", "Git Log", "git-history.html", "git-history")}
    </nav>`;
}

/**
 * 项目树一行的 HTML（实时树与"就地套用"共用同一份模板）。
 *
 * `live-data.js` 的 `patchProjectTree()` 需要单独造新行（只增删变化行、保留未变化行的节点），
 * 因此把行模板抽成函数，避免两处各写一份后漂移。
 */
/** 工具轨提示按 HelpTooltip 向右定位，快捷键只显示 Augit 已有绑定。 */
function bindRailHelpTooltip() {
  if (window.__augitRailHelpBound) return;
  window.__augitRailHelpBound = true;
  let owner=null, tip=null;
  const hide=()=>{owner?.removeAttribute('aria-describedby');tip?.remove();owner=null;tip=null;};
  const show=button=>{
    if(!button||button.closest('[inert]')||document.querySelector('[data-augit-overlay]'))return;
    if(owner===button&&tip?.isConnected)return;
    hide();owner=button;
    tip=document.createElement('div');tip.id='augit-rail-help';tip.className='rail-help-tooltip';tip.setAttribute('role','tooltip');
    const title=document.createElement('span');title.textContent=button.getAttribute('aria-label');tip.append(title);
    if(button.dataset.helpShortcut&&button.getAttribute('aria-disabled')!=='true') {
      const key=document.createElement('span');key.className='help-shortcut';key.textContent=button.dataset.helpShortcut;tip.append(key);
    }
    if(button.dataset.helpDescription){const text=document.createElement('div');text.className='help-description';text.textContent=button.dataset.helpDescription;tip.append(text);}
    document.querySelector('.augit-window')?.append(tip);button.setAttribute('aria-describedby',tip.id);
    const host=tip.parentElement.getBoundingClientRect(),rect=button.getBoundingClientRect(),box=tip.getBoundingClientRect();
    const anchor=button.closest('.tool-rail')?.dataset.anchor||'left';
    let x=anchor==='right'?rect.left-host.left-box.width-5:rect.right-host.left+5,y=rect.top-host.top+1;
    if(anchor==='top'||anchor==='bottom'){x=rect.left-host.left+1;y=anchor==='top'?rect.bottom-host.top+5:rect.top-host.top-box.height-5;}
    tip.dataset.alignment=anchor==='left'?'right':anchor==='right'?'left':anchor==='top'?'bottom':'top';
    tip.style.left=`${Math.max(4,Math.min(x,host.width-box.width-4))}px`;tip.style.top=`${Math.max(4,Math.min(y,host.height-box.height-4))}px`;
  };
  document.addEventListener('pointerover',event=>show(event.target.closest?.('.rail-button')));
  document.addEventListener('pointerout',event=>{if(owner&&event.target.closest?.('.rail-button')===owner&&!owner.contains(event.relatedTarget))hide();});
  document.addEventListener('focusin',event=>show(event.target.closest?.('.rail-button')));
  document.addEventListener('focusout',event=>{if(event.target===owner)hide();});
  for(const type of ['pointerdown','keydown','scroll'])document.addEventListener(type,hide,true);
  window.addEventListener('resize',hide);window.addEventListener('blur',hide);
  new MutationObserver(()=>{if(owner&&(!owner.isConnected||document.querySelector('[data-augit-overlay]')))hide();}).observe(document.getElementById('app'),{childList:true,subtree:true});
}

/** 项目树行模板由实时树及增量补丁共用，避免图形与ARIA状态漂移。 */
function liveTreeRowHtml(entry, selected, live) {
  const depth = entry.depth === 0 ? "root-row" : `depth-${entry.depth}`;
  const hasChildren = entry.isDirectory && entry.hasChildren;
  const chevron = hasChildren ? (entry.expanded ? "chevron-down" : "chevron-right") : "";
  const expanded = entry.expanded ? "true" : "false";
  const status = !entry.isDirectory ? treeGitStatus(entry.path, live) : null;
  const statusClass = status ? ` file-status-${status.className}` : "";
  const statusLabel = status ? `, Git status:${status.label}` : "";
  // 参考 Project Files 的根行直接显示工作区完整路径，不再拆成“项目名 + 路径后缀”。
  const label = entry.depth === 0 && live.root ? live.root : entry.name;
  // 选中判据以**路径**为准（重名文件可分布在多级目录，按名字会选错行）；只有实时外壳既没有
  // 用户点过的树行、也没有已打开文档时，`selected` 才会是视觉稿的默认**文件名**，故保留按名字
  // 回退，否则启动瞬间整棵树没有任何选中行（第 270 轮 §4.4 复跑实测：整行高亮断言因此拿不到节点）。
  const gitAttrs = status
    ? ` data-git-status="${status.className}" data-index-status="${escapeHtml(status.indexStatus)}" data-worktree-status="${escapeHtml(status.workTreeStatus)}"`
    : "";
  return `<div class="tree-row ${depth} ${entry.path === selected || entry.name === selected ? "selected" : ""}" style="--tree-depth:${entry.depth}" data-tree-path="${escapeHtml(entry.path)}" data-tree-directory="${entry.isDirectory}"${gitAttrs} role="treeitem" aria-label="${escapeHtml(label + statusLabel)}" aria-level="${entry.depth + 1}" aria-selected="${entry.path === selected || entry.name === selected ? "true" : "false"}" aria-expanded="${entry.isDirectory ? expanded : "false"}" tabindex="-1"><span class="chevron">${chevron ? icon(chevron) : ""}</span><span class="${entry.isDirectory ? "folder-icon" : "file-icon"}">${entry.isDirectory ? treeFolderIcon(entry.depth === 0) : fileTypeIcon(entry.name)}</span><span class="tree-name${statusClass}">${escapeHtml(label)}</span></div>`;
}

/** 项目树文件状态只影响文件名颜色与无障碍状态，不能覆盖文件类型图标的颜色。 */
function treeGitStatus(path, live) {
  const file = (live && live.status && Array.isArray(live.status.files) ? live.status.files : [])
    .find((item) => item && item.path === path);
  if (!file) return null;
  const kind = String(file.kind || "Modified");
  const status = {
    Added: { className: "added", label: "Added" },
    Untracked: { className: "unknown", label: "Unversioned" },
    Modified: { className: "modified", label: "Modified" },
    Deleted: { className: "deleted", label: "Delete" },
    Renamed: { className: "modified", label: "Rename" },
    Copied: { className: "added", label: "Copy" },
    TypeChanged: { className: "modified", label: "Type Changed" },
    Unmerged: { className: "conflict", label: "Conflicts" },
  }[kind];
  if (!status) return null;
  const composite = file.indexStatus === "D" && file.workTreeStatus === "?"
    ? "Staged deletion; an untracked file exists at the same path"
    : "";
  return {
    ...status,
    label: [status.label, composite, gitStatusDescription(file)].filter(Boolean).join("; "),
    indexStatus: typeof file.indexStatus === "string" ? file.indexStatus : " ",
    workTreeStatus: typeof file.workTreeStatus === "string" ? file.workTreeStatus : " ",
  };
}

// 外壳注入真实工作区数据时使用：结构与样例树一致，保证同一套 CSS 与交互绑定。
function liveProjectTree(selected, live) {
  const rows = live.tree.map((entry) => liveTreeRowHtml(entry, selected, live)).join("");
  return `
    <aside class="tool-window side-tool">
      <div class="tool-header"><span>Project</span><span>${icon("chevron-down")}</span><span class="grow"></span><span class="header-actions"><button class="icon-button" aria-label="Select Opened File">${icon("locate-fixed")}</button><button class="icon-button" aria-label="Collapse Project Tree">${icon("fold-vertical")}</button><button class="icon-button" aria-label="More">${icon("ellipsis-vertical")}</button><button class="icon-button" aria-label="Minimize">${icon("minus")}</button></span></div>
      <div class="side-content tree" role="tree" aria-label="Project Files">
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
      <div class="tool-header"><span>Project</span><span>${icon("chevron-down")}</span><span class="grow"></span><span class="header-actions"><button class="icon-button" aria-label="Select Opened File">${icon("locate-fixed")}</button><button class="icon-button" aria-label="Collapse Project Tree">${icon("fold-vertical")}</button><button class="icon-button" aria-label="More">${icon("ellipsis-vertical")}</button><button class="icon-button" aria-label="Minimize">${icon("minus")}</button></span></div>
      <div class="side-content tree">
        ${rows.map(([depth, chevron, fileIcon, name, path, href]) => {
          // 参考 Project Files 根行直接显示完整工作区路径，不再拼接项目名与路径后缀。
          const label = depth === "root-row" ? (path || name) : name;
          return `${href ? `<a href="${href}"` : `<div`} class="tree-row ${depth} ${name === selected ? "selected" : ""}" style="--tree-depth:${depth === "root-row" ? 0 : Number(depth.slice(6))}"><span class="chevron">${chevron ? icon(chevron) : ""}</span><span class="${fileIcon === "folder" ? "folder-icon" : "file-icon"}">${fileIcon === "folder" ? treeFolderIcon(depth === "root-row") : fileTypeIcon(name)}</span><span class="tree-name">${label}</span>${depth !== "root-row" && path ? `<span class="tree-path">${path}</span>` : ""}${href ? "</a>" : "</div>"}`;
        }).join("")}
      </div>
    </aside>`;
}

function commitMessageBox(disabled = false) {
  const state = disabled ? null : new URLSearchParams(location.search).get("commit-state");
  const error = state === "validation" ? "Commit message cannot be empty."
    : state === "hook-failure" ? "The commit-msg hook rejected the commit. Check repository rules." : "";
  return `<div class="commit-message-box"><div class="commit-feedback ${error ? "error" : ""}" role="status" title="${escapeHtml(error || "Commit message")}">${escapeHtml(error || "Commit message")}</div><textarea class="message-field" aria-label="Commit message" ${disabled ? "disabled" : ""}>${state === "hook-failure" ? "fix: 保留失败草稿" : ""}</textarea></div>`;
}

// 外壳注入真实 Git 状态时使用；结构与样例版一致，复用同一套样式与交互绑定。
/**
 * 规格 §9.2：Git 状态查询失败时必须显示**非阻塞**错误、保留上一次已知界面，
 * 并明确标记它不是最新状态。§10.2 要求说明发生了什么、哪些状态未改变、可以做什么。
 * 复用设计系统已有的 inline-alert，不自创视觉。
 */
function statusRefreshNotice() {
  const live = window.__augitLive || {};
  if (!live.statusError) return "";
  // 原因本身可能已经以句号结尾（宿主文案不定），避免出现「。。」。
  const reason = String(live.statusError);
  const sentence = /[。.！!？?]$/.test(reason) ? reason : reason + "。";
  return `<div class="inline-alert status-stale" role="status">Cannot read the latest Git status:${escapeHtml(sentence)}The list and branch information show the last successful result. Changes, selection, and the commit message are preserved. Click Refresh to retry.</div>`;
}

/**
 * 改动行的标记。抽成函数是**唯一来源**：整块渲染与增量更新都从这里取，
 * 否则两处标记迟早漂移（规格 §6.4 要求部分变化时复用未变化的行）。
 * `group` 是界面标签（Changes / Unversioned Files），不是宿主的 group 值。
 */
function changesFileCount(count) {
  return count === 0 ? "No files" : `${count} ${count === 1 ? "file" : "files"}`;
}

// 单仓库平铺列表与VcsPresentablePath的forceRelativePath一致：根目录显示仓库名。
function changeParentPresentation(file) {
  return file.directory ? file.directory.replaceAll("/", "\\") : (window.__augitLive?.workspaceName || "Augit");
}

function liveChangeFileRow(file, group, selectedName, fileIndex = 0) {
  const isSelected = selectedName === file.name;
  return `
      <div class="check-row change-file-row ${isSelected ? "selected" : ""}" data-file="${escapeHtml(file.name)}" data-path="${escapeHtml(file.path)}" data-group="${escapeHtml(group)}" data-index-status="${escapeHtml(file.indexStatus || " ")}" data-worktree-status="${escapeHtml(file.workTreeStatus || " ")}" role="treeitem" aria-level="2" aria-selected="${isSelected}" aria-label="${escapeHtml(changeAccessibleName(file.kind, file.name, file))}" id="change-file-${fileIndex}" data-change-path="${escapeHtml(file.path)}" tabindex="-1">
        <button class="fake-check${file.checked ? " checked" : ""}" type="button" role="checkbox" tabindex="-1" aria-checked="${file.checked}" aria-label="Choose ${escapeHtml(file.name)}"></button>
        <span class="file-icon">${fileTypeIcon(file.name)}</span><span class="tree-name file-status-${escapeHtml(String(file.kind).toLowerCase())}" title="${escapeHtml(gitStatusDescription(file))}">${escapeHtml(file.name)}</span><span class="tree-path">${escapeHtml(changeParentPresentation(file))}</span>
      </div>`;
}

/** 分组头**只含表头**：行由调用方拼在后面（这里多拼一次会让每行渲染两遍）。 */
// id 与视觉稿样例保持一致（change-group-N）：逐页对照按属性逐项比对，
// 缺一个 id 就是一处可见差异；顺序由调用方按分组顺序给出。
function liveChangeGroupRow(label, groupFiles, groupIndex = 0) {
  const state = groupFiles.every(file => file.checked) ? "true" : groupFiles.some(file => file.checked) ? "mixed" : "false";
  const checkClass = state === "true" ? " checked" : state === "mixed" ? " mixed" : "";
  return `<div class="check-row check-group-row" data-group="${escapeHtml(label)}" role="treeitem" aria-level="1" aria-expanded="true" aria-selected="false" id="change-group-${groupIndex}"><button class="change-chevron" type="button" tabindex="-1" aria-label="Collapse ${escapeHtml(label)}" aria-expanded="true">${icon("chevron-down")}</button><button class="fake-check${checkClass}" type="button" role="checkbox" tabindex="-1" aria-checked="${state}" aria-label="Select All ${escapeHtml(label)}"></button><strong>${escapeHtml(label)}</strong><span class="commit-meta">${changesFileCount(groupFiles.length)}</span></div>`;
}

function liveChangesSide(selected) {
  const status = window.__augitLive.status;
  const groups = [["Changes", "Changes"], ["UnversionedFiles", "Unversioned Files"]];
  // 无改动时复用样例版的空状态：文案与规格 §10.1 一致（「没有待提交的更改」），
  // 并保留提交工具窗口骨架（工具栏、提交框与禁用的动作）。
  if (status.files.length === 0) {
    return emptyChangesSide();
  }

  // 文件行的 id 连续编号（视觉稿样例用 change-file-<序号>）：分组之间不重新起算。
  let changeFileIndex = 0;
  const changeRows = groups.map(([key, label], groupIndex) => {
      const groupFiles = status.files.filter(file => file.group === key);
      if (groupFiles.length === 0) return "";
      return liveChangeGroupRow(label, groupFiles, groupIndex) + groupFiles.map(
        file => liveChangeFileRow(file, label, selected, changeFileIndex++)).join("");
    }).join("");
  const changed = status.files.filter(file => file.group === "Changes").length;
  return `
    <aside class="tool-window side-tool">
      <div class="tool-header"><span>Commit</span><span class="grow"></span><span class="header-actions"><button class="icon-button" aria-label="More">${icon("ellipsis-vertical")}</button><button class="icon-button" aria-label="Minimize">${icon("minus")}</button></span></div>
      <div class="changes-layout">
        <div class="toolbar"><button class="toolbar-button" aria-label="Refresh">${icon("refresh-cw")}</button><button class="toolbar-button" data-action="rollback-change" data-disabled-reason="Select a file in Changes first." aria-label="Rollback"${window.__augitLive && window.__augitLive.selectedChangePath ? "" : ' disabled title="Select a file in Changes first."'}>${icon("undo-2")}</button><button class="toolbar-button" data-action="show-change-diff" data-disabled-reason="Select a file in Changes first." aria-label="Show Diff"${window.__augitLive && window.__augitLive.selectedChangePath ? "" : ' disabled title="Select a file in Changes first."'}>${icon("git-compare-arrows")}</button><button class="toolbar-button" aria-label="Expand All">${icon("download")}</button><button class="toolbar-button" aria-label="Preview">${icon("eye")}</button></div>
        ${statusRefreshNotice()}
        <div class="changes-list" role="tree" aria-label="Files to Commit" tabindex="0">
          ${changeRows}
        </div>
        <div class="commit-box">
          <div class="commit-options"><span class="commit-amend" title="Amend this commit into the previous commit"><button class="fake-check" type="button" role="checkbox" aria-checked="false" aria-label="Amend"></button><span>Amend</span></span><a class="commit-last" href="git-history.html" title="View the last commit in Git Log">last commit</a><span class="commit-count commit-meta" title="${changed} files">${changed} files</span></div>
          ${commitMessageBox()}
          <div class="commit-actions"><a class="primary-button" href="operation-result.html">Commit</a><a class="secondary-button" href="push.html">Commit and Push…</a><span class="grow"></span><a class="icon-button" href="settings.html" aria-label="Commit Settings">${icon("settings")}</a></div>
        </div>
      </div>
    </aside>`;
}

function changesSide(selected = "app.manifest") {
  const groups = ["Changes", "Unversioned Files"];
  const changeRows = groups.map(group => {
    const groupFiles = changesSamples.filter(file => file.group === group);
    const rows = groupFiles.map(file => `
      <div class="check-row change-file-row ${selected === file.name ? "selected" : ""}" data-file="${escapeHtml(file.name)}" data-path="${escapeHtml(file.path)}" data-group="${group}" role="treeitem" aria-level="2" aria-selected="${selected === file.name}" id="change-file-${changesSamples.indexOf(file)}" tabindex="-1">
        <button class="fake-check${file.checked ? " checked" : ""}" type="button" role="checkbox" tabindex="-1" aria-checked="${file.checked}" aria-label="Choose ${escapeHtml(file.name)}"></button>
        <span class="file-icon">${fileTypeIcon(file.name)}</span><span class="tree-name file-status-modified">${escapeHtml(file.name)}</span><span class="tree-path">${escapeHtml(changeParentPresentation(file))}</span>
      </div>`).join("");
    return liveChangeGroupRow(group, groupFiles, groups.indexOf(group)) + rows;
  }).join("");
  return `
    <aside class="tool-window side-tool">
      <div class="tool-header"><span>Commit</span><span class="grow"></span><span class="header-actions"><button class="icon-button" aria-label="More">${icon("ellipsis-vertical")}</button><button class="icon-button" aria-label="Minimize">${icon("minus")}</button></span></div>
      <div class="changes-layout">
        <div class="toolbar"><button class="toolbar-button" aria-label="Refresh">${icon("refresh-cw")}</button><button class="toolbar-button" aria-label="Rollback">${icon("undo-2")}</button><button class="toolbar-button" data-action="show-change-diff" aria-label="Show Diff">${icon("git-compare-arrows")}</button><button class="toolbar-button" aria-label="Expand All">${icon("download")}</button><button class="toolbar-button" aria-label="Preview">${icon("eye")}</button></div>
        <div class="changes-list" role="tree" aria-label="Files to Commit" tabindex="0">
          ${changeRows}
        </div>
        <div class="commit-box">
          <div class="commit-options"><span class="commit-amend" title="Amend this commit into the previous commit"><button class="fake-check" type="button" role="checkbox" aria-checked="false" aria-label="Amend"></button><span>Amend</span></span><a class="commit-last" href="git-history.html" title="View the last commit in Git Log">last commit</a><span class="commit-count commit-meta" title="${changesSamples.filter(file => file.group === "Changes").length} files">${changesSamples.filter(file => file.group === "Changes").length} files</span></div>
          ${commitMessageBox()}
          <div class="commit-actions"><a class="primary-button" href="operation-result.html">Commit</a><a class="secondary-button" href="push.html">Commit and Push…</a><span class="grow"></span><a class="icon-button" href="settings.html" aria-label="Commit Settings">${icon("settings")}</a></div>
        </div>
      </div>
    </aside>`;
}

function editorTabs(active, extra = "") {
  const live = window.__augitLive;
  // 实时外壳：标签来自 live.tabs（规格 §5.2）。临时预览标签用 preview 类区分，
  // 关闭叉与中键都走同一套“不抢焦点”的关闭流程。
  // blame 文档（--blame）只写 live.blame、不建标签；此时 live.tabs 为空会让标签条回退成
  // 视觉稿样例标签（第 234/235 轮实测：样例的三个文件名与实际打开的文档不符）。
  // 这里用 blame 路径合成唯一的活动标签（模板 1：文件名 + 关闭叉），不改任何状态，
  // 也不影响编辑器视图选择（视图由 editor === "blame" 决定，见 blame 视图分支）。
  const liveTabs = live && Array.isArray(live.tabs) && live.tabs.length > 0
    ? live.tabs
    : (live && live.blame && live.blame.path
      ? [{ id: "__blame-document__", path: live.blame.path, title: live.blame.path, kind: "document", syntheticActive: true }]
      : null);
  if (live && liveTabs) {
    const markup = liveTabs.map(tab => {
      const cls = ["editor-tab"];
      if (tab.id === live.activeTabId || tab.syntheticActive === true) cls.push("active");
      if (tab.preview) cls.push("preview");
      if (tab.kind === "comparison") cls.push("comparison-tab");
      const label = tab.title || tab.path || "Untitled";
      const glyph = tab.kind === "comparison" ? icon("git-compare-arrows") : fileTypeIcon(label);
      // 比较标签按**三部分**渲染（文件名 / 来源 / 目标），每部分独立省略 —— 长文件名不会挤掉
      // 另外两部分（规格 §7.9 第十七条）；没有部件时退回整串文本。
      const caption = tab.kind === "comparison" && tab.comparison
        ? `<span class="comparison-caption"><span class="comparison-file">${escapeHtml(tab.comparison.file)}</span><span> · </span><span class="comparison-revision">${escapeHtml(tab.comparison.source)}</span><span> → </span><span class="comparison-revision">${escapeHtml(tab.comparison.target)}</span></span>`
        : `<span class="change-tab-caption">${escapeHtml(label)}</span>`;
      return `<a class="${cls.join(" ")}" href="#" data-tab-id="${escapeHtml(tab.id)}" title="${escapeHtml(label)}"${tab.id === live.activeTabId ? ' aria-current="true"' : ""}>${glyph} ${caption}<span class="tab-close" role="button" aria-label="Close File">${icon("close-small")}</span></a>`;
    }).join("");
    return `
    <div class="editor-tabs">
      ${markup}
      <span style="flex:1"></span><button class="icon-button" aria-label="Tab Options">${icon("ellipsis-vertical")}</button>
    </div>`;
  }

  if (live && live.document) {
    const name = live.document.name || live.document.path;
    return `
    <div class="editor-tabs">
      <a class="editor-tab active" href="#">${fileTypeIcon(name)} ${escapeHtml(name)}<span class="tab-close" aria-label="Close File">${icon("close-small")}</span></a>
      <span style="flex:1"></span><button class="icon-button" aria-label="Tab Options">${icon("ellipsis-vertical")}</button>
    </div>`;
  }

  // 实时外壳的空标签集合是真实空态；只有无宿主的视觉稿可以使用样例文件。
  if (live) return '<div class="editor-tabs"><span class="grow"></span></div>';

  return `
    <div class="editor-tabs">
      <a class="editor-tab ${active === "third" ? "active" : ""}" href="text-viewer.html">${fileTypeIcon("THIRD-PARTY-NOTICES.md")} THIRD-PARTY-NOTICES.md</a>
      <a class="editor-tab ${active === "roadmap" ? "active" : ""}" href="markdown-preview.html">${fileTypeIcon("roadmap.md")} roadmap.md</a>
      <a class="editor-tab ${active === "product" ? "active" : ""}" href="markdown-preview.html">${fileTypeIcon("product-spec.md")} product-spec.md</a>
      ${extra}<span style="flex:1"></span><button class="icon-button" aria-label="Tab Options">${icon("ellipsis-vertical")}</button>
    </div>`;
}

function markdownTabActions(mode) {
  return `<div class="segmented document-modes markdown-tab-actions" role="group" aria-label="Markdown view">${[['source','Raw','document-source'],['split','Split View','document-split'],['preview','Preview','document-preview']].map(([value,label,glyph]) => `<button type="button" class="segment${mode === value ? ' active' : ''}" data-markdown-mode="${value}" aria-label="${label}" aria-pressed="${mode === value}" title="${label}">${icon(glyph)}</button>`).join('')}</div>`;
}

function markdownView(mode = "preview") {
  if (liveDocument()) {
    // 规格 §7.3：会话内记住用户选过的模式。优先级：URL 参数（审计/视觉稿用）> 标签记住的 > 默认。
    const remembered = typeof window.__augitRememberedDocumentMode === 'function' ? window.__augitRememberedDocumentMode() : null;
    return liveMarkdownDocument(new URLSearchParams(location.search).get('markdown-mode') || remembered || mode);
  }
  const params = new URLSearchParams(location.search);
  mode = params.get('markdown-mode') || mode;
  if (!['source', 'split', 'preview'].includes(mode)) mode = 'preview';
  const sections = [
    ['1. 产品定位', 'Augit 是供外部 AI 协作开发场景使用的轻量桌面工具。它不接入任何 AI，也不负责生成代码。', '轻量优先是 Augit 的最高产品原则。'],
    ['2. 文件浏览', '在项目树中选择文件，双击或按 Enter 打开只读标签。', '普通文件查看器不提供编辑和保存。'],
    ['3. 查看变更', 'Changes 展示文件变化与完整文件提交选择。', 'Open Diff 后在比较区域阅读磁盘真实文本变化。'],
    ['4. 阅读 Markdown', '从文件树打开时默认预览，可以切换原文和左右对照。', '切换模式保留阅读位置，文件链接在 Augit 只读标签中打开。'],
    ['5. 轻量与响应', '外部文件和本地 Git 状态变化及时反映。', '后台空闲时不持续扫描仓库，不明显占用 CPU。'],
  ];
  const sourceLines = ['# Augit 产品规格', '', ...sections.flatMap(([title, ...paragraphs]) => [`## ${title}`, '', ...paragraphs.flatMap(p => [p, ''])]), '[返回产品定位](#section-0)', '', '[UX 规格](ux-spec.md)', '', '[不可用资源](missing.md)'];
  const source = `<div class="markdown-source code-view" tabindex="0" aria-label="Read-only Markdown source">${codeLines(sourceLines, 0)}</div>`;
  const preview = `<article class="markdown-preview" tabindex="0" aria-label="Markdown preview body"><h1>Augit 产品规格</h1>${sections.map(([title,...paragraphs],i) => `<h2 id="section-${i}">${title}</h2>${paragraphs.map(p => `<p>${p}</p>`).join('')}`).join('')}<p><a href="#section-0">返回产品定位</a></p><p><a href="markdown-preview.html">UX 规格</a></p><p><span class="markdown-blocked" title="missing.md">不可用资源（链接已阻止：路径不存在、越出工作区或协议不支持）</span></p></article>`;
  return `<div class="document-view markdown-document" data-markdown-mode="${mode}" data-markdown-state="${params.get('markdown-state') === 'failure' ? 'failure' : params.get('markdown-state') === 'loading' ? 'loading' : 'ready'}"><div class="markdown-panes">${source}<div class="markdown-divider" role="separator" aria-label="Adjust Markdown preview width" aria-orientation="vertical" tabindex="0"></div><div class="markdown-preview-region">${preview}<div class="markdown-feedback" role="status"></div></div></div></div>`;
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
  const positions = view.__augitMarkdownPositions || (view.__augitMarkdownPositions = new Map());
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
    document.querySelectorAll('.editor-tabs button[data-markdown-mode]').forEach(button => {
      const active = button.dataset.markdownMode === mode;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', String(active));
    });
    for (const [element, [x,y]] of positions) if (element.clientWidth) element.scrollTo(x,y);
    view.dispatchEvent(new Event('document-content-changed'));
    // 通知实时层把模式记到当前文档标签上（规格 §7.3 会话内记忆）；视觉稿页面没有监听方，纯空转。
    // 注意 `bubbles` 必须显式打开：CustomEvent 默认不冒泡，document 级监听收不到。
    view.dispatchEvent(new CustomEvent('document-mode-changed', { detail: { mode }, bubbles: true }));
  };
  // 标签区可独立重建，委托到 document 后仍只作用于当前 Markdown 正文。
  on(document, 'click', event => {
    const button = event.target.closest('.editor-tabs button[data-markdown-mode]');
    if (button && !button.disabled) setMode(button.dataset.markdownMode);
  });
  document.querySelectorAll('.editor-tabs button[data-markdown-mode]').forEach(button => {
    const active = button.dataset.markdownMode === view.dataset.markdownMode;
    button.classList.toggle('active', active);
    button.setAttribute('aria-pressed', String(active));
  });
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
  // 规格 §7.3：失败提示给出**当前原因**并可点击预览重试；原因由实时外壳写在
  // `data-markdown-reason` 上，没有它（静态视觉稿）时沿用基线文案。
  const failureReason = (view.dataset.markdownReason || '').trim();
  feedback.textContent = view.dataset.markdownState === 'loading' ? 'Generating Markdown preview…'
    : view.dataset.markdownState === 'failure'
      ? (failureReason ? `Preview failed:${failureReason} Click Preview to retry.` : 'Preview failed: the file could not be read.')
      : '';
  on(view.querySelector('.markdown-preview a[href^="#"]'), 'click', event => {
    event.preventDefault();
    preview.scrollTo(0, preview.querySelector('#section-0').offsetTop - preview.offsetTop - 24);
  });
  // 受控链接（规格 §7.3）：renderMarkdown 把链接渲染成带 data 属性的锚点，href 是占位 `#`。
  // 不接管的话，点外部链接会让 WebView 自己导航，点相对链接则会跳到页首——规格明确禁止
  // "跳到页首的空锚点"。这里先无条件 preventDefault，再按类型交给宿主处理；
  // 被阻止时在**原链接位置**替换成紧凑错误，保留原标签文字与原因，预览其余部分不受影响。
  on(preview, 'click', event => {
    const link = event.target && event.target.closest ? event.target.closest('a[data-external-link], a[data-document-link]') : null;
    if (!link) return;
    event.preventDefault();
    const external = link.hasAttribute('data-external-link');
    const target = link.getAttribute(external ? 'data-external-link' : 'data-document-link') || '';
    const blocked = reason => {
      const replacement = document.createElement('span');
      replacement.className = 'markdown-blocked';
      replacement.setAttribute('title', reason);
      replacement.textContent = `${link.textContent}(Link blocked:${reason}）`;
      link.replaceWith(replacement);
    };
    // 文档内锚点：留在预览区滚动，不算链接失败。
    if (!external && target.startsWith('#')) {
      const anchor = preview.querySelector(target);
      if (anchor) preview.scrollTo(0, anchor.offsetTop - preview.offsetTop - 24);
      else blocked('Anchor not found.');
      return;
    }

    if (typeof window.__augitOpenMarkdownLink !== 'function') return;
    void Promise.resolve(window.__augitOpenMarkdownLink(target)).then(result => {
      if (result && result.opened === false) blocked(result.reason || 'Cannot open this link.');
    }).catch(() => blocked('Cannot open this link.'));
  });
  // 已有比例时不再重算：`setRatio()` 按当前可用宽度四舍五入，把 0.7 写成 0.6927。
  // 重新绑定（外部更新、区域刷新）必须**逐值**保留用户的比例，因此只在没有比例时才初始化
  //（第 282 轮实测：就地更新 Markdown 后比例由 0.7 漂到 0.6927）。
  if (!panes.style.getPropertyValue('--markdown-source-ratio')) setRatio(ratio);
  setMode(view.dataset.markdownMode);
}

function measureCurrentFind() {
  document.querySelectorAll('.current-find').forEach(bar => {
    const status = bar.querySelector('.find-status');
    const canvas = document.createElement('canvas').getContext('2d');
    canvas.font = getComputedStyle(bar).font;
    // 参考StatusTextAction的宽度样本，普通计数变化不移动导航按钮。
    const width = Math.ceil(Math.max(canvas.measureText('9888 results').width, canvas.measureText(status.textContent).width)) + 4;
    bar.style.setProperty('--find-status-width', `${width}px`);
    // 最小窗口的大字号无法同时容纳六个缩放动作，保持单行并允许局部横向滚动。
    // 字段与图形不缩小；Tab聚焦浏览器自动滚入视口。滚动条占高只归属当前正文。
    const owner = bar.closest('.document-view, .diff-layout');
    if (owner) {
      owner.style.removeProperty('--augit-find-height');
      const base = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--augit-find-height')) || 42;
      if (bar.scrollWidth > bar.clientWidth) owner.style.setProperty('--augit-find-height', `${base + 10}px`);
    }
  });
}
window.addEventListener('resize', measureCurrentFind);
// 浏览器默认focus滚动可能留下数像素裁切；只滚动所属工具条，完整露出当前命中区。
document.addEventListener('focusin', event => {
  const bar = event.target.closest('.current-find, .diff-toolbar');
  if (!bar || bar.scrollWidth <= bar.clientWidth) return;
  const control = event.target.getBoundingClientRect(), bounds = bar.getBoundingClientRect();
  if (control.left < bounds.left + 4) bar.scrollLeft -= bounds.left + 4 - control.left;
  else if (control.right > bounds.right - 4) bar.scrollLeft += control.right - bounds.right + 4;
});

// 外壳打开真实文档时使用的视图构建器；没有真实文档时继续用视觉稿样例内容。
function liveDocument() {
  const live = window.__augitLive;
  return live && live.document ? live.document : null;
}

function liveLineViews(text) {
  const lines = String(text ?? "").replace(/\r\n?/g, "\n").split("\n");
  // 每行带 data-line：跳转行、错误定位这类"按行号找节点"的动作都靠它
  // （JSON 正文与差异正文一直是这么标的，实时纯文本/Markdown 正文此前漏了）。
  return lines.map((line, index) => `<div class="code-line" data-line="${index + 1}"><span class="line-number">${index + 1}</span><span>${escapeHtml(line) || " "}</span></div>`).join("");
}

function liveTextDocument(find) {
  const document_ = liveDocument();
  const name = document_.name || document_.path;
  const findBox = find ? currentFindBox() : "";
  return `<div class="document-view"><div class="document-toolbar"><span class="document-path">${escapeHtml(document_.path)}\u3000Read-only</span><button class="icon-button" aria-label="Soft Wraps">${icon("wrap-text")}</button><button class="icon-button" aria-label="Show Whitespaces">${icon("pilcrow")}</button><button class="icon-button" aria-label="Find in File">${icon("search")}</button><button class="icon-button" aria-label="Go to Line">${icon("corner-down-right")}</button></div>${largeFileBanner(document_, typeof window.__augitLargeFileWarningHidden === "function" && window.__augitLargeFileWarningHidden(document_))}${findBox}<div class="code-view" tabindex="0" aria-label="${escapeHtml(name)} Read-only content">${liveLineViews(document_.text)}</div></div>`;
}

/**
 * 大文件只读预览的警告横幅。
 *
 * 权威 `LargeFileNotificationProvider`（`platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/text/
 * LargeFileNotificationProvider.java:37-58`）：`EditorNotificationPanel` 的 **Warning** 状态，文案
 * `large.file.preview.notification` = **"The file is too large ({0}). Showing a read-only preview of the first {1}."**
 * （`IdeBundle.properties:2120`，`{0}` 文件大小、`{1}` = `FileSizeLimit.getPreviewLimit(extension)`），
 * 两个动作标签 `action.label.hide.notification`（**"Hide notification"**，只记在编辑器上、本次会话内隐藏）
 * 与 `label.dont.show`（**"Don't show again"**，写进 `PropertiesComponent` 永久关闭）。
 *
 * 面板画在**编辑器顶部**（正文之上）；隐藏判定见 `largeFileWarningHidden()`。
 */
function largeFileBanner(document_, hidden = false) {
  if (!document_ || !document_.readOnlyPreview || hidden) {
    return "";
  }

  const size = formatFileSize(document_.fileSize);
  const preview = formatFileSize(document_.previewBytes || document_.fileSize);
  return `<div class="editor-notification warning" role="status" data-large-file-banner>`
    + `<span class="editor-notification-icon">${icon("file-warning")}</span>`
    + `<span class="editor-notification-text">Large file (${escapeHtml(size)}). Showing the first ${escapeHtml(preview)} as a read-only preview.</span>`
    + `<span class="grow"></span>`
    + `<button type="button" class="editor-notification-action" data-large-file-action="hide">Hide Notification</button>`
    + `<button type="button" class="editor-notification-action" data-large-file-action="disable">Don't Show Again</button>`
    + `</div>`;
}

// 当前文件查找条：结构与视觉稿一致，初始为空状态。
function currentFindBox() {
  return currentFindBar();
}

/**
 * Markdown 预览状态（规格 §7.3）：实时外壳把"读取在途 >150ms"与"读取失败"记在
 * `live.markdownPreview` 上；没有该状态时（静态视觉稿、正常态）是 `ready`。
 * 只读、不写 DOM —— 渲染与"局部更新"两条路径都取同一份状态。
 */
function readMarkdownPreviewState() {
  const state = window.__augitLive && window.__augitLive.markdownPreview;
  const value = state && (state.state === "loading" || state.state === "failure") ? state.state : "ready";
  return { state: value, reason: value === "failure" ? String(state.reason || "") : "" };
}

function liveMarkdownDocument(mode) {
  const document_ = liveDocument();
  const source = `<div class="markdown-source code-view" tabindex="0" aria-label="Read-only Markdown source">${liveLineViews(document_.text)}</div>`;
  const preview = `<article class="markdown-preview" tabindex="0" aria-label="Markdown preview body">${document_.preview || ""}</article>`;
  // 预览状态（规格 §7.3：加载超 150ms 在预览区顶部给紧凑提示、失败给原因并可点预览重试）：
  // 状态放在 `live.markdownPreview`，这里只按状态渲染，避免"把状态写进 DOM 再读回来"。
  const markdownState = readMarkdownPreviewState();
  // `data-document-path`：实时层靠它判断"这次刷新是该文档的内容更新"，从而**就地**更新原文与预览
  //（规格 §7.3：外部更新复用原文控件，保留滚动/选区/比例/模式），而不是整块替换编辑区。
  return `<div class="document-view markdown-document" data-document-path="${escapeHtml(document_.path)}" data-markdown-mode="${mode}" data-markdown-state="${markdownState.state}" data-markdown-reason="${escapeHtml(markdownState.reason)}"><div class="markdown-panes">${source}<div class="markdown-divider" role="separator" aria-label="Adjust Markdown preview width" aria-orientation="vertical" tabindex="0"></div><div class="markdown-preview-region">${preview}<div class="markdown-feedback" role="status"></div></div></div></div>`;
}

function liveJsonDocument() {
  const document_ = liveDocument();
  // 格式错误（规格 §7.4）：默认显示原文、禁用"格式化"按钮、顶部给出准确行列与错误文字，
  // 错误条的点击/Enter/Space 都定位到出错行并把焦点交给正文。
  const error = document_.jsonError || null;
  const invalid = !!error;
  const formatted = invalid ? (document_.text || "") : (document_.formatted || document_.text || "");
  // 规格 §7.4：外部更新后要**保留当前模式**；文件恢复有效后继续保留原文模式，直到用户主动切换
  // ⇒ 模式来自标签记忆（`live-data.js` 的 `__augitRememberedJsonMode()`，与 Markdown 的模式记忆同款），
  // 只有"变无效"这一档强制原文。视觉稿页面没有该函数 ⇒ 退回原来的"有效即格式化"。
  const remembered = typeof window.__augitRememberedJsonMode === "function"
    ? window.__augitRememberedJsonMode()
    : null;
  const mode = invalid ? "source" : remembered === "source" ? "source" : "formatted";
  const errorBar = invalid
    ? `<button class="json-error" data-json-error-line="${escapeHtml(String(error.line))}" aria-label="Locate JSON Error">Invalid JSON: line ${escapeHtml(String(error.line))}, column ${escapeHtml(String(error.column))}. Click to locate.</button>`
    : "";
  const formattedButton = invalid
    ? `<button class="segment" aria-label="Formatted" data-json-mode="formatted" disabled title="Invalid JSON. See the error location in Raw view">${icon("document-formatted")}</button>`
    : `<button class="segment" aria-label="Formatted" data-json-mode="formatted">${icon("document-formatted")}</button>`;
  return `<div class="document-view json-document${invalid ? " json-invalid" : ""}" data-json-mode="${mode}"><div class="document-toolbar"><span class="document-path">${escapeHtml(document_.path)}\u3000Read-only</span><div class="segmented document-modes"><button class="segment" aria-label="Raw" data-json-mode="source">${icon("document-source")}</button>${formattedButton}</div><button class="icon-button" aria-label="More">${icon("ellipsis-vertical")}</button></div>${errorBar}<div class="code-view" tabindex="0" aria-label="Read-only JSON" data-json-source="${escapeHtml(document_.text || "")}" data-json-formatted="${escapeHtml(invalid ? "" : (document_.formatted || ""))}" data-json-error-line="${invalid ? escapeHtml(String(error.line)) : ""}">${liveLineViews(formatted)}</div></div>`;
}

/** 文件大小标签：与视觉稿一致（`52.5 KB` / `4.8 MB`）。 */
function formatFileSize(bytes) {
  const value = Number(bytes);
  if (!Number.isFinite(value) || value <= 0) return "";
  if (value >= 1024 * 1024) return `${(value / (1024 * 1024)).toFixed(1)} MB`;
  if (value >= 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${Math.round(value)} B`;
}

function liveImageDocument() {
  const document_ = liveDocument();
  const size = document_.pixelWidth && document_.pixelHeight ? `${document_.pixelWidth} × ${document_.pixelHeight}` : "";
  const bytes = formatFileSize(document_.fileSize);
  const label = [size, document_.typeName, bytes].filter(Boolean).join(" · ");
  return `<div class="document-view"><div class="document-toolbar image-toolbar"><button class="icon-button" aria-label="Zoom Out">${icon("zoom-out")}</button><span class="image-zoom-label">100%</span><button class="icon-button" aria-label="Zoom In">${icon("zoom-in")}</button><button class="icon-button" aria-label="Fit to View">${icon("image-fit")}</button><span class="image-size-label" title="${escapeHtml(label)}"><span class="image-size-content">${escapeHtml(label)}</span></span></div><div class="image-stage" tabindex="0" aria-label="Read-only image" data-document-path="${escapeHtml(document_.path)}"><img src="${escapeHtml(document_.dataUrl || "")}" alt="${escapeHtml(document_.name || "")}" draggable="false"></div></div>`;
}

/**
 * 实时外壳的 Stash 对话框正文（结构来自视觉稿的 stashBody）：
 * Git 根目录、当前分支、消息、保留索引状态，以及窗口内的状态提示。
 * 根目录在 Augit 里只有一个（单窗口单仓库），因此用只读文本而不是样例里的下拉框。
 */
/**
 * 「打开工作区」（视觉稿 workspace-open）：左侧三个入口，右侧最近目录列表。
 * 最近目录来自设置（宿主已下发），条目带完整路径与悬停说明。
 */
function liveWorkspaceOpenBody() {
  const live = window.__augitLive || {};
  const recent = (live.settings && live.settings.recentWorkspaces) || [];
  const name = (path) => String(path).split(/[\\/]/).filter((part) => part.length > 0).at(-1) || path;
  const rows = recent.length === 0
    ? '<p class="commit-meta">No other directories have been opened.</p>'
    : recent.map((path) => `<div class="tree-row" data-workspace-path="${escapeHtml(path)}" title="${escapeHtml(path)}"><span>${treeFolderIcon(false)}</span><span class="tree-name">${escapeHtml(name(path))}</span><span class="tree-path">${escapeHtml(path)}</span></div>`).join("");
  return `<div class="management-content" style="height:350px"><div class="management-list">`
    + `<div class="tree-row selected">${icon("history")} Recent Workspaces</div>`
    + `<div class="tree-row" data-workspace-action="pick">${icon("folder-open")} Choose Directory…</div>`
    + `<div class="tree-row" data-workspace-action="clone">${icon("clone")} Clone Repository…</div>`
    + `</div><div class="management-detail"><h2>Recent Workspaces</h2>${rows}`
    + `<p class="commit-meta">Opening the same directory activates its existing window.</p>`
    + `<p class="workspace-notice commit-meta" role="status" hidden></p></div></div>`;
}

function liveStashBody() {
  const live = window.__augitLive || {};
  const status = live.status || {};
  const root = status.root || live.root || "";
  const branch = status.branch || live.branch || "";
  return `<div class="form-grid"><label for="stash-root">Git root</label>`
    + `<input id="stash-root" class="text-field" value="${escapeHtml(root)}" readonly>`
    + `<label>Current branch</label><span class="stash-branch">${escapeHtml(branch)}</span>`
    + `<label for="stash-message">Message</label><textarea id="stash-message" class="message-field" data-stash-field="message"></textarea><span></span>`
    + `<div class="stash-check-row"><label class="check-line" title="Keep staged changes as-is"><input id="stash-keep" type="checkbox" data-stash-field="keep">Keep index</label><label class="check-line" title="Also stash untracked files."><input id="stash-include-untracked" type="checkbox" data-stash-field="includeUntracked">Include untracked files</label></div></div>`
    + `<div class="stash-notice" role="status" hidden></div>`;
}

function liveUnavailableDocument(overrideMessage = null) {
  const document_ = liveDocument();
  // 与视觉稿同构：`名称 · 类型 · 大小`、路径、原因，以及"使用系统默认程序打开"。
  const meta = [document_.name, document_.typeName, formatFileSize(document_.fileSize)]
    .filter((part) => part && String(part).length > 0)
    .map((part) => escapeHtml(part))
    .join(" · ");
  return `<div class="info-state"><div class="info-block"><span style="color:var(--augit-warning)">${icon("file-warning")}</span><h2>This file cannot be previewed in Augit</h2><p>${meta}</p><p>${escapeHtml(document_.path)}</p><p>${escapeHtml(overrideMessage || document_.message || "This file cannot be viewed as read-only text.")}</p><div class="button-row" style="justify-content:center"><button class="secondary-button" type="button" data-external-open>Open in Default Application</button></div></div></div>`;
}

function textView(find = false) {
  if (liveDocument()) return liveTextDocument(find);
  const state = new URLSearchParams(window.location.search).get('find-state');
  const query = state === 'invalid' ? '[' : state === 'timeout' ? '(a+)+$' : state === 'empty' ? '' : state === 'no-match' ? '不存在的文字' : 'Git';
  const result = state === 'loading' ? 'Searching…' : state === 'timeout' ? 'Search timed out' : state === 'invalid' ? 'Invalid regular expression' : '';
  const findBox = find ? currentFindBar(query, result, state === 'invalid' || state === 'timeout') : "";
  // `large-preview=1`／`large-preview=hidden`：大文件只读预览的警告横幅（有／已被隐藏）。
  const largePreview = new URLSearchParams(window.location.search).get("large-preview");
  const largeBanner = largePreview
    ? largeFileBanner({ readOnlyPreview: true, fileSize: 21 * 1024 * 1024, previewBytes: 2500 * 1024 }, largePreview === "hidden")
    : "";
  return `<div class="document-view"><div class="document-toolbar"><span class="document-path">Augit › src › Augit.App › MainWindow.cs　Read-only</span><button class="icon-button" aria-label="Soft Wraps">${icon("wrap-text")}</button><button class="icon-button" aria-label="Show Whitespaces">${icon("pilcrow")}</button><button class="icon-button" aria-label="Find in File">${icon("search")}</button><button class="icon-button" aria-label="Go to Line">${icon("corner-down-right")}</button></div>${largeBanner}${findBox}<div class="code-view" tabindex="0">${codeLines(textViewerSourceLines, 23)}</div></div>`;
}

// 归属背景与富文本提示参考AnnotateToggleAction.computeBgColors和GitFileAnnotation.getHtmlToolTip。
/** 按提交者时间倒序分组；同时间的不同提交使用同色，21色索引按参考整数截断。 */
function blameColorIndexes(lines) {
  const dates = new Map();
  for (const line of lines) {
    const time = Number(line.committerTimestamp ?? line.timestamp ?? Date.parse(line.dateTime || line.date));
    dates.set(line.fullHash || line.hash, Number.isFinite(time) ? time : 0);
  }
  const groups = [...new Set(dates.values())].sort((left, right) => right - left);
  const indexes = new Map(groups.map((time, index) => [time, Math.floor(21 * index / groups.length)]));
  return new Map([...dates].map(([hash, time]) => [hash, indexes.get(time)]));
}

/** 提示元数据只存转义后的文本；提交哈希与消息不作为HTML执行。 */
function blameRowAttributes(line, colorIndex, path) {
  return `style="--blame-row-background:var(--augit-blame-color-${colorIndex})" data-blame-author="${escapeHtml(line.author)}" data-blame-date="${escapeHtml(blameTooltipDate(line))}" data-blame-message="${escapeHtml(line.message ?? `${line.summary || ''}\n...`)}" data-blame-path="${escapeHtml(line.path && line.path !== path ? line.path : '')}"`;
}

/** 归属行悬停提示要用的日期串：优先宿主给的**日期时间**（`dateTime`），退化到短日期。 */
function blameTooltipDate(line) {
  return (line && (line.dateTime || line.date)) || "";
}

// 未提交行没有可归属的提交：参考 PyCharm Annotate 对这类行保留空白归属槽与行号。
// `git blame` 对工作区行返回全零 hash 与 "Not Committed Yet"；两者都按未提交处理。
function blameLineUncommitted(line) {
  const hash = String((line && line.hash) || "");
  return !hash || /^0+$/.test(hash) || String((line && line.author) || "").trim() === "Not Committed Yet";
}
// 外壳注入真实 Blame 时使用：行高由既有 measureCodeViews 统一校准，槽位结构与样例一致。
function liveBlameView() {
  const blame = window.__augitLive.blame;
  const lines = blame.lines || [];
  const colors = blameColorIndexes(lines);
  // 工具栏在按**上一修订**标注时补一行说明（权威在新标签里标注，Augit 就地重标注 ⇒ 至少要能看出依据哪一版）。
  const revisionNote = blame.revision
    ? `<span class="commit-meta">Previous Revision ${escapeHtml(String(blame.revision).slice(0, 7))}</span>`
    : "";
  return `<div class="document-view blame-document"><div class="document-toolbar"><span class="document-path">${escapeHtml(blame.path || "")}\u3000Read-only</span>${revisionNote}<span class="grow"></span><span class="commit-meta">${lines.length} Line Attribution</span><button class="icon-button" aria-label="Close Blame">${icon("x")}</button></div><div class="blame-layout"><div class="blame-gutter">${lines.map(line => {
              if (blameLineUncommitted(line)) return `<div class="blame-row uncommitted" aria-label="Line ${line.number} uncommitted"><span></span><span></span><span>${line.number}</span></div>`;
              return `<a href="#blame-commit" data-blame-commit="${escapeHtml(line.hash)}" data-blame-full="${escapeHtml(line.fullHash || line.hash)}" data-blame-previous="${escapeHtml(line.previousRevision || "")}" aria-label="Locate commit for line ${line.number} commit" class="blame-row" ${blameRowAttributes(line, colors.get(line.fullHash || line.hash), blame.path)}><span>${escapeHtml(line.date)}</span><span>${escapeHtml(line.author)}</span><span>${line.number}</span></a>`;
            }).join("")}</div><div class="code-view" tabindex="0" aria-label="Blame Read-only content">${lines.map(line => `<div class="code-line"><span class="line-number">${line.number}</span><span>${escapeHtml(line.content) || " "}</span></div>`).join("")}</div></div></div>`;
}

function blameView() {
  // 样例归属全部指向文件历史的根提交（data-blame-commit="commit-4"），悬停提示取该提交在日志样例里的
  // 同一份数据（主题、作者 I49、2026/8/28 8:25），修订沿用本文件对该提交的既有显示值。
  const blame = sourceLines.map((_, index) => ({ hash: "commit-4", date: "2026/8/28", dateTime: "2026/8/28 8:25", author: "I49", number: index + 1, message: "feat: 实现 Augit 阶段零至五功能" }));
  const colors = blameColorIndexes(blame);
  return `<div class="document-view blame-document"><div class="document-toolbar"><span class="document-path">docs/product-spec.md　Read-only</span><span class="commit-meta">${blame.length} Line Attribution</span><button class="icon-button" aria-label="Close Blame">${icon("x")}</button></div><div class="blame-layout"><div class="blame-gutter">${blame.map((item, index) => `<a href="#blame-commit" data-blame-commit="commit-4" data-blame-full="commit-4" aria-label="Locate commit for line ${item.number} commit" class="blame-row ${index === 18 ? "selected" : ""}" ${blameRowAttributes(item, colors.get(item.hash), "docs/product-spec.md")}><span>${item.date}</span><span>${item.author}</span><span>${item.number}</span></a>`).join("")}</div><div class="code-view" tabindex="0" aria-label="Blame Read-only content">${codeLines(sourceLines, 19)}</div></div></div>`;
}

/** 富文本提示按getHtmlToolTip呈现；完整消息按需合并，关闭或重绘后丢弃晚到结果。 */
function bindBlameTooltip(documentView) {
  const lifetime = new AbortController();
  const gutter = documentView.querySelector('.blame-gutter');
  const messages = new Map();
  let tip = null, owner = null, showTimer = null, hideTimer = null;
  const hide = () => {
    clearTimeout(showTimer); clearTimeout(hideTimer);
    owner?.removeAttribute('aria-describedby');
    tip?.remove(); tip = null; owner = null;
  };
  const laterHide = () => { clearTimeout(hideTimer); hideTimer = setTimeout(hide, 150); };
  const show = row => {
    hide();
    if (!row || row.classList.contains('uncommitted')) return;
    if (!documentView.isConnected || !row.getClientRects().length) return;
    owner = row;
    tip = document.createElement('div');
    tip.id = 'augit-blame-tooltip'; tip.className = 'blame-tooltip'; tip.setAttribute('role', 'tooltip');
    const revision = row.dataset.blameFull || row.dataset.blameCommit;
    tip.innerHTML = `<div>commit <a href="#blame-commit" class="blame-tooltip-revision">${escapeHtml(revision)}</a></div><div>Author: ${escapeHtml(row.dataset.blameAuthor)}</div><div>Date: ${escapeHtml(row.dataset.blameDate)}</div>${row.dataset.blamePath ? `<div>Path: ${escapeHtml(row.dataset.blamePath)}</div>` : ''}<div class="blame-tooltip-message"></div>`;
    tip.querySelector('.blame-tooltip-message').textContent = row.dataset.blameMessage;
    row.setAttribute('aria-describedby', tip.id);
    const currentTip = tip;
    tip.addEventListener('pointerenter', () => clearTimeout(hideTimer), { signal: lifetime.signal });
    tip.addEventListener('pointerleave', laterHide, { signal: lifetime.signal });
    tip.querySelector('a').addEventListener('click', event => {
      event.preventDefault(); row.focus({ preventScroll: true }); hide(); row.click();
    }, { signal: lifetime.signal });
    document.body.append(tip);
    const box = row.getBoundingClientRect();
    const position = () => {
      const bounds = currentTip.getBoundingClientRect();
      currentTip.style.left = `${Math.max(4, Math.min(innerWidth - bounds.width - 4, box.left))}px`;
      currentTip.style.top = `${Math.max(4, Math.min(innerHeight - bounds.height - 4, box.bottom + 4))}px`;
    };
    position();
    if (typeof window.__augitReadBlameCommit === 'function') {
      if (!messages.has(revision)) {
        if (messages.size >= 64) messages.delete(messages.keys().next().value);
        messages.set(revision, window.__augitReadBlameCommit(revision).catch(() => null));
      }
      void messages.get(revision).then(message => {
        // 大消息仍完整显示，但不让64项缓存累积到宿主输出上限的数十倍。
        if (typeof message === 'string' && message.length > 16384) messages.delete(revision);
        if (tip !== currentTip || owner !== row || !documentView.isConnected || typeof message !== 'string') return;
        tip.querySelector('.blame-tooltip-message').textContent = message;
        position();
      });
    }
  };
  gutter.addEventListener('pointerover', event => {
    const row = event.target.closest('.blame-row');
    if (!row || row.contains(event.relatedTarget)) return;
    hide(); showTimer = setTimeout(() => show(row), 500);
  }, { signal: lifetime.signal });
  gutter.addEventListener('pointerout', event => {
    const row = event.target.closest('.blame-row');
    if (row && !row.contains(event.relatedTarget)) { clearTimeout(showTimer); laterHide(); }
  }, { signal: lifetime.signal });
  gutter.addEventListener('focusin', event => { const row = event.target.closest('.blame-row'); if (row) show(row); }, { signal: lifetime.signal });
  gutter.addEventListener('focusout', event => { if (!tip?.contains(event.relatedTarget)) laterHide(); }, { signal: lifetime.signal });
  documentView.querySelector('.code-view').addEventListener('scroll', hide, { signal: lifetime.signal });
  document.addEventListener('keydown', event => {
    if (tip && event.key === 'Escape') { event.preventDefault(); hide(); }
  }, { signal: lifetime.signal, capture: true });
  document.addEventListener('pointerdown', event => { if (!tip?.contains(event.target)) hide(); }, { signal: lifetime.signal });
  window.addEventListener('resize', hide, { signal: lifetime.signal });
  window.addEventListener('pagehide', hide, { signal: lifetime.signal });
  const dispose = () => { hide(); messages.clear(); lifetime.abort(); delete documentView.dataset.blameTooltipBound; };
  if (typeof registerRegionDisposer === 'function') registerRegionDisposer(dispose);
  return dispose;
}

function bindBlame() {
  document.querySelectorAll(".blame-document").forEach(documentView => {
    if (!documentView.dataset.blameTooltipBound) {
      documentView.dataset.blameTooltipBound = 'true';
      documentView.__disposeBlameTooltip = bindBlameTooltip(documentView);
    }
    if (documentView.dataset.blameBound) return;
    documentView.dataset.blameBound = 'true';
    const body = documentView.querySelector(".code-view"), gutter = documentView.querySelector(".blame-gutter");
    const sync = () => { gutter.scrollTop = body.scrollTop; };
    body.addEventListener("scroll", sync, { passive: true });
    gutter.addEventListener("click", event => {
      const row = event.target.closest(".blame-row");
      if (!row || row.classList.contains("uncommitted")) return;
      event.preventDefault();
      gutter.querySelectorAll(".blame-row").forEach(item => item.classList.toggle("selected", item === row));
      // 实时外壳里这段链路属于实时层：`live-data.js` 的捕获阶段监听调 `locateBlameCommit()`
      // （用**完整**哈希、文件历史存在时先恢复日志布局、提交不在已加载历史里则就地说明）。
      // 这里若继续跑下面的**样例**分支，就会把样例日志写进实时界面 —— 第 255 轮实测：
      // 日志已可见时点一次归属行，实时提交列表被换成"只有命中那一行"的样例列表（其余提交与
      // 提交图都没了），随后实时层再点另一行时列表里已找不到目标 ⇒ 误报"无法定位到该提交"。
      // 因此样例分支只服务静态视觉稿（`blame.html`）；实时模式保留上面的选中反馈与下面的
      // 纵向同步（`sync()` 是真实行为，不属于样例）。
      if (window.__augitLive && window.__augitLive.blame) { sync(); return; }
      // 底部工具窗是主内容区第二行的直接子节点，不再嵌套在编辑器 workspace 中。
      let bottom = document.querySelector(".app-main > .bottom-tool");
      if (!bottom) return;
      // 固定样本来自文件历史中的根提交；定位只更新底部日志，正文和标签保持原位。
      const hash = row.dataset.blameCommit;
      if (bottom.dataset.blameCommit !== hash) {
        const template = document.createElement("template");
        template.innerHTML = gitLog(false);
        const log = template.content.firstElementChild;
        const commit = log.querySelector(`[data-hash="${hash}"]`);
        log.querySelector(".commit-list").replaceChildren(commit);
        commit.querySelector(".commit-graph-svg").outerHTML = commitGraphSvg(buildCommitGraph([{ hash, parents: [] }]), 0);
        log.querySelector('[aria-label="Text or hash"]').value = hash;
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
    documentView.querySelector('[aria-label="Close Blame"]').addEventListener("click", () => {
      // 实时外壳里"关闭 Blame"由实时层负责（`closeBlameView()`：使在途查询失效、清状态、
      // 恢复该文件的普通只读文档视图并把焦点交回正文）。样例分支会把**视觉稿的样例 Markdown 文档**
      // 换进实时界面 —— 第 262 轮实测关闭后正文变成"Augit 产品概要"样例、而 `live.blame` 仍然存在。
      if (window.__augitLive && window.__augitLive.blame) return;
      const top = body.scrollTop, left = body.scrollLeft;
      documentView.__disposeBlameTooltip?.();
      const template = document.createElement("template");
      template.innerHTML = markdownView("source");
      const replacement = template.content.firstElementChild;
      const source = replacement.querySelector(".markdown-source");
      body.classList.add("markdown-source");
      body.setAttribute("aria-label", "Read-only Markdown source");
      source.replaceWith(body);
      documentView.replaceWith(replacement);
      body.removeEventListener("scroll", sync);
      bindMarkdownModes();
      if (typeof bindCurrentFind === 'function') bindCurrentFind();
      if (typeof bindDiffFind === 'function') bindDiffFind();
      measureCodeViews();
      measureDocumentToolbar();
      body.scrollTo(left, top);
      body.focus({ preventScroll: true });
    });
  });
}

// ---- 提交图几何 ----------------------------------------------------------------
// 权威出处：`platform/vcs-log/impl/src/com/intellij/vcs/log/paint/PaintParameters.java:9-15`
// （单轨宽 16、节点半径 4、普通线宽 1.5、选中线宽 2.5、图形文字间距 2、基准行高 22）、
// 同文件 `:17-43` 的 `scaleWithRowHeight(value, h) = value * h / 22`，
// `SimpleGraphCellPainter.kt:79-99`（行中心／轨宽／轨中心／线宽／节点直径的取整），
// `HeadNodePainter.kt:22-29`（HEAD 三个同心圆与 `RADIUS_DELTA`），
// `GraphCommitCellUtil.kt:17-33`（图形区宽度公式）。
//
// 绘制尺寸不是常量本身：`PaintUtil.alignToInt(value, ctx, FLOOR, ODD)`
// （`platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java:163-172`）先把值换算到**设备空间**向下取整，
// 再对齐到最近的奇数，最后换算回用户空间。100% 缩放下因此各少 1（16→15、直径 8→7、线宽 1.5→1）。
const GRAPH_BASE_ROW_HEIGHT = 22;
const GRAPH_WIDTH_NODE = 16;
const GRAPH_CIRCLE_RADIUS = 4;
const GRAPH_THICK_LINE = 1.5;
const GRAPH_SELECT_THICK_LINE = 2.5;
const GRAPH_TEXT_GAP = 2;
const GRAPH_RADIUS_DELTA = 2;

let graphGeometryCache = { key: "", value: null };

/** 历史行高：与 `applyTypography()` 写入 `:root` 的 `--augit-history-row-height` 同源；未写入时回落 26。 */
function historyRowHeight() {
  const raw = Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--augit-history-row-height"));
  return Number.isFinite(raw) && raw > 0 ? raw : 26;
}

/** 当前行高与设备像素比下的几何；同一组输入只推导一次（每行渲染都要用）。 */
function currentGraphGeometry() {
  const rowHeight = historyRowHeight();
  const deviceScale = window.devicePixelRatio || 1;
  const key = `${rowHeight}|${deviceScale}`;
  if (graphGeometryCache.key !== key) graphGeometryCache = { key, value: graphGeometryFor(rowHeight, deviceScale) };
  return graphGeometryCache.value;
}

/** 基准常量按行高等比缩放，再按 `alignToInt(…, FLOOR, ODD)` 在设备空间对齐。 */
function graphGeometryFor(rowHeight, deviceScale) {
  const ratio = rowHeight / GRAPH_BASE_ROW_HEIGHT;
  const align = (value, odd) => {
    let device = Math.floor(value * deviceScale);
    if (odd && device % 2 === 0) device -= 1;
    return device / deviceScale;
  };
  const pixel = 1 / deviceScale;
  const lineThickness = Math.max(align(GRAPH_THICK_LINE * ratio, true), pixel);
  const nodeDiameter = align(2 * GRAPH_CIRCLE_RADIUS * ratio, true);
  const headOuterDiameter = align(2 * (GRAPH_CIRCLE_RADIUS + GRAPH_RADIUS_DELTA) * ratio, true);
  const nodeRadius = align(nodeDiameter / 2, false);
  const headOuterRadius = align(headOuterDiameter / 2, false);
  return {
    rowHeight,
    deviceScale,
    // 行中心 `alignToInt(rowHeight / 2, FLOOR, ODD)`（`SimpleGraphCellPainter.kt:81`）。
    rowCenter: align(rowHeight / 2, true),
    // 轨宽 `alignToInt(scaleWithRowHeight(16), FLOOR, ODD)`、轨中心 `alignToInt(…/2, FLOOR, null)`（`:83-84`）：
    // 第 position 轨中心 x = elementWidth * position + elementCenter。
    elementWidth: align(GRAPH_WIDTH_NODE * ratio, true),
    elementCenter: align(GRAPH_WIDTH_NODE * ratio / 2, false),
    lineThickness,
    // 选中线宽只服务**图元素选中**（悬停箭头、折叠片段高亮），Augit 没有这类动作
    // （`06-commit-graph.md`：选中集合只由图动作写入，表格行选中不进入该集合）⇒ 只登记缩放值、不绘制。
    selectedThickness: Math.max(align(GRAPH_SELECT_THICK_LINE * ratio, true), lineThickness + 2 * pixel),
    nodeDiameter,
    nodeRadius,
    headOuterDiameter,
    headOuterRadius,
    headDelta: align(GRAPH_RADIUS_DELTA * ratio, false),
    // 权威用**取整后的半径**做定位偏移、用**取整后的奇数直径**做尺寸（`SimpleGraphCellPainter.kt:172-178`），
    // 于是画出的圆心比轨中心偏 (radius - diameter / 2)（基准下 -0.5px）。照此复现，不"顺手居中"。
    nodeOffset: nodeRadius - nodeDiameter / 2,
    headOffset: headOuterRadius - headOuterDiameter / 2,
  };
}

/**
 * 图形区宽度：权威 `GraphCommitCellUtil.getGraphWidth`（`:32`）＝
 * `floor(列数 × elementWidth_原始) + floor(图形文字间距_原始)`。
 * 注意它用的是**未对齐的**原始轨宽（`PaintParameters.getElementWidth`），与绘制用的对齐值不同。
 */
function commitGraphWidth(columns, rowHeight) {
  const ratio = rowHeight / GRAPH_BASE_ROW_HEIGHT;
  return Math.floor(columns * GRAPH_WIDTH_NODE * ratio) + Math.floor(GRAPH_TEXT_GAP * ratio);
}

/**
 * 提交列表容器上的图形几何：列数留给重算用，宽度按权威公式当场写入
 * （不再使用固定值；CSS 里保留同式的 calc 作为未运行脚本时的回落）。
 */
function commitGraphListAttributes(graph) {
  const rowHeight = historyRowHeight();
  const width = commitGraphWidth(graph.columns, rowHeight);
  return `data-graph-columns="${graph.columns}" style="--augit-graph-columns:${graph.columns};--augit-graph-width:${width}px"`;
}

/** 字形或 DPI 变化后重算图形区宽度并重绘已有 SVG（几何变了才重绘，避免 resize 期间反复建节点）。 */
let appliedGraphKey = "";
function refreshCommitGraphGeometry() {
  const metrics = currentGraphGeometry();
  const key = `${metrics.rowHeight}|${metrics.deviceScale}|${document.body.dataset.theme || "light"}`;
  const changed = key !== appliedGraphKey;
  appliedGraphKey = key;
  document.querySelectorAll(".commit-list.commit-list-graph").forEach(list => {
    const columns = Number(list.dataset.graphColumns) || 1;
    list.style.setProperty("--augit-graph-width", `${commitGraphWidth(columns, metrics.rowHeight)}px`);
  });
  if (changed) {
    document.querySelectorAll(".commit-graph-svg").forEach(svg => {
      svg.outerHTML = commitGraphSvg({ columns: Number(svg.dataset.graphColumns) || 1, rows: [JSON.parse(svg.dataset.graphRow)] }, 0);
    });
  }
  // 行宽与作者/日期列宽都是按当前字体度量算出来的（`bindHistoryLayout()`），
  // 图形区宽度变了就必须重排一次，否则标题会停在旧图形区右边。
  document.querySelectorAll(".log-list-panel").forEach(panel => {
    panel.dispatchEvent(new CustomEvent("history-geometry-changed"));
  });
}

let graphColorContext = { key: "", colors: new Map() };

function buildCommitGraph(entries, context = null) {
  const cacheKey = context?.cacheKey || "";
  if (cacheKey && graphColorContext.key !== cacheKey) graphColorContext = { key: cacheKey, colors: new Map() };
  const cachedColors = cacheKey ? graphColorContext.colors : null;
  const aliases = new Map(entries.flatMap((entry) => [[entry.hash, entry.hash], [entry.fullHash || entry.hash, entry.hash]]));
  entries = entries.map((entry) => ({ ...entry, parents: [...new Set(entry.parents || [])].map((parent) => aliases.get(parent) || parent) }));
  const positions = new Map(entries.map((entry, index) => [entry.hash, index]));
  const lanes = [], rows = [];
  let fragmentIndex = 1, columnCount = 1;
  const stringHash = (value) => {
    let hash = 0;
    const text = String(value || "");
    for (let index = 0; index < text.length; index++) hash = (Math.imul(hash, 31) + text.charCodeAt(index)) | 0;
    return hash;
  };
  const remotes = (window.__augitLive?.remotes?.remotes || []).map((remote) => remote.name || remote);
  const refPriority = (name, entry) => ["origin/master", "origin/main"].includes(name) ? 0
    : remotes.some((remote) => name.startsWith(`${remote}/`)) || name.startsWith("origin/") ? 1
      : ["master", "main"].includes(name) ? 2 : name.startsWith("tag:") ? 4
        : name === "HEAD" ? 6 : name.startsWith("refs/") ? 7 : 3;
  const bestRef = (entry) => [...(entry.references || [])].sort((left, right) => refPriority(left, entry) - refPriority(right, entry)
    || (left < right ? -1 : left > right ? 1 : 0))[0] || null;
  const referencedParents = new Set(entries.flatMap((entry) => entry.parents));
  const heads = entries.filter((entry) => !referencedParents.has(entry.hash)).sort((left, right) => {
    const a = bestRef(left), b = bestRef(right);
    if (a === null || b === null) return a === b ? positions.get(left.hash) - positions.get(right.hash) : a === null ? 1 : -1;
    return refPriority(a, left) - refPriority(b, right) || (a < b ? -1 : a > b ? 1 : 0);
  });
  const fragments = new Map(), colors = new Map();
  // GraphLayoutBuilder 的显式栈遍历：第一父链继承 head 引用色，合并侧链使用片段号。
  for (const head of heads) {
    const headFragment = fragmentIndex;
    const ref = bestRef(head);
    const headColor = ref === null ? 0 : stringHash(ref.replace(/^tag:\s*/, ""));
    const stack = [head];
    while (stack.length) {
      const current = stack.at(-1);
      const firstVisit = !fragments.has(current.hash);
      if (firstVisit) {
        fragments.set(current.hash, fragmentIndex);
        colors.set(current.hash, cachedColors?.get(current.hash)
          ?? (fragmentIndex === headFragment ? headColor : fragmentIndex));
      }
      const parentHash = current.parents.find((parent) => positions.has(parent) && !fragments.has(parent));
      if (parentHash) stack.push(entries[positions.get(parentHash)]);
      else { if (firstVisit) fragmentIndex++; stack.pop(); }
    }
  }
  entries.forEach((entry, rowIndex) => {
    let column = lanes.findIndex(lane => lane.hash === entry.hash);
    const incoming = column >= 0;
    if (!incoming) {
      column = lanes.length;
      lanes.push({ hash: entry.hash, color: colors.get(entry.hash) ?? 0 });
    }
    const node = lanes[column], before = [...lanes];
    lanes.splice(column, 1);
    const parents = [...new Set(entry.parents)];
    const visible = parents.filter(hash => positions.get(hash) > rowIndex);
    const missingColors = [];
    let insertion = column;
    parents.forEach((hash, index) => {
      if (lanes.some(lane => lane.hash === hash)) return;
      const color = colors.get(hash) ?? (index === 0 ? node.color : fragmentIndex++);
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
  if (cachedColors) {
    entries.forEach((entry, index) => {
      cachedColors.set(entry.hash, rows[index].color);
      // 页边界未加载的第一父也继承本段颜色；下一页或筛选后出现时保持关联。
      entry.parents.forEach((parent, parentIndex) => {
        if (!positions.has(parent) && !cachedColors.has(parent)) {
          cachedColors.set(parent, parentIndex === 0 ? rows[index].color : fragmentIndex++);
        }
      });
    });
    while (cachedColors.size > 5000) cachedColors.delete(cachedColors.keys().next().value);
  }
  return { rows, columns: columnCount };
}

/**
 * HEAD 节点：权威 `HeadNodePainter.kt:31-60` 画的是**三个同心实心圆**
 * （外圆节点色 → 中圆行背景 → 内圆节点色），不是"描边环 + 圆点"。
 * 中圆与内圆各按 `RADIUS_DELTA`（缩放后单独取整，`:29`）从外圆直径里收进，环厚因此等于 delta。
 */
function commitGraphHeadNode(x, color, metrics) {
  const cx = x + metrics.headOffset, cy = metrics.rowCenter + metrics.headOffset;
  const outer = metrics.headOuterDiameter / 2;
  return `<circle fill="${color}" cx="${cx}" cy="${cy}" r="${outer}"/>`
    + `<circle fill="var(--augit-row-background, var(--augit-panel))" cx="${cx}" cy="${cy}" r="${outer - metrics.headDelta}"/>`
    + `<circle fill="${color}" cx="${cx}" cy="${cy}" r="${outer - 2 * metrics.headDelta}"/>`;
}

function commitGraphSvg(graph, index, geometry = null) {
  const metrics = geometry || currentGraphGeometry();
  const row = graph.rows[index];
  const colors = (value) => {
    const colorId = Number(value) || 0;
    if (colorId === 0) return "var(--augit-graph-default, #000000)";
    const range = (number) => Math.abs(number % 100) + 70;
    const r = range((Math.imul(colorId, 200) + 30) | 0) / 255;
    const g = range((Math.imul(colorId, 130) + 50) | 0) / 255;
    const b = range((Math.imul(colorId, 90) + 100) | 0) / 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b), delta = max - min;
    let hue = 0;
    if (delta > 0) {
      if (max === r) hue = ((g - b) / delta) % 6;
      else if (max === g) hue = (b - r) / delta + 2;
      else hue = (r - g) / delta + 4;
      hue = (hue * 60 + 360) % 360;
    }
    // DefaultColorGenerator 的 HSB 参数由当前 Islands 主题覆盖。
    const theme = getComputedStyle(document.body);
    const saturation = Number.parseFloat(theme.getPropertyValue("--augit-graph-saturation")) || 0.6;
    const brightness = Number.parseFloat(theme.getPropertyValue("--augit-graph-brightness")) || 0.7;
    const chroma = brightness * saturation;
    const x = chroma * (1 - Math.abs((hue / 60) % 2 - 1));
    const match = brightness - chroma;
    const sector = hue < 60 ? [chroma, x, 0] : hue < 120 ? [x, chroma, 0]
      : hue < 180 ? [0, chroma, x] : hue < 240 ? [0, x, chroma]
        : hue < 300 ? [x, 0, chroma] : [chroma, 0, x];
    return `rgb(${sector.map((channel) => Math.round((channel + match) * 255)).join(",")})`;
  };
  // 纵向：0 = 上一行中心、0.5 = 本行中心、1 = 下一行中心。权威的相邻两行中心正好相距一个行高
  //（`SimpleGraphCellPainter.kt:115,156-168`：本行 y1 = rowCenter，另一端 = rowCenter ± rowHeight）。
  const trackY = (fraction) => metrics.rowCenter + (fraction - 0.5) * metrics.rowHeight;
  const trackX = (column) => metrics.elementCenter + column * metrics.elementWidth;
  const paths = row.segments.map(([fromColumn, fromY, toColumn, toY, color, dashed]) => {
    const x1 = trackX(fromColumn), x2 = trackX(toColumn);
    const y1 = trackY(fromY), y2 = trackY(toY);
    const d = dashed
      ? `M${x1} ${y1}L${x1 + (x2 - x1) * 0.42} ${y1 + (y2 - y1) * 0.42}M${x1 + (x2 - x1) * 0.68} ${y1 + (y2 - y1) * 0.68}L${x2} ${y2}`
      : `M${x1} ${y1}L${x2} ${y2}`;
    return `<path class="graph-line" stroke="${colors(color)}" d="${d}"/>`;
  }).join("");
  const x = trackX(row.column), color = colors(row.color);
  const node = row.head
    ? commitGraphHeadNode(x, color, metrics)
    : `<circle fill="${color}" cx="${x + metrics.nodeOffset}" cy="${metrics.rowCenter + metrics.nodeOffset}" r="${metrics.nodeDiameter / 2}"/>`;
  return `<svg class="commit-graph-svg" data-graph-row='${JSON.stringify(row)}' data-graph-columns="${graph.columns}" style="--augit-graph-line-thickness:${metrics.lineThickness}px" viewBox="0 0 ${commitGraphWidth(graph.columns, metrics.rowHeight)} ${metrics.rowHeight}" aria-hidden="true">${paths}${node}</svg>`;
}

// 当前分支的提交范围：权威 `CurrentBranchHighlighter` 用 "contained in current branch" 判定，
// 即从当前分支 HEAD 沿父链可达的全部提交；被选中的行不染色（由 CSS 规则顺序保证选中优先）。
// 实时数据里 `entry.hash` 是短哈希、`entry.parents` 是完整哈希，因此两边都建索引。
function currentBranchCommitSet(entries) {
  const byHash = new Map();
  for (const entry of entries) {
    byHash.set(entry.hash, entry);
    if (entry.fullHash) byHash.set(entry.fullHash, entry);
  }
  const result = new Set();
  const queue = entries.filter((entry) => entry.head);
  while (queue.length > 0) {
    const entry = queue.pop();
    if (!entry || result.has(entry.hash)) continue;
    result.add(entry.hash);
    for (const parent of entry.parents || []) {
      const next = byHash.get(parent);
      if (next) queue.push(next);
    }
  }
  return result;
}

// 外壳注入真实历史时使用：沿用与样例版一致的提交行、分支标签与图形结构。
/**
 * 日志左竖条：**分支面板自己的动作组**，不是日志级动作的堆放处。
 *
 * 权威 `git4idea` 的"日志里的分支面板"：`BranchesInGitLogUiFactoryProvider.createMainComponent()`
 * （`plugins/git4idea/backend/src/ui/branch/dashboard/BranchesInGitLogUiFactoryProvider.kt:88-140`）
 * 把日志主区做成 `simplePanel().addToLeft(expandControlPanel).addToCenter(branchViewSplitter)`，
 * 其中 `expandControlPanel` 是一张 `CardLayout`：
 *   - 展开卡片 = **竖向** `ActionToolbar`（`createActionToolbar("Git.Log.Branches", actions, false)`，
 *     第三个实参 `false` 就是"竖向"），动作组 = `Git.Log.Hide.Branches` ＋ `Separator()` ＋
 *     `BranchesDashboardTreeComponent.createActionGroup()`（同目录 `BranchesDashboardTreeComponent.kt:171-195`）；
 *   - 折叠卡片 = `ExpandStripeButton`（同目录 `ExpandStripeButton.kt`：文本 "Show Branches"、图标
 *     `AllIcons.Actions.ArrowExpand`，**文字旋转 90° 绘制**，悬停底色取 `ToolWindow.Button.hoverBackground`）。
 *
 * 权威那组动作按 `createActionGroup()` 的顺序是：新建分支／更新选中分支／删除分支／与当前分支比较／
 * 我的分支／获取／收藏／定位到选中分支／(工具栏动作，本 checkout 为空组)／分隔／设置／全部展开／全部折叠。
 * 文案与图标（`intellij.vcs.git.backend.xml:129,135-153`、`GitBundle.properties`）：
 *   - `Git.Log.Hide.Branches` = "Hide Git Branches"（`:723`），图标 `AllIcons.Actions.ArrowCollapse`；
 *   - `Git.New.Branch.In.Log` = "New Branch"（`DvcsBundle.properties:78`），图标 `AllIcons.General.Add`；
 *   - `UpdateSelectedAction` = "Update Selected"（`:678`，图标 `AllIcons.Actions.CheckOut`）、
 *     `DeleteBranchAction` = "Delete Branch"（`:688`，`AllIcons.Actions.GC`）、
 *     `ShowBranchDiffAction` = "Compare with Current"（`:689`，`AllIcons.Actions.Diff`）、
 *     `ShowMyBranchesAction` = "Show My Branches"（`:695`，`AllIcons.Actions.Find`）、
 *     `Git.Fetch` = "Fetch"（`:653`，`AllIcons.Vcs.Fetch`）、
 *     `ToggleFavoriteAction` = "Mark/Unmark As Favorite"（`:703`，`AllIcons.Nodes.Favorite`，
 *     名字里有 Toggle 但它**不是** ToggleAction）、
 *     `NavigateLogToSelectedBranchAction` = "Navigate Log to Selected Branch Head"（`:711`，`AllIcons.General.Locate`）、
 *     `Git.Log.Branches.Settings` = "Branches Pane Settings"（`:705`，`AllIcons.General.GearPlain`）、
 *     ExpandAll／CollapseAll（`ActionsBundle.properties:229,233`）；
 *   - `Git.Log.Branches.Toolbar.Actions`（`:135`）在本 checkout 里是**空组**（无子项、无 add-to-group）⇒ 不渲染。
 * 折叠卡片的 `ExpandStripeButton` 首选尺寸是 `ActionToolbar.DEFAULT_MINIMUM_BUTTON_SIZE` = **22×22**
 * （`ExpandStripeButton.kt:49-51`、`ActionToolbar.java:77-79`）；Augit 竖条的 28px 按钮是实测基线、且 28 ≥ 22
 * （那个 22 是**下限**），两种状态统一到 28 以免折叠/展开时宽度跳变。
 *
 * 启用条件照各 action 的 `update()`（见 `BranchesDashboardActions.kt`）：全部动作在**没有选中引用**时禁用
 * （`BranchesActionBase.update`），删除分支还要求"选中的引用没有一个是当前分支"
 * （`DeleteBranchAction.update`：`refs.none { it.isCurrent || isRemoteBranchProtected }`），
 * 与当前分支比较同理（`ShowBranchDiffAction.update`：`branches.none { !it.isCurrent }` ⇒ 禁用），
 * 定位到选中分支要求选中节点有 `logNavigatableNodeDescriptor`
 * （`NavigateLogToSelectedBranchAction.update`）。Augit 侧因此把"需要选中引用"的项画成**禁用并写明原因**，
 * 并在引用树选中后由 `applyRefSelection()` 就地刷新 —— 与权威"项始终在、按选择启停"一致。
 * 日志级动作（刷新）**不在**这里 —— 权威把 `Vcs.Log.Refresh` 放在横向工具条的右角。
 */
function gitLogStripe(collapsed = false) {
  if (collapsed) {
    // 折叠卡片：只留一个竖排的分支面板入口（权威 `ExpandStripeButton`）。
    // 可见文字照权威的 `action.Git.Log.Show.Branches.text` = "Branches"（`GitBundle.properties:724`）；
    // 这是**标签**而不是动作名，所以无障碍名另给"显示分支"。
    return `<nav class="git-side-toolbar collapsed" aria-label="Git Branches Pane"><button class="toolbar-button stripe-expand" type="button" aria-label="Show Branches" title="Show Branches" data-ref-stripe="show-branches"><span class="stripe-expand-label">Branch</span>${icon("chevron-right")}</button></nav>`;
  }
  const state = refStripeState();
  const entry = (action, label, iconName) => {
    const item = state[action] || { enabled: true, reason: label };
    // 权威 `DeleteBranchAction.update()` 的文案随选中集变化（全是分支 / 混了标签）⇒ 允许状态覆盖标签。
    const text = item.label || label;
    const pressed = item.checked === true;
    const cls = `toolbar-button${pressed ? " selected" : ""}`;
    const pressedAttr = pressed ? ' aria-pressed="true"' : "";
    const title = pressed ? `${text}(enabled)` : text;
    return item.enabled
      ? `<button class="${cls}" type="button" aria-label="${escapeHtml(text)}"${pressedAttr} title="${escapeHtml(title)}" data-ref-stripe="${action}">${icon(iconName)}</button>`
      : `<button class="${cls}" type="button" aria-label="${escapeHtml(text)}"${pressedAttr} title="${escapeHtml(item.reason)}" aria-disabled="true" data-ref-stripe="${action}" disabled>${icon(iconName)}</button>`;
  };
  return `<nav class="git-side-toolbar" aria-label="Git Branches Pane">`
    + entry("hide-branches", "Hide Branches", "chevron-left")
    + `<span class="rail-separator"></span>`
    + entry("new-branch", "New Branch…", "plus")
    + entry("update-selected", "Update Selected", "branch-update")
    + entry("delete-branch", "Delete Branch…", "trash-2")
    + entry("show-diff", "Compare with Current", "git-compare-arrows")
    + entry("my-branches", "My Branches", "search")
    + entry("fetch", "Fetch", "branch-update")
    + entry("favorite", "Mark as Favorite", "star")
    + entry("locate-branch", "Locate Selected Branch", "locate-fixed")
    + entry("settings", "Branches Pane Settings", "settings")
    + entry("expand-all", "Expand All", "chevron-down")
    + entry("collapse-all", "Collapse All", "chevron-right")
    + `</nav>`;
}

/**
 * 竖条里"需要选中引用"的项的启用状态（顺序与文案见 `gitLogStripe()` 的权威表）。
 *
 * 判据全部来自权威的 `update()`；"Augit 尚未提供"的项（引用之间的整份比较、
 * 收藏/我的分支/分组）另给原因，等能力落地后再按同一张表改。
 */
function refStripeState() {
  const live = window.__augitLive || {};
  const references = live.references || { branches: [], tags: [] };
  // 选中集（权威 `BranchesTreeSelection`：引用树是 `DISCONTIGUOUS_TREE_SELECTION`，动作判据都对**整个选中集**）。
  const selections = Array.isArray(live.logRefSelection) ? live.logRefSelection.filter((item) => item && item.name) : [];
  const branches = references.branches || [];
  const tags = references.tags || [];
  const selectedBranches = selections
    .filter((item) => item.kind === "branch" || item.kind === "remote")
    .map((item) => branches.find((branch) => branch.isRemote === (item.kind === "remote") && branch.name === item.name))
    .filter(Boolean);
  const selectedTags = selections
    .filter((item) => item.kind === "tag")
    .map((item) => tags.find((tag) => tag.name === item.name))
    .filter(Boolean);
  const headSelected = selections.some((item) => item.kind === "head");
  const hasSelection = selections.length > 0;
  const isBranch = selectedBranches.length > 0;
  const currentSelected = selectedBranches.some((branch) => branch.isCurrent);
  // 权威 `UpdateSelectedBranchAction`：`isTrackingInfosExist(branchNames)` 只要**有**一个选中分支
  // 配了跟踪就可用；当前分支那一支走"更新方式"，Augit 未提供 ⇒ 只对非当前分支生效（登记差异）。
  const trackedNonCurrent = selectedBranches.filter((branch) => !branch.isCurrent && branch.upstream);
  const nonCurrentBranches = selectedBranches.filter((branch) => !branch.isCurrent);
  // 权威 `DeleteBranchAction.update()`：`allRefsAreBranches = refs.all { it is BranchInfo }` ——
  // **空集也成立**（vacuous），所以没选中时文案仍是 `action.Git.Delete.Branch.title`；
  // 一旦混进标签就换成 `button.delete` = "Delete"。中文没有复数形态，因此数量由选中集本身表达
  // （英文的 `{0,choice,1#Branch|2#Branches}` 在这里塌成同一个字符串，登记这条本地化差异）。
  // 只看选中集里的 kind（不依赖 `live.references` 是否已加载）：标签/HEAD 一旦入集就不是"全是分支"。
  const selectionIncludesTag = selections.some((item) => item.kind === "tag");
  const allBranches = !selectionIncludesTag && !headSelected;
  // 权威 `UpdateSelectedBranchAction.update()`：`GitFetchSupport.fetchSupport(project).isFetchRunning` 为真时禁用。
  const fetchRunning = !!live.logFetchRunning;
  const selectBranch = "Select a branch other than the current branch.";
  const trackingMissing = "The selected branch has no upstream branch.";
  return {
    "new-branch": { enabled: true, reason: "New Branch…" },
    "fetch": { enabled: !fetchRunning, reason: fetchRunning ? "Fetch is in progress." : "Fetch" },
    "hide-branches": { enabled: true, reason: "Hide Branches" },
    // 权威 `UpdateSelectedBranchAction.update()`：`hasRemotes` ＋ 非 fetch 进行中 ＋ `isTrackingInfosExist`；
    // 动作对**每个**选中的非当前分支按它自己的 refspec 快进（`GitBranchActionsUtil.updateBranches()`），
    // 对当前分支改走"更新方式"（合并或 rebase）—— 那条通道 Augit 尚未提供，故排除在选中集之外。
    "update-selected": {
      enabled: trackedNonCurrent.length > 0 && !fetchRunning,
      reason: !isBranch ? selectBranch
        : currentSelected && nonCurrentBranches.length === 0
          ? "Updating the current branch is unavailable."
          : nonCurrentBranches.length === 0 ? selectBranch
            : trackedNonCurrent.length === 0 ? trackingMissing
              : "Fetch is in progress.",
    },
    // 权威 `DeleteBranchAction.update()`：`refs.none { it.isCurrent || isRemoteBranchProtected }` ——
    // 选中集里任何一个当前分支都会禁用整个动作（不是只跳过它）。
    "delete-branch": {
      enabled: hasSelection && !currentSelected,
      label: allBranches ? "Delete Branch…" : "Delete",
      reason: currentSelected
        ? "The selection includes the checked-out branch and cannot be deleted."
        : hasSelection ? (selectionIncludesTag ? "Delete" : "Delete Branch…") : "Select a reference first.",
    },
    // 权威 `ShowBranchDiffAction.update()`（`BranchesDashboardActions.kt:290-313`）：`RefActionBase`
    // 要求有选中的引用，且 `selectedBranches` 里**至少有一个不是当前分支**才可用；
    // `actionPerformed` 对**每个**非当前分支各比较一次。
    "show-diff": {
      enabled: nonCurrentBranches.length > 0,
      reason: !isBranch ? selectBranch
        : nonCurrentBranches.length === 0 ? "The selection is the current branch; there are no commits to compare." : "Compare with Current",
    },
    "my-branches": {
      enabled: true,
      checked: !!live.showOnlyMyBranches,
      reason: live.myBranchesLoading ? "Computing My Branches…" : "My Branches",
    },
    // 权威 `ToggleFavoriteAction`（`BranchesDashboardActions.kt:459-472`）是**新增的持久化状态**
    // （`GitBranchManager.setFavorite` → `GitVcsSettings.branchSettings`，落在 workspace 文件），
    // 且影响行图标与排序。按用户裁决的范围边界（不新增 Augit 没有的功能）**暂不实施**、待产品裁决。
    "favorite": { enabled: false, reason: "Branch favorites are unavailable." },
    // 权威 `NavigateLogToSelectedBranchAction.update()`：选中节点要有 `logNavigatableNodeDescriptor`。
    "locate-branch": { enabled: hasSelection, reason: hasSelection ? "Locate Selected Branch" : "Select a reference first." },
    // 权威 `Git.Log.Branches.Settings`：弹层本身可用；其中只有「显示标签」在 Augit 侧能立即生效。
    "settings": { enabled: true, reason: "Branches Pane Settings" },
    // 权威 `ExpandAllAction`／`CollapseAllAction`：经 `TreeExpander` 作用在分组树上（引用树已有 本地/远程/标签 三组）。
    "expand-all": { enabled: !!live.references, reason: live.references ? "Expand All" : "References have not loaded." },
    "collapse-all": { enabled: !!live.references, reason: live.references ? "Collapse All" : "References have not loaded." },
  };
}

/**
 * 引用树（权威 `BranchesDashboardTreeComponent`）："分支或标签" 搜索 ＋ 引用树。
 *
 * 结构照权威的模型：HEAD/当前分支一行 ＋ `本地`／`远程`／`标签` 分组
 * （本轮只落地这些；收藏分区、"我的分支"过滤、按目录/按仓库分组与"显示标签"开关见 `10-backlog.md` §三·前 第 6 项）。
 * 选中态落在 `live.logRefSelection`（`{name, kind}`），跨区域刷新保留；搜索词落在 `live.logRefFilter`。
 * 引用尚未到达时退回"从已加载提交推出来的分支名"——不编造引用。
 */
function liveRefTree() {
  const live = window.__augitLive || {};
  const filter = (live.logRefFilter || "").trim().toLowerCase();
  // 选中集（权威 `BranchesTreeSelection`：`DISCONTIGUOUS_TREE_SELECTION`，可同时选中多个节点）。
  const selections = Array.isArray(live.logRefSelection) ? live.logRefSelection.filter((item) => item && item.name) : [];
  const refSelected = (name, kind) => selections.some((item) => item.name === name && item.kind === kind);
  const visible = (name) => !filter || name.toLowerCase().includes(filter);
  // 行内容照权威 `BranchTreeCellRenderer`（`BranchesTree.kt:130-152`）：只有名字（图标另示当前/收藏）；
  // 上游引用**只进 tooltip**（`:141-143,163-170` 的 `LinkedBranchDataImpl`），不写在行里。
  //
  // `ancestors` 是这一行所属的**全部可折叠祖先**（外层类型分组 ＋ 各层前缀分组），
  // 折叠/搜索时就地刷新只用它判定可见性，不必再按兄弟节点推算层级。
  // `depth` 用既有的 `.depth-N` 缩进阶梯（`.tree-row.depth-1` = 20px，`mockup.css:742-745`）：
  // 类型分组的直接子节点是 1（与分组功能落地前一致），每进一层前缀多一级。
  const row = (item, kind, ancestors, depth) => {
    const selected = refSelected(item.name, kind);
    const tip = kind === "branch" && item.upstream ? `${item.name} → ${item.upstream}` : item.name;
    return `<div class="tree-row depth-${depth}${selected ? " selected" : ""}" style="--tree-depth:${depth}" role="treeitem" tabindex="0" aria-selected="${selected ? "true" : "false"}" title="${escapeHtml(tip)}" data-ref-name="${escapeHtml(item.name)}" data-ref-kind="${kind}" data-ref-current="${item.isCurrent ? "true" : "false"}" data-ref-commit="${escapeHtml(item.commitHash || "")}" data-ref-group-owner="${escapeHtml(ancestors[0] || "")}" data-ref-ancestors="${escapeHtml(ancestors.join("|"))}">${branchTreeIcon(item, kind)} ${escapeHtml(item.name)}</div>`;
  };
  // 排序照权威 `getRefComparator()`（`GitBranchesTreeModel.kt:141-152`）：
  // 当前分支 → 收藏（Augit 暂无）→ **按目录分组开启时名字含 `/` 的在前**（第三个键
  // `!(isDirectoryGrouping && name.contains('/'))`）→ 引用名自然序
  // （`GitReference.REFS_NAMES_COMPARATOR` = `NaturalComparator`，忽略大小写优先）。
  const byName = (a, b) => String(a.name).localeCompare(String(b.name), "zh-Hans", { numeric: true, sensitivity: "base" });
  // 「按目录分组」（权威 `git.branches.group.by.directory`，`GitGroupBranchByDirectoryAction`；
  // 默认**开启** —— `DvcsBranchSettings.groupingKeyIds` 的默认值就是 `GROUPING_BY_DIRECTORY`，
  // `platform/dvcs-impl/shared/src/com/intellij/dvcs/branch/DvcsBranchSettings.kt:22-23,26-28`）。
  const grouping = live.logRefGroupByDirectory !== false;
  const order = (items) => [...items].sort((a, b) => (b.isCurrent ? 1 : 0) - (a.isCurrent ? 1 : 0)
    || Number(branchIsFavorite(b, b.isRemote ? 'remote' : 'branch')) - Number(branchIsFavorite(a, a.isRemote ? 'remote' : 'branch'))
    || (grouping ? (b.name.includes("/") ? 1 : 0) - (a.name.includes("/") ? 1 : 0) : 0)
    || byName(a, b));
  // 分组可折叠（权威是标准 JTree：`FilteringTree` + `SmartExpander`，打开时 `TreeUtil.expandAll` 默认全展开）。
  // 折叠状态按**折叠键**记在 `live.logRefCollapsed`（跨区域刷新保留）：类型分组用组名（`本地`），
  // 前缀分组用"类型/路径"（`本地/feature`），因此两层键不会互相撞名。
  // 行始终渲染，折叠只体现在 `hidden` 上，这样搜索时能照权威"过滤时展开全部"
  // （`FilteringTree.java:137-139`）就地显示命中行。
  const collapsedGroups = live.logRefCollapsed || {};
  const groupHeader = (label, key, ancestors, depth) => {
    const collapsed = !!collapsedGroups[key] && !filter;
    return `<div class="tree-row group-row depth-${depth}${collapsed ? " collapsed" : ""}" style="--tree-depth:${depth}" role="treeitem" tabindex="0" aria-expanded="${collapsed ? "false" : "true"}" data-ref-collapse-key="${escapeHtml(key)}" data-ref-ancestors="${escapeHtml(ancestors.join("|"))}"${label ? ` data-ref-group="${escapeHtml(label)}"` : ` data-ref-prefix-group="${escapeHtml(key)}"`}><span>${icon(collapsed ? "chevron-right" : "chevron-down")}</span><strong>${escapeHtml(label || key.split("/").at(-1))}</strong></div>`;
  };
  /**
   * 按目录分组（权威 `LazyRefsSubtreeHolder.buildSubTree()`，`GitBranchesTreeModelUtil.kt:255-289`）：
   * 名字按 `/` 切段逐层构树，**组节点落在第一个成员的位置**（`LinkedHashMap` 的插入序）；
   * 组内的子节点再按 `getSubTreeComparator()`（`GitBranchesTreeModel.kt:154-160`：子组 → 当前分支 → 其它）
   * **稳定**排序，而类型层（本地／远程／标签）的子节点不重排、保持 `getRefComparator()` 的顺序
   * （`GitBranchesTreeSingleRepoModel.kt:49-53` 只对 `BranchesPrefixGroup` 的子节点排序）。
   */
  const subtreeRows = (entries, kind, typeLabel, ancestors, depth, prefixPath, nested) => {
    const nodes = [];
    const groups = new Map();
    for (const entry of entries) {
      const [first, ...rest] = entry.parts;
      if (rest.length === 0) {
        nodes.push({ ref: entry.item });
      } else {
        if (!groups.has(first)) {
          groups.set(first, []);
          nodes.push({ prefix: first });
        }
        groups.get(first).push({ item: entry.item, parts: rest });
      }
    }
    if (nested) {
      const rank = (node) => (node.prefix ? 0 : node.ref.isCurrent ? 1 : 2);
      nodes.sort((a, b) => rank(a) - rank(b));
    }
    return nodes.map((node) => {
      if (node.ref) return row(node.ref, kind, ancestors, depth);
      const childPath = prefixPath ? `${prefixPath}/${node.prefix}` : node.prefix;
      const key = `${typeLabel}/${childPath}`;
      return groupHeader("", key, ancestors, depth)
        + subtreeRows(groups.get(node.prefix), kind, typeLabel, [...ancestors, key], depth + 1, childPath, true);
    }).join("");
  };
  const group = (label, items, kind) => {
    if (items.length === 0) return "";
    const header = groupHeader(label, label, [], 0);
    if (!grouping) return header + items.map((item) => row(item, kind, [label], 1)).join("");
    // 前缀分组的祖先链从类型分组开始，`subtreeRows` 再从空的前缀路径往下叠。
    return header + subtreeRows(items.map((item) => ({ item, parts: String(item.name).split("/") })), kind, label, [label], 1, "", false);
  };
  const references = live.references;
  if (!references) {
    const names = new Set();
    for (const commit of (live.history && live.history.commits) || []) {
      for (const name of commit.references || []) if (name !== "HEAD" && name !== "origin/HEAD") names.add(name);
    }
    const rows = [...names].sort().filter(visible);
    return refTreeShell(group("Local", rows.map((name) => ({ name })), "branch"));
  }
  const branches = references.branches || [];
  const tags = references.tags || [];
  // 「我的分支」（权威 `BranchesDashboardTreeModelBase.showOnlyMy` → `NodeDescriptorsModel.buildTreeNodes`
  // 的过滤器 `(ref as? BranchInfo)?.isMy == ThreeState.YES`，`BranchesTreeModel.kt:209-221`）：
  // HEAD 行在过滤之前**无条件**加入（`:215`），只留"我的"分支；标签不是 `BranchInfo` ⇒ 一并被过滤掉。
  const mine = live.showOnlyMyBranches ? new Set(live.myBranchNames || []) : null;
  const branchVisible = (branch) => (!mine || mine.has(branch.name)) && visible(branch.name);
  const local = order(branches.filter((branch) => !branch.isRemote && branchVisible(branch)));
  const remote = order(branches.filter((branch) => branch.isRemote && branchVisible(branch)));
  const visibleTags = order(mine ? [] : tags.filter((tag) => visible(tag.name)));
  const current = branches.find((branch) => branch.isCurrent && !branch.isRemote);
  // 权威 `BranchesTree.kt:210-217`：`HEAD` 节点的匹配文本是 null ⇒ **任何搜索词都保留它**。
  const headName = current ? current.name : "HEAD";
  const headPicked = refSelected(headName, "head");
  const headRow = `<div class="tree-row${headPicked ? " selected" : ""}" role="treeitem" tabindex="0" aria-selected="${headPicked ? "true" : "false"}" data-ref-name="${escapeHtml(headName)}" data-ref-kind="head" data-ref-current="true" data-ref-commit="${escapeHtml((current && current.commitHash) || "")}">HEAD (Current Branch)</div>`;
  // 权威 `git.branches.show.tags`（`GitBranchesTreeShowTagsAction`，默认 true，持久化在项目设置里）。
  const showTags = live.logRefShowTags !== false;
  return refTreeShell(headRow + group("Local", local, "branch")
    + group("Remote", remote, "remote")
    + (showTags ? group("Tags", visibleTags, "tag") : "")
    // 「我的分支」开着却没有命中任何分支时如实说明，不留一个只有 HEAD 行的空树。
    + (mine && local.length === 0 && remote.length === 0
      ? `<p class="commit-meta">${escapeHtml(live.myBranchesLoading ? "Computing My Branches…" : (live.myBranchesReason || `No branches owned only by ${live.myBranchesAuthor || "you"} branches`))}</p>`
      : ""));
}

/**
 * 分支面板设置弹层（权威 `Git.Log.Branches.Settings`，组名 "Branches Pane Settings"、
 * 第一段分隔文案 "On Single Click"，`intellij.vcs.git.backend.xml:142-153`、`GitBundle.properties:705-706`）。
 *
 * 条目按权威顺序：更新分支筛选（单击时筛选日志）／导航到分支头（单击时定位）／分隔／按目录分组／
 * 按仓库分组／显示标签（`action.git.branches.show.tags.text`，`DumbAwareToggleAction`，默认 true）。
 * 「单击时」两项是**互斥的**行为开关（`SelectionHandlingModeAction`，`BranchesDashboardActions.kt:474-496`，
 * 默认都不生效），状态落在 `live.logRefSelectionAction`（`null`／`"filter"`／`"navigate"`）；
 * 分组两项需要前缀分组与多仓库，故仍禁用并写明原因。
 */
function refPaneSettingsMenu() {
  const live = window.__augitLive || {};
  const showTags = live.logRefShowTags !== false;
  // 「按目录分组」默认**开启**（权威 `DvcsBranchSettings.groupingKeyIds` 的默认值就是 `directory`）。
  const groupByDirectory = live.logRefGroupByDirectory !== false;
  const selectionAction = live.logRefSelectionAction || null;
  const disabled = (label, reason) => `<span class="menu-item disabled" aria-disabled="true" title="${escapeHtml(reason)}">${icon("none")} ${label}</span>`;
  // 互斥的单选项：选中一个即清掉另一个（权威把两个布尔属性互斥地写回日志 UI 属性）。
  const singleClick = (key, label) => `<a class="menu-item" href="#" role="menuitemradio" aria-checked="${selectionAction === key ? "true" : "false"}" data-ref-selection-action="${key}"><span class="fake-check${selectionAction === key ? " checked" : ""}"></span> ${label}</a>`;
  return `<section class="popover context-menu ref-settings-menu" aria-label="Branches Pane Settings">`
    + `<div class="menu-item menu-group-label"><strong>On click</strong></div>`
    + singleClick("filter", "Update Branch Filter")
    + singleClick("navigate", "Navigate to Branch Head")
    + `<div class="menu-separator"></div>`
    + `<a class="menu-item" href="#" role="menuitemcheckbox" aria-checked="${groupByDirectory ? "true" : "false"}" data-ref-setting="group-by-directory"><span class="fake-check${groupByDirectory ? " checked" : ""}"></span> Group by Directory</a>`
    + disabled("Group by Repository", "Augit opens one repository per window.")
    + `<a class="menu-item" href="#" role="menuitemcheckbox" aria-checked="${showTags ? "true" : "false"}" data-ref-setting="show-tags"><span class="fake-check${showTags ? " checked" : ""}"></span> Show Tags</a>`
    + `</section>`;
}

/**
 * 「分支」筛选控件的弹层（权威 `BranchFilterPopupComponent`，`platform/vcs-log/impl/src/com/intellij/vcs/log/ui/filter/BranchFilterPopupComponent.java`：
 * `MultipleValueFilterPopupComponent` 的复选列表 ＋ `vcs.log.filter.all` = "All" 的清除项，
 * 值文本取 `BranchFilterModel.getFilterPresentation()`）。
 *
 * Augit 的引用树目前是**单选**（多选登记在 `10-backlog.md` §三·前 第 6 项①），
 * 因此这里是单选项列表：全部／HEAD（当前分支）／本地／远程；当前生效的分支打勾。
 * `HEAD` 这一项照权威：`getBranchLikeFilterParameters()` 的候选名里永远含 `GitUtil.HEAD`
 * （`GitLogProvider.kt:545-548`）。
 */
function historyBranchFilterMenu() {
  const live = window.__augitLive || {};
  const branch = (live.historyFilter && live.historyFilter.branch) || "";
  const references = live.references || { branches: [] };
  const branches = references.branches || [];
  const item = (value, label) => `<a class="menu-item" href="#" role="menuitemradio" aria-checked="${branch === value ? "true" : "false"}" data-history-branch="${escapeHtml(value)}"><span class="fake-check${branch === value ? " checked" : ""}"></span> ${escapeHtml(label)}</a>`;
  const group = (title, rows) => rows.length === 0 ? "" : `<div class="menu-item menu-group-label"><strong>${title}</strong></div>` + rows.join("");
  const local = branches.filter((entry) => !entry.isRemote).map((entry) => item(entry.name, entry.name));
  const remote = branches.filter((entry) => entry.isRemote).map((entry) => item(entry.name, entry.name));
  return `<section class="popover context-menu history-branch-menu" aria-label="Filter by Branch">`
    + item("", "All")
    + `<div class="menu-separator"></div>`
    + item("HEAD", "HEAD (Current Branch)")
    + group("Local", local)
    + group("Remote", remote)
    + `</section>`;
}

function refTreeShell(inner) {
  const live = window.__augitLive || {};
  return `<div class="log-ref-panel"><div class="log-filterbar"><label class="history-search">${icon("search")}<input class="search-field" placeholder="Branch or tag" aria-label="Branch or tag" value="${escapeHtml(live.logRefFilter || "")}"></label></div><div class="tree" role="tree" aria-label="Branch or tag">${inner || '<p class="commit-meta">No references</p>'}</div></div>`;
}

/** 引用树选中后**就地**刷新选中态与竖条的启停，不整块重绘（权威的 `update()` 也是按选择刷状态）。 */
window.__augitApplyRefSelection = () => {
  const live = window.__augitLive || {};
  const selections = Array.isArray(live.logRefSelection) ? live.logRefSelection.filter((item) => item && item.name) : [];
  document.querySelectorAll(".log-ref-panel .tree-row[data-ref-name]").forEach((node) => {
    const selected = selections.some((item) => item.name === node.dataset.refName && item.kind === node.dataset.refKind);
    node.classList.toggle("selected", selected);
    node.setAttribute("aria-selected", selected ? "true" : "false");
  });
  const state = refStripeState();
  document.querySelectorAll(".git-side-toolbar [data-ref-stripe]").forEach((button) => {
    const item = state[button.dataset.refStripe];
    if (!item) return;
    button.disabled = !item.enabled;
    // 就地刷新也要更新**文案与按下态**：`DeleteBranchAction` 的文案随选中集变化、
    // 「我的分支」是 ToggleAction —— 只在整块重绘时更新会让这两者停留在旧值（第 183 轮实测）。
    const text = item.label || button.getAttribute("aria-label");
    if (item.label) {
      button.setAttribute("aria-label", item.label);
    }
    const pressed = item.checked === true;
    if (pressed) {
      button.setAttribute("aria-pressed", "true");
      button.classList.add("selected");
    } else {
      button.removeAttribute("aria-pressed");
      button.classList.remove("selected");
    }
    if (item.enabled) {
      button.removeAttribute("aria-disabled");
      button.title = pressed ? `${text}(enabled)` : text;
    } else {
      button.setAttribute("aria-disabled", "true");
      button.title = item.reason;
    }
  });
};

/**
 * 引用树的折叠与"分支或标签"搜索共用一次就地刷新：
 *   - 折叠：`live.logRefCollapsed` 记住被折叠的**折叠键**（类型分组＝组名，前缀分组＝`类型/路径`），
 *     组内行 `hidden`；组头的箭头与 `aria-expanded` 同步；
 *   - 搜索：按引用名匹配（HEAD 行恒保留），**过滤期间忽略折叠**（权威 `FilteringTree.java:137-139`
 *     "pattern 非空即 `expandAll`"）；清空后回到各自的折叠状态。
 *
 * 每一行都带 `data-ref-ancestors`（它所属的**全部**可折叠祖先键，从外到内），
 * 因此一层的折叠能连带隐藏所有更深层内容，不必按兄弟节点推算层级（按目录分组落地前是那样做的）。
 */
window.__augitApplyRefTreeFilter = (value) => {
  const live = window.__augitLive || {};
  if (value !== undefined) live.logRefFilter = value;
  const needle = String(live.logRefFilter || "").trim().toLowerCase();
  const collapsed = live.logRefCollapsed || {};
  const keysOf = (node) => String(node.dataset.refAncestors || "").split("|").filter(Boolean);
  const hiddenByCollapse = (node) => keysOf(node).some((key) => collapsed[key]);
  const panel = document.querySelector(".log-ref-panel");
  if (!panel) return;
  panel.querySelectorAll(".tree-row[data-ref-name]").forEach((node) => {
    if (node.dataset.refKind === "head") { node.hidden = false; return; }
    if (needle) node.hidden = !node.dataset.refName.toLowerCase().includes(needle);
    else node.hidden = hiddenByCollapse(node);
  });
  // 分组行从**内层往外**算：内层先定 `hidden`，外层的"有没有可见后代"才准。
  const groupRows = [...panel.querySelectorAll(".tree-row.group-row")].reverse();
  for (const node of groupRows) {
    const key = node.dataset.refCollapseKey;
    const isCollapsed = !!collapsed[key] && !needle;
    node.classList.toggle("collapsed", isCollapsed);
    node.setAttribute("aria-expanded", isCollapsed ? "false" : "true");
    const chevron = node.querySelector("span");
    if (chevron) chevron.innerHTML = icon(isCollapsed ? "chevron-right" : "chevron-down");
    if (needle) {
      // 过滤期间按"还有没有可见的后代"决定这一层是否出现；`data-ref-ancestors` 里含本键的即后代。
      node.hidden = ![...panel.querySelectorAll(".tree-row[data-ref-name], .tree-row.group-row")]
        .some((other) => other !== node && keysOf(other).includes(key) && !other.hidden && other.dataset.refCollapseKey !== key);
    } else {
      node.hidden = hiddenByCollapse(node);
    }
  }
};

/** 竖条的「全部展开／全部折叠」（权威 `ExpandAllAction`／`CollapseAllAction`）：作用于**所有**分组层级。 */
window.__augitSetRefTreeExpanded = (expanded) => {
  const live = window.__augitLive || {};
  const labels = [...document.querySelectorAll(".log-ref-panel .tree-row.group-row")]
    .map((n) => n.dataset.refCollapseKey)
    .filter(Boolean);
  live.logRefCollapsed = expanded ? {} : Object.fromEntries(labels.map((label) => [label, true]));
  window.__augitApplyRefTreeFilter();
  return labels.length;
};

/** 筛选控件上的值文本：权威 `MultipleValueFilterPopupComponent.getText()` 截到 20 字符
 *  （`MAX_FILTER_VALUE_LENGTH = 20`，`MultipleValueFilterPopupComponent.java:41,88-90`）。 */
function truncateFilterValue(value) {
  const text = String(value || "");
  return text.length > 20 ? `${text.slice(0, 19)}…` : text;
}

/**
 * 日志筛选栏的四个筛选控件（权威 `VcsLogClassicFilterUi.createActionGroup()`：分支／用户／日期／路径／
 * 图选项；`platform/vcs-log/impl/src/com/intellij/vcs/log/ui/filter/VcsLogClassicFilterUi.kt:145-151`）。
 *
 * 每个控件是本仓库里 `com.intellij.util.ui.FilterComponent` 的实例（`platform/vcs-log/impl/.../FilterComponent.java`）：
 *   - `DrawLabelMode.ALWAYS` ⇒ 文本是"名称"（未设值时）或"名称: 值"（设了值时）；
 *   - 右侧按钮的图标**未设值时是向下箭头 `AllIcons.General.ArrowDown`，设了值换成关闭叉 `AllIcons.Actions.Close`**；
 *   - 点关闭叉（或键盘 `Delete`）复位该筛选，点其余部分打开弹层（`createResetAction()`、
 *     `showPopupMenuFromKeyboardAndClearOnDelete()`、`FilterComponent.java:96-200`）。
 *
 * Augit 侧目前只有「分支」有真实筛选（引用树双击/回车，或本控件弹层；宿主 `git/history` 的 `branch`）。
 * 其余三项**禁用并写明原因**，而不是保留"改占位符"那种假交互。
 */
/**
 * 筛选栏收纳菜单里的同四项（权威在窄宽度下把筛选控件收进 `ActionToolbar` 的溢出菜单，
 * 条目仍带各自的可用状态与当前值）。禁用项同样写明原因。
 */
/**
 * 用户筛选在控件上的值文本（权威 `MultipleValueFilterPopupComponent` 的值显示 = 已选用户列表）。
 */
function historyAuthorFilterText(filter) {
  const values = filter && Array.isArray(filter.authors) ? filter.authors : [];
  return values.length === 0 ? "" : values.join(", ");
}

/**
 * 用户筛选弹层（权威 `UserFilterPopupComponent` = `MultipleValueFilterPopupComponent` 的**复选列表**
 * ＋ 全选/全不选，值进 `VcsLogFilterObject.fromUserNames(values)`，复选框一变就 `setFilter`）。
 * 列表来自宿主 `git/authors`（历史里出现过的人）；勾选即应用。
 * **登记差异**：Augit 暂未做搜索框与显式「Filter」按钮（列表直接应用）。
 */
function historyUserFilterMenu() {
  const live = window.__augitLive || {};
  const authors = live.historyAuthors || [];
  const selected = new Set(((live.historyFilter || {}).authors) || []);
  const keyOf = (author) => author.email || author.name;
  // 权威 `MultipleValueFilterPopupComponent` 的列表顶部有**搜索框**：按名字/邮箱过滤列表行。
  const needle = String(live.historyUserFilter || "").trim().toLowerCase();
  const row = (author) => {
    const key = keyOf(author);
    const checked = selected.has(key);
    const text = `${author.name || key} <${author.email || ""}>`;
    const hidden = !!needle && !text.toLowerCase().includes(needle);
    return `<a class="menu-item" href="#" role="menuitemcheckbox"${hidden ? " hidden" : ""} aria-checked="${checked ? "true" : "false"}" data-history-user="${escapeHtml(key)}"><span class="fake-check${checked ? " checked" : ""}"></span> ${escapeHtml(author.name || key)} &lt;${escapeHtml(author.email || "")}&gt;</a>`;
  };
  const all = authors.length > 0 && authors.every((author) => selected.has(keyOf(author)));
  return `<section class="popover context-menu history-user-menu" aria-label="Filter by User">`
    + `<label class="history-search">${icon("search")}<input class="search-field" placeholder="Search users" aria-label="Search users" value="${escapeHtml(live.historyUserFilter || "")}"></label>`
    + `<a class="menu-item" href="#" data-history-user-all="true"><span class="fake-check${all ? " checked" : ""}"></span> Select All</a>`
    + `<a class="menu-item" href="#" data-history-user-none="true">${icon("none")} Select None</a>`
    + `<div class="menu-separator"></div>`
    + (authors.length === 0
      ? `<span class="menu-item disabled" aria-disabled="true">${icon("none")} ${live.historyAuthorsLoading ? "Loading authors…" : "No users available"}</span>`
      : authors.map(row).join(""))
    + `</section>`;
}

/**
 * 路径筛选在控件上的值文本（权威 `StructureFilterPopupComponent.getText()`，
 * `platform/vcs-log/impl/src/com/intellij/vcs/log/ui/filter/StructureFilterPopupComponent.java:96-137`）：
 * 路径按 `FILE_PATH_BY_PATH_COMPARATOR`（`getPresentableUrl()`，同文件 `:296-302`）排序后取第一条，
 * 用 `StringUtil.shortenPathWithEllipsis(path, FILTER_LABEL_LENGTH)` 缩到 30 个字符
 * （`StructureFilterPopupComponent.java:63` 的 `FILTER_LABEL_LENGTH = 30`；
 * `platform/util/src/com/intellij/openapi/util/text/StringUtil.java:2841-2848` ⇒
 * 保留前 `30 - 21 - 3 = 6` 个字符 ＋ `"..."` ＋ 末 21 个字符，其中 21 = `(int)(30 * 0.7)`）；
 * 多于一条时是「第一条 + N」（`firstFileName + " + " + (files.size() - 1)`，同文件 `:123-136`）。
 */
function shortenPathValue(value, maxLength) {
  const text = String(value || "");
  if (text.length <= maxLength) return text;
  const suffixLength = Math.trunc(maxLength * 0.7);
  return `${text.slice(0, maxLength - suffixLength - 3)}...${text.slice(text.length - suffixLength)}`;
}

function historyPathFilterText(filter) {
  const paths = (((filter && filter.paths) || [])).map((value) => String(value || "")).filter(Boolean);
  if (paths.length === 0) return "";
  const first = paths.slice().sort()[0];
  const text = shortenPathValue(first, 30);
  return paths.length === 1 ? text : `${text} + ${paths.length - 1}`;
}

/**
 * 路径筛选的悬停提示（权威 `StructureFilterPopupComponent.getToolTip()` 的 `getTooltipTextForFilePaths`
 * ＋ `vcs.log.filter.tooltip.folders` = "Paths:"）：排序后最多列 10 条，多出的用 `...` 收尾
 * （同文件 `:147-166`）。Augit 单根 ⇒ 没有「根」那一段（`vcs.log.filter.tooltip.roots`）。
 */
function historyPathFilterTooltip(filter) {
  const paths = (((filter && filter.paths) || [])).map((value) => String(value || "")).filter(Boolean).sort();
  if (paths.length === 0) return "";
  const head = paths.slice(0, 10);
  return `Path: ${head.join(" ")}${paths.length > 10 ? " ..." : ""}`;
}

/**
 * 路径筛选弹层（权威 `StructureFilterPopupComponent.createActionGroup()`，同文件 `:170-205`）：
 * 「选择…」= `EditPathsAction`（文本 `vcs.log.filter.edit.folders` = "Select…"，用一个多行文本框
 * `MultilinePopupBuilder` 收一组按 `\n` 分隔的路径，`Ctrl+Enter` 收尾）、
 * 「在树中选择…」= `SelectPathsInTreeAction`（文本 `vcs.log.filter.select.folders` = "Select in Tree…"，
 * 打开 `VcsStructureChooser`，标题 `vcs.log.select.folder.dialog.title` = "Select Paths to Filter by"）、
 * 分隔条「最近」(`vcs.log.filter.recent`) ＋ 最近用过的路径筛选（`SelectFromHistoryAction`：
 * 文本用同一个 `getTextFromFilePaths`，勾选态 = 与当前筛选相等，`KeepPopupOnPerform.Never` ⇒ 点完即关）。
 *
 * **登记差异**：① 权威只在 `myColorManager.hasMultiplePaths()` 为真时列「根」分组
 * （`SelectVisibleRootAction`，`Ctrl+点击` = 只看这个根），Augit 是单根仓库 ⇒ 不做根分组；
 * ② 权威把最近筛选写进 `MainVcsLogUiProperties.getRecentlyFilteredGroups("Paths")`（设置持久化，
 * 由 `myUiProperties.addRecentlyFilteredGroup(PATHS, …)` 维护），Augit 只在本次会话内保留
 * —— 不新增 Augit 没有的持久化设置项。
 */
function historyPathFilterMenu() {
  const live = window.__augitLive || {};
  const selected = (((live.historyFilter || {}).paths) || []).map(String);
  const recent = Array.isArray(live.historyPathRecent) ? live.historyPathRecent : [];
  const samePaths = (paths) => paths.length === selected.length && paths.every((path) => selected.includes(path));
  const recentRow = (paths, index) => {
    const checked = samePaths(paths);
    return `<a class="menu-item" href="#" role="menuitemcheckbox" aria-checked="${checked ? "true" : "false"}"`
      + ` title="${escapeHtml(paths.join(" "))}" data-log-path-recent="${index}">`
      + `<span class="fake-check${checked ? " checked" : ""}"></span> ${escapeHtml(historyPathFilterText({ paths }))}</a>`;
  };
  return `<section class="popover context-menu history-path-menu" aria-label="Filter by Path">`
    + `<a class="menu-item" href="#" data-log-path-action="select">${icon("none")} Select…</a>`
    + `<a class="menu-item" href="#" data-log-path-action="tree">${icon("none")} Select in Tree…</a>`
    + `<div class="menu-separator"></div>`
    + `<span class="menu-item disabled" role="presentation" aria-disabled="true">Recent</span>`
    + (recent.length === 0
      ? `<span class="menu-item disabled" aria-disabled="true">No recent path filters</span>`
      : recent.map((paths, index) => recentRow(paths.map(String), index)).join(""))
    + `</section>`;
}

/**
 * 日期筛选在控件上的值文本（权威 `DateFilterPopupComponent.getText()`，
 * `platform/vcs-log/impl/src/com/intellij/vcs/log/ui/filter/DateFilterPopupComponent.java:26-42`）：
 * 两端 ⇒ `"<after>-<before>"`；只有起点 ⇒ `vcs.log.date.filter.since` = "Since {0}"；
 * 只有终点 ⇒ `vcs.log.date.filter.until` = "Until {0}"；都没有 ⇒ 空（控件显示名称）。
 */
function historyDateFilterText(filter) {
  const short = (value) => {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return String(value || "");
    const pad = (number) => String(number).padStart(2, "0");
    return `${date.getFullYear()}/${pad(date.getMonth() + 1)}/${pad(date.getDate())}`;
  };
  const since = filter && filter.since ? short(filter.since) : "";
  const until = filter && filter.until ? short(filter.until) : "";
  if (since && until) return `${since}-${until}`;
  if (since) return `Since ${since}`;
  if (until) return `Until ${until}`;
  return "";
}

/**
 * 日期筛选弹层（权威 `DateFilterPopupComponent.createActionGroup()`，同文件 `:54-62`）：只有三项 ——
 * `SelectAction`（`vcs.log.filter.action.select` = "Select…"，打开 `DateFilterComponent` 的期间对话框）、
 * `DateAction(now-1d, "Last 24 hours")`、`DateAction(now-7d, "Last 7 days")`；
 * 两者都只是 `fromDates(since, null)`。清空走控件右侧的关闭叉（`FilterComponent` 的复位），
 * 弹层里**没有** "All" 项。
 * 「选择期间…」打开 `DateFilterComponent` 的期间对话框（见 `openHistoryDateRangeDialog()`）。
 */
function historyDateFilterMenu() {
  return `<section class="popover context-menu history-date-menu" aria-label="Filter by Date">`
    + `<a class="menu-item" href="#" data-history-date="select">Select…</a>`
    + `<a class="menu-item" href="#" data-history-date="last-day">Last 24 hours</a>`
    + `<a class="menu-item" href="#" data-history-date="last-week">Last 7 days</a>`
    + `</section>`;
}

function historyFilterOverflowItems() {
  const live = window.__augitLive || {};
  const active = live.historyFilter || {};
  const entries = [
    { key: "branch", label: "Branch", value: active.branch || "" },
    { key: "user", label: "User", value: historyAuthorFilterText(active) },
    { key: "date", label: "Date", value: historyDateFilterText(active) },
    { key: "path", label: "Path", value: historyPathFilterText(active), tooltip: historyPathFilterTooltip(active), truncate: false },
  ];
  return entries.map((entry, index) => {
    const selected = entry.value !== "";
    // 路径的值文本已经按权威自己的长度规则缩过（`FILTER_LABEL_LENGTH` = 30，中间省略），
    // 不再套用户/分支弹层的 `MAX_FILTER_VALUE_LENGTH` = 20（`entry.truncate === false`）。
    const text = selected
      ? `${entry.label}: ${entry.truncate === false ? entry.value : truncateFilterValue(entry.value)}`
      : entry.label;
    const disabled = !!entry.reason;
    const title = disabled ? entry.reason : selected
      ? `Clear${entry.label}filter${entry.tooltip ? ` · ${entry.tooltip}` : ""}`
      : `${entry.label}Filter`;
    return `<button type="button" data-history-filter="${index}" data-filter-key="${entry.key}"`
      + `${disabled ? ` disabled aria-disabled="true" title="${escapeHtml(entry.reason)}"` : ` title="${escapeHtml(title)}"`}>`
      + `${icon(selected ? "x" : "search")}${escapeHtml(text)}</button>`;
  }).join("");
}

function historyFilterButtons() {
  const live = window.__augitLive || {};
  const active = live.historyFilter || {};
  const entries = [
    { key: "branch", label: "Branch", value: active.branch || "" },
    { key: "user", label: "User", value: historyAuthorFilterText(active) },
    { key: "date", label: "Date", value: historyDateFilterText(active) },
    { key: "path", label: "Path", value: historyPathFilterText(active), tooltip: historyPathFilterTooltip(active), truncate: false },
  ];
  return entries.map((entry, index) => {
    const selected = entry.value !== "";
    // 路径的值文本已经按权威自己的长度规则缩过（`FILTER_LABEL_LENGTH` = 30，中间省略），
    // 不再套用户/分支弹层的 `MAX_FILTER_VALUE_LENGTH` = 20（`entry.truncate === false`）。
    const text = selected
      ? `${entry.label}: ${entry.truncate === false ? entry.value : truncateFilterValue(entry.value)}`
      : entry.label;
    const disabled = !!entry.reason;
    const title = disabled ? entry.reason : selected
      ? `Clear${entry.label}filter${entry.tooltip ? ` · ${entry.tooltip}` : ""}`
      : `${entry.label}Filter`;
    return `<button class="toolbar-button history-filter${selected ? " selected" : ""}" type="button"`
      + ` data-history-filter="${index}" data-filter-key="${entry.key}" aria-label="${escapeHtml(entry.label)}"`
      + ` title="${escapeHtml(title)}"${disabled ? ' aria-disabled="true" disabled' : ""}>`
      + `<span>${escapeHtml(text)}</span>${icon(selected ? "x" : "chevron-down")}</button>`;
  }).join("");
}

function liveGitLog(history, selected, cancelComparison, loading = false) {
  // 提交详情允许被区域刷新重建，因此渲染要读状态里的内容而不是只依赖 DOM 里写过的内容。
  const live = window.__augitLive || {};
  const commits = history.commits.map(commit => [commit.subject, (commit.references || []).join(" ") , commit.author, commit.date]);
  const entries = history.commits.map(commit => ({
    hash: commit.hash,
    fullHash: commit.fullHash,
    head: commit.fullHash === history.head,
    parents: commit.parents || [],
    references: commit.references || [],
  }));
  const fullByShort = new Map(history.commits.map(commit => [commit.hash, commit.fullHash]));
  for (const entry of entries) {
    entry.parents = (entry.parents || []).map(parent => fullByShort.get(parent) || parent);
  }

  const graph = buildCommitGraph(entries, { cacheKey: JSON.stringify([live.root, history.head, live.references || null]) });
  const currentBranchCommits = currentBranchCommitSet(entries);
  const filters = ["Branch", "User", "Date", "Path"];
  const branchLabel = (commit) => (commit.references || []).filter(name => name !== "HEAD").join(" ");
  // 无历史文案（规格 §10.1）：保留引用树与筛选栏，只替换提交列表内容。
  const emptyRow = `<div class="empty-tool-state"><div><strong>No commits in this repository</strong><p>Commits will appear here.</p></div></div>`;
  // 选中的提交由状态决定（规格 §7.9 的"提交选择"要跨重绘保留）：有记录就按哈希选中，
  // 没有（首次渲染）才退回"选中第一行"。此前模板写死 `index === 0`，任何整页重绘都会把选择丢回第一行。
  const historySelectedHash = (window.__augitLive && window.__augitLive.historySelectedHash) || "";
  const isCommitSelected = (commit, index) => historySelectedHash ? commit.hash === historySelectedHash : index === 0;
  // 历史未到达时的占位：不能复用"仓库还没有提交"（那是在断言一个我们尚未知道的事实）。
  const loadingRow = `<div class="empty-tool-state" role="status"><div><strong>Loading commit history…</strong><p>Reading commits from Git.</p></div></div>`;
  // 空态分两种（权威 `VcsLogBundle.properties:148,151,153`：`vcs.log.no.commits.status` = "No changes committed."、
  // `vcs.log.no.commits.matching.status` = "No commits matching filters"、`vcs.log.reset.filters.status.action` = "Reset filters"）：
  // 「仓库本身没有提交」与「**筛选**把结果筛空了」不是同一件事 —— 后者不能显示成仓库是空的，且要能就地清掉筛选。
  const filterEmptyRow = `<div class="empty-tool-state"><div><strong>No commits match the filters</strong><p>Adjust or clear the filters.</p><button type="button" class="secondary-button" data-history-action="reset-filters">Reset Filters</button></div></div>`;
  const branchesCollapsed = !!(live && live.logBranchesCollapsed);
  return `<section class="bottom-tool">
    <div class="bottom-header"><span class="bottom-title">Git</span><button class="tool-tab active">Log</button><span class="grow"></span>${cancelComparison ? "" : ""}<button class="icon-button">${icon("ellipsis-vertical")}</button><button class="icon-button">${icon("minus")}</button></div>
    <div class="git-toolbar-layout">
      ${gitLogStripe(!!(live && live.logBranchesCollapsed))}
      <div class="git-log${branchesCollapsed ? " branches-collapsed" : ""}">
        ${branchesCollapsed ? "" : liveRefTree()}
        <div class="log-list-panel">
          <div class="log-filterbar history-filters">
            <label class="history-search"><button type="button" class="history-search-trigger" data-history-text-menu aria-label="Recent Text Searches" title="Recent Text Searches" aria-haspopup="menu" aria-expanded="false">${icon("history-search")}</button><input class="search-field" placeholder="Text or hash" aria-label="Text or hash" value="${escapeHtml((window.__augitLive && window.__augitLive.historyFilterDraft) || "")}"></label>
            ${historyFilterButtons()}
            <details class="history-filter-overflow" hidden><summary class="toolbar-button" aria-label="More History Filters">${icon("chevron-right")}</summary><div class="history-filter-menu">${historyFilterOverflowItems()}</div></details>
            <span class="grow"></span><button class="toolbar-button history-utility" aria-label="Show Commit Details">${icon("eye")}</button><button class="toolbar-button history-utility" aria-label="Search Commits">${icon("search")}</button><button class="toolbar-button history-utility" aria-label="Refresh">${icon("refresh-cw")}</button>
          </div>
<div class="commit-list commit-list-graph" ${commitGraphListAttributes(graph)}>${loading ? loadingRow : history.commits.length === 0 ? (history.filterActive ? filterEmptyRow : emptyRow) : history.commits.map((commit, index) => `<div class="commit-row ${isCommitSelected(commit, index) ? "selected" : ""}${currentBranchCommits.has(commit.hash) ? " current-branch" : ""}" role="option" aria-selected="${isCommitSelected(commit, index)}" data-hash="${escapeHtml(commit.hash)}" data-full-hash="${escapeHtml(commit.fullHash)}">${commitGraphSvg(graph, index)}<span class="commit-subject">${escapeHtml(commit.subject)}</span><span class="branch-label">${logReferenceLabels(commit.references, commit.fullHash === history.head)}</span><span class="commit-meta commit-author">${escapeHtml(commit.author)}</span><time class="commit-meta commit-date" data-full="${escapeHtml(commit.date)}" data-compact="${escapeHtml(commit.date.slice(5, 10))}">${escapeHtml(commit.date)}</time></div>`).join("")}</div>
        </div>
        <div class="log-detail-panel"><div class="changed-files" data-live-changed-files>${live && live.commitDetails && history.commits.length > 0 ? live.commitDetails.filesHtml : (loading ? `<p class="commit-meta">Loading changes…</p>` : history.commits.length === 0 ? `<div class="empty-state">No changes to display</div>` : `<p class="commit-meta">Loading changes…</p>`)}</div><div class="commit-detail" data-live-commit-detail>${live && live.commitDetails && live.commitDetails.detailHtml ? live.commitDetails.detailHtml : `<h3>${history.commits.length === 0 ? "Commit Details" : escapeHtml(history.commits[0].subject)}</h3><div>${history.commits.length === 0 ? "" : `${escapeHtml(history.commits[0].hash)} · ${escapeHtml(history.commits[0].author)} · ${escapeHtml(history.commits[0].date)}`}</div>`}</div></div>
      </div>
    </div>
  </section>`;
}

function gitLog(selected = true, complexGraph = false, cancelComparison = false, emptyHistory = false) {
  // 空历史视觉稿（规格 §10.1）：直接复用实时侧的同一渲染器与同一段文案，
  // 避免"样例页自己写一份空态"与真实实现漂移（两侧标记同源才能做像素对照）。
  if (emptyHistory && !(window.__augitLive && window.__augitLive.history)) {
    return liveGitLog({ commits: [], head: null }, selected, cancelComparison, false);
  }
  const liveHistory = window.__augitLive && window.__augitLive.history;
  // 复杂泳道图同样使用真实历史：结构化行已带父子关系，
  // 泳道由 buildCommitGraph 推导，不需要另用样例数据。
  if (liveHistory) return liveGitLog(liveHistory, selected, cancelComparison);
  // live 已存在但历史未到达：不得退回样例提交（会显示工作区里不存在的提交）。
  // 但仍需走完整的 liveGitLog 结构：绑定器依赖其中的工具栏、筛选栏与详情区，
  // 手写精简骨架会让绑定器拿到 null（实测报 querySelectorAll of null）。
  if (window.__augitLive) return liveGitLog({ commits: [], head: null }, selected, cancelComparison, true);
  let commits = complexGraph ? [
    // 首个提交即当前分支 HEAD：引用串按 git --decorate 的真实形态给出，展示时过滤掉 HEAD 与箭头。
    ["merge: 合并历史界面调整", "HEAD -> main", "I49", "2026/8/29 11:00"],
    ["fix: 修正工具栏图标", "", "I49", "2026/8/29 10:00"],
    ["feat: 调整历史筛选布局", "feature/graph", "I49", "2026/8/29 9:00"],
    ["fix: 精确恢复安装前系统 PATH", "", "I49", "2026/8/28 22:55"],
    ["fix: 避免强制更新兼容的 .NET 10", "", "I49", "2026/8/28 21:32"],
    ["fix: 提升安装卸载与 Git 取消可靠性", "", "I49", "2026/8/28 21:25"],
    ["fix: 补充原生 Windows 应用清单", "", "I49", "2026/8/28 8:45"],
    ["feat: 实现 Augit 阶段零至五功能", "", "I49", "2026/8/28 8:25"],
  ] : [
    // 首个提交即当前分支 HEAD（参考图里该行有浅蓝底 + main 标签）。
    ["fix: 精确恢复安装前系统 PATH", "HEAD -> main", "I49", "2026/8/28 22:55"],
    ["fix: 避免强制更新兼容的 .NET 10", "", "I49", "2026/8/28 21:32"],
    ["fix: 提升安装卸载与 Git 取消可靠性", "", "I49", "2026/8/28 21:25"],
    ["fix: 补充原生 Windows 应用清单", "", "I49", "2026/8/28 8:45"],
    ["feat: 实现 Augit 阶段零至五功能", "", "I49", "2026/8/28 8:25"],
  ];
  let entries = commits.map((item, index) => ({ hash: `commit-${index}`, head: index === 0,
    parents: index < commits.length - 1 ? [`commit-${index + 1}`] : [], references: item[1] ? item[1].split(" ") : [] }));
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
  const currentBranchCommits = currentBranchCommitSet(entries);
  const filters = ["Branch", "User", "Date", "Path"];
  const branchRows = wideGraph
    ? `${Array.from({ length: 11 }, (_, index) => `<a class="tree-row depth-1" href="branches.html">${gitReferenceIcon()} feature/branch-${index + 1}</a>`).join("")}<a class="tree-row depth-1 selected" href="branches.html">${gitReferenceIcon()} main</a>`
    : complexGraph
    ? `<a class="tree-row depth-1" href="branches.html">${gitReferenceIcon()} feature/graph</a><a class="tree-row depth-1 selected" href="branches.html">${gitReferenceIcon()} main</a>`
    : `<a class="tree-row depth-1 selected" href="branches.html">${gitReferenceIcon()} main</a>`;
  const detailTitle = commits[1]?.[0] ?? "Commit Details";
  const detailMeta = complexGraph ? "cb20f43 · I49 · 2026/8/29 10:00" : "dfe5c25a · I49 · 2026/8/28 21:32";
  const detailBody = complexGraph ? "" : `<p class="commit-meta">安装器检测到兼容运行时后保持当前环境，不重复更新系统组件。</p>`;
  return `<section class="bottom-tool">
    <div class="bottom-header"><span class="bottom-title">Git</span><button class="tool-tab active">Log</button><span class="grow"></span>${cancelComparison ? '<a class="toolbar-button" href="history-diff-cancelled.html">Cancel Comparison</a>' : ""}<button class="icon-button">${icon("ellipsis-vertical")}</button><button class="icon-button">${icon("minus")}</button></div>
    <div class="git-toolbar-layout">
      ${gitLogStripe(false)}
      <div class="git-log">
        <div class="log-ref-panel"><div class="log-filterbar"><label class="history-search">${icon("search")}<input class="search-field" placeholder="Branch or tag" aria-label="Branch or tag"></label></div><div class="tree"><div class="tree-row">HEAD（Current branch）</div><div class="tree-row"><span>${icon("chevron-down")}</span><strong>Local</strong></div>${branchRows}</div></div>
        <div class="log-list-panel">
          <div class="log-filterbar history-filters">
            <label class="history-search"><button type="button" class="history-search-trigger" data-history-text-menu aria-label="Recent Text Searches" title="Recent Text Searches">${icon("history-search")}</button><input class="search-field" placeholder="Text or hash" aria-label="Text or hash"></label>
            ${historyFilterButtons()}
            <details class="history-filter-overflow" hidden><summary class="toolbar-button" aria-label="More History Filters">${icon("chevron-right")}</summary><div class="history-filter-menu">${historyFilterOverflowItems()}</div></details>
            <span class="grow"></span><button class="toolbar-button history-utility" aria-label="Show Commit Details">${icon("eye")}</button><button class="toolbar-button history-utility" aria-label="Search Commits">${icon("search")}</button><button class="toolbar-button history-utility" aria-label="Refresh">${icon("refresh-cw")}</button>
          </div>
<div class="commit-list commit-list-graph" ${commitGraphListAttributes(graph)}>${commits.map((item, index) => `<div class="commit-row ${selected && index === 1 ? "selected" : ""}${currentBranchCommits.has(entries[index].hash) ? " current-branch" : ""}" role="option" aria-selected="${selected && index === 1}" data-hash="${entries[index].hash}">${commitGraphSvg(graph, index)}<span class="commit-subject">${item[0]}</span><span class="branch-label">${logReferenceLabels(Array.isArray(item[1]) ? item[1] : item[1].split(" "))}</span><span class="commit-meta commit-author">${item[2]}</span><time class="commit-meta commit-date" data-full="${item[3]}" data-compact="${item[3].includes('8/29') ? '08-29' : '08-28'}">${item[3]}</time></div>`).join("")}</div>
        </div>
        <div class="log-detail-panel"><div class="changed-files">${selected ? complexGraph ? '<div class="tree-row">0 files</div>' : `<div class="tree-row"><span>${icon("chevron-down")}</span><span>${treeFolderIcon()}</span><strong>6 files</strong></div><div class="tree-row depth-1"><span>${icon("chevron-down")}</span><span>${treeFolderIcon()}</span> docs <span class="commit-meta">4 files</span></div>${["architecture.md", "performance-report.md", "roadmap.md", "runtime-dependencies.md"].map(name => `<div class="tree-row depth-2 file-status-modified" data-history-path="docs/${name}">${fileTypeIcon(name)}${name}</div>`).join("")}` : `<div class="empty-state">Select a commit to view changes</div>`}</div><div class="commit-detail">${selected ? `<h3>${detailTitle}</h3><div>${detailMeta}</div>${detailBody}` : `<div class="empty-state">Commit Details</div>`}</div></div>
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
  return historyFilesHtml(historySampleFiles(subject));
}

// 提交详情的变更文件树：视觉稿样例与实时外壳的真实 Git 数据共用这一份实现。
// 实时层此前自己拼了一版扁平列表（类名还是无人定义的 live-file-status-*），
// 没有目录分组、没有折叠箭头、也没有状态色，与视觉稿的树形结构完全不同。
// `ui` 是调用方（实时层）持有的**状态**：`{ collapsed: { 组键: true }, selectedPath }`。
// 折叠与选中此前只写在 DOM 上（箭头纯装饰、选中类名点完就丢），任何区域重绘都会把用户的选择
// 抹掉（规格 §7.9 条目三要求"变化文件树"的选择/折叠/顶部位置在重绘与往返后保持）。
// 树本身仍是状态预渲染出的 HTML，因此这里必须能**按状态**重建：组行的 `data-history-group`
// 是折叠状态的键（与项目树行同一个约定），叶行按 `selectedPath` 回填 `selected`。
function historyFilesHtml(files, ui) {
  const state = ui || {};
  const collapsed = state.collapsed || {};
  const selectedPath = state.selectedPath || null;
  const render = (items, prefix = "", depth = 1, hidden = false) => {
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
    // 缩进用视觉稿的 depth-N 类（mockup.css: .tree-row.depth-1..4 = 20/38/56/74px），
    // 不再写内联 padding——内联会盖住样式表，量出来的缩进与视觉稿相差 6 像素。
    return [...groups].sort(([a], [b]) => a.localeCompare(b)).map(([name, children]) => {
      const folded = !!collapsed[`${prefix}${name}`];
      // 祖先折叠时子树整体隐藏：`hidden` 属性逐个行加，不引入包裹容器
      //（包裹会改掉既有的扁平层级与按 `.changed-files > .tree-row` 量出来的结构）。
      return `<div class="tree-row depth-${depth}${folded ? " collapsed" : ""}" data-history-group="${escapeHtml(`${prefix}${name}`)}" aria-expanded="${folded ? "false" : "true"}"${hidden ? " hidden" : ""}>`
        + `<span>${icon(folded ? "chevron-right" : "chevron-down")}</span><span>${treeFolderIcon()}</span>${name}<span class="commit-meta">${children.length} files</span></div>`
        + render(children, `${prefix}${name}/`, depth + 1, hidden || folded);
    }).join("")
      + leaves.sort((a, b) => a.path.localeCompare(b.path)).map(file => {
        const label = `${file.path.slice(prefix.length)}${file.original ? ` ← ${file.original.split("/").at(-1)}` : ""}`;
        const selected = file.path === selectedPath ? " selected" : "";
        return `<div class="tree-row depth-${depth} file-status-${file.status}${selected}" data-history-path="${escapeHtml(file.path)}" aria-label="${escapeHtml(changeAccessibleName(file.status, label))}"${hidden ? " hidden" : ""}>${fileTypeIcon(file.path)}${label}</div>`;
      }).join("");
  };
  // 根行同样可折叠（键为空串），与组行共用一套点击处理。
  const rootFolded = !!collapsed[""];
  return `<div class="tree-row${rootFolded ? " collapsed" : ""}" data-history-group="" aria-expanded="${rootFolded ? "false" : "true"}"><span>${icon(rootFolded ? "chevron-right" : "chevron-down")}</span><span>${treeFolderIcon()}</span><strong>${files.length} files</strong></div>${render(files, "", 1, rootFolded)}`;
}

// 文件历史列表的四列表头。权威 `FileHistoryPanelImpl.createColumnList`（platform/vcs-impl/src/com/intellij/
// openapi/vcs/history/FileHistoryPanelImpl.java:292-306）按 **Version → Date → Author → [provider 附加列] →
// Commit Message** 依次追加，Git 的 `isDateOmittable()` = false（plugins/git4idea/backend/src/history/
// GitHistoryProvider.java:75-78）故日期列在；同一顺序另有 `VcsSelectionHistoryDialog.java:182-185` 佐证。
// 列名取各 `ColumnInfo.getName()`：`column.name.revision.version` / `column.name.revision.date` /
// `column.name.revision.list.author` / `label.selected.revision.commit.message`
// （platform/vcs-api/vcs-api-core/resources/messages/VcsBundle.properties:160、161、111、159），
// 本处按界面语言写中文。表头由 `DualView(ColumnInfo[])` 建表时自带（FileHistoryPanelImpl.java:184-200），
// 其高度参与布局计算（platform/platform-api/src/com/intellij/ui/dualView/DualView.java:462），
// 默认高度 25（platform/platform-impl/src/com/intellij/ide/ui/laf/darcula/DarculaTableHeaderUI.java:115）。
function fileHistoryColumns() {
  return `<div class="history-columns"><span>Revision</span><span>Date</span><span>Author</span><span>Commit message</span></div>`;
}

// 文件历史列表作者列的单元格。权威 `FileHistoryPanelImpl.AuthorColumnInfo`：
// 值 = `revision.getAuthor()`，**当 `author != committerName` 时追加 `*`**
//（`valueOf`，platform/vcs-impl/src/com/intellij/openapi/vcs/history/FileHistoryPanelImpl.java:780-788）；
// 单元格 tooltip = `{作者} <{邮箱}>`，提交者不同名时再追加
// `, via {提交者} <{提交者邮箱}>`（`getCustomizedRenderer`，`:764-778`；
// `file.history.details.committer.tooltip.info` = "via {0}"，VcsBundle.properties:935）。
// 字段缺失一律当空串：真实 Git 记录里提交者恒存在，空串只是"宿主没给"的退化形态。
function historyAuthorCell(commit) {
  const author = commit.author || "";
  const authorEmail = commit.authorEmail || "";
  const committerName = commit.committerName || "";
  const committerEmail = commit.committerEmail || "";
  const different = committerName !== author;
  let tooltip = author + (authorEmail ? ` <${authorEmail}>` : "");
  if (different && committerName) {
    tooltip += `, via ${committerName}` + (committerEmail ? ` <${committerEmail}>` : "");
  }
  return `<span title="${escapeHtml(tooltip)}">${escapeHtml(different ? author + "*" : author)}</span>`;
}

// 外壳注入真实文件历史时使用；结构与样例版一致，复用同一套样式。
function liveFileHistoryTool() {
  const fileHistory = window.__augitLive.fileHistory;
  const commits = fileHistory.commits || [];
  // 选择进状态（规格 §7.9 第二条「选择…继续使用同一提交身份」）：此前行上没有任何选中状态、
  // 右侧详情恒取 `commits[0]` —— 换选提交后详情不跟随，区域重绘也留不住选择。
  // 状态键 `fileHistory.selectedFull` 默认落在第一条（与视觉稿「首行选中」一致）。
  const selectedFull = fileHistory.selectedFull || (commits[0] && commits[0].fullHash) || null;
  const selected = commits.find((commit) => commit.fullHash === selectedFull) || commits[0] || null;
  // 「版本」列取短修订：权威 `RevisionColumnInfo` 的值过 `VcsUtil.getShortRevisionString`
  // （FileHistoryPanelImpl.java:657-666、platform/vcs-api/src/com/intellij/vcsUtil/VcsUtil.java:402-406）。
  const rows = commits.length === 0
    ? `<p class="commit-meta">No history</p>`
    : commits.map((commit) => {
      // 行的角色与提交标注沿用日志列表那一套（`role="option"` + `aria-selected` + 短/完整哈希）；
      // `data-history-full` 同时是选择与"打开比较"的身份键（规格 §7.9：始终用完整提交哈希）。
      const active = commit.fullHash === selectedFull;
      return `<div class="history-row${active ? " selected" : ""}" role="option" aria-selected="${active ? "true" : "false"}" data-history-hash="${escapeHtml(commit.hash)}" data-history-full="${escapeHtml(commit.fullHash)}"><span>${escapeHtml(commit.hash)}</span><span title="${escapeHtml(commit.dateTime || commit.date)}">${escapeHtml(commit.date)}</span>${historyAuthorCell(commit)}<span>${escapeHtml(commit.subject)}</span></div>`;
    }).join("");
  const head = selected;
  // 右侧详情：规格 §7.9 第六条要求这里是**完整只读比较视图**（工具栏九项、文件栏父版本·提交版本·路径、
  // 与引用比较同构的正文）。预览状态还没建立时（首帧）退回提交信息，避免闪出一个空面板。
  const pane = window.__augitLive.fileHistoryPreview
    ? `<div class="history-detail-pane" data-live-file-history-pane="preview">${liveFileHistoryPreviewView()}</div>`
    : `<div class="history-detail-pane" data-live-file-history-pane="commit"><div class="commit-detail" data-live-file-history-detail>${head ? `<h3>${escapeHtml(head.subject)}</h3><div>${escapeHtml(head.hash)} · ${escapeHtml(head.author)} · ${escapeHtml(head.date)}</div>` : `<p class="commit-meta">Select a commit to view changes</p>`}</div></div>`;
  // 「显示/隐藏提交详情」（规格 §7.9 第七条）：隐藏时容器收成单列（否则被隐藏的那一列仍占
  // `clamp(360px, 42%, 520px)`，右侧留一条空白带）。状态进 `live.fileHistoryDetailsHidden`，
  // 区域重绘按状态回填；标签/`aria-pressed` 沿用日志详情那一套既有语义（隐藏时标签是"隐藏提交详情"）。
  const detailsHidden = !!window.__augitLive.fileHistoryDetailsHidden;
  const eyeLabel = detailsHidden ? "Hide Commit Details" : "Show Commit Details";
  return `<section class="bottom-tool"><div class="bottom-header"><span class="bottom-title">Git</span><a class="tool-tab" href="git-history.html">Log</a><button class="tool-tab active">History: ${escapeHtml(fileHistory.path || "")}</button><span class="grow"></span><button class="icon-button">${icon("ellipsis-vertical")}</button><button class="icon-button">${icon("minus")}</button></div><div class="history-tool-content" data-details-hidden="${detailsHidden ? "true" : "false"}"><div class="history-list-pane"><div class="history-toolbar"><span>Branch: HEAD</span><button class="icon-button">${icon("x")}</button><span class="toolbar-separator"></span><button class="icon-button">${icon("refresh-cw")}</button><button class="icon-button">${icon("git-compare-arrows")}</button><button class="icon-button">${icon("history-expand")}</button><button class="icon-button" aria-label="${eyeLabel}" aria-pressed="${detailsHidden ? "true" : "false"}">${icon("eye")}</button></div>${fileHistoryColumns()}<div class="history-rows">${rows}</div></div>${pane}</div></section>`;
}

/**
 * 「与当前分支比较」的日志视图（权威 `ShowBranchDiffAction` → `GitBrancher.compare` →
 * `GitBranchesUIHandler.compareWithCurrent`）。
 *
 * 权威打开的是**按范围过滤的日志**，不是文件差异：`compareWithCurrent()` 取
 * `currentRef = repositories.getCommonCurrentBranch() ?: GitUtil.HEAD`，再
 * `GitCompareBranchesUi(project, repositories, branchName, currentRef)`，后者用
 * `VcsLogFilterObject.fromRange(currentRef, branchName)`（`GitCompareBranchesUi.kt:44-52`）
 * 建 `VcsLogRangeFilter`；范围文本取 `VcsLogRangeFilterImpl.getTextPresentation()` =
 * `"<exclusive>..<inclusive>"`，标签名取 `getEditorTabName()`（同文件 `:177-181`）：
 * `git.compare.branches.tab.name` = "Compare"、`git.compare.branches.tab.suffix` = "{0} and {1}"
 * （参数是 `(end=inclusive, start=exclusive)`，
 * `plugins/git4idea/shared/resources/messages/GitBundle.properties:1313-1314`）。
 * 该范围过滤器在权威里**不可从界面修改**（`getRange()` 的 check 写明 "changing it from the UI is disabled"），
 * 因此这里只显示范围文本，不提供筛选控件。
 *
 * 登记差异：权威把它开在**编辑器标签**里（`VcsLogFile` ＋ 完整 `VcsLogUiImpl`，含提交图与筛选栏）；
 * Augit 的日志本体在底部工具窗口，因此做成它的**兄弟标签**（与「历史: <文件>」同构），
 * 列表沿用同一套 `history-columns`／`history-rows`。
 */
function liveBranchCompareTool() {
  const compare = window.__augitLive.branchComparison || {};
  const commits = compare.commits || [];
  const rows = commits.length === 0
    ? `<p class="commit-meta">${compare.loading ? "Loading commits to compare…"
      : compare.cancelled ? "Comparison cancelled."
        : escapeHtml(compare.reason || "No exclusive commits between these references")}</p>`
    : commits.map((commit, index) => `<div class="history-row ${index === 0 ? "selected" : ""}" data-history-hash="${escapeHtml(commit.hash)}"><span>${escapeHtml(commit.hash)}</span><span>${escapeHtml(commit.date)}</span>${historyAuthorCell(commit)}<span>${escapeHtml(commit.subject)}</span></div>`).join("");
  const head = commits[0];
  const base = compare.base || "HEAD";
  const branch = compare.branch || "HEAD";
  // 规格 §7.9 第二十条：历史面板发起引用查询时**在自身区域**显示"取消比较"（不是全局"正在生成 diff"提示），
  // 查询完成后隐藏该入口。
  const cancelEntry = compare.loading
    ? '<button class="toolbar-button" data-branch-compare-cancel="true" aria-label="Cancel Comparison">Cancel Comparison</button>'
    : "";
  return `<section class="bottom-tool"><div class="bottom-header"><span class="bottom-title">Git</span><a class="tool-tab" href="git-history.html">Log</a><button class="tool-tab active">Compare: ${escapeHtml(branch)} with ${escapeHtml(base)}</button><span class="grow"></span>${cancelEntry}<button class="icon-button" aria-label="Close Comparison" data-branch-compare-close="true">${icon("x")}</button><button class="icon-button">${icon("ellipsis-vertical")}</button><button class="icon-button">${icon("minus")}</button></div><div class="history-tool-content"><div class="history-list-pane"><div class="history-toolbar"><span>${escapeHtml(`${base}..${branch}`)}</span><span class="toolbar-separator"></span><span class="commit-meta">${commits.length} commits</span><span class="grow"></span><button class="icon-button" aria-label="Refresh" data-branch-compare-refresh="true">${icon("refresh-cw")}</button><button class="icon-button" aria-label="Show Commit Details">${icon("eye")}</button></div>${fileHistoryColumns()}<div class="history-rows">${rows}</div></div><div class="history-detail-pane"><div class="commit-detail">${head ? `<h3>${escapeHtml(head.subject)}</h3><div>${escapeHtml(head.hash)} · ${escapeHtml(head.author)} · ${escapeHtml(head.date)}</div>` : `<p class="commit-meta">Select a commit to view changes</p>`}</div></div></div></section>`;
}

function fileHistoryTool() {
  // 样例行的四个槽位与实时侧同序：版本（该提交在样例日志里显示的哈希）→ 日期 → 作者 → 提交信息。
  // 第二行演示作者列的 `*`：作者与提交者不同名（权威 `AuthorColumnInfo.valueOf`），
  // 悬停提示因此带上 `, via {提交者}`。
  const sampleRows = [
    { hash: "commit-4", date: "2026/8/28 8:25", subject: "feat: 实现 Augit 阶段零至五功能", author: "I49", authorEmail: "i49@example.com", committerName: "I49", committerEmail: "i49@example.com" },
    { hash: "commit-3", date: "2026/8/27 19:02", subject: "docs: 补阶段五验收记录", author: "I49", authorEmail: "i49@example.com", committerName: "build-bot", committerEmail: "build@example.invalid" },
  ];
  const rows = sampleRows.map((commit, index) => `<div class="history-row${index === 0 ? " selected" : ""}"><span>${escapeHtml(commit.hash)}</span><span>${escapeHtml(commit.date)}</span>${historyAuthorCell(commit)}<span>${escapeHtml(commit.subject)}</span></div>`).join("");
  return `<section class="bottom-tool"><div class="bottom-header"><span class="bottom-title">Git</span><a class="tool-tab" href="git-history.html">Log</a><button class="tool-tab active">History: product-spec.md</button><span class="grow"></span><button class="icon-button">${icon("ellipsis-vertical")}</button><button class="icon-button">${icon("minus")}</button></div><div class="history-tool-content"><div class="history-list-pane"><div class="history-toolbar"><span>Branch: HEAD</span><button class="icon-button">${icon("x")}</button><span class="toolbar-separator"></span><button class="icon-button">${icon("refresh-cw")}</button><button class="icon-button">${icon("git-compare-arrows")}</button><button class="icon-button">${icon("history-expand")}</button><button class="icon-button">${icon("eye")}</button></div>${fileHistoryColumns()}<div class="history-rows">${rows}</div></div><div class="history-detail-pane">${diffView(true, new URLSearchParams(location.search).get("history-state") || "ready", false, true)}</div></div></section>`;
}

function terminalTool() {
  const loading = new URLSearchParams(location.search).get("terminal-state") === "loading";
  const shell = "Windows PowerShell";
  const name = `Local${loading ? " · Starting…" : ""}`;
  const output = loading ? "" : `<div><span class="terminal-prompt">PS </span><span class="terminal-path">D:\\github\\Augit</span>&gt; <span class="terminal-command">dotnet test Augit.slnx -c Release</span></div><div>Passed! - Failed: 0, Passed: 183, Skipped: 0, Total: 183</div><div><span class="terminal-prompt">PS </span><span class="terminal-path">D:\\github\\Augit</span>&gt; <span class="terminal-command">git status --short</span></div><div class="file-status-modified"> M docs/product-spec.md</div><div class="file-status-modified"> M src/Augit.App/NativeGitPanel.cs</div><div><span class="terminal-prompt">PS </span><span class="terminal-path">D:\\github\\Augit</span>&gt; <span class="terminal-command">_</span></div>`;
  return `<section class="bottom-tool terminal-tool"><div class="bottom-header terminal-header"><span class="bottom-title terminal-title">Terminal</span><span class="terminal-session" title="${shell}${loading ? " · Starting…" : ""}" aria-label="Local terminal: ${shell}">${name}</span><button class="icon-button terminal-session-close" aria-label="Close Terminal">${icon("close-small")}</button><button class="icon-button terminal-more" aria-label="More Actions">${icon("ellipsis-vertical")}</button><button class="icon-button terminal-hide" aria-label="Hide Terminal">${icon("minus")}</button></div><div class="terminal-view" aria-busy="${loading}">${output}</div></section>`;
}

// IntelliJ 通知图标（24×24 源文件，`platform/icons/src/general/notification*.svg`）：
// 错误 #DB5860 实心圆+感叹号、信息 #389FD6 实心圆+i、警告 #EDA200 三角+感叹号。
// `VcsNotifier.notifySuccess` 使用 `NotificationType.INFORMATION`，所以成功结果也用信息图标。
const TOAST_ICON_PATHS = {
  error: '<path fill="#DB5860" fill-rule="evenodd" d="M12,22 C6.4771525,22 2,17.5228475 2,12 C2,6.4771525 6.4771525,2 12,2 C17.5228475,2 22,6.4771525 22,12 C22,17.5228475 17.5228475,22 12,22 Z M10.5,5 L10.5,13 L13.5,13 L13.5,5 L10.5,5 Z M10.5,15 L10.5,18 L13.5,18 L13.5,15 L10.5,15 Z"/>',
  success: '<path fill="#389FD6" fill-rule="evenodd" d="M12,22 C6.4771525,22 2,17.5228475 2,12 C2,6.4771525 6.4771525,2 12,2 C17.5228475,2 22,6.4771525 22,12 C22,17.5228475 17.5228475,22 12,22 Z M10.5,10 L10.5,18 L13.5,18 L13.5,10 L10.5,10 Z M10.5,5 L10.5,8 L13.5,8 L13.5,5 L10.5,5 Z"/>',
  warning: '<path fill="#EDA200" fill-rule="evenodd" d="M12,2 L23,21 L1,21 L12,2 Z M13.5,19 L13.5,16 L10.5,16 L10.5,19 L13.5,19 Z M13.5,14 L13.5,8 L10.5,8 L10.5,14 L13.5,14 Z"/>',
};

/** 通知图标；未登记的 kind 不画图标（保持既有无类型提示的外观）。 */
function toastNotificationIcon(kind) {
  const path = TOAST_ICON_PATHS[kind];
  if (!path) return "";
  const label = kind === "error" ? "Error" : kind === "warning" ? "Warning" : "Success";
  return `<span class="toast-icon" role="img" aria-label="${label}"><svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">${path}</svg></span>`;
}

/**
 * 通知卡片（参考 balloon 形态，2026-10-20 按 PyCharm 2026.2.1 失败 balloon 实测）：
 * 白底、8px 圆角、16px 类型图标、粗体标题、正文、蓝色文字动作。
 * 提示仍由状态驱动；这里只统一标记，不引入独立“结果页”。
 */
function toastMarkup({ title, text = "", kind = "", action = null, extra = "" }) {
  const known = Object.hasOwn(TOAST_ICON_PATHS, kind) ? kind : "";
  const classes = ["toast", known ? `toast-${known}` : "", known === "error" ? "error" : ""].filter(Boolean).join(" ");
  const actionHtml = action && action.label
    ? (action.href
      ? `<a class="toast-action" href="${escapeHtml(action.href)}">${escapeHtml(action.label)}</a>`
      : `<button type="button" class="toast-action" data-toast-action="${escapeHtml(action.id || "")}">${escapeHtml(action.label)}</button>`)
    : "";
  return `<div class="${classes}" role="alert">${toastNotificationIcon(known)}<div class="toast-content"><div class="toast-title">${escapeHtml(title)}</div>${text ? `<div class="toast-text">${escapeHtml(text)}</div>` : ""}${extra}${actionHtml}</div></div>`;
}

/**
 * 规格 §10.2：提示由**状态**驱动渲染。
 *
 * 直接往窗口里 appendChild 会被下一次区域刷新抹掉（与之前修过的 diff 加载提示同类），
 * 而且同一时刻只允许存在一条提示。内容放在 `live.toast`，这里只负责画出来。
 */
function liveToast() {
  const live = window.__augitLive || {};
  const toast = live.toast;
  if (!toast || !toast.title) return "";
  return toastMarkup({
    title: toast.title,
    text: toast.text || "",
    kind: toast.kind || "",
    action: toast.action || null,
  });
}

/**
 * 普通文件读取尚未完成时的正文占位（规格 §6.7）。
 *
 * 用既有的 `.empty-tool-state`／`role="status"` 结构，与"正在读取改动…／正在读取提交历史…"同一族；
 * 关键是**不能**落到 `editor === "empty"` 的"选择文件以查看内容"无文档提示上。
 */
function documentLoadingView(path) {
  const text = typeof path === "string" ? path : "";
  const name = text.split("/").at(-1) || text;
  return `<div class="empty-tool-state" role="status"><div><strong>Loading ${escapeHtml(name)}…</strong><p>${escapeHtml(text)}</p></div></div>`;
}

function shell({ activeRail = "project", side = "project", editor = "markdown", bottom = "", overlay = "", toast = "", selectedFile = "product-spec.md", complexGraph = false, comparisonState = "ready", workspaceComparison = false, diffBoundary = false, emptyHistory = false, diffStatus = null, imageError = false, gitUnavailable = "" } = {}) {
  const live = window.__augitLive || null;
  // 实时外壳下，Git 不可用的原因**只**来自宿主检测（`showGitUnavailable`）：live 存在但没有原因，
  // 就是"Git 可用"，不能退回场景参数（否则 `?scene=git-unavailable` 在 Git 可用时也会禁用入口）。
  // 视觉稿单独打开（无 live）时才用参数给出同一条原因，使静态基线与实时降级页一致（ux-spec §7.18）。
  const railGitReason = live
    ? (typeof live.gitUnavailableReason === "string" ? live.gitUnavailableReason : "")
    : gitUnavailable;
  // 实时外壳下，工具窗口由用户操作驱动（规格 §5.1）：场景只提供初始布局，
  // 之后以 live.layout 为准。视觉稿单独打开时没有 live，行为完全不变，
  // 因此逐场景静态浏览与既有一致性不受影响。
  // 只有在用户实际操作过工具窗口之后才覆盖场景值。
  // 场景可以省略 activeRail/side/bottom 而依赖 shell() 的默认参数，
  // 若一开始就用 DOM 读出的值覆盖，会把场景依赖的默认值抹掉。
  if (live && live.layout && live.layout.userDriven) {
    activeRail = live.layout.activeRail || "";
    side = (live.layout.sideHidden ?? live.layout.collapsed === "side") ? "" : live.layout.side;
    bottom = (live.layout.bottomHidden ?? live.layout.collapsed === "bottom") ? "" : live.layout.bottom;
  }

  // 可见区域与活动归属分别保存；左侧和底部可同时显示，活动入口由 activeRail 决定。
  const activeSide = side;
  const activeBottom = bottom;

  // 实时外壳下由用户操作打开的弹层覆盖场景自带的那个（规格 §5.3）。
  // 只在确有实时弹层时覆盖，否则保留场景值，逐场景静态浏览与既有场景行为不变。
  // 实时搜索浮层（Ctrl+P／Ctrl+Shift+F）也必须走这条渲染路径：它的结果写在 `live.search` 上，
  // 区域刷新要靠 `renderScene()` 产出替换节点。此前只把节点手动挂在窗口上，而 `main-project`
  // 一类场景的模板里根本没有 overlay ⇒ `__augitRenderRegions('overlay')` 找不到替换源，
  // 结果永远画不出来（第 240 轮实测：`live.search.matches` 已 2 项、DOM 里没有 `.search-results`）。
  if (live && live.searchOpen && live.search) {
    overlay = liveSearchOverlay(live.search.kind === "repository" ? "repository" : "quick");
  } else if (live && live.overlay) {
    overlay = live.overlay;
  }

  // 比较标签（工作区 Diff、引用比较、历史比较）没有普通文档，但同样有明确视图：
  // 只看 live.document 会让比较正文渲染不出来（表现为 editor 已是 diff 而 DOM 仍是文档）。
  if (live && live.editor && (live.document || live.editor === "diff" || (live.editor === "blame" && live.blame))) editor = live.editor;
  // 树行选中态属于**用户**（规格 §6 第 37 条：异步读取收尾不得重选树行）：用户点过树行后以
  // `live.treeSelectedPath` 为准，只有还没点过时才回落到当前文档路径（启动恢复、直接打开文件）。
  // 此前这里直接用文档**文件名**，于是任何一次读取落地都会把树选中跳回刚打开的那一行
  //（第 270 轮实测：读取期间用户改选 `docs/product-spec.md`，收尾后选中又变回 `docs/notes.txt`）。
  if (live) {
    const treeTarget = live.treeSelectedPath ?? (live.document ? live.document.path : null);
    if (treeTarget !== null && treeTarget !== undefined) selectedFile = treeTarget;
    // 实时层的"就地套用"（`live-data.js` 的 `patchProjectTree()`）必须与渲染层用**同一个**选中判据，
    // 否则新插入的行会少掉默认选中（`main-project` 的场景默认是 `product-spec.md`）。这里把最终值
    // 公布出去，避免两边各写一份默认值后漂移（第 295 轮全量实测踩到）。
    window.__augitTreeSelection = selectedFile;
  }
  // 比较必须来自真实活动标签或已读取的差异，不能把场景的 editor="diff" 当作已打开文件。
  const liveComparison = live && live.editor === "diff" && (live.diff
    || (Array.isArray(live.tabs) && live.tabs.some(tab => tab.id === live.activeTabId && tab.kind === "comparison")));
  // 规格 §6.7：普通文件读取尚未完成时（典型是"读取在途时打开比较、随后关闭比较"返回的普通标签），
  // 正文显示该文件的**读取占位**，不能退回"选择文件以查看内容"的无文档提示。
  if (live && live.pendingDocument && !live.document && !liveComparison) {
    editor = "document-loading";
  }
  // 所有实时场景都依据实际文档状态：空会话、读取失败或关闭最后标签不能显示视觉稿样例。
  // 冲突弹层也不提供普通文件正文；无宿主视觉稿仍由场景参数呈现样例。
  if (live && !live.document && !live.blame && !live.pendingDocument && !liveComparison) {
    editor = "empty";
  }
  // 折叠状态（side 为空）不渲染侧栏，把整块宽度还给编辑区。
  const sideHtml = side === "" ? "" : side === "commit-empty"
    ? emptyChangesSide()
    : side === "commit"
      ? (live
        ? (live.status
          ? liveChangesSide(editor === "diff" || editor === "diff-loading" ? "app.manifest" : "")
          // live 已存在但状态未到：不得退化成样例数据（会显示工作区里不存在的文件）。
          : loadingChangesSide())
        : changesSide(editor === "diff" || editor === "diff-loading" ? "app.manifest" : ""))
      : projectTree(selectedFile, live && side === "project" ? live : null);
  let editorExtra = "";
  let editorBody = markdownView();
  if (editor === "text") editorBody = textView(false);
  if (editor === "text-find") editorBody = textView(true);
  if (editor === "json") editorBody = jsonView();
  if (editor === "image" && liveDocument()) editorBody = liveImageDocument();
  else if (editor === "image") editorBody = `<div class="document-view"><div class="document-toolbar image-toolbar"><button class="icon-button" aria-label="Zoom Out">${icon("zoom-out")}</button><span class="image-zoom-label">100%</span><button class="icon-button" aria-label="Zoom In">${icon("zoom-in")}</button><button class="icon-button" aria-label="Fit to View">${icon("image-fit")}</button><span class="image-size-label" title="1920 × 1200 · PNG · 52.5 KB"><span class="image-size-content">1920 × 1200 · PNG · 52.5 KB</span></span></div><div class="image-stage" tabindex="0" aria-label="Read-only image"><img src="assets/image-sample.png" alt="带透明边缘的山景样图" draggable="false"></div></div>`;
  if (editor === "file-limit" && liveDocument()) editorBody = liveUnavailableDocument();
  // 图片解码失败（宿主 ImageDecodeFailed / ImageTooLarge）：与实时侧 liveUnavailableDocument()
  // 同构，只有原因那行不同。此前视觉稿只覆盖了"GIF/WebP 不支持"，解码失败没有基线。
  else if (editor === "file-limit" && imageError) editorBody = `<div class="info-state"><div class="info-block"><span style="color:var(--augit-warning)">${icon("file-warning")}</span><h2>This file cannot be previewed in Augit</h2><p>corrupt.png · PNG image · 2.0 KB</p><p>D:\\github\\Augit\\docs\\assets\\corrupt.png</p><p>Cannot read image dimensions; preview stopped.</p><div class="button-row" style="justify-content:center"><button class="secondary-button">Open in Default Application</button></div></div></div>`;
  else if (editor === "file-limit") editorBody = `<div class="info-state"><div class="info-block"><span style="color:var(--augit-warning)">${icon("file-warning")}</span><h2>This file cannot be previewed in Augit</h2><p>animation.webp · WebP image · 4.8 MB</p><p>D:\\github\\Augit\\docs\\assets\\animation.webp</p><p>GIF, WebP, and other binary image formats are not supported.</p><div class="button-row" style="justify-content:center"><button class="secondary-button">Open in Default Application</button></div></div></div>`;
  if (editor === "blame") editorBody = (live && live.blame) ? liveBlameView() : blameView();
  // 规格 §6.7 的读取占位（见上方 editor 覆盖处）。
  if (editor === "document-loading") editorBody = documentLoadingView(live && live.pendingDocument);
  if (editor === "diff") {
    // 标签优先用真实差异路径；差异尚未到达时用启动参数里的目标路径，
    // 避免短暂显示样例文件名。
    const diffPath = (live && live.diff && live.diff.path)
      || (new URLSearchParams(location.search).get("diff"))
      || "app.manifest";
    editorExtra = `<a class="editor-tab active" href="#" data-workspace-diff-tab="true">${icon("git-compare-arrows")} <span class="change-tab-caption">Commit: ${escapeHtml(diffPath)}</span><button type="button" class="tab-close" aria-label="Close Comparison">${icon("close-small")}</button></a>`;
    editorBody = live ? liveDiffView() : diffView(false, "ready", false, false, undefined, null, diffStatus);
    // `diff-boundary` 场景的**样例**提示只属于静态视觉稿：实时外壳的提示由
    // `live-data.js` 的 `applyDiffBoundaryHint()` 按 `live.diffBoundaryHint` 状态插入，
    // 否则实时页面会凭空显示一条"再次点击可进入下一个文件"（第 251 轮实测：启动后
    // `live.diffBoundaryHint` 为 null，DOM 里却有一个提示节点，直到第一次交互才被清掉）。
    if (diffBoundary && !live) {
      editorBody = editorBody.replace('<div class="diff-columns">', '<div class="diff-columns diff-boundary-columns"><div class="diff-boundary-hint" role="status">Click again to open the next file</div>');
    }
  }
  if (editor === "comparison") {
    editorExtra = `<a class="editor-tab active comparison-tab" href="git-compare.html">${icon("git-compare-arrows")}<span class="comparison-caption"><span class="comparison-file">Compare: app.manifest</span><span> · </span><span class="comparison-revision">${workspaceComparison ? "HEAD" : "dfe5c25a^"}</span><span> → </span><span class="comparison-revision">${workspaceComparison ? "Workspace" : "dfe5c25a"}</span></span><span class="tab-close" aria-label="Close Comparison">${icon("close-small")}</span></a>`;
    editorBody = diffView(true, comparisonState, workspaceComparison);
  }
  if (editor === "diff-loading") {
    editorExtra = `<a class="editor-tab active" href="diff-loading.html">${icon("git-compare-arrows")} Commit: app.manifest</a>`;
    editorBody = `<div class="diff-layout"><div class="diff-toolbar"><button class="toolbar-button" aria-label="Previous Difference">${icon("arrow-up")}</button><button class="toolbar-button" aria-label="Next Difference">${icon("arrow-down")}</button><span class="grow"></span>${diffLoadingProgress()}<div class="segmented"><button class="segment active" aria-label="Side-by-side">${icon("diff-side-by-side")}</button><button class="segment" aria-label="Unified">${icon("diff-unified")}</button></div></div>${diffFileHeader("HEAD", "Current Version", "HEAD → Current Version")}<div class="diff-columns diff-loading-columns"><div class="diff-loading-message" role="status">Loading...</div></div></div>`;
  }
  if (editor === "empty") editorBody = `<div class="empty-state">Select a file to view its content</div>`;
  // 实时外壳下标签条**一律**来自 `live.tabs`（规格 §5.2）。此前 `editor === "image"`／`"json"`
  // 两个分支渲染的是**视觉稿样例标签**：`href="image-preview.html"`（`web/` 下只有 `index.html`，
  // 点它会整页导航到 404），而且没有 `data-tab-id` ⇒ 关闭叉、中键关闭、点击激活全部失效
  //（第 243 轮实测：实时侧打开图片后，标签条上是一个没有 tab-id 的样例标签）。
  const staticTabs = editor === "image"
    ? `<div class="editor-tabs"><a class="editor-tab active" href="image-preview.html">${fileTypeIcon("image-sample.png")}<span class="json-tab-caption">image-sample.png</span><span class="tab-close" aria-label="Close File">${icon("close-small")}</span></a><span class="grow"></span><button class="icon-button" aria-label="Tab Options">${icon("ellipsis-vertical")}</button></div>`
    : editor === "json"
    ? `<div class="editor-tabs"><a class="editor-tab active" href="json-preview.html">${fileTypeIcon("global.json")}<span class="json-tab-caption">global.json</span><span class="tab-close" aria-label="Close File">${icon("close-small")}</span></a><span class="grow"></span><button class="icon-button" aria-label="Tab Options">${icon("ellipsis-vertical")}</button></div>`
    : scene === "go-to-line" || scene === "text-viewer"
    ? `<div class="editor-tabs"><a class="editor-tab active" href="text-viewer.html">${fileTypeIcon("MainWindow.cs")} MainWindow.cs<span class="tab-close" aria-label="Close File">${icon("close-small")}</span></a><span class="grow"></span><button class="icon-button" aria-label="Tab Options">${icon("ellipsis-vertical")}</button></div>`
    : editor === "diff" || editor === "diff-loading" || editor === "comparison"
    ? editorTabs("", editorExtra)
    : editorTabs(editor === "markdown" || editor === "blame" ? "product" : "third", editorExtra);
  let tabs = live ? editorTabs("", editorExtra) : staticTabs;
  if (editor === 'markdown') {
    const mode = editorBody.match(/data-markdown-mode="(source|split|preview)"/)?.[1] || 'preview';
    tabs = tabs.replace('<button class="icon-button" aria-label="Tab Options">', `${markdownTabActions(mode)}<button class="icon-button" aria-label="Tab Options">`);
  }
  const bottomHtml = bottom === "git" ? gitLog(true, complexGraph, comparisonState === "loading", emptyHistory) : bottom === "terminal" ? terminalTool() : bottom === "file-history" ? ((live && live.fileHistory) ? liveFileHistoryTool() : fileHistoryTool()) : bottom === "branch-compare" ? ((live && live.branchComparison) ? liveBranchCompareTool() : fileHistoryTool()) : "";
  const mainClasses = ["app-main", side ? "" : "side-collapsed", bottom ? "bottom-open" : ""].filter(Boolean).join(" ");
  return `<div class="augit-window">${titlebar()}<main class="${mainClasses}" data-side="${side}" data-bottom="${bottom}">${rail(activeSide, railGitReason, activeBottom, activeRail)}${sideHtml}<section class="workspace"><article class="editor-area">${tabs}<div class="editor-content">${editorBody}</div></article></section>${bottomHtml}</main>${statusBar(editor, selectedFile)}${overlay}<div class="toast-layer">${liveToast() || toast}</div></div>`;
}

// 状态栏描述活动视图；比较补丁不提供源文件编码与换行事实。
function statusBar(editor, selectedFile) {
  const live = liveDocument();
  if (live) {
    const fields = [];
    if (live.encoding) fields.push(live.encoding);
    if (live.lineEndings) fields.push(live.lineEndings);
    fields.push("Read-only");
    const location = [live.workspaceName || "Augit", ...String(live.path).split("/")].filter(Boolean).join("  ›  ");
    return `<footer class="statusbar" aria-label="File Status"><span class="status-path" title="${escapeHtml(live.fullPath || live.path)}">${escapeHtml(location)}</span><div class="status-fields">${fields.map(text => `<span>${escapeHtml(text)}</span>`).join('')}</div></footer>`;
  }

  const comparison = ["diff", "diff-loading", "comparison"].includes(editor);
  // 外壳存在但没有文档时，状态栏显示工作区路径（规格 §4.1），
  // 而不是视觉稿里写死的工作区名与路径。
  if (window.__augitLive && !liveDocument()) {
    const workspace = window.__augitLive;
    const rootPath = workspace.root || "";
    const folder = rootPath ? rootPath.replaceAll("\\", "/").split("/").filter(Boolean).at(-1) : null;
    const name = workspace.workspaceName || folder || "Workspace";
    const title = rootPath ? `${rootPath}\\${workspace.name || folder || ""}` : name;
    return `<footer class="statusbar" aria-label="File Status"><span class="status-path" title="${escapeHtml(title)}">${escapeHtml(name)}</span><div class="status-fields"></div></footer>`;
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
  const ending = ["LF", "CRLF", "CR", "Mixed line endings", "No line endings"].includes(requestedEnding) ? requestedEnding : "LF";
  const fields = isText ? ["UTF-8", ending, "Read-only"] : hasDocument ? ["Read-only"] : [];
  return `<footer class="statusbar" aria-label="File Status"><span class="status-path" title="${escapeHtml(fullPath)}">${escapeHtml(location)}</span><div class="status-fields">${fields.map(text => `<span>${text}</span>`).join('')}</div></footer>`;
}

// 外壳注入真实搜索结果时使用；结构与样例版一致（浮层、开关、结果行、提示）。
function liveSearchOverlay(kind) {
  const live = window.__augitLive || {};
  const search = live.search;
  const repository = kind === "repository";
  const header = repository
    ? `<strong>Find in Files</strong>${[['case-sensitive', 'Match case', 'matchCase'], ['whole-word', 'Words', 'matchWholeWord'], ['regex', 'Regex', 'useRegularExpression']].map(([name, label, key]) => `<button class="icon-button search-option" aria-label="${label}" title="${label}" aria-pressed="${search && search.options && search.options[key] ? 'true' : 'false'}">${icon(name)}</button>`).join('')}<label class="search-ignored"><input type="checkbox" aria-label="Include ignored files"${search && search.options && search.options.includeIgnoredFiles ? ' checked' : ''}>Include ignored files</label>`
    : `<strong>Open File</strong><span class="grow"></span><span class="menu-shortcut">Ctrl+P</span>`;
  const query = (search && search.query) || "";
  const matches = (search && search.matches) || [];
  const rows = matches.map((match, index) => repository
    ? `<a class="search-result${index === 0 ? ' selected' : ''}" href="#" data-search-path="${escapeHtml(match.path)}" data-search-line="${match.line}">${fileTypeIcon(match.name)}<span>${escapeHtml(match.name)} <span class="commit-meta">${escapeHtml(String(match.line))}: ${escapeHtml(match.text)}</span></span><span class="commit-meta">${escapeHtml(match.directory)}</span></a>`
    : `<a class="search-result${index === 0 ? ' selected' : ''}" href="#" data-search-path="${escapeHtml(match.path)}">${fileTypeIcon(match.name)}<span>${escapeHtml(match.name)}</span><span class="commit-meta">${escapeHtml(match.directory)}</span></a>`).join("");
  // 搜索无结果（规格 §10.1）：显示「未找到结果」，输入框与查询保持不动。
  // 仅在**确实查询过**（有查询串）且没有命中时显示，避免打开浮层就报「未找到」。
  // 查询进行中另有中间态：权威 `SearchEverywhereUI.rebuildList()`（platform/lang-impl/src/com/intellij/
  // ide/actions/searcheverywhere/SearchEverywhereUI.java:932-936）每次搜索开始先 `expireResults()`、
  // 把列表空态设为 `label.choosebyname.searching` = **"Searching…"**（IdeBundle.properties:675），
  // 结果到达才替换 ⇒ 上一个查询的结果不得留在界面上冒充本次结果。
  const pending = !!(search && search.pending);
  const results = matches.length > 0
    ? `<div class="search-results">${rows}</div>`
    : (pending
      ? `<div class="search-results"><div class="empty-tool-state"><div><strong>Searching…</strong></div></div></div>`
      : (query.length > 0 ? `<div class="search-results"><div class="empty-tool-state"><div><strong>No results found</strong><p>Try another query or adjust the filters.</p></div></div></div>` : ""));
  const noticeText = search && search.notice ? search.notice : "";
  const notice = noticeText
    ? `<div class="search-notice" role="status" tabindex="0">${escapeHtml(noticeText)}</div>`
    : "";
  // 结果到限：按权威弹「结果过多」（Continue／Abort）。对话框叠在搜索浮层之上，
  // 在用户选择之前不重复发起搜索（`search.limitPrompt` 由 live-data 设置与清除）。
  // `live-overlay` 是"由实时层打开、可以被 `closeLiveOverlay()` 关掉"的标记；两种渲染路径
  //（首次手动挂载与后续区域替换）都要带，否则 Esc 走不到统一的关闭分支、状态也不会清。
  return `<div class="search-overlay live-overlay ${repository ? 'repository-mode' : ''}" data-augit-overlay><div class="search-tabs">${header}</div><div class="search-query"><input class="search-field" value="${escapeHtml(query)}" aria-label="Search"></div>${results}${notice}</div>`;
}

function searchOverlay(kind) {
  const repository = kind === "repository";
  const searchHeader = repository
    ? `<strong>Find in Files</strong>${[['case-sensitive', 'Match case'], ['whole-word', 'Words'], ['regex', 'Regex']].map(([name, label]) => `<button class="icon-button search-option" aria-label="${label}" title="${label}" aria-pressed="false">${icon(name)}</button>`).join('')}<label class="search-ignored"><input type="checkbox" aria-label="Include ignored files">Include ignored files</label>`
    : `<strong>Open File</strong><span class="grow"></span><span class="menu-shortcut">Ctrl+P</span>`;
  const state = new URLSearchParams(location.search).get('search-state') || (scene === 'search-limited' ? 'limited' : 'ready');
  const notices = { ready: '3 results', limited: 'More than 1000 results. Continue searching or narrow the query.', timeout: 'Search timed out; completed results are preserved.', error: 'Invalid search expression. Check the parentheses and try again.\n'.repeat(12).trim(), empty: '' };
  const rows = repository ? [['NativeGitPanel.cs', 'src/Augit.App', '214: Git status refreshed.'], ['architecture.md', 'docs', 'Git status reflects local facts'], ['Win32SmokeTests.cs', 'tests', 'Assert.Contains("Git Status")']]
    : [['NativeGitPanel.cs', 'src/Augit.App'], ['NativeGitPanelRenderingTests.cs', 'tests/Augit.App.Tests'], ['product-spec.md', 'docs']];
  const results = state === 'empty' || state === 'error' ? '' : `<div class="search-results">${rows.map(([name, directory, match], index) => `<a class="search-result${index === 0 ? ' selected' : ''}" href="text-viewer.html">${fileTypeIcon(name)}<span>${name}${match ? ` <span class="commit-meta">${match}</span>` : ''}</span><span class="commit-meta">${directory}</span></a>`).join('')}</div>`;
  const notice = repository && notices[state] ? `<div class="search-notice" role="status" tabindex="0">${notices[state]}</div>` : '';
  return `<div class="search-overlay ${repository ? 'repository-mode' : ''}" data-augit-overlay><div class="search-tabs">${searchHeader}</div><div class="search-query"><input class="search-field" value="${state === 'empty' ? '' : repository ? 'Git Status' : 'NativeGitPanel'}" aria-label="Search"></div>${results}${notice}</div>`;
}

/**
 * 「结果过多」对话框（权威 `UsageLimitUtil.showTooManyUsagesWarning`，
 * `platform/usageView/src/com/intellij/usages/UsageLimitUtil.java:26-34`）：
 * 标题 `find.excessive.usages.title` = **"Too Many Results"**、
 * 正文 `find.excessive.usage.count.prompt` = **"Too many results found. Are you sure you wish to continue?"**、
 * 按钮 `button.text.continue` = Continue 与 `button.text.abort` = Abort（警告图标）。
 * `MessageDialogBuilder.okCancel(...)` 让 **Continue 成为默认按钮**（与 Smart Checkout 的默认焦点相反）。
 *
 * 触发点是搜索超过 `UsageLimitUtil.USAGES_LIMIT`（`ide.find.result.count.warning.limit`，默认 1000）；
 * 用户选 Continue 后权威让同一次搜索**继续跑完且不再提示**（`UsageViewManagerImpl:334-357`），
 * 选 Abort 则取消搜索并保留已有结果。
 */
function searchLimitBody() {
  return `<div class="info-block" style="width:auto;text-align:left"><h2>Too Many Results</h2>`
    + `<p>More than 1000 results found. Continue searching?</p></div>`;
}

function searchLimitDialog() {
  // 窗口级模态：与其它实时对话框同构，单独占一个覆盖层。搜索浮层的区域刷新只替换**第一个**
  // `[data-augit-overlay]`，这里的兄弟节点因此不会被搜到、也不会在"继续"后留下残影
  // （实时侧由 `openSearchLimitDialog()`/`closeSearchLimitDialog()` 显式增删）。
  return `<div class="overlay-layer live-overlay search-limit-window" data-augit-overlay><div class="scrim search-limit-scrim"></div>`
    + `<section class="dialog search-limit-dialog" role="dialog" aria-modal="true" aria-label="Too many results">`
    + `<div class="dialog-header"><span>Too many results</span></div>`
    + `<div class="dialog-body">${searchLimitBody()}</div>`
    + `<div class="dialog-footer"><span class="footer-help"></span>`
    + `<button type="button" class="secondary-button" data-search-limit-action="abort">Abort</button>`
    + `<button type="button" class="primary-button" data-search-limit-action="continue">Continue</button>`
    + `</div></section></div>`;
}

function dialog(title, body, footer, wide = false, extraClass = "") {
  // scrim 与对话框作为同一个覆盖层整体替换，避免局部刷新后残留其一。
  // 远端使用已批准的实机标题结构，品牌仍为Augit自身标记。
  const remote = extraClass.split(' ').some(name => name === 'remote-dialog' || name === 'remote-define-dialog');
  const heading = remote
    ? `<span class="brand-mark" aria-hidden="true">A</span><span class="remote-title">${title}</span>`
    : `<span>${title}</span><span class="grow"></span>`;
  return `<div class="overlay-layer" data-augit-overlay><div class="scrim"></div><section class="dialog ${wide ? "wide" : ""} ${extraClass}" role="dialog" aria-label="${title}"><div class="dialog-header">${heading}<a class="icon-button" href="main-project.html" aria-label="Close">${icon("x")}</a></div><div class="dialog-body">${body}</div><div class="dialog-footer"><span class="footer-help"></span>${footer}</div></section></div>`;
}

/**
 * 「创建 Git 仓库」对话框的内容（权威 `GitInit`，
 * `plugins/git4idea/backend/src/actions/GitInit.java`）。
 *
 * - 入口文案取 `action.Git.Init.text` = **"Create Git Repository…"**（`GitBundle.properties:774`）；
 * - 目标目录一行对应权威的**单目录选择器**（`:45-64`：`createSingleFolderDescriptor`，标题
 *   `init.destination.directory.title` = "Create Git Repository"、描述
 *   `init.destination.directory.description` = "Select directory where the new Git repository will be created."，
 *   起点是当前选中目录、取不到则项目根）——外壳里由 `data-repo-init-action="pick"` 接到 `workspace/pick`；
 * - **不是仓库时没有任何确认**（`:66-74` 只在目标已在 Git 下才弹 Yes/No），因此这里没有"确认要初始化吗"的二次询问，
 *   按钮本身就是用户的明确意图；
 * - `busy` 时冻结重复提交（ux-spec「初始化进行中只锁定重复提交」）。
 *
 * 样例页与实时外壳共用本函数（`data-repo-init-action` 的绑定在 `live-data.js`）。
 */
function repositoryInitBody(target, notice, busy = false) {
  return `<div class="form-grid"><label for="repo-init-path">Destination</label>`
    + `<div class="path-row"><input id="repo-init-path" class="text-field" aria-label="Destination" value="${escapeHtml(target || "")}" readonly data-repo-init-path>`
    + `<button type="button" class="secondary-button" data-repo-init-action="pick"${busy ? " disabled" : ""}>Choose Directory…</button></div></div>`
    + `<p class="commit-meta">Initialization creates .git metadata in this directory; existing files are kept.</p>`
    + `<div class="stash-notice" role="status" data-repo-init-notice${notice ? "" : " hidden"}>${escapeHtml(notice || "")}</div>`;
}

/**
 * 「目标已在 Git 下」的 Yes/No 确认（权威 `GitInit` 的唯一确认时机，`:66-74`）。
 *
 * 标题 `init.warning.title` = "Git Init"、正文 `init.warning.already.under.git` =
 * "The selected directory {0} is already under Git.\nAre you sure that you want to create a new VCS root?"
 * （`GitBundle.properties:101-103`），带警告图标；两项结果就是 Yes/No。
 */
function repositoryInitWarningBody(target) {
  return `<div class="info-block" style="width:auto;text-align:left"><h2>The selected directory is already under Git</h2>`
    + `<p>${escapeHtml(target || "")}</p>`
    + `<p>Create a new VCS root for this directory?</p></div>`
    + `<div class="stash-notice" role="status" data-repo-init-notice hidden></div>`;
}

/** `repository-init` 场景（`init-state` 取 `ready`（默认）／`under-git`／`busy`／`failure`）。 */
function repositoryInitScene() {
  const state = new URLSearchParams(location.search).get("init-state") || "ready";
  const target = "D:\\projects\\notes";
  if (state === "under-git") {
    return dialog(
      "Git Init",
      repositoryInitWarningBody(target),
      '<button type="button" class="secondary-button" data-repo-init-action="cancel">Cancel</button>'
        + '<button type="button" class="primary-button" data-repo-init-action="confirm">Continue</button>',
      false,
      // 权威 `GitInit.java:66-74`：只有"目标已在 Git 下"这一档是 `Messages.showYesNoDialog(...,
      // getWarningIcon())` ⇒ 标题画警告图标（橙底白叹号）；其余初始化状态没有权威对话框。
      "repository-init-dialog repository-init-warning");
  }
  const busy = state === "busy";
  const notice = busy
    ? "Initializing…"
    : state === "failure"
      ? "Git initialization failed:fatal: unable to create 'D:/projects/notes/.git/': Permission denied"
      : "";
  return dialog(
    "Create Git Repository",
    repositoryInitBody(target, notice, busy),
    `<button type="button" class="secondary-button" data-repo-init-action="cancel"${busy ? " disabled" : ""}>Cancel</button>`
      + `<button type="button" class="primary-button" data-repo-init-action="create"${busy ? " disabled" : ""}>${busy ? "Initializing…" : "Create"}</button>`,
    false,
    "repository-init-dialog");
}

/**
 * Smart Checkout 对话框的内容（权威 `GitSmartOperationDialog`，
 * `plugins/git4idea/backend/src/branch/GitSmartOperationDialog.java:36-125`）。
 *
 * - 标题 `smart.operation.dialog.git.operation.name.problem` = "Git {0} Problem"（{0} 取
 *   `checkout.operation.name` = "checkout"，`GitBundle.properties:1356-1357`）⇒ 中文写「Git 检出问题」；
 * - 北侧说明取 stash 版 `smart.operation.dialog.north.panel.label.stash.text`（`:426`）：
 *   "Your local changes to the following files would be overwritten by checkout. … can stash the changes,
 *   checkout and unstash them after that."；
 * - 中部是**受影响的改动**（权威是 `ChangesBrowserWithRollback`，无 diff 数据时退化为路径列表
 *   `GitSimplePathsBrowser`，`:57-63`；Augit 拿到的是 git 报出的路径 ⇒ 用路径列表表达同一件事）；
 * - 两个按钮：OK = `smart.operation.dialog.smart.operation.name` = "Smart Checkout"
 *   （tooltip `…ok.action.stash.description` = "Stash local changes, {0}, unstash"），
 *   取消 = `smart.operation.dialog.don.t.operation.name` = "Don't {0}" ⇒「不检出」。
 *   权威把 `FOCUSED_ACTION` 设在取消上（`:118`）—— **默认焦点在取消**，实时侧照此聚焦。
 * - `busy` 时冻结重复提交；`conflict` 态由调用方用 `notice` 说明"临时 stash 已保留"。
 *
 * 样例页与实时外壳共用（`data-smart-action` 的绑定在 `live-data.js`）。
 */
function smartCheckoutBody(paths, notice, busy = false) {
  const list = (paths || []).length > 0
    ? `<div class="smart-checkout-paths" role="list">${paths.map((path) =>
      `<div class="smart-checkout-path" role="listitem" title="${escapeHtml(path)}">${escapeHtml(path)}</div>`).join("")}</div>`
    : `<p class="commit-meta">Local changes would be overwritten by the target branch.</p>`;
  return list
    + `<p class="commit-meta smart-checkout-impact">Local changes to these files would be overwritten by checkout. Augit can stash them, check out, and restore them afterward.</p>`
    + `<div class="stash-notice" role="status" data-smart-notice${notice ? "" : " hidden"}>${escapeHtml(notice || "")}</div>`;
}

/** `smart-checkout` 场景（`smart-state` 取 `ready`（默认）／`busy`／`conflict`／`restored`）。 */
function smartCheckoutScene() {
  const state = new URLSearchParams(location.search).get("smart-state") || "ready";
  const paths = ["docs/notes.txt", "src/App.cs"];
  const busy = state === "busy";
  const notice = busy
    ? "Stashing changes and checking out…"
    : state === "conflict"
      ? "Restoring changes caused conflicts. The temporary stash is kept; resolve conflicts to continue."
      : state === "restored"
        ? "Checked out feature/ux; the temporary stash was removed."
        : "";
  const footer = state === "conflict"
    ? '<button type="button" class="secondary-button" data-smart-action="cancel">Close</button>'
    : `<button type="button" class="secondary-button" data-smart-action="cancel"${busy ? " disabled" : ""}>Don't Checkout</button>`
      + `<button type="button" class="primary-button" data-smart-action="smart"${busy ? " disabled" : ""}>${busy ? "Checking out…" : "Smart Checkout"}</button>`;
  return dialog("Git Checkout Problem", smartCheckoutBody(paths, notice, busy), footer, false, "smart-checkout-dialog");
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
  return `<div class="overlay-layer" data-augit-overlay><div class="scrim"></div><section class="dialog compact-input" role="dialog" aria-modal="true" aria-label="${escapeHtml(title)}" data-compact-dialog="${escapeHtml(title)}"><div class="dialog-header"><span>${escapeHtml(title)}</span><span class="grow"></span><button type="button" class="icon-button" data-compact-action="close" aria-label="Close">${icon("x")}</button></div><div class="dialog-body"><label for="compact-input-field">${escapeHtml(label)}</label><input id="compact-input-field" class="text-field" aria-label="${escapeHtml(label)}" value="${escapeHtml(value || "")}" data-compact-field></div><div class="dialog-footer"><span class="footer-help"></span><button type="button" class="secondary-button" data-compact-action="cancel">Cancel</button><button type="button" class="primary-button" data-compact-action="confirm">${escapeHtml(confirmLabel)}</button></div></section></div>`;
}

// 外壳注入真实引用时使用：分组、当前分支标记与二级动作沿用样例版结构。
function liveBranchesPopover() {
  const references = window.__augitLive.references;
  const branches = references.branches || [];
  const tags = references.tags || [];
  const local = branches.filter(branch => !branch.isRemote);
  const remote = branches.filter(branch => branch.isRemote);
const row = (branch, selected) => `<div class="menu-item${selected ? " selected" : ""}" data-branch="${escapeHtml(branch.name)}" data-branch-kind="${branch.isRemote ? "remote" : "branch"}"${branch.isCurrent ? ' data-branch-current="true"' : ""}>${branchTreeIcon(branch, branch.isRemote ? 'remote' : 'branch')} ${escapeHtml(branch.name)}${branch.isCurrent ? ' <span class="commit-meta">Current</span>' : ''}${branch.upstream ? ` <span class="commit-meta">→ ${escapeHtml(branch.upstream)}</span>` : ''}<span class="grow"></span>${icon("chevron-right")}</div>`;
  const group = (label, items) => items.length === 0
    ? ""
    : `<div class="menu-item"><span>${icon("chevron-down")}</span><strong>${label}</strong></div>${items.join("")}`;
  const checkoutError = window.__augitCheckoutError
    ? `<div class="inline-alert danger" role="alert">${escapeHtml(window.__augitCheckoutError)}</div>`
    : "";
  const quick = `<input class="search-field" placeholder="Search branches and actions" aria-label="Search branches and actions">${checkoutError}<a class="menu-item" href="operation-result.html" data-popover-action="fetch">${icon("branch-update")} Update Project…</a><a class="menu-item" href="commit-changes.html" data-popover-action="commit">${icon("git-commit-horizontal")} Commit…</a><a class="menu-item" href="push.html" data-popover-action="push">${icon("branch-push")} Push…</a><div class="menu-separator"></div><a class="menu-item" href="branches.html" data-popover-action="create-branch">${icon("plus")} New Branch…</a><a class="menu-item" href="git-compare.html" data-popover-action="checkout-revision">${icon("git-compare-arrows")} Checkout Tag or Revision…</a><div class="menu-separator"></div>`;
  const groups = group("Local", local.map(branch => row(branch, branch.isCurrent)))
    + group("Remote", remote.map(branch => row(branch, false)))
    + group("Tags", tags.map(tag => `<div class="menu-item" data-branch="${escapeHtml(tag.name)}" data-branch-kind="tag">${gitReferenceIcon("tag")} ${escapeHtml(tag.name)}<span class="grow"></span>${icon("chevron-right")}</div>`));
  const current = local.find(branch => branch.isCurrent);
  const actions = current
    ? `<section class="popover branch-actions"><a class="menu-item" href="smart-checkout.html" data-branch-action="create">${icon("plus")} From ${escapeHtml(current.name)} New Branch…</a><a class="menu-item" href="git-compare.html" data-popover-action="compare-workspace">${icon("git-compare-arrows")} Compare with Workspace</a><a class="menu-item" href="worktrees.html" data-popover-action="create-worktree">${icon("folder-git-2")} New Worktree…</a><div class="menu-separator"></div><a class="menu-item" href="push.html">${icon("branch-push")} Push…</a><a class="menu-item" href="branches.html" data-branch-action="rename">${icon("rename")} Rename…</a></section>`
    : "";
  return `<div class="overlay-layer" data-augit-overlay><section class="popover branches-popover">${quick}${groups || '<p class="commit-meta">No references</p>'}</section>${actions}</div>`;
}

/**
 * 分支/标签行的动作菜单（权威 `GitSingleRefActions.getSingleRefActionGroup()`：`Git.Branch`
 * = `Git.Branch.Checkout` ＋ `Git.Branch.Placeholder` 里那一长串 ——
 * `plugins/git4idea/shared/resources/intellij.vcs.git.shared.xml:62-67`、
 * `plugins/git4idea/backend/resources/intellij.vcs.git.backend.xml:322-344`；
 * 每个引用都有自己的动作组，而不是只有"当前分支"才有一组动作）。
 *
 * 谁出现、谁不出现照权威的 `Presentation.setEnabledAndVisible()`（`GitSingleRefAction.update()`）：
 * 不适用就**整项不出现**，不是"灰掉"。
 *   - 检出：`GitCheckoutAction.isEnabledForRef()` —— 当前引用只在**多仓库分歧**时可再检出；
 *     Augit 是单仓库（`repositories.diverged()` 为假）⇒ 当前分支行没有"检出"。
 *   - 删除：`GitDeleteRefAction.isEnabledForRef()` = `!isCurrentRefInAnyRepo` ⇒ 当前分支行没有"删除"。
 *   - 重命名：`GitRenameBranchAction`（`refClass = GitBranch`、`disabledForRemote = true`）⇒ 只有本地分支有。
 *
 * 只有"权威有这个动作、Augit 尚未接线"的项才留成禁用＋原因（例如远端分支的删除）。
 */
function branchRowMenu({ name, kind, isCurrent }) {
  const noun = kind === "tag" ? "Tags" : "Branch";
  const link = (action, iconName, label) => `<a class="menu-item" href="#" data-ref-action="${action}">${icon(iconName)} ${label}</a>`;
  const blocked = (iconName, label, reason) => `<span class="menu-item disabled" aria-disabled="true" title="${escapeHtml(reason)}">${icon(iconName)} ${label}</span>`;
  const checkout = kind === "branch" && isCurrent
    ? ""
    : link("checkout", "branch-update", `Checkout${noun}`);
  const rename = kind === "branch" ? link("rename", "rename", "Rename…") : "";
  const remove = kind === "remote"
    ? blocked("trash-2", `Delete${noun}`, "Deleting remote branches is unavailable.")
    : isCurrent ? "" : link("delete", "trash-2", `Delete${noun}`);
  const tail = rename + remove;
  return `<section class="popover context-menu ref-menu" data-ref-name="${escapeHtml(name)}" data-ref-kind="${escapeHtml(kind)}">`
    + checkout
    + (checkout && tail ? `<div class="menu-separator"></div>` : "")
    + tail
    + `</section>`;
}

/**
 * 引用树行的弹出菜单（权威 `BranchesTree.kt:272`：`PopupHandler.installPopupMenu(component,
 * BranchesTreeActionGroup(), place)`；组内容由 `BranchActionsBuilder.build`
 * （`BranchesDashboardActions.kt:100-120`）**按选中集**决定）。
 *
 * 权威的构成状态机：
 *   - 单节点且（1 个引用 或 选中 HEAD）⇒ `GitSingleRefActions.getSingleRefActionGroup()`（该引用自己的动作组）；
 *   - HEAD ＋ 1 个分支（2 节点）⇒ `HeadAndBranchActions` = {两任意分支整份比较, 两任意分支单文件 diff}；
 *   - 引用数 > 1 ⇒ `MultipleLocalBranchActions` = {两任意分支整份比较, 两任意分支单文件 diff,
 *     `UpdateSelectedBranchAction`, `DeleteBranchAction`} —— **没有**检出与重命名；
 *   - 远端分组行 ⇒ `GroupActions`／`MultipleGroupActions`／`RemoteGlobalActions`。
 * 组本身 `isHideGroupIfEmpty = true` ⇒ 一条都没有时**不弹**。
 *
 * Augit 有对应能力的部分：单引用 ⇒ 检出／与当前分支比较／重命名…／删除…；
 * 多引用 ⇒ 与当前分支比较／更新选中分支／删除…；标签行 ⇒ 删除标签。
 * **登记差异**：`ShowArbitraryBranchesDiffAction`／`ShowArbitraryBranchesFileDiffAction`
 * （两**任意**分支的比较）Augit 未做，见 `09-icons.md` 第 184 轮。
 */
/**
 * 引用树行的右键菜单（权威 `BranchesTree.kt:272` 的 `BranchesTreeActionGroup` →
 * `BranchesDashboardActions.BranchActionsBuilder.build()`，`BranchesDashboardActions.kt:100-121`）。
 * 选中集的构成决定用哪一组动作：
 *
 * | 选中集 | 权威动作组 | 条目 |
 * | --- | --- | --- |
 * | 1 个节点（引用或 HEAD） | `GitSingleRefActions`（`Git.Branch`） | 检出／与当前分支比较／重命名／删除… |
 * | 2 个节点且 1 引用 + HEAD | `HeadAndBranchActions` | 比较分支／显示文件差异 |
 * | 全是引用且 > 1 | `MultipleLocalBranchActions` | 比较分支／显示文件差异／更新选中分支／删除分支 |
 * | 其它（含「引用 ＋ 标签」这类混合） | `else -> null` | 没有菜单 |
 *
 * 「比较分支」= `ShowArbitraryBranchesDiffAction`：**恰好两个分支**（或 1 分支 + HEAD）才出现；
 * 3 个及以上分支时 `getBranchPair()` 返回 null ⇒ 该动作 `isEnabledAndVisible = false`（隐藏）；
 * 两个名字相同（只可能是 HEAD + 当前分支）时禁用，说明文本是
 * `action.Git.Compare.Selected.description.disabled`。
 *
 * **登记差异**：权威这两组里还有 `ShowArbitraryBranchesFileDiffAction`（「显示文件差异」，
 * `GitBrancher.showDiff` → `CompareWithLocalDialog` 的**整树变更列表**对话框）；Augit 没有这个界面，
 * 按"不新增 Augit 没有的功能"暂不落地，见 `09-icons.md` 第 193 轮。
 */
function refTreeRowMenu({ selections = [], references = { branches: [], tags: [] } } = {}) {
  const link = (action, iconName, label) => `<a class="menu-item" href="#" data-ref-action="${action}">${icon(iconName)} ${label}</a>`;
  // 禁用项是**惰性**的行（没有 `data-ref-action`，因此点不动），但保留可断言的钩子与说明文本。
  const disabled = (action, iconName, label, reason) => `<span class="menu-item disabled" aria-disabled="true" title="${escapeHtml(reason)}" data-ref-action-disabled="${action}">${icon(iconName)} ${label}</span>`;
  const lookup = (item) => (references.branches || []).find(
    (branch) => branch.isRemote === (item.kind === "remote") && branch.name === item.name);
  const branches = selections
    .filter((item) => item.kind === "branch" || item.kind === "remote")
    .map(lookup)
    .filter(Boolean);
  const headSelected = selections.some((item) => item.kind === "head");
  // 权威 `action.Git.Compare.Selected.title` = "Compare Branches"。
  const compareLink = link("compare-branches", "git-compare-arrows", "Compare Branches");
  const compareDisabled = disabled("compare-branches", "git-compare-arrows", "Compare Branches", "Select two different branches in the same repository.");
  const items = [];
  if (selections.length === 1 && selections[0].kind === "tag") {
    items.push(link("delete", "trash-2", "Delete Tag"));
  } else if (selections.length === 1 && selections[0].kind !== "head" && branches.length === 1) {
    const branch = branches[0];
    if (!branch.isCurrent) {
      items.push(link("checkout", "branch-update", branch.isRemote ? "Checkout Remote Branch" : "Checkout Branch"));
      items.push(link("compare", "git-compare-arrows", "Compare with Current"));
    }
    items.push(link("rename", "rename", "Rename…"));
    if (!branch.isCurrent) {
      items.push(link("delete", "trash-2", "Delete Branch…"));
    }
  } else if (selections.length === 2 && headSelected && branches.length === 1) {
    // `HeadAndBranchActions`：配对是（该分支, 当前分支）；两个名字相同时禁用（`getBranchPair()` 的守卫）。
    items.push(branches[0].isCurrent ? compareDisabled : compareLink);
  } else if (branches.length === selections.length && branches.length > 1) {
    // `MultipleLocalBranchActions`：只有**恰好两个**分支时才给出「比较分支」（`getBranchPair()`）。
    if (branches.length === 2) {
      items.push(branches[0].name === branches[1].name ? compareDisabled : compareLink);
    }
    items.push(link("update-selected", "branch-update", "Update Selected"));
    items.push(link("delete", "trash-2", "Delete…"));
  }
  if (items.length === 0) {
    return "";
  }

  // 身份与目标集写在弹出元素自己身上：菜单动作的处理者只从 DOM 读（与 `branchRowMenu` 同形）。
  const primary = selections[0] || { name: "", kind: "branch" };
  return `<section class="popover context-menu ref-menu" data-ref-name="${escapeHtml(primary.name)}" data-ref-kind="${escapeHtml(primary.kind)}" data-ref-targets="${escapeHtml(JSON.stringify(selections))}">${items.join("")}</section>`;
}

function branchesPopover() {
  return `<div class="overlay-layer" data-augit-overlay><section class="popover"><input class="search-field" placeholder="Search branches and actions" aria-label="Search branches and actions"><a class="menu-item" href="operation-result.html">${icon("branch-update")} Update Project…</a><a class="menu-item" href="commit-changes.html">${icon("git-commit-horizontal")} Commit…</a><a class="menu-item" href="push.html">${icon("branch-push")} Push…</a><div class="menu-separator"></div><a class="menu-item" href="branches.html">${icon("plus")} New Branch…</a><a class="menu-item" href="git-compare.html">${icon("git-compare-arrows")} Checkout Tag or Revision…</a><div class="menu-separator"></div><div class="menu-item"><span>${icon("chevron-down")}</span><strong>Local</strong></div><div class="menu-item selected">${gitReferenceIcon()} main <span class="grow"></span>${icon("chevron-right")}</div></section><section class="popover branch-actions"><a class="menu-item" href="smart-checkout.html">${icon("plus")} New Branch from main…</a><a class="menu-item" href="git-compare.html">${icon("git-compare-arrows")} Compare with Workspace</a><a class="menu-item" href="worktrees.html">${icon("folder-git-2")} New Worktree…</a><div class="menu-separator"></div><a class="menu-item" href="push.html">${icon("branch-push")} Push…</a><a class="menu-item" href="branches.html">${icon("rename")} Rename…</a></section></div>`;
}

function renderScene() {
  if (window.__augitSettingsSurface) return '<main class="augit-window settings-surface"></main>';
  const settingsBody = settingsBodyFallback();
  // Stash 创建对话框的字段与提示取权威 `GitStashDialog.kt:41-53` + `GitBundle.properties:52-55,437-446`：
  //   字段顺序 根 → 当前分支 → 消息 → 复选；每个字段都带 toolTipText（`common.git.root.tooltip`、
  //   `common.current.branch.tooltip`、`stash.message.tooltip`、`stash.keep.index.tooltip`、
  //   `stash.include.untracked.tooltip`）。这里只补 Augit **已有字段**的提示。
  //   **`Include untracked` 复选是"规范 vs 权威"的冲突**：`ux-spec:524-525` 明确规定只要四个字段
  //   （根目录／当前分支／消息／保留索引），而权威有该复选、宿主机 `ShellBridge.cs:1970` 也早已支持
  //   `includeUntracked` 且**默认 true**。加上它等于改变"什么会被 stash"（产品行为，不只是呈现），
  //   已升级为人工裁决，见 docs/nui-behavior/10-backlog.md §三 与 13-git-dialogs.md §2.3。
  const stashBody = `<div class="form-grid"><label for="stash-root">Git root</label><select id="stash-root" class="select-field" title="Choose Git repository root"><option>D:\\github\\Augit</option></select><label>Current branch</label><span class="stash-branch" title="Currently checked out branch">main</span><label for="stash-message">Message</label><textarea id="stash-message" class="message-field" title="Enter Stash Message here"></textarea><span></span><div class="stash-check-row"><label class="check-line" title="Keep staged changes as-is"><input id="stash-keep" type="checkbox">Keep index</label><label class="check-line" title="Also stash untracked files."><input id="stash-include-untracked" type="checkbox">Include untracked files</label></div></div><div class="stash-notice" role="status" hidden></div>`;
  const cloneBody = `<div class="form-grid"><label for="clone-version">Version control</label><select id="clone-version" class="select-field"><option>Git</option></select><label for="clone-source">Repository URL</label><input id="clone-source" class="text-field" placeholder="https://example.com/team/repository.git"><label for="clone-destination">Directory</label><input id="clone-destination" class="text-field" placeholder="D:\\projects\\repository"><span></span><div class="clone-shallow-row"><label class="check-line"><input id="clone-shallow" type="checkbox">Shallow clone, history truncated to</label><div class="clone-depth-group disabled"><input id="clone-depth" class="text-field" value="1" inputmode="numeric" aria-label="Shallow clone depth" title="--depth" disabled><span>commits</span></div></div></div><div class="clone-notice" role="status" hidden></div>`;
  switch (scene) {
    case "main-project": return shell({ activeRail: "project", side: "project", editor: "markdown", bottom: "git" });
    case "text-viewer": return shell({ activeRail: "project", side: "project", editor: "text-find", selectedFile: "MainWindow.cs" });
    case "go-to-line": return shell({ activeRail: "project", side: "project", editor: "text", selectedFile: "MainWindow.cs", overlay: `<div class="scrim"></div><section class="dialog compact-input goto-line-dialog" role="dialog" aria-modal="true" aria-label="Go to Line"><div class="dialog-header"><span class="brand-mark" aria-hidden="true">A</span><span class="goto-line-title">Go to Line</span><a class="icon-button" href="text-viewer.html" aria-label="Close">${icon("x")}</a></div><div class="dialog-body"><label for="prompt-line">Line number</label><span class="goto-line-field"><input id="prompt-line" class="text-field" inputmode="numeric" aria-label="Line number" autofocus></span></div><div class="dialog-footer"><a class="primary-button" href="text-viewer.html">OK</a><a class="secondary-button" href="text-viewer.html">Cancel</a></div></section>` });
    case "markdown-preview": return shell({ activeRail: "project", side: "project", editor: "markdown" });
    case "json-preview": return shell({ activeRail: "project", side: "project", editor: "json", selectedFile: "global.json" });
    case "image-preview": return shell({ activeRail: "project", side: "project", editor: "image", selectedFile: "image-sample.png" });
    case "file-limit": return shell({ activeRail: "project", side: "project", editor: "file-limit", selectedFile: "animation.webp" });
    case "image-error": return shell({ activeRail: "project", side: "project", editor: "file-limit", selectedFile: "corrupt.png", imageError: true });
    case "commit-changes": return shell({ activeRail: "commit", side: "commit", editor: "markdown" });
    case "commit-diff": return shell({ activeRail: "commit", side: "commit", editor: "diff", bottom: "git", selectedFile: "app.manifest" });
    case "diff-status": return shell({ activeRail: "commit", side: "commit", editor: "diff", bottom: "git", selectedFile: "app.manifest",
      diffStatus: new URLSearchParams(location.search).get("diff-status") || "Binary" });
    case "diff-boundary": return shell({ activeRail: "commit", side: "commit", editor: "diff", bottom: "git", selectedFile: "app.manifest", diffBoundary: true });
    case "git-history": return shell({ activeRail: "history", side: "project", editor: "markdown", bottom: "git" });
    case "git-history-empty": return shell({ activeRail: "history", side: "project", editor: "markdown", bottom: "git", emptyHistory: true });
    case "git-history-graph": return shell({ activeRail: "history", side: "project", editor: "markdown", bottom: "git", complexGraph: true });
    case "file-history": return shell({ activeRail: "history", side: "project", editor: "text", bottom: "file-history", selectedFile: "product-spec.md" });
    case "git-compare": return shell({ activeRail: "history", side: "project", editor: "comparison", bottom: "git", selectedFile: "app.manifest", workspaceComparison: true });
    // 引用比较的"无文本差异"摘要态（⑭ 第 4 项）：live 侧在"该提交没有改这个文件"时走同一分支。
    case "git-compare-empty": return shell({ activeRail: "history", side: "project", editor: "comparison", bottom: "git", selectedFile: "app.manifest", comparisonState: "summary" });
    case "history-diff-loading":
    case "history-diff-failure":
    case "history-diff-cancelled": return shell({ activeRail: "history", side: "project", editor: "comparison", bottom: "git", selectedFile: "app.manifest", comparisonState: scene.slice("history-diff-".length) });
    case "branches": return shell({ activeRail: "commit", side: "commit", editor: "diff", bottom: "git", overlay: (window.__augitLive && window.__augitLive.references) ? liveBranchesPopover() : branchesPopover(), selectedFile: "app.manifest" });
    case "stash": return shell({ activeRail: "commit", side: "commit", editor: "diff", bottom: "git", overlay: dialog("Stash", stashBody, `<button class="primary-button">Create Stash</button><button class="secondary-button">Cancel</button>`, false, "stash-dialog") });
    // 危险操作的「影响确认」基线（⑭ 第 7 项）：正文出自实时侧同一函数 `dangerConfirmBody()`，
    // 确认按钮用动作名（规格 §10.4）。删除 Stash / 移除 Worktree / 删除分支或标签共用该结构。
    case "stash-drop-confirm": return shell({ activeRail: "commit", side: "commit", editor: "diff", bottom: "git", selectedFile: "product-spec.md",
      overlay: dialog("Delete Stash", dangerConfirmBody("stash@{0} · fix: 待处理的本地改动", [
        "Deleting this Stash discards changes in its 2 files permanently.",
        "The working tree and other Stashes are unchanged.",
      ]), '<button type="button" class="secondary-button">Cancel</button><button type="button" class="danger-button">Delete stash@{0}</button>', false, "stash-drop-dialog") });
    case "worktrees": return shell({ activeRail: "history", side: "project", editor: "text", bottom: "git", overlay: dialog("Worktrees", (window.__augitLive && window.__augitLive.worktrees) ? liveManagementPage("worktrees") : managementPage("worktrees"), `<a class="secondary-button" href="git-history.html">Close</a>`, true, "worktree-dialog") });
    case "remote": return shell({ activeRail: "history", side: "project", editor: "text", bottom: "git", overlay: dialog("Git Remotes", (window.__augitLive && window.__augitLive.remotes) ? liveManagementPage("remote") : managementPage("remote"), `<button class="primary-button" type="button" data-remote-action="ok">OK</button>`, true, "remote-dialog") });
    case "reset": return shell({ activeRail: "history", side: "project", editor: "text", bottom: "git", overlay: dialog("Reset Current Branch", (window.__augitLive && window.__augitLive.history) ? liveResetBody() : `<div class="form-grid"><label for="reset-target">Target commit</label><input id="reset-target" class="text-field" value="dfe5c25a"><label for="reset-mode">Mode</label><select id="reset-mode" class="select-field"><option value="mixed" selected>Mixed · Reset index</option><option value="soft">Soft · Move HEAD only</option><option value="hard">Hard · Reset index and working tree</option></select></div><div class="inline-alert reset-impact danger"><strong></strong><p class="commit-meta"></p></div><div class="reset-notice" role="status" hidden></div>`, `<button class="reset-run danger-button" type="button">Reset Hard</button><button class="secondary-button" type="button">Cancel</button>`, false, "reset-dialog") });
    case "clone": return shell({ activeRail: "project", side: "project", editor: "empty", overlay: dialog("Clone Repository", (window.__augitLive && window.__augitLive.settings) ? liveCloneBody() : cloneBody, `<button class="secondary-button">Cancel</button><button class="primary-button">Clone</button>`, true, "clone-dialog") });
    case "push": return shell({ activeRail: "commit", side: "commit", editor: "diff", overlay: dialog(pushDialogTitle(), (window.__augitLive && window.__augitLive.push) ? livePushDialogBody() : pushDialogBody(false), `<button class="secondary-button">Cancel</button><button class="primary-button">Push</button>`, true, "push-dialog") });
    case "quick-open": return shell({ activeRail: "project", side: "project", editor: "text", overlay: (window.__augitLive && window.__augitLive.search) ? liveSearchOverlay("quick") : searchOverlay("quick"), selectedFile: "MainWindow.cs" });
    case "repository-search": return shell({ activeRail: "search", side: "project", editor: "text", overlay: (window.__augitLive && window.__augitLive.search) ? liveSearchOverlay("repository") : searchOverlay("repository"), selectedFile: "NativeGitPanel.cs" });
    case "terminal": return shell({ activeRail: "terminal", side: "project", editor: "text", bottom: "terminal", selectedFile: "app.manifest" });
    case "settings": return shell({ activeRail: "project", side: "project", editor: "markdown", overlay: dialog("Settings — Augit", (window.__augitLive && window.__augitLive.settings) ? liveSettingsBody() : settingsBody, `<button type="button" class="primary-button" data-settings-action="save">OK</button><button type="button" class="secondary-button" data-settings-action="cancel">Cancel</button><button type="button" class="secondary-button" data-settings-action="apply">Apply</button>`, true, "dialog-xl") });
    case "settings-save-failure": return shell({ activeRail: "project", side: "project", editor: "markdown", overlay: dialog("Settings — Augit", (window.__augitLive && window.__augitLive.settings) ? liveSettingsBody() : settingsBody, `<span class="footer-help settings-failure">Settings were not saved: cannot access the file or directory. Check permissions or file locks and try again.</span><button type="button" class="primary-button" data-settings-action="save">OK</button><button type="button" class="secondary-button" data-settings-action="cancel">Cancel</button><button type="button" class="secondary-button" data-settings-action="apply">Apply</button>`, true, "dialog-xl"), selectedFile: "product-spec.md" });
    // 未保存修改标记（⑭ 第 6 项，PyCharm 实测的实心圆点）：静态基线里直接标"文件查看"有草稿。
    case "settings-dirty": return shell({ activeRail: "project", side: "project", editor: "markdown", overlay: dialog("Settings — Augit", (window.__augitLive && window.__augitLive.settings) ? liveSettingsBody() : settingsLayoutHtml("appearance", null, false, new Set(["file-view"])), `<button type="button" class="primary-button" data-settings-action="save">OK</button><button type="button" class="secondary-button" data-settings-action="cancel">Cancel</button><button type="button" class="secondary-button" data-settings-action="apply">Apply</button>`, true, "dialog-xl"), selectedFile: "product-spec.md" });
    case "git-unavailable": return shell({ activeRail: "project", side: "project", editor: "empty", gitUnavailable: "Git for Windows 2.40 or newer was not found.", toast: toastMarkup({ kind: "error", title: "Git unavailable", text: "Git for Windows 2.40 or newer was not found; file browsing remains available.", action: { label: "Configure git.exe", href: "settings.html" } }) });
    case "operation-result": return shell({ activeRail: "commit", side: "commit", editor: "diff", toast: toastMarkup({ kind: "error", title: "Push failed", text: "No remote is configured. Local commits and the working tree are unchanged.", extra: '<div class="commit-meta" style="margin-top:5px">Command output is discarded when this notification closes.</div>' }) });
    case "workspace-open": return shell({ activeRail: "project", side: "project", editor: "empty", overlay: dialog("Open Workspace", `<div class="management-content" style="height:350px"><div class="management-list"><div class="tree-row selected">${icon("history")} Recent Workspaces</div><div class="tree-row">${icon("folder-open")} Choose Directory…</div><div class="tree-row">${icon("clone")} Clone Repository…</div></div><div class="management-detail"><h2>Recent Workspaces</h2><a class="tree-row selected" href="main-project.html"><span>${treeFolderIcon(true)}</span><span class="tree-name">Augit</span><span class="tree-path">D:\\github\\Augit</span></a><p class="commit-meta">Opening the same directory activates its existing window.</p></div></div>`, `<a class="secondary-button" href="main-project.html">Cancel</a><button class="primary-button">Open</button>`, true) });
    case "repository-init": return shell({ activeRail: "project", side: "project", editor: "empty", overlay: repositoryInitScene() });
    case "blame": return shell({ activeRail: "history", side: "project", editor: "blame", bottom: "file-history", selectedFile: "product-spec.md" });
    case "smart-checkout": return shell({ activeRail: "commit", side: "commit", editor: "diff", overlay: smartCheckoutScene() });
    case "rollback": {
      const liveStatus = window.__augitLive && window.__augitLive.status;
      const rollbackFile = liveStatus && liveStatus.files ? (liveStatus.files.find(f => f.group === "Changes") || liveStatus.files[0]) : null;
      const title = rollbackFile ? `Rollback File · ${rollbackFile.path}` : "Rollback File · src/Augit.App/app.manifest";
      const body = rollbackFile ? liveRollbackBody() : `<div class="inline-alert danger rollback-impact"><strong>All local changes in this file will be discarded</strong><p class="commit-meta">Rollback affects the entire file, including all local changes.</p><p class="rollback-recycle" hidden>Untracked or new files are moved to the Windows Recycle Bin.</p></div><div class="rollback-comparison">${diffView(true, "ready", true)}</div>`;
      return shell({ activeRail: "commit", side: "commit", editor: "diff", overlay: dialog(title, body, `<a class="secondary-button" href="commit-diff.html">Cancel</a><a class="danger-button" href="operation-progress.html">Rollback File</a>`, true, "rollback-dialog") });
    }
    case "operation-progress": return shell({ activeRail: "commit", side: "commit", editor: "diff", toast: toastMarkup({ title: "Running Smart Checkout…", extra: '<div class="commit-meta">Restoring temporary Stash</div><div class="progress-track"><div class="progress-value"></div></div>', action: { label: "Cancel", href: "operation-result.html" } }) });
    case "terminal-close": return shell({ activeRail: "terminal", side: "project", editor: "text", bottom: "terminal", overlay: dialog("Close Terminal", `<div class="info-block" style="width:auto;text-align:left"><h2>A command is still running in the terminal</h2><p><code>dotnet test Augit.slnx -c Release</code></p><p>Continuing stops the foreground command, shell, and all child processes.</p></div>`, `<a class="secondary-button" href="terminal.html">Keep Terminal</a><a class="danger-button" href="main-project.html">Stop Command and Close</a>`) });
    case "search-limited": {
      // 到限态叠上权威的「结果过多」；`search-state=timeout` 等其它态只显示浮层自己的说明。
      const limited = (new URLSearchParams(location.search).get('search-state') || 'limited') === 'limited';
      return shell({ activeRail: "search", side: "project", editor: "text", overlay: searchOverlay("repository") + (limited ? searchLimitDialog() : ''), selectedFile: "NativeGitPanel.cs" });
    }
    case "conflict-list": return shell({ activeRail: "commit", side: "commit", editor: "empty", overlay: dialog("Rebase Conflicts", `<div class="toolbar"><strong>2 conflicting files</strong><span class="grow"></span><span class="commit-meta">Current step 2/4</span></div><div class="changes-list"><div class="check-row selected" title="Open read-only diff"><span style="color:var(--augit-red)">${icon("conflict")}</span><span>NativeGitPanel.cs</span><span class="tree-path">Content conflict</span></div><div class="check-row" title="Open read-only diff"><span style="color:var(--augit-red)">${icon("conflict")}</span><span>MainWindow.cs</span><span class="tree-path">Content conflict</span></div></div>`, `<a class="secondary-button" href="main-project.html">Abort Rebase</a><button class="secondary-button">Skip</button><button class="primary-button" disabled>Continue Rebase</button>`, true) });
    case "commit-empty": return shell({ activeRail: "commit", side: "commit-empty", editor: "markdown" });
    case "diff-loading": return shell({ activeRail: "commit", side: "commit", editor: "diff-loading", bottom: "git", selectedFile: "app.manifest" });
    case "push-no-remote": return shell({ activeRail: "commit", side: "commit", editor: "diff", overlay: dialog(pushDialogTitle(), pushDialogBody(true), `<button class="secondary-button">Cancel</button><button class="primary-button" disabled>Push</button>`, true, "push-dialog push-no-remote") });
    case "stash-manager": return shell({ activeRail: "history", side: "project", editor: "text", bottom: "git", overlay: dialog("Stash", (window.__augitLive && window.__augitLive.stashes) ? liveManagementPage("stash") : managementPage("stash"), `<a class="secondary-button" href="git-history.html">Close</a>`, true, "stash-manager-dialog") });
    case "project-context-menu": return shell({ activeRail: "project", side: "project", editor: "markdown", bottom: "terminal", overlay: projectContextMenu() });
    case "changes-context-menu": return shell({ activeRail: "commit", side: "commit", editor: "diff", bottom: "git", overlay: changesContextMenu(), selectedFile: "app.manifest" });
    case "git-history-menu": return shell({ activeRail: "history", side: "project", editor: "markdown", bottom: "git", overlay: gitLogContextMenu() });
    case "quick-open-empty": return shell({ activeRail: "project", side: "project", editor: "text", overlay: quickOpenEmpty(), selectedFile: "MainWindow.cs" });
    default: return shell();
  }
}

function bindHistoryToolbar(root = document) {
  root.querySelectorAll(".git-side-toolbar").forEach(toolbar => {
    // 幂等：这里 append 的是「更多」按钮与它的弹层。静态视觉稿只绑定一次，
    // 实时外壳每次区域刷新都会整体重新绑定，于是底部 Git 工具窗右侧越堆越多
    // （用户实测："一堆详情"）。同一个工具条只允许有一组入口。
    if (toolbar.querySelector(":scope > .history-tools-more")) return;
    const buttons = [...toolbar.querySelectorAll("button")];
    const trigger = document.createElement("button");
    trigger.className = "toolbar-button history-tools-more";
    trigger.setAttribute("aria-label", "More History Tools");
    trigger.setAttribute("aria-expanded", "false");
    trigger.innerHTML = icon("chevron-right");
    toolbar.append(trigger);
    const popup = document.createElement("div");
    popup.className = "history-tools-popup";
    popup.setAttribute("popover", "auto");
    popup.setAttribute("role", "toolbar");
    popup.setAttribute("aria-label", "History Tools");
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
    const observer = new ResizeObserver(layout);
    observer.observe(toolbar);
    // 区域被替换或整页重绘时解除观察，否则观察器会一直持有已脱离文档的节点。
    registerRegionDisposer(() => observer.disconnect());
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
          button.click();
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
      // 权威 `VcsLogClassicFilterUi.TextFilterField.getMinimumSize()`：文本/哈希输入框
      // 至少 `JBUIScale.scale(150)`。收纳判定必须按 150 预留，不能用更小的值把输入框压细。
      while (visible > 0 && available - slots() - 4 < 150) visible--;
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
        const host = row.querySelector(".branch-label");
        const labels = [...host.querySelectorAll('.log-reference-label')];
        if (labels.length) return labels.reduce((width, label) => width + measure(label.textContent)
          + Number(label.querySelector('svg').viewBox.baseVal.width) + 2, 0) + (labels.length - 1) * 4;
        const text = host.textContent.trim();
        return text ? measure(text) + 20 : 0;
      })));
      const fullWidth = Math.max(...rows.map(row => measure(row.querySelector("time").dataset.full)));
      const compactWidth = Math.max(...rows.map(row => measure(row.querySelector("time").dataset.compact)));
      // 复杂提交图占用完整的多轨宽度，不能沿用单轨圆点宽度而让连线覆盖标题。
      const graph = panel.querySelector(".commit-graph-svg");
      // 没有图形时的回落也按单轨公式推导，不再使用固定的 29px。
      const graphWidth = graph ? Number(graph.viewBox.baseVal.width) : commitGraphWidth(1, historyRowHeight());
      const rowWidth = Math.max(list.clientWidth, graphWidth + 120 + 8
        + (authorWidth > 0 ? Math.min(authorWidth, 48) + 8 : 0) + (compactWidth > 0 ? compactWidth + 8 : 0));
      const contentWidth = Math.max(0, rowWidth - graphWidth - 8);
      const budget = Math.max(0, contentWidth - Math.min(contentWidth, 120) - 8);
      // 窄栏时优先保留作者与日期列：完整日期还要为引用列保留至少 64px 的辨认空间，
      // 放不下就切换短日期，并整列隐藏引用。否则先保留完整日期，引用列再按实际文字宽度让位。
      const referenceMinimum = referenceWidth > 0 ? 64 : 0;
      const compact = fullWidth + Math.min(authorWidth, 48) + 8 + referenceMinimum + 8 > budget;
      const date = Math.min(budget, compact ? compactWidth : fullWidth);
      const author = Math.min(authorWidth, Math.max(0, budget - date - 8));
      const referenceBudget = Math.max(0, budget - date - (author ? author + 8 : 0) - 8);
      const reference = !compact && referenceBudget >= referenceMinimum
        ? Math.min(referenceWidth, referenceBudget) : 0;
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
    // 字号或 DPI 变化 → 图形区几何变化（`refreshCommitGraphGeometry()`）后必须重排：
    // 行宽、作者与日期列宽都按当前字体度量算，只换 CSS 变量不够。
    panel.addEventListener("history-geometry-changed", layout, { signal });
    layout();
    list.setAttribute("role", "listbox");
    list.setAttribute("aria-label", "Commit History");
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
      if (!row) return;
      select(rows.indexOf(row));
      // 选择同时进状态：规格 §7.9 要求"提交选择"跨重绘保留（进入/离开文件历史都会整块重绘），
      // 只改 DOM 的话重绘后必然退回第一行（第 164 轮实测）。
      const live = window.__augitLive;
      if (live) live.historySelectedHash = row.dataset.hash || null;
    });
    list.addEventListener("keydown", event => {
      if (!["ArrowUp", "ArrowDown", "Home", "End", "PageUp", "PageDown"].includes(event.key)
          || event.isComposing || event.keyCode === 229 || event.altKey || event.ctrlKey || event.metaKey) return;
      event.preventDefault();
      const current = rows.findIndex(row => row.classList.contains("selected"));
      // 分页按当前视口与行高计算；窗口、字体或 DPI 改变后不沿用旧步长。
      const pageRows = Math.max(1, Math.floor(list.clientHeight / (rows[0]?.offsetHeight || 1)));
      const delta = event.key === "PageUp" ? -pageRows : event.key === "PageDown" ? pageRows
        : event.key === "ArrowUp" ? -1 : 1;
      select(event.key === "Home" ? 0 : event.key === "End" ? rows.length - 1
        : Math.max(0, Math.min(rows.length - 1, current + delta)));
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
      // 旧行为是"把输入框的占位符改成该筛选的名字" —— 那是个**假交互**（看起来像筛选入口，实际什么都没筛）。
      // 权威里点筛选控件要么打开弹层、要么点右侧关闭叉复位（`FilterComponent.java:96-200`）；
      // 外壳侧由 `live-data.js` 的分派器接线（`data-filter-key`），静态视觉稿只收起收纳菜单。
      overflow.open = false;
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
    // 幂等：这里 insertBefore 的是「提交详情 / 文件历史 / Blame」操作行。静态视觉稿只绑定一次，
    // 实时外壳每次区域刷新都会整体重新绑定（`__augitRenderRegions()` → `bindInteractions()`），
    // 而且是对 document 下**所有**面板重新绑定 —— 包括没有被替换的那些。没有幂等判断时，
    // 每刷新一次别的区域就多插一行：多余的行落进网格的隐式行里，行高被压到几像素后互相重叠
    //（用户实测："右下角的提交详情叠了一堆"），同时把变化文件区与详情正文挤到几乎为零。
    // 同一个面板只允许有一组操作行（与 `bindHistoryToolbar()` 的「更多」入口同一处理）。
    if (panel.dataset.historyDetailsBound === "true") return;
    panel.dataset.historyDetailsBound = "true";
    const detail = panel.querySelector(".commit-detail");
    const files = panel.querySelector(".changed-files");
    detail.tabIndex = 0;
    detail.setAttribute("aria-label", "Commit Details");
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
    actions.innerHTML = '<strong>Commit Details</strong><a class="toolbar-button" href="file-history.html">File History</a><a class="toolbar-button" href="blame.html">Blame</a>';
    panel.insertBefore(actions, detail);
    const arrange = () => {
      const context = document.createElement("canvas").getContext("2d");
      const style = getComputedStyle(panel);
      context.font = `${style.fontSize} ${style.fontFamily}`;
      const required = Math.max(280, Math.max(84, context.measureText("Commit Details").width + 13)
        + Math.max(82, context.measureText("File History").width + 16)
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
      const row = event.target.closest("[data-history-path]");
      if (row) selectFile(row);
    });
    files.addEventListener("keydown", event => {
      if (event.key !== "ContextMenu" && !(event.shiftKey && event.key === "F10")) return;
      const row = files.querySelector(".selected") || files.querySelector("[data-history-path]");
      if (!row) return;
      event.preventDefault();
      row.scrollIntoView({ block: "nearest" });
      const bounds = row.getBoundingClientRect();
      row.dispatchEvent(new MouseEvent("contextmenu", { bubbles: true, cancelable: true,
        clientX: bounds.left, clientY: bounds.bottom }));
    });
    files.addEventListener("contextmenu", event => {
      const row = event.target.closest("[data-history-path]");
      if (!row) return;
      event.preventDefault();
      selectFile(row);
      document.querySelector(".history-files-menu")?.remove();
      const menu = document.createElement("div");
      menu.className = "popover context-menu history-files-menu";
      menu.setAttribute("role", "menu");
      menu.innerHTML = '<a class="menu-item" href="git-compare.html">Show Diff</a><a class="menu-item" href="file-history.html">File History</a><a class="menu-item" href="blame.html">Blame</a>';
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
    comparison.tab.querySelector(".comparison-caption").textContent = `Compare: ${path.split("/").at(-1) || "Select File"} · ${hash.slice(0, 8)}^ → ${hash.slice(0, 8)}`;
    comparison.tab.title = path;
    const mode = comparison.view.querySelector(".diff-layout")?.dataset.diffMode;
    comparison.view.innerHTML = path ? diffView(true, "ready", false, false, path, hash)
      : '<div class="comparison-notice">Select a file in this commit to view its diff.</div>';
    const header = comparison.view.querySelector(".diff-filebar");
    if (header) header.outerHTML = diffFileHeader(`${hash.slice(0, 8)}^`, hash.slice(0, 8), `${hash}^ → ${hash}`, path);
    bindDiffModes(comparison.view);
    if (mode === "unified") comparison.view.querySelector('[aria-label="Unified"]')?.click();
  };
  const open = row => {
    if (!row) return;
    if (!comparison) {
      const tab = document.createElement("a"), view = document.createElement("div");
      tab.className = "editor-tab comparison-tab"; tab.href = "#"; tab.dataset.historyComparison = "true";
      tab.innerHTML = `${icon("git-compare-arrows")}<span class="comparison-caption"></span><button type="button" class="tab-close" aria-label="Close Comparison">${icon("close-small")}</button>`;
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
    if (link?.textContent === "Show Diff") { event.preventDefault(); link.closest(".history-files-menu").remove(); open(currentFile()); }
  });
}

function bindSelectionFocus(root = document) {
  // 与原生工具列表一致：焦点只改变选中背景，不取消选择或执行链接。
  root.querySelectorAll(".side-content.tree, .changes-layout > .changes-list, .log-ref-panel > .tree, .commit-list, .log-detail-panel > .changed-files, .history-row, .settings-nav, .management-list, .branches-popover").forEach(region => {
    if (region.dataset.selectionFocusBound) return;
    region.dataset.selectionFocusBound = "true";
    region.tabIndex = 0;
    region.dataset.selectionRegion = "true";
    const update = () => {
      const focused = document.activeElement;
      const inactive = !region.contains(focused) || focused.matches('input, button, select, textarea, [role="button"]');
      const rows = region.matches(".selected") ? [region] : region.querySelectorAll(".selected");
      rows.forEach(row => row.classList.toggle("inactive", inactive));
    };
    region.addEventListener("focusin", update);
    region.addEventListener("focusout", () => queueMicrotask(update));
    region.addEventListener("click", () => queueMicrotask(update));
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
        const active = button.getAttribute("aria-label") === (unified ? "Unified" : "Side-by-side");
        button.classList.toggle("active", active);
        button.setAttribute("aria-pressed", String(active));
      });
      if (body && template) {
        body.classList.toggle("diff-unified-body", unified);
        body.innerHTML = unified ? template.innerHTML : split;
        if (boundaryDismissed) body.querySelector(".diff-boundary-hint")?.remove();
      }
    };
    // 文件历史右侧的预览是**另一份**正文（与编辑器正文可以同时开着、各自处于不同模式）：
    // 它的显示模式回调走预览自己的入口，绝不能再调 `__augitLoadDiffMode`（那会去改编辑器正文）。
    const previewPane = !!layout.closest("[data-live-file-history-preview]");
    buttons.forEach(button => button.addEventListener("click", () => {
      const unified = button.getAttribute("aria-label") === "Unified";
      // 重复点击当前显示模式：不排版、不查询（规格 §6.3）。`update()` 本身对同一模式是空操作，
      // 但下面还会回调外壳的 `loadDiff()`（它会刷新编辑区 = 白排一次版）—— 因此这里直接返回。
      if (layout.dataset.diffMode === (unified ? "unified" : "side-by-side")) return;
      update(unified);
      // 外壳存在时同步显示模式：相同内容只重新排版，不重新查询 Git（§6.3）。
      const hook = previewPane ? window.__augitLoadFileHistoryPreviewMode : window.__augitLoadDiffMode;
      const hasBody = previewPane
        ? !!(window.__augitLive && window.__augitLive.fileHistoryPreview)
        : !!(window.__augitLive && window.__augitLive.diff);
      if (hasBody && typeof hook === "function") hook(unified ? "unified" : "side-by-side");
    }));
    // 实时外壳下显示模式是**会话状态**（`live.diffMode`，由 `loadDiff()`／`switchDiffMode()` 维护）：
    // 正文重绘会换掉整个布局元素，不再按状态回填的话，切到单栏后一次刷新就退回双栏（第 224 轮修正）。
    // 这里只改 DOM、不回调宿主（回调只在点击监听器里），所以不会与重绘互相触发。
    const liveMode = previewPane
      ? (window.__augitLive && window.__augitLive.fileHistoryPreview && window.__augitLive.fileHistoryPreview.mode)
      : (window.__augitLive && window.__augitLive.diffMode);
    update(liveMode
      ? liveMode === "unified"
      : new URLSearchParams(window.location.search).get("diffMode") === "unified");
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
  const ordinaryTabs = [...tabs.querySelectorAll(".editor-tab")].filter(tab => !tab.textContent.trim().startsWith("Commit:"));
  const documents = new Map();
  let documentTab = ordinaryTabs.find(tab => tab.classList.contains("active")) || ordinaryTabs.at(-1);
  let activeComparison = null, preview = null;
  if (!content.querySelector(".diff-layout")) documents.set(documentTab, content.firstElementChild);
  const groupFor = row => groups.find(group => group.dataset.group === row.dataset.group);
  const selected = () => list.querySelector(".check-row.selected");
  const selectedFile = () => list.querySelector(".change-file-row.selected:not([hidden])");
  const updateActions = () => {
    const enabled = !!selectedFile();
    side.querySelectorAll('[data-action="show-change-diff"], .toolbar [aria-label="Rollback"]').forEach(button => {
      button.disabled = !enabled;
      // 禁用原因必须与状态一致（规格 §10.3）：解禁后还挂着"先在改动列表里选择一个文件"
      // 是错的说明，用户悬停会看到与实际状态矛盾的文字。
      if (enabled) button.removeAttribute("title");
      else button.setAttribute("title", button.dataset.disabledReason || "Select a file in Changes first.");
    });
  };
  const setStatus = (path, comparison) => {
    const label = status.querySelector(".status-path");
    label.textContent = ["Augit", ...path.split("/")].join("  ›  ");
    label.title = "D:\\github\\Augit\\" + path.replaceAll("/", "\\");
    status.querySelector(".status-fields").innerHTML = (comparison ? ["Read-only"] : ["UTF-8", "LF", "Read-only"])
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
          + escapeHtml(name) + '　Read-only</span></div><div class="code-view" tabindex="0">'
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
    comparison.path = row.dataset.path;
    // 实时外壳的标签栏由 live.tabs 渲染，比较对象没有自己的 tab 节点（`createComparison`
    // 在 live 模式返回 `tab: null`）；此前无条件写 `comparison.tab.dataset` 会在
    // commit-changes 里每次点文件都抛 null 解引用，并跳过路径/标题更新。
    if (comparison.tab) {
      comparison.tab.dataset.path = comparison.path;
      comparison.tab.querySelector(".change-tab-caption").textContent = "Commit: " + row.dataset.file;
      comparison.tab.title = comparison.path;
    }
    const holder = document.createElement("template");
    holder.innerHTML = diffView(false, "ready", false, false, comparison.path);
    const view = holder.content.firstElementChild, previous = comparison.view;
    const mode = previous?.dataset.diffMode;
    view.hidden = activeComparison !== comparison;
    if (previous) previous.replaceWith(view); else content.append(view);
    comparison.view = view;
    bindDiffModes(content);
    if (mode === "unified") view.querySelector('[aria-label="Unified"]').click();
    view.querySelector(".diff-toolbar > .file-status-modified").textContent =
      (changesSamples.findIndex(file => file.path === comparison.path) + 1) + "/" + changesSamples.length + " files";
    const position = changesSamples.findIndex(file => file.path === comparison.path);
    for (const [label, offset] of [["Previous File", -1], ["Next File", 1]]) {
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
    tab.innerHTML = icon("git-compare-arrows") + '<span class="change-tab-caption">Commit: '
      + escapeHtml(row.dataset.file) + '</span><button type="button" class="tab-close" aria-label="Close Comparison">'
      + icon("close-small") + "</button>";
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
    chevron.setAttribute("aria-label", (collapsed ? "Expand " : "Collapse ") + group.dataset.group);
    rows.filter(row => row.dataset.group === group.dataset.group).forEach(row => { row.hidden = collapsed; });
    if (collapsed && selected()?.hidden) select(group);
  };
  ordinaryTabs.forEach(tab => tab.addEventListener("click", event => { event.preventDefault(); showDocument(tab); }));
  const initialTab = [...tabs.querySelectorAll(".editor-tab")].find(tab => tab.textContent.trim().startsWith("Commit:"));
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
  side.querySelector('[aria-label="Expand All"]').addEventListener("click", () => {
    groups.filter(group => group.getAttribute("aria-expanded") === "false").forEach(toggleCollapsed);
  });
  // 样本没有后台 Git 查询；无变化刷新不重建任何列表或正文。
  side.querySelector('[aria-label="Refresh"]').addEventListener("click", updateActions);
  const message = side.querySelector(".message-field"), amend = side.querySelector('.commit-amend .fake-check');
  let originalDraft = message.value;
  amend.addEventListener("click", () => {
    const checked = amend.getAttribute("aria-checked") !== "true";
    if (checked) originalDraft = message.value;
    setCheck(amend, String(checked));
    // 实时外壳下由 `live-data.js` 的 Amend 处理器负责真实行为（调宿主读上一次提交信息，
    // 并按权威"仅当用户没改过信息时才覆盖/恢复"）；这里的示例信息会把用户真实输入冲掉，
    // 因此 live 下只翻转勾选状态，不写样例文本。视觉稿样例（无 live）仍用这条示例。
    if (window.__augitLive) return;
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

/**
 * 空提交信息的确认（权威，第 135 轮采集）。
 *
 * 权威：`platform/vcs-impl/src/com/intellij/vcs/commit/SingleChangeListCommitWorkflowHandler.kt:117-122`
 *   checkCommit = super.checkCommit(...) && (
 *     getCommitMessage().isNotEmpty() || ui.confirmCommitWithEmptyMessage())
 * 即**空信息不是阻断条件**，而是弹一个确认；用户确认后**照常提交**，取消才回到编辑。
 * 文案取 `VcsBundle.properties:36-37`（title `No Commit Message` / text `Add a summary of your
 * changes in the commit message field`）与 `:35` 的 `action.commit.anyway.text`（"… Anyway"），
 * 按 Augit 的中文界面本地化，语义逐条对应。
 *
 * 实时侧（`live-data.js` 的 `commitSelectedChanges`）与视觉稿共用本函数，避免各写一套。
 */
function confirmCommitWithEmptyMessage(onConfirm, onCancel) {
  document.querySelectorAll("[data-commit-empty-message]").forEach(node => node.remove());
  const host = document.querySelector(".app-frame") || document.body;
  host.insertAdjacentHTML("beforeend", dialog(
    "No Commit Message",
    '<div class="info-block"><p>Enter a summary in the commit message field.</p></div>',
    '<button type="button" class="secondary-button" data-commit-empty-cancel="1">Cancel</button>'
      + '<button type="button" class="primary-button" data-commit-empty-anyway="1">Commit Anyway</button>',
    false, "commit-empty-message-dialog"));
  const layer = host.lastElementChild;
  if (!layer) return;
  layer.setAttribute("data-commit-empty-message", "");
  const close = () => layer.remove();
  const cancel = layer.querySelector("[data-commit-empty-cancel]");
  const anyway = layer.querySelector("[data-commit-empty-anyway]");
  // 取消 = 不提交并把焦点退回提交信息栏（权威的 cancel 语义）。
  if (cancel) cancel.addEventListener("click", () => { close(); if (onCancel) onCancel(); });
  if (anyway) anyway.addEventListener("click", () => { close(); onConfirm(); });
  if (anyway) anyway.focus();
}

// 真宿主将同一会话裁切到独立窗口；浏览器预览沿用宿主内拖动。
function bindDialogMovement() {
  if (window.__augitSettingsSurface) return;
  if (window.__augitDialogMovementBound) return;
  window.__augitDialogMovementBound = true;
  let drag = null;
  const clamp = (dialog, left, top) => {
    const box = dialog.getBoundingClientRect();
    Object.assign(dialog.style, { left: `${Math.max(0, Math.min(left, innerWidth - box.width))}px`,
      top: `${Math.max(0, Math.min(top, innerHeight - box.height))}px`, transform: 'none' });
    dialog.dataset.moved = 'true';
  };
  const finish = () => {
    if (!drag) return;
    drag.observer.disconnect();
    if (drag.header.hasPointerCapture(drag.pointer)) drag.header.releasePointerCapture(drag.pointer);
    drag = null;
  };
  document.addEventListener('pointerdown', event => {
    const header = event.target.closest('.dialog-header');
    if (!header || event.button !== 0 || event.target.closest('button, a, input')) return;
    const dialog = header.closest('.dialog');
    if (!dialog || dialog.getAttribute('aria-disabled') === 'true') return;
    if (window.__augitNativeModalActive) {
      window.__augitNativeModalDrag?.();
      event.preventDefault();
      return;
    }
    const box = dialog.getBoundingClientRect();
    finish();
    const observer = new MutationObserver(() => { if (!dialog.isConnected) finish(); });
    observer.observe(document.body, { childList: true, subtree: true });
    drag = { dialog, header, observer, pointer: event.pointerId, x: event.clientX, y: event.clientY, left: box.left, top: box.top };
    header.setPointerCapture(event.pointerId);
    event.preventDefault();
  });
  document.addEventListener('pointermove', event => {
    if (drag && event.pointerId === drag.pointer) clamp(drag.dialog, drag.left + event.clientX - drag.x, drag.top + event.clientY - drag.y);
  });
  for (const event of ['pointerup', 'pointercancel', 'lostpointercapture']) document.addEventListener(event, finish);
  document.addEventListener('keydown', event => {
    if (drag && event.key === 'Escape') { finish(); event.preventDefault(); event.stopImmediatePropagation(); }
  }, true);
  window.addEventListener('resize', () => {
    finish();
    document.querySelectorAll('.dialog[data-moved]').forEach(dialog => { const box = dialog.getBoundingClientRect(); clamp(dialog, box.left, box.top); });
  });
  window.addEventListener('pagehide', finish);
}

function bindInteractions() {
  bindDialogMovement();
  bindStashDialog();
  bindCloneDialog();
  bindResetDialog();
  bindRollbackDialog();
  bindPushDialog();
  document.querySelectorAll(".commit-message-box").forEach(box => {
    const input = box.querySelector("textarea");
    const feedback = box.querySelector(".commit-feedback");
    const showError = error => {
      feedback.textContent = error || "Commit message";
      feedback.title = feedback.textContent;
      feedback.classList.toggle("error", !!error);
    };
    input.addEventListener("input", () => showError(""));
    box.closest(".commit-box").querySelectorAll(".commit-actions > .primary-button, .commit-actions > .secondary-button").forEach(button => {
      button.addEventListener("click", event => {
        // 已通过确认：放行这一次（`__augitCommitAnyway` 由确认回调置位，见下）。
        if (window.__augitCommitAnyway) { delete window.__augitCommitAnyway; return; }
        if (input.value.trim()) return;
        // 空信息**不阻断**：弹确认；确认后重新触发一次点击并放行。
        // 原实现是 `preventDefault()` + 报错且**永不继续**，属旧交互（见
        // docs/nui-behavior/12-commit-changes.md §5）。
        event.preventDefault();
        event.stopPropagation();
        confirmCommitWithEmptyMessage(() => {
          window.__augitCommitAnyway = true;
          button.click();
          delete window.__augitCommitAnyway;
        }, () => input.focus({ preventScroll: true }));
      });
    });
  });
  bindDiffModes();
  bindChangesWorkflow();
  bindJsonModes();
  bindMarkdownModes();
  if (typeof bindCurrentFind === 'function') bindCurrentFind();
  if (typeof bindDiffFind === 'function') bindDiffFind();
  bindBlame();
  if (typeof bindImagePreview === "function") bindImagePreview();
  measureCodeViews();
  bindSelectionFocus();
  bindHistoryToolbar();
  bindHistoryLayout();
  bindHistoryDetails();
  bindHistoryComparisonFollow();
  document.querySelectorAll("[aria-label]").forEach((element) => {
    if (!element.matches('.rail-button, .blame-row') && !element.title && (element.matches("button") || element.matches("a"))) {
      element.title = element.getAttribute("aria-label");
    }
  });
  bindRailHelpTooltip();

  // 汉堡按钮与内嵌菜单的 Esc 都挂在 document 上（标题栏节点会被区域刷新整块替换，
  // 挂节点的监听会随之失效）。但本函数每次渲染都会被调用，所以必须有幂等守卫：
  // 点击处理按"当前有没有菜单条"取反，两份监听叠加就会互相抵消 ——
  // 实测一次外部变化刷新之后点汉堡按钮毫无反应（规格 §5.1 要求它原位显示五个文字入口）。
  if (!window.__augitMainMenuBound) {
    window.__augitMainMenuBound = true;
    document.addEventListener("click", (event) => {
      const button = event.target.closest("[data-action='menu']");
      if (!button) return;

      const title = document.querySelector(".titlebar");
      if (title.querySelector(".main-menu-bar")) {
        title.outerHTML = titlebar();
      } else {
        title.innerHTML = `<div class="brand-mark" aria-label="Augit">A</div><button class="top-button" aria-label="Close Main Menu" data-action="menu">${icon("menu")}</button><nav class="main-menu-bar"><a class="main-menu-entry" href="workspace-open.html">File</a><a class="main-menu-entry" href="main-project.html">View</a><a class="main-menu-entry" href="git-history.html">Git</a><a class="main-menu-entry" href="terminal.html">Terminal</a><a class="main-menu-entry" href="settings.html">Settings</a></nav><span></span><span></span><nav class="window-actions" aria-label="Window Tools">${windowActionIcons()}</nav>`;
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

// 工具窗出现/消失只改变主网格的工具区域，保留编辑器节点、原生选区和滚动容器。
// 切换底部区域时也保留未变化的侧栏，避免项目树滚动与选中上下文被重建。
window.__augitRenderToolLayout = () => {
  const main = app?.querySelector('.app-main');
  const fragment = renderRegions();
  const next = fragment.querySelector('.app-main');
  if (!main || !next) return;
  disposeRegionBindings();
  for (const [key, selector] of [['side', '.side-tool'], ['bottom', '.bottom-tool']]) {
    if (main.dataset[key] === next.dataset[key]) continue;
    const current = main.querySelector(`:scope > ${selector}`);
    const replacement = next.querySelector(`:scope > ${selector}`);
    if (current && replacement) current.replaceWith(replacement);
    else if (current) current.remove();
    else if (replacement) main.appendChild(replacement);
  }
  main.className = next.className;
  main.dataset.side = next.dataset.side;
  main.dataset.bottom = next.dataset.bottom;
  main.querySelector('.tool-rail')?.replaceWith(next.querySelector('.tool-rail'));
  bindInteractions();
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
  // 提示的区域是**常驻容器**而不是 .toast 本身：区域替换只在目标与替换都存在时生效，
  // 用 .toast 当区域时"清空提示"会因为没有替换节点而被跳过，旧提示永远留在屏幕上。
  toast: ".toast-layer",
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
  toast: fragment => fragment.querySelector(".toast-layer"),
};

/**
 * 局部更新：只替换指定区域的内容，保留其余区域的 DOM 实例。
 * 整页重绘会丢掉焦点、滚动位置、展开状态与已建立的组件实例
 * （终端、搜索输入框等），规格 §6 要求局部状态变化不得重建全局结构。
 */
window.__augitRenderRegions = (...names) => {
  if (!app || names.length === 0) return;
  // 区域入口也可能被宿主或测试直接调用；替换项目树前保留树行焦点，
  // 根行路径为空字符串时必须按“存在”处理，不能用真假判断。
  const active = document.activeElement;
  const activeTreeRow = active && active.closest
    ? active.closest(".side-content.tree .tree-row[data-tree-path]")
    : null;
  const focusedTreePath = names.includes("side") && activeTreeRow
    ? activeTreeRow.dataset.treePath
    : null;
  const focusedFilterKey = names.includes('bottomTool')
    ? active?.closest?.('.history-filters [data-filter-key]')?.dataset.filterKey : null;
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
  if (focusedTreePath !== null) {
    const replacementRow = [...document.querySelectorAll(".side-content.tree .tree-row[data-tree-path]")]
      .find((row) => row.dataset.treePath === focusedTreePath);
    replacementRow?.focus({ preventScroll: true });
  }
  if (focusedFilterKey) {
    let control=document.querySelector(`.history-filters > [data-filter-key="${CSS.escape(focusedFilterKey)}"]`);
    if(control?.hidden){const overflow=document.querySelector('.history-filter-overflow');if(overflow){overflow.open=true;control=overflow.querySelector(`[data-filter-key="${CSS.escape(focusedFilterKey)}"]`);}}
    control?.focus({preventScroll:true});
  }
  void applyTypographyPreview();
};
window.__augitScene = () => scene;
window.__augitIcon = icon;
window.__augitFolderIcon = treeFolderIcon;
window.__augitFileIcon = fileTypeIcon;
window.__augitHistoryFiles = historyFilesHtml;
window.__augitBind = bindInteractions;
// 只重跑 Markdown 绑定（它自己会先释放上一次绑定）：实时侧在**不重绘正文**的前提下更新
// 预览区顶部的提示文案时用它，避免 150ms 提示把原文滚动位置与对照比例冲掉（规格 §7.3）。
window.__augitBindMarkdown = bindMarkdownModes;
// 文件历史预览的正文由实时层**定点替换**（不整页/整区重绘，避免丢掉列表焦点）⇒ 替换后要能单独重挂差异模式按钮。
window.__augitBindDiffModes = bindDiffModes;
// 文件历史右侧的只读比较视图（实时层定点替换详情面板时按状态重建它）。
window.__augitFileHistoryPreviewView = liveFileHistoryPreviewView;

if (app) {
  app.innerHTML = renderScene();
  bindInteractions();
  if (scene === "go-to-line") document.querySelector("#prompt-line")?.focus();
  if (scene === "quick-open" || scene === "quick-open-empty") {
    document.querySelector(".search-overlay input")?.focus();
  }
  // 独立设置由启动器装配完整表单后统一度量，不先对空容器重复加载字体。
  if (!window.__augitSettingsSurface) void applyTypographyPreview();
}
