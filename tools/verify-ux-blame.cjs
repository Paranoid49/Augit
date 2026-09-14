// 验证 Blame 三列、同步滚动及关闭后的原文上下文；不启动服务器。
// 用法：node tools/verify-ux-blame.cjs <Playwright 路径> <浏览器路径> [截图目录]
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

// 比较用户可见状态与节点身份；失焦样式或重新度量产生的空 style 属性不是内容变化。
function historyState() {
  const bottom = document.querySelector('.bottom-tool');
  return {
    text: bottom.textContent,
    filters: [...bottom.querySelectorAll('input')].map(input => input.value),
    selected: [...bottom.querySelectorAll('.commit-row.selected, .history-row.selected')].map(row => row.textContent),
    scroll: [...bottom.querySelectorAll('.commit-list, .commit-detail, .history-rows')].map(view => [view.scrollTop, view.scrollLeft]),
    bounds: [...['left', 'top', 'width', 'height']].map(key => bottom.getBoundingClientRect()[key])
  };
}

async function main() {
  const [modulePath, browserPath, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  const errors = [];
  let passed = 0;
  try {
    if (output) await fs.mkdir(output, { recursive: true });
    for (const scale of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ viewport: { width: 1180, height: 760 }, deviceScaleFactor: scale });
      try {
        const page = await context.newPage();
        page.on('pageerror', error => errors.push(error.message));
        for (const theme of ['light', 'dark']) for (const size of [13, 40]) for (const locate of [false, true]) {
          const label = `${theme}-${size}-${scale}-${locate ? 'locate' : 'close'}`;
          const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/blame.html'));
          url.search = new URLSearchParams({ theme, 'ui-size': '13', 'code-size': String(size) });
          await page.goto(url.href);
          await page.waitForSelector('body[data-typography-preview="ready"]');
          const initial = await page.evaluate(() => {
            const code = document.querySelector('.blame-layout > .code-view');
            const gutter = document.querySelector('.blame-gutter');
            const row = gutter.querySelector('.blame-row');
            const bounds = el => { const r = el.getBoundingClientRect(); return [r.left, r.top, r.right, r.bottom]; };
            return { text: code.textContent, count: code.querySelectorAll('.code-line').length,
              label: document.querySelector('.blame-document .commit-meta').textContent,
              first: bounds(code.querySelector('.code-line')), row: bounds(row),
              columns: [...row.children].map(bounds), gutter: bounds(gutter),
              tab: document.querySelector('.editor-tab.active').textContent.trim(),
              history: document.querySelector('.bottom-tool').innerHTML };
          });
          assert.equal(initial.tab, 'product-spec.md', label);
          assert.ok(initial.history.includes('历史: product-spec.md'), `${label} 初始保留文件历史`);
          assert.equal(initial.label, `${initial.count} 行归属`, label);
          assert.ok(Math.abs(initial.first[1] - initial.row[1]) < .02, label);
          assert.ok(Math.abs(initial.first[3] - initial.row[3]) < .02, label);
          let right = initial.gutter[0];
          for (const column of initial.columns) {
            assert.ok(column[0] >= right && column[2] <= initial.gutter[2], `${label} 三列不重叠或越界`);
            right = column[2];
          }
          const code = page.locator('.blame-layout > .code-view');
          await code.focus();
          await code.evaluate(view => { view.scrollTop = 100; });
          await page.waitForFunction(() => document.querySelector('.blame-gutter').scrollTop === document.querySelector('.blame-layout > .code-view').scrollTop);
          const after = await code.evaluate(view => ({ top: view.scrollTop, text: view.textContent }));
          assert.ok(after.top > 0, `${label} 必须实际滚动`);
          assert.equal(after.text, initial.text, label);
          const bodyHandle = await code.elementHandle();
          if (locate) {
            const visibleRow = await page.locator('.blame-gutter').evaluate(gutter => {
              const bounds = gutter.getBoundingClientRect();
              return [...gutter.children].findIndex(row => {
                const box = row.getBoundingClientRect();
                return box.top >= bounds.top && box.bottom <= bounds.bottom;
              });
            });
            assert.ok(visibleRow >= 0, label);
            await page.locator('.blame-row').nth(visibleRow).click();
            await page.waitForSelector('.commit-row[aria-selected="true"]');
            assert.equal(page.url(), url.href, `${label} 点击不得切换整页`);
            assert.equal(await page.evaluate(original => original === document.querySelector('.blame-layout > .code-view'), bodyHandle), true);
            assert.equal(await code.evaluate(view => view.scrollTop), after.top, label);
            assert.equal(await code.textContent(), initial.text, label);
            assert.equal(await page.locator('.commit-row').count(), 1, `${label} 按归属提交定位`);
            assert.equal(await page.locator('.commit-row.selected').getAttribute('data-hash'), 'commit-4', label);
            assert.equal(await page.locator('.commit-detail h3').textContent(), 'feat: 实现 Augit 阶段零至五功能', label);
            assert.equal(await page.locator('.history-tool-content').count(), 0, `${label} 解除文件历史布局`);
            const logHandle = await page.locator('.bottom-tool').elementHandle();
            // 由真实 Enter 触发同一归属行，重复定位保留正文与已有日志节点。
            await page.locator('.blame-row').nth(visibleRow).focus();
            await page.keyboard.press('Enter');
            assert.equal(await page.evaluate(original => original === document.querySelector('.bottom-tool'), logHandle), true);
            assert.equal(await page.locator('.blame-row.selected').count(), 1, label);
            assert.equal(await code.evaluate(view => view.scrollTop), after.top, label);
            await logHandle.dispose();
          }
          if (output && scale === 1 && size === 13) await page.screenshot({ path: path.join(output, `blame-${label}.png`) });
          await page.getByRole('button', { name: '关闭 Blame', exact: true }).focus();
          const historyBeforeClose = await page.evaluate(historyState);
          const bottomHandle = await page.locator('.bottom-tool').elementHandle();
          await page.keyboard.press('Enter');
          const closed = await page.evaluate(() => {
            const source = document.querySelector('.markdown-source');
            return { blame: !!document.querySelector('.blame-layout'), text: source.textContent, top: source.scrollTop,
              focus: document.activeElement === source, mode: document.querySelector('.markdown-document').dataset.markdownMode,
              icons: document.querySelectorAll('.document-toolbar button svg').length };
          });
          assert.equal(closed.blame, false, label);
          assert.equal(closed.text, initial.text, label);
          assert.equal(closed.top, after.top, label);
          assert.equal(closed.focus, true, label);
          assert.equal(closed.mode, 'source', label);
          assert.ok(closed.icons >= 3, `${label} 恢复对应原文工具栏图形`);
          assert.deepEqual(await page.evaluate(historyState), historyBeforeClose, `${label} 关闭保留底部上下文`);
          assert.equal(await page.evaluate(original => original === document.querySelector('.bottom-tool'), bottomHandle), true);
          assert.equal(await page.evaluate(original => original === document.querySelector('.markdown-source'), bodyHandle), true);
          await bottomHandle.dispose();
          await bodyHandle.dispose();
          passed++;
        }
      } finally { await context.close(); }
    }
    assert.deepEqual(errors, []);
    console.log(`PASS=${passed} Blame 主题、字号、DPI、三列、滚动、提交定位和关闭验证通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
