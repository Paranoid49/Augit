// 使用本机已有浏览器验证 Push 视觉稿；只模拟状态，不执行 Git 或启动服务器。
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, browserPath, outputPath] = process.argv.slice(2);
  assert.ok(modulePath && browserPath && outputPath, '请提供现有 Playwright、浏览器和证据目录。');
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  let layoutCases = 0, workflowCases = 0;
  try {
    await fs.mkdir(outputPath, { recursive: true });
    for (const theme of ['light', 'dark']) for (const dpi of [96, 120, 144]) for (const size of [13, 40]) for (const scene of ['push', 'push-no-remote']) {
      const context = await browser.newContext({ viewport: { width: 1024, height: 640 }, deviceScaleFactor: dpi / 96 });
      try {
        const page = await context.newPage(), errors = [];
        await page.route(/^https?:\/\//, route => route.abort());
        page.on('pageerror', error => errors.push(error.message));
        const url = pathToFileURL(path.resolve(__dirname, `../docs/ux-mockups/${scene}.html`));
        url.searchParams.set('theme', theme); url.searchParams.set('ui-size', String(size));
        await page.goto(url.href); await page.waitForSelector('body[data-typography-preview="ready"]');
        const geometry = await page.evaluate(() => {
          const rect = selector => (selector === '.push-dialog' ? document.querySelector(selector) : document.querySelector('.push-dialog').querySelector(selector))?.getBoundingClientRect().toJSON();
          return { dialog: rect('.push-dialog'), detail: rect('.management-detail'), list: rect('.management-list'),
            header: rect('.dialog-header'), footer: rect('.dialog-footer'), push: rect('.primary-button'), cancel: rect('.secondary-button'), tags: rect('.push-tags') };
        });
        assert.ok(geometry.dialog.left >= 0 && geometry.dialog.right <= 1024 && geometry.dialog.top >= 0 && geometry.dialog.bottom <= 640);
        for (const key of ['list', 'detail']) assert.ok(geometry[key].top >= geometry.header.bottom && geometry[key].bottom <= geometry.footer.top + 1, `${key} 不越过固定标题或底栏。`);
        assert.ok(geometry.cancel.right <= geometry.push.left && geometry.push.bottom <= geometry.dialog.bottom);
        assert.equal(await page.locator('.footer-help').count(), 0);
        if (scene === 'push-no-remote') {
          assert.ok(geometry.tags.bottom <= geometry.push.top);
          assert.equal(await page.locator('.push-dialog .primary-button').isDisabled(), true);
          assert.equal(await page.locator('.push-tags input').isDisabled(), true);
          assert.equal(await page.locator('.push-tags select').isDisabled(), true);
        } else {
          const row = page.locator('.push-commit').first(), second = page.locator('.push-commit').nth(1);
          assert.ok((await row.boundingBox()).height >= 27);
          await row.click(); await page.locator('.management-detail').focus();
          const selection = await row.evaluate(node => getComputedStyle(node).backgroundColor);
          await second.hover();
          assert.equal(await second.evaluate(node => getComputedStyle(node).backgroundColor), theme === 'dark' ? 'rgb(45, 47, 51)' : 'rgb(241, 242, 244)');
          await row.hover(); assert.equal(await row.evaluate(node => getComputedStyle(node).backgroundColor), selection);
        }
        if (dpi === 96 && size === 13) await page.screenshot({ path: path.join(outputPath, `${scene}-${theme}.png`) });
        assert.deepEqual(errors, []); layoutCases++;
      } finally { await context.close(); }
    }
    for (const result of ['error,success', 'pending', 'success']) {
      const context = await browser.newContext({ viewport: { width: 1180, height: 760 } });
      try {
        const page = await context.newPage();
        await page.route(/^https?:\/\//, route => route.abort());
        const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/push.html'));
        url.searchParams.set('push-result', result); url.searchParams.set('long-error', '1');
        await page.goto(url.href); await page.waitForSelector('body[data-typography-preview="ready"]');
        if (result === 'error,success') await page.screenshot({ path: path.join(outputPath, 'push-html-baseline.png') });
        const push = page.locator('.push-dialog .primary-button'), cancel = page.locator('.push-dialog .secondary-button');
        const before = await push.boundingBox();
        await push.click(); assert.equal(await push.isDisabled(), true);
        if (result === 'pending') {
          await cancel.click(); assert.match(await page.locator('.push-notice').textContent(), /正在取消推送/);
          await page.waitForFunction(() => document.querySelector('.push-dialog')?.dataset.state === 'cancelled');
          assert.equal(await push.isDisabled(), false); assert.equal(await page.locator('.push-notice').textContent(), '操作已取消。');
          await cancel.click();
        } else if (result === 'error,success') {
          await page.waitForFunction(() => document.querySelector('.push-dialog')?.dataset.state === 'error');
          assert.deepEqual(await push.boundingBox(), before);
          assert.equal(await page.locator('.push-notice').evaluate(node => getComputedStyle(node).color), 'rgb(199, 68, 64)');
          await page.locator('.management-detail').evaluate(node => node.scrollTop = node.scrollHeight);
          assert.ok(await page.locator('.management-detail').evaluate(node => node.scrollTop > 0));
          assert.deepEqual(await push.boundingBox(), before);
          await push.click();
        }
        await page.waitForSelector('.push-dialog', { state: 'detached' });
        assert.equal(await page.locator('.scrim').count(), 0);
        assert.equal(await page.locator('.augit-window > [inert]').count(), 0);
        workflowCases++;
      } finally { await context.close(); }
    }
    await fs.writeFile(path.join(outputPath, 'push-mockup.json'), JSON.stringify({ passed: true, layoutCases, workflowCases }, null, 2) + '\n');
    console.log(`Push 视觉稿 ${layoutCases} 组布局、${workflowCases} 组连续状态通过。`);
  } finally { await browser.close(); }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
