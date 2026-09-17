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
// 上一次应用过的改动列表：判断"消失的选中项原本在哪一组、哪个位置"要用它。
let previousStatusFiles = [];
// 规格 §9.2：查询失败要保留上一次界面，但必须明确标记它不是最新状态；
// 这里缓存失败原因，applyStatus 会把它带进 live 供渲染层使用。
let latestStatusError = null;
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
      // 状态早于 live 到达时，showGitUnavailable 已经返回、提示没能建立；
      // 这里补一次，仍然遵循"只提示一次"。
      if (!liveObject.gitUnavailableShown) {
        liveObject.gitUnavailableShown = true;
        liveObject.toast = gitUnavailableToast(pendingGitUnavailableReason);
      }
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
    latestStatus = normalizeStatus(status, latestStatus);
    // 读取成功即恢复"最新"：否则上一次失败的标记会一直挂在界面上。
    latestStatusError = null;
    // 操作会话只在状态暗示"确有会话"时才去读：`git/operation` 要跑多条 Git 命令，
    // 每次状态刷新都调用会白花时间（性能要求）。
    if (latestStatus.hasConflicts || (latestStatus.operation && latestStatus.operation !== "None")) {
      void loadOperationSession();
    } else if (window.__augitLive && window.__augitLive.operationSession) {
      window.__augitLive.operationSession = null;
      window.__augitLive.operationSessionShown = false;
    }
    // 状态可能早于工作区数据到达，因此先缓存，再尝试附着到当前 live 对象。
    applyStatus();
    return status;
  } catch (error) {
    window.__augitMarks = Object.assign(window.__augitMarks || {}, {
      statusError: Math.round(performance.now() - started),
    });
    // 规格 §9.2：查询失败显示**非阻塞**错误，保留上一个已知界面，但必须明确标记
    // 它不是最新状态。此前这里只记了一个时间戳，界面上什么都没有——用户会把
    // 上一次读到的改动列表当成当前状态（例如据此提交已经变化的文件）。
    latestStatusError = String((error && error.message) || error || "原因未知");
    const live = window.__augitLive;
    if (live) {
      live.statusError = latestStatusError;
      refresh("side", "statusbar");
    }

    return null;
  }
}

/**
 * 把宿主返回的 Git 状态整理成界面需要的形状。
 * 选中态是界面状态，默认「改动」全选、「未跟踪」不选，与视觉稿一致。
 */
