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
    available: true, isRepository: true, head: 'full-head-hash', hasNextPage: false,
    commits: [
      { hash: 'aaa1111', fullHash: 'full-head-hash', subject: 'feat: 真实提交一', author: 'l49', date: '2026/9/15 10:00', graph: '*', parents: ['bbb2222'], references: ['HEAD', 'dsh'] },
      { hash: 'bbb2222', fullHash: 'full-bbb2222', subject: 'fix: 真实提交二', author: 'l49', date: '2026/9/14 09:00', graph: '*', parents: [], references: [] },
    ],
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
      lineEndings: 'Lf', encoding: 'UTF-8',
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
      if (method === 'workspace/info') return { root: 'D:\\ws', name: 'ws', valid: true };
      if (method === 'workspace/list') return { path: params.path, entries: data.tree[params.path] || [] };
      if (method === 'git/status') return { available: true, isRepository: true, isDetached: false, branch: data.status.branch, files: data.status.files };
      if (method === 'git/history') return data.history;
      if (method === 'git/blame') return data.blame;
      if (method === 'git/file-history') return data.fileHistory;
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
      await page.waitForFunction('window.__augitReady === true', null, { timeout: 20000 });
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
    const { page, errors } = await openScene('scene=main-project&theme=dark');
    check('无页面脚本错误: ' + JSON.stringify(errors.slice(0, 2)), errors.length === 0);
    check('注入真实工作区数据', await page.evaluate('!!window.__augitLive'));
    check('树显示根与一层', await page.locator('.side-content.tree .tree-row').count() === 4);
    check('分支标签来自宿主', (await page.locator('.branch-chip').innerText()).includes('dsh'));

    await page.locator('.side-content.tree .tree-row[data-tree-path="docs"]').click();
    await page.waitForFunction('document.querySelectorAll(".side-content.tree .tree-row").length === 6', null, { timeout: 10000 });
    check('展开后出现子项', await page.locator('.side-content.tree .tree-row[data-tree-path="docs/product-spec.md"]').count() === 1);

    await page.locator('.side-content.tree .tree-row[data-tree-path="docs/product-spec.md"]').click();
    await page.waitForFunction('window.__augitLive && window.__augitLive.document && window.__augitLive.document.path === "docs/product-spec.md"', null, { timeout: 10000 });
    const preview = await page.locator('.markdown-preview').innerHTML();
    check('Markdown 预览来自真实内容', preview.includes('真实标题') && preview.includes('<strong>加粗</strong>'));
    check('预览不残留样例标题', !preview.includes('Augit 产品规格'));
    check('标签显示真实文件名', (await page.locator('.editor-tab.active').innerText()).includes('product-spec.md'));
    check('展开状态在重绘后保留', await page.locator('.side-content.tree .tree-row[data-tree-path="docs/notes.txt"]').count() === 1);

    await page.locator('.side-content.tree .tree-row[data-tree-path="docs/notes.txt"]').click();
    await page.waitForFunction('window.__augitLive && window.__augitLive.document && window.__augitLive.document.path === "docs/notes.txt"', null, { timeout: 10000 });
    const lines = await page.locator('.code-view .code-line').allInnerTexts();
    check('纯文本按行渲染', lines.length === 4 && lines[0].includes('第一行'));
    check('状态栏显示真实路径', (await page.locator('.statusbar .status-path').innerText()).includes('notes.txt'));

    // ---- 底部 Git 日志 ----
    const subjects = await page.locator('.commit-subject').allInnerTexts();
    check('Git 日志显示真实提交: ' + JSON.stringify(subjects), subjects.length === 2 && subjects[0].includes('真实提交一'));
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
