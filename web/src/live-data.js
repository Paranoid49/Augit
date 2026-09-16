// 外壳启动器：在 WebView2 内先取真实工作区数据，再交给 visual mockup 的构建器渲染。
// 没有宿主（浏览器里直接打开视觉稿）时不做任何事，视觉稿保持原样。
//
// 性能约束：桥接调用每次跨进程，必须尽量少而并行。
// 因此首屏只取工作区信息、Git 状态与根目录一层；更深层级在展开时再取。

import { hasHost, invoke, subscribe } from "./bridge.js";
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
    // 在建立 live 对象时就固定初始工具窗口布局：
    // 之后若干次渲染会把场景值覆盖掉，晚读会丢失初始的底部工具窗口。
    liveObject.layout = readInitialLayout();
    if (pendingGitUnavailableReason) {
      liveObject.gitUnavailableReason = pendingGitUnavailableReason;
    }

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
    if (!status) return null;
    if (!status.available) {
      // Git 缺失或版本过低：保留文件浏览，只提示一次（规格 §7.18）。
      showGitUnavailable(status.reason);
      return null;
    }

    if (!status.isRepository) {
      return null;
    }
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
  // 连续选择不同文件时只接纳最后一次：旧响应不得覆盖新的归属结果。
  const token = ++detailViewToken;
  try {
    const blame = await invoke("git/blame", { path }, 60000);
    if (token !== detailViewToken) return null;
    // 与 document/read 同样的契约校验：缺少身份的载荷不得进入状态，
    // 否则渲染层会拿到 path 为 undefined 的对象。
    if (!blame || !blame.available || !Array.isArray(blame.lines)) return null;
    if (typeof blame.path !== "string" || blame.path.length === 0) return null;
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
/**
 * 推送预览（规格 §7.12）。
 *
 * 待推送提交必须来自宿主对 `@{u}..HEAD` 的查询，**不能用已加载的历史页推导**：
 * 历史是分页的，推导结果在未加载完整时会偏少，且「尚未加载」与「没有待推送」
 * 会被混为一谈——那会让界面在真正有待推送提交时禁用推送。
 */
function computePush(live) {
  if (!live || !live.status) return null;
  const branch = live.status.branch;
  if (!branch) return null;
  // 上游与待推送提交都取自同一次查询结果。
  // 若上游另从分支引用推导，就会出现「显示 origin/dsh」却「预览失败」的自相矛盾状态。
  return {
    branch,
    upstream: live.unpushedUpstream || null,
    commits: live.unpushedCommits || [],
    ready: !!live.unpushedReady,
    reason: live.unpushedReason || null,
  };
}

/**
 * 读取待推送提交并刷新推送预览。
 * 未配置上游时 ready=false 且给出原因——规格要求此时保留「定义远端」并禁用推送。
 */
async function loadUnpushed() {
  const live = window.__augitLive;
  if (!live) return null;
  try {
    const result = await invoke("git/unpushed", {}, 60000);
    if (!result || !result.available) {
      live.unpushedReady = false;
      live.unpushedCommits = [];
      live.unpushedUpstream = null;
      live.unpushedReason = (result && result.reason) || "当前无法读取待推送提交。";
    } else {
      live.unpushedReady = !!result.ready;
      live.unpushedCommits = result.commits || [];
      live.unpushedUpstream = result.upstream || null;
      live.unpushedReason = result.ready ? null : (result.reason || null);
    }
  } catch (error) {
    live.unpushedReady = false;
    live.unpushedCommits = [];
    live.unpushedUpstream = null;
    live.unpushedReason = String(error && error.message || error);
  }

  live.push = computePush(live);
  return live.push;
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
    refresh("overlay");
    // 覆盖层被替换后输入框是新的，需要重新绑定并恢复焦点与光标位置。
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

/**
 * 搜索结果（规格 §5.2）。
 * 单击只改变选中项；Enter 或双击打开并激活**临时预览标签**，
 * 下一个结果复用同一个标签。
 */
function selectSearchResult(row) {
  for (const other of document.querySelectorAll(".search-result.selected")) {
    other.classList.remove("selected");
  }

  if (row) row.classList.add("selected");
}

function openSearchResult(row) {
  const path = row && row.dataset.searchPath;
  if (!path) return;
  selectSearchResult(row);
  void openDocument(path, { preview: true });
}

document.addEventListener("click", (event) => {
  const row = event.target.closest && event.target.closest(".search-result");
  if (!row || !row.dataset.searchPath) return;
  event.preventDefault();
  // 双击（detail >= 2）打开；单击只改选中，不抢占编辑区。
  if (event.detail >= 2) {
    openSearchResult(row);
    return;
  }

  selectSearchResult(row);
}, true);

document.addEventListener("keydown", (event) => {
  if (event.key !== "Enter") return;
  const row = event.target.closest && event.target.closest(".search-result");
  if (!row || !row.dataset.searchPath) return;
  event.preventDefault();
  openSearchResult(row);
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
      void loadUnpushed();
      refresh("side", "editorContent", "statusbar", "bottomTool", "overlay", "titlebar");
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
// 已加载的补丁缓存：键是「内容」维度（规格 §6.3），不含单/双栏这类显示维度。
// 这样切换显示模式只重新排版，不再次查询 Git。
const diffPatches = new Map();

/**
 * Diff 请求键（规格 §6.3）：由仓库、文件路径、比较基准、**显示模式**、
 * 忽略空白与重命名选项组成。
 * 前五项之外还带内容版本，用于判断外部变化后是否需要重新计算。
 */
function diffRequestKey({ path, revision, commit, mode, ignoreWhitespace, detectRenames, version }) {
  const live = window.__augitLive;
  return [
    live ? live.root : "",
    path,
    revision || "工作区",
    commit || "",
    mode || "split",
    ignoreWhitespace ? "ignore-ws" : "keep-ws",
    detectRenames ? "renames" : "no-renames",
    version === undefined || version === null ? "" : String(version),
  ].join("\u0000");
}

/** 补丁键：与请求键相同，但不含显示模式（单双栏共用同一份补丁）。 */
function diffPatchKey(parts) {
  return diffRequestKey({ ...parts, mode: "" });
}

// 并发请求去重：键为请求键。
const diffRequests = new Map();
// 递增令牌用于丢弃过期结果：快速连选多个文件时，先发的请求后到不得闪回。
let diffToken = 0;

/**
 * 读取差异。
 *
 * `mode` 是显示维度：相同内容下切换单栏/双栏直接复用已加载补丁，不查询 Git。
 * `version` 是内容版本：只有它变化（真实外部变化）才重新计算当前 diff。
 */
async function loadDiff(path, options = {}) {
  const live0 = window.__augitLive;
  const parts = {
    path,
    revision: options.revision || "工作区",
    // 历史比较传入提交：宿主自行解析父版本，比较的是两个版本而不是工作区。
    // 它必须进入请求键，否则同一文件的"工作区 Diff"与"历史比较"会互相复用结果。
    commit: options.commit,
    mode: options.mode || (live0 && live0.diffMode) || "split",
    ignoreWhitespace: !!options.ignoreWhitespace,
    detectRenames: !!options.detectRenames,
    version: options.version,
  };
  const requestKey = diffRequestKey(parts);
  const existing = diffRequests.get(requestKey);
  if (existing) return existing;

  // 同一项重复选择不重复加载（§5.4 / §12.2）：请求键与当前已显示的一致时直接复用。
  if (!options.force && live0 && live0.diff
      && live0.diffRequestKey === requestKey && live0.editor === "diff") {
    return live0.diff;
  }

  // 内容未变、仅显示模式不同：复用已缓存的补丁（§6.3「只重新排版，不再次查询 Git」）。
  const patchKey = diffPatchKey(parts);
  const cached = diffPatches.get(patchKey);
  if (!options.force && cached) {
    if (live0) {
      live0.diff = cached;
      live0.diffRequestKey = requestKey;
      live0.diffMode = parts.mode;
    }

    return cached;
  }

  const token = ++diffToken;
  const request = (async () => {
    try {
      const diff = await invoke("git/diff", {
        path,
        ignoreWhitespace: parts.ignoreWhitespace,
        // 「工作区」是界面里的默认占位，不是真实基准：不传时宿主按 HEAD 处理。
        revision: parts.revision === "工作区" ? undefined : parts.revision,
        // 历史比较：提交交给宿主解析父版本。
        commit: parts.commit,
      }, 30000);
      const live = window.__augitLive;
      // 只有最新一次请求可以写回界面状态（每个请求带递增版本号，旧结果必须丢弃）。
      if (token === diffToken && live) {
        // 差异必须带 path，否则详情区与标签会渲染出 undefined。
        const validDiff = diff && diff.available
          && typeof diff.path === "string" && diff.path.length > 0
          && Array.isArray(diff.rows);
        live.diff = validDiff ? diff : null;
        live.diffRequestKey = requestKey;
        live.diffMode = parts.mode;
        // 有差异时切到差异视图；无差异时保留当前文档视图。
        // 但**只有比较标签在前台时才允许切换视图**：跟随 Changes 选择时的后台更新
        // 不得改变前台正文（规格 §5.2「只后台更新，不抢占焦点」）。
        const comparison = findComparisonTab();
        const foreground = !live.activeTabId || (comparison && live.activeTabId === comparison.id);
        if (live.diff && foreground) {
          live.editor = "diff";
        }

        if (live.diff) {
          diffPatches.set(patchKey, live.diff);
        }
      }

      window.__augitDiffReady = true;
      return live && live.diff && live.diff.path === path ? live.diff : null;
    } catch (error) {
      if (token === diffToken) {
        window.__augitError = "load-diff:" + String(error && error.message || error);
      }

      return null;
    } finally {
      diffRequests.delete(requestKey);
    }
  })();

  diffRequests.set(requestKey, request);
  return request;
}

/** 关闭工作区 Diff：释放补丁与正文，并取消尚未完成的请求（规格 §6.3）。 */
function closeDiff() {
  const live = window.__augitLive;
  diffPatches.clear();
  diffRequests.clear();
  // 使在途请求的结果失效：令牌前进后，旧结果不会再写回。
  diffToken += 1;
  if (live) {
    live.diff = null;
    live.diffRequestKey = null;
    live.editor = live.document ? live.document.editor : "empty";
  }
}

/** 切换差异显示模式；相同内容下复用已缓存补丁，不重新查询 Git。 */
async function switchDiffMode(mode) {
  const live = window.__augitLive;
  if (!live || !live.diff) return null;
  const path = live.diff.path;
  live.diffMode = mode;
  const diff = await loadDiff(path, { mode });
  refresh("editorContent", "editorTabs");
  return diff;
}

// 供验收套件在已打开的 Push 弹层上重算预览；不新开一层。
window.__augitRefreshPush = () => { refreshPush(); };

window.__augitLoadDiffMode = switchDiffMode;
window.__augitCloseDiff = closeDiff;
window.__augitDiffPatchCount = () => diffPatches.size;
// 在途请求数：用于验证"关闭比较后仍有请求在飞、且其晚到结果不得回写"。
window.__augitDiffRequestCount = () => diffRequests.size;

// 加载反馈阈值（规格 §6.5）：预计低于 150 毫秒的操作不显示加载动画，避免闪烁。
const LoadingFeedbackDelay = 150;
let diffLoadingMark = null;

/**
 * 在差异加载超过阈值后才显示加载提示（规格 §6.5 / §9.1）。
 *
 * 提示改为**由状态驱动渲染**（`live.diffLoading`），不再注入 DOM 节点。
 * 原因是区域刷新会重建编辑区，注入的节点会被随之清掉——实测提示在 686.9 毫秒
 * 出现、随即被刷新抹掉，整个加载窗口内 `marker` 采样恒为 false，等于提示从未可见。
 * 状态驱动的提示在每次渲染时都会重新出现，也能落到规格要求的**文件标题行**。
 */
function scheduleDiffLoadingMarker() {
  clearDiffLoadingMarker();
  // 记录调度时刻：验收据此核对 150 毫秒阈值（规格 §9.1），
  // 避免测试端用「等待固定时长再取样」这种依赖时序的判据。
  window.__augitLoadingMarkerScheduledAt = performance.now();
  diffLoadingMark = window.setTimeout(() => {
    diffLoadingMark = null;
    const live = window.__augitLive;
    if (!live) return;
    live.diffLoading = true;
    window.__augitLoadingMarkerShownAt = performance.now();
    // 只刷新编辑区：提示必须随 diff 正文一起重绘。
    refresh("editorContent");
  }, LoadingFeedbackDelay);
}

function clearDiffLoadingMarker() {
  if (diffLoadingMark !== null) {
    window.clearTimeout(diffLoadingMark);
    diffLoadingMark = null;
  }

  const live = window.__augitLive;
  if (live) live.diffLoading = false;
  document.querySelectorAll(".diff-loading-status").forEach((node) => node.remove());
}


/** 单击只更新改动列表的选中态，不请求差异（规格 §12.2）。 */
function selectChangeRow(row) {
  for (const other of document.querySelectorAll(".changes-list .change-file-row.selected")) {
    other.classList.remove("selected");
    other.removeAttribute("aria-selected");
  }

  row.classList.add("selected");
  row.setAttribute("aria-selected", "true");
  // 选中行也要记在状态里：区域刷新会重建改动列表，只加类名会在刷新后丢失
  // （规格 §5.2 要求关闭比较保留 Changes 的选中行）。
  const live = window.__augitLive;
  if (live) live.selectedChangePath = row.dataset.path || null;
  // 已打开并处于跟随状态的比较标签随选择更新；未打开时单击不创建标签。
  // 注意改动行与树行的路径字段**不同**：改动行是 `data-path`，树行才是 `data-tree-path`。
  // 取错字段不会报错，只会让跟随静默失效——曾因此漏掉一整条跟随行为。
  const path = row.dataset.path;
  if (path) void followChangeSelection(path);
}

/** 找到工作区比较标签（规格 §5.2：最多只有一个）。 */
function findComparisonTab() {
  const live = window.__augitLive;
  return live && live.tabs ? live.tabs.find((tab) => tab.kind === "comparison") || null : null;
}

/**
 * 同步比较标签的标题与目标路径。
 *
 * 比较标签是**复用**的：同一标签会依次承载工作区比较、引用比较、不同文件的差异。
 * 只更新正文而不同步 `path`/`title`，标签会一直显示第一次的内容
 * （规格 §7.9 要求标签先显示文件名，再显示来源与目标引用）。
 */
function syncComparisonTab(tab, path, title) {
  if (!tab) return;
  tab.path = path;
  tab.title = title;
}

/**
 * 打开或更新工作区比较标签（规格 §5.2）。
 *
 * - 只保留一个比较标签，双击 / Enter / 「显示 Diff」都打开并激活它；
 * - 普通文档处于前台时只后台更新该标签正文，不抢占焦点；
 * - `live.followChanges` 表示比较标签是否跟随 Changes 选择：
 *   显式打开时置为 true，关闭比较后解除（置为 false），
 *   此后 Changes 的单击与无变化刷新不会重新创建该标签。
 */
async function openChangeDiff(path, options = {}) {
  const { activate = true } = options;
  const live = window.__augitLive;
  if (!live) return;
  live.followChanges = true;
  scheduleDiffLoadingMarker();
  try {
    const diff = await loadDiff(path);
    if (!diff) return;
    const title = `提交: ${diff.name || path}`;
    if (!findComparisonTab()) {
      live.tabs ??= [];
      const tab = {
        id: nextTabId(),
        kind: "comparison",
        path,
        title,
        editor: "diff",
        preview: false,
      };
      live.tabs.push(tab);
      if (activate) activateComparisonTab(tab);
    } else {
      const tab = findComparisonTab();
      // 比较标签是复用的：跟随到另一个文件时必须同步标签文字，
      // 否则标签会一直显示第一次打开的文件名。
      syncComparisonTab(tab, path, title);
      if (activate) activateComparisonTab(tab);
    }

    // 规格 §12.2 要求已有 Diff 标签时「只更新该标签正文」，不得刷新改动列表、
    // 复选框、提交信息与列表滚动；同时延后到事件派发结束再替换编辑区，
    // 使视觉稿挂在冒泡阶段的「双击建比较标签」监听能收到事件。
    refreshAfterEvent("editorContent", "editorTabs", "statusbar");
  } finally {
    clearDiffLoadingMarker();
  }
}

/**
 * 历史比较标签（规格 §7.8）。
 *
 * 契约来自 `docs/ux-mockups/` 里已经存在的历史比较实现（标签格式
 * `比较: <文件名> · <hash>^ → <hash>`、双击或 `Enter` 打开、单击跟随、
 * 关闭解除跟随），这里把它落到实时外壳的标签模型上：
 *
 * - 与工作区比较**共用同一套标签语义**（`live.tabs` 里的 `comparison` 标签），
 *   因此"最多一个比较标签""关闭后解除跟随""关闭不重建"等规则自动一致；
 * - 但跟随对象是**提交选择**而不是 Changes 选择，所以用独立的 `live.historyComparison`
 *   记录当前目标，人工切换标签时不会被 Changes 的跟随改写。
 * - 父版本用 Git 的祖先后缀 `^` 表示（规格要求保留该后缀），由宿主解析真实父提交。
 */
function historyComparisonLabel(path, hash) {
  const name = String(path || "").split("/").at(-1) || "选择文件";
  const short = String(hash || "").slice(0, 8);
  return `比较: ${name} · ${short}^ → ${short}`;
}

/** 当前选中的提交（底部 Git 日志）与其中的变化文件。 */
function selectedHistoryCommit() {
  const row = document.querySelector('.commit-row[aria-selected="true"]');
  return row && row.dataset.fullHash ? row.dataset.fullHash : (row ? row.dataset.hash || null : null);
}

function historyFileRows() {
  return [...document.querySelectorAll("[data-live-changed-files] [data-history-path]")];
}

async function openHistoryComparison(row) {
  const live = window.__augitLive;
  if (!live || !row) return;
  const path = row.dataset.historyPath;
  const commit = selectedHistoryCommit();
  if (!path || !commit) return;
  return applyHistoryComparison(path, commit, { activate: true });
}

/**
 * 打开或更新历史比较标签。
 *
 * `activate` 为 false 时只后台更新正文（普通文档在前台时不抢占，规格 §7.8）。
 */
async function applyHistoryComparison(path, commit, options = {}) {
  const { activate = true } = options;
  const live = window.__augitLive;
  if (!live) return null;
  const label = historyComparisonLabel(path, commit);
  const token = ++historyComparisonToken;
  // 标签立即建立并显示双方引用，正文随后填充（规格 §7.8：激活时立即打开并显示
  // 双方引用及文件路径，Git 查询完成后只填充正文，不再次激活标签）。
  const tab = ensureHistoryComparisonTab(label, path);
  live.historyComparison = { path, commit, label, status: "loading" };
  if (activate) activateComparisonTab(tab);

  refreshAfterEvent("editorTabs", "editorContent", "statusbar", "bottomTool");

  const diff = await loadDiff(path, { commit, force: true }).catch(() => null);
  // 只接纳最后一次有效结果（规格 §7.8：只接纳最后一次有效结果）。
  if (token !== historyComparisonToken) return diff;
  if (!diff) {
    live.historyComparison = { path, commit, label, status: "unavailable" };
    refreshAfterEvent("editorTabs", "editorContent", "statusbar");
    return null;
  }

  live.historyComparison = { path, commit, label, status: "ready" };
  if (activate) activateComparisonTab(tab);

  refreshAfterEvent("editorTabs", "editorContent", "statusbar", "bottomTool");
  return diff;
}

/**
 * 让比较标签成为前台。
 *
 * 必须同时清掉 `live.document`：视觉稿的编辑器分支写作
 * `if (live && live.document && live.editor) editor = live.editor;`，
 * 残留的文档会让 `live.editor = "diff"` 被忽略、比较正文渲染不出来
 * （历史比较首轮实现时正是卡在这里，表现为 `editor` 已是 diff 但 DOM 仍是文档）。
 */
function activateComparisonTab(tab) {
  const live = window.__augitLive;
  if (!live || !tab) return;
  live.activeTabId = tab.id;
  live.document = null;
  live.editor = "diff";
}

/** 建立或复用唯一的比较标签。 */
function ensureHistoryComparisonTab(label, path) {
  const live = window.__augitLive;
  live.tabs ??= [];
  const existing = findComparisonTab();
  if (existing) {
    syncComparisonTab(existing, path, label);
    return existing;
  }

  const tab = {
    id: nextTabId(),
    kind: "comparison",
    path,
    title: label,
    editor: "diff",
    preview: false,
  };
  live.tabs.push(tab);
  return tab;
}

/**
 * 提交或文件选择变化时更新已打开的历史比较（规格 §7.8）。
 * 普通文档在前台时只后台更新，不抢占编辑区。
 */
function followHistoryComparison() {
  const live = window.__augitLive;
  if (!live || !live.historyComparison) return;
  if (live.historyComparison.status === "closed") return;
  const row = historyFileRows().find((item) => item.dataset.historyPath === live.historyComparison.path)
    || historyFileRows()[0];
  const path = row ? row.dataset.historyPath : null;
  const commit = selectedHistoryCommit();
  if (!path || !commit) return;
  if (path === live.historyComparison.path && commit === live.historyComparison.commit) return;
  const tab = findComparisonTab();
  const background = !tab || live.activeTabId !== tab.id;
  void applyHistoryComparison(path, commit, { activate: false }).then(() => {
    if (background) {
      const active = (live.tabs || []).find((item) => item.id === live.activeTabId);
      live.editor = active ? active.editor : (live.document ? live.document.editor : "empty");
      refreshAfterEvent("editorTabs", "statusbar");
    }
  });
}

// 历史比较的递增令牌：改选提交或文件时丢弃旧响应。
let historyComparisonToken = 0;

/** Changes 列表选择变化时的跟随入口：解除跟随后不再自动更新比较标签。 */
async function followChangeSelection(path) {
  const live = window.__augitLive;
  if (!live || !live.followChanges) return;
  const tab = findComparisonTab();
  if (!tab) return;
  // 普通文档在前台时只后台更新正文，不抢占焦点（规格 §5.2）。
  const background = live.activeTabId !== tab.id;
  // 记下加载前的前台视图：loadDiff 会按需把 live.editor 切到 "diff"，
  // 后台更新必须如实还原，否则前台正文与状态不一致。
  const editorBefore = live.editor;
  const diff = await loadDiff(path).catch(() => null);
  if (!diff) return;
  if (background) {
    live.editor = editorBefore;
    // 前台正文没有变化，只更新标签与状态栏；不刷新编辑区，避免打断阅读位置。
    refreshAfterEvent("editorTabs", "statusbar");
    return;
  }

  syncComparisonTab(tab, path, `提交: ${diff.name || path}`);
  refreshAfterEvent("editorContent", "editorTabs", "statusbar");
}

/** 读取设置并挂上保存动作。 */
async function loadSettings() {
  try {
    const settings = await invoke("settings/read", {}, 15000);
    const live = window.__augitLive;
    if (live && settings) {
      live.settings = settings;
      applySavedPanelSizes(settings);
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
  if (!saved || !saved.saved) {
    // 把宿主给出的原因带给用户；缺失时退回通用提示。
    throw new Error(saved && saved.reason ? saved.reason : "设置未能保存");
  }
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

/**
 * 文件系统变化轮询。
 * 宿主不主动推送，界面按固定间隔读取累积的变化并做局部刷新：
 * - `.git` 变化 -> 刷新 Git 状态与历史（对应 §12.2「外部改变当前文件后在
 *   500 毫秒内更新当前 diff」）；
 * - 普通文件变化 -> 只在该文件正是当前查看/差异对象时刷新，避免无关文件
 *   触发当前视图进入加载状态（对应 §12.2「外部改变无关文件时当前 diff
 *   不进入加载状态」）。
 */
// 变化以宿主推送为主：事件到达即处理，延迟由文件系统监视的合并窗口（50 毫秒）决定，
// 远低于规格的 500 毫秒要求，空闲时不产生任何定时唤醒。
// 兜底轮询只用于事件可能丢失的场景（例如观察器建立失败），间隔远大于合并窗口。
const ChangeFallbackIntervalMs = 2000;
let changeTimer = 0;

async function drainWorkspaceChanges() {
  const live = window.__augitLive;
  if (!live) return;
  try {
    const changes = await invoke("workspace/changes", {}, 10000);
    if (changes && changes.available) {
      await applyWorkspaceChanges(changes);
    }
  } catch {
    // 单次失败不影响后续。
  }
}

async function pollWorkspaceChanges() {
  if (changeTimer !== 0) return;
  changeTimer = window.setInterval(() => {
    if (document.hidden) return;
    void drainWorkspaceChanges();
  }, ChangeFallbackIntervalMs);
}

function startWorkspaceChangePolling() {
  if (!hasHost()) return;
  // 宿主推送立即处理；兜底轮询保持低频。
  subscribe("workspace-changed", (payload) => {
    void applyWorkspaceChanges(payload || { files: [], gitMetadata: false });
  });
  void pollWorkspaceChanges();
}

/** 把一次变化批次应用到界面。只刷新受影响的区域。 */
async function applyWorkspaceChanges(changes) {
  const live = window.__augitLive;
  if (!live) return;
  const files = changes.files || [];
  const currentPath = live.document ? live.document.path : null;
  const diffPath = live.diff ? live.diff.path : null;
  // 宿主只应传字符串路径；非字符串项直接丢弃，避免一次异常让整轮刷新中断。
  const toRelative = (absolute) => {
    if (typeof absolute !== "string" || absolute.length === 0) return null;
    const root = typeof live.root === "string" ? live.root : "";
    const normalized = absolute.replaceAll("\\", "/");
    const base = root.replaceAll("\\", "/").replace(/\/+$/, "");
    return base.length > 0 && normalized.toLowerCase().startsWith(base.toLowerCase() + "/")
      ? normalized.slice(base.length + 1)
      : normalized;
  };
  const relative = files.map(toRelative).filter((path) => path !== null);

  let touchedCurrent = false;
  let touchedNothing = false;

  if (changes.gitMetadata) {
    // Git 元数据变化：状态与历史都可能变，重新读取后局部刷新。
    await Promise.all([loadStatus().catch(() => null), loadHistory().catch(() => null)]);
    touchedCurrent = true;
  }

  if (relative.length > 0) {
    const hitsCurrent = currentPath !== null && relative.some((path) => path.toLowerCase() === currentPath.toLowerCase());
    const hitsDiff = diffPath !== null && relative.some((path) => path.toLowerCase() === diffPath.toLowerCase());
    if (hitsDiff && diffPath) {
      // 只有当前差异对象本身变化才重新请求差异。
      await loadDiff(diffPath, { force: true }).catch(() => null);
      touchedCurrent = true;
    } else if (hitsCurrent) {
      // 当前查看的普通文件被外部修改或删除。
      // 先取一次最新状态：判断「是否已删除」必须基于变化后的状态，
      // 否则拿到的是变化前的列表，永远看不到 Deleted。
      await loadStatus().catch(() => null);
      const path = currentPath;
      const deleted = live.status && Array.isArray(live.status.files)
        && live.status.files.some((file) => file.path === path && file.kind === "Deleted");
      if (deleted) {
        // 文件已不存在：移除它的标签，而不是留下一个打不开的标签（规格 §5.2）。
        for (const tab of (live.tabs || []).filter((item) => item.kind === "document" && item.path === path)) {
          closeTab(tab.id);
        }
      } else {
        // 仍在：重新读取内容，并保留该标签在标签栏中的位置与当前标签。
        const index = (live.tabs || []).findIndex((item) => item.kind === "document" && item.path === path);
        await openDocument(path, { activate: false }).catch(() => null);
        if (index >= 0 && live.document && live.document.path === path) {
          const current = live.tabs.find((item) => item.kind === "document" && item.path === path);
          if (current && live.tabs.indexOf(current) !== index) {
            live.tabs.splice(live.tabs.indexOf(current), 1);
            live.tabs.splice(index, 0, current);
          }
        }
      }

      touchedCurrent = true;
    } else {
      // 无关文件变化：当前 diff 不进入加载状态，只刷新状态列表。
      touchedNothing = true;
    }

    await loadStatus().catch(() => null);
    touchedCurrent = true;
  }

  if (!touchedCurrent && !touchedNothing) return;
  refresh("side", "editorContent", "editorTabs", "statusbar", "bottomTool", "titlebar");
  void refreshCommitDetails();
}

/**
 * 稳定快照（规格 §6.2）。至少包含：工作区路径与仓库可用状态、当前分支与 HEAD、
 * Changes 与 Unversioned Files 的路径/状态/排序、Git 操作会话、当前选中标识。
 *
 * 快照只取「界面必须据此更新」的字段：比较它来决定是否需要重绘。
 */
function buildSnapshot(status, history) {
  const live = window.__augitLive;
  return {
    root: live ? live.root : null,
    repositoryAvailable: !!(status && status.available),
    branch: status ? status.branch : null,
    isDetached: !!(status && status.isDetached),
    head: history ? history.head : null,
    // 顺序敏感：规格要求「路径、状态和排序」都进入快照。
    files: status && status.files
      ? status.files.map((file) => `${file.group}|${file.path}|${file.kind}|${file.staged ? 1 : 0}|${file.workingTree ? 1 : 0}`)
      : [],
    commits: history && history.commits
      ? history.commits.map((commit) => `${commit.fullHash}|${commit.subject}|${(commit.references || []).join(",")}`)
      : [],
    hasNextPage: !!(history && history.hasNextPage),
  };
}

/** 当前已应用快照的序列化结果，用于等价比较。 */
let appliedSnapshot = null;

/**
 * 在新数据到达后决定是否需要更新界面。
 * 快照相等时不做任何更新：不重建列表、不重设选中项、不重新渲染 diff、
 * 不改变工具窗口大小、不触发布局（规格 §6.2）。
 *
 * 返回 true 表示界面已更新。
 */
function applySnapshot(status, history) {
  const next = buildSnapshot(status, history);
  const serialized = JSON.stringify(next);
  if (serialized === appliedSnapshot) {
    return false;
  }

  appliedSnapshot = serialized;
  return true;
}

/** 历史是否与已应用快照一致；供验收套件查询。 */
window.__augitSnapshotEqual = (status, history) =>
  JSON.stringify(buildSnapshot(status || latestStatus, history || latestHistory)) === appliedSnapshot;

// 供验收套件调用：走与真实数据到达完全相同的快照应用路径。
window.__augitApplyHistorySnapshot = () => {
  const live = window.__augitLive;
  if (!live) return false;
  const updated = applySnapshot(latestStatus, latestHistory);
  if (updated) {
    refresh("side", "editorContent", "statusbar", "bottomTool", "titlebar");
    void refreshCommitDetails();
  }

  return updated;
};

/**
 * 应用已保存的面板尺寸（规格 §4.2：用户拖动后持久化，再次打开时恢复）。
 * 只写入 CSS 变量，不触碰任何其它布局状态。
 */
function applySavedPanelSizes(settings) {
  const root = document.documentElement;
  if (typeof settings.projectPanelWidth === "number" && settings.projectPanelWidth > 0) {
    root.style.setProperty("--augit-side-width", `${Math.round(settings.projectPanelWidth)}px`);
  }

  if (typeof settings.bottomPanelHeight === "number" && settings.bottomPanelHeight > 0) {
    // 与视觉稿同一口径：最小高度随字号扩展，上限在必要时同步扩展。
    const minimum = bottomMinimum();
    const ceiling = Math.max(305, minimum);
    const height = Math.max(minimum, Math.min(ceiling, settings.bottomPanelHeight));
    root.style.setProperty("--augit-bottom-height", `${Math.round(height)}px`);
  }
}

// 分隔条拖拽（规格 §4.2）。视觉稿没有可见的分隔条元素，
// 命中区域就是面板之间的 4 像素间隙，因此用文档级指针事件 + 命中判定实现。
const SIDE_DRAG_ZONE = 4;      // app-main 的 column-gap
const BOTTOM_DRAG_ZONE = 4;    // workspace 的 row-gap
const SIDE_MIN = 300;
const SIDE_MAX = 360;
const BOTTOM_MAX = 305;

/**
 * 底部面板的最小高度。规格 §4.2：字号增大时最小高度按 max(180, 4h + 80) 扩展；
 * 视觉稿把这个值写入 `--augit-bottom-min-height`，这里读取它以保持一致，
 * 避免在字号较大时把面板压到容不下标题与一行正文。
 */
function bottomMinimum() {
  const rootStyle = getComputedStyle(document.documentElement);
  // 视觉稿在测量后写入该变量，优先使用它。
  const declared = Number.parseFloat(rootStyle.getPropertyValue('--augit-bottom-min-height'));
  if (Number.isFinite(declared) && declared > 0) {
    return declared;
  }

  // 该变量只在测量流程跑过之后才存在，因此这里按同一公式自行推导：
  // 先取代码视图的实际行高，取不到时用界面字号 × 1.72（与视觉稿一致）。
  const codeView = document.querySelector('.code-view, .diff-columns, .markdown-source');
  const measured = codeView ? Number.parseFloat(getComputedStyle(codeView).lineHeight) : Number.NaN;
  const fontSize = Number.parseFloat(rootStyle.fontSize);
  const lineHeight = Number.isFinite(measured) && measured > 0
    ? measured
    : (Number.isFinite(fontSize) && fontSize > 0 ? fontSize * 1.72 : 22);
  return Math.max(180, Math.round(lineHeight * 4 + 80));
}
let panelDrag = null;

/** 侧栏宽度下限：规格允许 300–360，但不能把编辑区挤到不足 320。 */
function sideBounds() {
  const main = document.querySelector('.app-main');
  const available = (main ? main.clientWidth : window.innerWidth) - 42 - SIDE_DRAG_ZONE;
  return { min: SIDE_MIN, max: Math.max(SIDE_MIN, Math.min(SIDE_MAX, available - 320)) };
}

/** 底部面板高度下限：规格允许 180–305，但不能把正文挤到不足 260。 */
function bottomBounds() {
  const workspace = document.querySelector('.workspace');
  const available = workspace ? workspace.clientHeight : window.innerHeight;
  // 最小高度随字号扩展；上限在最小高度超过 305 时同步扩展，
  // 不能产生无效尺寸区间（规格 §4.2）。
  const minimum = bottomMinimum();
  const ceiling = Math.max(BOTTOM_MAX, minimum);
  return { min: minimum, max: Math.max(minimum, Math.min(ceiling, available - 260)) };
}

/** 命中判定：返回正在拖拽的分隔条类型，或 null。 */
function hitPanelDivider(event) {
  const main = document.querySelector('.app-main');
  const side = document.querySelector('.side-tool');
  if (main && side) {
    const r = side.getBoundingClientRect();
    if (event.clientY >= r.top && event.clientY <= r.bottom
        && event.clientX >= r.right && event.clientX <= r.right + SIDE_DRAG_ZONE + 4) {
      return 'side';
    }
  }

  const bottom = document.querySelector('.bottom-tool');
  if (bottom) {
    const r = bottom.getBoundingClientRect();
    if (event.clientX >= r.left && event.clientX <= r.right
        && event.clientY >= r.top - BOTTOM_DRAG_ZONE - 4 && event.clientY <= r.top) {
      return 'bottom';
    }
  }

  return null;
}

/** 应用一个面板尺寸：只写 CSS 变量，不触发布局重建。 */
function applyPanelSize(kind, size) {
  const root = document.documentElement;
  if (kind === 'side') {
    root.style.setProperty('--augit-side-width', `${Math.round(size)}px`);
  } else {
    root.style.setProperty('--augit-bottom-height', `${Math.round(size)}px`);
  }
}

/** 把当前面板尺寸写回设置（规格 §4.2：拖动后持久化）。 */
function persistPanelSize(kind, size) {
  const live = window.__augitLive;
  if (!live || !live.settings) return;
  const key = kind === 'side' ? 'projectPanelWidth' : 'bottomPanelHeight';
  const rounded = Math.round(size);
  if (live.settings[key] === rounded) return;
  live.settings[key] = rounded;
  void invoke("settings/write", { [key]: rounded }, 15000).catch(() => {});
}

function endPanelDrag() {
  if (!panelDrag) return;
  const { kind, size, moved } = panelDrag;
  panelDrag = null;
  document.body.style.removeProperty('cursor');
  document.body.style.removeProperty('user-select');
  // 未实际移动的按下不写回设置。
  if (moved) persistPanelSize(kind, size);
}

/** 安装分隔条拖拽。挂在 document 上，因此不受区域刷新影响。 */
function bindPanelDividers() {
  if (window.__augitDividersBound) return;
  window.__augitDividersBound = true;

  document.addEventListener("pointerdown", (event) => {
    if (event.button !== 0 || panelDrag) return;
    if (event.target.closest && event.target.closest('button, a, input, summary')) return;
    const kind = hitPanelDivider(event);
    if (!kind) return;
    event.preventDefault();
    const element = kind === 'side'
      ? document.querySelector('.side-tool')
      : document.querySelector('.bottom-tool');
    if (!element) return;
    const rect = element.getBoundingClientRect();
    panelDrag = {
      kind,
      pointerId: event.pointerId,
      // 从按下时的实际显示尺寸计算增量，避免初始跳动（规格 §4.2）。
      startSize: kind === 'side' ? rect.width : rect.height,
      startPosition: kind === 'side' ? event.clientX : event.clientY,
      size: kind === 'side' ? rect.width : rect.height,
      moved: false,
    };
    document.body.style.cursor = kind === 'side' ? 'col-resize' : 'row-resize';
    document.body.style.userSelect = 'none';
  }, true);

  document.addEventListener("pointermove", (event) => {
    if (!panelDrag || event.pointerId !== panelDrag.pointerId) return;
    event.preventDefault();
    const bounds = panelDrag.kind === 'side' ? sideBounds() : bottomBounds();
    const current = panelDrag.kind === 'side' ? event.clientX : event.clientY;
    const delta = current - panelDrag.startPosition;
    const next = Math.max(bounds.min, Math.min(bounds.max, panelDrag.startSize + delta));
    if (Math.abs(next - panelDrag.size) < 0.5) return;
    panelDrag.size = next;
    panelDrag.moved = true;
    applyPanelSize(panelDrag.kind, next);
  }, true);

  document.addEventListener("pointerup", (event) => {
    if (panelDrag && event.pointerId === panelDrag.pointerId) endPanelDrag();
  }, true);
  document.addEventListener("pointercancel", (event) => {
    if (panelDrag && event.pointerId === panelDrag.pointerId) endPanelDrag();
  }, true);
  // Esc 与窗口失焦都要结束拖拽；只结束拖拽，不关闭查找条（规格 §4.2）。
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && panelDrag) {
      event.preventDefault();
      event.stopImmediatePropagation();
      endPanelDrag();
    }
  }, true);
  window.addEventListener("blur", endPanelDrag);
}

// 供验收套件在清理测试残留后重新应用面板尺寸。
// 供验收套件直接调用数据加载入口。
window.__augitListDirectory = (path) => invoke("workspace/list", { path }, 15000);

window.__augitSaveSettings = () => saveSettings();

// 供验收套件直接验证写入口的字段校验；返回宿主响应并刷新本地副本。
window.__augitSettingsWrite = async (payload) => {
  const result = await invoke("settings/write", payload, 15000);
  const fresh = await invoke("settings/read", {}, 15000);
  if (window.__augitLive && fresh) window.__augitLive.settings = fresh;
  return result;
};

window.__augitLoadCommitDetails = (revision) => loadCommitDetails(revision);

window.__augitLoadBlame = (path) => loadBlame(path);
window.__augitLoadFileHistory = (path) => loadFileHistory(path);
window.__augitLoadDiff = (path, options) => loadDiff(path, options);

// 供验收套件走与点击相同的打开路径。
window.__augitOpenDocument = (path) => openDocument(path);

window.__augitApplyPanelSizes = (settings) => applySavedPanelSizes(settings || {});

// 供验收套件查询拖拽是否仍在进行。
window.__augitPanelDragActive = () => panelDrag !== null;

/**
 * Git 不可用时的局部提示（规格 §7.18）。
 * 只显示一次：不反复弹出错误，也不阻塞普通文件查看。
 * 提示里提供配置 git.exe 的入口。
 */
function showGitUnavailable(reason) {
  // 状态可能在 live 对象建立之前就返回，因此先缓存原因，稍后再附着。
  pendingGitUnavailableReason = reason || "未找到 Git for Windows 2.40 或更高版本。";
  window.__augitGitUnavailable = true;
  const live = window.__augitLive;
  if (!live) return;
  live.gitUnavailableReason = pendingGitUnavailableReason;
  // 提示只出现一次，但原因始终记录（后续入口的禁用说明需要它）。
  if (live.gitUnavailableShown) return;
  live.gitUnavailableShown = true;

  const host = document.querySelector(".toast-layer") || document.querySelector(".augit-window");
  if (!host) return;
  const toast = document.createElement("div");
  toast.className = "toast error";
  toast.setAttribute("role", "alert");
  toast.innerHTML = '<div class="toast-title">Git 不可用</div>'
    + `<div>${escapeText(live.gitUnavailableReason)} 文件浏览仍可使用。</div>`
    + '<div class="button-row"><a class="secondary-button" href="settings.html">配置 git.exe</a></div>';
  host.appendChild(toast);
}

/** Git 不可用的原因；状态可能早于 live 对象返回，因此单独缓存。 */
let pendingGitUnavailableReason = null;

/** 读取当前冲突会话与冲突文件列表。 *//** 读取当前冲突会话与冲突文件列表。 */
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
/** 状态或引用变化后按已有数据重算推送预览；不发起查询。 */
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

/**
 * 刷新界面。优先只替换受影响的区域（规格 §6：局部状态变化不得重建全局结构），
 * 保留其余区域的焦点、滚动位置与已建立的组件实例；
 * 页面未提供区域刷新入口时退回整页重绘。
 */
function refresh(...regions) {
  if (typeof window.__augitRenderRegions === "function" && regions.length > 0) {
    window.__augitRenderRegions(...regions);
    rebindAfterRender();
    return;
  }

  window.__augitRender();
  rebindAfterRender();
}

/**
 * 每次渲染后重新挂上动作与恢复状态。
 *
 * 两条刷新路径（定点替换与整页重绘）都必须调用它：绑定只写在其中一条上，
 * 另一条就会出现「某些入口在启动后没有动作」——实测分支芯片因此整页导航离开应用。
 */
function rebindAfterRender() {
  // 工具窗口、标签栏与改动列表可能已被替换。
  bindToolRail?.();
  bindEditorTabs?.();
  bindChangesState?.();
  bindChangesScroll();
  restoreChangesState();
  bindOverlayEscape();
  bindTitlebarMenuEscape();
  bindCompactDialogKeys();
  bindGlobalShortcuts();
  reflectWriteOperation();
  guardUnwiredNavigation();
}

/**
 * 编辑器标签集合（规格 §5.2）。
 *
 * live.tabs 按显示顺序保存标签，live.activeTabId 指向当前标签。
 * 三类标签：正式文档标签、临时预览标签（preview=true，只保留一个）、
 * 工作区比较标签（kind="comparison"）。
 * live.document / live.editor 是「当前标签」的派生视图，保留写入以兼容既有渲染路径。
 */
let tabSequence = 0;

function tabState() {
  const live = window.__augitLive;
  if (!live) return null;
  live.tabs ??= [];
  live.activeTabId ??= null;
  return live;
}

function nextTabId() {
  tabSequence += 1;
  return `tab-${tabSequence}`;
}

/** 让 live.document / live.editor 反映当前标签。 */
function syncActiveTab() {
  const live = tabState();
  if (!live) return;
  const tab = live.tabs.find((item) => item.id === live.activeTabId) || null;
  live.activeTab = tab;
  if (tab && tab.kind === "document") {
    live.document = tab.document;
    live.editor = tab.editor;
    return;
  }

  if (tab && tab.kind === "comparison") {
    live.editor = tab.editor || "diff";
    return;
  }

  // 没有活动标签：没有正文可显示。
  live.document = null;
  live.editor = live.diff ? "diff" : "empty";
}

/** 用已读取的载荷建立或复用标签；调用方负责令牌检查。 */
function openDocumentTab(path, payload, options = {}) {
  const { preview = false, activate = true } = options;
  const live = tabState();
  if (!live) return;
  const existing = live.tabs.find((tab) => tab.kind === "document" && tab.path === path);
  if (existing) {
    if (!preview) existing.preview = false;
    if (activate) {
      live.activeTabId = existing.id;
      syncActiveTab();
    }

    return;
  }

  const model = toLiveDocument(payload);
  const tab = {
    id: nextTabId(),
    kind: "document",
    path,
    title: model.name || path,
    document: model,
    editor: model.editor,
    preview,
  };
  // 临时预览标签只保留一个：新的预览顶替旧的，位置不变。
  const previewIndex = live.tabs.findIndex((item) => item.kind === "document" && item.preview);
  if (preview && previewIndex >= 0) {
    live.tabs[previewIndex] = tab;
  } else {
    live.tabs.push(tab);
  }

  if (activate) {
    live.activeTabId = tab.id;
    syncActiveTab();
  }
}

/** 激活指定标签。 */
function activateTab(id) {
  const live = tabState();
  if (!live || !live.tabs.some((tab) => tab.id === id)) return;
  if (live.activeTabId === id) return;
  live.activeTabId = id;
  syncActiveTab();
  refreshAfterEvent("editorContent", "editorTabs", "side", "statusbar", "titlebar");
}

/**
 * 关闭指定标签（规格 §5.2）。
 * 关闭后台标签只更新标签栏；关闭当前标签则激活相邻标签（先右后左）。
 */
function closeTab(id) {
  const live = tabState();
  if (!live) return;
  const index = live.tabs.findIndex((tab) => tab.id === id);
  if (index < 0) return;
  const closing = live.tabs[index];
  const wasActive = live.activeTabId === id;
  live.tabs.splice(index, 1);
  // 关闭比较标签：解除跟随 Changes 选择（规格 §5.2）。
  // 历史比较同样在此解除跟随——关闭后单击不自动重开，只有再次双击或 Enter 才打开。
  if (closing && closing.kind === "comparison") {
    live.followChanges = false;
    if (live.historyComparison) live.historyComparison.status = "closed";
    closeDiff();
  }

  if (!wasActive) {
    refreshAfterEvent("editorTabs");
    return;
  }

  const neighbour = live.tabs[index] || live.tabs[index - 1] || null;
  live.activeTabId = neighbour ? neighbour.id : null;
  syncActiveTab();
  refreshAfterEvent("editorContent", "editorTabs", "side", "statusbar", "titlebar");
}

/** Ctrl+W：关闭当前标签并激活相邻标签。 */
function closeActiveTab() {
  const live = tabState();
  if (!live || !live.activeTabId) return;
  closeTab(live.activeTabId);
}

/**
 * 由指针/键盘事件触发的刷新。
 *
 * 为什么要单独一个入口：这些事件的处理挂在 document 的**捕获阶段**，
 * 而视觉稿自己的监听（双击改动行建比较标签、列表键盘处理等）挂在**冒泡阶段**。
 * 若在捕获阶段同步替换被点击的元素，它会在这个事件的后续阶段开始前离开文档，
 * 冒泡阶段的监听就再也收不到这个事件。
 *
 * 因此这里把替换推迟到当前事件派发结束之后（宏任务），
 * 让所有阶段的监听都能看到原来的节点。
 */
function refreshAfterEvent(...regions) {
  window.setTimeout(() => refresh(...regions), 0);
}

/**
 * 工具窗口切换与折叠（规格 §5.1）。
 *
 * 左侧顶部固定顺序：项目、提交、搜索；左侧底部固定顺序：终端、Git 历史。
 * - 点击已激活的入口：折叠该区域，再次点击恢复上次状态；
 * - 点击同区域的另一个入口：原位替换；
 * - 终端与 Git 历史互斥，不能同时占用底部区域。
 *
 * 布局由 live.layout 驱动，shell() 读取它；视觉稿单独打开时没有 live，
 * 因此静态浏览行为不变。
 */
// 搜索按视觉稿是**浮层**而不是侧栏内容，因此不属于侧栏区域。
const RAIL_SIDE = ["project", "commit"];
const RAIL_BOTTOM = ["terminal", "history"];
// 视觉稿的入口用中文 aria-label 标识；沿用同一标识，避免改动设计基线标记。
const RAIL_LABELS = {
  项目: "project",
  提交: "commit",
  搜索: "search",
  终端: "terminal",
  "Git 历史": "history",
};
// 底部区域的入口标识与 shell() 的 bottom 取值不同名（Git 历史的取值是 git）。
const RAIL_BOTTOM_VALUE = { terminal: "terminal", history: "git" };

/** 从当前 DOM 读出场景给出的初始布局，作为 live.layout 的起点。 */
function readInitialLayout() {
  const buttons = [...document.querySelectorAll(".tool-rail .rail-button")];
  const names = buttons.map((b) => RAIL_LABELS[b.getAttribute("aria-label")] || null);
  const activeIndex = buttons.findIndex((b) => b.classList.contains("active"));
  const activeRail = activeIndex >= 0 ? names[activeIndex] : "project";
  // 搜索入口保持侧栏为项目，搜索界面在浮层里（与视觉稿一致）。
  const side = RAIL_SIDE.includes(activeRail) ? activeRail : "project";
  const bottom = RAIL_BOTTOM.includes(activeRail) ? (RAIL_BOTTOM_VALUE[activeRail] || "") : "";
  return { activeRail, side, bottom, collapsed: null, userDriven: false };
}

function currentLayout() {
  const live = window.__augitLive;
  // 首屏渲染可能早于 live 对象建立；此时没有布局可谈，交由调用方跳过。
  if (!live) return null;
  live.layout ??= readInitialLayout();
  return live.layout;
}

function applyRailAction(name) {
  const layout = currentLayout();
  if (!layout) return;
  // 标记为「用户已操作」，此后由 live.layout 接管场景值。
  layout.userDriven = true;
  const previousCollapsed = layout.collapsed;
  const previousBottom = layout.bottom;
  const inSide = RAIL_SIDE.includes(name);
  const isActive = layout.activeRail === name;
  if (isActive) {
    // 搜索是浮层：已激活时再次点击只是关闭浮层，不折叠侧栏或底部。
    if (name === "search") {
      closeLiveOverlay();
      return;
    }

    // 再次点击同一入口：折叠 / 恢复该区域。
    layout.collapsed = layout.collapsed === (inSide ? "side" : "bottom") ? null : (inSide ? "side" : "bottom");
  } else {
    layout.activeRail = name;
    layout.collapsed = null;
    if (name === "search") {
      // 搜索打开浮层；侧栏内容保持项目（视觉稿 repository-search 即如此）。
      layout.side = "project";
      layout.bottom = "";
    } else if (inSide) {
      layout.side = name;
    } else {
      layout.bottom = RAIL_BOTTOM_VALUE[name] || "";
    }
  }

  if (name === "search") {
    // 搜索入口打开搜索浮层；不重建编辑器与底部区域（规格 §9.4）。
    refreshAfterEvent("rail", "side", "bottomTool", "editorContent", "statusbar");
    openSearchOverlay("repository");
    return;
  }

  // 区域替换只在「两侧都存在」时替换，无法表达节点的出现与消失。
  // 折叠/恢复会增删 .side-tool 或 .bottom-tool，那一步必须整页重绘；
  // 仅仅是同区域内切换或跨区域切换时，两个区域节点都在，走定点替换即可，
  // 这样不会丢掉编辑标签与已建立的组件实例。
  const needsStructural = previousCollapsed !== layout.collapsed
    || (previousBottom === "") !== (layout.bottom === "");
  if (needsStructural && typeof window.__augitRender === "function") {
    // 整页重绘会换掉工具窗口入口，必须重新挂上动作；
    // 否则下一次点击会走 <a> 的默认跳转，直接离开应用页面。
    window.__augitRender();
  } else {
    refresh("rail", "side", "bottomTool", "editorContent", "statusbar");
  }

  bindToolRail();
}

function bindToolRail() {
  if (!window.__augitLive || window.__augitRailBound) return;
  window.__augitRailBound = true;
  // 挂在 document 的捕获阶段：工具窗口入口会在整页重绘时被换掉，
  // 挂在节点上的监听会随节点一起消失，导致下一次点击走 <a> 默认跳转离开应用。
  // 放在捕获阶段还能在默认动作之前阻止跳转。
  document.addEventListener("click", (event) => {
    const button = event.target.closest && event.target.closest(".tool-rail .rail-button");
    if (!button) return;
    // 外壳里这些入口是应用内动作，不是页面跳转。
    event.preventDefault();
    const name = RAIL_LABELS[button.getAttribute("aria-label")] || null;
    if (name) applyRailAction(name);
  }, true);
}

/**
 * 改动列表的勾选与提交草稿（规格 §5.2）。
 *
 * 这两项必须保存在状态里而不是只放在 DOM 上：关闭比较标签、外部变化刷新、
 * 切换工具窗口都会替换改动列表节点，DOM 里的勾选与草稿会随之丢失。
 * 规格明确要求「关闭比较保留 Changes 的选中行、勾选、草稿和滚动」。
 */
function toggleChangeChecked(path, checked) {
  const live = window.__augitLive;
  if (!live || !live.status) return;
  const file = live.status.files.find((item) => item.path === path);
  if (!file) return;
  file.checked = checked === undefined ? !file.checked : !!checked;
  refreshAfterEvent("side");
}

function toggleChangeGroup(group, checked) {
  const live = window.__augitLive;
  if (!live || !live.status) return;
  const label = group === "UnversionedFiles" ? "Unversioned Files" : group;
  for (const file of live.status.files) {
    if (file.group === label) file.checked = !!checked;
  }

  refreshAfterEvent("side");
}

/** 记录提交信息草稿，供列表重绘后恢复。 */
function rememberCommitDraft(value) {
  const live = window.__augitLive;
  if (!live) return;
  live.commitDraft = value;
}

/**
 * 把草稿与滚动位置写回改动列表。
 * 区域替换会新建 textarea 与列表容器，草稿与滚动位置都只存在于旧节点上，
 * 因此每次刷新后都要恢复（规格 §5.2：关闭比较保留草稿与滚动）。
 */
function restoreChangesState() {
  const live = window.__augitLive;
  if (!live) return;
  const box = document.querySelector(".commit-box .message-field");
  if (box && typeof live.commitDraft === "string" && box.value !== live.commitDraft) {
    box.value = live.commitDraft;
  }

  // 提交校验与失败的反馈写回视觉稿已有的提示位（规格 §7.6）。
  const feedback = document.querySelector(".commit-box .commit-feedback");
  if (feedback) {
    const error = window.__augitCommitError || "";
    feedback.textContent = error || "提交信息";
    feedback.title = error || "提交信息";
    feedback.classList.toggle("error", error.length > 0);
  }

  const list = document.querySelector(".changes-list");
  if (list && typeof live.changesScrollTop === "number") {
    list.scrollTop = live.changesScrollTop;
  }

  // 恢复选中行（规格 §5.2）。
  if (live.selectedChangePath) {
    const rows = [...document.querySelectorAll(".changes-list .change-file-row")];
    const target = rows.find((row) => row.dataset.path === live.selectedChangePath);
    if (target && !target.classList.contains("selected")) {
      for (const other of rows) {
        other.classList.remove("selected");
        other.removeAttribute("aria-selected");
      }

      target.classList.add("selected");
      target.setAttribute("aria-selected", "true");
    }
  }
}

/** 记住改动列表的滚动位置，供刷新后恢复。 */
function bindChangesScroll() {
  const list = document.querySelector(".changes-list");
  if (!list || list.__augitScrollBound) return;
  list.__augitScrollBound = true;
  list.addEventListener("scroll", () => {
    const live = window.__augitLive;
    if (live) live.changesScrollTop = list.scrollTop;
  }, { passive: true });
}

/** 绑定勾选、分组勾选与草稿输入（挂在 document 上，不受区域刷新影响）。 */
function bindChangesState() {
  if (!window.__augitLive || window.__augitChangesBound) return;
  window.__augitChangesBound = true;
  document.addEventListener("click", (event) => {
    const check = event.target.closest && event.target.closest(".changes-list .fake-check");
    if (check) {
      event.preventDefault();
      const row = check.closest(".check-row");
      if (!row) return;
      if (row.classList.contains("check-group-row")) {
        const state = check.getAttribute("aria-checked");
        toggleChangeGroup(row.dataset.group, state !== "true");
        return;
      }

      toggleChangeChecked(row.dataset.path);
      return;
    }

    const chevron = event.target.closest && event.target.closest(".changes-list .change-chevron");
    if (chevron) event.preventDefault();
  }, true);

  document.addEventListener("input", (event) => {
    const box = event.target.closest && event.target.closest(".commit-box .message-field, .commit-box textarea");
    if (box) rememberCommitDraft(box.value);
  }, true);
}

/**
 * 兜住未接线的跳转链接。
 *
 * 视觉稿为逐场景浏览把交互写成指向 `*.html` 的链接；在外壳里这些应当是应用内动作。
 * 已经接线的入口（工具窗口入口、标签、改动行等）会在各自的处理里 preventDefault；
 * 这里兜住**尚未接线**的那些，避免点击后整页导航离开应用——那会让界面退回静态视觉稿，
 * 与「这是一个应用」直接冲突。
 *
 * 必须挂在 window 的捕获阶段：它晚于 document 上的既有处理，因此不会抢走已接线的动作；
 * 又早于浏览器的默认导航，因此能拦住未接线的那些。
 */
function guardUnwiredNavigation() {
  if (!window.__augitLive || window.__augitNavGuarded) return;
  window.__augitNavGuarded = true;
  window.addEventListener("click", (event) => {
    if (event.defaultPrevented) return;
    // 分支弹层的快捷动作。
    const popoverAction = event.target.closest && event.target.closest("[data-augit-overlay] [data-popover-action]");
    if (popoverAction) {
      event.preventDefault();
      void runPopoverAction(popoverAction.dataset.popoverAction);
      return;
    }

    // 分支弹层的二级动作（新建 / 重命名）：打开紧凑输入窗口（规格 §5.3）。
    const branchAction = event.target.closest && event.target.closest("[data-augit-overlay] [data-branch-action]");
    if (branchAction) {
      event.preventDefault();
      openBranchActionDialog(branchAction.dataset.branchAction);
      return;
    }

    // Push 内嵌的「定义远端」：打开嵌套窗口（规格 §7.12）。
    // 只在本轮实现挂载与关闭行为，不改动弹层归属机制。
    const defineRemote = event.target.closest && event.target.closest('a[href$="remote.html"]');
    if (defineRemote && document.querySelector(".dialog.push-dialog")) {
      event.preventDefault();
      openRemoteDialog();
      return;
    }

    // 写操作的取消入口（规格 §9.3）。
    const writeCancel = event.target.closest && event.target.closest("[data-write-cancel]");
    if (writeCancel) {
      event.preventDefault();
      void cancelWriteOperation();
      return;
    }

    // 提交设置入口：打开设置对话框（规格 §7.6 的入口适配）。
    const settingsEntry = event.target.closest && event.target.closest('[aria-label="提交设置"]');
    if (settingsEntry && document.querySelector(".commit-actions")) {
      event.preventDefault();
      openSettingsDialog();
      return;
    }

    // 设置对话框的动作。
    const settingsAction = event.target.closest && event.target.closest("[data-settings-action]");
    if (settingsAction) {
      event.preventDefault();
      if (settingsAction.dataset.settingsAction === "save") {
        void saveSettings().catch((error) => {
          window.__augitError = "save-settings:" + String(error && error.message || error);
        }).finally(() => closeSettingsDialog());
      } else {
        closeSettingsDialog();
      }

      return;
    }

    // Worktree 窗口的动作。
    const worktreeAction = event.target.closest && event.target.closest("[data-worktree-action]");
    if (worktreeAction) {
      event.preventDefault();
      void runWorktreeAction(worktreeAction.dataset.worktreeAction);
      return;
    }

    // 远端窗口的动作。
    const remoteAction = event.target.closest && event.target.closest("[data-remote-action]");
    if (remoteAction) {
      event.preventDefault();
      void runRemoteAction(remoteAction.dataset.remoteAction);
      return;
    }

    // 推送对话框的动作。
    const pushAction = event.target.closest && event.target.closest("[data-push-action]");
    if (pushAction) {
      event.preventDefault();
      if (pushAction.dataset.pushAction === "confirm") void confirmPushDialog();
      else closePushDialog();
      return;
    }

    // 紧凑输入窗口的按钮与标题栏关闭。
    const compactAction = event.target.closest && event.target.closest("[data-compact-dialog] [data-compact-action]");
    if (compactAction) {
      event.preventDefault();
      const action = compactAction.dataset.compactAction;
      if (action === "confirm") void submitCompactDialog();
      else closeCompactDialog();
      return;
    }

    // 变化文件右键菜单里的动作。
    const changesMenu = event.target.closest && event.target.closest(".changes-menu .menu-item");
    if (changesMenu) {
      event.preventDefault();
      const label = (changesMenu.textContent || "").trim();
      const action = label.includes("显示 Diff") ? "diff"
        : label.includes("文件历史") ? "file-history"
          : label.includes("Blame") ? "blame" : null;
      if (action) void runChangesContextAction(action);
      else closeLiveOverlay();
      return;
    }

    // 分支弹层里的引用行是 <div>（不是链接），单独处理：点击即检出（规格 §7.11）。
    const branchRow = event.target.closest && event.target.closest("[data-augit-overlay] [data-branch]");
    if (branchRow) {
      event.preventDefault();
      void checkoutReference(branchRow.dataset.branch, branchRow.dataset.branchKind || "branch");
      return;
    }

    // 标题栏内嵌菜单的五个入口（规格 §5.1）：
    // 必须放在"链接兜底"之前，否则会先被记成未接线动作。
    const menuEntry = event.target.closest && event.target.closest(".titlebar .main-menu-entry");
    if (menuEntry) {
      const menuAction = MAIN_MENU_ACTIONS[(menuEntry.textContent || "").trim()];
      if (menuAction) {
        event.preventDefault();
        // 规格：终端与设置执行前先恢复普通标题栏。
        closeMainMenu();
        void runMainMenuAction(menuAction);
        return;
      }
    }

    const link = event.target.closest && event.target.closest('a[href$=".html"]');
    if (!link) return;
    event.preventDefault();
    // 已接线的入口在这里做应用内动作。
    if (link.classList.contains("branch-chip")) {
      openBranchesPopover();
      return;
    }



    // 改动列表的提交动作（规格 §7.6）。
    const commitActions = link.closest(".commit-actions");
    if (commitActions && isControlDisabled(link)) return;
    if (commitActions) {
      if (link.classList.contains("primary-button")) {
        void commitSelectedChanges(false);
        return;
      }

      if (link.textContent.includes("提交并推送")) {
        // 推送尚未接线，先完成提交并如实说明。
        void commitSelectedChanges(true);
        return;
      }
    }

    // 其余尚未接线：留在应用内，记录以便定位。
    window.__augitUnwiredAction = link.getAttribute("href");
    window.__augitUnwiredLabel = (link.getAttribute("aria-label") || link.innerText || "").trim().slice(0, 40);
  }, true);
}

/**
 * 提交用户在改动列表勾选的文件（规格 §7.6）。
 *
 * 只提交勾选项：未勾选的文件不进提交，也不把既有暂存项带入。
 * 提交前后都不自行推断状态，成功与否都以宿主返回的真实结果为准。
 */
async function commitSelectedChanges(andPush) {
  const live = window.__augitLive;
  if (!live || !live.status) return;
  const files = live.status.files || [];
  const selected = files.filter((file) => file.checked);
  const message = typeof live.commitDraft === "string" ? live.commitDraft.trim() : "";
  if (selected.length === 0) {
    window.__augitCommitError = describeFailure("请至少选择一个要提交的文件。", {
      unchanged: "工作区没有变化。",
      next: "在改动列表里勾选要提交的文件。",
    });
    refreshAfterEvent("side");
    return;
  }

  if (message.length === 0) {
    window.__augitCommitError = describeFailure("提交信息不能为空。", {
      unchanged: "勾选保持不变。",
      next: "填写提交信息后重试。",
    });
    refreshAfterEvent("side");
    return;
  }

  // 进行中：禁用重复触发（规格 §9.3），并记录当前动作供界面显示。
  if (live.writeOperation) return;
  live.writeOperation = "提交";
  refreshAfterEvent("side", "statusbar");

  // 每次提交前清掉上一次的错误与结果。
  // 结果必须在**入口**就清：只在失败分支清会漏掉异常抛出等路径，
  // 那时界面会继续显示上次的成功哈希，让用户以为本次也提交成功了。
  window.__augitCommitError = null;
  window.__augitCommitResult = null;
  let result;
  try {
    result = await invoke("git/commit-create", { message, paths: selected.map((file) => file.path) }, 120000);
  } catch (error) {
    window.__augitCommitResult = null;
    window.__augitCommitError = String(error && error.message || error);
    live.writeOperation = null;
    refreshAfterEvent("side", "statusbar");
    return;
  }

  if (!result || !result.committed) {
    window.__augitCommitResult = null;
    window.__augitCommitError = describeFailure((result && result.reason) || "提交失败。", {
      unchanged: "改动列表、勾选与提交信息都没有变化。",
      next: "修正后可直接重试。",
    });
    live.writeOperation = null;
    refreshAfterEvent("side", "statusbar");
    return;
  }

  // 提交成功后按规格 §9.3 刷新相关事实；进行中标记在重读状态之后清除。
  live.writeOperation = null;

  // 提交成功：清空草稿、重新读取真实状态，并按块刷新。
  live.commitDraft = "";
  live.selectedChangePath = null;
  live.followChanges = false;
  window.__augitCommitResult = { hash: result.commitHash, andPush: !!andPush, pushed: false };

  if (andPush) {
    // 提交已经成功，推送失败不能把整件事报成失败：
    // 分开记录，让用户知道「提交成功、推送失败」这个真实状态。
    const push = await pushCurrentBranch();
    window.__augitCommitResult = {
      hash: result.commitHash,
      andPush: true,
      pushed: !!(push && push.pushed),
      pushReason: push && !push.pushed ? push.reason : null,
    };
    if (push && !push.pushed) {
      window.__augitCommitError = "提交已成功，但推送失败：" + (push.reason || "原因未知。");
    }
  }

  await loadStatus().catch(() => null);
  refreshAfterEvent("side", "editorContent", "editorTabs", "statusbar", "bottomTool");
}

/**
 * 推送当前分支（规格 §7.12）。未配置远端时不伪造成功。
 */
async function pushCurrentBranch() {
  try {
    const result = await invoke("git/push", {}, 300000);
    return result || { pushed: false, reason: "推送没有返回结果。" };
  } catch (error) {
    return { pushed: false, reason: String(error && error.message || error) };
  }
}

/**
 * 打开分支与标签弹层（规格 §5.3 的非模态弹层）。
 * 复用视觉稿的 liveBranchesPopover，避免另写一套标记。
 */
function openBranchesPopover() {
  const live = window.__augitLive;
  if (!live) return;
  closeLiveOverlay();
  rememberDialogFocus();
  if (!live.references) {
    // 引用数据尚未到达：提示而不是静默无反应。
    window.__augitError = "branches:references-not-ready";
    return;
  }

  // liveBranchesPopover 返回的**本身就是完整覆盖层**（含 .overlay-layer 与 data 属性），
  // 因此这里不再包第二层——实测重复包裹会出现嵌套的两层覆盖层。
  const host = document.querySelector(".augit-window");
  if (!host) return;
  closeLiveOverlay();
  const template = document.createElement("template");
  template.innerHTML = liveBranchesPopover();
  const layer = template.content.firstElementChild;
  if (!layer) return;
  layer.classList.add("live-overlay");
  // 弹层没有遮罩，补一个点击即关闭的层（与视觉稿其它弹层一致）。
  const scrim = document.createElement("div");
  scrim.className = "scrim";
  scrim.addEventListener("click", () => closeLiveOverlay());
  layer.prepend(scrim);
  host.appendChild(layer);
}

/**
 * 检出分支、远端引用或标签（规格 §7.11）。
 *
 * 工作区不干净时由 Git 自身拒绝，Augit 不代为清理、不伪造成功；
 * 失败原因如实回传并保留弹层，让用户可以改选。
 */
async function checkoutReference(name, kind) {
  const live = window.__augitLive;
  if (!live || !name) return;
  window.__augitCheckoutError = null;
  let result;
  try {
    result = await invoke("git/checkout", { name, kind }, 120000);
  } catch (error) {
    window.__augitCheckoutError = String(error && error.message || error);
    openBranchesPopover();
    return;
  }

  if (!result || !result.switched) {
    window.__augitCheckoutError = describeFailure((result && result.reason) || "检出失败。", {
      unchanged: "当前分支与工作区都没有变化。",
      next: "请先提交或贮藏改动，再重试。",
    });
    // 保留弹层并把原因显示出来，不静默关闭。
    openBranchesPopover();
    return;
  }

  // 成功：关闭弹层，重新读取状态与引用（HEAD 与工作区都已改变）。
  closeLiveOverlay();
  live.references = null;
  await Promise.all([
    loadStatus().catch(() => null),
    loadReferences().catch(() => null),
  ]);
  refreshAfterEvent("titlebar", "side", "editorContent", "editorTabs", "statusbar", "bottomTool");
}

/**
 * 紧凑单行输入窗口（规格 §5.3）。
 *
 * Tab 按「输入框 → 取消 → 确定 → 标题栏关闭」循环，Shift+Tab 反向；
 * 输入框或确定按钮上的 Enter 确认；取消或关闭按钮上的 Enter、Esc 与标题栏关闭均取消；
 * 中文输入法组词期间 Enter 与 Esc 交给输入法，不提交也不关闭。
 */
let compactDialogKind = null;

function openBranchActionDialog(kind) {
  const live = window.__augitLive;
  if (!live) return;
  closeLiveOverlay();
  rememberDialogFocus();
  const current = (live.references && live.references.branches || []).find((item) => item.isCurrent);
  compactDialogKind = kind;
  if (kind === "rename" && !current) {
    window.__augitCheckoutError = "当前不在任何本地分支上，无法重命名。";
    openBranchesPopover();
    return;
  }

  const title = kind === "create" ? "新建分支" : "重命名分支";
  const label = kind === "create" ? "分支名" : "新名称";
  const value = kind === "rename" ? (current ? current.name : "") : "";
  const host = document.querySelector(".augit-window");
  if (!host) return;
  document.querySelectorAll("[data-augit-overlay].live-overlay").forEach((node) => node.remove());
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = compactInputDialog(title, label, value, kind === "create" ? "创建" : "重命名");
  host.appendChild(layer);
  const field = layer.querySelector("[data-compact-field]");
  if (field) {
    field.focus();
    field.select();
  }
}

function closeCompactDialog() {
  compactDialogKind = null;
  closeLiveOverlay();
  restoreDialogFocus();
}

/** 提交紧凑输入窗口；业务校验在调用方做，窗口本身不写 Git。 */
async function submitCompactDialog() {
  const layer = document.querySelector("[data-compact-dialog]");
  const field = layer && layer.querySelector("[data-compact-field]");
  if (!field) return;
  const value = field.value.trim();
  if (value.length === 0) {
    window.__augitCheckoutError = "名称不能为空。";
    return;
  }

  const kind = compactDialogKind;
  const live = window.__augitLive;
  let result;
  try {
    if (kind === "checkout-revision") {
      // 标签与任意版本都走 detach 检出，名称由 Git 解析，Augit 不猜。
      result = await invoke("git/checkout", { name: value, kind: "tag" }, 120000);
      result = result ? { changed: result.switched, reason: result.reason } : result;
    } else if (kind === "rename") {
      result = await invoke("git/branch", { action: "rename", from: currentBranchName(), name: value }, 60000);
    } else {
      result = await invoke("git/branch", { action: "create", name: value }, 60000);
    }
  } catch (error) {
    window.__augitCheckoutError = String(error && error.message || error);
    closeCompactDialog();
    openBranchesPopover();
    return;
  }

  if (!result || !result.changed) {
    window.__augitCheckoutError = describeFailure((result && result.reason) || "分支操作失败。", {
      unchanged: "已有分支与当前分支都没有变化。",
      next: "请换一个名称后重试。",
    });
    closeCompactDialog();
    openBranchesPopover();
    return;
  }

  closeCompactDialog();
  if (live) {
    live.references = null;
    await loadReferences().catch(() => null);
  }

  refreshAfterEvent("titlebar", "side", "bottomTool", "statusbar");
}

function currentBranchName() {
  const live = window.__augitLive;
  const current = live && live.references && (live.references.branches || []).find((item) => item.isCurrent);
  return current ? current.name : "";
}

/** 紧凑输入窗口的键盘规则（规格 §5.3）。 */
function bindCompactDialogKeys() {
  if (window.__augitCompactBound) return;
  window.__augitCompactBound = true;
  document.addEventListener("keydown", (event) => {
    const layer = document.querySelector("[data-compact-dialog]");
    if (!layer) return;
    // 组词期间 Enter 与 Esc 交给输入法。
    if (event.isComposing || event.keyCode === 229) return;
    const field = layer.querySelector("[data-compact-field]");
    const order = [
      field,
      layer.querySelector('[data-compact-action="cancel"]'),
      layer.querySelector('[data-compact-action="confirm"]'),
      layer.querySelector('[data-compact-action="close"]'),
    ].filter(Boolean);

    if (event.key === "Escape") {
      event.preventDefault();
      closeCompactDialog();
      return;
    }

    if (event.key === "Tab") {
      event.preventDefault();
      const index = order.indexOf(document.activeElement);
      const backwards = event.shiftKey;
      const next = order[(index + (backwards ? -1 : 1) + order.length) % order.length];
      if (next) next.focus();
      return;
    }

    if (event.key !== "Enter") return;
    const target = document.activeElement;
    // 输入框或确定按钮上的 Enter 确认；取消与关闭按钮上的 Enter 取消。
    const onInput = target === field;
    const onConfirm = target && target.dataset.compactAction === "confirm";
    event.preventDefault();
    if (onInput || onConfirm) void submitCompactDialog();
    else closeCompactDialog();
  }, true);
}

/**
 * 分支弹层里的快捷动作（规格 §7.11 / §5.1）。
 * 需要写 Git 的动作只在确认后执行；失败原因如实回传。
 */
async function runPopoverAction(action) {
  const live = window.__augitLive;
  if (!live) return;
  window.__augitCheckoutError = null;

  // 纯界面动作：切到提交工具窗口，不碰 Git。
  if (action === "commit") {
    closeLiveOverlay();
    live.layout = live.layout || {};
    live.layout.userDriven = true;
    live.layout.activeRail = "commit";
    live.layout.side = "commit";
    live.layout.collapsed = null;
    if (typeof window.__augitRender === "function") window.__augitRender();
    rebindAfterRender();
    return;
  }

  if (action === "create-branch") {
    openBranchActionDialog("create");
    return;
  }

  if (action === "checkout-revision") {
    openRevisionDialog();
    return;
  }

  if (action === "compare-workspace") {
    void compareWithWorkspace();
    return;
  }

  if (action === "create-worktree") {
    openWorktreeDialog();
    return;
  }

  if (action === "push") {
    // 规格 §7.12：推送前先显示待推送提交并让用户确认，不直接推送。
    void openPushDialog();
    return;
  }

  const method = action === "fetch" ? "git/fetch" : null;
  if (!method) return;
  const key = "fetched";
  let result;
  try {
    result = await invoke(method, {}, 300000);
  } catch (error) {
    window.__augitCheckoutError = String(error && error.message || error);
    openBranchesPopover();
    return;
  }

  if (!result || !result[key]) {
    window.__augitCheckoutError = describeFailure((result && result.reason) || "操作失败。", {
      unchanged: "本地引用与工作区都没有变化。",
      next: "请检查网络或远端配置后重试。",
    });
    openBranchesPopover();
    return;
  }

  // 成功：关闭弹层并重新读取状态与引用。
  closeLiveOverlay();
  live.references = null;
  await Promise.all([
    loadStatus().catch(() => null),
    loadReferences().catch(() => null),
  ]);
  refreshAfterEvent("titlebar", "side", "bottomTool", "statusbar");
}

/**
 * 与工作区比较（规格 §7.9 引用比较）。
 *
 * 以当前分支为基准、对当前选中的改动文件生成差异。
 * 没有任何改动文件时不创建比较标签，而是如实说明——避免打开一个空比较。
 */
async function compareWithWorkspace() {
  const live = window.__augitLive;
  if (!live) return;
  const files = (live.status && live.status.files) || [];
  const target = files.find((file) => file.path === live.selectedChangePath) || files[0];
  if (!target) {
    window.__augitCheckoutError = "当前没有可比较的改动文件。";
    openBranchesPopover();
    return;
  }

  const branch = currentBranchName() || "HEAD";
  closeLiveOverlay();
  // 引用比较独立于 Changes 跟随（规格 §5.2/§7.9）：它比较的是整个引用与工作区，
  // 不是当前选中的某个改动文件，因此必须解除跟随，否则后续单击改动行会把它改写成工作区 Diff。
  live.followChanges = false;
  const diff = await loadDiff(target.path, { revision: branch, force: true }).catch(() => null);
  if (!diff) {
    window.__augitError = "compare-workspace:" + target.path;
    return;
  }

  const title = `比较: ${branch}`;
  const existing = findComparisonTab();
  if (!existing) {
    live.tabs = live.tabs || [];
    const tab = {
      id: nextTabId(),
      kind: "comparison",
      path: target.path,
      title,
      editor: "diff",
      preview: false,
    };
    live.tabs.push(tab);
    activateComparisonTab(tab);
  } else {
    // 复用的比较标签必须同步目标，否则标签仍显示上一个比较的文件名。
    syncComparisonTab(existing, target.path, title);
    activateComparisonTab(existing);
  }

  live.editor = "diff";
  refreshAfterEvent("editorContent", "editorTabs", "statusbar");
}

/**
 * 推送对话框（规格 §7.12）。
 *
 * 推送前先读取待推送提交并显示；未配置上游或读取失败时**禁用推送**并给出原因，
 * 同时保留「定义远端」入口。预览未就绪或没有待推送提交时不允许推送。
 */
async function openPushDialog() {
  const live = window.__augitLive;
  if (!live) return;
  rememberDialogFocus();
  closeLiveOverlay();
  const host = document.querySelector(".augit-window");
  if (!host) return;

  // 打开时重新读取，避免显示过期预览。
  window.__augitPushDialogOpen = true;
  await loadUnpushed();
  if (!window.__augitPushDialogOpen) return;
  renderPushDialog();
}

function renderPushDialog() {
  const live = window.__augitLive;
  if (!live) return;
  const push = live.push || { branch: live.status && live.status.branch, upstream: null, commits: [], ready: false };
  const host = document.querySelector(".augit-window");
  if (!host) return;
  // 场景自带一份静态 Push 弹层（没有 live-overlay 类）。只清 .live-overlay 会留下两份，
  // 用户可能点到没有动作的那一份，选择器也会因此歧义。
  document.querySelectorAll("[data-augit-overlay].live-overlay").forEach((node) => node.remove());
  document.querySelectorAll(".dialog.push-dialog").forEach((node) => {
    const owner = node.closest("[data-augit-overlay]") || node;
    owner.remove();
  });
  const canPush = push.ready === true && (push.commits || []).length > 0;
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "Push",
    livePushDialogBody(),
    `<button type="button" class="secondary-button" data-push-action="cancel">取消</button>`
      + `<button type="button" class="primary-button" data-push-action="confirm"${canPush ? "" : " disabled"}>推送</button>`,
    true,
    "push-dialog");
  host.appendChild(layer);
  const list = layer.querySelector(".push-commits");
  if (list) list.focus();
}

/** 关闭推送对话框。 */
function closePushDialog() {
  window.__augitPushDialogOpen = false;
  closeLiveOverlay();
  restoreDialogFocus();
}

/**
 * Push 内嵌的远端管理窗口（规格 §7.12）。
 *
 * 关闭它**只重新读取推送预览**：不创建第二个 Push 窗口，
 * 不改变提交列表、提交草稿，也不重排主窗口布局。
 */
function openRemoteDialog() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  rememberDialogFocus();
  document.querySelectorAll("[data-augit-overlay].live-overlay.remote-window").forEach((node) => node.remove());
  const remotes = (live.remotes && live.remotes.remotes) || [];
  const first = remotes[0];
  const body = `<div class="management-content"><div class="management-list">`
    + (remotes.length === 0
      ? `<div class="tree-row"><span class="commit-meta">没有配置远端</span></div>`
      : remotes.map((remote) => `<div class="tree-row" data-remote-entry="${escapeText(remote.name)}"><strong>${escapeText(remote.name)}</strong><span class="commit-meta">${escapeText(remote.fetchUrl)}</span></div>`).join(""))
    + `</div><div class="management-detail" tabindex="0" aria-label="远端详情，可滚动阅读">`
    + `<h2>${first ? escapeText(first.name) : "定义远端"}</h2>`
    + `<div class="form-grid"><label for="remote-name">名称</label>`
    + `<input id="remote-name" class="text-field" data-remote-field="name" value="${escapeText(first ? first.name : "origin")}">`
    + `<label for="remote-fetch">获取 URL</label>`
    + `<input id="remote-fetch" class="text-field" data-remote-field="fetchUrl" value="${escapeText(first ? first.fetchUrl : "")}">`
    + `<label for="remote-push">推送 URL</label>`
    + `<input id="remote-push" class="text-field" data-remote-field="pushUrl" value="${escapeText(first && first.pushUrl ? first.pushUrl : "")}">`
    + `</div><div class="remote-notice" role="status" hidden></div></div></div>`;
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay remote-window";
  layer.setAttribute("data-augit-overlay", "");
  if (first) layer.dataset.remoteCurrent = first.name;
  layer.innerHTML = dialog(
    "远端管理",
    body,
    `<button type="button" class="secondary-button" data-remote-action="cancel">关闭</button>`
      + `<button type="button" class="primary-button" data-remote-action="save">保存远端</button>`,
    true,
    "remote-dialog");
  host.appendChild(layer);
}

/** 关闭远端窗口：只重读推送预览，不动 Push 窗口本身。 */
async function closeRemoteDialog() {
  const live = window.__augitLive;
  document.querySelectorAll("[data-augit-overlay].live-overlay.remote-window").forEach((node) => node.remove());
  if (!live) return;
  restoreDialogFocus();
  const [references, remotes] = await Promise.all([
    invoke("git/references", {}, 30000).catch(() => null),
    invoke("git/remotes", {}, 30000).catch(() => null),
  ]);
  if (references && references.available) live.references = references;
  if (remotes && remotes.available) live.remotes = remotes;
  // 只重读预览；Push 窗口节点原样保留（不新建第二个）。
  await loadUnpushed();
  renderPushDialog();
}

/** 保存远端；失败原因写在远端窗口内，不关闭窗口。 */
async function runRemoteAction(action) {
  if (action === "cancel") {
    await closeRemoteDialog();
    return;
  }

  const layer = document.querySelector(".remote-window");
  if (!layer) return;
  const value = (name) => {
    const field = layer.querySelector(`[data-remote-field="${name}"]`);
    return field ? field.value.trim() : "";
  };

  let result;
  try {
    result = await invoke("git/remote-write", {
      action: layer.dataset.remoteCurrent ? "update" : "add",
      name: value("name"),
      currentName: layer.dataset.remoteCurrent || undefined,
      fetchUrl: value("fetchUrl"),
      pushUrl: value("pushUrl") || undefined,
    }, 60000);
  } catch (error) {
    showRemoteNotice(String(error && error.message || error));
    return;
  }

  if (!result || !result.changed) {
    showRemoteNotice((result && result.reason) || "远端保存失败。");
    return;
  }

  await closeRemoteDialog();
}

function showRemoteNotice(message) {
  const notice = document.querySelector(".remote-window .remote-notice");
  if (!notice) return;
  notice.textContent = message;
  notice.hidden = false;
}

/** 执行推送并关闭对话框；失败时保留对话框并显示原因。 */
async function confirmPushDialog() {
  const live = window.__augitLive;
  const result = await pushCurrentBranch();
  if (!result || !result.pushed) {
    window.__augitPushError = (result && result.reason) || "推送失败。";
    const notice = document.querySelector("[data-augit-overlay] .push-notice");
    if (notice) {
      notice.textContent = window.__augitPushError;
      notice.hidden = false;
    }

    return;
  }

  window.__augitPushError = null;
  window.__augitCommitResult = { pushed: true };
  closePushDialog();
  if (live) {
    live.references = null;
    await Promise.all([loadStatus().catch(() => null), loadReferences().catch(() => null)]);
  }

  refreshAfterEvent("titlebar", "side", "bottomTool", "statusbar");
}

/**
 * 设置对话框（规格 §5.3 的模态对话框）。
 *
 * 复用视觉稿的 liveSettingsBody 与既有的保存绑定；
 * 打开期间不重建背景页面，保存后只应用设置本身的影响。
 */
function openSettingsDialog() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  rememberDialogFocus();
  if (!live.settings) {
    window.__augitError = "settings:not-loaded";
    return;
  }

  closeLiveOverlay();
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay settings-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "设置 — Augit",
    liveSettingsBody(),
    `<button type="button" class="secondary-button" data-settings-action="cancel">取消</button>`
      + `<button type="button" class="primary-button" data-settings-action="save">保存</button>`,
    true,
    "settings-dialog");
  host.appendChild(layer);
  // 复用既有的保存动作绑定（它按 .dialog-xl 结构挂载）。
  bindSettingsSave();
}

