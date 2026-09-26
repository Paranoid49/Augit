// 主框架按钮与终端标题的静态状态、布局验证；使用已有浏览器，不启动 Shell 或服务器。
// 用法：node tools/verify-ux-frame-buttons.cjs <Playwright 路径> <浏览器路径> [截图目录]
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, browserPath, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  let passed = 0;
  try {
    if (output) await fs.mkdir(output, { recursive: true });
    for (const state of ['ready', 'loading']) for (const scale of [1, 1.25, 1.5]) for (const theme of ['light', 'dark']) for (const size of [13, 40]) {
      const context = await browser.newContext({ viewport: { width: 1180, height: 760 }, deviceScaleFactor: scale });
      try {
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/terminal.html'));
        url.search = new URLSearchParams({ theme, 'ui-size': String(size), 'terminal-state': state });
        await page.goto(url.href);
        await page.waitForSelector('body[data-typography-preview="ready"]');
        assert.equal(await page.locator('.terminal-view').getAttribute('aria-busy'), String(state === 'loading'));
        assert.equal(await page.locator('.terminal-session').textContent(),
          `Windows PowerShell${state === 'loading' ? ' · 正在启动…' : ''}`);
        if (state === 'loading') assert.equal(await page.locator('.terminal-view').textContent(), '');
        // 悬停色按 权威 `ActionButton.hoverBackground`（浅 #00000012／深 #FFFFFF16）（第 116 轮更新；原 rgb(241,242,244)/rgb(45,47,51) 属已删除的 --augit-blue-hover，无权威依据）。
        const hover = theme === 'dark' ? 'rgba(255, 255, 255, 0.086)' : 'rgba(0, 0, 0, 0.07)';
        const accent = theme === 'dark' ? 'rgb(53, 116, 240)' /* 权威 accent-brand-bg 深色 = Blue6 #3574F0（第 116 轮订正；原 Blue8 无依据） */ : 'rgb(56, 113, 225)';
        const panel = theme === 'dark' ? 'rgb(30, 31, 34)' : 'rgb(255, 255, 255)';
        // 禁用态底取 `--augit-panel-muted`：浅 = Islands `*.disabledBackground` = `dialog-bg` = `gray-160`
        // `#F7F8F9`（第 131 轮订正；原 `#F7F8FA` = **未被采用**的 expUI_light `Gray13`，更早的 #F5F8FE 无依据）；
        // 深 = expUI_dark `*.disabledBackground` = `Gray2` `#2B2D30`（原 #25262A 无依据）。
        const mutedPanel = theme === 'dark' ? 'rgb(43, 45, 48)' : 'rgb(247, 248, 249)';
        const faint = theme === 'dark' ? 'rgb(90, 93, 99)' : 'rgb(159, 162, 168)';
        // 轨道按钮的悬停底：权威 `ToolWindow.Button.hoverBackground` 代码默认 = #55555528／#0f0f0f28（第 79 轮）。
        const railHover = theme === 'dark' ? 'rgba(15, 15, 15, 0.157)' : 'rgba(85, 85, 85, 0.157)';
        const controls = page.locator('.rail-button, .side-tool:has(.side-content.tree) > .tool-header .icon-button, .terminal-header .icon-button');
        assert.equal(await controls.count(), 12);
        const bounds = await controls.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().toJSON()));
        const read = (button, pseudo = null) => button.evaluate((element, pseudo) => {
          const style = getComputedStyle(element, pseudo);
          return { color: style.color, background: style.backgroundColor, border: style.borderTopColor, content: style.content };
        }, pseudo);
        for (let index = 0; index < await controls.count(); index++) {
          const button = controls.nth(index);
          const active = await button.evaluate(element => element.classList.contains('active'));
          const close = await button.evaluate(element => element.classList.contains('terminal-session-close'));
          const rail = await button.evaluate(element => element.classList.contains('rail-button'));
          await button.hover();
          // 权威 `SquareStripeButtonLook.getBackgroundColor()`：轨道按钮**只有自身聚焦**时才用 accent，
          // 选中但未聚焦时用普通前景 + 透明底（悬停时给 `--augit-rail-hover`）。第 84 轮已据此实现，此处同步期望。
          assert.equal((await read(button, close ? '::before' : null)).background, rail && active ? railHover : active ? accent : hover);
          await button.focus();
          assert.equal((await read(button, '::after')).border, accent);
          if (active) assert.equal((await read(button, '::before')).border, panel);
          await button.evaluate((element, rail) => { if (rail) element.setAttribute('aria-disabled', 'true'); else element.disabled = true; }, rail);
          assert.equal((await read(button)).color, faint);
          assert.equal((await read(button, close ? '::before' : null)).background, close ? mutedPanel : 'rgba(0, 0, 0, 0)');
          assert.equal((await read(button, '::after')).content, 'none');
          await button.evaluate(element => { element.removeAttribute('aria-disabled'); element.disabled = false; element.blur(); });
        }
        assert.deepEqual(await controls.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().toJSON())), bounds);
        assert.deepEqual(await page.locator('.terminal-header button').evaluateAll(elements => elements.map(e => e.getAttribute('aria-label'))),
          ['关闭终端', '更多操作', '隐藏终端']);
        for (const width of [1024, 1180]) {
          await page.setViewportSize({ width, height: 760 });
          await page.evaluate(() => measureTerminalHeaders());
          const layout = await page.locator('.terminal-header').evaluate(header => {
            const rect = element => { const r = element.getBoundingClientRect(); return { left: r.left, top: r.top, right: r.right, bottom: r.bottom, height: r.height }; };
            return { header: rect(header), children: [...header.children].map(rect) };
          });
          let previous = layout.header.left;
          for (const child of layout.children) {
            assert.ok(child.left >= previous - .01, `终端标题重叠：${theme}/${size}/${width}`);
            assert.ok(child.right <= layout.header.right && child.bottom <= layout.header.bottom);
            previous = child.right;
          }
          for (const child of layout.children.slice(2)) assert.equal(child.height, 24);
        }
        if (output && scale === 1) {
          await page.getByRole('link', { name: '终端', exact: true }).focus();
          await page.screenshot({ path: path.join(output, `terminal-${state}-${theme}-${size}.png`) });
        }
        assert.deepEqual(errors, []);
        passed++;
      } finally { await context.close(); }
    }
    console.log(`主框架按钮与终端标题就绪/加载矩阵 ${passed}/24 通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