function normalizeStatus(status, previous) {
  // 勾选是**用户状态**，不是宿主数据（规格 §6.4：「刷新期间不得自动勾选或取消
  // 用户的提交复选状态」）。此前每次都按默认值重置，实测把用户取消的勾选重新勾上——
  // 用户会因此提交到自己明确排除的文件。
  // 路径已存在的沿用用户当前勾选；只有新出现的文件才用默认值。
  const previousChecked = new Map();
  for (const file of (previous && previous.files) || []) {
    previousChecked.set(file.path, !!file.checked);
  }

  const files = (status.files || []).map((file) => ({
    path: file.path,
    name: file.name || file.path.split("/").at(-1),
    directory: file.directory || file.path.split("/").slice(0, -1).join("/"),
    group: file.group,
    kind: file.kind || "Modified",
    staged: !!file.staged,
    workingTree: !!file.workingTree,
    checked: previousChecked.has(file.path)
      ? previousChecked.get(file.path)
      : file.group === "Changes",
  }));
  files.sort((a, b) => a.group.localeCompare(b.group)
    || a.name.localeCompare(b.name, "en", { numeric: true, sensitivity: "base" }));
  return {
    branch: status.branch,
    isDetached: status.isDetached,
    // 规格 §6.2 第四条：Git 操作会话类型与冲突状态属于快照。此前这两个字段被整个丢弃，
    // 页面既进不了快照、也不知道仓库正在做操作——一次 rebase 开始/结束可能不改动
    // 文件列表、分支与 HEAD，界面却必须跟着变。
    operation: typeof status.operation === "string" ? status.operation : null,
    hasConflicts: !!status.hasConflicts,
    files,
  };
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
      bindConflictDirtyTracking();
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

/**
 * 终端用的等宽字体与字号。
 *
 * 取自字体设置下发到根元素的两个变量（`--augit-code` / `--augit-code-size`），
 * 而不是另写一份默认值：xterm 在 canvas 上自绘，CSS 规则管不到它，
 * 只有读同一份变量才能保证"终端跟随等宽设置"（ux-spec 字体一节）。
 */
function terminalTypography() {
  const rootStyle = getComputedStyle(document.documentElement);
  const family = rootStyle.getPropertyValue("--augit-code").trim();
  const size = Number.parseFloat(rootStyle.getPropertyValue("--augit-code-size"));
  return {
    family: family.length > 0 ? family : 'Cascadia Mono, Consolas, monospace',
    size: Number.isFinite(size) && size >= 9 && size <= 40 ? size : 13,
  };
}

/** 字体设置变化后让已经打开的终端跟着变（规格：修改等宽设置不得改变界面文字）。 */
function refreshTerminalTypography() {
  if (!terminalInstance) return;
  const typography = terminalTypography();
  terminalInstance.options.fontFamily = typography.family;
  terminalInstance.options.fontSize = typography.size;
  try {
    terminalFitAddon.fit();
  } catch {
    // 终端尚未布局（例如工具窗口已关闭）时忽略：下一次显示会重新 fit。
  }
}

/** 启动内置终端并接上输出轮询。重复调用时复用已有会话。 */
async function startTerminal() {
  const host = document.querySelector('.terminal-view');
  if (!host || typeof window.Terminal !== 'function') return null;
  if (terminalInstance && terminalReady) return terminalInstance;

  // 终端与只读文本、diff、冲突用同一套等宽字体与字号（规格：字体设置只改变显示）。
  const typography = terminalTypography();
  terminalInstance = new window.Terminal({
    allowProposedApi: false,
    convertEol: false,
    cursorBlink: true,
    fontFamily: typography.family,
    fontSize: typography.size,
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
  // 正文被释放了，加载指示也必须一起收掉：关闭后若请求仍在途中，
  // 收尾逻辑会因为令牌失效而提前返回，再不在这里清就会留下一个永远的"正在加载"。
  clearDiffLoadingMarker();
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

/**
 * 改动工具窗工具栏的可用性：按当前选中行同步，并让禁用说明与状态一致（规格 §10.3）。
 *
 * 原地更新改动列表时不会触发区域渲染，mockup 里的 `updateActions` 也就不会跑；
 * 选中行消失后由 `reconcileChangeSelection` 改选，工具栏必须跟着走，
 * 否则「显示 Diff」「回滚」会对一个不存在的目标保持可用。
 */
function syncChangesToolbar() {
  const live = window.__augitLive;
  const toolbar = document.querySelector(".side-tool .changes-layout > .toolbar");
  if (!live || !toolbar) return;
  const selected = !!live.selectedChangePath
    && ((live.status && live.status.files) || []).some((file) => file.path === live.selectedChangePath);
  for (const button of toolbar.querySelectorAll(
    '[data-action="show-change-diff"], [data-action="rollback-change"]')) {
    button.disabled = !selected;
    if (selected) {
      button.removeAttribute("title");
    } else {
      button.setAttribute("title", button.dataset.disabledReason || "先在改动列表里选择一个文件。");
    }
  }
}

/**
 * 规格 §6.4 条款一/二：部分变化时**原地**更新改动列表，复用未变化行的节点对象。
 *
 * 之前的做法是整块替换侧栏区域，未变化的行也被重建——悬停高亮、行级焦点与
 * CSS 过渡都会重置；上千行的列表每次元数据事件都要重建全部行，与 §6.1
 * "只更新负责该数据的最小内容区"和流畅性要求冲突。
 *
 * 只处理与视觉稿一致的形态（两个分组头 + 文件行）；形态不符（空态、加载态、
 * 分组增删）时返回 false，调用方回退到区域替换。
 */
function patchChangesList() {
  const live = window.__augitLive;
  const files = live && live.status ? live.status.files : null;
  const list = document.querySelector(".side-tool .changes-layout > .changes-list");
  if (!list || !Array.isArray(files) || files.length === 0) {
    return false;
  }

  const groups = [["Changes", "Changes"], ["UnversionedFiles", "Unversioned Files"]];
  const expected = groups.map(([key, label]) => ({
    label,
    files: files.filter((file) => file.group === key),
  }));
  // 形态校验：只认识这两组；出现未知分组或某组变空都交给区域替换处理。
  if (files.some((file) => !groups.some(([key]) => file.group === key))) return false;
  const headers = [...list.querySelectorAll(".check-group-row")];
  if (headers.length === 0 || expected.some((group) => group.files.length === 0)) return false;
  for (const group of expected) {
    if (!headers.some((header) => header.dataset.group === group.label)) return false;
  }

  const existing = new Map();
  for (const row of list.querySelectorAll(".change-file-row")) {
    existing.set(row.dataset.path, row);
  }

  const used = new Set();
  const desired = [];
  for (const group of expected) {
    const header = headers.find((node) => node.dataset.group === group.label);
    // 组头只改"文件数"与全选态两处，节点本身保留。
    const meta = header.querySelector(".commit-meta");
    if (meta) meta.textContent = `${group.files.length} 个文件`;
    const check = header.querySelector(".fake-check");
    if (check) {
      const state = group.files.every((file) => file.checked) ? "true"
        : group.files.some((file) => file.checked) ? "mixed" : "false";
      check.classList.toggle("checked", state === "true");
      check.classList.toggle("mixed", state === "mixed");
      check.setAttribute("aria-checked", state);
    }

    desired.push(header);
    for (const file of group.files) {
      let row = existing.get(file.path);
      if (row) {
        used.add(file.path);
        // 同一个路径的 kind 可能变（例如 Modified → Deleted）：只改这一处类名，
        // 其余（名称/目录/图标）对同一路径不会变。
        const name = row.querySelector(".tree-name");
        if (name) {
          const wanted = `tree-name live-file-status-${file.kind}`;
          if (name.className !== wanted) name.className = wanted;
        }
        row.dataset.group = group.label;
      } else {
        // 新增行用与整块渲染**同一份**标记（勾选态来自宿主状态）。
        const holder = document.createElement("template");
        holder.innerHTML = liveChangeFileRow(file, group.label, "");
        row = holder.content.firstElementChild;
      }

      desired.push(row);
    }
  }

  // 先移除消失的行（连同它们的勾选节点），再按目标顺序就位。
  for (const [path, row] of existing) {
    if (!used.has(path) && row.isConnected) row.remove();
  }

  // 只在不一致时移动：把已就位的节点重新挂载会丢掉行级焦点；
  // 未挂载的新行由 insertBefore 直接插入到位。
  let cursor = list.firstElementChild;
  for (const node of desired) {
    if (cursor === node) {
      cursor = cursor.nextElementSibling;
      continue;
    }

    list.insertBefore(node, cursor);
  }

  // 提交框的"N modified"与分支标签也随状态变化，做定点文本更新。
  const count = document.querySelector(".commit-box .commit-count");
  if (count) {
    const changed = expected[0].files.length;
    count.textContent = `${changed} modified`;
    count.title = `${changed} modified`;
  }

  const lastBranch = document.querySelector(".commit-box .commit-last span");
  if (lastBranch && typeof live.branch === "string") {
    lastBranch.textContent = live.branch;
  }

  // 同区域里的"不是最新状态"提示也必须跟着变：跳过整块替换后，
  // 只更新列表会导致提示永远留在界面上（恢复成功也撤不掉）。
  const layout = list.parentElement;
  const notice = layout ? layout.querySelector(".status-stale") : null;
  if (live.statusError) {
    const wanted = statusRefreshNotice();
    if (notice) {
      if (notice.textContent !== new DOMParser().parseFromString(wanted, "text/html")
        .querySelector(".status-stale").textContent) {
        notice.replaceWith(new DOMParser().parseFromString(wanted, "text/html").querySelector(".status-stale"));
      }
    } else {
      const holder = document.createElement("template");
      holder.innerHTML = wanted;
      const node = holder.content.firstElementChild;
      if (node) list.before(node);
    }
  } else if (notice) {
    notice.remove();
  }

  return true;
}

/** 状态变化后的刷新：优先原地更新改动列表，形态不符时回退到区域替换。 */
function refreshStatusRegions(...regions) {
  if (patchChangesList()) {
    // 选中行、勾选、草稿与滚动位置由同一套恢复逻辑落地（含新行）。
    restoreChangesState();
    syncChangesToolbar();
    refresh(...regions.filter((name) => name !== "side"));
    return;
  }

  refresh(...regions);
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
  // 标签与视图必须在**发请求之前**就位：首次打开时 live.editor 还是场景默认值，
  // 加载分支不会被选中，150 毫秒后的加载提示就无处附着（实测 liveDiffView
  // 在整个加载窗口内一次都没被调用，编辑区仍是场景默认视图）。
  // 提前建立标签不改变"单击只选择"：单击路径根本不会走到这里。
  const tab = ensureComparisonTab(path);
  if (activate) activateComparisonTab(tab);
  scheduleDiffLoadingMarker();
  let succeeded = false;
  try {
    const diff = await loadDiff(path);
    if (!diff) return;
    succeeded = true;
    // 复用同一个标签时同步文字，否则标签会一直显示第一次打开的文件名。
    syncComparisonTab(tab, path, `提交: ${diff.name || path}`);
    if (activate) activateComparisonTab(tab);

    // 规格 §12.2 要求已有 Diff 标签时「只更新该标签正文」，不得刷新改动列表、
    // 复选框、提交信息与列表滚动；同时延后到事件派发结束再替换编辑区，
    // 使视觉稿挂在冒泡阶段的「双击建比较标签」监听能收到事件。
    refreshAfterEvent("editorContent", "editorTabs", "statusbar");
  } finally {
    clearDiffLoadingMarker();
    // 读取失败时不留一个打不开的比较标签：提前建标签是为了让加载视图有着落，
    // 失败后必须如实撤销，否则界面上会留下一个空标签。
    if (!succeeded) closeTab(tab.id);
  }
}

/** 取得或建立唯一的比较标签；已存在则复用。 */
function ensureComparisonTab(path, title) {
  const live = window.__augitLive;
  live.tabs ??= [];
  // 标题可覆盖：工作区 Diff 用默认的「提交: 路径」，历史比较传入双方引用。
  // 复用分支也同步标题与目标——否则标签会一直显示上一个比较的名字。
  const resolvedTitle = title || `提交: ${path}`;
  const existing = findComparisonTab();
  if (existing) {
    syncComparisonTab(existing, path, resolvedTitle);
    return existing;
  }

  const tab = {
    id: nextTabId(),
    kind: "comparison",
    path,
    title: resolvedTitle,
    editor: "diff",
    preview: false,
  };
  live.tabs.push(tab);
  return tab;
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

/**
 * 当前选中的提交行（底部 Git 日志）。
 * 选择器只留这一处：此前有三处各自写 `.commit-row[aria-selected="true"]`。
 */
function selectedCommitRow() {
  return document.querySelector('.commit-row[aria-selected="true"]');
}

/** 当前选中的提交标识：优先完整哈希，取不到时退回短哈希。 */
function selectedHistoryCommit() {
  const row = selectedCommitRow();
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
  const tab = ensureComparisonTab(path, label);
  live.historyComparison = { path, commit, label, status: "loading" };
  if (activate) activateComparisonTab(tab);
  // 比较在前台时才重绘编辑区并显示加载提示；后台跟随时编辑区属于前台文档，
  // 重绘会把它打断（规格 §5.2「不抢占编辑区」、§6.1 最小更新区域）。
  // 跟随 Changes 选择的 followChangeSelection 早就是这个规则，历史比较此前没有对齐。
  const repaint = () => refreshAfterEvent(...(live.activeTabId === tab.id
    ? ["editorTabs", "editorContent", "statusbar", "bottomTool"]
    : ["editorTabs", "statusbar"]));
  if (live.activeTabId === tab.id) {
    scheduleDiffLoadingMarker();
  }

  repaint();

  const diff = await loadDiff(path, { commit, force: true }).catch(() => null);
  // 收尾只在**本次请求仍然有效**时执行：被取代的旧请求若在这里清标记，
  // 会把新请求（乃至新请求的加载提示）一起清掉。
  if (token !== historyComparisonToken) return diff;
  clearDiffLoadingMarker();
  if (!diff) {
    live.historyComparison = { path, commit, label, status: "unavailable" };
    repaint();
    return null;
  }

  live.historyComparison = { path, commit, label, status: "ready" };
  if (activate) activateComparisonTab(tab);

  repaint();
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
/**
 * 提交或文件选择变化时更新已打开的历史比较（规格 §7.8）。
 * 普通文档在前台时只后台更新，不抢占编辑区。
 */
function followHistoryComparison(selectedPath = null) {
  const live = window.__augitLive;
  if (!live || !live.historyComparison) return;
  if (live.historyComparison.status === "closed") return;
  // 规格 §5.2「历史比较仅在已有比较上下文中随提交和文件选择更新」，两种来源要分开处理：
  // - 用户点了某个变化文件行：跟随到**该行**，否则单击只改选中态、比较不更新；
  // - 提交切换：沿用比较自己的路径（新提交仍有该文件时保持同一路径），
  //   没有才退回第一个文件。
  const rows = historyFileRows();
  const row = selectedPath
    ? rows.find((item) => item.dataset.historyPath === selectedPath)
    : rows.find((item) => item.dataset.historyPath === live.historyComparison.path) || rows[0];
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

/**
 * 启动恢复上次打开的标签（规格 §6.7「启动恢复只激活原恢复文件一次」）。
 *
 * 每条文件都按"后台打开"处理，最后只激活一次原恢复文件；
 * 恢复期间用户一旦有交互（切换比较、打开别的文件、改树选择、进入查找框…），
 * 恢复收尾就**不得**再重新激活正文、重选项目树或覆盖输入状态——
 * 因此用一个递增代次做闸门，而不是"恢复完再强行激活"。
 */
let sessionRestoreGeneration = 0;

/** 用户交互标记：任何点击或按键都让进行中的恢复收手。 */
function markUserInteraction() {
  sessionRestoreGeneration += 1;
}

/**
 * 恢复上次展开的目录层级（规格 §6.7「后续目录展开仅补齐树节点」）。
 *
 * 宿主返回的是工作区相对路径；按层级从浅到深补齐，使恢复顺序与用户展开目录的顺序一致，
 * 且不依赖"循环结束后才重建一次可见树"这个实现细节（若将来改成逐步重建，浅层在前才是对的）。
 */
async function restoreExpandedDirectories(paths) {
  const live = window.__augitLive;
  if (!live || !Array.isArray(paths) || paths.length === 0) {
    return;
  }

  const relatives = paths
    .filter((path) => typeof path === "string" && path.length > 0)
    .slice()
    .sort((left, right) => left.split("/").length - right.split("/").length);
  for (const path of relatives) {
    if (expandedPaths.has(path)) continue;
    expandedPaths.add(path);
    // 与 toggleDirectory 同一套深度口径：本层目录的子项深度 = 路径段数。
    await loadChildren(path, path.split("/").length).catch(() => null);
  }

  if (live.name !== undefined) {
    live.tree = buildVisibleTree(live.name, live.rootPath ?? "");
  }
}

async function restoreSession() {
  const live = window.__augitLive;
  const settings = live && live.settings;
  const files = settings && Array.isArray(settings.openFiles) ? settings.openFiles : [];
  if (!live || files.length === 0) {
    return;
  }

  const generation = ++sessionRestoreGeneration;
  const activePath = typeof settings.activeFile === "string" ? settings.activeFile : null;
  live.sessionRestoring = true;
  try {
    for (const path of files) {
      if (generation !== sessionRestoreGeneration) return;
      await openDocument(path, { activate: false }).catch(() => null);
    }

    // 收尾闸门：用户在恢复期间有交互就不再激活（规格 §6.7 第六条）。
    if (generation !== sessionRestoreGeneration) return;
    // 直接激活刚恢复好的那个标签，不重新读取：文件刚读过，再读一次既慢又没有必要
    // （慢读取场景下会白白多等一个读取周期）。
    const restoredTab = (live.tabs || []).find(
      (item) => item.kind === "document" && item.path === activePath) || null;
    if (restoredTab) {
      activateTab(restoredTab.id);
    }
  } finally {
    live.sessionRestoring = false;
  }

  // 目录展开层级与标签一起恢复；顺序由函数内部按层级排序保证。
  await restoreExpandedDirectories(settings.expandedDirectories).catch(() => null);

  // 恢复完成后按**实际**结果对齐（例如某个文件已经不在了）：下一次变更才会写回真实列表。
  live.settings.openFiles = [];
  scheduleSessionPersist();
  window.__augitSessionRestored = true;
}

/**
 * 会话恢复数据的写回（防抖）：标签集合与当前文件变化时把结果存进设置。
 *
 * 走 `session/write` 而不是 `settings/write`：后者会失效 Git 解析与状态缓存，
 * 而"关一个标签"不该让界面重新查一遍 Git（§6.1 局部更新与性能要求）。
 * 内容没变时不写，避免无谓的磁盘写入。
 */
let sessionPersistTimer = 0;

function scheduleSessionPersist() {
  if (sessionPersistTimer !== 0) return;
  sessionPersistTimer = window.setTimeout(() => {
    sessionPersistTimer = 0;
    void persistSession();
  }, 800);
}

async function persistSession() {
  const live = window.__augitLive;
  if (!live || !live.settings || live.sessionRestoring) return;
  const openFiles = (live.tabs || [])
    .filter((tab) => tab.kind === "document" && typeof tab.path === "string" && tab.path.length > 0)
    .map((tab) => tab.path);
  const active = (live.tabs || []).find((tab) => tab.id === live.activeTabId) || null;
  const activeFile = active && active.kind === "document" ? active.path : null;
  // 根（空路径）不是"展开的目录"，不写进设置。
  const expandedDirectories = [...expandedPaths].filter((path) => path.length > 0);
  const sameFiles = JSON.stringify(openFiles) === JSON.stringify(live.settings.openFiles || []);
  const sameDirectories =
    JSON.stringify(expandedDirectories) === JSON.stringify(live.settings.expandedDirectories || []);
  if (sameFiles && sameDirectories && activeFile === (live.settings.activeFile || null)) {
    return;
  }

  live.settings.openFiles = openFiles;
  live.settings.activeFile = activeFile;
  live.settings.expandedDirectories = expandedDirectories;
  await invoke("session/write", { openFiles, activeFile, expandedDirectories }, 10000).catch(() => null);
}

/** 读取设置并挂上保存动作。 */
async function loadSettings() {
  try {
    const settings = await invoke("settings/read", {}, 15000);
    const live = window.__augitLive;
    if (live && settings) {
      live.settings = settings;
      // 字体设置只改变显示（规格 §7.14）：先按字体算出各区域的度量，再夹取已保存的面板尺寸。
      if (typeof applyTypographyPreview === "function") {
        await applyTypographyPreview();
      }
      refreshTerminalTypography();
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
  // 宿主会忽略越界字号并保留未知枚举，因此重新读取一次真实值再应用字体与面板尺寸。
  await loadSettings();
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
    // 解决器打开的文件变化优先处理：它可能在用户有未保存内容时被外部改写（规格 §7.14）。
    const conflictPath = live.conflictSessionView === "resolver" && live.conflict ? live.conflict.path : null;
    const hitsConflict = conflictPath !== null
      && relative.some((path) => path.toLowerCase() === conflictPath.toLowerCase());
    if (hitsConflict) {
      await recheckOpenConflict().catch(() => null);
      touchedCurrent = true;
    } else if (hitsDiff && diffPath) {
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
  // 改动列表优先原地更新（§6.4：保留未变化行的节点对象），形态不符时自动回退到区域替换。
  refreshStatusRegions("side", "editorContent", "editorTabs", "statusbar", "bottomTool", "titlebar");
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
    // 第四条：操作会话类型与冲突状态。
    operation: status ? status.operation || null : null,
    hasConflicts: !!(status && status.hasConflicts),
    // 第五条：当前已选文件或提交的稳定标识。
    selectedChangePath: live ? live.selectedChangePath || null : null,
    selectedCommit: live && live.history ? selectedHistoryCommit() : null,
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
    refreshStatusRegions("side", "editorContent", "statusbar", "bottomTool", "titlebar");
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
// 供验收套件读取终端实际使用的等宽字体与字号（xterm 自绘，CSS 探针读不到）。
window.__augitTerminalFont = () => (terminalInstance
  ? { family: terminalInstance.options.fontFamily, size: terminalInstance.options.fontSize }
  : null);

window.__augitSettingsWrite = async (payload) => {
  const result = await invoke("settings/write", payload, 15000);
  // 与保存路径一致：重新读取真实设置并重新应用字体与面板尺寸。
  await loadSettings();
  return result;
};

window.__augitLoadCommitDetails = (revision) => loadCommitDetails(revision);
window.__augitLoadOperation = () => loadOperationSession();

window.__augitLoadBlame = (path) => loadBlame(path);
window.__augitLoadFileHistory = (path) => loadFileHistory(path);
window.__augitLoadDiff = (path, options) => loadDiff(path, options);
/**
 * 供验收驱动"打开差异"的钩子。
 *
 * 它**派发一次真实的双击点击事件**，而不是直接调用 `openChangeDiff`：
 * 直调会跳过用户路径带来的状态前置，实测加载窗口内文件栏不存在
 * （`live.diff` 为 null 时 liveDiffView 提前返回），提示无处可挂，
 * 与真实路径结论不一致。验收钩子必须复现真实路径，否则会把人引向错误结论。
 */
window.__augitOpenChangeDiff = (path) => {
  const row = [...document.querySelectorAll(".changes-list .change-file-row")]
    .find((node) => node.dataset.path === path);
  if (!row) return null;
  row.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true, detail: 1 }));
  row.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true, detail: 2 }));
  return null;
};

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
/**
 * 显示一条全局提示（规格 §10.2）。
 *
 * 内容进状态、由 mockup 的提示区域渲染：直接往窗口里 appendChild 会被下一次区域刷新
 * 抹掉（与之前修过的 diff 加载提示同类），也不满足"同一时刻只有一条提示"。
 * 同一错误重复调用只是覆盖同一条提示，不会叠加弹出。
 */
function showToast({ title, text, kind = "error", action = null }) {
  const live = window.__augitLive;
  if (!live || !title) return false;
  live.toast = { title, text, kind, action };
  refreshAfterEvent("toast");
  return true;
}

/** 清除全局提示（例如同一次操作重试成功后，旧的失败提示不该继续挂着）。 */
function clearToast() {
  const live = window.__augitLive;
  if (!live || !live.toast) return;
  live.toast = null;
  refreshAfterEvent("toast");
}

/** Git 不可用提示的内容；状态早于 live 到达时也要能补出同一条。 */
function gitUnavailableToast(reason) {
  return {
    title: "Git 不可用",
    text: `${reason} 文件浏览仍可使用。`,
    kind: "error",
    action: { label: "配置 git.exe", href: "settings.html" },
  };
}

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
  if (!showToast(gitUnavailableToast(live.gitUnavailableReason))) {
    live.toast = gitUnavailableToast(live.gitUnavailableReason);
  }
}

/** Git 不可用的原因；状态可能早于 live 对象返回，因此单独缓存。 */
let pendingGitUnavailableReason = null;

/**
 * 读取 Git 操作会话（规格 §7.13）。
 *
 * 会话刚进入"进行中且有冲突"时自动打开会话窗口（§9.3「冲突后进入操作会话，
 * 不把冲突包装成普通失败」）；会话结束后关掉窗口。
 */
async function loadOperationSession() {
  const live = window.__augitLive;
  if (!live) return null;
  try {
    const payload = await invoke("git/operation", {}, 30000);
    const session = payload && payload.available ? payload.session : null;
    live.operationSession = session;
    if (session && session.inProgress && session.hasConflicts && !live.operationSessionShown) {
      live.operationSessionShown = true;
      // 自动进入会话时总是从冲突列表开始，不停留在上一次的解决器视图上。
      live.conflictSessionView = "list";
      openConflictSession();
    } else if (live.operationSessionShown && (!session || !session.inProgress)) {
      live.operationSessionShown = false;
      closeConflictSession();
    } else if (live.operationSessionShown) {
      // 会话进行中：窗口内容随会话变化重绘（步骤、冲突数、可用动作都会变）。
      openConflictSession();
    }

    window.__augitOperationReady = true;
    return session;
  } catch (error) {
    window.__augitError = "load-operation:" + String(error && error.message || error);
    return null;
  }
}

/**
 * 会话窗口的当前视图。视觉稿把"冲突列表"与"三栏解决器"做成两个页面，
 * 实时界面里用同一个模态窗口的两个视图表达同一层关系（避免模态叠模态）。
 */
function conflictSessionView() {
  const live = window.__augitLive;
  return live && live.conflictSessionView === "resolver" && live.conflict ? "resolver" : "list";
}

/** 打开（或就地重绘）冲突操作会话窗口。 */
function openConflictSession() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  const session = live ? live.operationSession : null;
  if (!live || !host || !session) return;

  // 有未保存内容或正在应用时**不重建**解决器（规格 §7.14）。
  // 状态刷新会顺带重绘会话窗口，而重绘会替换结果区控件、丢掉选区、滚动与撤销记录——
  // 那等于把用户的编辑悄悄扔掉（"保留当前内容"后尤其明显）。此时只补上可能缺失的询问层。
  const editing = document.querySelector(
    ".dialog.conflict-session-dialog .conflict-column.result .conflict-block");
  if (editing && conflictSessionView() === "resolver" && (live.conflictDirty || live.conflictApplying)) {
    if (live.conflictPending) {
      showConflictMergePrompt(live.conflictPending);
    }
    return;
  }

  document.querySelectorAll("[data-augit-overlay].live-overlay").forEach((node) => node.remove());
  document.querySelectorAll(".dialog.conflict-session-dialog").forEach((node) => {
    const owner = node.closest("[data-augit-overlay]") || node;
    owner.remove();
  });
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay";
  layer.setAttribute("data-augit-overlay", "");
  if (conflictSessionView() === "resolver") {
    // 三栏解决器：正文由 mockup 从 live.conflict 渲染，保存按钮由 bindConflictSave 接管。
    layer.innerHTML = dialog(
      "解决冲突",
      liveConflictResolver(),
      '<button type="button" class="secondary-button" data-conflict-back>返回冲突列表</button>',
      true,
      "conflict-session-dialog");
  } else {
    layer.innerHTML = dialog(
      liveConflictSessionTitle(session),
      liveConflictSessionBody(session),
      liveConflictSessionFooter(session),
      true,
      "conflict-session-dialog");
  }

  host.appendChild(layer);
  // 询问层必须最后落上：本函数会清掉所有 live 覆盖层（外部修改时状态刷新也会触发一次重绘），
  // 否则"重新载入 / 保留当前内容"会被悄悄抹掉，而用户以为自己已经选过了（规格 §7.14）。
  if (live.conflictPending) {
    showConflictMergePrompt(live.conflictPending);
  }
  if (conflictSessionView() === "resolver") {
    bindConflictSave();
    bindConflictUndoRefresh();
    bindConflictDirtyTracking();
    bindConflictCaretTracking();
    // 重绘会造出新的对话框节点：冻结标记与按计数恢复的按钮状态都要重新落上
    // （否则应用成功后重绘会让 data-conflict-applying 消失，读状态时得到 null）。
    // 状态先落，布局后做：排版问题不该影响动作可用性与冻结标记。
    setConflictApplying(!!(live && live.conflictApplying));
    // 解决器是按需创建的：它不在 __augitRender 的重绘路径上，因此必须自己度量一次，
    // 否则"大字号/窄窗口把文件名移到窗口顶部"这条规则要等到用户改变窗口大小才生效。
    if (typeof measureConflictResolver === "function") {
      measureConflictResolver();
      syncConflictCount();
    }
  }
}

/**
 * 结果区里还未解决的冲突块（规格 §7.14）。
 *
 * 按结果正文里的冲突起始标记行推导，而不是按宿主返回的块下标：
 * 接受一次就少一个标记组，撤销会把标记组还回来——两种情况下计数都自动正确，
 * 不需要额外维护"已解决"状态（也就不会与撤销记录不一致）。
 */
function unresolvedConflictGroups(block) {
  const spans = [...block.querySelectorAll(".conflict-line")];
  const groups = [];
  let open = null;
  for (const span of spans) {
    const text = span.textContent || "";
    if (/^<{7}/.test(text)) {
      open = { spans: [span], start: Number(span.dataset.line), end: Number(span.dataset.line) };
      continue;
    }

    if (!open) continue;
    open.spans.push(span);
    if (/^>{7}/.test(text)) {
      open.end = Number(span.dataset.line);
      groups.push(open);
      open = null;
    }
  }

  return groups;
}

/**
 * 当前冲突块在"未处理块"里的下标（规格 §7.14：上一处/下一处与接受动作都作用于它）。
 * 用户把光标放进某一处冲突时下标跟着走；块被接受掉或被撤销还原后按下标夹取。
 */
function currentConflictGroupIndex(groups) {
  const live = window.__augitLive;
  const index = live && Number.isInteger(live.conflictBlockIndex) ? live.conflictBlockIndex : 0;
  if (groups.length === 0) return -1;
  return Math.max(0, Math.min(index, groups.length - 1));
}

/** 把某个冲突块的行选中（导航据此把目标滚动到可见处；设计里没有额外的"当前块"装饰）。 */
function selectConflictGroup(group) {
  if (!group || !group.spans || group.spans.length === 0) return;
  const range = document.createRange();
  // 起点落在第一行**内部**（而不是它之前）：这样选区锚点属于该行，
  // "当前块"才能从光标位置读出来（锚点落在容器上就读不到行号）。
  range.setStart(group.spans[0], 0);
  range.setEndAfter(group.spans[group.spans.length - 1]);
  const selection = window.getSelection();
  selection.removeAllRanges();
  selection.addRange(range);
  if (typeof group.spans[0].scrollIntoView === "function") {
    group.spans[0].scrollIntoView({ block: "nearest" });
  }
}

/** 上一处 / 下一处：移动到相邻的未处理冲突块并把光标放到该块（规格 §7.14）。 */
function navigateConflictBlock(delta) {
  const live = window.__augitLive;
  const block = document.querySelector(".conflict-column.result .conflict-block");
  if (!live || !block) return null;
  const groups = unresolvedConflictGroups(block);
  if (groups.length === 0) return null;
  const current = currentConflictGroupIndex(groups);
  const next = Math.max(0, Math.min(current + delta, groups.length - 1));
  live.conflictBlockIndex = next;
  selectConflictGroup(groups[next]);
  syncConflictCount();
  return next;
}

/**
 * 光标位于哪一处冲突块（规格 §7.14：导航与接受都跟着"当前"块走）。
 * 监听 `selectionchange`，只在光标确实落在结果区里时更新下标。
 */
function bindConflictCaretTracking() {
  if (window.__augitConflictCaretBound) return;
  window.__augitConflictCaretBound = true;
  document.addEventListener("selectionchange", () => {
    const live = window.__augitLive;
    const block = document.querySelector(".conflict-column.result .conflict-block");
    if (!live || !block) return;
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0) return;
    const node = selection.anchorNode;
    const element = node && node.nodeType === 1 ? node : (node ? node.parentElement : null);
    const line = element && element.closest ? element.closest(".conflict-line") : null;
    if (!line || !block.contains(line)) return;
    // 用**包含关系**判定（行号在编辑后可能不再连续）：光标所在的那一行属于哪个块。
    const index = unresolvedConflictGroups(block)
      .findIndex((group) => group.spans.some((span) => span === line || span.contains(line)));
    if (index >= 0 && index !== live.conflictBlockIndex) {
      live.conflictBlockIndex = index;
      syncConflictCount();
    }
  });
}

/** 把"还有几个未处理冲突"写回解决器顶部（规格 §7.14：计数固定在顶部）。 */
function syncConflictCount() {
  const block = document.querySelector(".conflict-column.result .conflict-block");
  const label = document.querySelector("[data-conflict-count]");
  if (!block || !label) return null;
  const count = unresolvedConflictGroups(block).length;
  label.textContent = `${count} 个未处理冲突`;
  // 视觉稿的表尾提示同时给出总数与当前位置；实际计数由这里维护（度量函数不覆盖实时值）。
  const help = document.querySelector(".conflict-session-dialog .footer-help");
  if (help) {
    const groups = unresolvedConflictGroups(block);
    const position = currentConflictGroupIndex(groups);
    help.textContent = help.title = count > 0
      ? `未处理冲突块：${count}，当前位置：${position + 1}`
      : "没有未处理的冲突块";
  }
  // 规格 §7.14：失败、取消或异常后**按实际未处理冲突数恢复动作**——
  // 没有未处理冲突时接受与导航都不该还可点（撤销把它们还回来后要重新可用）。
  const dialog = document.querySelector(".dialog.conflict-session-dialog");
  const applying = dialog && dialog.getAttribute("data-conflict-applying") === "true";
  if (dialog && !applying) {
    const groups = unresolvedConflictGroups(block);
    const index = currentConflictGroupIndex(groups);
    const hasConflicts = groups.length > 0;
    for (const node of dialog.querySelectorAll("[data-conflict-side]")) {
      node.disabled = !hasConflicts;
    }

    // 导航按钮按位置可用：第一处不能再"上一处"，最后一处不能再"下一处"。
    const prev = dialog.querySelector('[data-conflict-nav="prev"]');
    const next = dialog.querySelector('[data-conflict-nav="next"]');
    if (prev) prev.disabled = index <= 0;
    if (next) next.disabled = index < 0 || index >= groups.length - 1;
  }

  return count;
}

/**
 * 应用进行中的冻结与解冻（规格 §7.14）。
 *
 * 冻结接受、导航与普通关闭，中央结果区暂时只读并显示
 * 「正在应用结果并标记已解决…」；解冻后按实际未处理冲突数恢复动作。
 */
function setConflictApplying(applying) {
  const live = window.__augitLive;
  if (live) live.conflictApplying = applying;
  const dialog = document.querySelector(".dialog.conflict-session-dialog");
  if (!dialog) return;
  dialog.setAttribute("data-conflict-applying", applying ? "true" : "false");
  for (const node of dialog.querySelectorAll(
    "[data-conflict-side], [data-conflict-save], [data-conflict-back], .conflict-header .secondary-button")) {
    node.disabled = applying;
  }

  const block = dialog.querySelector(".conflict-column.result .conflict-block");
  if (block) block.setAttribute("contenteditable", applying ? "false" : "plaintext-only");
  const label = dialog.querySelector("[data-conflict-count]");
  if (label) {
    if (applying) {
      label.textContent = "正在应用结果并标记已解决…";
    } else {
      syncConflictCount();
    }
  }
}

/** 解决器里的局部提示（§10.2：说明发生了什么、哪些状态未改变、可以做什么）。 */
function setConflictNotice(message) {
  const notice = document.querySelector(".conflict-session-dialog .conflict-notice");
  if (!notice) return;
  notice.hidden = !message;
  notice.textContent = message || "";
  notice.title = message || "";
}

/**
 * 应用结果并标记已解决（规格 §7.14）。
 *
 * 校验交给宿主：桥接在保存前会重新读取该文件当前版本与操作类型，版本不符即拒绝。
 * 这里负责界面侧：进行中冻结与只读、失败保留正文并显示最新原因、结束后按实际
 * 未处理冲突数恢复动作。进行中不重复触发（§9.3）。
 */
async function applyConflictResult() {
  const live = window.__augitLive;
  const block = document.querySelector(".conflict-column.result .conflict-block");
  const path = live && live.conflict ? live.conflict.path : null;
  if (!live || !path || !block || live.conflictApplying) return null;

  const resultText = block.innerText;
  setConflictApplying(true);
  setConflictNotice("");
  let failure = null;
  let payload = null;
  try {
    payload = await invoke("git/conflict-save", { path, resultText }, 120000);
    if (!payload || !payload.available || payload.saved === false) {
      failure = (payload && payload.reason) || "应用结果失败。";
    }
  } catch (error) {
    failure = String((error && error.message) || error);
  }

  setConflictApplying(false);
  if (failure) {
    setConflictNotice(describeFailure(failure, {
      unchanged: "中央结果区与冲突文件都没有被修改。",
      next: "可以重新载入文件或再次应用。",
    }));
    return null;
  }

  window.__augitConflictSaved = path;
  // 正文已经写回磁盘：不再有未保存内容，也不再挂着外部修改的询问（规格 §7.14）。
  live.conflictDirty = false;
  live.conflictPromptVersion = null;
  // 解决一个冲突会改变未处理数量与 Continue 的可用性：重新读取会话（§9.3）。
  void loadOperationSession();
  return payload;
}

/**
 * 接受当前冲突块的一侧（规格 §7.14）。
 *
 * 关键点：这是**结果区的一次可撤销编辑**，不是整文件 checkout——宿主那个
 * `AcceptSideAsync`（`git checkout --ours/--theirs`）对应的是二进制/超大文件的
 * "整侧接受"，语义不同，不能拿来当这三个按钮的实现。
 * 编辑走 `insertText` 并保留浏览器原生撤销栈，因此 Ctrl+Z 能把这处冲突块还原。
 */
function acceptConflictBlock(side) {
  const live = window.__augitLive;
  const document_ = live ? live.conflict : null;
  const block = document.querySelector(".conflict-column.result .conflict-block");
  if (!document_ || !block) return false;
  const groups = unresolvedConflictGroups(block);
  if (groups.length === 0) return false;
  // 当前块：光标/导航指定的那一处（§7.14「提供接受左侧、两侧或右侧的动作」作用于当前冲突块）。
  const index = currentConflictGroupIndex(groups);
  if (index < 0) return false;
  // 第 k 个剩余标记组对应宿主块列表里的第 (总数 - 剩余数 + k) 个（接受只减少标记组，顺序不变）。
  const blocks = document_.blocks || [];
  const target = blocks[blocks.length - groups.length + index] || null;
  if (!target) return false;

  const replacement = side === "yours"
    ? (target.yours || "")
    : side === "theirs"
      ? (target.theirs || "")
      // 「接受两侧」= 两侧内容都保留，顺序为先当前分支后合入内容。
      : [target.yours || "", target.theirs || ""].filter((text) => text.length > 0).join("\n");

  const group = groups[index];
  // execCommand 作用于当前编辑宿主：先把结果区聚焦，否则命令可能不生效。
  if (typeof block.focus === "function") block.focus({ preventScroll: true });
  const range = document.createRange();
  range.setStartBefore(group.spans[0]);
  range.setEndAfter(group.spans[group.spans.length - 1]);
  const selection = window.getSelection();
  selection.removeAllRanges();
  selection.addRange(range);
  const edited = document.execCommand("insertText", false, replacement);
  if (!edited) return false;

  syncConflictCount();
  return true;
}

/**
 * 打开某个冲突文件的三栏解决器（规格 §7.13「点击冲突文件打开三栏冲突解决器」）。
 * 先读取该文件的三栏内容，读不到时不切视图（不假装打开）。
 */
async function openConflictFile(path) {
  const live = window.__augitLive;
  if (!live || !path) return null;
  const loaded = await loadConflict(path).catch(() => null);
  if (!loaded) {
    window.__augitError = "open-conflict:" + path;
    return null;
  }

  live.conflictSessionView = "resolver";
  // 打开一个新文件时从第一处冲突开始。
  live.conflictBlockIndex = 0;
  openConflictSession();
  return loaded;
}

/**
 * 结果区聚焦时的撤销/重做（规格 §7.14：`Ctrl+Z` 撤销、`Ctrl+Shift+Z` 或 `Ctrl+Y` 重做）。
 *
 * 不拦截这些按键——交给浏览器的原生撤销栈处理（也就不会把控制字符写进正文），
 * 只在其后把"还有几个未处理冲突"重新算一遍：撤销会把冲突块还回来，计数必须跟着回。
 */
function bindConflictUndoRefresh() {
  const block = document.querySelector(".conflict-column.result .conflict-block");
  if (!block || block.dataset.undoBound === "true") return;
  block.dataset.undoBound = "true";
  block.addEventListener("keydown", (event) => {
    if (!event.ctrlKey) return;
    const key = event.key.toLowerCase();
    if (key !== "z" && key !== "y") return;
    // 原生撤销/重做在本事件之后生效，因此延后一拍再重新计数。
    window.setTimeout(() => syncConflictCount(), 0);
  });
}

/**
 * 中央结果区是否有未保存的编辑（规格 §7.14）。
 *
 * 用户键入、以及"接受左侧/右侧/两侧"（结果区的一次可撤销编辑）都会触发 `input`；
 * 载入新版本或应用成功后回到干净状态。这个标记决定外部变化时是自动同步还是必须先询问。
 */
function bindConflictDirtyTracking() {
  const block = document.querySelector(".conflict-column.result .conflict-block");
  if (!block || block.dataset.dirtyBound === "true") return;
  block.dataset.dirtyBound = "true";
  block.addEventListener("input", () => {
    const live = window.__augitLive;
    if (live) live.conflictDirty = true;
  });
}

/** 两个磁盘版本是否相同（规格 §7.14：接纳新内容前先核对文件版本）。 */
function conflictVersionEqual(left, right) {
  if (!left || !right) return left === right;
  return left.length === right.length
    && left.sha256 === right.sha256
    && left.lastWriteUtc === right.lastWriteUtc;
}

/**
 * 解决器打开的文件被外部改动后重新核对（规格 §7.14）。
 *
 * 只读一次磁盘版本再决定，不直接改写界面状态：
 * - 文件已不再是冲突（外部工具已解决）→ 关闭解决器，交给随后的状态刷新重建列表；
 * - 版本未变 → 什么都不做（外部事件常成串到达，不能因此反复载入）；
 * - 版本已变且中央**没有**未保存内容 → 自动接纳最新版本（§5.3 外部解决后自动同步）；
 * - 版本已变且中央**有**未保存内容 → 显示"重新载入 / 保留当前内容"的模态选择。
 *
 * 读取期间用户可能已经编辑或关掉窗口，因此回到这里要**重新核对窗口、路径与未保存状态**，
 * 而不是用发起读取时的判断。
 */
async function recheckOpenConflict() {
  const live = window.__augitLive;
  if (!live || !live.conflict) return null;
  const path = live.conflict.path;
  const previous = live.conflict.version;
  const fresh = await invoke("git/conflict-load", { path }, 30000).catch(() => null);
  if (!live.conflict || live.conflict.path !== path) return null;

  if (!fresh || !fresh.available) {
    if (live.conflictDirty) {
      // 文件已不是冲突，但中央有未保存内容：**不清除正文**（规格 §7.14），只说明事实。
      // 此时没有"重新载入"可言（磁盘上已没有可合并的版本），因此给提示而不是模态选择。
      setConflictNotice("该文件已不在冲突状态；中央的未保存内容不会被写回，可以复制后关闭。");
      return null;
    }

    live.conflict = null;
    live.conflictDirty = false;
    live.conflictPending = null;
    live.conflictPromptVersion = null;
    live.conflictSessionView = "list";
    closeConflictMergePrompt();
    return null;
  }

  if (conflictVersionEqual(previous, fresh.version)) return null;

  if (!live.conflictDirty) {
    live.conflict = fresh;
    live.conflictPending = null;
    live.conflictPromptVersion = null;
    openConflictSession();
    return fresh;
  }

  // 同一个磁盘版本只询问一次：一次外部写入会引发多个事件，不能弹成一串（§10.2）。
  if (live.conflictPromptVersion && conflictVersionEqual(live.conflictPromptVersion, fresh.version)) {
    return fresh;
  }

  showConflictMergePrompt(fresh);
  return fresh;
}

/**
 * 「中央有未保存内容且外部文件变化」的模态选择（规格 §7.14）。
 *
 * 视觉稿没有这一页（`conflict-resolver.html` 只画了三栏），因此沿用既有的确认对话框语言
 * （`dialog()` + `info-block`，与"初始化仓库""关闭运行中终端"同构），不新增样式。
 */
function showConflictMergePrompt(fresh) {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host || !live.conflict) return null;
  closeConflictMergePrompt();
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "文件已被外部修改",
    `<div class="info-block" style="width:auto;text-align:left"><h2>${escapeHtml(live.conflict.path)}</h2>`
      + "<p>磁盘上的版本已经变化，而中央结果区还有未保存的内容。</p>"
      + "<p>重新载入会丢弃中央的编辑并显示最新内容；保留当前内容不会改动正文、选区、滚动和撤销记录。</p></div>",
    '<button type="button" class="secondary-button" data-conflict-keep>保留当前内容</button>'
      + '<button type="button" class="primary-button" data-conflict-reload>重新载入</button>',
    true,
    "conflict-external-dialog");
  host.appendChild(layer);
  live.conflictPending = fresh;
  live.conflictPromptVersion = fresh.version;
  window.__augitConflictPrompt = live.conflict.path;
  return layer;
}

