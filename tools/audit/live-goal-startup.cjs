// 工作区启动失败、等待截止、重试与乱序返回必须保持真实空态，页面由调用方释放。
const assert = require('node:assert/strict');

function startupHost() {
  const original = window.__hostStub;
  const mode = new URLSearchParams(location.search).get('startup-case');
  // 缩短界面等待门槛，不改 bridge 的查询超时；长任务由明确的测试闸门放行。
  const schedule = window.setTimeout.bind(window);
  window.setTimeout = (callback, ms, ...args) => schedule(callback, ms === 8000 ? 30 : ms, ...args);
  window.__goalWorkspaceAttempts = 0;
  window.__hostStub = async (method, params) => {
    if (method === 'workspace/info') {
      window.__goalWorkspaceAttempts++;
      if (!window.__goalWorkspaceRecovered) {
        if (mode === 'info-error') throw new Error('Cannot read workspace information.');
        if (mode === 'info-invalid') return {};
        if (mode === 'slow-info' || mode === 'stale-info') {
          return new Promise(resolve => { window.__goalReleaseInfo = resolve; });
        }
      }
    }
    if (method === 'workspace/list' && !window.__goalWorkspaceRecovered) {
      if (mode === 'root-error') throw new Error('Cannot read root directory.');
      if (mode === 'root-invalid') return { available: false, entries: [] };
    }
    return original(method, params);
  };
}

async function assertNoSamples(page) {
  assert.equal(await page.locator('.editor-tabs .editor-tab').count(), 0);
  assert.equal(await page.locator('.editor-content .markdown-document, .editor-content .diff-layout').count(), 0);
  assert.equal(await page.evaluate(() => window.__augitLive !== null), true);
  assert.equal(await page.locator('.side-content.tree .tree-row').count(), 0);
}

module.exports = async function verifyGoalStartup({ openScene, check }) {
  for (const mode of ['info-error', 'info-invalid', 'root-error', 'root-invalid', 'slow-info', 'stale-info']) {
    const scene = await openScene(`scene=main-project&theme=light&no-session-restore=1&startup-case=${mode}`, startupHost);
    const { page } = scene;
    try {
      await assertNoSamples(page);
      const delayed = mode.endsWith('info') && !mode.startsWith('info-');
      assert.equal(await page.evaluate(() => !!window.__augitLive.workspaceLoading), delayed);
      assert.equal(await page.evaluate(() => !!window.__augitLive.workspaceLoadError), !delayed);
      await page.waitForFunction(() => !!document.querySelector('.toast'));
      assert.match(await page.locator('.toast').innerText(), delayed ? /Loading Workspace/ : /Cannot Read Workspace/);
      check(`${mode} 启动仍可交互且不呈现静态样例标签、正文和文件树`, true);

      if (mode === 'slow-info') {
        // 用户在等待期间切换了工具窗口；信息晚到应保留同一个状态与焦点。
        await page.locator('.rail-button[data-tool="commit"]').click();
        await page.locator('.commit-box .message-field').fill('feat: keep startup draft');
        await page.evaluate(() => { window.__goalLiveBefore = window.__augitLive; });
        await page.evaluate(() => window.__goalReleaseInfo({ root: 'D:\\late-workspace', name: 'late-workspace', valid: true }));
        await page.waitForFunction(() => window.__augitLive.name === 'late-workspace');
        assert.equal(await page.evaluate(() => window.__goalLiveBefore === window.__augitLive), true);
        assert.equal(await page.locator('.commit-box .message-field').inputValue(), 'feat: keep startup draft');
        assert.equal(await page.evaluate(() => document.activeElement.matches('.commit-box .message-field')), true);
        assert.equal(await page.evaluate(() => window.__augitLive.layout.side), 'commit');
        await page.waitForFunction(() => !document.querySelector('.toast'));
        check('启动信息晚到保留状态对象、当前工具窗、草稿和输入焦点', true);
      } else {
        await page.evaluate(() => { window.__goalWorkspaceRecovered = true; });
        await page.keyboard.press('F5');
        await page.waitForFunction(() => window.__augitLive.name === 'live-ws' && !window.__augitLive.workspaceLoading);
        await page.waitForSelector('.side-content.tree .tree-row');
        assert.equal(await page.evaluate(() => window.__augitLive.workspaceLoadError), null);
        assert.equal(await page.evaluate(() => window.__augitLive.workspaceNotice), null);
        await page.waitForFunction(() => !document.querySelector('.toast'));
        assert.equal(await page.locator('.editor-tabs .editor-tab').count(), 0);
        if (mode === 'stale-info') {
          await page.evaluate(() => window.__goalReleaseInfo({ root: 'D:\\stale', name: 'stale', valid: true }));
          await page.evaluate(() => new Promise(resolve => setTimeout(resolve, 0)));
          assert.equal(await page.evaluate(() => window.__augitLive.name), 'live-ws');
          check('F5 新读取生效后旧启动返回不能覆盖新工作区信息', true);
        }
        check(`${mode} F5 只恢复实际工作区并清除启动失败或加载提示`, true);
      }
      assert.deepEqual(scene.errors, []);
    } finally {
      await page.close();
    }
  }
};
