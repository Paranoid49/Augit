// 验证提交图及列表局部交互，使用已有浏览器，不启动服务器。
// 用法：node tools/verify-ux-commit-graph.cjs <Playwright 路径> <浏览器路径> <证据目录>
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
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    for (const theme of ['light', 'dark']) {
      for (const width of [1024, 1645]) {
        for (const variant of ['', 'wide', 'filtered', 'page']) {
          const label = `${theme}-${width}-${variant || 'merge'}`;
          await page.setViewportSize({ width, height: 760 });
          const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/git-history-graph.html'));
          url.searchParams.set('theme', theme);
          url.searchParams.set('graph', variant);
          await page.goto(url.href);
          await page.evaluate(async () => { await document.fonts.ready; await new Promise(requestAnimationFrame); });
          const layout = await page.evaluate(() => {
            const list = document.querySelector('.commit-list');
            const rows = [...list.querySelectorAll('.commit-row')];
            window.graphEditorBefore = document.querySelector('.editor-area');
            window.graphListBefore = list;
            const bounds = element => { const r = element.getBoundingClientRect(); return { x: r.x, right: r.right, width: r.width }; };
            return { viewport: list.clientWidth, extent: list.scrollWidth, graph: rows[0].querySelector('svg').viewBox.baseVal.width,
              bodyOverflow: document.documentElement.scrollWidth > innerWidth,
              rows: rows.map(row => ({ row: bounds(row), graph: bounds(row.querySelector('svg')),
                subject: bounds(row.querySelector('.commit-subject')), author: bounds(row.querySelector('.commit-author')),
                date: bounds(row.querySelector('.commit-date')) })) };
          });
          assert.equal(layout.bodyOverflow, false, `${label}：整个窗口不应横向滚动。`);
          for (const row of layout.rows) {
            assert.ok(row.subject.x >= row.graph.right - 0.1, `${label}：提交图遮盖标题。`);
            assert.ok(row.subject.width >= 119.9, `${label}：标题可读宽度不足。`);
            assert.ok(row.author.width >= 12 && row.date.width >= 28, `${label}：元数据不可辨认。`);
            assert.ok(row.date.right <= row.row.right, `${label}：日期越界。`);
          }
          if (variant === 'wide') {
            assert.equal(layout.graph, 205);
            if (width === 1024) assert.ok(layout.extent > layout.viewport, `${label}：缺少横向滚动。`);
          }
          if (variant === 'filtered' || variant === 'page') {
            assert.ok(await page.evaluate(() => {
              const graph = buildCommitGraph([{ hash: 'merge', parents: ['main', 'hidden'] }, { hash: 'main', parents: [] }]);
              const solid = graph.rows[0].segments.find(segment => !segment[5]);
              const missing = graph.rows[0].segments.find(segment => segment[5]);
              return missing && missing[2] !== solid[2] && missing[3] < 1;
            }), `${label}：缺失父关系不能被可见父关系遮挡。`);
          }
          await page.locator('.commit-row').first().click();
          const firstTitle = await page.locator('.commit-row .commit-subject').first().textContent();
          assert.equal(await page.locator('.commit-detail h3').textContent(), firstTitle);
          const list = page.locator('.commit-list');
          await list.press('ArrowDown');
          const secondTitle = await page.locator('.commit-row .commit-subject').nth(1).textContent();
          assert.equal(await page.locator('.commit-detail h3').textContent(), secondTitle);
          assert.match(await page.locator('.changed-files').textContent(), /0 个文件/);
          const detailIdentity = await page.evaluate(() => {
            window.graphDetailBefore = document.querySelector('.commit-detail h3');
            return true;
          });
          assert.ok(detailIdentity);
          await page.locator('.commit-row').nth(1).click();
          assert.ok(await page.evaluate(() => graphDetailBefore === document.querySelector('.commit-detail h3')),
            `${label}：相同选择重复创建详情。`);
          if (!variant) {
            await list.press('End');
            assert.match(await page.locator('.changed-files').textContent(), /35 个文件/);
            await list.press('Home');
            assert.match(await page.locator('.changed-files').textContent(), /0 个文件/);
          }
          await page.evaluate(() => { const list = document.querySelector('.commit-list'); list.scrollLeft = list.scrollWidth; });
          const offset = await list.evaluate(element => element.scrollLeft);
          await list.press('ArrowUp');
          assert.equal(await list.evaluate(element => element.scrollLeft), offset, `${label}：选择提交重置横向偏移。`);
          assert.equal(page.url(), url.href, `${label}：单击提交跳转整页。`);
          assert.ok(await page.evaluate(() => graphEditorBefore === document.querySelector('.editor-area')
            && graphListBefore === document.querySelector('.commit-list')), `${label}：选择重建了周边区域。`);
          await list.evaluate(element => { element.scrollLeft = 0; });
          if (width === 1024) await page.screenshot({ path: path.join(outputPath, `${label}.png`) });
          assert.deepEqual(errors, []);
          passed++;
        }
      }
    }
    console.log(`通过 ${passed} 组提交图、窄栏和连续选择场景。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