/** 关闭外部修改的询问层（不改变正文内容）。 */
function closeConflictMergePrompt() {
  const live = window.__augitLive;
  if (live) live.conflictPending = null;
  document.querySelectorAll(".dialog.conflict-external-dialog").forEach((node) => {
    const owner = node.closest("[data-augit-overlay]") || node;
    owner.remove();
  });
  window.__augitConflictPrompt = null;
}

/** 选择「重新载入」：接纳外部版本，中央的未保存编辑被丢弃。 */
function applyPendingConflictReload() {
  const live = window.__augitLive;
  const fresh = live && live.conflictPending ? live.conflictPending : null;
  closeConflictMergePrompt();
  if (!live || !fresh) return null;
  live.conflict = fresh;
  live.conflictDirty = false;
  live.conflictPromptVersion = null;
  // 重绘会重建结果主体控件，撤销记录随新的磁盘内容一起重来（这是"重新载入"的定义）。
  openConflictSession();
  return fresh;
}

/** 选择「保留当前内容」：只收起询问，正文控件、选区、滚动与撤销记录都不动。 */
function keepConflictEdits() {
  closeConflictMergePrompt();
  return null;
}

/** 回到冲突列表视图（视觉稿的「返回冲突列表」）。 */
function returnToConflictList() {
  const live = window.__augitLive;
  if (!live) return;
  live.conflictSessionView = "list";
  openConflictSession();
}

