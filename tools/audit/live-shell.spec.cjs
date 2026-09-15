// 在无头 Chromium 中验证外壳的真实数据路径：用 addInitScript 模拟 WebView2 宿主，
// 因此不需要启动 Windows 应用即可验证桥接、目录展开与文档打开。
// 用法：node tools/audit/live-shell.spec.cjs <playwright 模块路径>
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');

const MODULE = process.argv[2] || '/root/.npm/_npx/e41f203b7505f1fb/node_modules/playwright';

// ES 模块在 file:// 下会被 CORS 拒绝，因此这里起一个最小静态服务；
// 真实运行时由 WebView2 的虚拟主机映射（https://augit.local/）承担同样职责。
const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
};

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
  blame: {
    available: true, path: 'docs/notes.txt',
    lines: [
      { number: 1, hash: 'aaa1111', fullHash: 'full-aaa', author: 'l49', date: '2026/9/15', summary: 'feat: 一', content: '第一行' },
      { number: 2, hash: 'aaa1111', fullHash: 'full-aaa', author: 'l49', date: '2026/9/15', summary: 'feat: 一', content: '第二行' },
      { number: 3, hash: 'bbb2222', fullHash: 'full-bbb', author: 'l49', date: '2026/9/14', summary: 'fix: 二', content: '第三行' },
    ],
  },
  fileHistory: {
    available: true, path: 'docs/notes.txt',
    commits: [
      { hash: 'bbb2222', fullHash: 'full-bbb', subject: 'fix: 文件历史一', author: 'l49', date: '2026/9/14 09:00' },
    ],
  },
  history: {
    available: true, isRepository: true,
    head: 'full-head-hash',
    hasNextPage: false,
    commits: [
      { hash: 'aaa1111', fullHash: 'full-head-hash', subject: 'feat: 真实提交一', author: 'l49', date: '2026/9/15 10:00', graph: '*', parents: ['bbb2222'], references: ['HEAD', 'dsh'] },
      { hash: 'bbb2222', fullHash: 'full-bbb2222', subject: 'fix: 真实提交二', author: 'l49', date: '2026/9/14 09:00', graph: '*', parents: [], references: [] },
    ],
  },
  status: {
    branch: 'live-branch',
    files: [
      { path: 'src/App.cs', name: 'App.cs', directory: 'src', group: 'Changes', kind: 'Modified', staged: false, workingTree: true },
      { path: 'README.md', name: 'README.md', directory: '', group: 'Changes', kind: 'Modified', staged: false, workingTree: true },
      { path: 'notes/draft.txt', name: 'draft.txt', directory: 'notes', group: 'UnversionedFiles', kind: 'Untracked', staged: false, workingTree: false },
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
  try {
    const context = await browser.newContext({ viewport: { width: 1180, height: 760 }, deviceScaleFactor: 1 });
    await context.addInitScript(() => {
      const listeners = [];
      window.chrome = {
        webview: {
          addEventListener: (type, handler) => { if (type === 'message') listeners.push(handler); },
          postMessage: (raw) => {
            const request = JSON.parse(raw);
            Promise.resolve().then(() => {
              const result = window.__hostStub(request.method, request.params);
              for (const handler of listeners) handler({ data: JSON.stringify({ id: request.id, result }) });
            });
          },
        },
      };
    });
    await context.addInitScript((data) => {
      window.__hostStub = (method, params) => {
        if (method === 'workspace/info') return { root: 'D:\\ws', name: 'ws', valid: true };
        if (method === 'workspace/list') return { path: params.path, entries: data.tree[params.path] || [] };
        if (method === 'document/read') {
          const found = data.documents[params.path];
          if (!found) throw new Error('not found: ' + params.path);
          return found;
        }
        if (method === 'git/history') {
          return data.history;
        }
        if (method === 'git/blame') {
          return data.blame;
        }
        if (method === 'git/file-history') {
          return data.fileHistory;
        }
        if (method === 'git/status') {
          return {
            available: true, isRepository: true, isDetached: false,
            branch: data.status.branch,
            files: data.status.files,
          };
        }
        throw new Error('unexpected method ' + method);
      };
    }, WORKSPACE);
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', (error) => errors.push(error.message));
    await page.goto(`http://127.0.0.1:${port}/index.html?scene=main-project&theme=dark`, { waitUntil: 'load' });
    try {
      await page.waitForFunction('window.__augitReady === true', null, { timeout: 15000 });
    } catch {
      const state = await page.evaluate(() => ({
        ready: window.__augitReady,
        err: window.__augitError,
        live: !!window.__augitLive,
        app: document.getElementById('app') ? document.getElementById('app').innerHTML.length : -1,
      })).catch(() => null);
      throw new Error(`等待 __augitReady 超时；状态=${JSON.stringify(state)}；页面错误=${JSON.stringify(errors)}`);
    }
    try {
      // 状态与历史到达后各会补一次重绘；先等全部就绪，避免点击被重绘打断。
    await page.waitForFunction('window.__augitGitReady === true && window.__augitHistoryReady === true', null, { timeout: 20000 });
    await page.waitForSelector('.side-content.tree .tree-row[data-tree-path="docs"]', { timeout: 8000 });
    } catch {
      const state = await page.evaluate(() => ({
        live: !!window.__augitLive,
        err: window.__augitError,
        app: document.getElementById('app') ? document.getElementById('app').innerHTML.length : -1,
        rows: document.querySelectorAll('.side-content.tree .tree-row').length,
      }));
      throw new Error('等待树超时 state=' + JSON.stringify(state));
    }

    check('无页面脚本错误: ' + JSON.stringify(errors.slice(0, 2)), errors.length === 0);
    check('注入真实工作区数据', await page.evaluate('!!window.__augitLive'));
    const tree = page.locator('.side-content.tree .tree-row');
    check('树显示根与一层', await tree.count() === 4);
    check('分支标签来自宿主', (await page.locator('.branch-chip').innerText()).includes('live-branch'));

    // 展开 docs：子项来自宿主
    const rowLocator = page.locator('.side-content.tree .tree-row[data-tree-path="docs"]');
    await rowLocator.click();
    try {
      await page.waitForFunction('document.querySelectorAll(".side-content.tree .tree-row").length === 6', null, { timeout: 8000 });
    } catch {
      const state = await page.evaluate(() => ({
        rows: document.querySelectorAll('.side-content.tree .tree-row').length,
        treeLen: window.__augitLive ? window.__augitLive.tree.length : -1,
        expanded: window.__augitLive ? window.__augitLive.tree.map((r) => r.path + (r.expanded ? '*' : '')) : null,
        err: window.__augitError || null,
      }));
      throw new Error('展开未发生 state=' + JSON.stringify(state));
    }
    check('展开后出现子项', await page.locator('.side-content.tree .tree-row[data-tree-path="docs/product-spec.md"]').count() === 1);

    // 打开真实 Markdown：预览必须来自文档内容，而不是视觉稿样例
    await page.locator('.side-content.tree .tree-row[data-tree-path="docs/product-spec.md"]').click();
    // 打开文档是异步的：必须等真实文档落到 live 状态，不能只等容器出现。
    try {
      await page.waitForFunction(
        'window.__augitLive && window.__augitLive.document && window.__augitLive.document.path === "docs/product-spec.md"',
        null,
        { timeout: 8000 });
    } catch {
      const state = await page.evaluate(() => ({
        doc: window.__augitLive && window.__augitLive.document ? window.__augitLive.document.path : null,
        err: window.__augitError || null,
        editor: window.__augitLive ? window.__augitLive.editor : null,
        rows: document.querySelectorAll('.side-content.tree .tree-row').length,
      }));
      throw new Error('打开文档未发生 state=' + JSON.stringify(state));
    }
    await page.waitForSelector('.markdown-document', { timeout: 8000 });
    const preview = await page.locator('.markdown-preview').innerHTML();
    check('预览来自真实内容', preview.includes('真实标题') && preview.includes('<strong>加粗</strong>') && preview.includes('<code>代码</code>'));
    check('预览不残留样例标题', !preview.includes('Augit 产品规格'));
    check('标签显示真实文件名', (await page.locator('.editor-tab.active').innerText()).includes('product-spec.md'));
    check('原文含真实 Markdown 源码', (await page.locator('.markdown-source').innerText()).includes('# 真实标题'));
    check('展开状态在重绘后保留', await page.locator('.side-content.tree .tree-row[data-tree-path="docs/notes.txt"]').count() === 1);

    // 打开真实纯文本
    await page.locator('.side-content.tree .tree-row[data-tree-path="docs/notes.txt"]').click();
    await page.waitForFunction(
      'window.__augitLive && window.__augitLive.document && window.__augitLive.document.path === "docs/notes.txt"',
      null,
      { timeout: 8000 });
    await page.waitForSelector('.code-view .code-line', { timeout: 8000 });
    const lines = await page.locator('.code-view .code-line').allInnerTexts();
    check('纯文本按行渲染', lines.length === 4 && lines[0].includes('第一行'));
    check('状态栏显示真实路径', (await page.locator('.statusbar .status-path').innerText()).includes('notes.txt'));

    // ?open= 启动参数：首次渲染前就应打开指定文档
    const bootPage = await context.newPage();
    await bootPage.goto(`http://127.0.0.1:${port}/index.html?scene=main-project&theme=dark&open=docs%2Fnotes.txt`, { waitUntil: 'load' });
    await bootPage.waitForFunction('window.__augitReady === true', null, { timeout: 15000 });
    const bootOpened = await bootPage.evaluate('window.__augitLive && window.__augitLive.document ? window.__augitLive.document.path : null');
    check('?open= 启动即打开指定文档', bootOpened === 'docs/notes.txt');
    const bootLines = await bootPage.locator('.code-view .code-line').allInnerTexts();
    check('?open= 内容是真实文件', bootLines.length === 4 && bootLines[0].includes('第一行'));
    await bootPage.close();

    // Changes 工具窗：必须渲染真实 Git 改动
    const changesPage = await context.newPage();
    await changesPage.goto(`http://127.0.0.1:${port}/index.html?scene=commit-changes&theme=dark`, { waitUntil: 'load' });
    await changesPage.waitForFunction('window.__augitGitReady === true', null, { timeout: 15000 });
    await changesPage.waitForSelector('.change-file-row', { timeout: 8000 });
    const changeFiles = await changesPage.locator('.change-file-row').evaluateAll((els) => els.map((e) => e.dataset.path));
    check('Changes 显示真实改动文件: ' + JSON.stringify(changeFiles), changeFiles.length === 3 && changeFiles.includes('src/App.cs'));
    const groups = await changesPage.locator('.check-group-row strong').allInnerTexts();
    check('Changes 分组来自 Git: ' + JSON.stringify(groups), groups.includes('Changes') && groups.includes('Unversioned Files'));
    check('分组计数正确', (await changesPage.locator('.check-group-row .commit-meta').first().innerText()).includes('2 个文件'));
    const modifiedCount = await changesPage.locator('.commit-count').innerText();
    check('提交区显示改动数: ' + modifiedCount, modifiedCount.includes('2 modified'));
    check('未跟踪文件默认不勾选', await changesPage.locator('.change-file-row[data-path="notes/draft.txt"] .fake-check.checked').count() === 0);
    await changesPage.close();

    // 底部 Git 日志：必须渲染真实提交
    const logPage = await context.newPage();
    await logPage.goto(`http://127.0.0.1:${port}/index.html?scene=main-project&theme=dark`, { waitUntil: 'load' });
    await logPage.waitForFunction('window.__augitHistoryReady === true', null, { timeout: 20000 });
    await logPage.waitForSelector('.commit-row', { timeout: 8000 });
    const subjects = await logPage.locator('.commit-subject').allInnerTexts();
    check('Git 日志显示真实提交: ' + JSON.stringify(subjects), subjects.length === 2 && subjects[0].includes('真实提交一'));
    check('提交行带完整哈希', await logPage.locator('.commit-row[data-full-hash="full-head-hash"]').count() === 1);
    const branchLabels = await logPage.locator('.branch-label').allInnerTexts();
    check('分支标签来自真实引用: ' + JSON.stringify(branchLabels), branchLabels.some((text) => text.includes('dsh')));
    const logBranches = await logPage.locator('.log-ref-panel .tree-row').allInnerTexts();
    check('引用树列出真实分支: ' + JSON.stringify(logBranches), logBranches.some((text) => text.includes('dsh')));
    await logPage.close();

    // Blame：必须渲染真实逐行归属
    const blamePage = await context.newPage();
    await blamePage.goto(`http://127.0.0.1:${port}/index.html?scene=blame&theme=dark&blame=docs%2Fnotes.txt`, { waitUntil: 'load' });
    await blamePage.waitForFunction('window.__augitLive && !!window.__augitLive.blame', null, { timeout: 15000 });
    await blamePage.waitForSelector('.blame-document .blame-row', { timeout: 8000 });
    const blameRows = await blamePage.locator('.blame-document .blame-row').count();
    check('Blame 行数与真实归属一致: ' + blameRows, blameRows === 3);
    const blameText = await blamePage.locator('.blame-document .blame-gutter').innerText();
    check('Blame 槽位含真实日期与作者', blameText.includes('2026/9/15') && blameText.includes('l49'));
    const blameBody = await blamePage.locator('.blame-document .code-view').innerText();
    check('Blame 正文为真实文件内容', blameBody.includes('第一行') && blameBody.includes('第三行'));
    check('Blame 不残留样例归属', !blameText.includes('2026/8/28'));
    await blamePage.close();

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
