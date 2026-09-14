// 使用现有浏览器验证提交错误的位置、焦点和输入恢复，不启动服务器或执行 Git。
// 用法：node tools/verify-ux-commit-feedback.cjs <playwright 路径> <浏览器路径> <证据目录>
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, browserPath, outputPath] = process.argv.slice(2);
  assert.ok(modulePath && browserPath && outputPath, '请提供现有依赖、浏览器和证据目录。');
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  const results = [];
  try {
    await fs.mkdir(outputPath, { recursive: true });
    for (const theme of ['light', 'dark']) for (const dpi of [1, 1.25, 1.5]) for (const size of [13, 19, 40]) {
      const context = await browser.newContext({ viewport: size === 40 ? { width: 1024, height: 640 } : { width: 1645, height: 900 }, deviceScaleFactor: dpi });
      try {
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        for (const state of ['normal', 'validation', 'hook-failure', 'empty']) {
          const url = pathToFileURL(path.resolve(__dirname, `../docs/ux-mockups/${state === 'empty' ? 'commit-empty' : 'commit-changes'}.html`));
          url.search = new URLSearchParams({ theme, 'commit-state': state, 'ui-size': String(size) });
          await page.goto(url.href, { waitUntil: 'domcontentloaded' });
          await page.waitForSelector('body[data-typography-preview="ready"]');
          const box = page.locator('.commit-message-box');
          await box.waitFor();
          const input = box.locator('textarea');
          const feedback = box.locator('.commit-feedback');
          const original = await box.boundingBox();
          const listSelector = state === 'empty' ? '.changes-layout > .empty-tool-state' : '.changes-layout > .changes-list';
          const listBounds = await page.locator(listSelector).boundingBox();
          const actionBounds = await page.locator('.commit-actions').boundingBox();
          const caption = await feedback.textContent();
          assert.equal(caption, state === 'normal' || state === 'empty' ? '提交信息'
            : state === 'validation' ? '提交信息不能为空。' : 'commit-msg hook 拒绝提交。请检查仓库提交规则。');
          assert.equal(await feedback.getAttribute('title'), caption);
          const labelBounds = await feedback.boundingBox();
          const editBounds = await input.boundingBox();
          assert.ok(labelBounds.y + labelBounds.height <= editBounds.y + 1);
          assert.ok(labelBounds.x >= original.x && editBounds.x + editBounds.width <= original.x + original.width);
          const controls = await page.evaluate(() => {
            const rect = selector => {
              const r = document.querySelector(selector).getBoundingClientRect();
              return { left: r.left, top: r.top, right: r.right, bottom: r.bottom, height: r.height };
            };
            return { commit: rect('.commit-actions > .primary-button'), push: rect('.commit-actions > .secondary-button'),
              settings: rect('.commit-actions > .icon-button'), amend: rect('.commit-amend'), last: rect('.commit-last'),
              count: rect('.commit-count'), side: rect('.side-tool') };
          });
          assert.ok(controls.commit.right <= controls.push.left || controls.commit.bottom <= controls.push.top);
          assert.ok(controls.commit.right <= controls.settings.left);
          if (state !== 'empty') assert.ok(controls.amend.right <= controls.last.left && controls.last.right <= controls.count.left);
          assert.equal(controls.settings.height, 30);
          assert.ok(editBounds.height >= size, '正文至少容纳一行输入。');
          assert.ok(controls.push.bottom <= controls.side.bottom, '大字号动作不得越过面板。');
          assert.equal(await input.inputValue(), state === 'hook-failure' ? 'fix: 保留失败草稿' : '');
          if (state === 'normal') {
            await page.locator('.commit-actions > .primary-button').click();
            assert.equal(page.url(), url.href, '空提交不能跳转其他页面。');
            assert.equal(await feedback.textContent(), '提交信息不能为空。');
            assert.ok(await input.evaluate(element => document.activeElement === element));
            assert.deepEqual(await box.boundingBox(), original);
            assert.deepEqual(await page.locator('.changes-layout > .changes-list').boundingBox(), listBounds);
            assert.deepEqual(await page.locator('.commit-actions').boundingBox(), actionBounds);
          }
          if (dpi === 1 && state === 'validation')
            await page.screenshot({ path: path.join(outputPath, `commit-${theme}-${size}-${state}.png`) });
          if (state === 'empty') {
            assert.ok(await input.isDisabled());
            assert.ok(await page.locator('.commit-actions > .primary-button').isDisabled());
            if (size === 40) assert.equal(await page.locator('.empty-tool-state p').isVisible(), false);
          } else {
            await input.fill('fix: 更正提交信息');
            assert.equal(await feedback.textContent(), '提交信息');
            assert.equal(await feedback.getAttribute('title'), '提交信息');
          }
          assert.deepEqual(await box.boundingBox(), original);
          assert.deepEqual(errors, []);
          results.push({ theme, dpi, size, state, passed: true });
        }
      } finally { await context.close(); }
    }
  } finally { await browser.close(); }
  await fs.writeFile(path.join(outputPath, 'html-feedback.json'), JSON.stringify(results, null, 2) + '\n');
  process.stdout.write(`提交反馈 ${results.length} 组布局与交互检查通过。\n`);
}

main().catch(error => { process.stderr.write(`${error.stack}\n`); process.exitCode = 1; });