/** 关闭设置对话框。 */
function closeSettingsDialog() {
  document.querySelectorAll(".settings-window").forEach((node) => node.remove());
  restoreDialogFocus();
}

/**
 * 新建 Worktree 表单（规格 §7.11）。
 *
 * 两个字段：目标目录与来源分支。字段校验只判断非空；
 * 目录是否可用、分支是否存在由 Git 给出原因（窗口本身不执行 Git 写入）。
 */
function openWorktreeDialog() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  closeLiveOverlay();
  rememberDialogFocus();
  const branch = currentBranchName() || "HEAD";
  const body = `<div class="form-grid">`
    + `<label for="worktree-destination">目录</label>`
    + `<input id="worktree-destination" class="text-field" data-worktree-field="destination" placeholder="D:\\projects\\repository-worktree">`
    + `<label for="worktree-branch">分支</label>`
    + `<input id="worktree-branch" class="text-field" data-worktree-field="branch" value="${escapeText(branch)}">`
    + `</div><div class="worktree-notice" role="status" hidden></div>`;
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay worktree-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "新建 Worktree",
    body,
    `<button type="button" class="secondary-button" data-worktree-action="cancel">取消</button>`
      + `<button type="button" class="primary-button" data-worktree-action="create">创建</button>`,
    false,
    "worktree-dialog");
  host.appendChild(layer);
  const field = layer.querySelector('[data-worktree-field="destination"]');
  if (field) field.focus();
}

