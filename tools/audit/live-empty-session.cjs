// 空会话必须显示真实空态；页面、浏览器和静态服务生命周期由调用方负责。
const assert = require('node:assert/strict');

function emptyWorkspaceHost() {
  const original = window.__hostStub;
  const query = new URLSearchParams(location.search);
  window.__emptyDocumentReads = [];
  window.__hostStub = async (method, params) => {
    if (method === 'workspace/info') return { root: 'D:\\empty-session', name: 'empty-session', valid: true };
    if (method === 'workspace/list') return { entries: params.path ? [] : [
      { name: 'App.txt', path: 'App.txt', isDirectory: false, canExpand: false },
    ] };
    if (method === 'settings/read') {
      const settings = await original(method, params);
      return { ...settings, openFiles: query.has('restore-async') ? ['App.txt'] : [],
        activeFile: query.has('restore-async') ? 'App.txt' : null, expandedDirectories: [] };
    }
    if (method === 'document/read') {
      window.__emptyDocumentReads.push(params.path);
      if (params.path !== 'App.txt') throw new Error('File not found: ' + params.path);
      if (query.has('restore-async') && !window.__emptyReadReleased) {
        window.__emptyReadStarted = true;
        await new Promise(resolve => {
          window.__releaseEmptyRead = () => { window.__emptyReadReleased = true; resolve(); };
        });
      }
      return { path: 'App.txt', name: 'App.txt', fullPath: 'D:\\empty-session\\App.txt',
        workspaceName: 'empty-session', kind: 'Text', status: 'TextReady',
        text: 'actual empty-workspace document\nsecond line', encoding: 'UTF-8', lineEndings: 'LF' };
    }
    if (method === 'git/blame') return { available: true, path: 'App.txt', lines: [
      { number: 1, hash: 'actual01', fullHash: 'actual-blame-01', author: 'author',
        date: '2026/10/11', dateTime: '2026/10/11 10:00', timestamp: 1791684000,
        summary: 'feat: actual file', content: 'actual empty-workspace document' },
    ] };
    return original(method, params);
  };
}

async function emptyState(page) {
  return page.evaluate(() => ({
    tabs: [...document.querySelectorAll('.editor-tabs .editor-tab')].map(node => node.textContent.trim()),
    body: document.querySelector('.editor-content')?.textContent.trim(),
    sampleBody: !!document.querySelector('.editor-content .markdown-document, .editor-content .diff-layout'),
    reads: [...window.__emptyDocumentReads],
    document: window.__augitLive?.document?.path || null,
  }));
}

async function assertEmpty(page) {
  const state = await emptyState(page);
  assert.deepEqual(state.tabs, [], JSON.stringify(state));
  assert.equal(state.body, 'Select a file to view its content', JSON.stringify(state));
  assert.equal(state.sampleBody, false, JSON.stringify(state));
  assert.equal(state.document, null);
  return state;
}

