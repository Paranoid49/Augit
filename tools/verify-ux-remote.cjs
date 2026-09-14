// 验证远端管理的真实浏览器布局、详情滚动与焦点可见性；不启动服务器或执行 Git。
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, executablePath, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath, headless: true });
  const errors = [];
  let passed = 0;
  try {
    await fs.mkdir(output, { recursive: true });
    for (const dpi of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ deviceScaleFactor: dpi });
      try {
        const page = await context.newPage();
        page.on('pageerror', error => errors.push(error.message));
        await page.route('https://unpkg.com/**', route => route.abort());
        for (const theme of ['light', 'dark']) for (const size of [13, 40]) {
          const label = `${theme}-${size}-${dpi}`;
          await page.setViewportSize({ width: 1024, height: 640 });
          const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/remote.html'));
          url.searchParams.set('theme', theme); url.searchParams.set('ui-size', size);
          await page.goto(url.href);
          await page.waitForSelector('body[data-typography-preview="ready"]');
          const dialog = page.locator('.remote-dialog'), detail = dialog.locator('.management-detail');
          const form = dialog.locator('.form-grid');
          assert.deepEqual(await form.locator('label').allTextContents(), ['名称', '获取 URL', '推送 URL']);
          assert.equal(await dialog.locator('.footer-help').count(), 0);
          const layout = await dialog.evaluate(node => {
            const bounds = node.getBoundingClientRect(), detail = node.querySelector('.management-detail');
            const footer = node.querySelector('.dialog-footer').getBoundingClientRect();
            const fields = [...node.querySelectorAll('.form-grid input')];
            const labels = [...node.querySelectorAll('.form-grid label')];
            return {
              fits: bounds.left >= 0 && bounds.right <= innerWidth && bounds.top >= 0 && bounds.bottom <= innerHeight,
              fieldFits: fields.every((field, index) => field.getBoundingClientRect().height >= parseFloat(getComputedStyle(field).fontSize)
                && field.getBoundingClientRect().left >= labels[index].getBoundingClientRect().right),
              overflowX: detail.scrollWidth > detail.clientWidth,
              needsScroll: detail.scrollHeight > detail.clientHeight,
              footerBelow: detail.getBoundingClientRect().bottom <= footer.top + 1,
            };
          });
          assert.ok(layout.fits && layout.fieldFits && layout.footerBelow, `${label} 标题、字段或底栏越界。`);
          assert.equal(layout.overflowX, false, `${label} 表单不能横向溢出。`);
          assert.equal(layout.needsScroll, size === 40, `${label} 详情滚动范围错误。`);
          const footerBefore = await dialog.locator('.dialog-footer').boundingBox();
          await dialog.getByRole('button', { name: '保存', exact: true }).focus();
          const visible = await dialog.getByRole('button', { name: '保存', exact: true }).evaluate(button => {
            const bounds = button.getBoundingClientRect(), panel = button.closest('.management-detail').getBoundingClientRect();
            return bounds.top >= panel.top && bounds.bottom <= panel.bottom;
          });
          assert.ok(visible, `${label} 聚焦保存后按钮必须完整可见。`);
          assert.deepEqual(await dialog.locator('.dialog-footer').boundingBox(), footerBefore);
          await form.locator('input').first().focus();
          assert.equal(await form.locator('input').first().inputValue(), 'origin');
          if (dpi === 1) await page.screenshot({ path: path.join(output, `remote-${label}.png`) });
          passed++;
        }
      } finally { await context.close(); }
    }
    assert.deepEqual(errors, []);
    console.log(`PASS=${passed} 远端管理字号、滚动与焦点组合通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