function closeWorktreeDialog() {
  document.querySelectorAll(".worktree-window").forEach((node) => node.remove());
  restoreDialogFocus();
}

/** 创建 Worktree；失败原因显示在窗口内并保留输入。 */
async function runWorktreeAction(action) {
  if (action === "cancel") {
    closeWorktreeDialog();
    return;
  }

  const layer = document.querySelector(".worktree-window");
  if (!layer) return;
  const value = (name) => {
    const field = layer.querySelector(`[data-worktree-field="${name}"]`);
    return field ? field.value.trim() : "";
  };
  const destination = value("destination");
  const branch = value("branch");
  if (destination.length === 0) {
    showWorktreeNotice("请填写目标目录。");
    return;
  }

  if (branch.length === 0) {
    showWorktreeNotice("请填写来源分支。");
    return;
  }

  let result;
  try {
    result = await invoke("git/worktree-write", { destination, branch }, 120000);
  } catch (error) {
    showWorktreeNotice(String(error && error.message || error));
    return;
  }

  if (!result || !result.changed) {
    showWorktreeNotice(describeFailure((result && result.reason) || "Worktree 创建失败。", {
      unchanged: "已填写的目录与分支都保留在表单里。",
      next: "换一个空目录后重试。",
    }));
    return;
  }

  closeWorktreeDialog();
  const live = window.__augitLive;
  if (live) {
    live.worktrees = null;
    await loadReferences().catch(() => null);
  }

  refreshAfterEvent("side", "bottomTool", "statusbar");
}

