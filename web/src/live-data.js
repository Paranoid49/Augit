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
      refreshPush();
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
function diffRequestKey({ path, revision, mode, ignoreWhitespace, detectRenames, version }) {
  const live = window.__augitLive;
  return [
    live ? live.root : "",
    path,
    revision || "工作区",
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
        if (live.diff) {
          live.editor = "diff";
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

window.__augitLoadDiffMode = switchDiffMode;
window.__augitCloseDiff = closeDiff;
window.__augitDiffPatchCount = () => diffPatches.size;

// 加载反馈阈值（规格 §6.5）：预计低于 150 毫秒的操作不显示加载动画，避免闪烁。
const LoadingFeedbackDelay = 150;
let diffLoadingMark = null;

/**
 * 在差异加载超过阈值后才显示加载提示。
 * 提示只挂在**差异正文区**，不隐藏编辑工作区，也不影响工具窗口与标签
 * （规格 §6.1 / §6.5）。
 */
function scheduleDiffLoadingMarker() {
  clearDiffLoadingMarker();
  diffLoadingMark = window.setTimeout(() => {
    diffLoadingMark = null;
    const host = document.querySelector(".diff-layout .diff-columns, .editor-content");
    if (!host || host.querySelector(".diff-loading-status")) return;
    const marker = document.createElement("div");
    marker.className = "diff-loading-status";
    marker.setAttribute("role", "status");
    marker.innerHTML = '<span class="loading-mark"></span><span>正在生成 diff…</span>';
    host.prepend(marker);
  }, LoadingFeedbackDelay);
}

function clearDiffLoadingMarker() {
  if (diffLoadingMark !== null) {
    window.clearTimeout(diffLoadingMark);
    diffLoadingMark = null;
  }

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
  // 注意树行的路径字段是 treePath（不是 path），取错字段会让跟随永不触发。
  const path = row.dataset.treePath;
  if (path && row.dataset.treeDirectory !== "true") void followChangeSelection(path);
}

/** 找到工作区比较标签（规格 §5.2：最多只有一个）。 */
function findComparisonTab() {
  const live = window.__augitLive;
  return live && live.tabs ? live.tabs.find((tab) => tab.kind === "comparison") || null : null;
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
    if (!findComparisonTab()) {
      live.tabs ??= [];
      const tab = {
        id: nextTabId(),
        kind: "comparison",
        path,
        title: `提交: ${diff.name || path}`,
        editor: "diff",
        preview: false,
      };
      live.tabs.push(tab);
      if (activate) {
        live.activeTabId = tab.id;
        live.editor = "diff";
      }
    } else if (activate) {
      const tab = findComparisonTab();
      live.activeTabId = tab.id;
      live.editor = "diff";
    }

    // 规格 §12.2 要求已有 Diff 标签时「只更新该标签正文」，不得刷新改动列表、
    // 复选框、提交信息与列表滚动；同时延后到事件派发结束再替换编辑区，
    // 使视觉稿挂在冒泡阶段的「双击建比较标签」监听能收到事件。
    refreshAfterEvent("editorContent", "editorTabs", "statusbar");
  } finally {
    clearDiffLoadingMarker();
  }
}

/** Changes 列表选择变化时的跟随入口：解除跟随后不再自动更新比较标签。 */
async function followChangeSelection(path) {
  const live = window.__augitLive;
  if (!live || !live.followChanges) return;
  const tab = findComparisonTab();
  if (!tab) return;
  // 普通文档在前台时只后台更新正文，不抢占焦点。
  const background = live.activeTabId !== tab.id;
  const diff = await loadDiff(path).catch(() => null);
  if (!diff) return;
  if (background) {
    refreshAfterEvent("editorContent", "editorTabs", "statusbar");
  }
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
window.__augitLoadDiff = (path) => loadDiff(path);

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
  if (closing && closing.kind === "comparison") {
    live.followChanges = false;
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
const RAIL_SIDE = ["project", "commit", "search"];
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
    // 再次点击同一入口：折叠 / 恢复该区域。
    layout.collapsed = layout.collapsed === (inSide ? "side" : "bottom") ? null : (inSide ? "side" : "bottom");
  } else {
    layout.activeRail = name;
    layout.collapsed = null;
    if (inSide) {
      layout.side = name;
    } else {
      layout.bottom = RAIL_BOTTOM_VALUE[name] || "";
    }
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
    const link = event.target.closest && event.target.closest('a[href$=".html"]');
    if (!link) return;
    event.preventDefault();
    // 已接线的入口在这里做应用内动作。
    if (link.classList.contains("branch-chip")) {
      openBranchesPopover();
      return;
    }

    // 其余尚未接线：留在应用内，记录以便定位。
    window.__augitUnwiredAction = link.getAttribute("href");
    window.__augitUnwiredLabel = (link.getAttribute("aria-label") || link.innerText || "").trim().slice(0, 40);
  }, true);
}

/**
 * 打开分支与标签弹层（规格 §5.3 的非模态弹层）。
 * 复用视觉稿的 liveBranchesPopover，避免另写一套标记。
 */
function openBranchesPopover() {
  const live = window.__augitLive;
  if (!live) return;
  if (!live.references) {
    // 引用数据尚未到达：提示而不是静默无反应。
    window.__augitError = "branches:references-not-ready";
    return;
  }

  // 直接挂节点：区域替换只在「目标与替换两侧都存在」时生效，
  // 而多数场景本来就没有 .overlay-layer 目标节点，靠区域刷新挂不出来。
  const host = document.querySelector(".augit-window");
  if (!host) return;
  document.querySelectorAll("[data-augit-overlay].live-overlay").forEach((node) => node.remove());
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = `<div class="scrim"></div>${liveBranchesPopover()}`;
  host.appendChild(layer);
  // 点击遮罩关闭（与视觉稿的弹层行为一致）。
  layer.querySelector(".scrim").addEventListener("click", () => closeLiveOverlay());
}

/** 关闭实时弹层。 */
function closeLiveOverlay() {
  const layers = document.querySelectorAll("[data-augit-overlay].live-overlay");
  if (layers.length === 0) return false;
  layers.forEach((node) => node.remove());
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

    // 实时外壳打开的弹层（例如分支弹层）优先关闭。
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
    // 历史更慢，到达后再补一次重绘，底部 Git 日志与历史工具窗随之更新。
    window.__augitRender();
    document.addEventListener("history-commit-selected", () => {
      const selected = document.querySelector('.commit-row[aria-selected="true"]');
      const revision = selected && selected.dataset.fullHash ? selected.dataset.fullHash : null;
      if (revision) void loadCommitDetails(revision);
    });
    // 历史比状态慢，到达后由快照判定是否需要刷新（§6.2）。
    if (applySnapshot(latestStatus, latestHistory)) {
      refresh("side", "editorContent", "statusbar", "bottomTool", "overlay", "titlebar");
      void refreshCommitDetails();
    }

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
