// 连接调用方隔离启动的真实WebView2；调用方负责关闭外壳和删除profile。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');

(async () => {
  const [modulePath, endpoint, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  let browser;
  try {
    for (let attempt = 0; attempt < 30; attempt++) {
      try { browser = await chromium.connectOverCDP(endpoint); break; }
      catch (error) {
        if (attempt === 29) throw error;
        await new Promise(resolve => setTimeout(resolve, 300));
      }
    }
    const context = browser.contexts()[0];
    assert.ok(context, '本次WebView2须创建浏览器上下文。');
    const page = context.pages()[0] || await context.waitForEvent('page', { timeout: 15000 });
    // 诊断端口可先于首个文档导航就绪；隔离profile只有本次外壳页面。
    await page.waitForURL(/augit/i, { timeout: 30000 });
    await page.waitForFunction(() => window.__augitReady && window.__augitSettingsReady);
    const result = await page.evaluate(() => ({ dpr: devicePixelRatio, width: innerWidth, height: innerHeight,
      uiFont: getComputedStyle(document.body).fontFamily, uiSize: getComputedStyle(document.body).fontSize }));
    await fs.mkdir(output, { recursive: true });
    await page.locator('.titlebar [aria-label="Settings"]').click();
    await page.waitForFunction(() => window.__augitLive.fontFamilies?.length > 0);
    result.fonts = await page.evaluate(() => ({
      jetBrainsMono: window.__augitLive.fontFamilies.includes('JetBrains Mono'),
      cascadiaMono: window.__augitLive.fontFamilies.includes('Cascadia Mono'),
      consolas: window.__augitLive.fontFamilies.includes('Consolas'),
    }));
    await page.locator('[data-setting="textFontFamily"]').selectOption('Tahoma');
    await page.locator('[data-settings-action="cancel"]').focus();
    await page.keyboard.press('Enter');
    assert.equal(await page.locator('.settings-window').count(), 0);
    assert.equal(await page.evaluate(() => window.__augitLive.settings.textFontFamily), 'Microsoft YaHei UI');
    await page.locator('.titlebar [aria-label="Settings"]').click();
    await page.locator('[data-restore-reference]').click();
    await page.waitForFunction(() => window.__augitLive.settingsDraft.monospaceFontFamily);
    result.reference = await page.evaluate(() => ({ draft: window.__augitLive.settingsDraft, message: document.querySelector('.settings-window .footer-help').textContent }));
    assert.equal(result.reference.draft.monospaceFontFamily, result.fonts.jetBrainsMono ? 'JetBrains Mono' : result.fonts.cascadiaMono ? 'Cascadia Mono' : 'Consolas');
    await page.locator('[data-settings-page="file-view"]').click();
    await page.screenshot({ path: path.join(output, 'settings-font-baseline.png') });
    await page.locator('[data-settings-action="apply"]').focus();
    assert.equal(await page.locator('[data-settings-action="apply"]').isEnabled(), true);
    await page.keyboard.press('Enter');
    await page.waitForFunction(() => document.querySelector('.settings-window .dialog').dataset.settingsState === 'saved');
    assert.equal(await page.locator('.settings-window').count(), 1);
    assert.equal(await page.locator('[data-settings-action="apply"]').isDisabled(), true);
    await page.locator('[data-settings-action="cancel"]').click();
    await page.waitForSelector('.markdown-preview');
    assert.equal(await page.locator('.markdown-document > .document-toolbar').count(), 0);
    await page.locator('.editor-tabs [data-markdown-mode="preview"]').click();
    await page.keyboard.press('Control+f');
    await page.waitForSelector('.markdown-document > .current-find');
    assert.equal(await page.locator('.markdown-document').getAttribute('data-markdown-mode'), 'source');
    result.previewFind = { mode: 'source', opened: true };
    await page.keyboard.press('Escape');
    await page.locator('.editor-tabs [data-markdown-mode="split"]').click();
    result.markdown = await page.evaluate(() => ({
      modes: [...document.querySelectorAll('.markdown-tab-actions button')].map(node => ({ label: node.getAttribute('aria-label'), title: node.title, pressed: node.getAttribute('aria-pressed') })),
      body: getComputedStyle(document.querySelector('.markdown-preview')).fontSize,
      heading: getComputedStyle(document.querySelector('.markdown-preview h1')).fontSize,
      code: getComputedStyle(document.querySelector('.markdown-source')).fontFamily,
    }));
    assert.equal(result.markdown.body, '12px');
    assert.equal(result.markdown.heading, '26.4px');
    assert.equal(result.markdown.modes[1].pressed, 'true');
    await page.screenshot({ path: path.join(output, 'markdown-tabs-light.png') });
    await page.locator('.titlebar [aria-label="Settings"]').click();
    await page.locator('[data-setting="theme"]').selectOption('Dark');
    await page.locator('[data-settings-action="apply"]').click();
    await page.waitForFunction(() => document.querySelector('.settings-window .dialog').dataset.settingsState === 'saved');
    await page.locator('[data-settings-action="cancel"]').click();
    await page.screenshot({ path: path.join(output, 'markdown-tabs-dark.png') });
    result.errors = await page.evaluate(() => window.__augitErrors || []);
    assert.deepEqual(result.errors, []);
    await fs.writeFile(path.join(output, 'webview2-results.json'), JSON.stringify(result, null, 2) + '\n');
    console.log(JSON.stringify(result));
    await page.locator('[data-window-action="close"]').click();
  } finally { await browser?.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
