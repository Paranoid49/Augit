// 验证历史工具栏的收纳、键盘循环和焦点返回；使用本机浏览器，不启动服务器。
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, executablePath, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath, headless: true });
  let passed = 0;
  try {
    if (output) await fs.mkdir(output, { recursive: true });
    for (const dpi of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ viewport: { width: 1645, height: 900 }, deviceScaleFactor: dpi });
      try {
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        for (const theme of ['light', 'dark']) for (const size of [13, 40]) {
          const label = `${theme}-${size}-${dpi}`;
          const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/git-history.html'));
          url.search = new URLSearchParams({ theme, 'ui-size': String(size) });
          await page.goto(url.href);
          await page.waitForSelector('body[data-typography-preview="ready"]');
          const settle = () => page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
          const toolbar = page.locator('.git-side-toolbar');
          const resize = async height => { await toolbar.evaluate((e, h) => { e.style.height = `${h}px`; }, height); await settle(); };
          const locate = page.locator('.git-side-toolbar > button[aria-label="定位 HEAD"]');
          const trigger = page.locator('.history-tools-more');
          const popup = page.locator('.history-tools-popup');
          const focused = target => target.evaluate(e => e === document.activeElement);
          await resize(360);
          await locate.focus();
          await resize(100);
          assert.ok(await focused(trigger), `${label} 收起时焦点没有交给箭头`);
          const focusStyle = await trigger.evaluate(e => {
            const style = getComputedStyle(e);
            const probe = document.createElement('span');
            probe.style.color = 'var(--augit-blue)';
            e.append(probe);
            const accent = getComputedStyle(probe).color;
            probe.remove();
            return { outline: style.outlineColor, width: style.outlineWidth, accent };
          });
          assert.equal(focusStyle.outline, focusStyle.accent, `${label} 焦点框必须使用主题强调色`);
          assert.equal(focusStyle.width, '1px', label);
          const rail = await toolbar.boundingBox();
          for (const button of await page.locator('.git-side-toolbar > button:visible').all()) {
            const bounds = await button.boundingBox();
            assert.ok(bounds.y + bounds.height <= rail.y + rail.height - 4 + 1, `${label} 按钮越过底边`);
            assert.equal(bounds.height, 28, label);
          }
          await page.keyboard.press('Enter');
          assert.ok(await popup.evaluate(e => e.matches(':popover-open')), label);
          const actions = popup.locator('button:not(:disabled)');
          await actions.last().focus();
          await page.keyboard.press('Tab');
          assert.ok(await focused(actions.first()), `${label} Tab 必须在弹层内循环`);
          await page.keyboard.press('Shift+Tab');
          assert.ok(await focused(actions.last()), label);
          await page.keyboard.press('Escape');
          assert.ok(await focused(trigger), `${label} Esc 必须返回入口`);
          await page.keyboard.press('Enter');
          await popup.getByRole('button', { name: '搜索', exact: true }).focus();
          await page.keyboard.press('Enter');
          assert.ok(await focused(page.getByRole('textbox', { name: '文本或哈希', exact: true })), label);
          await trigger.focus();
          await resize(360);
          assert.ok(await focused(locate), `${label} 展开后不能遗留隐藏焦点`);

          const panel = page.locator('.log-list-panel');
          const widen = async width => { await panel.evaluate((e, w) => { e.style.width = `${w}px`; }, width); await settle(); };
          const fileFilter = page.locator('.history-filters > button[data-history-filter="3"]');
          await widen(1200);
          await fileFilter.focus();
          await widen(260);
          assert.ok(await focused(page.locator('.history-filter-overflow > summary')), `${label} 筛选收纳丢失焦点`);
          await widen(1200);
          assert.ok(await focused(fileFilter), `${label} 筛选展开丢失焦点`);
          await toolbar.evaluate(e => e.style.removeProperty('height'));
          await panel.evaluate(e => e.style.removeProperty('width'));
          await settle();
          assert.deepEqual(errors, [], label);
          if (output && dpi === 1 && size === 13) await page.screenshot({ path: path.join(output, `history-toolbar-${label}.png`) });
          passed++;
        }
      } finally { await context.close(); }
    }
    console.log(`PASS=${passed} 历史工具栏连续输入组合通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
