// 使用现有开发环境的 Playwright 和已安装浏览器验证视觉稿，不下载浏览器或引入应用依赖。
// 用法：node tools/verify-ux-history.cjs <playwright 模块路径> <浏览器 exe 路径> [截图目录]
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const fs = require('node:fs/promises');

async function main() {
  const [modulePath, browserPath, outputPath] = process.argv.slice(2);
  assert.ok(modulePath && browserPath, '请传入现有 Playwright 模块和浏览器路径。');
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  let passed = 0;
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    if (outputPath) await fs.mkdir(outputPath, { recursive: true });
    for (const scene of ['git-history', 'git-history-graph', 'git-compare',
      'history-diff-loading', 'history-diff-failure', 'history-diff-cancelled', 'diff-boundary']) {
      for (const theme of ['light', 'dark']) {
        for (const width of [1024, 1180, 1645]) {
          const label = `${scene} / ${theme} / ${width}`;
          await page.setViewportSize({ width, height: width === 1024 ? 640 : 760 });
          const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups', `${scene}.html`));
          url.searchParams.set('theme', theme);
          await page.goto(url.href);
          await page.evaluate(async () => {
            await document.fonts.ready;
            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
          });
          const result = await page.evaluate(() => {
            const rect = element => {
              const r = element.getBoundingClientRect();
              return { x: r.x, y: r.y, width: r.width, height: r.height, right: r.right, bottom: r.bottom };
            };
            return {
              rows: [...document.querySelectorAll('.commit-row')].map(row => ({
                row: rect(row),
                graph: rect(row.firstElementChild),
                subject: row.querySelector(':scope > .commit-subject')
                  ? rect(row.querySelector(':scope > .commit-subject')) : null,
                author: rect(row.querySelector('.commit-author')),
                date: rect(row.querySelector('.commit-date')),
              })),
              filters: [...document.querySelectorAll('.history-filter:not([hidden])')].map(button => ({
                button: rect(button), text: rect(button.querySelector('span')), icon: rect(button.querySelector('svg')),
              })),
              notice: document.querySelector('.comparison-notice')?.textContent.trim(),
              comparisonReferences: [...document.querySelectorAll('.reference-source, .reference-target')]
                .map(element => element.textContent),
              comparisonCaption: document.querySelector('.comparison-caption')?.textContent,
              diffModeIcons: [...document.querySelectorAll('.diff-toolbar button[aria-label="忽略空白"], .diff-toolbar button[aria-label="双栏"], .diff-toolbar button[aria-label="单栏"]')]
                .map(button => ({ label: button.getAttribute('aria-label'),
                  icon: button.querySelector('svg')?.getAttribute('data-augit-icon'),
                  bounds: button.querySelector('svg') ? rect(button.querySelector('svg')) : null })),
              loading: !!document.querySelector('.diff-loading-columns'),
              diffGroups: [...document.querySelectorAll('.diff-toolbar .segmented')].map(group => ({
                bounds: rect(group), background: getComputedStyle(group).backgroundColor,
                border: getComputedStyle(group).borderColor,
                buttons: [...group.children].map(button => ({ bounds: rect(button),
                  active: button.classList.contains('active'), background: getComputedStyle(button).backgroundColor })),
              })),
              comparisonToolbar: document.querySelector('.comparison-toolbar') ? {
                bounds: rect(document.querySelector('.comparison-toolbar')),
                buttons: [...document.querySelectorAll('.comparison-toolbar button')].map(button => ({
                  label: button.getAttribute('aria-label'), bounds: rect(button), disabled: button.disabled,
                })),
                summary: document.querySelector('.comparison-summary') ? {
                  text: document.querySelector('.comparison-summary').textContent,
                  bounds: rect(document.querySelector('.comparison-summary')),
                } : null,
              } : null,
              boundary: document.querySelector('.diff-boundary-hint') ? {
                hint: rect(document.querySelector('.diff-boundary-hint')),
                body: rect(document.querySelector('.diff-boundary-columns')),
                text: document.querySelector('.diff-boundary-hint').textContent,
              } : null,
              bodyOverflow: document.documentElement.scrollWidth > innerWidth,
            };
          });
          assert.ok(result.rows.length > 0, `${label}：没有提交列表。`);
          for (const row of result.rows) {
            assert.ok(row.subject && row.subject.width >= 90 && row.subject.height >= 12,
              `${label}：提交标题缺失或被压成不可辨认的区域。`);
            assert.ok(row.author.width >= 12 && row.date.width >= 28, `${label}：作者或日期不可辨认。`);
            assert.ok(row.subject.x >= row.graph.right - 0.1, `${label}：提交图覆盖了标题。`);
            for (const cell of [row.subject, row.author, row.date]) {
              assert.ok(cell.y >= row.row.y && cell.bottom <= row.row.bottom + 1,
                `${label}：提交字段落到其他行。`);
              assert.ok(cell.right <= row.row.right + 1, `${label}：提交字段越过列表边界。`);
            }
          }
          for (const filter of result.filters) {
            assert.ok(filter.icon.x >= filter.text.right - 1 && filter.icon.bottom <= filter.button.bottom + 1,
              `${label}：筛选箭头换行或超出按钮。`);
          }
          assert.equal(result.bodyOverflow, false, `${label}：页面横向越界。`);
          for (const button of result.diffModeIcons) {
            const expectedIcon = { '忽略空白': 'diff-ignore-whitespace', '双栏': 'diff-side-by-side', '单栏': 'diff-unified' }[button.label];
            assert.equal(button.icon, expectedIcon, `${label}：Diff 模式没有使用独立的统一图形。`);
            assert.ok(button.bounds.width === 16 && button.bounds.height === 16,
              `${label}：Diff 模式图形没有保持 16px 网格。`);
          }
          if (scene.endsWith('-loading')) assert.ok(result.loading, `${label}：加载占位没有显示。`);
          for (const group of result.diffGroups) {
            assert.equal(group.bounds.width, 81, `${label}：分段组宽度不符。`);
            assert.equal(group.bounds.height, 31, `${label}：分段组高度不符。`);
            assert.equal(group.background, theme === 'dark' ? 'rgb(37, 38, 42)' : 'rgb(244, 245, 247)');
            assert.equal(group.border, theme === 'dark' ? 'rgb(75, 77, 83)' : 'rgb(209, 211, 217)');
            group.buttons.forEach((button, index) => {
              assert.equal(button.bounds.width, 38);
              assert.equal(button.bounds.height, 27);
              assert.equal(button.bounds.x - group.bounds.x, index === 0 ? 2 : 41);
              assert.equal(button.bounds.y - group.bounds.y, 2);
              if (button.active) assert.equal(button.background, theme === 'dark' ? 'rgb(30, 31, 34)' : 'rgb(255, 255, 255)');
            });
          }
          if (scene.endsWith('-failure')) assert.match(result.notice, /Git 查询失败/, `${label}：失败说明缺失。`);
          if (scene.endsWith('-cancelled')) assert.equal(result.notice, '比较已取消。', `${label}：取消说明缺失。`);
          if (result.comparisonToolbar) {
            const { bounds, buttons, summary } = result.comparisonToolbar;
            assert.deepEqual(buttons.map(button => button.label),
              ['上一处差异', '下一处差异', '查找', '忽略空白', '双栏', '单栏', '设置']);
            let previousRight = bounds.x;
            for (const button of buttons) {
              assert.ok(button.bounds.x >= previousRight && button.bounds.right <= bounds.right,
                `${label}：比较工具栏按钮重叠或越界。`);
              previousRight = button.bounds.right;
            }
            if (scene === 'git-compare') {
              assert.deepEqual(result.comparisonReferences, ['HEAD', '工作区'], `${label}：比较样本的双方引用不一致。`);
              assert.equal(result.comparisonCaption, '比较: app.manifest · HEAD → 工作区');
              assert.equal(summary?.text, '1 处差异');
              assert.ok(summary.bounds.x >= buttons[2].bounds.right && summary.bounds.right <= buttons[3].bounds.x,
                `${label}：差异计数没有位于查找与右侧操作之间。`);
            } else {
              assert.deepEqual(result.comparisonReferences, ['dfe5c25a^', 'dfe5c25a'], `${label}：历史比较被误改为工作区比较。`);
              assert.equal(summary, null, `${label}：非正文状态仍显示旧差异计数。`);
              assert.ok(buttons.slice(0, 3).every(button => button.disabled), `${label}：非正文导航没有禁用。`);
            }
          }
          if (scene === 'diff-boundary') {
            assert.ok(result.boundary, `${label}：文件边界提示缺失。`);
            const { hint, body, text } = result.boundary;
            assert.equal(text, '再次点击可进入下一个文件');
            assert.ok(hint.x >= body.x + 8 && hint.right <= body.right - 8
              && hint.y >= body.y + 8 && hint.bottom <= body.bottom - 8,
            `${label}：提示越过正文边界。`);
          }
          assert.deepEqual(errors, [], `${label}：页面脚本报错。`);
          if (outputPath && width === 1180 && (scene.startsWith('history-diff-') || scene === 'diff-boundary' || scene === 'git-compare')) {
            await page.screenshot({ path: path.join(outputPath, `${scene}-${theme}.png`) });
          }
          if (result.diffModeIcons.length) {
            const readHeader = () => page.evaluate(() => {
              const layout = document.querySelector('.diff-layout');
              const rect = e => { const r = e.getBoundingClientRect(); return {x:r.x,y:r.y,width:r.width,height:r.height,bottom:r.bottom}; };
              return { layout:rect(layout), header:rect(layout.querySelector('.reference-filebar')),
                source:rect(layout.querySelector('.reference-source')), target:rect(layout.querySelector('.reference-target')),
                body:rect(layout.querySelector(':scope > .diff-columns, :scope > .comparison-notice')),
                identity:layout.querySelector('.reference-filebar').textContent,
                count:layout.querySelector('.comparison-summary')?.textContent };
            });
            const split = await readHeader();
            assert.equal(split.header.height, 31);
            assert.equal(split.source.y, split.target.y);
            assert.ok(split.target.x > split.source.x + split.source.width, `${label}：双栏引用没有分开。`);
            await page.locator('.diff-toolbar button[aria-label="单栏"]').click();
            const unified = await readHeader();
            assert.equal(unified.header.height, 55);
            assert.equal(unified.source.x, unified.target.x);
            assert.equal(unified.target.y - unified.source.y, 24);
            assert.equal(unified.body.y, unified.header.bottom);
            assert.deepEqual(unified.layout, split.layout, `${label}：模式切换移动了整个比较区。`);
            assert.equal(unified.identity, split.identity);
            assert.equal(unified.count, split.count);
            if (scene === 'git-compare' || scene === 'diff-boundary') {
              await page.locator('.diff-columns').evaluate(body => { body.scrollTop = 100; body.dataset.reuse = '保留'; });
              const scrollTop = await page.locator('.diff-columns').evaluate(body => body.scrollTop);
              await page.locator('.diff-toolbar button[aria-label="单栏"]').click();
              assert.equal(await page.locator('.diff-columns').evaluate(body => body.scrollTop), scrollTop,
                `${label}：重复激活当前模式重置了滚动。`);
              await page.locator('.diff-columns').evaluate(body => { body.scrollTop = 0; });
            }
            if (outputPath && width === 1180 && (scene === 'git-compare' || scene === 'diff-boundary'))
              await page.screenshot({ path: path.join(outputPath, `${scene}-unified-${theme}.png`) });
            await page.locator('.diff-toolbar button[aria-label="双栏"]').click();
            assert.deepEqual(await readHeader(), split, `${label}：返回双栏未恢复原布局。`);
            if (scene === 'diff-boundary') assert.equal(await page.locator('.diff-boundary-hint').count(), 0,
              `${label}：模式切换后不应恢复旧文件边界提示。`);
          }
          passed++;
        }
      }
    }
    console.log(`历史视觉稿布局与状态检查通过：${passed} 组。`);
  } finally {
    await browser.close();
  }
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
