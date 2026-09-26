// 「结果过多」对话框的权威文案、按钮与布局验证；不启动服务器、不执行搜索。
//
// 权威 `UsageLimitUtil.showTooManyUsagesWarning`（platform/usageView/src/com/intellij/usages/UsageLimitUtil.java:26-34）：
// 标题 `find.excessive.usages.title` = "Too Many Results"、正文 `find.excessive.usage.count.prompt` =
// "Too many results found. Are you sure you wish to continue?"、按钮 `button.text.continue` / `button.text.abort`
// （警告图标），`MessageDialogBuilder.okCancel` 让 Continue 成为默认按钮。
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, browserPath, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  let passed = 0;
  try {
    await fs.mkdir(output, { recursive: true });
    for (const theme of ['light', 'dark']) {
      for (const viewport of [{ width: 1024, height: 640 }, { width: 1645, height: 900 }]) {
        const context = await browser.newContext({ viewport, deviceScaleFactor: 1 });
        try {
          const page = await context.newPage();
          const errors = [];
          page.on('pageerror', error => errors.push(error.message));
          const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/search-limited.html'));
          url.searchParams.set('theme', theme);
          await page.goto(url.href);
          const dialog = page.locator('.search-limit-dialog');
          await dialog.waitFor();

          assert.equal(await dialog.getAttribute('aria-label'), '结果过多');
          const text = await dialog.locator('.dialog-body').innerText();
          assert.ok(text.includes('结果已超过 1000 条'), `正文要说明到限：${text}`);
          assert.ok(text.includes('确定要继续搜索吗'), `正文要问是否继续：${text}`);
          assert.deepEqual(await dialog.locator('[data-search-limit-action]').allTextContents(), ['中止', '继续']);
          // 到限的搜索结果仍在对话框之下的浮层里（不丢结果）。
          assert.ok(await page.locator('.search-overlay .search-result').count() > 0, '对话框之下仍保留已找到的结果');

          // 模态覆盖整个窗口，且不越界。
          const [scrimBox, dialogBox, viewBox] = await Promise.all([
            page.locator('.search-limit-scrim').boundingBox(),
            dialog.boundingBox(),
            page.locator('.augit-window').boundingBox(),
          ]);
          assert.ok(scrimBox.width >= viewBox.width - 0.5 && scrimBox.height >= viewBox.height - 0.5,
            `${theme}/${viewport.width}：模态遮罩未覆盖窗口。`);
          assert.ok(dialogBox.x >= 0 && dialogBox.y >= 0
            && dialogBox.x + dialogBox.width <= viewport.width + 0.5
            && dialogBox.y + dialogBox.height <= viewport.height + 0.5,
          `${theme}/${viewport.width}：对话框越出窗口。`);
          assert.deepEqual(errors, []);
          passed++;
        } finally {
          await context.close();
        }
      }
    }
    for (const theme of ['light', 'dark']) {
      const page = await browser.newPage({ viewport: { width: 1180, height: 760 } });
      const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/search-limited.html'));
      url.searchParams.set('theme', theme);
      await page.goto(url.href);
      await page.locator('.search-limit-dialog').waitFor();
      await page.screenshot({ path: path.join(output, `search-limited-${theme}.png`) });
      await page.close();
      passed++;
    }
    console.log(`通过 ${passed} 组「结果过多」对话框的文案、按钮与布局验证。`);
  } finally {
    await browser.close();
  }
}

main().catch((error) => { console.error(error); process.exitCode = 1; });
