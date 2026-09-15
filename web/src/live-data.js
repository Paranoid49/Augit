// 外壳启动器：在 WebView2 内先取真实工作区数据，再交给 visual mockup 的构建器渲染。
// 没有宿主（浏览器里直接打开视觉稿）时不做任何事，视觉稿保持原样。
//
// 性能约束：桥接调用每次跨进程，必须尽量少而并行。
// 因此首屏只取工作区信息、Git 状态与根目录一层；更深层级在展开时再取。

import { hasHost, invoke } from "./bridge.js";
import { renderMarkdown } from "./markdown.js";

const MAX_ENTRIES_PER_DIRECTORY = 200;
// 项目树隐藏构建产物与本地工具目录：目录名命中列表，或名称以点开头（如 .git/.idea/.vs/.tmp）。
// 以点开头的文件（如 .gitignore、.editorconfig）保留，它们是有意义的项目文件。
const SKIPPED_DIRECTORIES = new Set(["bin", "obj", "node_modules", "TestResults", "publish", "artifacts", ".git", ".idea", ".vs", ".tmp"]);

function isVisibleEntry(entry) {
  if (SKIPPED_DIRECTORIES.has(entry.name)) return false;
  if (entry.isDirectory && entry.name.startsWith(".")) return false;
  return true;
}

// 目录树状态：展开集合与子项缓存都保存在这里，
// 这样打开文件触发整页重绘时不会丢失展开状态，也不会重复向宿主请求。
let latestStatus = null;
let latestHistory = null;
const expandedPaths = new Set([""]);
const childrenByPath = new Map();

/** 列举一个目录并转换成树行（只取一层，展开时再调用）。 */
async function loadChildren(parentPath, parentDepth) {
  if (childrenByPath.has(parentPath)) {
    return childrenByPath.get(parentPath);
  }

  const rows = [];
  let listing;
  try {
    listing = await invoke("workspace/list", { path: parentPath }, 30000);
  } catch {
    return rows;
  }

  const entries = (listing.entries || []).filter(isVisibleEntry);
  const directories = entries.filter((entry) => entry.isDirectory);
  const files = entries.filter((entry) => !entry.isDirectory);
  const childDepth = parentDepth + 1;
  for (const entry of [...directories, ...files].slice(0, MAX_ENTRIES_PER_DIRECTORY)) {
    rows.push({
      name: entry.name,
      path: entry.path,
      depth: childDepth,
      isDirectory: entry.isDirectory,
      hasChildren: entry.isDirectory,
      expanded: false,
    });
  }

  childrenByPath.set(parentPath, rows);
  return rows;
}

/** 只从缓存拼装可见树：展开集合决定哪些层级可见，不发起新的宿主请求。 */
function buildVisibleTree(rootName, rootPath) {
  const rows = [{
    name: rootName || "工作区",
    path: rootPath,
    depth: 0,
    isDirectory: true,
    hasChildren: true,
    expanded: true,
  }];
  const walk = (parentPath, depth) => {
    if (!expandedPaths.has(parentPath)) return;
    for (const child of childrenByPath.get(parentPath) || []) {
      rows.push({ ...child, depth: depth + 1, expanded: expandedPaths.has(child.path) });
      if (child.isDirectory) walk(child.path, depth + 1);
    }
  };
  walk(rootPath, 0);
  return rows;
}

async function loadDocument() {
  const marks = {};
  const started = performance.now();
  const mark = (name) => {
    marks[name] = Math.round(performance.now() - started);
  };
  try {
    // 首屏只等「工作区信息 + 根目录一层」；两者并行，通常几十毫秒内返回。
    const infoPromise = invoke("workspace/info");
    const rootPromise = invoke("workspace/list", { path: "" }, 30000).catch(() => null);
    const info = await infoPromise;
    mark("info");
    const listing = await rootPromise;
    mark("root");

    if (listing) {
      const entries = (listing.entries || []).filter(isVisibleEntry);
      const directories = entries.filter((entry) => entry.isDirectory);
      const files = entries.filter((entry) => !entry.isDirectory);
      childrenByPath.set("", [...directories, ...files].slice(0, MAX_ENTRIES_PER_DIRECTORY).map((entry) => ({
        name: entry.name,
        path: entry.path,
        depth: 1,
        isDirectory: entry.isDirectory,
        hasChildren: entry.isDirectory,
        expanded: false,
      })));
    }

    const tree = buildVisibleTree(info.name, "");

    window.__augitMarks = marks;
    const liveObject = {
      root: info.root,
      rootPath: "",
      name: info.name,
      // 标题栏显示工作区名；与树根的 name 分开命名，避免与文档名混淆。
      workspaceName: info.name,
      valid: info.valid,
      error: info.error,
      branch: null,
      isDetached: false,
      changeCount: 0,
      tree,
    };
    window.__augitLive = liveObject;
    applyStatus();
    applyHistory();
    return liveObject;
  } catch (error) {
    window.__augitError = "load-document:" + String(error && error.message || error);
    return null;
  }
}

