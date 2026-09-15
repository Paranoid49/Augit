// 外壳启动器：在 WebView2 内先取真实工作区数据，再交给 visual mockup 的构建器渲染。
// 没有宿主（浏览器里直接打开视觉稿）时不做任何事，视觉稿保持原样。
//
// 性能约束：桥接调用每次跨进程，必须尽量少而并行。
// 因此首屏只取工作区信息、Git 状态与根目录一层；更深层级在展开时再取。

import { hasHost, invoke } from "./bridge.js";

const MAX_ENTRIES_PER_DIRECTORY = 200;
// 项目树隐藏构建产物与本地工具目录：目录名命中列表，或名称以点开头（如 .git/.idea/.vs/.tmp）。
// 以点开头的文件（如 .gitignore、.editorconfig）保留，它们是有意义的项目文件。
const SKIPPED_DIRECTORIES = new Set(["bin", "obj", "node_modules", "TestResults", "publish", "artifacts", ".git", ".idea", ".vs", ".tmp"]);

function isVisibleEntry(entry) {
  if (SKIPPED_DIRECTORIES.has(entry.name)) return false;
  if (entry.isDirectory && entry.name.startsWith(".")) return false;
  return true;
}

/** 列举一个目录并转换成树行（只取一层，展开时再调用）。 */
async function loadChildren(parentPath, parentDepth) {
  const rows = [];
  let listing;
  try {
    listing = await invoke("workspace/list", { path: parentPath });
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
    const rootPromise = invoke("workspace/list", { path: "" }).catch(() => null);
    const info = await infoPromise;
    mark("info");
    const listing = await rootPromise;
    mark("root");

    const tree = [{
      name: info.name || "工作区",
      path: "",
      depth: 0,
      isDirectory: true,
      hasChildren: true,
      expanded: true,
    }];
    if (listing) {
      const entries = (listing.entries || []).filter(isVisibleEntry);
      const directories = entries.filter((entry) => entry.isDirectory);
      const files = entries.filter((entry) => !entry.isDirectory);
      for (const entry of [...directories, ...files].slice(0, MAX_ENTRIES_PER_DIRECTORY)) {
        tree.push({
          name: entry.name,
          path: entry.path,
          depth: 1,
          isDirectory: entry.isDirectory,
          hasChildren: entry.isDirectory,
          expanded: false,
        });
      }
    }

    window.__augitMarks = marks;
    return {
      root: info.root,
      name: info.name,
      valid: info.valid,
      error: info.error,
      branch: null,
      isDetached: false,
      changeCount: 0,
      tree,
    };
  } catch (error) {
    window.__augitError = "load-document:" + String(error && error.message || error);
    return null;
  }
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
    if (window.__augitLive) {
      window.__augitLive.branch = status.branch;
      window.__augitLive.isDetached = status.isDetached;
      window.__augitLive.changeCount = status.files ? status.files.length : 0;
    }
    return status;
  } catch (error) {
    window.__augitMarks = Object.assign(window.__augitMarks || {}, {
      statusError: Math.round(performance.now() - started),
    });
    return null;
  }
}

/** 分支名是标题栏里的独立标签，单独更新即可，避免整页重绘。 */
function applyBranch(branch) {
  if (!branch) return;
  const chip = document.querySelector(".branch-chip");
  if (!chip) return;
  for (const node of chip.childNodes) {
    if (node.nodeType === Node.TEXT_NODE && node.textContent.trim()) {
      node.textContent = ` ${branch} `;
      return;
    }
  }
}

async function boot() {
  let statusPromiseRef = Promise.resolve(null);
  if (hasHost()) {
    // 桥接异常不能阻塞界面：超时后回退视觉稿样例数据。
    statusPromiseRef = loadStatus();
    window.__augitLive = await Promise.race([
      loadDocument(),
      new Promise((resolve) => setTimeout(() => resolve(null), 8000)),
    ]);

    if (window.__augitLive === null) {
      window.__augitError = (window.__augitError || "") + "|timeout";
    }
  }

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

  const live = window.__augitLive;
  if (live && live.tree && live.tree.length > 1) {
    bindLiveTree(live);
  }

  // 界面已就绪，再补 Git 事实；分支变化只更新标题栏标签。
  const status = await statusPromiseRef;
  if (status) {
    applyBranch(status.branch);
  }
  window.__augitReady = true;
}

/** 展开/折叠真实目录；每次展开只取一层。 */
function bindLiveTree(live) {
  const container = document.querySelector(".side-content.tree");
  if (!container) return;
  const icon = window.__augitIcon;
  container.addEventListener("click", async (event) => {
    const row = event.target.closest(".tree-row");
    if (!row || row.dataset.treeDirectory !== "true" || row.dataset.treePath === undefined) return;
    event.preventDefault();
    if (row.dataset.expanded === "true") {
      collapseRow(row);
      row.dataset.expanded = "false";
      const chevron = row.querySelector(".chevron");
      if (chevron && icon) chevron.innerHTML = icon("chevron-right");
      return;
    }

    row.dataset.expanded = "true";
    const chevron = row.querySelector(".chevron");
    if (chevron && icon) chevron.innerHTML = icon("chevron-down");
    const depth = Number(row.getAttribute("aria-level") || 1);
    const children = await loadChildren(row.dataset.treePath, depth);
    row.insertAdjacentHTML("afterend", children.map(renderLiveRow).join(""));
  });
}

function collapseRow(row) {
  const depth = Number(row.getAttribute("aria-level") || 1);
  let next = row.nextElementSibling;
  while (next && Number(next.getAttribute("aria-level") || 0) > depth) {
    const current = next;
    next = next.nextElementSibling;
    current.remove();
  }
}

/** 与 mockup.js 的 liveProjectTree 保持同一 DOM 结构，复用同一套样式与绑定。 */
function renderLiveRow(entry) {
  const icon = window.__augitIcon || (() => "");
  const depthClass = entry.depth === 0 ? "root-row" : `depth-${entry.depth}`;
  const chevron = entry.isDirectory && entry.hasChildren ? icon("chevron-right") : "";
  const iconHtml = entry.isDirectory
    ? (window.__augitFolderIcon ? window.__augitFolderIcon(entry.depth === 0) : "")
    : (window.__augitFileIcon ? window.__augitFileIcon(entry.name) : "");
  return `<div class="tree-row ${depthClass}" data-tree-path="${escapeText(entry.path)}" data-tree-directory="${entry.isDirectory}" data-expanded="false" role="treeitem" aria-level="${entry.depth + 1}" aria-expanded="${entry.isDirectory}" tabindex="-1"><span class="chevron">${chevron}</span><span class="${entry.isDirectory ? "folder-icon" : "file-icon"}">${iconHtml}</span><span class="tree-name">${escapeText(entry.name)}</span></div>`;
}

function escapeText(value) {
  return String(value).replace(/[&<>"']/g, (character) => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
  }[character]));
}

await boot();
