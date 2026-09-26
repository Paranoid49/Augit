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
        const chromeCss = theme === 'dark' ? 'rgb(43, 45, 48)' : 'rgb(233, 234, 238)';
        // 主菜单条目是**菜单项**，悬停底取 `--title-menu-hover`（浅 #00000012／深 #FFFFFF1A），与按钮悬停的 #FFFFFF16 不同。
        const menuHover = theme === 'dark' ? 'rgba(255, 255, 255, 0.1)' : 'rgba(0, 0, 0, 0.07)';
        // 按钮悬停底 `--title-button-hover`（浅 #00000012／深 #FFFFFF16）。
        const hover = theme === 'dark' ? 'rgba(255, 255, 255, 0.086)' : 'rgba(0, 0, 0, 0.07)';
        // 按下底是独立一档：`--title-button-pressed`（浅 #00000020／深 Gray3 #393B40）。
        const titlePressed = theme === 'dark' ? 'rgb(57, 59, 64)' : 'rgba(0, 0, 0, 0.125)';
        // 权威 `accent-brand-bg`：浅 blue-80 #3871E1／深 Blue6 #3574F0（原写 Blue8 #548AF7，无依据）。
        const accent = theme === 'dark' ? [53, 116, 240] : [56, 113, 225];
        const text = theme === 'dark' ? [223, 225, 229] : [32, 33, 36];
        // 权威 `Label.disabledForeground` → `text-disabled`：浅 gray-100 #9FA2A8／深 expUI_dark 灰阶 Gray6 #5A5D63
        // （原 [160,164,170]/[111,115,123] 为旧值，无依据）。
        const faint = theme === 'dark' ? [90, 93, 99] : [159, 162, 168];
        // 直接读 computed 字符串（canvas 读法会丢 alpha，见第 120/125 轮）。
        const rawBg = (locator) => locator.evaluate(el => getComputedStyle(el).backgroundColor);
        const controls = page.locator('.titlebar :is(.top-button, .top-chip, .titlebar-context)');
        const bounds = await controls.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().toJSON()));
        if (outputPath && dpi === 96 && size === 13)
          await page.screenshot({ path: path.join(outputPath, `main-${theme}.png`) });
        for (let index = 0; index < await controls.count(); index++) {
          const button = controls.nth(index);
          const type = await button.getAttribute('class');
          const workspace = type.includes('workspace-chip'), branch = type.includes('branch-chip');
          // 工作区 chip 的**常态**底是透明的（项目色走的是标题栏背景渐变 `--augit-title-glow`，
          // 不是 chip 底色；`MainToolbar` 里也没有"项目色底"这个键）。第 70 轮已据此实现，此处同步期望。
          const normal = workspace ? 'rgba(0, 0, 0, 0)' : chromeCss;
          // 悬停/按下底改为权威令牌（第 85/87/117 轮）：chips 与当前文件入口 = `--title-chip-hover`/`--title-context-hover`
          // （浅 #00000012／深 #FFFFFF1A），其余按钮 = `--title-button-hover`（浅 #00000012／深 #FFFFFF16），
          // 按下 = `--title-button-pressed`（浅 #00000020／深 Gray3 #393B40）。
          // 这些值是**半透明**的，原来的 canvas 读法会丢掉 alpha，故直接用 computed 字符串比较。
          const dark = theme === 'dark';
          const chipish = workspace || branch || type.includes('titlebar-context');
          const hovered = chipish
            ? (dark ? 'rgba(255, 255, 255, 0.1)' : 'rgba(0, 0, 0, 0.07)')
            : (dark ? 'rgba(255, 255, 255, 0.086)' : 'rgba(0, 0, 0, 0.07)');
          const pressed = dark ? 'rgb(57, 59, 64)' : 'rgba(0, 0, 0, 0.125)';
          await page.mouse.move(700, 700);
          if (workspace) assert.equal(await rawBg(button), normal);
          await button.hover();
          assert.equal(await rawBg(button), hovered, `${type} 悬停色`);
          await page.mouse.down();
          assert.equal(await rawBg(button), pressed, `${type} 按下色`);
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
          // 禁用态不显示悬停/按下（权威），底色回到常态；此处与 `normal` 同为字符串比较。
          assert.equal(await rawBg(button), normal);
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
          assert.equal(await rawBg(button), hover);
          await page.mouse.down();
          // 按下是独立一档（不再沿用悬停色）；关闭按钮（索引 2）按下为 Windows 关闭红 #C42B1C，
          // 其余按钮为 `--title-button-pressed`。
          assert.equal(await rawBg(button), index === 2 ? 'rgb(196, 43, 28)' : titlePressed);
          await page.mouse.move(700, 700);
          await page.mouse.up();
        }
        await page.locator('[data-action="menu"]').click();
        const entries = page.locator('.main-menu-entry');
        assert.deepEqual(await entries.allTextContents(), ['文件', '视图', 'Git', '终端', '设置']);
        await entries.first().hover();
        assert.equal(await rawBg(entries.first()), menuHover);
        await entries.first().focus();
        assert.deepEqual(await color(entries.first(), 'outlineColor'), accent);
        await page.locator('[data-action="menu"]').click();
        await page.mouse.move(700, 700);
        assert.equal(await rawBg(page.locator('.workspace-chip')), 'rgba(0, 0, 0, 0)');   // 工作区 chip 常态透明（见第 127 轮）
        assert.deepEqual(errors, []);
        passed++;
      } finally { await context.close(); }
    }
    console.log(`标题栏状态矩阵 ${passed}/12 通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