/**
 * 取回文档内容。
 * 已知限制：WebView2 的消息桥无法可靠送达超过约 100 KB 的响应，大文件会超时；
 * 需要改为资源通道后再恢复支持（见 docs/ui-refactor-baseline.md）。
 */
async function fetchDocument(path) {
  return invoke("document/read", { path }, 30000);
}

/** Git 状态明显慢于目录列举，因此在界面出现后再补，不阻塞首屏。 */
async function loadStatus() {
  const started = performance.now();
  try {
    const status = await invoke("git/status", {}, 30000);
    window.__augitMarks = Object.assign(window.__augitMarks || {}, {
      status: Math.round(performance.now() - started),
    });
    if (!status || !status.available || !status.isRepository) return null;
    latestStatus = normalizeStatus(status);
    // 状态可能早于工作区数据到达，因此先缓存，再尝试附着到当前 live 对象。
    applyStatus();
    return status;
  } catch {
    window.__augitMarks = Object.assign(window.__augitMarks || {}, {
      statusError: Math.round(performance.now() - started),
    });
    return null;
  }
}

/**
 * 把宿主返回的 Git 状态整理成界面需要的形状。
 * 选中态是界面状态，默认「改动」全选、「未跟踪」不选，与视觉稿一致。
 */
function normalizeStatus(status) {
  const files = (status.files || []).map((file) => ({
    path: file.path,
    name: file.name || file.path.split("/").at(-1),
    directory: file.directory || file.path.split("/").slice(0, -1).join("/"),
    group: file.group,
    kind: file.kind || "Modified",
    staged: !!file.staged,
    workingTree: !!file.workingTree,
    checked: file.group === "Changes",
  }));
  files.sort((a, b) => a.group.localeCompare(b.group)
    || a.name.localeCompare(b.name, "en", { numeric: true, sensitivity: "base" }));
  return { branch: status.branch, isDetached: status.isDetached, files };
}

/**
 * 读取 Git 历史。与状态一样在首屏之后补取，不阻塞界面出现。
 */
async function loadHistory() {
  try {
    const history = await invoke("git/history", {}, 60000);
    if (!history || !history.available || !history.isRepository || !history.commits) {
      return null;
    }

    latestHistory = {
      head: history.head,
      branch: latestStatus ? latestStatus.branch : null,
      commits: history.commits.map((commit) => ({
        hash: commit.hash,
        fullHash: commit.fullHash,
        subject: commit.subject,
        author: commit.author,
        date: commit.date,
        references: commit.references || [],
        parents: commit.parents || [],
      })),
    };
    applyHistory();
    return latestHistory;
  } catch (error) {
    window.__augitError = "load-history:" + String(error && error.message || error);
    return null;
  }
}

/** 读取指定文件的 Blame；失败只记录，不影响其它视图。 */
async function loadBlame(path) {
  try {
    const blame = await invoke("git/blame", { path }, 60000);
    if (!blame || !blame.available || !blame.lines) return null;
    const live = window.__augitLive;
    if (live) {
      live.blame = {
        path: blame.path,
        lines: blame.lines.map((line) => ({
          number: line.number,
          hash: line.hash,
          fullHash: line.fullHash,
          author: line.author,
          date: line.date,
          summary: line.summary,
          content: line.content,
        })),
      };
      live.editor = "blame";
    }
    return live ? live.blame : null;
  } catch (error) {
    window.__augitError = "load-blame:" + String(error && error.message || error);
    return null;
  }
}

/**
 * 计算待推送信息：当前分支、上游引用与领先的提交。
 * 上游提交在已加载的历史窗口内时可直接切片得出；不在窗口内说明领先超过一页，
 * 此时只给出数量未知的提示，不猜测具体提交。
 */
