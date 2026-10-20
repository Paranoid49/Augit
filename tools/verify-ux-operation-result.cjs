// 操作结果通知（balloon 形态）核对：白底卡片、类型图标、粗体标题、正文、蓝色文字动作。
// 参考：PyCharm 2026.2.1 `Push failed` balloon（artifacts/pixel-followup-2026-10-19/git-failure-closure）。
// 用法：node tools/verify-ux-operation-result.cjs <playwright 模块路径> <浏览器 exe 路径> <截图目录>
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, browserPath, outputPath] = process.argv.slice(2);
  assert.ok(modulePath && browserPath && outputPath, '请提供 Playwright 模块、浏览器和证据目录。');
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  let passed = 0;
  try {
    await fs.mkdir(outputPath, { recursive: true });
    const page = await browser.newPage({ viewport: { width: 1180, height: 760 }, deviceScaleFactor: 1.75 });
    for (const theme of ['light', 'dark']) {
      const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/operation-result.html'));
      url.search = new URLSearchParams({ theme });
      await page.goto(url.href, { waitUntil: 'domcontentloaded' });
      await page.waitForSelector('body[data-typography-preview="ready"]');
      await page.waitForSelector('.toast');
      const state = await page.evaluate(() => {
        const toast = document.querySelector('.toast');
        const icon = toast.querySelector('.toast-icon svg path');
        const box = toast.getBoundingClientRect();
        const style = getComputedStyle(toast);
        return {
          classes: toast.className,
          width: Math.round(box.width),
          height: Math.round(box.height),
          radius: style.borderRadius,
          iconFill: icon ? getComputedStyle(icon).fill : null,
          title: toast.querySelector('.toast-title').textContent.trim(),
          text: toast.querySelector('.toast-text').textContent.trim(),
          action: toast.querySelector('.toast-action') ? toast.querySelector('.toast-action').textContent.trim() : null,
        };
      });
      assert.match(state.classes, /toast-error/, theme);
      assert.equal(state.iconFill, 'rgb(219, 88, 96)', `${theme} 错误图标颜色`);
      assert.equal(state.title, 'Push failed', theme);
      assert.match(state.text, /No remote is configured/, theme);
      assert.equal(state.action, null, theme);
      assert.equal(state.width, 375, theme);
      assert.ok(state.height >= 88 && state.height <= 110, `${theme} 高度 ${state.height}`);
      assert.equal(state.radius, '8px', theme);
      passed += 8;
      await page.screenshot({ path: path.join(outputPath, `operation-result-${theme}.png`) });

      // 同一通知组件的动作按文字链接呈现（参考 balloon 的 "Show details in console" 形态）。
      const unavailable = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/git-unavailable.html'));
      unavailable.search = new URLSearchParams({ theme });
      await page.goto(unavailable.href, { waitUntil: 'domcontentloaded' });
      await page.waitForSelector('body[data-typography-preview="ready"]');
      await page.waitForSelector('.toast .toast-action');
      const action = await page.evaluate(() => {
        const node = document.querySelector('.toast .toast-action');
        const style = getComputedStyle(node);
        return { tag: node.tagName, text: node.textContent.trim(), color: style.color, decoration: style.textDecorationLine };
      });
      assert.equal(action.tag, 'A', theme);
      assert.equal(action.text, 'Configure git.exe', theme);
      assert.equal(action.color, 'rgb(53, 116, 240)', `${theme} 动作颜色`);
      assert.equal(action.decoration, 'none', `${theme} 默认无下划线`);
      passed += 4;
      await page.screenshot({ path: path.join(outputPath, `git-unavailable-${theme}.png`) });
    }
    console.log(`操作结果通知核对通过：${passed} 项。`);
  } finally {
    await browser.close();
  }
}

main().catch((error) => { console.error(error); process.exit(1); });