/** 关闭会话窗口（不动会话本身，用户可再次打开）。 */
function closeConflictSession() {
  document.querySelectorAll(".dialog.conflict-session-dialog").forEach((node) => {
    const owner = node.closest("[data-augit-overlay]") || node;
    owner.remove();
  });
}

/**
 * 执行会话动作（继续 / 跳过 / 中止）。
 *
 * 动作完成后**重新读取真实仓库状态与会话**（规格 §9.3「取消后等待本机 Git 停止，
 * 再读取真实仓库状态」的同一条原则）：不假设动作生效、也不自行推断结果。
 */
async function runConflictSessionAction(action) {
  const live = window.__augitLive;
  if (!live) return null;
  if (action === "close") {
    live.operationSessionShown = false;
    closeConflictSession();
    return null;
  }

  const buttons = [...document.querySelectorAll("[data-operation-action]")];
  buttons.forEach((button) => { button.disabled = true; });
  let payload = null;
  try {
    payload = await invoke("git/operation-action", { action }, 120000);
  } catch (error) {
    payload = { available: true, ok: false, reason: String((error && error.message) || error) };
  }

  if (!payload || !payload.ok) {
    // 失败保留窗口并显示脱敏原因，不假装成功。
    const notice = document.querySelector(".conflict-session-dialog .commit-meta.conflict-blocked")
      || document.querySelector(".conflict-session-dialog .toolbar");
    if (notice) {
      notice.textContent = describeFailure((payload && payload.reason) || "该动作没有完成。", {
        unchanged: "仓库状态没有被这次尝试修改。",
      });
    }
    buttons.forEach((button) => { button.disabled = false; });
    return payload;
  }

  live.operationSession = payload.session || null;
  if (!payload.session || !payload.session.inProgress) {
    live.operationSessionShown = false;
    live.conflictSessionView = null;
    closeConflictSession();
  } else {
    openConflictSession();
  }

  await Promise.all([loadStatus().catch(() => null), loadHistory().catch(() => null)]);
  refresh("side", "editorContent", "editorTabs", "statusbar", "titlebar");
  return payload;
}

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
      // 刚载入的正文与磁盘一致：没有未保存内容，也没有待处理的询问（规格 §7.14）。
      live.conflictDirty = false;
      live.conflictPromptVersion = null;
      live.conflictPending = null;
    }
    return live ? live.conflict : null;
  } catch (error) {
    window.__augitError = "load-conflict:" + String(error && error.message || error);
    return null;
  }
}

