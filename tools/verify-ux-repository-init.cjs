// 「创建 Git 仓库」视觉稿的状态、权威文案与布局验证；不启动服务器、不执行 Git。
//
// 权威 `GitInit`（plugins/git4idea/backend/src/actions/GitInit.java）：
// 入口文案 `action.Git.Init.text` = "Create Git Repository…"；目标来自单目录选择器
// （`init.destination.directory.title` / `.description`，`:45-64`）；**只有"目标已在 Git 下"**
// 才弹 Yes/No 警告（`init.warning.already.under.git` / `init.warning.title`，`:66-74`）；
// 失败给带 Git 错误输出的错误通知（`action.Git.Init.error`，`:80-83`）。
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
        for (const state of ['ready', 'under-git', 'busy', 'failure']) {
          const context = await browser.newContext({ viewport, deviceScaleFactor: 1 });
          try {
            const page = await context.newPage();
            const errors = [];
            page.on('pageerror', error => errors.push(error.message));
            const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/repository-init.html'));
            url.searchParams.set('theme', theme);
            if (state !== 'ready') url.searchParams.set('init-state', state);
            await page.goto(url.href);
            const dialog = page.locator('.repository-init-dialog');
            await dialog.waitFor();

            if (state === 'under-git') {
              assert.equal(await dialog.getAttribute('aria-label'), '初始化 Git');
              const text = await dialog.locator('.dialog-body').innerText();
              assert.ok(text.includes('D:\\projects\\notes'), '警告里要写出目标目录（权威消息带 {0}）');
              assert.ok(text.includes('已在 Git 下'), '警告要说明目录已在 Git 下');
              assert.deepEqual(await dialog.locator('[data-repo-init-action]').allTextContents(), ['取消', '继续']);
            } else {
              assert.equal(await dialog.getAttribute('aria-label'), '创建 Git 仓库');
              const target = dialog.locator('[data-repo-init-path]');
              assert.equal(await target.inputValue(), 'D:\\projects\\notes');
              // 目标是**选择出来的**，不是手输：字段只读，配一个「选择目录…」按钮（权威的单目录选择器）。
              assert.equal(await target.getAttribute('readonly'), '');
              assert.deepEqual(await dialog.locator('[data-repo-init-action]').allTextContents(),
                state === 'busy' ? ['选择目录…', '取消', '正在初始化…'] : ['选择目录…', '取消', '创建']);
              const notice = dialog.locator('[data-repo-init-notice]');
              if (state === 'busy') {
                assert.equal(await notice.isHidden(), false);
                assert.ok((await notice.textContent()).includes('正在初始化'));
                for (const button of await dialog.locator('[data-repo-init-action]').all()) {
                  assert.equal(await button.isDisabled(), true, '初始化进行中不得重复提交');
                }
                assert.equal((await dialog.locator('[data-repo-init-action="create"]').textContent()).trim(), '正在初始化…');
              } else if (state === 'failure') {
                assert.equal(await notice.isHidden(), false);
                const text = await notice.textContent();
                assert.ok(text.includes('Git 初始化失败'), '失败要在窗口内写明是初始化失败');
                assert.ok(text.includes('Permission denied'), '失败要带上 Git 的错误输出');
                assert.equal(await dialog.locator('[data-repo-init-action="create"]').isDisabled(), false, '失败后要能重试');
              } else {
                assert.equal(await notice.isHidden(), true, '就绪态不应有状态行');
              }
            }

            // 目录行不得把对话框撑破：输入框与按钮同排且都在正文范围内。
            const row = dialog.locator('.path-row');
            if (await row.count()) {
              const [rowBox, bodyBox, dialogBox] = await Promise.all([
                row.boundingBox(), dialog.locator('.dialog-body').boundingBox(), dialog.boundingBox(),
              ]);
              assert.ok(rowBox.x >= bodyBox.x - 0.5 && rowBox.x + rowBox.width <= bodyBox.x + bodyBox.width + 0.5,
                `${theme}/${viewport.width}/${state}：目录行越出对话框正文。`);
              assert.ok(rowBox.x + rowBox.width <= dialogBox.x + dialogBox.width + 0.5);
            }
            assert.deepEqual(errors, []);
            passed++;
          } finally {
            await context.close();
          }
        }
      }
    }
    // 两个主题各出一张基准截图，便于人工核对（不作为断言）。
    for (const theme of ['light', 'dark']) {
      const page = await browser.newPage({ viewport: { width: 1180, height: 760 } });
      const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/repository-init.html'));
      url.searchParams.set('theme', theme);
      await page.goto(url.href);
      await page.locator('.repository-init-dialog').waitFor();
      await page.screenshot({ path: path.join(output, `repository-init-${theme}.png`) });
      await page.close();
      passed++;
    }
    console.log(`通过 ${passed} 组仓库初始化入口、确认、进行中与失败状态验证。`);
  } finally {
    await browser.close();
  }
}

main().catch((error) => { console.error(error); process.exitCode = 1; });
