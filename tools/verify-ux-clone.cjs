// 验证 Clone 视觉稿的布局、校验、失败保留、取消等待和重试流程；不调用 Git，也不启动服务器。
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, executablePath, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath, headless: true });
  const pageErrors = [];
  let passed = 0;
  try {
    await fs.mkdir(output, { recursive: true });
    const file = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/clone.html'));
    for (const dpi of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ deviceScaleFactor: dpi });
      try {
        const page = await context.newPage();
        page.on('pageerror', error => pageErrors.push(error.message));
        await page.route('https://unpkg.com/**', route => route.abort());
        for (const [width, height] of [[1024, 640], [1440, 900]]) for (const theme of ['light', 'dark']) for (const size of [13, 40]) {
          await page.setViewportSize({ width, height });
          const url = new URL(file); url.searchParams.set('theme', theme); url.searchParams.set('ui-size', size);
          url.searchParams.set('clone-result', 'failure,pending,success'); url.searchParams.set('long-error', '1');
          await page.emulateMedia({ colorScheme: theme });
          await page.goto(url.href); await page.waitForSelector('body[data-typography-preview="ready"]');
          const dialog = page.locator('.clone-dialog'), body = dialog.locator('.dialog-body');
          assert.equal(await dialog.locator('.footer-help').count(), 0);
          assert.equal(await dialog.locator('.dialog-header a svg').count(), 1);
          const geometry = await dialog.evaluate(node => {
            const r = node.getBoundingClientRect(), b = node.querySelector('.dialog-body').getBoundingClientRect(), f = node.querySelector('.dialog-footer').getBoundingClientRect();
            return { fits: r.left >= 0 && r.top >= 0 && r.right <= innerWidth && r.bottom <= innerHeight,
              horizontal: node.querySelector('.dialog-body').scrollWidth > node.querySelector('.dialog-body').clientWidth,
              footerStable: f.bottom <= r.bottom && b.bottom <= f.top + 1,
              clipped: [...node.querySelectorAll('input:not([type=checkbox]),select,button')].some(field => field.clientHeight < parseFloat(getComputedStyle(field).fontSize)) };
          });
          assert.deepEqual(geometry, { fits: true, horizontal: false, footerStable: true, clipped: false });
          if (size === 13) {
            const bounds = await dialog.boundingBox(); assert.equal(bounds.width, 930); assert.equal(bounds.height, 289);
            assert.equal(await body.evaluate(node => node.scrollHeight > node.clientHeight), false);
          }
          const footerBefore = await dialog.locator('.dialog-footer').boundingBox(), dialogBefore = await dialog.boundingBox();
          if (dpi === 1 && width === 1024) await page.screenshot({ path: path.join(output, `clone-initial-${theme}-${size}.png`) });
          assert.equal(await dialog.locator('#clone-depth').isDisabled(), true);
          await dialog.getByRole('button', { name: '克隆', exact: true }).click();
          assert.equal(await dialog.locator('.clone-notice').textContent(), '请输入仓库地址和目标目录。');
          assert.equal(await dialog.locator('#clone-source').evaluate(node => node === document.activeElement), true);
          await dialog.locator('#clone-source').fill('https://example.invalid/team/repo.git');
          await dialog.locator('#clone-destination').fill('D:\\clone-result');
          await dialog.locator('#clone-shallow').check();
          assert.equal(await dialog.locator('#clone-depth').isDisabled(), false);
          await dialog.locator('#clone-depth').fill('0');
          await dialog.getByRole('button', { name: '克隆', exact: true }).click();
          assert.equal(await dialog.locator('#clone-source').isDisabled(), false);
          assert.equal(await dialog.locator('.clone-notice').textContent(), '浅克隆深度必须是正整数。');
          await dialog.locator('#clone-depth').fill('7');
          await dialog.getByRole('button', { name: '克隆', exact: true }).click();
          assert.equal(await dialog.locator('#clone-source').isDisabled(), true);
          await page.waitForSelector('.clone-dialog[data-state="failure"]');
          assert.equal(await dialog.locator('#clone-source').inputValue(), 'https://example.invalid/team/repo.git');
          assert.equal(await dialog.locator('#clone-destination').inputValue(), 'D:\\clone-result');
          assert.equal(await dialog.locator('#clone-depth').inputValue(), '7');
          assert.equal(await dialog.locator('#clone-shallow').isChecked(), true);
          assert.equal(await body.evaluate(node => node.scrollHeight > node.clientHeight), true);
          assert.deepEqual(await dialog.boundingBox(), dialogBefore);
          assert.deepEqual(await dialog.locator('.dialog-footer').boundingBox(), footerBefore);
          await body.evaluate(node => node.scrollTop = node.scrollHeight);
          assert.equal(await dialog.locator('.clone-notice').evaluate(node => node.getBoundingClientRect().bottom <= node.closest('.dialog-body').getBoundingClientRect().bottom), true);
          if (dpi === 1 && width === 1024) await page.screenshot({ path: path.join(output, `clone-error-${theme}-${size}.png`) });
          await dialog.locator('#clone-source').focus();
          assert.equal(await dialog.locator('#clone-source').evaluate(node => {
            const bounds = node.getBoundingClientRect(), viewport = node.closest('.dialog-body').getBoundingClientRect();
            return bounds.top >= viewport.top && bounds.bottom <= viewport.bottom;
          }), true);
          await dialog.getByRole('button', { name: '克隆', exact: true }).click();
          await dialog.getByRole('button', { name: '取消操作', exact: true }).click();
          assert.equal(await dialog.getAttribute('data-state'), 'cancelling');
          assert.equal(await dialog.locator('#clone-source').isDisabled(), true);
          await page.waitForSelector('.clone-dialog[data-state="cancelled"]');
          assert.equal(await dialog.locator('.clone-notice').textContent(), '操作已取消。');
          assert.equal(await dialog.locator('#clone-depth').inputValue(), '7');
          assert.deepEqual(await dialog.locator('.dialog-footer').boundingBox(), footerBefore);
          await dialog.getByRole('button', { name: '克隆', exact: true }).click();
          await page.waitForSelector('.clone-dialog', { state: 'detached' });
          assert.equal(await dialog.count(), 0);
          assert.equal(await page.locator('.scrim').count(), 0);
          assert.equal(await page.locator('[inert]').count(), 0);
          passed++;
        }
      } finally { await context.close(); }
    }
    const page = await browser.newPage({ viewport: { width: 1024, height: 640 } });
    page.on('pageerror', error => pageErrors.push(error.message));
    await page.route('https://unpkg.com/**', route => route.abort());
    const pending = new URL(file); pending.searchParams.set('clone-result', 'pending'); pending.searchParams.set('ui-size', '13');
    await page.goto(pending.href); await page.waitForSelector('body[data-typography-preview="ready"]');
    const dialog = page.locator('.clone-dialog');
    await dialog.locator('#clone-version').focus();
    for (const selector of ['#clone-source', '#clone-destination', '#clone-shallow', '.secondary-button', '.primary-button', '.dialog-header a', '#clone-version']) {
      await page.keyboard.press('Tab'); assert.equal(await dialog.locator(selector).evaluate(node => node === document.activeElement), true);
    }
    await page.keyboard.press('Shift+Tab');
    assert.equal(await dialog.locator('.dialog-header a').evaluate(node => node === document.activeElement), true);
    await dialog.locator('#clone-shallow').focus(); await page.keyboard.press('Space'); await page.keyboard.press('Tab');
    assert.equal(await dialog.locator('#clone-depth').evaluate(node => node === document.activeElement), true);
    await dialog.locator('#clone-shallow').uncheck();
    await dialog.locator('#clone-depth').evaluate(node => node.value = '无效深度');
    await dialog.locator('#clone-source').fill('https://example.invalid/team/repo.git');
    await dialog.locator('#clone-destination').fill('D:\\clone-result');
    await dialog.locator('#clone-source').focus();
    await dialog.locator('#clone-source').dispatchEvent('compositionstart');
    await page.keyboard.press('Enter'); await page.keyboard.press('Escape');
    assert.equal(await dialog.count(), 1); assert.equal(await dialog.locator('#clone-source').isDisabled(), false);
    await dialog.locator('#clone-source').dispatchEvent('compositionend'); await page.keyboard.press('Enter');
    await dialog.getByRole('button', { name: '取消操作', exact: true }).click();
    assert.equal(await dialog.getByRole('button', { name: '取消操作', exact: true }).count(), 1);
    await page.waitForSelector('.clone-dialog[data-state="cancelled"]');
    assert.equal(await dialog.locator('.clone-notice').textContent(), '操作已取消。');
    await page.keyboard.press('Escape'); assert.equal(await dialog.count(), 0);
    const success = new URL(file); success.searchParams.set('clone-result', 'success');
    await page.goto(success.href); await page.waitForSelector('body[data-typography-preview="ready"]');
    await page.locator('#clone-source').fill('https://example.invalid/team/repo.git');
    await page.locator('#clone-destination').fill('D:\\clone-result');
    await page.getByRole('button', { name: '克隆', exact: true }).click();
    await page.waitForSelector('.clone-dialog', { state: 'detached' });
    assert.equal(await page.locator('.scrim').count(), 0);
    assert.deepEqual(pageErrors, []);
    console.log(`PASS=${passed + 2} Clone 布局、校验、失败保留、取消等待、重试与成功流程通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