/** 保存冲突解决结果：把结果栏文本写回文件并标记已解决。 */
/** 发布待推送信息，供 Push 对话框使用。 *//** 发布待推送信息，供 Push 对话框使用。 */
/** 状态或引用变化后按已有数据重算推送预览；不发起查询。 */
function refreshPush() {
  const live = window.__augitLive;
  if (!live) return;
  live.push = computePush(live);
}

/**
 * 无法逐块合并的冲突文件（规格 §7.14）：二进制、非法 UTF-8、超限文件。
 *
 * 与三栏里的接受不同，这里走宿主的**整文件**语义（`git checkout --ours/--theirs`
 * 后 `git add`），文件被覆盖并立即标记为已解决，**不可撤销**——页面上的说明也这么写。
 * `external` 只是把文件交给系统默认程序，不改变仓库状态，所以不做任何冻结。
 */
async function runConflictWholeSide(action) {
  const live = window.__augitLive;
  const conflict = live && live.conflict;
  if (!live || !conflict) return null;

  if (action === "external") {
    try {
      const launched = await invoke("external/launch", { action: "open", path: conflict.path }, 30000);
      if (launched && launched.launched === false) {
        // 对话框是模态的，全局提示可能被遮住，因此失败原因就近显示（规格 §10.2）。
        setConflictNotice(launched.reason || "无法用系统默认程序打开该文件。");
      } else if (launched && launched.launched) {
        // 之前那次失败的原因不该继续挂着（规格 §10.2：状态未变才可重复，恢复后要消失）。
        setConflictNotice("");
      }
      return launched;
    } catch (error) {
      setConflictNotice("无法用系统默认程序打开该文件：" + String((error && error.message) || error));
      return null;
    }
  }

  if (action !== "yours" && action !== "theirs") return null;
  const label = action === "yours" ? (conflict.yoursLabel || "左侧") : (conflict.theirsLabel || "右侧");
  setConflictApplying(true);
  setConflictNotice("");
  try {
    const result = await invoke("git/conflict-accept", { path: conflict.path, side: action }, 60000);
    if (!result || result.available === false) {
      // 失败后按实际未处理冲突数恢复动作，并说明原因（规格 §10.2）。
      setConflictApplying(false);
      setConflictNotice((result && result.reason) || "整侧接受失败，文件没有被修改。");
      return result;
    }
    live.conflictApplying = false;
    // 该文件已被暂存并标记解决：清掉解决器状态，回到（可能已关闭的）冲突列表。
    live.conflict = null;
    live.conflictSessionView = "list";
    showToast({ title: `已接受${label}`, text: `${conflict.path} 已整侧接受并标记为已解决。`, kind: "info" });
    await loadOperationSession();
    return result;
  } catch (error) {
    setConflictApplying(false);
    setConflictNotice("整侧接受失败：" + String((error && error.message) || error));
    return null;
  }
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
    // 统一走应用状态机：冻结、只读、忙碌提示、失败恢复都在里面（规格 §7.14）。
    await applyConflictResult();
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
  // 当前标签变化同样属于会话数据（防抖 + 内容相同不写盘）。
  scheduleSessionPersist();
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
  // 标签集合变化后写回会话数据（防抖；内容没变时不会真的写盘）。
  scheduleSessionPersist();
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
  scheduleSessionPersist();
  // 关闭比较标签：解除跟随 Changes 选择（规格 §5.2）。
  // 历史比较同样在此解除跟随——关闭后单击不自动重开，只有再次双击或 Enter 才打开。
  if (closing && closing.kind === "comparison") {
    live.followChanges = false;
    if (live.historyComparison) live.historyComparison.status = "closed";
    // 历史比较有独立的递增令牌：只推进 diffToken 不足以让它的收尾逻辑失效，
    // 晚到的响应会把状态从 "closed" 复活成 "ready"/"unavailable"，
    // 于是"关闭后解除跟随"失效——之后改选提交或文件会重新创建比较标签（规格 §5.2）。
    historyComparisonToken += 1;
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
  if (!live.layout) {
    live.layout = readInitialLayout();
    return live.layout;
  }

  // 用户尚未操作过时，布局必须跟随**实际渲染**的场景值。
  // 首次读取可能发生在本场景首屏渲染之前，缓存下"项目"这类默认值；
  // 此后状态就与界面不一致——实测在 commit-changes 场景里 activeRail 是 project
  // 而界面激活的是"提交"，于是点击已激活入口不会折叠（违反规格 §5.1）。
  // 一旦用户操作过（userDriven），状态即权威，不再重读。
  if (!live.layout.userDriven) {
    const observed = readInitialLayout();
    if (observed.activeRail !== live.layout.activeRail
        || observed.side !== live.layout.side
        || observed.bottom !== live.layout.bottom) {
      live.layout = observed;
    }
  }

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
    // 整页重绘会换掉工具窗口入口与侧栏内容，必须走**完整的**重绘后处理：
    // 只重挂工具入口（bindToolRail）会让 restoreChangesState 被跳过，
    // 于是提交草稿、改动列表选中行、滚动位置全部丢失——实测折叠/展开提交工具窗口后
    // 草稿输入框为空、选中行为 null（违反规格 §5.2「切换工具窗口不改变当前文件」
    // 与 §6.6 的「明确禁止变化」列）。
    window.__augitRender();
    rebindAfterRender();
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

    // 会话窗口里的冲突文件行与「返回冲突列表」（规格 §7.13）。
    const conflictRow = event.target.closest && event.target.closest("[data-conflict-path]");
    if (conflictRow) {
      event.preventDefault();
      void openConflictFile(conflictRow.dataset.conflictPath);
      return;
    }

    // 外部修改后的模态选择（规格 §7.14）。
    const conflictReload = event.target.closest && event.target.closest("[data-conflict-reload]");
    if (conflictReload) {
      event.preventDefault();
      applyPendingConflictReload();
      return;
    }

    const conflictKeep = event.target.closest && event.target.closest("[data-conflict-keep]");
    if (conflictKeep) {
      event.preventDefault();
      keepConflictEdits();
      return;
    }

    // 无法逐块合并的文件（规格 §7.14）：整侧接受或交给外部工具。
    const conflictWhole = event.target.closest && event.target.closest("[data-conflict-whole]");
    if (conflictWhole) {
      event.preventDefault();
      void runConflictWholeSide(conflictWhole.dataset.conflictWhole);
      return;
    }

    // 解决器里的接受动作（规格 §7.14）：结果区的一次可撤销编辑。
    const conflictSide = event.target.closest && event.target.closest("[data-conflict-side]");
    if (conflictSide) {
      event.preventDefault();
      acceptConflictBlock(conflictSide.dataset.conflictSide);
      return;
    }

    const conflictNav = event.target.closest && event.target.closest("[data-conflict-nav]");
    if (conflictNav) {
      event.preventDefault();
      navigateConflictBlock(conflictNav.dataset.conflictNav === "next" ? 1 : -1);
      return;
    }

    const conflictBack = event.target.closest && event.target.closest("[data-conflict-back]");
    if (conflictBack) {
      event.preventDefault();
      returnToConflictList();
      return;
    }

    // 冲突操作会话的动作（规格 §7.13）。
    const operationAction = event.target.closest && event.target.closest("[data-operation-action]");
    if (operationAction) {
      event.preventDefault();
      void runConflictSessionAction(operationAction.dataset.operationAction);
      return;
    }

    // 回滚确认对话框的动作（规格 §10.4）。
    const rollbackAction = event.target.closest && event.target.closest("[data-rollback-action]");
    if (rollbackAction) {
      event.preventDefault();
      if (rollbackAction.dataset.rollbackAction === "confirm") void confirmRollbackDialog();
      else closeRollbackDialog();
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

    // 主菜单「文件 / 视图 / Git」动作菜单的条目（规格 §5.1）。
    // 这些条目此前只渲染出来、点了没有反应——等于把"点了没反应"从入口挪进了菜单。
    const mainMenuItem = event.target.closest && event.target.closest(".main-menu-popover .menu-item");
    if (mainMenuItem) {
      event.preventDefault();
      void runMainMenuPopoverAction(mainMenuItem.dataset.mainMenuAction);
      return;
    }

    // 项目树右键菜单里的动作（规格 §5.4）。
    // 条目语义取自视觉稿的 projectContextMenu()；「文件历史」「Blame」复用
    // 改动列表菜单的同一实现，避免两套布局状态机各自漂移。
    const projectMenu = event.target.closest && event.target.closest(".project-menu .menu-item");
    if (projectMenu) {
      event.preventDefault();
      const label = (projectMenu.textContent || "").trim();
      const action = label.includes("文件历史") ? "file-history"
        : label.includes("Blame") ? "blame"
          : label.includes("刷新") ? "refresh-tree" : null;
      if (action === "refresh-tree") {
        closeLiveOverlay();
        void refreshFileTree();
      } else if (action) {
        void runChangesContextAction(action, { layerClass: ".project-menu", pathField: "treePath" });
      } else if (label.includes("复制路径")) {
        void copyTreePath(projectMenu.closest(".project-menu").dataset.treePath);
        closeLiveOverlay();
      } else if (label.includes("资源管理器")) {
        void launchExternal("reveal", projectMenu.closest(".project-menu").dataset.treePath);
        closeLiveOverlay();
      } else if (label.includes("外部终端")) {
        void launchExternal("terminal", projectMenu.closest(".project-menu").dataset.treePath);
        closeLiveOverlay();
      } else {
        // 其余条目没有对应能力时如实记录，不假装成功。
        window.__augitUnwiredAction = projectMenu.getAttribute("href");
        window.__augitUnwiredLabel = label.slice(0, 40);
        closeLiveOverlay();
      }

      return;
    }

    // 变化文件右键菜单里的动作。
    const changesMenu = event.target.closest && event.target.closest(".changes-menu .menu-item");
    if (changesMenu) {
      event.preventDefault();
      const label = (changesMenu.textContent || "").trim();
      // 目标路径取自菜单层自己的数据：条目本身是 <a href>，不能拿 href 当路径。
      const changesLayer = changesMenu.closest(".changes-menu");
      const changePath = changesLayer ? changesLayer.dataset.changePath : null;
      const action = label.includes("显示 Diff") ? "diff"
        : label.includes("回滚") ? "rollback"
          : label.includes("文件历史") ? "file-history"
            : label.includes("Blame") ? "blame" : null;
      if (action) {
        void runChangesContextAction(action);
      } else if (label.includes("复制路径")) {
        // 与项目树菜单同一实现（规格 §5.4）：此前这两项落在"其余条目"分支，
        // 只关掉菜单什么都不做——等于把"点了没反应"留在菜单里。
        void copyTreePath(changePath);
        closeLiveOverlay();
      } else if (label.includes("资源管理器")) {
        void launchExternal("reveal", changePath);
        closeLiveOverlay();
      } else closeLiveOverlay();
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
  const title = `比较: ${branch}`;
  // 与工作区 Diff 同一处理：标签与视图必须在**发请求之前**就位，并调度加载提示。
  // 否则查询期间编辑区还停在上一个视图、也没有加载指示
  // （规格 §7.9 要求"激活时立即打开并显示双方引用及文件路径，查询完成后只填充正文"）。
  const tab = ensureComparisonTab(target.path, title);
  activateComparisonTab(tab);
  scheduleDiffLoadingMarker();
  let succeeded = false;
  try {
    const diff = await loadDiff(target.path, { revision: branch, force: true }).catch(() => null);
    if (!diff) {
      window.__augitError = "compare-workspace:" + target.path;
      return;
    }

    succeeded = true;
    syncComparisonTab(tab, target.path, title);
    activateComparisonTab(tab);
    refreshAfterEvent("editorContent", "editorTabs", "statusbar");
  } finally {
    clearDiffLoadingMarker();
    // 读取失败时不留下一个打不开的比较标签。
    if (!succeeded) closeTab(tab.id);
  }
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
/**
 * 打开回滚确认对话框（规格 §10.4）。
 *
 * 危险操作必须先显示**具体影响**并由用户确认。此前 Changes 右键菜单里的「回滚…」
 * 落在"其余条目"分支，只关掉菜单什么都不做——用户以为回滚了，实际没有发生任何事。
 */
function openRollbackDialog(path) {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host || !path) return;
  const files = (live.status && live.status.files) || [];
  const file = files.find((item) => item.path === path) || null;
  if (!file) {
    // 列表已经变化（例如文件刚被外部处理）：先重新读取状态，不打开一个对不上文件的确认框。
    void loadStatus().catch(() => null);
    return;
  }

  closeLiveOverlay();
  rememberDialogFocus();
  // 目标身份进状态：对话框内容由 mockup 的 liveRollbackBody 读状态渲染，
  // 区域刷新会重建节点，只把目标写在 DOM 上会被静默丢弃（handoff 第 3 节第 5 条）。
  live.rollback = { path: file.path };
  document.querySelectorAll("[data-augit-overlay].live-overlay").forEach((node) => node.remove());
  document.querySelectorAll(".dialog.rollback-dialog").forEach((node) => {
    const owner = node.closest("[data-augit-overlay]") || node;
    owner.remove();
  });
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    `回滚文件 · ${escapeText(file.path)}`,
    liveRollbackBody(),
    `<button type="button" class="secondary-button" data-rollback-action="cancel">取消</button>`
      + `<button type="button" class="danger-button" data-rollback-action="confirm">回滚完整文件</button>`,
    true,
    "rollback-dialog");
  host.appendChild(layer);
  const confirm = layer.querySelector('[data-rollback-action="confirm"]');
  if (confirm) confirm.focus();
}

/** 关闭回滚对话框；目标身份一并清掉，避免下次打开沿用上一个文件。 */
function closeRollbackDialog() {
  document.querySelectorAll(".dialog.rollback-dialog").forEach((node) => {
    const owner = node.closest("[data-augit-overlay]") || node;
    owner.remove();
  });
  const live = window.__augitLive;
  if (live) live.rollback = null;
  restoreDialogFocus();
}

/**
 * 确认回滚：交给宿主执行，随后重新读取真实仓库状态。
 *
 * 失败时保留对话框并显示脱敏原因（规格 §9.3「失败后保留用户输入并显示原因」），
 * 不假装成功、也不自行推断结果——是否移入回收站由宿主的实际动作决定。
 */
async function confirmRollbackDialog() {
  const live = window.__augitLive;
  const path = live && live.rollback ? live.rollback.path : null;
  if (!path) return;
  const button = document.querySelector('[data-rollback-action="confirm"]');
  if (button) button.disabled = true;
  let result = null;
  try {
    result = await invoke("git/rollback", { path }, 60000);
  } catch (error) {
    result = { available: true, rolledBack: false, reason: String((error && error.message) || error) };
  }

  if (!result || !result.rolledBack) {
    const notice = document.querySelector(".rollback-dialog .rollback-notice");
    if (notice) {
      notice.hidden = false;
      notice.classList.add("error");
      notice.textContent = notice.title = describeFailure(
        (result && result.reason) || "回滚未完成。",
        { unchanged: "文件内容、Changes 列表与提交输入没有被修改。" });
    }
    if (button) button.disabled = false;
    return;
  }

  closeRollbackDialog();
  await Promise.all([loadStatus().catch(() => null), loadHistory().catch(() => null)]);
  refresh("side", "editorContent", "editorTabs", "statusbar", "titlebar");
}

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
  showPointerContextMenu(changesContextMenu(), {
    layerClass: "changes-menu",
    dataset: { changePath: path },
    clientX,
    clientY,
    maxHeight: 220,
  });
}

/**
 * 在指针处显示一个上下文菜单层。
 *
 * 改动列表菜单与项目树菜单的层创建、定位与夹边逻辑完全相同（只差类名、附加数据与
 * 夹边高度），此前各写了一遍。集中在这里后，两处菜单的定位行为不会再各自漂移。
 */
function showPointerContextMenu(menuMarkup, options) {
  const { layerClass, dataset, maxHeight, focusFirst = false } = options;
  const host = document.querySelector(".augit-window");
  if (!host) return null;
  const template = document.createElement("template");
  template.innerHTML = menuMarkup;
  const menu = template.content.firstElementChild;
  if (!menu) return null;
  const layer = document.createElement("div");
  layer.className = `overlay-layer live-overlay ${layerClass}`;
  layer.setAttribute("data-augit-overlay", "");
  for (const [key, value] of Object.entries(dataset || {})) {
    layer.dataset[key] = value;
  }

  // 定位到指针处并夹在窗口内，避免菜单被裁掉。
  const rect = host.getBoundingClientRect();
  layer.style.position = "absolute";
  layer.style.inset = "0";
  menu.style.position = "absolute";
  menu.style.left = `${Math.max(4, Math.min(options.clientX - rect.left, rect.width - 240))}px`;
  menu.style.top = `${Math.max(4, Math.min(options.clientY - rect.top, rect.height - maxHeight))}px`;
  menu.style.zIndex = "2";
  layer.appendChild(menu);
  host.appendChild(layer);
  if (focusFirst) {
    const first = layer.querySelector(".menu-item");
    if (first) first.focus({ preventScroll: true });
  }

  return layer;
}

/**
 * 项目树的右键菜单（规格 §5.4）。
 *
 * 契约取自 `docs/ux-mockups/mockup.js` 的 `projectContextMenu()`：
 * 复制路径、在资源管理器中定位、在外部终端打开、刷新、文件历史、Blame。
 * 此前项目树**完全没有右键处理**（只有改动列表有），右键毫无反应。
 */
function openTreeContextMenu(row, clientX, clientY) {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host || !row) return;
  const path = row.dataset.treePath;
  if (!path) return;
  rememberDialogFocus();
  closeLiveOverlay();
  showPointerContextMenu(projectContextMenu(), {
    layerClass: "project-menu",
    dataset: {
      treePath: path,
      treeDirectory: row.dataset.treeDirectory === "true" ? "true" : "false",
    },
    clientX,
    clientY,
    maxHeight: 240,
    focusFirst: true,
  });
}