function computePush(live) {
  if (!live || !live.status || !live.references || !live.history) return null;
  const branch = live.status.branch;
  if (!branch) return null;
  const current = (live.references.branches || []).find((item) => item.name === branch && !item.isRemote);
  const upstream = current && current.upstream ? current.upstream : null;
  if (!upstream) {
    return { branch, upstream: null, commits: [] };
  }

  const upstreamBranch = (live.references.branches || []).find((item) => item.name === upstream);
  const commits = live.history.commits || [];
  const index = upstreamBranch
    ? commits.findIndex((commit) => commit.fullHash === upstreamBranch.commitHash)
    : -1;
  return {
    branch,
    upstream,
    commits: index >= 0 ? commits.slice(0, index) : [],
    truncated: index < 0,
  };
}

/**
 * 搜索浮层：快速打开按文件名，全仓搜索按内容。
 * 输入去抖 180 毫秒，避免每敲一个字符都拉起一次 ripgrep。
 */
let searchToken = 0;
async function runSearch(kind, query, options) {
  const live = window.__augitLive;
  if (!live) return;
  const token = ++searchToken;
  const method = kind === "repository" ? "search/text" : "search/files";
  try {
    const result = await invoke(method, { query, ...(options || {}) }, 30000);
    // 晚到的旧查询不得覆盖新查询的结果。
    if (token !== searchToken) return;
    live.search = {
      kind,
      query,
      options: options || {},
      matches: result && result.matches ? result.matches : [],
      notice: result && result.notice ? result.notice : "",
      truncated: !!(result && result.truncated),
    };
    window.__augitSearchReady = true;
    window.__augitRender();
    bindSearchOverlay(kind);
  } catch (error) {
    if (token !== searchToken) return;
    window.__augitError = "search:" + String(error && error.message || error);
  }
}

/**
 * 绑定浮层输入与键盘。整页重绘会替换节点，因此每次重绘后重新调用。
 * Esc 关闭浮层并恢复原焦点；上下方向键移动选择，Enter 打开。
 */
function bindSearchOverlay(kind) {
  const input = document.querySelector(".search-overlay .search-field");
  if (!input || input.dataset.searchBound === "true") return;
  input.dataset.searchBound = "true";
  let timer = 0;
  input.addEventListener("input", () => {
    window.clearTimeout(timer);
    const value = input.value;
    const options = currentSearchOptions();
    timer = window.setTimeout(() => void runSearch(kind, value, options), 180);
  });
  input.addEventListener("keydown", (event) => {
    const results = [...document.querySelectorAll(".search-result")];
    const index = results.findIndex((row) => row.classList.contains("selected"));
    if (event.key === "Escape") {
      event.preventDefault();
      document.querySelector(".search-overlay")?.remove();
      return;
    }

    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      if (results.length === 0) return;
      const next = Math.max(0, Math.min(results.length - 1, index + (event.key === "ArrowDown" ? 1 : -1)));
      results.forEach((row, i) => row.classList.toggle("selected", i === next));
      results[next].scrollIntoView({ block: "nearest" });
      return;
    }

    if (event.key === "Enter" && results.length > 0) {
      event.preventDefault();
      const row = results[Math.max(0, index)];
      const path = row.dataset.searchPath;
      if (path) void openDocument(path);
    }
  });
  input.focus();
}

/** 读取浮层上的搜索开关当前状态。 */
function currentSearchOptions() {
  const pressed = (label) => document.querySelector(`.search-option[aria-label="${label}"]`)?.getAttribute("aria-pressed") === "true";
  return {
    matchCase: pressed("区分大小写"),
    matchWholeWord: pressed("全字匹配"),
    useRegularExpression: pressed("正则表达式"),
    includeIgnoredFiles: !!document.querySelector(".search-ignored input")?.checked,
  };
}

/** 点击结果行打开对应文件。 */
document.addEventListener("click", (event) => {
  const row = event.target.closest && event.target.closest(".search-result");
  if (!row) return;
  const path = row.dataset.searchPath;
  if (!path) return;
  event.preventDefault();
  void openDocument(path);
}, true);

