// 验证 Stash 视觉稿的布局及连续反馈，样本操作不调用 Git，也不启动服务器。
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, executablePath, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath, headless: true });
  const errors = []; let passed = 0;
  try {
    await fs.mkdir(output, { recursive: true });
    for (const dpi of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ deviceScaleFactor: dpi, viewport: { width: 1024, height: 640 } });
      try {
        const page = await context.newPage();
        page.on('pageerror', error => errors.push(error.message));
        await page.route('https://unpkg.com/**', route => route.abort());
        for (const theme of ['light', 'dark']) for (const size of [13, 40]) {
          const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/stash.html'));
          url.searchParams.set('theme', theme); url.searchParams.set('ui-size', size);
          url.searchParams.set('stash-result', 'failure'); url.searchParams.set('long-error', '1');
          await page.goto(url.href); await page.waitForSelector('body[data-typography-preview="ready"]');
          const dialog = page.locator('.stash-dialog'), body = dialog.locator('.dialog-body');
          assert.equal(await dialog.locator('.footer-help').count(), 0);
          assert.equal(await dialog.locator('.dialog-header a svg').count(), 1);
          const geometry = await dialog.evaluate(node => {
            const r = node.getBoundingClientRect(), body = node.querySelector('.dialog-body'), footer = node.querySelector('.dialog-footer').getBoundingClientRect();
            return { fits: r.left >= 0 && r.top >= 0 && r.right <= innerWidth && r.bottom <= innerHeight,
              horizontal: body.scrollWidth > body.clientWidth, overlap: body.getBoundingClientRect().bottom > footer.top + 1,
              clipped: [...node.querySelectorAll('textarea,select,button')].some(field => field.clientHeight < parseFloat(getComputedStyle(field).fontSize)) };
          });
          assert.deepEqual(geometry, { fits: true, horizontal: false, overlap: false, clipped: false });
          if (dpi === 1) await page.screenshot({ path: path.join(output, `stash-initial-${theme}-${size}.png`) });
          const footerBefore = await dialog.locator('.dialog-footer').boundingBox();
          await dialog.locator('#stash-message').fill('第一行\n第二行');
          await dialog.locator('#stash-keep').check();
          await dialog.getByRole('button', { name: '创建 Stash', exact: true }).click();
          assert.equal(await dialog.locator('#stash-message').isDisabled(), true);
          await page.waitForSelector('.stash-dialog[data-state="failure"]');
          assert.equal(await dialog.locator('#stash-message').inputValue(), '第一行\n第二行');
          assert.equal(await dialog.locator('#stash-keep').isChecked(), true);
          assert.deepEqual(await dialog.locator('.dialog-footer').boundingBox(), footerBefore);
          assert.equal(await body.evaluate(node => node.scrollHeight > node.clientHeight), true);
          await dialog.locator('#stash-message').focus();
          assert.equal(await dialog.locator('#stash-message').evaluate(node => {
            const bounds = node.getBoundingClientRect(), parent = node.closest('.dialog-body').getBoundingClientRect();
            return bounds.top >= parent.top && bounds.bottom <= parent.bottom;
          }), true);
          if (dpi === 1) await page.screenshot({ path: path.join(output, `stash-${theme}-${size}.png`) });
          await dialog.getByRole('button', { name: '取消', exact: true }).click();
          assert.equal(await dialog.count(), 0);
          assert.equal(await page.locator('.scrim').count(), 0);
          passed++;
        }
        const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/stash.html'));
        url.searchParams.set('stash-result', 'pending');
        await page.goto(url.href); await page.waitForSelector('body[data-typography-preview="ready"]');
        await page.locator('#stash-root').focus();
        for (const selector of ['#stash-message', '#stash-keep', '.stash-dialog .secondary-button', '.stash-dialog .primary-button', '.stash-dialog .dialog-header a', '#stash-root']) {
          await page.keyboard.press('Tab'); assert.equal(await page.locator(selector).evaluate(node => node === document.activeElement), true);
        }
        await page.locator('.stash-dialog .primary-button').click();
        await page.locator('.stash-dialog .secondary-button').click();
        assert.equal(await page.locator('.stash-notice').textContent(), '操作已取消。');
        await page.keyboard.press('Escape'); assert.equal(await page.locator('.stash-dialog').count(), 0);
        await page.goto(pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/stash.html')).href);
        await page.waitForSelector('body[data-typography-preview="ready"]');
        await page.locator('.stash-dialog .primary-button').click();
        await page.waitForSelector('.stash-dialog', { state: 'detached' });
        assert.equal(await page.locator('.scrim').count(), 0);
      } finally { await context.close(); }
    }
    assert.deepEqual(errors, []);
    console.log(`PASS=${passed} Stash 布局、失败保留、滚动、键盘、成功及取消流程通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