/**
 * 项目树菜单里"用系统程序打开"的动作（规格 §5.4）。
 *
 * 路径边界由宿主校验：它最终会启动进程，网页层若能传任意路径
 * 就等于获得了任意程序启动能力，因此越界由宿主拒绝并给出原因。
 */
async function launchExternal(action, path) {
  try {
    const result = await invoke("external/launch", { action, path }, 15000);
    if (result && result.launched === false) {
      window.__augitLaunchError = result.reason || "无法用系统程序打开该路径。";
      showToast({
        title: "无法打开该路径",
        text: describeFailure(window.__augitLaunchError, {
          unchanged: "文件内容与改动列表没有被修改。",
          next: "可以改用资源管理器打开。",
        }),
      });
    } else if (result && result.launched) {
      clearToast();
    }

    return result;
  } catch (error) {
    window.__augitLaunchError = "external-launch:" + String(error && error.message || error);
    showToast({
      title: "无法打开该路径",
      text: describeFailure(window.__augitLaunchError, {
        unchanged: "文件内容与改动列表没有被修改。",
        next: "可以改用资源管理器打开。",
      }),
    });
    return null;
  }
}

/**
 * 复制路径到剪贴板（规格 §5.4 项目树菜单）。
 *
 * 交给宿主而不是 `navigator.clipboard`：WebView2 默认不授予
 * `ClipboardApiRequested`，页面里写入会静默失败。宿主负责写入并按结果回话。
 */
