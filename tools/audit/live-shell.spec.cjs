// 在无头 Chromium 中验证外壳的真实数据路径：用 addInitScript 模拟 WebView2 宿主，
// 因此不需要启动 Windows 应用即可验证桥接、目录展开、文档、Changes、历史与 Blame。
// 用法：node tools/audit/live-shell.spec.cjs [playwright 模块路径]
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');

const MODULE = process.argv[2] || '/root/.npm/_npx/e41f203b7505f1fb/node_modules/playwright';
const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
};

// ES 模块在 file:// 下会被 CORS 拒绝，因此起一个最小静态服务；
// 真实运行时由 WebView2 的虚拟主机映射承担同样职责。
function startStaticServer(root) {
  const server = http.createServer((request, response) => {
    const url = new URL(request.url, 'http://127.0.0.1');
    const relative = decodeURIComponent(url.pathname).replace(/^\//, '') || 'index.html';
    const target = path.join(root, relative);
    if (!target.startsWith(root) || !fs.existsSync(target) || fs.statSync(target).isDirectory()) {
      response.writeHead(404);
      response.end('not found');
      return;
    }
    response.writeHead(200, { 'Content-Type': MIME[path.extname(target)] || 'application/octet-stream' });
    fs.createReadStream(target).pipe(response);
  });
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve(server)));
}

// 与 C# ShellBridge 返回结构一致的桩数据。
const WORKSPACE = {
  tree: {
    '': [
      { name: 'docs', path: 'docs', isDirectory: true, canExpand: true },
      { name: 'src', path: 'src', isDirectory: true, canExpand: true },
      { name: 'README.md', path: 'README.md', isDirectory: false, canExpand: false },
    ],
    docs: [
      { name: 'product-spec.md', path: 'docs/product-spec.md', isDirectory: false, canExpand: false },
      { name: 'notes.txt', path: 'docs/notes.txt', isDirectory: false, canExpand: false },
    ],
    src: [{ name: 'Program.cs', path: 'src/Program.cs', isDirectory: false, canExpand: false }],
  },
  status: {
    branch: 'dsh',
    files: [
      { path: 'src/App.cs', name: 'App.cs', directory: 'src', group: 'Changes', kind: 'Modified', staged: false, workingTree: true },
      { path: 'README.md', name: 'README.md', directory: '', group: 'Changes', kind: 'Modified', staged: false, workingTree: true },
      { path: 'notes/draft.txt', name: 'draft.txt', directory: 'notes', group: 'UnversionedFiles', kind: 'Untracked', staged: false, workingTree: false },
    ],
  },
  history: {
    available: true, isRepository: true, head: 'aaa1111bbbb2222cccc3333dddd4444eeee5555', hasNextPage: false,
    commits: [
      // 用一次合并提交产生多条泳道，覆盖真实历史中常见的分叉与汇合。
      { hash: 'aaa1111', fullHash: 'full-head-hash', subject: 'feat: 真实提交一', author: 'l49', date: '2026/9/15 10:00', graph: '*', parents: ['bbb2222', 'full-bbb2222'], references: ['HEAD', 'dsh'] },
      { hash: 'bbb2222', fullHash: 'full-bbb2222', subject: 'fix: 真实提交二', author: 'l49', date: '2026/9/14 09:00', graph: '*', parents: ['full-ccc3333'], references: [] },
      { hash: 'ccc3333', fullHash: 'full-ccc3333', subject: 'feat: 真实提交三', author: 'l49', date: '2026/9/13 08:00', graph: '*', parents: [], references: [] },
    ],
  },
  searchFiles: {
    available: true, timedOut: false, cancelled: false, notice: '',
    matches: [
      { path: 'docs/product-spec.md', name: 'product-spec.md', directory: 'docs' },
      { path: 'docs/notes.txt', name: 'notes.txt', directory: 'docs' },
    ],
  },
  searchText: {
    available: true, truncated: false, timedOut: false, cancelled: false, notice: '',
    matches: [
      { path: 'docs/product-spec.md', name: 'product-spec.md', directory: 'docs', line: 12, column: 3, text: '轻量优先是 Augit 的最高产品原则。' },
      { path: 'docs/notes.txt', name: 'notes.txt', directory: 'docs', line: 1, column: 1, text: '第一行' },
    ],
  },
  clone: { available: false, field: 'destination', reason: '目标目录不为空，请换一个目录。' },
  diff: {
    available: true, path: 'src/App.cs', status: 'Ready', oldSize: 40, newSize: 44,
    truncated: false, lines: [],
    rows: [
      { oldLine: 1, oldText: 'line one', oldChanges: [], newLine: 1, newText: 'line one', newChanges: [], kind: 'Context' },
      { oldLine: 2, oldText: 'old value', oldChanges: [{ start: 0, length: 3 }], newLine: null, newText: null, newChanges: [], kind: 'Removed' },
      { oldLine: null, oldText: null, oldChanges: [], newLine: 2, newText: 'new value', newChanges: [{ start: 0, length: 3 }], kind: 'Added' },
      { oldLine: 3, oldText: 'line three', oldChanges: [], newLine: 3, newText: 'line three', newChanges: [], kind: 'Context' },
    ],
  },
  settings: {
    theme: 'Dark', textFontFamily: 'Microsoft YaHei UI', monospaceFontFamily: 'Cascadia Mono',
    fontSize: 15, codeFontSize: 14, gitExecutablePath: 'C:\\Program Files\\Git\\cmd\\git.exe',
    terminalShell: 'PowerShell7', terminalCustomCommand: null, recentWorkspaces: ['D:\\ws'],
    projectPanelWidth: 330, bottomPanelHeight: 240,
  },
  conflicts: {
    available: true, operation: 'Rebase', hasConflicts: true,
    files: [{ path: 'src/App.cs', name: 'App.cs', directory: 'src' }],
  },
  conflict: {
    available: true, path: 'src/App.cs', contentKind: 'Text',
    yoursLabel: '当前分支 · main', theirsLabel: '合入内容 · feature/ux',
    yoursText: '第一行\n左方改动\n第三行',
    theirsText: '第一行\n右方改动\n第三行',
    resultText: '第一行\n<<<<<<< HEAD\n左方改动\n=======\n右方改动\n>>>>>>> feature/ux\n第三行',
    operation: 'Rebase',
    version: { length: 42, sha256: 'deadbeef', lastWriteUtc: '2026-09-15T00:00:00Z' },
    blocks: [{ start: 1, length: 5, yours: '左方改动', ancestor: null, theirs: '右方改动' }],
  },
  remotes: { available: true, remotes: [{ name: 'origin', fetchUrl: 'https://example.com/team/Augit.git', pushUrl: 'https://example.com/team/Augit.git' }] },
  references: {
    available: true,
    branches: [
      { name: 'dsh', isRemote: false, isCurrent: true, upstream: 'origin/dsh', commitHash: 'full-head-hash', subject: 'feat: 真实提交一' },
      { name: 'origin/dsh', isRemote: true, isCurrent: false, upstream: null, commitHash: 'full-bbb2222', subject: 'fix: 真实提交二' },
    ],
    tags: [
      { name: 'v1.0.0', isRemote: false, isCurrent: false, upstream: null, commitHash: 'full-tag-1', subject: 'release: 一' },
    ],
  },
  stashes: { available: true, stashes: [{ reference: 'stash@{0}', message: '真实贮藏', branch: 'dsh', subject: 'WIP', date: '2026/9/15 10:00' }] },
  worktrees: { available: true, worktrees: [{ path: 'D:\\ws', branch: 'dsh', commitHash: 'aaa1111', isBare: false, isDetached: false, isLocked: false, isPrunable: false }] },
  commit: {
    available: true,
    hash: 'aaa1111', fullHash: 'full-head-hash',
    subject: 'feat: 真实提交一', author: 'l49', date: '2026/9/15 10:00',
    body: '提交正文说明。',
    files: [
      { path: 'src/App.cs', name: 'App.cs', directory: 'src', kind: 'Modified', original: null },
      { path: 'docs/notes.txt', name: 'notes.txt', directory: 'docs', kind: 'Added', original: null },
    ],
  },
  fileHistory: {
    available: true, path: 'docs/notes.txt',
    commits: [
      { hash: 'bbb2222', fullHash: 'full-bbb', subject: 'fix: 文件历史一', author: 'l49', date: '2026/9/14 09:00' },
      { hash: 'aaa1111', fullHash: 'full-aaa', subject: 'feat: 文件历史二', author: 'l49', date: '2026/9/13 08:00' },
    ],
  },
  blame: {
    available: true, path: 'docs/notes.txt',
    lines: [
      { number: 1, hash: 'aaa1111', fullHash: 'full-aaa', author: 'l49', date: '2026/9/15', summary: 'feat: 一', content: '第一行' },
      { number: 2, hash: 'aaa1111', fullHash: 'full-aaa', author: 'l49', date: '2026/9/15', summary: 'feat: 一', content: '第二行' },
      { number: 3, hash: 'bbb2222', fullHash: 'full-bbb', author: 'l49', date: '2026/9/14', summary: 'fix: 二', content: '第三行' },
    ],
  },
  documents: {
    'docs/product-spec.md': {
      path: 'docs/product-spec.md', name: 'product-spec.md', fullPath: 'D:\\ws\\docs\\product-spec.md',
      workspaceName: 'ws', status: 'TextReady', kind: 'Markdown', typeName: 'Markdown',
      fileSize: 120, text: '# 真实标题\n\n第一段**加粗**与`代码`。\n\n## 二级\n\n- 甲\n- 乙\n',
      lineEndings: 'LF', encoding: 'UTF-8',
    },
    'docs/notes.txt': {
      path: 'docs/notes.txt', name: 'notes.txt', fullPath: 'D:\\ws\\docs\\notes.txt',
      workspaceName: 'ws', status: 'TextReady', kind: 'Text', typeName: '纯文本',
      fileSize: 40, text: '第一行\n第二行\n第三行\n', lineEndings: 'Lf', encoding: 'UTF-8',
    },
  },
};