/** 读取远端、分支标签、Stash 与 Worktree，供管理窗口使用。 */
async function loadReferences() {
  try {
    const [remotes, references, stashes, worktrees] = await Promise.all([
      invoke("git/remotes", {}, 30000).catch(() => null),
      invoke("git/references", {}, 30000).catch(() => null),
      invoke("git/stashes", {}, 30000).catch(() => null),
      invoke("git/worktrees", {}, 30000).catch(() => null),
    ]);
    const live = window.__augitLive;
    let changed = false;
    if (live) {
      if (remotes && remotes.available) { live.remotes = remotes; changed = true; }
      if (references && references.available) { live.references = references; changed = true; }
      if (stashes && stashes.available) { live.stashes = stashes; changed = true; }
      if (worktrees && worktrees.available) { live.worktrees = worktrees; changed = true; }
    }

    // 管理窗口依赖这些数据，因此到达后补一次重绘；重绘会清空提交详情区，需要重新补齐。
    if (changed && typeof window.__augitRender === "function") {
      refreshPush();
      window.__augitRender();
      reattachTerminal();
      bindSettingsSave();
      bindConflictSave();
      if (window.__augitLive && window.__augitLive.search) bindSearchOverlay(window.__augitLive.search.kind);
      void refreshCommitDetails();
    }
  } catch (error) {
    window.__augitError = "load-references:" + String(error && error.message || error);
  } finally {
    // 即使失败也要放行，避免把界面卡在等待状态。
    window.__augitRefsReady = true;
  }
}

// 终端：xterm.js 负责渲染，ConPTY 会话由宿主管理，输出按偏移量增量拉取。
let terminalInstance = null;
let terminalFitAddon = null;
let terminalOffset = 0;
let terminalTimer = 0;
let terminalReady = false;

/**
 * 整页重绘会替换终端宿主元素，但 xterm 实例与会话必须保留，
 * 因此重绘后把 xterm 自己的 DOM 节点搬回新的宿主里。
 */
function reattachTerminal() {
  if (!terminalInstance || !terminalReady) return;
  const host = document.querySelector('.terminal-view');
  if (!host) return;
  const element = terminalInstance.element;
  if (!element) return;
  if (element.parentElement !== host) {
    host.innerHTML = '';
    host.appendChild(element);
  }

  try { terminalFitAddon.fit(); } catch { /* 宿主未完成布局时忽略 */ }
}

/** 启动内置终端并接上输出轮询。重复调用时复用已有会话。 */
async function startTerminal() {
  const host = document.querySelector('.terminal-view');
  if (!host || typeof window.Terminal !== 'function') return null;
  if (terminalInstance && terminalReady) return terminalInstance;

  terminalInstance = new window.Terminal({
    allowProposedApi: false,
    convertEol: false,
    cursorBlink: true,
    fontFamily: 'Cascadia Mono, Consolas, monospace',
    fontSize: 13,
    lineHeight: 1.7,
    scrollback: 2000,
  });
  terminalFitAddon = new window.FitAddon.FitAddon();
  terminalInstance.loadAddon(terminalFitAddon);
  host.innerHTML = '';
  terminalInstance.open(host);
  try { terminalFitAddon.fit(); } catch { /* 宿主尚未布局时忽略 */ }

  terminalInstance.onData((data) => {
    void invoke('terminal/write', { data }, 10000).catch(() => {});
  });
  terminalInstance.onResize((size) => {
    void invoke('terminal/resize', { columns: size.cols, rows: size.rows }, 10000).catch(() => {});
  });

  const started = await invoke('terminal/start', {
    columns: terminalInstance.cols,
    rows: terminalInstance.rows,
  }, 30000);
  if (!started || !started.available) {
    window.__augitError = 'terminal-start:' + String(started && started.reason ? started.reason : 'unavailable');
    return null;
  }

  terminalReady = true;
  window.__augitTerminalShell = started.displayName;
  pollTerminal();
  return terminalInstance;
}

/** 增量拉取终端输出；会话结束后停止轮询。 */
function pollTerminal() {
  if (terminalTimer !== 0) return;
  terminalTimer = window.setInterval(async () => {
    if (!terminalInstance) return;
    try {
      const chunk = await invoke('terminal/read', { offset: terminalOffset }, 10000);
      if (chunk && typeof chunk.data === 'string' && chunk.data.length > 0) {
        terminalInstance.write(chunk.data);
      }
      if (chunk && typeof chunk.offset === 'number') terminalOffset = chunk.offset;
      if (chunk && (chunk.exited || !chunk.running)) {
        window.clearInterval(terminalTimer);
        terminalTimer = 0;
        window.__augitTerminalExited = true;
      }
    } catch {
      // 单次读取失败不终止轮询，下次重试。
    }
  }, 60);
}

/** 结束会话并停止轮询。 */
async function stopTerminal() {
  if (terminalTimer !== 0) {
    window.clearInterval(terminalTimer);
    terminalTimer = 0;
  }
  terminalReady = false;
  await invoke('terminal/stop', {}, 15000).catch(() => {});
}

