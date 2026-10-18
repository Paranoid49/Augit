// 使用现有 Playwright 与浏览器验证项目树无悬停底和缩进矩阵；不下载依赖，不启动服务器。
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
              background: getComputedStyle(document.querySelector('.side-content.tree .tree-row:not(.selected)')).backgroundColor,
            }));
            await row.hover();
            // 当前裁决遵循未安装 TreeHoverListener 的参考行为；表格 hover 不适用于项目树。
            const expected = before.background;
            assert.equal(await row.evaluate(element => getComputedStyle(element).backgroundColor), expected);
            const after = await page.evaluate(() => ({
              selected: document.querySelector('.side-content.tree .selected')?.textContent,
              focus: document.activeElement?.outerHTML,
              scroll: document.querySelector('.side-content.tree').scrollTop,
              background: getComputedStyle(document.querySelector('.side-content.tree .tree-row:not(.selected)')).backgroundColor,
            }));
            assert.deepEqual(after, before, '悬停不改选、不打开文件、不抢焦点或滚动。');
            assert.equal(page.url(), url.href);
            if (outputPath && dpi === 96 && size === 13)
              await page.screenshot({ path: path.join(outputPath, `tree-hover-${theme}.png`) });
            await page.mouse.move(700, 20);
            assert.equal(await row.evaluate(element => getComputedStyle(element).backgroundColor), expected);
            await selected.scrollIntoViewIfNeeded();
            const selectedColor = await selected.evaluate(element => getComputedStyle(element).backgroundColor);
            await selected.hover();
            assert.equal(await selected.evaluate(element => getComputedStyle(element).backgroundColor), selectedColor, '悬停不能覆盖选中背景。');
            assert.deepEqual(errors, []);
            // ClassicPainter的左缩进至少为展开画布半宽：默认max(7,16/2)+11=19。
            // 旧实现是四条固定 `depth-N` 规则写成 16px 步长、且超过 depth-4 会退回 6px，因此在
            // 每个DPI/字号均核对步长，注入depth-7验证深层节点。
            const indent = await page.evaluate(() => {
              const tree = document.querySelector('.side-content.tree');
              const px = (element) => parseFloat(getComputedStyle(element).paddingLeft);
              const numeric = (path) => parseFloat(getComputedStyle(tree.querySelector(`.tree-row.${path}`)).paddingLeft);
              const deep = document.createElement('div');
              deep.className = 'tree-row depth-7';
              deep.style.setProperty('--tree-depth', '7');
              tree.append(deep);
              const value = px(deep);
              deep.remove();
              return { d1: numeric('depth-1'), d2: numeric('depth-2'), d3: numeric('depth-3'), d7: value };
            });
            const scale=Math.max(.75,Math.round(size/12*4)/4),step=({13:19,40:62}[size]);
            assert.equal(indent.d2 - indent.d1, step, `项目树第 1→2 级按字号缩放左右子缩进：${JSON.stringify(indent)}`);
            assert.equal(indent.d3 - indent.d2, step, `项目树第 2→3 级按字号缩放左右子缩进：${JSON.stringify(indent)}`);
            assert.equal(indent.d7, Math.round(4*scale)+step*7, `项目树第 7 级必须保留缩放后的完整缩进：${JSON.stringify(indent)}`);
            if (theme === 'light' && dpi === 96 && size === 13) console.log('项目树缩进=' + JSON.stringify(indent));
            passed++;
          } finally { await context.close(); }
        }
      }
    }
    console.log(`项目树悬停矩阵 ${passed}/12 通过。`);
  } finally { await browser.close(); }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
