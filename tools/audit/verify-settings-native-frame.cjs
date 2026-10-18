// 验证原生设置窗的外框归属，不把静态样式检查当作系统逐帧验收。
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { chromium } = require(process.argv[2]);
(async () => {
  let browser, passed = 0;
  try {
    browser = await chromium.launch({ executablePath: process.argv[3], headless: true });
    for (const dpr of [1, 1.75, 2]) for (const size of [12, 40]) for (const theme of ['light', 'dark']) {
      const context = await browser.newContext({ viewport: { width: 1400, height: 930 }, deviceScaleFactor: dpr });
      try {
        const page = await context.newPage(), errors = [];
        page.on('pageerror', error => errors.push(error.message));
        const url = pathToFileURL(path.resolve('docs/ux-mockups/settings.html'));
        url.search = 'theme=' + theme + '&ui-size=' + size;
        await page.goto(url.href);
        await page.waitForSelector('body[data-typography-preview="ready"]');
        const result = await page.locator('.dialog:has(.settings-layout)').evaluate(dialog => {
          const read = () => {
            const style = getComputedStyle(dialog);
            return {
              geometry: [dialog, ...dialog.querySelectorAll('*')].map(node => node.getBoundingClientRect().toJSON()),
              borderWidth: style.borderTopWidth, borderColor: style.borderTopColor,
              radius: style.borderRadius, shadow: style.boxShadow
            };
          };
          const before = read();
          dialog.dataset.nativeWindow = 'true';
          const active = read();
          delete dialog.dataset.nativeWindow;
          return { before, active, restored: read() };
        });
        assert.deepEqual(result.active.geometry, result.before.geometry, '整窗和所有后代几何须保持不变');
        assert.equal(result.active.borderWidth, result.before.borderWidth, '保留边框占位');
        assert.notEqual(result.before.borderColor, 'rgba(0, 0, 0, 0)', '非原生设置仍保留网页外框');
        assert.equal(result.active.borderColor, 'rgba(0, 0, 0, 0)');
        assert.equal(result.active.radius, '0px');
        assert.equal(result.active.shadow, 'none');
        assert.deepEqual(result.restored, result.before, '移除原生标记恢复网页外观');
        assert.deepEqual(errors, []);
        passed++;
      } finally { await context.close(); }
    }
    console.log(JSON.stringify({ passed }));
  } finally { await browser?.close(); console.log('本次浏览器已关闭；未启动服务器'); }
})().catch(error => { console.error(error); process.exitCode = 1; });