/** 读取工作区中某个文件的差异，供编辑器差异视图使用。 *//** 读取工作区中某个文件的差异，供编辑器差异视图使用。 */
async function loadDiff(path) {
  try {
    const started = performance.now();
    const diff = await invoke("git/diff", { path }, 30000);
    window.__augitDiffTrace = `ok ms=${Math.round(performance.now() - started)} available=${diff && diff.available} rows=${diff && diff.rows ? diff.rows.length : -1}`;
    const live = window.__augitLive;
    if (live) {
      live.diff = diff && diff.available ? diff : null;


      // 有差异时切到差异视图；无差异时保留当前文档视图。
      if (live.diff) live.editor = "diff";
      window.__augitDiffReady = true;
    }
    return live ? live.diff : null;
  } catch (error) {
    window.__augitDiffTrace = "threw:" + String(error && error.message || error);
    window.__augitError = "load-diff:" + String(error && error.message || error);
    return null;
  }
}

/** 读取设置并挂上保存动作。 */
async function loadSettings() {
  try {
    const settings = await invoke("settings/read", {}, 15000);
    const live = window.__augitLive;
    if (live && settings) {
      live.settings = settings;
      window.__augitSettingsReady = true;
    }
  } catch (error) {
    window.__augitError = "load-settings:" + String(error && error.message || error);
  }
}

/**
 * 收集设置窗口里带 data-setting 的字段并写回。
 * 只提交界面上真实存在的字段，未出现的字段由宿主保留原值。
 */
async function saveSettings() {
  const payload = {};
  for (const element of document.querySelectorAll("[data-setting]")) {
    const key = element.dataset.setting;
    if (element.type === "number") {
      const value = Number(element.value);
      if (Number.isFinite(value)) payload[key] = value;
    } else {
      payload[key] = element.value;
    }
  }

  const saved = await invoke("settings/write", payload, 15000);
  if (!saved || !saved.saved) throw new Error("设置未能保存");
  window.__augitSettingsSaved = true;
  return saved;
}

/**
 * 设置窗口的保存动作。
 * 用文档级委托而不是直接绑定按钮：整页重绘会替换对话框节点，
 * 直接绑定会随着节点替换失效（这是本轮实际踩到的问题）。
 * 底栏顺序是「取消 / 应用 / 确定」，只对后两个写回设置。
 */
function bindSettingsSave() {
  if (window.__augitSettingsDelegated) return;
  window.__augitSettingsDelegated = true;
  document.addEventListener("click", (event) => {
    const live = window.__augitLive;
    if (!live || !live.settings) return;
    const dialog = event.target.closest && event.target.closest(".dialog.dialog-xl");
    if (!dialog) return;
    const button = event.target.closest(".dialog-footer .secondary-button, .dialog-footer .primary-button");
    if (!button) return;
    const buttons = [...dialog.querySelectorAll(".dialog-footer .secondary-button, .dialog-footer .primary-button")];
    if (buttons.indexOf(button) === 0) return;

    event.preventDefault();
    void saveSettings().catch((error) => {
      window.__augitError = "save-settings:" + String(error && error.message || error);
    });
  }, true);
}

/** 读取当前冲突会话与冲突文件列表。 */
async function loadConflicts() {
  try {
    const conflicts = await invoke("git/conflicts", {}, 30000);
    const live = window.__augitLive;
    if (live && conflicts && conflicts.available) {
      live.conflicts = conflicts;
      window.__augitConflictsReady = true;
    }
  } catch (error) {
    window.__augitError = "load-conflicts:" + String(error && error.message || error);
  }
}

/** 读取单个冲突文件的三栏内容；结果栏可编辑。 */
async function loadConflict(path) {
  try {
    const conflict = await invoke("git/conflict-load", { path }, 30000);
    const live = window.__augitLive;
    if (live && conflict && conflict.available) {
      live.conflict = conflict;
    }
    return live ? live.conflict : null;
  } catch (error) {
    window.__augitError = "load-conflict:" + String(error && error.message || error);
    return null;
  }
}

/** 保存冲突解决结果：把结果栏文本写回文件并标记已解决。 */
async function saveConflict(path, resultText) {
  const saved = await invoke("git/conflict-save", { path, resultText }, 30000);
  if (!saved || !saved.available) {
    throw new Error(saved && saved.reason ? saved.reason : "保存失败");
  }

  return saved;
}

/** 发布待推送信息，供 Push 对话框使用。 *//** 发布待推送信息，供 Push 对话框使用。 */
function refreshPush() {
  const live = window.__augitLive;
  if (!live) return;
  live.push = computePush(live);
}

/**
 * 重新绑定冲突解决器的保存动作。整页重绘会替换按钮，因此每次重绘后都要调用。
 */
