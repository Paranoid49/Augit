// 验证深色 accent-brand 令牌与激活工具轨底色；使用本地浏览器和 file:// 视觉稿，不启动服务器。
// 用法：node tools/verify-ux-dark-accent.cjs <Playwright 模块路径> <浏览器路径>
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, browserPath] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  try {
    for (const [theme, accent, rail] of [
      ['light', '#3871e1', 'rgb(56, 113, 225)'],
      ['dark', '#3574f0', 'rgb(53, 116, 240)'],
    ]) {
      const page = await browser.newPage({ viewport: { width: 1180, height: 760 } });
      try {
        const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/terminal.html'));
        url.search = new URLSearchParams({ theme, 'ui-size': '13', 'terminal-state': 'ready' });
        await page.goto(url.href);
        await page.waitForFunction(() => document.body.dataset.typographyPreview === 'ready');
        const actual = await page.evaluate(() => {
          const rail = document.querySelector('.rail-button.active');
          return {
            theme: document.body.dataset.theme || 'light',
            accent: getComputedStyle(document.body).getPropertyValue('--augit-accent-brand').trim(),
            rail: rail ? getComputedStyle(rail, '::before').backgroundColor : null,
          };
        });
        assert.equal(actual.theme, theme);
        assert.equal(actual.accent, accent, `${theme} 的 accent-brand 应与权威值一致`);
        assert.equal(actual.rail, rail, `${theme} 的激活工具轨底色应使用 accent-brand`);
      } finally {
        await page.close();
      }
    }
    console.log('PASS: 深色 accent-brand = #3574F0，激活工具轨 rgb(53,116,240)；浅色保持 #3871E1/rgb(56,113,225)。');
  } finally {
    await browser.close();
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
