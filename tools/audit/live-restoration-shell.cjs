// 通过真实 WebView2 的诊断端口校准本机外观；截图只作为外壳结构与生效配置证据，
// 不代替未遮挡屏幕上的 PyCharm 字体栅格化验收。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');

(async () => {
  const [modulePath, endpoint, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  let browser;
  for (let attempt = 0; attempt < 20; attempt++) {
    try { browser = await chromium.connectOverCDP(endpoint); break; }
    catch (error) {
      if (attempt === 19) throw error;
      await new Promise((resolve) => setTimeout(resolve, 500));
    }
  }
  try {
    await fs.mkdir(output, { recursive: true });
    const page = browser.contexts()[0].pages().find((page) => /augit/i.test(page.url()));
    assert.ok(page, '必须连接到本次 Augit 的页面。');
    await page.waitForFunction('window.__augitReady && window.__augitSettingsReady', null, { timeout: 45000 });
    const state = () => page.evaluate(() => ({
      dpi: devicePixelRatio, width: innerWidth, height: innerHeight,
      font: getComputedStyle(document.body).fontFamily, size: getComputedStyle(document.body).fontSize,
      codeFont: getComputedStyle(document.documentElement).getPropertyValue('--augit-code').trim(),
      codeSize: getComputedStyle(document.documentElement).getPropertyValue('--augit-code-size').trim(),
      settings: window.__augitLive.settings,
      errors: window.__augitErrors || [],
    }));
    const before = await state();
    await page.locator('.titlebar [aria-label="Settings"]').click();
    await page.waitForSelector('.settings-window [data-restore-reference]');
    await page.screenshot({ path: path.join(output, 'settings-before-font-calibration.png') });
    await page.locator('[data-restore-reference]').click();
    await page.waitForFunction(() => window.__augitLive.settingsDraft.monospaceFontFamily);
    const expectedMono = await page.evaluate(() => window.__augitLive.settingsDraft.monospaceFontFamily);
    await page.locator('[data-settings-action="apply"]').click();
    await page.waitForFunction('window.__augitLive.settings.fontSize === 12 && window.__augitLive.settings.codeFontSize === 13');
    const after = await state();
    assert.equal(after.size, '12px');
    assert.equal(after.settings.textFontFamily, 'Microsoft YaHei UI');
    assert.equal(after.settings.monospaceFontFamily, expectedMono);
    assert.equal(after.settings.theme, 'Light');
    assert.deepEqual(after.settings.openFiles, before.settings.openFiles);
    assert.equal(after.settings.activeFile, before.settings.activeFile);
    await page.screenshot({ path: path.join(output, 'settings-after-font-calibration.png') });
    await page.locator('[data-settings-action="cancel"]').click();
    await page.screenshot({ path: path.join(output, 'main-after-font-calibration.png') });
    await fs.writeFile(path.join(output, 'shell-font-calibration.json'), JSON.stringify({ before, after }, null, 2) + '\n');
    console.log(JSON.stringify({ before: [before.font, before.size, before.codeSize], after: [after.font, after.size, after.codeSize], dpi: after.dpi, errors: after.errors }));
    await page.locator('[data-window-action="close"]').click();
  } finally { await browser.close(); }
})().catch((error) => { console.error(error); process.exitCode = 1; });