function showWorktreeNotice(message) {
  const notice = document.querySelector(".worktree-window .worktree-notice");
  if (!notice) return;
  notice.textContent = message;
  notice.hidden = false;
}

/** 检出标签或任意版本：紧凑输入窗口，名称交给 Git 解析。 */
function openRevisionDialog() {
  const host = document.querySelector(".augit-window");
  if (!host) return;
  rememberDialogFocus();
  compactDialogKind = "checkout-revision";
  document.querySelectorAll("[data-augit-overlay].live-overlay").forEach((node) => node.remove());
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = compactInputDialog("检出标签或版本", "标签或版本", "", "检出");
  host.appendChild(layer);
  const field = layer.querySelector("[data-compact-field]");
  if (field) field.focus();
}

/**
 * 变化文件的右键菜单（规格 §7.8）。
 *
 * 只对文件行提供，打开菜单**不打开比较**；文件历史与 Blame 由此进入。
 * 菜单项复用视觉稿的 changesContextMenu 节点。
 */
function openChangesContextMenu(row, clientX, clientY) {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host || !row) return;
  rememberDialogFocus();
  const path = row.dataset.path;
  if (!path) return;
  closeLiveOverlay();
  const template = document.createElement("template");
  template.innerHTML = changesContextMenu();
  const menu = template.content.firstElementChild;
  if (!menu) return;
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay changes-menu";
  layer.setAttribute("data-augit-overlay", "");
  layer.dataset.changePath = path;
  // 定位到指针处并夹在窗口内，避免菜单被裁掉。
  const rect = host.getBoundingClientRect();
  layer.style.position = "absolute";
  layer.style.inset = "0";
  menu.style.position = "absolute";
  menu.style.left = `${Math.max(4, Math.min(clientX - rect.left, rect.width - 240))}px`;
  menu.style.top = `${Math.max(4, Math.min(clientY - rect.top, rect.height - 220))}px`;
  menu.style.zIndex = "2";
  layer.appendChild(menu);
  host.appendChild(layer);
}

