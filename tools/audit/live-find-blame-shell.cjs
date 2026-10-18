// 读取本轮隔离WebView2实例：查找视觉稿与真实Git归属分场景取证，连接在finally释放。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
(async () => {
  const [modulePath, endpoint, output, mode] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  let browser;
  for (let attempt = 0; attempt < 20; attempt++) {
    try { browser = await chromium.connectOverCDP(endpoint); break; }
    catch (error) { if (attempt === 19) throw error; await new Promise(resolve => setTimeout(resolve, 500)); }
  }
  try {
    const page = browser.contexts()[0].pages().find(page => /augit/i.test(page.url()));
    assert.ok(page);
    await fs.mkdir(output, { recursive: true });
    const result = await page.evaluate(() => ({ font: getComputedStyle(document.body).fontFamily, size: getComputedStyle(document.body).fontSize, dpr: devicePixelRatio, width: innerWidth, height: innerHeight }));
    if (mode === 'find') {
      await page.waitForSelector('.current-find');
      const input = page.getByRole('textbox', { name: 'Find in File' });
      await input.fill('Git');
      await page.waitForFunction(() => document.querySelector('.find-status')?.textContent === '1/12');
      result.find = await page.locator('.current-find').evaluate(bar => {
        const rect = node => node.getBoundingClientRect().toJSON();
        return { shell: rect(bar.querySelector('.find-input-shell')), options: [...bar.querySelectorAll('.find-options button')].map(rect), count: rect(bar.querySelector('.find-status')), close: rect(bar.querySelector('[aria-label="Close Find"]')) };
      });
      assert.equal(result.find.options.length, 3);
      assert.ok(result.find.options.every(box => box.left >= result.find.shell.left && box.right <= result.find.shell.right));
      await page.screenshot({ path: path.join(output, 'shell-find.png') });
      await input.fill('missing');
      assert.equal(await page.getByRole('button', { name: 'Next Match', exact: true }).isDisabled(), true);
      await page.screenshot({ path: path.join(output, 'shell-find-no-match.png') });
    } else {
      await page.waitForFunction(() => window.__augitReady && window.__augitLive?.blame?.lines.length);
      result.blame = await page.evaluate(() => ({
        groups: new Set(window.__augitLive.blame.lines.map(line => line.committerTimestamp)).size,
        colors: [...new Set([...document.querySelectorAll('.blame-row')].map(row => getComputedStyle(row).backgroundColor))],
        timestampAvailable: window.__augitLive.blame.lines.every(line => Number.isFinite(line.committerTimestamp)),
      }));
      assert.equal(result.blame.timestampAvailable, true);
      assert.equal(result.blame.colors.length, Math.min(21, result.blame.groups));
      await page.locator('.blame-row').first().hover();
      await page.waitForSelector('.blame-tooltip');
      result.tooltip = await page.locator('.blame-tooltip').textContent();
      assert.ok(result.tooltip.includes('Author:') && result.tooltip.includes('Date:'));
      assert.equal(await page.locator('.blame-row').first().getAttribute('title'), null);
      await page.screenshot({ path: path.join(output, 'shell-blame-tooltip.png') });
      await page.keyboard.press('Escape');
      await page.evaluate(async () => { await window.__augitLoadFileHistory('web/src/mockup.js'); window.__augitRenderRegions('bottomTool'); });
      result.dates = await page.locator('.history-row[data-history-full]').evaluateAll(rows => rows.slice(0, 3).map(row => ({ text: row.children[1].textContent, title: row.children[1].title })));
      assert.ok(result.dates.length > 0 && result.dates.every(date => date.text && date.title));
      await page.screenshot({ path: path.join(output, 'shell-file-history.png') });
    }
    result.errors = await page.evaluate(() => window.__augitErrors || []);
    assert.deepEqual(result.errors, []);
    await fs.writeFile(path.join(output, `shell-${mode}.json`), JSON.stringify(result, null, 2) + '\n');
    console.log(JSON.stringify(result));
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
