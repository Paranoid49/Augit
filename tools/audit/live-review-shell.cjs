// 同尺寸实机验证：只连接本轮隔离 CDP，不写用户字体设置，不关闭用户原有实例。
const fs = require('node:fs/promises');
const path = require('node:path');
const assert = require('node:assert/strict');
(async () => {
  const [modulePath, endpoint, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.connectOverCDP(endpoint);
  try {
    const page = browser.contexts()[0].pages().find(page => page.url().includes('augit'));
    assert.ok(page);
    await page.reload();
    await page.waitForFunction(() => window.__augitReady && window.__augitSettingsReady && window.__augitHistoryReady);
    const value = await page.evaluate(() => {
      const box = node => { const rect = node.getBoundingClientRect(); return { x: rect.x, y: rect.y, width: rect.width, height: rect.height }; };
      return { width: innerWidth, height: innerHeight, dpi: devicePixelRatio,
        font: getComputedStyle(document.body).fontFamily, size: getComputedStyle(document.body).fontSize,
        side: box(document.querySelector('.side-tool')), rail: box(document.querySelector('.tool-rail')),
        buttons: [...document.querySelectorAll('.rail-button')].map(node => ({ ...box(node), tool: node.dataset.tool,
          drawing: { inset: getComputedStyle(node, '::before').inset, radius: getComputedStyle(node, '::before').borderRadius } })),
        rows: [...document.querySelectorAll('.side-content.tree .tree-row')].slice(0, 10).map(box),
        errors: window.__augitErrors || [] };
    });
    assert.equal(value.size, '12px'); assert.equal(value.dpi, 1.75);
    assert.ok(value.rows.every(row => Math.abs(row.height - 24) < .1));
    assert.ok(Math.abs(value.rail.width - 37) < .1 && Math.abs(value.side.x - 41) < .1);
    assert.deepEqual(value.errors, []);
    await fs.mkdir(output, { recursive: true });
    await fs.writeFile(path.join(output, 'augit-same-size.json'), JSON.stringify(value, null, 2) + '\n');
    await page.screenshot({ path: path.join(output, 'augit-same-size-cdp.png') });
    console.log(JSON.stringify(value));
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