/**
 * 右键菜单里的动作（规格 §7.8）。
 * 「文件历史」切到底部工具窗口并读取该路径的历史；「Blame」进入归属视图。
 */
async function runChangesContextAction(action) {
  const layer = document.querySelector(".changes-menu");
  const path = layer && layer.dataset.changePath;
  closeLiveOverlay();
  if (!path) return;
  if (action === "diff") {
    await openChangeDiff(path);
    return;
  }

  if (action === "file-history") {
    await loadFileHistory(path);
    const live = window.__augitLive;
    if (live) {
      // 底部工具窗口切到文件历史（规格 §5.1 的同一套布局状态）。
      const hadBottom = !!(live.layout && live.layout.bottom);
      live.layout = live.layout || {};
      live.layout.userDriven = true;
      live.layout.bottom = "file-history";
      live.layout.collapsed = null;
      // 底部区域从无到有是结构性变化：区域替换只在「两侧都存在」时生效，
      // 而该场景没有 .bottom-tool 节点，定点刷新无法把它插进来
      // （实测底部工具窗口始终不出现）。这类变化整页重绘。
      if (!hadBottom && typeof window.__augitRender === "function") {
        window.__augitRender();
        rebindAfterRender();
        return;
      }
    }

    refreshAfterEvent("bottomTool", "statusbar", "editorContent", "editorTabs");
    return;
  }

  if (action === "blame") {
    await loadBlame(path);
    refreshAfterEvent("side", "editorContent", "editorTabs", "statusbar");
  }
}