function bindConflictSave() {
  const button = document.querySelector("[data-conflict-save]");
  const live = window.__augitLive;
  if (!button || !live || !live.conflict) return;
  if (button.dataset.bound === "true") return;
  button.dataset.bound = "true";
  button.addEventListener("click", async () => {
    const block = document.querySelector(".conflict-column.result .conflict-block");
    if (!block) return;
    button.disabled = true;
    try {
      await saveConflict(live.conflict.path, block.innerText);
      window.__augitConflictSaved = live.conflict.path;
      button.textContent = "已标记为已解决";
    } catch (error) {
      window.__augitError = "save-conflict:" + String(error && error.message || error);
      button.disabled = false;
    }
  });
}

/** 转义为可安全插入 HTML 的文本。 */
function escapeText(value) {
  return String(value ?? "").replace(/[&<>"']/g, (character) => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
  }[character]));
}

/**
 * 在当前渲染结果上补齐提交详情。整页重绘会清空详情区，
 * 因此每次重绘后都要调用它；已加载同一个提交时跳过，避免重复请求。
 */
async function refreshCommitDetails() {
  const live = window.__augitLive;
  if (!live || !live.history || live.history.commits.length === 0) return;
  const selected = document.querySelector('.commit-row[aria-selected="true"]');
  const revision = selected && selected.dataset.fullHash
    ? selected.dataset.fullHash
    : live.history.commits[0].fullHash;
  if (window.__augitCommitLoaded === revision
      && document.querySelector('[data-live-changed-files] .tree-row')) {
    return;
  }

  await loadCommitDetails(revision);
}

/**
 * 读取单个提交的详情并填入底部日志的详情区。
 * 提交选择由 mockup 的既有绑定派发 history-commit-selected 事件触发。
 */
async function loadCommitDetails(revision) {
  const panel = document.querySelector(".log-detail-panel");
  if (!panel) return;
  const filesHost = panel.querySelector("[data-live-changed-files]");
  const detailHost = panel.querySelector("[data-live-commit-detail]");
  if (!filesHost || !detailHost) return;

  try {
    const commit = await invoke("git/commit", { revision }, 30000);
    if (!commit || !commit.available) {
      filesHost.innerHTML = `<p class="commit-meta">${escapeText(commit && commit.reason ? commit.reason : "无法读取提交详情")}</p>`;
      return;
    }

    const files = commit.files || [];
    filesHost.innerHTML = files.length === 0
      ? `<p class="commit-meta">该提交没有变更文件</p>`
      : `<div class="tree-row"><span>${escapeText(String(files.length))} 个文件</span></div>`
        + files.map((file) => `<div class="tree-row depth-1 live-file-status-${escapeText(file.kind)}" data-history-path="${escapeText(file.path)}">${escapeText(file.name)}<span class="commit-meta">${escapeText(file.directory)}</span></div>`).join("");
    detailHost.innerHTML = `<h3>${escapeText(commit.subject)}</h3>`
      + `<div>${escapeText(commit.hash)} · ${escapeText(commit.author)} · ${escapeText(commit.date)}</div>`
      + (commit.body ? `<p class="commit-meta">${escapeText(commit.body)}</p>` : "");
    window.__augitCommitLoaded = commit.hash;
  } catch (error) {
    window.__augitError = "load-commit:" + String(error && error.message || error);
  }
}

/** 读取限定到某个文件的提交历史。 */
async function loadFileHistory(path) {
  try {
    const history = await invoke("git/file-history", { path }, 60000);
    if (!history || !history.available || !history.commits) return null;
    const live = window.__augitLive;
    if (live) {
      live.fileHistory = {
        path: history.path,
        commits: history.commits.map((commit) => ({
          hash: commit.hash,
          fullHash: commit.fullHash,
          subject: commit.subject,
          author: commit.author,
          date: commit.date,
        })),
      };
    }
    return live ? live.fileHistory : null;
  } catch (error) {
    window.__augitError = "load-file-history:" + String(error && error.message || error);
    return null;
  }
}

/** 历史与状态可能任意先后到达，因此统一在这里附着。 */
function applyHistory() {
  const live = window.__augitLive;
  if (!live || !latestHistory) return;
  if (!latestHistory.branch && live.branch) latestHistory.branch = live.branch;
  live.history = latestHistory;
}

