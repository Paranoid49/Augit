// 验证历史比较在同页复用、后台更新和关闭；只使用固定样本，不启动服务器。
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
async function main() {
  const [modulePath, browserPath] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: browserPath, headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1180, height: 760 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    let cases = 0;
    for (const theme of ['light', 'dark']) {
      const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/git-history.html'));
      url.searchParams.set('theme', theme);
      await page.goto(url.href);
      await page.waitForSelector('body[data-typography-preview="ready"]');
      const files = page.locator('.changed-files'), rows = files.locator('[data-history-path]');
      const tabs = page.locator('[data-history-comparison]'), view = page.locator('.history-follow-view');
      await page.evaluate(() => {
        window.retainedDocument = document.querySelector('.editor-content').firstElementChild;
        window.retainedLog = document.querySelector('.commit-list');
      });
      await rows.first().click();
      assert.equal(await tabs.count(), 0, '第一次单击只选择');
      await files.press('Enter');
      assert.equal(await tabs.count(), 1);
      assert.equal(await tabs.getAttribute('data-path'), 'docs/architecture.md');
      await rows.nth(1).click();
      assert.equal(await tabs.getAttribute('data-path'), 'docs/performance-report.md');
      assert.equal(await view.isVisible(), true);
      assert.equal(await files.evaluate(element => element === document.activeElement), true);
      await rows.nth(2).dblclick();
      assert.equal(await tabs.count(), 1, '双击不固定额外标签');
      assert.equal(await tabs.getAttribute('data-path'), 'docs/roadmap.md');
      await page.locator('.editor-tabs .editor-tab').filter({ hasText: 'product-spec.md' }).click();
      assert.equal(await view.isVisible(), false);
      await rows.first().click();
      assert.equal(await tabs.getAttribute('data-path'), 'docs/architecture.md');
      assert.equal(await view.isVisible(), false, '普通文档前台不抢占');
      const before = await tabs.getAttribute('data-commit');
      await page.locator('.commit-row').filter({ hasText: 'fix: 提升安装卸载与 Git 取消可靠性' }).click();
      assert.notEqual(await tabs.getAttribute('data-commit'), before);
      assert.equal(await view.isVisible(), false);
      assert.equal(await page.evaluate(() => window.retainedDocument === document.querySelector('.editor-content').firstElementChild
        && window.retainedLog === document.querySelector('.commit-list')), true);
      const currentPath = await tabs.getAttribute('data-path');
      await tabs.click();
      assert.equal(await view.isVisible(), true);
      assert.equal(await view.locator('.reference-path').textContent(), currentPath);
      await tabs.getByRole('button', { name: '关闭比较' }).click();
      await rows.first().click();
      await page.locator('.commit-row').nth(1).click();
      assert.equal(await tabs.count(), 0, '关闭后单击不重开');
      await rows.first().click({ button: 'right' });
      await page.locator('.history-files-menu .menu-item').filter({ hasText: '显示 Diff' }).click();
      assert.equal(await tabs.count(), 1);
      assert.equal(page.url(), url.href, '整个工作流不跳转页面');
      cases++;
    }
    assert.deepEqual(errors, []);
    console.log(`历史比较连续工作流通过：${cases} 个主题，单击、Enter、双击、文件/提交跟随、后台更新、返回和关闭。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