/**
 * 对话框的焦点恢复（规格 §5.3）。
 *
 * 打开对话框前记录当时的焦点；关闭后：
 * - **取消**：恢复到打开前的元素；
 * - **确认**：回到触发区域（同样是打开前的元素——它就是触发点）。
 * 记录只保留一个：对话框是模态的，不会同时打开两个。
 */
let focusBeforeDialog = null;

/** 打开对话框前调用；记录当前焦点供关闭后恢复。 */
function rememberDialogFocus() {
  // 已有快照就不覆盖：嵌套打开（从弹层里再开对话框）时，
  // 恢复目标应是最外层那一次打开前的元素。
  if (focusBeforeDialog) return;
  const active = document.activeElement;
  focusBeforeDialog = active && active !== document.body ? active : null;
}

/**
 * 关闭对话框后调用；把焦点交回打开前的元素。
 * 元素可能已被区域刷新替换，此时按同样的选择器语义找回——找不到就保持现状，
 * 不强行把焦点塞给不可见节点。
 */
function restoreDialogFocus() {
  const previous = focusBeforeDialog;
  focusBeforeDialog = null;
  if (!previous) return;
  if (previous.isConnected && previous.getClientRects().length > 0) {
    previous.focus({ preventScroll: true });
    return;
  }

  // 已被替换：按 aria-label 或 data 属性找回等价入口。
  const label = previous.getAttribute && previous.getAttribute("aria-label");
  if (!label) return;
  const again = document.querySelector(`[aria-label="${CSS.escape(label)}"]`);
  if (again && again.getClientRects().length > 0) again.focus({ preventScroll: true });
}

