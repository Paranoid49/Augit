// 验证标题栏主题、DPI、字号和按钮状态；使用已有浏览器，不启动服务器或下载依赖。
// 用法：node tools/verify-ux-titlebar.cjs <Playwright 模块路径> <浏览器路径> [证据目录]
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

const mix = (background, foreground, percent) => background.map((value, index) => Math.round(value + (foreground[index] - value) * percent / 100));
async function color(locator, property = 'backgroundColor') {
  return locator.evaluate((element, property) => {
    const context = document.createElement('canvas').getContext('2d');
    context.fillStyle = getComputedStyle(element)[property];
    context.fillRect(0, 0, 1, 1);
    return [...context.getImageData(0, 0, 1, 1).data].slice(0, 3);
  }, property);
}
async function main() {
  const [modulePath, browserPath, outputPath] = process.argv.slice(2);
  assert.ok(modulePath && browserPath, '请提供现有 Playwright 与浏览器路径。');
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  let passed = 0;
  try {
    if (outputPath) await fs.mkdir(outputPath, { recursive: true });
    for (const theme of ['light', 'dark']) for (const dpi of [96, 120, 144]) for (const size of [13, 40]) {
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
        const chrome = theme === 'dark' ? [43, 45, 48] : [233, 234, 238];
        const accent = theme === 'dark' ? [84, 138, 247] : [56, 113, 225];
        const text = theme === 'dark' ? [223, 225, 229] : [32, 33, 36];
        const faint = theme === 'dark' ? [111, 115, 123] : [160, 164, 170];
        const controls = page.locator('.titlebar :is(.top-button, .top-chip, .titlebar-context)');
        const bounds = await controls.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().toJSON()));
        if (outputPath && dpi === 96 && size === 13)
          await page.screenshot({ path: path.join(outputPath, `main-${theme}.png`) });
        for (let index = 0; index < await controls.count(); index++) {
          const button = controls.nth(index);
          const type = await button.getAttribute('class');
          const workspace = type.includes('workspace-chip'), branch = type.includes('branch-chip');
          const normal = workspace ? mix(chrome, accent, 8) : chrome;
          const hovered = mix(chrome, workspace || branch ? accent : text, workspace || branch ? 12 : type.includes('titlebar-context') ? 7 : 8);
          await page.mouse.move(700, 700);
          if (workspace) assert.deepEqual(await color(button), normal);
          await button.hover();
          assert.deepEqual(await color(button), hovered, `${type} 悬停色`);
          await page.mouse.down();
          assert.deepEqual(await color(button), hovered, `${type} 按下色`);
          await page.mouse.move(700, 700);
          await page.mouse.up();
          assert.equal(page.url(), url.href, '取消按下不能执行跳转。');
          await button.focus();
          assert.deepEqual(await color(button, 'outlineColor'), accent);
          assert.equal(await button.evaluate(element => getComputedStyle(element).outlineWidth), '1px');
          if (outputPath && dpi === 96 && size === 13 && workspace)
            await page.locator('.titlebar').screenshot({ path: path.join(outputPath, `focus-${theme}.png`) });
          await button.evaluate(element => element.setAttribute('aria-disabled', 'true'));
          await button.hover();
          assert.deepEqual(await color(button), normal);
          assert.deepEqual(await color(button, 'color'), faint);
          assert.equal(await button.evaluate(element => getComputedStyle(element).outlineStyle), 'none');
          await button.evaluate(element => { element.removeAttribute('aria-disabled'); element.blur(); });
        }
        assert.deepEqual(await controls.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().toJSON())), bounds,
          '状态变化不能改变标题栏布局。');
        const windowButtons = page.locator('.window-dot');
        for (let index = 0; index < 3; index++) {
          const button = windowButtons.nth(index);
          await button.hover();
          assert.deepEqual(await color(button), mix(chrome, text, 8));
          await page.mouse.down();
          assert.deepEqual(await color(button), index === 2 ? [196, 43, 28] : mix(chrome, text, 8));
          await page.mouse.move(700, 700);
          await page.mouse.up();
        }
        await page.locator('[data-action="menu"]').click();
        const entries = page.locator('.main-menu-entry');
        assert.deepEqual(await entries.allTextContents(), ['文件', '视图', 'Git', '终端', '设置']);
        await entries.first().hover();
        assert.deepEqual(await color(entries.first()), mix(chrome, text, 8));
        await entries.first().focus();
        assert.deepEqual(await color(entries.first(), 'outlineColor'), accent);
        await page.locator('[data-action="menu"]').click();
        await page.mouse.move(700, 700);
        assert.deepEqual(await color(page.locator('.workspace-chip')), mix(chrome, accent, 8));
        assert.deepEqual(errors, []);
        passed++;
      } finally { await context.close(); }
    }
    console.log(`标题栏状态矩阵 ${passed}/12 通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
