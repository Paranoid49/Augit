// 阴影采集必须读取独立设置控制器，不能再把主窗重挂状态当作设置状态。
const assert = require('node:assert/strict');
const {waitAsyncCondition}=require('./wait-async-condition.cjs');
async function settingsState(main) {
  return main.evaluate(async () => {
    const {invoke} = await import('/src/bridge.js');
    return {settings: await invoke('window/settings-state'), modal: await invoke('window/modal-state')};
  });
}
function verifyState({settings, modal}, active) {
  assert.equal(settings.active, active);
  assert.equal(modal.active, false, '独立设置不得重挂主控制器');
  assert.equal(modal.snapshotCount, 0, '独立设置不得创建背景快照');
  assert.equal(modal.captureCoverWindow, 0, '独立设置不得创建遮盖窗口');
  assert.equal(modal.ownerEnabled, !active);
  if (active) {
    assert.equal(settings.ready, true);
    assert.equal(settings.visible, true);
    assert.ok(Number.isSafeInteger(settings.window) && settings.window > 0);
  } else assert.equal(settings.window, 0);
  return {...modal, window: settings.window, independentSettings: settings};
}
async function settingsSurface(browser, main, pause = ms => new Promise(resolve => setTimeout(resolve, ms))) {
  await waitAsyncCondition(async()=>(await settingsState(main)).settings.ready, {label:'阴影采集前设置呈现'});
  // 宿主呈现完成与CDP发现新target不同时到达；只等待缺失，不掩盖重复。
  let pages = [];
  for (let attempt = 0; attempt < 50 && pages.length === 0; attempt++) {
    pages = browser.contexts().flatMap(context => context.pages())
      .filter(page => new URL(page.url()).searchParams.get('surface') === 'settings');
    if (!pages.length) await pause(100);
  }
  assert.equal(pages.length, 1, '设置必须恰有一个独立页面');
  assert.equal(await main.locator('.settings-window').count(), 0, '主窗不得保留第二份设置表单');
  const page = pages[0];
  await page.waitForFunction(() => window.__augitReady && window.__augitSettingsReady);
  assert.equal(await page.locator('.settings-window .dialog').count(), 1);
  verifyState(await settingsState(main), true);
  return page;
}
module.exports = {settingsState, verifyState, settingsSurface};
