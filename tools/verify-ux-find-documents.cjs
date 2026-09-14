// 验证 Markdown 与 JSON 视觉稿都能从预览进入顶部查找条；不启动服务器。
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, browserPath] = process.argv.slice(2);
  assert.ok(modulePath && browserPath, '请传入现有 Playwright 模块和浏览器路径。');
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  try {
    for (const scene of ['json-preview', 'markdown-preview']) {
      const page = await browser.newPage({ viewport: { width: 1024, height: 640 } });
      const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups', `${scene}.html`));
      await page.goto(url.href);
      await page.waitForSelector('body[data-typography-preview="ready"]');
      await page.keyboard.press('Control+f');
      await page.waitForSelector('.current-find');
      if (scene === 'markdown-preview') {
        assert.equal(await page.locator('.markdown-document').getAttribute('data-markdown-mode'), 'source', 'Markdown 查找必须回到原文。');
      }
      const bar = await page.locator('.current-find').boundingBox();
      const parent = await page.locator('.document-view').boundingBox();
      const code = await page.locator('.code-view').boundingBox();
      assert.ok(bar && parent && code);
      assert.ok(Math.abs(bar.width - parent.width) < 0.1, `${scene} 查找条未占满正文宽度。`);
      assert.ok(bar.y + bar.height <= code.y + 0.1, `${scene} 查找条覆盖正文。`);
      await page.locator('.current-find input').fill(scene === 'json-preview' ? 'sdk' : 'Augit');
      assert.match(await page.locator('.find-status').textContent(), /^[1-9]\/[1-9][0-9]*$/, `${scene} 查找计数错误。`);
      await page.keyboard.press('Escape');
      assert.equal(await page.locator('.current-find').count(), 0, `${scene} 查找条未关闭。`);
      await page.close();
    }
    console.log('JSON/Markdown 查找流程通过。');
  } finally {
    await browser.close();
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
