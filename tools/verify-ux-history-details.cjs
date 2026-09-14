// 验证提交详情长正文、独立滚动和窄栏入口；使用现有浏览器，结束后关闭，不启动服务器。
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, executablePath, outputPath] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath, headless: true });
  let passed = 0;
  try {
    await fs.mkdir(outputPath, { recursive: true });
    for (const theme of ['light', 'dark']) {
      for (const size of [13, 40]) {
        for (const width of [1024, 1645]) {
          for (const scale of [1, 1.5]) {
            const context = await browser.newContext({ deviceScaleFactor: scale, viewport: { width, height: 1000 } });
            const page = await context.newPage();
            const errors = [];
            page.on('pageerror', error => errors.push(error.message));
            const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/git-history.html'));
            url.search = new URLSearchParams({ theme, 'ui-size': size, details: 'long' }).toString();
            await page.goto(url.href);
            await page.waitForSelector('body[data-typography-preview="ready"]');
            const details = page.locator('.commit-detail');
            await details.focus();
            await page.keyboard.press('End');
            await page.waitForFunction(() => { const detail = document.querySelector('.commit-detail'); return Math.abs(detail.scrollHeight - detail.clientHeight - detail.scrollTop) <= 1; });
            const state = await page.evaluate(() => {
              const detail = document.querySelector('.commit-detail');
              const panel = document.querySelector('.log-detail-panel');
              const actions = panel.querySelector('.history-detail-actions');
              const bounds = panel.getBoundingClientRect();
              return {
                last: detail.textContent.includes('最后一段必须可见。'),
                atEnd: Math.abs(detail.scrollHeight - detail.clientHeight - detail.scrollTop) <= 1,
                overflow: document.documentElement.scrollWidth > innerWidth,
                actionsVisible: !actions.hidden,
                actionsContained: [...actions.children].every(child => child.getBoundingClientRect().right <= bounds.right + 1),
                detailFits: detail.getBoundingClientRect().bottom <= bounds.bottom + 1,
                panelBottom: bounds.bottom, detailBottom: detail.getBoundingClientRect().bottom,
                scroll: [detail.scrollTop, detail.clientHeight, detail.scrollHeight],
              };
            });
            assert.ok(state.last && state.atEnd && state.detailFits, `${theme}/${size}/${width}/${scale} ${JSON.stringify(state)}`);
            assert.equal(state.overflow, false);
            if (state.actionsVisible) assert.ok(state.actionsContained);
            if (size === 40) assert.equal(state.actionsVisible, false);
            const selected = await page.locator('.commit-row.selected').getAttribute('data-hash');
            await page.keyboard.press('Home');
            await page.waitForFunction(() => document.querySelector('.commit-detail').scrollTop === 0);
            assert.equal(await page.locator('.commit-row.selected').getAttribute('data-hash'), selected);
            if (scale === 1 && width === 1024)
              await page.screenshot({ path: path.join(outputPath, `details-${theme}-${size}.png`) });
            await page.locator('.changed-files .file-status-modified').first().click({ button: 'right' });
            const menu = page.locator('.history-files-menu');
            await menu.waitFor();
            assert.deepEqual(await menu.locator('a').allTextContents(), ['显示 Diff', '文件历史', 'Blame']);
            await page.keyboard.press('Escape');
            assert.equal(await menu.count(), 0);
            assert.equal(await page.locator('.changed-files').evaluate(element => element === document.activeElement), true);
            await page.keyboard.press('Shift+F10');
            await menu.waitFor();
            await page.keyboard.press('ArrowDown');
            assert.equal(await page.evaluate(() => document.activeElement.textContent), '文件历史');
            await page.keyboard.press('Escape');
            assert.equal(await menu.count(), 0);
            assert.deepEqual(errors, []);
            await context.close();
            passed++;
          }
        }
      }
    }
    console.log(`通过 ${passed} 组提交详情独立滚动、字号、布局与文件菜单检查。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
