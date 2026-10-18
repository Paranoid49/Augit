// 读取本次真实外壳的生效字体、DPI、工具轨与 Git 工具窗，不保存或覆盖用户设置。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');

(async () => {
  const [modulePath, endpoint, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  let browser;
  let page;
  for (let attempt = 0; attempt < 20; attempt++) {
    try { browser = await chromium.connectOverCDP(endpoint); break; }
    catch (error) {
      if (attempt === 19) throw error;
      await new Promise(resolve => setTimeout(resolve, 500));
    }
  }
  try {
    await fs.mkdir(output, { recursive: true });
    page = browser.contexts()[0].pages().find(page => /augit/i.test(page.url()));
    assert.ok(page, '只连接到本次启动的 Augit 页面。');
    await page.waitForFunction('window.__augitReady && window.__augitSettingsReady && window.__augitHistoryReady', null, { timeout: 45000 });
    const result = await page.evaluate(() => {
      const style = getComputedStyle(document.body);
      const rect = node => { const box = node.getBoundingClientRect(); return { width: box.width, height: box.height, x: box.x, y: box.y }; };
      const root = getComputedStyle(document.documentElement);
      return { dpi: devicePixelRatio, width: innerWidth, height: innerHeight,
        uiFont: style.fontFamily, uiSize: style.fontSize, codeFont: root.getPropertyValue('--augit-code').trim(),
        codeSize: root.getPropertyValue('--augit-code-size').trim(), language: document.documentElement.lang,
        projectColor: document.querySelector('.titlebar').dataset.projectColor,
        status: { ...rect(document.querySelector('.statusbar')), background: getComputedStyle(document.querySelector('.statusbar')).backgroundColor },
        rail: [...document.querySelectorAll('.rail-button')].map(node => ({ tool: node.dataset.tool,
          classes: node.className, ...rect(node), inkGrid: node.querySelector('svg').getAttribute('viewBox'),
          background: getComputedStyle(node, '::before').backgroundColor })),
        rows: [...document.querySelectorAll('.commit-row')].slice(0, 3).map(rect),
        fileGap: [...document.querySelectorAll('.log-ref-panel .tree-row, .changed-files .tree-row')].slice(0, 3).map(node => getComputedStyle(node).gap),
        errors: window.__augitErrors || [],
      };
    });
    assert.equal(result.uiSize, '12px');
    assert.ok(result.uiFont.includes('Microsoft YaHei UI'));
    assert.equal(result.codeSize, '13px');
    assert.equal(result.language, 'en');
    assert.equal(result.projectColor, '4');
    assert.deepEqual(result.errors, []);
    await fs.writeFile(path.join(output, 'shell-reference-current.json'), JSON.stringify(result, null, 2) + '\n');
    console.log(JSON.stringify(result));
    await page.screenshot({ path: path.join(output, 'shell-reference-current.png') });
  } finally {
    await page?.evaluate(() => document.querySelector('[data-window-action="close"]')?.click()).catch(() => {});
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
