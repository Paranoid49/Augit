// 验证 Rollback Changes 多文件树的字号布局、固定标题/底栏与树内滚动；仅加载本地视觉稿。
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, executablePath, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath, headless: true });
  let passed = 0;
  try {
    await fs.mkdir(output, { recursive: true });
    for (const dpi of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ deviceScaleFactor: dpi, viewport: { width: 1024, height: 640 } });
      try {
        const page = await context.newPage();
        await page.route('https://unpkg.com/**', route => route.abort());
        for (const theme of ['light', 'dark']) for (const size of [13, 40]) {
          const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/rollback.html'));
          url.searchParams.set('theme', theme); url.searchParams.set('ui-size', size);
          await page.goto(url.href);
          await page.waitForSelector('body[data-typography-preview="ready"]');
          const dialog = page.locator('.rollback-dialog'), body = dialog.locator('.dialog-body');
          assert.equal(await dialog.locator('.footer-help').count(), 0);
          assert.equal(await dialog.getAttribute('aria-label'), 'Rollback Changes');
          assert.equal(await dialog.locator('.rollback-tree .rollback-row').count(), 4);
          assert.equal(await dialog.locator('.rollback-summary').textContent(), '2 modified');
          assert.equal(await dialog.locator('[data-rollback-delete]').getAttribute('aria-disabled'), 'true');
          assert.equal(await dialog.locator('.dialog-footer [data-rollback-action="confirm"]').textContent(), 'Rollback');
          assert.deepEqual(await dialog.evaluate(node => {
            const rect = node.getBoundingClientRect(), body = node.querySelector('.dialog-body');
            const footer = node.querySelector('.dialog-footer').getBoundingClientRect();
            return { fits: rect.left >= 0 && rect.top >= 0 && rect.right <= innerWidth && rect.bottom <= innerHeight,
              horizontal: body.scrollWidth > body.clientWidth + 1,
              overlap: body.getBoundingClientRect().bottom > footer.top + 1,
              clipped: [...node.querySelectorAll('.dialog-footer button')].some(button => button.clientWidth < button.scrollWidth || button.clientHeight < parseFloat(getComputedStyle(button).fontSize)) };
          }), { fits: true, horizontal: false, overlap: false, clipped: false });
          await dialog.locator('.rollback-leaf').first().focus();
          assert.equal(await dialog.locator('.rollback-leaf').first().evaluate(node => {
            const rect = node.getBoundingClientRect(), tree = node.closest('.rollback-tree').getBoundingClientRect();
            return rect.top >= tree.top - 1 && rect.bottom <= tree.bottom + 1;
          }), true);
          if (dpi === 1) await page.screenshot({ path: path.join(output, `rollback-${theme}-${size}.png`) });
          passed++;
        }
      } finally { await context.close(); }
    }
    console.log(`PASS=${passed} Rollback Changes 树、固定底栏与字号布局通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