module.exports = async function verifyEmptySession({ openScene, check }) {
  const workspaceDiff = await openScene('scene=commit-diff&theme=dark&no-session-restore=1&diff=src%2FApp.cs', emptyWorkspaceHost);
  try {
    const { page } = workspaceDiff;
    await page.waitForSelector('.editor-tab.comparison-tab[data-tab-id]');
    assert.match(await page.locator('.editor-tab .change-tab-caption').innerText(), /src\/App\.cs/);
    assert.equal(await page.evaluate(() => window.__augitLive.workspaceDiff), true);
    await page.locator('.comparison-tab .tab-close').click();
    await page.waitForFunction(() => window.__augitLive.tabs.length === 0);
    await assertEmpty(page);
    assert.equal(await page.evaluate(() => window.__augitLive.workspaceDiff), false);
    await page.evaluate(() => window.__augitRenderRegions('editorContent', 'editorTabs'));
    await assertEmpty(page);
    assert.deepEqual(workspaceDiff.errors, []);
    check('显式工作区 Diff 保留真实可关闭标签，关闭后释放正文与导航状态且不恢复样例', true);
  } finally {
    await workspaceDiff.page.close();
  }
  for (const sceneName of ['main-project', 'git-history', 'file-history', 'commit-changes', 'commit-diff']) {
    const scene = await openScene(`scene=${sceneName}&theme=dark&no-session-restore=1`, emptyWorkspaceHost);
    try {
      const { page } = scene;
      await assertEmpty(page);
      assert.deepEqual(await page.evaluate(() => window.__augitLive.tree.filter(row => !row.isDirectory).map(row => row.path)), ['App.txt']);
      assert.deepEqual(await page.evaluate(() => window.__emptyDocumentReads), []);
      await page.evaluate(() => {
        window.__augitLive.tabs = [];
        window.__augitRenderRegions('editorContent', 'editorTabs');
      });
      await assertEmpty(page);
      assert.deepEqual(scene.errors, []);
      check(`${sceneName} 实时空工作区与空标签刷新不显示样例，也不读取任何文件`, true);
    } finally {
      await scene.page.close();
    }
  }

  for (const query of ['scene=git-history&theme=dark', 'scene=git-history&theme=dark&open=Missing.txt&no-session-restore=1']) {
    const scene = await openScene(query, emptyWorkspaceHost);
    try {
      await assertEmpty(scene.page);
      assert.deepEqual(scene.errors, []);
      check(`${query.includes('Missing') ? '显式启动文件读取失败' : '设置 openFiles=[] 正常恢复'}保持真实空态`, true);
    } finally {
      await scene.page.close();
    }
  }

  const opened = await openScene('scene=git-history&theme=dark&no-session-restore=1&open=App.txt', emptyWorkspaceHost);
  try {
    const { page } = opened;
    await page.waitForSelector('.editor-tabs [data-tab-id]');
    assert.deepEqual(await page.locator('.editor-tabs .change-tab-caption').allTextContents(), ['App.txt']);
    assert.match(await page.locator('.editor-content').innerText(), /actual empty-workspace document/);
    await page.evaluate(() => window.__augitRenderRegions('editorContent', 'editorTabs'));
    assert.match(await page.locator('.editor-content').innerText(), /actual empty-workspace document/);
    check('显式打开真实文件和区域刷新保留真实标签、内容', true);

    // 兼容旧调用者只注入真实 document、尚未建立 tabs 的路径。
    await page.evaluate(() => {
      window.__savedEmptyTabs = window.__augitLive.tabs;
      delete window.__augitLive.tabs;
      window.__augitRenderRegions('editorContent', 'editorTabs');
    });
    assert.equal(await page.locator('.editor-tabs .editor-tab').count(), 1);
    assert.match(await page.locator('.editor-tabs .editor-tab').innerText(), /App\.txt/);
    assert.match(await page.locator('.editor-content').innerText(), /actual empty-workspace document/);
    await page.evaluate(() => {
      window.__augitLive.tabs = window.__savedEmptyTabs;
      window.__augitRenderRegions('editorContent', 'editorTabs');
    });
    check('仅有真实 document 的兼容路径保持真实文档，不落回样例', true);

    await page.locator('.editor-tab .tab-close').click();
    await assertEmpty(page);
    await page.evaluate(() => window.__augitRenderRegions('editorContent', 'editorTabs'));
    await assertEmpty(page);
    check('关闭最后一个真实文件后保持空态，Git 场景刷新不恢复样例', true);

    await page.evaluate(() => window.__augitOpenDocument('Missing.txt'));
    await assertEmpty(page);
    assert.deepEqual(await page.evaluate(() => window.__emptyDocumentReads), ['App.txt', 'Missing.txt']);
    check('空会话打开不存在的文件失败后不造标签或样例正文', true);

    await page.locator('.rail-button[data-tool="commit"]').click();
    await page.waitForSelector('.changes-list .change-file-row[data-path="src/App.cs"]');
    await page.evaluate(() => window.__augitOpenChangeDiff('src/App.cs'));
    await page.waitForSelector('.editor-tab.comparison-tab[data-tab-id]');
    await page.waitForSelector('.editor-content .diff-columns');
    const comparisonBefore = await page.locator('.editor-tab.comparison-tab').getAttribute('data-tab-id');
    await page.evaluate(() => window.__augitRenderRegions('editorContent', 'editorTabs'));
    assert.equal(await page.locator('.editor-tab.comparison-tab').getAttribute('data-tab-id'), comparisonBefore);
    assert.match(await page.locator('.editor-content .diff-columns').innerText(), /new value/);
    assert.equal(await page.locator('.editor-tabs .editor-tab').count(), 1);
    check('仅有真实比较标签时保留 Diff 标签、正文和身份', true);

    await page.locator('.comparison-tab .tab-close').click();
    await assertEmpty(page);
    check('关闭唯一比较标签后返回真实空态', true);
    assert.deepEqual(opened.errors, []);
  } finally {
    await opened.page.close();
  }

  const restoring = await openScene('scene=git-history&theme=dark&restore-async=1', emptyWorkspaceHost);
  try {
    const { page } = restoring;
    await page.waitForFunction(() => window.__emptyReadStarted === true);
    assert.equal(await page.locator('.editor-tabs .editor-tab').count(), 0);
    assert.equal(await page.locator('.editor-content .markdown-document').count(), 0);
    await page.evaluate(() => window.__augitRenderRegions('editorContent', 'editorTabs'));
    assert.match(await page.locator('.editor-content').innerText(), /Loading App\.txt/);
    await page.evaluate(() => window.__releaseEmptyRead());
    await page.waitForFunction(() => window.__augitSessionRestored === true);
    assert.deepEqual(await page.locator('.editor-tabs .change-tab-caption').allTextContents(), ['App.txt']);
    assert.match(await page.locator('.editor-content').innerText(), /actual empty-workspace document/);
    assert.deepEqual(restoring.errors, []);
    check('异步恢复只显示真实读取占位，再激活恢复文档，不暴露静态样例', true);
  } finally {
    await restoring.page.close();
  }

  const blaming = await openScene('scene=git-history&theme=dark&blame=App.txt&no-session-restore=1', emptyWorkspaceHost);
  try {
    assert.equal(await blaming.page.locator('.editor-tabs .editor-tab').count(), 1);
    assert.match(await blaming.page.locator('.editor-tabs .editor-tab').innerText(), /App\.txt/);
    assert.match(await blaming.page.locator('.editor-content .blame-layout').innerText(), /actual empty-workspace document/);
    await blaming.page.evaluate(() => window.__augitRenderRegions('editorContent', 'editorTabs'));
    assert.equal(await blaming.page.locator('.editor-tabs .editor-tab').count(), 1);
    assert.match(await blaming.page.locator('.editor-content .blame-layout').innerText(), /actual empty-workspace document/);
    assert.deepEqual(blaming.errors, []);
    check('无普通标签的真实 Blame 保留合成文件标签与归属正文', true);
  } finally {
    await blaming.page.close();
  }

  const staticScene = await openScene('scene=git-history&theme=dark', () => {
    window.chrome = {};
    // 调用方等待 Git 就绪；无宿主视觉稿不查询 Git，本项只解除测试框架的等待门槛。
    window.__augitGitReady = true;
  });
  try {
    assert.equal(await staticScene.page.evaluate(() => !!window.__augitLive), false);
    assert.equal(await staticScene.page.locator('.editor-tabs .editor-tab').count(), 3);
    assert.match(await staticScene.page.locator('.editor-content .markdown-preview').innerText(), /Augit 产品规格/);
    assert.deepEqual(staticScene.errors, []);
    check('无宿主静态视觉稿仍保留样例标签和正文', true);
  } finally {
    await staticScene.page.close();
  }
};
