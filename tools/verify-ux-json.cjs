// 验证 JSON 视觉稿的文件身份、只读模式、错误定位及字号布局，不启动服务器。
// 用法：node tools/verify-ux-json.cjs <playwright 模块路径> <浏览器 exe 路径> [截图目录]
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const fs = require('node:fs/promises');

async function main() {
  const [modulePath, browserPath, outputPath] = process.argv.slice(2);
  assert.ok(modulePath && browserPath, '请传入现有 Playwright 模块和浏览器路径。');
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  try {
    if (outputPath) await fs.mkdir(outputPath, { recursive: true });
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    let passed = 0;
    for (const size of [13, 40]) for (const theme of ['light', 'dark'])
      for (const width of [1024, 1180]) for (const state of ['formatted', 'source', 'invalid']) {
        const label = `${theme}-${size}-${width}-${state}`;
        await page.setViewportSize({ width, height: width === 1024 ? 640 : 760 });
        const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/json-preview.html'));
        url.search = new URLSearchParams({ 'ui-size': String(size), theme, 'json-state': state });
        await page.goto(url.href);
        await page.waitForSelector('body[data-typography-preview="ready"]');
        const code = page.locator('.json-document .code-view');
        const body = () => code.locator('.code-line > span:last-child').allTextContents();
        const text = (await body()).join('\n');
        assert.match(await page.locator('.editor-tab.active').innerText(), /global\.json/, label);
        assert.equal(await page.locator('.document-path').innerText(), 'Augit › global.json　只读', label);
        assert.equal((await page.locator('.status-path').innerText()).replace(/\s/g, ''), 'Augit›global.json', label);
        assert.equal(await code.locator('textarea, input, [contenteditable="true"], .json-key, .json-string, .json-number').count(), 0, label);
        const geometry = await page.evaluate(() => {
          const rect = selector => {
            const b = document.querySelector(selector).getBoundingClientRect();
            return { left: b.left, right: b.right, top: b.top, bottom: b.bottom, height: b.height };
          };
          return { parent: rect('.editor-content'), toolbar: rect('.document-toolbar'), path: rect('.document-path'),
            modes: rect('.document-modes'), more: rect('.document-toolbar > .icon-button'), code: rect('.code-view'),
            error: document.querySelector('.json-error') ? rect('.json-error') : null,
            font: getComputedStyle(document.querySelector('.code-view')).fontSize };
        });
        assert.equal(geometry.font, '13px', label);
        assert.ok(geometry.path.right <= geometry.modes.left && geometry.modes.right <= geometry.more.left, label);
        assert.ok(geometry.more.right <= geometry.parent.right, label);
        assert.ok(geometry.code.bottom <= geometry.parent.bottom + .01, label);
        if (state === 'invalid') {
          assert.equal(await page.getByRole('button', { name: '格式化', exact: true }).isDisabled(), true, label);
          assert.ok(geometry.error.top >= geometry.toolbar.bottom && geometry.error.bottom <= geometry.code.top, label);
          if (size === 40) assert.ok(geometry.error.height > 60, `${label} 错误文字必须换行`);
          await page.getByRole('button', { name: '定位 JSON 错误' }).focus();
          await page.keyboard.press('Enter');
          assert.equal(await code.evaluate(element => element === document.activeElement), true, label);
          assert.equal(await code.locator('.active').getAttribute('data-line'), '4', label);
        } else {
          const value = JSON.parse(text);
          assert.deepEqual(Object.keys(value.sdk), ['version', 'rollForward', 'allowPrerelease'], label);
          await page.getByRole('button', { name: '原文', exact: true }).click();
          const source = (await body()).join('\n');
          assert.equal(source.split('\n').length, 2, `${label} 原文保留文件末尾换行`);
          await page.getByRole('button', { name: '格式化', exact: true }).click();
          assert.equal((await body()).join('\n'), JSON.stringify(JSON.parse(source), null, 2), label);
          if (state === 'source') await page.getByRole('button', { name: '原文', exact: true }).click();
        }
        await code.focus();
        await page.keyboard.type('should-not-edit');
        assert.equal((await body()).join('\n'), text, `${label} 只读正文不能接受输入`);
        if (outputPath && width === 1024) await page.screenshot({ path: path.join(outputPath, `mockup-${label}.png`) });
        passed++;
      }
    assert.deepEqual(errors, [], '视觉稿不得出现脚本错误。');
    console.log(`JSON 视觉稿检查通过：${passed} 个状态。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
