// 验证 Worktree 视觉稿的双栏边界、详情滚动和字号适配；不启动服务器或执行 Git。
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
    await fs.mkdir(output, { recursive: true });
    for (const theme of ['light', 'dark']) for (const size of [13, 40]) for (const scale of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ viewport: { width: 1024, height: 640 }, deviceScaleFactor: scale });
      try {
        const page = await context.newPage(), errors = [];
        page.on('pageerror', error => errors.push(error.message));
        const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/worktrees.html'));
        url.search = new URLSearchParams({ theme, 'ui-size': String(size) });
        await page.goto(url.href);
        await page.waitForSelector('body[data-typography-preview="ready"]');
        const dialog = page.locator('.worktree-dialog'), body = dialog.locator('.dialog-body');
        const header = dialog.locator('.dialog-header'), footer = dialog.locator('.dialog-footer');
        assert.equal(await dialog.getAttribute('aria-label'), 'Worktree 管理');
        const box = async locator => locator.boundingBox();
        const [windowBox, dialogBox, headerBox, bodyBox, footerBox] = await Promise.all([
          box(page.locator('.augit-window')), box(dialog), box(header), box(body), box(footer),
        ]);
        assert.ok(dialogBox.x >= 0 && dialogBox.x + dialogBox.width <= 1024, '模态不能超出浏览器客户区');
        assert.ok(headerBox.y + headerBox.height <= bodyBox.y + .5 && bodyBox.y + bodyBox.height <= footerBox.y + .5);
        assert.ok(dialogBox.height <= 600, `${theme}/${size}/${scale} 模态超出宿主安全高度`);
        const columns = (await dialog.locator('.management-content').evaluate(element => getComputedStyle(element).gridTemplateColumns)).split(/\s+/);
        assert.equal(columns[0], '260px');
        assert.ok(parseFloat(columns[1]) > 0);
        const detail = dialog.locator('.management-detail'), detailBox = await box(detail);
        const detailScrollHeight = await detail.evaluate(element => element.scrollHeight);
        const buttons = dialog.locator('.management-detail .button-row > button');
        for (const button of await buttons.all()) {
          const buttonBox = await button.boundingBox();
          assert.ok(buttonBox.x >= detailBox.x && buttonBox.x + buttonBox.width <= detailBox.x + detailBox.width + .5);
          assert.ok(buttonBox.y + buttonBox.height <= detailBox.y + detailScrollHeight + .5);
        }
        assert.equal(await detail.evaluate(element => getComputedStyle(element).overflowY), 'auto');
        assert.deepEqual(errors, []);
        if (scale === 1 && size === 13 || scale === 1 && size === 40)
          await page.screenshot({ path: path.join(output, `worktree-${theme}-${size}.png`) });
        passed++;
      } finally { await context.close(); }
    }
    console.log(`Worktree 视觉稿边界矩阵 ${passed}/12 通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
