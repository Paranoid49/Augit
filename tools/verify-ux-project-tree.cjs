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
        // 悬停色按 权威 `List/Tree.hoverBackground` = `selection-bg-hovered`（浅 #00000008／深代码默认 #464A4D）（第 116 轮更新；原 rgb(241,242,244)/rgb(45,47,51) 属已删除的 --augit-blue-hover，无权威依据）。
            const expected = theme === 'dark' ? 'rgb(70, 74, 77)' : 'rgba(0, 0, 0, 0.03)';
            assert.equal(await row.evaluate(element => getComputedStyle(element).backgroundColor), expected);
            assert.equal(await row.evaluate(element => getComputedStyle(element).getPropertyValue('--augit-row-background').trim()), theme === 'dark' ? '#464a4d' : '#00000008');
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
            // 缩进步长 = 18px（权威 `Tree.leftChildIndent`(7) + `Tree.rightChildIndent`(11)，
            // `ClassicPainter.getRendererOffset()` 的 `(depth-1)*(left+right)`；2026 参考图 33 物理px ≈ 18.4）。
            // 旧实现是四条固定 `depth-N` 规则写成 16px 步长、且超过 depth-4 会退回 6px，因此在
            // dpi/size 的每一步都核对步长，并注入一个 depth-7 的行证明任意深度都按 18 递增。
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
            assert.equal(indent.d2 - indent.d1, 18, `项目树第 1→2 级步长应为 18px：${JSON.stringify(indent)}`);
            assert.equal(indent.d3 - indent.d2, 18, `项目树第 2→3 级步长应为 18px：${JSON.stringify(indent)}`);
            assert.equal(indent.d7, 4 + 18 * 7, `项目树第 7 级应按 18px 递增（4 + 18×7 = 130）：${JSON.stringify(indent)}`);
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
