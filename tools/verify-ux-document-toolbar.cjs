// 文档工具栏的统一布局验证；使用现有浏览器，不启动服务器。
// 用法：node tools/verify-ux-document-toolbar.cjs <Playwright 路径> <浏览器路径> [截图目录]
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, browserPath, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  const errors = [];
  let passed = 0;
  try {
    if (output) await fs.mkdir(output, { recursive: true });
    for (const scale of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ viewport: { width: 1024, height: 640 }, deviceScaleFactor: scale });
      try {
        const page = await context.newPage();
        page.on('pageerror', error => errors.push(error.message));
        for (const theme of ['light', 'dark']) for (const size of [13, 40])
          for (const scene of ['text-viewer', 'markdown-preview', 'json-preview', 'image-preview', 'target']) {
            const label = `${scene}-${theme}-${size}-${scale}`;
            const url = pathToFileURL(path.resolve(__dirname, `../docs/ux-mockups/${scene === 'target' ? 'json-preview' : scene}.html`));
            url.search = new URLSearchParams({ theme, 'ui-size': String(size), 'json-state': 'invalid', ...(scene === 'target' ? { target: '1' } : {}) });
            await page.goto(url.href);
            await page.waitForSelector('body[data-typography-preview="ready"]');
            const data = await page.evaluate(() => {
              const rect = el => { const r = el.getBoundingClientRect(); return { left: r.left, right: r.right, top: r.top, bottom: r.bottom, width: r.width, height: r.height }; };
              const toolbar = document.querySelector('.document-toolbar') || document.querySelector('.markdown-tab-actions');
              const controls = [...toolbar.children];
              const error = document.querySelector('.json-error'), target = document.querySelector('.document-target');
              const textHeight = el => { const range = document.createRange(); range.selectNodeContents(el); return range.getBoundingClientRect().height; };
              return { toolbar: rect(toolbar), children: controls.map(el => ({ ...rect(el), label: el.getAttribute('aria-label'),
                button: el.tagName === 'BUTTON', text: el.textContent, scroll: el.scrollWidth, client: el.clientWidth })),
                error: error ? { ...rect(error), textHeight: textHeight(error), scroll: error.scrollHeight, client:error.clientHeight, overflow:getComputedStyle(error).overflowY } : null,
                target: target ? { ...rect(target), textHeight: textHeight(target), scroll: target.scrollHeight, client:target.clientHeight, overflow:getComputedStyle(target).overflowY } : null,
                code: document.querySelector('.code-view') ? rect(document.querySelector('.code-view')) : null };
            });
            let right = data.toolbar.left;
            for (const control of data.children) {
              assert.ok(control.left >= right - .1, `${label} 控件不得重叠`);
              assert.ok(control.right <= data.toolbar.right && control.bottom <= data.toolbar.bottom, label);
              if (control.button) {
                const hit = Math.round((scene === 'markdown-preview' ? 26 : 28) * Math.max(.75, Math.round(size / 12 * 4) / 4));
                assert.equal(control.width, hit, label); assert.equal(control.height, hit, label);
              }
              right = control.right;
            }
            if (scene === 'image-preview') {
              for (const control of data.children.filter(item => !item.button))
                assert.ok(control.scroll <= control.client, `${label} 图片文字不得裁切`);
              // 大字号最小窗口的适应比例可能已达下限，禁用的Zoom Out不进入Tab顺序。
              const zoomOut = page.getByRole('button', { name: 'Zoom Out', exact: true });
              if (await zoomOut.isEnabled()) {
                await zoomOut.focus();
                await page.keyboard.press('Tab');
                assert.equal(await page.evaluate(() => document.activeElement.getAttribute('aria-label')), 'Zoom In', label);
              } else await page.getByRole('button', { name: 'Zoom In', exact: true }).focus();
              await page.keyboard.press('Tab');
              assert.equal(await page.evaluate(() => document.activeElement.getAttribute('aria-label')), 'Fit to View', label);
            }
            if (data.error) {
              assert.ok(data.error.height >= data.error.textHeight + 16 - 1 || data.error.overflow === 'auto' && data.error.scroll > data.error.client && data.code.height > 0, label);
              assert.equal(data.code.top, data.error.bottom, label);
            }
            if (data.target) {
              assert.equal(data.target.top, data.toolbar.bottom, label);
              assert.equal(data.target.bottom, data.error.top, label);
              assert.ok(data.target.height >= data.target.textHeight + 16 - 1 || data.target.overflow === 'auto' && data.target.scroll > data.target.client && data.code.height > 0, label);
            }
            if (output && scale === 1 && (scene === 'image-preview' || scene === 'target'))
              await page.screenshot({ path: path.join(output, `mockup-${label}.png`) });
            passed++;
          }
      } finally { await context.close(); }
    }
    assert.deepEqual(errors, []);
    console.log(`PASS=${passed} 文档工具栏主题、字号、DPI 与键盘顺序检查通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