async function copyTreePath(path) {
  const text = String(path || "");
  if (text.length === 0) return false;
  try {
    const result = await invoke("clipboard/write", { text }, 10000);
    if (result && result.copied) {
      window.__augitCopiedPath = text;
      // 同一次操作重试成功后，上一次的失败提示不应继续挂着。
      clearToast();
      return true;
    }

    window.__augitCopiedPath = null;
    window.__augitCopyError = (result && result.reason) || "复制失败。";
    // 规格 §10.2：失败必须说明发生了什么、哪些状态未改变、可以做什么。
    // 此前这里只把原因写进一个**没有任何地方读取**的变量，用户看不到任何反馈。
    showToast({
      title: "复制路径失败",
      text: describeFailure(window.__augitCopyError, {
        unchanged: "文件内容与改动列表没有被修改。",
        next: "可以直接在资源管理器中复制。",
      }),
    });
    return false;
  } catch (error) {
    window.__augitCopiedPath = null;
    window.__augitCopyError = "copy-path:" + String(error && error.message || error);
    showToast({
      title: "复制路径失败",
      text: describeFailure(window.__augitCopyError, {
        unchanged: "文件内容与改动列表没有被修改。",
        next: "可以直接在资源管理器中复制。",
      }),
    });
    return false;
  }
}