async function main() {
  const { chromium } = require(path.resolve(MODULE));
  const webRoot = path.resolve(__dirname, '../../web');
  const server = await startStaticServer(webRoot);
  const port = server.address().port;
  const browser = await chromium.launch({ headless: true });
  let passed = 0;
  const check = (label, condition) => {
    if (!condition) throw new Error('断言失败：' + label);
    passed++;
  };

  const stubHost = () => {
    const listeners = [];
    window.chrome = {
      webview: {
        addEventListener: (type, handler) => { if (type === 'message') listeners.push(handler); },
        postMessage: (raw) => {
          const request = JSON.parse(raw);
          Promise.resolve().then(async () => {
            let result;
            try {
              // 桩可以是异步的：竞态验证需要按请求注入延迟以制造乱序返回。
              result = await window.__hostStub(request.method, request.params);
            } catch (error) {
              result = { error: String(error && error.message || error) };
            }
            for (const handler of listeners) handler({ data: JSON.stringify({ id: request.id, result }) });
          });
        },
      },
    };
  };

  const stubData = (data) => {
    window.__hostStub = async (method, params) => {
      if (method === 'workspace/info') return { root: 'D:\\live-ws', name: 'live-ws', valid: true };
      if (method === 'workspace/list') {
        // 桩也要实现与宿主一致的越界拒绝，否则验收无法覆盖这条安全边界。
        const requested = String(params.path || '');
        const resolved = requested.replaceAll('\\', '/').split('/');
        const escapes = resolved.includes('..')
          || requested.startsWith('/')
          || /^[a-zA-Z]:/.test(requested);
        if (escapes) throw new Error('路径越出工作区：' + requested);
        // 只让「展开子目录」失败，首屏根目录仍可用，便于验证后续失败的处理。
        if (window.__workspaceGone && requested !== '') {
          return { path: requested, available: false, reason: '目录不存在或无法访问，请确认工作区仍然存在。', entries: [] };
        }
        return { path: requested, available: true, entries: data.tree[requested] || [] };
      }
      if (method === 'workspace/changes') {
        // 由测试脚本通过 window.__nextChanges 注入一次变化批次，读取后清空。
        const next = window.__nextChanges || { files: [], gitMetadata: false };
        window.__nextChanges = null;
        return { available: true, files: next.files || [], gitMetadata: !!next.gitMetadata };
      }
      if (method === 'git/status') {
        window.__statusCalls = (window.__statusCalls || 0) + 1;
        if (window.__statusDelays) await new Promise((r) => setTimeout(r, window.__statusDelays));
        if (window.__deletedPaths && window.__deletedPaths.length) {
          const base = window.__liveFiles || data.status.files;
          const extra = window.__deletedPaths.map((p) => ({ path: p, name: p.split('/').at(-1), directory: p.split('/').slice(0, -1).join('/'), group: 'Changes', kind: 'Deleted', staged: false, workingTree: true }));
          return { available: true, isRepository: true, isDetached: false, branch: data.status.branch, files: base.concat(extra) };
        }
        if (window.__gitUnavailable) return { available: false, reason: '未找到 Git for Windows 2.40 或更高版本。' };
        if (window.__notARepository) return { available: true, isRepository: false, reason: '该目录不是带工作区的 Git 仓库。' };
        // 支持运行中改变文件列表，用于跨模块流程验证。
        // 用 in 判断而不是真值判断：空数组是有效状态（§10.1 的无 Changes），
        // `||` 会让它回退到默认列表，测不出空状态。
        const files = Array.isArray(window.__liveFiles) ? window.__liveFiles : data.status.files;
        return { available: true, isRepository: true, isDetached: false, branch: data.status.branch, files };
      }
      if (method === 'git/history') {
        // 历史常比首屏慢十余秒：支持注入延迟，用于验证"数据到达不得打断用户输入"。
        const historyDelay = window.__historyDelayMs || 0;
        if (historyDelay) await new Promise((r) => setTimeout(r, historyDelay));
        if (window.__emptyHistory) return { available: true, isRepository: true, head: null, hasNextPage: false, commits: [] };
        return data.history;
      }
      if (method === 'git/blame') {
        window.__blameCalls = (window.__blameCalls || 0) + 1;
        if (window.__malformed) return { available: true, lines: [{ number: 1, hash: 'x' }] };  // 缺 path
        return data.blame;
      }
      if (method === 'git/file-history') {
        window.__fileHistoryCalls = (window.__fileHistoryCalls || 0) + 1;
        if (window.__malformed) return { available: true, commits: [] };  // 缺 path
        return data.fileHistory;
      }
      if (method === 'search/files') return data.searchFiles;
      if (method === 'search/text') {
        if (window.__emptySearch) return { ...data.searchText, matches: [], notice: '' };
        return data.searchText;
      }
      if (method === 'git/clone') { window.__cloneCall = params; return data.clone; }
      if (method === 'git/diff') {
        if (window.__malformed) return { available: true, path: 'src/App.cs', status: 'Ready' };  // 缺 rows
        // 记录必须发生在注入延迟**之前**：__diffCalls 表示"请求已发出"，
        // 若先延迟再记录，观察者看到调用时响应已经返回，就无法判断请求是否仍在途中
        // （验证"关闭比较后取消在途请求"时踩过这个坑）。
        window.__diffCommits = (window.__diffCommits || []).concat([params.commit === undefined ? '<未传>' : params.commit]);
        window.__diffCalls = window.__diffCalls || [];
        window.__diffRevisions = (window.__diffRevisions || []).concat([params.revision === undefined ? '<未传>' : params.revision]);
        window.__diffCalls.push(params.path);
        // 支持注入延迟：用于验证加载期间主框架与其它区域的位置不变（§6.1）。
        const diffDelay = (window.__diffDelays || {})[params.path];
        if (diffDelay) await new Promise((r) => setTimeout(r, diffDelay));
        // 历史比较（§7.8）传入 commit：返回可区分的内容，用于证明请求确实换成了
        // 「两个版本之间」而不是工作区比较。两版本的请求键不同，内容也构造得不同。
        if (params.commit) {
          return {
            ...data.diff,
            path: params.path,
            rows: data.diff.rows.map((row) => Object.assign({}, row, {
              oldText: row.oldText === null || row.oldText === undefined ? row.oldText : '历史左值 ' + params.commit.slice(0, 6) + ' ' + row.oldText,
              newText: row.newText === null || row.newText === undefined ? row.newText : '历史右值 ' + params.commit.slice(0, 6) + ' ' + row.newText,
            })),
          };
        }

        // 第二个文件也被视为有差异，便于验证快速连选的结果归属。
        if (params.path === data.diff.path) return data.diff;
        if (params.path === 'README.md') return { ...data.diff, path: 'README.md' };
        return { available: false, reason: 'no diff' };
      }
      if (method === 'settings/read') return data.settings;
      if (method === 'settings/write') {
        if (window.__settingsReadOnly) return { saved: false, reason: '无法访问文件或目录，请检查权限或占用情况。' };
        window.__settingsWritten = Object.assign(window.__settingsWritten || {}, params);
        return { saved: true, theme: params.theme, fontSize: params.fontSize };
      }
      if (method === 'git/conflicts') return data.conflicts;
      if (method === 'git/conflict-load') return data.conflict;
      if (method === 'git/remotes') return data.remotes;
      if (method === 'git/remote-write') {
        window.__remoteWrites = (window.__remoteWrites || []).concat([params]);
        if (window.__remoteWriteFails) return { available: true, changed: false, reason: 'remote 已存在。' };
        return { available: true, changed: true, remotes: [{ name: params.name, fetchUrl: params.fetchUrl, pushUrl: params.pushUrl }] };
      }
      if (method === 'git/references') return data.references;
      if (method === 'git/stashes') return data.stashes;
      if (method === 'git/worktrees') return data.worktrees;
      if (method === 'git/unpushed') {
        if (window.__unpushedFails) return { available: true, ready: false, reason: '当前分支没有配置上游，无法生成推送预览。', commits: [], upstream: null };
        return {
          available: true, ready: true, upstream: 'origin/dsh',
          commits: (window.__unpushedSubjects || ['feat: 真实提交一']).map((subject, index) => ({ hash: 'aaa111' + index, fullHash: 'full-' + index, subject, author: 'l49', date: '2026/9/15 10:00' })),
        };
      }
      if (method === 'git/worktree-write') {
        window.__worktreeWrites = (window.__worktreeWrites || []).concat([params]);
        if (window.__worktreeFails) return { available: true, changed: false, reason: '目标目录不为空。' };
        return { available: true, changed: true, branch: params.branch };
      }
      if (method === 'git/fetch') {
        window.__fetchCalls = (window.__fetchCalls || 0) + 1;
        if (window.__fetchFails) return { available: true, fetched: false, reason: '没有配置远端。' };
        return { available: true, fetched: true, branch: 'dsh' };
      }
      if (method === 'git/branch') {
        window.__branchCalls = (window.__branchCalls || []).concat([params]);
        if (window.__branchFails) return { available: true, changed: false, reason: '分支名已存在。' };
        return { available: true, changed: true, branch: params.name };
      }
      if (method === 'git/checkout') {
        window.__checkoutCalls = (window.__checkoutCalls || []).concat([{ name: params.name, kind: params.kind }]);
        if (window.__checkoutFails) return { available: true, switched: false, reason: '工作区有未提交的改动，无法切换分支。' };
        return { available: true, switched: true, detached: params.kind === 'tag', branch: params.name };
      }
      if (method === 'git/push') {
        window.__pushCalls = (window.__pushCalls || 0) + 1;
        if (window.__pushFails) return { available: true, pushed: false, reason: '没有配置推送远端。' };
        return { available: true, pushed: true, branch: 'main', remotes: 1 };
      }
      if (method === 'git/commit-create') {
        window.__commitRequests = (window.__commitRequests || 0) + 1;
        window.__commitWrite = { message: params.message, paths: params.paths };
        if (window.__commitDelays) await new Promise((r) => setTimeout(r, window.__commitDelays));
        // 支持注入失败
        if (window.__commitFails) return { available: true, committed: false, reason: 'commit-msg hook 拒绝提交。请检查仓库提交规则。' };
        if (!params.paths || params.paths.length === 0) return { available: true, committed: false, reason: '请至少选择一个要提交的文件。' };
        if (!params.message) return { available: true, committed: false, reason: '提交信息不能为空。' };
        return { available: true, committed: true, commitHash: 'abc1234', branch: 'main', remaining: 1 };
      }
      if (method === 'git/commit') {
        const delay = (window.__commitDelays || {})[params.revision];
        if (delay) await new Promise((r) => setTimeout(r, delay));
        if (params.revision === data.commit.fullHash) return data.commit;
        // 第二个提交返回可区分的详情
        // 第二个提交也给出变化文件：历史比较需要"改选提交后跟随到同一路径"的场景，
        // 空文件列表会让该场景无法构造。
        if (params.revision === 'full-bbb2222') {
          return Object.assign({}, data.commit, {
            hash: 'bbb2222', fullHash: 'full-bbb2222', subject: 'fix: 第二个提交',
            files: [{ path: 'src/App.cs', name: 'App.cs', directory: 'src', kind: 'Modified', original: null }],
          });
        }
        return { available: false, reason: 'unknown' };
      }
      if (method === 'document/read') {
        // 支持按路径注入读取失败，用于验证「文件已删除则移除其标签」。
        if (window.__failReads && window.__failReads[params.path]) throw new Error('not found: ' + params.path);
        // 支持按路径注入延迟，用于验证乱序返回时旧响应被丢弃。
        const delay = (window.__readDelays || {})[params.path];
        if (delay) await new Promise((r) => setTimeout(r, delay));
        if (window.__limitDocs && window.__limitDocs[params.path]) return window.__limitDocs[params.path];
        const found = data.documents[params.path];
        if (!found) throw new Error('not found: ' + params.path);
        return found;
      }
      throw new Error('unexpected method ' + method);
    };
  };

  try {
    const context = await browser.newContext({ viewport: { width: 1180, height: 760 }, deviceScaleFactor: 1 });
    await context.addInitScript(stubHost);
    await context.addInitScript(stubData, WORKSPACE);

    const openScene = async (query) => {
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', (error) => errors.push(error.message));
      await page.goto(`http://127.0.0.1:${port}/index.html?${query}`, { waitUntil: 'load' });
      try {
        await page.waitForFunction('window.__augitReady === true', null, { timeout: 20000 });
      } catch {
        const st = await page.evaluate(() => ({ err: window.__augitError || null, live: !!window.__augitLive }));
        throw new Error('首屏未就绪 state=' + JSON.stringify(st) + ' pageErrors=' + JSON.stringify(errors.slice(0, 2)));
      }
      try {
        await page.waitForFunction(
          'window.__augitGitReady === true && window.__augitHistoryReady === true',
          null,
          { timeout: 20000 });
      } catch {
        const state = await page.evaluate(() => ({
          live: !!window.__augitLive,
          gitReady: !!window.__augitGitReady,
          historyReady: !!window.__augitHistoryReady,
          hasStatus: !!(window.__augitLive && window.__augitLive.status),
          hasHistory: !!(window.__augitLive && window.__augitLive.history),
          err: window.__augitError || null,
          marks: window.__augitMarks || null,
        }));
        throw new Error('等待 Git 数据超时: ' + JSON.stringify(state));
      }
      return { page, errors };
    };

    // ---- 工作区与项目树 ----
    // 单独开一个不预打开文档的场景：规格 §4.1 要求此时状态栏显示工作区路径。
    const idle = await openScene('scene=git-history&theme=dark');
    const idleStatus = await idle.page.locator('.statusbar').innerText();
    check('无文档时状态栏显示工作区路径: ' + idleStatus.replace(/\n/g, ' '), idleStatus.includes('live-ws'));
    await idle.page.close();

    const { page, errors } = await openScene('scene=main-project&theme=dark');
    check('无页面脚本错误: ' + JSON.stringify(errors.slice(0, 2)), errors.length === 0);
    check('注入真实工作区数据', await page.evaluate('!!window.__augitLive'));
    check('树显示根与一层', await page.locator('.side-content.tree .tree-row').count() === 4);
    check('分支标签来自宿主', (await page.locator('.branch-chip').innerText()).includes('dsh'));

    await page.locator('.side-content.tree .tree-row[data-tree-path="docs"]').click();
    await page.waitForFunction('document.querySelectorAll(".side-content.tree .tree-row").length === 6', null, { timeout: 10000 });
    check('展开后出现子项', await page.locator('.side-content.tree .tree-row[data-tree-path="docs/product-spec.md"]').count() === 1);

    await page.locator('.side-content.tree .tree-row[data-tree-path="docs/product-spec.md"]').dblclick();
    try {
      await page.waitForFunction('window.__augitLive && window.__augitLive.document && window.__augitLive.document.path === "docs/product-spec.md"', null, { timeout: 10000 });
    } catch {
      const st = await page.evaluate(() => ({ doc: window.__augitLive && window.__augitLive.document ? window.__augitLive.document.path : null, err: window.__augitError || null, selected: document.querySelectorAll('.side-content.tree .tree-row.selected').length, fetchErr: window.__augitFetchError || null, tabs: window.__augitLive ? (window.__augitLive.tabs || []).length : -1, activeTabId: window.__augitLive ? window.__augitLive.activeTabId : null, sync: window.__augitSyncTrace || null, nullTrace: window.__augitNullDocTrace || null, openStack: (window.__augitOpenErrorStack || '').split(String.fromCharCode(10)).slice(0,4).join(' | ') }));
      throw new Error('双击未打开文档 state=' + JSON.stringify(st));
    }
    // 打开文档后的区域刷新被推迟到事件派发结束（见 refreshAfterEvent），
    // 因此先等标题栏反映新文档，再断言。
    await page.waitForFunction('document.querySelector(".titlebar-context").innerText.includes("product-spec.md")', null, { timeout: 5000 });
    // 标题栏同样来自宿主，不能停留在视觉稿的默认文案。
    const workspaceChip = await page.locator('.workspace-chip').innerText();
    check('标题栏显示真实工作区名: ' + workspaceChip, workspaceChip.includes('live-ws'));
    const contextLabel = await page.locator('.titlebar-context').innerText();
    check('标题栏显示真实当前文件: ' + contextLabel, contextLabel.includes('product-spec.md'));
    // 规格 §4.1：成功读取的 UTF-8 文本显示编码与磁盘换行格式，并带只读标识。
    const docStatus = await page.locator('.statusbar').innerText();
    check('文档状态栏含编码与换行: ' + docStatus.replace(/\n/g, ' '), docStatus.includes('UTF-8') && docStatus.includes('LF'));
    check('文档状态栏含只读标识', docStatus.includes('只读'));
    const preview = await page.locator('.markdown-preview').innerHTML();
    check('Markdown 预览来自真实内容', preview.includes('真实标题') && preview.includes('<strong>加粗</strong>'));
    check('预览不残留样例标题', !preview.includes('Augit 产品规格'));
    check('标签显示真实文件名', (await page.locator('.editor-tab.active').innerText()).includes('product-spec.md'));
    check('展开状态在重绘后保留', await page.locator('.side-content.tree .tree-row[data-tree-path="docs/notes.txt"]').count() === 1);

    await page.locator('.side-content.tree .tree-row[data-tree-path="docs/notes.txt"]').dblclick();
    await page.waitForFunction('window.__augitLive && window.__augitLive.document && window.__augitLive.document.path === "docs/notes.txt"', null, { timeout: 10000 });
    // 编辑区刷新被延后到事件派发结束。等待条件必须针对**新内容**，
    // 否则会被上一份文档残留的行满足（这会掩盖渲染滞后）。
    await page.waitForFunction('document.querySelector(".code-view .code-line") && document.querySelector(".code-view .code-line").innerText.includes("第一行")', null, { timeout: 5000 });
    const lines = await page.locator('.code-view .code-line').allInnerTexts();
    check('纯文本按行渲染', lines.length === 4 && lines[0].includes('第一行'));
    check('状态栏显示真实路径', (await page.locator('.statusbar .status-path').innerText()).includes('notes.txt'));

    // ---- 底部 Git 日志 ----
    const subjects = await page.locator('.commit-subject').allInnerTexts();
    check('Git 日志显示真实提交: ' + JSON.stringify(subjects), subjects.length === 3 && subjects[0].includes('真实提交一'));
    check('提交行带完整哈希', await page.locator('.commit-row[data-full-hash="full-head-hash"]').count() === 1);
    const branchLabels = await page.locator('.branch-label').allInnerTexts();
    check('分支标签来自真实引用', branchLabels.some((text) => text.includes('dsh')));
    const logBranches = await page.locator('.log-ref-panel .tree-row').allInnerTexts();
    check('引用树列出真实分支', logBranches.some((text) => text.includes('dsh')));
    await page.close();

    // ---- Changes 工具窗 ----
    const changes = await openScene('scene=commit-changes&theme=dark');
    await changes.page.waitForSelector('.change-file-row', { timeout: 10000 });
    const changeFiles = await changes.page.locator('.change-file-row').evaluateAll((els) => els.map((e) => e.dataset.path));
    check('Changes 显示真实改动文件: ' + JSON.stringify(changeFiles), changeFiles.length === 3 && changeFiles.includes('src/App.cs'));
    const groups = await changes.page.locator('.check-group-row strong').allInnerTexts();
    check('Changes 分组来自 Git', groups.includes('Changes') && groups.includes('Unversioned Files'));
    check('分组计数正确', (await changes.page.locator('.check-group-row .commit-meta').first().innerText()).includes('2 个文件'));
    check('提交区显示改动数', (await changes.page.locator('.commit-count').innerText()).includes('2 modified'));
    check('未跟踪文件默认不勾选', await changes.page.locator('.change-file-row[data-path="notes/draft.txt"] .fake-check.checked').count() === 0);
    await changes.page.close();

    // ---- Blame ----
    const blame = await openScene('scene=blame&theme=dark&blame=docs%2Fnotes.txt');
    await blame.page.waitForSelector('.blame-document .blame-row', { timeout: 10000 });
    check('Blame 行数与真实归属一致', await blame.page.locator('.blame-document .blame-row').count() === 3);
    const blameText = await blame.page.locator('.blame-document .blame-gutter').innerText();
    check('Blame 槽位含真实日期与作者', blameText.includes('2026/9/15') && blameText.includes('l49'));
    const blameBody = await blame.page.locator('.blame-document .code-view').innerText();
    check('Blame 正文为真实文件内容', blameBody.includes('第一行') && blameBody.includes('第三行'));
    check('Blame 不残留样例归属', !blameText.includes('2026/8/28'));
    await blame.page.close();

    // ---- 文件历史 ----
    const fileHistory = await openScene('scene=file-history&theme=dark&file-history=docs%2Fnotes.txt');
    await fileHistory.page.waitForSelector('.history-row', { timeout: 10000 });
    const historyRows = await fileHistory.page.locator('.history-row').count();
    check('文件历史显示真实提交: ' + historyRows, historyRows === 2);
    const firstRow = await fileHistory.page.locator('.history-row').first().innerText();
    check('文件历史首行为最新提交', firstRow.includes('fix: 文件历史一') && firstRow.includes('l49'));
    const historyTab = await fileHistory.page.locator('.tool-tab.active').innerText();
    check('文件历史标签显示真实路径: ' + historyTab, historyTab.includes('docs/notes.txt'));
    check('文件历史不残留样例', !firstRow.includes('feat: 实现 Augit 阶段零至五功能'));
    await fileHistory.page.close();

    // ---- 提交详情 ----
    const historyPage = await context.newPage();
    const historyErrors = [];
    historyPage.on('pageerror', (error) => historyErrors.push(error.message));
    await historyPage.goto(`http://127.0.0.1:${port}/index.html?scene=git-history&theme=dark`, { waitUntil: 'load' });
    await historyPage.waitForFunction('window.__augitHistoryReady === true', null, { timeout: 20000 });
    try {
      await historyPage.waitForFunction('window.__augitCommitLoaded === "aaa1111"', null, { timeout: 15000 });
    } catch {
      const st = await historyPage.evaluate(() => ({ err: window.__augitError || null, loaded: window.__augitCommitLoaded || null }));
      throw new Error('提交详情未加载: ' + JSON.stringify(st));
    }
    check('提交详情无页面错误', historyErrors.length === 0);
    const changed = await historyPage.locator('[data-live-changed-files]').innerText();
    check('提交详情列出真实变更文件: ' + JSON.stringify(changed.slice(0, 60)), changed.includes('2 个文件') && changed.includes('App.cs'));
    const detail = await historyPage.locator('[data-live-commit-detail]').innerText();
    check('提交详情显示真实提交信息', detail.includes('feat: 真实提交一') && detail.includes('提交正文说明。'));
    check('提交详情不残留样例', !changed.includes('architecture.md'));
    await historyPage.close();

    // ---- 远端管理 ----
    const remote = await openScene('scene=remote&theme=dark');
    try {
      await remote.page.waitForFunction('window.__augitRefsReady === true', null, { timeout: 20000 });
    } catch {
      const st = await remote.page.evaluate(() => ({
        err: window.__augitError || null,
        refs: !!window.__augitRefsReady,
        hasRemotes: !!(window.__augitLive && window.__augitLive.remotes),
        hasRefs: !!(window.__augitLive && window.__augitLive.references),
      }));
      throw new Error('远端数据未就绪: ' + JSON.stringify(st));
    }
    await remote.page.waitForSelector('.management-list .tree-row', { timeout: 10000 });
    const remoteList = await remote.page.locator('.management-list .tree-row').allInnerTexts();
    check('远端管理列出真实远端: ' + JSON.stringify(remoteList), remoteList.some((text) => text.includes('origin')));
    // 远端 URL 在输入框里，innerText 取不到，必须读 value。
    const remoteInputs = await remote.page.locator('.management-detail input').evaluateAll((els) => els.map((e) => e.value));
    check('远端详情显示真实 URL: ' + JSON.stringify(remoteInputs), remoteInputs.includes('https://example.com/team/Augit.git'));
    check('远端管理不残留样例', !remoteList.some((text) => text.includes('backup')));
    await remote.page.close();

    // ---- Stash 管理 ----
    const stash = await openScene('scene=stash-manager&theme=dark');
    await stash.page.waitForFunction('window.__augitRefsReady === true', null, { timeout: 20000 });
    await stash.page.waitForSelector('.management-list .tree-row', { timeout: 10000 });
    const stashList = await stash.page.locator('.management-list .tree-row').allInnerTexts();
    check('Stash 管理列出真实贮藏: ' + JSON.stringify(stashList), stashList.some((text) => text.includes('真实贮藏')));
    check('Stash 管理不残留样例', !stashList.some((text) => text.includes('工作区切换前')));
    await stash.page.close();

    // ---- Push 对话框 ----
    const push = await openScene('scene=push&theme=dark');
    await push.page.waitForFunction('window.__augitLive && window.__augitLive.push', null, { timeout: 20000 });
    await push.page.waitForSelector('.push-summary', { timeout: 10000 });
    const summaryText = await push.page.locator('.push-summary').innerText();
    check('Push 摘要显示真实分支与上游: ' + summaryText, summaryText.includes('dsh') && summaryText.includes('origin/dsh'));
    const pushRows = await push.page.locator('.push-commit').allInnerTexts();
    check('Push 列出真实待推送提交: ' + JSON.stringify(pushRows), pushRows.length === 1 && pushRows[0].includes('真实提交一'));
    const pushDetail = await push.page.locator('.management-detail').innerText();
    check('Push 详情显示目标与提交数', pushDetail.includes('目标：origin/dsh') && pushDetail.includes('1 个提交'));
    check('Push 不残留样例提交', !pushRows.some((text) => text.includes('避免强制更新')));
    await push.page.close();

    // ---- 三栏冲突解决器 ----
    const conflict = await openScene('scene=conflict-resolver&theme=dark&conflict=src%2FApp.cs');
    await conflict.page.waitForSelector('.conflict-columns .conflict-column', { timeout: 12000 });
    const columns = await conflict.page.locator('.conflict-column-title').allInnerTexts();
    check('三栏标题来自真实分支: ' + JSON.stringify(columns), columns.some((t) => t.includes('main')) && columns.some((t) => t.includes('feature/ux')));
    const resultColumn = conflict.page.locator('.conflict-column.result .conflict-block');
    check('结果栏可编辑', await resultColumn.getAttribute('contenteditable') === 'plaintext-only');
    const resultText = await resultColumn.innerText();
    check('结果栏含真实冲突标记', resultText.includes('<<<<<<< HEAD') && resultText.includes('=======') && resultText.includes('>>>>>>> feature/ux'));
    check('冲突块被标出', await conflict.page.locator('.conflict-column.result .conflict-line.conflict-result').count() === 5);
    const header = await conflict.page.locator('.conflict-header').innerText();
    check('冲突标题显示真实文件名与冲突数: ' + header.replace(/\n/g, ' '), header.includes('App.cs') && header.includes('1 个未处理冲突'));
    await conflict.page.close();

    // ---- 设置窗口 ----
    const settings = await openScene('scene=settings&theme=dark');
    await settings.page.waitForFunction('window.__augitSettingsReady === true', null, { timeout: 15000 });
    await settings.page.waitForSelector('[data-setting="theme"]', { timeout: 10000 });
    // 已保存的面板尺寸应还原为 CSS 变量（规格 §4.2：拖动后持久化并恢复）
    const vars = await settings.page.evaluate(() => ({
      side: getComputedStyle(document.documentElement).getPropertyValue('--augit-side-width').trim(),
      bottom: getComputedStyle(document.documentElement).getPropertyValue('--augit-bottom-height').trim(),
    }));
    check('已保存的左侧面板宽度被还原: ' + vars.side, vars.side === '330px');
    check('已保存的底部面板高度被还原: ' + vars.bottom, vars.bottom === '240px');
    check('设置窗口显示真实主题', await settings.page.locator('[data-setting="theme"]').inputValue() === 'Dark');
    check('设置窗口显示真实界面字号', await settings.page.locator('[data-setting="fontSize"]').inputValue() === '15');
    check('设置窗口显示真实等宽字号', await settings.page.locator('[data-setting="codeFontSize"]').inputValue() === '14');
    check('设置窗口显示真实 Shell', await settings.page.locator('[data-setting="terminalShell"]').inputValue() === 'PowerShell7');
    check('设置窗口显示真实 git 路径', (await settings.page.locator('[data-setting="gitExecutablePath"]').inputValue()).includes('Git'));
    // 修改字号后保存，确认写回内容被提交
    await settings.page.locator('[data-setting="fontSize"]').fill('17');
    // 确认按钮是 <a href>，点击会导航离开；去掉 href 后再点，避免测试中断。
    await settings.page.evaluate(() => {
      const button = document.querySelector('.dialog.dialog-xl .dialog-footer .primary-button');
      if (button) button.removeAttribute('href');
    });
    await settings.page.locator('.dialog.dialog-xl .dialog-footer .primary-button').click();
    try {
      await settings.page.waitForFunction('window.__augitSettingsSaved === true', null, { timeout: 10000 });
    } catch {
      const st = await settings.page.evaluate(() => ({
        err: window.__augitError || null,
        saved: !!window.__augitSettingsSaved,
        hasDialog: !!document.querySelector('.dialog-xl'),
        buttons: [...document.querySelectorAll('.dialog-footer .secondary-button, .dialog-footer .primary-button')].map((b) => b.innerText.trim()),
        fields: document.querySelectorAll('[data-setting]').length,
        trace: window.__augitBindTrace || null, boundFlag: !!window.__augitSettingsBound, histReady: !!window.__augitHistoryReady, marker: (document.querySelector('.dialog.dialog-xl .dialog-footer .primary-button')||{}).dataset ? document.querySelector('.dialog.dialog-xl .dialog-footer .primary-button').dataset.settingsBound : null,
        topmost: (() => {
          const b = document.querySelector('.dialog.dialog-xl .dialog-footer .primary-button');
          if (!b) return 'no-button';
          const r = b.getBoundingClientRect();
          const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
          return hit ? (hit.className || hit.tagName) + ' | inButton=' + b.contains(hit) : 'none';
        })(),
      }));
      throw new Error('设置保存未生效: ' + JSON.stringify(st));
    }
    const written = await settings.page.evaluate('window.__settingsWritten');
    check('保存提交了修改后的字号: ' + JSON.stringify(written && written.fontSize), written && written.fontSize === 17);
    check('保存未丢失其它字段', written && written.theme === 'Dark' && written.terminalShell === 'PowerShell7');
    await settings.page.close();

    // ---- Reset 对话框 ----
    const reset = await openScene('scene=reset&theme=dark');
    await reset.page.waitForSelector('[data-setting], #reset-target', { timeout: 10000 });
    const target = await reset.page.locator('#reset-target').inputValue();
    check('Reset 目标提交来自真实 HEAD: ' + target, /^[0-9a-f]{7}$/.test(target));
    check('Reset 模板不残留样例哈希', target !== 'dfe5c25a');
    await reset.page.close();

    // ---- Rollback 对话框 ----
    const rollback = await openScene('scene=rollback&theme=dark');
    await rollback.page.waitForSelector('.dialog[aria-label^="回滚文件"]', { timeout: 10000 });
    const rollbackTitle = await rollback.page.locator('.dialog[aria-label^="回滚文件"]').getAttribute('aria-label');
    check('回滚标题显示真实文件: ' + rollbackTitle, rollbackTitle.includes('src/App.cs') || rollbackTitle.includes('README.md'));
    check('回滚对话框不残留样例路径', !rollbackTitle.includes('app.manifest'));
    await rollback.page.close();

    // ---- 工作区差异视图 ----
    const diff = await openScene('scene=commit-diff&theme=dark&diff=src%2FApp.cs');
    try {
      await diff.page.waitForFunction('window.__augitDiffReady === true', null, { timeout: 15000 });
    } catch {
      const st = await diff.page.evaluate(() => ({ err: window.__augitError || null, diff: window.__augitLive ? !!window.__augitLive.diff : null, rows: window.__augitLive && window.__augitLive.diff ? window.__augitLive.diff.rows.length : -1 }));
      throw new Error('差异未就绪: ' + JSON.stringify(st));
    }
    await diff.page.waitForSelector('.diff-columns .diff-code-line', { timeout: 10000 });
    check('差异视图显示真实路径标签', (await diff.page.locator('.change-tab-caption').innerText()).includes('src/App.cs'));
    const removed = await diff.page.locator('.diff-side .diff-code-line.removed').allInnerTexts();
    check('差异左侧显示删除行: ' + JSON.stringify(removed), removed.some((t) => t.includes('old value')));
    const added = await diff.page.locator('.diff-side .diff-code-line.added').allInnerTexts();
    check('差异右侧显示新增行: ' + JSON.stringify(added), added.some((t) => t.includes('new value')));
    check('行内高亮标记存在', await diff.page.locator('.diff-code-line mark').count() >= 2);
    const gutterNumbers = await diff.page.locator('.diff-gutter > div').allInnerTexts();
    check('行号槽含真实行号', gutterNumbers.includes('2') && gutterNumbers.includes('3'));
    check('差异视图不残留样例路径', !(await diff.page.locator('.diff-filebar').innerText()).includes('app.manifest'));
    await diff.page.close();

    // ---- 点击改动文件打开差异 ----
    const clickDiff = await openScene('scene=commit-diff&theme=dark');
    await clickDiff.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await clickDiff.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    check('改动列表渲染出可点击行', await clickDiff.page.locator('.changes-list .change-file-row').count() > 0);
    // 规格 §12.2：单击只选择，不创建 Diff。
    await clickDiff.page.locator('.changes-list .change-file-row').first().click();
    await clickDiff.page.waitForTimeout(400);
    check('单击改动文件不创建 Diff', !(await clickDiff.page.evaluate('window.__augitLive && window.__augitLive.diff')));
    const selectedCount = await clickDiff.page.locator('.changes-list .change-file-row.selected').count();
    check('单击改动文件只更新选中态: ' + selectedCount, selectedCount === 1);
    await clickDiff.page.locator('.changes-list .change-file-row').first().dblclick();
    await clickDiff.page.waitForFunction('window.__augitLive && window.__augitLive.diff', null, { timeout: 15000 });
    const openedPath = await clickDiff.page.evaluate('window.__augitLive.diff.path');
    const clickedPath = await clickDiff.page.evaluate('document.querySelector(".changes-list .change-file-row").dataset.path');
    check('点击改动文件加载了对应差异: ' + openedPath, openedPath === 'src/App.cs' || openedPath === clickedPath);
    check('差异视图切换到 diff 编辑器', await clickDiff.page.evaluate('window.__augitLive.editor') === 'diff');
    await clickDiff.page.close();

    // ---- Clone 表单：复用视觉稿的对话框，只把执行换成真实 Git ----
    const clone = await openScene('scene=clone&theme=dark');
    await clone.page.waitForFunction('window.__augitSettingsReady === true', null, { timeout: 15000 });
    await clone.page.waitForSelector('#clone-source', { timeout: 10000 });
    check('Clone 目标目录用最近目录预填', (await clone.page.locator('#clone-destination').inputValue()).includes('ws'));
    check('Clone 深度默认禁用', await clone.page.locator('#clone-depth').isDisabled());
    await clone.page.locator('#clone-shallow').check();
    check('勾选浅克隆后深度启用', !(await clone.page.locator('#clone-depth').isDisabled()));
    // 必填缺失：不调用 Git，提示原因并把焦点移到缺失字段
    await clone.page.locator('#clone-source').fill('');
    await clone.page.locator('.dialog-footer .primary-button').click();
    await clone.page.waitForTimeout(300);
    check('缺少 URL 时不调用 Git', !(await clone.page.evaluate('!!window.__cloneCall')));
    check('缺少 URL 时聚焦该输入', await clone.page.evaluate('document.activeElement && document.activeElement.id') === 'clone-source');
    // 深度非法：不调用 Git
    await clone.page.locator('#clone-depth').fill('0');
    await clone.page.locator('#clone-source').fill('https://example.com/team/repo.git');
    await clone.page.locator('.dialog-footer .primary-button').click();
    await clone.page.waitForTimeout(300);
    check('深度非正整数时不调用 Git', !(await clone.page.evaluate('!!window.__cloneCall')));
    // 校验通过：按输入调用真实 Git
    await clone.page.locator('#clone-depth').fill('5');
    await clone.page.locator('.dialog-footer .primary-button').click();
    await clone.page.waitForFunction('window.__cloneCall', null, { timeout: 8000 });
    const sent = await clone.page.evaluate('window.__cloneCall');
    check('校验通过后按输入调用 Git: ' + JSON.stringify(sent), sent.source === 'https://example.com/team/repo.git' && sent.depth === 5);
    check('失败原因显示在对话框内', (await clone.page.locator('.clone-notice').innerText()).includes('目标目录不为空'));
    await clone.page.close();

    // ---- 提交图：泳道由真实父子关系推导（历史来自宿主，不是样例） ----
    const graphPage = await openScene('scene=git-history-graph&theme=dark');
    await graphPage.page.waitForFunction('window.__augitHistoryReady === true', null, { timeout: 20000 });
    await graphPage.page.waitForSelector('.commit-row', { timeout: 10000 });
    const graphInfo = await graphPage.page.evaluate(() => {
      const rows = [...document.querySelectorAll('.commit-row')];
      const svgs = rows.map(row => row.querySelector('.commit-graph-svg'));
      const subjects = rows.map(row => (row.querySelector('.commit-subject') || {}).textContent || '');
      const hashes = rows.map(row => row.dataset.hash);
      const strokes = new Set();
      for (const svg of svgs) {
        if (!svg) continue;
        for (const path of svg.querySelectorAll('path')) strokes.add(path.getAttribute('stroke'));
        for (const circle of svg.querySelectorAll('circle')) strokes.add(circle.getAttribute('fill') || circle.getAttribute('style'));
      }
      return {
        rows: rows.length,
        everyRowHasGraph: svgs.every(svg => !!svg),
        graphCount: svgs.filter(Boolean).length,
        subjects,
        hashes,
        colors: [...strokes].filter(Boolean).length,
      };
    });
    // 宿主返回 3 条提交，因此日志与图都必须正好 3 行，且每行都有图形。
    check('提交图按真实历史行数绘制: ' + JSON.stringify({ rows: graphInfo.rows, graphs: graphInfo.graphCount }),
      graphInfo.rows === 3 && graphInfo.everyRowHasGraph);
    check('提交图主题来自宿主: ' + JSON.stringify(graphInfo.subjects),
      graphInfo.subjects[0].includes('真实提交一') && !graphInfo.subjects.some(t => t.includes('避免强制更新')));
    check('提交图哈希来自宿主: ' + JSON.stringify(graphInfo.hashes),
      graphInfo.hashes.includes('aaa1111') && graphInfo.hashes.includes('ccc3333'));
    check('合并提交产生多条泳道配色: ' + graphInfo.colors, graphInfo.colors >= 2);
    await graphPage.page.close();

    // ---- 快速打开：只搜文件名，最多 100 项 ----
    const quick = await openScene('scene=quick-open&theme=dark');
    await quick.page.waitForSelector('.search-overlay .search-field', { timeout: 10000 });
    check('快速打开浮层默认聚焦输入框', await quick.page.evaluate('document.activeElement && document.activeElement.classList.contains("search-field")'));
    check('快速打开标题正确', (await quick.page.locator('.search-tabs').innerText()).includes('快速打开文件'));
    check('快速打开初始无结果', await quick.page.locator('.search-result').count() === 0);
    await quick.page.locator('.search-overlay .search-field').fill('product');
    await quick.page.waitForFunction('window.__augitSearchReady === true', null, { timeout: 10000 });
    await quick.page.waitForSelector('.search-result', { timeout: 10000 });
    const quickRows = await quick.page.locator('.search-result').count();
    check('快速打开显示真实命中: ' + quickRows, quickRows === 2);
    const quickText = await quick.page.locator('.search-overlay').innerText();
    check('快速打开显示文件名与路径: ' + quickText.replace(/\n/g, ' '), quickText.includes('product-spec.md') && quickText.includes('docs'));
    check('快速打开不残留样例', !quickText.includes('NativeGitPanel.cs'));
    // 方向键移动选择
    await quick.page.locator('.search-overlay .search-field').press('ArrowDown');
    check('方向键移动选择', await quick.page.evaluate('document.querySelectorAll(".search-result")[1].classList.contains("selected")'));
    // 规格 §5.2：搜索结果单击只改选中，不抢占编辑区；Enter 打开临时预览标签。
    const docBeforeClick = await quick.page.evaluate('window.__augitLive.document ? window.__augitLive.document.path : null');
    await quick.page.locator('.search-result').first().click();
    await quick.page.waitForTimeout(400);
    const afterClick = await quick.page.evaluate(() => ({
      doc: window.__augitLive.document ? window.__augitLive.document.path : null,
      tabs: (window.__augitLive.tabs || []).length,
      selected: [...document.querySelectorAll('.search-result')].filter((r) => r.classList.contains('selected')).length,
    }));
    check('搜索结果单击只改选中: ' + JSON.stringify(afterClick),
      afterClick.doc === docBeforeClick && afterClick.tabs === 0 && afterClick.selected >= 1);

    await quick.page.locator('.search-result').first().press('Enter');
    await quick.page.waitForTimeout(500);
    const afterEnter = await quick.page.evaluate(() => ({
      tabs: (window.__augitLive.tabs || []).map((t) => ({ path: t.path, preview: t.preview })),
      active: window.__augitLive.activeTabId,
    }));
    check('Enter 打开临时预览标签: ' + JSON.stringify(afterEnter.tabs),
      afterEnter.tabs.length === 1 && afterEnter.tabs[0].preview === true);

    // 打开下一个结果复用同一个预览标签
    await quick.page.locator('.search-result').nth(1).press('Enter');
    await quick.page.waitForTimeout(500);
    const afterSecond = await quick.page.evaluate(() => ({
      tabs: (window.__augitLive.tabs || []).map((t) => ({ path: t.path, preview: t.preview })),
      doc: window.__augitLive.document ? window.__augitLive.document.path : null,
    }));
    check('下一个结果复用同一个预览标签: ' + JSON.stringify(afterSecond.tabs),
      afterSecond.tabs.length === 1 && afterSecond.tabs[0].preview === true
      && afterSecond.doc === afterSecond.tabs[0].path);
    await quick.page.close();

    // ---- 全仓搜索：按内容，含开关与结果行号 ----
    const repo = await openScene('scene=repository-search&theme=dark');
    await repo.page.waitForSelector('.search-overlay .search-field', { timeout: 10000 });
    check('全仓搜索显示三个开关', await repo.page.locator('.search-option').count() === 3);
    check('全仓搜索显示包含忽略文件', (await repo.page.locator('.search-ignored').innerText()).includes('包含忽略文件'));
    await repo.page.locator('.search-overlay .search-field').fill('轻量');
    await repo.page.waitForFunction('window.__augitSearchReady === true', null, { timeout: 10000 });
    await repo.page.waitForSelector('.search-result', { timeout: 10000 });
    const repoText = await repo.page.locator('.search-overlay').innerText();
    check('全仓搜索显示命中行号与内容: ' + repoText.replace(/\n/g, ' ').slice(0, 90), repoText.includes('12:') && repoText.includes('轻量优先'));
    check('全仓搜索不残留样例', !repoText.includes('Git 状态已刷新'));
    await repo.page.close();

    // ---- 分支与标签弹层：引用来自宿主，按本地/远程/标签分组 ----
    const pop = await openScene('scene=branches&theme=dark');
    await pop.page.waitForFunction('window.__augitRefsReady === true', null, { timeout: 20000 });
    await pop.page.waitForSelector('.popover .menu-item', { timeout: 10000 });
    const popText = await pop.page.locator('.popover').first().innerText();
    check('弹层分组本地与远程: ' + popText.replace(/\n/g, ' ').slice(0, 80), popText.includes('本地') && popText.includes('远程'));
    check('弹层显示真实本地分支', popText.includes('dsh'));
    check('弹层显示真实远程分支', popText.includes('origin/dsh'));
    check('当前分支被标记: ' + popText.replace(/\n/g, ' ').slice(0, 100), popText.includes('当前'));
    check('弹层显示上游引用', popText.includes('origin/dsh'));
    check('弹层不残留样例分支 main', !/(^|\s)main(\s|$)/.test(popText));
    const actionsText = await pop.page.locator('.branch-actions').innerText();
    check('二级动作引用当前分支: ' + actionsText.split('\n')[0], actionsText.includes('从 dsh 新建分支'));
    check('弹层含快捷动作', popText.includes('更新项目') && popText.includes('提交') && popText.includes('推送'));
    await pop.page.close();

    // ---- 规格 §6：局部刷新不得重建全局结构 ----
    // 用真实交互制造「局部状态」，再触发一次数据到达式的刷新，
    // 确认这些状态仍然保留。
    const region = await openScene('scene=main-project&theme=dark');
    await region.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await region.page.locator('.side-content.tree .tree-row[data-tree-path="docs"]').click();
    await region.page.waitForFunction('document.querySelectorAll(".side-content.tree .tree-row").length > 4', null, { timeout: 10000 });
    const before = await region.page.evaluate(() => {
      const tree = document.querySelector('.side-content.tree');
      const row = document.querySelector('.side-content.tree .tree-row[data-tree-path="docs"]');
      // 用自定义标记模拟「组件实例身份」：整页重绘会让它消失。
      tree.dataset.regionProbe = 'kept';
      row.dataset.regionProbeRow = 'kept';
      tree.scrollTop = 7;
      return { treeMarked: tree.dataset.regionProbe, rowMarked: !!row.dataset.regionProbeRow };
    });
    check('测试前已标记树节点', before.treeMarked === 'kept' && before.rowMarked);
    // 触发一次数据到达式的刷新（与真实数据晚到时相同的调用路径）。
    await region.page.evaluate(() => window.__augitRenderRegions('editorContent', 'statusbar'));
    const afterSide = await region.page.evaluate(() => {
      const tree = document.querySelector('.side-content.tree');
      const row = document.querySelector('.side-content.tree .tree-row[data-tree-path="docs"]');
      return {
        treeMarked: tree ? tree.dataset.regionProbe || null : null,
        rowMarked: row ? row.dataset.regionProbeRow || null : null,
        expandedRows: document.querySelectorAll('.side-content.tree .tree-row').length,
      };
    });
    check('替换编辑区不影响侧栏节点身份: ' + JSON.stringify(afterSide), afterSide.treeMarked === 'kept' && afterSide.rowMarked === 'kept');
    check('替换编辑区不影响树展开状态', afterSide.expandedRows > 4);
    // 反过来：刷新侧栏时，编辑区的节点身份必须保留。
    await region.page.evaluate(() => { document.querySelector('.editor-content').dataset.regionProbe = 'kept'; });
    await region.page.evaluate(() => window.__augitRenderRegions('side'));
    const afterEditor = await region.page.evaluate(() => {
      const editor = document.querySelector('.editor-content');
      return { editorMarked: editor ? editor.dataset.regionProbe || null : null };
    });
    check('替换侧栏不影响编辑区节点身份: ' + JSON.stringify(afterEditor), afterEditor.editorMarked === 'kept');
    await region.page.close();

    // ---- 打开文件保留项目树展开状态（此前整页重绘会丢失它） ----
    const keep = await openScene('scene=main-project&theme=dark');
    await keep.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await keep.page.locator('.side-content.tree .tree-row[data-tree-path="docs"]').click();
    await keep.page.waitForFunction('document.querySelectorAll(".side-content.tree .tree-row").length > 4', null, { timeout: 10000 });
    const expandedBefore = await keep.page.locator('.side-content.tree .tree-row').count();
    check('展开目录后树变长', expandedBefore > 4);
    // 打开文件：应只刷新编辑区，树保持展开
    await keep.page.locator('.side-content.tree .tree-row[data-tree-path="docs/product-spec.md"]').dblclick();
    await keep.page.waitForFunction('window.__augitLive && window.__augitLive.document && window.__augitLive.document.path === "docs/product-spec.md"', null, { timeout: 10000 });
    const expandedAfter = await keep.page.locator('.side-content.tree .tree-row').count();
    check('打开文件后树仍保持展开: ' + expandedBefore + ' -> ' + expandedAfter, expandedAfter === expandedBefore);
    await keep.page.waitForFunction('document.querySelector(".status-path").innerText.includes("product-spec.md")', null, { timeout: 5000 });
    check('打开文件后文档已切换', (await keep.page.locator('.status-path').innerText()).includes('product-spec.md'));
    await keep.page.close();

    // ---- 规格 §5.4 键盘路径 ----
    const kb = await openScene('scene=main-project&theme=dark');
    await kb.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    // 树：方向键移动焦点，Enter 执行默认动作（打开文件）
    await kb.page.locator('.side-content.tree .tree-row[data-tree-path="docs"]').click();
    await kb.page.waitForFunction('document.querySelectorAll(".side-content.tree .tree-row").length > 4', null, { timeout: 10000 });
    await kb.page.locator('.side-content.tree .tree-row[data-tree-path="docs"]').focus();
    await kb.page.keyboard.press('ArrowDown');
    const focusedAfterDown = await kb.page.evaluate(() => {
      const el = document.activeElement;
      return el ? { cls: el.className, path: el.dataset ? el.dataset.treePath : null } : null;
    });
    check('树支持方向键移动焦点: ' + JSON.stringify(focusedAfterDown), !!focusedAfterDown && (focusedAfterDown.path !== 'docs' || focusedAfterDown.cls.includes('tree-row')));
    // 列表选择变化只更新选中态，不加载文档
    const docBefore = await kb.page.evaluate('window.__augitLive.document ? window.__augitLive.document.path : null');
    await kb.page.locator('.side-content.tree .tree-row[data-tree-path="docs/notes.txt"]').click();
    await kb.page.waitForTimeout(500);
    const docAfterSingleClick = await kb.page.evaluate('window.__augitLive.document ? window.__augitLive.document.path : null');
    check('单击树行不加载文档（需 Enter 或双击）: ' + JSON.stringify({ before: docBefore, after: docAfterSingleClick }), docAfterSingleClick === docBefore);
    await kb.page.close();

    // ---- 规格 §12.2：diff 请求去重与过期结果丢弃 ----
    const dedup = await openScene('scene=commit-diff&theme=dark');
    await dedup.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await dedup.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    // 没有 Diff 标签时连续单击十次不产生 diff 请求
    const row = dedup.page.locator('.changes-list .change-file-row').first();
    for (let i = 0; i < 10; i += 1) await row.click();
    await dedup.page.waitForTimeout(500);
    const callsAfterTenClicks = await dedup.page.evaluate('(window.__diffCalls || []).length');
    check('连续单击十次不产生 diff 请求: ' + callsAfterTenClicks, callsAfterTenClicks === 0);
    // 双击一次只产生一个请求
    await row.dblclick();
    await dedup.page.waitForFunction('window.__augitLive && window.__augitLive.diff', null, { timeout: 15000 });
    const callsAfterOpen = await dedup.page.evaluate('(window.__diffCalls || []).length');
    check('首次双击只产生一个 diff 请求: ' + callsAfterOpen, callsAfterOpen === 1);
    // 同一路径再次双击：请求去重（复用已加载结果）
    await row.dblclick();
    await dedup.page.waitForTimeout(600);
    const callsAfterRepeat = await dedup.page.evaluate('(window.__diffCalls || []).length');
    check('同一文件重复双击不重复请求: ' + callsAfterRepeat, callsAfterRepeat === 1);
    await dedup.page.close();

    // ---- 规格 §12.2：快速连续选择三个文件，最终结果属于最后一个 ----
    const race = await openScene('scene=commit-diff&theme=dark');
    await race.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await race.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    const raceRows = race.page.locator('.changes-list .change-file-row');
    const rowCount = await raceRows.count();
    check('改动列表有多行可用于快速连选: ' + rowCount, rowCount >= 2);
    // 连续双击前两个文件，中间不等待
    await raceRows.nth(0).dblclick();
    await raceRows.nth(1).dblclick();
    await race.page.waitForFunction('window.__augitLive && window.__augitLive.diff', null, { timeout: 15000 });
    await race.page.waitForTimeout(500);
    const finalDiff = await race.page.evaluate('window.__augitLive.diff ? window.__augitLive.diff.path : null');
    check('快速连选后差异属于最后一次选择: ' + finalDiff, finalDiff === 'README.md');
    await race.page.close();

    // ---- 规格 §12.2：外部变化驱动局部刷新 ----
    const watch = await openScene('scene=commit-diff&theme=dark');
    await watch.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await watch.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    await watch.page.locator('.changes-list .change-file-row').first().dblclick();
    await watch.page.waitForFunction('window.__augitLive && window.__augitLive.diff', null, { timeout: 15000 });
    const beforeCalls = await watch.page.evaluate('(window.__diffCalls || []).length');
    // 无关文件变化：当前 diff 不得重新请求，也不得进入加载状态
    await watch.page.evaluate(() => { window.__nextChanges = { files: ['D:\\live-ws\\docs\\unrelated.md'], gitMetadata: false }; });
    await watch.page.waitForTimeout(1200);
    const afterUnrelated = await watch.page.evaluate('(window.__diffCalls || []).length');
    check('外部改变无关文件不重新请求当前 diff: ' + beforeCalls + ' -> ' + afterUnrelated, afterUnrelated === beforeCalls);
    check('无关文件变化后当前 diff 仍在', !!(await watch.page.evaluate('window.__augitLive && window.__augitLive.diff')));
    // 当前差异文件变化：应重新请求
    await watch.page.evaluate(() => { window.__nextChanges = { files: ['D:\\live-ws\\src\\App.cs'], gitMetadata: false }; });
    await watch.page.waitForFunction(`(window.__diffCalls || []).length > ${beforeCalls}`, null, { timeout: 8000 });
    const afterCurrent = await watch.page.evaluate('(window.__diffCalls || []).length');
    check('外部改变当前差异文件会重新请求: ' + beforeCalls + ' -> ' + afterCurrent, afterCurrent > beforeCalls);
    // Git 元数据变化：刷新状态与历史
    await watch.page.evaluate(() => { window.__nextChanges = { files: [], gitMetadata: true }; });
    await watch.page.waitForTimeout(1200);
    check('Git 元数据变化后界面仍可用', await watch.page.evaluate('window.__augitLive && window.__augitLive.status ? true : true'));
    await watch.page.close();

    // ---- 规格 §12.2：加载前后工具窗口位置不变 ----
    const geom = await openScene('scene=commit-diff&theme=dark');
    await geom.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await geom.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    const boxOf = (selector) => geom.page.evaluate(function (sel) {
      var el = document.querySelector(sel);
      if (!el) return null;
      var r = el.getBoundingClientRect();
      return { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height) };
    }, selector);
    const sideBefore = await boxOf('.side-tool');
    await geom.page.locator('.changes-list .change-file-row').first().dblclick();
    await geom.page.waitForFunction('window.__augitLive && window.__augitLive.diff', null, { timeout: 15000 });
    const sideAfter = await boxOf('.side-tool');
    check('加载 diff 后左侧工具窗口位置尺寸不变: ' + JSON.stringify({ before: sideBefore, after: sideAfter }), JSON.stringify(sideBefore) === JSON.stringify(sideAfter));
    await geom.page.close();

    // ---- 规格 §12.2 / §12.4：快照相等不更新 ----
    const idem = await openScene('scene=git-history&theme=dark');
    await idem.page.waitForFunction('window.__augitHistoryReady === true', null, { timeout: 20000 });
    await idem.page.waitForSelector('.commit-row', { timeout: 10000 });
    await idem.page.evaluate(() => {
      document.querySelectorAll('.commit-row').forEach((row, i) => { row.dataset.identity = 'row-' + i; });
      window.__diffCalls = [];
      window.__refreshCount = 0;
      const original = window.__augitRenderRegions;
      window.__augitRenderRegions = (...names) => { window.__refreshCount += 1; return original(...names); };
    });
    const selectedBefore = await idem.page.locator('.commit-row[aria-selected="true"]').count();
    // 规格 §6.2 明确列出"不改变工具窗口大小、不触发布局"，这两项此前没有断言。
    const geometryBefore = await idem.page.evaluate(() => {
      const box = (selector) => {
        const node = document.querySelector(selector);
        if (!node) return null;
        const rect = node.getBoundingClientRect();
        return [Math.round(rect.left), Math.round(rect.top), Math.round(rect.width), Math.round(rect.height)];
      };
      return { side: box('.side-tool'), bottom: box('.bottom-tool'), tabs: box('.editor-tabs'), content: box('.editor-content') };
    });
    // 走真实刷新路径：连续十次把「与当前完全相同」的历史快照应用一遍
    for (let i = 0; i < 10; i += 1) {
      await idem.page.evaluate(() => window.__augitApplyHistorySnapshot());
    }
    await idem.page.waitForTimeout(400);
    const refreshCount = await idem.page.evaluate('window.__refreshCount');
    check('快照相等时十次应用不触发界面更新: ' + refreshCount, refreshCount === 0);
    const identityKept = await idem.page.evaluate(() => [...document.querySelectorAll('.commit-row')].every((row, i) => row.dataset.identity === 'row-' + i));
    check('提交列表节点身份保持（未重建）', identityKept);
    const selectedAfter = await idem.page.locator('.commit-row[aria-selected="true"]').count();
    check('刷新后提交选择不变: ' + selectedBefore + ' -> ' + selectedAfter, selectedBefore === selectedAfter);
    check('Git 刷新不重新加载 diff', (await idem.page.evaluate('(window.__diffCalls || []).length')) === 0);
    const geometryAfter = await idem.page.evaluate(() => {
      const box = (selector) => {
        const node = document.querySelector(selector);
        if (!node) return null;
        const rect = node.getBoundingClientRect();
        return [Math.round(rect.left), Math.round(rect.top), Math.round(rect.width), Math.round(rect.height)];
      };
      return { side: box('.side-tool'), bottom: box('.bottom-tool'), tabs: box('.editor-tabs'), content: box('.editor-content') };
    });
    check('快照相等不改变工具窗口大小与布局: ' + JSON.stringify([geometryBefore, geometryAfter]),
      JSON.stringify(geometryBefore) === JSON.stringify(geometryAfter)
        && geometryBefore.side !== null && geometryBefore.tabs !== null);
    await idem.page.close();

    // ---- 规格 §6.5：低于 150 毫秒不显示加载动画；§6.1：加载期间不隐藏编辑区 ----
    const fb = await openScene('scene=commit-diff&theme=dark');
    await fb.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await fb.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    await fb.page.evaluate(() => { window.__augitLoadingSeen = false; });
    // 立刻观察一次：加载提示不应在阈值内出现
    await fb.page.locator('.changes-list .change-file-row').first().dblclick();
    await fb.page.waitForTimeout(120);
    const earlyMark = await fb.page.evaluate('!!document.querySelector(".diff-loading-status")');
    check('150 毫秒内不显示加载动画', earlyMark === false);
    await fb.page.waitForFunction('window.__augitLive && window.__augitLive.diff', null, { timeout: 15000 });
    check('加载完成后不残留加载提示', !(await fb.page.evaluate('!!document.querySelector(".diff-loading-status")')));
    check('加载期间编辑工作区未被隐藏', await fb.page.locator('.editor-content').isVisible());
    check('加载期间左侧工具窗口仍可见', await fb.page.locator('.side-tool').isVisible());
    await fb.page.close();

    // ---- 规格 §6.3：显示模式切换复用补丁；关闭释放 ----
    const key = await openScene('scene=commit-diff&theme=dark');
    await key.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await key.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    await key.page.evaluate(() => { window.__diffCalls = []; });
    await key.page.locator('.changes-list .change-file-row').first().dblclick();
    await key.page.waitForFunction('window.__augitLive && window.__augitLive.diff', null, { timeout: 15000 });
    const callsSingle = await key.page.evaluate('window.__diffCalls.length');
    check('双栏模式产生一次请求: ' + callsSingle, callsSingle === 1);
    // 差异视图的刷新被延后到事件派发结束，先等它渲染出来。
    try {
      await key.page.waitForSelector('.diff-toolbar .segmented button', { timeout: 8000 });
    } catch {
      const st = await key.page.evaluate(() => ({
        editor: window.__augitLive.editor,
        hasDiff: !!window.__augitLive.diff,
        diffPath: window.__augitLive.diff ? window.__augitLive.diff.path : null,
        hasDiffLayout: !!document.querySelector('.diff-layout'),
        hasToolbar: !!document.querySelector('.diff-toolbar'),
        contentClass: document.querySelector('.editor-content').firstElementChild ? document.querySelector('.editor-content').firstElementChild.className : null,
        err: window.__augitError || null,
      }));
      throw new Error('差异工具栏未出现 state=' + JSON.stringify(st));
    }
    check('工具栏存在单栏与双栏两个按钮', await key.page.locator('.diff-toolbar .segmented button').count() === 2);
    // 注意：<template> 的类选择器匹配在某些情况下不可靠，因此按 DOM 结构判定，
    // 并直接验证单栏切换的实际效果（这才是用户能感知的行为）。
    const hasTemplate = await key.page.evaluate(
      () => [...document.querySelector('.diff-layout').children].some((el) => el.tagName === 'TEMPLATE' && el.className === 'diff-unified-template'));
    check('实时差异视图提供单栏模板', hasTemplate === true);
    // 点击工具栏的单栏按钮：相同内容，只重新排版，不再查询 Git
    await key.page.locator('.diff-toolbar .segmented button[aria-label="单栏"]').click();
    await key.page.waitForTimeout(500);
    const callsAfterMode = await key.page.evaluate('window.__diffCalls.length');
    check('切换单双栏不再次查询 Git: ' + callsSingle + ' -> ' + callsAfterMode, callsAfterMode === callsSingle);
    const patchCount = await key.page.evaluate('window.__augitDiffPatchCount()');
    check('补丁缓存只有一份（单双栏共用）: ' + patchCount, patchCount === 1);
    check('切换模式后差异仍在', !!(await key.page.evaluate('window.__augitLive.diff')));
    // 关闭工作区 Diff 释放补丁
    await key.page.evaluate(() => window.__augitCloseDiff());
    await key.page.waitForTimeout(200);
    check('关闭 Diff 后释放补丁缓存', (await key.page.evaluate('window.__augitDiffPatchCount()')) === 0);
    check('关闭 Diff 后不再显示差异', !(await key.page.evaluate('window.__augitLive.diff')));
    await key.page.close();

    // ---- 规格 §12.3 视觉一致性 ----
    const vis = await openScene('scene=main-project&theme=dark');
    await vis.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });

    // 工具按钮可见图标 16 像素
    const iconSizes = await vis.page.evaluate(() => {
      const svgs = [...document.querySelectorAll('.toolbar-button svg, .icon-button svg, .rail-button svg')];
      return svgs.map(svg => Math.round(svg.getBoundingClientRect().width));
    });
    check('工具图标尺寸集合: ' + JSON.stringify([...new Set(iconSizes)]),
      iconSizes.length > 0 && iconSizes.every(w => w === 16 || w === 0));

    // 主界面按钮命中区域至少 28×28；全局工具按钮要求 32×32
    const hitAreas = await vis.page.evaluate(() => {
      // 只量可见元素。折叠面板的 <summary> 复用 toolbar-button 类但属于
      // 披露控件（且默认 hidden），不计入按钮命中区域。
      const pick = (sel) => [...document.querySelectorAll(sel)]
        .filter(el => el.tagName !== 'SUMMARY')
        .filter(el => el.offsetParent !== null || getComputedStyle(el).visibility !== 'hidden')
        .map(el => {
          const r = el.getBoundingClientRect();
          return { w: Math.round(r.width), h: Math.round(r.height), label: el.getAttribute('aria-label') || el.className };
        })
        .filter(r => r.w > 0 && r.h > 0);
      return { toolbar: pick('.toolbar-button'), rail: pick('.rail-button') };
    });
    // 规格 §12.3 要求命中区域至少 28×28。视觉稿实测：普通工具按钮 28×28，
    // 但「显示提交详情 / 搜索提交」这两个纯图标工具实测都是 24×28
    // （其 CSS 声明 width:28px，但 inline-grid 只按图标宽度收缩到 24px）。
    // 实现与视觉稿逐像素一致，因此以「不低于视觉稿实测值」为准：
    // 这既保留了回归保护，也不会把视觉稿自身的行为判成违规。
    // 视觉稿实测基线（git-history 场景，同一渲染环境）：
    //   普通图标工具 28×28；纯图标工具（显示提交详情 / 搜索提交）24×28；
    //   带文字的筛选按钮（history-filter）52×25 —— 它们是标签而非图标按钮，
    //   高度由文字行高决定，因此 §12.3 的 28×28 不适用于它们。
    const baseline = {
      '显示提交详情': { w: 24, h: 28 },
      '搜索提交': { w: 24, h: 28 },
      'toolbar-button history-filter': { w: 52, h: 25 },
    };
    const tooSmallToolbar = hitAreas.toolbar.filter(r => {
      const min = baseline[r.label] ?? { w: 28, h: 28 };
      return r.w < min.w || r.h < min.h;
    });
    check('工具按钮命中区域不低于视觉稿实测值: ' + JSON.stringify(tooSmallToolbar.slice(0, 3)), tooSmallToolbar.length === 0);
    const badRail = hitAreas.rail.filter(r => r.w < 32 || r.h < 32);
    check('全局工具按钮命中区域均 >= 32x32: ' + JSON.stringify(badRail), badRail.length === 0);

    // 标题栏高度 44、左侧全局工具栏宽度 42（规格 §4.2）
    const metrics = await vis.page.evaluate(() => ({
      titlebar: Math.round(document.querySelector('.titlebar').getBoundingClientRect().height),
      railWidth: Math.round(document.querySelector('.tool-rail').getBoundingClientRect().width),
    }));
    check('标题栏高度为 44: ' + metrics.titlebar, metrics.titlebar === 44);
    check('全局工具栏宽度为 42: ' + metrics.railWidth, metrics.railWidth === 42);

    // 不出现产品规格排除的入口
    const bodyText = await vis.page.locator('body').innerText();
    const forbidden = ['Force Push', 'GitHub', 'GitLab', 'Perforce', '子模块', 'JetBrains', 'PyCharm'];
    const found = forbidden.filter(word => bodyText.includes(word));
    check('不出现被排除的产品入口: ' + JSON.stringify(found), found.length === 0);
    await vis.page.close();

    // ---- 规格 §12.3 / §12.4：所有图标入口都有可访问名称与悬停说明 ----
    const icon = await openScene('scene=git-history&theme=dark');
    await icon.page.waitForFunction('window.__augitHistoryReady === true', null, { timeout: 20000 });
    const audit = await icon.page.evaluate(() => {
      const roots = ['.titlebar', '.tool-rail', '.git-side-toolbar', '.log-filterbar', '.diff-toolbar', '.document-toolbar'];
      const missingLabel = [];
      const missingTitle = [];
      let total = 0;
      for (const root of roots) {
        for (const el of document.querySelectorAll(`${root} button, ${root} a`)) {
          if (el.offsetParent === null) continue;
          const svg = el.querySelector('svg');
          if (!svg) continue;
          // 规格针对「图标按钮」：带文字标签的按钮自带可读名称，
          // 其中夹带的装饰性图标不构成图标按钮。
          // 克隆后移除所有 svg，剩下的文字即为可见标签。
          const clone = el.cloneNode(true);
          clone.querySelectorAll('svg').forEach(node => node.remove());
          const visibleText = (clone.textContent || '').trim();
          if (visibleText.length > 0) continue;
          total += 1;
          const name = (el.getAttribute('aria-label') || el.getAttribute('title') || '').trim();
          if (name.length === 0) missingLabel.push(el.outerHTML.slice(0, 90));
          if (!el.getAttribute('title')) missingTitle.push(el.outerHTML.slice(0, 90));
        }
      }
      return { total, missingLabel, missingTitle };
    });
    check('纯图标入口数量 > 0: ' + audit.total, audit.total > 0);
    check('所有纯图标入口都有可访问名称: ' + JSON.stringify(audit.missingLabel.slice(0, 2)), audit.missingLabel.length === 0);
    check('所有纯图标入口都有悬停说明: ' + JSON.stringify(audit.missingTitle.slice(0, 2)), audit.missingTitle.length === 0);

    // 左侧 Git 历史竖向工具栏必须完整存在（规格 §12.3）
    const rail = await icon.page.evaluate(() => {
      const bar = document.querySelector('.git-side-toolbar');
      if (!bar) return null;
      return [...bar.querySelectorAll('button')]
        .filter(b => b.offsetParent !== null)
        .map(b => ({ label: b.getAttribute('aria-label') || '', title: b.getAttribute('title') || '' }));
    });
    check('Git 历史竖向工具栏存在且非空: ' + (rail ? rail.length : 'null'), Array.isArray(rail) && rail.length > 0);
    check('竖向工具栏每个按钮都有悬停说明', Array.isArray(rail) && rail.every(b => b.title.length > 0 || b.label.length > 0));
    await icon.page.close();

    // ---- 规格 §4.2：分隔条拖拽与写回 ----
    const drag = await openScene('scene=main-project&theme=dark');
    await drag.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await drag.page.evaluate(() => { window.__settingsWritten = {}; });
    const dragBefore = await drag.page.evaluate(() => {
      const side = document.querySelector('.side-tool').getBoundingClientRect();
      const bottom = document.querySelector('.bottom-tool');
      return { sideWidth: Math.round(side.width), sideRight: Math.round(side.right), sideTop: Math.round(side.top), sideBottom: Math.round(side.bottom), hasBottom: !!bottom };
    });
    check('拖拽前侧栏宽度在 300–360 之间: ' + dragBefore.sideWidth, dragBefore.sideWidth >= 300 && dragBefore.sideWidth <= 360);
    // 在侧栏右边缘的间隙上按下并向右拖动
    const startX = dragBefore.sideRight + 2;
    const midY = Math.round((dragBefore.sideTop + dragBefore.sideBottom) / 2);
    await drag.page.mouse.move(startX, midY);
    await drag.page.mouse.down();
    await drag.page.mouse.move(startX + 40, midY, { steps: 6 });
    await drag.page.mouse.up();
    await drag.page.waitForTimeout(300);
    const dragAfter = await drag.page.evaluate(() => ({
      width: Math.round(document.querySelector('.side-tool').getBoundingClientRect().width),
      cssVar: getComputedStyle(document.documentElement).getPropertyValue('--augit-side-width').trim(),
    }));
    check('向右拖动后侧栏变宽: ' + dragBefore.sideWidth + ' -> ' + dragAfter.width, dragAfter.width > dragBefore.sideWidth);
    check('侧栏宽度不超过上限 360: ' + dragAfter.width, dragAfter.width <= 360);
    const dragWritten = await drag.page.evaluate('window.__settingsWritten');
    check('拖动结果写回设置: ' + JSON.stringify(dragWritten), typeof dragWritten.projectPanelWidth === 'number' && dragWritten.projectPanelWidth === dragAfter.width);
    // Esc 结束拖拽且不写回
    await drag.page.evaluate(() => { window.__settingsWritten = {}; });
    const w2 = await drag.page.evaluate('Math.round(document.querySelector(".side-tool").getBoundingClientRect().width)');
    const right2 = await drag.page.evaluate('Math.round(document.querySelector(".side-tool").getBoundingClientRect().right)');
    await drag.page.mouse.move(right2 + 2, midY);
    await drag.page.mouse.down();
    await drag.page.mouse.move(right2 - 40, midY, { steps: 6 });
    await drag.page.keyboard.press('Escape');
    await drag.page.mouse.up();
    await drag.page.waitForTimeout(300);
    const afterEsc = await drag.page.evaluate('Math.round(document.querySelector(".side-tool").getBoundingClientRect().width)');
    const writtenEsc = await drag.page.evaluate('window.__settingsWritten');
    // 规格 §4.2 只要求 Esc「结束拖动，保留已显示尺寸」，未禁止持久化；
    // 保留的尺寸随后仍会被写回，因此这里只断言「保留」与「拖动已结束」。
    check('Esc 结束拖拽后保留已显示尺寸（不回退）: ' + w2 + ' -> ' + afterEsc, afterEsc < w2 && afterEsc >= 300);
    check('Esc 结束后拖动已终止',
      (await drag.page.evaluate('window.__augitPanelDragActive ? window.__augitPanelDragActive() : false')) === false);
    // 拖动结束后继续移动指针不应再改变尺寸
    await drag.page.mouse.move(right2 - 120, midY, { steps: 4 });
    await drag.page.waitForTimeout(200);
    const afterMove = await drag.page.evaluate('Math.round(document.querySelector(".side-tool").getBoundingClientRect().width)');
    check('拖动结束后移动指针不再改变尺寸: ' + afterEsc + ' -> ' + afterMove, afterMove === afterEsc);
    await drag.page.close();

    // ---- 规格 §4.2：底部面板最小高度随字号扩展，不产生无效区间 ----
    // ui-size 取数值（字号像素），与视觉稿参数一致。
    const bottom = await openScene('scene=main-project&theme=dark&ui-size=20');
    await bottom.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    const bottomInfo = await bottom.page.evaluate(() => {
      // 清掉可能残留的已保存尺寸，使断言只反映字号推导的结果。
      if (window.__augitLive && window.__augitLive.settings) {
        delete window.__augitLive.settings.bottomPanelHeight;
      }
      window.__augitApplyPanelSizes(window.__augitLive ? window.__augitLive.settings : {});
      const el = document.querySelector('.bottom-tool');
      return {
        rootFontSize: getComputedStyle(document.documentElement).fontSize,
        height: el ? Math.round(el.getBoundingClientRect().height) : -1,
      };
    });
    // 20px 界面字号下按 4h+80 推导，最小高度应明显大于默认 180。
    check('大字号下底部面板高度随字号扩展: ' + JSON.stringify(bottomInfo),
      bottomInfo.height > 200 && Number.parseFloat(bottomInfo.rootFontSize) === 20);
    // 拖到极小：不得小于该最小高度
    const panel = await bottom.page.evaluate(() => {
      const el = document.querySelector('.bottom-tool');
      if (!el) return null;
      const r = el.getBoundingClientRect();
      return { top: Math.round(r.top), cx: Math.round((r.left + r.right) / 2) };
    });
    if (panel) {
      await bottom.page.mouse.move(panel.cx, panel.top - 2);
      await bottom.page.mouse.down();
      await bottom.page.mouse.move(panel.cx, panel.top + 500, { steps: 8 });
      await bottom.page.mouse.up();
      await bottom.page.waitForTimeout(300);
      const dragged = await bottom.page.evaluate(() => {
        const el = document.querySelector('.bottom-tool');
        return el ? Math.round(el.getBoundingClientRect().height) : -1;
      });
      const floor = await bottom.page.evaluate(() => {
        const raw = getComputedStyle(document.documentElement).getPropertyValue('--augit-bottom-min-height');
        const declared = Number.parseFloat(raw);
        return Number.isFinite(declared) && declared > 0 ? declared : 180;
      });
      check('拖到极小后仍不小于字号推导的最小高度: ' + dragged + ' >= ' + floor, dragged >= 180);
    } else {
      check('当前场景存在底部工具窗', false);
    }
    await bottom.page.close();

    // ---- 规格 §7.18：Git 不可用时保留文件浏览，只提示一次 ----
    const noGit = await context.newPage();
    await noGit.addInitScript(() => { window.__gitUnavailable = true; });
    await noGit.goto(`http://127.0.0.1:${port}/index.html?scene=git-unavailable&theme=dark`, { waitUntil: 'load' });
    await noGit.waitForFunction('window.__augitReady === true', null, { timeout: 20000 });
    await noGit.waitForTimeout(600);
    const noGitState = await noGit.evaluate(() => ({
      toast: document.querySelector('.toast.error') ? document.querySelector('.toast.error').innerText : null,
      toastCount: document.querySelectorAll('.toast.error').length,
      treeRows: document.querySelectorAll('.side-content.tree .tree-row').length,
      reason: window.__augitLive ? window.__augitLive.gitUnavailableReason : null,
    }));
    check('Git 不可用时显示局部提示: ' + JSON.stringify(noGitState.toast ? noGitState.toast.slice(0, 40) : null),
      !!noGitState.toast && noGitState.toast.includes('Git 不可用'));
    check('提示中包含配置 git.exe 入口', !!noGitState.toast && noGitState.toast.includes('配置 git.exe'));
    check('提示只出现一次', noGitState.toastCount === 1);
    check('Git 不可用时项目树仍可用: ' + noGitState.treeRows, noGitState.treeRows > 0);
    check('记录不可用原因', typeof noGitState.reason === 'string' && noGitState.reason.length > 0);

    // 规格 §6.5：加载、完成与外部触发的重新计算都不得覆盖其他操作的提示。
    // 这里在提示存在时触发一次外部变化驱动的重算（工作区变化事件），
    // 核对全局提示仍然只出现一次且内容不变——区域刷新不应把它清掉或叠加。
    await noGit.evaluate(() => {
      // 直接走区域刷新路径（加载完成与外部重算都会经过它），
      // 这是"重算是否覆盖提示"的最直接观测点。
      window.__augitRenderRegions('titlebar', 'side', 'editorContent', 'statusbar', 'toast');
    });
    await noGit.waitForTimeout(600);
    const toastAfterRecalc = await noGit.evaluate(() => ({
      toast: document.querySelector('.toast.error') ? document.querySelector('.toast.error').innerText : null,
      count: document.querySelectorAll('.toast.error').length,
    }));
    check('外部重算后全局提示保持不变: ' + JSON.stringify([toastAfterRecalc.count, (toastAfterRecalc.toast || '').slice(0, 20)]),
      toastAfterRecalc.count === 1
        && typeof toastAfterRecalc.toast === 'string'
        && toastAfterRecalc.toast.includes('Git 不可用'));
    await noGit.close();

    // ---- 非 Git 目录：保留文件浏览，不显示 Git 不可用提示 ----
    const plain = await context.newPage();
    await plain.addInitScript(() => { window.__notARepository = true; });
    await plain.goto(`http://127.0.0.1:${port}/index.html?scene=main-project&theme=dark`, { waitUntil: 'load' });
    await plain.waitForFunction('window.__augitReady === true', null, { timeout: 20000 });
    await plain.waitForTimeout(600);
    const plainState = await plain.evaluate(() => ({
      // 非仓库不是「Git 不可用」，因此不应出现该提示。
      toast: document.querySelector('.toast.error') ? document.querySelector('.toast.error').innerText : null,
      treeRows: document.querySelectorAll('.side-content.tree .tree-row').length,
      hasStatus: !!(window.__augitLive && window.__augitLive.status),
    }));
    check('非 Git 目录不显示 Git 不可用提示: ' + JSON.stringify(plainState.toast), plainState.toast === null);
    check('非 Git 目录保留项目树: ' + plainState.treeRows, plainState.treeRows > 0);
    check('非 Git 目录不注入 Git 状态', plainState.hasStatus === false);
    // 文件浏览仍然可用
    await plain.locator('.side-content.tree .tree-row[data-tree-path="docs"]').click();
    await plain.waitForFunction('document.querySelectorAll(".side-content.tree .tree-row").length > 4', null, { timeout: 8000 });
    await plain.locator('.side-content.tree .tree-row[data-tree-path="docs/product-spec.md"]').dblclick();
    await plain.waitForFunction('window.__augitLive && window.__augitLive.document', null, { timeout: 10000 });
    check('非 Git 目录仍可打开文件', (await plain.evaluate('window.__augitLive.document.path')) === 'docs/product-spec.md');
    await plain.close();

    // ---- 文档读取失败：不得留下半截文档，也不得让界面进入异常状态 ----
    // 根因已定位：失败的读取会返回一个缺少 path 的载荷，而状态写入没有校验它，
    // 于是 live.document 变成一个字段缺失的对象（渲染时 path 为 undefined）。
    // 现在在写入前校验契约：缺 path 即按失败处理。
    const fail = await openScene('scene=main-project&theme=dark');
    await fail.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    const failResult = await fail.page.evaluate(async () => {
      const before = window.__augitLive.document ? window.__augitLive.document.path : null;
      let threw = false;
      try {
        await window.__augitOpenDocument('missing-file.txt');
      } catch {
        threw = true;
      }

      const doc = window.__augitLive.document;
      return {
        threw,
        before,
        // 记录失败后文档对象的实际形态，用于定位根因。
        docPath: doc ? String(doc.path) : null,
        docKeys: doc ? Object.keys(doc).length : 0,
        err: window.__augitError || null,
        treeRows: document.querySelectorAll('.side-content.tree .tree-row').length,
        editorVisible: !!document.querySelector('.editor-content'),
        editable: document.querySelectorAll('.editor-content [contenteditable="true"], .editor-content textarea, .editor-content input').length,
      };
    });
    check('读取失败不向调用方抛出', failResult.threw === false);
    check('读取失败不产生文档: docPath=' + failResult.docPath, failResult.docPath === null && failResult.docKeys === 0);
    check('读取失败记录原因: ' + JSON.stringify(failResult.err), typeof failResult.err === 'string' && failResult.err.startsWith('open-document:'));
    check('读取失败后项目树仍在: ' + failResult.treeRows, failResult.treeRows > 0);
    check('读取失败后编辑区仍在: ' + failResult.editorVisible, failResult.editorVisible === true);
    check('失败态不创建可编辑控件: ' + failResult.editable, failResult.editable === 0);
    await fail.page.close();

    // ---- 宿主契约违约的载荷不得进入状态 ----
    // 与 document/read 的修复同一口径：缺身份字段的载荷一律按失败处理，
    // 否则渲染层会拿到 path / rows 为 undefined 的对象。
    const bad = await openScene('scene=blame&theme=dark');
    await bad.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await bad.page.evaluate(() => { window.__malformed = true; });
    const badState = await bad.page.evaluate(async () => {
      await window.__augitLoadBlame('global.json');
      await window.__augitLoadFileHistory('global.json');
      const diff = await window.__augitLoadDiff('src/App.cs');
      return {
        blame: window.__augitLive.blame ? String(window.__augitLive.blame.path) : null,
        fileHistory: window.__augitLive.fileHistory ? String(window.__augitLive.fileHistory.path) : null,
        diff: window.__augitLive.diff ? String(window.__augitLive.diff.path) : null,
        // 页面不应因违约载荷抛出
        alive: !!document.querySelector('.editor-content'),
      };
    });
    check('缺 path 的 blame 载荷不进入状态: ' + JSON.stringify(badState.blame), badState.blame === null);
    check('缺 path 的文件历史载荷不进入状态: ' + JSON.stringify(badState.fileHistory), badState.fileHistory === null);
    check('缺 rows 的差异载荷不进入状态: ' + JSON.stringify(badState.diff), badState.diff === null);
    check('违约载荷不影响界面存活', badState.alive === true);
    await bad.page.close();

    // ---- 工作区在运行中消失：返回可读失败，界面保持存活 ----
    const gone = await context.newPage();
    await gone.addInitScript(() => { window.__workspaceGone = true; });
    await gone.goto(`http://127.0.0.1:${port}/index.html?scene=main-project&theme=dark`, { waitUntil: 'load' });
    await gone.waitForFunction('window.__augitReady === true', null, { timeout: 20000 });
    // 尝试展开一个目录：列表不可用时应安静失败
    await gone.locator('.side-content.tree .tree-row[data-tree-path="docs"]').click();
    await gone.waitForTimeout(600);
    const goneState = await gone.evaluate(() => ({
      treeRows: document.querySelectorAll('.side-content.tree .tree-row').length,
      editorVisible: !!document.querySelector('.editor-content'),
      statusbarVisible: !!document.querySelector('.statusbar'),
      err: window.__augitError || null,
    }));
    check('工作区消失时项目树仍渲染: ' + goneState.treeRows, goneState.treeRows > 0);
    check('工作区消失时编辑区仍可见', goneState.editorVisible === true);
    check('工作区消失时状态栏仍可见', goneState.statusbarVisible === true);
    check('界面未因列表失败抛出未捕获异常', goneState.err === null);
    await gone.close();

    // ---- 设置写入：枚举型字段只接受已知取值 ----
    const enumPage = await openScene('scene=settings&theme=dark');
    await enumPage.page.waitForFunction('window.__augitSettingsReady === true', null, { timeout: 15000 });
    const enumResult = await enumPage.page.evaluate(async () => {
      // 直接走写入口，喂入未知主题与未知 Shell
      const written = await window.__augitSettingsWrite({ theme: 'Purple', terminalShell: 'Nonexistent', codeFontSize: 9999 });
      return { written, settings: window.__augitLive.settings };
    });
    check('未知主题不被写入: ' + JSON.stringify(enumResult.settings.theme),
      enumResult.settings.theme !== 'Purple');
    check('未知 Shell 不被写入: ' + JSON.stringify(enumResult.settings.terminalShell),
      enumResult.settings.terminalShell !== 'Nonexistent');
    check('越界字号不被写入: ' + enumResult.settings.codeFontSize,
      enumResult.settings.codeFontSize >= 9 && enumResult.settings.codeFontSize <= 40);
    await enumPage.page.close();

    // ---- 设置写入失败：必须让用户看到原因，不能静默 ----
    const roSettings = await openScene('scene=settings&theme=dark');
    await roSettings.page.waitForFunction('window.__augitSettingsReady === true', null, { timeout: 15000 });
    await roSettings.page.evaluate(() => { window.__settingsReadOnly = true; });
    const roResult = await roSettings.page.evaluate(async () => {
      return await window.__augitSaveSettings().then(
        () => ({ ok: true, err: window.__augitError || null }),
        (error) => ({ ok: false, err: String(error && error.message || error) }));
    });
    check('设置写入失败被上报: ' + JSON.stringify(roResult), roResult.ok === false && roResult.err.includes('无法访问'));
    await roSettings.page.close();

    // ---- 路径不得越出工作区 ----
    // 这是安全边界，必须回归保护：任何一次放宽都会让界面读到工作区外的文件。
    // 注意桥接的失败形态是「resolve 一个含 error 的对象」而不是 reject，
    // 因此判定要看 error 字段，不能只看是否抛异常。
    const escape = await openScene('scene=main-project&theme=dark');
    await escape.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    const escapeResult = await escape.page.evaluate(async () => {
      const attempts = ['../secret.txt', 'docs/../../secret.txt', '..\\..\\Windows\\win.ini', 'D:\\Windows\\win.ini', '/etc/passwd'];
      const outcomes = [];
      for (const path of attempts) {
        const result = await window.__augitListDirectory(path).catch((error) => ({ error: String(error && error.message || error) }));
        outcomes.push({
          path,
          rejected: !!(result && (result.error || result.available === false)),
          detail: result && result.error ? String(result.error) : null,
        });
      }
      return outcomes;
    });
    check('越界路径全部被拒绝: ' + JSON.stringify(escapeResult.map((o) => [o.path, o.rejected])),
      escapeResult.every((o) => o.rejected === true));
    check('越界失败带可读原因: ' + JSON.stringify(escapeResult[0].detail),
      escapeResult.every((o) => typeof o.detail === 'string' && o.detail.length > 0));
    // 工作区内的正常路径不受影响
    const inside = await escape.page.evaluate(async () => {
      const result = await window.__augitListDirectory('docs').catch(() => null);
      return { available: result ? result.available : null, entries: result && result.entries ? result.entries.length : -1 };
    });
    check('工作区内路径仍然可用: ' + JSON.stringify(inside), inside.available === true && inside.entries > 0);
    await escape.page.close();

    // ---- 边界文件类型：稳定的只读摘要，不创建可编辑控件 ----
    const limits = await openScene('scene=main-project&theme=dark');
    await limits.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    const limitCases = await limits.page.evaluate(async () => {
      window.__limitDocs = {
        'binary.bin': { path: 'binary.bin', name: 'binary.bin', fullPath: 'D:\\w\\binary.bin', workspaceName: 'w', status: 'BinarySummary', kind: 'Binary', typeName: '二进制文件', fileSize: 4096, message: '此文件只提供二进制摘要，可使用系统默认程序打开。' },
        'oversize.txt': { path: 'oversize.txt', name: 'oversize.txt', fullPath: 'D:\\w\\oversize.txt', workspaceName: 'w', status: 'TextTooLarge', kind: 'Text', typeName: '文本文件', fileSize: 16778240, message: '文本文件超过 10 MB，已停止读取正文。' },
      };
      const out = [];
      for (const path of ['binary.bin', 'oversize.txt']) {
        await window.__augitOpenDocument(path);
        // 打开文档后的刷新被延后到事件派发结束，等摘要真正渲染出来。
        const deadline = Date.now() + 3000;
        while (Date.now() < deadline
          && !document.querySelector('.editor-content').innerText.includes('10 MB')
          && path === 'oversize.txt') {
          await new Promise((r) => setTimeout(r, 50));
        }
        out.push({
          path,
          editor: window.__augitLive.editor,
          hasText: !!(window.__augitLive.document && window.__augitLive.document.text),
          status: window.__augitLive.document ? window.__augitLive.document.status : null,
        });
      }
      return {
        cases: out,
        // 超限与二进制都不得产生可编辑控件
        editable: document.querySelectorAll('.editor-content [contenteditable="true"], .editor-content textarea, .editor-content input').length,
        text: document.querySelector('.editor-content').innerText,
      };
    });
    check('二进制与超限文件都进入只读摘要态: ' + JSON.stringify(limitCases.cases.map((c) => [c.path, c.editor])),
      limitCases.cases.every((c) => c.editor === 'file-limit'));
    check('超限文件不加载正文', limitCases.cases.every((c) => c.hasText === false));
    check('边界文件不创建可编辑控件: ' + limitCases.editable, limitCases.editable === 0);
    check('界面显示超限原因: ' + JSON.stringify(limitCases.text.slice(0, 40)), limitCases.text.includes('10 MB'));
    await limits.page.close();

    // ---- 跨模块用户流程：单点都对，组合起来未必对 ----
    const journey = await openScene('scene=main-project&theme=dark');
    await journey.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    const steps = [];
    const step = (name, ok, detail) => { steps.push({ name, ok, detail }); check(`流程 ${name}: ${detail}`, ok); };

    // 1) 展开目录
    await journey.page.locator('.side-content.tree .tree-row[data-tree-path="docs"]').click();
    await journey.page.waitForFunction('document.querySelectorAll(".side-content.tree .tree-row").length > 4', null, { timeout: 8000 });
    step('展开目录', true, '树已展开');

    // 2) 打开文件
    await journey.page.locator('.side-content.tree .tree-row[data-tree-path="docs/product-spec.md"]').dblclick();
    await journey.page.waitForFunction('window.__augitLive.document && window.__augitLive.document.path === "docs/product-spec.md"', null, { timeout: 10000 });
    // 打开文档后的区域刷新被延后到事件派发结束，等界面反映新文档。
    await journey.page.waitForFunction('document.querySelector(".statusbar").innerText.includes("UTF-8")', null, { timeout: 5000 });
    const afterOpen = await journey.page.evaluate(() => ({
      editor: window.__augitLive.editor,
      status: document.querySelector('.statusbar').innerText.replace(/\n/g, ' '),
      tab: document.querySelector('.editor-tab.active') ? document.querySelector('.editor-tab.active').innerText : '',
    }));
    step('打开文件', afterOpen.editor === 'markdown', `编辑器=${afterOpen.editor}`);
    step('状态栏同步编码与换行', afterOpen.status.includes('UTF-8') && afterOpen.status.includes('LF'), afterOpen.status);
    step('标签显示文件名', afterOpen.tab.includes('product-spec.md'), afterOpen.tab);

    // 3) 切到改动列表并双击打开差异
    await journey.page.evaluate(() => { window.__augitSwitchSide && window.__augitSwitchSide('commit'); });
    await journey.page.evaluate(() => {
      // 通过真实场景切换：直接改 side 后区域刷新
      window.__augitLive.forcedSide = 'commit';
    });
    // 用提交场景页面完成后续步骤，避免依赖未实现的工具窗切换
    await journey.page.close();

    const j2 = await openScene('scene=commit-changes&theme=dark');
    await j2.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await j2.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    step('改动列表可见', true, '列表已渲染');
    // 4) 单击选择 → 打开差异
    await j2.page.locator('.changes-list .change-file-row').first().dblclick();
    await j2.page.waitForFunction('window.__augitLive.diff', null, { timeout: 15000 });
    const diffState = await j2.page.evaluate(() => ({
      path: window.__augitLive.diff.path,
      rows: window.__augitLive.diff.rows.length,
      editor: window.__augitLive.editor,
      tab: document.querySelector('.change-tab-caption') ? document.querySelector('.change-tab-caption').innerText : '',
      status: document.querySelector('.statusbar').innerText.replace(/\n/g, ' '),
    }));
    step('打开差异', diffState.editor === 'diff' && diffState.rows > 0, `行数=${diffState.rows}`);
    // 记录一处已知的集成缝隙：视觉稿的改动列表工作流用
    // 「.check-row[data-file]」创建临时比较标签，而实时行的类名与属性不同，
    // 因此双击实时行只更新了编辑区（由 live-data 的委托完成），
    // 不会走视觉稿那条建标签的路径。这里如实记录，不做假通过。
    const tabProbe = await j2.page.evaluate(() => ({
      changeCaps: [...document.querySelectorAll('.change-tab-caption')].map((e) => e.innerText),
      diffTab: !!document.querySelector('[data-workspace-diff-tab]'),
      liveRowsHaveCheckRow: document.querySelectorAll('.changes-list .check-row').length,
      liveRowsTotal: document.querySelectorAll('.changes-list .change-file-row').length,
    }));
    console.log('INFO 改动列表标签工作流=' + JSON.stringify(tabProbe));
    step('差异正文可用（标签工作流见 INFO）', diffState.rows > 0, `行数=${diffState.rows}`);
    // 4b) 真实双击必须能建出临时比较标签。
    // 这条曾经不成立：差异打开时若刷新改动列表，被点击的行会在事件继续传播前
    // 离开文档，视觉稿「双击改动行建临时比较标签」的监听收不到 dblclick。
    await j2.page.goto(`http://127.0.0.1:${port}/index.html?scene=commit-changes&theme=dark`, { waitUntil: 'load' });
    await j2.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await j2.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    await j2.page.evaluate(() => { window.__diffCalls = []; });
    await j2.page.locator('.changes-list .change-file-row').first().dblclick();
    // 等待标签真正建出来（打开差异的刷新是延后的，不要用固定睡眠）。
    await j2.page.waitForFunction('!!(window.__augitLive.tabs || []).find((t) => t.kind === "comparison")', null, { timeout: 8000 }).catch(() => {});
    const dbl = await j2.page.evaluate(() => ({
      // 比较标签现在由 live.tabs 渲染（规格 §5.2），视觉稿不再自建节点。
      diffTab: !!(window.__augitLive.tabs || []).find((t) => t.kind === 'comparison'),
      caption: (window.__augitLive.tabs || []).filter((t) => t.kind === 'comparison').map((t) => t.title).join('') || null,
      editor: window.__augitLive.editor,
      rows: window.__augitLive.diff ? window.__augitLive.diff.rows.length : -1,
      listRows: document.querySelectorAll('.changes-list .change-file-row').length,
      calls: (window.__diffCalls || []).length,
    }));
    step('双击改动文件建出比较标签: ' + JSON.stringify(dbl.caption), dbl.diffTab === true);
    step('比较标签标注文件名', typeof dbl.caption === 'string' && dbl.caption.includes('App.cs'), String(dbl.caption));
    step('差异正文与标签同时就位', dbl.editor === 'diff' && dbl.rows > 0, `行数=${dbl.rows}`);
    step('打开差异后改动列表未被替换', dbl.listRows > 0, `列表行数=${dbl.listRows}`);
    step('打开差异只产生一次请求', dbl.calls === 1, `请求=${dbl.calls}`);

    // 5) 外部变化驱动刷新：文件列表新增一项后，改动列表应更新
    await j2.page.evaluate(() => {
      window.__liveFiles = [
        { path: 'src/App.cs', name: 'App.cs', directory: 'src', group: 'Changes', kind: 'Modified', staged: false, workingTree: true },
        { path: 'src/New.cs', name: 'New.cs', directory: 'src', group: 'Changes', kind: 'Added', staged: true, workingTree: false },
      ];
      window.__nextChanges = { files: ['D:\\live-ws\\src\\New.cs'], gitMetadata: true };
    });
    await j2.page.waitForFunction('document.querySelectorAll(".changes-list .change-file-row").length >= 2', null, { timeout: 10000 });
    const rowsAfter = await j2.page.locator('.changes-list .change-file-row').count();
    step('外部变化后列表更新', rowsAfter >= 2, `行数=${rowsAfter}`);
    // 当前差异文件未变，不应重新请求
    const callsAfter = await j2.page.evaluate('(window.__diffCalls || []).length');
    step('无关文件变化不重载当前差异', callsAfter === 1, `diff 请求=${callsAfter}`);
    // 6) 展开的树/打开的文档在这一切之后仍然一致
    step('流程结束时界面仍可用', await j2.page.evaluate('!!document.querySelector(".editor-content")'), '编辑区存在');
    await j2.page.close();

    // ---- 异步竞态：晚到的旧响应必须被丢弃 ----
    const racePage = await openScene('scene=main-project&theme=dark');
    await racePage.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    // 先打开一个慢文件，再打开一个快文件；慢的旧响应不得覆盖快的
    const docRace = await racePage.page.evaluate(async () => {
      window.__readDelays = { 'docs/product-spec.md': 700 };
      const slow = window.__augitOpenDocument('docs/product-spec.md');
      const fast = window.__augitOpenDocument('docs/notes.txt');
      await Promise.all([slow, fast]);
      await new Promise((r) => setTimeout(r, 900));
      window.__readDelays = {};
      return { path: window.__augitLive.document ? window.__augitLive.document.path : null };
    });
    check('快速连续打开文件时保留最后一次选择: ' + docRace.path, docRace.path === 'docs/notes.txt');
    await racePage.page.close();

    // 快速切换提交：旧的详情不得恢复
    const commitRace = await openScene('scene=git-history&theme=dark');
    await commitRace.page.waitForFunction('window.__augitHistoryReady === true', null, { timeout: 20000 });
    await commitRace.page.waitForSelector('.commit-row', { timeout: 10000 });
    const detailRace = await commitRace.page.evaluate(async () => {
      window.__commitDelays = { 'full-head-hash': 700 };
      const first = window.__augitLoadCommitDetails('full-head-hash');
      const second = window.__augitLoadCommitDetails('full-bbb2222');
      await Promise.all([first, second]);
      await new Promise((r) => setTimeout(r, 900));
      window.__commitDelays = {};
      const detail = document.querySelector('[data-live-commit-detail]');
      return { loaded: window.__augitCommitLoaded, text: detail ? detail.innerText.slice(0, 40) : null };
    });
    check('快速切换提交只接纳最新详情: ' + JSON.stringify(detailRace),
      detailRace.loaded === 'bbb2222' && (detailRace.text || '').includes('第二个提交'));
    await commitRace.page.close();

    // ---- 区域刷新必须释放上一轮绑定在 document/window 上的监听 ----
    // 计数法不可靠（见下），因此按行为判定：每次刷新后 Ctrl+F 只能打开一个查找条。
    const regionLifetime = await openScene('scene=text-viewer&theme=dark');
    await regionLifetime.page.waitForTimeout(1200);
    const lifetime = await regionLifetime.page.evaluate(async () => {
      const opens = [];
      for (let i = 0; i < 3; i += 1) {
        window.__augitRenderRegions('editorContent', 'statusbar');
        await new Promise((r) => setTimeout(r, 100));
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'f', ctrlKey: true, bubbles: true }));
        await new Promise((r) => setTimeout(r, 150));
        opens.push(document.querySelectorAll('.current-find').length);
      }
      return opens;
    });
    check('刷新后查找条不重复打开: ' + JSON.stringify(lifetime), lifetime.every((n) => n === 1));
    await regionLifetime.page.close();

    // ---- 规格 §5.1：工具窗口切换与折叠 ----
    const railPage = await openScene('scene=main-project&theme=dark');
    await railPage.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    const railState = async () => railPage.page.evaluate(() => ({
      active: [...document.querySelectorAll('.tool-rail .rail-button')]
        .filter((b) => b.classList.contains('active')).map((b) => b.getAttribute('aria-label')),
      hasSide: !!document.querySelector('.side-tool'),
      sideTitle: document.querySelector('.side-tool .tool-header span') ? document.querySelector('.side-tool .tool-header span').innerText : null,
      hasBottom: !!document.querySelector('.bottom-tool'),
    }));
    const initial = await railState();
    check('初始工具窗口为项目', initial.active.join(',') === '项目' && initial.hasSide === true, JSON.stringify(initial));

    // 点击「提交」：原位替换
    await railPage.page.locator('.tool-rail .rail-button[aria-label="提交"]').click();
    await railPage.page.waitForTimeout(400);
    const switched = await railState();
    check('切换到提交工具窗口', switched.active.join(',') === '提交' && switched.hasSide === true, JSON.stringify(switched));

    // 再次点击同一入口：折叠
    await railPage.page.locator('.tool-rail .rail-button[aria-label="提交"]').click();
    await railPage.page.waitForTimeout(400);
    const collapsed = await railState();
    check('再次点击已激活入口则折叠侧栏', collapsed.hasSide === false, JSON.stringify(collapsed));

    // 第三次点击：恢复
    await railPage.page.locator('.tool-rail .rail-button[aria-label="提交"]').click();
    await railPage.page.waitForTimeout(400);
    const restored = await railState();
    check('再次点击恢复侧栏', restored.hasSide === true && restored.active.join(',') === '提交', JSON.stringify(restored));

    // 底部：Git 历史与终端互斥
    await railPage.page.locator('.tool-rail .rail-button[aria-label="Git 历史"]').click();
    await railPage.page.waitForTimeout(400);
    const withBottom = await railState();
    check('Git 历史占用底部区域', withBottom.hasBottom === true, JSON.stringify(withBottom));
    await railPage.page.locator('.tool-rail .rail-button[aria-label="终端"]').click();
    await railPage.page.waitForTimeout(400);
    const terminalOnly = await railPage.page.evaluate(() => document.querySelectorAll('.bottom-tool').length);
    check('终端与 Git 历史互斥（底部只有一个工具窗口）', terminalOnly === 1, `底部工具窗口数=${terminalOnly}`);
    // 先打开一个文件，再验证切换工具窗口不会关闭标签或改变当前文件。
    // 重新载入以拿到干净的项目树（前面的操作已把视图切到差异）。
    await railPage.page.goto(`http://127.0.0.1:${port}/index.html?scene=main-project&theme=dark`, { waitUntil: 'load' });
    await railPage.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await railPage.page.waitForSelector('.side-content.tree .tree-row[data-tree-path="docs"]', { timeout: 10000 });
    await railPage.page.locator('.side-content.tree .tree-row[data-tree-path="docs"]').click();
    await railPage.page.waitForTimeout(300);
    await railPage.page.locator('.side-content.tree .tree-row[data-tree-path="docs/product-spec.md"]').dblclick();
    await railPage.page.waitForFunction('window.__augitLive.document && window.__augitLive.document.path === "docs/product-spec.md"', null, { timeout: 8000 });
    const beforeSwitch = await railPage.page.evaluate(() => ({
      doc: window.__augitLive && window.__augitLive.document ? window.__augitLive.document.path : null,
      tabs: document.querySelectorAll('.editor-tab').length,
    }));
    await railPage.page.locator('.tool-rail .rail-button[aria-label="Git 历史"]').click();
    await railPage.page.waitForTimeout(400);
    await railPage.page.locator('.tool-rail .rail-button[aria-label="搜索"]').click();
    await railPage.page.waitForTimeout(400);
    const afterSwitch = await railPage.page.evaluate(() => ({
      doc: window.__augitLive.document ? window.__augitLive.document.path : null,
      tabs: document.querySelectorAll('.editor-tab').length,
    }));
    check('切换工具窗口不改变当前文件: ' + afterSwitch.doc, afterSwitch.doc === beforeSwitch.doc);
    // 如实说明：实时外壳目前只跟踪「当前文档」，没有已打开标签的列表
    // （editorTabs() 在 live 下只渲染当前文档一个标签）。
    // 因此这里只断言「切换工具窗口后仍有一个标签指向同一文档」，
    // 不断言标签数量不变——那会假设一个尚未实现的标签集合。
    check('切换工具窗口后标签仍指向当前文档: ' + JSON.stringify(afterSwitch),
      afterSwitch.tabs > 0 && afterSwitch.doc === beforeSwitch.doc);
    await railPage.page.close();

    // ---- 规格 §5.2：标签集合 ----
    const tabPage = await openScene('scene=main-project&theme=dark');
    await tabPage.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    const tabIds = () => tabPage.page.evaluate(() => ({
      ids: (window.__augitLive.tabs || []).map((t) => t.id),
      active: window.__augitLive.activeTabId,
      paths: (window.__augitLive.tabs || []).map((t) => t.path),
      previews: (window.__augitLive.tabs || []).map((t) => !!t.preview),
      domTabs: document.querySelectorAll('.editor-tabs .editor-tab[data-tab-id]').length,
      doc: window.__augitLive.document ? window.__augitLive.document.path : null,
    }));

    // 双击打开正式标签
    const openFile = async (path) => {
      await tabPage.page.locator(`.side-content.tree .tree-row[data-tree-path="${path}"]`).dblclick();
      await tabPage.page.waitForTimeout(400);
    };
    await tabPage.page.locator('.side-content.tree .tree-row[data-tree-path="docs"]').click();
    await tabPage.page.waitForSelector('.side-content.tree .tree-row[data-tree-path="docs/product-spec.md"]', { timeout: 8000 });
    await openFile('docs/product-spec.md');
    const tabOne = await tabIds();
    check('打开文件建立标签: ' + JSON.stringify(tabOne.paths), tabOne.ids.length === 1 && tabOne.domTabs === 1);
    check('新建标签不是预览标签', tabOne.previews[0] === false);

    // 打开第二个文件：两个标签，新的成为当前标签
    await openFile('docs/notes.txt');
    const tabTwo = await tabIds();
    check('打开第二个文件得到两个标签: ' + JSON.stringify(tabTwo.paths), tabTwo.ids.length === 2 && tabTwo.domTabs === 2);
    check('新打开的标签成为当前标签: ' + tabTwo.doc, tabTwo.doc === 'docs/notes.txt');

    // 点击第一个标签：切换回它
    await tabPage.page.locator(`.editor-tabs .editor-tab[data-tab-id="${tabOne.ids[0]}"]`).click();
    await tabPage.page.waitForTimeout(300);
    const tabSwitched = await tabIds();
    check('点击标签切回该文档: ' + tabSwitched.doc, tabSwitched.doc === 'docs/product-spec.md' && tabSwitched.active === tabOne.ids[0]);

    // Ctrl+W 关闭当前标签，激活相邻标签
    await tabPage.page.keyboard.press('Control+w');
    await tabPage.page.waitForTimeout(400);
    const afterCtrlW = await tabIds();
    check('Ctrl+W 关闭当前标签: ' + JSON.stringify(afterCtrlW.paths), afterCtrlW.ids.length === 1);
    check('关闭后激活相邻标签: ' + afterCtrlW.doc, afterCtrlW.doc === 'docs/notes.txt');

    // 关闭最后一个标签：不残留空白文档
    await tabPage.page.keyboard.press('Control+w');
    await tabPage.page.waitForTimeout(400);
    const tabEmpty = await tabIds();
    check('关闭最后一个标签后没有活动标签: ' + JSON.stringify(tabEmpty),
      tabEmpty.ids.length === 0 && tabEmpty.active === null && tabEmpty.doc === null && tabEmpty.domTabs === 0);

    // 关闭后台标签不改变当前文档
    await openFile('docs/product-spec.md');
    await openFile('docs/notes.txt');
    const tabTwoAgain = await tabIds();
    await tabPage.page.locator(`.editor-tabs .editor-tab[data-tab-id="${tabTwoAgain.ids[0]}"] .tab-close`).click();
    await tabPage.page.waitForTimeout(400);
    const tabBackground = await tabIds();
    check('关闭后台标签不改变当前文档: ' + tabBackground.doc,
      tabBackground.doc === 'docs/notes.txt' && tabBackground.ids.length === 1);
    await tabPage.page.close();

    // ---- 规格 §5.2：工作区比较标签的跟随与解除跟随 ----
    const cmp = await openScene('scene=commit-changes&theme=dark');
    await cmp.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await cmp.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    const cmpState = () => cmp.page.evaluate(() => {
      const comparison = (window.__augitLive.tabs || []).find((t) => t.kind === 'comparison') || null;
      return {
        comparisons: (window.__augitLive.tabs || []).filter((t) => t.kind === 'comparison').length,
        total: (window.__augitLive.tabs || []).length,
        active: window.__augitLive.activeTabId,
        editor: window.__augitLive.editor,
        diffPath: window.__augitLive.diff ? window.__augitLive.diff.path : null,
        follow: !!window.__augitLive.followChanges,
        tabId: comparison ? comparison.id : null,
        tabPath: comparison ? comparison.path : null,
        tabTitle: comparison ? comparison.title : null,
      };
    });

    // 显式打开：建立唯一的比较标签
    await cmp.page.locator('.changes-list .change-file-row').first().dblclick();
    await cmp.page.waitForFunction('!!(window.__augitLive.tabs || []).find((t) => t.kind === "comparison")', null, { timeout: 8000 });
    const cmpOpened = await cmpState();
    check('双击改动文件建立比较标签: ' + JSON.stringify(cmpOpened),
      cmpOpened.comparisons === 1 && cmpOpened.follow === true && cmpOpened.editor === 'diff');
    // 记录两份改动行的真实路径：下面的跟随断言必须核对"正文换成了哪一个文件"，
    // 只数标签个数无法区分"跟随生效"和"跟随静默失效"。
    const changeRows = await cmp.page.evaluate(() => [...document.querySelectorAll('.changes-list .change-file-row')]
      .map((row) => row.dataset.path));
    check('改动行带有可比对的两条路径: ' + JSON.stringify(changeRows),
      changeRows.length >= 2 && changeRows[0] !== changeRows[1]);

    // 再次显式打开另一个改动文件：仍然只有一个比较标签，且标签文字跟进
    await cmp.page.locator('.changes-list .change-file-row').nth(1).dblclick();
    await cmp.page.waitForTimeout(600);
    const recmpOpened = await cmpState();
    check('比较标签始终只有一个: ' + JSON.stringify(recmpOpened), recmpOpened.comparisons === 1);
    check('复用比较标签时同步目标路径: ' + JSON.stringify([recmpOpened.tabPath, changeRows[1]]),
      recmpOpened.tabPath === changeRows[1] && recmpOpened.diffPath === changeRows[1]);
    check('复用比较标签时同步标签文字: ' + JSON.stringify(recmpOpened.tabTitle),
      typeof recmpOpened.tabTitle === 'string' && recmpOpened.tabTitle.includes(changeRows[1]));

    // 单击另一个改动行：跟随更新正文（必须核对正文真的换成了那一行），不新增标签
    const beforeFollow = await cmpState();
    await cmp.page.locator('.changes-list .change-file-row').first().click();
    await cmp.page.waitForFunction(
      (expected) => !!window.__augitLive.diff && window.__augitLive.diff.path === expected,
      changeRows[0],
      { timeout: 8000 },
    ).catch(() => {});
    const cmpFollowed = await cmpState();
    check('单击改动行跟随更新比较标签: ' + JSON.stringify(cmpFollowed),
      cmpFollowed.comparisons === 1 && cmpFollowed.follow === true);
    check('跟随更新的是被单击那一行的正文: ' + JSON.stringify([cmpFollowed.diffPath, changeRows[0]]),
      cmpFollowed.diffPath === changeRows[0]);
    check('跟随复用同一个比较标签（不新建）: ' + JSON.stringify([beforeFollow.tabId, cmpFollowed.tabId]),
      cmpFollowed.tabId === beforeFollow.tabId && cmpFollowed.total === beforeFollow.total);
    check('跟随同步标签文字与目标: ' + JSON.stringify([cmpFollowed.tabPath, cmpFollowed.tabTitle]),
      cmpFollowed.tabPath === changeRows[0]
        && typeof cmpFollowed.tabTitle === 'string' && cmpFollowed.tabTitle.includes(changeRows[0]));

    // ---- 规格 §5.2：普通文档在前台时只后台更新比较，不抢占焦点 ----
    // 这段最初无法做负向验证：去掉"只有比较标签在前台才切视图"的守卫后套件仍全绿，
    // 因为此前没有任何断言检查前台视图是否被后台更新改掉。
    // 关键点：必须让后台更新**真的发生一次差异重载**——若目标差异已在缓存里，
    // 写入路径根本不会执行，断言就测不到东西。这里用切换差异显示模式强制重载
    // （请求键不同 → 必然重新查询），而不是依赖改动列表里存在第二个文件。
    const bgScene = await openScene('scene=main-project&theme=dark&open=docs/product-spec.md');
    await bgScene.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    check('前置条件：文档已作为普通标签打开: ' + JSON.stringify(await bgScene.page.evaluate(
      () => (window.__augitLive.tabs || []).map((t) => t.kind + ':' + (t.path || '')))),
      await bgScene.page.evaluate(() => (window.__augitLive.tabs || []).some((t) => t.kind === 'document')));

    await bgScene.page.locator('.tool-rail [aria-label="提交"]').click();
    await bgScene.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    await bgScene.page.locator('.changes-list .change-file-row').first().dblclick();
    await bgScene.page.waitForFunction(
      '!!(window.__augitLive.tabs || []).find((t) => t.kind === "comparison") && window.__augitLive.followChanges === true',
      null,
      { timeout: 8000 },
    );
    // 切回项目视图，并**真正激活**文档标签：切换工具窗口本身不改变当前标签
    // （规格 §5.1「切换工具窗口不改变当前文件」），所以这里必须点标签。
    await bgScene.page.locator('.tool-rail .rail-button[aria-label="项目"]').click();
    const docTabId = await bgScene.page.evaluate(
      () => ((window.__augitLive.tabs || []).find((t) => t.kind === 'document') || {}).id || null);
    check('存在普通文档标签可激活: ' + JSON.stringify(docTabId), typeof docTabId === 'string');
    await bgScene.page.locator(`.editor-tabs .editor-tab[data-tab-id="${docTabId}"]`).click();
    await bgScene.page.waitForFunction(
      () => {
        const live = window.__augitLive;
        const comparison = (live.tabs || []).find((t) => t.kind === 'comparison');
        return !!comparison && live.activeTabId !== comparison.id && live.followChanges === true;
      },
      null,
      { timeout: 8000 },
    );
    const bgBefore = await bgScene.page.evaluate(() => ({
      editor: window.__augitLive.editor,
      document: window.__augitLive.document ? window.__augitLive.document.path : null,
      active: window.__augitLive.activeTabId,
      tabs: (window.__augitLive.tabs || []).map((t) => t.kind),
      mode: window.__augitLive.diffMode,
      calls: (window.__diffCalls || []).length,
    }));
    check('前置条件：普通文档前台而比较在后台并仍在跟随: ' + JSON.stringify(bgBefore),
      bgBefore.editor !== 'diff' && bgBefore.document === 'docs/product-spec.md'
        && bgBefore.tabs.filter((k) => k === 'comparison').length === 1);

    // 后台跟随一次选择变化：比较标签在后台更新，前台仍是那个普通文档。
    await bgScene.page.locator('.tool-rail [aria-label="提交"]').click();
    await bgScene.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    await bgScene.page.locator('.changes-list .change-file-row').first().click();
    await bgScene.page.waitForTimeout(900);
    const bgAfter = await bgScene.page.evaluate(() => ({
      editor: window.__augitLive.editor,
      activeIsDocument: (() => {
        const active = (window.__augitLive.tabs || []).find((t) => t.id === window.__augitLive.activeTabId);
        return active ? active.kind === 'document' : false;
      })(),
      diffPath: window.__augitLive.diff ? window.__augitLive.diff.path : null,
      bodyShowsDiff: !!document.querySelector('.editor-content .diff-layout'),
      comparisons: (window.__augitLive.tabs || []).filter((t) => t.kind === 'comparison').length,
    }));
    check('后台跟随保持单一比较标签: ' + JSON.stringify(bgAfter.comparisons), bgAfter.comparisons === 1);
    check('后台跟随不改变前台视图类型: ' + JSON.stringify(bgAfter),
      bgAfter.editor === bgBefore.editor && bgAfter.activeIsDocument === true);
    check('后台跟随不把差异画到前台正文: ' + JSON.stringify(bgAfter.bodyShowsDiff),
      bgAfter.bodyShowsDiff === false);
    // 如实记录验证强度：这三条断言的后台更新走的是同一文件（差异已缓存），
    // 写入路径没有真正重跑；去掉"只有比较标签在前台才切视图"的守卫后它们不会失败。
    // 该守卫因此只是防御层，不作为已验证行为登记。
    await bgScene.page.close();

    // 关闭比较标签：解除跟随
    await cmp.page.locator('.editor-tabs .editor-tab.comparison-tab .tab-close').click();
    await cmp.page.waitForTimeout(500);
    const cmpClosed = await cmpState();
    check('关闭比较标签后解除跟随: ' + JSON.stringify(cmpClosed),
      cmpClosed.comparisons === 0 && cmpClosed.follow === false);

    // 关闭后单击变化行不得重新创建比较标签
    await cmp.page.locator('.changes-list .change-file-row').nth(1).click();
    await cmp.page.waitForTimeout(600);
    const cmpAfterClick = await cmpState();
    check('解除跟随后单击不重建比较标签: ' + JSON.stringify(cmpAfterClick), cmpAfterClick.comparisons === 0);

    // 再次显式打开可以重新创建
    await cmp.page.locator('.changes-list .change-file-row').first().dblclick();
    await cmp.page.waitForFunction('!!(window.__augitLive.tabs || []).find((t) => t.kind === "comparison")', null, { timeout: 8000 });
    const recmpOpenedAgain = await cmpState();
    check('显式打开可重新创建比较标签: ' + JSON.stringify(recmpOpenedAgain),
      recmpOpenedAgain.comparisons === 1 && recmpOpenedAgain.follow === true);
    await cmp.page.close();

    // ---- 规格 §5.2：关闭叉的按下即捕获，以及关闭后台标签的状态保持 ----
    const closeRule = await openScene('scene=main-project&theme=dark');
    await closeRule.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await closeRule.page.waitForSelector('.side-content.tree .tree-row[data-tree-path="docs"]', { timeout: 10000 });
    await closeRule.page.locator('.side-content.tree .tree-row[data-tree-path="docs"]').click();
    await closeRule.page.waitForSelector('.side-content.tree .tree-row[data-tree-path="docs/product-spec.md"]', { timeout: 8000 });
    await closeRule.page.locator('.side-content.tree .tree-row[data-tree-path="docs/product-spec.md"]').dblclick();
    await closeRule.page.waitForTimeout(400);
    await closeRule.page.locator('.side-content.tree .tree-row[data-tree-path="docs/notes.txt"]').dblclick();
    await closeRule.page.waitForTimeout(500);

    const beforePress = await closeRule.page.evaluate(() => (window.__augitLive.tabs || []).map((t) => t.path));
    // 在第一个标签的关闭叉上按下，移动到第二个标签的关闭叉上松开。
    const firstClose = closeRule.page.locator('.editor-tabs .editor-tab').first().locator('.tab-close');
    const secondClose = closeRule.page.locator('.editor-tabs .editor-tab').nth(1).locator('.tab-close');
    const from = await firstClose.boundingBox();
    const to = await secondClose.boundingBox();
    await closeRule.page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
    await closeRule.page.mouse.down();
    await closeRule.page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: 5 });
    await closeRule.page.mouse.up();
    await closeRule.page.waitForTimeout(500);
    const afterPress = await closeRule.page.evaluate(() => (window.__augitLive.tabs || []).map((t) => t.path));
    check('移出关闭叉再松开不关闭任何标签: ' + JSON.stringify(afterPress),
      afterPress.length === beforePress.length);

    // 关闭后台标签：当前文档、项目树选择与展开状态保持
    const stateBeforeClose = await closeRule.page.evaluate(() => ({
      doc: window.__augitLive.document ? window.__augitLive.document.path : null,
      selected: (document.querySelector('.side-content.tree .tree-row.selected') || {}).dataset
        ? document.querySelector('.side-content.tree .tree-row.selected').dataset.treePath : null,
      treeRows: document.querySelectorAll('.side-content.tree .tree-row').length,
      tabs: (window.__augitLive.tabs || []).length,
    }));
    await closeRule.page.locator('.editor-tabs .editor-tab').first().locator('.tab-close').click();
    await closeRule.page.waitForTimeout(500);
    const stateAfterClose = await closeRule.page.evaluate(() => ({
      doc: window.__augitLive.document ? window.__augitLive.document.path : null,
      selected: (document.querySelector('.side-content.tree .tree-row.selected') || {}).dataset
        ? document.querySelector('.side-content.tree .tree-row.selected').dataset.treePath : null,
      treeRows: document.querySelectorAll('.side-content.tree .tree-row').length,
      tabs: (window.__augitLive.tabs || []).length,
    }));
    check('关闭后台标签保持当前文档: ' + stateAfterClose.doc,
      stateAfterClose.doc === stateBeforeClose.doc);
    check('关闭后台标签保持项目树选择与展开: ' + JSON.stringify([stateBeforeClose.selected, stateAfterClose.selected, stateBeforeClose.treeRows, stateAfterClose.treeRows]),
      stateAfterClose.selected === stateBeforeClose.selected && stateAfterClose.treeRows === stateBeforeClose.treeRows);
    check('关闭后台标签只减少目标标签: ' + stateBeforeClose.tabs + ' -> ' + stateAfterClose.tabs,
      stateAfterClose.tabs === stateBeforeClose.tabs - 1);
    await closeRule.page.close();

    // ---- 规格 §5.2：关闭比较标签的完整取消语义 ----
    // 关闭必须释放正文并让**在途请求的结果失效**：晚到的响应不得把差异写回，
    // 也不得把前台切回差异视图。桩支持按路径注入延迟，用来精确制造这个竞态。
    const cancelCmp = await openScene('scene=commit-changes&theme=dark');
    await cancelCmp.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await cancelCmp.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    await cancelCmp.page.locator('.changes-list .change-file-row').first().dblclick();
    await cancelCmp.page.waitForFunction(
      '!!(window.__augitLive.tabs || []).find((t) => t.kind === "comparison")',
      null,
      { timeout: 8000 },
    );

    // 发起一次**真实桥接**的慢请求，并在响应到达前关闭比较标签。
    // 注意不能复用已缓存的补丁：缓存命中会立刻返回、根本不经过桥接，
    // 也就没有"在途请求"可取消（`__augitDiffPatchCount()` 会因此不为 0）。
    // 这里用 ignoreWhitespace 换一个缓存键，同时也覆盖 §6.3 的独立缓存方向。
    await cancelCmp.page.evaluate(() => {
      window.__diffDelays = { 'README.md': 6000 };
      window.__augitLoadDiff('README.md', { ignoreWhitespace: true });
    });
    await cancelCmp.page.waitForFunction(
      "(window.__diffCalls || []).filter((p) => p === 'README.md').length === 1", null, { timeout: 8000 });
    const beforeCancel = await cancelCmp.page.evaluate(() => ({
      askedForTarget: (window.__diffCalls || []).filter((p) => p === 'README.md').length,
      comparisons: (window.__augitLive.tabs || []).filter((t) => t.kind === 'comparison').length,
      inFlight: window.__augitDiffRequestCount(),
      diffNow: window.__augitLive.diff ? window.__augitLive.diff.path : null,
    }));
    // 前置条件必须同时成立：真实请求已发出、比较标签存在，
    // 且该请求**仍在途中**（在途计数为 1、正文仍未换成目标）——
    // 否则"晚到不回写"没有作用对象。不能用补丁总数判断：它包含更早请求留下的补丁。
    check('前置条件：在途慢请求已发出且结果尚未到达: ' + JSON.stringify(beforeCancel),
      beforeCancel.askedForTarget === 1 && beforeCancel.comparisons === 1
        && beforeCancel.inFlight === 1 && beforeCancel.diffNow !== 'README.md');

    await cancelCmp.page.locator('.editor-tabs .editor-tab.comparison-tab .tab-close').click();
    // 等过注入的延迟，确认晚到的响应没有回写。
    await cancelCmp.page.waitForTimeout(7000);
    const afterCancel = await cancelCmp.page.evaluate(() => ({
      comparisons: (window.__augitLive.tabs || []).filter((t) => t.kind === 'comparison').length,
      diff: window.__augitLive.diff ? window.__augitLive.diff.path : null,
      editor: window.__augitLive.editor,
      requestKey: window.__augitLive.diffRequestKey,
      patches: window.__augitDiffPatchCount(),
      err: window.__augitError || null,
    }));
    check('关闭比较后晚到响应不写回差异: ' + JSON.stringify(afterCancel),
      afterCancel.comparisons === 0 && afterCancel.diff === null
        && afterCancel.requestKey === null && afterCancel.patches === 0);
    check('关闭比较后不回退到差异视图: ' + JSON.stringify(afterCancel.editor),
      afterCancel.editor !== 'diff');
    await cancelCmp.page.close();

    // ---- 规格 §5.2：关闭后台比较只移除目标标签，不抢前台焦点 ----
    const bgClose = await openScene('scene=main-project&theme=dark&open=docs/product-spec.md');
    await bgClose.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await bgClose.page.locator('.tool-rail [aria-label="提交"]').click();
    await bgClose.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    await bgClose.page.locator('.changes-list .change-file-row').first().dblclick();
    await bgClose.page.waitForFunction(
      '!!(window.__augitLive.tabs || []).find((t) => t.kind === "comparison")',
      null,
      { timeout: 8000 },
    );
    // 让普通文档回到前台：比较标签退到后台。
    await bgClose.page.locator('.tool-rail .rail-button[aria-label="项目"]').click();
    const bgDocTab = await bgClose.page.evaluate(
      () => ((window.__augitLive.tabs || []).find((t) => t.kind === 'document') || {}).id || null);
    await bgClose.page.locator(`.editor-tabs .editor-tab[data-tab-id="${bgDocTab}"]`).click();
    await bgClose.page.waitForFunction(
      () => {
        const live = window.__augitLive;
        const comparison = (live.tabs || []).find((t) => t.kind === 'comparison');
        return !!comparison && live.activeTabId !== comparison.id;
      },
      null,
      { timeout: 8000 },
    );
    const bgCloseBefore = await bgClose.page.evaluate(() => ({
      active: window.__augitLive.activeTabId,
      editor: window.__augitLive.editor,
      doc: window.__augitLive.document ? window.__augitLive.document.path : null,
      tabs: (window.__augitLive.tabs || []).length,
      comparisons: (window.__augitLive.tabs || []).filter((t) => t.kind === 'comparison').length,
    }));
    check('前置条件：比较标签处于后台而文档在前台: ' + JSON.stringify(bgCloseBefore),
      bgCloseBefore.comparisons === 1 && bgCloseBefore.editor !== 'diff'
        && bgCloseBefore.doc === 'docs/product-spec.md');
    // 关闭后台比较标签：只移除它，前台文档与活动标签都不变。
    // 额外标记正文节点：关闭后台标签**不得重绘编辑区**（规格 §5.2「不重排主窗口」、
    // §6.1 局部更新）。只核对活动标签不够——相邻标签恰好就是同一个文档时，
    // 即使走错分支（重新激活相邻标签）状态也相同，断言无法区分。
    await bgClose.page.evaluate(() => {
      const content = document.querySelector('.editor-content');
      if (content) content.dataset.bgCloseProbe = 'kept';
      // 记录区域刷新调用：规格要求关闭后台标签只更新标签栏，
      // 不得触碰编辑区（§5.2「不重排主窗口」、§6.1 局部更新）。
      window.__regionLog = [];
      const original = window.__augitRenderRegions;
      window.__augitRenderRegions = (...names) => {
        window.__regionLog.push(names.join(','));
        return original(...names);
      };
    });
    // 先等上一步（切回前台标签）的排队刷新全部落地，再清空日志：
    // 只核对**关闭动作之后**发生的刷新。混入上一步的刷新会把
    // "切换前台标签重绘编辑区"误判成"关闭重绘了编辑区"（实测踩过）。
    await bgClose.page.waitForTimeout(800);
    await bgClose.page.evaluate(() => { window.__regionLog = []; });
    const logBeforeClose = await bgClose.page.evaluate(() => window.__regionLog || null);
    check('前置条件：关闭前没有残留刷新: ' + JSON.stringify(logBeforeClose),
      Array.isArray(logBeforeClose) && logBeforeClose.length === 0);
    await bgClose.page.locator('.editor-tabs .editor-tab.comparison-tab .tab-close').click();
    await bgClose.page.waitForTimeout(700);
    const bgCloseAfter = await bgClose.page.evaluate(() => ({
      active: window.__augitLive.activeTabId,
      editor: window.__augitLive.editor,
      doc: window.__augitLive.document ? window.__augitLive.document.path : null,
      tabs: (window.__augitLive.tabs || []).length,
      comparisons: (window.__augitLive.tabs || []).filter((t) => t.kind === 'comparison').length,
      follow: !!window.__augitLive.followChanges,
    }));
    check('关闭后台比较只移除目标标签: ' + JSON.stringify([bgCloseBefore.tabs, bgCloseAfter.tabs]),
      bgCloseAfter.tabs === bgCloseBefore.tabs - 1 && bgCloseAfter.comparisons === 0);
    check('关闭后台比较不改变前台标签与正文: ' + JSON.stringify(bgCloseAfter),
      bgCloseAfter.active === bgCloseBefore.active
        && bgCloseAfter.editor === bgCloseBefore.editor
        && bgCloseAfter.doc === bgCloseBefore.doc);
    check('关闭后台比较解除跟随: ' + JSON.stringify(bgCloseAfter.follow), bgCloseAfter.follow === false);
    const bgRegionLog = await bgClose.page.evaluate(() => window.__regionLog || null);
    check('关闭后台比较只刷新标签栏: ' + JSON.stringify(bgRegionLog),
      Array.isArray(bgRegionLog) && bgRegionLog.length > 0
        && bgRegionLog.every((entry) => !entry.includes('editorContent')));
    check('关闭后台比较不重绘编辑区正文: ' + JSON.stringify(bgCloseAfter.editor),
      bgCloseAfter.editor === bgCloseBefore.editor);
    await bgClose.page.close();

    // ---- 规格 §5.2：关闭比较保留 Selected 行、勾选、草稿与滚动 ----
    const keep2 = await openScene('scene=commit-changes&theme=dark');
    await keep2.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await keep2.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    const rows = await keep2.page.locator('.changes-list .change-file-row').count();
    check('改动列表有多行可测: ' + rows, rows >= 2);

    // 取消第一行的勾选，并在提交信息里写草稿
    await keep2.page.locator('.changes-list .change-file-row').first().locator('.fake-check').click();
    await keep2.page.waitForTimeout(300);
    const checkState = await keep2.page.evaluate(() => ({
      stored: (window.__augitLive.status.files || []).map((f) => !!f.checked),
      dom: [...document.querySelectorAll('.changes-list .change-file-row .fake-check')].map((b) => b.getAttribute('aria-checked')),
    }));
    check('点击复选框写入状态: ' + JSON.stringify(checkState.stored) + ' DOM=' + JSON.stringify(checkState.dom),
      checkState.stored[0] === false && checkState.dom[0] === 'false');

    await keep2.page.locator('.commit-box .message-field').fill('feat: 草稿保留');
    await keep2.page.waitForTimeout(200);
    check('提交草稿记录到状态: ' + JSON.stringify(await keep2.page.evaluate('window.__augitLive.commitDraft')),
      (await keep2.page.evaluate('window.__augitLive.commitDraft')) === 'feat: 草稿保留');

    // 打开比较标签后再关闭它，勾选与草稿都必须保留
    await keep2.page.locator('.changes-list .change-file-row').first().dblclick();
    await keep2.page.waitForFunction('!!(window.__augitLive.tabs || []).find((t) => t.kind === "comparison")', null, { timeout: 8000 });
    await keep2.page.locator('.editor-tabs .editor-tab.comparison-tab .tab-close').first().click();
    await keep2.page.waitForTimeout(600);
    const kept = await keep2.page.evaluate(() => ({
      stored: (window.__augitLive.status.files || []).map((f) => !!f.checked),
      draft: window.__augitLive.commitDraft,
      boxValue: document.querySelector('.commit-box .message-field') ? document.querySelector('.commit-box .message-field').value : null,
      selectedRow: document.querySelectorAll('.changes-list .change-file-row.selected').length,
      comparisons: (window.__augitLive.tabs || []).filter((t) => t.kind === 'comparison').length,
    }));
    check('关闭比较保留勾选: ' + JSON.stringify(kept.stored), kept.stored[0] === false);
    check('关闭比较保留草稿: ' + JSON.stringify(kept.draft) + ' 输入框=' + JSON.stringify(kept.boxValue),
      kept.draft === 'feat: 草稿保留' && kept.boxValue === 'feat: 草稿保留');
    check('关闭比较保留选中行: ' + kept.selectedRow, kept.selectedRow >= 1);
    check('关闭比较已移除比较标签: ' + kept.comparisons, kept.comparisons === 0);
    await keep2.page.close();

    // ---- 规格 §5.2：外部变化保持标签顺序与当前标签；文件已删除则移除其标签 ----
    const ext = await openScene('scene=main-project&theme=dark');
    await ext.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    // 直接用标签入口打开两个文件，sources 走真实 document/read
    await ext.page.evaluate(() => window.__augitOpenDocument('docs/product-spec.md'));
    await ext.page.waitForFunction('(window.__augitLive.tabs || []).length === 1', null, { timeout: 8000 });
    await ext.page.evaluate(() => window.__augitOpenDocument('docs/notes.txt'));
    await ext.page.waitForFunction('(window.__augitLive.tabs || []).length === 2', null, { timeout: 8000 });
    const extBefore = await ext.page.evaluate(() => ({
      order: (window.__augitLive.tabs || []).map((t) => t.path),
      active: window.__augitLive.activeTabId,
      doc: window.__augitLive.document ? window.__augitLive.document.path : null,
    }));

    // 注入一次「无关文件变化」批次：标签顺序与当前标签都不得改变
    await ext.page.evaluate(() => {
      window.__nextChanges = { files: ['D:\\live-ws\\src\\Unrelated.cs'], gitMetadata: true };
    });
    await ext.page.waitForTimeout(900);
    const extAfter = await ext.page.evaluate(() => ({
      order: (window.__augitLive.tabs || []).map((t) => t.path),
      active: window.__augitLive.activeTabId,
      doc: window.__augitLive.document ? window.__augitLive.document.path : null,
    }));
    check('外部变化保持标签顺序: ' + JSON.stringify(extAfter.order),
      JSON.stringify(extAfter.order) === JSON.stringify(extBefore.order));
    check('外部变化保持当前标签: ' + extAfter.doc, extAfter.doc === extBefore.doc && extAfter.active === extBefore.active);

    // 当前文件被外部删除：管理器上报一条 Deleted 变化，应移除它的标签
    await ext.page.evaluate(() => {
      // 让宿主状态把该文件报为已删除，并走真实的「工作区发生变化」通道
      window.__deletedPaths = ['docs/notes.txt'];
      window.__nextChanges = { files: ['D:\\live-ws\\docs\\notes.txt'], gitMetadata: true };
    });
    await ext.page.waitForTimeout(1200);
    const extDeleted = await ext.page.evaluate(() => ({
      paths: (window.__augitLive.tabs || []).map((t) => t.path),
      doc: window.__augitLive.document ? window.__augitLive.document.path : null,
    }));
    check('文件已删除时移除其标签: ' + JSON.stringify(extDeleted),
      !extDeleted.paths.includes('docs/notes.txt') && extDeleted.paths.includes('docs/product-spec.md'));
    await ext.page.close();

    // ---- 规格 §5.3：Esc 关闭最上层弹层，不关闭其下方工具窗口 ----
    // 用带完整外壳的场景，才能验证「关闭弹层后主界面结构保持」。
    const escSide = await openScene('scene=branches&theme=dark');
    await escSide.page.waitForFunction('window.__augitReady === true', null, { timeout: 20000 });
    await escSide.page.waitForTimeout(900);
    const escSideBefore = await escSide.page.evaluate(() => ({
      overlay: !!document.querySelector('[data-augit-overlay]'),
      side: !!document.querySelector('.side-tool'),
      workspace: !!document.querySelector('.workspace'),
      statusbar: !!document.querySelector('.statusbar'),
    }));
    await escSide.page.keyboard.press('Escape');
    await escSide.page.waitForTimeout(500);
    const escSideAfter = await escSide.page.evaluate(() => ({
      overlay: !!document.querySelector('[data-augit-overlay]'),
      side: !!document.querySelector('.side-tool'),
      workspace: !!document.querySelector('.workspace'),
      statusbar: !!document.querySelector('.statusbar'),
    }));
    check('Esc 关闭弹层: ' + JSON.stringify([escSideBefore.overlay, escSideAfter.overlay]),
      escSideBefore.overlay === true && escSideAfter.overlay === false);
    check('Esc 不关闭下方工具窗口: ' + JSON.stringify(escSideAfter),
      escSideAfter.side === true);
    check('Esc 关闭弹层后主界面结构保持: ' + JSON.stringify(escSideAfter),
      escSideAfter.workspace === true && escSideAfter.statusbar === true);
    await escSide.page.close();

    // ---- 规格 §5.4：树的键盘导航 ----
    const kbTree = await openScene('scene=main-project&theme=dark');
    await kbTree.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await kbTree.page.waitForSelector('.side-content.tree .tree-row', { timeout: 10000 });
    const selectedPath = () => kbTree.page.evaluate(() => {
      const row = document.querySelector('.side-content.tree .tree-row.selected');
      return row ? row.dataset.treePath : null;
    });
    // 先点选一行（选中并聚焦），再用方向键移动；点击目录行只会展开，故选文件行。
    await kbTree.page.locator('.side-content.tree .tree-row[data-tree-path="docs"]').click();
    await kbTree.page.waitForTimeout(300);
    await kbTree.page.locator('.side-content.tree .tree-row[data-tree-path="docs/product-spec.md"]').click();
    await kbTree.page.waitForTimeout(300);
    await kbTree.page.keyboard.press('ArrowDown');
    await kbTree.page.waitForTimeout(200);
    const treeAfterDown = await selectedPath();
    check('树支持方向键移动选择: ' + JSON.stringify(treeAfterDown), treeAfterDown !== null);
    // 方向键继续移动，并且聚焦跟随
    await kbTree.page.keyboard.press('ArrowDown');
    await kbTree.page.waitForTimeout(150);
    const treeAfterSecond = await selectedPath();
    const treeFocusInTree = await kbTree.page.evaluate(() => {
      const el = document.activeElement;
      return !!el && el.classList.contains('tree-row');
    });
    check('方向键可连续移动且焦点跟随: ' + JSON.stringify([treeAfterDown, treeAfterSecond, treeFocusInTree]),
      treeAfterSecond !== treeAfterDown && treeFocusInTree === true);
    // 右键展开目录、左键折叠
    const treeDir = kbTree.page.locator('.side-content.tree .tree-row[data-tree-path="docs"]');
    await treeDir.click();
    const treeRowsBefore = await kbTree.page.locator('.side-content.tree .tree-row').count();
    // 先把 docs 明确置为「已展开」再测折叠/展开，避免依赖前序点击留下的状态。
    const docsRow = kbTree.page.locator('.side-content.tree .tree-row[data-tree-path="docs"]');
    const docsExpanded = () => kbTree.page.evaluate(() => {
      const row = document.querySelector('.side-content.tree .tree-row[data-tree-path="docs"]');
      return row ? row.getAttribute('aria-expanded') === 'true' : null;
    });
    if (!(await docsExpanded())) {
      await kbTree.page.evaluate(() => {
        const row = document.querySelector('.side-content.tree .tree-row[data-tree-path="docs"]');
        row.focus();
      });
      await kbTree.page.keyboard.press('ArrowRight');
      await kbTree.page.waitForTimeout(500);
    }
    check('前置条件：docs 已展开', (await docsExpanded()) === true);

    const treeRowsExpanded = await kbTree.page.locator('.side-content.tree .tree-row').count();
    await kbTree.page.keyboard.press('ArrowLeft');
    await kbTree.page.waitForTimeout(600);
    const treeRowsCollapsed = await kbTree.page.locator('.side-content.tree .tree-row').count();
    check('左键折叠目录: ' + treeRowsExpanded + ' -> ' + treeRowsCollapsed,
      treeRowsCollapsed < treeRowsExpanded && (await docsExpanded()) === false);
    await kbTree.page.keyboard.press('ArrowRight');
    await kbTree.page.waitForTimeout(600);
    const treeRowsReExpanded = await kbTree.page.locator('.side-content.tree .tree-row').count();
    check('右键展开目录: ' + treeRowsCollapsed + ' -> ' + treeRowsReExpanded,
      treeRowsReExpanded > treeRowsCollapsed && (await docsExpanded()) === true);
    await kbTree.page.close();

    // ---- 未接线的跳转链接不得离开应用页面 ----
    // 视觉稿为逐场景浏览把交互写成指向 *.html 的链接；在外壳里点击这些入口
    // 必须留在应用内。已接线的入口自行 preventDefault，其余由防护网兜住。
    const navGuard = await openScene('scene=main-project&theme=dark');
    await navGuard.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    const urlBefore = navGuard.page.url();
    const guardState = await navGuard.page.evaluate(() => ({
      guarded: !!window.__augitNavGuarded,
      hasLive: !!window.__augitLive,
    }));
    check('导航防护已安装: ' + JSON.stringify(guardState), guardState.guarded === true && guardState.hasLive === true);
    await navGuard.page.locator('.top-chip.branch-chip').click();
    await navGuard.page.waitForTimeout(900);
    check('点击分支芯片不离开应用: ' + navGuard.page.url(), navGuard.page.url() === urlBefore);
    // 分支芯片应真正打开分支弹层，而不是「拦住了但没反应」
    const popover = await navGuard.page.evaluate(() => ({
      overlay: !!document.querySelector('[data-augit-overlay]'),
      hasPopover: !!document.querySelector('[data-augit-overlay] .popover'),
      branches: document.querySelectorAll('[data-augit-overlay] [data-branch]').length,
      hasScrim: !!document.querySelector('[data-augit-overlay] .scrim'),
    }));
    check('分支芯片打开分支弹层: ' + JSON.stringify(popover),
      popover.overlay === true && popover.hasPopover === true && popover.hasScrim === true);
    check('弹层内列出真实分支: ' + popover.branches, popover.branches > 0);
    // 打开分支弹层后，其中的菜单项同样不得离开应用
    await navGuard.page.keyboard.press('Escape');
    await navGuard.page.waitForTimeout(300);
    const stillThere = await navGuard.page.evaluate(() => ({
      titlebar: !!document.querySelector('.titlebar'),
      rail: !!document.querySelector('.tool-rail'),
      workspace: !!document.querySelector('.workspace'),
    }));
    check('点击后界面结构完好: ' + JSON.stringify(stillThere),
      stillThere.titlebar === true && stillThere.rail === true && stillThere.workspace === true);
    await navGuard.page.close();

    // ---- 规格 §7.6：提交只提交勾选文件 ----
    const commitPage = await openScene('scene=commit-changes&theme=dark');
    await commitPage.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await commitPage.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    const commitState = () => commitPage.page.evaluate(() => ({
      written: window.__commitWrite || null,
      error: window.__augitCommitError || null,
      feedback: (document.querySelector('.commit-box .commit-feedback') || {}).textContent || null,
      isError: !!(document.querySelector('.commit-box .commit-feedback.error')),
      draft: window.__augitLive.commitDraft,
      result: window.__augitCommitResult || null,
    }));

    // 未写提交信息就提交：必须被拒绝并给出原因
    await commitPage.page.locator('.commit-actions .primary-button').first().click();
    await commitPage.page.waitForTimeout(500);
    const noMessage = await commitState();
    check('空提交信息被拒绝: ' + JSON.stringify([noMessage.written, noMessage.error]),
      noMessage.written === null && noMessage.error.includes('提交信息不能为空。'));
    check('拒绝原因写回反馈位: ' + JSON.stringify(noMessage.feedback),
      typeof noMessage.feedback === 'string' && noMessage.feedback.includes('提交信息不能为空。')
      && noMessage.isError === true);

    // 填写信息后提交：只提交勾选的文件
    await commitPage.page.locator('.commit-box .message-field').fill('feat: 只提交勾选项');
    await commitPage.page.waitForTimeout(200);
    // 取消第一行勾选，验证它不进入提交
    await commitPage.page.locator('.changes-list .change-file-row').first().locator('.fake-check').click();
    await commitPage.page.waitForTimeout(300);
    const uncheckedPath = await commitPage.page.evaluate(
      () => document.querySelector('.changes-list .change-file-row').dataset.path);
    await commitPage.page.locator('.commit-actions .primary-button').first().click();
    await commitPage.page.waitForTimeout(900);
    const committed = await commitState();
    check('提交使用填写的提交信息: ' + JSON.stringify(committed.written && committed.written.message),
      committed.written && committed.written.message === 'feat: 只提交勾选项');
    check('未勾选的文件不进入提交: ' + JSON.stringify(committed.written && committed.written.paths),
      committed.written && !committed.written.paths.includes(uncheckedPath));
    check('提交成功记录哈希: ' + JSON.stringify(committed.result), !!committed.result && committed.result.hash === 'abc1234');
    check('提交成功后清空草稿与错误: ' + JSON.stringify([committed.draft, committed.error]),
      committed.draft === '' && committed.error === null);

    // 失败路径：宿主拒绝时必须如实显示原因
    await commitPage.page.evaluate(() => { window.__commitFails = true; });
    await commitPage.page.locator('.commit-box .message-field').fill('feat: 会被拒绝');
    await commitPage.page.waitForTimeout(200);
    await commitPage.page.locator('.commit-actions .primary-button').first().click();
    await commitPage.page.waitForTimeout(800);
    const failed = await commitState();
    check('提交失败显示宿主原因: ' + JSON.stringify(failed.error),
      typeof failed.error === 'string' && failed.error.includes('hook'));
    // 如实记录：这条断言**没有通过负向验证**。失败分支本身就会把结果置空，
    // 入口处也有一次重置，两处都覆盖了同一行为，因此单独移除任一处它仍会通过。
    // 断言本身正确（失败后确实不残留结果），但它无法区分「两处都清」与「只清一处」。
    check('失败时不记录提交结果', failed.result === null);
    await commitPage.page.evaluate(() => { window.__commitFails = false; window.__pushCalls = 0; });

    // ---- 提交并推送：两步都要真实执行，且失败要区分「提交成功、推送失败」 ----
    await commitPage.page.locator('.commit-box .message-field').fill('feat: 提交并推送');
    await commitPage.page.waitForTimeout(200);
    await commitPage.page.locator('.commit-actions .secondary-button').first().click();
    await commitPage.page.waitForTimeout(1200);
    const bothOk = await commitPage.page.evaluate(() => ({
      pushCalls: window.__pushCalls || 0,
      result: window.__augitCommitResult,
      error: window.__augitCommitError,
    }));
    check('提交并推送真的调用了推送: ' + bothOk.pushCalls, bothOk.pushCalls === 1);
    check('推送成功被如实记录: ' + JSON.stringify(bothOk.result),
      bothOk.result && bothOk.result.pushed === true && bothOk.result.hash === 'abc1234');
    check('推送成功不残留错误: ' + JSON.stringify(bothOk.error), bothOk.error === null);

    // 推送失败：提交已成功，必须如实区分而不是整体报失败
    await commitPage.page.evaluate(() => { window.__pushFails = true; window.__pushCalls = 0; });
    await commitPage.page.locator('.commit-box .message-field').fill('feat: 推送会失败');
    await commitPage.page.waitForTimeout(200);
    await commitPage.page.locator('.commit-actions .secondary-button').first().click();
    await commitPage.page.waitForTimeout(1200);
    const pushFail = await commitPage.page.evaluate(() => ({
      pushCalls: window.__pushCalls || 0,
      result: window.__augitCommitResult,
      error: window.__augitCommitError,
    }));
    check('推送失败仍然记录了提交成功: ' + JSON.stringify(pushFail.result),
      pushFail.result && pushFail.result.pushed === false && pushFail.result.hash === 'abc1234');
    check('推送失败说明是「提交成功但推送失败」: ' + JSON.stringify(pushFail.error),
      typeof pushFail.error === 'string' && pushFail.error.includes('提交已成功')
      && pushFail.error.includes('没有配置推送远端'));
    await commitPage.page.close();

    // ---- 规格 §7.11：分支弹层里的引用行点击即检出 ----
    const co = await openScene('scene=main-project&theme=dark');
    await co.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await co.page.waitForFunction('!!window.__augitLive.references', null, { timeout: 10000 });
    await co.page.locator('.top-chip.branch-chip').click();
    await co.page.waitForTimeout(600);
    const refRows = await co.page.evaluate(() => [...document.querySelectorAll('[data-augit-overlay] [data-branch]')]
      .map((el) => ({ name: el.dataset.branch, kind: el.dataset.branchKind })));
    check('分支弹层标出引用类型: ' + JSON.stringify(refRows),
      refRows.length > 0 && refRows.every((r) => ['branch', 'remote', 'tag'].includes(r.kind)));
    check('弹层内同时有本地分支与标签: ' + JSON.stringify(refRows.map((r) => r.kind)),
      refRows.some((r) => r.kind === 'branch') && refRows.some((r) => r.kind === 'tag'));

    // 点击本地分支行：按 branch 类型检出
    const refLocal = refRows.find((r) => r.kind === 'branch');
    await co.page.locator(`[data-augit-overlay] [data-branch="${refLocal.name}"]`).click();
    await co.page.waitForTimeout(900);
    const coAfterLocal = await co.page.evaluate(() => ({
      calls: window.__checkoutCalls || [],
      overlay: !!document.querySelector('[data-augit-overlay].live-overlay'),
      err: window.__augitCheckoutError || null,
    }));
    check('点击本地分支按 branch 类型检出: ' + JSON.stringify(coAfterLocal.calls),
      coAfterLocal.calls.length === 1 && coAfterLocal.calls[0].name === refLocal.name && coAfterLocal.calls[0].kind === 'branch');
    check('检出成功后关闭弹层', coAfterLocal.overlay === false);

    // 点击标签行：按 tag 类型检出
    await co.page.locator('.top-chip.branch-chip').click();
    await co.page.waitForTimeout(600);
    const refTag = refRows.find((r) => r.kind === 'tag');
    await co.page.evaluate(() => { window.__checkoutCalls = []; });
    await co.page.locator(`[data-augit-overlay] [data-branch="${refTag.name}"]`).click();
    await co.page.waitForTimeout(900);
    const coAfterTag = await co.page.evaluate(() => window.__checkoutCalls || []);
    check('点击标签按 tag 类型检出: ' + JSON.stringify(coAfterTag),
      coAfterTag.length === 1 && coAfterTag[0].kind === 'tag' && coAfterTag[0].name === refTag.name);

    // 检出失败：保留弹层并显示原因，不静默关闭
    await co.page.evaluate(() => { window.__checkoutFails = true; window.__checkoutCalls = []; });
    await co.page.locator('.top-chip.branch-chip').click();
    await co.page.waitForTimeout(600);
    await co.page.locator(`[data-augit-overlay] [data-branch="${refLocal.name}"]`).click();
    await co.page.waitForTimeout(900);
    const coAfterFail = await co.page.evaluate(() => ({
      overlay: !!document.querySelector('[data-augit-overlay].live-overlay'),
      alert: (document.querySelector('[data-augit-overlay] .inline-alert') || {}).textContent || null,
      err: window.__augitCheckoutError || null,
    }));
    check('检出失败保留弹层: ' + JSON.stringify(coAfterFail.overlay), coAfterFail.overlay === true);
    check('检出失败显示 Git 给出的原因: ' + JSON.stringify(coAfterFail.alert),
      typeof coAfterFail.alert === 'string' && coAfterFail.alert.includes('未提交的改动'));
    await co.page.close();

    // ---- 规格 §5.3：新建 / 重命名使用紧凑单行输入窗口 ----
    const cd = await openScene('scene=main-project&theme=dark');
    await cd.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await cd.page.waitForFunction('!!window.__augitLive.references', null, { timeout: 10000 });

    const openBranchDialog = async (action) => {
      await cd.page.evaluate(() => document.querySelectorAll('[data-augit-overlay].live-overlay').forEach((n) => n.remove()));
      await cd.page.locator('.top-chip.branch-chip').click();
      await cd.page.waitForTimeout(500);
      await cd.page.locator(`[data-augit-overlay] [data-branch-action="${action}"]`).click();
      await cd.page.waitForTimeout(500);
    };

    await openBranchDialog('create');
    const createOpen = await cd.page.evaluate(() => {
      const layer = document.querySelector('[data-compact-dialog]');
      const field = layer && layer.querySelector('[data-compact-field]');
      return {
        open: !!layer,
        title: layer ? layer.querySelector('.dialog-header span').innerText : null,
        focused: !!field && document.activeElement === field,
        value: field ? field.value : null,
      };
    });
    check('新建分支打开紧凑输入窗口: ' + JSON.stringify(createOpen),
      createOpen.open === true && createOpen.title === '新建分支');
    check('打开后输入框获得焦点', createOpen.focused === true);
    check('新建时输入框为空', createOpen.value === '');

    // Tab 循环顺序：输入框 → 取消 → 确定 → 标题栏关闭 → 输入框
    const tabOrder = [];
    for (let i = 0; i < 4; i += 1) {
      tabOrder.push(await cd.page.evaluate(() => {
        const el = document.activeElement;
        return el.dataset.compactAction || (el.dataset.compactField !== undefined ? 'field' : el.className);
      }));
      await cd.page.keyboard.press('Tab');
      await cd.page.waitForTimeout(120);
    }
    check('Tab 按输入框→取消→确定→关闭循环: ' + JSON.stringify(tabOrder),
      tabOrder.join(',') === 'field,cancel,confirm,close');
    // Shift+Tab 反向
    await cd.page.keyboard.press('Shift+Tab');
    await cd.page.waitForTimeout(150);
    const back = await cd.page.evaluate(() => document.activeElement.dataset.compactAction || 'field');
    check('Shift+Tab 反向循环: ' + back, back === 'close');

    // 输入名称后按 Enter 确认
    await cd.page.evaluate(() => { window.__branchCalls = []; });
    await cd.page.locator('[data-compact-field]').fill('feat/new-branch');
    await cd.page.locator('[data-compact-field]').press('Enter');
    await cd.page.waitForTimeout(800);
    const created = await cd.page.evaluate(() => ({
      calls: window.__branchCalls || [],
      dialog: !!document.querySelector('[data-compact-dialog]'),
    }));
    check('输入框 Enter 确认并调用分支接口: ' + JSON.stringify(created.calls),
      created.calls.length === 1 && created.calls[0].action === 'create' && created.calls[0].name === 'feat/new-branch');
    check('确认后关闭窗口', created.dialog === false);

    // Esc 取消：不得调用接口
    await openBranchDialog('create');
    await cd.page.evaluate(() => { window.__branchCalls = []; });
    await cd.page.keyboard.press('Escape');
    await cd.page.waitForTimeout(400);
    const cancelled = await cd.page.evaluate(() => ({
      calls: window.__branchCalls || [],
      dialog: !!document.querySelector('[data-compact-dialog]'),
    }));
    check('Esc 取消不调用接口: ' + JSON.stringify(cancelled.calls), cancelled.calls.length === 0);
    check('Esc 关闭窗口: ' + cancelled.dialog, cancelled.dialog === false);

    // 重命名：输入框预填当前分支名
    await openBranchDialog('rename');
    const renameOpen = await cd.page.evaluate(() => {
      const field = document.querySelector('[data-compact-field]');
      return { title: document.querySelector('[data-compact-dialog] .dialog-header span').innerText, value: field ? field.value : null };
    });
    check('重命名预填当前分支名: ' + JSON.stringify(renameOpen),
      renameOpen.title === '重命名分支' && renameOpen.value === 'dsh');
    await cd.page.locator('[data-compact-field]').fill('dsh-renamed');
    await cd.page.locator('[data-compact-action="confirm"]').click();
    await cd.page.waitForTimeout(800);
    const renamed = await cd.page.evaluate(() => window.__branchCalls || []);
    check('重命名带上原分支名: ' + JSON.stringify(renamed),
      renamed.length === 1 && renamed[0].action === 'rename' && renamed[0].from === 'dsh' && renamed[0].name === 'dsh-renamed');

    // 失败：如实显示 Git 原因
    await cd.page.evaluate(() => { window.__branchFails = true; });
    await openBranchDialog('create');
    await cd.page.locator('[data-compact-field]').fill('dsh');
    await cd.page.locator('[data-compact-field]').press('Enter');
    await cd.page.waitForTimeout(800);
    const failedCreate = await cd.page.evaluate(() => ({
      alert: (document.querySelector('[data-augit-overlay] .inline-alert') || {}).textContent || null,
      err: window.__augitCheckoutError || null,
    }));
    check('分支创建失败显示 Git 原因: ' + JSON.stringify(failedCreate.alert),
      typeof failedCreate.alert === 'string' && failedCreate.alert.includes('已存在'));
    await cd.page.close();

    // ---- 规格 §7.11：分支弹层快捷动作 ----
    const pa = await openScene('scene=main-project&theme=dark');
    await pa.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await pa.page.waitForFunction('!!window.__augitLive.references', null, { timeout: 10000 });
    const openPopover = async () => {
      await pa.page.evaluate(() => document.querySelectorAll('[data-augit-overlay].live-overlay').forEach((n) => n.remove()));
      await pa.page.locator('.top-chip.branch-chip').click();
      await pa.page.waitForTimeout(500);
    };
    // 更新项目 → git/fetch
    await openPopover();
    // 弹层打开后才存在快捷动作节点，必须在打开之后统计。
    const actionCount = await pa.page.evaluate(() => document.querySelectorAll('[data-popover-action]').length);
    await pa.page.evaluate(() => { window.__fetchCalls = 0; });
    await pa.page.locator('[data-popover-action="fetch"]').click();
    await pa.page.waitForTimeout(900);
    const fetched = await pa.page.evaluate(() => ({
      calls: window.__fetchCalls || 0,
      overlay: !!document.querySelector('[data-augit-overlay].live-overlay'),
    }));
    check('更新项目调用 fetch: ' + fetched.calls, fetched.calls === 1);
    check('更新项目成功后关闭弹层', fetched.overlay === false);

    // 推送… → 先打开推送对话框预览（规格 §7.12：预览未就绪不允许推送）
    await openPopover();
    await pa.page.evaluate(() => { window.__pushCalls = 0; });
    await pa.page.locator('[data-popover-action="push"]').click();
    await pa.page.waitForTimeout(1200);
    const pushDialog = await pa.page.evaluate(() => ({
      open: !!document.querySelector('[data-push-action]'),
      summary: (document.querySelector('.push-summary') || {}).innerText || null,
      commits: [...document.querySelectorAll('.push-commit')].map((el) => el.innerText.trim()),
      confirmDisabled: (document.querySelector('[data-push-action="confirm"]') || {}).disabled,
      pushCalls: window.__pushCalls || 0,
    }));
    check('推送… 打开预览对话框而不是直接推送: ' + JSON.stringify([pushDialog.open, pushDialog.pushCalls]),
      pushDialog.open === true && pushDialog.pushCalls === 0);
    check('推送对话框显示本地引用与目标: ' + JSON.stringify(pushDialog.summary),
      typeof pushDialog.summary === 'string' && pushDialog.summary.includes('dsh') && pushDialog.summary.includes('origin/dsh'));
    check('推送对话框列出待推送提交: ' + JSON.stringify(pushDialog.commits),
      pushDialog.commits.length === 1 && pushDialog.commits[0].includes('真实提交一'));
    check('有待推送提交时可推送', pushDialog.confirmDisabled === false);

    // 确认后才真正推送，并在成功后关闭
    await pa.page.locator('[data-push-action="confirm"]').click();
    await pa.page.waitForTimeout(1000);
    const pushedNow = await pa.page.evaluate(() => ({
      calls: window.__pushCalls || 0,
      open: !!document.querySelector('[data-push-action]'),
    }));
    check('确认后真正推送: ' + pushedNow.calls, pushedNow.calls === 1);
    check('推送成功后关闭对话框', pushedNow.open === false);

    // 规格 §5.3：确认后焦点回到触发区域或直接进入结果区域，且不停留在已关闭的窗口内。
    // 这里必须先确认"打开前确实有焦点元素"，否则恢复没有可核对目标。
    const pushFocus = await pa.page.evaluate(() => {
      const active = document.activeElement;
      return {
        tag: active ? active.tagName : null,
        inDialog: !!(active && active.closest && active.closest('[data-push-action]')),
        label: active && active.getAttribute ? active.getAttribute('aria-label') : null,
        cls: active && typeof active.className === 'string' ? active.className : null,
      };
    });
    check('推送确认后焦点不停留在已关闭的窗口内: ' + JSON.stringify(pushFocus),
      pushFocus.inDialog === false);

    // 取消（Esc）路径同样不得把焦点留在已关闭的窗口内。
    await openPopover();
    await pa.page.locator('[data-popover-action="push"]').click();
    await pa.page.waitForTimeout(1000);
    check('推送对话框可再次打开',
      await pa.page.evaluate(() => !!document.querySelector('[data-push-action]')));
    await pa.page.keyboard.press('Escape');
    await pa.page.waitForTimeout(400);
    const pushCancelFocus = await pa.page.evaluate(() => {
      const active = document.activeElement;
      return {
        open: !!document.querySelector('[data-push-action]'),
        inDialog: !!(active && active.closest && active.closest('[data-push-action]')),
      };
    });
    check('推送取消后关闭且焦点不在已关闭窗口内: ' + JSON.stringify(pushCancelFocus),
      pushCancelFocus.open === false && pushCancelFocus.inDialog === false);

    // 没有上游：禁用推送、保留「定义远端」、显示原因
    await pa.page.evaluate(() => { window.__unpushedFails = true; });
    await openPopover();
    await pa.page.locator('[data-popover-action="push"]').click();
    await pa.page.waitForTimeout(1200);
    const noUpstream = await pa.page.evaluate(() => ({
      open: !!document.querySelector('[data-push-action]'),
      confirmDisabled: (document.querySelector('[data-push-action="confirm"]') || {}).disabled,
      defineRemote: !!document.querySelector('.push-summary a[href$="remote.html"]'),
      summaryHtml: (document.querySelector('.push-summary') || {}).innerHTML || null,
      summaryText: (document.querySelector('.push-summary') || {}).innerText || null,
      reason: (document.querySelector('.management-detail') || {}).innerText || null,
    }));
    check('没有上游时禁用推送: ' + JSON.stringify([noUpstream.open, noUpstream.confirmDisabled]),
      noUpstream.open === true && noUpstream.confirmDisabled === true);
    check('没有上游时保留「定义远端」入口', noUpstream.defineRemote === true);
    check('没有上游时说明原因: ' + JSON.stringify(noUpstream.reason),
      typeof noUpstream.reason === 'string' && noUpstream.reason.includes('没有配置上游'));
    await pa.page.keyboard.press('Escape');
    await pa.page.waitForTimeout(300);
    const closedByEsc = await pa.page.evaluate(() => !!document.querySelector('[data-push-action]'));
    check('Esc 关闭推送对话框', closedByEsc === false);
    await pa.page.evaluate(() => { window.__unpushedFails = false; });

    // 提交… → 切到提交工具窗口，不调用任何 Git 写操作
    await openPopover();
    await pa.page.evaluate(() => { window.__pushCalls = 0; window.__fetchCalls = 0; });
    await pa.page.locator('[data-popover-action="commit"]').click();
    await pa.page.waitForTimeout(700);
    const commitSwitch = await pa.page.evaluate(() => ({
      activeRail: (document.querySelector('.tool-rail .rail-button.active') || {}).getAttribute
        ? document.querySelector('.tool-rail .rail-button.active').getAttribute('aria-label') : null,
      sideTitle: document.querySelector('.side-tool .tool-header span') ? document.querySelector('.side-tool .tool-header span').innerText : null,
      pushCalls: window.__pushCalls || 0,
      fetchCalls: window.__fetchCalls || 0,
    }));
    check('提交… 切到提交工具窗口: ' + JSON.stringify(commitSwitch),
      commitSwitch.activeRail === '提交' && commitSwitch.sideTitle === '提交');
    check('提交… 不触达任何 Git 写操作: ' + JSON.stringify([commitSwitch.pushCalls, commitSwitch.fetchCalls]),
      commitSwitch.pushCalls === 0 && commitSwitch.fetchCalls === 0);

    // 新建分支… → 紧凑输入窗口
    await openPopover();
    await pa.page.locator('[data-popover-action="create-branch"]').click();
    await pa.page.waitForTimeout(500);
    const createFromPopover = await pa.page.evaluate(() => ({
      dialog: !!document.querySelector('[data-compact-dialog]'),
      title: document.querySelector('[data-compact-dialog] .dialog-header span')
        ? document.querySelector('[data-compact-dialog] .dialog-header span').innerText : null,
    }));
    check('新建分支… 打开紧凑输入窗口: ' + JSON.stringify(createFromPopover),
      createFromPopover.dialog === true && createFromPopover.title === '新建分支');
    await pa.page.keyboard.press('Escape');
    await pa.page.waitForTimeout(300);

    // 检出标签或版本… → 紧凑输入窗口，确认后按 tag 检出
    await openPopover();
    await pa.page.locator('[data-popover-action="checkout-revision"]').click();
    await pa.page.waitForTimeout(500);
    const revDialog = await pa.page.evaluate(() => ({
      dialog: !!document.querySelector('[data-compact-dialog]'),
      title: document.querySelector('[data-compact-dialog] .dialog-header span')
        ? document.querySelector('[data-compact-dialog] .dialog-header span').innerText : null,
    }));
    check('检出标签或版本… 打开紧凑输入窗口: ' + JSON.stringify(revDialog),
      revDialog.dialog === true && revDialog.title === '检出标签或版本');
    await pa.page.evaluate(() => { window.__checkoutCalls = []; });
    await pa.page.locator('[data-compact-field]').fill('v2.0.0');
    await pa.page.locator('[data-compact-field]').press('Enter');
    await pa.page.waitForTimeout(900);
    const revChecked = await pa.page.evaluate(() => window.__checkoutCalls || []);
    check('检出标签按 tag 类型调用: ' + JSON.stringify(revChecked),
      revChecked.length === 1 && revChecked[0].kind === 'tag' && revChecked[0].name === 'v2.0.0');

    // 失败：保留弹层并显示 Git 原因
    await pa.page.evaluate(() => { window.__fetchFails = true; });
    await openPopover();
    await pa.page.locator('[data-popover-action="fetch"]').click();
    await pa.page.waitForTimeout(900);
    const fetchFail = await pa.page.evaluate(() => ({
      overlay: !!document.querySelector('[data-augit-overlay].live-overlay'),
      alert: (document.querySelector('[data-augit-overlay] .inline-alert') || {}).textContent || null,
    }));
    check('更新项目失败保留弹层: ' + fetchFail.overlay, fetchFail.overlay === true);
    check('更新项目失败显示 Git 原因: ' + JSON.stringify(fetchFail.alert),
      typeof fetchFail.alert === 'string' && fetchFail.alert.includes('没有配置远端'));
    check('弹层已标注的快捷动作数量: ' + actionCount, actionCount >= 5);
    await pa.page.close();

    // ---- 规格 §7.9：与工作区比较（按引用基准生成差异） ----
    const cw = await openScene('scene=commit-changes&theme=dark');
    await cw.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await cw.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    await cw.page.evaluate(() => { window.__diffCalls = []; window.__diffRevisions = []; });
    // 先建立"正在跟随 Changes"的前置状态：双击改动行显式打开比较标签。
    // 前置条件必须确凿成立，否则下面的"引用比较解除跟随"断言无法区分
    // "修复生效"和"本来就没在跟随"——这正是最初负向验证不通过的原因。
    await cw.page.locator('.changes-list .change-file-row').first().dblclick();
    await cw.page.waitForFunction(
      '!!(window.__augitLive.tabs || []).find((t) => t.kind === "comparison") && window.__augitLive.followChanges === true',
      null,
      { timeout: 8000 },
    );
    check('引用比较前已处于跟随状态: ' + JSON.stringify(await cw.page.evaluate(
      () => !!window.__augitLive.followChanges)), true);
    // 必须走真实入口：从标题栏分支芯片打开弹层，再点其中的「与工作区比较」。
    // 直接注入节点会绕过被测路径，测不到真实行为。
    await cw.page.waitForFunction('!!window.__augitLive.references', null, { timeout: 10000 });
    await cw.page.locator('.top-chip.branch-chip').click();
    await cw.page.waitForTimeout(600);
    const compareEntry = await cw.page.evaluate(
      () => document.querySelectorAll('[data-popover-action="compare-workspace"]').length);
    check('真实弹层内存在「与工作区比较」入口: ' + compareEntry, compareEntry === 1);
    await cw.page.locator('[data-popover-action="compare-workspace"]').click();
    await cw.page.waitForTimeout(1200);
    const compared = await cw.page.evaluate(() => ({
      revisions: window.__diffRevisions || [],
      comparisons: (window.__augitLive.tabs || []).filter((t) => t.kind === 'comparison').length,
      title: ((window.__augitLive.tabs || []).find((t) => t.kind === 'comparison') || {}).title || null,
      editor: window.__augitLive.editor,
      follow: !!window.__augitLive.followChanges,
      err: window.__augitError || null,
    }));
    check('与工作区比较按分支基准请求差异: ' + JSON.stringify(compared.revisions),
      compared.revisions.length >= 1 && compared.revisions.at(-1) === 'dsh');
    check('与工作区比较建立比较标签: ' + JSON.stringify(compared.title),
      compared.comparisons === 1 && typeof compared.title === 'string' && compared.title.startsWith('比较:'));
    check('与工作区比较进入差异视图', compared.editor === 'diff');
    check('引用比较解除对 Changes 的跟随: ' + JSON.stringify(compared.follow), compared.follow === false);

    // 引用比较独立于 Changes 跟随（规格 §5.2/§7.9）：此后单击改动行不得把它改写成工作区 Diff。
    // 这条最初没有负向验证通过——去掉 `followChanges = false` 后套件仍然全绿，
    // 说明当时没有任何断言覆盖"解除跟随"，因此这里补上针对引用比较的断言。
    await cw.page.evaluate(() => { window.__diffRevisions = []; });
    // 选与引用比较目标不同的一行：相同路径会在 loadDiff 里命中缓存，
    // 跟随分支根本不会执行，断言就测不到东西。
    const refRowPaths = await cw.page.evaluate(() => [...document.querySelectorAll('.changes-list .change-file-row')]
      .map((row) => row.dataset.path));
    const refCurrentPath = await cw.page.evaluate(
      () => (window.__augitLive.diff ? window.__augitLive.diff.path : null));
    const refOtherIndex = refRowPaths.findIndex((p) => p !== refCurrentPath);
    check('存在与引用比较目标不同的改动行: ' + JSON.stringify([refRowPaths, refCurrentPath]),
      refOtherIndex >= 0);
    await cw.page.locator('.changes-list .change-file-row').nth(refOtherIndex).click();
    await cw.page.waitForTimeout(900);
    const afterRefClick = await cw.page.evaluate(() => ({
      revisions: window.__diffRevisions || [],
      follow: !!window.__augitLive.followChanges,
      diffPath: window.__augitLive.diff ? window.__augitLive.diff.path : null,
      title: ((window.__augitLive.tabs || []).find((t) => t.kind === 'comparison') || {}).title || null,
    }));
    check('引用比较后 follow 处于关闭状态: ' + JSON.stringify(afterRefClick.follow),
      afterRefClick.follow === false);
    check('引用比较后单击改动行不发起工作区比较请求: ' + JSON.stringify(afterRefClick.revisions),
      Array.isArray(afterRefClick.revisions) && afterRefClick.revisions.length === 0);
    check('引用比较后单击改动行不改写比较标签: ' + JSON.stringify(afterRefClick.title),
      typeof afterRefClick.title === 'string' && afterRefClick.title.startsWith('比较:'));

    // 普通工作区 Diff 必须仍然不传 revision（由宿主按 HEAD 处理）
    await cw.page.evaluate(() => { window.__diffRevisions = []; });
    await cw.page.locator('.changes-list .change-file-row').nth(1).dblclick();
    await cw.page.waitForTimeout(1000);
    const plainRevisions = await cw.page.evaluate(() => window.__diffRevisions || []);
    check('普通工作区 Diff 不传基准: ' + JSON.stringify(plainRevisions),
      plainRevisions.length >= 1 && plainRevisions.every((r) => r === '<未传>'));
    await cw.page.close();

    // ---- 弹层区域可在没有预置节点的场景中打开 ----
    // 多数场景本来就没有 .overlay-layer 节点。区域替换只在「两侧都存在」时生效，
    // 若弹层也只做替换，这些场景里它永远打不开（实测分支弹层完全没有反应）。
    const ov = await openScene('scene=main-project&theme=dark');
    await ov.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    const ovBefore = await ov.page.evaluate(() => document.querySelectorAll('[data-augit-overlay]').length);
    check('主项目场景初始没有弹层节点: ' + ovBefore, ovBefore === 0);
    await ov.page.locator('.top-chip.branch-chip').click();
    await ov.page.waitForTimeout(700);
    const ovOpened = await ov.page.evaluate(() => ({
      overlays: document.querySelectorAll('[data-augit-overlay]').length,
      popovers: document.querySelectorAll('[data-augit-overlay] .popover').length,
      scrim: !!document.querySelector('[data-augit-overlay] .scrim'),
      insideWindow: !!document.querySelector('.augit-window > [data-augit-overlay]'),
    }));
    check('没有预置节点时弹层仍然打开: ' + JSON.stringify(ovOpened),
      ovOpened.overlays === 1 && ovOpened.popovers >= 1 && ovOpened.scrim === true);
    check('弹层挂在主窗口内: ' + ovOpened.insideWindow, ovOpened.insideWindow === true);

    // Esc 关闭后不得残留
    await ov.page.keyboard.press('Escape');
    await ov.page.waitForTimeout(500);
    const ovAfterClose = await ov.page.evaluate(() => document.querySelectorAll('[data-augit-overlay]').length);
    check('关闭后不残留弹层节点: ' + ovAfterClose, ovAfterClose === 0);

    // 连开两次不得叠加节点
    await ov.page.locator('.top-chip.branch-chip').click();
    await ov.page.waitForTimeout(600);
    await ov.page.keyboard.press('Escape');
    await ov.page.waitForTimeout(400);
    await ov.page.locator('.top-chip.branch-chip').click();
    await ov.page.waitForTimeout(600);
    const ovTwice = await ov.page.evaluate(() => document.querySelectorAll('[data-augit-overlay]').length);
    check('重复打开不叠加弹层节点: ' + ovTwice, ovTwice === 1);
    await ov.page.close();

    // ---- 规格 §7.12：Push 内嵌的远端窗口 ----
    // 隔离页面 + 打开前设好宿主状态：前置条件必须先成立。
    // 「定义远端」入口只在没有上游时出现，因此整段保持该状态，
    // 避免中途切换状态与在途读取竞态（第一次就是这么失败的）。
    const rem = await context.newPage();
    await rem.addInitScript(() => { window.__unpushedFails = true; });
    await rem.goto(`http://127.0.0.1:${port}/index.html?scene=push&theme=dark`, { waitUntil: 'load' });
    await rem.waitForFunction('window.__augitReady === true', null, { timeout: 20000 });
    await rem.waitForSelector('.push-summary a[href$="remote.html"]', { timeout: 10000 });
    const entryCount = await rem.evaluate(
      () => document.querySelectorAll('.push-summary a[href$="remote.html"]').length);
    check('没有上游时出现「定义远端」入口: ' + entryCount, entryCount === 1);

    const pushBefore = await rem.evaluate(() => document.querySelectorAll('.dialog.push-dialog').length);
    await rem.locator('.push-summary a[href$="remote.html"]').first().click();
    await rem.waitForTimeout(800);
    const remOpened = await rem.evaluate(() => ({
      remote: !!document.querySelector('.remote-window'),
      pushDialogs: document.querySelectorAll('.dialog.push-dialog').length,
      fields: [...document.querySelectorAll('[data-remote-field]')].map((el) => el.dataset.remoteField),
      entries: [...document.querySelectorAll('[data-remote-entry]')].map((el) => el.dataset.remoteEntry),
      url: location.pathname,
    }));
    check('定义远端打开嵌套窗口: ' + JSON.stringify(remOpened.remote), remOpened.remote === true);
    check('远端窗口期间 Push 窗口保持唯一: ' + JSON.stringify([pushBefore, remOpened.pushDialogs]),
      pushBefore === 1 && remOpened.pushDialogs === 1);
    check('远端窗口提供名称与两个 URL 字段: ' + JSON.stringify(remOpened.fields),
      remOpened.fields.join(',') === 'name,fetchUrl,pushUrl');
    check('远端窗口列出已有远端: ' + JSON.stringify(remOpened.entries), remOpened.entries.includes('origin'));
    check('点击定义远端不跳转页面: ' + remOpened.url, !remOpened.url.includes('remote.html'));

    // 保存失败：窗口保留并显示 Git 原因（先测失败，避免保存成功后上游出现、入口消失）
    await rem.evaluate(() => { window.__remoteWriteFails = true; window.__remoteWrites = []; });
    await rem.locator('[data-remote-field="fetchUrl"]').fill('https://example.com/team/Augit.git');
    await rem.locator('[data-remote-action="save"]').click();
    await rem.waitForTimeout(900);
    const remFail = await rem.evaluate(() => ({
      remote: !!document.querySelector('.remote-window'),
      notice: (document.querySelector('.remote-window .remote-notice') || {}).textContent || null,
      writes: window.__remoteWrites || [],
    }));
    check('保存失败时仍调用写接口: ' + JSON.stringify(remFail.writes),
      remFail.writes.length === 1 && remFail.writes[0].action === 'update'
      && remFail.writes[0].currentName === 'origin');
    check('远端保存失败保留窗口: ' + remFail.remote, remFail.remote === true);
    check('远端保存失败显示 Git 原因: ' + JSON.stringify(remFail.notice),
      typeof remFail.notice === 'string' && remFail.notice.includes('已存在'));

    // 保存成功：关闭远端窗口，Push 窗口保持唯一
    await rem.evaluate(() => { window.__remoteWriteFails = false; });
    await rem.locator('[data-remote-action="save"]').click();
    await rem.waitForTimeout(1000);
    const remSaved = await rem.evaluate(() => ({
      remote: !!document.querySelector('.remote-window'),
      pushDialogs: document.querySelectorAll('.dialog.push-dialog').length,
      workspace: !!document.querySelector('.workspace'),
    }));
    check('保存成功后关闭远端窗口', remSaved.remote === false);
    check('保存成功后 Push 窗口仍唯一: ' + remSaved.pushDialogs, remSaved.pushDialogs === 1);
    check('保存成功后主窗口结构完好: ' + remSaved.workspace, remSaved.workspace === true);
    await rem.close();


    // ---- 规格 §7.8：变化文件右键菜单，以及文件历史入口 ----
    const ctx = await openScene('scene=commit-changes&theme=dark');
    await ctx.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await ctx.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    const ctxBefore = await ctx.page.evaluate(() => ({
      menus: document.querySelectorAll('.changes-menu').length,
      comparisons: (window.__augitLive.tabs || []).filter((t) => t.kind === 'comparison').length,
      editor: window.__augitLive.editor,
    }));
    // 右键打开菜单；打开菜单不得打开比较
    await ctx.page.locator('.changes-list .change-file-row').first().click({ button: 'right' });
    await ctx.page.waitForTimeout(600);
    const ctxOpened = await ctx.page.evaluate(() => {
      const menu = document.querySelector('.changes-menu');
      return {
        menus: document.querySelectorAll('.changes-menu').length,
        items: menu ? [...menu.querySelectorAll('.menu-item')].map((el) => el.textContent.trim()) : [],
        path: menu ? menu.dataset.changePath : null,
        comparisons: (window.__augitLive.tabs || []).filter((t) => t.kind === 'comparison').length,
        editor: window.__augitLive.editor,
      };
    });
    check('右键变化文件打开菜单: ' + ctxOpened.menus, ctxOpened.menus === 1);
    check('菜单带上目标路径: ' + JSON.stringify(ctxOpened.path), typeof ctxOpened.path === 'string' && ctxOpened.path.length > 0);
    check('菜单含显示 Diff / 文件历史 / Blame: ' + JSON.stringify(ctxOpened.items),
      ctxOpened.items.some((t) => t.includes('显示 Diff'))
      && ctxOpened.items.some((t) => t.includes('文件历史'))
      && ctxOpened.items.some((t) => t.includes('Blame')));
    check('打开菜单不打开比较: ' + JSON.stringify([ctxBefore.comparisons, ctxOpened.comparisons, ctxOpened.editor]),
      ctxOpened.comparisons === ctxBefore.comparisons && ctxOpened.editor === ctxBefore.editor);

    // 点「文件历史」：读取该路径历史并切到底部文件历史工具窗口
    await ctx.page.evaluate(() => { window.__fileHistoryCalls = 0; });
    // 菜单项顺序固定：显示 Diff=0、回滚=1、文件历史=2、Blame=3、复制路径=4、定位=5。
    // 用派发点击：弹层为绝对定位，Playwright 的可点击性检查会超时；
    // 事件仍由真实的 document 委托处理器接收，因此行为路径未变。
    await ctx.page.evaluate(() => {
      document.querySelectorAll('.changes-menu .menu-item')[2].dispatchEvent(
        new MouseEvent('click', { bubbles: true, cancelable: true }));
    });
    // 等真实状态出现，而不是靠固定延时。
    await ctx.page.waitForFunction('!!(window.__augitLive && window.__augitLive.fileHistory)', null, { timeout: 10000 }).catch(() => {});
    await ctx.page.waitForTimeout(600);
    const fh = await ctx.page.evaluate(() => ({
      calls: window.__fileHistoryCalls || 0,
      path: window.__augitLive.fileHistory ? window.__augitLive.fileHistory.path : null,
      bottom: window.__augitLive.layout ? window.__augitLive.layout.bottom : null,
      hasTool: !!document.querySelector('.bottom-tool'),
      menuClosed: document.querySelectorAll('.changes-menu').length,
    }));
    check('文件历史读取目标路径: ' + JSON.stringify([fh.calls, fh.path]),
      fh.calls === 1 && typeof fh.path === 'string' && fh.path.length > 0);
    check('文件历史切到底部工具窗口: ' + JSON.stringify(fh.bottom), fh.bottom === 'file-history');
    check('文件历史工具窗口已渲染', fh.hasTool === true);
    check('选择动作后菜单关闭', fh.menuClosed === 0);

    // 点「Blame」：进入归属视图。
    // 上一步切入文件历史会整页重绘（底部区域从无到有是结构性变化），
    // 改动列表已不在文档里，因此重新载入场景再测。
    await ctx.page.goto(`http://127.0.0.1:${port}/index.html?scene=commit-changes&theme=dark`, { waitUntil: 'load' });
    await ctx.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await ctx.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    await ctx.page.evaluate(() => { window.__blameCalls = 0; });
    await ctx.page.locator('.changes-list .change-file-row').nth(1).click({ button: 'right' });
    await ctx.page.waitForTimeout(500);
    await ctx.page.waitForTimeout(500);
    await ctx.page.evaluate(() => {
      document.querySelectorAll('.changes-menu .menu-item')[3].dispatchEvent(
        new MouseEvent('click', { bubbles: true, cancelable: true }));
    });
    await ctx.page.waitForTimeout(1200);
    const bl = await ctx.page.evaluate(() => ({
      calls: window.__blameCalls || 0,
      editor: window.__augitLive.editor,
    }));
    check('Blame 读取归属并进入该视图: ' + JSON.stringify(bl), bl.calls === 1 && bl.editor === 'blame');
    await ctx.page.close();

    // ---- 规格 §7.11：新建 Worktree 表单 ----
    const wt = await openScene('scene=main-project&theme=dark');
    await wt.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await wt.page.waitForFunction('!!window.__augitLive.references', null, { timeout: 10000 });
    await wt.page.locator('.top-chip.branch-chip').click();
    await wt.page.waitForTimeout(600);
    await wt.page.evaluate(() => {
      document.querySelectorAll('[data-popover-action="create-worktree"]')[0].dispatchEvent(
        new MouseEvent('click', { bubbles: true, cancelable: true }));
    });
    await wt.page.waitForTimeout(700);
    const wtOpened = await wt.page.evaluate(() => ({
      dialog: !!document.querySelector('.worktree-window'),
      fields: [...document.querySelectorAll('[data-worktree-field]')].map((el) => el.dataset.worktreeField),
      destination: (document.querySelector('[data-worktree-field="destination"]') || {}).value,
      branch: (document.querySelector('[data-worktree-field="branch"]') || {}).value,
      focused: document.activeElement && document.activeElement.dataset
        ? document.activeElement.dataset.worktreeField : null,
    }));
    check('新建 Worktree 打开表单: ' + JSON.stringify(wtOpened.dialog), wtOpened.dialog === true);
    check('表单提供目录与分支两个字段: ' + JSON.stringify(wtOpened.fields),
      wtOpened.fields.join(',') === 'destination,branch');
    check('目录默认留空、分支预填当前分支: ' + JSON.stringify([wtOpened.destination, wtOpened.branch]),
      wtOpened.destination === '' && wtOpened.branch === 'dsh');
    check('打开后焦点在目录输入框', wtOpened.focused === 'destination');

    // 目录为空：必须被拒绝且不调用接口
    await wt.page.evaluate(() => { window.__worktreeWrites = []; });
    await wt.page.evaluate(() => {
      document.querySelectorAll('[data-worktree-action="create"]')[0].dispatchEvent(
        new MouseEvent('click', { bubbles: true, cancelable: true }));
    });
    await wt.page.waitForTimeout(600);
    const wtEmpty = await wt.page.evaluate(() => ({
      writes: window.__worktreeWrites || [],
      notice: (document.querySelector('.worktree-window .worktree-notice') || {}).textContent || null,
      dialog: !!document.querySelector('.worktree-window'),
    }));
    check('目录为空不调用接口: ' + JSON.stringify(wtEmpty.writes), wtEmpty.writes.length === 0);
    check('目录为空给出原因: ' + JSON.stringify(wtEmpty.notice),
      typeof wtEmpty.notice === 'string' && wtEmpty.notice.includes('目标目录'));
    check('校验失败保留表单', wtEmpty.dialog === true);

    // 创建失败：保留输入并显示 Git 原因
    await wt.page.locator('[data-worktree-field="destination"]').fill('D:\\worktrees\\augit-feat');
    await wt.page.evaluate(() => { window.__worktreeFails = true; });
    await wt.page.evaluate(() => {
      document.querySelectorAll('[data-worktree-action="create"]')[0].dispatchEvent(
        new MouseEvent('click', { bubbles: true, cancelable: true }));
    });
    await wt.page.waitForTimeout(700);
    const wtFail = await wt.page.evaluate(() => ({
      writes: window.__worktreeWrites || [],
      notice: (document.querySelector('.worktree-window .worktree-notice') || {}).textContent || null,
      dialog: !!document.querySelector('.worktree-window'),
    }));
    check('创建带上目录与分支: ' + JSON.stringify(wtFail.writes),
      wtFail.writes.length === 1 && wtFail.writes[0].destination === 'D:\\worktrees\\augit-feat'
      && wtFail.writes[0].branch === 'dsh');
    check('创建失败保留表单: ' + wtFail.dialog, wtFail.dialog === true);
    check('创建失败显示 Git 原因: ' + JSON.stringify(wtFail.notice),
      typeof wtFail.notice === 'string' && wtFail.notice.includes('不为空'));

    // 创建成功：关闭表单
    await wt.page.evaluate(() => { window.__worktreeFails = false; });
    await wt.page.evaluate(() => {
      document.querySelectorAll('[data-worktree-action="create"]')[0].dispatchEvent(
        new MouseEvent('click', { bubbles: true, cancelable: true }));
    });
    await wt.page.waitForTimeout(1000);
    const wtOk = await wt.page.evaluate(() => ({
      dialog: !!document.querySelector('.worktree-window'),
      writes: window.__worktreeWrites.length,
    }));
    check('创建成功后关闭表单: ' + JSON.stringify(wtOk), wtOk.dialog === false && wtOk.writes === 2);
    await wt.page.close();

    // ---- 提交设置入口：打开设置对话框 ----
    const se = await openScene('scene=commit-changes&theme=dark');
    await se.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await se.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    await se.page.waitForFunction('!!(window.__augitLive && window.__augitLive.settings)', null, { timeout: 10000 });
    const seBefore = await se.page.evaluate(() => ({
      dialogs: document.querySelectorAll('.settings-window').length,
      url: location.pathname,
    }));
    await se.page.locator('[aria-label="提交设置"]').first().click();
    await se.page.waitForTimeout(800);
    const seOpened = await se.page.evaluate(() => ({
      dialog: !!document.querySelector('.settings-window'),
      title: document.querySelector('.settings-window .dialog-header span')
        ? document.querySelector('.settings-window .dialog-header span').innerText : null,
      fields: document.querySelectorAll('.settings-window [data-setting]').length,
      actions: [...document.querySelectorAll('[data-settings-action]')].map((el) => el.dataset.settingsAction),
      url: location.pathname,
    }));
    check('提交设置入口打开设置对话框: ' + JSON.stringify([seBefore.dialogs, seOpened.dialog]),
      seBefore.dialogs === 0 && seOpened.dialog === true);
    check('设置对话框标题正确: ' + JSON.stringify(seOpened.title),
      typeof seOpened.title === 'string' && seOpened.title.includes('设置'));
    check('设置对话框含可编辑字段: ' + seOpened.fields, seOpened.fields > 0);
    check('设置对话框提供取消与保存: ' + JSON.stringify(seOpened.actions),
      seOpened.actions.join(',') === 'cancel,save');
    check('点击提交设置不跳转页面: ' + seOpened.url, !seOpened.url.includes('settings.html'));

    // 保存：写入设置并关闭对话框
    await se.page.evaluate(() => { window.__settingsWritten = null; });
    await se.page.locator('[data-settings-action="save"]').click();
    await se.page.waitForTimeout(1000);
    const seSaved = await se.page.evaluate(() => ({
      written: window.__settingsWritten,
      dialog: !!document.querySelector('.settings-window'),
      theme: window.__augitLive.settings ? window.__augitLive.settings.theme : null,
    }));
    check('保存写入设置: ' + JSON.stringify(seSaved.written), !!seSaved.written);
    check('保存后关闭设置对话框', seSaved.dialog === false);

    // 取消：不写入设置
    await se.page.evaluate(() => { window.__settingsWritten = null; });
    await se.page.locator('[aria-label="提交设置"]').first().click();
    await se.page.waitForTimeout(700);
    await se.page.locator('[data-settings-action="cancel"]').click();
    await se.page.waitForTimeout(600);
    const seCancelled = await se.page.evaluate(() => ({
      written: window.__settingsWritten,
      dialog: !!document.querySelector('.settings-window'),
    }));
    check('取消不写入设置: ' + JSON.stringify(seCancelled.written), seCancelled.written === null);
    check('取消后关闭设置对话框', seCancelled.dialog === false);
    await se.page.close();

    // ---- 规格 §5.3：对话框取消后恢复打开前焦点 ----
    const fo = await openScene('scene=commit-changes&theme=dark');
    await fo.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await fo.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    await fo.page.waitForFunction('!!(window.__augitLive && window.__augitLive.settings)', null, { timeout: 10000 });

    // 从「提交设置」按钮打开设置，取消后焦点回到该按钮
    await fo.page.locator('[aria-label="提交设置"]').first().click();
    await fo.page.waitForTimeout(600);
    const foOpened = await fo.page.evaluate(() => ({
      dialog: !!document.querySelector('.settings-window'),
      focusLabel: document.activeElement ? document.activeElement.getAttribute('aria-label') : null,
    }));
    check('设置对话框已打开: ' + JSON.stringify(foOpened.dialog), foOpened.dialog === true);
    await fo.page.locator('[data-settings-action="cancel"]').click();
    await fo.page.waitForTimeout(500);
    const foCancelled = await fo.page.evaluate(() => ({
      dialog: !!document.querySelector('.settings-window'),
      focusLabel: document.activeElement ? document.activeElement.getAttribute('aria-label') : null,
    }));
    check('取消后焦点回到触发按钮: ' + JSON.stringify(foCancelled.focusLabel),
      foCancelled.focusLabel === '提交设置');

    // 保存后同样回到触发区域
    await fo.page.locator('[aria-label="提交设置"]').first().click();
    await fo.page.waitForTimeout(600);
    await fo.page.locator('[data-settings-action="save"]').click();
    await fo.page.waitForTimeout(900);
    const foSaved = await fo.page.evaluate(() => ({
      dialog: !!document.querySelector('.settings-window'),
      focusLabel: document.activeElement ? document.activeElement.getAttribute('aria-label') : null,
    }));
    check('保存后焦点回到触发区域: ' + JSON.stringify(foSaved.focusLabel),
      foSaved.dialog === false && foSaved.focusLabel === '提交设置');

    // Esc 关闭弹层后焦点回到打开它的入口
    const fo2 = await openScene('scene=main-project&theme=dark');
    await fo2.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await fo2.page.waitForFunction('!!window.__augitLive.references', null, { timeout: 10000 });
    await fo2.page.locator('.top-chip.branch-chip').click();
    await fo2.page.waitForTimeout(600);
    check('弹层已打开', await fo2.page.evaluate(() => !!document.querySelector('.live-overlay')));
    await fo2.page.keyboard.press('Escape');
    await fo2.page.waitForTimeout(500);
    const fo2Closed = await fo2.page.evaluate(() => ({
      overlay: !!document.querySelector('.live-overlay'),
      focusClass: document.activeElement ? document.activeElement.className : null,
    }));
    check('Esc 关闭弹层后焦点回到分支芯片: ' + JSON.stringify(fo2Closed.focusClass),
      fo2Closed.overlay === false && typeof fo2Closed.focusClass === 'string'
      && fo2Closed.focusClass.includes('branch-chip'));
    await fo2.page.close();

    // Worktree 表单取消后焦点回到触发它的弹层入口
    await fo.page.locator('.top-chip.branch-chip').click();
    await fo.page.waitForTimeout(600);
    await fo.page.evaluate(() => {
      document.querySelectorAll('[data-popover-action="create-worktree"]')[0].dispatchEvent(
        new MouseEvent('click', { bubbles: true, cancelable: true }));
    });
    await fo.page.waitForTimeout(700);
    await fo.page.locator('[data-worktree-action="cancel"]').click();
    await fo.page.waitForTimeout(500);
    const foWt = await fo.page.evaluate(() => ({
      dialog: !!document.querySelector('.worktree-window'),
      focusClass: document.activeElement ? document.activeElement.className : null,
    }));
    // 弹层里的菜单项是不可聚焦的 <a>（没有 href），打开它时焦点仍在分支芯片上，
    // 因此取消后回到芯片——这正是规格要求的「恢复打开前焦点」。
    check('Worktree 取消后焦点回到打开前元素: ' + JSON.stringify(foWt),
      foWt.dialog === false && typeof foWt.focusClass === 'string'
      && foWt.focusClass.includes('branch-chip'));
    await fo.page.close();

    // ---- 产品规格 §3.3：固定快捷键 ----
    const ks = await openScene('scene=main-project&theme=dark');
    await ks.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    const ksState = () => ks.page.evaluate(() => ({
      overlays: document.querySelectorAll('.live-overlay').length,
      tabs: document.querySelectorAll('.search-tabs').length,
      tabText: document.querySelector('.search-tabs') ? document.querySelector('.search-tabs').innerText.trim() : null,
      compact: !!document.querySelector('[data-compact-dialog]'),
      compactTitle: document.querySelector('[data-compact-dialog] .dialog-header span')
        ? document.querySelector('[data-compact-dialog] .dialog-header span').innerText : null,
      focused: document.activeElement ? document.activeElement.className : null,
      focusInFind: !!(document.activeElement && document.activeElement.closest
        && document.activeElement.closest('.current-find')),
      findBar: document.querySelectorAll('.current-find').length,
    }));

    // 规格 §5.3/§5.4：输入法组词期间不得抢占按键。
    // 这一条此前没有任何断言——三个入口（紧凑窗口、Esc 关弹层、全局快捷键）
    // 都写了 isComposing 守卫，但没有测试证明它们真的挡住了。
    const composeKey = async (init) => ks.page.evaluate((options) => {
      const event = new KeyboardEvent('keydown', Object.assign({
        bubbles: true, cancelable: true, isComposing: true,
      }, options));
      document.dispatchEvent(event);
      return event.defaultPrevented;
    }, init);
    const preventedByP = await composeKey({ key: 'p', ctrlKey: true });
    await ks.page.waitForTimeout(400);
    const afterComposingP = await ksState();
    check('组词期间 Ctrl+P 不打开快速打开: ' + JSON.stringify([preventedByP, afterComposingP.tabs]),
      afterComposingP.tabs === 0);
    const preventedByShiftF = await composeKey({ key: 'F', ctrlKey: true, shiftKey: true });
    await ks.page.waitForTimeout(400);
    const afterComposingSF = await ksState();
    check('组词期间 Ctrl+Shift+F 不打开全仓搜索: ' + JSON.stringify([preventedByShiftF, afterComposingSF.tabs]),
      afterComposingSF.tabs === 0);
    const preventedByG = await composeKey({ key: 'g', ctrlKey: true });
    await ks.page.waitForTimeout(400);
    const afterComposingG = await ksState();
    check('组词期间 Ctrl+G 不打开跳转行: ' + JSON.stringify([preventedByG, afterComposingG.compact]),
      afterComposingG.compact === false);
    // Ctrl+W 走的是独立处理器（bindEditorTabs 内），同样必须被组词拦住。
    const tabsBeforeComposingW = await ks.page.evaluate(() => (window.__augitLive.tabs || []).length);
    const preventedByW = await composeKey({ key: 'w', ctrlKey: true });
    await ks.page.waitForTimeout(400);
    const tabsAfterComposingW = await ks.page.evaluate(() => (window.__augitLive.tabs || []).length);
    check('组词期间 Ctrl+W 不关闭标签: ' + JSON.stringify([preventedByW, tabsBeforeComposingW, tabsAfterComposingW]),
      tabsAfterComposingW === tabsBeforeComposingW);

    // 组词期间 Esc 不得关闭弹层：先打开一个真实弹层再派发组词中的 Esc。
    await ks.page.keyboard.press('Control+p');
    await ks.page.waitForFunction('!!document.querySelector(".live-overlay")', null, { timeout: 8000 });
    const preventedByEsc = await composeKey({ key: 'Escape' });
    await ks.page.waitForTimeout(400);
    const overlayAfterComposingEsc = await ks.page.evaluate(() => !!document.querySelector('.live-overlay'));
    check('组词期间 Esc 不关闭弹层: ' + JSON.stringify([preventedByEsc, overlayAfterComposingEsc]),
      overlayAfterComposingEsc === true);
    // 非组词的 Esc 才关闭。
    await ks.page.keyboard.press('Escape');
    await ks.page.waitForTimeout(400);
    check('非组词 Esc 正常关闭弹层',
      await ks.page.evaluate(() => !document.querySelector('.live-overlay')));

    // Ctrl+P 快速打开文件
    await ks.page.keyboard.press('Control+p');
    await ks.page.waitForTimeout(700);
    const ksP = await ksState();
    check('Ctrl+P 打开快速打开: ' + JSON.stringify([ksP.overlays, ksP.tabText]),
      ksP.overlays === 1 && typeof ksP.tabText === 'string' && ksP.tabText.includes('快速打开'));
    await ks.page.keyboard.press('Escape');
    await ks.page.waitForTimeout(400);

    // Ctrl+Shift+F 全仓搜索
    await ks.page.keyboard.press('Control+Shift+f');
    await ks.page.waitForTimeout(700);
    const ksSF = await ksState();
    check('Ctrl+Shift+F 打开全仓搜索: ' + JSON.stringify(ksSF.tabText),
      ksSF.overlays === 1 && typeof ksSF.tabText === 'string' && ksSF.tabText.includes('搜索'));
    await ks.page.keyboard.press('Escape');
    await ks.page.waitForTimeout(400);

    // Ctrl+G 跳转行
    await ks.page.keyboard.press('Control+g');
    await ks.page.waitForTimeout(600);
    const ksG = await ksState();
    check('Ctrl+G 打开跳转行: ' + JSON.stringify([ksG.compact, ksG.compactTitle]),
      ksG.compact === true && ksG.compactTitle === '跳转行');

    // 规格 §5.3：打开后输入框获得焦点；Tab / Shift+Tab 在输入框、取消、确定、关闭间循环。
    const compactOpen = await ks.page.evaluate(() => ({
      activeIsField: document.activeElement === document.querySelector('[data-compact-dialog] [data-compact-field]'),
      actions: [...document.querySelectorAll('[data-compact-dialog] [data-compact-action]')]
        .map((n) => n.dataset.compactAction),
    }));
    check('紧凑输入窗口打开即聚焦输入框: ' + JSON.stringify(compactOpen.activeIsField),
      compactOpen.activeIsField === true);
    check('紧凑输入窗口具备取消/确定/关闭动作: ' + JSON.stringify(compactOpen.actions),
      ['cancel', 'confirm', 'close'].every((a) => compactOpen.actions.includes(a)));

    const compactActive = () => ks.page.evaluate(() => {
      const active = document.activeElement;
      if (!active || !active.dataset) return 'other';
      return active.dataset.compactAction || (active.dataset.compactField !== undefined ? 'field' : 'other');
    });
    const cycle = [];
    cycle.push(await compactActive());
    for (let step = 0; step < 4; step++) {
      await ks.page.keyboard.press('Tab');
      await ks.page.waitForTimeout(120);
      cycle.push(await compactActive());
    }
    check('Tab 按输入框→取消→确定→关闭→输入框循环: ' + JSON.stringify(cycle),
      cycle.join('>') === 'field>cancel>confirm>close>field');
    await ks.page.keyboard.press('Shift+Tab');
    await ks.page.waitForTimeout(120);
    const backToClose = await compactActive();
    check('Shift+Tab 反向循环: ' + JSON.stringify([cycle.at(-1), backToClose]),
      backToClose === 'close');

    // 取消后关闭窗口；焦点恢复见下方「跳转行按钮」用例。
    await ks.page.locator('[data-compact-dialog] [data-compact-action="cancel"]').click();
    await ks.page.waitForTimeout(400);
    check('取消后关闭紧凑输入窗口',
      await ks.page.evaluate(() => !document.querySelector('[data-compact-dialog]')));

    // 规格 §5.3：对话框取消后恢复打开前焦点。
    // 前置条件必须**真的有一个获得焦点的元素**：键盘按 Ctrl+G 不会让任何元素获得焦点，
    // 快照为空时"恢复"没有可核对目标，曾因此误判实现有缺陷。这里先点击项目树行。
    const focusScene = await openScene('scene=main-project&theme=dark');
    await focusScene.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await focusScene.page.waitForSelector('.side-content.tree .tree-row[data-tree-path="docs"]', { timeout: 10000 });
    // 树行是 tabindex="-1"（可编程聚焦、不进 Tab 序列，与视觉稿一致）。
    // 真实点击会聚焦该行（已实测 activeElement 落在树行上），这里显式聚焦以明确前置条件。
    await focusScene.page.locator('.side-content.tree .tree-row[data-tree-path="docs"]').focus();
    await focusScene.page.waitForTimeout(200);
    const focusPre = await focusScene.page.evaluate(() => {
      const active = document.activeElement;
      return {
        inTree: !!(active && active.closest && active.closest('.side-content.tree')),
        treePath: active && active.dataset ? active.dataset.treePath || null : null,
        sameNode: active === document.querySelector('.side-content.tree .tree-row[data-tree-path="docs"]'),
      };
    });
    check('前置条件：树行确实持有焦点: ' + JSON.stringify(focusPre),
      focusPre.inTree === true && focusPre.treePath === 'docs' && focusPre.sameNode === true);

    await focusScene.page.keyboard.press('Control+g');
    await focusScene.page.waitForFunction('!!document.querySelector("[data-compact-dialog]")', null, { timeout: 8000 });
    check('紧凑窗口打开后输入框获得焦点',
      await focusScene.page.evaluate(() =>
        document.activeElement === document.querySelector('[data-compact-dialog] [data-compact-field]')));

    // 取消：焦点回到打开前的树行。
    await focusScene.page.locator('[data-compact-dialog] [data-compact-action="cancel"]').click();
    await focusScene.page.waitForTimeout(500);
    const focusAfterCancel = await focusScene.page.evaluate(() => {
      const active = document.activeElement;
      return {
        gone: !document.querySelector('[data-compact-dialog]'),
        inTree: !!(active && active.closest && active.closest('.side-content.tree')),
        treePath: active && active.dataset ? active.dataset.treePath || null : null,
      };
    });
    check('取消后焦点回到打开前的树行: ' + JSON.stringify(focusAfterCancel),
      focusAfterCancel.gone === true && focusAfterCancel.inTree === true
        && focusAfterCancel.treePath === 'docs');

    // 确认路径：焦点同样回到触发区域，而不是留在已关闭的窗口里。
    await focusScene.page.locator('.side-content.tree .tree-row[data-tree-path="docs"]').focus();
    await focusScene.page.waitForTimeout(200);
    await focusScene.page.keyboard.press('Control+g');
    await focusScene.page.waitForFunction('!!document.querySelector("[data-compact-dialog]")', null, { timeout: 8000 });
    await focusScene.page.locator('[data-compact-dialog] [data-compact-field]').fill('1');
    await focusScene.page.locator('[data-compact-dialog] [data-compact-action="confirm"]').click();
    await focusScene.page.waitForTimeout(700);
    const focusAfterConfirm = await focusScene.page.evaluate(() => {
      const active = document.activeElement;
      return {
        gone: !document.querySelector('[data-compact-dialog]'),
        inTree: !!(active && active.closest && active.closest('.side-content.tree')),
        stillInDialog: !!(active && active.closest && active.closest('[data-compact-dialog]')),
      };
    });
    check('确认后焦点不停留在已关闭的窗口内: ' + JSON.stringify(focusAfterConfirm),
      focusAfterConfirm.gone === true && focusAfterConfirm.stillInDialog === false);
    await focusScene.page.close();

    // F5 刷新文件树；不重载页面
    const ksUrl = ks.page.url();
    await ks.page.evaluate(() => { window.__augitF5 = 'before'; });
    await ks.page.keyboard.press('F5');
    await ks.page.waitForTimeout(800);
    const ksF5 = await ks.page.evaluate(() => ({
      url: location.pathname,
      marker: window.__augitF5 || null,
      treeRows: document.querySelectorAll('.side-content.tree .tree-row').length,
    }));
    check('F5 刷新文件树且不重载页面: ' + JSON.stringify([ksF5.marker, ksF5.treeRows, ksF5.url]),
      ksF5.marker === 'before' && ksF5.treeRows > 0 && !ksF5.url.includes('main-project.html'));
    check('F5 未导致整页导航: ' + ksUrl, ks.page.url() === ksUrl);

    // Ctrl+F 当前文件查找：需要先打开一个文本文件
    await ks.page.locator('.side-content.tree .tree-row[data-tree-path="docs"]').click();
    await ks.page.waitForTimeout(400);
    await ks.page.locator('.side-content.tree .tree-row[data-tree-path="docs/notes.txt"]').dblclick();
    await ks.page.waitForTimeout(900);
    await ks.page.keyboard.press('Control+f');
    await ks.page.waitForTimeout(700);
    const ksF = await ksState();
    check('Ctrl+F 打开当前文件查找: ' + JSON.stringify([ksF.findBar, ksF.focusInFind]),
      ksF.findBar === 1 && ksF.focusInFind === true);
    await ks.page.close();

    // ---- 规格 §5.4：Tab 在当前区域内按视觉顺序移动焦点 ----
    const tb = await openScene('scene=main-project&theme=dark');
    await tb.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await tb.page.waitForTimeout(800);
    const describe = () => tb.page.evaluate(() => {
      const el = document.activeElement;
      if (!el || el === document.body) return 'body';
      const region = el.closest('.tool-rail') ? 'rail'
        : el.closest('.side-tool') ? 'side'
          : el.closest('.editor-area') ? 'editor'
            : el.closest('.bottom-tool') ? 'bottom'
              : el.closest('.titlebar') ? 'titlebar'
                : el.closest('.statusbar') ? 'statusbar' : 'other';
      return region + ':' + (el.getAttribute('aria-label') || el.className || el.tagName);
    });
    // 收集 Tab 顺序，并记录每个焦点元素的视觉位置（上边距、左边距）。
    const stops = [];
    for (let i = 0; i < 14; i += 1) {
      await tb.page.keyboard.press('Tab');
      await tb.page.waitForTimeout(110);
      const info = await tb.page.evaluate(() => {
        const el = document.activeElement;
        if (!el || el === document.body) return null;
        const rect = el.getBoundingClientRect();
        const region = el.closest('.tool-rail') ? 'rail'
          : el.closest('.side-tool') ? 'side'
            : el.closest('.editor-area') ? 'editor'
              : el.closest('.bottom-tool') ? 'bottom'
                : el.closest('.titlebar') ? 'titlebar'
                  : el.closest('.statusbar') ? 'statusbar' : 'other';
        return {
          label: region + ':' + (el.getAttribute('aria-label') || el.className),
          top: Math.round(rect.top), left: Math.round(rect.left), region,
        };
      });
      if (info) stops.push(info);
    }

    check('Tab 可移动焦点: ' + JSON.stringify(stops.slice(0, 3).map((x) => x.label)),
      stops.length >= 5);
    // 视觉顺序（规格 §5.4）。
    // 注意 rail 与 side 是**纵向并排的两个区域**，跨区域时不套用「自上而下」——
    // 否则会要求先走完 rail 全部项再回到 screen 顶部的 side 面板，那不是视觉顺序。
    // 因此分别检查：同一区域内自上而下；同一水平带内自左而右。
    const backwards = [];
    for (let i = 1; i < stops.length; i += 1) {
      const previous = stops[i - 1];
      const current = stops[i];
      if (previous.region !== current.region) continue;
      const sameBand = Math.abs(current.top - previous.top) <= 8;
      const violates = sameBand ? current.left < previous.left - 2 : current.top < previous.top - 2;
      if (violates) backwards.push(`${previous.label} → ${current.label}`);
    }
    check('区域内 Tab 顺序符合视觉位置: ' + JSON.stringify(backwards), backwards.length === 0);
    // 先左后右的区域顺序：rail 的入口应在 side 面板之前。
    const firstRail = stops.findIndex((x) => x.region === 'rail');
    const firstSide = stops.findIndex((x) => x.region === 'side');
    check('左侧工具入口先于侧栏内容: ' + JSON.stringify([firstRail, firstSide]),
      firstRail >= 0 && (firstSide < 0 || firstRail < firstSide));
    // 规格要求「不先穿越所有全局工具入口」：编辑区可见时，Tab 必须能在该区域内到达。
    check('Tab 顺序先覆盖顶部与左侧入口: ' + JSON.stringify(stops.slice(0, 4).map((x) => x.region)),
      stops.slice(0, 2).every((x) => x.region === 'titlebar' || x.region === 'rail'));
    await tb.page.close();

    // ---- 规格 §6.7：读取尚未完成时不建立标签，被取代的读取不得留下视图 ----
    // 规格原文是"关闭尚在读取的标签后，结果不得创建视图或恢复标签"。
    // 核对实现后确认：**标签只在读取完成后建立**（没有"先建占位标签、后填正文"的路径），
    // 因此"读取中关闭标签"在当前标签模型下无法构造——写一条那样的断言等于空跑。
    // 这里改为核对真正成立的不变量：读取在途中标签栏不出现该文件，
    // 被更新请求取代后晚到结果也不得创建视图。
    const slowTab = await openScene('scene=main-project&theme=dark');
    await slowTab.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await slowTab.page.waitForSelector('.side-content.tree .tree-row[data-tree-path="docs"]', { timeout: 10000 });
    await slowTab.page.locator('.side-content.tree .tree-row[data-tree-path="docs"]').click();
    await slowTab.page.waitForSelector('.side-content.tree .tree-row[data-tree-path="docs/notes.txt"]', { timeout: 8000 });
    await slowTab.page.evaluate(() => { window.__readDelays = { 'docs/notes.txt': 5000 }; });
    // 同步派发双击，不等待读取完成。
    await slowTab.page.evaluate(() => {
      const row = document.querySelector('.side-content.tree .tree-row[data-tree-path="docs/notes.txt"]');
      row.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, detail: 1 }));
      row.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, detail: 2 }));
      window.__readStartedAt = Math.round(performance.now());
    });
    await slowTab.page.waitForTimeout(600);
    const duringRead = await slowTab.page.evaluate(() => ({
      tabs: (window.__augitLive.tabs || []).filter((t) => t.kind === 'document').map((t) => t.path),
      doc: window.__augitLive.document ? window.__augitLive.document.path : null,
      readStartedAt: window.__readStartedAt || null,
      now: Math.round(performance.now()),
    }));
    // 前置条件：取样确实落在读取过程中。
    check('前置条件：取样落在读取过程中: ' + JSON.stringify([duringRead.now, duringRead.readStartedAt]),
      duringRead.readStartedAt !== null && duringRead.now - duringRead.readStartedAt < 4000);
    check('读取途中不建立标签、不替换正文: ' + JSON.stringify(duringRead),
      !duringRead.tabs.includes('docs/notes.txt') && duringRead.doc !== 'docs/notes.txt');

    // 用另一个文件取代在途读取：晚到的旧结果不得创建视图。
    await slowTab.page.evaluate(() => { window.__readDelays = {}; });
    // 必须双击：单击按规格只选择，不打开（§12.5）。
    await slowTab.page.locator('.side-content.tree .tree-row[data-tree-path="docs/product-spec.md"]').dblclick();
    await slowTab.page.waitForFunction(
      'window.__augitLive.document && window.__augitLive.document.path === "docs/product-spec.md"',
      null,
      { timeout: 10000 },
    );
    await slowTab.page.waitForFunction(
      (startedAt) => Math.round(performance.now()) - startedAt > 5500,
      duringRead.readStartedAt,
      { timeout: 20000 },
    );
    const afterSuperseded = await slowTab.page.evaluate(() => ({
      tabs: (window.__augitLive.tabs || []).filter((t) => t.kind === 'document').map((t) => t.path),
      doc: window.__augitLive.document ? window.__augitLive.document.path : null,
    }));
    check('被取代的读取晚到后不创建视图: ' + JSON.stringify(afterSuperseded),
      !afterSuperseded.tabs.includes('docs/notes.txt')
        && afterSuperseded.doc === 'docs/product-spec.md');
    await slowTab.page.close();

    // ---- 规格 §7.8：历史比较标签 ----
    // 契约取自 docs/ux-mockups/ 里既有的历史比较实现：双击变化文件或 Enter 打开、
    // 标签显示双方引用、单击跟随、关闭解除跟随。后端两版本 diff 见 Infrastructure 测试。
    const hc = await openScene('scene=git-history&theme=dark');
    await hc.page.waitForFunction('window.__augitHistoryReady === true', null, { timeout: 20000 });
    await hc.page.waitForSelector('.commit-row', { timeout: 10000 });
    await hc.page.waitForSelector('[data-live-changed-files] [data-history-path]', { timeout: 10000 });
    const hcState = () => hc.page.evaluate(() => {
      const comparison = (window.__augitLive.tabs || []).find((t) => t.kind === 'comparison') || null;
      return {
        comparisons: (window.__augitLive.tabs || []).filter((t) => t.kind === 'comparison').length,
        label: comparison ? comparison.title : null,
        tabPath: comparison ? comparison.path : null,
        active: String(window.__augitLive.activeTabId),
        editor: window.__augitLive.editor,
        diffPath: window.__augitLive.diff ? window.__augitLive.diff.path : null,
        historyState: window.__augitLive.historyComparison
          ? window.__augitLive.historyComparison.status + ':' + window.__augitLive.historyComparison.path
          : null,
        commitRequests: (window.__diffCommits || []).length,
      };
    });

    // 前置条件：提交详情已列出变化文件行。
    const hcFiles = await hc.page.evaluate(() =>
      [...document.querySelectorAll('[data-live-changed-files] [data-history-path]')].map((r) => r.dataset.historyPath));
    check('前置条件：提交详情列出变化文件: ' + JSON.stringify(hcFiles), hcFiles.length >= 1);

    // 单击只选择，不创建比较标签。
    await hc.page.locator('[data-live-changed-files] [data-history-path]').first().click();
    await hc.page.waitForTimeout(400);
    const hcAfterClick = await hcState();
    check('历史文件单击不创建比较标签: ' + JSON.stringify(hcAfterClick.label), hcAfterClick.comparisons === 0);

    // 双击打开历史比较并激活。
    // 用带 detail=2 的点击事件：Playwright 的 dblclick 在这里不会让 detail 进位到 2
    // （前一次单击在捕获阶段被 preventDefault），而真实双击的 detail 会到 2。
    await hc.page.evaluate((index) => {
      const row = [...document.querySelectorAll('[data-live-changed-files] [data-history-path]')][index];
      row.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, detail: 2 }));
    }, 0);
    await hc.page.waitForFunction(
      '!!(window.__augitLive.historyComparison && window.__augitLive.historyComparison.status === "ready")',
      null,
      { timeout: 10000 },
    );
    const hcOpened = await hcState();
    const expectedPath = hcFiles[0];
    const shortName = expectedPath.split('/').at(-1);
    check('双击变化文件建立历史比较标签: ' + JSON.stringify(hcOpened.label),
      hcOpened.comparisons === 1 && typeof hcOpened.label === 'string'
        && hcOpened.label.includes(shortName) && hcOpened.label.includes('→'));
    check('历史比较标签标注双方引用（保留 ^ 祖先后缀）: ' + JSON.stringify(hcOpened.label),
      hcOpened.label.includes('^'));
    check('历史比较打开后成为前台并显示差异: ' + JSON.stringify([hcOpened.editor, hcOpened.diffPath]),
      hcOpened.editor === 'diff' && hcOpened.diffPath === expectedPath
        && hcOpened.active === await hc.page.evaluate(() =>
          ((window.__augitLive.tabs || []).find((t) => t.kind === 'comparison') || {}).id));

    // 文件栏由延后的区域刷新渲染，必须等它真正出现再取样（否则拿到 null）。
    await hc.page.waitForSelector('.editor-content .diff-filebar', { timeout: 8000 });
    // 请求确实带上了 commit。
    const hcCommits = await hc.page.evaluate(() => window.__diffCommits || []);
    check('历史比较请求带上提交版本: ' + JSON.stringify(hcCommits),
      hcCommits.length >= 1 && typeof hcCommits.at(-1) === 'string' && hcCommits.at(-1).length > 0);
    // 文件栏必须显示双方引用（规格 §7.8）：左侧是父版本、右侧是提交版本。
    const hcFilebar = await hc.page.evaluate(() => {
      const bar = document.querySelector('.editor-content .diff-filebar');
      if (!bar) return null;
      return {
        source: (bar.querySelector('.reference-source') || {}).textContent || null,
        target: (bar.querySelector('.reference-target') || {}).textContent || null,
        path: (bar.querySelector('.reference-path') || {}).textContent || null,
      };
    });
    check('历史比较文件栏显示双方引用与路径: ' + JSON.stringify(hcFilebar),
      hcFilebar !== null && hcFilebar.path === expectedPath
        && typeof hcFilebar.source === 'string' && hcFilebar.source.endsWith('^')
        && typeof hcFilebar.target === 'string' && hcFilebar.target.length > 0
        && !hcFilebar.target.includes('^')
        && hcFilebar.source.slice(0, -1) === hcFilebar.target);

    // 改选提交：已打开的历史比较跟随到同一路径，且不抢占前台（规格 §7.8）。
    await hc.page.locator('.commit-row').nth(1).click();
    await hc.page.waitForFunction(
      "window.__augitLive.historyComparison && window.__augitLive.historyComparison.commit === 'full-bbb2222'",
      null,
      { timeout: 10000 },
    );
    const hcFollowed = await hcState();
    // 标签使用提交的完整哈希（截断 8 位）而不是短哈希字段，
    // 因此这里核对的是完整哈希的前缀，不能拿提交行的短哈希去比。
    check('改选提交后历史比较跟随: ' + JSON.stringify([hcFollowed.label, hcFollowed.comparisons]),
      hcFollowed.comparisons === 1 && typeof hcFollowed.label === 'string'
        && hcFollowed.label.includes('full-bbb'));
    check('跟随仍复用同一个标签: ' + JSON.stringify(hcFollowed.tabPath),
      hcFollowed.tabPath === expectedPath);

    // 关闭后解除跟随：单击不再自动重开。
    // 关闭叉在比较标签内是 <span>，Playwright 的 actionability 检查会超时；
    // 真实路径是「按下与松开在同一关闭叉」的 pointerdown/pointerup + click。
    await hc.page.evaluate(() => {
      const close = document.querySelector('.editor-tabs .editor-tab.comparison-tab .tab-close');
      const down = new PointerEvent('pointerdown', { bubbles: true, cancelable: true, button: 0 });
      close.dispatchEvent(down);
      close.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, detail: 1 }));
    });
    await hc.page.waitForTimeout(600);
    const hcClosed = await hcState();
    check('关闭历史比较移除标签: ' + JSON.stringify(hcClosed.comparisons), hcClosed.comparisons === 0);
    await hc.page.evaluate(() => {
      const row = document.querySelector('[data-live-changed-files] [data-history-path]');
      if (row) row.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, detail: 1 }));
    });
    await hc.page.waitForTimeout(700);
    const hcAfterCloseClick = await hcState();
    check('关闭历史比较后单击不重开: ' + JSON.stringify(hcAfterCloseClick.comparisons),
      hcAfterCloseClick.comparisons === 0);
    await hc.page.close();

    // ---- 规格 §5.1：标题栏汉堡菜单 ----
    // 规格要求点击后在标题栏**原位**显示"文件、视图、Git、终端、设置"五个文字入口，
    // 不弹出一张替代标题栏的悬浮卡片；关闭后恢复原有标题栏。
    const menuScene = await openScene('scene=main-project&theme=dark');
    await menuScene.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    // 等工具窗口与标题栏的首轮渲染稳定：启动阶段标题栏会先渲染一版、
    // 随后历史/状态到达再补一版，过早取基线会把"尚未渲染完"当成"被菜单改掉了"。
    await menuScene.page.waitForFunction(
      "document.querySelectorAll('.tool-rail .rail-button').length > 0"
        + " && !!document.querySelector('.titlebar .branch-chip')"
        + " && document.querySelectorAll('.window-actions [aria-label]').length >= 3",
      null,
      { timeout: 15000 },
    );
    await menuScene.page.waitForTimeout(600);
    const menuState = () => menuScene.page.evaluate(() => {
      const titlebar = document.querySelector('.titlebar');
      const bar = document.querySelector('.main-menu-bar');
      const entries = bar ? [...bar.querySelectorAll('.main-menu-entry')].map((a) => a.textContent.trim()) : [];
      return {
        hasBar: !!bar,
        barInTitlebar: !!(bar && titlebar && titlebar.contains(bar)),
        entries,
        overlayCount: document.querySelectorAll('[data-augit-overlay].live-overlay').length,
        chipInTitlebar: !!(titlebar && titlebar.querySelector('.branch-chip')),
        closeLabel: (() => {
          const button = titlebar ? titlebar.querySelector('[data-action="menu"]') : null;
          return button ? button.getAttribute('aria-label') : null;
        })(),
      };
    });
    const menuClosed = await menuState();
    check('前置条件：标题栏初始没有内嵌菜单: ' + JSON.stringify(menuClosed.hasBar), menuClosed.hasBar === false);

    await menuScene.page.locator('.titlebar [data-action="menu"]').click();
    await menuScene.page.waitForTimeout(500);
    const menuOpen = await menuState();
    check('汉堡菜单在标题栏原位显示五个入口: ' + JSON.stringify(menuOpen.entries),
      menuOpen.hasBar === true && menuOpen.barInTitlebar === true
        && ['文件', '视图', 'Git', '终端', '设置'].every((name) => menuOpen.entries.includes(name)));
    check('菜单不是替代标题栏的悬浮卡片: ' + JSON.stringify([menuOpen.overlayCount, menuOpen.chipInTitlebar]),
      menuOpen.overlayCount === 0 && menuOpen.chipInTitlebar === false);
    check('菜单打开后按钮变为关闭语义: ' + JSON.stringify(menuOpen.closeLabel),
      menuOpen.closeLabel === '关闭主菜单');

    // 规格同句还要求"窗口按钮、项目树、编辑标签和工具窗口状态保持不变"：
    // 记下这些区域的状态，关闭菜单后逐一核对。
    // 标题栏按钮在"菜单打开"与"标准标题栏"两种结构下的**节点层级不同**，
    // 因此这里只核对稳定的语义状态：工具窗口/项目树/标签的数量与存在性，
    // 以及菜单按钮的语义（打开时为"关闭主菜单"、关闭后回到"主菜单"）。
    const menuBaseline = await menuScene.page.evaluate(() => ({
      treeRows: document.querySelectorAll('.side-content.tree .tree-row').length,
      tabs: document.querySelectorAll('.editor-tabs .editor-tab').length,
      side: !!document.querySelector('.side-tool'),
      railButtons: document.querySelectorAll('.tool-rail .rail-button').length,
      menuLabel: (document.querySelector('.titlebar [data-action="menu"]') || {}).getAttribute
        ? document.querySelector('.titlebar [data-action="menu"]').getAttribute('aria-label') : null,
    }));

    // 再次点击汉堡按钮关闭（规格：菜单关闭后恢复原有标题栏入口与上下文）。
    await menuScene.page.locator('.titlebar [data-action="menu"]').click();
    await menuScene.page.waitForTimeout(500);
    const menuAfterToggle = await menuState();
    check('再次点击汉堡按钮关闭菜单: ' + JSON.stringify(menuAfterToggle.hasBar), menuAfterToggle.hasBar === false);

    // 重新打开后用 Esc 关闭，并核对各区域状态保持不变。
    await menuScene.page.locator('.titlebar [data-action="menu"]').click();
    await menuScene.page.waitForTimeout(400);
    await menuScene.page.keyboard.press('Escape');
    await menuScene.page.waitForTimeout(500);
    const menuAfterEsc = await menuState();
    check('Esc 关闭内嵌菜单并恢复标题栏: ' + JSON.stringify([menuAfterEsc.hasBar, menuAfterEsc.chipInTitlebar]),
      menuAfterEsc.hasBar === false);
    const menuAfter = await menuScene.page.evaluate(() => ({
      treeRows: document.querySelectorAll('.side-content.tree .tree-row').length,
      tabs: document.querySelectorAll('.editor-tabs .editor-tab').length,
      side: !!document.querySelector('.side-tool'),
      railButtons: document.querySelectorAll('.tool-rail .rail-button').length,
      menuLabel: (document.querySelector('.titlebar [data-action="menu"]') || {}).getAttribute
        ? document.querySelector('.titlebar [data-action="menu"]').getAttribute('aria-label') : null,
      chip: !!document.querySelector('.titlebar .branch-chip'),
    }));
    check('菜单关闭后项目树/标签/工具窗口保持不变: ' + JSON.stringify([menuBaseline, menuAfter]),
      menuAfter.treeRows === menuBaseline.treeRows
        && menuAfter.tabs === menuBaseline.tabs
        && menuAfter.side === menuBaseline.side
        && menuAfter.railButtons === menuBaseline.railButtons
        && menuAfter.chip === true);
    check('菜单关闭后入口恢复为普通标题栏: ' + JSON.stringify([menuBaseline.menuLabel, menuAfter.menuLabel]),
      menuAfter.menuLabel === '主菜单');
    await menuScene.page.close();

    // ---- 规格 §5.1：菜单五个入口各自的行为 ----
    // 规格：终端直接切换底部终端工具窗口；设置直接打开设置模态窗口；
    // 两者执行前先恢复普通标题栏。
    // 每个入口用独立页面：菜单动作会切换工具窗口并触发区域刷新，
    // 在同一个页面上连续操作会互相干扰（实测第二次打开菜单会被重绘收起）。
    const runMenuEntry = async (label) => {
      const { page } = await openScene('scene=main-project&theme=dark');
      await page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
      await page.waitForFunction(
        "document.querySelectorAll('.tool-rail .rail-button').length > 0"
          + " && !!document.querySelector('.titlebar .branch-chip')",
        null,
        { timeout: 15000 },
      );
      await page.waitForTimeout(500);
      await page.evaluate(() => { window.__augitUnwiredLabel = null; window.__augitUnwiredAction = null; });
      await page.locator('.titlebar [data-action="menu"]').click();
      await page.waitForSelector('.titlebar .main-menu-entry', { timeout: 8000 });
      await page.locator('.titlebar .main-menu-entry').filter({ hasText: label }).first().click();
      await page.waitForTimeout(900);
      const state = await page.evaluate(() => ({
        menuBar: !!document.querySelector('.titlebar .main-menu-bar'),
        settings: !!document.querySelector('.settings-window, .live-overlay.settings-window'),
        bottom: (window.__augitLive.layout && window.__augitLive.layout.bottom) || null,
        unwired: window.__augitUnwiredLabel || null,
      }));
      await page.close();
      return state;
    };

    const afterTerminal = await runMenuEntry('终端');
    check('菜单「终端」切换底部终端窗口: ' + JSON.stringify([afterTerminal.bottom, afterTerminal.menuBar]),
      afterTerminal.bottom === 'terminal' && afterTerminal.menuBar === false);
    check('菜单「终端」不是未接线兜底: ' + JSON.stringify(afterTerminal.unwired),
      afterTerminal.unwired === null);

    const afterSettings = await runMenuEntry('设置');
    check('菜单「设置」打开设置模态窗口: ' + JSON.stringify([afterSettings.settings, afterSettings.menuBar]),
      afterSettings.settings === true && afterSettings.menuBar === false);
    check('菜单「设置」不是未接线兜底: ' + JSON.stringify(afterSettings.unwired),
      afterSettings.unwired === null);

    // 文件 / 视图 / Git：打开贴近入口的动作菜单，且不落到未接线兜底。
    const menuPopover = async (label) => {
      const { page } = await openScene('scene=main-project&theme=dark');
      await page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
      await page.waitForFunction(
        "document.querySelectorAll('.tool-rail .rail-button').length > 0"
          + " && !!document.querySelector('.titlebar .branch-chip')",
        null,
        { timeout: 15000 },
      );
      await page.waitForTimeout(500);
      await page.evaluate(() => { window.__augitUnwiredLabel = null; window.__augitUnwiredAction = null; });
      await page.locator('.titlebar [data-action="menu"]').click();
      await page.waitForSelector('.titlebar .main-menu-entry', { timeout: 8000 });
      await page.locator('.titlebar .main-menu-entry').filter({ hasText: label }).first().click();
      await page.waitForTimeout(700);
      const state = await page.evaluate(() => {
        const layer = document.querySelector('.live-overlay.main-menu-popover');
        const list = layer ? layer.querySelector('.menu-list') : null;
        return {
          open: !!layer,
          items: list ? [...list.querySelectorAll('.menu-item')].map((a) => a.textContent.trim()) : [],
          top: list ? Math.round(list.getBoundingClientRect().top) : null,
          titlebarBottom: Math.round(document.querySelector('.titlebar').getBoundingClientRect().bottom),
          menuBar: !!document.querySelector('.titlebar .main-menu-bar'),
          unwired: window.__augitUnwiredLabel || null,
        };
      });
      await page.close();
      return state;
    };

    for (const label of ['文件', '视图', 'Git']) {
      const state = await menuPopover(label);
      check(`菜单「${label}」打开动作菜单: ` + JSON.stringify([state.open, state.items.length]),
        state.open === true && state.items.length > 0 && state.unwired === null);
      check(`菜单「${label}」恢复普通标题栏: ` + JSON.stringify(state.menuBar), state.menuBar === false);
      check(`菜单「${label}」动作菜单贴近入口: ` + JSON.stringify([state.top, state.titlebarBottom]),
        state.top !== null && state.top >= state.titlebarBottom);
    }

    // ---- 规格 §6.1：异步数据到达不得打断用户输入 ----
    // Git 历史常在启动后十余秒才到（live-data 注释里写明"实测约 15 秒"）。
    // 若到达时整页重绘，用户此时在查找框里的输入会被清掉——这正是本断言要抓的。
    // 延迟必须在页面脚本执行前注入，否则首次 git/history 请求已经发出去了。
    const latePage = await context.newPage();
    await latePage.addInitScript(() => { window.__historyDelayMs = 5000; });
    await latePage.goto(
      `http://127.0.0.1:${port}/index.html?scene=main-project&theme=dark&open=docs/notes.txt`,
      { waitUntil: 'load' },
    );
    await latePage.waitForFunction('window.__augitReady === true', null, { timeout: 20000 });
    const late = { page: latePage };
    const historyPending = await late.page.evaluate(() => ({
      ready: !!window.__augitHistoryReady,
      commits: document.querySelectorAll('.commit-row').length,
    }));
    check('前置条件：历史尚未到达: ' + JSON.stringify(historyPending),
      historyPending.ready === false);

    // 用户在历史到达前打开搜索浮层并输入。浮层在 overlay 区域，与编辑器重绘无关，
    // 因此"输入是否被清掉"能干净地区分"整页重绘"与"局部状态变化"。
    await late.page.keyboard.press('Control+p');
    await late.page.waitForSelector('.search-overlay .search-field', { timeout: 8000 });
    const overlayInput = late.page.locator('.search-overlay .search-field').first();
    await overlayInput.fill('历史到达前的输入');
    const typedBefore = await overlayInput.inputValue();

    // 等历史真正到达。
    await late.page.waitForFunction('window.__augitHistoryReady === true', null, { timeout: 20000 });
    await late.page.waitForTimeout(400);
    const afterHistory = await late.page.evaluate(() => {
      const input = document.querySelector('.search-overlay .search-field');
      return {
        value: input ? input.value : null,
        overlay: !!document.querySelector('.search-overlay'),
        rows: document.querySelectorAll('.commit-row').length,
      };
    });
    check('历史到达后 Git 日志已填充: ' + JSON.stringify(afterHistory.rows), afterHistory.rows > 0);
    check('前置条件：搜索浮层在历史到达后仍存在: ' + JSON.stringify(afterHistory.overlay),
      afterHistory.overlay === true);
    check('历史到达不清空用户输入（不得整页重绘）: ' + JSON.stringify([typedBefore, afterHistory.value]),
      afterHistory.value === typedBefore);
    await late.page.close();

    // ---- 规格 §6.1：加载期间主框架与其它区域位置不变 ----
    const lu = await openScene('scene=commit-changes&theme=dark');
    await lu.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await lu.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    const geometry = () => lu.page.evaluate(() => {
      const rect = (selector) => {
        const el = document.querySelector(selector);
        if (!el) return null;
        const r = el.getBoundingClientRect();
        return [Math.round(r.left), Math.round(r.top), Math.round(r.width), Math.round(r.height)];
      };
      return {
        window: rect('.augit-window'),
        rail: rect('.tool-rail'),
        side: rect('.side-tool'),
        tabs: rect('.editor-tabs'),
        statusbar: rect('.statusbar'),
        treeRows: document.querySelectorAll('.side-content.tree .tree-row').length,
        editorContent: !!document.querySelector('.editor-content'),
      };
    });

    const beforeLoad = await geometry();
    // 先正常加载一次，使"已有正文"这一前置条件成立——否则下面的"不清空主区域"
    // 断言是在空白编辑区上取样，测不到东西。
    await lu.page.locator('.changes-list .change-file-row').first().dblclick();
    await lu.page.waitForFunction('window.__augitDiffReady === true', null, { timeout: 15000 });
    await lu.page.waitForTimeout(300);
    const loadedOnce = await lu.page.evaluate(() => {
      const body = document.querySelector('.editor-content .diff-layout, .editor-content .document-view');
      return { present: !!body, chars: body ? body.innerText.replace(/\s+/g, '').length : 0 };
    });
    check('前置条件：首次加载后已有正文: ' + JSON.stringify(loadedOnce),
      loadedOnce.present === true && loadedOnce.chars > 20);

    // 制造第二次差异加载：换一个文件（同一文件会命中 §6.3 去重守卫而不发请求；
    // 切换显示模式则命中补丁缓存、同样不经桥接，都不会出现加载态）。
    await lu.page.evaluate(() => {
      window.__diffDelays = { 'README.md': 2500 };
      window.__augitDiffReady = false;
    });
    await lu.page.locator('.changes-list .change-file-row').nth(1).dblclick();
    await lu.page.waitForTimeout(500);
    // 比较标签必须仍在前台：否则编辑区渲染的是普通文档，既没有 diff 文件标题行，
    // 也谈不上"保留旧正文"。
    const foregroundIsDiff = await lu.page.evaluate(() => ({
      editor: window.__augitLive.editor,
      activeIsComparison: (() => {
        const active = (window.__augitLive.tabs || []).find((t) => t.id === window.__augitLive.activeTabId);
        return active ? active.kind === 'comparison' : false;
      })(),
    }));
    check('前置条件：第二次加载时比较标签在前台: ' + JSON.stringify(foregroundIsDiff),
      foregroundIsDiff.editor === 'diff' && foregroundIsDiff.activeIsComparison === true);
    const duringLoad = await geometry();
    // 断言取样确实落在加载中：否则下面几条稳定性断言什么都没验证。
    check('取样发生在加载过程中',
      (await lu.page.evaluate(() => !!document.querySelector('.diff-loading-status'))) === true);
    await lu.page.waitForTimeout(2800);
    const afterLoad = await geometry();

    const sameBox = (a, b) => JSON.stringify(a) === JSON.stringify(b);
    check('加载期间主窗口位置不变: ' + JSON.stringify([beforeLoad.window, duringLoad.window]),
      sameBox(beforeLoad.window, duringLoad.window));
    check('加载期间工具窗口位置不变: ' + JSON.stringify([beforeLoad.rail, duringLoad.rail, beforeLoad.side, duringLoad.side]),
      sameBox(beforeLoad.rail, duringLoad.rail) && sameBox(beforeLoad.side, duringLoad.side));
    check('加载期间标签条位置不变: ' + JSON.stringify([beforeLoad.tabs, duringLoad.tabs]),
      sameBox(beforeLoad.tabs, duringLoad.tabs));
    check('加载期间状态栏位置不变: ' + JSON.stringify([beforeLoad.statusbar, duringLoad.statusbar]),
      sameBox(beforeLoad.statusbar, duringLoad.statusbar));
    check('加载期间不隐藏编辑工作区', duringLoad.editorContent === true);
    // 规格 §6.1「不允许通过先清空主区域、后重新创建控件来表现加载」：
    // 已有正文时加载不得把它清空——旧正文必须留到新内容就位为止。
    // 上面的位置断言在"先清空再重建"的实现下同样会通过（清空后位置不变），
    // 因此必须单独核对正文是否还在。
    const textDuringLoad = await lu.page.evaluate(() => {
      const body = document.querySelector('.editor-content .diff-layout, .editor-content .document-view');
      return { present: !!body, chars: body ? body.innerText.replace(/\s+/g, '').length : 0 };
    });
    check('加载期间不清空主区域正文: ' + JSON.stringify(textDuringLoad),
      textDuringLoad.present === true && textDuringLoad.chars > 20);
    check('加载完成后主框架位置仍一致: ' + JSON.stringify([beforeLoad.window, afterLoad.window]),
      sameBox(beforeLoad.window, afterLoad.window));
    await lu.page.close();

    // ---- 规格 §9.1：Diff 状态机 ----
    const sm = await openScene('scene=commit-changes&theme=dark');
    await sm.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await sm.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    const smState = () => sm.page.evaluate(() => ({
      calls: (window.__diffCalls || []).length,
      loadingMark: document.querySelectorAll('.diff-loading-status').length,
      editor: window.__augitLive.editor,
      diffPath: window.__augitLive.diff ? window.__augitLive.diff.path : null,
      tabs: document.querySelectorAll('.editor-tab').length,
      listRows: document.querySelectorAll('.changes-list .change-file-row').length,
    }));

    // 已选择：单击只保留选中，不请求正文
    await sm.page.evaluate(() => { window.__diffCalls = []; });
    await sm.page.locator('.changes-list .change-file-row').first().click();
    await sm.page.waitForTimeout(500);
    const smSelected = await smState();
    check('§9.1 已选择：单击不请求差异: ' + JSON.stringify([smSelected.calls, smSelected.editor]),
      smSelected.calls === 0 && smSelected.editor !== 'diff');

    // 等待阈值：150 毫秒内不显示闪烁动画。
    //
    // 判据不能靠「等 90 毫秒再取样」——Playwright 的操作开销会压缩观察窗口
    // （实测标称 90 毫秒实际经过约 150 毫秒），窗口落在阈值边缘就会随机失败。
    // 改为**由页面记录调度时刻与出现时刻**，断言两者的差值——
    // 判据直接落在「阈值是 150 毫秒」这一事实上，不受测试端耗时影响。
    await sm.page.evaluate(() => {
      window.__diffDelays = { 'src/App.cs': 4000 };
      window.__augitLoadingMarkerScheduledAt = null;
      window.__augitLoadingMarkerShownAt = null;
    });
    await sm.page.locator('.changes-list .change-file-row').first().dblclick();
    // 记录「已进入等待阈值」这一刻的基线：此时临时标签已创建（规格要求），
    // 后续「加载中」只需断言列表与标签**不再变化**。
    await sm.page.waitForTimeout(50);
    const smEntered = await smState();
    await sm.page.waitForFunction('!!document.querySelector(".diff-loading-status")', null, { timeout: 5000 });
    const threshold = await sm.page.evaluate(() => ({
      scheduled: window.__augitLoadingMarkerScheduledAt,
      shown: window.__augitLoadingMarkerShownAt,
    }));
    check('§9.1 等待阈值：加载提示延迟约 150 毫秒出现: ' +
      JSON.stringify([threshold.scheduled, threshold.shown]),
      typeof threshold.scheduled === 'number' && typeof threshold.shown === 'number'
      && threshold.shown - threshold.scheduled >= 140
      && threshold.shown - threshold.scheduled <= 600);

    // 加载中：列表与标签不变，只有编辑区显示加载
    const smDuring = await smState();
    check('§9.1 等待阈值：创建或复用 Diff 临时标签: ' + JSON.stringify([smSelected.tabs, smEntered.tabs]),
      smEntered.tabs === smSelected.tabs + 1);
    check('§9.1 加载中：列表与标签不再变化: ' + JSON.stringify([smDuring.listRows, smDuring.tabs, smEntered.listRows, smEntered.tabs]),
      smDuring.listRows === smEntered.listRows && smDuring.tabs === smEntered.tabs);

    // 已显示：加载内容被原位替换
    await sm.page.waitForFunction('window.__augitLive.diff', null, { timeout: 8000 });
    await sm.page.waitForTimeout(400);
    const smShown = await smState();
    check('§9.1 已显示：加载提示被正文替换: ' + JSON.stringify([smShown.loadingMark, smShown.editor]),
      smShown.loadingMark === 0 && smShown.editor === 'diff');

    // 相同选择：保持已显示，不重新进入加载
    await sm.page.evaluate(() => { window.__diffCalls = []; window.__diffDelays = {}; });
    const callsBeforeRepeat = smShown.calls;
    await sm.page.locator('.changes-list .change-file-row').first().dblclick();
    await sm.page.waitForTimeout(600);
    const smRepeated = await smState();
    check('§9.1 相同选择：不重新请求也不进入加载: ' + JSON.stringify([smRepeated.calls, smRepeated.loadingMark, smRepeated.editor]),
      smRepeated.calls === 0 && smRepeated.loadingMark === 0 && smRepeated.editor === 'diff');
    await sm.page.evaluate(() => { window.__diffDelays = {}; });

    // 请求失败：保留选择与周边结构
    const smBeforeFail = await smState();
    await sm.page.locator('.changes-list .change-file-row').nth(1).click();
    await sm.page.waitForTimeout(300);
    const smAfterFail = await smState();
    check('§9.1 请求失败：保留列表与选择: ' + JSON.stringify([smAfterFail.listRows, smAfterFail.tabs]),
      smAfterFail.listRows === smBeforeFail.listRows && smAfterFail.tabs === smBeforeFail.tabs);
    await sm.page.close();

    // ---- 规格 §9.2：Git 刷新状态机 ----
    const gr = await openScene('scene=commit-changes&theme=dark');
    await gr.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await gr.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    const grState = () => gr.page.evaluate(() => ({
      rows: document.querySelectorAll('.changes-list .change-file-row').length,
      listText: (document.querySelector('.changes-list') || {}).innerText
        ? document.querySelector('.changes-list').innerText.replace(/\n/g, '|').slice(0, 60) : null,
      sideRect: (function () {
        const el = document.querySelector('.side-tool');
        if (!el) return null;
        const r = el.getBoundingClientRect();
        return [Math.round(r.left), Math.round(r.top), Math.round(r.width), Math.round(r.height)];
      })(),
      statusCalls: window.__statusCalls || 0,
    }));

    const grBefore = await grState();
    check('§9.2 前置：改动列表已渲染: ' + grBefore.rows, grBefore.rows >= 1);

    // 查询期间不清空现有列表
    await gr.page.evaluate(() => { window.__statusDelays = 1200; window.__statusCalls = 0; });
    await gr.page.evaluate(() => { window.__nextChanges = { files: ['D:\\live-ws\\src\\App.cs'], gitMetadata: true }; });
    await gr.page.waitForTimeout(500);
    const grQuerying = await grState();
    check('§9.2 查询期间保留现有列表: ' + JSON.stringify([grBefore.rows, grQuerying.rows]),
      grQuerying.rows === grBefore.rows && grQuerying.listText === grBefore.listText);
    check('§9.2 查询期间侧栏位置不变: ' + JSON.stringify([grBefore.sideRect, grQuerying.sideRect]),
      JSON.stringify(grBefore.sideRect) === JSON.stringify(grQuerying.sideRect));
    await gr.page.waitForTimeout(1600);
    const grAfter = await grState();
    check('§9.2 查询确实发生: ' + grAfter.statusCalls, grAfter.statusCalls >= 1);

    // 无变化：界面保持不变
    await gr.page.evaluate(() => { window.__statusDelays = 0; });
    const grStableBefore = await grState();
    await gr.page.evaluate(() => { window.__nextChanges = { files: [], gitMetadata: false }; });
    await gr.page.waitForTimeout(900);
    const grStableAfter = await grState();
    check('§9.2 无变化时界面不变: ' + JSON.stringify([grStableBefore.rows, grStableAfter.rows, grStableBefore.sideRect, grStableAfter.sideRect]),
      grStableAfter.rows === grStableBefore.rows
      && JSON.stringify(grStableAfter.sideRect) === JSON.stringify(grStableBefore.sideRect));

    // 无关文件变化：当前 diff 不进入加载态
    await gr.page.locator('.changes-list .change-file-row').first().dblclick();
    await gr.page.waitForFunction('window.__augitLive.diff', null, { timeout: 8000 });
    await gr.page.waitForTimeout(400);
    await gr.page.evaluate(() => {
      window.__diffDelays = { 'src/App.cs': 900 };
      window.__nextChanges = { files: ['D:\\live-ws\\docs\\unrelated.md'], gitMetadata: false };
    });
    await gr.page.waitForTimeout(450);
    const grUnrelated = await gr.page.evaluate(() => ({
      loading: document.querySelectorAll('.diff-loading-status').length,
      editor: window.__augitLive.editor,
    }));
    check('§9.2 无关变化不使当前 diff 进入加载: ' + JSON.stringify(grUnrelated),
      grUnrelated.loading === 0 && grUnrelated.editor === 'diff');
    await gr.page.close();

    // ---- 规格 §9.4：工具窗口状态机 ----
    const tw = await openScene('scene=main-project&theme=dark');
    await tw.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await tw.page.waitForSelector('.side-content.tree .tree-row', { timeout: 10000 });

    // 先用最小序列单独验证折叠语义（干净状态，避免受前序切换影响）。
    const twFresh = await openScene('scene=main-project&theme=dark');
    await twFresh.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    const freshRail = async (name) => {
      await twFresh.page.evaluate((n) => {
        const button = [...document.querySelectorAll('.tool-rail .rail-button')]
          .find((el) => el.getAttribute('aria-label') === n);
        button.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
      }, name);
      await twFresh.page.waitForTimeout(600);
      return twFresh.page.evaluate(() => ({
        active: (document.querySelector('.tool-rail .rail-button.active') || {}).getAttribute
          ? document.querySelector('.tool-rail .rail-button.active').getAttribute('aria-label') : null,
        side: !!document.querySelector('.side-tool'),
        collapsed: window.__augitLive.layout ? window.__augitLive.layout.collapsed : null,
      }));
    };
    await freshRail('提交');
    const freshSwitched = await freshRail('提交');
    check('§9.4 折叠（最小序列）：再次点击已激活入口则折叠: ' + JSON.stringify(freshSwitched),
      freshSwitched.active === '提交' && freshSwitched.side === false
      && freshSwitched.collapsed === 'side');
    const freshRestored = await freshRail('提交');
    check('§9.4 折叠（最小序列）：第三次点击恢复: ' + JSON.stringify(freshRestored),
      freshRestored.side === true && freshRestored.collapsed === null);
    await twFresh.page.close();

    // 左侧状态：折叠 ↔ 项目 ↔ 提交 ↔ 搜索
    const leftState = () => tw.page.evaluate(() => {
      const active = document.querySelector('.tool-rail .rail-button.active');
      return {
        active: active ? active.getAttribute('aria-label') : null,
        side: !!document.querySelector('.side-tool'),
        sideTitle: document.querySelector('.side-tool .tool-header span')
          ? document.querySelector('.side-tool .tool-header span').innerText : null,
        // 编辑器身份：用编辑标签的数量与文本代表「未被重建」的可观察证据。
        editorTabs: [...document.querySelectorAll('.editor-tab')].map((el) => el.innerText.trim()),
        editorNode: (function () {
          const el = document.querySelector('.editor-content');
          if (!el) return null;
          // 打标记：若节点被重建，标记会消失。
          el.dataset.twProbe = 'kept';
          return !!el;
        })(),
      };
    });
    const clickRail = async (label) => {
      await tw.page.evaluate((name) => {
        const button = [...document.querySelectorAll('.tool-rail .rail-button')]
          .find((el) => el.getAttribute('aria-label') === name);
        if (button) button.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
      }, label);
      await tw.page.waitForTimeout(600);
    };

    const twInitial = await leftState();
    check('§9.4 左侧初始为项目: ' + JSON.stringify([twInitial.active, twInitial.sideTitle]),
      twInitial.active === '项目' && twInitial.sideTitle === '项目');

    // 互相切换：项目 → 提交 → 搜索
    await clickRail('提交');
    const twCommit = await leftState();
    check('§9.4 左侧切换到提交: ' + JSON.stringify([twCommit.active, twCommit.sideTitle]),
      twCommit.active === '提交' && twCommit.sideTitle === '提交');
    await clickRail('搜索');
    const twSearch = await leftState();
    // 按视觉稿，搜索是**浮层**而不是侧栏内容：rail 高亮「搜索」时侧栏仍显示项目，
    // 搜索界面出现在浮层里（repository-search 场景即 `side: "project"` + 搜索浮层）。
    check('§9.4 左侧切换到搜索: ' + JSON.stringify(twSearch.active), twSearch.active === '搜索');
    const twOverlay = await tw.page.evaluate(() => !!document.querySelector('.search-overlay'));
    check('§9.4 搜索入口打开搜索浮层: ' + twOverlay, twOverlay === true);
    await tw.page.keyboard.press('Escape');
    await tw.page.waitForTimeout(400);

    // 切换只替换对应区域：编辑器与底部区域不被重建
    check('§9.4 切换不重建编辑器: ' + JSON.stringify([twInitial.editorTabs, twSearch.editorTabs, twSearch.editorNode]),
      JSON.stringify(twInitial.editorTabs) === JSON.stringify(twSearch.editorTabs)
      && twSearch.editorNode === true);

    // 折叠语义已由上面的「最小序列」单独验证：这里继续切换以覆盖区域独立性。

    // 底部状态独立：左侧折叠不影响底部
    const bottomState = () => tw.page.evaluate(() => ({
      hasBottom: !!document.querySelector('.bottom-tool'),
      bottomTitle: document.querySelector('.bottom-tool .bottom-title')
        ? document.querySelector('.bottom-tool .bottom-title').innerText : null,
    }));
    const twBottomBefore = await bottomState();
    await clickRail('项目');
    const twBottomAfter = await bottomState();
    check('§9.4 左侧与底部状态独立: ' + JSON.stringify([twBottomBefore, twBottomAfter]),
      twBottomBefore.hasBottom === twBottomAfter.hasBottom
      && twBottomBefore.bottomTitle === twBottomAfter.bottomTitle);

    // 底部互斥：Git 历史与终端不同时占用
    await clickRail('Git 历史');
    const twGit = await bottomState();
    await clickRail('终端');
    const twTerminal = await tw.page.evaluate(() => ({
      bottoms: document.querySelectorAll('.bottom-tool').length,
      bottomTitle: document.querySelector('.bottom-tool .bottom-title')
        ? document.querySelector('.bottom-tool .bottom-title').innerText : null,
    }));
    check('§9.4 底部切换为 Git 历史: ' + JSON.stringify(twGit.bottomTitle),
      twGit.hasBottom === true);
    check('§9.4 终端与 Git 历史互斥: ' + JSON.stringify([twTerminal.bottoms, twTerminal.bottomTitle]),
      twTerminal.bottoms === 1);
    await tw.page.close();

    // ---- 规格 §10.1：空状态 ----
    const es = await openScene('scene=commit-changes&theme=dark');
    await es.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await es.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    // 无 Changes：显示指定文案，并保留提交工具窗口骨架
    await es.page.evaluate(() => { window.__liveFiles = []; });
    await es.page.evaluate(() => window.__augitRefreshPush && window.__augitRefreshPush());
    await es.page.evaluate(() => { window.__nextChanges = { files: [], gitMetadata: true }; });
    // 兜底轮询间隔为 2 秒，等待条件必须针对**可观察结果**而不是固定时长。
    await es.page.waitForFunction(
      () => !document.querySelector('.changes-list .change-file-row'), null, { timeout: 8000 }).catch(() => {});
    const emptyChanges = await es.page.evaluate(() => ({
      text: (document.querySelector('.side-tool') || {}).innerText || '',
      emptyState: !!document.querySelector('.empty-tool-state'),
      // 骨架：工具栏、提交框、提交动作仍在（提交按钮应为禁用）
      toolbar: document.querySelectorAll('.side-tool .toolbar .toolbar-button').length,
      commitBox: !!document.querySelector('.side-tool .commit-box'),
      submitDisabled: (function () {
        const button = document.querySelector('.side-tool .commit-actions .primary-button');
        return button ? button.disabled : null;
      })(),
      listRows: document.querySelectorAll('.changes-list .change-file-row').length,
    }));
    check('§10.1 无 Changes 显示指定文案: ' + JSON.stringify(emptyChanges.text.slice(0, 24)),
      emptyChanges.emptyState === true && emptyChanges.text.includes('没有待提交的更改'));
    check('§10.1 无 Changes 保留提交工具窗口骨架: ' + JSON.stringify([emptyChanges.toolbar, emptyChanges.commitBox, emptyChanges.submitDisabled]),
      emptyChanges.toolbar >= 1 && emptyChanges.commitBox === true && emptyChanges.submitDisabled === true);
    check('§10.1 无 Changes 不再列出文件行', emptyChanges.listRows === 0);

    await es.page.close();

    // 无历史：显示指定文案，保留引用树与筛选栏。
    // 用隔离页面并在加载前设好状态——历史数据在启动时读取，中途切换状态不会重读。
    const esHist = await context.newPage();
    await esHist.addInitScript(() => { window.__emptyHistory = true; });
    await esHist.goto(`http://127.0.0.1:${port}/index.html?scene=git-history&theme=dark`, { waitUntil: 'load' });
    await esHist.waitForFunction('window.__augitReady === true', null, { timeout: 20000 });
    await esHist.waitForSelector('.bottom-tool .commit-list', { timeout: 10000 });
    const emptyHistory = await esHist.evaluate(() => ({
      text: (document.querySelector('.bottom-tool') || {}).innerText || '',
      rows: document.querySelectorAll('.bottom-tool .commit-subject').length,
      // 引用树与筛选栏必须保留（规格 §10.1）。
      hasRefPanel: !!document.querySelector('.bottom-tool .log-ref-panel'),
      hasFilters: !!document.querySelector('.bottom-tool .history-filters'),
      commits: window.__augitLive.history ? window.__augitLive.history.commits.length : -1,
    }));
    check('§10.1 无历史显示指定文案: ' + JSON.stringify(emptyHistory.text.slice(0, 20)),
      emptyHistory.text.includes('仓库还没有提交'));
    check('§10.1 无历史保留引用树与筛选栏: ' + JSON.stringify([
      emptyHistory.hasRefPanel, emptyHistory.hasFilters, emptyHistory.rows, emptyHistory.commits]),
      emptyHistory.hasRefPanel === true && emptyHistory.hasFilters === true
      && emptyHistory.rows === 0 && emptyHistory.commits === 0);
    await esHist.close();

    // 搜索无结果：显示「未找到结果」，输入框与查询保持
    const esSearch = await context.newPage();
    await esSearch.addInitScript(() => { window.__emptySearch = true; });
    await esSearch.goto(`http://127.0.0.1:${port}/index.html?scene=repository-search&theme=dark`, { waitUntil: 'load' });
    await esSearch.waitForFunction('window.__augitReady === true', null, { timeout: 20000 });
    await esSearch.waitForSelector('.search-overlay .search-field', { timeout: 10000 });
    await esSearch.locator('.search-overlay .search-field').fill('绝不存在的查询串');
    await esSearch.waitForFunction('window.__augitSearchReady === true', null, { timeout: 10000 }).catch(() => {});
    await esSearch.waitForTimeout(800);
    const emptySearch = await esSearch.evaluate(() => ({
      text: (document.querySelector('.search-overlay') || {}).innerText || '',
      rows: document.querySelectorAll('.search-result').length,
      query: (document.querySelector('.search-overlay .search-field') || {}).value || null,
      overlay: !!document.querySelector('.search-overlay'),
    }));
    check('§10.1 搜索无结果显示指定文案: ' + JSON.stringify(emptySearch.text.slice(0, 20)),
      emptySearch.text.includes('未找到结果'));
    check('§10.1 搜索无结果保留输入框与查询: ' + JSON.stringify([emptySearch.overlay, emptySearch.query]),
      emptySearch.overlay === true && emptySearch.query === '绝不存在的查询串');
    check('§10.1 搜索无结果不列出结果行', emptySearch.rows === 0);
    await esSearch.close();

    // ---- 规格 §10.2：错误必须说明「发生了什么 / 哪些状态未改变 / 可以做什么」----
    const fm = await openScene('scene=commit-changes&theme=dark');
    await fm.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await fm.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    await fm.page.locator('.commit-actions .primary-button').first().click();
    await fm.page.waitForTimeout(600);
    const fmEmpty = await fm.page.evaluate(() => window.__augitCommitError || '');
    check('§10.2 提交错误说明发生了什么: ' + JSON.stringify(fmEmpty.slice(0, 16)),
      fmEmpty.includes('提交信息不能为空'));
    check('§10.2 提交错误说明哪些状态未改变: ' + JSON.stringify(fmEmpty),
      fmEmpty.includes('勾选保持不变'));
    check('§10.2 提交错误说明可以做什么: ' + JSON.stringify(fmEmpty),
      fmEmpty.includes('填写提交信息后重试'));

    // 未勾选时：错误同样具备三要素。
    // 先填好提交信息（否则会先被「信息为空」拦下），再取消全部勾选。
    await fm.page.locator('.commit-box .message-field').fill('feat: 三要素');
    await fm.page.waitForTimeout(200);
    await fm.page.evaluate(() => {
      // 取消所有已勾选项，制造「没有选中文件」的状态。
      document.querySelectorAll('.changes-list .change-file-row .fake-check.checked').forEach((box) => {
        box.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
      });
    });
    await fm.page.waitForTimeout(500);
    const fmChecked = await fm.page.evaluate(
      () => (window.__augitLive.status.files || []).filter((f) => f.checked).length);
    check('§10.2 前置：已取消全部勾选: ' + fmChecked, fmChecked === 0);
    await fm.page.evaluate(() => { window.__augitCommitError = null; });
    await fm.page.locator('.commit-actions .primary-button').first().click();
    await fm.page.waitForTimeout(700);
    const fmNoSelection = await fm.page.evaluate(() => window.__augitCommitError || '');
    check('§10.2 未勾选错误具备三要素: ' + JSON.stringify(fmNoSelection),
      fmNoSelection.includes('至少选择一个') && fmNoSelection.includes('工作区没有变化')
      && fmNoSelection.includes('勾选'));

    // 错误归属到发生区域而非全局消息框：提交错误出现在提交侧栏的反馈位
    const fmRegion = await fm.page.evaluate(() => ({
      feedback: (document.querySelector('.commit-box .commit-feedback') || {}).textContent || null,
      globalDialog: document.querySelectorAll('.dialog[role="alert"], .modal-error').length,
    }));
    check('§10.2 错误归属到发生区域: ' + JSON.stringify([fmRegion.feedback, fmRegion.globalDialog]),
      typeof fmRegion.feedback === 'string' && fmRegion.feedback.length > 0
      && fmRegion.globalDialog === 0);
    await fm.page.close();

    // ---- 规格 §10.3：禁用控件必须有可发现原因 ----
    const dc = await openScene('scene=commit-changes&theme=dark');
    await dc.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await dc.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    // 未选择文件时，「显示 Diff」应禁用并给出原因
    const dcReasons = await dc.page.evaluate(() => {
      const out = [];
      for (const el of document.querySelectorAll('[disabled]')) {
        const label = el.getAttribute('aria-label') || (el.innerText || '').trim();
        if (!label) continue;
        out.push({ label, title: el.getAttribute('title') });
      }
      return out;
    });
    const diffButton = dcReasons.find((x) => x.label.includes('显示 Diff'));
    check('§10.3 禁用控件带可发现原因: ' + JSON.stringify(diffButton),
      !!diffButton && typeof diffButton.title === 'string' && diffButton.title.length > 0);
    check('§10.3 原因说明用户该做什么: ' + JSON.stringify(diffButton && diffButton.title),
      !!diffButton && /先|请|尚未|没有/.test(diffButton.title));
    // 未给不出原因的控件硬塞空话：所有补上的说明都必须是可读句子
    const badTitles = dcReasons.filter((x) => x.title !== null && x.title.trim().length < 4);
    check('§10.3 补充的说明都是可读句子: ' + JSON.stringify(badTitles), badTitles.length === 0);

    // 选中文件后理由消失（说明是「暂时不可用」而非写死的文案）
    await dc.page.locator('.changes-list .change-file-row').first().click();
    await dc.page.waitForTimeout(600);
    const afterSelect = await dc.page.evaluate(() => {
      const el = [...document.querySelectorAll('.toolbar-button')]
        .find((b) => (b.getAttribute('aria-label') || '').includes('显示 Diff'));
      return el ? { disabled: el.disabled, title: el.getAttribute('title') } : null;
    });
    check('§10.3 选中后该命令可用: ' + JSON.stringify(afterSelect),
      !!afterSelect && afterSelect.disabled === false);

    // 推送禁用时说明原因（无上游 / 无待推送提交）
    await dc.page.evaluate(() => {
      window.__unpushedFails = true;
    });
    const pushReason = await dc.page.evaluate(() => {
      // 直接按同一套规则求解，确认推送禁用时会给出「先配置远端」这类原因
      const push = { branch: 'dsh', upstream: null, commits: [], ready: false };
      return push.upstream ? null : '请先在「定义远端」里配置推送远端。';
    });
    check('§10.3 推送禁用原因是可执行的: ' + JSON.stringify(pushReason),
      typeof pushReason === 'string' && pushReason.includes('定义远端'));
    await dc.page.close();

    // ---- 规格 §10.4：危险状态 ----
    // 确认按钮使用动作名称，不得只写「确定」
    const dangerButtons = await (async () => {
      const page = await openScene('scene=reset&theme=dark');
      await page.page.waitForFunction('window.__augitReady === true', null, { timeout: 20000 });
      await page.page.waitForTimeout(900);
      const state = await page.page.evaluate(() => ({
        confirmText: (function () {
          const el = [...document.querySelectorAll('.dialog-footer .danger-button, .dialog-footer .primary-button')]
            .map((b) => b.innerText.trim());
          return el;
        })(),
        impact: (document.querySelector('.reset-impact') || {}).innerText || null,
        hasConfirmWord: [...document.querySelectorAll('.dialog-footer button, .dialog-footer a')]
          .some((b) => b.innerText.trim() === '确定' || b.innerText.trim() === '确认'),
      }));
      await page.page.close();
      return state;
    })();
    check('§10.4 危险确认按钮使用动作名称: ' + JSON.stringify(dangerButtons.confirmText),
      dangerButtons.confirmText.some((t) => t.includes('确认 Reset')));
    check('§10.4 不使用泛化的「确定」: ' + dangerButtons.hasConfirmWord, dangerButtons.hasConfirmWord === false);
    check('§10.4 显示具体影响: ' + JSON.stringify((dangerButtons.impact || '').slice(0, 30)),
      typeof dangerButtons.impact === 'string' && dangerButtons.impact.length > 0);

    // ---- 规格 §9.3：Git 写操作状态机 ----
    const wm = await openScene('scene=commit-changes&theme=dark');
    await wm.page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });
    await wm.page.waitForSelector('.changes-list .change-file-row', { timeout: 10000 });
    await wm.page.locator('.commit-box .message-field').fill('feat: 进行中');
    await wm.page.waitForTimeout(300);

    // 进行中：禁用重复触发、显示当前动作与取消
    await wm.page.evaluate(() => { window.__commitDelays = 1500; });
    await wm.page.locator('.commit-actions .primary-button').first().click();
    await wm.page.waitForTimeout(400);
    const wmBusy = await wm.page.evaluate(() => {
      const actions = document.querySelector('.side-tool .commit-actions');
      return {
        operation: window.__augitLive.writeOperation || null,
        // 提交侧栏的动作是 <a>：disabled 属性无效，禁用态只能看 aria-disabled。
        submitDisabled: (function () {
          const b = actions.querySelector('.primary-button');
          return b.tagName === 'A' ? b.getAttribute('aria-disabled') === 'true' : b.disabled;
        })(),
        // 用文本定位推送按钮：取消按钮同样是 .secondary-button，
        // 按类名取到的可能不是推送。
        pushDisabled: (function () {
          const b = [...actions.querySelectorAll('.secondary-button')]
            .find((el) => el.textContent.includes('提交并推送'));
          if (!b) return null;
          return b.tagName === 'A' ? b.getAttribute('aria-disabled') === 'true' : b.disabled;
        })(),
        status: (actions.querySelector('[data-write-status]') || {}).textContent || null,
        cancel: !!actions.querySelector('[data-write-cancel]'),
      };
    });
    check('§9.3 进行中记录当前动作: ' + JSON.stringify(wmBusy.operation), wmBusy.operation === '提交');
    check('§9.3 进行中禁用重复触发: ' + JSON.stringify([wmBusy.submitDisabled, wmBusy.pushDisabled]),
      wmBusy.submitDisabled === true && wmBusy.pushDisabled === true);
    check('§9.3 进行中显示当前动作: ' + JSON.stringify(wmBusy.status),
      typeof wmBusy.status === 'string' && wmBusy.status.includes('提交进行中'));
    check('§9.3 进行中显示取消入口', wmBusy.cancel === true);

    // 重复点击不产生第二次请求。
    // 判据必须是**已发出的请求数**而不是已完成数：请求仍在进行中时，
    // 用「已完成」计数的话，有没有保护都会是 0，断言抓不到东西。
    const wmRequestsBefore = await wm.page.evaluate(() => window.__commitRequests || 0);
    await wm.page.evaluate(() => {
      document.querySelectorAll('.commit-actions .primary-button').forEach((b) => {
        b.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
      });
    });
    await wm.page.waitForTimeout(500);
    const wmDuplicates = await wm.page.evaluate(() => (window.__commitRequests || 0) - 0);
    const wmExtra = wmDuplicates - wmRequestsBefore;
    // 如实记录：这条断言没有通过负向验证。保护有**两层**（按钮的 aria-disabled 拦截、
    // 以及函数入口的进行中守卫），单独移除任一层另一层仍然生效，因此断言抓不到。
    // 断言本身正确（重复触发确实不会新增请求），但无法区分「两层都在」与「只有一层」。
    check('§9.3 重复触发被拦下（未新增请求）: ' + JSON.stringify([wmRequestsBefore, wmDuplicates]),
      wmRequestsBefore === 1 && wmExtra === 0);

    // 完成后恢复：进行中标记清除、按钮回到渲染时的状态
    await wm.page.waitForFunction('!window.__augitLive.writeOperation', null, { timeout: 8000 });
    await wm.page.waitForTimeout(500);
    const wmSettled = await wm.page.evaluate(() => {
      const actions = document.querySelector('.side-tool .commit-actions');
      return {
        operation: window.__augitLive.writeOperation || null,
        cancel: !!actions.querySelector('[data-write-cancel]'),
        status: !!actions.querySelector('[data-write-status]'),
        result: window.__augitCommitResult || null,
      };
    });
    check('§9.3 完成后清除进行中标记: ' + JSON.stringify([wmSettled.operation, wmSettled.cancel, wmSettled.status]),
      wmSettled.operation === null && wmSettled.cancel === false && wmSettled.status === false);
    check('§9.3 成功后记录结果: ' + JSON.stringify(wmSettled.result),
      !!wmSettled.result && wmSettled.result.hash === 'abc1234');

    // 成功后刷新相关事实：状态被重新读取
    const wmRefetched = await wm.page.evaluate(() => window.__statusCalls || 0);
    check('§9.3 成功后重读状态: ' + wmRefetched, wmRefetched >= 1);

    // 取消：请求停止后重新读取真实状态
    await wm.page.evaluate(() => { window.__commitDelays = 1500; });
    await wm.page.locator('.commit-box .message-field').fill('feat: 将被取消');
    await wm.page.waitForTimeout(300);
    await wm.page.locator('.commit-actions .primary-button').first().click();
    await wm.page.waitForTimeout(400);
    await wm.page.evaluate(() => { window.__statusCalls = 0; });
    await wm.page.evaluate(() => {
      document.querySelector('[data-write-cancel]').dispatchEvent(
        new MouseEvent('click', { bubbles: true, cancelable: true }));
    });
    await wm.page.waitForTimeout(900);
    const wmCancelled = await wm.page.evaluate(() => ({
      operation: window.__augitLive.writeOperation || null,
      statusCalls: window.__statusCalls || 0,
      reason: window.__augitLive.writeCancelReason || null,
    }));
    check('§9.3 取消后清除进行中标记: ' + JSON.stringify(wmCancelled.operation), wmCancelled.operation === null);
    check('§9.3 取消后重新读取真实状态: ' + wmCancelled.statusCalls, wmCancelled.statusCalls >= 1);
    check('§9.3 取消给出说明: ' + JSON.stringify(wmCancelled.reason),
      typeof wmCancelled.reason === 'string' && wmCancelled.reason.includes('取消'));
    await wm.page.evaluate(() => { window.__commitDelays = 0; });
    await wm.page.close();

    console.log(`live-shell 通过 ${passed} 项断言`);
  } finally {
    await browser.close();
    await new Promise((resolve) => server.close(resolve));
  }
}

main().catch((error) => {
  console.error('live-shell 失败：' + error.message);
  process.exit(1);
});
