// 验证回滚确认的大字号布局与正文焦点；仅加载本地视觉稿，不执行 Git。
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
          url.searchParams.set('theme', theme); url.searchParams.set('ui-size', size); url.searchParams.set('recycle', '1');
          await page.goto(url.href); await page.waitForSelector('body[data-typography-preview="ready"]');
          const dialog = page.locator('.rollback-dialog'), body = dialog.locator('.dialog-body');
          assert.equal(await dialog.locator('.footer-help').count(), 0);
          assert.equal(await dialog.locator('.rollback-recycle').isVisible(), true);
          assert.equal(await dialog.locator('.reference-source').textContent(), 'HEAD');
          assert.equal(await dialog.locator('.reference-target').textContent(), '工作区');
          assert.deepEqual(await dialog.evaluate(node => {
            const rect = node.getBoundingClientRect(), body = node.querySelector('.dialog-body');
            const footer = node.querySelector('.dialog-footer').getBoundingClientRect();
            return { fits: rect.left >= 0 && rect.top >= 0 && rect.right <= innerWidth && rect.bottom <= innerHeight,
              horizontal: body.scrollWidth > body.clientWidth,
              overlap: body.getBoundingClientRect().bottom > footer.top + 1,
              clipped: [...node.querySelectorAll('.dialog-footer a')].some(button => button.clientWidth < button.scrollWidth || button.clientHeight < parseFloat(getComputedStyle(button).fontSize)) };
          }), { fits: true, horizontal: false, overlap: false, clipped: false });
          const footer = await dialog.locator('.dialog-footer').boundingBox();
          if (size === 40) assert.equal(await body.evaluate(node => node.scrollHeight > node.clientHeight), true);
          await body.evaluate(node => { node.scrollTop = node.scrollHeight; });
          await dialog.locator('.rollback-comparison button').first().focus();
          assert.equal(await dialog.locator('.rollback-comparison button').first().evaluate(node => {
            const rect = node.getBoundingClientRect(), body = node.closest('.dialog-body').getBoundingClientRect();
            return rect.top >= body.top && rect.bottom <= body.bottom;
          }), true);
          assert.deepEqual(await dialog.locator('.dialog-footer').boundingBox(), footer);
          await body.evaluate(node => { node.scrollTop = 0; });
          if (dpi === 1) await page.screenshot({ path: path.join(output, `rollback-${theme}-${size}.png`) });
          passed++;
        }
      } finally { await context.close(); }
    }
    console.log(`PASS=${passed} 回滚风险说明、比较焦点与固定底栏通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
