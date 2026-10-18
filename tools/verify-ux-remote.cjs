// 验证远端视觉稿的 Name/URL 表格、固定工具条和底栏、限高滚动；不启动服务或执行 Git。
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
          const dialog = page.locator('.remote-dialog');
          assert.deepEqual(await dialog.locator('.remote-table-head span').allTextContents(), ['Name', 'URL']);
          assert.deepEqual(await dialog.locator('.remote-toolbar button').evaluateAll(nodes => nodes.map(node => node.dataset.remoteAction)), ['add', 'remove', 'edit']);
          assert.equal(await dialog.locator('.remote-row svg').count(), 0);
          assert.equal(await dialog.locator('.management-detail, .management-list, input').count(), 0);
          assert.equal(await dialog.locator('.dialog-footer button').count(), 1);
          assert.equal(await dialog.locator('.dialog-footer button').innerText(), 'OK');
          assert.equal(await dialog.locator('.footer-help').count(), 0);
          const layout = await dialog.evaluate(node => {
            const bounds = node.getBoundingClientRect(), table = node.querySelector('.remote-table');
            const footer = node.querySelector('.dialog-footer').getBoundingClientRect();
            const toolbar = node.querySelector('.remote-toolbar').getBoundingClientRect();
            const rows = [...node.querySelectorAll('.remote-row')];
            const textHeight = parseFloat(getComputedStyle(rows[0]).fontSize);
            return {
              fits: bounds.left >= 0 && bounds.right <= innerWidth && bounds.top >= 0 && bounds.bottom <= innerHeight,
              rowFits: rows.every(row => row.getBoundingClientRect().height >= textHeight),
              tableInside: table.getBoundingClientRect().top >= toolbar.bottom - 1 && table.getBoundingClientRect().bottom <= footer.top + 1,
              row: getComputedStyle(node).getPropertyValue('--remote-row'),
            };
          });
          assert.ok(layout.fits && layout.rowFits && layout.tableInside, `${label} 主表、工具条或底栏越界：${JSON.stringify(layout)}`);
          assert.ok(Number.parseFloat(layout.row) >= size);
          const footerBefore = await dialog.locator('.dialog-footer').boundingBox();
          await dialog.locator('.dialog-footer button').focus();
          const visible = await dialog.locator('.dialog-footer button').evaluate(button => {
            const bounds = button.getBoundingClientRect(), panel = button.closest('.dialog-footer').getBoundingClientRect();
            return bounds.top >= panel.top - 1 && bounds.bottom <= panel.bottom + 1;
          });
          assert.ok(visible, `${label} OK 按钮必须完整可见。`);
          assert.deepEqual(await dialog.locator('.dialog-footer').boundingBox(), footerBefore);
          await dialog.locator('.remote-table').focus();
          if (dpi === 1) await page.screenshot({ path: path.join(output, `remote-${label}.png`) });
          const scroll = await dialog.evaluate(node => {
            const table = node.querySelector('.remote-table');
            const toolbar = node.querySelector('.remote-toolbar').getBoundingClientRect().top;
            const footer = node.querySelector('.dialog-footer').getBoundingClientRect().top;
            const sample = table.querySelector('.remote-row');
            for (let index = 0; index < 30; index++) table.appendChild(sample.cloneNode(true));
            table.scrollTop = 300;
            return { scrolled: table.scrollTop > 0, toolbarStable: toolbar === node.querySelector('.remote-toolbar').getBoundingClientRect().top,
              footerStable: footer === node.querySelector('.dialog-footer').getBoundingClientRect().top };
          });
          assert.ok(scroll.scrolled && scroll.toolbarStable && scroll.footerStable, `${label} 表格滚动不得移动动作区。`);
          passed++;
        }
      } finally { await context.close(); }
    }
    assert.deepEqual(errors, []);
    console.log(`PASS=${passed} 远端表格主题、字号、DPR 与滚动组合通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
