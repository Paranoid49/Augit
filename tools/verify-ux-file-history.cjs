// 文件历史视觉稿的局部布局与连续单双栏切换验证；无服务器，浏览器在 finally 退出。
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
      for (const { width, height } of [
        { width: 1024, height: 640 },
        { width: 1180, height: 760 },
        { width: 1645, height: 900 },
      ]) {
        for (const state of ['ready', 'loading', 'failure', 'cancelled']) {
          await page.setViewportSize({ width, height });
          const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/file-history.html'));
          url.searchParams.set('theme', theme);
          url.searchParams.set('history-state', state);
          await page.goto(url.href);
          const preview = page.locator('.history-detail-pane .diff-layout');
          await preview.waitFor();
          const historyRow = page.locator('.history-row.selected');
          assert.deepEqual(await historyRow.locator('span').allTextContents(), [
            'I49', '2026/8/28 8:25', 'feat: 实现 Augit 阶段零至五功能',
          ]);
          assert.deepEqual(await historyRow.evaluate(row => [...row.children].slice(0, 2)
            .map(column => column.getBoundingClientRect().width)), [130, 90]);
          assert.ok(await historyRow.evaluate(row => row.getBoundingClientRect().width >= 360));
          assert.equal(await preview.locator('.reference-path').textContent(), 'docs/product-spec.md');
          assert.equal(await preview.locator('.diff-toolbar button').count(), 7);
          const bounds = await preview.evaluate(layout => {
            const right = layout.getBoundingClientRect().right;
            return [...layout.querySelector('.diff-toolbar').children].every(child => child.getBoundingClientRect().right <= right + 0.1);
          });
          assert.ok(bounds, `${theme}/${width}/${state}：比较按钮越过详情范围。`);
          if (state === 'ready') {
            const leftLines = preview.locator('.diff-side').first().locator('.diff-code-line');
            const rightLines = preview.locator('.diff-side').last().locator('.diff-code-line');
            assert.equal(await leftLines.count(), await rightLines.count(), '双栏必须用空行对齐插入后的上下文。');
            assert.equal(await leftLines.nth(13).textContent(), '');
            assert.equal(await leftLines.nth(13).getAttribute('class'), 'diff-code-line ');
            assert.equal(await leftLines.nth(15).textContent(), await rightLines.nth(15).textContent());
            const aligned = await preview.evaluate(layout => {
              const sides = layout.querySelectorAll('.diff-side');
              const gutter = layout.querySelector('.diff-gutter');
              const target = layout.querySelector('.reference-after');
              return Math.abs(target.getBoundingClientRect().left - sides[1].getBoundingClientRect().left) < 0.1
                && parseFloat(getComputedStyle(sides[0]).paddingTop) === 8
                && parseFloat(getComputedStyle(sides[0].firstElementChild).paddingLeft) === 13
                && gutter.getBoundingClientRect().width >= 84;
            });
            assert.ok(aligned, `${theme}/${width}：正文内边距、行号或版本标题没有对齐。`);
          }
          const normalText = await page.locator('.editor-area').count() ? await page.locator('.editor-area').textContent() : '';
          await preview.getByRole('button', { name: '单栏', exact: true }).click();
          assert.equal(await preview.getAttribute('data-diff-mode'), 'unified');
          if (state === 'ready') {
            assert.equal(await preview.locator('.comparison-summary').textContent(), '1 处差异');
            assert.ok((await preview.locator('.diff-unified-body').textContent()).includes('保留文件标签、选择和滚动位置。'));
            assert.equal(await preview.locator('.diff-unified-body .removed').count(), 1);
            assert.equal(await preview.locator('.diff-unified-body .added').count(), 2);
          } else {
            assert.equal(await preview.locator('.comparison-summary').count(), 0);
            assert.equal(await preview.getByRole('button', { name: '上一处差异' }).isDisabled(), true);
          }
          if (normalText) assert.equal(await page.locator('.editor-area').textContent(), normalText);
          if (width === 1180 && state === 'ready') await page.screenshot({ path: path.join(outputPath, `${theme}-recommended.png`) });
          if (width === 1645 && state === 'ready') await page.screenshot({ path: path.join(outputPath, `${theme}-unified.png`) });
          await preview.getByRole('button', { name: '双栏', exact: true }).click();
          assert.equal(await preview.getAttribute('data-diff-mode'), 'side-by-side');
          if (width === 1645) await page.screenshot({ path: path.join(outputPath, `${theme}-${state}.png`) });
          passed++;
        }
      }
    }
    for (const size of [19, 40]) {
      const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/file-history.html'));
      url.searchParams.set('code-size', size);
      await page.goto(url.href);
      await page.waitForFunction(() => parseFloat(getComputedStyle(document.querySelector('.diff-columns')).fontSize) > 13);
      const fit = await page.locator('.diff-gutter').evaluate(gutter => [...gutter.children].every(column =>
        [...column.childNodes].filter(node => node.nodeType === Node.TEXT_NODE).every(node => {
          const range = document.createRange(); range.selectNodeContents(node);
          const bounds = range.getBoundingClientRect(), box = column.getBoundingClientRect();
          return bounds.left >= box.left + 6.9 && bounds.right <= box.right - 6.9;
        })));
      assert.ok(fit, `等宽 ${size}px：行号被裁切。`);
      passed++;
    }
    assert.deepEqual(errors, []);
    console.log(`通过 ${passed} 组文件历史结构、状态与单双栏连续切换验证。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