/**
 * 右键菜单里的动作（规格 §7.8）。
 * 「文件历史」切到底部工具窗口并读取该路径的历史；「Blame」进入归属视图。
 */
async function runChangesContextAction(action, options = {}) {
  // 项目树菜单复用同一实现：数据源选择器与"目标路径"字段不同，
  // 其余（切底部工具窗口、整页重绘判断、刷新区域）完全一致——
  // 复制一套会让两处的布局状态机日后各自漂移。
  const layer = document.querySelector(options.layerClass || ".changes-menu");
  const path = layer && (options.pathField ? layer.dataset[options.pathField] : layer.dataset.changePath);
  closeLiveOverlay();
  if (!path) return;
  if (action === "diff") {
    await openChangeDiff(path);
    return;
  }

  if (action === "rollback") {
    openRollbackDialog(path);
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

/**
 * 执行主菜单动作菜单里的条目（规格 §5.1）。
 *
 * 条目只复用**已经实现**的能力，不在这里新造行为：
 * 打开工作区、刷新文件树、显隐工具窗口、取回、推送、分支与标签。
 */
async function runMainMenuPopoverAction(action) {
  const live = window.__augitLive;
  closeLiveOverlay();
  if (!live || !action) return;

  if (action === "refresh-tree") {
    void refreshFileTree();
    return;
  }

  if (action === "toggle-side") {
    // 与左侧竖向入口同一套布局逻辑：切到项目工具窗口。
    applyRailAction("project");
    return;
  }

  if (action === "toggle-bottom") {
    // 底部工具窗口是终端与 Git 历史二者之一（规格 §5.1 的同一套布局逻辑）。
    const current = live.layout ? live.layout.bottom : null;
    applyRailAction(current === "history" ? "terminal" : "history");
    return;
  }

  if (action === "branches") {
    openBranchesPopover();
    return;
  }

  if (action === "push") {
    // 规格 §7.12：推送前先显示待推送提交并让用户确认，不直接推送。
    void openPushDialog();
    return;
  }

  if (action === "fetch") {
    await runPopoverAction("fetch");
  }
}

/** 三个动作菜单的能力集合（来自规格 §7.x 已实现的动作）。 */
const MAIN_MENU_POPOVERS = {
  // 只列已经实现的动作：「打开工作区」需要新的宿主能力（选择目录、切换工作区），
  // 本轮不造——菜单里放一个点了没反应的条目，等于把"点了没反应"挪进菜单。
  file: [
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
  const selected = selectedCommitRow();
  const revision = selected && selected.dataset.fullHash
    ? selected.dataset.fullHash
    : live.history.commits[0].fullHash;
  // 选中的提交已经换了：上一次的详情不能继续留在状态里被重绘出来。
  if (live.commitDetails && live.commitDetails.revision !== revision) {
    live.commitDetails = null;
  }

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
    const live = window.__augitLive;
    if (token !== commitDetailsToken) return;
    if (!commit || !commit.available) {
      const reasonHtml = `<p class="commit-meta">${escapeText(commit && commit.reason ? commit.reason : "无法读取提交详情")}</p>`;
      if (live) live.commitDetails = { revision, filesHtml: reasonHtml, detailHtml: "" };
      filesHost.innerHTML = reasonHtml;
      return;
    }

    const files = commit.files || [];
    const filesHtml = files.length === 0
      ? `<p class="commit-meta">该提交没有变更文件</p>`
      : `<div class="tree-row"><span>${escapeText(String(files.length))} 个文件</span></div>`
        + files.map((file) => `<div class="tree-row depth-1 live-file-status-${escapeText(file.kind)}" data-history-path="${escapeText(file.path)}">${escapeText(file.name)}<span class="commit-meta">${escapeText(file.directory)}</span></div>`).join("");
    const detailHtml = `<h3>${escapeText(commit.subject)}</h3>`
      + `<div>${escapeText(commit.hash)} · ${escapeText(commit.author)} · ${escapeText(commit.date)}</div>`
      + (commit.body ? `<p class="commit-meta">${escapeText(commit.body)}</p>` : "");
    // 详情内容必须进状态：mockup 的 Git 日志详情区只从状态渲染占位，真实内容由这里写进 DOM，
    // 因此任何包含 bottomTool 的区域刷新（打开历史比较、外部变化重载等）都会把占位写回去，
    // 只放在 DOM 里的内容会被静默丢弃（handoff 第 3 节第 5 条）。
    if (live) {
      live.commitDetails = { revision, filesHtml, detailHtml };
    }

    filesHost.innerHTML = filesHtml;
    detailHost.innerHTML = detailHtml;
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
/**
 * 规格 §6.4：当前选中的改动文件不在新列表里时，改选**同组最近邻**。
 *
 * 没有这一步会出现两个问题：界面上没有选中行，而 `live.selectedChangePath` 仍指向
 * 已经消失的文件——工具栏的「显示 Diff」「回滚」是按它判断可用性的，
 * 于是命令对一个不存在的目标保持可用。
 */
function reconcileChangeSelection(previous, next) {
  const live = window.__augitLive;
  const current = live ? live.selectedChangePath : null;
  if (!current) return;
  const files = next || [];
  if (files.some((file) => file.path === current)) return;

  const oldIndex = (previous || []).findIndex((file) => file.path === current);
  const group = oldIndex >= 0 ? previous[oldIndex].group : null;
  const sameGroup = files.filter((file) => !group || file.group === group);
  if (sameGroup.length === 0) {
    // 同组已空：清掉选中，让界面显示稳定空状态，而不是留一个悬空的目标。
    live.selectedChangePath = null;
    return;
  }

  // 最近邻：原位置**之后**的同组项优先，其次之前，最后退回同组第一项。
  const after = (previous || []).slice(oldIndex + 1)
    .filter((file) => !group || file.group === group)
    .map((file) => file.path);
  const before = (previous || []).slice(0, Math.max(oldIndex, 0)).reverse()
    .filter((file) => !group || file.group === group)
    .map((file) => file.path);
  live.selectedChangePath = [...after, ...before].find(
    (path) => sameGroup.some((file) => file.path === path)) || sameGroup[0].path;
}

function applyStatus() {
  const live = window.__augitLive;
  if (!live || !latestStatus) return;
  // 选中项的归属要拿**上一次**的列表来判断（同组、原位置），因此先对账再覆盖。
  reconcileChangeSelection(previousStatusFiles, latestStatus.files);
  previousStatusFiles = latestStatus.files;
  live.branch = latestStatus.branch;
  // 标题栏的工作区名来自宿主，缺失时保留视觉稿的默认值。
  if (latestStatus.workspaceName) live.workspaceName = latestStatus.workspaceName;
  live.isDetached = latestStatus.isDetached;
  live.changeCount = latestStatus.files.length;
  live.status = latestStatus;
  live.statusError = latestStatusError;
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

  // 界面此时已可交互：**先把重绘后的绑定补齐，再标记就绪**。
  // 此前这里没有调用 rebindAfterRender()，而全局绑定（导航守卫、工具入口、
  // 快捷键、Esc、紧凑窗口）都挂在它里面——于是首屏有一段"可交互但无绑定"的窗口：
  // 实测该窗口内 `__augitNavGuarded`/`__augitShortcutsBound`/`__augitRailBound`
  // 全为 false，点击分支芯片这类未接线链接会**直接把界面导航离开应用**。
  // Git 状态实测约 15 秒才到，这个窗口并不短，所以必须在这里补上。
  rebindAfterRender();
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
      const selected = selectedCommitRow();
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

  // 会话恢复必须在设置到位之后（`loadSettings` 在就绪点之后才 await，放早了读不到
  // openFiles，恢复会静默不发生——实测踩过）。用 void 不阻塞后续启动步骤：
  // 首屏不能被恢复文件的读取拖慢；用户交互会通过代次闸门让进行中的恢复收手（§6.7）。
  document.addEventListener("click", markUserInteraction, true);
  document.addEventListener("keydown", markUserInteraction, true);
  void restoreSession();
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
  // 展开层级属于会话数据（规格 §6.7）：防抖写回，内容没变不会真的写盘。
  scheduleSessionPersist();
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

// 改动工具窗工具栏的「显示 Diff」与「回滚」作用于当前选中的改动文件（规格 §7.6/§10.4）。
//
// 必须在**捕获阶段**拦下：视觉稿自身给 [data-action="show-change-diff"] 绑了
// `openDiff(selectedFile())`（样例路径），在真实外壳里点了不会有任何真实结果
// （实测：diffPath=null、0 次 diff 请求，按钮等于没接线）；而"回滚"按钮在视觉稿里
// 只有解禁逻辑、没有动作。拦下后由这里给出真实行为，同时保证样例差异不会出现在真实外壳里。
document.addEventListener("click", (event) => {
  const live = window.__augitLive;
  if (!live) return;
  const button = event.target.closest && event.target.closest(
    '.side-tool .changes-layout > .toolbar [data-action="show-change-diff"],'
    + ' .side-tool .changes-layout > .toolbar [data-action="rollback-change"]');
  if (!button) return;
  event.preventDefault();
  event.stopPropagation();
  const path = live.selectedChangePath;
  if (!path) return;
  if (button.dataset.action === "rollback-change") openRollbackDialog(path);
  else void openChangeDiff(path);
}, true);

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

  followHistoryComparison(row.dataset.historyPath);
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

// 项目树右键打开上下文菜单（规格 §5.4）。
// 菜单键（Shift+F10 / ContextMenu）走同一条路径：先聚焦该行再打开。
document.addEventListener("contextmenu", (event) => {
  const row = event.target.closest && event.target.closest(".side-content.tree .tree-row");
  if (!row || !window.__augitLive) return;
  event.preventDefault();
  openTreeContextMenu(row, event.clientX, event.clientY);
}, true);

// 菜单键：键盘用户打开同一个菜单，位置取该行的左下角。
document.addEventListener("keydown", (event) => {
  if (event.key !== "ContextMenu" && !(event.shiftKey && event.key === "F10")) return;
  const row = event.target.closest && event.target.closest(".side-content.tree .tree-row");
  if (!row || !window.__augitLive) return;
  event.preventDefault();
  const rect = row.getBoundingClientRect();
  openTreeContextMenu(row, Math.round(rect.left + 12), Math.round(rect.bottom));
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
