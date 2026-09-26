// 大文件只读预览的警告横幅验证；不启动服务器、不执行 Git。
//
// 权威 `LargeFileNotificationProvider`（platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/text/
// LargeFileNotificationProvider.java:37-58）：`EditorNotificationPanel` 的 Warning 状态，文案
// `large.file.preview.notification` = "The file is too large ({0}). Showing a read-only preview of the first {1}."
// （`IdeBundle.properties:2120`），两个动作 `action.label.hide.notification`（"Hide notification"，本次隐藏）
// 与 `label.dont.show`（"Don't show again"，永久关闭）。
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
      for (const viewport of [{ width: 1024, height: 640 }, { width: 1180, height: 760 }]) {
        for (const state of ['visible', 'hidden']) {
          const context = await browser.newContext({ viewport, deviceScaleFactor: 1 });
          try {
            const page = await context.newPage();
            const errors = [];
            page.on('pageerror', error => errors.push(error.message));
            const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/text-viewer.html'));
            url.searchParams.set('theme', theme);
            url.searchParams.set('large-preview', state === 'hidden' ? 'hidden' : '1');
            await page.goto(url.href);
            const view = page.locator('.document-view');
            await view.waitFor();
            const banner = page.locator('[data-large-file-banner]');
            const content = page.locator('.code-view');

            if (state === 'visible') {
              await banner.waitFor();
              const text = await banner.innerText();
              assert.ok(text.includes('文件过大（21.0 MB）'), `警告要给出文件大小（权威 {0}）：${text}`);
              assert.ok(text.includes('前 2.4 MB 的只读预览'), `警告要给出预览上限（权威 {1}）：${text}`);
              assert.deepEqual(await banner.locator('[data-large-file-action]').allTextContents(),
                ['隐藏通知', '不再显示']);
              // 面板画在编辑器顶部：正文在它之后。
              const order = await view.evaluate((node) => {
                const panel = node.querySelector('[data-large-file-banner]');
                const code = node.querySelector('.code-view');
                return panel && code ? (panel.compareDocumentPosition(code) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0 : false;
              });
              assert.ok(order, '警告横幅必须排在正文之前（编辑器顶部）');
              // 面板不得越出编辑器区域。
              const [bannerBox, viewBox] = await Promise.all([banner.boundingBox(), view.boundingBox()]);
              assert.ok(bannerBox.x >= viewBox.x - 0.5
                && bannerBox.x + bannerBox.width <= viewBox.x + viewBox.width + 0.5,
              `${theme}/${viewport.width}：警告横幅越出编辑器区域。`);
            } else {
              assert.equal(await banner.count(), 0, '已隐藏时不得出现警告横幅');
              assert.ok(await content.count() > 0, '隐藏警告后正文仍在');
            }
            assert.deepEqual(errors, []);
            passed++;
          } finally {
            await context.close();
          }
        }
      }
    }
    for (const theme of ['light', 'dark']) {
      const page = await browser.newPage({ viewport: { width: 1180, height: 760 } });
      const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/text-viewer.html'));
      url.searchParams.set('theme', theme);
      url.searchParams.set('large-preview', '1');
      await page.goto(url.href);
      await page.locator('[data-large-file-banner]').waitFor();
      await page.screenshot({ path: path.join(output, `large-file-preview-${theme}.png`) });
      await page.close();
      passed++;
    }
    console.log(`通过 ${passed} 组大文件只读预览警告横幅的显示、隐藏与布局验证。`);
  } finally {
    await browser.close();
  }
}

main().catch((error) => { console.error(error); process.exitCode = 1; });
