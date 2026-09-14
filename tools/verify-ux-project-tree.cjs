// 使用现有 Playwright 与浏览器验证项目树悬停；不下载依赖，不启动服务器。
// 用法：node tools/verify-ux-project-tree.cjs <Playwright 模块路径> <浏览器路径> [截图目录]
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, browserPath, outputPath] = process.argv.slice(2);
  assert.ok(modulePath && browserPath, '请提供现有 Playwright 与浏览器路径。');
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  let passed = 0;
  try {
    if (outputPath) await fs.mkdir(outputPath, { recursive: true });
    for (const theme of ['light', 'dark']) {
      for (const dpi of [96, 120, 144]) {
        for (const size of [13, 40]) {
          const context = await browser.newContext({ viewport: { width: 1180, height: 760 }, deviceScaleFactor: dpi / 96 });
          try {
            const page = await context.newPage();
            const errors = [];
            page.on('pageerror', error => errors.push(error.message));
            const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/main-project.html'));
            url.searchParams.set('theme', theme);
            url.searchParams.set('ui-size', String(size));
            await page.goto(url.href);
            await page.waitForSelector('body[data-typography-preview="ready"]');
            const row = page.locator('.side-content.tree .tree-row:not(.selected)').first();
            const selected = page.locator('.side-content.tree .tree-row.selected').first();
            await row.scrollIntoViewIfNeeded();
            const before = await page.evaluate(() => ({
              selected: document.querySelector('.side-content.tree .selected')?.textContent,
              focus: document.activeElement?.outerHTML,
              scroll: document.querySelector('.side-content.tree').scrollTop,
            }));
            await row.hover();
            const expected = theme === 'dark' ? 'rgb(45, 47, 51)' : 'rgb(241, 242, 244)';
            assert.equal(await row.evaluate(element => getComputedStyle(element).backgroundColor), expected);
            assert.equal(await row.evaluate(element => getComputedStyle(element).getPropertyValue('--augit-row-background').trim()), theme === 'dark' ? '#2d2f33' : '#f1f2f4');
            const after = await page.evaluate(() => ({
              selected: document.querySelector('.side-content.tree .selected')?.textContent,
              focus: document.activeElement?.outerHTML,
              scroll: document.querySelector('.side-content.tree').scrollTop,
            }));
            assert.deepEqual(after, before, '悬停不改选、不打开文件、不抢焦点或滚动。');
            assert.equal(page.url(), url.href);
            if (outputPath && dpi === 96 && size === 13)
              await page.screenshot({ path: path.join(outputPath, `tree-hover-${theme}.png`) });
            await page.mouse.move(700, 20);
            assert.notEqual(await row.evaluate(element => getComputedStyle(element).backgroundColor), expected);
            await selected.scrollIntoViewIfNeeded();
            const selectedColor = await selected.evaluate(element => getComputedStyle(element).backgroundColor);
            await selected.hover();
            assert.equal(await selected.evaluate(element => getComputedStyle(element).backgroundColor), selectedColor, '悬停不能覆盖选中背景。');
            assert.deepEqual(errors, []);
            passed++;
          } finally { await context.close(); }
        }
      }
    }
    console.log(`项目树悬停矩阵 ${passed}/12 通过。`);
  } finally { await browser.close(); }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