/**
 * 给失败原因补上「未改变什么」与「可以做什么」（规格 §10.2）。
 *
 * 宿主给出的原因通常只说明**发生了什么**（例如「目标目录不为空」）。
 * 规格还要求说明**哪些状态未改变**与**用户可以做什么**——
 * 这两项由界面层补，因为它才知道当前保留了哪些输入与区域。
 */
function describeFailure(reason, { unchanged, next }) {
  const parts = [reason || "操作失败。"];
  if (unchanged) parts.push(unchanged);
  if (next) parts.push(next);
  return parts.join(" ");
}

/**
 * 把「进行中」状态体现在改动侧栏（规格 §9.3）。
 *
 * 进行中必须**禁用重复触发**，并让用户看到**当前动作**与可取消入口。
 * 宿主侧写操作支持取消，这里给出取消按钮；点击即请求停止。
 */
function reflectWriteOperation() {
  const live = window.__augitLive;
  if (!live) return;
  const actions = document.querySelector(".side-tool .commit-actions");
  if (!actions) return;
  const busy = !!live.writeOperation;
  const submit = actions.querySelector(".primary-button");
  const push = actions.querySelector(".secondary-button");
  for (const button of [submit, push]) {
    if (!button) continue;
    // 记住渲染时的禁用态，取消后按原样恢复，不凭猜测启用。
    if (!busy) button.dataset.idleDisabled = isControlDisabled(button) ? "true" : "false";
    const shouldDisable = busy || button.dataset.idleDisabled === "true";
    // <a> 的 disabled 属性无效，必须同时用 aria-disabled 与类表达，
    // 否则「进行中禁用重复触发」对链接型按钮形同虚设。
    if (button.tagName === "A") {
      button.setAttribute("aria-disabled", shouldDisable ? "true" : "false");
      button.classList.toggle("disabled", shouldDisable);
    } else {
      button.disabled = shouldDisable;
    }
  }

  let cancel = actions.querySelector("[data-write-cancel]");
  if (busy && !cancel) {
    cancel = document.createElement("button");
    cancel.type = "button";
    cancel.className = "secondary-button";
    cancel.dataset.writeCancel = "true";
    cancel.textContent = "取消";
    // 插到推送按钮之后：取消按钮同样是 .secondary-button，
    // 插在提交按钮之前会让「.secondary-button」指向取消而不是推送。
    const pushButton = actions.querySelector(".secondary-button");
    if (pushButton && pushButton.nextSibling) {
      actions.insertBefore(cancel, pushButton.nextSibling);
    } else {
      actions.appendChild(cancel);
    }
  } else if (!busy && cancel) {
    cancel.remove();
  }

  let label = actions.querySelector("[data-write-status]");
  if (busy && !label) {
    label = document.createElement("span");
    label.className = "commit-meta";
    label.dataset.writeStatus = "true";
    actions.insertBefore(label, actions.firstChild);
  }

  if (label) {
    if (busy) label.textContent = `${live.writeOperation}进行中…`;
    else label.remove();
  }
}

/** 控件是否处于禁用态；链接型按钮按 aria-disabled 判断。 */
function isControlDisabled(element) {
  if (!element) return true;
  if (element.tagName === "A") return element.getAttribute("aria-disabled") === "true";
  return element.disabled === true;
}

/**
 * 取消进行中的写操作（规格 §9.3）。
 *
 * 请求宿主停止后**重新读取真实仓库状态**——不假设取消生效，也不自行回滚。
 */
async function cancelWriteOperation() {
  const live = window.__augitLive;
  if (!live || !live.writeOperation) return;
  const operation = live.writeOperation;
  live.writeOperation = null;
  live.writeCancelReason = `${operation}已请求取消。`;
  // 取消后必须读到真实仓库状态，因此重新读取状态与历史。
  await Promise.all([
    loadStatus().catch(() => null),
    loadHistory().catch(() => null),
  ]);
  refreshAfterEvent("side", "editorContent", "statusbar", "bottomTool");
}

/** 关闭实时弹层。 */
function closeLiveOverlay() {
  const layers = document.querySelectorAll("[data-augit-overlay].live-overlay");
  if (layers.length === 0) return false;
  layers.forEach((node) => node.remove());
  // 通用弹层关闭路径（Esc、遮罩点击、菜单选择）也要交回焦点（规格 §5.3）。
  // 各打开函数都在调用本函数**之前**记录焦点，因此这里恢复的是打开前的元素。
  restoreDialogFocus();
  return true;
}

/**
 * 全局 Esc：只关闭最上层弹层（规格 §5.3）。
 *
 * 视觉稿里弹层自己的 Esc 监听挂在弹层节点上，**只有焦点在弹层内时才生效**；
 * 用户点击别处后焦点离开，Esc 就再也没有反应。这里补一层 document 级处理。
 * 模态对话框自带取消逻辑，不在这里处理，避免重复关闭。
 */
function bindOverlayEscape() {
  if (!window.__augitLive || window.__augitOverlayEscBound) return;
  window.__augitOverlayEscBound = true;
  document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") return;
    if (event.defaultPrevented) return;
    // 组词中的 Esc 交给输入法（规格 §5.3）。
    if (event.isComposing || event.keyCode === 229) return;

    // 推送对话框与其它实时弹层优先关闭。
    if (document.querySelector("[data-push-action]")) {
      event.preventDefault();
      closePushDialog();
      return;
    }

    if (closeLiveOverlay()) {
      event.preventDefault();
      return;
    }

    // 非模态弹层有两类：搜索浮层自身，以及包着 .popover 的 .overlay-layer。
    // 模态对话框同样用 .overlay-layer 包裹，但它自带取消逻辑，交给它自己处理。
    const overlays = [...document.querySelectorAll('[data-augit-overlay]')]
      .filter((node) => !node.querySelector(':scope > .dialog') && !node.classList.contains('dialog'));
    const overlay = overlays.at(-1);
    if (!overlay) return;
    event.preventDefault();
    const popup = overlay.matches('[popover]') ? overlay : overlay.querySelector(':scope > [popover]');
    if (popup && typeof popup.hidePopover === "function" && popup.matches(":popover-open")) {
      popup.hidePopover();
      return;
    }

    overlay.remove();
  }, true);
}

/**
 * 绑定标签栏交互（规格 §5.2）。
 * 标签栏会在区域刷新时被替换，因此监听挂在 document 上，用 data-tab-id 定位。
 */
function bindEditorTabs() {
  if (!window.__augitLive || window.__augitTabBound) return;
  window.__augitTabBound = true;

  // 关闭叉的「按下即捕获」：记录按下时命中的关闭叉。
  // 按下后移出原目标再松开，不得关闭其他标签；pointercancel 视为取消。
  let pressedClose = null;

  document.addEventListener("pointerdown", (event) => {
    pressedClose = event.button === 0 && event.target.closest
      ? event.target.closest(".editor-tabs .tab-close")
      : null;
  }, true);

  document.addEventListener("pointercancel", () => { pressedClose = null; }, true);

  document.addEventListener("click", (event) => {
    const tab = event.target.closest && event.target.closest(".editor-tabs .editor-tab[data-tab-id]");
    if (!tab) return;
    event.preventDefault();
    const closeButton = event.target.closest(".tab-close");
    if (closeButton) {
      // 只有在同一个关闭叉上按下并松开才关闭；否则保留按下记录并放弃本次关闭。
      const sameTarget = pressedClose === closeButton;
      pressedClose = null;
      if (!sameTarget) return;
      // 关闭叉按下时不抢焦点（不调用 focus），并保留关闭前的焦点位置。
      const heldFocus = document.activeElement;
      closeTab(tab.dataset.tabId);
      // 被关闭的标签可能持有焦点；若它已离开文档，把焦点交还给原处。
      if (heldFocus && !heldFocus.isConnected && document.body.contains(heldFocus) === false) {
        const next = document.querySelector(".side-content.tree .tree-row.selected")
          || document.querySelector(".changes-list .change-file-row.selected")
          || document.querySelector(".editor-tabs .editor-tab.active");
        if (next && typeof next.focus === "function") next.focus({ preventScroll: true });
      }

      return;
    }

    activateTab(tab.dataset.tabId);
  }, true);

  // 中键关闭：与关闭叉同样只移除目标标签。
  document.addEventListener("auxclick", (event) => {
    if (event.button !== 1) return;
    const tab = event.target.closest && event.target.closest(".editor-tabs .editor-tab[data-tab-id]");
    if (!tab) return;
    event.preventDefault();
    closeTab(tab.dataset.tabId);
  }, true);

  document.addEventListener("keydown", (event) => {
    if (!event.ctrlKey || event.shiftKey || event.altKey) return;
    if (event.key.toLowerCase() !== "w") return;
    const live = window.__augitLive;
    if (!live || !live.activeTabId) return;
    event.preventDefault();
    closeActiveTab();
  }, true);
}

/**
 * 固定快捷键（产品规格 §3.3）。
 *
 * `Ctrl+P` 快速打开文件、`Ctrl+Shift+F` 全仓搜索、`Ctrl+F` 当前文件搜索、
 * `Ctrl+G` 跳转行、`Ctrl+W` 关闭当前标签、`F5` 刷新文件树、
 * `Esc` 关闭浮层或取消搜索。
 *
 * 全部挂在 document 的捕获阶段：区域刷新会换掉节点，挂在节点上的监听会随节点消失。
 */
function bindGlobalShortcuts() {
  if (window.__augitShortcutsBound) return;
  window.__augitShortcutsBound = true;
  document.addEventListener("keydown", (event) => {
    if (!window.__augitLive) return;
    // 组词期间不抢占按键（规格 §5.3）。
    if (event.isComposing || event.keyCode === 229) return;
    const key = event.key.toLowerCase();

    // F5 刷新文件树；不重载页面。
    if (event.key === "F5" && !event.ctrlKey && !event.altKey) {
      event.preventDefault();
      void refreshFileTree();
      return;
    }

    if (!event.ctrlKey || event.altKey) return;

    // Ctrl+Shift+F 全仓搜索（必须先于 Ctrl+F 判断）。
    if (event.shiftKey && key === "f") {
      event.preventDefault();
      openSearchOverlay("repository");
      return;
    }

    if (event.shiftKey) return;
    if (key === "p") {
      event.preventDefault();
      openSearchOverlay("quick");
      return;
    }

    if (key === "f") {
      event.preventDefault();
      openCurrentFileFind();
      return;
    }

    if (key === "g") {
      event.preventDefault();
      openGoToLineDialog();
      return;
    }
  }, true);
}

/**
 * 标题栏内嵌菜单的五个入口（规格 §5.1）。
 *
 * 规格把行为写得很具体：
 * - 「终端」直接切换底部终端工具窗口；「设置」直接打开设置模态窗口；
 *   两者执行前**先恢复普通标题栏**。
 * - 「文件 / 视图 / Git」打开贴近入口的动作菜单，菜单关闭后恢复原有标题栏入口、
 *   当前分支和当前文件上下文。
 *
 * 视觉稿只提供了这五个 `<a>` 入口的标记（`docs/ux-mockups/mockup.js` 的内嵌菜单模板），
 * 没有绑定行为；实时外壳此前让它们落到 `__augitUnwired*` 兜底，等于点了没有反应。
 */
const MAIN_MENU_ACTIONS = {
  终端: "terminal",
  设置: "settings",
  文件: "file",
  视图: "view",
  Git: "git",
};

/** 恢复标准标题栏（内嵌菜单收起）。 */
function closeMainMenu() {
  const host = document.querySelector(".titlebar");
  if (host && host.querySelector(".main-menu-bar")) host.outerHTML = titlebar();
}

async function runMainMenuAction(action) {
  const live = window.__augitLive;
  if (!live) return;
  if (action === "terminal") {
    // 直接切换底部终端工具窗口（与左侧入口同一套布局逻辑）。
    applyRailAction("terminal");
    return;
  }

  if (action === "settings") {
    openSettingsDialog();
    return;
  }

  // 文件 / 视图 / Git：打开贴近入口的动作菜单。
  openMainMenuPopover(action);
}

/** 文件 / 视图 / Git 的动作菜单：贴近入口显示，收纳各自的能力。 */
function openMainMenuPopover(action) {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  const titlebarNode = document.querySelector(".titlebar");
  if (!live || !host || !titlebarNode) return;
  rememberDialogFocus();
  closeLiveOverlay();
  const items = MAIN_MENU_POPOVERS[action] || [];
  const template = document.createElement("template");
  template.innerHTML = `<div class="overlay-layer live-overlay main-menu-popover"><div class="menu-list" role="menu">`
    + items.map((item) => `<a class="menu-item" href="#" data-main-menu-action="${escapeText(item.action)}">${escapeText(item.label)}</a>`).join("")
    + `</div></div>`;
  const layer = template.content.firstElementChild;
  if (!layer) return;
  layer.setAttribute("data-augit-overlay", "");
  const rect = titlebarNode.getBoundingClientRect();
  layer.querySelector(".menu-list").style.top = `${Math.round(rect.bottom + 4)}px`;
  host.appendChild(layer);
  const first = layer.querySelector(".menu-item");
  if (first) first.focus({ preventScroll: true });
}

/** 三个动作菜单的能力集合（来自规格 §7.x 已实现的动作）。 */
const MAIN_MENU_POPOVERS = {
  file: [
    { action: "open-workspace", label: "打开工作区…" },
    { action: "refresh-tree", label: "刷新文件树" },
  ],
  view: [
    { action: "toggle-side", label: "显示/隐藏项目工具窗口" },
    { action: "toggle-bottom", label: "显示/隐藏底部工具窗口" },
  ],
  git: [
    { action: "fetch", label: "获取" },
    { action: "push", label: "推送…" },
    { action: "branches", label: "分支与标签…" },
  ],
};

/**
 * 标题栏内嵌菜单的 Esc 关闭（规格 §5.1）。
 *
 * 视觉稿只在 `quick-open` 场景里处理了 Esc，实时外壳下按 Esc 不会恢复标题栏
 * （实测菜单一直留着）。这条要求"关闭后恢复原有标题栏"，
 * 因此这里补上：Esc 只在菜单确实打开时生效，并恢复标准标题栏。
 */
