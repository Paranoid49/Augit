// 使用延迟的真实桥接消息验证设置按钮语义、保存单飞和窗口代次。
const assert = require('node:assert/strict');

module.exports = async function verifySettingsState({ openScene, check }) {
  for (const theme of ['light', 'dark']) {
    const scene = await openScene(`scene=git-history&theme=${theme}&ui-font-size=12`);
    const page = scene.page;
    const open = async () => {
      await page.locator('.titlebar [aria-label="Settings"]').click();
      await page.waitForSelector('.settings-window .font-family-select');
    };
    const release = async (fail = false) => {
      await page.evaluate(fail => window.__settingsRelease(fail), fail);
      await page.waitForFunction(() => document.querySelector('.settings-window .dialog')?.getAttribute('aria-busy') !== 'true');
    };
    try {
      await page.evaluate(() => {
        const original = window.__hostStub;
        window.__settingsRequests = [];
        window.__settingsHold = false;
        window.__hostStub = async (method, params) => {
          if (method !== 'settings/write') return original(method, params);
          window.__settingsRequests.push(params);
          if (window.__settingsHold) {
            const failed = await new Promise(resolve => { window.__settingsRelease = resolve; });
            if (failed) return { saved: false, reason: '测试保存失败' };
          }
          return original(method, params);
        };
      });
      await open();
      await page.locator('[data-setting="textFontFamily"]').selectOption('Tahoma');
      await page.locator('[data-settings-action="cancel"]').focus();
      await page.keyboard.press('Enter');
      assert.equal(await page.locator('.settings-window').count(), 0);
      assert.equal(await page.evaluate(() => window.__settingsRequests.length), 0);
      check(`${theme} Cancel 的 Enter 关闭且零写入`, true);

      await open();
      await page.locator('[data-settings-page="file-view"]').click();
      await page.locator('[data-settings-nav="back"]').focus();
      await page.keyboard.press('Enter');
      assert.equal(await page.locator('[data-setting="textFontFamily"]').count(), 1);
      await page.locator('[data-restore-reference]').focus();
      await page.keyboard.press('Enter');
      assert.equal(await page.evaluate(() => window.__settingsRequests.length), 0);
      await page.locator('[data-setting="textFontFamily"]').selectOption('Tahoma');
      await page.locator('[data-setting="theme"]').focus();
      await page.keyboard.press('Enter');
      await page.keyboard.press('Escape');
      assert.equal(await page.evaluate(() => window.__settingsRequests.length), 0);
      // 组词与旧版 IME 的 229 键码都不能提交；长按也不能重复。
      await page.locator('[data-setting="fontSize"]').evaluate(node => {
        node.focus();
        for (const flags of [{ isComposing: true }, { keyCode: 229 }, { repeat: true }]) {
          node.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, ...flags }));
        }
      });
      assert.equal(await page.evaluate(() => window.__settingsRequests.length), 0);
      await page.locator('[data-settings-action="apply"]').focus();
      await page.keyboard.press('Enter');
      await page.waitForFunction(() => window.__augitLive.settings.textFontFamily === 'Tahoma');
      assert.equal(await page.locator('.settings-window').count(), 1);
      assert.equal(await page.locator('[data-settings-action="apply"]').isDisabled(), true);
      check(`${theme} 导航、恢复、选择框与 IME 保持自身语义，Apply 保存留窗`, true);
      await page.locator('[data-settings-action="cancel"]').click();

      await open();
      await page.locator('[data-setting="textFontFamily"]').selectOption('Segoe UI');
      await page.evaluate(() => { window.__settingsHold = true; });
      await page.locator('[data-setting="fontSize"]').focus();
      await page.keyboard.press('Enter');
      await page.waitForFunction(() => typeof window.__settingsRelease === 'function');
      const count = await page.evaluate(() => window.__settingsRequests.length);
      assert.equal(await page.locator('[data-settings-action="save"]').isDisabled(), true);
      assert.equal(await page.locator('[data-setting="textFontFamily"]').isDisabled(), true);
      await page.locator('[data-settings-action="save"]').evaluate(node => node.click());
      await page.evaluate(() => window.__augitSaveSettings());
      assert.equal(await page.evaluate(() => window.__settingsRequests.length), count);
      await page.locator('[data-settings-action="cancel"]').click();
      await open();
      await page.locator('[data-setting="textFontFamily"]').selectOption('Courier New');
      await release();
      assert.equal(await page.locator('.settings-window').count(), 1);
      assert.equal(await page.locator('[data-setting="textFontFamily"]').inputValue(), 'Courier New');
      assert.equal(await page.locator('.settings-failure').count(), 0);
      assert.equal(await page.locator('[data-settings-action="save"]').isEnabled(), true);
      check(`${theme} 慢保存冻结修改和重复提交，旧成功不关闭新窗或清除新草稿`, true);

      await page.locator('[data-settings-action="apply"]').click();
      await page.waitForFunction(() => document.querySelector('.settings-window .dialog').getAttribute('aria-busy') === 'true');
      await release(true);
      await page.waitForSelector('.settings-failure');
      assert.equal(await page.locator('[data-setting="textFontFamily"]').inputValue(), 'Courier New');
      assert.equal(await page.locator('[data-setting="textFontFamily"]').isEnabled(), true);
      assert.equal(await page.locator('[data-settings-action="apply"]').isEnabled(), true);
      check(`${theme} 保存失败保留草稿并允许重试`, true);
      await page.locator('[data-settings-action="apply"]').click();
      await page.waitForFunction(() => document.querySelector('.settings-window .dialog').getAttribute('aria-busy') === 'true');
      await page.keyboard.press('Escape');
      await open();
      await page.locator('[data-setting="textFontFamily"]').selectOption('Segoe UI');
      await release(true);
      assert.equal(await page.locator('.settings-failure').count(), 0);
      assert.equal(await page.locator('[data-setting="textFontFamily"]').inputValue(), 'Segoe UI');
      check(`${theme} Esc 关闭重开后旧失败不覆盖新窗口错误和草稿`, true);
      await page.evaluate(() => { window.__settingsHold = false; });
      await page.locator('[data-settings-action="save"]').focus();
      await page.keyboard.press('Enter');
      await page.waitForSelector('.settings-window', { state: 'detached' });
      assert.deepEqual(scene.errors, []);
      check(`${theme} OK 保存成功后仅关闭所属窗口且无未捕获异常`, true);
    } finally {
      await page.close();
    }
  }
};
