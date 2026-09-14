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
        const hover = theme === 'dark' ? 'rgb(45, 47, 51)' : 'rgb(241, 242, 244)';
        const accent = theme === 'dark' ? 'rgb(84, 138, 247)' : 'rgb(56, 113, 225)';
        const panel = theme === 'dark' ? 'rgb(30, 31, 34)' : 'rgb(255, 255, 255)';
        const mutedPanel = theme === 'dark' ? 'rgb(37, 38, 42)' : 'rgb(245, 248, 254)';
        const faint = theme === 'dark' ? 'rgb(111, 115, 123)' : 'rgb(160, 164, 170)';
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
          assert.equal((await read(button, close ? '::before' : null)).background, active ? accent : hover);
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
