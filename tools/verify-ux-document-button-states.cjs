// 仅验证既有文档按钮的视觉状态；使用本机依赖，不启动服务器。
// 用法：node tools/verify-ux-document-button-states.cjs <Playwright 路径> <浏览器路径> [截图目录]
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
    for (const scale of [1, 1.25, 1.5]) for (const theme of ['light', 'dark']) for (const size of [13, 40]) {
      const context = await browser.newContext({ viewport: { width: 1180, height: 760 }, deviceScaleFactor: scale });
      try {
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        const hover = theme === 'dark' ? 'rgb(45, 47, 51)' : 'rgb(241, 242, 244)';
        const selected = theme === 'dark' ? 'rgb(47, 70, 111)' : 'rgb(208, 223, 254)';
        const accent = theme === 'dark' ? 'rgb(84, 138, 247)' : 'rgb(56, 113, 225)';
        const faint = theme === 'dark' ? 'rgb(111, 115, 123)' : 'rgb(160, 164, 170)';
        for (const scene of ['text-viewer', 'markdown-preview', 'json-preview', 'image-preview', 'blame']) {
          const label = `${scene}-${theme}-${size}-${scale}`;
          const url = pathToFileURL(path.resolve(__dirname, `../docs/ux-mockups/${scene}.html`));
          url.search = new URLSearchParams({ theme, 'ui-size': String(size) });
          await page.goto(url.href);
          await page.waitForSelector('body[data-typography-preview="ready"]');
          if (scene === 'text-viewer') {
            await page.getByRole('button', { name: '当前文件搜索', exact: true }).click();
            await page.getByRole('textbox', { name: '当前文件查找', exact: true }).fill('Git');
            await page.waitForFunction(() => document.querySelector('.find-status')?.textContent.includes('/'));
          }
          const controls = page.locator(':is(.document-toolbar, .current-find) :is(.icon-button, .segment)');
          const bounds = await controls.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().toJSON()));
          const read = async (button, pseudo = null) => button.evaluate((element, pseudo) => {
            const style = getComputedStyle(element, pseudo);
            return { color: style.color, background: style.backgroundColor, border: style.borderTopColor,
              width: style.borderTopWidth, content: style.content };
          }, pseudo);
          for (let index = 0; index < await controls.count(); index++) {
            const button = controls.nth(index);
            const mode = await button.evaluate(element => element.classList.contains('segment'));
            const active = await button.evaluate(element => element.classList.contains('active'));
            const pseudo = mode ? '::before' : null;
            const saved = await read(button, pseudo);
            await button.hover();
            assert.equal((await read(button, pseudo)).background, active ? saved.background : hover, label);
            await button.focus();
            const focus = await read(button, '::after');
            assert.equal(focus.border, accent, label);
            assert.equal(focus.width, '1px', label);
            await page.mouse.down();
            assert.equal((await read(button, pseudo)).background, active ? saved.background : hover, label);
            await page.mouse.move(10, 10);
            await page.mouse.up();
            assert.equal(page.url(), url.href, `${label} 取消按下不能跳转`);
            const pressed = await button.getAttribute('aria-pressed');
            if (!mode) {
              // 固定样本的动作并非全部可执行；这里只构造已有开关的选中视觉状态。
              await button.evaluate(element => element.setAttribute('aria-pressed', 'true'));
              await button.hover();
              assert.equal((await read(button)).background, selected, label);
            }
            await button.evaluate(element => { element.disabled = true; });
            await button.hover({ force: true });
            assert.equal((await read(button)).color, faint, label);
            assert.equal((await read(button, '::after')).content, 'none', label);
            assert.equal((await read(button, pseudo)).background, mode && active ? saved.background : 'rgba(0, 0, 0, 0)', label);
            await button.evaluate((element, pressed) => {
              element.disabled = false;
              if (pressed === null) element.removeAttribute('aria-pressed'); else element.setAttribute('aria-pressed', pressed);
              element.blur();
            }, pressed);
          }
          assert.deepEqual(await controls.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().toJSON())), bounds,
            `${label} 状态不得改变控件位置`);
          if (output && scale === 1 && size === 13) {
            const target = controls.first();
            await target.hover();
            await target.focus();
            await page.screenshot({ path: path.join(output, `${scene}-${theme}.png`) });
          }
          assert.deepEqual(errors, []);
          passed++;
        }
      } finally { await context.close(); }
    }
    console.log(`文档按钮状态矩阵 ${passed}/60 通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
