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
          // 第 173／174 轮：左竖条＝分支面板的权威动作组（12 项，多数在短窗口里收进溢出弹层）。
          // 这里不认某个固定按钮，改用「当前可见的最后一个竖条按钮」，对条目数变化免疫。
          const lastVisible = page.locator('.git-side-toolbar > button:not([hidden])').last();
          const trigger = page.locator('.history-tools-more');
          const popup = page.locator('.history-tools-popup');
          const focused = target => target.evaluate(e => e === document.activeElement);
          await resize(360);
          await lastVisible.focus();
          await resize(100);
          assert.ok(await focused(trigger), `${label} 收起时焦点没有交给箭头`);
          const focusStyle = await trigger.evaluate(e => {
            const style = getComputedStyle(e);
            const probe = document.createElement('span');
            // 焦点环的权威是 **accent**（design-system："焦点使用命中区内侧 1px accent 圆角边框"）。
            // 第 117 轮已把 14 条焦点态规则由 `--augit-blue` 改为 `--augit-accent-brand`；此处探针原先
            // 读的是 `--augit-blue`，浅色下两者不同（`#3871E1` vs `#3574F0`）故失败，深色下同值所以一直没暴露。
            probe.style.color = 'var(--augit-accent-brand)';
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
          // 弹层里的条目执行后要收起弹层并把焦点交回入口（`bindHistoryToolbar` 的复制点击处理）。
          await popup.getByRole('button', { name: '新建分支…', exact: true }).focus();
          await page.keyboard.press('Enter');
          assert.ok(await focused(trigger), `${label} 弹层动作执行后焦点回到入口`);
          await trigger.focus();
          await resize(360);
          // 展开后焦点必须落在**可见且可用**的竖条按钮上（不能留在隐藏按钮或已消失的箭头上）。
          assert.ok(await page.evaluate(() => {
            const el = document.activeElement;
            return !!el && el.classList && el.classList.contains('toolbar-button')
              && !el.hidden && !el.disabled && !!el.closest('.git-side-toolbar');
          }), `${label} 展开后不能遗留隐藏焦点`);

          const panel = page.locator('.log-list-panel');
          const widen = async width => { await panel.evaluate((e, w) => { e.style.width = `${w}px`; }, width); await settle(); };
          // 第 179 轮按 New UI 对齐：筛选栏四项里只有「分支」已接线（用户／日期／路径当时是**禁用**入口，
          // 禁用项不参与焦点循环），因此用「分支」控件验证"收进溢出菜单后焦点转移"。
          // 第 185 轮把**日期**接上（权威 `DateFilterPopupComponent`）、第 191 轮把**路径**接上
          // （权威 `StructureFilterPopupComponent`）⇒ 四项全部可用，收纳关闭时焦点按 Augit 既有规则
          // 交给同组**最后一个可见可用**的动作，即路径控件。
          const branchFilter = page.locator('.history-filters > button[data-filter-key="branch"]');
          await widen(1200);
          await branchFilter.focus();
          await widen(260);
          assert.ok(await focused(page.locator('.history-filter-overflow > summary')), `${label} 筛选收纳丢失焦点`);
          await widen(1200);
          assert.ok(
            await focused(page.locator('.history-filters > button[data-filter-key="path"]')),
            `${label} 筛选展开丢失焦点`);
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
