// 验证状态栏的活动文件身份、元数据适用范围与字号/DPI 布局，不启动服务器。
// 用法：node tools/verify-ux-statusbar.cjs <playwright 模块路径> <浏览器 exe 路径> [截图目录]
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const fs = require('node:fs/promises');

async function main() {
  const [modulePath, browserPath, outputPath] = process.argv.slice(2);
  assert.ok(modulePath && browserPath, '请传入现有 Playwright 模块和浏览器路径。');
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  const scenes = [
    ['text-viewer', 'src/Augit.App/MainWindow.cs', true],
    ['json-preview', 'global.json', true],
    ['markdown-preview', 'docs/product-spec.md', true],
    ['image-preview', 'docs/assets/image-sample.png', false],
    ['file-limit', 'docs/assets/animation.webp', false],
    ['commit-diff', 'src/Augit.App/app.manifest', false],
    ['git-compare', 'src/Augit.App/app.manifest', false],
    ['history-diff-loading', 'src/Augit.App/app.manifest', false],
    ['git-unavailable', '', false],
  ];
  const errors = [];
  let passed = 0;
  try {
    if (outputPath) await fs.mkdir(outputPath, { recursive: true });
    for (const dpi of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ viewport: { width: 1024, height: 640 }, deviceScaleFactor: dpi });
      try {
        const page = await context.newPage();
        page.on('pageerror', error => errors.push(error.message));
        for (const size of [13, 40]) for (const theme of ['light', 'dark']) for (const [scene, relative, text] of scenes) {
          const url = pathToFileURL(path.resolve(__dirname, `../docs/ux-mockups/${scene}.html`));
          url.search = new URLSearchParams({ 'ui-size': String(size), theme, eol: '混合换行' });
          await page.goto(url.href);
          await page.waitForSelector('body[data-typography-preview="ready"]');
          const label = `${scene}-${theme}-${size}-${dpi}`;
          const expectedPath = 'D:\\github\\Augit' + (relative ? '\\' + relative.replaceAll('/', '\\') : '');
          const statusPath = page.locator('.status-path');
          assert.equal(await statusPath.getAttribute('title'), expectedPath, label);
          assert.equal((await statusPath.innerText()).replace(/\s/g, ''), ['Augit', ...relative.split('/').filter(Boolean)].join('›'), label);
          assert.deepEqual(await page.locator('.status-fields span').allTextContents(), text ? ['UTF-8', '混合换行', '只读'] : relative ? ['只读'] : [], label);
          const geometry = await page.locator('.statusbar').evaluate(bar => {
            const rect = element => {
              const box = element.getBoundingClientRect();
              return { left: box.left, right: box.right, top: box.top, bottom: box.bottom };
            };
            return {
              bar: rect(bar), location: rect(bar.querySelector('.status-path')), font: getComputedStyle(bar).fontSize,
              fields: [...bar.querySelectorAll('.status-fields span')].map(element => {
                const range = document.createRange(); range.selectNodeContents(element);
                return { box: rect(element), ink: rect(range) };
              }),
            };
          });
          assert.equal(geometry.font, `${size}px`, label);
          assert.equal(geometry.bar.right, 1024, `${label} 状态栏不能撑宽窗口`);
          assert.ok(geometry.location.right > geometry.location.left + 200, `${label} 必须保留路径空间`);
          let previous = geometry.location;
          for (const field of geometry.fields) {
            assert.ok(field.box.left >= previous.right + 11.9, `${label} 字段必须保持间隔`);
            assert.ok(field.ink.top >= geometry.bar.top && field.ink.bottom <= geometry.bar.bottom, `${label} 文字不能纵向裁切`);
            assert.ok(field.box.right <= geometry.bar.right - 5.9, `${label} 右侧字段不能越界`);
            previous = field.box;
          }
          if (scene === 'json-preview' && dpi === 1 && size === 13 && theme === 'light') {
            for (const ending of ['LF', 'CRLF', 'CR', '无换行']) {
              url.searchParams.set('eol', ending);
              await page.goto(url.href);
              await page.waitForSelector('body[data-typography-preview="ready"]');
              assert.deepEqual(await page.locator('.status-fields span').allTextContents(), ['UTF-8', ending, '只读'], `${label}-${ending}`);
            }
          }
          if (outputPath && dpi === 1 && ((scene === 'json-preview' && size === 40) || (scene === 'image-preview' && size === 13)))
            await page.screenshot({ path: path.join(outputPath, `mockup-${label}.png`) });
          const longPath = 'D:\\github\\Augit\\' + '很长的目录名称\\'.repeat(25) + '文件.cs';
          await statusPath.evaluate((element, value) => { element.textContent = value; element.title = value; }, longPath);
          assert.deepEqual(await statusPath.evaluate(element => [getComputedStyle(element).textOverflow, element.scrollWidth > element.clientWidth]), ['ellipsis', true], label);
          assert.equal(await page.locator('.statusbar').evaluate(element => element.getBoundingClientRect().right), 1024, `${label} 长路径也不能撑宽窗口`);
          assert.equal(await statusPath.getAttribute('title'), longPath, label);
          passed++;
        }
      } finally { await context.close(); }
    }
    assert.deepEqual(errors, [], '视觉稿不得出现脚本错误。');
    console.log(`状态栏视觉稿检查通过：${passed} 个场景组合。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
