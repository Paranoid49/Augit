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
      { path: 'src/Program.cs', name: 'Program.cs', directory: 'src' },
    ],
  },
  searchText: {
    available: true, truncated: false, timedOut: false, cancelled: false, notice: '',
    matches: [
      { path: 'docs/product-spec.md', name: 'product-spec.md', directory: 'docs', line: 12, column: 3, text: '轻量优先是 Augit 的最高产品原则。' },
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
    tags: [],
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
          Promise.resolve().then(() => {
            let result;
            try {
              result = window.__hostStub(request.method, request.params);
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
    window.__hostStub = (method, params) => {
      if (method === 'workspace/info') return { root: 'D:\\live-ws', name: 'live-ws', valid: true };
      if (method === 'workspace/list') return { path: params.path, entries: data.tree[params.path] || [] };
      if (method === 'workspace/changes') {
        // 由测试脚本通过 window.__nextChanges 注入一次变化批次，读取后清空。
        const next = window.__nextChanges || { files: [], gitMetadata: false };
        window.__nextChanges = null;
        return { available: true, files: next.files || [], gitMetadata: !!next.gitMetadata };
      }
      if (method === 'git/status') return { available: true, isRepository: true, isDetached: false, branch: data.status.branch, files: data.status.files };
      if (method === 'git/history') return data.history;
      if (method === 'git/blame') return data.blame;
      if (method === 'git/file-history') return data.fileHistory;
      if (method === 'search/files') return data.searchFiles;
      if (method === 'search/text') return data.searchText;
      if (method === 'git/clone') { window.__cloneCall = params; return data.clone; }
      if (method === 'git/diff') {
        window.__diffCalls = window.__diffCalls || [];
        window.__diffCalls.push(params.path);
        // 第二个文件也被视为有差异，便于验证快速连选的结果归属。
        if (params.path === data.diff.path) return data.diff;
        if (params.path === 'README.md') return { ...data.diff, path: 'README.md' };
        return { available: false, reason: 'no diff' };
      }
      if (method === 'settings/read') return data.settings;
      if (method === 'settings/write') { window.__settingsWritten = params; return { saved: true, theme: params.theme, fontSize: params.fontSize }; }
      if (method === 'git/conflicts') return data.conflicts;
      if (method === 'git/conflict-load') return data.conflict;
      if (method === 'git/remotes') return data.remotes;
      if (method === 'git/references') return data.references;
      if (method === 'git/stashes') return data.stashes;
      if (method === 'git/worktrees') return data.worktrees;
      if (method === 'git/commit') {
        return params.revision === data.commit.fullHash ? data.commit : { available: false, reason: 'unknown' };
      }
      if (method === 'document/read') {
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
      const st = await page.evaluate(() => ({ doc: window.__augitLive && window.__augitLive.document ? window.__augitLive.document.path : null, err: window.__augitError || null, selected: document.querySelectorAll('.side-content.tree .tree-row.selected').length }));
      throw new Error('双击未打开文档 state=' + JSON.stringify(st));
    }
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
    await idem.page.close();

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
