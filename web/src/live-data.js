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
// 历史请求令牌（规格 §7.8）：全量重读递增，在途的"下一页"结果因此作废，不覆盖新上下文。
let historyRequestToken = 0;
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
      // 非 Git 目录（产品规格 §2）。状态可能早于 live 对象与 `mockup.js` 到达，
      // 需求先缓存，由 `showRepositoryInitEntry()` 在可弹时补上；只弹一次。
      pendingRepositoryInit = true;
      showRepositoryInitEntry();
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
 * 把界面上的日志筛选翻成 `git/history` 的参数。
 *
 * 权威 `GitLogProvider.getGitLogParameters()`（`plugins/git4idea/backend/src/log/GitLogProvider.kt:446-533`）
 * 把六类筛选翻成 git 参数：分支/引用 → 起始修订、文本 → `--grep`（`--fixed-strings` ＋ `--regexp-ignore-case`）、
 * 用户 → `--author`、日期 → `--after`／`--before`、路径 → `--` 之后的路径。
 * Augit 的宿主 `git/history` 收 `branch`／`message`／`hash`／`author`／`since`／`until`／`path`；
 * 一个都不传时是整仓历史（等价权威 `GitLogUtil.LOG_ALL` = `HEAD --branches --remotes --tags`，即 `--all`）。
 */
function historyFilterParams(filter, page = 0) {
  const active = filter || {};
  const params = {};
  if (active.branch) params.branch = active.branch;
  if (Array.isArray(active.branches) && active.branches.length > 0) params.branches = active.branches;
  if (Array.isArray(active.authors) && active.authors.length > 0) params.authors = active.authors;
  if (active.message) params.message = active.message;
  if (active.hash) params.hash = active.hash;
  if (active.author) params.author = active.author;
  if (active.since) params.since = active.since;
  if (active.until) params.until = active.until;
  if (active.path) params.path = active.path;
  // 「按路径筛选」的值是一组路径（权威 `VcsLogStructureFilter`）：宿主的 `paths`（数组）优先于单值 `path`。
  if (Array.isArray(active.paths) && active.paths.length > 0) params.paths = active.paths;
  // 规格 §7.8：分页只在滚动触底时请求下一页；第 0 页不送 `page`，避免改变既有请求形状。
  if (page > 0) params.page = page;
  return params;
}

/** 日志筛选是否生效（决定空态文案与"重置筛选"入口是否出现）。 */
function historyFilterActive(filter) {
  return Object.keys(historyFilterParams(filter)).length > 0;
}

/**
 * 读取 Git 历史。与状态一样在首屏之后补取，不阻塞界面出现。
 */
async function loadHistory() {
  try {
    // `git/status`／历史在 `loadDocument()` **之前**就发出，因此这里不能一开始就捕获 `live`：
    // 响应回来时 `window.__augitLive` 往往才刚建立（第 218 轮实测：早捕获会让页码/`hasNextPage` 写不进状态）。
    const filter = (window.__augitLive && window.__augitLive.historyFilter) || {};
    // 全量重读（换筛选、刷新、进入/离开文件历史）⇒ 递增令牌：在途的"下一页"结果作废，
    // 不能把旧上下文的页追加到新列表上（规格 §7.8「旧页结果即使晚到也不能覆盖当前列表」）。
    const token = ++historyRequestToken;
    const history = await invoke("git/history", historyFilterParams(filter), 60000);
    if (token !== historyRequestToken) return null;
    if (!history || !history.available || !history.isRepository || !history.commits) {
      return null;
    }

    latestHistory = {
      head: history.head,
      branch: latestStatus ? latestStatus.branch : null,
      filterActive: historyFilterActive(filter),
      hasNextPage: !!history.hasNextPage,
      commits: history.commits.map(mapHistoryCommit),
    };
    const live = window.__augitLive;
    if (live) {
      live.historyPage = 0;
      live.historyHasNextPage = !!history.hasNextPage;
      live.historyLoadingMore = false;
      // 全量重读＝新的列表上下文：滚动回到顶部（分页追加路径不会再走这里）。
      live.historyScrollTop = 0;
    }
    applyHistory();
    return latestHistory;
  } catch (error) {
    window.__augitError = "load-history:" + String(error && error.message || error);
    return null;
  }
}

/** 历史提交载荷 → 界面条目（日志与「与当前分支比较」共用同一套作者列字段）。 */
function mapHistoryCommit(commit) {
  return {
    hash: commit.hash,
    fullHash: commit.fullHash,
    subject: commit.subject,
    author: commit.author,
    // 「与当前分支比较」沿用文件历史列表的行：作者列的值与 tooltip 需要邮箱与提交者
    //（权威 `FileHistoryPanelImpl.AuthorColumnInfo`，`FileHistoryPanelImpl.java:751-799`）。
    authorEmail: commit.authorEmail,
    committerName: commit.committerName,
    committerEmail: commit.committerEmail,
    date: commit.date,
    references: commit.references || [],
    parents: commit.parents || [],
  };
}

/**
 * 读取下一页提交并追加（规格 §7.8「分页加载在列表底部触发」）。
 *
 * 规则（逐条对应 `ux-spec.md:475-476`）：
 * - 下一页**成功接纳后**才更新已加载页码；失败/取消不更新页码，下次触底仍从同一页重试；
 * - 追加不改变已有 100 条的可见性（保留 `scrollTop`）；
 * - 查询期间不禁用列表（只忽略重复触发）；
 * - 上下文切换后（`historyRequestToken` 变化）旧页结果丢弃，不覆盖当前列表。
 */
async function loadHistoryPage(page) {
  const live = window.__augitLive;
  if (!live || live.historyLoadingMore || page <= 0) return false;
  const token = historyRequestToken;
  const filter = live.historyFilter || {};
  live.historyLoadingMore = true;
  // 观测用（也给验收套件读）：不改变界面，列表在加载期间仍可选择与滚动（规格 §7.8）。
  window.__augitHistoryLoadingMore = true;
  try {
    const history = await invoke("git/history", historyFilterParams(filter, page), 60000);
    if (token !== historyRequestToken) return false;
    if (!history || !history.available || !history.isRepository || !Array.isArray(history.commits)) {
      // 失败：保留当前页与页码，下一次触底从同一页重试（不显示为成功）。
      window.__augitHistoryPageError = (history && history.reason) || "读取下一页失败。";
      return false;
    }
    const known = new Set((latestHistory.commits || []).map((commit) => commit.fullHash));
    const appended = history.commits.map(mapHistoryCommit).filter((commit) => !known.has(commit.fullHash));
    latestHistory = {
      ...latestHistory,
      commits: [...(latestHistory.commits || []), ...appended],
      hasNextPage: !!history.hasNextPage,
    };
    live.historyPage = page;
    live.historyHasNextPage = !!history.hasNextPage;
    window.__augitHistoryPageError = null;
    applyHistory();
    // 追加后区域刷新会重建列表、`scrollTop` 归零。位置由两重还原保证：
    // ① `live.historyScrollTop` 持续记录、`restoreHistoryScroll()` 每次渲染后还原；
    // ② 本函数再按刷新前抓到的值补一次（下一帧），覆盖刷新之后仍会发生的重建。
    const list = document.querySelector(".log-list-panel .commit-list");
    const keepScroll = list ? list.scrollTop : null;
    if (keepScroll !== null) live.historyScrollTop = keepScroll;
    refresh("bottomTool");
    if (keepScroll !== null) {
      window.requestAnimationFrame(() => {
        const current = document.querySelector(".log-list-panel .commit-list");
        if (current && Math.abs(current.scrollTop - keepScroll) > 1) current.scrollTop = keepScroll;
      });
    }
    return true;
  } catch (error) {
    if (token === historyRequestToken) {
      window.__augitHistoryPageError = String((error && error.message) || error);
    }
    return false;
  } finally {
    if (token === historyRequestToken) {
      live.historyLoadingMore = false;
      window.__augitHistoryLoadingMore = false;
    }
  }
}

/**
 * 历史列表滚动触底加载下一页。
 *
 * 只有**纵向向下**滚动才触发：横向滚动与无关按键不触发（规格 §7.8）。
 * 监听挂在 `.commit-list`（滚动容器）上，随区域刷新重建，因此每次渲染后重新绑定。
 */
function bindHistoryScroll() {
  const list = document.querySelector(".log-list-panel .commit-list");
  if (!list || list.__augitHistoryScrollBound) return;
  list.__augitHistoryScrollBound = true;
  let lastTop = list.scrollTop;
  list.addEventListener("scroll", () => {
    const live = window.__augitLive;
    if (!live) return;
    // 持续记录位置：区域刷新会重建列表，`restoreHistoryScroll()` 用它还原
    //（规格 §7.8「加载下一页时现有 100 条保持可见」与 §6.4 的滚动保持）。
    // 恢复窗口内的滚动事件**不是用户动作**：渲染之后 `applyTypography()` 还会按"改字号前捕获的锚点"
    // 回填一次 `.commit-list`（它捕获时列表刚重建、位置还是 0），第 239 轮实测这一步会把刚恢复好的
    // `scrollTop` 冲成 0，并顺着这个监听把状态也写成 0。窗口很短，窗口内的滚动一律不记录。
    if (pendingHistoryScroll) return;
    // 只在**该轴当前真的可滚动**时记录：渲染与"文件历史往返"期间会有一段列表还没有可滚高度的窗口，
    // 那时读到的 0 同样会冲掉上一次的真实位置。列表不可滚动时该轴本来就是 0，不记录不会丢信息。
    if (list.scrollHeight > list.clientHeight) live.historyScrollTop = list.scrollTop;
    // 横向位置同样要记：多轨窄栏下 `.commit-list` 会**局部横向滚动**（行宽 > 列表宽），
    // 区域刷新重建列表会把 `scrollLeft` 归零。规格 §7.8 第 22 条明确要求
    //「改变选择、重复点击和相同快照刷新保留横向位置」，因此它与 `scrollTop` 一起记、
    // 但**不随** `loadHistory()` 的重读清零（只有新列表上下文才归零纵向）。
    if (list.scrollWidth > list.clientWidth) live.historyScrollLeft = list.scrollLeft;
    const vertical = list.scrollTop > lastTop;
    lastTop = list.scrollTop;
    if (!vertical || !live.historyHasNextPage || live.historyLoadingMore) return;
    if (list.scrollTop + list.clientHeight < list.scrollHeight - 4) return;
    void loadHistoryPage(live.historyPage + 1);
  }, { passive: true });
}

/**
 * 渲染后还原历史列表的滚动位置（纵横向都由滚动监听持续记录）。
 *
 * 两组位置都直接赋给容器：**超出当前范围的赋值会被浏览器夹回**，这正好实现规格 §7.8
 * 「缩小内容范围时将超出部分归位」——例如去掉长作者名后行宽回落到列表宽，横向位置自动回到 0。
 */
/**
 * 待恢复的历史列表滚动位置。渲染之后**不止一条路径**会写 `.commit-list`：
 * `applyTypography()` 应用完字体会按"改前捕获的锚点"回填一次，而它捕获时列表刚重建、位置还是 0
 *（第 239 轮实测栈：`restoreScrollAnchors()` → `.commit-list.scrollTop = 0`），于是刚恢复好的位置又被冲掉。
 * 因此把"要恢复到哪里"留在本地变量里、跨越几帧再对齐，窗口内也不把滚动事件当成用户动作记录。
 */
let pendingHistoryScroll = null;
let pendingHistoryScrollTimer = 0;

/** 把待恢复的纵/横向位置写给当前列表；越界值由浏览器夹回（即"缩小内容范围时归位"）。 */
function applyPendingHistoryScroll() {
  if (!pendingHistoryScroll) return;
  const list = document.querySelector(".log-list-panel .commit-list");
  if (!list) return;
  if (typeof pendingHistoryScroll.top === "number") list.scrollTop = pendingHistoryScroll.top;
  if (typeof pendingHistoryScroll.left === "number") list.scrollLeft = pendingHistoryScroll.left;
}

function restoreHistoryScroll() {
  const live = window.__augitLive;
  if (!live) return;
  if (typeof live.historyScrollTop !== "number" && typeof live.historyScrollLeft !== "number") return;
  pendingHistoryScroll = { top: live.historyScrollTop, left: live.historyScrollLeft };
  applyPendingHistoryScroll();
  // 行宽/行高由 `bindHistoryLayout()` 的 `layout()` 在 ResizeObserver 回调里再确定一次，
  // 首次赋值可能被当时还不足的范围夹住 ⇒ 下一帧再对齐；
  // 字体与锚点回填更晚（`applyTypography()` 是异步的）⇒ 窗口结束时再对齐一次，
  // 之后才允许滚动事件重新记录位置（规格 §7.8 第 22 条：往返后恢复纵横滚动）。
  requestAnimationFrame(applyPendingHistoryScroll);
  window.clearTimeout(pendingHistoryScrollTimer);
  pendingHistoryScrollTimer = window.setTimeout(() => {
    applyPendingHistoryScroll();
    pendingHistoryScroll = null;
  }, 200);
}

/**
 * 读取指定文件的 Blame；失败只记录，不影响其它视图。
 *
 * `revision` 非空时按**该修订**标注（权威 `AnnotatePreviousRevisionAction` →
 * `AnnotateRevisionAction`：用 `PreviousFileRevisionProvider.getPreviousRevision(lineNumber)`
 * 拿到上一修订后重新标注，`GitFileAnnotation.java:482-501`）。
 */
async function loadBlame(path, revision = null) {
  // 连续选择不同文件时只接纳最后一次：旧响应不得覆盖新的归属结果。
  const token = ++detailViewToken;
  try {
    const blame = await invoke("git/blame", revision ? { path, revision } : { path }, 60000);
    if (token !== detailViewToken) return null;
    // 与 document/read 同样的契约校验：缺少身份的载荷不得进入状态，
    // 否则渲染层会拿到 path 为 undefined 的对象。
    if (!blame || !blame.available || !Array.isArray(blame.lines)) return null;
    if (typeof blame.path !== "string" || blame.path.length === 0) return null;
    const live = window.__augitLive;
    if (live) {
      live.blame = {
        path: blame.path,
        // 当前标注所依据的修订（空表示工作区）；「标注上一修订」后非空。
        revision: blame.revision || null,
        lines: blame.lines.map((line) => ({
          // 该行的上一修订（`git blame --line-porcelain` 的 `previous` 头，权威同源）；
          // 空串表示没有更早的修订，权威此时把该动作隐藏。
          previousRevision: line.previousRevision || "",
          number: line.number,
          hash: line.hash,
          fullHash: line.fullHash,
          author: line.author,
          // 槽位显示短日期；悬停提示的 `Date:` 要用日期时间（权威 `DateFormatUtil.formatDateTime`）。
          date: line.date,
          dateTime: line.dateTime,
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
  // 权威 `SearchEverywhereUI.rebuildList()`：每次搜索开始先清空上一个查询的结果，空态显示
  // "Searching…"，结果到达才替换 ⇒ 先落到"进行中"状态再发查询。
  live.search = { kind, query, options: options || {}, matches: [], notice: "", truncated: false, pending: true };
  renderSearchOverlay(kind);
  try {
    const result = await invoke(method, { query, ...(options || {}) }, 30000);
    // 晚到的旧查询不得覆盖新查询的结果。
    if (token !== searchToken) return;
    const matches = result && result.matches ? result.matches : [];
    const truncated = !!(result && result.truncated);
    live.search = {
      kind,
      query,
      options: options || {},
      matches,
      notice: result && result.notice ? result.notice : "",
      truncated,
      pending: false,
      // 权威 `UsageLimitUtil.USAGES_LIMIT`：到限先问一句 Continue／Abort
      // （`showTooManyUsagesWarning`，`:26-34`），选 Continue 才继续。
      limitPrompt: truncated && kind === "repository"
        ? { query, options: options || {}, offset: matches.length }
        : null,
    };
    window.__augitSearchReady = true;
    renderSearchOverlay(kind);
    // 到限按权威弹「结果过多」（Continue／Abort）。对话框是独立的覆盖层：
    // 搜索浮层的区域刷新不会连带把它重画，用户的答案由下面的两个函数显式落地。
    if (live.search.limitPrompt) openSearchLimitDialog();
    else closeSearchLimitDialog();
  } catch (error) {
    if (token !== searchToken) return;
    window.__augitError = "search:" + String(error && error.message || error);
  }
}

/**
 * 重绘搜索浮层（"进行中"状态或结果），但**不覆盖查询框里用户已经继续输入的内容**。
 * 权威的搜索框内容从不由结果回写；Augit 之前每次结果到达都整体重绘、把输入框重置成已发出的那个 query，
 * 慢查询返回时会把用户新敲的字吞掉（与历史面板"数据到达不得打断用户输入"同一类缺陷，第 153 轮改正）。
 */
function renderSearchOverlay(kind) {
  const field = document.querySelector(".search-overlay .search-field");
  const typed = field ? { value: field.value, start: field.selectionStart, end: field.selectionEnd } : null;
  refresh("overlay");
  // 覆盖层被替换后输入框是新的，需要重新绑定并恢复焦点与光标位置。
  bindSearchOverlay(kind);
  const next = document.querySelector(".search-overlay .search-field");
  if (next && typed) {
    next.value = typed.value;
    try { next.setSelectionRange(typed.start, typed.end); } catch { /* 不支持选择区的输入类型忽略 */ }
  }
}

/** 打开/关闭「结果过多」对话框（权威 `UsageLimitUtil.showTooManyUsagesWarning`）。 */
function openSearchLimitDialog() {
  const host = document.querySelector(".augit-window");
  if (!host || document.querySelector(".search-limit-window") || typeof searchLimitDialog !== "function") return;
  const template = document.createElement("template");
  template.innerHTML = searchLimitDialog();
  const layer = template.content.firstElementChild;
  if (!layer) return;
  host.appendChild(layer);
  // 默认按钮是「继续」（权威 `MessageDialogBuilder.okCancel` 的 OK）。
  const next = layer.querySelector('[data-search-limit-action="continue"]');
  if (next) next.focus({ preventScroll: true });
}

function closeSearchLimitDialog() {
  document.querySelectorAll(".search-limit-window").forEach((node) => node.remove());
}

/**
 * 「结果过多」里选**继续**：按分页把余下的结果取回来并追加。
 *
 * 权威选 Continue 后让同一次搜索继续跑完且**不再提示**（`UsageViewManagerImpl:334-357`）——
 * Augit 用 `offset` 分页表达"继续"，每页仍是 1000 条：既等价于继续跑完，
 * 又不让单次桥接响应超过 WebView2 可靠传输的规模。
 */
async function continueLimitedSearch() {
  const live = window.__augitLive;
  const prompt = live && live.search && live.search.limitPrompt;
  if (!live || !prompt) return null;
  const token = searchToken;
  live.search.limitPrompt = null;
  closeSearchLimitDialog();
  renderSearchOverlay("repository");
  let offset = prompt.offset || 0;
  // 安全上限：即使结果被持续追加，也不让一次"继续"无限循环（100 页 = 10 万条）。
  for (let page = 0; page < 100; page++) {
    let result = null;
    try {
      result = await invoke("search/text", {
        query: prompt.query,
        ...(prompt.options || {}),
        offset,
        limit: 1000,
      }, 30000);
    } catch (error) {
      window.__augitError = "search-continue:" + String((error && error.message) || error);
      return null;
    }
    // 用户又发起了新查询：晚到的分页结果不得落到新查询上。
    if (token !== searchToken) return null;
    const added = result && result.matches ? result.matches : [];
    const current = live.search;
    if (!current) return null;
    current.matches = current.matches.concat(added);
    current.truncated = !!(result && result.truncated);
    current.notice = result && result.notice ? result.notice : "";
    current.pending = false;
    offset += added.length;
    renderSearchOverlay("repository");
    if (!current.truncated || added.length === 0) break;
  }
  return live.search;
}

/** 「结果过多」里选**中止**：保留已有结果并说明是谁停止了搜索（权威 Abort 即取消搜索）。 */
function abortLimitedSearch() {
  const live = window.__augitLive;
  if (!live || !live.search) return null;
  const count = (live.search.matches || []).length;
  live.search.limitPrompt = null;
  live.search.truncated = false;
  live.search.notice = `已按你的选择中止继续搜索，当前显示前 ${count} 条。`;
  closeSearchLimitDialog();
  renderSearchOverlay("repository");
  return live.search;
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
// 同一时刻只允许一个在途的 terminal/read（见 pollTerminal）。
let terminalPolling = false;
// 终端启动代际（规格 §7.16）：关闭/切换时递增，使在途的 terminal/start 收尾失效 ——
// 晚到的启动不得接管界面，也不能把已经启动的 Shell 留在宿主里（要发 terminal/stop 释放）。
let terminalGeneration = 0;

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
/**
 * 终端配色：xterm 自绘在 canvas 上，CSS 规则管不到它；**不传 theme 时默认是纯黑背景**，
 * 与视觉稿的终端底色不一致（同引擎像素对照实测：实时 (0,0,0) vs 视觉稿 (30,31,34)）。
 *
 * 取色必须从**终端宿主自身的计算样式**读：根元素 :root 上挂的是浅色令牌
 * （实测 `--augit-panel` = `#ffffff`，深色令牌在 body[data-theme="dark"] 下），
 * 从 :root 读会把终端整成白底（第 286 轮踩过）。
 */
function terminalTheme() {
  const host = document.querySelector('.terminal-view') || document.body;
  const style = getComputedStyle(host);
  const background = style.backgroundColor;
  const foreground = style.color;
  return {
    background,
    foreground,
    cursor: foreground,
    // 选中底色沿用界面里的选中蓝（DOM 对照实测 rgb(47,70,111)）。
    selectionBackground: '#2F466F',
  };
}

function refreshTerminalTypography() {
  if (!terminalInstance) return;
  const typography = terminalTypography();
  terminalInstance.options.fontFamily = typography.family;
  terminalInstance.options.fontSize = typography.size;
  terminalInstance.options.theme = terminalTheme();
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
  // 启动期间的代际：`terminal/start` 是异步的（宿主真正拉起 Shell），期间用户可能关闭终端。
  // 晚到的成功若继续设置 ready/轮询，就把已关闭的终端"复活"了（规格 §7.16 第 3 条）。
  const generation = terminalGeneration;

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
    theme: terminalTheme(),
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
  // 权威 `TerminalEscapeKeyListener`（platform/execution-impl/src/com/intellij/terminal/
  // TerminalEscapeKeyListener.java:34-72）：在终端工具窗口里按 **`Esc`**（即
  // `Terminal.SwitchFocusToEditor` 的快捷键）会把焦点**交给编辑器组件**并 `consume()` 掉这个键
  // —— ESC 不送给 Shell。Augit 原来所有按键都经 onData 原样转发，Esc 会被送到 pty。
  // 只拦**不带修饰键**的 Esc（权威的 `isEscape`：`VK_ESCAPE && modifiersEx == 0`）；
  // Shift/Esc 这类组合仍是 Shell 的输入。没有可聚焦的正文时不拦（权威在拿不到工具窗口时也不处理）。
  terminalInstance.attachCustomKeyEventHandler((event) => {
    if (event.type !== 'keydown' || event.key !== 'Escape') return true;
    if (event.ctrlKey || event.altKey || event.shiftKey || event.metaKey) return true;
    return !focusEditorFromTerminal();
  });

  const started = await invoke('terminal/start', {
    columns: terminalInstance.cols,
    rows: terminalInstance.rows,
  }, 30000);
  if (generation !== terminalGeneration) {
    // 启动期间终端已被关闭：释放刚启动的 Shell，旧请求不得重新显示或接管界面。
    if (started && started.available) await invoke('terminal/stop', {}, 15000).catch(() => {});
    return null;
  }
  if (!started || !started.available) {
    window.__augitError = 'terminal-start:' + String(started && started.reason ? started.reason : 'unavailable');
    return null;
  }

  terminalReady = true;
  window.__augitTerminalShell = started.displayName;
  pollTerminal();
  return terminalInstance;
}

/**
 * 记录日志筛选框里**尚未执行**的输入（规格 §7.9：进入文件历史要保存"尚未执行的筛选输入"）。
 *
 * 草稿存进状态、由模板回填，而不是留在 DOM 里：底部工具窗口是**整块重绘**的
 * （进入/离开文件历史、刷新历史都会触发），输入框节点会被重建 —— 长度、选区与值一起丢。
 * 这与搜索浮层第 153 轮修过的"结果到达把输入框冲回旧值"是同一类缺陷。
 */
function bindLogFilterDraft() {
  const field = document.querySelector('.log-filterbar.history-filters [aria-label="文本或哈希"]');
  if (!field || field.dataset.logFilterDraftBound === "true") return;
  field.dataset.logFilterDraftBound = "true";
  field.addEventListener("input", () => {
    const live = window.__augitLive;
    if (live) live.historyFilterDraft = field.value;
  });
  // 权威 `VcsLogClassicFilterUi.TextFilterField`（`VcsLogClassicFilterUi.kt:228-266`）：
  // 输入框自己的 `ActionListener`（回车）执行筛选并入历史；`onFieldCleared()` 在清空时清掉筛选；
  // `onFocusLost()` 在文本与已应用的不一致时也执行一次（on-the-fly 关闭时即失焦执行）。
  field.addEventListener("keydown", (event) => {
    if (event.key !== "Enter") return;
    event.preventDefault();
    if (applyHistoryTextFilter(field.value)) void reloadHistoryKeepingFocus();
  });
  field.addEventListener("blur", () => {
    // 区域重绘会把输入框节点摘下；那不是"用户离开输入框"，不能在这里执行筛选
    // （第 160 轮的草稿断言正是靠"重绘后草稿仍在"成立的）。
    if (!field.isConnected) return;
    const live = window.__augitLive;
    const applied = live && live.historyFilter
      ? (live.historyFilter.hash || live.historyFilter.message || "")
      : "";
    if (String(field.value).trim() === applied) return;
    if (applyHistoryTextFilter(field.value)) void reloadHistoryKeepingFocus();
  });
}

/**
 * 文本/哈希输入成立为一组哈希前缀吗 —— 照权威 `VcsLogFilterObject.fromHash`
 * （`platform/vcs-log/impl/src/com/intellij/vcs/log/visible/filters/VcsLogFilters.kt:149-160`
 * ＋ `HashSeparatorCharFilter` 的 `,`／`;`／空白切词）：**每个**词都要匹配
 * `VcsLogUtil.GIT_HASH_REGEX` = `[a-fA-F0-9]{7,64}`
 * （`platform/vcs-log/impl/src/com/intellij/vcs/log/util/VcsLogUtil.java:92`），否则整串不是哈希。
 */
function isHistoryHashQuery(text) {
  const words = String(text || "").trim().split(/[\s,;]+/).filter(Boolean);
  return words.length > 0 && words.every((word) => /^[0-9a-fA-F]{7,64}$/.test(word));
}

/**
 * 应用"文本或哈希"筛选（权威 `TextFilterField.applyFilter()`／`textFilterModel.setFilterText()`
 * 的 `setFilter(collection(createTextFilter(text), VcsLogFilterObject.fromHash(text)))`，
 * `platform/vcs-log/impl/src/com/intellij/vcs/log/ui/filter/TextFilterModel.kt:96-103`）：
 * 空白 ⇒ 清掉文本与哈希筛选（`onFieldCleared()`）；否则**同时**挂上文本筛选与（像哈希时的）哈希筛选。
 *
 * 两者必须一起送：权威 `applyHashFilter()` 只在前缀**命中非空**时短路其它筛选，一条都没命中时
 * `return null` 落回普通筛选（`VcsLogFiltererImpl.kt:336-341`），此时文本筛选（`--grep`）才是结果来源。
 * **只动这两项**：`setFilterText` 只改文本筛选，分支/用户/日期都保留。
 */
function applyHistoryTextFilter(value) {
  const live = window.__augitLive;
  if (!live) return false;
  const text = String(value == null ? "" : value).trim();
  const next = { ...(live.historyFilter || {}) };
  delete next.hash;
  delete next.message;
  if (text) {
    next.message = text;
    if (isHistoryHashQuery(text)) next.hash = text;
  }
  live.historyFilterDraft = text;
  live.historyFilter = next;
  return true;
}

/**
 * 设置／清除「分支」筛选（权威 `VcsLogFilterUiEx.filterBy(branches)`，
 * `BranchesInGitLogUiFactoryProvider.kt:267-277`：先 `without(VcsLogBranchLikeFilter)` 再按选中集 `with(...)`，
 * 即**替换**同类筛选、不动其它筛选；选中集为空则清掉）。
 */
function setHistoryBranchFilter(branches) {
  const live = window.__augitLive;
  if (!live) return false;
  const next = { ...(live.historyFilter || {}) };
  const values = (Array.isArray(branches) ? branches : [branches])
    .map((value) => String(value || ""))
    .filter(Boolean);
  delete next.branch;
  delete next.branches;
  // 权威的筛选值是一组分支名（`fromBranches`）：单个仍走单值参数，多个走 `branches`（宿主两者等价）。
  if (values.length === 1) next.branch = values[0];
  else if (values.length > 1) next.branches = values;
  live.historyFilter = next;
  return true;
}

/**
 * 当前引用树的选中集（权威 `BranchesTreeSelection`：树是 `DISCONTIGUOUS_TREE_SELECTION`，可多选）。
 * 返回 `[{name, kind}]`，顺序与状态里保存的一致。
 */
function selectedRefs() {
  const live = window.__augitLive;
  return Array.isArray(live && live.logRefSelection)
    ? live.logRefSelection.filter((item) => item && item.name)
    : [];
}

/** 选中集对应的**行**，按 DOM 顺序（= 树的可见顺序）。 */
function selectedRefRows() {
  const selections = selectedRefs();
  return [...document.querySelectorAll(".log-ref-panel .tree-row[data-ref-name]")]
    .filter((row) => selections.some((item) => item.name === row.dataset.refName && item.kind === row.dataset.refKind));
}

/**
 * 把一组引用行翻成日志筛选的引用名（权威 `BranchesTreeSelection.selectedBranchFilters`）：
 * 分支行给分支名、HEAD 行给 `VcsLogUtil.HEAD` = "HEAD"、**标签给不出**（权威里该动作对标签不成立）。
 * 一个都给不出时返回空表。
 */
function branchFilterNamesOf(rows) {
  const names = [];
  for (const row of rows || []) {
    const kind = row.dataset.refKind;
    if (kind === "head") names.push("HEAD");
    else if (kind === "branch" || kind === "remote") names.push(row.dataset.refName);
  }
  return names;
}

/**
 * 读取历史里出现过的作者（权威 `VcsLogUserResolver`／`GitUserRegistry` 从日志收集用户），
 * 供「按用户筛选」的弹层列表；只在弹层打开时读一次并缓存。
 */
async function ensureHistoryAuthors() {
  const live = window.__augitLive;
  if (!live) return null;
  if (Array.isArray(live.historyAuthors)) return live.historyAuthors;
  live.historyAuthorsLoading = true;
  refreshAfterEvent("bottomTool");
  const result = await invoke("git/authors", {}, 30000).catch(() => null);
  const liveNow = window.__augitLive;
  if (!liveNow) return null;
  liveNow.historyAuthorsLoading = false;
  liveNow.historyAuthors = result && result.available && Array.isArray(result.authors) ? result.authors : [];
  refreshAfterEvent("bottomTool");
  return liveNow.historyAuthors;
}

/** 设置用户筛选（权威 `fromUserNames(values)`：一组用户；空集即清除）。 */
function setHistoryAuthorFilter(values) {
  const live = window.__augitLive;
  if (!live) return false;
  const next = { ...(live.historyFilter || {}) };
  delete next.authors;
  const list = (Array.isArray(values) ? values : [values]).map((v) => String(v || "")).filter(Boolean);
  if (list.length > 0) next.authors = list;
  live.historyFilter = next;
  return true;
}

/**
 * 设置／清除「路径」筛选（权威 `StructureFilterPopupComponent.setStructureFilter(...)`，
 * `platform/vcs-log/impl/src/com/intellij/vcs/log/ui/filter/StructureFilterPopupComponent.java:452-458`：
 * `myFilterModel.setFilter(VcsLogFilterObject.collection(newFilter))` 之后把**整组**值记进最近筛选）。
 * 空集即清除（权威 `EditPathsAction` 在多行文本里一个路径都不剩时 `myFilterModel.setFilter(null)`）。
 *
 * 路径写法沿用宿主与 Git 的相对路径：目录给不带尾斜杠的相对路径（`git log -- docs` 本身就按目录筛），
 * 用户手输里的 `\` 与首尾空白在这里归一。
 */
function setHistoryPathFilter(paths) {
  const live = window.__augitLive;
  if (!live) return false;
  const list = (Array.isArray(paths) ? paths : [paths])
    .map((value) => String(value || "").replace(/\\/g, "/").trim().replace(/^\.\//, "").replace(/\/+$/, ""))
    .filter(Boolean);
  const next = { ...(live.historyFilter || {}) };
  delete next.paths;
  delete next.path;
  if (list.length > 0) next.paths = [...new Set(list)];
  live.historyFilter = next;
  if (list.length > 0) rememberHistoryPathFilter(next.paths);
  return true;
}

/**
 * 「最近」路径筛选（权威 `MainVcsLogUiProperties.addRecentlyFilteredGroup("Paths", values)` →
 * `VcsLogProjectTabsProperties.addRecentGroup`，`platform/vcs-log/impl/src/com/intellij/vcs/log/impl/VcsLogProjectTabsProperties.kt:142-152`：
 * 相同的整组先去重再插到最前，上限 `RECENTLY_FILTERED_VALUES_LIMIT = 10`，同文件 `:139`）。
 *
 * **登记差异**：权威把它写进项目级设置（`RECENT_FILTERS`），Augit 只在本次会话内保留
 * —— 不新增 Augit 没有的持久化设置项。
 */
function rememberHistoryPathFilter(paths) {
  const live = window.__augitLive;
  if (!live) return;
  const key = (group) => group.slice().sort().join("\u0000");
  const group = paths.slice();
  const recent = (Array.isArray(live.historyPathRecent) ? live.historyPathRecent : [])
    .filter((item) => Array.isArray(item) && key(item) !== key(group));
  recent.unshift(group);
  live.historyPathRecent = recent.slice(0, 10);
}

/**
 * 「选择期间…」对话框（权威 `DateFilterPopupComponent.SelectAction` → `DateFilterComponent`
 * ＋ `DialogBuilder`，标题 `vcs.log.date.filter.select.period.dialog.title` = "Select Period"）：
 * 起始／结束两个日期字段；确定后走 `VcsLogFilterObject.fromDates(after, before)`，
 * **两端都空则不设筛选**（权威 `if (after != null || before != null)`）。
 */
function openHistoryDateRangeDialog() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  closeLiveOverlay();
  rememberDialogFocus();
  const filter = live.historyFilter || {};
  const day = (value) => {
    const date = value ? new Date(value) : null;
    if (!date || Number.isNaN(date.getTime())) return "";
    const pad = (number) => String(number).padStart(2, "0");
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  };
  const body = `<div class="form-grid">`
    + `<label for="history-date-since">起始</label>`
    + `<input id="history-date-since" type="date" class="text-field" data-history-date-field="since" value="${day(filter.since)}">`
    + `<label for="history-date-until">结束</label>`
    + `<input id="history-date-until" type="date" class="text-field" data-history-date-field="until" value="${day(filter.until)}">`
    + `</div>`;
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay history-date-range-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "选择期间",
    body,
    `<button type="button" class="secondary-button" data-history-date-range="cancel">取消</button>`
      + `<button type="button" class="primary-button" data-history-date-range="confirm">确定</button>`,
    false,
    "history-date-range-dialog");
  host.appendChild(layer);
  const first = layer.querySelector('[data-history-date-field="since"]');
  if (first) first.focus({ preventScroll: true });
}

/**
 * 「选择…」对话框（权威 `StructureFilterPopupComponent.EditPathsAction` →
 * `MultilinePopupBuilder(project, oldValue, {'\n'})`，同文件 `:404-441`）：
 * 一个多行文本框，初始值是当前筛选路径按 `\n` 连接；弹层底部的提示文本是
 * `vcs.log.filter.popup.advertisement.with.key.text` = "Select one or more values separated with {0},
 * use {1} to finish"（{0} = `….text.new.lines` = "new lines"，{1} = `Ctrl+Enter`），
 * 输入按 `\n` 切分、逐项 trim、丢空行，`Ctrl+Enter` 以 OK 关闭并应用（`popup.closeOk(...)` ⇒ `event.isOk()`）；
 * 一个路径都不剩时清掉筛选（`myFilterModel.setFilter(null)`）。
 *
 * **登记差异**：权威是**无标题**弹层（提示文本在弹层底部）、只有 `Ctrl+Enter` 一个收尾手势；
 * Augit 用带标题的对话框，并保留「确定／取消」按钮（键盘仍支持 `Ctrl+Enter`）。
 */
function openHistoryPathTextDialog() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  closeLiveOverlay();
  rememberDialogFocus();
  const current = ((live.historyFilter || {}).paths) || [];
  const body = `<div class="form-grid">`
    + `<label for="history-path-text">路径</label>`
    + `<textarea id="history-path-text" class="text-field history-path-text" rows="6" aria-label="路径" data-log-path-field>${escapeHtml(current.join("\n"))}</textarea>`
    + `</div>`;
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay history-path-text-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "按路径筛选",
    body,
    `<button type="button" class="secondary-button" data-log-path-text="cancel">取消</button>`
      + `<button type="button" class="primary-button" data-log-path-text="confirm">确定</button>`,
    false,
    "history-path-text-dialog");
  host.appendChild(layer);
  // 提示文本复用对话框底栏的空位（`.footer-help`），不新增视觉语言。
  const help = layer.querySelector(".dialog-footer .footer-help");
  if (help) help.textContent = "每行一个值，Ctrl+Enter 完成。";
  const field = layer.querySelector("[data-log-path-field]");
  if (field) {
    // 权威的收尾手势就是 `Ctrl+Enter`（`MultilinePopupBuilder` 给 `okAction` 注册的
    // `CommonShortcuts.getCtrlEnter()`）；这里同样生效，按钮只是 Augit 多给的一条路。
    field.addEventListener("keydown", (event) => {
      if (event.key !== "Enter" || !(event.ctrlKey || event.metaKey)) return;
      event.preventDefault();
      const confirm = layer.querySelector('[data-log-path-text="confirm"]');
      if (confirm) confirm.click();
    });
    field.focus({ preventScroll: true });
  }
}

/**
 * 「在树中选择…」对话框（权威 `StructureFilterPopupComponent.SelectPathsInTreeAction` →
 * `VcsStructureChooser(project, vcs.log.select.folder.dialog.title, files, roots)`，同文件 `:364-390`；
 * `platform/vcs-log/impl/src/com/intellij/vcs/log/ui/filter/VcsStructureChooser.java:77-140`）：
 * 标题 = "Select Paths to Filter by"，一棵带复选框的树，勾选结果进 `VcsLogFilterObject.fromVirtualFiles(...)`；
 * 底部标签是 `vcs.log.filters.structure.label` = "Selected: {0}"，**一个都没勾时确定按钮禁用**
 * （`setOKActionEnabled(!mySelectedFiles.isEmpty())`，同文件 `:136`）。
 *
 * **登记差异**：权威的树是模块文件系统的完整 `CheckboxTree`（带速搜与 `MAX_FOLDERS` 上限提示，
 * 同文件 `:231-234`）；Augit 用**已经加载**的项目树（`live.tree`，懒加载 ⇒ 未展开过的目录不在候选里）。
 */
function openHistoryPathTreeDialog() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  closeLiveOverlay();
  rememberDialogFocus();
  const current = new Set((((live.historyFilter || {}).paths) || []).map(String));
  const rows = (Array.isArray(live.tree) ? live.tree : []).filter((entry) => entry && entry.path && entry.depth > 0);
  const row = (entry) => {
    const checked = current.has(entry.path);
    return `<a class="menu-item" href="#" role="menuitemcheckbox" aria-checked="${checked ? "true" : "false"}"`
      + ` data-log-path-row="${escapeHtml(entry.path)}" style="padding-left:${8 + entry.depth * 14}px">`
      + `<span class="fake-check${checked ? " checked" : ""}"></span> ${escapeHtml(entry.name)}</a>`;
  };
  const body = `<div class="form-grid">`
    + `<label>路径</label>`
    + `<div id="history-path-tree-list" role="group" aria-label="选择要筛选的路径" data-log-path-list>`
    + (rows.length === 0 ? `<span class="menu-item disabled" aria-disabled="true">项目树里还没有可选的路径</span>` : rows.map(row).join(""))
    + `</div>`
    + `</div>`;
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay history-path-tree-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "选择要筛选的路径",
    body,
    `<button type="button" class="secondary-button" data-log-path-tree="cancel">取消</button>`
      + `<button type="button" class="primary-button" data-log-path-tree="confirm"${current.size === 0 ? " disabled aria-disabled=\"true\"" : ""}>确定</button>`,
    false,
    "history-path-tree-dialog");
  host.appendChild(layer);
  // 底部标签就是权威 `vcs.log.filters.structure.label` = "Selected: {0}"，复用底栏空位。
  const help = layer.querySelector(".dialog-footer .footer-help");
  if (help) help.textContent = `已选择: ${current.size}`;
  const first = layer.querySelector("[data-log-path-row]");
  if (first) first.focus({ preventScroll: true });
}

/**
 * 设置／清除日期筛选（权威 `VcsLogFilterObject.fromDates(after, before)`：
 * `DateFilterPopupComponent` 的两个预设都只是 `fromDates(since, null)`）。
 */
function setHistoryDateFilter(since, until) {
  const live = window.__augitLive;
  if (!live) return false;
  const next = { ...(live.historyFilter || {}) };
  delete next.since;
  delete next.until;
  if (since) next.since = since;
  if (until) next.until = until;
  live.historyFilter = next;
  return true;
}

/** 清除全部日志筛选（权威空态的 `vcs.log.reset.filters.status.action` = "Reset filters"）。 */
function resetHistoryFilters() {
  const live = window.__augitLive;
  if (!live) return false;
  live.historyFilter = {};
  live.historyFilterDraft = "";
  return true;
}

/**
 * 按引用树的行设置分支筛选（权威 `UpdateBranchFilterInLogAction` ＋
 * `BranchesTreeSelection.selectedBranchFilters`，`BranchesTreeSelection.kt:34-41`）：
 * 本地/远程分支取分支名，HEAD 节点取 `VcsLogUtil.HEAD`，**标签与分组不产生筛选** ⇒ 双击标签是空操作。
 */
function filterLogToRefRow(row) {
  return filterLogToRefRows(row ? [row] : []);
}

/**
 * 把**一整组**引用行筛选到日志（权威 `UpdateBranchFilterInLogAction` →
 * `BranchesTreeSelection.selectedBranchFilters`：整个选中集一起进去，`fromBranches` 是并集）。
 * 选中集里全是标签时给不出筛选值 ⇒ 返回 false（权威里该动作此刻禁用）。
 */
function filterLogToRefRows(rows) {
  const names = branchFilterNamesOf(rows);
  if (names.length === 0) return false;
  return setHistoryBranchFilter(names);
}

/**
 * 重新读历史并重绘底部区域，同时把焦点交回**触发它的那个元素**。
 *
 * 底部区域是整块重绘的（`__augitRenderRegions` 不保留焦点），而权威在应用筛选后
 * 光标仍在筛选框／引用行上（`TextFilterField` 与树都不重建）。这里按"身份"恢复：
 * 引用行按 `data-ref-kind` + `data-ref-name`，筛选框按 `aria-label`。
 */
async function reloadHistoryKeepingFocus() {
  const active = document.activeElement;
  const restore = active && active.getAttribute
    ? {
      label: active.getAttribute("aria-label"),
      kind: active.dataset ? active.dataset.refKind : null,
      name: active.dataset ? active.dataset.refName : null,
    }
    : null;
  await loadHistory().catch(() => null);
  refresh("bottomTool");
  if (!restore) return;
  let target = null;
  if (restore.name) {
    target = document.querySelector(
      `.log-ref-panel .tree-row[data-ref-kind="${restore.kind}"][data-ref-name="${CSS.escape(restore.name)}"]`);
  } else if (restore.label === "文本或哈希") {
    target = document.querySelector('.log-filterbar.history-filters [aria-label="文本或哈希"]');
  }
  if (target && typeof target.focus === "function") target.focus({ preventScroll: true });
}

/**
 * 把焦点从终端交回编辑器正文（权威 `TerminalEscapeKeyListener` 的
 * `ToolWindowManager.activateEditorComponent()`）。
 *
 * 返回是否真的找到了可聚焦的正文：找不到时不消费按键，让 Esc 照常送给 Shell
 * （对应权威在 `toolWindow == null` 时不处理的守卫）。
 */
function focusEditorFromTerminal() {
  const target = document.querySelector(
    '.editor-content .code-view, .editor-content .markdown-source, .editor-content [tabindex="0"]');
  if (!target || typeof target.focus !== 'function') return false;
  target.focus({ preventScroll: true });
  return true;
}

/** 增量拉取终端输出；会话结束后停止轮询。 */
function pollTerminal() {
  if (terminalTimer !== 0) return;
  terminalTimer = window.setInterval(async () => {
    if (!terminalInstance) return;
    // 上一轮还没回来就跳过这一轮：60ms 的间隔短于一次桥接往返时，
    // 旧写法会不停堆积在途请求（实测每个 tick 都新发一个），把渲染进程和
    // UI 线程一起拖住，表现为"点开终端像卡死"。
    if (terminalPolling) return;
    terminalPolling = true;
    try {
      // 分批读：每轮最多 128 KB。真机实测一次搬最多 4 MB 的积压会让大输出后的终端
      // **永久停止更新**（§3.2 第 15 条）；宿主按返回的 offset 连续轮询即可追平。
      const chunk = await invoke('terminal/read', { offset: terminalOffset, maximumLength: 131072 }, 10000);
      if (chunk && typeof chunk.data === 'string' && chunk.data.length > 0) {
        terminalInstance.write(chunk.data);
      }
      if (chunk && typeof chunk.offset === 'number') terminalOffset = chunk.offset;
      window.__augitTerminalReadError = null;
      window.__augitTerminalBacklog = chunk && typeof chunk.pending === 'number' ? chunk.pending : 0;
      window.__augitTerminalNotifyError = chunk && chunk.notifyError ? chunk.notifyError : null;
      window.__augitTerminalOutputEnded = !!(chunk && chunk.outputEnded === true);
      if (chunk && (chunk.exited || !chunk.running)) {
        window.clearInterval(terminalTimer);
        terminalTimer = 0;
        window.__augitTerminalExited = true;
      }
    } catch (error) {
      // 单次读取失败不终止轮询，下次重试；但**必须留下痕迹** —— 此前这里静默吞掉，
      // 真机大输出冻结时 __augitError 一直是 null，用户与探针都看不到原因。
      window.__augitTerminalReadError = String(error && error.message || error);
    } finally {
      terminalPolling = false;
    }
  }, 60);
}

/**
 * 终端面板出现时确保会话已经建好（规格 §7.16「按需单会话」）。
 *
 * 此前 `startTerminal()` 只在 `--scene terminal` 启动路径上调用，
 * 运行时点左下角终端入口只会渲染出一个空的 `.terminal-view`：
 * 没有 xterm、没有会话、输入无回显（用户实测"点开卡死"）。
 */
async function ensureTerminal() {
  const host = document.querySelector('.terminal-view');
  if (!host) return null;
  if (terminalInstance && terminalReady) {
    reattachTerminal();
    return terminalInstance;
  }
  const instance = await startTerminal();
  reattachTerminal();
  if (instance) window.__augitTerminalReady = true;
  return instance;
}

/** 结束会话并停止轮询。 */
async function stopTerminal() {
  if (terminalTimer !== 0) {
    window.clearInterval(terminalTimer);
    terminalTimer = 0;
  }
  terminalReady = false;
  terminalPolling = false;
  await invoke('terminal/stop', {}, 15000).catch(() => {});
}

/** 关闭终端：先问宿主有没有前台命令，有则先确认（规格 §7.16）。 */
async function requestCloseTerminal() {
  const status = await invoke('terminal/status', {}, 8000).catch(() => null);
  if (status && status.foreground) {
    openTerminalCloseDialog();
    return;
  }
  await closeTerminalNow();
}

/** 真正结束会话并收起底部工具窗口。 */
async function closeTerminalNow() {
  // 先推进代际：在途的 `terminal/start` 收尾据此作废（规格 §7.16 第 3 条）。
  terminalGeneration += 1;
  await stopTerminal();
  const live = window.__augitLive;
  if (terminalInstance) {
    try { terminalInstance.dispose(); } catch { /* 已销毁时忽略 */ }
    terminalInstance = null;
  }
  terminalOffset = 0;
  window.__augitTerminalExited = false;
  window.__augitTerminalReady = false;
  if (live && live.layout && live.layout.bottom === 'terminal') {
    live.layout.userDriven = true;
    live.layout.bottom = '';
    live.layout.collapsed = null;
    // 关闭后入口必须回到侧栏那个入口：否则 activeRail 仍是"终端"，
    // 再次点击会被 applyRailAction 当成"折叠已激活入口"，面板永远打不开
    // （实测：关闭后点入口得到 bottom='' 且 collapsed='bottom'）。
    live.layout.activeRail = RAIL_SIDE.includes(live.layout.side) ? live.layout.side : 'project';
    window.__augitRender();
    rebindAfterRender();
  }
}

/** 「关闭终端」确认窗口（视觉稿 terminal-close 页的对话框，规格 §7.16）。 */
function openTerminalCloseDialog() {
  const host = document.querySelector('.augit-window');
  if (!host) return;
  closeLiveOverlay();
  rememberDialogFocus();
  const layer = document.createElement('div');
  layer.className = 'overlay-layer live-overlay terminal-close-window';
  layer.setAttribute('data-augit-overlay', '');
  layer.innerHTML = dialog(
    '关闭终端',
    `<div class="info-block" style="width:auto;text-align:left"><h2>终端中仍有命令正在运行</h2><p>继续将结束前台命令、Shell 及其整个子进程树。</p></div>`,
    `<button type="button" class="secondary-button" data-terminal-close="cancel">保留终端</button>`
      + `<button type="button" class="danger-button" data-terminal-close="confirm">结束命令并关闭</button>`,
    false,
    'terminal-close-dialog');
  host.appendChild(layer);
  const confirm = layer.querySelector('[data-terminal-close="confirm"]');
  if (confirm) confirm.focus();

  layer.addEventListener('click', (event) => {
    const action = event.target.closest && event.target.closest('[data-terminal-close]');
    if (!action) return;
    event.preventDefault();
    if (action.dataset.terminalClose === 'confirm') {
      closeLiveOverlay();
      void closeTerminalNow();
    } else {
      closeLiveOverlay();
    }
  });
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
function diffRequestKey({ path, revision, commit, ignoreWhitespace, detectRenames, version }) {
  const live = window.__augitLive;
  return [
    live ? live.root : "",
    path,
    revision || "工作区",
    commit || "",
    ignoreWhitespace ? "ignore-ws" : "keep-ws",
    detectRenames ? "renames" : "no-renames",
    version === undefined || version === null ? "" : String(version),
  ].join("\u0000");
}

/**
 * 补丁键：与请求键**同口径**（单双栏共用同一份补丁）。
 *
 * 显示模式本来就不该进这两个键：规格 §6.3 要求"查询期间切换显示模式只改变最终呈现方式"——
 * 同一份内容无论单栏还是双栏都是**同一次查询**（单双栏只决定排版）。此前 mode 进了请求键，
 * 于是加载期间切模式会并发发出第二次请求、补丁缓存也按模式各存一份（第 265 轮实测）。
 */
function diffPatchKey(parts) {
  return diffRequestKey(parts);
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
    // 「忽略空白」是**会话选项**（规格 §7.7 第 5 条的工具条开关）：调用方没显式传时沿用当前状态，
    // 否则切显示模式、切相邻文件、外部刷新这三条重载路径都会把它悄悄丢掉（状态在 live.diffOptions）。
    ignoreWhitespace: options.ignoreWhitespace === undefined
      ? !!(live0 && live0.diffOptions && live0.diffOptions.ignoreWhitespace)
      : !!options.ignoreWhitespace,
    detectRenames: !!options.detectRenames,
    version: options.version,
  };
  // 重新加载（含切显示模式、切相邻文件、外部重载）同样撤销"待跨文件"状态（规格 §7.7 第 10 条）。
  clearDiffBoundaryHint();
  const requestKey = diffRequestKey(parts);
  // 记住"当前正文"的完整请求上下文：切换显示模式要**只重新排版**（规格 §6.3），
  // 必须复用同一次请求的 revision／commit／ignoreWhitespace／version，否则历史比较与引用比较
  // 切单双栏会退化成"这条路径的工作区差异"（第 249 轮实测：历史比较切单栏后 `live.diff` 变成 null、
  // 编辑区退回视觉稿样例数据，且再切回双栏也回不来）。
  if (live0) live0.diffParts = parts;
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
      // **不写 `live.diffMode`**：显示模式是用户状态，不是响应的属性。加载期间切模式时，
      // 响应里带的还是发起时的模式，写回就会把用户最后的选择吞掉（第 265 轮实测：
      // 加载中切到单栏，响应落地后又变回双栏）。调用方（`switchDiffMode()`）自己设这个键。
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
        // 同上：不把响应里记的模式写回 `live.diffMode`（规格 §6.3"查询期间切换显示模式只改变最终呈现方式"）。
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
  if (live) live.referenceComparison = null;
  diffPatches.clear();
  diffRequests.clear();
  // 使在途请求的结果失效：令牌前进后，旧结果不会再写回。
  diffToken += 1;
  // 正文被释放了，加载指示也必须一起收掉：关闭后若请求仍在途中，
  // 收尾逻辑会因为令牌失效而提前返回，再不在这里清就会留下一个永远的"正在加载"。
  clearDiffLoadingMarker();
  // 关闭 Diff 同样撤销"待跨文件"状态（规格 §7.7 第 10 条）。
  clearDiffBoundaryHint();
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
  // 显示模式是**排版维度**：带上当前正文的请求上下文，单双栏因此命中同一份补丁缓存
  // （`diffPatchKey` 不含 mode）——既不重新查 Git，也不会把历史/引用比较降级成工作区差异。
  const diff = await loadDiff(path, { ...(live.diffParts || {}), mode });
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
  // 选择文件撤销"待跨文件"状态（规格 §7.7 第 10 条）。
  clearDiffBoundaryHint();
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
          // 类名必须与 `liveChangeFileRow()` 的初始渲染一致：`file-status-<小写 kind>`。
          // 这里曾经写的是 `live-file-status-*` —— mockup.css 里**没有任何规则**匹配它
          //（`03-editor-tabs` 那轮也为同一类名问题留下过注释），于是外部更新走增量补丁之后
          // 文件名会**丢掉 Git 状态色**（第 245 轮实测：补丁后 `file-status-*` 整个消失）。
          const wanted = `tree-name file-status-${String(file.kind).toLowerCase()}`;
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

  // 提交框的 `.commit-last` 槽位是设计基线的**「上一次提交」入口**
  //（`mockup.js` 的 `changesSide()`：`<a href="git-history.html" title="上一次提交">上一次提交</a>` ＋ 历史图标），
  // 不是分支名——第 247 轮实测：这里原先写 `live.branch`，既没有图标也没有 `title`，
  // 到 `icon-only` 档（装不下文字时）干脆剩一个**空槽**。分支名由状态栏承担。

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

// 比较标签的取消代际（规格 §5.2）：关闭比较后，在途请求的收尾必须失效。
// 只靠 diffToken 不够：diffToken 管的是"谁可以写正文"，而调用方在 await 之后
// 还会同步标题、激活标签、刷新区域——被关闭的标签会因此被重新激活，
// 活动标签指向一个已经不存在的 id（工作区 Diff、历史比较、引用比较三条调用方都要查）。
let comparisonGeneration = 0;

/** 这次比较请求的收尾是否仍然有效：标签还在标签栏里，且期间没有关闭过比较。 */
function comparisonStillCurrent(tab, generation) {
  const live = window.__augitLive;
  return generation === comparisonGeneration && !!(live && live.tabs && tab && live.tabs.includes(tab));
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
  // 打开改动行是**比较**视图（规格 §489：比较工具条没有文件导航）⇒ 清掉工作区 Diff 标记。
  live.workspaceDiff = false;
  const tab = ensureComparisonTab(path);
  if (activate) activateComparisonTab(tab);
  const generation = comparisonGeneration;
  scheduleDiffLoadingMarker();
  // 标签必须**立即**出现在标签栏里（规格 §5.2「打开并激活」、§7.9「激活时立即打开并显示
  // 双方引用及文件路径，查询完成后只填充正文」）。此前只在查询完成后刷新，
  // 整个加载窗口内标签栏上什么都没有——用户既看不到这次比较，也点不到关闭叉取消它。
  refreshAfterEvent("editorTabs", "statusbar");
  let succeeded = false;
  let stale = false;
  try {
    const diff = await loadDiff(path);
    // 关闭叉可能发生在请求中途：此时不得再同步标题、激活标签或刷新区域。
    stale = !comparisonStillCurrent(tab, generation);
    if (stale || !diff) return;
    succeeded = true;
    // 规格 §7.8：重试**成功后必须清掉上一次的失败状态与说明**，否则"可重试"重试成功了界面上仍写着失败
    //（第 131 轮实测：`afterRetry` 里 diff 已装载，但 `diffError` 与说明还在）。
    if (live) live.diffError = null;
    renderDiffErrorNotice();
    // 复用同一个标签时同步文字，否则标签会一直显示第一次打开的文件名。
    syncComparisonTab(tab, path, `提交: ${diff.name || path}`);
    if (activate) activateComparisonTab(tab);

    // 规格 §12.2 要求已有 Diff 标签时「只更新该标签正文」，不得刷新改动列表、
    // 复选框、提交信息与列表滚动；同时延后到事件派发结束再替换编辑区，
    // 使视觉稿挂在冒泡阶段的「双击建比较标签」监听能收到事件。
    refreshAfterEvent("editorContent", "editorTabs", "statusbar");
  } finally {
    // 已经失效的收尾不能清加载提示：那会把后来那次请求的提示一起清掉。
    if (!stale) clearDiffLoadingMarker();
    // 规格 §7.8 要求"失败与取消**保留标签可重试**"，与此处行为**相反**（见 §3.2 #35）。
    // 第 128/129 两轮都**没能触发真实的 diff 失败**（注入的失败旋钮没被应用采用，原因待查），
    // 因此按纪律**不发布未验证的行为改动**，维持原行为。
    // 规格 §7.8：「**失败与取消保留标签可重试**」——第 131 轮按规格改为**保留标签**并给出失败说明，
    // 并在 `diff-boundary`（工作区 Diff）场景用 `window.__failAllDiffs` **稳定触发**后验证。
    if (!stale && !succeeded) {
      const liveNow = window.__augitLive;
      if (liveNow) {
        liveNow.diffLoading = false;
        liveNow.diffError = { path, reason: window.__augitError || "读取差异失败" };
      }
      renderDiffErrorNotice();
      refreshAfterEvent("editorContent", "editorTabs", "statusbar");
    }
  }
}

/**
 * 失败的比较**保留标签**后的说明与重试提示（规格 §7.8）。挂在 `rebindAfterRender()` 上重贴：
 * 说明所在区域会被后续刷新重绘（第 128 轮两次直接注入都被覆盖）。
 */
function renderDiffErrorNotice() {
  const live = window.__augitLive;
  const host = document.querySelector(".editor-area .editor-content, .editor-area");
  if (!host) return;
  const existing = document.querySelector("[data-augit-diff-error]");
  if (!live || !live.diffError) {
    if (existing) existing.remove();
    return;
  }
  if (existing) return;
  const notice = document.createElement("div");
  notice.className = "comparison-notice diff-status-notice";
  notice.setAttribute("role", "status");
  notice.setAttribute("data-augit-diff-error", String(live.diffError.path || ""));
  notice.textContent = "无法读取 " + String(live.diffError.path || "")
    + " 的差异：" + String(live.diffError.reason || "读取差异失败") + "。可再次双击该文件重试。";
  host.insertBefore(notice, host.firstChild);
}

/** 取得或建立唯一的比较标签；已存在则复用。 */
function ensureComparisonTab(path, title, parts) {
  const live = window.__augitLive;
  live.tabs ??= [];
  // 标题可覆盖：工作区 Diff 用默认的「提交: 路径」，历史比较传入双方引用。
  // 复用分支也同步标题与目标——否则标签会一直显示上一个比较的名字。
  // `parts` 是标签的**三部分结构化标签**（文件名 / 来源 / 目标）：实时标签条按它渲染三个独立省略的 span
  //（规格 §7.9 第十七条），没有它才退回整串文本。
  const resolvedTitle = title || `提交: ${path}`;
  const existing = findComparisonTab();
  if (existing) {
    syncComparisonTab(existing, path, resolvedTitle);
    existing.comparison = parts || null;
    return existing;
  }

  const tab = {
    id: nextTabId(),
    kind: "comparison",
    path,
    title: resolvedTitle,
    comparison: parts || null,
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
/**
 * 比较标签的三部分（规格 §7.9 第十七条）：**先文件名，再来源与目标引用**，三部分各自独立省略。
 *
 * 展示值：完整 40/64 位提交哈希只显示前 8 位、保留 `^`/`~` 祖先后缀；命名引用保持原名。
 * 完整值另存在部件的 `title` 上（悬停说明），实际 Git 查询始终用完整原值（调用方传的就是完整值）。
 */
function comparisonParts(path, source, target) {
  const name = String(path || "").split("/").at(-1) || "选择文件";
  return { file: `比较: ${name}`, source: String(source || ""), target: String(target || "") };
}

/** 提交/引用的展示形式：完整哈希截前 8 位并保留祖先后缀；命名引用保持原名。 */
function shortReference(value) {
  const text = String(value || "");
  if (!/^[0-9a-fA-F]{40}([~^].*)?$/.test(text) && !/^[0-9a-fA-F]{64}([~^].*)?$/.test(text)) return text;
  const suffix = /([~^].*)$/.exec(text);
  return text.slice(0, 8) + (suffix ? suffix[1] : "");
}

function historyComparisonLabel(path, hash) {
  const parts = comparisonParts(path, `${shortReference(hash)}^`, shortReference(hash));
  return `${parts.file} · ${parts.source} → ${parts.target}`;
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

/** 文件历史列表（底部的"历史: <路径>"工具窗）里的提交行。 */
function fileHistoryRows() {
  return [...document.querySelectorAll(".history-rows .history-row[data-history-full]")];
}

/** 当前选中的文件历史提交（优先完整哈希）。 */
function selectedFileHistoryCommit() {
  const live = window.__augitLive;
  const fileHistory = live && live.fileHistory;
  if (!fileHistory || !Array.isArray(fileHistory.commits)) return null;
  const full = fileHistory.selectedFull || (fileHistory.commits[0] && fileHistory.commits[0].fullHash) || null;
  return fileHistory.commits.find((commit) => commit.fullHash === full) || null;
}

/**
 * 把文件历史列表的选择落到 DOM（行选中态 + 右侧详情）。
 *
 * 只做定点更新、不整页重绘：列表与右侧详情在同一个工具窗里，重绘会丢掉行的焦点。
 * 同一提交重复选择时**不重建详情节点**（规格 §6.3「相同快照不重新排版」的同一口径），
 * 因此节点身份与阅读位置都保持。
 */
function syncFileHistorySelection(commit, options = {}) {
  if (!commit) return;
  const rows = fileHistoryRows();
  for (const row of rows) {
    const active = row.dataset.historyFull === commit.fullHash;
    row.classList.toggle("selected", active);
    row.setAttribute("aria-selected", active ? "true" : "false");
  }
  const detail = document.querySelector("[data-live-file-history-detail]");
  if (detail && detail.dataset.historyDetail !== commit.fullHash) {
    detail.dataset.historyDetail = commit.fullHash;
    detail.innerHTML = `<h3>${escapeText(commit.subject)}</h3>`
      + `<div>${escapeText(commit.hash)} · ${escapeText(commit.author)} · ${escapeText(commit.date)}</div>`;
  }
  if (options.focus === true) {
    const row = rows.find((item) => item.dataset.historyFull === commit.fullHash);
    if (row) {
      row.focus({ preventScroll: true });
      row.scrollIntoView({ block: "nearest" });
    }
  }
}

/**
 * 选中文件历史里的某条提交（规格 §7.9 第二条「选择…继续使用同一提交身份」）。
 *
 * 选择进 `live.fileHistory.selectedFull`：区域重绘由 `liveFileHistoryTool()` 按状态回填，
 * 因此重建后仍选中同一条提交；返回 `true` 表示**换选**（同一提交重复选择返回 false，
 * 调用方可据此跳过重建）。
 */
function selectFileHistoryCommit(fullHash, options = {}) {
  const live = window.__augitLive;
  const fileHistory = live && live.fileHistory;
  if (!fileHistory || !Array.isArray(fileHistory.commits) || !fullHash) return false;
  const commit = fileHistory.commits.find((item) => item.fullHash === fullHash) || null;
  if (!commit) return false;
  const changed = fileHistory.selectedFull !== commit.fullHash;
  fileHistory.selectedFull = commit.fullHash;
  syncFileHistorySelection(commit, options);
  // 换选提交立即取消旧预览并装载新的（规格 §7.9 第五条）：同一提交重复选择时
  // `loadFileHistoryPreview()` 命中"相同快照"分支，不重查也不重写正文。
  void loadFileHistoryPreview({ commit: commit.fullHash });
  return changed;
}

// ---- 文件历史预览（规格 §7.9 第五条/第六条）----
// 右侧的只读比较视图由"提交 + 路径"驱动：请求按内容维度复用（同一项重复选择不重复查询），
// 换选提交立即取消旧预览（令牌 + 请求键双保险，晚到成功或失败都不回写）。
const fileHistoryPreviewRequests = new Map();  // 内容键 -> 进行中的查询（同一项重复选择复用同一次请求）
const fileHistoryPreviewPatches = new Map();   // 内容键 -> 已完成的补丁（**不含**显示模式 ⇒ 单双栏共用）
let fileHistoryPreviewToken = 0;
// 预览的**会话选项**（显示模式、忽略空白）与"当前这一份正文"分开保存：未完成的正文被丢弃后，
// 重显时的补查仍沿用用户选的模式与选项（规格 §7.9 第七条"保留显示模式"）。
let fileHistoryPreviewOptions = { mode: "split", ignoreWhitespace: false };

/** 预览的内容键：路径 + 提交 + 忽略空白（显示模式不参与 —— 它只是排版维度，规格 §6.3）。 */
function fileHistoryPreviewKey(parts) {
  return [parts.path, parts.commit, parts.ignoreWhitespace ? "ws" : "nows"].join("|");
}

/** 释放预览：取消未完成查询并丢掉已完成的补丁（清除文件历史、折叠工具窗口、销毁窗口）。 */
function releaseFileHistoryPreview() {
  const live = window.__augitLive;
  fileHistoryPreviewToken += 1;
  fileHistoryPreviewRequests.clear();
  fileHistoryPreviewPatches.clear();
  if (live) {
    live.fileHistoryPreview = null;
    live.fileHistoryPreviewScroll = null;
  }
}

/**
 * 把预览状态落到 DOM。
 *
 * **只替换右侧详情面板**（定点更新）：文件历史列表的方向键导航依赖行上的焦点，
 * 整区重绘会把焦点丢掉（第 256 轮刚接上的选择）。替换后单独重挂差异模式按钮。
 */
function applyFileHistoryPreview() {
  if (typeof window.__augitFileHistoryPreviewView !== "function") return;
  const pane = document.querySelector('[data-live-file-history-pane="preview"]');
  if (!pane) {
    // 右侧还是提交信息面板（首次进入时预览状态才建立，渲染已经跑过）⇒ 让渲染路径按状态换一次。
    // 用定点区域重绘而不是整页：底部之外的部分不该被牵动。
    if (!document.querySelector("[data-live-file-history-pane]")) return;
    if (typeof window.__augitRenderRegions === "function") {
      window.__augitRenderRegions("bottomTool");
      // 区域重绘换掉了整个底部工具窗 ⇒ 必须走一次渲染后的重挂（工具条的既有标签、
      // 变化文件树的滚动监听等都挂在被替换掉的节点上）。`ensureFileHistoryPreview()` 此时
      // 已有预览状态 ⇒ 直接返回，不会递归。
      rebindAfterRender();
    }
    return;
  }
  pane.innerHTML = window.__augitFileHistoryPreviewView();
  if (typeof window.__augitBindDiffModes === "function") window.__augitBindDiffModes(pane);
  // 正文被重建后回到同一份内容的阅读位置（规格 §7.9 第七条）。
  restoreFileHistoryPreviewScroll();
}

/**
 * 加载/更新文件历史右侧的预览（规格 §7.9 第五、六条）。
 *
 * 三条规则与编辑器的 `loadDiff()` 同构：
 * - **按提交与路径复用**：内容键相同且已就绪时不重查、不重写正文（相同快照刷新不重写、不改位置）；
 * - **显示模式只是排版维度**：补丁缓存不含模式 ⇒ 单双栏切换复用同一份补丁，不重新调用 Git；
 * - **改选立即取消旧预览**：令牌在每次装载（含换选、清除、隐藏）时前进，旧响应即使成功也不回写。
 */
async function loadFileHistoryPreview(options = {}) {
  const live = window.__augitLive;
  const fileHistory = live && live.fileHistory;
  if (!live || !fileHistory) return null;
  const commit = options.commit || (selectedFileHistoryCommit() || {}).fullHash || null;
  if (!commit) return null;
  const previous = live.fileHistoryPreview;
  const parts = {
    path: fileHistory.path,
    commit,
    mode: options.mode || (previous && previous.mode) || fileHistoryPreviewOptions.mode,
    ignoreWhitespace: options.ignoreWhitespace === undefined
      ? (previous ? !!previous.ignoreWhitespace : fileHistoryPreviewOptions.ignoreWhitespace)
      : !!options.ignoreWhitespace,
  };
  fileHistoryPreviewOptions = { mode: parts.mode, ignoreWhitespace: parts.ignoreWhitespace };
  const key = fileHistoryPreviewKey(parts);
  // 相同快照（提交 + 路径 + 忽略空白 + 显示模式）：不重查、不重写正文、不改阅读位置。
  if (!options.force && previous && previous.key === key && previous.mode === parts.mode && previous.ready) {
    return previous.diff;
  }

  // 命中已完成的同一内容（显示模式切换、选项来回切换且未要求强制重查）：只重新排版，不查询 Git
  // （规格 §6.3／§7.9 第六条：单双栏共用同一份补丁）。
  const cached = options.force ? null : fileHistoryPreviewPatches.get(key);
  if (cached) {
    live.fileHistoryPreview = { ...parts, key, loading: false, ready: true, diff: cached };
    applyFileHistoryPreview();
    return cached;
  }

  const token = ++fileHistoryPreviewToken;
  live.fileHistoryPreview = { ...parts, key, loading: true, ready: false, diff: null };
  applyFileHistoryPreview();

  let pending = fileHistoryPreviewRequests.get(key);
  if (!pending) {
    pending = invoke("git/diff", {
      path: parts.path,
      commit: parts.commit,
      ignoreWhitespace: parts.ignoreWhitespace,
    }, 30000).finally(() => fileHistoryPreviewRequests.delete(key));
    fileHistoryPreviewRequests.set(key, pending);
  }
  let diff = null;
  try {
    diff = await pending;
  } catch (error) {
    if (token === fileHistoryPreviewToken) {
      window.__augitError = "load-file-history-preview:" + String(error && error.message || error);
    }
  }
  // 晚到响应一律作废：令牌或请求键不匹配即丢弃（改选提交、清除文件历史、折叠工具窗都会推进令牌）。
  if (token !== fileHistoryPreviewToken) return null;
  const current = live.fileHistoryPreview;
  if (!current || current.key !== key) return null;
  const valid = diff && diff.available
    && typeof diff.path === "string" && diff.path.length > 0
    && Array.isArray(diff.rows);
  const value = valid ? diff : null;
  if (value) fileHistoryPreviewPatches.set(key, value);
  live.fileHistoryPreview = { ...parts, key, loading: false, ready: true, diff: value };
  applyFileHistoryPreview();
  return value;
}

/** 文件历史预览的显示模式（规格 §6.3：相同内容只重新排版，不重新查询 Git）。 */
async function switchFileHistoryPreviewMode(mode) {
  const live = window.__augitLive;
  if (!live || !live.fileHistoryPreview) return null;
  return loadFileHistoryPreview({ mode });
}
window.__augitLoadFileHistoryPreviewMode = switchFileHistoryPreviewMode;

/** 文件历史可见但没有预览（首次进入、重新展开工具窗）时按当前选择补查（规格 §7.9 第七条）。 */
function ensureFileHistoryPreview() {
  const live = window.__augitLive;
  if (!live || !live.fileHistory || live.fileHistoryPreview) return;
  // 右侧详情被隐藏时不预查：重显那一次才按当前选择补查（规格第七条）。
  if (live.fileHistoryDetailsHidden) return;
  if (!document.querySelector("[data-live-file-history-pane]")) return;
  void loadFileHistoryPreview();
}

/**
 * 预览正文的滚动容器（与 `diffScrollSync` 同一口径：从正文行往上找第一个真的可滚动的祖先）。
 *
 * 隐藏用的是 `display:none`，其子树重新显示后 `scrollTop` 会归零 ⇒ 阅读位置必须显式保存/恢复
 *（规格 §7.9 第七条"已经完成的比较保留正文、显示模式和阅读位置"）。
 */
function fileHistoryPreviewScroller() {
  const pane = document.querySelector('[data-live-file-history-pane="preview"]');
  if (!pane) return null;
  const line = pane.querySelector(".diff-code-line") || pane.firstElementChild;
  let node = line ? line.parentElement : pane;
  while (node && node !== document.body && node !== document.documentElement) {
    if (node.scrollHeight > node.clientHeight + 1) return node;
    node = node.parentElement;
  }
  return null;
}

/** 隐藏前记住当前正文的阅读位置（连同内容键 —— 内容或选项变了就不该沿用旧位置）。 */
function rememberFileHistoryPreviewScroll() {
  const live = window.__augitLive;
  const preview = live && live.fileHistoryPreview;
  const scroller = fileHistoryPreviewScroller();
  if (!live || !preview || !scroller) return;
  live.fileHistoryPreviewScroll = { key: preview.key, top: scroller.scrollTop };
}

/** 重显后恢复同一份正文的阅读位置（内容或选项变了、或那一份还没就绪 ⇒ 不恢复）。 */
function restoreFileHistoryPreviewScroll() {
  const live = window.__augitLive;
  const saved = live && live.fileHistoryPreviewScroll;
  const preview = live && live.fileHistoryPreview;
  if (!live || !saved || !preview || !preview.ready || preview.key !== saved.key) return;
  if (live.fileHistoryDetailsHidden) return;
  const apply = () => {
    const scroller = fileHistoryPreviewScroller();
    if (scroller && saved.top > 0) scroller.scrollTop = saved.top;
  };
  apply();
  // `display` 从 none 切回来的同一帧里，浏览器可能还没恢复布局 ⇒ 下一帧再对齐一次。
  if (typeof requestAnimationFrame === "function") requestAnimationFrame(apply);
}

/**
 * 隐藏右侧详情时取消**在途查询与排版**（规格 §7.9 第七条）。
 *
 * 已完成的比较不动：正文、显示模式与阅读位置都留着（第七条"已经完成的比较保留正文、显示模式和阅读位置"）；
 * 只有还没就绪的那一份被丢弃，重显时按当前选择补查。
 */
function cancelFileHistoryPreviewRender() {
  const live = window.__augitLive;
  if (!live) return;
  fileHistoryPreviewToken += 1;      // 在途响应作废（晚到不得回写）
  fileHistoryPreviewRequests.clear();
  const preview = live.fileHistoryPreview;
  if (preview && !preview.ready) live.fileHistoryPreview = null;
  // 会话选项（模式/忽略空白）已经在 `fileHistoryPreviewOptions` 里，重显时的补查自动沿用。
}

/**
 * 把"文件历史右侧详情显隐"落到 DOM（规格 §7.9 第七条）。
 *
 * 与日志详情同一套语义：隐藏时容器收成单列、右侧面板不显示；按钮标签/`aria-pressed` 沿用既有口径
 *（隐藏时标签是"隐藏提交详情"、`aria-pressed` 为 "true"）。区域重绘由 `liveFileHistoryTool()` 按状态回填。
 */
function applyFileHistoryDetailsState() {
  const live = window.__augitLive;
  const hidden = !!(live && live.fileHistoryDetailsHidden);
  const content = document.querySelector(".history-tool-content");
  if (content) content.dataset.detailsHidden = hidden ? "true" : "false";
  const pane = document.querySelector("[data-live-file-history-pane]");
  if (pane) pane.style.display = hidden ? "none" : "";
  const button = document.querySelector('.history-tool-content [aria-label="显示提交详情"], .history-tool-content [aria-label="隐藏提交详情"]');
  if (button) {
    button.setAttribute("aria-label", hidden ? "隐藏提交详情" : "显示提交详情");
    button.setAttribute("aria-pressed", hidden ? "true" : "false");
  }
}

// 文件历史列表的行选择：单击只选择（双击打开比较由归属行/变化文件那一套链路负责；
// 预览与取消语义在后续轮次按规格 §7.9 第五条接线）。此前这条列表**没有任何点击处理**：
// 行上的 `.selected` 恒是第一条，"换选提交"在界面上做不到。
document.addEventListener("click", (event) => {
  const row = event.target.closest && event.target.closest(".history-rows .history-row[data-history-full]");
  if (!row || !window.__augitLive) return;
  event.preventDefault();
  selectFileHistoryCommit(row.dataset.historyFull);
}, true);

// 文件历史列表的方向键选择（与项目树/日志列表同一交互口径）。
document.addEventListener("keydown", (event) => {
  if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
  const row = event.target.closest && event.target.closest(".history-rows .history-row[data-history-full]");
  if (!row || !window.__augitLive) return;
  const rows = fileHistoryRows();
  const index = rows.indexOf(row);
  if (index < 0) return;
  const next = rows[index + (event.key === "ArrowDown" ? 1 : -1)];
  if (!next) return;
  event.preventDefault();
  selectFileHistoryCommit(next.dataset.historyFull, { focus: true });
}, true);

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
  const generation = comparisonGeneration;
  // 标签立即建立并显示双方引用，正文随后填充（规格 §7.8：激活时立即打开并显示
  // 双方引用及文件路径，Git 查询完成后只填充正文，不再次激活标签）。
  const parts = comparisonParts(path, `${shortReference(commit)}^`, shortReference(commit));
  const tab = ensureComparisonTab(path, label, parts);
  live.historyComparison = { path, commit, label, parts, status: "loading" };
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
  if (token !== historyComparisonToken || !comparisonStillCurrent(tab, generation)) return diff;
  clearDiffLoadingMarker();
  if (!diff) {
    live.historyComparison = { path, commit, label, parts, status: "unavailable" };
    repaint();
    return null;
  }

  live.historyComparison = { path, commit, label, parts, status: "ready" };
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
  // 切换到比较标签使**在途的 Blame／文件历史详情**失效（规格 §7.9 第十条）：
  // 详情与比较正文共用 `detailViewToken` 的"只接纳最后一次"约束 —— 第 262 轮给普通文档补上了，
  // 这里补比较标签这条路径（否则晚到的归属会把视图从比较正文抢回 Blame）。
  detailViewToken += 1;
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
      // 分支面板「显示标签」（权威 `git.branches.show.tags`，默认 true）：从持久化设置恢复，
      // 而不是每次启动都回到默认值。
      live.logRefShowTags = settings.showGitBranchesTags !== false;
      // 分支面板「按目录分组」（权威 `git.branches.group.by.directory`，默认 true）：同上。
      live.logRefGroupByDirectory = settings.groupBranchesByDirectory !== false;
      // 大文件只读预览的警告横幅「不再显示」（权威 `PropertiesComponent` 的
      // `large.file.editor.notification.disabled`，`LargeFileNotificationProvider.java:38-58`）。
      live.hideLargeFileWarning = settings.hideLargeFileWarning === true;
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
  // 先把**其它分类**的草稿合并进来：分页后 DOM 里只有当前分类的字段，
  // 只收 DOM 会静默丢掉在别的分类里改过的值。
  const live = window.__augitLive;
  if (live && live.settingsDraft) Object.assign(payload, live.settingsDraft);
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
 * 宿主说"图片就绪"、浏览器却解不开时的兜底（规格 §7.5 / §10.2）。
 *
 * 实测：宿主只读文件头的像素尺寸（`ImageDimensionsReader`），
 * 所以 IDAT 被截断的 PNG 依然返回 `ImageReady` 并带上 data: URL，
 * 而 `<img>` 的 `naturalWidth` 为 0 —— 页面上只剩一张破图，用户读不到任何原因。
 * 这里把该正文换成与"不可预览文件"完全相同的信息态（同一组件、同一入口），
 * 而不是新增一种视觉语言。
 *
 * `error` 事件不冒泡，只有捕获阶段能监听到，因此必须 `capture: true`。
 */
function bindImageDecodeFallback() {
  if (window.__augitImageFallbackBound) return;
  window.__augitImageFallbackBound = true;
  document.addEventListener("error", (event) => {
    const image = event.target;
    if (!image || image.tagName !== "IMG" || !image.closest || !image.closest(".image-stage")) return;
    const view = image.closest(".document-view");
    if (!view) return;
    const live = window.__augitLive || {};
    // 同一张图只替换一次：替换后 `<img>` 已不在文档里，事件不会再触发。
    const holder = document.createElement("template");
    holder.innerHTML = liveUnavailableDocument("图片数据无法解码，文件可能已损坏。");
    const replacement = holder.content.firstElementChild;
    if (!replacement) return;
    view.replaceWith(replacement);
    if (live.document) live.document.decodeFailed = true;
  }, true);
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
    // 接管范围靠"对话框里有没有 [data-setting] 字段"判定，不靠容器类名：
    // live 设置框是 `.dialog.wide.settings-dialog`，`.dialog-xl` 只存在于视觉稿页面；
    // 只认后者会让真机的保存路径整条静默失效，只认 `[data-settings-action]` 又会让
    // 视觉稿场景（取消/应用/确定 三个无标记按钮）失去唯一的处理者（两次都实测踩到）。
    const button = event.target.closest && event.target.closest(
      ".dialog-footer [data-settings-action], .dialog-footer .secondary-button, .dialog-footer .primary-button");
    if (!button) return;
    const dialog = button.closest(".dialog");
    if (!dialog || !dialog.querySelector("[data-setting]")) return;
    const buttons = [...dialog.querySelectorAll(".dialog-footer .secondary-button, .dialog-footer .primary-button")];
    // live 的两个按钮带显式语义；视觉稿场景退回"第一个（取消）之外都算保存"。
    const isCancel = button.dataset.settingsAction
      ? button.dataset.settingsAction === "cancel"
      : buttons.indexOf(button) === 0;

    event.preventDefault();
    if (isCancel) {
      // 取消 = 全部改动的 configurable `cancel()` 后关窗（权威 `SettingsEditor.cancel()`：
      // 遍历 `filter.context.getModified()` 调 `configurable.cancel()`）。
      closeSettingsDialog();
      return;
    }
    // 「应用」= 写回设置但**不关窗**（权威 `SettingsDialog.createActions()` 里的 Apply 只 `editor.apply()`，
    // 关窗是 OK 的 `applyAndClose()`）；「确定」= 写回后关窗。
    const applyOnly = button.dataset.settingsAction === "apply";
    void saveSettings()
      .then(() => {
        if (applyOnly) {
          // 应用后未保存标记与「应用」的可用性都要就地重算（权威在 `updateStatus()` 里做同一件事）。
          syncSettingsDirtyMarkers();
          return;
        }
        closeSettingsDialog();
      })
      .catch((error) => {
        const reason = String((error && error.message) || error);
        window.__augitError = "save-settings:" + reason;
        showSettingsFailure(reason);
      });
  }, true);
}

/** 把保存失败的原因显示在对话框底栏（复用既有的 footer-help 位，不新增视觉语言）。 */
function showSettingsFailure(reason) {
  const dialog = document.querySelector(".settings-window .dialog")
    || document.querySelector(".dialog.wide.settings-dialog")
    || document.querySelector(".dialog-xl");
  const help = dialog && dialog.querySelector(".dialog-footer .footer-help");
  if (!help) return;
  // 与视觉稿 `settings-save-failure` 逐字一致（像素对照比的是同一段文案），
  // 并满足 §10.2 的三段式：发生了什么 / 哪些状态没有改变 / 可以做什么。
  help.textContent = "设置没有保存成功：" + describeFailure(reason, {
    unchanged: "设置没有被修改，可以修正后重试。",
  });
  help.title = help.textContent;
  help.classList.add("settings-failure");
}

/**
 * 归属行的右键菜单：权威把「标注上一修订」放在**注释槽的动作组**里
 * （`AnnotateToggleAction.java:272-276` 把 `AnnotateCurrentRevisionAction` 与
 * `AnnotatePreviousRevisionAction` 加进槽的展示组），该动作只在
 * `PreviousFileRevisionProvider` 能给出上一修订时才出现（`AnnotatePreviousRevisionAction.update()`：
 * `myProvider == null` 时 `setEnabledAndVisible(false)`）⇒ Augit 只在行带 `previousRevision` 时给这一项。
 *
 * **登记差异**：权威拿到上一修订后在**新标签**里打开标注（动作描述即 "…in a new tab"），
 * Augit 目前就地重标注同一份视图（工具栏显示所依据的修订）。
 */
function openBlameRowMenu(row, clientX, clientY) {
  const live = window.__augitLive;
  if (!live || !live.blame || !row) return;
  const revision = row.dataset.blamePrevious || "";
  closeLiveOverlay();
  if (!revision) return;
  const markup = `<section class="popover context-menu blame-row-menu" aria-label="归属行" data-blame-revision="${escapeHtml(revision)}">`
    + `<a class="menu-item" href="#" data-blame-action="annotate-previous">${icon("git-history")} 标注上一修订</a>`
    + `</section>`;
  const layer = showPointerContextMenu(markup, {
    layerClass: "blame-row-menu-layer",
    clientX,
    clientY,
    maxHeight: 240,
  });
  if (layer && typeof layer.focus === "function") layer.focus({ preventScroll: true });
}

document.addEventListener("contextmenu", (event) => {
  const row = event.target.closest && event.target.closest(".blame-document .blame-row");
  if (!row || !window.__augitLive) return;
  event.preventDefault();
  openBlameRowMenu(row, event.clientX, event.clientY);
}, true);

// 「标注上一修订」：按该行的上一修订重新标注（权威 `AnnotatePreviousRevisionAction.getFileRevision`）。
document.addEventListener("click", (event) => {
  const item = event.target.closest && event.target.closest('[data-blame-action="annotate-previous"]');
  if (!item) return;
  const live = window.__augitLive;
  const layer = item.closest(".blame-row-menu");
  const revision = layer ? layer.dataset.blameRevision : null;
  if (!live || !live.blame || !revision) return;
  event.preventDefault();
  closeLiveOverlay();
  // 与"进入 Blame"同一条收尾：读完后刷新受影响区域（编辑区/标签/状态栏/侧栏）。
  void loadBlame(live.blame.path, revision).then(() => {
    refreshAfterEvent("side", "editorContent", "editorTabs", "statusbar");
  });
}, true);

/**
 * 点击归属行的提交 → 定位 Git 历史并选择对应提交（`ux-spec` §7.9）。
 *
 * 权威 `GitFileAnnotation.showAffectedPaths()`（`plugins/git4idea/backend/src/annotate/GitFileAnnotation.java:253-271`）：
 * 注释槽的每个 aspect 都实现 `EditorGutterAction.doAction()` ⇒ 点击时若处于非模态且
 * registry `vcs.blame.show.affected.files.in.log`（默认 **true**，`registry.properties:772`）为真，
 * 就 `VcsLogNavigationUtil.jumpToRevisionAsync(project, root, hash, info.getFilePath())`
 * —— 在日志里跳到该修订；否则退回"显示受影响文件"的对话框。Augit 采用前者。
 *
 * 映射用**完整提交哈希**（`ux-spec.md:493`）。日志里没有该提交时按权威"跳转失败"的同类口径
 * 就地说明，而不是静默无反应。
 */
async function locateBlameCommit(fullHash) {
  const live = window.__augitLive;
  if (!live || typeof fullHash !== "string" || fullHash.length === 0) return false;
  if (live.fileHistory) {
    // 从文件历史进入的 Blame：恢复日志布局、解除旧文件路径限定与预览请求（`ux-spec.md:498`）。
    // `clearHistoryPathFilter()` 会重绘整页；选中在它之后进行，且不主动聚焦（不抢焦点）。
    await clearHistoryPathFilter();
  }
  // 点击提交的目标就是日志视图。**日志已经可见时不重绘**：区域刷新会按 `live.historySelectedHash`
  // 重排选中态，而日志的单击选中只写在 DOM 上（`historySelectedHash` 只由文件历史返回上下文写），
  // 因此在"目标提交不存在、只给提示"的路径上重绘会把用户当前的选中行重置成首行。
  const layout = live.layout || {};
  const logVisible = layout.bottom === "git" && layout.collapsed !== "bottom"
    && !!document.querySelector(".log-list-panel .commit-list");
  if (!logVisible || !live.history) {
    live.layout = layout;
    live.layout.userDriven = true;
    live.layout.bottom = "git";
    live.layout.collapsed = null;
    if (!live.history) await loadHistory().catch(() => null);
    refresh("bottomTool", "statusbar");
  }
  const commit = document.querySelector(`.commit-row[data-full-hash="${CSS.escape(fullHash)}"]`);
  if (!commit) {
    showToast({
      title: "无法定位到该提交",
      text: "该提交不在当前加载的历史里。",
      kind: "error",
    });
    return false;
  }
  // 提交选择由 mockup 的既有绑定派发 `history-commit-selected`（与"定位到选中分支"同一入口）。
  commit.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
  commit.scrollIntoView({ block: "nearest" });
  return true;
}

document.addEventListener("click", (event) => {
  if (event.target.closest && event.target.closest("[data-blame-action]")) return;
  const row = event.target.closest && event.target.closest(".blame-document .blame-row");
  if (!row || !window.__augitLive) return;
  event.preventDefault();
  void locateBlameCommit(row.dataset.blameFull || row.dataset.blameCommit || "");
}, true);

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

/**
 * 同一错误在外部状态未变化时不重复提示（规格 §10.2）。
 *
 * 判据是"期间有没有成功过一次"：成功即视为外部状态已变化，记忆清空，
 * 之后再次失败仍会提示；相同的错误连续出现则只提示第一次。
 * 这样轮询类失败既不会沉默，也不会每次刷新都弹一遍。
 */
const reportedErrors = new Map();

function notifyErrorOnce(key, title, text, options = {}) {
  const signature = `${title}\u0000${text}`;
  if (reportedErrors.get(key) === signature) return false;
  reportedErrors.set(key, signature);
  showToast({ title, text, kind: options.kind || "error", action: options.action || null });
  return true;
}

/** 某个来源恢复成功：清掉它的错误记忆。 */
function clearReportedError(key) {
  reportedErrors.delete(key);
}

async function drainWorkspaceChanges() {
  const live = window.__augitLive;
  if (!live) return;
  try {
    const changes = await invoke("workspace/changes", {}, 10000);
    if (changes && changes.available) {
      // 读取成功即视为外部状态已变化：清掉记忆，之后再次失败仍会提示。
      clearReportedError("workspace-changes");
      await applyWorkspaceChanges(changes);
    }
  } catch (error) {
    // 单次失败不影响后续，但必须让用户知道"现在看到的可能不是最新的"（规格 §10.2）；
    // 同一错误在恢复之前只提示一次，避免每次轮询都弹。
    notifyErrorOnce(
      "workspace-changes",
      "无法读取工作区变化",
      describeFailure(String((error && error.message) || error), {
        unchanged: "当前显示的改动列表可能不是最新的。",
        next: "会在下一次轮询时自动重试。",
      }));
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
  // 这一批是否**碰到了正文所依赖的东西**（当前文档或当前比较）。只有碰到时才需要替换
  // editorContent/editorTabs 的节点；否则替换会销毁用户正在看的节点、丢掉焦点与输入上下文
  // （规格 §125：不改变焦点、滚动和主窗口布局；第 293/294 轮同引擎实测）。
  let editorTouched = false;

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
      editorTouched = true;
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
      editorTouched = true;
    } else {
      // 无关文件变化：当前 diff 不进入加载状态，只刷新状态列表。
      touchedNothing = true;
    }

    await loadStatus().catch(() => null);
    touchedCurrent = true;
  }

  if (!touchedCurrent && !touchedNothing) return;
  // 正文区域只在这一批**真的碰到当前文档/比较**时才替换（见 editorTouched 注释）。
  // 其余情况仍然刷新列表、状态栏与标题栏：外部变化的提示与列表收敛必须照常发生
  // （§6.4/§6.5），第 295/296 轮证明"整批跳过刷新"会破坏这条规格要求。
  const regions = ["side", "statusbar", "bottomTool", "titlebar"];
  if (editorTouched) { regions.push("editorContent", "editorTabs"); }
  refreshStatusRegions(...regions);
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

window.__augitOpenStashManager = () => openStashManagerDialog();

window.__augitOpenStashDialog = () => openStashDialog();

window.__augitOpenWorktreeManager = () => openWorktreeManagerDialog();

window.__augitOpenRemoteManager = () => openRemoteDialog();

window.__augitOpenWorkspaceDialog = () => openWorkspaceDialog();

// 供验收套件读取"已经提示过哪些错误"，用于验证 §10.2 的不重复语义。
window.__augitReportedErrors = () => [...reportedErrors.keys()];

window.__augitSettingsWrite = async (payload) => {
  const result = await invoke("settings/write", payload, 15000);
  // 与保存路径一致：重新读取真实设置并重新应用字体与面板尺寸。
  await loadSettings();
  return result;
};

window.__augitLoadCommitDetails = (revision) => loadCommitDetails(revision);
window.__augitLoadOperation = () => loadOperationSession();

window.__augitLoadBlame = (path, revision) => loadBlame(path, revision || null);
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
  // 供验收套件统计"实际弹了几次"：区域替换会换掉 .toast-layer 节点，
  // 用 MutationObserver 观察节点是数不到的（节点本身被替换了）。
  window.__augitToastShows = (window.__augitToastShows || 0) + 1;
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
  const changed = live.gitUnavailableReason !== pendingGitUnavailableReason;
  live.gitUnavailableReason = pendingGitUnavailableReason;
  // 提交/Git 历史入口的禁用态属于 `rail` 区域（ux-spec §7.18）：原因晚于首帧到达时必须重绘该区域，
  // 否则入口会停在"可用"状态。`git/status` 通常比目录列举慢，这条是常规路径而非兜底。
  if (changed) refreshAfterEvent("rail");
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
 * 非 Git 目录是否还要给出「创建 Git 仓库」入口。
 *
 * 与 `pendingGitUnavailableReason` 同理：`git/status` 在 `loadDocument()` 之前就发出，
 * 返回时 `window.__augitLive` 还不存在，而对话框用的 `repositoryInitBody()` 在 `mockup.js` 里、
 * 那个脚本要稍后才加载 ⇒ 需求先缓存，首屏可交互后再弹。
 */
let pendingRepositoryInit = false;

/**
 * 非 Git 工作区的入口（产品规格 §2「允许打开非 Git 目录，并由用户显式初始化仓库」，
 * ux-spec §8「非 Git 目录 → 初始化仓库 → Git 操作进行中，随后启用 Git 工具窗口」）。
 *
 * 权威 `GitInit` 的入口在 VCS 菜单里，Augit 对应 Git 主菜单的「创建 Git 仓库…」；
 * 打开非 Git 目录时只自动弹**一次**（与「Git 不可用」同一套"只提示一次"处理），
 * 「取消」关掉后仍可从 Git 菜单再次打开。
 */
function showRepositoryInitEntry() {
  const live = window.__augitLive;
  if (!live || live.repositoryInitShown) return;
  if (typeof repositoryInitBody !== "function") {
    pendingRepositoryInit = true;
    return;
  }
  live.repositoryInitShown = true;
  openRepositoryInitDialog();
}

/** 打开「创建 Git 仓库」对话框（权威 `action.Git.Init.text` = "Create Git Repository…"）。 */
function openRepositoryInitDialog() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host || typeof repositoryInitBody !== "function") return;
  closeLiveOverlay();
  rememberDialogFocus();
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay repository-init-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "创建 Git 仓库",
    // 目标目录缺省取工作区根（权威：起点是当前选中目录、取不到则项目根）。
    repositoryInitBody(live.root || "", "", false),
    '<button type="button" class="secondary-button" data-repo-init-action="cancel">取消</button>'
      + '<button type="button" class="primary-button" data-repo-init-action="create">创建</button>',
    false,
    "repository-init-dialog");
  host.appendChild(layer);
  const field = layer.querySelector("[data-repo-init-path]");
  if (field) field.focus({ preventScroll: true });
}

/** 目标已在 Git 下时的 Yes/No 警告（权威唯一的确认时机，`GitInit.java:66-74`）。 */
function openRepositoryInitWarning(target) {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host || typeof repositoryInitWarningBody !== "function") return;
  live.repositoryInitTarget = target;
  closeLiveOverlay();
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay repository-init-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "初始化 Git",
    repositoryInitWarningBody(target),
    '<button type="button" class="secondary-button" data-repo-init-action="cancel">取消</button>'
      + '<button type="button" class="primary-button" data-repo-init-action="confirm">继续</button>',
    false,
    "repository-init-dialog");
  host.appendChild(layer);
}

/** 对话框内的状态行（失败原因／进行中说明）。 */
function setRepositoryInitNotice(notice) {
  const node = document.querySelector(".repository-init-window [data-repo-init-notice]");
  if (!node) return;
  node.hidden = !notice;
  node.textContent = notice || "";
}

/** 进行态：冻结所有动作，锁住重复提交（ux-spec「初始化进行中只锁定重复提交」）。 */
function setRepositoryInitBusy(busy, notice) {
  const layer = document.querySelector(".repository-init-window .dialog");
  if (!layer) return;
  for (const node of layer.querySelectorAll("[data-repo-init-action]")) {
    node.disabled = busy;
  }
  const create = layer.querySelector('[data-repo-init-action="create"]');
  if (create) create.textContent = busy ? "正在初始化…" : "创建";
  const confirm = layer.querySelector('[data-repo-init-action="confirm"]');
  if (confirm) confirm.textContent = busy ? "正在初始化…" : "继续";
  setRepositoryInitNotice(notice);
}

/** 「选择目录…」：权威用单目录选择器，外壳用系统的 `workspace/pick`（同一选择动作）。 */
async function pickRepositoryInitTarget() {
  const field = document.querySelector(".repository-init-window [data-repo-init-path]");
  let payload = null;
  let failure = null;
  try {
    payload = await invoke("workspace/pick", {}, 120000);
    if (!payload || payload.picked === false) {
      // 取消不是错误：保留原目标，不提示失败。
      return null;
    }
  } catch (error) {
    failure = String((error && error.message) || error);
  }
  if (failure) {
    setRepositoryInitNotice(`无法选择目录：${failure}`);
    return null;
  }
  if (field && payload && typeof payload.path === "string") {
    field.value = payload.path;
  }
  return payload;
}

/**
 * 提交「创建 Git 仓库」。
 *
 * 权威只在**目标已在 Git 下**时才问一次（`GitInit.java:66-74`）；不是仓库时没有任何确认，
 * 点按钮即意图 ⇒ 直接初始化。
 */
async function submitRepositoryInit() {
  const live = window.__augitLive;
  const field = document.querySelector(".repository-init-window [data-repo-init-path]");
  const target = field ? field.value : (live && live.root) || "";
  if (!live || !target || live.repositoryInitializing) return null;
  live.repositoryInitializing = true;
  setRepositoryInitBusy(true, "正在初始化…");
  let payload = null;
  let failure = null;
  try {
    payload = await invoke("git/init", { path: target }, 120000);
  } catch (error) {
    failure = String((error && error.message) || error);
  }
  live.repositoryInitializing = false;

  if (!failure && payload && payload.requiresConfirmation) {
    openRepositoryInitWarning(target);
    return payload;
  }

  return finishRepositoryInit(payload, failure);
}

/** 警告窗口里选「继续」：带 `confirm` 再调一次（权威 Yes 分支）。 */
async function confirmRepositoryInit() {
  const live = window.__augitLive;
  const target = live && live.repositoryInitTarget ? live.repositoryInitTarget : null;
  if (!live || !target || live.repositoryInitializing) return null;
  live.repositoryInitializing = true;
  setRepositoryInitBusy(true, "正在初始化…");
  let payload = null;
  let failure = null;
  try {
    payload = await invoke("git/init", { path: target, confirm: true }, 120000);
  } catch (error) {
    failure = String((error && error.message) || error);
  }
  live.repositoryInitializing = false;
  return finishRepositoryInit(payload, failure);
}

/** 收尾：失败在窗口内写明原因（权威是带 Git 错误输出的错误通知），成功则关窗并刷新。 */
async function finishRepositoryInit(payload, failure) {
  if (!failure && payload && payload.cancelled) {
    failure = payload.reason || "操作已取消。";
  }
  if (!failure && payload && payload.available === false) {
    failure = payload.reason || "无法初始化该目录。";
  }
  if (!failure && (!payload || payload.initialized !== true)) {
    failure = "初始化没有完成。";
  }
  if (failure) {
    setRepositoryInitBusy(false, `Git 初始化失败：${failure}`);
    return null;
  }

  closeLiveOverlay();
  await refreshAfterRepositoryInit();
  return payload;
}

/**
 * 初始化成功后的刷新（ux-spec §8：Git 操作进行中，随后启用 Git 工具窗口；
 * 保持不变的状态：项目树、当前文件和标签）。
 *
 * 目录是**刚**变成仓库的：状态、历史与引用都要重读，文件树也要刷新；
 * 宿主侧同时已作废它的 Git 解析缓存（`ShellBridge.InitRepositoryAsync`）。
 */
async function refreshAfterRepositoryInit() {
  const live = window.__augitLive;
  if (!live) return;
  await Promise.all([
    loadStatus().catch(() => null),
    loadHistory().catch(() => null),
    loadReferences().catch(() => null),
  ]);
  void refreshFileTree();
  refresh("side", "editorContent", "editorTabs", "statusbar", "bottomTool", "titlebar");
  void refreshCommitDetails();
}

/**
 * 检出被本地改动挡住时的 Smart Checkout 对话框（权威 `GitSmartOperationDialog`，
 * `plugins/git4idea/backend/src/branch/GitSmartOperationDialog.java:36-125`）。
 *
 * 结构与文案在 `mockup.js` 的 `smartCheckoutBody()`／`smartCheckoutScene()` 里（样例页与实时共用）；
 * 这里只负责打开、绑定与默认焦点 —— 权威把 `FOCUSED_ACTION` 设在**取消**上（`:118`），
 * 因此打开后焦点落在「不检出」，回车不会误触发会改写工作区的操作。
 */
function openSmartCheckoutDialog(name, kind, paths) {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host || typeof smartCheckoutBody !== "function") return;
  closeLiveOverlay();
  rememberDialogFocus();
  live.smartCheckout = { name, kind: kind || "branch" };
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay smart-checkout-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "Git 检出问题",
    smartCheckoutBody(paths, "", false),
    '<button type="button" class="secondary-button" data-smart-action="cancel">不检出</button>'
      + '<button type="button" class="primary-button" data-smart-action="smart">Smart Checkout</button>',
    false,
    "smart-checkout-dialog");
  host.appendChild(layer);
  const cancel = layer.querySelector('[data-smart-action="cancel"]');
  if (cancel) cancel.focus({ preventScroll: true });
}

/** 对话框内的状态行（进行中说明／恢复冲突说明）。 */
function setSmartCheckoutNotice(notice) {
  const node = document.querySelector(".smart-checkout-window [data-smart-notice]");
  if (!node) return;
  node.hidden = !notice;
  node.textContent = notice || "";
}

/** 进行态：冻结全部动作，锁住重复提交。 */
function setSmartCheckoutBusy(busy, notice) {
  const layer = document.querySelector(".smart-checkout-window .dialog");
  if (!layer) return;
  for (const node of layer.querySelectorAll("[data-smart-action]")) {
    node.disabled = busy;
  }
  const smart = layer.querySelector('[data-smart-action="smart"]');
  if (smart) smart.textContent = busy ? "正在切换…" : "Smart Checkout";
  setSmartCheckoutNotice(notice);
}

/**
 * 执行 Smart Checkout：`stash → 检出 → 恢复`（权威 `GitPreservingProcess`，
 * `GitCheckoutOperation.java:505-524`）。
 */
async function submitSmartCheckout() {
  const live = window.__augitLive;
  const pending = live && live.smartCheckout;
  if (!live || !pending || !pending.name || live.smartCheckoutRunning) return null;
  live.smartCheckoutRunning = true;
  setSmartCheckoutBusy(true, "正在暂存改动并切换…");
  let payload = null;
  let failure = null;
  try {
    payload = await invoke("git/checkout-smart", { name: pending.name }, 120000);
  } catch (error) {
    failure = String((error && error.message) || error);
  }
  live.smartCheckoutRunning = false;
  return finishSmartCheckout(payload, failure);
}

/**
 * 收尾：成功即关窗并刷新；恢复失败会留下 `SmartCheckout` 冲突会话（临时 stash 保留），
 * 按权威「改动不丢」的语义说明后交由既有的操作会话／冲突界面继续处理（规格 §7.13）。
 */
async function finishSmartCheckout(payload, failure) {
  if (!failure && payload && payload.cancelled) {
    failure = payload.reason || "操作已取消。";
  }
  if (!failure && payload && payload.available === false) {
    failure = payload.reason || "无法执行 Smart Checkout。";
  }

  const session = payload && payload.session ? payload.session : null;
  if (!failure && session && session.kind === "SmartCheckout" && session.hasConflicts) {
    closeLiveOverlay();
    await refreshAfterSmartCheckout();
    showToast({
      title: "Smart Checkout 未完成",
      text: "恢复改动时发生冲突：改动已保留在临时 stash 中，解决冲突后可从操作会话继续。",
      kind: "warning",
    });
    return payload;
  }

  if (failure || !payload || payload.switched !== true) {
    setSmartCheckoutBusy(false, `Smart Checkout 失败：${failure || "分支没有切换。"}`);
    return null;
  }

  closeLiveOverlay();
  await refreshAfterSmartCheckout();
  return payload;
}

/** Smart Checkout 之后的刷新：HEAD 与工作区都可能变，引用也要重读。 */
async function refreshAfterSmartCheckout() {
  const live = window.__augitLive;
  if (!live) return;
  live.references = null;
  await Promise.all([
    loadStatus().catch(() => null),
    loadReferences().catch(() => null),
  ]);
  refreshAfterEvent("titlebar", "side", "editorContent", "editorTabs", "statusbar", "bottomTool");
}

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
  layer.className = "overlay-layer live-overlay conflict-external-window";
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
  document.querySelectorAll("[data-augit-overlay].live-overlay.conflict-external-window").forEach((node) => node.remove());
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
  // 区域替换连焦点一起丢（`__augitRenderRegions` 不保留焦点）：在替换前先记下焦点意图。
  rememberCommitFocusBeforeRender();
  // 项目树的滚动位置与树内焦点同理（规格 §6 第 37 条）：`refreshAfterEvent("side")` 会把整棵
  // `.side-content.tree` 换掉，新节点的 `scrollTop` 是 0、原来聚焦的行也不存在了。
  rememberTreeStateBeforeRender();
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
/**
 * 比较工具栏的「上一处/下一处差异」（规格 §7.8/§7.9/§7.10）。
 *
 * 条文（`ux-spec.md:438/439/441/502`）：左侧依次为上一处、下一处…；**差异按连续变更块计算**
 * （同一次替换的删除行与新增行算同一处，未修改的上下文或补丁段分隔变更块）；
 * 箭头定位正文后**保留触发按钮焦点**，可继续按 Enter/Space。
 * 核实：该按钮（`.diff-toolbar .toolbar-button`）此前在 live 代码里**没有任何处理** ⇒ 死入口。
 */
function diffChangeBlocks(scope = null) {
  const root = scope
    ? (scope.matches && scope.matches(".diff-layout, .document-view") ? scope : scope.querySelector(".diff-layout, .document-view"))
    : document.querySelector(".diff-layout, .document-view");
  if (!root) return [];
  // 规格 `ux-spec.md:441`「差异数量按连续变更块计算」：双栏视图里**每一栏都是同一份变更块的
  // 完整行列表**（`.diff-side` 各含全部行），按整份 DOM 计块会把每一处差异算两次 ——
  // `data-diff-total` 因此是规格口径的两倍，按同方向要按两倍次才走完（第 224 轮修正）。
  // 只按**一栏**计块：取后一栏（`mockup.js` 里它是"当前版本"侧）；单栏模式下 `.diff-side`
  // 被统一行列表替换掉，此时容器本身就是唯一一份行列表。
  const sides = root.querySelectorAll(".diff-columns > .diff-side");
  const container = sides.length ? sides[sides.length - 1] : root;
  const lines = [...container.querySelectorAll(".diff-code-line")];
  // 「同一次替换的删除行与新增行属于同一处差异」：解析器把成对的删/增合成一行 `Modified`
  // （`GitUnifiedDiffParser.AppendChangedRows`），界面给它的类名是 `changed`
  // （`mockup.js` 的 `cssKind()`），所以块判据必须同时认 `added`／`removed`／`changed`／冲突行，
  // 否则纯修改型 Diff 一处差异都定位不到（第 223 轮修正）。
  const changed = (el) => /(^|\s)(added|removed|changed|conflict\S*)(\s|$)/.test(el.className || "");
  const blocks = [];
  lines.forEach((el, index) => {
    if (!changed(el)) return;
    const last = blocks[blocks.length - 1];
    if (last && last.lastIndex === index - 1) { last.lastIndex = index; return; }
    blocks.push({ first: el, lastIndex: index });
  });
  return blocks;
}

function diffScrollableAncestor(el) {
  let node = el && el.parentElement;
  while (node) {
    if (node.scrollHeight > node.clientHeight + 1) return node;
    node = node.parentElement;
  }
  return document.scrollingElement || document.documentElement;
}

/**
 * 边界提示（规格 `ux-spec.md:441`）：在工作区 Diff 里到达首/尾变更块、同方向再按时显示
 * "再次点击可进入上一个/下一个文件"。用注入而不是改共享视觉稿 —— 静态变体已有同构元素
 * （`.diff-boundary-hint`，文案"再次点击可进入下一个文件"）。
 */
// `Esc` 先关边界提示：只有真的显示着提示时才消费这个按键（规格 §7.7 第 10 条），
// 否则 Esc 照旧走弹层/查找条/冲突解决器自己的语义。
document.addEventListener("keydown", (event) => {
  if (event.key !== "Escape") return;
  const live = window.__augitLive;
  if (!live || !live.diffBoundaryHint) return;
  event.preventDefault();
  event.stopPropagation();
  clearDiffBoundaryHint();
}, true);

/**
 * 关闭 Blame 视图（规格 §7.9 第十一条）。
 *
 * 关闭入口要把"对应文件类型的工具栏 + 当前原文"还给用户：复用同路径的普通标签，没有就按只读文档打开。
 * 同时**使在途查询失效**（规格第十条：关闭目标文件使旧请求失效）—— 令牌前进后晚到的归属不得回写，
 * 否则关闭后正文会被重新换成 Blame。
 */
async function closeBlameView() {
  const live = window.__augitLive;
  if (!live || !live.blame) return false;
  const path = live.blame.path;
  detailViewToken += 1;
  live.blame = null;
  if (path) await openDocument(path).catch(() => null);
  // 视图类型必须在 `openDocument()` 之后按 `live.document` 定：同路径标签已激活时它直接返回，
  // 不会替我们改 `live.editor`（第 262 轮实测：不补这一步会留下"标签是活动文档、正文却是空态"）。
  live.editor = live.document ? live.document.editor : "empty";
  // 正文节点也要在重绘之后才存在 ⇒ 先刷新，再把焦点交回正文（规格第十一条）。
  refresh("editorContent", "editorTabs", "statusbar");
  // 区域重绘之后还有**异步的排版收尾**（`applyTypographyPreview()` 等）会再动一次正文节点，
  // 因此对齐两次：立刻一次、下一个任务再一次（第 262 轮实测：只对齐一次时 `activeElement` 会掉回 body）。
  const focusBody = () => {
    const body = document.querySelector(".editor-content .code-view");
    if (body) body.focus({ preventScroll: true });
  };
  focusBody();
  if (typeof setTimeout === "function") setTimeout(focusBody, 60);
  return true;
}

// Blame 头部的关闭入口（规格 §7.9 第十一条）：实时模式下由这里接管样例链路。
document.addEventListener("click", (event) => {
  const button = event.target.closest && event.target.closest('[aria-label="关闭 Blame"]');
  if (!button || !window.__augitLive || !window.__augitLive.blame) return;
  event.preventDefault();
  void closeBlameView();
}, true);

/**
 * 把"提交详情显隐"落到 DOM（规格 §7.9 条目三的"详情显隐"）。
 *
 * 此前这个状态**只写在 DOM 上**（`panel.style.display`）⇒ 底部工具窗一重绘（进入/离开文件历史、
 * 外部刷新、切筛选都会重绘）就丢，用户"隐藏详情"的选择在返回后变回显示（第 253 轮实测）。
 * 状态进 `live.historyDetailsHidden`，这里在每次渲染后按状态落地；打开/关闭入口只改状态再调它。
 */
function applyHistoryDetailsState() {
  const live = window.__augitLive;
  const panel = document.querySelector(".log-detail-panel");
  const button = document.querySelector('.log-filterbar.history-filters [aria-label="显示提交详情"], .log-filterbar.history-filters [aria-label="隐藏提交详情"]')
    || document.querySelector('[aria-label="显示提交详情"], [aria-label="隐藏提交详情"]');
  const hidden = !!(live && live.historyDetailsHidden);
  if (panel) panel.style.display = hidden ? "none" : "";
  if (button) {
    // 标签与 `aria-pressed` 沿用既有语义（第 68 轮的断言钉住的取值：隐藏后标签是"隐藏提交详情"、
    // `aria-pressed` 为 "true"）—— 本轮只把"显隐"从 DOM 搬进状态，不顺手改标签语义。
    button.setAttribute("aria-label", hidden ? "隐藏提交详情" : "显示提交详情");
    button.setAttribute("aria-pressed", hidden ? "true" : "false");
  }
}

/**
 * 提交详情里"变化文件树"的用户状态（规格 §7.9 条目三）。
 *
 * 三项都曾**只写在 DOM 上**：折叠箭头没有绑定（点了不动）、叶行的 `.selected` 点完就丢、
 * 顶部位置随 `innerHTML` 替换归零 ⇒ 任何包含 bottomTool 的区域刷新（切筛选、外部变化重载、
 * 进入/离开文件历史）都会把用户的选择抹掉。这里把三项收进 `live.commitDetailsUi`，
 * 树的 HTML 由这份状态重建（`historyFilesHtml(files, ui)`），重绘后自然一致。
 * 状态按 `revision` 归属：换了提交（或详情不可用）就整体重置 —— 选择与折叠属于那一条提交。
 */
function commitDetailsUi(revision) {
  const live = window.__augitLive;
  if (!live) return { revision, selectedPath: null, collapsed: {}, scrollTop: 0 };
  const current = live.commitDetailsUi;
  if (!current || current.revision !== revision) {
    live.commitDetailsUi = { revision, selectedPath: null, collapsed: {}, scrollTop: 0 };
  }
  return live.commitDetailsUi;
}

/** 按状态重建变化文件树的 HTML（唯一构建入口，实时层与重绘路径共用）。 */
function commitFilesHtml(files, revision) {
  if (typeof window.__augitHistoryFiles !== "function") {
    return `<p class="commit-meta">该提交没有变更文件</p>`;
  }
  return window.__augitHistoryFiles(files, commitDetailsUi(revision));
}

/**
 * 只重建状态里的变化文件树 HTML（**不动 DOM**）。
 *
 * 那两份 HTML 必须跟着状态走：区域重绘与文件历史往返读的都是 `live.commitDetails.filesHtml`，
 * 只在折叠时重建、选择时不重建的话，返回后渲染出来的是"折叠了但没选中"的旧快照
 *（第 254 轮实测：`stateSelected === 'src/App.cs'` 而 DOM 里没有 `.selected`）。
 */
function rebuildCommitFilesHtml() {
  const live = window.__augitLive;
  if (!live || !live.commitDetails || !Array.isArray(live.commitDetails.files)) return;
  live.commitDetails.filesHtml = commitFilesHtml(live.commitDetails.files, live.commitDetails.revision);
}

/**
 * 待恢复的变化文件树顶部位置 + 恢复窗口。
 *
 * 与 `.commit-list`（`pendingHistoryScroll`）同一处理：`innerHTML` 替换、区域重绘都会把
 * `scrollTop` 清零，清零本身会派发滚动事件；若照单记录，用户的位置会被"程序造成的 0"冲掉
 *（第 254 轮实测：往返后状态与界面都变成 0）。窗口内的滚动一律不当用户动作。
 */
let pendingCommitFilesScroll = null;
let pendingCommitFilesScrollTimer = 0;

function applyPendingCommitFilesScroll() {
  if (pendingCommitFilesScroll === null) return;
  const host = document.querySelector("[data-live-changed-files]");
  if (!host) return;
  host.scrollTop = pendingCommitFilesScroll;
}

/** 渲染后按状态恢复变化文件树的顶部位置；越界值由浏览器夹回（内容变短即"归位"）。 */
function restoreCommitFilesScroll() {
  const live = window.__augitLive;
  const ui = live && live.commitDetailsUi;
  const host = document.querySelector("[data-live-changed-files]");
  if (!ui || !host || !ui.scrollTop) return;
  pendingCommitFilesScroll = ui.scrollTop;
  applyPendingCommitFilesScroll();
  requestAnimationFrame(applyPendingCommitFilesScroll);
  window.clearTimeout(pendingCommitFilesScrollTimer);
  pendingCommitFilesScrollTimer = window.setTimeout(() => {
    applyPendingCommitFilesScroll();
    pendingCommitFilesScroll = null;
    // 窗口结束后按**实际**位置回写：内容变短时浏览器会把越界位置夹回，
    // 状态要落到夹回后的真实值，否则状态与界面会长期不一致。
    const liveNow = window.__augitLive;
    const hostNow = document.querySelector("[data-live-changed-files]");
    if (liveNow && liveNow.commitDetailsUi && hostNow && hostNow.isConnected) {
      liveNow.commitDetailsUi.scrollTop = hostNow.scrollTop;
    }
  }, 200);
}

/** 滚动事件写状态：用户滚到哪，重绘后就回到哪（规格 §7.9 条目三的"顶部位置"）。 */
function bindCommitFilesScroll() {
  const host = document.querySelector("[data-live-changed-files]");
  if (!host || host.dataset.commitFilesScrollBound === "true") return;
  host.dataset.commitFilesScrollBound = "true";
  host.addEventListener("scroll", () => {
    const live = window.__augitLive;
    if (!live || !live.commitDetailsUi) return;
    // 恢复窗口内的滚动不是用户动作。
    if (pendingCommitFilesScroll !== null) return;
    // 被替换下来的旧宿主的 `scrollTop` 会归零并派发滚动事件（区域重绘后它已脱离文档）：
    // 照单记录就会把用户的位置写成 0。
    if (!host.isConnected) return;
    // 只在**真的可滚动**时记录：渲染期间存在"宿主还没有可滚高度"的窗口，那时读到的 0
    // 同样会冲掉上一次的真实位置（与 `.commit-list` 同一处理）。
    if (host.scrollHeight <= host.clientHeight) return;
    live.commitDetailsUi.scrollTop = host.scrollTop;
  }, { passive: true });
}

/** 折叠状态变化后用同一份状态重建变化文件树并重写 DOM（状态是唯一事实来源）。 */
function syncCommitFilesTree() {
  const live = window.__augitLive;
  const host = document.querySelector("[data-live-changed-files]");
  if (!live || !host || !live.commitDetails || !Array.isArray(live.commitDetails.files)) return;
  rebuildCommitFilesHtml();
  host.innerHTML = live.commitDetails.filesHtml;
  bindCommitFilesScroll();
  // `innerHTML` 替换会把滚动位置清零：重写后立刻按状态回填（这次回填不算用户动作）。
  restoreCommitFilesScroll();
}

/** 记录变化文件树里被选中的行（单击与 Enter 都算选择，规格 §7.8/§7.9）。 */
function rememberHistoryFileSelection(row) {
  const live = window.__augitLive;
  const path = row && row.dataset ? row.dataset.historyPath : null;
  if (!live || !live.commitDetailsUi || !path) return;
  live.commitDetailsUi.selectedPath = path;
  // DOM 侧的 `.selected` 由调用方已经落好；这里只把**状态里的 HTML** 同步重建，
  // 让随后的区域重绘/文件历史往返渲染出同一份选择（不重写 DOM，避免把刚点的行拆下来）。
  rebuildCommitFilesHtml();
}

/**
 * 撤销"待跨文件"状态与边界提示（规格 §7.7 第 10 条）。
 *
 * 规格要求 `Esc`、改变方向、选择文件、切换显示模式、隐藏/关闭 Diff、重新加载、改变窗口布局
 * 都撤销这一状态 —— 撤销后必须**重新走两段式**（同方向第一次只给提示），而不是直接跨文件。
 * 状态记在 `live.diffBoundaryHint`，所以只有清这里才算真的撤销（DOM 会被重绘抹掉）。
 */
function clearDiffBoundaryHint() {
  const live = window.__augitLive;
  if (live) live.diffBoundaryHint = null;
  applyDiffBoundaryHint();
}

function applyDiffBoundaryHint() {
  const live = window.__augitLive;
  const layout = document.querySelector(".diff-layout");
  if (!layout) return;
  const all = [...document.querySelectorAll(".diff-boundary-hint")];
  const existing = all[0] || null;
  const hint = live && live.workspaceDiff ? live.diffBoundaryHint : null;
  if (!hint) {
    // 清**所有**同类元素：第 116 轮第一次跑时只删了第一处，切换文件后旧的提示还留在 DOM 里。
    all.forEach((node) => node.remove());
    return;
  }
  all.slice(1).forEach((node) => node.remove());
  // 规格 §7.7 第 10 条：列表首/尾没有相邻文件时**只显示"已到首/尾"的局部说明**，不循环整个文件列表。
  const text = hint.atEnd
    ? (hint.direction > 0 ? "已到改动列表的最后一个文件" : "已到改动列表的首个文件")
    : (hint.direction > 0 ? "再次点击可进入下一个文件" : "再次点击可进入上一个文件");
  if (existing) {
    if (existing.textContent !== text) existing.textContent = text;
    return;
  }
  const node = document.createElement("div");
  node.className = "diff-boundary-hint";
  node.setAttribute("role", "status");
  node.textContent = text;
  const columns = layout.querySelector(".diff-columns");
  if (columns) {
    columns.classList.add("diff-boundary-columns");
    columns.insertBefore(node, columns.firstChild);
    return;
  }
  layout.appendChild(node);
}

function moveDiffChange(direction, scope = null) {
  const blocks = diffChangeBlocks(scope);
  if (!blocks.length) return null;
  // 导航状态（当前块索引/总数）挂在**布局根**上：每次正文重绘都会换一个新的 `.diff-layout`，
  // 内容变了就自然回到"首次点击"（规格 `ux-spec.md:441`：首次点下一处定位第一块）。
  // 此前挂在**滚动容器**上，而滚动容器会跨重绘存活 —— 切单双栏或换了文件之后，第一次点同方向
  // 箭头会拿上一个内容的索引去判"已在边界"，从而**直接切到相邻文件**（第 224 轮修正）。
  const root = scope
    ? (scope.querySelector(".diff-layout, .document-view") || scope)
    : document.querySelector(".diff-layout, .document-view");
  const stateHost = root || diffScrollableAncestor(blocks[0].first);
  const total = blocks.length;
  let index = Number(stateHost.dataset.diffIndex);
  const hadIndex = Number.isFinite(index);
  if (!hadIndex) index = direction > 0 ? -1 : total;
  // 规格 `ux-spec.md:441`：到达当前文件**首/尾变更块**后再按**同方向**，第一次只显示
  // "再次点击可进入上一个/下一个文件"，**再按同方向**才切换相邻文件（文件导航只属于工作区 Diff）。
  // 「是否已在边界」必须用**点击前**的位置判断（第 224 轮修正）：否则"定位到首/尾块"的那一次点击
  // 会立刻变成提示，违背"首次点击先定位"（`ux-spec.md:441` 的"首次点击上一处定位最后一块"）。
  const wasAtEdge = hadIndex && (direction > 0 ? index === total - 1 : index === 0);
  index = Math.max(0, Math.min(total - 1, index + direction));
  // 先把夹住的索引写进 dataset（**要在边界分支之前**：边界分支会 return，若之后再写就读不到索引 ——
  // 第 116 轮第一次跑 `afterEnter: null` 就是这么来的）。
  stateHost.dataset.diffIndex = String(index);
  stateHost.dataset.diffTotal = String(total);
  const live = window.__augitLive;
  const atEdge = direction > 0 ? (index === total - 1) : (index === 0);
  // 两段式边界提示与"再按进入相邻文件"只属于工作区 Diff（规格 §7.7 第 8 条）：
  // 文件历史预览是**比较视图**，同方向到底就停在最后一块（`scope` 非空即跳过整段）。
  if (wasAtEdge && atEdge && !scope && live && live.workspaceDiff) {
    const hint = live.diffBoundaryHint;
    if (!hint || hint.direction !== direction) {
      live.diffBoundaryHint = { direction };
      applyDiffBoundaryHint();
      return { index, total, hint: true };
    }
    const moved = moveDiffFile(direction);
    if (moved) {
      live.diffBoundaryHint = null;
      applyDiffBoundaryHint();
      return { index, total, movedFile: moved.path };
    }
    // 没有相邻文件：不循环整个列表，只保留一条"已到首/尾"的局部说明（规格 §7.7 第 10 条）。
    live.diffBoundaryHint = { direction, atEnd: true };
    applyDiffBoundaryHint();
    return { index, total, atEnd: true };
  }
  if (live && !scope) live.diffBoundaryHint = null;
  if (!scope) applyDiffBoundaryHint();
  // 规格 `ux-spec.md:438/439/502` 只要求"上一处/下一处差异"能**定位**到变更块并保持触发按钮焦点，
  // **没有**"当前差异块整块染色"这一层：权威 `DiffDrawUtil.PaintMode` 只有 `DEFAULT`／`IGNORED`／
  // `RESOLVED`／`EXCLUDED_*`，不存在"当前差异"模式（第 223 轮据此删除 `.diff-current` 层）。
  blocks[index].first.scrollIntoView({ block: "center" });
  // 规格 §502：定位后**保留触发按钮焦点** —— 这里刻意不调用 focus()，由调用方保持按钮焦点。
  return { index, total };
}

/**
 * 文件历史工具条的「清除路径筛选」入口（规格 §7.9）。
 * 视觉稿里这个 × 图标按钮**本来就在**（`.history-tool-content .history-toolbar` 的第一个 `.icon-button`），
 * 但它既没有 `aria-label`、也没有绑定 —— 于是"路径筛选固定为当前文件并**显示清除入口**"只实现了一半。
 * 这里只补标签（不改共享视觉稿），点击由全局委托处理。
 */
function labelFileHistoryClearEntry() {
  const button = document.querySelector(".history-tool-content .history-toolbar .icon-button");
  if (!button) return;
  if (!button.getAttribute("aria-label")) button.setAttribute("aria-label", "清除路径筛选");
}

/** 清掉文件历史的路径筛选，并**恢复进入前的底部工具窗上下文**（规格 §7.9）。 */
async function clearHistoryPathFilter() {
  const live = window.__augitLive;
  if (!live) return;
  const back = live.fileHistoryReturn || {};
  // 选择与正文位置必须**在重绘之前**写回状态：渲染读的是 `live.historySelectedHash`，
  // 详情的滚动值由 `loadCommitDetails()` 收尾时套用（与既有的"End 意图"同一处）。
  if (back.hash) live.historySelectedHash = back.hash;
  if (back.detailScroll > 0) live.commitDetailScrollRestore = back.detailScroll;
  live.fileHistory = null;
  live.layout = live.layout || {};
  // 必须同时把布局标成"用户驱动"：否则 `shell()` 会继续用**场景参数**里的 `bottom: file-history`，
  // 于是刚清掉的文件历史会被渲染成**样例行**（实测：清掉后底部仍有一条 `feat: 实现 Augit 阶段零至五功能`）——
  // 第 161 轮由"点日志标签后数 `.history-row`"的断言抓出来。
  live.layout.userDriven = true;
  // 清除文件历史 = 释放右侧预览正文与未完成查询（规格 §7.9 第五条："清除文件历史后释放隐藏预览正文"）。
  releaseFileHistoryPreview();
  // 从 URL 直接进文件历史时没有"进入前上下文"，此时按"折叠底部区域"处理。
  live.layout.bottom = back.bottom || "";
  live.layout.collapsed = null;
  // 已加载（含"已确认是空"）的日志**直接恢复**，不重复查询（规格 §7.9：已加载的空日志
  // 仍直接恢复，不因没有提交而重复查询）。只有从未查过历史的入口才补一次查询。
  if (!live.history) await loadHistory().catch(() => null);
  // 详情显隐同样属于"进入前的上下文"：写回状态后由 `applyHistoryDetailsState()` 落到 DOM。
  if (typeof back.detailsHidden === "boolean") live.historyDetailsHidden = back.detailsHidden;
  if (typeof window.__augitRender === "function") {
    window.__augitRender();
    rebindAfterRender();
    applyHistoryDetailsState();
    return;
  }
  refresh("bottomTool", "statusbar");
  applyHistoryDetailsState();
}

/**
 * 规格 §4.1 第 123 行：**比较**（工作区 Diff / 文件历史 / 引用比较）激活时，状态栏"只显示适用的只读标识"，
 * 且**不继承后台文件的编码或换行**。
 *
 * 实测（第 112 轮）：live 的工作区 Diff 激活时（`live.diff.path = src/App.cs`）状态栏字段区是**空的** ——
 * 渲染走到了"有工作区、无文档"那条分支，于是连"只读"标识都没有。这里按规格补上（只改 live-data.js，
 * 不动共享视觉稿）。
 */
function ensureComparisonReadonlyMarker() {
  const live = window.__augitLive;
  if (!live) return;
  const bar = document.querySelector(".statusbar");
  const fields = bar && bar.querySelector(".status-fields");
  if (!fields) return;
  // 判定"是否处于比较"：工作区 Diff、文件历史（`live.fileHistory` 才是该场景的真实状态键，
  // 第 112 轮实测 `layout.bottom` 在那里并不是 `"file-history"`）、历史/引用比较。
  const comparing = !!((live.diff && live.diff.path)
    || (live.fileHistory && live.fileHistory.path)
    || (live.layout && live.layout.bottom === "file-history")
    || (live.historyComparison && live.historyComparison.path)
    || (live.referenceComparison && live.referenceComparison.path));
  if (!comparing) return;
  const spans = [...fields.querySelectorAll("span")];
  if (spans.some((span) => span.textContent.trim() === "只读")) return;
  const marker = document.createElement("span");
  marker.textContent = "只读";
  fields.appendChild(marker);
}

/**
 * 规格 §4.1 第 121/123 行：**读取尚未完成**的文件要"显示本次打开的路径"并"只显示适用的只读标识"。
 *
 * 实测（第 112 轮）：pending 窗口里状态栏显示的是**工作区名**、字段区**为空** —— 两条要求都没满足
 * （读完才变成 `["UTF-8","LF","只读"]`）。这里按 `live.pendingDocument` 直接补齐（只改 live-data.js）。
 */
function applyPendingDocumentStatus() {
  const live = window.__augitLive;
  const bar = document.querySelector(".statusbar");
  const fields = bar && bar.querySelector(".status-fields");
  const path = bar && bar.querySelector(".status-path");
  if (!live || !bar || !fields || !path) return;
  const pending = live.pendingDocument;
  if (!pending) return;
  const location = [live.workspaceName || "Augit", ...String(pending).split("/")].filter(Boolean).join("  ›  ");
  path.textContent = location;
  path.setAttribute("title", pending);
  fields.textContent = "";
  const marker = document.createElement("span");
  marker.textContent = "只读";
  fields.appendChild(marker);
}

/**
 * 显示选项开关（规格 §7.2 文档工具栏的"自动换行 / 显示空白"）。
 *
 * 核实过：这两个按钮此前在 live 代码里**零命中** —— 点了没有任何效果，也没有状态标记
 * （连 `aria-pressed` 都没有，第 118 轮想验"Enter/Space 激活"时因此拿不到任何可观测效果）。
 * 这里先实现**自动换行**这一档：状态存 `live.displayOptions.wrap`，渲染后重新贴标记。
 */
/**
 * 显示空白（规格 §7.2 显示选项）。视觉稿里这个按钮同样**没有任何行为**（live 代码零命中、无状态标记）。
 * 实现方式：把代码行里的空格/制表符**包进 `span.ws`**，点位由 CSS 伪元素画（`content: "·"`），
 * 因此 `textContent` 不变 —— 查找、选择与既有断言都不受影响；关闭时把 span 还原成纯文本。
 */
let whitespaceObserver = null;

function applyWhitespaceMarkers() {
  const live = window.__augitLive;
  if (!live) return;
  const on = !!(live.displayOptions && live.displayOptions.whitespace);
  // 实测（第 120 轮第一次跑）：点击后应用会**重渲染代码区**，一次性包裹会被新 DOM 覆盖
  // （区域刷新不等于整页重渲染，`rebindAfterRender` 那条钩子不会再跑）⇒ 用 MutationObserver 续贴。
  if (on && !whitespaceObserver && typeof MutationObserver === "function") {
    whitespaceObserver = new MutationObserver(() => {
      if (whitespaceObserver) {
        whitespaceObserver.disconnect();
        whitespaceObserver = null;
        applyWhitespaceMarkers();
      }
    });
    whitespaceObserver.observe(document.body, { childList: true, subtree: true });
  } else if (!on && whitespaceObserver) {
    whitespaceObserver.disconnect();
    whitespaceObserver = null;
  }
  const view = document.querySelector(".code-view, .diff-columns");
  if (view) view.classList.toggle("show-whitespace", on);
  const button = document.querySelector('.document-toolbar [aria-label="显示空白"]');
  if (button) {
    button.setAttribute("aria-pressed", on ? "true" : "false");
    button.classList.toggle("active", on);
  }
  const lines = [...document.querySelectorAll(".code-line")];
  if (!on) {
    lines.forEach((line) => {
      const marks = [...line.querySelectorAll("span.ws")];
      if (marks.length === 0) return;
      marks.forEach((mark) => mark.replaceWith(document.createTextNode(mark.textContent)));
      line.normalize();
    });
    return;
  }
  lines.forEach((line) => {
    if (line.querySelector("span.ws")) return;
    // 用 **TreeWalker** 抓**全部**文本节点（不只直接子节点），并跳过行号 —— 第 120 轮第一次跑
    // 只遍历了直接子文本节点，结果一个都没包上（`marks: 0`）。
    if (typeof document.createTreeWalker !== "function") return;
    const walker = document.createTreeWalker(line, NodeFilter.SHOW_TEXT, {
      acceptNode(node) {
        const text = node.nodeValue || "";
        if (!/[ \t]/.test(text)) return NodeFilter.FILTER_REJECT;
        const parent = node.parentElement;
        if (parent && (parent.classList.contains("line-number") || parent.closest(".line-number"))) {
          return NodeFilter.FILTER_REJECT;
        }
        return NodeFilter.FILTER_ACCEPT;
      },
    });
    const targets = [];
    while (walker.nextNode()) targets.push(walker.currentNode);
    for (const node of targets) {
      const text = node.nodeValue || "";
      const fragment = document.createDocumentFragment();
      for (const piece of text.split(/([ \t])/)) {
        if (piece === "") continue;
        if (piece === " " || piece === "\t") {
          const span = document.createElement("span");
          span.className = "ws";
          span.textContent = piece;
          fragment.appendChild(span);
        } else {
          fragment.appendChild(document.createTextNode(piece));
        }
      }
      node.replaceWith(fragment);
    }
  });
}

function applyDisplayOptionMarkers() {
  const live = window.__augitLive;
  if (!live) return;
  const wrap = !!(live.displayOptions && live.displayOptions.wrap);
  const view = document.querySelector(".code-view, .diff-columns");
  if (view) view.classList.toggle("wrap", wrap);
  const button = document.querySelector('.document-toolbar [aria-label="自动换行"]');
  if (button) {
    button.setAttribute("aria-pressed", wrap ? "true" : "false");
    button.classList.toggle("active", wrap);
  }
}

document.addEventListener("click", (event) => {
  const whitespace = event.target.closest && event.target.closest('.document-toolbar [aria-label="显示空白"]');
  if (whitespace) {
    event.preventDefault();
    const live = window.__augitLive;
    if (live) {
      live.displayOptions = live.displayOptions || {};
      live.displayOptions.whitespace = !live.displayOptions.whitespace;
      applyWhitespaceMarkers();
    }
    return;
  }
  const button = event.target.closest && event.target.closest('.document-toolbar [aria-label="自动换行"]');
  if (!button) return;
  event.preventDefault();
  const live = window.__augitLive;
  if (!live) return;
  live.displayOptions = live.displayOptions || {};
  live.displayOptions.wrap = !live.displayOptions.wrap;
  applyDisplayOptionMarkers();
}, true);

function rebindAfterRender() {
  // 工具窗口、标签栏与改动列表可能已被替换。
  bindToolRail?.();
  // 提交详情的显隐是**状态**：重绘后按 `live.historyDetailsHidden` 重新落地（规格 §7.9 条目三）。
  applyHistoryDetailsState();
  // 文件历史右侧详情的显隐同理（规格 §7.9 第七条）：重绘后按 `live.fileHistoryDetailsHidden` 落地。
  applyFileHistoryDetailsState();
  restoreFileHistoryPreviewScroll();
  bindEditorTabs?.();
  // 标签栏是区域刷新的产物，重建后滚动位置归零，边缘渐隐要按新状态重量一次。
  if (typeof window.__augitMeasureTabFade === "function") window.__augitMeasureTabFade();
  bindChangesState?.();
  bindChangesScroll();
  // 规格 §7.8：历史列表滚动触底加载下一页；追加后的滚动位置在新节点上还原。
  bindHistoryScroll();
  restoreHistoryScroll();
  // 提交详情里的变化文件树同样是"状态 → DOM"：折叠与选择已由 `historyFilesHtml()` 按状态渲染，
  // 这里只需重挂滚动监听并按状态回到原来的顶部位置（规格 §7.9 条目三）。
  bindCommitFilesScroll();
  restoreCommitFilesScroll();
  // 文件历史右侧的预览同样是"状态 → DOM"：状态缺失时按当前选择补查（首次进入、重新展开工具窗）。
  ensureFileHistoryPreview();
  // 图片画布中心的加载提示是"状态 → DOM"：区域重绘会抹掉追加的节点，重绘后按状态补回
  //（规格 §7.5：提示只属于当时那张图，读取完成或切标签时由 `clearImagePreviewState()` 撤去）。
  syncImagePreviewState();
  restoreChangesState();
  // 项目树的位置与树内焦点同样跨重绘保留（规格 §6 第 37 条）。
  restoreTreeState();
  bindOverlayEscape();
  bindTitlebarMenuEscape();
  bindWindowChrome();
  bindManagementFieldDraft();
  bindLogFilterDraft();
  // 引用树选中态决定竖条里"需要选中引用"的项能否用（权威按选择刷 `update()`）；
  // 区域刷新会重建行与竖条，因此每次渲染后都要按状态重新落一遍。
  if (typeof window.__augitApplyRefSelection === "function") window.__augitApplyRefSelection();
  bindCompactDialogKeys();
  bindStashDialogKeys();
  bindRegionTabOrder();
  bindDocumentModeMemory();
  bindGlobalShortcuts();
  bindRefTreeKeys();
  bindRefTreeDoubleClick();
  bindRefTreeFilter();
  bindHistoryUserSearch();
  bindModalBackground();
  labelFileHistoryClearEntry();
  renderDiffErrorNotice();
  applyWhitespaceMarkers();
  ensureComparisonReadonlyMarker();
  applyDisplayOptionMarkers();
  applyPendingDocumentStatus();
  restoreAmendDraft();
  // 重绘路径先按状态对齐一次（不消费）：详情可能是**缓存重绘**，不会再走异步收尾；
  // 随后的定点刷新还可能再替换一次详情，所以再排一次短延时对齐。
  applyPendingDetailScroll(false);
  scheduleDetailScrollReapply();
  bindDetailScrollIntent();
  reflectWriteOperation();
  guardUnwiredNavigation();
  bindSettingsPages();
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
  // Markdown 预览的加载/失败提示只属于"当时显示的那个文档"：切到别的标签（或没有标签）就清掉，
  // 否则提示与「点预览重试」的入口会跟到另一个文件的预览上（规格 §7.3 的提示只对当前预览）。
  const previewState = live.markdownPreview;
  if (previewState && previewState.path
      && (!tab || tab.kind !== "document" || tab.path !== previewState.path)) {
    live.markdownPreview = null;
  }
  // 图片画布的加载提示同理（规格 §7.5）：它只属于"当时显示的那张图"。
  const imageState = live.imagePreview;
  if (imageState && imageState.path
      && (!tab || tab.kind !== "document" || tab.path !== imageState.path)) {
    clearImagePreviewState();
  }
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
    // 外部更新复用**同一个标签**，但内容必须换成这次读取的结果：此前这里直接 return，
    // 于是"重新读取"的载荷被丢掉 —— 界面上仍是旧正文/旧位图，连规格 §7.5 第 6 条要求的
    // "即使大小不变也重新解码"与"更新后损坏时显示准确的信息页"都做不到
    //（第 244 轮实测：桩按注入尺寸现造 2000×1200 的图，推送后正文仍是 400×300）。
    // 标签身份、位置、预览标记与会话内记住的文档模式都保持不变（复用控件，不重建标签）。
    const model = toLiveDocument(payload);
    existing.document = model;
    existing.editor = model.editor;
    existing.title = model.name || path;
    if (activate) live.activeTabId = existing.id;
    syncActiveTab();
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
    // 当前会话记住的文档模式（规格 §7.3：切换原文/对照/预览后在会话内记忆）。
    // 之前模式只存在 DOM 的 data 属性上，任何一次区域重绘都会把它重置成默认"预览"。
    documentMode: null,
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
  // 切走标签等于把 Diff 收起来：撤销"待跨文件"状态（规格 §7.7 第 10 条）。
  clearDiffBoundaryHint();
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
    comparisonGeneration += 1;
    live.followChanges = false;
    if (live.historyComparison) live.historyComparison.status = "closed";
    // 历史比较有独立的递增令牌：只推进 diffToken 不足以让它的收尾逻辑失效，
    // 晚到的响应会把状态从 "closed" 复活成 "ready"/"unavailable"，
    // 于是"关闭后解除跟随"失效——之后改选提交或文件会重新创建比较标签（规格 §5.2）。
    historyComparisonToken += 1;
    closeDiff();
  }

  // 关闭标签要让**这个文件**在途的读取失效：否则晚到的响应会走 `openDocument` 的成功分支，
  // 把刚关掉的标签重新建出来 —— 规格 §7.5 明确要求"关闭后晚到位图必须释放"，
  // 而这条对普通文档同样成立（`documentToken` 是全局的，因此只在该文件正是待打开路径时才推进）。
  if (closing && closing.kind === "document" && live.pendingDocument === closing.path) {
    documentToken += 1;
    live.pendingDocument = null;
    clearTimeout(markdownPreviewHintTimer);
    markdownPreviewHintTimer = null;
    clearImagePreviewState();
  }
  // 关闭图片标签时一并丢掉它的视图记忆（比例/平移）：重新打开应按适应区域显示，
  // 而不是继承上一次的缩放（那份记忆只为"同一张图的外部更新复用预览"而留，规格 §7.5）。
  if (closing && closing.kind === "document" && live.imageView && live.imageView.path === closing.path) {
    live.imageView = null;
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
  // 底部工具窗取**实际渲染出来的那个**：场景可以只给 `bottom` 参数、而 `activeRail` 仍是侧栏入口
  // （`main-project` 就是 `bottom: "git"` ＋ `activeRail: "project"`）。只从 activeRail 推导会把
  // "日志正开着"读成空串 ⇒ "进入文件历史前的上下文"记成空串 ⇒ 返回时底部被折叠而不是回到日志
  // （第 162/163 轮实测 `after.bottom === ""`）。
  let bottom = RAIL_BOTTOM.includes(activeRail) ? (RAIL_BOTTOM_VALUE[activeRail] || "") : "";
  const rendered = document.querySelector(".bottom-tool");
  if (rendered) {
    if (rendered.classList.contains("terminal-tool")) bottom = "terminal";
    else if (rendered.querySelector(".history-tool-content")) bottom = "file-history";
    else if (rendered.querySelector(".git-toolbar-layout")) bottom = "git";
  }
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
  // 改变窗口布局同样撤销"待跨文件"状态（规格 §7.7 第 10 条）。
  clearDiffBoundaryHint();
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
    // 折叠底部工具窗等于隐藏承载预览的工具窗口：取消未完成查询并丢掉预览正文（规格 §7.9 第五条）。
    if (!inSide && layout.collapsed === "bottom") releaseFileHistoryPreview();
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

  // 终端面板刚出现时创建会话（规格 §7.16 按需单会话）。
  // 必须在渲染之后：startTerminal 需要真实的 .terminal-view 宿主节点。
  if (layout.bottom === "terminal" && layout.collapsed !== "bottom") {
    void ensureTerminal();
  }
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
    // ux-spec §7.18：Git 不可用时提交与 Git 历史入口是禁用态（`rail()` 写入
    // `aria-disabled` + `title` 原因）。点击不再切换工具窗口，也不重复弹错——
    // 局部错误与「配置 git.exe」入口在检测时已给出一次。
    if (button.getAttribute("aria-disabled") === "true") return;
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
 * 在区域替换**之前**记下"焦点是否还在提交区内"（规格 §7.6 第 14 条：等待与失败期间不抢焦点）。
 *
 * 必须在替换之前读：替换之后 `document.activeElement` 已经掉到文档主体，
 * 再无法区分"被这次重绘弄丢"与"用户本来就没把焦点放在这里"。
 */
function rememberCommitFocusBeforeRender() {
  const live = window.__augitLive;
  if (!live) return;
  const active = document.activeElement;
  const box = document.querySelector(".commit-box");
  // 与提交动作显式置的信号取"或"：点击可能让焦点先离开提交区（按钮是否吃焦点因控件类型而异），
  // 但"用户按了提交"本身就意味着这次重绘要把焦点交回提交信息。
  const inCommitBox = !!(box && active && active !== document.body && box.contains(active));
  live.commitFocusBeforeRender = !!live.commitFocusBeforeRender || inCommitBox;
}

/**
 * 在区域替换**之前**记下项目树的滚动位置与"焦点是否在树内"（规格 §6 第 37 条）。
 *
 * 条文：读取期间用户"单击其他项目树行、滚动项目树或把焦点移到其他控件"时，已打开的文件
 * 可以完成显示，但**不得重选树行、滚回原视口或抢回焦点**。选中树行那一半由
 * `live.treeSelectedPath` 负责；这里负责另外两半 —— 位置与焦点。
 *
 * 必须在替换之前读：替换之后旧节点已经脱离文档，读到的只能是新节点的 0 与 `document.body`，
 * 再无法区分"被这次重绘弄丢"与"用户本来就没滚/没把焦点放在这里"。
 * 只在该轴**真的可滚动**时记录纵向位置：树还没展开、还没溢出的窗口里读到 0 会冲掉上一次的
 * 真实位置（与 `.changes-list`／日志列表同一坑）。
 */
function rememberTreeStateBeforeRender() {
  const live = window.__augitLive;
  if (!live) return;
  const tree = document.querySelector(".side-content.tree");
  if (!tree) return;
  if (tree.scrollHeight > tree.clientHeight) live.treeScrollTop = tree.scrollTop;
  const active = document.activeElement;
  const row = active && active.closest ? active.closest(".side-content.tree .tree-row") : null;
  live.treeFocusPath = row ? (row.dataset.treePath || null) : null;
  live.treeFocusInside = !!(active && active !== document.body && tree.contains(active));
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

  // 提交进行中或校验失败后，提交信息的输入焦点与光标必须回到输入框（规格 §7.6）。
  //
  // 权威里提交期间与失败之后**焦点始终在提交信息编辑器上**：失败原因出现在提交区，
  // 用户的下一个动作就是改提交信息（`CommitChangeListDialog` 的消息编辑器是对话框的
  // 焦点组件，出错不改变焦点）。Augit 的区域替换会重建 textarea，焦点会掉到文档主体，
  // 于是"失败后直接继续输入"这个动作在界面上做不到——这是实现落差，不是产品差异。
  //
  // 只在焦点**确实是被这次重绘弄丢的**时候夺回：重绘前焦点就在提交区内
  //（`rememberCommitFocusBeforeRender()` 记的是一次性信号）。用户把焦点放在别处
  //（编辑器正文、改动行、其它工具窗口）时绝不抢——那时重绘前的焦点不在提交区，
  // 信号为假，这里整段跳过。用完即清，避免下一次不经过 `refresh()` 的重绘复用旧信号。
  const lostByRender = !!live.commitFocusBeforeRender;
  live.commitFocusBeforeRender = false;
  if (box && lostByRender && (window.__augitCommitError || live.writeOperation)) {
    const caret = live.commitSelection;
    box.focus({ preventScroll: true });
    if (caret && typeof box.setSelectionRange === "function") {
      const end = String(box.value || "").length;
      const start = Math.min(Math.max(0, caret.start | 0), end);
      try {
        box.setSelectionRange(start, Math.min(Math.max(start, caret.end | 0), end));
      } catch (error) {
        // 控件不支持选区（不是文本框）时保持 focus() 的结果。
      }
    }
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

/**
 * 项目树的滚动位置与树内焦点（规格 §6 第 37 条）。
 *
 * 读取收尾的 `refreshAfterEvent("side", …)`（`openDocument` 末尾）会把整个 `.side-tool` 换成
 * `renderScene()` 产出的新片段 —— 注释里写的"保留项目树的展开状态与滚动位置"对**展开状态**成立
 * （它是状态、按状态渲染），但 `.side-content.tree` 这个**节点**确实是新的：新节点 `scrollTop` 为 0、
 * 原来聚焦的树行也不存在了（第 271 轮实测 `sameTree:false`、`treeScrollTop` 60 → 0、焦点掉到 BODY）。
 * 两者都属于用户状态，必须跨越重绘保留，否则"读取期间滚动项目树 / 把焦点放在树行上"这两个动作
 * 会在文件读完的瞬间被抹掉。
 */
let pendingTreeScroll = null;
let pendingTreeScrollTimer = 0;

/**
 * 把待恢复的纵向位置写给当前树；越界值由浏览器夹回（即"内容变短时归位"）。
 *
 * 只在位置仍处于**我们已知的三种状态**时对齐：刚替换/被锚点回填的 `0`、目标值本身、
 * 或我们上一次写下的值。出现第四种值说明用户在窗口内自己滚动过 —— 立刻放弃，
 * 绝不把用户的新位置拉回去（第 271 轮全量复跑实测：§154 在"读取收尾刷新"之后把树滚到 260，
 * 200ms 窗口把旧的 0 又写了回去，`§154 字号变化保持树的第一个可见节点` 因此失败）。
 */
function applyPendingTreeScroll() {
  if (!pendingTreeScroll) return;
  const tree = document.querySelector(".side-content.tree");
  if (!tree) return;
  const target = pendingTreeScroll.top;
  if (typeof target !== "number") return;
  const current = tree.scrollTop;
  const ours = current === 0 || current === target || current === pendingTreeScroll.lastApplied;
  if (!ours) { pendingTreeScroll = null; return; }
  if (current !== target) tree.scrollTop = target;
  pendingTreeScroll.lastApplied = target;
}

/**
 * 渲染后把项目树的位置与树内焦点交还给用户。
 *
 * 与日志列表（`pendingHistoryScroll`）同一处理：`applyTypography()` 在区域替换之后异步运行，
 * 它按"替换刚发生时捕获的锚点"（那时树的位置已经是 0）回填一次 ⇒ 同步恢复会被冲掉，
 * 因此把目标位置留在本地变量里，下一帧与窗口末各对齐一次。
 * 焦点按**路径**交还给新节点上的同一行；重绘前焦点不在树内时绝不移动焦点。
 */
function restoreTreeState() {
  const live = window.__augitLive;
  if (!live) return;
  if (typeof live.treeScrollTop === "number") {
    pendingTreeScroll = { top: live.treeScrollTop };
    applyPendingTreeScroll();
    requestAnimationFrame(applyPendingTreeScroll);
    window.clearTimeout(pendingTreeScrollTimer);
    pendingTreeScrollTimer = window.setTimeout(() => {
      applyPendingTreeScroll();
      pendingTreeScroll = null;
    }, 200);
  }

  const focusPath = live.treeFocusPath;
  const focusInside = !!live.treeFocusInside;
  live.treeFocusPath = null;
  live.treeFocusInside = false;
  if (!focusInside) return;
  const tree = document.querySelector(".side-content.tree");
  if (!tree) return;
  const row = focusPath
    ? [...tree.querySelectorAll(".tree-row[data-tree-path]")].find((item) => item.dataset.treePath === focusPath)
    : null;
  const target = row || tree;
  if (target && typeof target.focus === "function") target.focus({ preventScroll: true });
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

  // 提交信息的光标位置与草稿一样属于用户状态：区域替换会重建 textarea，
  // 节点上的选区无从在重绘后找回，因此同样记进 live（六条硬约束之四）。
  // 没有它，`restoreChangesState()` 恢复草稿时的 `box.value = …` 赋值会把选区留在**末尾**
  //（第 245 轮实测：光标从提交前的 3 变成 7），用户在信息中间继续编辑时会被甩到末尾。
  const rememberCaret = (box) => {
    const live = window.__augitLive;
    if (!live || !box) return;
    try {
      live.commitSelection = { start: box.selectionStart, end: box.selectionEnd };
    } catch (error) {
      // 控件不支持选区读取（不是文本框）时按"放到末尾"处理。
      live.commitSelection = null;
    }
  };
  for (const name of ["input", "keyup", "select", "click", "focusin"]) {
    document.addEventListener(name, (event) => {
      const box = event.target.closest && event.target.closest(".commit-box .message-field, .commit-box textarea");
      if (box) rememberCaret(box);
    }, true);
  }
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

    // 标题栏右侧的搜索与设置入口（视觉稿 titlebar 的两个 top-button，规格 §5.1）。
    // 它们此前没有任何绑定，直接落到文件末尾的"其余尚未接线"兜底 ——
    // 点下去只有 preventDefault，界面毫无反应（用户实测"右上角放大镜和齿轮点了没反应"）。
    const titlebarEntry = event.target.closest && event.target.closest(".titlebar .top-button[aria-label]");
    const titlebarLabel = titlebarEntry ? titlebarEntry.getAttribute("aria-label") : null;
    if (titlebarLabel === "搜索" || titlebarLabel === "设置") {
      event.preventDefault();
      if (titlebarLabel === "搜索") openSearchOverlay("quick");
      else openSettingsDialog();
      return;
    }

    // 工作区芯片（"A <工作区名> ▾"，title="切换工作区"）与"当前文件"芯片
    // （`.titlebar-context`，title="快速打开文件"）。第 368 轮的真机清单发现它们**和曾经的
    // 放大镜/齿轮一样没有任何绑定**：点下去只留下兜底记录，界面毫无反应
    // （`grep -n "titlebar-context\|workspace-chip" web/src/live-data.js` 只命中窗口拖拽的
    // 控件选择器，没有任何点击处理）。这里按各自 title 声明的语义接上：
    // 工作区芯片 → 打开工作区页；当前文件芯片 → 打开快速打开浮层。
    const workspaceChip = event.target.closest && event.target.closest('.top-chip.workspace-chip');
    if (workspaceChip) {
      event.preventDefault();
      openWorkspaceDialog();
      return;
    }

    const contextChip = event.target.closest && event.target.closest('.titlebar-context');
    if (contextChip) {
      event.preventDefault();
      openSearchOverlay('quick');
      return;
    }

    // 终端标题行的动作（规格 §7.16）。这两个入口此前同样没有绑定 ——
    // 终端关不掉、也收不起来，只能靠点左侧入口折叠。
    const terminalClose = event.target.closest && event.target.closest('[aria-label="关闭终端"]');
    if (terminalClose && terminalClose.closest('.terminal-tool')) {
      event.preventDefault();
      void requestCloseTerminal();
      return;
    }

    const terminalHide = event.target.closest && event.target.closest('[aria-label="隐藏终端"]');
    if (terminalHide && terminalHide.closest('.terminal-tool')) {
      event.preventDefault();
      // 隐藏只收起底部工具窗口，会话保留（再次点入口即恢复）。
      applyRailAction("terminal");
      return;
    }

    // 文档工具栏与工具窗标题栏上的"死入口"（第 169 轮行为实测扫出：这些 aria-label 在 live-data 里
    // 一处都没有出现）——「跳转行」按钮（`Ctrl+G` 可用、按钮没接）、侧栏「最小化」、
    // 「定位当前文件」、「折叠项目树」。四者的能力都已存在，分别接到既有实现上。
    // 必须限定在**文档工具栏**里：紧凑输入窗口自己的 `aria-label` 就是标题（"跳转行"），
    // 用裸的 `[aria-label=…]` 会把对话框内的点击（包括"跳转"按钮）也当成这个入口
    // —— 实测后果是点"跳转"又开一个窗口、目标行永远不生效。
    const jumpToLineButton = event.target.closest
      && event.target.closest('.document-toolbar [aria-label="跳转行"]');
    if (jumpToLineButton) {
      event.preventDefault();
      openGoToLineDialog();
      return;
    }

    const minimizeTool = event.target.closest
      && event.target.closest('.side-tool .tool-header [aria-label="最小化"]');
    if (minimizeTool) {
      event.preventDefault();
      const layout = currentLayout();
      if (layout) {
        layout.userDriven = true;
        layout.collapsed = "side";
        window.__augitRender();
        rebindAfterRender();
      }
      return;
    }

    const locateCurrentFile = event.target.closest
      && event.target.closest('.side-tool .tool-header [aria-label="定位当前文件"]');
    if (locateCurrentFile) {
      event.preventDefault();
      const live = window.__augitLive;
      const path = live && live.document ? live.document.path : null;
      const row = path
        ? document.querySelector(`.side-content.tree .tree-row[data-tree-path="${CSS.escape(path)}"]`)
        : null;
      if (row) {
        selectTreeRow(row);
        row.scrollIntoView({ block: "nearest" });
      }
      return;
    }

    const collapseTree = event.target.closest
      && event.target.closest('.side-tool .tool-header [aria-label="折叠项目树"]');
    if (collapseTree) {
      event.preventDefault();
      // 展开层级是会话数据（`expandedPaths` + `live.tree`）：清空后重建可见树并按规格 §6.7 防抖写回。
      expandedPaths.clear();
      const live = window.__augitLive;
      if (live) live.tree = buildVisibleTree(live.name, live.rootPath ?? "");
      scheduleSessionPersist();
      refresh("side");
      return;
    }

    // 提交侧栏工具栏「刷新」（规格 §5.2）：第 168 轮行为实测（点一下看宿主请求/焦点）发现它点了没反应。
    const changesRefresh = event.target.closest
      && event.target.closest('.changes-layout .toolbar [aria-label="刷新"]');
    if (changesRefresh) {
      event.preventDefault();
      void loadStatus().catch(() => null);
      return;
    }

    // 日志左竖条＝**分支面板自己的动作组**（权威 `BranchesInGitLogUiFactoryProvider.createMainComponent`：
    // `expandControlPanel` 的展开卡片是竖向 ActionToolbar〔`Git.Log.Hide.Branches` ＋ 分隔 ＋
    // `BranchesDashboardTreeComponent.createActionGroup()`〕，折叠卡片是 `ExpandStripeButton`）。
    // 日志级动作（刷新）在横向工具条的右角，由下面的分支处理；这里只接竖条自己的入口。
    const refStripe = event.target.closest && event.target.closest('.git-side-toolbar [data-ref-stripe]');
    if (refStripe) {
      event.preventDefault();
      const live = window.__augitLive;
      const action = refStripe.dataset.refStripe;
      if (action === 'hide-branches' || action === 'show-branches') {
        // 权威 `HideBranchesAction`：把 `Show.Git.Branches` 置为 false（折叠卡片随即接管竖条）。
        if (live) live.logBranchesCollapsed = action === 'hide-branches';
        refreshAfterEvent("bottomTool");
        return;
      }
      if (action === 'new-branch') {
        closeLiveOverlay();
        openBranchActionDialog('create');
        return;
      }
      if (action === 'fetch') {
        // 与分支弹层的「更新项目…」同一条通道（`git/fetch`）。
        closeLiveOverlay();
        void runPopoverAction('fetch');
        return;
      }
      if (action === 'update-selected') {
        // 权威 `UpdateSelectedBranchAction`：整个选中集一起交给 `updateBranches`（逐个快进）。
        const names = selectedRefs()
          .filter((item) => item.kind === 'branch')
          .map((item) => item.name);
        if (names.length > 0) {
          closeLiveOverlay();
          void updateSelectedBranch(names);
        }
        return;
      }
      if (action === 'delete-branch') {
        // 权威 `DeleteBranchAction` → `GitBrancher.deleteBranches`：删除**整个选中集**
        // （本地/远端分支与标签分别处理）；确认层与引用行菜单共用（第 171 轮的两步删除）。
        const targets = selectedRefs().map((item) => ({ name: item.name, kind: item.kind }));
        if (targets.length > 0) {
          closeLiveOverlay();
          openRefDeleteConfirm(targets, false);
        }
        return;
      }
      if (action === 'settings') {
        // 权威 `Git.Log.Branches.Settings`：齿轮弹层（位置与关闭规则与其它指针菜单一致）。
        closeLiveOverlay();
        const rect = refStripe.getBoundingClientRect();
        showPointerContextMenu(refPaneSettingsMenu(), {
          layerClass: 'ref-settings-menu',
          clientX: Math.round(rect.right + 4),
          clientY: Math.round(rect.top),
          maxHeight: 320,
        });
        return;
      }
      if (action === 'expand-all' || action === 'collapse-all') {
        // 权威 `ExpandAllAction`／`CollapseAllAction`：经 `TreeExpander` 展开/折叠全部分组。
        if (typeof window.__augitSetRefTreeExpanded === 'function') {
          window.__augitSetRefTreeExpanded(action === 'expand-all');
        }
        return;
      }
      if (action === 'locate-branch') {
        // 权威 `NavigateLogToSelectedBranchAction`（图标 `AllIcons.General.Locate`）：把日志定位到选中分支的提交。
        void locateSelectedRef();
        return;
      }
      if (action === 'my-branches') {
        // 权威 `ShowMyBranchesAction`（真正的 `ToggleAction`：`isSelected` 读 `controller.showOnlyMy`，
        // `setSelected` 写回；状态**会话内**、不持久化）。打开时才现算判据数据。
        const next = !(live && live.showOnlyMyBranches);
        if (live) {
          live.showOnlyMyBranches = next;
          if (!next) {
            // 关掉时清掉结果，下次打开重新算（分支可能已经变了）。
            live.myBranchNames = null;
            live.myBranchesReason = null;
          }
        }
        closeLiveOverlay();
        if (next) void ensureMyBranches();
        refreshAfterEvent('bottomTool');
        return;
      }
      if (action === 'show-diff') {
        // 权威 `ShowBranchDiffAction.actionPerformed`（`BranchesDashboardActions.kt:290-313`）：
        // 对**每个**非当前选中分支 `GitBrancher.compare(name, repositories)` 各比较一次。
        const names = selectedRefs()
          .filter((item) => item.kind === 'branch' || item.kind === 'remote')
          .map((item) => item.name)
          .filter((name) => {
            const branch = ((live.references && live.references.branches) || [])
              .find((item) => item.name === name);
            return !!branch && !branch.isCurrent;
          });
        if (names.length > 0) {
          closeLiveOverlay();
          // 权威对每个非当前分支各开一个比较视图；Augit 只有一个比较视图，因此只比较选中集里的第一个
          // （登记差异，见 `09-icons.md` 第 183 轮）。
          void openBranchComparison(names[0]);
        }
        return;
      }
    }

    // 横向筛选行右角（权威 `Vcs.Log.Toolbar.RightCorner`：`Refresh` 在右角、`GoToRef` 紧随其后）。
    const logToolbarEntry = event.target.closest
      && event.target.closest('.history-filters .toolbar-button[aria-label]');
    if (logToolbarEntry) {
      const label = logToolbarEntry.getAttribute('aria-label');
      if (label === '刷新') {
        event.preventDefault();
        // 规格 §7.8：右角的「刷新」要**重新读取并重画**历史。此前只 `loadHistory()`（只写状态、
        // 不刷新区域），列表继续显示旧内容 —— 宿主已经返回了新的引用名/新提交，DOM 却一动不动
        //（第 238 轮实测：给第一行注入长引用后点刷新，`__historyCalls` 2 → 3，标签仍是旧值）。
        // 与筛选路径的 `reloadHistoryKeepingFocus()`（读完 `refresh("bottomTool")`）也不一致。
        // 刷新按钮自己会被区域替换掉，因此读完再按同一个无障碍名把焦点放回新节点。
        void reloadHistoryKeepingFocus().then(() => {
          const next = document.querySelector(
            `.history-filters .toolbar-button[aria-label="${CSS.escape(label)}"]`);
          if (next && typeof next.focus === 'function') next.focus({ preventScroll: true });
        });
        return;
      }
    }

    // 分支面板设置里的开关（权威 `git.branches.show.tags` 与 `git.branches.group.by.directory`）。
    const refSetting = event.target.closest && event.target.closest('[data-ref-setting]');
    if (refSetting) {
      event.preventDefault();
      const live = window.__augitLive;
      if (refSetting.dataset.refSetting === 'show-tags' && live) {
        // 权威把该开关持久化在项目设置（`GitVcsSettings.showTags()`，`GitBranchesTreeShowTagsAction`），
        // 因此这里写回设置文件；登记差异：权威是项目级（workspace 文件），Augit 的设置文件是应用级。
        live.logRefShowTags = live.logRefShowTags === false;
        void invoke('settings/write', { showGitBranchesTags: live.logRefShowTags }, 15000).catch(() => null);
      } else if (refSetting.dataset.refSetting === 'group-by-directory' && live) {
        // 权威 `GitGroupBranchByDirectoryAction`（`GitGroupBranchAction.kt:26-45`）：
        // `setBranchGroupingSettings(GROUPING_BY_DIRECTORY, state)` ＋ `saveSettingsForRemoteDevelopment`，
        // 即**持久化**在项目设置里（默认开启）。这里同样写回设置文件。
        live.logRefGroupByDirectory = live.logRefGroupByDirectory === false;
        void invoke('settings/write', { groupBranchesByDirectory: live.logRefGroupByDirectory }, 15000).catch(() => null);
      }
      closeLiveOverlay();
      refreshAfterEvent('bottomTool');
      return;
    }

    // 分支面板设置里的「单击时」两项（权威 `SelectionHandlingModeAction`，`BranchesDashboardActions.kt:474-496`）：
    // 互斥 —— 选中一个即清掉另一个，再点已选中的那个则回到"单击只选中"。默认都不生效。
    const selectionMode = event.target.closest && event.target.closest('[data-ref-selection-action]');
    if (selectionMode) {
      event.preventDefault();
      const live = window.__augitLive;
      const key = selectionMode.dataset.refSelectionAction;
      if (live) live.logRefSelectionAction = live.logRefSelectionAction === key ? null : key;
      closeLiveOverlay();
      refreshAfterEvent('bottomTool');
      return;
    }

    // 日志筛选栏的四个筛选控件（权威 `com.intellij.util.ui.FilterComponent`：
    // 设了值时右侧是关闭叉 ⇒ 点它复位该筛选；否则点开弹层）。
    const filterControl = event.target.closest
      && event.target.closest('.history-filters [data-filter-key]');
    if (filterControl) {
      event.preventDefault();
      const liveFilter = window.__augitLive;
      const key = filterControl.dataset.filterKey;
      const filterNow = (liveFilter && liveFilter.historyFilter) || {};
      // 日期筛选的值在 `since`／`until` 两个键上、路径筛选在 `paths` 上，都不是控件名本身。
      const selected = key === 'date'
        ? !!(filterNow.since || filterNow.until)
        : key === 'user' ? !!(filterNow.authors && filterNow.authors.length > 0)
        : key === 'path' ? !!(filterNow.paths && filterNow.paths.length > 0)
        : !!filterNow[key];
      if (selected) {
        // 关闭叉：复位（权威 `createResetAction()` → `setFilter(null)`）。
        if (key === 'branch') {
          setHistoryBranchFilter('');
          void reloadHistoryKeepingFocus();
        } else if (key === 'date') {
          setHistoryDateFilter(null, null);
          void reloadHistoryKeepingFocus();
        } else if (key === 'user') {
          setHistoryAuthorFilter([]);
          void reloadHistoryKeepingFocus();
        } else if (key === 'path') {
          setHistoryPathFilter([]);
          void reloadHistoryKeepingFocus();
        }
        return;
      }
      if (key === 'user') {
        closeLiveOverlay();
        // 每次打开都是一张新弹层：搜索词从空开始（权威的弹层不保留上次的搜索）。
        if (liveFilter) liveFilter.historyUserFilter = "";
        const rect = filterControl.getBoundingClientRect();
        // **先读列表再弹**：列表来自宿主（`git/authors`），弹早了会显示"没有可选的用户"且不会二次刷新。
        void ensureHistoryAuthors().then(() => {
          showPointerContextMenu(historyUserFilterMenu(), {
            layerClass: 'history-user-filter-menu',
            clientX: Math.round(rect.left),
            clientY: Math.round(rect.bottom + 4),
            maxHeight: 320,
            // 规格 §7.8：窄栏收纳菜单选完项后要把输入焦点交给**相应已有控件**（这里就是它打开的弹层）。
            // 弹层不拿焦点时，键盘用户从收纳菜单回车后焦点会掉到 `body`
            //（第 237 轮实测：summary → Tab → 日期 → Enter，`document.activeElement` 变成 body）。
            focusFirst: true,
          });
        });
        return;
      }
      if (key === 'branch' || key === 'date') {
        // 弹层（权威 `BranchFilterPopupComponent`／`DateFilterPopupComponent`）。位置与关闭规则与其它指针菜单一致。
        closeLiveOverlay();
        const rect = filterControl.getBoundingClientRect();
        showPointerContextMenu(key === 'branch' ? historyBranchFilterMenu() : historyDateFilterMenu(), {
          layerClass: key === 'branch' ? 'history-branch-filter-menu' : 'history-date-menu',
          clientX: Math.round(rect.left),
          clientY: Math.round(rect.bottom + 4),
          maxHeight: 320,
          focusFirst: true,
        });
        return;
      }
      if (key === 'path') {
        // 路径弹层（权威 `StructureFilterPopupComponent.createActionGroup()`）。
        closeLiveOverlay();
        const rect = filterControl.getBoundingClientRect();
        showPointerContextMenu(historyPathFilterMenu(), {
          layerClass: 'history-path-menu',
          clientX: Math.round(rect.left),
          clientY: Math.round(rect.bottom + 4),
          maxHeight: 320,
          focusFirst: true,
        });
      }
      return;
    }

    // 路径弹层：「选择…」= 多行文本框（权威 `EditPathsAction`）、「在树中选择…」= 复选框树
    // （权威 `SelectPathsInTreeAction`）。
    const pathAction = event.target.closest && event.target.closest('[data-log-path-action]');
    if (pathAction) {
      event.preventDefault();
      if (pathAction.dataset.logPathAction === 'select') openHistoryPathTextDialog();
      else openHistoryPathTreeDialog();
      return;
    }

    // 路径弹层里的「最近」条目（权威 `SelectFromHistoryAction`：点哪一条就把整组作为筛选，
    // `KeepPopupOnPerform.Never` ⇒ 点完即关）。
    const pathRecent = event.target.closest && event.target.closest('[data-log-path-recent]');
    if (pathRecent) {
      event.preventDefault();
      const livePath = window.__augitLive;
      const recent = (livePath && livePath.historyPathRecent) || [];
      const group = recent[Number(pathRecent.dataset.logPathRecent)];
      if (Array.isArray(group)) {
        setHistoryPathFilter(group.slice());
        closeLiveOverlay();
        void reloadHistoryKeepingFocus();
      }
      return;
    }

    // 「选择…」对话框的确定／取消（权威 `MultilinePopupBuilder`：按 `\n` 切分、逐项 trim、丢空行；
    // 一个都不剩 ⇒ `setFilter(null)`）。
    const pathText = event.target.closest && event.target.closest('[data-log-path-text]');
    if (pathText) {
      event.preventDefault();
      const layer = pathText.closest('.history-path-text-window');
      const field = layer ? layer.querySelector('[data-log-path-field]') : null;
      const action = pathText.dataset.logPathText;
      const values = action === 'confirm' && field
        ? field.value.split('\n').map((line) => line.trim()).filter(Boolean)
        : [];
      closeLiveOverlay();
      if (action === 'confirm') {
        setHistoryPathFilter(values);
        void reloadHistoryKeepingFocus();
      }
      return;
    }

    // 「在树中选择…」对话框里的勾选行（权威 `VcsStructureChooser` 的 `CheckboxTree`）：
    // 勾选只改这一份待确认的集合，确定时才进筛选；底栏同步 `vcs.log.filters.structure.label`，
    // 并在一个都没勾时禁用确定（`setOKActionEnabled(!mySelectedFiles.isEmpty())`）。
    const pathRow = event.target.closest && event.target.closest('[data-log-path-row]');
    if (pathRow) {
      event.preventDefault();
      const layer = pathRow.closest('.history-path-tree-window');
      const checked = pathRow.getAttribute('aria-checked') === 'true';
      pathRow.setAttribute('aria-checked', checked ? 'false' : 'true');
      const box = pathRow.querySelector('.fake-check');
      if (box) box.classList.toggle('checked', !checked);
      if (layer) {
        const count = layer.querySelectorAll('[data-log-path-row][aria-checked="true"]').length;
        const help = layer.querySelector('.dialog-footer .footer-help');
        if (help) help.textContent = `已选择: ${count}`;
        const confirm = layer.querySelector('[data-log-path-tree="confirm"]');
        if (confirm) {
          confirm.disabled = count === 0;
          if (count === 0) confirm.setAttribute('aria-disabled', 'true');
          else confirm.removeAttribute('aria-disabled');
        }
      }
      return;
    }

    // 「在树中选择…」的确定／取消：确定时把勾选集合作为筛选（权威 `VcsLogFilterObject.fromVirtualFiles(...)`）。
    const pathTree = event.target.closest && event.target.closest('[data-log-path-tree]');
    if (pathTree) {
      event.preventDefault();
      const layer = pathTree.closest('.history-path-tree-window');
      const values = layer
        ? [...layer.querySelectorAll('[data-log-path-row][aria-checked="true"]')].map((row) => row.dataset.logPathRow)
        : [];
      const action = pathTree.dataset.logPathTree;
      closeLiveOverlay();
      if (action === 'confirm') {
        setHistoryPathFilter(values);
        void reloadHistoryKeepingFocus();
      }
      return;
    }

    // 「分支」筛选弹层里的条目（权威 `vcs.log.filter.all` = "All" ＋ 分支列表）。
    const branchFilterItem = event.target.closest && event.target.closest('[data-history-branch]');
    if (branchFilterItem) {
      event.preventDefault();
      setHistoryBranchFilter(branchFilterItem.dataset.historyBranch);
      closeLiveOverlay();
      void reloadHistoryKeepingFocus();
      return;
    }

    // 日期筛选弹层里的预设（权威 `DateFilterPopupComponent` 的 `DateAction`：
    // `fromDates(now-1d/now-7d, null)`，即只给 `since`）。
    const dateItem = event.target.closest && event.target.closest('[data-history-date]');
    if (dateItem) {
      event.preventDefault();
      if (dateItem.dataset.historyDate === 'select') {
        // 权威 `SelectAction`：打开期间对话框（两端都空时不设筛选）。
        openHistoryDateRangeDialog();
        return;
      }
      const days = dateItem.dataset.historyDate === 'last-day' ? 1 : 7;
      const since = new Date(Date.now() - days * 24 * 60 * 60 * 1000);
      setHistoryDateFilter(since.toISOString(), null);
      closeLiveOverlay();
      void reloadHistoryKeepingFocus();
      return;
    }

    // 用户筛选弹层的复选行与全选/全不选（权威 `MultipleValueFilterPopupComponent`）。
    const userItem = event.target.closest && event.target.closest('[data-history-user]');
    if (userItem) {
      event.preventDefault();
      closeLiveOverlay();
      const liveUser = window.__augitLive;
      const current = ((liveUser && liveUser.historyFilter && liveUser.historyFilter.authors) || []).slice();
      const key = userItem.dataset.historyUser;
      const next = current.includes(key) ? current.filter((value) => value !== key) : [...current, key];
      setHistoryAuthorFilter(next);
      void reloadHistoryKeepingFocus();
      return;
    }
    const userAll = event.target.closest && event.target.closest('[data-history-user-all], [data-history-user-none]');
    if (userAll) {
      event.preventDefault();
      closeLiveOverlay();
      const liveUser = window.__augitLive;
      const authors = (liveUser && liveUser.historyAuthors) || [];
      const values = userAll.dataset.historyUserAll ? authors.map((author) => author.email || author.name) : [];
      setHistoryAuthorFilter(values);
      void reloadHistoryKeepingFocus();
      return;
    }

    // 「选择期间…」对话框的确定／取消（本地零点；本地日期 → ISO，宿主按 ISO 解析）。
    const dateRange = event.target.closest && event.target.closest('[data-history-date-range]');
    if (dateRange) {
      event.preventDefault();
      const layer = dateRange.closest('.history-date-range-window');
      const read = (name) => {
        const input = layer ? layer.querySelector(`[data-history-date-field="${name}"]`) : null;
        return input && input.value ? new Date(`${input.value}T00:00:00`) : null;
      };
      const since = read('since');
      const until = read('until');
      const action = dateRange.dataset.historyDateRange;
      closeLiveOverlay();
      if (action === 'confirm' && (since || until)) {
        setHistoryDateFilter(since ? since.toISOString() : null, until ? until.toISOString() : null);
        void reloadHistoryKeepingFocus();
      }
      return;
    }

    // 空态里的「重置筛选」（权威 `vcs.log.reset.filters.status.action` = "Reset filters"）。
    const resetFilters = event.target.closest && event.target.closest('[data-history-action="reset-filters"]');
    if (resetFilters) {
      event.preventDefault();
      resetHistoryFilters();
      void reloadHistoryKeepingFocus();
      return;
    }

    // 引用树分组头（权威标准 JTree）：单击折叠/展开该组；状态记在 `live.logRefCollapsed`。
    // 类型分组与前缀分组都带 `data-ref-collapse-key`（`[data-ref-group]` 只有类型分组）。
    const refGroup = event.target.closest
      && event.target.closest('.log-ref-panel .tree-row.group-row[data-ref-collapse-key]');
    if (refGroup) {
      event.preventDefault();
      toggleRefGroup(refGroup.dataset.refCollapseKey);
      return;
    }

    // 引用树选中（权威 `BranchesTreeSelection`）：单击本身只改选中。
    // 但「单击时」的**行为开关**打开后单击要顺带执行该行为（权威
    // `BranchesDashboardTreeController.init` 的 `TreeSelectionListener`：`selectionAction` 为
    // `FILTER` 就 `updateLogBranchFilter()`、为 `NAVIGATE` 就 `navigateTo(...)`）。
    // 双击/回车恒为「筛选到该分支」（`Git.Log.Branches.Change.Branch.Filter`，注册为
    // `button1 doubleClick` ＋ `ENTER`，见 `intellij.vcs.git.backend.xml:130-134`）。
    const refRow = event.target.closest
      && event.target.closest('.log-ref-panel .tree-row[data-ref-name]');
    if (refRow) {
      event.preventDefault();
      selectRefRow(refRow, {
        toggle: event.ctrlKey || event.metaKey,
        range: event.shiftKey,
      });
      // 权威的选择监听对**任何**选择变化都触发（含 Ctrl/Shift 多选）：
      // FILTER ⇒ 用整个选中集刷日志筛选；NAVIGATE ⇒ 定位到选中集里第一个可导航的引用。
      const liveNow = window.__augitLive;
      const selectionAction = (liveNow && liveNow.logRefSelectionAction) || null;
      if (selectionAction === "filter") {
        if (filterLogToRefRows(selectedRefRows())) void reloadHistoryKeepingFocus();
      } else if (selectionAction === "navigate") {
        void locateSelectedRef();
      }
      return;
    }

    // 分支/标签行动作菜单（权威 `GitBranchPopupActions` 的每引用动作组）。
    const refMenuAction = event.target.closest && event.target.closest('.ref-menu [data-ref-action]');
    if (refMenuAction) {
      event.preventDefault();
      const menu = refMenuAction.closest('.ref-menu');
      const name = menu ? menu.dataset.refName : null;
      const kind = menu ? menu.dataset.refKind : null;
      const action = refMenuAction.dataset.refAction;
      // 引用树行的菜单带着**整个选中集**（`data-ref-targets`）；分支浮层的行菜单只有这一行。
      let targets = [{ name, kind: kind || 'branch' }];
      if (menu && menu.dataset.refTargets) {
        try {
          targets = normalizeRefTargets(JSON.parse(menu.dataset.refTargets));
        } catch (error) {
          targets = [{ name, kind: kind || 'branch' }];
        }
      }
      if (action === 'compare') {
        closeLiveOverlay();
        void openBranchComparison(name);
        return;
      }
      // 「比较分支」（权威 `ShowArbitraryBranchesDiffAction` → `compareAny(b1, b2)`）：
      // 配对从**选中集**里取（`getBranchPair()`）：两个分支 ⇒ `b2..b1`；1 分支 + HEAD ⇒ `当前..该分支`。
      if (action === 'compare-branches') {
        closeLiveOverlay();
        const headPicked = targets.some((item) => item.kind === 'head');
        const picked = targets.filter((item) => item.kind === 'branch' || item.kind === 'remote');
        if (headPicked && picked.length === 1) {
          void openBranchComparison(picked[0].name);
        } else if (picked.length === 2) {
          void openBranchComparison(picked[0].name, picked[1].name);
        }
        return;
      }
      if (action === 'update-selected') {
        closeLiveOverlay();
        void updateSelectedBranch(targets.filter((item) => item.kind !== 'tag').map((item) => item.name));
        return;
      }
      if (name && action === 'checkout') {
        closeLiveOverlay();
        void checkoutReference(name, kind || 'branch');
        return;
      }
      if (name && action === 'delete') {
        openRefDeleteConfirm(targets, false);
        return;
      }
      if (name && action === 'rename') {
        closeLiveOverlay();
        // 权威 `GitRenameBranchAction`：重命名的是**这一行**那个分支，不是"当前分支"。
        openBranchActionDialog('rename', { from: name });
        return;
      }
    }

    const refDeleteCancel = event.target.closest && event.target.closest('[data-ref-delete-cancel]');
    if (refDeleteCancel) {
      event.preventDefault();
      closeLiveOverlay();
      openBranchesPopover();
      return;
    }

    const refDeleteConfirm = event.target.closest && event.target.closest('[data-ref-delete-confirm]');
    if (refDeleteConfirm) {
      event.preventDefault();
      const layer = refDeleteConfirm.closest('.ref-delete-window');
      const name = layer ? layer.dataset.refName : null;
      const kind = layer ? layer.dataset.refKind : null;
      const force = refDeleteConfirm.dataset.refDeleteConfirm === 'force';
      // 多选删除时确认层带着**整个目标集**（`data-ref-targets`），单选时退回该行的身份。
      let targets = [{ name, kind }];
      if (layer && layer.dataset.refTargets) {
        try {
          targets = normalizeRefTargets(JSON.parse(layer.dataset.refTargets));
        } catch (error) {
          targets = [{ name, kind }];
        }
      }
      closeLiveOverlay();
      void runRefDelete(targets, force);
      return;
    }

    // 日志右键菜单的动作（第 167 轮接线）：只接有能力的两项，其余在模板里就是禁用＋原因的 `span`，
    // 因此这里不会收到它们的点击（KISS：不在处理端再判一次）。
    const logMenuAction = event.target.closest && event.target.closest('.log-menu [data-log-action]');
    if (logMenuAction) {
      event.preventDefault();
      const action = logMenuAction.dataset.logAction;
      const menu = logMenuAction.closest('.log-menu');
      const fullHash = menu ? menu.dataset.commitFullHash : null;
      closeLiveOverlay();
      if (action === 'copy-hash' && fullHash) {
        // 与"复制路径"同一条宿主通道：WebView2 里页面自己写剪贴板会静默失败。
        void invoke('clipboard/write', { text: fullHash }, 10000).then((result) => {
          if (result && result.copied) window.__augitCopiedHash = fullHash;
        }).catch(() => {});
        return;
      }
      if (action === 'new-branch') {
        // 权威 `Git.CreateNewBranch.FromCommit`：从**选中的提交**起分支，不是 HEAD。
        openBranchActionDialog('create', { startPoint: fullHash });
        return;
      }
      if (action === 'new-tag') {
        // 权威 `Git.CreateNewTag` → `GitCreateTagAction`：单字段输入框，标签打在选中的提交上。
        openTagDialog(fullHash);
        return;
      }
    }

    // 提交历史工具栏「搜索提交」（规格 §7.8）：把焦点交给日志搜索框。
    // 核实过：`history-utility`（该按钮的类）在 live-data.js 里此前**完全没有出现** ⇒ 死入口。
    const historySearchEntry = event.target.closest
      && event.target.closest('[aria-label="搜索提交"]');
    if (historySearchEntry) {
      event.preventDefault();
      const input = historySearchEntry.closest(".log-filterbar")
        ? historySearchEntry.closest(".log-filterbar").querySelector(".history-search input")
        : document.querySelector(".history-search input");
      if (input) input.focus({ preventScroll: true });
      return;
    }

    // 文件历史「清除路径筛选」入口（规格 §7.9）。
    const clearHistoryPath = event.target.closest
      && event.target.closest('.history-tool-content [aria-label="清除路径筛选"]');
    if (clearHistoryPath) {
      event.preventDefault();
      void clearHistoryPathFilter();
      return;
    }

    // 提交框的「上一次提交」入口（规格 §7.6 第 10 条）：视觉稿里它是
    // `<a class="commit-last" href="git-history.html" title="上一次提交">`，语义就是打开 Git 历史工具窗口。
    // 实时外壳此前没有处理者 ⇒ 被"未接线兜底"拦成死入口。这里复用「打开日志视图」的同一条路径：
    // 日志已经可见时不重绘（避免把用户当前选中行重置），因此重复点击也不会折叠底部区域。
    const lastCommitEntry = event.target.closest
      && event.target.closest('.commit-box .commit-last[href$="git-history.html"]');
    if (lastCommitEntry) {
      event.preventDefault();
      const liveEntry = window.__augitLive;
      const entryLayout = liveEntry ? liveEntry.layout : null;
      const entryVisible = entryLayout && entryLayout.bottom === "git" && entryLayout.collapsed !== "bottom"
        && !!document.querySelector(".log-list-panel .commit-list");
      if (liveEntry && entryLayout && !entryVisible) {
        liveEntry.layout.userDriven = true;
        liveEntry.layout.bottom = "git";
        liveEntry.layout.collapsed = null;
        void (liveEntry.history ? Promise.resolve() : loadHistory().catch(() => null))
          .then(() => refresh("bottomTool", "statusbar"));
      }
      return;
    }

    // 底部「日志」标签：从文件历史切回日志（规格 §7.9「点击底部『日志』标签与清除路径筛选入口
    // **均**恢复进入前的日志上下文」）。视觉稿里它是 `<a class="tool-tab" href="git-history.html">`，
    // 实时外壳此前没有任何处理者 —— 实测点下去 URL 不变、文件历史照旧（被未接线兜底拦成死入口）。
    // 两条入口的结果是同一个：放弃这次文件历史、恢复进入前的底部上下文，故复用同一条路径。
    const historyLogTab = event.target.closest
      && event.target.closest('.bottom-header .tool-tab[href$="git-history.html"]');
    if (historyLogTab) {
      event.preventDefault();
      // 「比较」也是底部工具窗口的一个标签（与「历史: <文件>」同构）⇒ 点「日志」同样恢复进入前的上下文。
      const liveNow = window.__augitLive;
      void (liveNow && liveNow.branchComparison ? closeBranchComparison() : clearHistoryPathFilter());
      return;
    }

    // 「与当前分支比较」视图的关闭叉与刷新（权威是关闭/刷新那个比较日志标签）。
    const branchCompareClose = event.target.closest && event.target.closest('[data-branch-compare-close]');
    if (branchCompareClose) {
      event.preventDefault();
      void closeBranchComparison();
      return;
    }

    const branchCompareRefresh = event.target.closest && event.target.closest('[data-branch-compare-refresh]');
    if (branchCompareRefresh) {
      event.preventDefault();
      const liveNow = window.__augitLive;
      const compare = liveNow && liveNow.branchComparison;
      if (compare) void openBranchComparison(compare.branch);
      return;
    }

    // 提交历史工具栏的「显示/隐藏提交详情」（规格 §7.8）：切换底部日志详情区的显隐。
    // 此前该入口有 aria-label、有图形，却没有绑定（点击无任何效果）。
    const historyDetails = event.target.closest
      && event.target.closest('[aria-label="显示提交详情"], [aria-label="隐藏提交详情"]');
    if (historyDetails && historyDetails.closest(".history-tool-content")) {
      // 文件历史右侧详情（规格 §7.9 第七条）：隐藏时取消在途查询与排版，重显按当前选择补查，
      // 已经完成的比较保留正文、显示模式与阅读位置。
      event.preventDefault();
      const liveFileHistory = window.__augitLive;
      if (!liveFileHistory) return;
      const hidden = !liveFileHistory.fileHistoryDetailsHidden;
      // 隐藏前先记下阅读位置（display:none 会让子树重新显示后 scrollTop 归零）。
      if (hidden) rememberFileHistoryPreviewScroll();
      liveFileHistory.fileHistoryDetailsHidden = hidden;
      applyFileHistoryDetailsState();
      if (hidden) cancelFileHistoryPreviewRender();
      else { ensureFileHistoryPreview(); restoreFileHistoryPreviewScroll(); }
      return;
    }
    if (historyDetails) {
      event.preventDefault();
      const liveNow = window.__augitLive;
      const panel = document.querySelector(".log-detail-panel");
      // 以**状态**为准（DOM 只读用来兜底没有 live 的场景）：隐藏 ⇄ 显示的意图写进 `live`，
      // 这样底部工具窗重绘后仍然成立（规格 §7.9 条目三"详情显隐"）。
      if (liveNow) {
        liveNow.historyDetailsHidden = panel ? panel.style.display !== "none" : !liveNow.historyDetailsHidden;
      } else if (panel) {
        panel.style.display = panel.style.display === "none" ? "" : "none";
        historyDetails.setAttribute("aria-label", panel.style.display === "none" ? "显示提交详情" : "隐藏提交详情");
        historyDetails.setAttribute("aria-pressed", panel.style.display === "none" ? "true" : "false");
        return;
      }
      applyHistoryDetailsState();
      return;
    }

    // 终端标题栏「更多操作」（规格 §7.16）：菜单提供「切换 Shell 配置」与「在外部终端打开」。
    const terminalMore = event.target.closest
      && event.target.closest('.terminal-header [aria-label="更多操作"]');
    if (terminalMore) {
      event.preventDefault();
      const rect = terminalMore.getBoundingClientRect();
      // 注意 `showPointerContextMenu(menuMarkup, …)` 收的是 **markup 字符串**（内部走 template.innerHTML）：
      // 传 DOM 元素会被字符串化，`firstElementChild` 为 null 后**静默返回 null**（第一次实测菜单没出现就是这个原因）。
      const menuMarkup = '<section class="popover context-menu">'
        + '<a class="menu-item" href="#" data-terminal-more="settings">切换 Shell 配置</a>'
        + '<a class="menu-item" href="#" data-terminal-more="external">在外部终端打开</a>'
        + '</section>';
      showPointerContextMenu(menuMarkup, {
        layerClass: "terminal-more-menu",
        clientX: rect.left,
        clientY: rect.bottom,
        maxHeight: 200,
      });
      return;
    }

    const terminalMoreAction = event.target.closest && event.target.closest("[data-terminal-more]");
    if (terminalMoreAction) {
      event.preventDefault();
      const kind = terminalMoreAction.dataset.terminalMore;
      closeLiveOverlay();
      if (kind === "settings") {
        openSettingsDialog();
        switchSettingsPage("terminal");
      } else {
        const live = window.__augitLive;
        const root = (live && (live.workspaceRoot || live.statusRoot || live.repoRoot))
          || (live && live.tree && live.tree.root) || "";
        void launchExternal("terminal", root);
      }
      return;
    }

    // 设置对话框的动作由 `bindSettingsSave()` 单独负责（保存失败要留在对话框里显示原因）。
    // 这里曾经再挂一条 `[data-settings-action]` 路径：它会在 finally 里**无条件关闭**对话框，
    // 于是保存失败时用户什么都看不到（实测：open=false、原因只在 __augitError 里），
    // 而且同一次点击会写两遍设置。

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

    // 打开工作区页（产品规格 §2）：最近目录条目与两个入口。
    const workspaceRow = event.target.closest && event.target.closest("[data-workspace-path]");
    if (workspaceRow) {
      event.preventDefault();
      void openWorkspacePath(workspaceRow.dataset.workspacePath);
      return;
    }

    const workspaceAction = event.target.closest && event.target.closest("[data-workspace-action]");
    if (workspaceAction) {
      event.preventDefault();
      const action = workspaceAction.dataset.workspaceAction;
      if (action === "pick") {
        void pickWorkspaceAndOpen();
      } else if (action === "clone") {
        openCloneDialog();
      } else if (action === "cancel") {
        closeLiveOverlay();
        restoreDialogFocus();
      }
      return;
    }

    // 远端管理页（规格 §7.11）：选择条目与编辑字段都进状态。
    const remoteRow = event.target.closest && event.target.closest("[data-remote-index]");
    if (remoteRow) {
      event.preventDefault();
      const live = window.__augitLive;
      const index = Number(remoteRow.dataset.remoteIndex);
      if (live && Number.isInteger(index)) {
        live.selectedRemoteIndex = index;
        live.remoteDraft = null;
        renderRemoteManager();
      }
      return;
    }

    // 管理页工具栏（三个管理页共用）：新建 / 删除 / 刷新。
    const mgmtAction = event.target.closest && event.target.closest("[data-mgmt-action]");
    if (mgmtAction) {
      event.preventDefault();
      const kind = currentManagementKind();
      const action = mgmtAction.dataset.mgmtAction;
      window.__augitMgmtTrace = (window.__augitMgmtTrace || []).concat([[kind, action]]);
      if (action === "refresh") {
        void refreshManagementList(kind);
      } else if (action === "new") {
        startManagementCreate(kind);
      } else if (action === "delete") {
        if (kind === "remote") void deleteRemoteFromManager();
        else if (kind === "stash") openStashDropConfirm();
        else if (kind === "worktrees") openWorktreeRemoveConfirm();
      }
      return;
    }

    // Worktree 管理页（规格 §5.3 / §10.4）。
    const worktreeRow = event.target.closest && event.target.closest("[data-worktree-index]");
    if (worktreeRow) {
      event.preventDefault();
      const live = window.__augitLive;
      const index = Number(worktreeRow.dataset.worktreeIndex);
      if (live && Number.isInteger(index)) {
        live.selectedWorktreeIndex = index;
        live.worktreeRemoval = null;
        void loadWorktreeRemoval()
          .then(() => renderWorktreeManager())
          .catch(() => null);
      }
      return;
    }

    const worktreeCancel = event.target.closest && event.target.closest("[data-worktree-remove-cancel]");
    if (worktreeCancel) {
      event.preventDefault();
      closeWorktreeRemoveConfirm();
      return;
    }

    const worktreeConfirm = event.target.closest && event.target.closest("[data-worktree-remove-confirm]");
    if (worktreeConfirm) {
      event.preventDefault();
      closeWorktreeRemoveConfirm();
      void runWorktreeRemove();
      return;
    }

    const worktreeButton = event.target.closest && event.target.closest("[data-wtm-action]");
    if (worktreeButton) {
      event.preventDefault();
      const action = worktreeButton.dataset.wtmAction;
      if (action === "close") {
        closeLiveOverlay();
        restoreDialogFocus();
      } else if (action === "open") {
        void openWorktreeWindow();
      } else if (action === "create") {
        openWorktreeDialog();
      } else if (action === "remove") {
        openWorktreeRemoveConfirm();
      }
      return;
    }

    // Smart Checkout 对话框（权威 `GitSmartOperationDialog`）：取消（默认焦点，绝不执行 Git）/ 执行。
    const smartAction = event.target.closest && event.target.closest("[data-smart-action]");
    if (smartAction) {
      event.preventDefault();
      if (smartAction.dataset.smartAction === "smart") void submitSmartCheckout();
      else closeLiveOverlay();
      return;
    }

    // 「创建 Git 仓库」对话框（权威 `GitInit`）：选择目录 / 创建 / 已在 Git 下的继续 / 取消。
    const repoInitAction = event.target.closest && event.target.closest("[data-repo-init-action]");
    if (repoInitAction) {
      event.preventDefault();
      const action = repoInitAction.dataset.repoInitAction;
      if (action === "pick") void pickRepositoryInitTarget();
      else if (action === "create") void submitRepositoryInit();
      else if (action === "confirm") void confirmRepositoryInit();
      else closeLiveOverlay();
      return;
    }

    // Stash 对话框（规格 §5.3）：取消 / 创建。
    const stashCreateAction = event.target.closest && event.target.closest("[data-stash-create-action]");
    if (stashCreateAction) {
      event.preventDefault();
      if (stashCreateAction.dataset.stashCreateAction === "create") {
        void submitStashDialog();
      } else {
        void cancelStashDialog();
      }
      return;
    }

    // Stash 管理页（规格 §7.11 / §10.4）。
    const stashRow = event.target.closest && event.target.closest("[data-stash-index]");
    if (stashRow) {
      event.preventDefault();
      const live = window.__augitLive;
      const index = Number(stashRow.dataset.stashIndex);
      if (live && Number.isInteger(index)) {
        live.selectedStashIndex = index;
        live.stashFiles = null;
        void loadStashFiles().then(() => renderStashManager()).catch(() => null);
      }
      return;
    }

    const stashCancel = event.target.closest && event.target.closest("[data-stash-cancel]");
    if (stashCancel) {
      event.preventDefault();
      closeStashDropConfirm();
      return;
    }

    const stashConfirm = event.target.closest && event.target.closest("[data-stash-confirm]");
    if (stashConfirm) {
      event.preventDefault();
      closeStashDropConfirm();
      void runStashAction(stashConfirm.dataset.stashConfirm);
      return;
    }

    const stashAction = event.target.closest && event.target.closest("[data-stash-action]");
    if (stashAction) {
      event.preventDefault();
      const action = stashAction.dataset.stashAction;
      if (action === "close") {
        closeLiveOverlay();
        restoreDialogFocus();
      } else if (action === "drop") {
        openStashDropConfirm();
      } else if (action === "view") {
        void openStashContentDiff();
      } else {
        void runStashAction(action);
      }
      return;
    }

    // 文件超限/二进制页的「使用系统默认程序打开」（规格 §7.5）。
    // 「结果过多」对话框（权威 `UsageLimitUtil.showTooManyUsagesWarning`）。
    const searchLimit = event.target.closest && event.target.closest("[data-search-limit-action]");
    if (searchLimit) {
      event.preventDefault();
      if (searchLimit.dataset.searchLimitAction === "continue") void continueLimitedSearch();
      else abortLimitedSearch();
      return;
    }

    // 大文件只读预览的通知面板（权威 `EditorNotificationPanel` 的「隐藏通知」/「不再显示」）。
    const largeFileAction = event.target.closest && event.target.closest("[data-large-file-action]");
    if (largeFileAction) {
      event.preventDefault();
      const live = window.__augitLive;
      const document_ = live && live.document;
      if (largeFileAction.dataset.largeFileAction === "disable") {
        // 「不再显示」：写进设置（权威写 `PropertiesComponent` 的 DISABLE_KEY）。
        if (live) {
          live.hideLargeFileWarning = true;
          live.settings = live.settings || {};
          live.settings.hideLargeFileWarning = true;
        }
        void invoke("settings/write", { hideLargeFileWarning: true }, 15000).catch(() => null);
      } else if (live && document_ && document_.path) {
        // 「隐藏通知」：只记在本次会话（权威的 HIDDEN_KEY 记在编辑器上）。
        live.hiddenLargeFileWarnings = live.hiddenLargeFileWarnings || new Set();
        live.hiddenLargeFileWarnings.add(document_.path);
      }
      refreshAfterEvent("editorContent");
      return;
    }

    const externalOpen = event.target.closest && event.target.closest("[data-external-open]");
    if (externalOpen) {
      event.preventDefault();
      const live = window.__augitLive;
      if (live && live.document) void launchExternal("open", live.document.path);
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
        void commitSelectedChanges(false, event);
        return;
      }

      if (link.textContent.includes("提交并推送")) {
        // 推送尚未接线，先完成提交并如实说明。
        void commitSelectedChanges(true, event);
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
async function commitSelectedChanges(andPush, event, confirmed = false) {
  const live = window.__augitLive;
  if (!live || !live.status) return;
  const files = live.status.files || [];
  const selected = files.filter((file) => file.checked);
  const message = typeof live.commitDraft === "string" ? live.commitDraft.trim() : "";
  // 无勾选 = 权威的 `hasDiffs() === false`。权威此时**按钮本身就是禁用的**
  // （`CommitChangeListDialog.java:602-604,616-618`）；Augit 的禁用由渲染侧负责，
  // 这里只保留一次兜底说明，避免"点了没反应"。
  if (selected.length === 0) {
    window.__augitCommitError = describeFailure("请至少选择一个要提交的文件。", {
      unchanged: "工作区没有变化。",
      next: "在改动列表里勾选要提交的文件。",
    });
    refreshAfterEvent("side");
    return;
  }

  // 空提交信息**不是阻断**（第 135 轮采集，见 docs/nui-behavior/12-commit-changes.md）：
  // 权威 `SingleChangeListCommitWorkflowHandler.kt:117-122` 是
  // `getCommitMessage().isNotEmpty() || ui.confirmCommitWithEmptyMessage()` ——
  // **弹确认、确认后照常提交**。原实现是"设错误后 return，永不提交"，属旧交互。
  // 这里同时拦下冒泡：共享绑定（`mockup.js` 的 bindInteractions）里还有一道同样的门，
  // 不拦会让两处各弹一次。
  if (message.length === 0 && !confirmed) {
    if (event) {
      event.preventDefault();
      event.stopPropagation();
    }
    confirmCommitWithEmptyMessage(() => { void commitSelectedChanges(andPush, null, true); },
      () => {
        const field = document.querySelector(".commit-box .message-field, .commit-box textarea");
        if (field) field.focus({ preventScroll: true });
      });
    return;
  }

  // 进行中：禁用重复触发（规格 §9.3），并记录当前动作供界面显示。
  if (live.writeOperation) return;
  live.writeOperation = "提交";
  // 提交动作由提交区触发：权威把焦点留在提交信息编辑器上（用户接着看结果或改信息），
  // 因此这次重绘要把焦点还回输入框（`restoreChangesState()` 读这个一次性信号）。
  // 不依赖"点击后 activeElement 在哪"——按钮是否吃焦点因浏览器与控件类型而异。
  live.commitFocusBeforeRender = true;
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
  // 提交成功等于"新面板"：Amend 的初始信息基线与成对草稿一并重置，否则下一次勾选会把
  // 提交前那条旧信息当成 `initialMessage`（权威每次 `activate()` 重设 `initialMessage`）。
  amendInitialMessages.clear();
  amendDrafts.clear();
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
  // 规格 §7.10：打开分支弹层后**搜索框自动获得焦点**。
  // 实测（第 65 轮 harness）修复前什么都不聚焦，焦点留在被点击的分支芯片 <a> 上 ——
  // 弹层虽然开了，键盘用户却要先 Tab 才能搜索，与条文不符。
  const branchSearch = layer.querySelector("input.search-field");
  if (branchSearch) branchSearch.focus({ preventScroll: true });
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
    // 权威 `GitCheckoutOperation.smartCheckoutOrNotify`（`:367-395`）：普通检出被**本地改动／未跟踪文件**
    // 挡住时，不给一句"失败"，而是列出受影响文件并提供 Smart Checkout。宿主把 git 的那条错误
    // 解析成 `overwriteRisk` / `overwritePaths`（`ShellBridge.ParseCheckoutOverwritePaths`）。
    const overwritePaths = result && Array.isArray(result.overwritePaths) ? result.overwritePaths : [];
    if (result && result.overwriteRisk === true && overwritePaths.length > 0) {
      window.__augitCheckoutError = null;
      openSmartCheckoutDialog(name, kind || "branch", overwritePaths);
      return;
    }

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
/** 重命名窗口的目标分支（来自某一行的动作菜单时是那一行，否则是当前分支）。 */
let compactDialogFrom = null;
/** 新建分支窗口的起点提交（日志右键菜单用；空串即当前 HEAD）。 */
let compactDialogStartPoint = null;

/** 显示紧凑输入窗口（标题、字段标签、初值、确认按钮文案）。 */
function showCompactInput(title, label, value, confirmLabel) {
  const host = document.querySelector(".augit-window");
  if (!host) return;
  document.querySelectorAll("[data-augit-overlay].live-overlay").forEach((node) => node.remove());
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = compactInputDialog(title, label, value, confirmLabel);
  host.appendChild(layer);
  const field = layer.querySelector("[data-compact-field]");
  if (field) {
    field.focus();
    field.select();
  }
}

function openBranchActionDialog(kind, context) {
  const live = window.__augitLive;
  if (!live) return;
  closeLiveOverlay();
  rememberDialogFocus();
  const branches = (live.references && live.references.branches) || [];
  const fromName = (context && context.from) || currentBranchName();
  const target = branches.find((item) => item.name === fromName) || null;
  compactDialogKind = kind;
  compactDialogFrom = target ? target.name : null;
  compactDialogStartPoint = (context && context.startPoint) || null;
  if (kind === "rename" && !target) {
    window.__augitCheckoutError = "找不到要重命名的分支。";
    openBranchesPopover();
    return;
  }

  const title = kind === "create" ? "新建分支" : "重命名分支";
  const label = kind === "create" ? "分支名" : "新名称";
  const value = kind === "rename" ? (target ? target.name : "") : "";
  showCompactInput(title, label, value, kind === "create" ? "创建" : "重命名");
}

/**
 * 在某个提交上新建标签（日志右键菜单，权威 `Git.CreateNewTag` → `GitCreateTagAction`）。
 *
 * 权威用的是 `Messages.showInputDialog` —— **单字段**：标题 `git.new.tag.dialog.title`
 * = `Create New Tag On {0}`（`{0}` 是**选中的提交**，`GitCreateTagAction.java:36-38` 传的是
 * `commit.asString()`）、字段文案 `git.new.tag.dialog.tag.name.label`
 * = `Enter the name of new tag`（`GitBundle.properties:1234-1235`），
 * 校验是"非空且不含空白字符"（同文件 `:42-53` 的 `checkInput`／`canClose`），
 * 确认后在该提交上建**轻量**标签（无 message、无 force）。
 *
 * 注意：四字段的 `GitTagDialog` 是**分支浮层**那条路径（`Git.Tag`），不是这个入口。
 */
function openTagDialog(hash) {
  const live = window.__augitLive;
  if (!live) return;
  closeLiveOverlay();
  rememberDialogFocus();
  compactDialogKind = "tag";
  compactDialogFrom = null;
  compactDialogStartPoint = hash || null;
  showCompactInput(`在 ${hash || "HEAD"} 上新建标签`, "新标签名称", "", "创建");
}

function closeCompactDialog() {
  compactDialogKind = null;
  compactDialogFrom = null;
  compactDialogStartPoint = null;
  closeLiveOverlay();
  restoreDialogFocus();
}

/** 提交紧凑输入窗口；业务校验在调用方做，窗口本身不写 Git。 */
/**
 * 跳转行（产品规格 §3.3 `Ctrl+G`）：把正文滚到目标行、标出该行，并把焦点交给正文。
 *
 * 与视觉稿里"点击 JSON 错误行"同一套动作（`active` 类 + `scrollIntoView` + 正文获得焦点）。
 * 行号非法或超出范围时只在窗口内说明原因：窗口不关闭、不写 Git、不动正文。
 */
function goToLine(text) {
  const dialog = document.querySelector("[data-compact-dialog]");
  const field = dialog && dialog.querySelector("[data-compact-field]");
  const report = (message) => {
    const help = dialog && dialog.querySelector(".footer-help");
    if (help) {
      help.textContent = message;
      help.title = message;
    }
    if (field) field.focus();
    return false;
  };

  const raw = String(text ?? "").trim();
  if (!/^\d+$/.test(raw)) return report("请输入行号（正整数）。");
  const line = Number.parseInt(raw, 10);
  if (line < 1) return report("行号从 1 开始。");

  const view = document.querySelector(".editor-content .code-view");
  if (!view) return report("当前没有可跳转的只读正文。");
  // 优先按 data-line 找；个别正文（Blame 等）没有该属性时按顺序兜底。
  const row = view.querySelector(`.code-line[data-line="${line}"]`)
    || view.querySelectorAll(".code-line")[line - 1]
    || null;
  if (!row) {
    const total = view.querySelectorAll(".code-line").length;
    return report(total > 0 ? `当前文件只有 ${total} 行。` : "当前正文没有可跳转的行。");
  }

  view.querySelectorAll(".code-line.active").forEach((node) => node.classList.remove("active"));
  row.classList.add("active");
  // 权威 `EditorGotoLineNumberDialog.doOKAction()`（platform/platform-impl/src/com/intellij/ide/util/
  // EditorGotoLineNumberDialog.java:50-57）：`getScrollingModel().scrollToCaret(ScrollType.CENTER)`
  // —— 目标行滚到**可视区中部**，不是"够到就停"（Augit 原为 `block:"nearest"`，第 152 轮按权威改正）。
  row.scrollIntoView({ block: "center" });
  closeCompactDialog();
  // 关闭时按规格把焦点交回触发区域；跳转行的"结果区域"就是正文，因此再交给正文。
  view.focus({ preventScroll: true });
  return true;
}

async function submitCompactDialog() {
  const layer = document.querySelector("[data-compact-dialog]");
  const field = layer && layer.querySelector("[data-compact-field]");
  if (!field) return;
  const value = field.value.trim();
  const kind = compactDialogKind;

  // 跳转行只移动正文，不写任何 Git 状态（规格 §5.3：窗口本身不执行 Git 或文件写入）。
  if (kind === "go-to-line") {
    goToLine(value);
    return;
  }

  if (value.length === 0) {
    window.__augitCheckoutError = "名称不能为空。";
    return;
  }

  // 权威对这三个窗口都有"不含空白字符"的前置校验：
  // `GitCreateTagAction.java:42-53`（`!StringUtil.containsWhitespaces`）、
  // `GitReferenceValidator.java`（分支/引用名走 `git rev-parse --verify`，空白名必然不通过）。
  // 宿主 `check-ref-format` 也会拒绝，这里先在窗口内给出原因，不打无谓的 Git 调用。
  if (kind !== "checkout-revision" && /\s/.test(value)) {
    window.__augitCheckoutError = "名称不能包含空白字符。";
    return;
  }

  const live = window.__augitLive;
  let result;
  try {
    if (kind === "checkout-revision") {
      // 标签与任意版本都走 detach 检出，名称由 Git 解析，Augit 不猜。
      result = await invoke("git/checkout", { name: value, kind: "tag" }, 120000);
      result = result ? { changed: result.switched, reason: result.reason } : result;
    } else if (kind === "rename") {
      result = await invoke("git/branch", { action: "rename", from: compactDialogFrom || currentBranchName(), name: value }, 60000);
    } else if (kind === "create") {
      // `startPoint` 来自日志右键菜单（从选中的提交起分支）；芯片里的「新建分支…」不带它 ⇒ 取 HEAD。
      const payload = { action: "create", name: value };
      if (compactDialogStartPoint) payload.startPoint = compactDialogStartPoint;
      result = await invoke("git/branch", payload, 60000);
    } else if (kind === "tag") {
      // 权威 `Git.CreateNewTag`：标签打在**选中的提交**上（`target` 为空时宿主才回退到 HEAD）。
      const payload = { action: "create", name: value };
      if (compactDialogStartPoint) payload.target = compactDialogStartPoint;
      result = await invoke("git/tag", payload, 60000);
    } else {
      // 未知类型绝不能落到某个写操作上（此前 `else` 落在"创建分支"：
      // 确认"跳转行"窗口会去建一个以行号命名的分支）。
      window.__augitError = "submit-compact:unknown-kind:" + String(kind);
      closeCompactDialog();
      return;
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
  // 权威 `UpdateSelectedBranchAction.update()` 用 `GitFetchSupport.isFetchRunning` 判断"获取进行中"；
  // Augit 侧对应 `live.logFetchRunning`（竖条的「获取」「更新选中分支」据此禁用）。
  const liveNow = window.__augitLive;
  if (liveNow) {
    liveNow.logFetchRunning = true;
    if (typeof window.__augitApplyRefSelection === "function") window.__augitApplyRefSelection();
  }
  let result;
  try {
    result = await invoke(method, {}, 300000);
  } catch (error) {
    if (liveNow) liveNow.logFetchRunning = false;
    window.__augitCheckoutError = String(error && error.message || error);
    openBranchesPopover();
    return;
  }

  if (liveNow) {
    liveNow.logFetchRunning = false;
    if (typeof window.__augitApplyRefSelection === "function") window.__augitApplyRefSelection();
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
 * 竖条「更新选中分支」（权威 `UpdateSelectedBranchAction` → `updateBranches()`）。
 *
 * 权威对**非当前**的受跟踪本地分支用 refspec `远端分支:本地分支` 直接快进该分支
 * （`plugins/git4idea/backend/src/ui/branch/GitBranchActionsUtil.kt:93-96`），对当前分支改走
 * "更新方式"（合并或 rebase）—— 那条通道 Augit 尚未提供，因此竖条对当前分支保持禁用，
 * 这里也只接受非当前的受跟踪本地分支（`refStripeState()` 是同一判据，双重保险）。
 */
async function updateSelectedBranch(names) {
  const live = window.__augitLive;
  if (!live) return;
  // 权威 `UpdateSelectedBranchAction.actionPerformed`：`updateBranches(project, repos, branchNames)` ——
  // 对**选中的每个**非当前、有跟踪配置的本地分支按它自己的 refspec 快进。
  const wanted = (Array.isArray(names) ? names : [names]).map((value) => String(value || "")).filter(Boolean);
  const targets = wanted.filter((name) => {
    const branch = ((live.references && live.references.branches) || [])
      .find((item) => !item.isRemote && item.name === name);
    return !!branch && !branch.isCurrent && !!branch.upstream;
  });
  if (targets.length === 0) return;
  live.logFetchRunning = true;
  if (typeof window.__augitApplyRefSelection === "function") window.__augitApplyRefSelection();
  let result;
  try {
    result = await invoke(
      "git/fetch",
      targets.length === 1 ? { branch: targets[0] } : { branches: targets },
      300000);
  } catch (error) {
    live.logFetchRunning = false;
    window.__augitCheckoutError = String(error && error.message || error);
    openBranchesPopover();
    return;
  }

  live.logFetchRunning = false;
  if (!result || !result.fetched) {
    window.__augitCheckoutError = describeFailure((result && result.reason) || "操作失败。", {
      unchanged: "该分支与远端没有可快进的内容。",
      next: "请检查网络、远端配置或该分支的跟踪关系后重试。",
    });
    openBranchesPopover();
    return;
  }

  // 成功：该分支的引用移动了，重读状态与引用（当前分支与工作区不变）。
  live.references = null;
  await Promise.all([
    loadStatus().catch(() => null),
    loadReferences().catch(() => null),
  ]);
  refreshAfterEvent("titlebar", "side", "bottomTool", "statusbar");
}

/**
 * 「与当前分支比较」（权威 `ShowBranchDiffAction` → `GitBrancher.compare` →
 * `GitBranchesUIHandler.compareWithCurrent`）。
 *
 * 权威先取 `currentRef = repositories.getCommonCurrentBranch() ?: GitUtil.HEAD`，再用
 * `VcsLogFilterObject.fromRange(currentRef, branchName)` 打开一个**按范围过滤的日志**
 * （不是文件差异）：范围文本是 `"<currentRef>..<branchName>"`。Augit 侧因此把底部工具窗口
 * 切成"比较"标签，并用 `git/history` 的 `rangeExclusive`/`rangeInclusive` 取这一批提交
 * （宿主解析成 `git log <currentRef>..<branchName>`）。
 */
/**
 * 打开「比较」日志视图（权威 `GitBranchesUIHandler.compare(repos, branchName, otherBranchName)`，
 * `GitBranchesUIHandler.kt:22-27`：`GitCompareBranchesUi` 的构造就是
 * `fromRange(otherBranchName, branchName)`，即范围 **`otherBranchName..branchName`**）。
 *
 * - 单值调用＝「与当前分支比较」（`compareWithCurrent` 把 `otherBranchName` 取成当前分支／`HEAD`）
 *   ⇒ 范围 `当前..选中`；
 * - 两个分支时＝「比较分支」（`ShowArbitraryBranchesDiffAction` → `compareAny(b1, b2)`）
 *   ⇒ `branchName = b1`（选中集里的**第一个**）、`otherBranchName = b2`（第二个），范围 `b2..b1`
 *   —— 方向按 `BranchesTreeSelection.selectedBranches` 的顺序，与权威逐字一致。
 */
// 引用比较的代际：取消或换分支时前进，旧响应据此作废（规格 §7.9 第二十条/第二十一条）。
let branchComparisonToken = 0;

async function openBranchComparison(branchName, otherBranchName = null) {
  const live = window.__augitLive;
  if (!live || !branchName) return null;
  // 引用查询的代际（规格 §7.9 第二十条/第二十一条）：用户点"取消比较"或又比较了别的分支时，
  // 旧响应一律作废，不得把已取消的比较重新填回来。
  const token = ++branchComparisonToken;
  const base = otherBranchName || currentBranchName() || "HEAD";
  // 进入前的底部上下文：与文件历史同一套往返规则（点「日志」标签或关闭比较即恢复）。
  const layout = (typeof currentLayout === "function" ? currentLayout() : null) || live.layout || {};
  const hadBottom = !!layout.bottom && layout.bottom !== "branch-compare";
  // 刷新（已在比较视图里）时**不要**覆盖进入前的上下文，否则"关闭后回到哪里"会被改成 git。
  if (!live.branchComparison) {
    live.branchComparisonReturn = { bottom: hadBottom ? layout.bottom : "git" };
  }
  live.branchComparison = { branch: branchName, base, commits: [], loading: true };
  live.layout = live.layout || {};
  live.layout.userDriven = true;
  live.layout.bottom = "branch-compare";
  live.layout.collapsed = null;
  if (!hadBottom && typeof window.__augitRender === "function") {
    window.__augitRender();
    rebindAfterRender();
  } else {
    refreshAfterEvent("bottomTool", "statusbar");
  }

  const page = await invoke("git/history", {
    rangeExclusive: base,
    rangeInclusive: branchName,
  }, 60000).catch(() => null);
  const liveNow = window.__augitLive;
  if (!liveNow || !liveNow.branchComparison) return null;
  // 用户取消（或期间又比较了别的分支/已经关闭）：只接纳最新一次的结果。
  if (token !== branchComparisonToken) return liveNow.branchComparison;
  if (liveNow.branchComparison.branch !== branchName || liveNow.branchComparison.base !== base) {
    return liveNow.branchComparison;
  }

  const commits = page && page.available && page.isRepository && page.commits ? page.commits : [];
  liveNow.branchComparison = {
    branch: branchName,
    base,
    loading: false,
    reason: commits.length === 0 ? ((page && page.reason) || null) : null,
    commits: commits.map((commit) => ({
      hash: commit.hash,
      fullHash: commit.fullHash,
      subject: commit.subject,
      author: commit.author,
      date: commit.date,
    })),
  };
  refreshAfterEvent("bottomTool", "statusbar");
  return liveNow.branchComparison;
}

/** 关闭「与当前分支比较」的日志视图，恢复进入前的底部上下文（权威是关闭该日志标签）。 */
async function closeBranchComparison() {
  const live = window.__augitLive;
  if (!live) return;
  const back = live.branchComparisonReturn || {};
  live.branchComparison = null;
  live.layout = live.layout || {};
  live.layout.userDriven = true;
  live.layout.bottom = back.bottom || "git";
  live.layout.collapsed = null;
  // 已加载的日志直接恢复，不重复查询（与文件历史同一口径）。
  if (!live.history) await loadHistory().catch(() => null);
  refreshAfterEvent("bottomTool", "statusbar");
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
  // 引用比较的双方引用要同时出现在**标签**和**文件栏**（规格 §7.9 第十四/十七条）：
  // 状态记下实际查询用的修订（完整命名引用），标签拿到结构化三部件。
  live.referenceComparison = { path: target.path, revision: branch };
  const referenceParts = comparisonParts(target.path, branch, "工作区");
  // 与工作区 Diff 同一处理：标签与视图必须在**发请求之前**就位，并调度加载提示。
  // 否则查询期间编辑区还停在上一个视图、也没有加载指示
  // （规格 §7.9 要求"激活时立即打开并显示双方引用及文件路径，查询完成后只填充正文"）。
  const tab = ensureComparisonTab(target.path, title, referenceParts);
  activateComparisonTab(tab);
  const generation = comparisonGeneration;
  scheduleDiffLoadingMarker();
  // 与工作区比较同理：标签与加载指示都要立刻可见，查询完成后只填充正文。
  refreshAfterEvent("editorTabs", "statusbar");
  let succeeded = false;
  let stale = false;
  try {
    const diff = await loadDiff(target.path, { revision: branch, force: true }).catch(() => null);
    // 关闭叉可能发生在查询途中：此时既不能激活标签，也不该记一条"比较失败"的错误。
    stale = !comparisonStillCurrent(tab, generation);
    if (stale) return;
    if (!diff) {
      window.__augitError = "compare-workspace:" + target.path;
      return;
    }

    succeeded = true;
    syncComparisonTab(tab, target.path, title);
    activateComparisonTab(tab);
    refreshAfterEvent("editorContent", "editorTabs", "statusbar");
  } finally {
    if (!stale) clearDiffLoadingMarker();
    // 规格 §7.8：失败的比较**保留标签可重试**（与 openChangeDiff 同一口径）。
    if (!stale && !succeeded) {
      const liveNow = window.__augitLive;
      if (liveNow) {
        liveNow.diffLoading = false;
        liveNow.diffError = { path: target.path, reason: window.__augitError || "读取差异失败" };
      }
      renderDiffErrorNotice();
      refreshAfterEvent("editorContent", "editorTabs", "statusbar");
    }
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
  // 视觉稿的度量函数（字号→`--push-row`／`--push-tags`／`--push-height` 等）此前只在静态页与
  // 后续区域重绘时跑到，从弹层打开的实时 Push 对话框因此拿不到任何 `--push-*` 令牌：
  // 提交行高固定 27px（不随界面字号扩展）、无远端时也不为标签选项预留空间（第 274 轮实测
  // `--push-row` 为空）。这里像 `openCloneDialog()` 一样在挂载后立即量一次。
  if (typeof measurePushDialog === "function") measurePushDialog();
  const list = layer.querySelector(".push-commits");
  if (list) {
    bindPushCommitSelection(list);
    list.focus();
  }
}

/**
 * Push 提交行的选择（规格 §7.12 第 7 条：单击只选中提交，不改变整个引用的推送范围）。
 *
 * 为什么不能整段复用视觉稿的 `bindPushDialog()`：它同时接管推送/取消的执行链，而实时层
 * 已经用 `[data-push-action]` 接了同一条链（`live-data.js` 的处理器），两段一起生效会让
 * 一次点击发出两次 `git/push`。这里只接"选中"这一件事：单击与方向键都只改 `.selected` /
 * `aria-selected` / `aria-activedescendant`，不请求 Git、不重建内容。
 *
 * 此前只有 `scene=push` 静态页绑定了它，从分支弹层打开的真实 Push 对话框（`renderPushDialog()`）
 * 行点了不动（第 274 轮实测：`sel:[false]`、`aria:["false"]`）。
 */
function bindPushCommitSelection(list) {
  if (!list || list.__augitPushSelectionBound) return;
  list.__augitPushSelectionBound = true;
  const items = () => [...list.querySelectorAll(".push-commit:not([hidden])")];
  const select = (index) => {
    const rows = items();
    rows.forEach((row, candidate) => {
      const active = candidate === index;
      row.classList.toggle("selected", active);
      row.setAttribute("aria-selected", String(active));
    });
    list.setAttribute("aria-activedescendant", rows[index] ? rows[index].id : "");
  };
  const disabled = () => list.getAttribute("aria-disabled") === "true";
  list.addEventListener("click", (event) => {
    const row = event.target.closest && event.target.closest(".push-commit");
    if (!row || disabled()) return;
    list.focus({ preventScroll: true });
    select(items().indexOf(row));
  });
  list.addEventListener("keydown", (event) => {
    if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key) || disabled()) return;
    const rows = items();
    if (rows.length === 0) return;
    event.preventDefault();
    const current = rows.findIndex((row) => row.classList.contains("selected"));
    let next = current;
    if (event.key === "ArrowDown") next = Math.min(rows.length - 1, (current < 0 ? -1 : current) + 1);
    else if (event.key === "ArrowUp") next = Math.max(0, (current < 0 ? rows.length : current) - 1);
    else if (event.key === "Home") next = 0;
    else next = rows.length - 1;
    select(next);
    rows[next].scrollIntoView({ block: "nearest" });
  });
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

/**
 * 远端管理（规格 §7.11 / §7.12）。
 *
 * 页面本体用视觉稿的 `liveManagementPage("remote")`（列表 + 可编辑详情 + 删除/保存动作行），
 * 底栏只留「关闭」：此前这里自己写了一份重复的列表与表单，既与视觉稿结构不一致、
 * 也少了设计里的删除动作。
 */
function openRemoteDialog() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  rememberDialogFocus();
  document.querySelectorAll("[data-augit-overlay].live-overlay.remote-window").forEach((node) => node.remove());
  const remotes = (live.remotes && live.remotes.remotes) || [];
  if (!Number.isInteger(live.selectedRemoteIndex) || live.selectedRemoteIndex >= remotes.length) {
    live.selectedRemoteIndex = remotes.length > 0 ? 0 : null;
  }
  live.remoteDraft = null;
  renderRemoteManager();
}

/** 当前打开的管理页类型（远端 / Stash / Worktree），工具栏动作据此分派。 */
function currentManagementKind() {
  if (document.querySelector(".dialog.remote-dialog")) return "remote";
  if (document.querySelector(".dialog.stash-manager-dialog")) return "stash";
  if (document.querySelector(".dialog.worktree-dialog")) return "worktrees";
  return null;
}

/** 管理页工具栏的「刷新」：重新读取真实列表并原位更新。 */
async function refreshManagementList(kind) {
  await loadReferences().catch(() => null);
  if (kind === "remote") {
    renderRemoteManager();
    setRemoteNotice("已刷新远端列表。");
  } else if (kind === "stash") {
    await loadStashFiles().catch(() => null);
    renderStashManager();
    setStashNotice("已刷新 Stash 列表。");
  } else if (kind === "worktrees") {
    await loadWorktreeRemoval().catch(() => null);
    renderWorktreeManager();
    setWorktreeNotice("已刷新 Worktree 列表。");
  }
}

/** 重绘远端管理窗口（保留 Push 窗口本身）。 */
function renderRemoteManager() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  // 按**覆盖层**删除：`dialog()` 自己带一层 [data-augit-overlay]，
  // 只删内层 dialog 会留下空的 .remote-window 外壳，反复重绘就层层累积。
  document.querySelectorAll("[data-augit-overlay].live-overlay.remote-window").forEach((node) => node.remove());
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay remote-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "远端管理",
    liveManagementPage("remote"),
    '<button type="button" class="secondary-button" data-remote-action="cancel">关闭</button>',
    true,
    "remote-dialog");
  host.appendChild(layer);
  setRemoteNotice("");
}

/** 远端管理页内的局部提示（规格 §10.2）。 */
function setRemoteNotice(message) {
  const notice = document.querySelector(".remote-window .remote-notice");
  if (!notice) return;
  notice.hidden = !message;
  notice.textContent = message || "";
  notice.title = message || "";
}

/** 读取当前编辑中的远端草稿（界面字段优先，其次是选中的远端）。 */
function currentRemoteDraft() {
  const live = window.__augitLive;
  const layer = document.querySelector(".remote-window");
  if (!live || !layer) return null;
  const field = (name) => {
    const node = layer.querySelector(`[data-remote-field="${name}"]`);
    return node ? node.value.trim() : "";
  };
  const remotes = (live.remotes && live.remotes.remotes) || [];
  const selected = Number.isInteger(live.selectedRemoteIndex) ? remotes[live.selectedRemoteIndex] || null : null;
  if (!layer.querySelector("[data-remote-field]")) return null;
  return {
    name: field("name"),
    fetchUrl: field("fetchUrl"),
    pushUrl: field("pushUrl"),
    currentName: selected ? selected.name : null,
  };
}

/** 保存远端：没有选中项时按"新增"提交，否则按"更新"提交（名称可改）。 */
async function saveRemoteFromManager() {
  const live = window.__augitLive;
  const draft = currentRemoteDraft();
  if (!live || !draft) return null;
  if (draft.name.length === 0 || draft.fetchUrl.length === 0) {
    setRemoteNotice("名称与获取 URL 都不能为空。");
    return null;
  }

  const payload = draft.currentName
    ? { action: "update", currentName: draft.currentName, name: draft.name, fetchUrl: draft.fetchUrl, pushUrl: draft.pushUrl }
    : { action: "add", name: draft.name, fetchUrl: draft.fetchUrl, pushUrl: draft.pushUrl };
  let result = null;
  let failure = null;
  try {
    result = await invoke("git/remote-write", payload, 60000);
    if (!result || result.changed === false) {
      failure = (result && result.reason) || "远端没有保存成功。";
    }
  } catch (error) {
    failure = String((error && error.message) || error);
  }

  if (failure) {
    setRemoteNotice(describeFailure(failure, {
      unchanged: "远端配置没有变化。",
      next: "可以检查名称与 URL 后重试。",
    }));
    return null;
  }

  window.__augitRemoteSaved = true;
  live.remoteDraft = null;
  await loadReferences().catch(() => null);
  const remotes = (live.remotes && live.remotes.remotes) || [];
  const index = remotes.findIndex((remote) => remote.name === draft.name);
  live.selectedRemoteIndex = index >= 0 ? index : 0;
  renderRemoteManager();
  setRemoteNotice(draft.currentName ? `已保存 ${draft.name}。` : `已新增 ${draft.name}。`);
  // 远端变化会影响推送预览：按已有数据重算，不发起新的远端查询。
  refreshPush();
  return result;
}

/** 删除选中的远端（视觉稿的动作行里就有它，不需要额外确认：不涉及本地数据）。 */
async function deleteRemoteFromManager() {
  const live = window.__augitLive;
  const remotes = (live && live.remotes && live.remotes.remotes) || [];
  const selected = live && Number.isInteger(live.selectedRemoteIndex) ? remotes[live.selectedRemoteIndex] || null : null;
  if (!live || !selected) {
    setRemoteNotice("先选择一个远端。");
    return null;
  }

  let result = null;
  let failure = null;
  try {
    result = await invoke("git/remote-write", { action: "delete", name: selected.name }, 60000);
    if (!result || result.changed === false) {
      failure = (result && result.reason) || "远端没有删除。";
    }
  } catch (error) {
    failure = String((error && error.message) || error);
  }

  if (failure) {
    setRemoteNotice(describeFailure(failure, {
      unchanged: "远端配置没有变化。",
      next: "可以先刷新列表确认远端是否仍存在。",
    }));
    return null;
  }

  window.__augitRemoteDeleted = true;
  live.remoteDraft = null;
  await loadReferences().catch(() => null);
  const remaining = (live.remotes && live.remotes.remotes) || [];
  live.selectedRemoteIndex = remaining.length > 0 ? 0 : null;
  renderRemoteManager();
  setRemoteNotice(`已删除远端 ${selected.name}。`);
  refreshPush();
  return result;
}

/** 管理页工具栏的「新建」：远端切到空白草稿；Stash 与 Worktree 复用各自的新建流程。 */
function startManagementCreate(kind) {
  const live = window.__augitLive;
  if (!live) return;
  if (kind === "remote") {
    live.remoteDraft = { name: "", fetchUrl: "", pushUrl: "" };
    live.selectedRemoteIndex = null;
    renderRemoteManager();
    const field = document.querySelector(".remote-window [data-remote-field=\"name\"]");
    if (field) field.focus({ preventScroll: true });
    return;
  }
  if (kind === "stash") {
    openStashDialog();
    return;
  }
  if (kind === "worktrees") {
    closeLiveOverlay();
    openWorktreeDialog();
  }
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

  if (action === "delete") {
    await deleteRemoteFromManager();
    return;
  }

  // 保存不再关闭窗口：视觉稿把「保存」放在详情动作行里，窗口留在原地显示结果。
  if (action === "save") {
    await saveRemoteFromManager();
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
  // 每次打开都从第一个分类开始；草稿只在**本次打开期间**跨分类保留未保存编辑。
  live.settingsPage = "appearance";
  live.settingsDraft = {};
  // 搜索框也每次从空开始（权威 `SearchTextField` 只记历史、不恢复文本）。
  live.settingsFilter = "";
  live.settingsFilterNoHits = false;
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay settings-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "设置 — Augit",
    liveSettingsBody("appearance"),
    // 底栏照权威 `SettingsDialog.createActions()`：OK ＋ Cancel ＋ Apply（主设置对话框
    // `isApplyButtonNeeded = true`、`isResetButtonNeeded = false`，`SettingsDialog.java:86-96,200-215`）
    // ⇒ 「取消 / 应用 / 确定」，与视觉稿同一组。`apply` 只在有未保存修改时可用
    //（`SettingsEditor.updateStatus()`：`editor.getApplyAction().setEnabled(isModified())`）。
    `<button type="button" class="secondary-button" data-settings-action="cancel">取消</button>`
      + `<button type="button" class="secondary-button" data-settings-action="apply" disabled aria-disabled="true">应用</button>`
      + `<button type="button" class="primary-button" data-settings-action="save">确定</button>`,
    true,
    "settings-dialog");
  host.appendChild(layer);
  // 复用既有的保存动作绑定（它按 .dialog-xl 结构挂载）。
  bindSettingsSave();
  bindSettingsPages();
  void ensureGitDetection();
}

/** 读取一次 Git 检测结果（规格 §7.17 的 Git 分类要显示检测结果与最低版本）。 */
async function ensureGitDetection() {
  const live = window.__augitLive;
  if (!live || live.gitDetection !== undefined) return live ? live.gitDetection : null;
  live.gitDetection = null;
  let detection = null;
  try {
    detection = await invoke("git/detect", {}, 15000);
  } catch (error) {
    detection = { available: false, reason: String((error && error.message) || error) };
  }
  live.gitDetection = detection || { available: false, reason: "无法读取 Git 检测结果。" };
  // 对话框正开在 Git 分类上时补一次重绘，避免一直停在"正在检测…"。
  if (live.settingsPage === "git" && document.querySelector(".settings-window")) renderSettingsDialog();
  return live.gitDetection;
}

/** 收集当前分类里已改动的字段到草稿：切页/保存前都必须先调用，否则切页会丢编辑。 */
function collectSettingsDraft() {
  const live = window.__augitLive;
  if (!live) return;
  const draft = live.settingsDraft || (live.settingsDraft = {});
  for (const element of document.querySelectorAll(".settings-page [data-setting]")) {
    const key = element.dataset.setting;
    // 数字字段必须存成数字：宿主的 GetDouble 只接受 JSON number，
    // 存字符串会被静默忽略（字号看起来保存成功、实际没生效）。
    if (element.type === "number") {
      const value = Number(element.value);
      if (Number.isFinite(value)) draft[key] = value;
    } else {
      draft[key] = element.value;
    }
  }
}

/**
 * 设置窗口的导航与字段联动（规格 §7.17）。
 *
 * 文档级委托 + 幂等守卫：设置对话框会被区域刷新整块替换，
 * 挂节点的监听会随节点消失（同 `bindToolRail`/`bindSettingsSave` 的做法）。
 */
function bindSettingsPages() {
  if (window.__augitSettingsPagesBound) return;
  window.__augitSettingsPagesBound = true;

  // 归属判定一律用"在 .settings-layout 里"，不写死外层容器类：
  // 实时外壳是 `.settings-window`，`--scene settings` 是 `.dialog-xl`；
  // 写死容器会让场景里的分类点了不动（实测踩过）。
  document.addEventListener("click", (event) => {
    const row = event.target.closest && event.target.closest("[data-settings-page]");
    if (!row || !row.closest(".settings-layout")) return;
    event.preventDefault();
    switchSettingsPage(row.dataset.settingsPage);
  }, true);

  // 设置搜索（权威 `SettingsFilter` ＋ `SearchableOptionsRegistrar`）：
  //   - 命中判据是**选项**（标签、下拉项、当前取值）与**分类名**，不是只有分类名
  //     （`SearchableOptionsRegistrar.getConfigurables()` 返回 nameHits／contentHits，
  //      `SettingsFilter.shouldBeShowing()` 让"含命中项的分类"保持可见）；
  //   - 文字变化后 **100 ms 去抖**再过滤（`SettingsFilter.update()` 的 `delay(100.milliseconds)`）；
  //   - 一个命中都没有时搜索框底色变红（`SettingsEditor` 把编辑器背景设成 `LightColors.RED`
  //     = `SearchField.errorBackground`，JBColor 兜底 浅 `0xffcccc` / 深 `0x743A3A`）；
  //   - 当前分类没有命中而别的分类有 ⇒ 选中第一个命中的分类（`SettingsFilter.update()` 的
  //     `shouldMoveSelection`），命中项按 spotlight 边框色标出并滚到视野中央
  //     （`SpotlightPainter` 的 `glassPanel.addSpotlight` ＋ `center(component)`）；
  //   - ESC 在文本框有内容时清空过滤（`SettingsSearch.preprocessEventForTextField`）。
  // 过滤**不能**重绘右页以外的状态：重绘由 `switchSettingsPage` 负责，且搜索词记在
  // `live.settingsFilter` 里，重绘后由 `settingsNavHtml` 回填、这里再恢复焦点与光标。
  document.addEventListener("input", (event) => {
    const field = event.target.closest && event.target.closest("[data-settings-filter]");
    if (!field || !field.closest(".settings-layout")) return;
    const live = window.__augitLive;
    if (live) live.settingsFilter = field.value;
    window.clearTimeout(settingsFilterTimer);
    settingsFilterTimer = window.setTimeout(() => refreshSettingsFilter(), 100);
  }, true);
  // ESC 清空搜索：挂在 **window 捕获阶段**，因为"Esc 关弹层"的通用处理者
  //（`bindOverlayEscape`，document 捕获、注册更早）会先跑并整层移除设置对话框；
  // 它只认 `event.defaultPrevented` ⇒ 这里先 `preventDefault()` 就能按权威
  //（`SettingsSearch.preprocessEventForTextField`：文本框有内容时 ESC 清空过滤、事件被消费）
  // 让 ESC 只清搜索、不关对话框；文本为空时仍交给通用处理者关窗。
  window.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") return;
    const field = event.target.closest && event.target.closest("[data-settings-filter]");
    if (!field || !field.value) return;
    event.preventDefault();
    const live = window.__augitLive;
    if (live) live.settingsFilter = "";
    field.value = "";
    refreshSettingsFilter();
  }, true);

  // 设置窗口的快捷键与默认动作（都挂在 window 捕获，理由同上：要抢在文档级处理者之前）：
  //   ① `Ctrl+F` = 聚焦并全选搜索框（权威 `SettingsDialog.init()` 把 `SearchTextField.FindAction`
  //      注册到 `ACTION_FIND` 的快捷键上，`SearchTextField.java:490-498`：`selectText()` ＋ `requestFocus()`）；
  //      必须 `stopPropagation`，否则文档级的 `Ctrl+F`（打开"当前文件查找"）也会被触发。
  //   ② 搜索框里的 `↑/↓` = 移动**分类树**的选择（权威 `SettingsSearch.preprocessEventForTextField()`
  //      把无修饰键的上下键委托给 `treeView.getTree().processKeyEvent(event)`，
  //      焦点仍留在搜索框）。
  //   ③ `Enter` = 默认按钮「确定」（权威 `DialogWrapper` 把 OK 动作的按钮 `setDefaultButton`，
  //      单行输入框不消费 Enter；多行输入自己吞掉，所以这里跳过 TEXTAREA）。
  const moveSettingsSelection = (step) => {
    const live = window.__augitLive;
    if (!live) return false;
    const rows = [...document.querySelectorAll(".settings-window .settings-nav [data-settings-page]")]
      .filter((row) => !row.hidden);
    if (rows.length === 0) return false;
    const index = rows.findIndex((row) => row.dataset.settingsPage === live.settingsPage);
    const target = rows[Math.max(0, Math.min(rows.length - 1, (index < 0 ? 0 : index) + step))];
    if (!target || target.dataset.settingsPage === live.settingsPage) return false;
    switchSettingsPage(target.dataset.settingsPage);
    return true;
  };
  window.addEventListener("keydown", (event) => {
    if (event.isComposing) return;
    if (event.ctrlKey && !event.shiftKey && !event.altKey && !event.metaKey
        && String(event.key).toLowerCase() === "f") {
      const field = document.querySelector(".settings-window [data-settings-filter]")
        || document.querySelector(".dialog-xl [data-settings-filter]");
      if (!field) return;
      event.preventDefault();
      event.stopPropagation();
      field.focus({ preventScroll: true });
      if (typeof field.select === "function") field.select();
      return;
    }
    const searchField = event.target.closest && event.target.closest("[data-settings-filter]");
    if (searchField && (event.key === "ArrowDown" || event.key === "ArrowUp")) {
      if (event.shiftKey || event.altKey || event.ctrlKey || event.metaKey) return;
      if (moveSettingsSelection(event.key === "ArrowDown" ? 1 : -1)) event.preventDefault();
      return;
    }
    if (event.key !== "Enter" || event.ctrlKey || event.altKey || event.metaKey || event.shiftKey) return;
    if (event.target && event.target.tagName === "TEXTAREA") return;
    const dialog = document.querySelector(".settings-window .dialog");
    if (!dialog) return;
    const save = dialog.querySelector('[data-settings-action="save"]');
    if (!save) return;
    event.preventDefault();
    save.click();
  }, true);

  // Shell 不是"自定义命令"时禁用自定义启动命令输入框：避免"填了却不生效"的错觉。
  document.addEventListener("change", (event) => {
    const select = event.target.closest && event.target.closest('[data-setting="terminalShell"]');
    if (!select || !select.closest(".settings-layout")) return;
    const layout = select.closest(".settings-layout");
    const command = layout.querySelector('[data-setting="terminalCustomCommand"]');
    if (command) command.disabled = select.value !== "Custom";
  }, true);

  for (const type of ["input", "change"]) {
    document.addEventListener(type, (event) => {
      if (event.target.closest && event.target.closest(".settings-window .settings-page")) syncSettingsDirtyMarkers();
    }, true);
  }
}

/**
 * 设置搜索的取值来源：与 `liveSettingsBody()` 同一套优先级
 * （草稿 > 设置文件 > Git 检测结果），否则搜索会与用户眼前的值不一致。
 */
function settingsSearchValues() {
  const live = window.__augitLive || {};
  return Object.assign({}, live.settings || {}, live.settingsDraft || {}, {
    gitDetection: live.gitDetection || null,
    gitMinimum: live.gitDetection ? live.gitDetection.minimumVersion : "2.40",
  });
}

/**
 * 一个设置字段的可见标签：先按 `label[for]`（元素 id，如 `ui-font-size`）；否则回到 `.form-grid`，
 * 找**承载该字段的那一格**（字段可能包在 `.font-setting` 这类容器里）并取它前面的 `<label>`。
 */
function settingsFieldLabelNode(field, root) {
  if (!field) return null;
  if (field.id) {
    const explicit = (root || document).querySelector(`label[for="${CSS.escape(field.id)}"]`);
    if (explicit) return explicit;
  }
  const grid = field.closest ? field.closest(".form-grid") : null;
  let cell = field;
  if (grid) {
    while (cell && cell.parentElement !== grid) cell = cell.parentElement;
  }
  const parent = (grid && cell) ? grid : field.parentElement;
  if (!parent) return null;
  const children = [...parent.children];
  for (let index = children.indexOf(cell || field) - 1; index >= 0; index -= 1) {
    if (children[index].tagName === "LABEL") return children[index];
  }
  return null;
}

/**
 * 一个分类里可被搜索的文本（权威 `SearchableOptionsRegistrar` 索引的是**选项**：
 * 标签文案、下拉项文案、取值；分类自己的显示名另算 `nameHits`）。
 * 用离屏模板渲染该分类的正文来取，避免维护一份会漂移的标签清单。
 */
function settingsPageSearchTexts(page) {
  const holder = document.createElement("template");
  holder.innerHTML = settingsPageHtml(page, settingsSearchValues(), true);
  const root = holder.content;
  return [...root.querySelectorAll("[data-setting]")].map((field) => {
    const label = settingsFieldLabelNode(field, root);
    const texts = [
      label ? label.textContent : "",
      field.getAttribute("aria-label") || "",
      field.getAttribute("title") || "",
      field.getAttribute("placeholder") || "",
      field.value || "",
      ...[...(field.options || [])].map((option) => option.textContent || ""),
    ];
    return { key: field.dataset.setting, text: texts.join("\u0001") };
  });
}

/**
 * 设置搜索的判定与落地（权威 `SettingsFilter` ＋ `SearchableOptionsRegistrar`）。
 * 返回 `{ query, pages }`：`pages` 是命中的分类 id（顺序照导航）。
 */
function applySettingsFilter(layout, rawQuery) {
  const live = window.__augitLive || {};
  const query = String(rawQuery || "").trim().toLowerCase();
  const pages = [];
  const hitsByPage = new Map();
  for (const item of SETTINGS_PAGES) {
    const hits = [];
    if (query && item.title.toLowerCase().includes(query)) pages.push(item.id);
    if (query) {
      for (const entry of settingsPageSearchTexts(item.id)) {
        if (entry.text.toLowerCase().includes(query)) hits.push(entry.key);
      }
    }
    hitsByPage.set(item.id, hits);
    if (query && hits.length > 0 && !pages.includes(item.id)) pages.push(item.id);
  }
  // 导航：命中分类可见（权威 `SettingsFilter.shouldBeShowing()`）；空查询恢复全部。
  for (const row of layout.querySelectorAll(".settings-nav [data-settings-page]")) {
    row.hidden = query.length > 0 && !pages.includes(row.dataset.settingsPage);
  }
  // 无命中 ⇒ 搜索框底色变红（`SearchField.errorBackground`）。
  const noHits = query.length > 0 && pages.length === 0;
  live.settingsFilterNoHits = noHits;
  const field = layout.querySelector("[data-settings-filter]");
  if (field) field.classList.toggle("no-hits", noHits);
  // 命中项标记 + 滚到视野中央（权威 `SpotlightPainter` 的 spotlight 与 `center(component)`）。
  for (const node of layout.querySelectorAll("[data-settings-hit]")) {
    node.removeAttribute("data-settings-hit");
    node.classList.remove("settings-hit");
  }
  const keys = new Set(hitsByPage.get(live.settingsPage) || []);
  let first = null;
  for (const node of layout.querySelectorAll(".settings-page [data-setting]")) {
    if (!keys.has(node.dataset.setting)) continue;
    node.setAttribute("data-settings-hit", "true");
    node.classList.add("settings-hit");
    const label = settingsFieldLabelNode(node, layout);
    if (label) {
      label.setAttribute("data-settings-hit", "true");
      label.classList.add("settings-hit");
    }
    if (!first) first = node;
  }
  if (first && typeof first.scrollIntoView === "function") first.scrollIntoView({ block: "center" });
  return { query, pages };
}

/** 设置搜索的 100 ms 去抖句柄（`live-data.js` 是 module ⇒ 严格模式，未声明的赋值会抛 ReferenceError）。 */
let settingsFilterTimer = 0;

/**
 * 按当前搜索词刷新设置窗口（输入 100 ms 去抖后调用、切页重绘后也会调用）。
 * 当前分类没有命中而别的分类有 ⇒ 切到第一个命中的分类（权威 `SettingsFilter.update()`：
 * 当前 configurable 不在命中集里就移动选择）；切页会重绘，随后这里再跑一次标出命中项。
 */
function refreshSettingsFilter(options = {}) {
  const live = window.__augitLive;
  const layout = document.querySelector(".settings-window .settings-layout")
    || document.querySelector(".dialog-xl .settings-layout")
    || document.querySelector("[data-augit-overlay] .settings-layout");
  if (!live || !layout) return null;
  const result = applySettingsFilter(layout, live.settingsFilter || "");
  if (result.query && result.pages.length > 0 && !result.pages.includes(live.settingsPage)) {
    switchSettingsPage(result.pages[0]);
    return result;
  }
  const field = layout.querySelector("[data-settings-filter]");
  if (field && options.keepFocus !== false) {
    field.focus({ preventScroll: true });
    const end = field.value.length;
    try { field.setSelectionRange(end, end); } catch { /* 非文本输入忽略 */ }
  }
  return result;
}

/**
 * 未保存修改标记与「应用」的可用性（PyCharm 实测的实心圆点 ＋ 权威 `SettingsEditor.updateStatus()`：
 * `getApplyAction().setEnabled(isModified())`）。随输入即时更新，但**不重绘右页** ——
 * 重绘会把焦点与光标位置一起清掉（"输入一个字符就跳到开头"）；只增删导航行上的标记节点、
 * 改底栏按钮的禁用态，因此不需要重建页面。
 */
function syncSettingsDirtyMarkers() {
  const live = window.__augitLive;
  const layout = document.querySelector(".settings-window .settings-layout")
    || document.querySelector("[data-augit-overlay] .settings-layout");
  if (!live || !layout) return;
  collectSettingsDraft();
  const dirty = settingsDirtyPages(live);
  for (const row of layout.querySelectorAll(".settings-nav [data-settings-page]")) {
    const page = row.dataset.settingsPage;
    const existing = row.querySelector(".settings-dirty");
    if (dirty.has(page) && !existing) {
      row.insertAdjacentHTML("beforeend",
        '<span class="settings-dirty" role="status" aria-label="有未保存的修改" title="有未保存的修改"></span>');
    } else if (!dirty.has(page) && existing) {
      existing.remove();
    }
  }
  // 「应用」只在有未保存修改时可用；没有改动时它不解锁（权威同一条 `setEnabled(isModified())`）。
  // 注意按钮在**底栏**、不在 `.settings-layout` 里 ⇒ 必须从对话框（或整页）查。
  const applyButton = document.querySelector(".settings-window [data-settings-action=\"apply\"]")
    || document.querySelector(".dialog-xl [data-settings-action=\"apply\"]")
    || layout.querySelector('[data-settings-action="apply"]');
  if (applyButton) {
    const enabled = dirty.size > 0;
    applyButton.disabled = !enabled;
    if (enabled) applyButton.removeAttribute("aria-disabled");
    else applyButton.setAttribute("aria-disabled", "true");
  }
}

/** 切到某个分类：先收草稿，再只重绘右页（左导航与底栏保持）。 */
function switchSettingsPage(page) {
  const live = window.__augitLive;
  if (!live) return;
  const allowed = ["appearance", "file-view", "git", "terminal"];
  const next = allowed.includes(page) ? page : "appearance";
  collectSettingsDraft();
  live.settingsPage = next;
  renderSettingsDialog();
  // 搜索驱动的切页：重绘后重新标出命中项并把焦点交回搜索框（权威在过滤期间保持搜索框持有焦点）。
  if (live.settingsFilter) refreshSettingsFilter({ keepFocus: true });
  // 切到 Git 分类时按需取检测结果（场景路径同样要能拿到，否则永远停在"正在检测…"）。
  if (next === "git") void ensureGitDetection();
}

/** 用当前分类与草稿重绘设置对话框正文。 */
function renderSettingsDialog() {
  const live = window.__augitLive;
  // 两种承载都要认：实时外壳用 `.settings-window` 覆盖层，
  // `--scene settings` 场景用 `.dialog-xl`（实测只认前者时，场景里点分类不切页）。
  const window_ = document.querySelector(".settings-window .settings-layout")
    || document.querySelector(".dialog-xl .settings-layout")
    || document.querySelector("[data-augit-overlay] .settings-layout");
  if (!live || !window_) return;
  const holder = document.createElement("template");
  holder.innerHTML = liveSettingsBody(live.settingsPage || "appearance");
  const replacement = holder.content.firstElementChild;
  if (!replacement) return;
  window_.replaceWith(replacement);
  bindSettingsPages();
}

/** 关闭设置对话框。 */
function closeSettingsDialog() {
  document.querySelectorAll(".settings-window").forEach((node) => node.remove());
  restoreDialogFocus();
}

/**
 * 新建 Worktree 表单（规格 §7.11）。
 *
 * 字段与顺序见 `openWorktreeDialog()` 的注释（分支 → 新分支 → 目录，取权威 `GitWorkingTreeDialog`）。
 * 字段校验只判断非空；目录是否可用、分支是否存在、新分支名是否合法都由 Git 给出原因
 *（第 146 轮起「新分支」会作为 `newBranch` 传给宿主，由 `check-ref-format --branch` 校验）。
 */
/**
 * Stash 管理（规格 §7.11 / §10.4）。
 *
 * 视觉稿的 stash-manager 页：左侧列表、右侧详情带动作行（应用 / 弹出 / 查看内容 / 删除）
 * 与「包含 N 个文件」。选中项进状态（`live.selectedStashIndex`），
 * 详情里的文件列表来自独立的 `git/stash-content` 查询。
 */
/** 打开 Stash 对话框（规格 §5.3；键位与状态机取自视觉稿的 bindStashDialog）。 */
/**
 * Reset 当前分支（规格 §7.11 / §10.4 / §9.3）。
 *
 * 模式说明、影响预览、进行态冻结、Tab/Enter/Esc 循环与底栏按钮都由视觉稿的
 * `bindResetDialog()` 提供；这里只负责建层、把它接到当前 HEAD，并让"执行/取消"
 * 落到真实的宿主写操作（`git/reset` / `write/cancel`）。
 */
function openResetDialog() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  if (!live.history || !live.history.head) {
    // 没有 HEAD 就没有可确认的目标：不伪造一个样例哈希。
    window.__augitError = "reset:no-head";
    return;
  }

  closeLiveOverlay();
  rememberDialogFocus();
  document.querySelectorAll(".dialog.reset-dialog").forEach((node) => {
    const owner = node.closest("[data-augit-overlay]") || node;
    owner.remove();
  });
  const template = document.createElement("template");
  template.innerHTML = dialog(
    "Reset 当前分支",
    liveResetBody(),
    `<button type="button" class="secondary-button">取消</button>`
      + `<button type="button" class="reset-run danger-button">确认 Reset Hard</button>`,
    false,
    "reset-dialog");
  // 直接用 dialog() 自己的覆盖层当实时层：再套一层 div 会让
  // `dialog.closest('[data-augit-overlay]')` 命中内层，关闭后留下一个空覆盖层
  // （实测"关闭后仍存在 reset-window 层"）。
  const layer = template.content.firstElementChild;
  if (!layer) return;
  layer.classList.add("live-overlay", "reset-window");
  host.appendChild(layer);
  // 共享绑定提供模式切换/影响预览/键盘循环/焦点，避免实时外壳另写一套状态机。
  bindResetDialog();
  if (typeof measureResetDialog === "function") measureResetDialog();
}

/**
 * 「打开工作区」（产品规格 §2 / 视觉稿 workspace-open）。
 *
 * 最近目录来自设置；条目点击后交给宿主：同目录激活已有窗口、不同目录开新窗口。
 * 结果按事实说明，不假设成功（规格 §10.2）。
 */
function openWorkspaceDialog() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  closeLiveOverlay();
  rememberDialogFocus();
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay workspace-open-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "打开工作区",
    liveWorkspaceOpenBody(),
    '<button type="button" class="secondary-button" data-workspace-action="cancel">取消</button>',
    true,
    "workspace-open-dialog");
  host.appendChild(layer);
}

/** 打开工作区页内的提示。 */
function setWorkspaceNotice(message) {
  const notice = document.querySelector(".workspace-open-window .workspace-notice");
  if (!notice) return;
  notice.hidden = !message;
  notice.textContent = message || "";
  notice.title = message || "";
}

/** 交给宿主打开某个目录，并按结果给用户一个明确说法。 */
async function openWorkspacePath(path) {
  const live = window.__augitLive;
  if (!live || !path) return null;
  setWorkspaceNotice(`正在打开 ${path}…`);
  let payload = null;
  let failure = null;
  try {
    payload = await invoke("workspace/open", { path }, 30000);
    if (!payload || payload.opened === "invalid") {
      failure = (payload && payload.reason) || "无法打开该目录。";
    }
  } catch (error) {
    failure = String((error && error.message) || error);
  }

  if (failure) {
    setWorkspaceNotice(describeFailure(failure, {
      unchanged: "当前窗口与已打开的工作区都没有变化。",
      next: "可以换一个目录，或先在资源管理器里确认路径。",
    }));
    return null;
  }

  window.__augitWorkspaceOpened = payload.opened;
  setWorkspaceNotice(
    payload.opened === "current"
      ? "这个目录已经在当前窗口打开。"
      : payload.opened === "activated"
        ? "已激活该目录已有的窗口。"
        : "已在新窗口中打开该目录。");
  if (payload.opened !== "current") {
    closeLiveOverlay();
    restoreDialogFocus();
  }
  return payload;
}

/** 「选择目录…」：先用系统对话框取路径，再按同一套逻辑打开。 */
async function pickWorkspaceAndOpen() {
  setWorkspaceNotice("正在等待系统目录选择…");
  let payload = null;
  let failure = null;
  try {
    payload = await invoke("workspace/pick", {}, 120000);
    if (!payload || payload.picked === false) {
      // 取消不是错误：不关窗口，也不提示失败。
      setWorkspaceNotice(payload && payload.reason ? payload.reason : "");
      return null;
    }
  } catch (error) {
    failure = String((error && error.message) || error);
  }

  if (failure) {
    setWorkspaceNotice(describeFailure(failure, {
      unchanged: "当前窗口与已打开的工作区都没有变化。",
      next: "可以重试，或从最近目录里选择。",
    }));
    return null;
  }

  return openWorkspacePath(payload.path);
}

/**
 * 「克隆仓库…」：复用视觉稿的 Clone 对话框（校验、焦点与冻结逻辑都在它里面），
 * 只把真实克隆的入口接上——此前它只在 `scene=clone` 独立页里可用。
 */
function openCloneDialog() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  closeLiveOverlay();
  rememberDialogFocus();
  // 直接建层而不是写 live.overlay 再刷区域：区域替换只在"目标与替换节点都存在"时生效，
  // 当前没有覆盖层时那样做等于什么都不发生（实测克隆对话框一直不出现）。
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay clone-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "克隆仓库",
    liveCloneBody(),
    '<button type="button" class="secondary-button">取消</button>'
      + '<button type="button" class="primary-button">克隆</button>',
    true,
    "clone-dialog");
  host.appendChild(layer);
  // 视觉稿自己的克隆逻辑（校验、焦点、冻结与真实克隆入口）在此绑定。
  if (typeof bindCloneDialog === "function") bindCloneDialog();
  if (typeof measureCloneDialog === "function") measureCloneDialog();
}

function openStashDialog() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  closeLiveOverlay();
  rememberDialogFocus();
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "Stash",
    liveStashBody(),
    '<button type="button" class="secondary-button" data-stash-create-action="cancel">取消</button>'
      + '<button type="button" class="primary-button" data-stash-create-action="create">创建 Stash</button>',
    false,
    "stash-dialog");
  host.appendChild(layer);
  const message = layer.querySelector("#stash-message");
  if (message) message.focus({ preventScroll: true });
  if (typeof measureStashDialog === "function") measureStashDialog();
}

/** 窗口内的状态提示（与视觉稿同一处：表单下方）。 */
function setStashDialogNotice(text) {
  const notice = document.querySelector(".dialog.stash-dialog .stash-notice");
  if (!notice) return;
  notice.textContent = notice.title = text || "";
  notice.hidden = !text;
}

/**
 * Stash 对话框的键盘规则（规格 §5.3；顺序与视觉稿一致：字段 → 取消 → 创建 → 关闭）。
 *
 * 挂在 document 的捕获阶段：对话框是动态创建的，节点上的监听会随重绘消失。
 * 组词期间不抢 Enter/Esc/Tab；进行中不允许再次提交（按钮已禁用，这里再挡一次）。
 */
/**
 * 管理页字段编辑进状态（规格 handoff 第 3 节第 5 条：用户状态放状态里，不只放 DOM）。
 *
 * 只写状态、不重绘：重绘会替换输入框，正在输入的内容与光标都会丢。
 */
function bindManagementFieldDraft() {
  if (window.__augitMgmtFieldBound) return;
  window.__augitMgmtFieldBound = true;
  document.addEventListener("input", (event) => {
    const live = window.__augitLive;
    const field = event.target.closest && event.target.closest(".remote-window [data-remote-field]");
    if (!live || !field) return;
    const layer = document.querySelector(".remote-window");
    if (!layer) return;
    const read = (name) => {
      const node = layer.querySelector(`[data-remote-field="${name}"]`);
      return node ? node.value : "";
    };
    // currentName 由当前选中项推导：草稿一旦丢失这个字段，"保存"会被当成"新增"。
    const remotes = (live.remotes && live.remotes.remotes) || [];
    const selected = Number.isInteger(live.selectedRemoteIndex) ? remotes[live.selectedRemoteIndex] || null : null;
    live.remoteDraft = {
      name: read("name"),
      fetchUrl: read("fetchUrl"),
      pushUrl: read("pushUrl"),
      currentName: selected ? selected.name : null,
    };
  }, true);
}

function bindStashDialogKeys() {
  if (window.__augitStashDialogBound) return;
  window.__augitStashDialogBound = true;
  document.addEventListener("keydown", (event) => {
    const layer = document.querySelector(".dialog.stash-dialog");
    if (!layer) return;
    if (event.isComposing || event.keyCode === 229) return;
    const fields = [...layer.querySelectorAll("[data-stash-field]")];
    const cancel = layer.querySelector('[data-stash-create-action="cancel"]');
    const create = layer.querySelector('[data-stash-create-action="create"]');
    const close = layer.querySelector(".dialog-header .icon-button");
    const order = [...fields, cancel, create, close].filter(Boolean);

    if (event.key === "Escape") {
      event.preventDefault();
      void cancelStashDialog();
      return;
    }

    if (event.key !== "Tab") return;
    event.preventDefault();
    const available = order.filter((node) => !node.disabled && node.getAttribute("aria-disabled") !== "true");
    if (available.length === 0) return;
    const index = available.indexOf(document.activeElement);
    const step = event.shiftKey ? -1 : 1;
    const next = available[(index + step + available.length) % available.length];
    if (next) next.focus();
  }, true);
}

/** 关闭 Stash 对话框并把焦点交回打开前的元素。 */
function closeStashDialog() {
  document.querySelectorAll(".dialog.stash-dialog").forEach((node) => {
    const owner = node.closest("[data-augit-overlay]") || node;
    owner.remove();
  });
  restoreDialogFocus();
}

/** 取消：进行中不许直接关掉（视觉稿：取消按钮先取消这次操作，再关闭）。 */
async function cancelStashDialog() {
  const live = window.__augitLive;
  if (live && live.stashCreating) {
    live.stashCreating = false;
    setStashDialogNotice("操作已取消。");
    setStashDialogRunning(false);
    return;
  }
  closeStashDialog();
}

/** 进行态：冻结字段与按钮，并按视觉稿改按钮文字。 */
function setStashDialogRunning(running) {
  const live = window.__augitLive;
  const layer = document.querySelector(".dialog.stash-dialog");
  if (!layer) return;
  if (live) live.stashCreating = running;
  for (const node of layer.querySelectorAll("[data-stash-field]")) node.disabled = running;
  const create = layer.querySelector('[data-stash-create-action="create"]');
  const cancel = layer.querySelector('[data-stash-create-action="cancel"]');
  const close = layer.querySelector(".dialog-header .icon-button");
  if (create) {
    create.disabled = running;
    create.textContent = running ? "正在创建 Stash…" : "创建 Stash";
  }
  if (cancel) cancel.textContent = running ? "取消操作" : "取消";
  if (close) close.setAttribute("aria-disabled", String(running));
}

/** 提交创建：交给宿主执行，失败在窗口内说明原因（规格 §10.2）。 */
async function submitStashDialog() {
  const live = window.__augitLive;
  const layer = document.querySelector(".dialog.stash-dialog");
  if (!live || !layer || live.stashCreating) return null;
  const message = layer.querySelector("#stash-message");
  const keep = layer.querySelector("#stash-keep");
  // 权威 `GitStashDialog.kt:30-36,48-56`：`Include untracked` 复选**默认不勾选**，
  // 与「保留索引状态」同处一行。此前 Augit 没有这个复选、且写死 `includeUntracked: true`，
  // 等于总是把未跟踪文件一起暂存（第 168 轮按权威改正）。
  const include = layer.querySelector("#stash-include-untracked");

  setStashDialogRunning(true);
  setStashDialogNotice("正在创建 Stash…");
  let payload = null;
  let failure = null;
  try {
    payload = await invoke("git/stash", {
      message: message ? message.value : "",
      keepIndex: !!(keep && keep.checked),
      includeUntracked: !!(include && include.checked),
    }, 120000);
    if (!payload || payload.ok === false) {
      failure = (payload && payload.reason) || "创建 Stash 失败。";
    }
  } catch (error) {
    failure = String((error && error.message) || error);
  }

  if (!live.stashCreating) {
    // 进行中被用户取消：结果不再回到界面，也不留下"正在创建…"。
    return null;
  }
  live.stashCreating = false;
  if (payload && payload.stashes) {
    live.stashes = payload.stashes;
  }
  if (failure) {
    setStashDialogRunning(false);
    setStashDialogNotice(describeFailure(failure, {
      unchanged: "工作区与 Stash 列表都没有变化。",
      next: "可以先刷新状态确认是否还有需要暂存的改动。",
    }));
    return null;
  }

  window.__augitStashCreated = true;
  closeStashDialog();
  await loadStashFiles().catch(() => null);
  const remaining = (live.stashes && live.stashes.stashes) || [];
  if (live.selectedStashIndex >= remaining.length) {
    live.selectedStashIndex = Math.max(0, remaining.length - 1);
  }
  await Promise.all([loadStatus().catch(() => null), loadHistory().catch(() => null)]);
  refreshAfterEvent("side", "statusbar", "bottomTool", "titlebar");
  return payload;
}

async function openStashManagerDialog() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  closeLiveOverlay();
  rememberDialogFocus();
  if (!live.stashes) {
    await loadReferences().catch(() => null);
  }
  const stashes = (live.stashes && live.stashes.stashes) || [];
  if (!Number.isInteger(live.selectedStashIndex) || live.selectedStashIndex >= stashes.length) {
    live.selectedStashIndex = 0;
  }
  await loadStashFiles().catch(() => null);
  renderStashManager();
}

/** 读取当前选中 Stash 的文件列表；读不到时按失败事实呈现，不假装有内容。 */
async function loadStashFiles() {
  const live = window.__augitLive;
  if (!live) return null;
  const stashes = (live.stashes && live.stashes.stashes) || [];
  const current = stashes[live.selectedStashIndex || 0] || null;
  if (!current) {
    live.stashFiles = { available: false, files: [], reason: "没有可查看的 Stash。" };
    return live.stashFiles;
  }

  try {
    const payload = await invoke("git/stash-content", { reference: current.reference }, 30000);
    live.stashFiles = payload && payload.available
      ? payload
      : { available: false, files: [], reason: (payload && payload.reason) || "无法读取 Stash 内容。" };
  } catch (error) {
    live.stashFiles = {
      available: false,
      files: [],
      reason: String((error && error.message) || error),
    };
  }
  return live.stashFiles;
}

/** 重绘管理窗口（只替换弹层，不重建背景）。 */
function renderStashManager() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  document.querySelectorAll("[data-augit-overlay].live-overlay.stash-manager-window").forEach((node) => node.remove());
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay stash-manager-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "Stash 管理",
    liveManagementPage("stash"),
    '<button type="button" class="secondary-button" data-stash-action="close">关闭</button>',
    true,
    "stash-manager-dialog");
  host.appendChild(layer);
  setStashNotice("");
  // 管理窗口也是按需创建的：不在 __augitRender 的重绘路径上，因此自己度量一次。
  if (typeof measureStashManagerDialog === "function") measureStashManagerDialog();
}

/** 管理页内的局部提示（规格 §10.2：说明发生了什么、哪些状态未改变、可以做什么）。 */
function setStashNotice(message) {
  const notice = document.querySelector(".dialog.stash-manager-dialog .stash-notice");
  if (!notice) return;
  notice.hidden = !message;
  notice.textContent = message || "";
  notice.title = message || "";
}

/**
 * 删除 Stash 的确认（规格 §10.4：必须显示具体影响，确认按钮使用动作名称）。
 *
 * 删除不可恢复，因此先把"删掉哪个、里面有什么"讲清楚，再让用户按带动作名的按钮。
 */
function openStashDropConfirm() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  const stashes = live && live.stashes ? live.stashes.stashes || [] : [];
  const current = stashes[live ? live.selectedStashIndex || 0 : 0] || null;
  if (!live || !host || !current) return;

  const files = (live.stashFiles && live.stashFiles.files) || [];
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay stash-drop-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "删除 Stash",
    // 与视觉基线共用 `dangerConfirmBody()`：基线页与实时侧的结构出自同一函数。
    dangerConfirmBody(`${current.reference} · ${current.message}`, [
      `删除后这个 Stash 及其 ${files.length} 个文件的改动都会消失，并且无法恢复。`,
      "工作区与其它 Stash 不会被修改。",
    ]),
    '<button type="button" class="secondary-button" data-stash-cancel="1">取消</button>'
      + `<button type="button" class="danger-button" data-stash-confirm="drop">删除 ${escapeText(current.reference)}</button>`,
    false,
    "stash-drop-dialog");
  host.appendChild(layer);
  const confirm = layer.querySelector("[data-stash-confirm]");
  if (confirm) confirm.focus();
}

/** 关闭删除确认层。 */
function closeStashDropConfirm() {
  document.querySelectorAll("[data-augit-overlay].live-overlay.stash-drop-window").forEach((node) => node.remove());
}

/**
 * 执行一个 Stash 动作（应用 / 弹出 / 删除）。
 *
 * 三种动作都由宿主执行并**回读真实列表**：界面不自行推断结果，
 * 失败时在页面内说明原因并保持列表为最新事实（规格 §10.2）。
 */
async function runStashAction(action) {
  const live = window.__augitLive;
  const stashes = live && live.stashes ? live.stashes.stashes || [] : [];
  const current = stashes[live ? live.selectedStashIndex || 0 : 0] || null;
  if (!live || !current) return null;

  setStashNotice(`正在${action === "apply" ? "应用" : action === "pop" ? "弹出" : "删除"} ${current.reference}…`);
  let payload = null;
  let failure = null;
  try {
    payload = await invoke("git/stash-write", { action, reference: current.reference }, 120000);
    if (!payload || payload.ok === false) {
      failure = (payload && payload.reason) || "Stash 操作失败。";
    }
  } catch (error) {
    failure = String((error && error.message) || error);
  }

  if (payload && payload.stashes) {
    live.stashes = payload.stashes;
  }
  const remaining = (live.stashes && live.stashes.stashes) || [];
  if (live.selectedStashIndex >= remaining.length) {
    live.selectedStashIndex = Math.max(0, remaining.length - 1);
  }
  await loadStashFiles().catch(() => null);
  renderStashManager();

  if (failure) {
    setStashNotice(`${describeFailure(failure, {
      unchanged: action === "drop" ? "Stash 列表没有变化。" : "工作区与 Stash 列表都没有变化。",
      next: "可以先查看内容确认，再重试。",
    })}`);
    return null;
  }

  window.__augitStashAction = action;
  setStashNotice(action === "apply"
    ? `已应用 ${current.reference}，改动回到工作区；该 Stash 仍然保留。`
    : action === "pop"
      ? `已弹出 ${current.reference}，改动回到工作区，该 Stash 已删除。`
      : `已删除 ${current.reference}。`);
  await Promise.all([loadStatus().catch(() => null), loadHistory().catch(() => null)]);
  refreshAfterEvent("side", "statusbar", "bottomTool", "titlebar");
  return payload;
}

/**
 * 「查看内容」：打开该 Stash 里第一个文件与基准版本的差异。
 *
 * 视觉稿只有一个入口按钮，而 Augit 的比较视图是按文件打开的：
 * 这里打开第一个文件，用户可在同一 Stash 的其它文件上继续比较。
 */
async function openStashContentDiff() {
  const live = window.__augitLive;
  const stashes = live && live.stashes ? live.stashes.stashes || [] : [];
  const current = stashes[live ? live.selectedStashIndex || 0 : 0] || null;
  const files = (live && live.stashFiles && live.stashFiles.files) || [];
  if (!live || !current) return null;
  if (files.length === 0) {
    setStashNotice("这个 Stash 里没有可查看的文件。");
    return null;
  }

  const path = files[0].path;
  const label = `比较: ${path.split("/").at(-1)} · ${current.reference}`;
  const tab = ensureComparisonTab(path, label);
  activateComparisonTab(tab);
  const diff = await loadDiff(path, { commit: current.reference, force: true }).catch(() => null);
  if (!diff) {
    closeTab(tab.id);
    setStashNotice(`无法读取 ${path} 在 ${current.reference} 中的差异。`);
    return null;
  }

  syncComparisonTab(tab, path, label);
  activateComparisonTab(tab);
  closeLiveOverlay();
  refreshAfterEvent("editorContent", "editorTabs", "statusbar");
  return diff;
}

/**
 * Worktree 管理（规格 §5.3 / §10.4）。
 *
 * 视觉稿的 worktrees 页：路径、状态、终端会话，以及动作行
 * （打开窗口 / 新建 Worktree / 移除…）。选中项与"能否安全移除"都进状态。
 */
async function openWorktreeManagerDialog() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  closeLiveOverlay();
  rememberDialogFocus();
  if (!live.worktrees) {
    await loadReferences().catch(() => null);
  }
  const list = (live.worktrees && live.worktrees.worktrees) || [];
  if (!Number.isInteger(live.selectedWorktreeIndex) || live.selectedWorktreeIndex >= list.length) {
    live.selectedWorktreeIndex = 0;
  }
  await loadWorktreeRemoval().catch(() => null);
  renderWorktreeManager();
}

/** 读取当前选中 Worktree 的移除就绪状态（规格 §5.3：干净 + 无运行中的内置终端会话）。 */
async function loadWorktreeRemoval() {
  const live = window.__augitLive;
  if (!live) return null;
  const list = (live.worktrees && live.worktrees.worktrees) || [];
  const current = list[live.selectedWorktreeIndex || 0] || null;
  if (!current) {
    live.worktreeRemoval = null;
    return null;
  }

  try {
    const payload = await invoke("git/worktree-removal", { path: current.path }, 30000);
    live.worktreeRemoval = payload && payload.available
      ? Object.assign({ path: current.path }, payload)
      : { path: current.path, available: false, canRemove: false, reason: (payload && payload.reason) || "无法检查该 Worktree。" };
  } catch (error) {
    live.worktreeRemoval = {
      path: current.path,
      available: false,
      canRemove: false,
      reason: String((error && error.message) || error),
    };
  }
  return live.worktreeRemoval;
}

/** 重绘 Worktree 管理窗口。 */
function renderWorktreeManager() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  document.querySelectorAll("[data-augit-overlay].live-overlay.worktree-manager-window").forEach((node) => node.remove());
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay worktree-manager-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "Worktree 管理",
    liveManagementPage("worktrees"),
    '<button type="button" class="secondary-button" data-wtm-action="close">关闭</button>',
    true,
    "worktree-dialog");
  host.appendChild(layer);
  setWorktreeNotice("");
  if (typeof measureWorktreeDialog === "function") measureWorktreeDialog();
}

/** 管理页内的局部提示（规格 §10.2）。 */
function setWorktreeNotice(message) {
  const notice = document.querySelector(".dialog.worktree-dialog .worktree-notice");
  if (!notice) return;
  notice.hidden = !message;
  notice.textContent = message || "";
  notice.title = message || "";
}

/** 「打开窗口」：让系统为这个 Worktree 启动一个新的 Augit 窗口（规格 §7.10）。 */
async function openWorktreeWindow() {
  const live = window.__augitLive;
  const list = (live && live.worktrees && live.worktrees.worktrees) || [];
  const current = list[live ? live.selectedWorktreeIndex || 0 : 0] || null;
  if (!current) return null;
  const result = await launchExternal("augit", current.path);
  if (result && result.launched) {
    setWorktreeNotice(`已在新窗口中打开 ${current.path}。`);
  } else {
    setWorktreeNotice("");
  }
  return result;
}

/**
 * 「移除…」确认（规格 §10.4：必须显示具体影响）。
 *
 * 移除会删掉该 Worktree 的目录，因此把"哪个分支、哪个路径、当前状态、
 * 终端会话"都讲清楚，确认按钮用动作名称，并且只在可安全移除时才可用。
 */
function openWorktreeRemoveConfirm() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  const list = (live && live.worktrees && live.worktrees.worktrees) || [];
  const current = list[live ? live.selectedWorktreeIndex || 0 : 0] || null;
  const readiness = live ? live.worktreeRemoval : null;
  if (!live || !host || !current) return;
  if (!readiness || !readiness.canRemove) {
    setWorktreeNotice((readiness && readiness.reason) || "正在检查是否可以安全移除。");
    return;
  }

  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay worktree-remove-window";
  layer.setAttribute("data-augit-overlay", "");
  layer.innerHTML = dialog(
    "移除 Worktree",
    // 与视觉基线 `stash-drop-confirm` 共用 `dangerConfirmBody()`：结构由同一函数产出，
    // 基线页与实时侧不会再各写一套（§10.4 要求显示具体影响）。
    dangerConfirmBody(current.branch || "(detached)", [
      `将移除 ${current.path} 的目录与 Worktree 登记；分支本身不会被删除。`,
      "该目录当前没有本地改动，也没有运行中的内置终端会话。",
      "移除后需要重新 `git worktree add` 才能再次使用这个目录。",
    ]),
    '<button type="button" class="secondary-button" data-worktree-remove-cancel="1">取消</button>'
      + '<button type="button" class="danger-button" data-worktree-remove-confirm="remove">移除 Worktree</button>',
    false,
    "worktree-remove-dialog");
  host.appendChild(layer);
  const confirm = layer.querySelector("[data-worktree-remove-confirm]");
  if (confirm) confirm.focus();
}

/** 关闭移除确认层。 */
function closeWorktreeRemoveConfirm() {
  document.querySelectorAll("[data-augit-overlay].live-overlay.worktree-remove-window").forEach((node) => node.remove());
}

/** 执行安全移除：守卫由宿主强制执行，这里按真实结果更新界面。 */
async function runWorktreeRemove() {
  const live = window.__augitLive;
  const list = (live && live.worktrees && live.worktrees.worktrees) || [];
  const current = list[live ? live.selectedWorktreeIndex || 0 : 0] || null;
  if (!live || !current) return null;

  setWorktreeNotice(`正在移除 ${current.path}…`);
  let payload = null;
  let failure = null;
  try {
    payload = await invoke("git/worktree-remove", { path: current.path }, 120000);
    if (!payload || payload.ok === false) {
      failure = (payload && payload.reason) || "移除 Worktree 失败。";
    }
  } catch (error) {
    failure = String((error && error.message) || error);
  }

  if (payload && payload.worktrees) {
    live.worktrees = payload.worktrees;
  }
  const remaining = (live.worktrees && live.worktrees.worktrees) || [];
  if (live.selectedWorktreeIndex >= remaining.length) {
    live.selectedWorktreeIndex = Math.max(0, remaining.length - 1);
  }
  live.worktreeRemoval = null;
  await loadWorktreeRemoval().catch(() => null);
  renderWorktreeManager();

  if (failure) {
    setWorktreeNotice(describeFailure(failure, {
      unchanged: "Worktree、分支与工作区都没有变化。",
      next: "可以先查看状态，确认目录是否干净、终端是否已关闭。",
    }));
    return null;
  }

  window.__augitWorktreeRemoved = true;
  setWorktreeNotice(`已移除 ${current.path}。分支仍然保留。`);
  await Promise.all([loadStatus().catch(() => null), loadHistory().catch(() => null)]);
  refreshAfterEvent("side", "statusbar", "bottomTool", "titlebar");
  return payload;
}

function openWorktreeDialog() {
  const live = window.__augitLive;
  const host = document.querySelector(".augit-window");
  if (!live || !host) return;
  closeLiveOverlay();
  rememberDialogFocus();
  const branch = currentBranchName() || "HEAD";
  // 字段与顺序取权威 `GitWorkingTreeDialog.kt:175-230`：
  //   ① 来源引用（`createRefComboBox`）② **「新分支」复选 + 新分支名**（`:185-198`，
  //   `createNewBranch` 默认 **false**）③ 名称 ④ 位置（带浏览按钮）。
  // Augit 没有独立的"名称"字段（`destination` 就是完整路径，覆盖权威的"位置 + 名称"两栏），
  // 因此顺序为 **分支 → 新分支 → 目录**（原实现是"目录 → 分支"，与权威相反，第 146 轮改正）。
  // 「新分支」的宿主能力**早已存在**（`ShellBridge.cs:507` 的 `newBranch` →
  // `GitWorktreeService.cs:103-122` 用 `check-ref-format --branch` 校验后加 `-b`），
  // 只是 UI 从未传过它；默认**不勾** ⇒ 不改变任何现有行为（不新增功能，只是暴露已有能力）。
  const body = `<div class="form-grid">`
    + `<label for="worktree-branch">分支</label>`
    + `<input id="worktree-branch" class="text-field" data-worktree-field="branch" value="${escapeText(branch)}">`
    + `<span class="worktree-new-branch-row"><label class="check-line"><input id="worktree-new-branch" type="checkbox" data-worktree-field="newBranchEnabled">新分支</label>`
    + `<input id="worktree-new-branch-name" class="text-field" data-worktree-field="newBranch" placeholder="新分支名" disabled></span>`
    + `<label for="worktree-destination">目录</label>`
    + `<input id="worktree-destination" class="text-field" data-worktree-field="destination" placeholder="D:\\projects\\repository-worktree">`
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
  // 「新分支」复选联动分支名输入（权威 `.enabledIf(...)` 式的联动：不勾则禁用）。
  const newBranchToggle = layer.querySelector('[data-worktree-field="newBranchEnabled"]');
  const newBranchField = layer.querySelector('[data-worktree-field="newBranch"]');
  const syncNewBranch = () => {
    if (!newBranchField) return;
    newBranchField.disabled = !(newBranchToggle && newBranchToggle.checked);
    if (!newBranchField.disabled) newBranchField.focus({ preventScroll: true });
  };
  if (newBranchToggle) newBranchToggle.addEventListener("change", syncNewBranch);
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
  // 权威 `GitWorkingTreeDialog.kt:185-198`：「新分支」勾选后必须给出分支名
  //（`validationOnInput` / `validationOnApply` 都只在非空时才放行）。
  const newBranchEnabled = (() => {
    const toggle = layer.querySelector('[data-worktree-field="newBranchEnabled"]');
    return !!(toggle && toggle.checked);
  })();
  const newBranch = newBranchEnabled ? value("newBranch") : "";
  if (branch.length === 0) {
    showWorktreeNotice("请填写来源分支。");
    return;
  }

  if (newBranchEnabled && newBranch.length === 0) {
    showWorktreeNotice("请填写新分支名。");
    const field = layer.querySelector('[data-worktree-field="newBranch"]');
    if (field) field.focus({ preventScroll: true });
    return;
  }

  if (destination.length === 0) {
    showWorktreeNotice("请填写目标目录。");
    return;
  }

  let result;
  try {
    // 不勾「新分支」时不传该参数 ⇒ 宿主走原有路径（`newBranch` 为 null），行为与改动前完全一致。
    result = await invoke("git/worktree-write", newBranch.length > 0
      ? { destination, branch, newBranch }
      : { destination, branch }, 120000);
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

/** 打开分支/标签行的动作菜单（每个引用一组动作，见 `branchRowMenu()` 的权威出处）。 */
/**
 * 引用树行的右键菜单（权威 `BranchesTree.kt:272` 的 `BranchesTreeActionGroup` ＋
 * `Tree.MyMouseListener` 的右键选择规则 `Tree.java:1112-1130`：右键落到**不在选中集里**的行时，
 * 先把选中集替换成它；已在选中集里则保持整个多选）。
 *
 * 菜单内容由 `refTreeRowMenu()` 按选中集构成（对应 `BranchActionsBuilder.build` 的分支）。
 */
function openRefTreeMenu(row, clientX, clientY) {
  const live = window.__augitLive;
  if (!live || !row) return;
  if (!isSelectedRefRow(row)) {
    selectRefRow(row);
  }
  const markup = refTreeRowMenu({
    selections: selectedRefs(),
    references: live.references || { branches: [], tags: [] },
  });
  if (!markup) return;
  rememberDialogFocus();
  closeLiveOverlay();
  showPointerContextMenu(markup, {
    layerClass: "ref-menu",
    dataset: {
      refName: row.dataset.refName || "",
      refKind: row.dataset.refKind || "branch",
      refTargets: JSON.stringify(selectedRefs()),
    },
    clientX,
    clientY,
    maxHeight: 260,
  });
}

function openRefMenu(row, clientX, clientY) {
  const live = window.__augitLive;
  if (!live || !row) return;
  const name = row.dataset.branch;
  if (!name) return;
  // 当前分支在模板里带 `data-branch-current`：用它判定"不能检出/不能删除"，
  // 不用 `.selected`（那个类也用于键盘高亮，判定会漂）。
  const isCurrent = row.dataset.branchCurrent === "true";
  rememberDialogFocus();
  closeLiveOverlay();
  showPointerContextMenu(branchRowMenu({ name, kind: row.dataset.branchKind || "branch", isCurrent }), {
    layerClass: "ref-menu",
    dataset: { refName: name, refKind: row.dataset.branchKind || "branch" },
    clientX,
    clientY,
    maxHeight: 200,
  });
}

/**
 * 删除引用（规格 §5.2／§10.4；权威 `GitDeleteBranchOperation`）。
 *
 * 分支分两步：先 `force=false`，由 Git 拒绝未完全合并的分支并给出原因；用户确认"丢弃未合并提交"后再 `force=true`。
 * 标签一步删除（`git/tag` 的 `delete`）。
 */
/**
 * 引用树选中（权威 `BranchesTreeSelection` ＋ Swing `DefaultTreeSelectionModel` 的
 * `DISCONTIGUOUS_TREE_SELECTION`，`Tree.java:141,291`）：
 * 普通单击**替换**选中集、Ctrl/⌘+单击**切换**该行、Shift+单击从**锚点**做区间扩展（只数可见行）。
 *
 * 选中集落在 `live.logRefSelection`（`[{name, kind}]`，跨区域刷新保留），随后**就地**刷新
 * 行的 `.selected` 与竖条的启停（`window.__augitApplyRefSelection`），不整块重绘 ——
 * 权威也是按选择刷 `update()`。
 */
function selectRefRow(row, options = {}) {
  const live = window.__augitLive;
  if (!live || !row) return;
  const item = { name: row.dataset.refName, kind: row.dataset.refKind };
  const current = Array.isArray(live.logRefSelection)
    ? live.logRefSelection.filter((entry) => entry && entry.name)
    : [];
  const same = (a, b) => !!a && !!b && a.name === b.name && a.kind === b.kind;
  if (options.toggle) {
    live.logRefSelection = current.some((entry) => same(entry, item))
      ? current.filter((entry) => !same(entry, item))
      : [...current, item];
    live.logRefAnchor = item;
  } else if (options.range) {
    const anchor = live.logRefAnchor || current[0] || item;
    const rows = [...row.closest(".tree").querySelectorAll(".tree-row[data-ref-name]")]
      .filter((node) => !node.hidden);
    const from = rows.findIndex((node) => node.dataset.refName === anchor.name && node.dataset.refKind === anchor.kind);
    const to = rows.indexOf(row);
    if (from < 0 || to < 0) {
      live.logRefSelection = [item];
    } else {
      const start = Math.min(from, to);
      const end = Math.max(from, to);
      live.logRefSelection = rows.slice(start, end + 1)
        .map((node) => ({ name: node.dataset.refName, kind: node.dataset.refKind }));
    }
  } else {
    live.logRefSelection = [item];
    live.logRefAnchor = item;
  }

  if (typeof window.__augitApplyRefSelection === "function") window.__augitApplyRefSelection();
}

/** 这一行是否在当前选中集里。 */
function isSelectedRefRow(row) {
  if (!row) return false;
  return selectedRefs().some((item) => item.name === row.dataset.refName && item.kind === row.dataset.refKind);
}

/** 选中集是否**恰好**是这一行（用于判断"单击已选中的行"这类只在单选时成立的动作）。 */
function isOnlySelectedRef(row) {
  if (!row) return false;
  const selections = selectedRefs();
  return selections.length === 1
    && selections[0].name === row.dataset.refName
    && selections[0].kind === row.dataset.refKind;
}

/**
 * 折叠/展开一个引用树分组（权威标准 JTree 的组节点行为）。
 * 参数是**折叠键**：类型分组用组名（`本地`），前缀分组用 `类型/路径`（`本地/feature`）。
 */
function setRefGroupCollapsed(label, collapsed) {
  const live = window.__augitLive;
  if (!live || !label) return;
  live.logRefCollapsed = live.logRefCollapsed || {};
  if (collapsed) live.logRefCollapsed[label] = true;
  else delete live.logRefCollapsed[label];
  if (typeof window.__augitApplyRefTreeFilter === "function") window.__augitApplyRefTreeFilter();
}

function toggleRefGroup(label) {
  const live = window.__augitLive;
  const collapsed = !!(live && live.logRefCollapsed && live.logRefCollapsed[label]);
  setRefGroupCollapsed(label, !collapsed);
}

/**
 * 引用树键盘：上下键在**可见行**之间移动选择并把焦点交给新行（权威的树是标准 JTree 行为）；
 * 回车 = 「筛选到该分支」（权威把 `Git.Log.Branches.Change.Branch.Filter` 注册在
 * `ENTER` 与 `button1 doubleClick` 上，`intellij.vcs.git.backend.xml:130-134`）；
 * 空格只是选中（JTree 的空格用于勾选，Augit 的树没有勾选框）。
 *
 * 分组节点（类型分组 `本地/远程/标签` 与前缀分组 `feature`）都用**折叠键** `data-ref-collapse-key`
 * 标识：类型分组是组名、前缀分组是 `类型/路径`，所以两层键不会撞名。
 */
function bindRefTreeKeys() {
  if (window.__augitRefTreeKeysBound) return;
  window.__augitRefTreeKeysBound = true;
  document.addEventListener("keydown", (event) => {
    const row = document.activeElement;
    if (!row || !row.classList || !row.classList.contains("tree-row")) return;
    if (!row.closest(".log-ref-panel")) return;
    const collapseKey = row.dataset.refCollapseKey;
    if (collapseKey) {
      // 组头：Enter/Space 与单击同义；左右键照 JTree 约定折叠/展开。
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        toggleRefGroup(collapseKey);
        return;
      }
      if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
        event.preventDefault();
        setRefGroupCollapsed(collapseKey, event.key === "ArrowLeft");
        return;
      }
    }
    if (!row.dataset.refName) return;
    if (event.key === "Enter") {
      event.preventDefault();
      // ENTER 就是 `Git.Log.Branches.Change.Branch.Filter`：把**整个选中集**筛选到日志
      // （权威 `selectedBranchFilters` 收全部选中分支，HEAD 行给 `HEAD`，标签给不出）。
      // 焦点行不在选中集里时先把它选上（Swing 里回车作用于选择，不是焦点）。
      if (!isSelectedRefRow(row)) selectRefRow(row);
      if (filterLogToRefRows(selectedRefRows())) void reloadHistoryKeepingFocus();
      return;
    }
    if (event.key === " ") {
      event.preventDefault();
      // Space = 切换该行的选中（与 Ctrl+单击同义，键盘用户的多选入口）。
      selectRefRow(row, { toggle: true });
      return;
    }
    if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
    event.preventDefault();
    const rows = [...row.closest(".tree").querySelectorAll(".tree-row[data-ref-name]")]
      .filter((node) => !node.hidden);
    const index = rows.indexOf(row);
    const next = rows[index + (event.key === "ArrowDown" ? 1 : -1)];
    if (!next) return;
    next.focus({ preventScroll: true });
    next.scrollIntoView({ block: "nearest" });
    // Shift+方向键 = 从锚点扩展区间（Swing `BasicTreeUI` 的 "selectNextChangeLead" 同义）；
    // 不带 Shift 就是普通的单选移动。
    selectRefRow(next, { range: event.shiftKey });
  });
}

/**
 * 引用树双击 = 把日志筛选到该分支（权威 `Git.Log.Branches.Change.Branch.Filter` 的
 * `button1 doubleClick`）。单击已经在上面按「单击时」开关分派，这里只补双击这一条。
 */
function bindRefTreeDoubleClick() {
  if (!window.__augitLive || window.__augitRefDoubleClickBound) return;
  window.__augitRefDoubleClickBound = true;
  document.addEventListener("dblclick", (event) => {
    const row = event.target.closest
      && event.target.closest('.log-ref-panel .tree-row[data-ref-name]');
    if (!row) return;
    event.preventDefault();
    if (!isSelectedRefRow(row)) selectRefRow(row);
    // 双击 = `Git.Log.Branches.Change.Branch.Filter`：作用于**整个选中集**。
    if (filterLogToRefRows(selectedRefRows())) void reloadHistoryKeepingFocus();
  }, true);
}

/** "分支或标签"搜索（权威 `FilteringBranchesTree`）：按子串就地过滤行与空分组，并保留输入焦点。 */
function bindRefTreeFilter() {
  if (window.__augitRefTreeFilterBound) return;
  window.__augitRefTreeFilterBound = true;
  document.addEventListener("input", (event) => {
    const field = event.target;
    if (!field || field.getAttribute("aria-label") !== "分支或标签") return;
    const live = window.__augitLive;
    if (live) live.logRefFilter = field.value;
    if (typeof window.__augitApplyRefTreeFilter === "function") window.__augitApplyRefTreeFilter(field.value);
  });
}

/**
 * 读取「我的分支」（权威 `ShowMyBranchesAction` → `BranchesDashboardUtil.checkIsMyBranchesSynchronously`，
 * `plugins/git4idea/backend/src/ui/branch/dashboard/BranchesDashboardUtil.kt:85-132`）：
 * 分支的**独占提交**非空、且**全部**由当前 Git 用户提交。
 *
 * 只在开关打开时算一次并缓存（权威的 `isMy` 是树模型上的三态缓存）；失败时给出可读原因而不是清空列表。
 * 登记差异：权威要求日志索引可用（`supportsIndexing && isGraphReady && allRootsIndexed`），
 * Augit 没有日志索引、按需现算（`git/branches-mine`），因此开关恒可用。
 */
async function ensureMyBranches() {
  const live = window.__augitLive;
  if (!live) return null;
  if (Array.isArray(live.myBranchNames) && !live.myBranchesReason) return live.myBranchNames;
  live.myBranchesLoading = true;
  refreshAfterEvent("bottomTool");
  const result = await invoke("git/branches-mine", {}, 30000).catch(() => null);
  const liveNow = window.__augitLive;
  if (!liveNow) return null;
  liveNow.myBranchesLoading = false;
  liveNow.myBranchNames = result && result.available && Array.isArray(result.mine) ? result.mine : [];
  liveNow.myBranchesAuthor = result && result.author ? result.author : null;
  liveNow.myBranchesReason = result && result.available
    ? null
    : ((result && result.reason) || "无法判断哪些分支属于你。");
  refreshAfterEvent("bottomTool");
  return liveNow.myBranchNames;
}

/**
 * 用户弹层的搜索框（权威 `MultipleValueFilterPopupComponent` 列表顶部的搜索框）：
 * 就地隐藏不匹配的行并保留输入焦点（与引用树搜索同一口径）。
 */
function bindHistoryUserSearch() {
  if (window.__augitUserSearchBound) return;
  window.__augitUserSearchBound = true;
  document.addEventListener("input", (event) => {
    const field = event.target;
    if (!field || !field.getAttribute || field.getAttribute("aria-label") !== "搜索用户") return;
    const live = window.__augitLive;
    if (live) live.historyUserFilter = field.value;
    const needle = String(field.value || "").trim().toLowerCase();
    document.querySelectorAll(".history-user-filter-menu [data-history-user]").forEach((node) => {
      node.hidden = !!needle && !node.textContent.toLowerCase().includes(needle);
    });
  });
}

/** 定位到选中引用（权威 `navigateLogToRef`）：把日志选到该引用的提交；不在当前加载页时说明原因。 */
async function locateSelectedRef() {
  const live = window.__augitLive;
  // 权威 `BranchesTreeSelection.logNavigatableNodeDescriptor` 取的是选中集里**第一个**可导航的节点
  // （`selectedNodes.firstNotNullOfOrNull { … }`）——多选时定位第一个，不是最后一个。
  const rows = selectedRefRows();
  if (rows.length === 0) return false;
  const row = rows.find((node) => node.dataset.refCommit) || rows[0];
  const target = row ? row.dataset.refCommit : "";
  const commit = target ? document.querySelector(`.commit-row[data-full-hash="${CSS.escape(target)}"]`) : null;
  if (!commit) {
    showToast({
      title: "无法定位到该引用",
      text: target
        ? "该引用的提交不在当前加载的历史里。"
        : "这个引用没有可定位的提交。",
      kind: "error",
    });
    return false;
  }
  commit.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
  commit.scrollIntoView({ block: "nearest" });
  return true;
}

function openRefDeleteConfirm(targets, force) {
  const host = document.querySelector(".augit-window");
  if (!host) return;
  // 目标可以是单个 `{name, kind}`，也可以是选中集（权威 `DeleteBranchAction.delete()` 对整个选中集生效）。
  const list = (Array.isArray(targets) ? targets : [targets])
    .filter((item) => item && item.name)
    .map((item) => ({ name: item.name, kind: item.kind || "branch" }));
  if (list.length === 0) return;
  const name = list[0].name;
  const onlyTag = list.every((item) => item.kind === "tag");
  const noun = onlyTag ? "标签" : "分支";
  closeLiveOverlay();
  rememberDialogFocus();
  const layer = document.createElement("div");
  layer.className = "overlay-layer live-overlay ref-delete-window";
  layer.setAttribute("data-augit-overlay", "");
  // 确认层自己带上目标身份：点击处理者只从 DOM 读，不再依赖闭包里的变量（重绘后仍正确）。
  layer.dataset.refName = name;
  layer.dataset.refKind = list[0].kind;
  layer.dataset.refTargets = JSON.stringify(list);
  const what = list.length === 1 ? `${noun} ${name}` : `${list.length} 个引用`;
  layer.innerHTML = dialog(
    `删除${noun}`,
    dangerConfirmBody(what, force
      ? ["其中还有提交没有合并到当前分支。", "继续将删除，未合并的提交会一并丢弃。", "删除后只能通过 Reflog 找回。"]
      : [`将删除${what}的引用。`, "已合并到当前分支的提交不会丢失。", "删除后需要重新创建才能再次使用这些引用。"]),
    '<button type="button" class="secondary-button" data-ref-delete-cancel="1">取消</button>'
      + `<button type="button" class="danger-button" data-ref-delete-confirm="${force ? "force" : "plain"}">删除${noun}</button>`,
    false,
    "ref-delete-dialog");
  host.appendChild(layer);
  const confirm = layer.querySelector("[data-ref-delete-confirm]");
  if (confirm) confirm.focus();
}

/** 执行删除：按真实结果更新界面（失败时把原因给用户，必要时再问一次"是否丢弃未合并提交"）。 */
async function runRefDelete(targets, force) {
  const live = window.__augitLive;
  if (!live) return null;
  // 目标可以是单个 `{name, kind}`，也可以是选中集（权威 `DeleteBranchAction.delete()` 对整个选中集生效：
  // 本地/远端分支一次交给 `git/branch` 的 `names`（宿主逐个删），标签逐个走 `git/tag` 的 delete）。
  const list = normalizeRefTargets(targets);
  if (list.length === 0) return null;
  const branches = list.filter((item) => item.kind !== "tag").map((item) => item.name);
  const tags = list.filter((item) => item.kind === "tag").map((item) => item.name);
  let payload = null;
  try {
    if (branches.length > 0) {
      payload = await invoke("git/branch", {
        action: "delete",
        name: branches[0],
        names: branches,
        force: !!force,
      }, 60000);
    }
    for (const name of tags) {
      const result = await invoke("git/tag", { action: "delete", name }, 60000);
      if (!payload) payload = result;
      else if (!result || result.changed !== true) {
        payload = { ...payload, reason: (result && result.reason) || payload.reason };
      }
    }
  } catch (error) {
    payload = { available: true, changed: false, reason: String((error && error.message) || error) };
  }

  const refused = (payload && Array.isArray(payload.refused) ? payload.refused : [])
    .filter((item) => item && item.name)
    .map((item) => ({ name: item.name, kind: "branch" }));
  const changed = !!(payload && payload.changed === true);
  // 未完全合并：这是**预期内的第一次拒绝**，按权威的"说明影响后再删"再问一次。
  // 宿主多目标时回 `refused[]`（只对被拒的那些再问）；单目标/旧形状只回 `reason`，用原目标再问。
  const notMerged = /not fully merged|未.{0,4}合并/.test((refused[0] && refused[0].reason) || (payload && payload.reason) || "");
  if (!force && notMerged && branches.length > 0) {
    await loadReferences().catch(() => null);
    openRefDeleteConfirm(refused.length > 0 ? refused : list.filter((item) => item.kind !== "tag"), true);
    return payload;
  }
  if (!changed) {
    window.__augitReferenceError = (payload && payload.reason) || "删除失败。";
    openBranchesPopover();
    return payload;
  }

  window.__augitReferenceDeleted = { name: list[0].name, kind: list[0].kind };
  if (refused.length > 0) window.__augitReferenceError = refused[0].reason;
  await loadReferences().catch(() => null);
  refreshAfterEvent("overlay", "side", "statusbar");
  return payload;
}

/** 把"单个目标或目标数组"归一化成 `[{name, kind}]`（多选删除的入口都用它）。 */
function normalizeRefTargets(targets) {
  return (Array.isArray(targets) ? targets : [targets])
    .filter((item) => item && item.name)
    .map((item) => ({ name: String(item.name), kind: item.kind || "branch" }));
}

/** 打开日志提交行的右键菜单（与改动列表菜单共用同一套定位、夹边与焦点逻辑）。 */
function openLogContextMenu(row, clientX, clientY) {
  const live = window.__augitLive;
  if (!live || !row) return;
  const hash = row.dataset.hash;
  if (!hash) return;
  rememberDialogFocus();
  closeLiveOverlay();
  showPointerContextMenu(gitLogContextMenu(true), {
    layerClass: "log-menu",
    dataset: { commitHash: hash, commitFullHash: row.dataset.fullHash || hash },
    clientX,
    clientY,
    maxHeight: 280,
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
 * 把 Markdown 预览里的相对链接解析成工作区相对路径（规格 §7.3）。
 *
 * 解析基准是当前文档所在目录；`..` 越出工作区根时返回 `null`，
 * 由调用方按"阻止"处理。这里只做纯字符串解析，不碰文件系统。
 */
function resolveMarkdownTarget(target, basePath) {
  const raw = String(target || "");
  if (raw.length === 0) return null;
  // 绝对路径与盘符（`/x`、`C:\x`、`\\server`）都不属于工作区相对链接。
  if (/^[\\/]/.test(raw) || /^[a-zA-Z]:/.test(raw)) return null;
  const base = String(basePath || "").split("/").slice(0, -1);
  const stack = base.slice();
  for (const part of raw.split(/[\\/]/)) {
    if (part.length === 0 || part === ".") continue;
    if (part === "..") {
      // 基准目录已经被退完时再退一级就越出工作区根。
      if (stack.length === 0) return null;
      stack.pop();
      continue;
    }
    stack.push(part);
  }
  return stack.length > 0 ? stack.join("/") : null;
}

/**
 * Markdown 预览里的受控链接（规格 §7.3）。
 *
 * "相对文件链接在 Augit 标签中打开，外部链接交给系统前保持当前页面"：
 * 网页层始终自行处理点击，任何情况下都不让 WebView 自己导航。
 * 越界、协议不支持、目标不存在时返回阻止原因，由界面在**原链接位置**显示紧凑错误，
 * 保留原标签文字；不使用跳到页首的空锚点，也不让整篇预览失败。
 */
async function openMarkdownLink(target) {
  const raw = String(target || "");
  if (raw.length === 0) return { opened: false, reason: "链接目标为空。" };
  if (/^(https?:)?\/\//i.test(raw) || /^mailto:/i.test(raw)) {
    const result = await launchExternal("open", raw);
    if (result && result.launched) return { opened: true };
    return { opened: false, reason: window.__augitLaunchError || "系统无法打开该链接。" };
  }
  // 其它协议（javascript:、file: 等）一律阻止，不做任何降级解释。
  if (/^[a-zA-Z][a-zA-Z0-9+.-]*:/.test(raw)) {
    return { opened: false, reason: "不支持该链接协议。" };
  }
  let decoded = raw;
  try { decoded = decodeURIComponent(raw); } catch { /* 保留原文 */ }
  const document_ = (window.__augitLive && window.__augitLive.document) || null;
  const resolved = resolveMarkdownTarget(decoded.split("#")[0].split("?")[0], document_ && document_.path);
  if (!resolved) return { opened: false, reason: "目标越出工作区。" };
  const parent = resolved.split("/").slice(0, -1).join("/");
  const name = resolved.split("/").at(-1);
  let entry = null;
  try {
    const listing = await invoke("workspace/list", { path: parent }, 15000);
    entry = ((listing && listing.entries) || []).find((item) => item && item.name === name) || null;
  } catch {
    return { opened: false, reason: "无法确认目标是否存在。" };
  }
  if (!entry) return { opened: false, reason: "路径不存在。" };
  if (entry.isDirectory) return { opened: false, reason: "目标是目录，不是可查看的文件。" };
  await openDocument(entry.path || resolved);
  return { opened: true };
}

window.__augitResolveMarkdownTarget = resolveMarkdownTarget;
window.__augitOpenMarkdownLink = (target) => openMarkdownLink(target);

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
    // 规格 §7.9：进入前先记下上下文（底部工具窗状态、**提交选择**、**详情正文位置**）。
    // 必须在 `await loadFileHistory(path)` **之前**记：读取过程会重绘底部区域，
    // 那时详情正文已经被重建、scrollTop 归零（第 164 轮实测：放在后面记下来的一直是 0）。
    const liveBefore = window.__augitLive;
    let hadBottom = false;
    if (liveBefore) {
      // 用 `currentLayout()` 而不是直接读 `live.layout`：布局在 `!userDriven` 时由**实际渲染**校正
      // （`readInitialLayout()`），直接读可能拿到首屏之前写入的空值，那样"进入前上下文"就记成了空串。
      const layout = currentLayout() || liveBefore.layout || {};
      hadBottom = !!layout.bottom;
      const detailHost = document.querySelector("[data-live-commit-detail]");
      const selectedRow = selectedCommitRow();
      const detailPanel = document.querySelector(".log-detail-panel");
      liveBefore.fileHistoryReturn = {
        bottom: hadBottom ? layout.bottom : "",
        hash: selectedRow ? selectedRow.dataset.hash : null,
        detailScroll: detailHost ? Math.round(detailHost.scrollTop) : 0,
        // 详情显隐（规格 §7.9 条目三）：DOM 上读一次当前值，返回时按它恢复。
        detailsHidden: !!(liveBefore.historyDetailsHidden || (detailPanel && detailPanel.style.display === "none")),
      };
    }

    await loadFileHistory(path);
    const live = window.__augitLive;
    if (live) {
      // 底部工具窗口切到文件历史（规格 §5.1 的同一套布局状态）。
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
  // 先解除背景的 inert 再归还焦点：观察器要到微任务之后才重算，
  // 而 `focus()` 对 inert 元素是静默无效的——不先同步这一步，焦点会掉到文档主体。
  syncModalBackground();
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
 * 提交动作的可用性 + 「进行中」状态（规格 §9.3；启用判据见权威）。
 *
 * **启用判据取权威**（`CommitChangeListDialog.java:616-624`）：
 *   boolean enabled = hasDiffs() && !myWorkflow.isExecuting();
 * 其中 `hasDiffs() = !getIncludedChanges().isEmpty() || !getIncludedUnversionedFiles().isEmpty()`
 * （`:602-604`）—— 即「**有已勾选项** 且 不在提交中」。**提交信息是否为空不参与该判据**
 * （空信息走确认层，见 `docs/nui-behavior/12-commit-changes.md` §2）。
 *
 * 原实现只处理了 `isExecuting()` 那一半，把「空闲时的禁用态」寄存在 `dataset.idleDisabled`
 * 里**等别处设置** —— 而实时侧没有任何地方设置它，于是"无勾选时提交按钮仍可用、点了才报错"，
 * 与权威的"按钮直接禁用"不一致（第 136 轮发现）。
 *
 * 进行中还必须**禁用重复触发**，并让用户看到**当前动作**与可取消入口。
 */
function reflectWriteOperation() {
  const live = window.__augitLive;
  if (!live) return;
  const actions = document.querySelector(".side-tool .commit-actions");
  if (!actions) return;
  const busy = !!live.writeOperation;
  // 勾选项（`hasDiffs()`）：未版本化文件同样计入，Augit 的模型里它们就在 status.files 里。
  const hasIncluded = ((live.status && live.status.files) || []).some((file) => file.checked);
  const submit = actions.querySelector(".primary-button");
  const push = actions.querySelector(".secondary-button");
  for (const button of [submit, push]) {
    if (!button) continue;
    const shouldDisable = busy || !hasIncluded;
    // <a> 的 disabled 属性无效，必须同时用 aria-disabled 与类表达，
    // 否则禁用对链接型按钮形同虚设。
    if (button.tagName === "A") {
      button.setAttribute("aria-disabled", shouldDisable ? "true" : "false");
      button.classList.toggle("disabled", shouldDisable);
    } else {
      button.disabled = shouldDisable;
    }
    // 禁用理由与状态一致（规格 §10.3）：无勾选时说明要先勾选；进行中由下面的状态文字负责。
    if (!busy && !hasIncluded) button.setAttribute("title", "先在改动列表里勾选要提交的文件。");
    else button.removeAttribute("title");
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

  if (cancel) {
    // 权威 `ProgressDialogUI.initCancellation()`（platform/platform-impl/src/com/intellij/openapi/progress/util/
    // ProgressDialogUI.kt:136-152）：`ActionListener { cancelAction(); cancelButton.isEnabled = false }`
    // —— 取消是**一次性**动作，按过即禁用（同一监听器也绑在 Esc 上）。
    // 不禁用的话，宿主确认之前再按一次会发出第二次 `write/cancel`。
    const cancelling = !!live.writeCancelling;
    cancel.disabled = cancelling;
    cancel.setAttribute("aria-disabled", cancelling ? "true" : "false");
    cancel.classList.toggle("disabled", cancelling);
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
  if (!live || !live.writeOperation) return null;
  // 取消是一次性动作（权威 `ProgressDialogUI.kt:136-152` 按过即禁用按钮）：已经处于"取消中"时
  // 不再发第二次请求 —— 按钮禁用是表层，这里是第二层守卫。
  if (live.writeCancelling) return null;
  const operation = live.writeOperation;
  // 取消中：**保持进行态**（按钮仍禁用），等宿主确认本机 Git 真的停下来（规格 §9.3）。
  // 提前解冻会让用户在命令仍在跑的时候再点一次提交。
  live.writeCancelling = true;
  live.writeCancelReason = `${operation}取消中…`;
  refreshAfterEvent("side", "statusbar");

  let stopped = false;
  let reason = null;
  try {
    const payload = await invoke("write/cancel", {}, 30000);
    stopped = !!(payload && payload.cancelled);
    reason = payload ? payload.reason : null;
  } catch (error) {
    reason = String((error && error.message) || error);
  }

  live.writeCancelling = false;
  // 不假设取消生效，也不自行回滚：**任何结果都重新读取真实仓库状态**。
  await Promise.all([
    loadStatus().catch(() => null),
    loadHistory().catch(() => null),
  ]);
  if (!stopped) {
    // 宿主说没有可取消的操作：说明命令已经自己结束了，此时才解除进行态。
    live.writeOperation = null;
  }
  live.writeCancelReason = stopped
    ? `${operation}已取消。`
    : `${operation}已结束（${reason || "没有可取消的操作"}）。`;
  refreshAfterEvent("side", "editorContent", "statusbar", "bottomTool");
  window.__augitWriteCancelStopped = stopped;
  return stopped;
}

/** 关闭实时弹层。 */
/**
 * 模态窗口打开期间禁用背景（规格 §5.3/§5.3 第 4 条、设计稿的静态预览同样用 `inert`）。
 *
 * 规则与视觉稿一致：`augit-window` 的直接子节点里，只有**最上层那个含对话框的覆盖层**
 * 保持可交互，其余（标题栏、主区域、状态栏、提示层、下层的模态）一律 `inert`。
 * 非模态弹层（快速打开、分支/右键菜单）不含 `.dialog`，因此不会禁用背景。
 * 关闭后恢复：`inert` 逐个与目标状态比较，只有变化时才写。
 *
 * 用观察器集中处理：实时弹层由十多处各自创建与关闭，逐处调用必然漏（实测分支弹层就有两套入口）。
 */
function syncModalBackground() {
  const host = document.querySelector(".augit-window");
  if (!host) return;
  const children = [...host.children];
  const modalLayers = children.filter(
    (node) => node.hasAttribute("data-augit-overlay") && node.querySelector(".dialog"));
  const top = modalLayers.length > 0 ? modalLayers[modalLayers.length - 1] : null;
  for (const node of children) {
    const inert = top !== null && node !== top;
    if (node.inert !== inert) node.inert = inert;
  }
}

function bindModalBackground() {
  if (window.__augitModalObserved) {
    syncModalBackground();
    return;
  }
  const root = document.getElementById("app");
  if (!root) return;
  window.__augitModalObserved = true;
  // 观察 #app 而不是 .augit-window：整页重绘会换掉后者，而 #app 一直在。
  // 回调里只做一次 querySelectorAll，且 MutationObserver 会合并同一批改动。
  new MutationObserver(() => syncModalBackground()).observe(root, { childList: true, subtree: true });
  syncModalBackground();
}

/**
 * 区域焦点顺序（规格 §5.4）：`Tab` 在当前区域内按**视觉顺序**移动焦点，
 * 不先穿越所有全局工具入口。
 *
 * 区域用选择器定义（与视觉稿的 `REGION_SELECTORS` 同一风格），取**最内层**命中的那个：
 * 查找条在编辑区之内，因此它是自己的区域（规格 §5.4 要求查找条自成循环）。
 * 区域之间按"内容在前、全局入口在后"衔接，边界处进入相邻区域的第一个可聚焦元素；
 * 没有任何可聚焦元素的区域直接跳过。
 *
 * 只处理真正的键盘 Tab：弹层/对话框有自己的焦点规则（紧凑输入窗口自行循环），
 * 组词期间不抢占，`body` 上没有区域时不干预（让浏览器决定起点）。
 */
const FOCUS_REGIONS = [
  { name: "side", selector: ".side-tool", segment: "content" },
  { name: "find", selector: ".current-find", segment: "content" },
  { name: "editorTabs", selector: ".editor-tabs", segment: "content" },
  { name: "editorContent", selector: ".editor-content", segment: "content" },
  // 规格 §7.16：终端标题栏的三个动作**按可见顺序循环** Tab/Shift+Tab（自成循环，不跨到别的区域）。
  // 之前它落在外层 `bottomTool` 区域里，于是第三个动作之后焦点直接离开标题栏 —— 与"循环"不符。
  { name: "terminalHeader", selector: ".terminal-header", segment: "content", wrap: true },
  { name: "bottomTool", selector: ".bottom-tool", segment: "content" },
  { name: "titlebar", selector: ".titlebar", segment: "global" },
  { name: "rail", selector: ".tool-rail", segment: "global" },
  { name: "statusbar", selector: ".statusbar", segment: "global" },
];

const FOCUSABLE_SELECTOR = "a[href], button, input, select, textarea, [tabindex]";

function isFocusableNode(node) {
  if (!node || node.disabled === true) return false;
  if (node.getAttribute && node.getAttribute("aria-disabled") === "true") return false;
  if (node.tabIndex < 0) return false;
  return node.getClientRects().length > 0;
}

/** 元素所属的区域：取最内层命中的区域（嵌套时以里层为准）。 */
function focusRegionOf(node) {
  let best = null;
  for (const region of FOCUS_REGIONS) {
    const element = node.closest(region.selector);
    if (!element) continue;
    if (!best || (best.element !== element && best.element.contains(element))) {
      best = { region, element };
    }
  }
  return best;
}

/** 区域内可直接聚焦的元素，按视觉顺序（先上后下，同一行先左后右）；排除嵌套区域。 */
function regionFocusables(regionElement) {
  const nodes = [...regionElement.querySelectorAll(FOCUSABLE_SELECTOR)].filter((node) => {
    if (!isFocusableNode(node)) return false;
    const owner = focusRegionOf(node);
    return !!owner && owner.element === regionElement;
  });
  nodes.sort((left, right) => {
    const a = left.getBoundingClientRect();
    const b = right.getBoundingClientRect();
    // 同一视觉行（顶边差在 4 像素内）按左边界排序，否则从上到下。
    if (Math.abs(a.top - b.top) > 4) return a.top - b.top;
    return a.left - b.left;
  });
  return nodes;
}

/** 同一段内的相邻区域（跳过没有可聚焦元素的区域）；段内环绕，不跨越内容段与全局段。 */
function moveFocusWithinSegment(owner, step) {
  const segment = FOCUS_REGIONS.filter((region) => region.segment === owner.region.segment);
  const index = segment.findIndex((region) => region.name === owner.region.name);
  if (index < 0) return false;
  for (let offset = 1; offset < segment.length; offset += 1) {
    const position = ((index + step * offset) % segment.length + segment.length) % segment.length;
    const element = document.querySelector(segment[position].selector);
    if (!element) continue;
    const candidates = regionFocusables(element);
    if (candidates.length === 0) continue;
    (step > 0 ? candidates[0] : candidates[candidates.length - 1]).focus();
    return true;
  }
  return false;
}

/** 段内没有别的可聚焦区域时的兜底：只有"离开内容段向前"和"离开全局段向后"才跨段。 */
function moveFocusAcrossSegments(owner, step) {
  const content = owner.region.segment === "content";
  const forward = step > 0;
  const crosses = content === forward;
  if (!crosses) {
    // 反方向边界（内容段后退 / 全局段前进）：留在本段内环绕到本区域自身。
    const own = regionFocusables(document.querySelector(owner.region.selector) || document.body);
    if (own.length > 0) (forward ? own[0] : own[own.length - 1]).focus();
    return;
  }
  const target = FOCUS_REGIONS.filter((region) => region.segment !== owner.region.segment);
  for (const region of (forward ? target : [...target].reverse())) {
    const element = document.querySelector(region.selector);
    if (!element) continue;
    const candidates = regionFocusables(element);
    if (candidates.length === 0) continue;
    (forward ? candidates[0] : candidates[candidates.length - 1]).focus();
    return;
  }
}

function bindRegionTabOrder() {
  if (!window.__augitLive || window.__augitRegionTabBound) return;
  window.__augitRegionTabBound = true;
  document.addEventListener("keydown", (event) => {
    if (event.key !== "Tab" || event.defaultPrevented) return;
    if (event.ctrlKey || event.altKey || event.metaKey) return;
    // 组词中的 Tab 交给输入法（规格 §5.3）。
    if (event.isComposing || event.keyCode === 229) return;
    const active = document.activeElement;
    if (!active || active === document.body) return;
    // 弹层与对话框自带焦点规则，不在这里接管。
    // 开放中的 `popover`（竖条溢出菜单等）与开放中的 `details` 菜单（筛选栏收纳菜单）在顶层、
    // 又不是 `[data-augit-overlay]`，此前漏掉了：Tab 于是被处理两次（本处理器一次 ＋ 弹层自己一次），
    // 在弹层里**跳过一个动作**（第 236 轮实测：焦点在「新建分支…」时按一次 Tab 直接落到「获取」，
    // 跳过「我的分支」）；筛选栏收纳菜单更直接 —— 焦点被带出菜单，键盘够不到收纳项（第 237 轮实测：
    // summary →「定位当前文件」）。这两处的键位由弹层自己负责，这里整体让开。
    if (active.closest("[data-augit-overlay], [popover]:popover-open, details[open]")) return;
    const owner = focusRegionOf(active);
    if (!owner) return;
    const items = regionFocusables(owner.element);
    if (items.length === 0) return;
    event.preventDefault();
    const step = event.shiftKey ? -1 : 1;
    const index = items.indexOf(active);
    // 自成循环的区域（规格 §7.16 终端标题栏）：Tab/Shift+Tab 在区内回绕，不跨到相邻区域。
    if (owner.region.wrap === true) {
      const current = index >= 0 ? index : 0;
      items[(current + step + items.length) % items.length].focus();
      return;
    }
    if (index >= 0) {
      const next = index + step;
      if (next >= 0 && next < items.length) {
        items[next].focus();
        return;
      }
    }

    // 区域边界：先在**同一段**内找相邻区域（内容段与全局入口段分开）。
    // 这样从内容区域的第一个元素反向走不会先穿越标题栏和工具栏，
    // 只有从内容段末尾继续向前才会进入全局入口；全局段反向离开时回到内容段末尾。
    if (!moveFocusWithinSegment(owner, step)) {
      moveFocusAcrossSegments(owner, step);
    }
  }, true);
}

/**
 * 文档模式记忆（规格 §7.3）：用户切换原文/对照/预览后，在**当前会话**里记住这个选择，
 * 切到别的标签再回来仍是离开时的模式。模式本身由渲染层的 `setMode` 产生并派发事件，
 * 这里只负责把它写到活动文档标签上（不写文件、不写设置）。
 */
function bindDocumentModeMemory() {
  if (!window.__augitLive || window.__augitDocumentModeBound) return;
  window.__augitDocumentModeBound = true;
  document.addEventListener("document-mode-changed", (event) => {
    const live = window.__augitLive;
    if (!live || !event.detail) return;
    const tab = (live.tabs || []).find((item) => item.id === live.activeTabId);
    if (!tab || tab.kind !== "document") return;
    tab.documentMode = event.detail.mode;
  });
}

/** 当前活动文档标签记住的模式；没有记住时返回 null（调用方回落到默认模式）。 */
// 窗口尺寸变化属于"改变窗口布局"：同样撤销"待跨文件"状态（规格 §7.7 第 10 条）。
window.addEventListener("resize", () => { clearDiffBoundaryHint(); });

window.__augitRememberedDocumentMode = () => {
  const live = window.__augitLive;
  if (!live) return null;
  const tab = (live.tabs || []).find((item) => item.id === live.activeTabId);
  return tab && tab.kind === "document" && typeof tab.documentMode === "string" ? tab.documentMode : null;
};

/** 关闭所有实时弹层。 */
function closeLiveOverlay() {
  // 关掉搜索浮层时必须**同时清状态**：否则下一次区域刷新会按 `live.searchOpen` 把它重新画出来
  //（第 240 轮：不清理就会"Esc 关掉、一刷新又回来"）。
  const live = window.__augitLive;
  if (live) {
    live.searchOpen = false;
    live.overlay = null;
  }
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

    // 「结果过多」对话框优先：Esc = 中止继续搜索（权威 `okCancel` 的取消分支），
    // 不把整个搜索浮层关掉。
    if (document.querySelector('.search-limit-dialog')) {
      event.preventDefault();
      abortLimitedSearch();
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

  if (action === "workspace-open") {
    openWorkspaceDialog();
    return;
  }

  if (action === "git-init") {
    // 权威 `GitInit`（VCS 菜单 → Create Git Repository…）：目标目录缺省为工作区根，
    // 需要别的目录时在窗口里用「选择目录…」走系统目录选择器。
    openRepositoryInitDialog();
    return;
  }

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

  if (action === "stash-manager") {
    void openStashManagerDialog();
    return;
  }

  if (action === "stash-create") {
    openStashDialog();
    return;
  }

  if (action === "worktree-manager") {
    void openWorktreeManagerDialog();
    return;
  }

  if (action === "reset") {
    openResetDialog();
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
    { action: "workspace-open", label: "打开工作区…" },
    { action: "refresh-tree", label: "刷新文件树" },
  ],
  view: [
    { action: "toggle-side", label: "显示/隐藏项目工具窗口" },
    { action: "toggle-bottom", label: "显示/隐藏底部工具窗口" },
  ],
  git: [
    // 权威 `GitInit` 的入口就在 VCS 菜单里（`action.Git.Init.text` = "Create Git Repository…"）。
    { action: "git-init", label: "创建 Git 仓库…" },
    { action: "fetch", label: "获取" },
    { action: "push", label: "推送…" },
    { action: "branches", label: "分支与标签…" },
    { action: "stash-manager", label: "Stash 管理…" },
    { action: "stash-create", label: "创建 Stash…" },
    { action: "worktree-manager", label: "Worktree 管理…" },
    { action: "reset", label: "Reset 当前分支…" },
  ],
};

/**
 * 自绘窗口按钮与标题栏拖动。
 *
 * 外壳窗口不再带 Win32 caption（否则右上角会同时出现原生关闭按钮与视觉稿自绘的关闭按钮，
 * 同一处两套按钮）。caption 去掉之后系统不再提供任何窗口操作入口，因此：
 * 最小化／最大化／关闭由视觉稿标题栏右侧的三个点发起，走宿主 window/* 命令；
 * 拖动只在标题栏**空白处**发起——标题栏左侧是主菜单、工作区、分支、快速打开等真实控件，
 * 整条交给系统 caption 会把这些控件的点击全部吞掉，所以由网页判断按在谁身上；
 * 最大化状态来自宿主事件：用户用快捷键、双击标题栏或贴边最大化时图标要跟着换。
 */
function bindWindowChrome() {
  if (window.__augitWindowChromeBound) return;
  window.__augitWindowChromeBound = true;

  document.addEventListener("click", (event) => {
    const dot = event.target.closest ? event.target.closest(".window-dot[data-window-action]") : null;
    if (!dot || !hasHost()) return;
    event.preventDefault();
    const action = dot.dataset.windowAction;
    const call = action === "minimize" ? "window/minimize"
      : action === "maximize" ? "window/maximize"
        : action === "close" ? "window/close" : null;
    if (!call) return;
    invoke(call).then((state) => applyWindowState(state)).catch(() => {});
  });

  // 控件自己会处理点击，不能在这里抢走鼠标。
  const CONTROLS = ".window-dot, .top-button, .top-chip, .titlebar-context, .main-menu-bar, "
    + ".main-menu-entry, a, button, input, select, textarea, [contenteditable='true']";
  document.addEventListener("mousedown", (event) => {
    if (event.button !== 0 || !hasHost()) return;
    const target = event.target;
    if (!target.closest || target.closest(CONTROLS)) return;

    // 先判边缘缩放：WebView2 的子窗口铺满客户区，系统会把边缘的命中测试交给子窗口，
    // 窗口自己的 WM_NCHITTEST 根本不会被问到（实测左边界内 3 像素处真实拖动，尺寸不变）。
    // 因此边缘与拖动同路：网页判断按在哪条边，宿主用原生缩放循环执行。
    const edge = windowEdgeAt(event.clientX, event.clientY);
    if (edge) {
      invoke("window/resize", { edge }).catch(() => {});
      return;
    }

    // 标题栏空白处才拖动——标题栏左侧是主菜单、工作区、分支、快速打开等真实控件。
    if (!target.closest(".titlebar")) return;
    // 宿主收到后交给系统移动循环：贴边、双击最大化、从最大化拖出还原都保持原生行为。
    invoke("window/drag").catch(() => {});
  });

  subscribe("window/state", (payload) => applyWindowState(payload));
  // 主题跟随 Windows：外壳在系统应用模式变化时推送新的生效主题（用户显式选浅色/深色时外壳不推）。
  // 主题只替换颜色与资源，不改变尺寸、间距和交互状态（design-system.md §10），
  // 因此这里只切 data-theme，并刷新按主题取色的自绘区域——终端画在 canvas 上，CSS 规则管不到它。
  subscribe("theme/changed", (payload) => {
    const name = String((payload && payload.theme) || "").trim().toLowerCase();
    if (name !== "dark" && name !== "light") return;
    if (name === "dark") document.body.dataset.theme = "dark";
    else delete document.body.dataset.theme;
    refreshTerminalTypography();
  });
  invoke("window/query").then((state) => applyWindowState(state)).catch(() => {});
}

/**
 * 窗口边缘命中：按**逻辑像素**计算，与宿主按 DPI 换算后的抓取宽度一致
 * （视觉稿在 175% 缩放下仍是 8 个 CSS 像素，宿主是 14 个物理像素）。
 * 返回 null 表示不在边缘；最大化时宿主会忽略缩放请求，这里不必重复判断。
 */
function windowEdgeAt(x, y) {
  // 8 个 CSS 像素就是宿主按 DPI 换算后的抓取宽度：175% 缩放下宿主是 14 物理像素，
  // 而 14 物理像素正好对应 8 个 CSS 像素，两边说的是同一条边。
  const size = 8;
  const width = window.innerWidth;
  const height = window.innerHeight;
  const vertical = y < size ? "top" : y >= height - size ? "bottom" : "";
  const horizontal = x < size ? "left" : x >= width - size ? "right" : "";
  if (vertical && horizontal) return vertical + horizontal;
  return vertical || horizontal || null;
}

/**
 * 应用窗口状态。图标就地替换而不重建标题栏：
 * 重建会收起已展开的内嵌主菜单，而规格要求最大化不改变其他窗口状态。
 */
function applyWindowState(state) {
  const live = window.__augitLive;
  const maximized = !!(state && state.maximized);
  if (live) live.windowMaximized = maximized;
  const dot = document.querySelector('.window-dot[data-window-action="maximize"]');
  if (!dot || typeof window.__augitIcon !== "function") return;
  const label = maximized ? "向下还原" : "最大化";
  dot.setAttribute("aria-label", label);
  dot.setAttribute("title", label);
  dot.innerHTML = window.__augitIcon(maximized ? "window-restore" : "window-maximize");
}

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
  // 标记"搜索浮层已打开"：结果的定点刷新要靠它让 `renderScene()` 产出 overlay 替换节点
  //（首次打开时区域里还没有 overlay，只能手动挂载；之后每次结果更新都走区域替换）。
  live.searchOpen = true;
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
 * 恢复"进入文件历史前的详情正文位置"（规格 §7.9 的**正文位置**）。
 *
 * 与既有的"End 意图"不同，这里按**数值**恢复。两处都要调用：详情可能是**缓存重绘**
 * （`refreshCommitDetails()` 直接返回、不重新请求），也可能是异步请求回来后再排版 ——
 * 因此只有当宿主真的已经排到那么高（`scrollHeight - clientHeight` 够）时才套用并清除待办，
 * 否则留给下一次渲染（第 164 轮实测：只挂在异步分支上，缓存重绘那条路径就恢复不到）。
 */
function applyPendingDetailScroll(consume) {
  const live = window.__augitLive;
  if (!live || typeof live.commitDetailScrollRestore !== "number") return;
  const host = document.querySelector("[data-live-commit-detail]");
  if (!host) return;
  if (host.scrollHeight - host.clientHeight < live.commitDetailScrollRestore) return;
  host.scrollTop = live.commitDetailScrollRestore;
  // 只有**异步详情真正排完**那一处才消费掉这个待办：重绘路径先套一次是"临时对齐"，
  // 之后详情还可能因为重新请求而整体重建（innerHTML 替换会把 scrollTop 清零），
  // 提前消费就再也恢复不到了（第 164 轮实测：`pending` 已为 null 但 scrollTop 仍是 0）。
  if (consume) live.commitDetailScrollRestore = null;
}

let detailScrollReapplyTimer = 0;

/**
 * 用户一旦自己操作详情正文（滚轮／按键／按下），就放弃"进入前位置"这份待办 ——
 * 不能拿程序恢复的位置去覆盖用户的新意图。
 */
function bindDetailScrollIntent() {
  const host = document.querySelector("[data-live-commit-detail]");
  if (!host || host.dataset.detailScrollBound === "true") return;
  host.dataset.detailScrollBound = "true";
  for (const type of ["wheel", "keydown", "pointerdown"]) {
    host.addEventListener(type, () => {
      const live = window.__augitLive;
      if (live) live.commitDetailScrollRestore = null;
    }, { passive: true });
  }
}

/**
 * 重绘路径套用"详情正文位置"之后，详情仍可能被**随后的定点刷新**再次替换（innerHTML 替换会把
 * scrollTop 清零）。因此在一个短延时内再对齐一次；待办本身留到异步详情真正排完或用户自己滚动时再清。
 */
function scheduleDetailScrollReapply() {
  const live = window.__augitLive;
  if (!live || typeof live.commitDetailScrollRestore !== "number") return;
  window.clearTimeout(detailScrollReapplyTimer);
  detailScrollReapplyTimer = window.setTimeout(() => {
    detailScrollReapplyTimer = 0;
    applyPendingDetailScroll(false);
  }, 150);
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
    // 换了提交：上一条待恢复的"正文位置"属于**另一个**提交，必须丢掉（否则会把新详情滚到旧位置）。
    live.commitDetailScrollRestore = null;
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
// 规格 §7.8：「超 150ms 显示局部加载与"取消比较"」——加载态的"取消比较"入口。
// 语义要点（同条规格的后半）：**失败与取消都保留标签可重试** ⇒ 只作废在途请求与加载态，**不关标签**。
document.addEventListener("click", (event) => {
  const cancel = event.target.closest && event.target.closest('[aria-label="取消比较"]');
  if (!cancel) return;
  event.preventDefault();
  const live = window.__augitLive;
  // 引用比较的**自身区域**入口（规格 §7.9 第二十条）：取消这次引用查询并隐藏该入口，
  // 晚到的响应由代际令牌拦掉（不关面板、不写全局提示）。
  if (cancel.dataset && cancel.dataset.branchCompareCancel === "true") {
    branchComparisonToken += 1;
    if (live && live.branchComparison) {
      live.branchComparison = { ...live.branchComparison, loading: false, cancelled: true, commits: [] };
    }
    refreshAfterEvent("bottomTool", "statusbar");
    return;
  }
  comparisonGeneration += 1;          // 在途请求的收尾据此失效（同一套代际机制）
  clearDiffLoadingMarker();
  if (live) live.diffLoading = false;
  refresh("editorContent", "editorTabs");
}, true);

// 规格 §7.8：读盘期间按 End 只**记录"定位末尾"的意图**，等完整排版结束后再执行。
document.addEventListener("keydown", (event) => {
  if (event.key !== "End") return;
  const live = window.__augitLive;
  if (!live || !live.commitDetailLoading) return;
  const panel = event.target.closest && event.target.closest(".log-detail-panel, [data-live-commit-detail]");
  if (!panel && !document.querySelector(".log-detail-panel")) return;
  live.commitDetailScrollIntent = "end";
}, true);

async function loadCommitDetails(revision) {
  const panel = document.querySelector(".log-detail-panel");
  if (!panel) return;
  const filesHost = panel.querySelector("[data-live-changed-files]");
  const detailHost = panel.querySelector("[data-live-commit-detail]");
  if (!filesHost || !detailHost) return;

  // 规格 §7.8：快速切换提交只接纳最新详情，旧详情不得恢复旧内容。
  const token = ++commitDetailsToken;
  // 规格 §7.8 另两半：**先呈现首段再更新滚动范围** + 期间按 **End 记录末尾意图、完成后执行**。
  // 实现方式：读盘期间置 `commitDetailLoading`（End 监听据此记录意图）；拿到载荷后先渲染**首段**，
  // 下一帧再渲染完整正文（滚动范围随之更新），最后若有末尾意图就滚到末尾。
  const live0 = window.__augitLive;
  if (live0) {
    live0.commitDetailLoading = true;
    live0.commitDetailScrollIntent = null;
  }
  try {
    const commit = await invoke("git/commit", { revision }, 30000);
    const live = window.__augitLive;
    if (token !== commitDetailsToken) {
      if (live) live.commitDetailLoading = false;
      return;
    }
    if (!commit || !commit.available) {
      const reasonHtml = `<p class="commit-meta">${escapeText(commit && commit.reason ? commit.reason : "无法读取提交详情")}</p>`;
      // 规格 §7.8：「提交信息读取超过既有 20 MB 输出边界时明确说明原因」。
      // 原因必须**同时**进详情栏：此前只写左栏（变化文件），右栏就只剩行头（主题/哈希/作者/日期），
      // 读者看不出正文为什么不见了；而且 `detailHtml` 留空时，任何区域重绘都会退回占位模板 ——
      // 那个模板固定取 `history.commits[0]`，选中别的提交就会把**第一个提交的主题**画进详情栏。
      // 桥接把「提交信息超限」与「变化文件列表超限」折叠成同一条 `reason`（`GitCommitDetailsResult`
      // 只有一个错误消息），因此两栏都如实写这条原因，不猜是哪一侧读失败。
      if (live) live.commitDetails = { revision, filesHtml: reasonHtml, detailHtml: reasonHtml };
      filesHost.innerHTML = reasonHtml;
      detailHost.innerHTML = reasonHtml;
      return;
    }

    // 结构必须与视觉稿一致：直接复用视觉稿的变更文件树构建器（目录分组、折叠箭头、
    // 状态色与 data-history-path 都在那一份实现里），不在实时层另拼一版。
    const files = (commit.files || []).map((file) => ({
      path: file.path,
      // 视觉稿的状态类名是小写（file-status-modified/-added/-deleted），宿主给的是
      // GitChangeKind（Modified/Added/...），这里统一转小写。
      status: String(file.kind || "modified").toLowerCase(),
      original: file.original || null,
    }));
    const filesHtml = commitFilesHtml(files, revision);
    const detailHtml = `<h3>${escapeText(commit.subject)}</h3>`
      + `<div>${escapeText(commit.hash)} · ${escapeText(commit.author)} · ${escapeText(commit.date)}</div>`
      + (commit.body ? `<p class="commit-meta">${escapeText(commit.body)}</p>` : "");
    // 详情内容必须进状态：mockup 的 Git 日志详情区只从状态渲染占位，真实内容由这里写进 DOM，
    // 因此任何包含 bottomTool 的区域刷新（打开历史比较、外部变化重载等）都会把占位写回去，
    // 只放在 DOM 里的内容会被静默丢弃（handoff 第 3 节第 5 条）。变化文件的**原始列表**同样进状态：
    // 折叠/选择变化后要用它按新状态重建树（`syncCommitFilesTree()`）。
    if (live) {
      live.commitDetails = { revision, files, filesHtml, detailHtml };
    }

    // ① 先呈现**首段**（主题/元信息 + 正文第一段），让用户尽快看到内容；
    const bodyText = commit.body ? String(commit.body) : "";
    const firstParagraph = bodyText.split(/\n\s*\n/)[0] || "";
    const firstStageHtml = `<h3>${escapeText(commit.subject)}</h3>`
      + `<div>${escapeText(commit.hash)} · ${escapeText(commit.author)} · ${escapeText(commit.date)}</div>`
      + (firstParagraph ? `<p class="commit-meta">${escapeText(firstParagraph)}</p>` : "");
    filesHost.innerHTML = filesHtml;
    detailHost.innerHTML = firstStageHtml;
    // 变化文件树的滚动监听要在每次写入后重挂（宿主节点是区域渲染的产物）；
    // 同一个提交被重新读取时（缓存重绘、返回日志）按状态回到用户原来的顶部位置。
    bindCommitFilesScroll();
    restoreCommitFilesScroll();
    window.__commitDetailStages = 1;
    // ② 下一帧再渲染完整正文：滚动范围在此时更新（首段阶段的范围与完整阶段不同）。
    await new Promise((resolve) => {
      if (typeof requestAnimationFrame === "function") requestAnimationFrame(() => resolve());
      else setTimeout(resolve, 0);
    });
    if (token !== commitDetailsToken) {
      if (live) live.commitDetailLoading = false;
      return;
    }
    detailHost.innerHTML = detailHtml;
    window.__commitDetailStages = 2;
    // 变化文件树可能是**缓存重绘**（`refreshCommitDetails()` 直接返回、不重新请求）之后
    // 由渲染路径写回来的 ⇒ 收尾处再对齐一次顶部位置（与详情正文位置同一处理）。
    restoreCommitFilesScroll();
    // ③ 读盘期间按过 End ⇒ 完成后执行"定位末尾"的意图。
    if (live && live.commitDetailScrollIntent === "end") {
      detailHost.scrollTop = detailHost.scrollHeight;
      live.commitDetailScrollIntent = null;
    }
    // 从文件历史返回时要恢复"进入前的详情正文位置"（规格 §7.9）；这是异步详情排完的收尾处 ⇒ 消费待办。
    applyPendingDetailScroll(true);
    if (live) live.commitDetailLoading = false;
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
      const commits = history.commits.map((commit) => ({
        hash: commit.hash,
        fullHash: commit.fullHash,
        subject: commit.subject,
        author: commit.author,
        // 作者列的值与 tooltip（权威 `FileHistoryPanelImpl.AuthorColumnInfo`）：
        // `*` 看作者是否等于提交者，tooltip 还要两组邮箱。
        authorEmail: commit.authorEmail,
        committerName: commit.committerName,
        committerEmail: commit.committerEmail,
        date: commit.date,
      }));
      live.fileHistory = {
        path: history.path,
        commits,
        // 选择是**状态**（规格 §7.9 第二条）：默认落在最新一条，与视觉稿"首行选中"一致。
        // 在状态层初始化（而不是留给渲染兜底），返回上下文、预览取消与重绘三条路径才都读到同一个键。
        selectedFull: (commits[0] && commits[0].fullHash) || null,
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
  // 分页状态随历史一起附着。`loadHistory` 可能早于 `window.__augitLive` 建立（见其注释），
  // 那时只能把 `hasNextPage` 存在 `latestHistory` 上；`loadDocument()` 末尾会再调一次本函数，
  // 这里把页码补进状态。只在未设置时初始化，避免覆盖 `loadHistoryPage` 已推进的页码。
  if (typeof live.historyPage !== "number") live.historyPage = 0;
  if (typeof live.historyHasNextPage !== "boolean") live.historyHasNextPage = !!latestHistory.hasNextPage;
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
  if (hasHost()) {
    // 把真实克隆交给视觉稿的 Clone 对话框调用：校验、焦点与冻结逻辑已在其中实现。
    // 不再只对场景页生效——「打开工作区」页的「克隆仓库…」也要用同一个对话框。
    window.__augitCloneRequest = (request) => invoke("git/clone", request, 600000);
    // 规格 §7.12 第 5 条「取消先请求 Git 结束」：宿主把 `git/clone` 与其它写操作放在同一个
    // 写队列里，`write/cancel` 会取消正在运行的 Git。对话框的「取消」/Esc 因此必须先调它，
    // 再等 `__augitCloneRequest` 的 promise 收尾（不能像视觉稿那样用模拟计时器把取消当完成）。
    window.__augitCloneCancel = () => invoke("write/cancel", {}, 30000);
    // Reset 同理：模式说明、影响预览、进行态冻结与 Tab/Enter/Esc 循环都在视觉稿的对话框里，
    // 实时外壳只把"执行"与"取消"接到宿主，并在结果后重读真实状态（规格 §7.11 / §9.3）。
    window.__augitResetRequest = async (request) => {
      let result = null;
      try {
        result = await invoke("git/reset", request, 600000);
      } catch (error) {
        result = { available: true, reset: false, reason: String((error && error.message) || error) };
      }
      if (!result || !result.reset) {
        return {
          ok: false,
          reason: describeFailure((result && result.reason) || "Reset 未完成。", {
            unchanged: "HEAD、索引、工作区与改动列表没有被修改。",
          }),
        };
      }
      // 不假设成功即生效：重新读取真实仓库状态后再刷新相关区域。
      await Promise.all([loadStatus().catch(() => null), loadHistory().catch(() => null)]);
      refresh("side", "editorContent", "editorTabs", "statusbar", "titlebar", "bottomTool");
      window.__augitResetResult = { target: result.target || null, mode: result.mode || null };
      return { ok: true };
    };
    window.__augitResetCancel = async () => {
      let stopped = false;
      try {
        const payload = await invoke("write/cancel", {}, 30000);
        stopped = !!(payload && payload.cancelled);
      } catch {
        stopped = false;
      }
      // 不假设取消生效：任何结果都重读真实仓库状态。
      await Promise.all([loadStatus().catch(() => null), loadHistory().catch(() => null)]);
      refresh("side", "editorContent", "statusbar");
      return { stopped };
    };
    // 关闭 Reset 对话框时归还打开前焦点（规格 §5.3）；视觉稿的对话框本身没有焦点栈。
    window.__augitResetClosed = () => restoreDialogFocus();
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
    // `--diff` 打开的是**工作区 Diff**（不建比较标签）：只有这种视图才有"上一个文件/计数/下一个文件"。
    window.__augitLive.workspaceDiff = true;
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

  // 非 Git 目录：此刻 `mockup.js` 已加载、live 对象也已建立，可以给出「创建 Git 仓库」入口。
  if (pendingRepositoryInit && window.__augitLive) {
    pendingRepositoryInit = false;
    showRepositoryInitEntry();
  }

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
      // 选中要写进状态：区域刷新会重建列表，只写在 DOM 上的选中会被重置成首行
      //（规格 §6.4「刷新后提交选择不变」）。渲染按**短**哈希比较（`liveGitLog` 的 `isCommitSelected`），
      // 提交详情仍按完整哈希读取。
      const live = window.__augitLive;
      if (live && selected.dataset.hash) live.historySelectedHash = selected.dataset.hash;
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
  // 显式文档类启动参数优先于会话恢复：restoreSession 是 fire-and-forget，
  // 它会在文档参数（--open/--blame/--file-history/--diff/--conflict）之后才落地，
  // 并把恢复集合里的活动文件重新激活 —— 实测 `--blame docs/product-spec.md` 时
  // blame 标签已排在首位却不是活动标签，编辑器显示的是恢复出来的二进制文档。
  const explicitDocument = Boolean(
    requestedDocument || requestedBlame || requestedFileHistory || requestedDiff || requestedConflict);
  // 审计用（`--no-session-restore`）：逐场景截图必须只取决于启动参数；
  // 会话恢复会把上一次运行的活动文件带进来，把场景渲染成另一个页面（实测踩过）。
  const noSessionRestore = query.get("no-session-restore") === "1";
  if (!explicitDocument && !noSessionRestore) void restoreSession();
  if (wantsSettings) {
    window.__augitRender();
    bindSettingsSave();
    // 场景渲染的设置页同样要能切分类（`__augitRender` 只走 bindInteractions，
    // 不经过 rebindAfterRender，这里显式补一次；函数本身有幂等守卫）。
    bindSettingsPages();
    void ensureGitDetection();
  }

  if (wantsTerminal) {
    // 终端是独占资源，只在终端场景启动，避免无谓的常驻进程。
    await startTerminal();
    window.__augitTerminalReady = true;
  }

  startWorkspaceChangePolling();
  bindPanelDividers();
  bindImageDecodeFallback();

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
  // 选中态进状态（规格 §6 第 37 条）：异步读取收尾重绘侧栏时不得把选中跳回刚打开的文件行。
  const liveSelection = window.__augitLive;
  if (liveSelection) liveSelection.treeSelectedPath = row.dataset.treePath || null;
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

// 改动工具窗工具栏的「预览」（规格 §7.7 第 14 条）：Markdown 与 JSON 的默认 Git 页面仍是**磁盘真实文本 diff**，
// 修改后的**预览**从工具栏打开。此前这个按钮在 live 里**没有任何处理者**（第 250 轮实测：点下去编辑区仍是差异、
// 读取次数 0、连"未接线"都不记录）⇒ 现在把选中的改动文件按只读文档打开。
// Markdown 显式切到**预览**模式（与文档工具栏的原文／对照／预览同一套模式状态）；
// JSON 不用额外设置——`liveJsonDocument()` 的默认就是格式化视图（无效 JSON 时才回落原文）。
document.addEventListener("click", (event) => {
  const live = window.__augitLive;
  if (!live) return;
  const button = event.target.closest
    && event.target.closest('.side-tool .changes-layout > .toolbar [aria-label="预览"]');
  if (!button) return;
  event.preventDefault();
  event.stopPropagation();
  const path = live.selectedChangePath
    || ((live.status && live.status.files && live.status.files[0]) ? live.status.files[0].path : null);
  if (!path) {
    window.__augitError = "preview:no-selection";
    return;
  }
  void openDocument(path).then(() => {
    if (!/\.(md|markdown)$/i.test(path)) return;
    const liveNow = window.__augitLive;
    if (!liveNow) return;
    const tab = (liveNow.tabs || []).find((item) => item.id === liveNow.activeTabId);
    if (tab && tab.kind === "document") tab.documentMode = "preview";
    refresh("editorContent", "editorTabs");
  }).catch(() => null);
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

// 变化文件树的**目录行**：点箭头（或整行）折叠/展开（规格 §7.9 条目三）。
// 此前这里的箭头只是装饰：`historyFilesHtml()` 画了 chevron-down，却没有任何绑定，
// 点了不动、也没有可折叠的语义。折叠状态进 `live.commitDetailsUi.collapsed`，再按状态重建树。
document.addEventListener("click", (event) => {
  const group = event.target.closest && event.target.closest("[data-live-changed-files] [data-history-group]");
  const live = window.__augitLive;
  if (!group || !live || !live.commitDetailsUi) return;
  event.preventDefault();
  const key = group.dataset.historyGroup || "";
  const collapsed = live.commitDetailsUi.collapsed;
  if (collapsed[key]) delete collapsed[key];
  else collapsed[key] = true;
  syncCommitFilesTree();
}, true);

// 历史提交里的变化文件：双击或 Enter 打开历史比较，单击只选择并跟随（规格 §7.8）。
// 与 mockup 里既有的历史比较实现保持同一套交互契约。
document.addEventListener("click", (event) => {
  const row = event.target.closest && event.target.closest("[data-live-changed-files] [data-history-path]");
  if (!row || !window.__augitLive) return;
  event.preventDefault();
  for (const other of historyFileRows()) other.classList.remove("selected");
  row.classList.add("selected");
  // 选择同样进状态：区域重绘后 `historyFilesHtml()` 按它回填 `.selected`（规格 §7.9 条目三）。
  rememberHistoryFileSelection(row);
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
  rememberHistoryFileSelection(row);
  void openHistoryComparison(row);
}, true);

// 变化文件行右键打开上下文菜单；打开菜单不打开比较（规格 §7.8）。
document.addEventListener("contextmenu", (event) => {
  const row = event.target.closest && event.target.closest(".changes-list .change-file-row");
  if (!row || !window.__augitLive) return;
  event.preventDefault();
  openChangesContextMenu(row, event.clientX, event.clientY);
}, true);

// 「Amend」勾选后读取上一次提交信息；取消后恢复用户尚未提交的原文本（规格 §7.6）。
//
// 三处实测教训（前两处来自第 100 轮，第三处来自"一半未通过"的复盘）：
// ① 视觉稿的 Amend 是 `<button role="checkbox" aria-checked>`（fake-check），**不是 input** ——
//    既不能监听 `change`、也不能读 `.checked`；
// ② 状态由应用自己的处理翻转，所以必须**捕获阶段 + 延后一拍**读取才读得到翻转后的值；
// ③ 第 100 轮把草稿存在 `dataset` 上**活不过提交区重渲染**（取消后字段被应用自身的草稿绑定覆盖）⇒
//    草稿改存**模块级 Map**。
//
// 第 216 轮按权威补齐"仅在用户没改过信息时才覆盖/恢复"（`AmendCommitHandlerImpl.kt:78-115`）：
// 字段值本身改由 `live.commitDraft` 跨重渲染保持（载入/恢复时两处一起写），`amendDrafts`
// 只记录一次"载入 → 可恢复"的成对数据 `{ before, amend }`；是否载入由 `amendInitialMessages`
// 的基线判定。
const amendDrafts = new Map();

function amendFieldKey(field) {
  const layout = field.closest(".changes-layout") || field.closest(".commit-box");
  return (layout && (layout.getAttribute("data-workspace") || layout.id)) || "commit-box";
}

/**
 * Amend 勾选时提交动作**改名**（权威）。
 *
 * `AmendCommitHandlerImpl.kt:50-52` 在切换 Amend 后调 `workflowHandler.updateDefaultCommitActionName()`，
 * 文案随之改变：`amend.action.name` = "Amend {0}"（`VcsBundle.properties:38`）、
 * `action.amend.commit.and.push.text` = "Amend Commit and Push…"（`DvcsBundle.properties:128`）。
 * **是改名不是换动作** —— 类名与点击路径都不变（`live-data.js` 的 `.commit-actions` 分支照旧按
 * `primary-button` / "提交并推送" 分流；注意"修改提交并推送…"仍含子串"提交并推送"）。
 * 按 Augit 中文界面本地化。
 *
 * 每次重渲染后也要重贴（区域刷新会把文案恢复成渲染默认值），因此由 `restoreAmendDraft()` 统一调用。
 */
function applyAmendActionLabel(box, checked) {
  const actions = box && box.querySelector(".commit-actions");
  if (!actions) return;
  const primary = actions.querySelector(".primary-button");
  const secondary = actions.querySelector(".secondary-button");
  if (primary) primary.textContent = checked ? "修改提交" : "提交";
  if (secondary) secondary.textContent = checked ? "修改提交并推送…" : "提交并推送…";
}

/**
 * 面板"激活"时的提交信息（权威 `initialMessage`）。
 *
 * `SingleChangeListCommitWorkflowHandler.kt:75` 在 `activate()` 里执行
 * `amendCommitHandler.initialMessage = getCommitMessage()` —— 即**面板打开那一刻**的信息；
 * `AmendCommitHandlerImpl.kt:82` 用它判断"用户有没有改过"：只有
 * `initialMessage == null || beforeAmendMessage == initialMessage` 才允许载入上一次提交信息。
 * Augit 没有 Swing 的 `activate()` 事件，用"每个提交框第一次出现时看到的值"作为基线；
 * 提交成功后清空（下一次是新面板）。
 */
const amendInitialMessages = new Map();

/** 权威 `StringUtil.equalsIgnoreWhitespaces`：忽略空白比较提交信息。 */
function equalIgnoringWhitespace(left, right) {
  return String(left).replace(/\s+/g, "") === String(right).replace(/\s+/g, "");
}

/** 渲染后重贴 Amend 的动作文案，并记录"面板激活时的初始信息"。 */
function restoreAmendDraft() {
  document.querySelectorAll(".commit-box .message-field, .commit-box textarea").forEach((field) => {
    const box = field.closest(".commit-box");
    const amend = box && box.querySelector('[aria-label="Amend"]');
    if (!amend) return;
    const checked = amend.matches('input[type="checkbox"]')
      ? amend.checked
      : amend.getAttribute("aria-checked") === "true";
    // 提交动作的文案随 Amend 状态（权威 `updateDefaultCommitActionName()`）。
    applyAmendActionLabel(box, checked);
    const key = amendFieldKey(field);
    if (!amendInitialMessages.has(key)) amendInitialMessages.set(key, field.value);
  });
}

document.addEventListener("click", (event) => {
  const amend = event.target.closest && event.target.closest('.commit-box [aria-label="Amend"]');
  if (!amend) return;
  const box = amend.closest(".commit-box");
  const field = box && box.querySelector(".message-field, textarea");
  if (!field) return;
  const key = amendFieldKey(field);
  setTimeout(() => {
    const checked = amend.matches && amend.matches('input[type="checkbox"]')
      ? amend.checked
      : amend.getAttribute("aria-checked") === "true";
    // 文案随状态（权威 `updateDefaultCommitActionName()`，见 applyAmendActionLabel 注释）。
    applyAmendActionLabel(box, checked);
    if (checked) {
      // 权威 `AmendCommitHandlerImpl.kt:78-89`：只有"用户没改过信息"（当前值 == 面板激活时的
      // 初始信息 `initialMessage`）才载入上一次提交信息；用户改过就保持他的文本 ——
      // 不覆盖、不记 `AmendData`、也不移焦点。
      const initial = amendInitialMessages.has(key) ? amendInitialMessages.get(key) : field.value;
      const before = field.value;
      if (before !== initial) return;
      void invoke("git/last-commit-message", {}, 30000).then((result) => {
        if (!result || !result.available || typeof result.message !== "string") {
          window.__augitError = "amend:last-commit-unavailable";
          return;
        }
        const amendMessage = result.message;
        // 权威 `:98-100`：忽略空白相等时不动字段，也不记 `AmendData`。
        if (equalIgnoringWhitespace(before, amendMessage)) return;
        amendDrafts.set(key, { before, amend: amendMessage });
        field.value = amendMessage;
        // 字段值同时写进提交草稿，区域刷新后不会退回原文（`restoreChangesState()`）。
        rememberCommitDraft(amendMessage);
        // 权威 `AmendCommitHandlerImpl.kt:117-119` 的 `setCommitMessageAndFocus()`：
        // 载入上次提交信息后**把焦点移到提交信息栏**。
        field.focus({ preventScroll: true });
      }).catch(() => { window.__augitError = "amend:last-commit-failed"; });
    } else {
      const draft = amendDrafts.get(key);
      if (!draft) return;
      amendDrafts.delete(key);
      // 权威 `:107-115` 的 `restoreBeforeAmendMessage()`：只有字段**仍等于**载入的 amend 信息
      // 才恢复"进入 amend 前"的信息；用户改过就保留他的文本。
      if (field.value === draft.amend) {
        field.value = draft.before;
        rememberCommitDraft(draft.before);
      }
    }
  }, 0);
}, true);

/**
 * 比较工具栏的「上一个文件/下一个文件」与"文件计数"（规格 `ux-spec.md:438/441/442`）。
 *
 * 视觉稿侧本来就是对的：工作区 Diff 的工具条顺序是
 * `上一处差异, 下一处差异, 查找, 上一个文件, <span class="file-status-modified">1/42 个文件</span>, 下一个文件, …`
 * —— 缺的是 **live 端行为**（本函数补的就是这一段）：切换相邻改动文件、同步 Changes 选中与文件计数。
 * 边界（首/尾）本增量**留在原地**；规格要求的"再次点击才跨文件"的提示留给后续增量（§3.2 #26）。
 */
function diffChangedFiles() {
  return [...document.querySelectorAll(".changes-list .change-file-row[data-path]")]
    .map((row) => row.dataset.path)
    .filter((path) => typeof path === "string" && path.length > 0);
}

function moveDiffFile(direction) {
  const files = diffChangedFiles();
  if (files.length === 0) return null;
  const live = window.__augitLive;
  const current = live && live.diff && live.diff.path ? live.diff.path : null;
  let index = files.indexOf(current);
  if (index < 0) index = direction > 0 ? -1 : files.length;
  const next = index + direction;
  if (next < 0 || next >= files.length) return null;
  const path = files[next];
  for (const row of document.querySelectorAll(".changes-list .change-file-row.selected")) {
    row.classList.remove("selected");
    row.setAttribute("aria-selected", "false");
  }
  const row = document.querySelector(`.changes-list .change-file-row[data-path="${CSS.escape(path)}"]`);
  if (row) {
    row.classList.add("selected");
    row.setAttribute("aria-selected", "true");
  }
  // 规格 §441：文件箭头是**工作区 Diff 视图内**的导航 —— 不能像 `openChangeDiff` 那样另开比较标签
  // （那会切到 comparison 变体，工具条上的文件导航与计数随之消失，第 102 轮实测就是这个现象）。
  // 现在 `liveDiffView` 自己渲染计数（`live.status.files` + `diff.path`），这里只切换装载目标。
  live.followChanges = true;
  live.selectedChangePath = path;
  // 跨文件查询也要有加载反馈（规格 §6.5 的 150ms 阈值 / §7.7 第 9 条"查询期间禁用差异箭头"）：
  // 此前这条路径**不设**加载标记 ⇒ `live.diffLoading` 永远为假，`liveDiffView` 的加载分支
  // （禁用差异箭头 + 标题行提示）在工作区 Diff 的文件切换上根本不可达（第 248 轮实测）。
  scheduleDiffLoadingMarker();
  void loadDiff(path)
    .then(() => { clearDiffLoadingMarker(); refreshAfterEvent("editorContent", "statusbar"); })
    .catch(() => { clearDiffLoadingMarker(); refreshAfterEvent("editorContent", "statusbar"); });
  return { path, index: next, total: files.length };
}

document.addEventListener("click", (event) => {
  const button = event.target.closest
    && event.target.closest('.diff-toolbar [aria-label="上一个文件"], .diff-toolbar [aria-label="下一个文件"]');
  if (!button) return;
  event.preventDefault();
  window.__diffFileHit = (window.__diffFileHit || 0) + 1;
  moveDiffFile(button.getAttribute("aria-label") === "下一个文件" ? 1 : -1);
}, true);

// 比较工具栏「忽略空白」（规格 §7.7 第 5 条）：这是**真实的差异选项**——宿主 `git/diff` 收
// `ignoreWhitespace`（`ShellBridge.cs`），请求键与补丁缓存键都已含它，因此切换后必须**重查**，
// 不能只改按钮外观。没有正文（加载/空差异）时只改状态，下一次装载自然生效。
document.addEventListener("click", (event) => {
  const button = event.target.closest && event.target.closest('.diff-toolbar [aria-label="忽略空白"]');
  if (!button) return;
  event.preventDefault();
  const live = window.__augitLive;
  if (!live) return;
  // 文件历史预览有自己的忽略空白选项（与编辑器正文互不影响）：它是真实差异选项 ⇒ 重查。
  if (button.closest("[data-live-file-history-preview]")) {
    const preview = live.fileHistoryPreview;
    if (!preview) return;
    const next = !preview.ignoreWhitespace;
    button.setAttribute("aria-pressed", next ? "true" : "false");
    button.classList.toggle("active", next);
    void loadFileHistoryPreview({ ignoreWhitespace: next, force: true });
    return;
  }
  live.diffOptions = live.diffOptions || {};
  live.diffOptions.ignoreWhitespace = !live.diffOptions.ignoreWhitespace;
  button.setAttribute("aria-pressed", live.diffOptions.ignoreWhitespace ? "true" : "false");
  button.classList.toggle("active", live.diffOptions.ignoreWhitespace);
  const path = live.diff && live.diff.path;
  if (!path) return;
  void loadDiff(path, { force: true })
    .then(() => refreshAfterEvent("editorContent", "statusbar"))
    .catch(() => null);
}, true);

// 比较工具栏「设置」：按应用既有约定打开设置对话框（与提交框的「提交设置」同一入口）。
document.addEventListener("click", (event) => {
  const button = event.target.closest && event.target.closest('.diff-toolbar [aria-label="设置"]');
  if (!button) return;
  event.preventDefault();
  openSettingsDialog();
}, true);

// 比较工具栏「上一处/下一处差异」（规格 §7.8/§7.9/§7.10）。
// 用**捕获阶段**的独立监听器，而不是大点击链的尾部：链尾可能被更早监听器的
// `preventDefault()` 影响（那里有 `if (event.defaultPrevented) return;` 守卫），
// 而捕获阶段先于所有冒泡监听器执行 —— 这也与项目树/上下文菜单的既有做法一致。
document.addEventListener("click", (event) => {
  const button = event.target.closest
    && event.target.closest('.diff-toolbar [aria-label="上一处差异"], .diff-toolbar [aria-label="下一处差异"]');
  if (!button) return;
  window.__diffNavHit = (window.__diffNavHit || 0) + 1;
  event.preventDefault();
  // 同一套箭头服务两份正文：点在预览工具条上时只在**预览**里定位（作用域交给 `moveDiffChange`）。
  moveDiffChange(button.getAttribute("aria-label") === "下一处差异" ? 1 : -1,
    button.closest("[data-live-file-history-preview]"));
}, true);

// 项目树右键打开上下文菜单（规格 §5.4）。
// 菜单键（Shift+F10 / ContextMenu）走同一条路径：先聚焦该行再打开。
document.addEventListener("contextmenu", (event) => {
  const row = event.target.closest && event.target.closest(".side-content.tree .tree-row");
  if (!row || !window.__augitLive) return;
  event.preventDefault();
  openTreeContextMenu(row, event.clientX, event.clientY);
}, true);

// 引用树行：右键（以及菜单键／Shift+F10）打开该引用自己的动作菜单。
// 权威 `BranchesTree.kt:272` 给树装了 `BranchesTreeActionGroup` 弹出组，右键的选择规则见
// `Tree.java:1112-1130`（`openRefTreeMenu` 里实现）。
document.addEventListener("contextmenu", (event) => {
  const row = event.target.closest && event.target.closest(".log-ref-panel .tree-row[data-ref-name]");
  if (!row || !window.__augitLive) return;
  event.preventDefault();
  openRefTreeMenu(row, event.clientX, event.clientY);
}, true);

document.addEventListener("keydown", (event) => {
  if (event.key !== "ContextMenu" && !(event.shiftKey && event.key === "F10")) return;
  const row = document.activeElement;
  if (!row || !row.closest || !row.closest(".log-ref-panel")) return;
  if (!row.dataset || !row.dataset.refName) return;
  event.preventDefault();
  const rect = row.getBoundingClientRect();
  openRefTreeMenu(row, Math.round(rect.left + 12), Math.round(rect.bottom));
}, true);

// 分支/标签行：右键（以及菜单键／Shift+F10）打开该引用自己的动作菜单（规格 §5.2）。
document.addEventListener("contextmenu", (event) => {
  const row = event.target.closest && event.target.closest(".branches-popover [data-branch]");
  if (!row || !window.__augitLive) return;
  event.preventDefault();
  openRefMenu(row, event.clientX, event.clientY);
}, true);

document.addEventListener("keydown", (event) => {
  if (event.key !== "ContextMenu" && !(event.shiftKey && event.key === "F10")) return;
  const row = event.target.closest && event.target.closest(".branches-popover [data-branch]");
  if (!row || !window.__augitLive) return;
  event.preventDefault();
  const rect = row.getBoundingClientRect();
  openRefMenu(row, Math.round(rect.left + 12), Math.round(rect.bottom));
}, true);

// 日志提交行：右键（以及菜单键／Shift+F10）打开提交菜单（规格 §7.8）。
// 此前 `gitLogContextMenu()` 只被静态场景用到，实时外壳里**没有任何 contextmenu 处理者** ⇒
// 用户右键提交行什么都不发生（第 167 轮实测）。
document.addEventListener("contextmenu", (event) => {
  const row = event.target.closest && event.target.closest(".commit-list .commit-row");
  if (!row || !window.__augitLive) return;
  event.preventDefault();
  openLogContextMenu(row, event.clientX, event.clientY);
}, true);

document.addEventListener("keydown", (event) => {
  if (event.key !== "ContextMenu" && !(event.shiftKey && event.key === "F10")) return;
  const row = event.target.closest && event.target.closest(".commit-list .commit-row");
  if (!row || !window.__augitLive) return;
  event.preventDefault();
  const rect = row.getBoundingClientRect();
  openLogContextMenu(row, Math.round(rect.left + 12), Math.round(rect.bottom));
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
  } else if (event.key === "Enter") {
    // 规格 §12.5：「单击只选择，双击或 Enter 才正式打开」；目录行仍是展开/折叠。
    // 此前只接了双击与方向键，Enter 落空（第 81 轮 harness 实测：行保持焦点、document 仍为 null）。
    event.preventDefault();
    window.__treeEnterFired = (window.__treeEnterFired || 0) + 1;
    void activateTreeRow(row, { open: true });
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

/**
 * 预览失败后"再次点击预览重试"（规格 §7.3）。预览区里的 `<article class="markdown-preview">`
 * 本身可聚焦（`tabindex="0"`），因此同时接受点击与 Enter/Space —— 键盘用户与鼠标用户走同一条路径。
 */
function retryMarkdownPreview(event) {
  const live = window.__augitLive;
  if (!live || !live.markdownPreview || live.markdownPreview.state !== "failure") return;
  const preview = event.target && event.target.closest && event.target.closest(".markdown-preview");
  if (!preview) return;
  const path = live.markdownPreview.retryPath;
  if (!path) return;
  event.preventDefault();
  void openDocument(path);
}

document.addEventListener("click", retryMarkdownPreview, true);
document.addEventListener("keydown", (event) => {
  if (event.key !== "Enter" && event.key !== " ") return;
  retryMarkdownPreview(event);
}, true);

/** 把宿主返回的文档结果整理成界面需要的形状。 */
function toLiveDocument(payload) {
  const kind = payload.kind || "Text";
  const imageKind = ["Png", "Jpeg", "Bmp", "Gif", "WebP"].includes(kind);
  const editor = kind === "Markdown" ? "markdown"
    : kind === "Json" ? "json"
      // 图片只有宿主**确实解码成功**时才能按图片渲染（实测：`ImageDecodeFailed`/`ImageTooLarge`
      // 的 `dataUrl` 是 null，按 kind 直接映射会让正文只剩一张 src 为空的破图，
      // 宿主给出的原因在界面上没有任何位置 —— 违反 §10.2）。
      // GIF/WebP 也走同一条：宿主 `IsSupportedImage` 只含 Png/Jpeg/Bmp，它们本就是二进制摘要。
      : imageKind ? (payload.status === "ImageReady" ? "image" : "file-limit")
        // 大文件只读预览：权威把它交给**纯文本编辑器**（`LargeFileEditorProvider` 建的是
        // `TextEditorImpl` ＋ `editor.setViewer(true)`，`LargeFileEditorProvider.java:53-59`），
        // 没有 Markdown/JSON 之类的语言能力 ⇒ 即使扩展名是 .md/.json 也走 `text`
        // （否则截断的 Markdown 会被当完整文档渲染）。
        : payload.status === "TextPreview" ? "text"
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
    // 图片文档的位图数据（宿主 document/read 返回的 data: URL）。
    // toLiveDocument 是白名单映射，不显式搬运的话渲染层读到的 dataUrl 永远是 undefined，
    // 图片预览就只能停在加载指示上（第 246-253 轮实测）。
    dataUrl: payload.dataUrl || null,
    fileSize: payload.fileSize,
    text: payload.text,
    preview: kind === "Markdown" && payload.text ? renderMarkdown(payload.text) : "",
    // 宿主的 JSON 结果（规格 §7.4）：格式有效时给重新序列化的正文，
    // 无效时给从 1 开始的行号与按 Unicode 标量计的列号。
    // toLiveDocument 是白名单映射，不搬运的话渲染层永远读到 undefined（同 dataUrl 的教训）。
    formatted: typeof payload.formatted === "string" ? payload.formatted : "",
    jsonError: payload.jsonError && typeof payload.jsonError.line === "number"
      ? { line: payload.jsonError.line, column: payload.jsonError.column }
      : null,
    dataUrl: payload.dataUrl,
    pixelWidth: payload.pixelWidth,
    pixelHeight: payload.pixelHeight,
    message: payload.message,
    lineEndings: payload.lineEndings,
    encoding: payload.encoding,
    // 大文件只读预览（宿主 `DocumentReadStatus.TextPreview`）：横幅文案要 `{0}` 文件大小与
    // `{1}` 预览字节数（权威 `LargeFileNotificationProvider` 的 `large.file.preview.notification`）。
    readOnlyPreview: payload.status === "TextPreview",
    previewBytes: typeof payload.previewBytes === "number" ? payload.previewBytes : null,
    editor,
  };
}

/**
 * Markdown 预览的局部状态（规格 §7.3）：`live.markdownPreview` = `{state, reason, retryPath}`。
 * 只服务**已经显示着 Markdown 文档**时的两种状态：
 *   `loading` —— 下一次读取仍在途且已超过 `LoadingFeedbackDelay`（150ms）；
 *   `failure` —— 读取失败：保留原文与旧预览，在预览区顶部给出原因，点预览重试。
 * 状态是会话数据（`live`），DOM 只按它渲染；`syncMarkdownPreviewState()` 是**不重绘正文**的局部同步，
 * 这样 150ms 提示不会把原文滚动位置与对照比例冲掉。
 */
let markdownPreviewHintTimer = null;

function isMarkdownDocumentVisible() {
  const live = window.__augitLive;
  return !!(live && live.document && live.document.editor === "markdown");
}

function syncMarkdownPreviewState() {
  const view = document.querySelector(".markdown-document");
  if (!view) return;
  const state = window.__augitLive && window.__augitLive.markdownPreview;
  const value = state && (state.state === "loading" || state.state === "failure") ? state.state : "ready";
  view.dataset.markdownState = value;
  view.dataset.markdownReason = value === "failure" ? String(state.reason || "") : "";
  // 只重跑 Markdown 绑定（`mockup.js` 的 `bindMarkdownModes` 会先释放上一次绑定）：
  // 反馈文案与"失败时可点预览重试"的语义都由它统一处理，不在这里复制一份文案。
  if (typeof window.__augitBindMarkdown === "function") window.__augitBindMarkdown();
}

/** 读取在途超过 150ms 才给提示（规格 §7.3）；读取结束时由 `openDocument` 取消。 */
function scheduleMarkdownPreviewHint(token) {
  clearTimeout(markdownPreviewHintTimer);
  markdownPreviewHintTimer = null;
  if (!isMarkdownDocumentVisible()) return;
  markdownPreviewHintTimer = setTimeout(() => {
    markdownPreviewHintTimer = null;
    if (token !== documentToken) return;
    if (!isMarkdownDocumentVisible()) return;
    const live = window.__augitLive;
    if (!live || !live.pendingDocument) return;
    // 记下"提示属于哪个文档"：切到别的标签时由 `syncActiveTab()` 清掉，
    // 否则提示（以及失败态的重试入口）会跟到另一个文件的预览上。
    live.markdownPreview = { state: "loading", path: live.document ? live.document.path : null };
    syncMarkdownPreviewState();
  }, LoadingFeedbackDelay);
}

/**
 * 图片画布的加载提示（规格 §7.5）：**已经显示着图片**时重新读取超过 `LoadingFeedbackDelay`（150ms）
 * 才在画布中心显示"正在读取文件…"，不改变工具栏、标签或面板尺寸；完成或失败后撤去。
 * 短读取不闪提示。状态进 `live.imagePreview`，DOM 只按状态渲染 —— 与 Markdown 预览的 §7.3 提示同一套做法。
 *
 * 只对"当前显示的就是图片"这一种情况给提示：首次打开图片时画布还不存在，正文走 §6.7 的读取占位。
 */
let imagePreviewHintTimer = null;

function isImageDocumentVisible() {
  const live = window.__augitLive;
  return !!(live && live.document && live.document.editor === "image");
}

/** 按 `live.imagePreview` 就地增删画布中心的提示节点（不重绘正文，尺寸与其它区域都不动）。 */
function syncImagePreviewState() {
  const stage = document.querySelector(".document-view .image-stage");
  if (!stage) return;
  const live = window.__augitLive;
  const state = live && live.imagePreview;
  const loading = !!(state && state.state === "loading");
  const hint = stage.querySelector(".image-loading");
  // 只认**自己加的**提示：`?image-state=loading` 是视觉稿/静态审计用的加载态（由 `image-preview.js` 建节点），
  // 不能被这里的同步逻辑删掉（第 243 轮全量跑抓到的回归）。
  const liveHint = stage.querySelector('.image-loading[data-live-hint="true"]');
  if (loading && !hint) {
    const node = document.createElement("div");
    node.className = "image-loading";
    node.dataset.liveHint = "true";
    node.setAttribute("role", "status");
    node.textContent = "正在读取文件…";
    stage.append(node);
  } else if (!loading && liveHint) {
    liveHint.remove();
  }
}

/** 读取在途超过 150ms 才给提示（规格 §7.5）；读取结束时由 `openDocument` 取消。 */
function scheduleImagePreviewHint(token) {
  clearTimeout(imagePreviewHintTimer);
  imagePreviewHintTimer = null;
  if (!isImageDocumentVisible()) return;
  imagePreviewHintTimer = setTimeout(() => {
    imagePreviewHintTimer = null;
    if (token !== documentToken) return;
    if (!isImageDocumentVisible()) return;
    const live = window.__augitLive;
    if (!live || !live.pendingDocument) return;
    live.imagePreview = { state: "loading", path: live.document ? live.document.path : null };
    syncImagePreviewState();
  }, LoadingFeedbackDelay);
}

/** 撤去图片加载提示并清状态（读取完成、失败或切换标签都走这里）。 */
function clearImagePreviewState() {
  clearTimeout(imagePreviewHintTimer);
  imagePreviewHintTimer = null;
  const live = window.__augitLive;
  if (live) live.imagePreview = null;
  syncImagePreviewState();
}

/**
 * 大文件只读预览的警告是否已隐藏。
 *
 * 权威的两个动作用两处状态：`HIDDEN_KEY` 记在**编辑器**上（本次打开内隐藏）、
 * `DISABLE_KEY` 记在 `PropertiesComponent`（永久关闭）——`LargeFileNotificationProvider.java:38-58`。
 * Augit 的对应物是 `live.hiddenLargeFileWarnings`（会话内、按路径）与设置里的 `hideLargeFileWarning`
 * （应用级、持久化）。
 */
function largeFileWarningHidden(document_) {
  const live = window.__augitLive;
  if (!live || !document_) return false;
  if (live.hideLargeFileWarning === true) return true;
  const dismissed = live.hiddenLargeFileWarnings;
  return !!(dismissed && document_.path && dismissed.has(document_.path));
}

window.__augitLargeFileWarningHidden = largeFileWarningHidden;

/** 打开一个真实文件：取回内容、更新活动文档并重绘编辑区。 */
async function openDocument(path, options = {}) {
  const { preview = false, activate = true } = options;
  const live = window.__augitLive;
  if (!live) return;
  // 切换普通文件使**在途的 Blame／文件历史详情**失效（规格 §7.9 第十条）：它们与文档区共用
  // `detailViewToken`，旧响应晚到时不得把视图抢回 Blame（第 262 轮实测：不前进令牌时，
  // 打开另一个文档后晚到的归属会把编辑区换回 Blame，而 `live.document` 仍是新文档）。
  detailViewToken += 1;
  live.tabs ??= [];
  // 已经是激活标签且不要求转为正式标签：无需重复读取。
  const current = live.tabs.find((tab) => tab.kind === "document" && tab.path === path);
  if (current && activate && live.activeTabId === current.id && !preview) return;
  // 规格 §6.7 第一条：读取完成时"是否显示必须**重新核对**…当前活动标签"。
  // 这里记下读取开始时用户正看着哪个标签；收尾时若当前活动标签变成了**另一个仍然存在的**标签，
  // 说明用户在读取期间自己切走了（切到标签栏里已有的标签不会发起新的读取，因此不会被递增令牌
  // 拦下），此时结果仍要放进它自己的标签，但**不得夺回显示**——否则用户最后选择的文件会被一次
  // 后台读取覆盖（第 272 轮实测：后台重读 `docs/notes.txt` 期间切到 `docs/product-spec.md`，
  // 读取落地后活动标签又变回 `docs/notes.txt`）。
  // 只在"当前活动标签非空且不同于开始时"才判定为切走：读取期间用户把原来的标签**关掉**时
  // `closeTab()` 会把活动标签让给邻居或置空，那不是"切到别处看"，读取结果理应显示出来
  //（否则 §6 第 38 条"关闭它前面的后台标签不使当前读取失效"会被破坏：标签建出来却没人显示）。
  const activeAtStart = live.activeTabId ?? null;
  const started = performance.now();
  // 快速连续打开时只接纳最后一次选择：晚到的旧响应不得覆盖新文档。
  const token = ++documentToken;
  // 规格 §4.1 第 121/123 行：读取尚未完成时状态栏要显示"本次打开的路径"并给"只读"标识。
  live.pendingDocument = path;
  applyPendingDocumentStatus();
  // 规格 §7.3：预览加载期间保留原文或上一次预览，**只在预览侧**显示局部加载状态；
  // 短读取（<150ms）不闪提示。
  scheduleMarkdownPreviewHint(token);
  scheduleImagePreviewHint(token);
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
    // `activate` 必须与"读取开始时的活动标签"对账：用户在读取期间切到**另一个仍然存在**的标签就
    // 不再显示这个结果；原来的标签被关掉（活动标签置空或让给邻居）不算切走，仍要显示。
    const activeNow = live.activeTabId ?? null;
    const switchedAway = activeNow !== null && activeNow !== activeAtStart;
    live.tabs ??= [];
    openDocumentTab(path, payload, {
      preview,
      activate: activate && !switchedAway,
    });
    window.__augitMarks = Object.assign(window.__augitMarks || {}, {
      open: Math.round(performance.now() - started),
    });
    live.pendingDocument = null;
    clearTimeout(markdownPreviewHintTimer);
    markdownPreviewHintTimer = null;
    live.markdownPreview = null;
    clearImagePreviewState();
    refresh("statusbar");
  } catch (error) {
    // 失败路径也必须清掉"读取中"状态，否则状态栏会**卡在**"只读 + 待打开路径"上（成功路径已清）。
    if (live.pendingDocument === path) {
      live.pendingDocument = null;
      refresh("statusbar");
    }
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

    clearTimeout(markdownPreviewHintTimer);
    markdownPreviewHintTimer = null;
    clearImagePreviewState();
    // 规格 §7.3：失败提示给出**当前原因**，且"原文或旧预览继续可见，用户可再次点击预览重试"。
    // 这一条只对**已经显示着 Markdown 文档**的正文成立（提示挂在预览区顶部）；其它情况保持
    // 原有行为：不留下半截文档，清空并记录原因，界面回退到「无文档」状态。
    if (isMarkdownDocumentVisible()) {
      live.markdownPreview = {
        state: "failure",
        // `path` 是**当时显示的那个文档**（提示与旧预览属于它）；`retryPath` 是失败的那次读取。
        path: live.document ? live.document.path : null,
        reason: String((error && error.message) || error || "文件读取失败。"),
        retryPath: path,
      };
      window.__augitError = "open-document:" + String((error && error.message) || error);
      syncMarkdownPreviewState();
      return;
    }
    window.__augitError = "open-document:" + String(error && error.message || error);
    live.document = null;
    live.markdownPreview = null;
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
