// 验证 Reset 的字号、正文滚动和连续反馈；仅使用本地视觉稿，不调用 Git。
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
          const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/reset.html'));
          url.searchParams.set('theme', theme); url.searchParams.set('ui-size', size);
          url.searchParams.set('reset-result', 'failure'); url.searchParams.set('long-error', '1');
          await page.goto(url.href); await page.waitForSelector('body[data-typography-preview="ready"]');
          const dialog = page.locator('.reset-dialog'), body = dialog.locator('.dialog-body');
          assert.equal(await dialog.locator('.footer-help').count(), 0);
          if (await body.evaluate(node => node.scrollWidth > node.clientWidth)) {
            console.log(JSON.stringify({ theme, size, dpi, bounds: await body.evaluate(node => ({
              width: node.clientWidth, scroll: node.scrollWidth,
              children: [...node.querySelectorAll('input,select,.form-grid,.reset-impact')].map(child => ({
                type: child.className, width: child.clientWidth, scroll: child.scrollWidth,
                min: getComputedStyle(child).minWidth, right: child.getBoundingClientRect().right - node.getBoundingClientRect().left,
              })),
            })) }));
          }
          assert.deepEqual(await dialog.evaluate(node => {
            const rect = node.getBoundingClientRect(), body = node.querySelector('.dialog-body');
            const footer = node.querySelector('.dialog-footer').getBoundingClientRect();
            return { fits: rect.left >= 0 && rect.top >= 0 && rect.right <= innerWidth && rect.bottom <= innerHeight,
              horizontal: body.scrollWidth > body.clientWidth,
              overlap: body.getBoundingClientRect().bottom > footer.top + 1,
              clipped: [...node.querySelectorAll('input,select,button')].some(field => field.clientHeight < parseFloat(getComputedStyle(field).fontSize)) };
          }), { fits: true, horizontal: false, overlap: false, clipped: false });
          const footer = await dialog.locator('.dialog-footer').boundingBox();
          await dialog.locator('#reset-mode').selectOption({ index: 0 });
          assert.equal(await dialog.locator('.reset-impact strong').textContent(), '仅移动 HEAD，索引和工作区保持不变');
          assert.equal(await dialog.locator('.reset-run').textContent(), '执行 Reset');
          await dialog.locator('#reset-mode').selectOption({ index: 2 });
          await dialog.locator('#reset-target').fill('main~2');
          if (dpi === 1) await page.screenshot({ path: path.join(output, `reset-initial-${theme}-${size}.png`) });
          await dialog.locator('.reset-run').click();
          assert.equal(await dialog.locator('#reset-target').isDisabled(), true);
          await page.waitForSelector('.reset-dialog[data-state="failure"]');
          assert.equal(await dialog.locator('#reset-target').inputValue(), 'main~2');
          assert.equal(await dialog.locator('#reset-mode').evaluate(node => node.selectedIndex), 2);
          assert.deepEqual(await dialog.locator('.dialog-footer').boundingBox(), footer);
          assert.equal(await body.evaluate(node => node.scrollHeight > node.clientHeight), true);
          await dialog.locator('#reset-target').focus();
          assert.equal(await dialog.locator('#reset-target').evaluate(node => {
            const child = node.getBoundingClientRect(), parent = node.closest('.dialog-body').getBoundingClientRect();
            return child.top >= parent.top && child.bottom <= parent.bottom;
          }), true);
          if (dpi === 1) await page.screenshot({ path: path.join(output, `reset-failure-${theme}-${size}.png`) });
          await dialog.locator('.secondary-button').click();
          assert.equal(await dialog.count(), 0);
          passed++;
        }
      } finally { await context.close(); }
    }
    console.log(`PASS=${passed} Reset 字号、正文滚动、模式说明及失败保留通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
