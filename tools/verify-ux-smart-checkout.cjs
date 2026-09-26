// Smart Checkout 视觉稿的状态、权威文案与布局验证；不启动服务器、不执行 Git。
//
// 权威 `GitSmartOperationDialog`（plugins/git4idea/backend/src/branch/GitSmartOperationDialog.java:36-125）：
// 标题 `smart.operation.dialog.git.operation.name.problem` = "Git {0} Problem"（`checkout.operation.name` = "checkout"）；
// 北侧说明取 stash 版 `…north.panel.label.stash.text`；中部是受影响的改动列表；两个按钮
// `smart.operation.dialog.smart.operation.name` = "Smart Checkout" 与 `smart.operation.dialog.don.t.operation.name`
// = "Don't {0}"，且**默认焦点在取消上**（`FOCUSED_ACTION`，`:118`）。文案出处 `GitBundle.properties:426-433,1356-1357`。
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
    for (const theme of ['light', 'dark']) {
      for (const viewport of [{ width: 1024, height: 640 }, { width: 1180, height: 760 }]) {
        for (const state of ['ready', 'busy', 'conflict', 'restored']) {
          const context = await browser.newContext({ viewport, deviceScaleFactor: 1 });
          try {
            const page = await context.newPage();
            const errors = [];
            page.on('pageerror', error => errors.push(error.message));
            const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/smart-checkout.html'));
            url.searchParams.set('theme', theme);
            if (state !== 'ready') url.searchParams.set('smart-state', state);
            await page.goto(url.href);
            const dialog = page.locator('.smart-checkout-dialog');
            await dialog.waitFor();

            assert.equal(await dialog.getAttribute('aria-label'), 'Git 检出问题');
            const text = await dialog.locator('.dialog-body').innerText();
            assert.ok(text.includes('会被检出覆盖'), '说明要写明本地改动会被检出覆盖（权威 stash 版文案）');
            assert.deepEqual(await dialog.locator('.smart-checkout-path').allTextContents(),
              ['docs/notes.txt', 'src/App.cs'], '中部要列出受影响文件');

            const notice = dialog.locator('[data-smart-notice]');
            if (state === 'ready') {
              assert.deepEqual(await dialog.locator('[data-smart-action]').allTextContents(),
                ['不检出', 'Smart Checkout']);
              assert.equal(await notice.isHidden(), true, '就绪态不应有状态行');
            } else if (state === 'busy') {
              assert.deepEqual(await dialog.locator('[data-smart-action]').allTextContents(),
                ['不检出', '正在切换…']);
              assert.equal(await notice.isHidden(), false);
              assert.ok((await notice.textContent()).includes('正在暂存'));
              for (const button of await dialog.locator('[data-smart-action]').all()) {
                assert.equal(await button.isDisabled(), true, '执行中不得重复提交');
              }
            } else if (state === 'conflict') {
              // 恢复失败：临时 stash 已保留，只剩"关闭"（解决冲突由操作会话继续）。
              assert.deepEqual(await dialog.locator('[data-smart-action]').allTextContents(), ['关闭']);
              assert.equal(await notice.isHidden(), false);
              assert.ok((await notice.textContent()).includes('临时 stash 已保留'));
            } else {
              assert.ok((await notice.textContent()).includes('已切换'), '成功态要说明切换与暂存删除的结果');
            }

            // 文件列表不得把对话框撑破。
            const [pathsBox, bodyBox, dialogBox] = await Promise.all([
              dialog.locator('.smart-checkout-paths').boundingBox(),
              dialog.locator('.dialog-body').boundingBox(),
              dialog.boundingBox(),
            ]);
            assert.ok(pathsBox.x >= bodyBox.x - 0.5 && pathsBox.x + pathsBox.width <= bodyBox.x + bodyBox.width + 0.5,
              `${theme}/${viewport.width}/${state}：文件列表越出对话框正文。`);
            assert.ok(pathsBox.y + pathsBox.height <= dialogBox.y + dialogBox.height + 0.5);
            assert.deepEqual(errors, []);
            passed++;
          } finally {
            await context.close();
          }
        }
      }
    }
    for (const theme of ['light', 'dark']) {
      const page = await browser.newPage({ viewport: { width: 1180, height: 760 } });
      const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/smart-checkout.html'));
      url.searchParams.set('theme', theme);
      await page.goto(url.href);
      await page.locator('.smart-checkout-dialog').waitFor();
      await page.screenshot({ path: path.join(output, `smart-checkout-${theme}.png`) });
      await page.close();
      passed++;
    }
    console.log(`通过 ${passed} 组 Smart Checkout 入口、执行中、冲突与完成状态验证。`);
  } finally {
    await browser.close();
  }
}

main().catch((error) => { console.error(error); process.exitCode = 1; });