/** 把已到达的 Git 状态附着到 live 对象；两个异步结果先后不定，谁后到都调用它。 */
function applyStatus() {
  const live = window.__augitLive;
  if (!live || !latestStatus) return;
  live.branch = latestStatus.branch;
  // 标题栏的工作区名来自宿主，缺失时保留视觉稿的默认值。
  if (latestStatus.workspaceName) live.workspaceName = latestStatus.workspaceName;
  live.isDetached = latestStatus.isDetached;
  live.changeCount = latestStatus.files.length;
  live.status = latestStatus;
}

async function boot() {
  let statusPromiseRef = Promise.resolve(null);
  let historyPromiseRef = Promise.resolve(null);
  const query = new URLSearchParams(window.location.search);
  const requestedDocument = query.get("open");
  const requestedBlame = query.get("blame");
  const requestedFileHistory = query.get("file-history");
  const requestedConflict = query.get("conflict");
  const requestedDiff = query.get("diff");

  const wantsTerminal = query.get("scene") === "terminal";
  const wantsSettings = query.get("scene") === "settings";
  const wantsClone = query.get("scene") === "clone";
  const searchScene = query.get("scene") === "quick-open" ? "quick"
    : query.get("scene") === "repository-search" ? "repository"
      : null;
  if (wantsClone && hasHost()) {
    // 把真实克隆交给视觉稿的 Clone 对话框调用：校验、焦点与冻结逻辑已在其中实现。
    window.__augitCloneRequest = (request) => invoke("git/clone", request, 600000);
  }
  if (hasHost()) {
    // 桥接异常不能阻塞界面：超时后回退视觉稿样例数据。
    statusPromiseRef = loadStatus();
    historyPromiseRef = loadHistory();
    window.__augitLive = await Promise.race([
      loadDocument(),
      new Promise((resolve) => setTimeout(() => resolve(null), 8000)),
    ]);
    if (window.__augitLive === null) {
      window.__augitError = (window.__augitError || "") + "|timeout";
    }
  }

  // 先加载构建器：openDocument 需要 __augitRender 才能把结果画出来。
  await new Promise((resolve) => {
    const mockup = document.createElement("script");
    mockup.src = "src/mockup.js";
    mockup.addEventListener("load", resolve, { once: true });
    mockup.addEventListener("error", () => {
      window.__augitError = "mockup-load-failed";
      resolve();
    }, { once: true });
    document.head.appendChild(mockup);
  });

  if (requestedDocument && window.__augitLive) {
    await openDocument(requestedDocument);
  }

  if (searchScene && window.__augitLive) {
    // 浮层先以空查询渲染，再绑定输入并聚焦，符合「打开即聚焦」的规格要求。
    window.__augitLive.search = { kind: searchScene, query: "", options: {}, matches: [], notice: "" };
    window.__augitRender();
    bindSearchOverlay(searchScene);
  }

  if (requestedBlame && window.__augitLive) {
    await loadBlame(requestedBlame);
    window.__augitRender();
  }

  if (requestedFileHistory && window.__augitLive) {
    await loadFileHistory(requestedFileHistory);
    window.__augitRender();
  }

  if (requestedDiff && window.__augitLive) {
    await loadDiff(requestedDiff);
    window.__augitRender();
    // 后续的参考数据与历史到达时会再各重绘一次；差异数据必须在那之后仍然可见，
    // 因此在轮询结束后再补一次重绘，避免被后到的重绘覆盖成样例内容。
    window.setTimeout(() => { if (window.__augitLive && window.__augitLive.diff) window.__augitRender(); }, 2500);
  }

  if (requestedConflict && window.__augitLive) {
    await loadConflicts();
    await loadConflict(requestedConflict);
    window.__augitRender();
  }

  // 界面此时已可交互：立即标记就绪，不能等 Git 状态（实测约 15 秒）。
  window.__augitReady = true;

  const status = await statusPromiseRef;
  if (status) {
    // Git 状态比首屏慢，到达后补一次重绘：分支名与 Changes 工具窗都随之更新。
    window.__augitRender();
    window.__augitGitReady = true;
  }

  const referencesPromise = hasHost() ? loadReferences() : Promise.resolve(null);
  const history = await historyPromiseRef;
  if (history) {
    // 历史更慢，到达后再补一次重绘，底部 Git 日志与历史工具窗随之更新。
    window.__augitRender();
    document.addEventListener("history-commit-selected", () => {
      const selected = document.querySelector('.commit-row[aria-selected="true"]');
      const revision = selected && selected.dataset.fullHash ? selected.dataset.fullHash : null;
      if (revision) void loadCommitDetails(revision);
    });
    // 先标记就绪：详情加载不能拖住其它断言与界面可用性。
    refreshPush();
    window.__augitHistoryReady = true;
    if (history.commits.length > 0) {
      void refreshCommitDetails();
    }
  } else {
    window.__augitHistoryReady = true;
  }

  if (wantsClone) {
    await loadSettings();
    window.__augitRender();
  }

  if (wantsSettings) {
    await loadSettings();
    window.__augitRender();
    bindSettingsSave();
  }

  if (wantsTerminal) {
    // 终端是独占资源，只在终端场景启动，避免无谓的常驻进程。
    await startTerminal();
    window.__augitTerminalReady = true;
  }

  // 远端、分支、Stash 与 Worktree 供管理窗口使用；失败不影响主界面。
  await referencesPromise;
  // 三类数据都到齐后才能算出待推送信息；此后再补一次重绘。
  refreshPush();
  window.__augitRender();
  reattachTerminal();
  if (window.__augitLive && window.__augitLive.search) bindSearchOverlay(window.__augitLive.search.kind);
  void refreshCommitDetails();
}

