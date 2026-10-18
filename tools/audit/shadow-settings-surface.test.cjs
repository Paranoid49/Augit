const test = require('node:test');
const assert = require('node:assert/strict');
const {verifyState, settingsSurface} = require('./shadow-settings-surface.cjs');
function state(active = true) {
  return {settings: {active, ready: active, visible: active, window: active ? 123 : 0},
    modal: {active: false, snapshotCount: 0, captureCoverWindow: 0, ownerEnabled: !active, owner: 456}};
}
test('独立设置按自身句柄采集，关闭后恢复所有者', () => {
  assert.equal(verifyState(state(), true).window, 123);
  assert.equal(verifyState(state(false), false).owner, 456);
});
test('拒绝主窗重挂、遮盖残留和未呈现的设置', () => {
  for (const [group, key, value] of [['modal','active',true], ['modal','snapshotCount',1],
    ['modal','captureCoverWindow',999], ['modal','ownerEnabled',true], ['settings','ready',false],
    ['settings','visible',false], ['settings','window',0]]) {
    const sample = state(); sample[group][key] = value;
    assert.throws(() => verifyState(sample, true));
  }
});
test('拒绝重复设置页面或主窗遗留表单', async () => {
  const settings = {url: () => 'https://augit.local/?surface=settings'};
  const main = {evaluate: async () => state(), locator: () => ({count: async () => 1})};
  for (const [pages,message] of [[[settings, settings],/恰有一个/], [[settings],/第二份/]]) {
    await assert.rejects(settingsSurface({contexts: () => [{pages: () => pages}]}, main),message);
  }
});
test('等待迟到的CDP页面，但不把缺失当通过', async () => {
  let reads = 0, waits = 0;
  const settings = {url: () => 'https://augit.local/?surface=settings',
    waitForFunction: async () => {}, locator: () => ({count: async () => 1})};
  const main = {waitForFunction: async () => {}, evaluate: async () => state(),
    locator: () => ({count: async () => 0})};
  const browser = {contexts: () => [{pages: () => ++reads === 1 ? [] : [settings]}]};
  assert.equal(await settingsSurface(browser, main, async () => {waits++;}), settings);
  assert.equal(waits, 1);
  await assert.rejects(settingsSurface({contexts: () => [{pages: () => []}]}, main, async () => {}));
});
