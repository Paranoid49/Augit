// 验证 Stash 管理视觉稿在主题、字号和 DPI 变化下保持双栏与独立详情滚动。
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, executablePath] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath, headless: true });
  let passed = 0;
  try {
    for (const dpi of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ deviceScaleFactor: dpi, viewport: { width: 1024, height: 640 } });
      try {
        const page = await context.newPage();
        await page.goto(pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/stash-manager.html')).href + `?theme=dark&ui-size=40`);
        await page.waitForSelector('body[data-typography-preview="ready"]');
        const result = await page.locator('.stash-manager-dialog').evaluate(dialog => {
          const body = dialog.querySelector('.dialog-body'), detail = dialog.querySelector('.management-detail'), footer = dialog.querySelector('.dialog-footer').getBoundingClientRect();
          const rect = dialog.getBoundingClientRect();
          return { fits: rect.left >= 0 && rect.top >= 0 && rect.right <= innerWidth && rect.bottom <= innerHeight,
            bodyOverflow: body.scrollHeight >= body.clientHeight, detailScroll: getComputedStyle(detail).overflowY === 'auto',
            footerInside: footer.bottom <= rect.bottom + 1, help: dialog.querySelector('.footer-help') !== null };
        });
        assert.equal(result.fits, true); assert.equal(result.bodyOverflow, true); assert.equal(result.detailScroll, true); assert.equal(result.footerInside, true); assert.equal(result.help, false);
        passed++;
      } finally { await context.close(); }
    }
  } finally { await browser.close(); }
  console.log(`PASS=${passed} Stash 管理视觉稿双栏、独立详情滚动和底栏固定通过。`);
}
main().catch(error => { console.error(error); process.exitCode = 1; });