function bindTitlebarMenuEscape() {
  if (window.__augitTitlebarEscBound) return;
  window.__augitTitlebarEscBound = true;
  document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") return;
    if (event.isComposing || event.keyCode === 229) return;
    const bar = document.querySelector(".titlebar .main-menu-bar");
    if (!bar) return;
    event.preventDefault();
    const host = document.querySelector(".titlebar");
    if (host) host.outerHTML = titlebar();
  }, true);
}

/**
 * 打开搜索浮层（快速打开或全仓搜索）。
 * 与场景自带浮层不同，这里由输入触发，因此直接挂载节点。
 */
function openSearchOverlay(kind) {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  closeLiveOverlay();
  rememberDialogFocus();
  live.search = { kind, query: "", options: {}, matches: [], notice: "" };
  const template = document.createElement("template");
  template.innerHTML = liveSearchOverlay(kind);
  const layer = template.content.firstElementChild;
  if (!layer) return;
  layer.classList.add("live-overlay");
  host.appendChild(layer);
  bindSearchOverlay(kind);
  const field = layer.querySelector(".search-field");
  if (field) field.focus();
}

/** Ctrl+F：当前文件查找条。没有可查找的正文时如实说明。 */
function openCurrentFileFind() {
  const view = document.querySelector(".document-view:has(.code-view):not(.blame-document)");
  if (!view) {
    window.__augitError = "current-find:no-document";
    return;
  }

  if (typeof bindCurrentFind === "function") {
    bindCurrentFind();
  }

  const message = { bubbles: true, cancelable: true };
  view.dispatchEvent(new CustomEvent("document-content-changed", message));
  const field = document.querySelector(".current-find .search-field");
  if (field) field.focus();
}

/** Ctrl+G：跳转行（复用紧凑输入窗口）。 */
function openGoToLineDialog() {
  const host = document.querySelector(".augit-window");
  if (!host) return;
  closeLiveOverlay();
  rememberDialogFocus();
  compactDialogKind = "go-to-line";
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay go-to-line-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = compactInputDialog("跳转行", "行号", "", "跳转");
  host.appendChild(layer);
  const field = layer.querySelector("[data-compact-field]");
  if (field) {
    field.setAttribute("inputmode", "numeric");
    field.focus();
  }
}

/** F5：刷新文件树；保留展开状态，不重载页面。 */
async function refreshFileTree() {
  const live = window.__augitLive;
  if (!live) return;
  await loadStatus().catch(() => null);
  refreshAfterEvent("side", "statusbar");
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

  // 规格 §7.8：快速切换提交只接纳最新详情，旧详情不得恢复旧内容。
  const token = ++commitDetailsToken;
  try {
    const commit = await invoke("git/commit", { revision }, 30000);
    if (token !== commitDetailsToken) return;
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
    if (token === commitDetailsToken) {
      window.__augitError = "load-commit:" + String(error && error.message || error);
    }
  }
}

// 提交详情的递增令牌：快速切换提交时丢弃旧响应。
let commitDetailsToken = 0;

// 打开文档的递增令牌：快速连续打开时只接纳最后一次。
let documentToken = 0;

// Blame 与文件历史共用：两者都占据文档区，后发的胜出。
let detailViewToken = 0;

/** 读取限定到某个文件的提交历史。 */
async function loadFileHistory(path) {
  const token = ++detailViewToken;
  try {
    const history = await invoke("git/file-history", { path }, 60000);
    if (token !== detailViewToken) return null;
    if (!history || !history.available || !Array.isArray(history.commits)) return null;
    if (typeof history.path !== "string" || history.path.length === 0) return null;
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
    refresh("editorContent", "editorTabs", "statusbar", "titlebar");
  }

  if (requestedFileHistory && window.__augitLive) {
    await loadFileHistory(requestedFileHistory);
    refresh("side", "editorContent", "editorTabs", "statusbar", "titlebar", "bottomTool");
  }

  if (requestedDiff && window.__augitLive) {
    await loadDiff(requestedDiff);
    refresh("side", "editorContent", "editorTabs", "statusbar", "titlebar");
  }

  if (requestedConflict && window.__augitLive) {
    await loadConflicts();
    await loadConflict(requestedConflict);
    refresh("editorContent", "editorTabs", "statusbar", "overlay");
  }

  // 界面此时已可交互：立即标记就绪，不能等 Git 状态（实测约 15 秒）。
  window.__augitReady = true;

  const status = await statusPromiseRef;
  if (status) {
    // Git 状态比首屏慢，到达后补一次刷新；快照相等时不做任何更新（§6.2）。
    if (applySnapshot(status, latestHistory)) {
      refresh("side", "editorContent", "statusbar", "bottomTool", "overlay", "titlebar");
      void refreshCommitDetails();
    }

    window.__augitGitReady = true;
  }

  const referencesPromise = hasHost() ? loadReferences() : Promise.resolve(null);
  const history = await historyPromiseRef;
  if (history) {
    document.addEventListener("history-commit-selected", () => {
      const selected = document.querySelector('.commit-row[aria-selected="true"]');
      const revision = selected && selected.dataset.fullHash ? selected.dataset.fullHash : null;
      if (!revision) return;
      // 提交详情是异步填充的，变化文件行要等它写进去之后才能跟随（规格 §7.8），
      // 因此只在同一次加载完成后触发跟随，不重复发起查询。
      void loadCommitDetails(revision)
        .then(() => followHistoryComparison())
        .catch(() => {});
    });
    // 历史比状态慢，到达后由快照判定是否需要刷新（§6.2）。
    // 这里必须是**定点刷新**而不是整页重绘：历史常在启动后十余秒才到，
    // 此时用户可能已在查找框或搜索浮层里输入，整页重绘会打断输入（§6.1）。
    if (applySnapshot(latestStatus, latestHistory)) {
      refresh("bottomTool", "side", "statusbar", "overlay", "titlebar");
      void refreshCommitDetails();
    }

    void loadUnpushed();
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

  // 设置始终加载：面板尺寸恢复与拖动写回都需要它；该调用很轻（实测 0–11 毫秒），
  // 因此不做场景区分。
  await loadSettings();
  if (wantsSettings) {
    window.__augitRender();
    bindSettingsSave();
  }

  if (wantsTerminal) {
    // 终端是独占资源，只在终端场景启动，避免无谓的常驻进程。
    await startTerminal();
    window.__augitTerminalReady = true;
  }

  startWorkspaceChangePolling();
  bindPanelDividers();

  // 远端、分支、Stash 与 Worktree 供管理窗口使用；失败不影响主界面。
  await referencesPromise;
  // 三类数据都到齐后才能算出待推送信息；此后再补一次重绘。
  refreshPush();
  refresh("side", "editorContent", "statusbar", "bottomTool", "overlay", "titlebar");
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
  // 只刷新侧栏：展开/折叠不应影响编辑区、焦点与滚动位置。
  refresh("side");
}

// 单击只选择，双击或 Enter 才打开（规格 §12.5 的 main-project 不变量：
// 「单击只选择，双击或 Enter 才正式打开」）。
// 目录仍是单击展开/折叠。
function activateTreeRow(row, { open = true } = {}) {
  const path = row.dataset.treePath;
  if (path === undefined) return;
  if (row.dataset.treeDirectory === "true") {
    void toggleDirectory(row);
    return;
  }

  selectTreeRow(row);
  if (open) {
    void openDocument(path);
  }
}

/** 只更新树的选中态，不请求文件内容。 */
function selectTreeRow(row) {
  for (const other of document.querySelectorAll(".side-content.tree .tree-row.selected")) {
    other.classList.remove("selected");
    other.removeAttribute("aria-selected");
  }

  row.classList.add("selected");
  row.setAttribute("aria-selected", "true");
  // 已打开并处于跟随状态的比较标签随选择更新；未打开时单击不创建标签。
  // 注意树行的路径字段是 treePath（不是 path），取错字段会让跟随永不触发。
  const path = row.dataset.treePath;
  if (path && row.dataset.treeDirectory !== "true") void followChangeSelection(path);
}

// 点击改动文件时打开它的差异视图。与项目树用同一套委托思路：
// 捕获阶段 + closest，既不受整页重绘影响，也不依赖内联处理器。
document.addEventListener("click", (event) => {
  const row = event.target.closest && event.target.closest(".changes-list .change-file-row");
  if (!row) return;
  const path = row.dataset.path;
  if (!path) return;
  event.preventDefault();
  // 规格 §12.2：首次单击不创建 Diff；双击或 Enter 才打开。
  if (event.detail < 2) {
    selectChangeRow(row);
    return;
  }

  selectChangeRow(row);
  void openChangeDiff(path);
}, true);

// 改动文件行上的 Enter 打开差异。
document.addEventListener("keydown", (event) => {
  if (event.key !== "Enter") return;
  const row = event.target.closest && event.target.closest(".changes-list .change-file-row");
  if (!row || !row.dataset.path) return;
  event.preventDefault();
  selectChangeRow(row);
  void openChangeDiff(row.dataset.path);
}, true);

// 用捕获阶段的委托监听：不受内容安全策略对内联处理器的限制，也不受整页重绘影响。
document.addEventListener("click", (event) => {
  const row = event.target.closest && event.target.closest(".side-content.tree .tree-row");
  if (!row) return;
  event.preventDefault();
  // 双击打开，单击只选择。
  activateTreeRow(row, { open: event.detail >= 2 });
}, true);

// 历史提交里的变化文件：双击或 Enter 打开历史比较，单击只选择并跟随（规格 §7.8）。
// 与 mockup 里既有的历史比较实现保持同一套交互契约。
document.addEventListener("click", (event) => {
  const row = event.target.closest && event.target.closest("[data-live-changed-files] [data-history-path]");
  if (!row || !window.__augitLive) return;
  event.preventDefault();
  for (const other of historyFileRows()) other.classList.remove("selected");
  row.classList.add("selected");
  if (event.detail >= 2) {
    void openHistoryComparison(row);
    return;
  }

  followHistoryComparison();
}, true);

// 历史变化文件行上的 Enter 打开比较。
document.addEventListener("keydown", (event) => {
  if (event.key !== "Enter") return;
  const row = event.target.closest && event.target.closest("[data-live-changed-files] [data-history-path]");
  if (!row) return;
  event.preventDefault();
  void openHistoryComparison(row);
}, true);

// 变化文件行右键打开上下文菜单；打开菜单不打开比较（规格 §7.8）。
document.addEventListener("contextmenu", (event) => {
  const row = event.target.closest && event.target.closest(".changes-list .change-file-row");
  if (!row || !window.__augitLive) return;
  event.preventDefault();
  openChangesContextMenu(row, event.clientX, event.clientY);
}, true);

// 树的键盘导航（规格 §5.4）：方向键移动选择，右键展开、左键折叠。
document.addEventListener("keydown", (event) => {
  const row = event.target.closest && event.target.closest(".side-content.tree .tree-row");
  if (!row) return;
  const rows = [...document.querySelectorAll(".side-content.tree .tree-row")];
  const index = rows.indexOf(row);
  if (index < 0) return;
  const isDirectory = row.dataset.treeDirectory === "true";
  const expanded = row.getAttribute("aria-expanded") === "true";
  let next = null;
  if (event.key === "ArrowDown") next = rows[index + 1] || null;
  else if (event.key === "ArrowUp") next = rows[index - 1] || null;
  else if ((event.key === "ArrowRight" && isDirectory && !expanded)
    || (event.key === "ArrowLeft" && isDirectory && expanded)) {
    event.preventDefault();
    // 展开/折叠会重绘侧栏并换掉行节点，因此按路径把焦点移回同一行，
    // 否则键盘导航在第一次展开后就断掉了。
    const keepPath = row.dataset.treePath;
    void Promise.resolve(activateTreeRow(row, { open: false })).then(() => {
      const again = document.querySelector(`.side-content.tree .tree-row[data-tree-path="${CSS.escape(keepPath)}"]`);
      if (again) {
        selectTreeRow(again);
        again.focus({ preventScroll: true });
      }
    });
    return;
  } else if (event.key === "Home") next = rows[0] || null;
  else if (event.key === "End") next = rows.at(-1) || null;
  else return;

  event.preventDefault();
  if (!next) return;
  selectTreeRow(next);
  next.focus({ preventScroll: true });
  next.scrollIntoView({ block: "nearest" });
}, true);

// 树行上的 Enter 执行默认动作（打开文件）。
document.addEventListener("keydown", (event) => {
  if (event.key !== "Enter") return;
  const row = event.target.closest && event.target.closest(".side-content.tree .tree-row");
  if (!row || row.dataset.treeDirectory === "true") return;
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
async function openDocument(path, options = {}) {
  const { preview = false, activate = true } = options;
  const live = window.__augitLive;
  if (!live) return;
  live.tabs ??= [];
  // 已经是激活标签且不要求转为正式标签：无需重复读取。
  const current = live.tabs.find((tab) => tab.kind === "document" && tab.path === path);
  if (current && activate && live.activeTabId === current.id && !preview) return;
  const started = performance.now();
  // 快速连续打开时只接纳最后一次选择：晚到的旧响应不得覆盖新文档。
  const token = ++documentToken;
  try {
    const payload = await fetchDocument(path);
    // 令牌检查必须在写入任何状态之前：快速连续打开时，
    // 晚到的旧响应不得建立或激活标签，否则会覆盖用户最后一次选择。
    if (token !== documentToken) return;
    // 宿主契约：成功的读取必须带 path。缺 path 的载荷无法构成有效文档，
    // 直接按失败处理，避免把「字段缺失的文档对象」留在 live 上——
    // 那会让后续每一次渲染都依赖调用方的容错。
    if (!payload || typeof payload.path !== "string" || payload.path.length === 0) {
      throw new Error("document/read 返回的载荷缺少 path。");
    }

    // 打开为标签；openDocumentTab 同步写入 live.document / live.editor。
    live.tabs ??= [];
    openDocumentTab(path, payload, { preview, activate });
    window.__augitMarks = Object.assign(window.__augitMarks || {}, {
      open: Math.round(performance.now() - started),
    });
  } catch (error) {
    // 外部变化后文件可能已不存在：此时移除它的标签，而不是留下一个打不开的标签
    // （规格 §5.2：外部文件变化保持标签顺序与当前标签，「除非文件已不存在」）。
    const status = live.status;
    const deleted = status && Array.isArray(status.files)
      && status.files.some((file) => file.path === path && file.kind === "Deleted");
    if (deleted) {
      for (const tab of (live.tabs || []).filter((item) => item.kind === "document" && item.path === path)) {
        closeTab(tab.id);
      }

      return;
    }

    // 其它读取失败：不留下半截文档，清空并记录原因，界面回退到「无文档」状态。
    window.__augitError = "open-document:" + String(error && error.message || error);
    live.document = null;
    refresh("editorContent", "editorTabs", "statusbar", "titlebar");
    return;
  }

  // 打开文档只影响编辑区、标签、状态栏与侧栏选中态；
  // 只做区域刷新以保留项目树的展开状态与滚动位置；
  // 并延后到事件派发结束，避免在捕获阶段就替换掉被点击的行。
  refreshAfterEvent("side", "editorContent", "editorTabs", "statusbar", "titlebar");
}

try {
  await boot();
} catch (error) {
  window.__augitError = "boot-failed:" + String(error && error.stack || error);
  window.__augitHistoryReady = true;
  window.__augitGitReady = true;
}