/** 目录展开/折叠：状态写入 store 后整页重绘，因此打开文件不会丢失展开层级。 */
async function toggleDirectory(row) {
  const path = row.dataset.treePath;
  if (expandedPaths.has(path)) {
    expandedPaths.delete(path);
  } else {
    expandedPaths.add(path);
    await loadChildren(path, Number(row.getAttribute("aria-level") || 1));
  }

  const live = window.__augitLive;
  live.tree = buildVisibleTree(live.name, live.rootPath ?? "");
  window.__augitRender();
}

function activateTreeRow(row) {
  const path = row.dataset.treePath;
  if (path === undefined) return;
  if (row.dataset.treeDirectory === "true") {
    void toggleDirectory(row);
    return;
  }

  void openDocument(path);
}

// 点击改动文件时打开它的差异视图。与项目树用同一套委托思路：
// 捕获阶段 + closest，既不受整页重绘影响，也不依赖内联处理器。
document.addEventListener("click", (event) => {
  const row = event.target.closest && event.target.closest(".changes-list .change-file-row");
  if (!row) return;
  const path = row.dataset.path;
  if (!path) return;
  event.preventDefault();
  void (async () => {
    const diff = await loadDiff(path);
    if (diff) {
      window.__augitRender();
    }
  })();
}, true);

// 用捕获阶段的委托监听：不受内容安全策略对内联处理器的限制，也不受整页重绘影响。
document.addEventListener("click", (event) => {
  const row = event.target.closest && event.target.closest(".side-content.tree .tree-row");
  if (!row) return;
  event.preventDefault();
  activateTreeRow(row);
}, true);

/** 把宿主返回的文档结果整理成界面需要的形状。 */
function toLiveDocument(payload) {
  const kind = payload.kind || "Text";
  const editor = kind === "Markdown" ? "markdown"
    : kind === "Json" ? "json"
      : kind === "Png" || kind === "Jpeg" || kind === "Bmp" ? "image"
        : payload.status === "TextReady" ? "text"
          : "file-limit";
  return {
    path: payload.path,
    name: payload.name,
    fullPath: payload.fullPath,
    workspaceName: payload.workspaceName,
    kind,
    typeName: payload.typeName,
    status: payload.status,
    fileSize: payload.fileSize,
    text: payload.text,
    preview: kind === "Markdown" && payload.text ? renderMarkdown(payload.text) : "",
    dataUrl: payload.dataUrl,
    pixelWidth: payload.pixelWidth,
    pixelHeight: payload.pixelHeight,
    message: payload.message,
    lineEndings: payload.lineEndings,
    encoding: payload.encoding,
    editor,
  };
}

/** 打开一个真实文件：取回内容、更新活动文档并重绘编辑区。 */
async function openDocument(path) {
  const live = window.__augitLive;
  if (!live) return;
  if (live.document && live.document.path === path) return;
  const started = performance.now();
  try {
    const payload = await fetchDocument(path);
    live.document = toLiveDocument(payload);
    live.editor = live.document.editor;
    window.__augitMarks = Object.assign(window.__augitMarks || {}, {
      open: Math.round(performance.now() - started),
    });
  } catch (error) {
    window.__augitError = "open-document:" + String(error && error.message || error);
    return;
  }

  window.__augitRender();
  // 重绘会替换项目树，事件委托挂在 #app 上因此仍然有效。
}

try {
  await boot();
} catch (error) {
  window.__augitError = "boot-failed:" + String(error && error.stack || error);
  window.__augitHistoryReady = true;
  window.__augitGitReady = true;
}
