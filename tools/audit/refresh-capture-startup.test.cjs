// 防止配置迁移或审计覆盖让录像主题、正常入口声明与运行条件不一致。
const { test } = require('node:test'), assert = require('node:assert/strict');
const { captureSettings, captureSceneArguments, verifyCaptureAppearance } = require('./refresh-capture-startup.cjs');

test('软件渲染仅显式诊断启用，普通采集不改变GPU参数', () => {
  const { captureBrowserArguments } = require('./refresh-capture-startup.cjs');
  assert.equal(captureBrowserArguments(50001), '--remote-debugging-port=50001');
  assert.equal(captureBrowserArguments(50001, true), '--remote-debugging-port=50001 --disable-gpu');
  assert.equal(captureBrowserArguments(50001, false, true), '--remote-debugging-port=50001 --disable-direct-composition');
  assert.throws(() => captureBrowserArguments(50001, true, true));
  assert.equal(captureBrowserArguments(50001, false, false, true), '--remote-debugging-port=50001 --disable-features=CalculateNativeWinOcclusion');
  assert.throws(() => captureBrowserArguments(50001, true, false, true));
  assert.throws(() => captureBrowserArguments(50001, false, true, true));
  assert.throws(() => captureBrowserArguments(50001, false, false, 'true'));
  for (const port of [0, 65536, 1.5, '50001']) assert.throws(() => captureBrowserArguments(port));
  assert.throws(() => captureBrowserArguments(50001, 'false'));
});
test('深浅主题均携带已迁移版本，正常入口用会话恢复打开真实文件', () => {
  for (const theme of ['Light', 'Dark']) {
    const settings = captureSettings(theme, 'D:/fixture', true);
    assert.equal(settings.appearanceVersion, 1); assert.equal(settings.theme, theme);
    assert.equal(settings.lastWorkspace, require('node:path').resolve('D:/fixture')); assert.deepEqual(settings.openFiles, ['notes.txt']);
    assert.equal(settings.activeFile, 'notes.txt'); assert.equal(settings.fontSize, 13);
  }
});
test('正常入口没有场景和恢复覆盖，原有隔离场景仍可显式选用', () => {
  assert.deepEqual(captureSceneArguments(true, false), []);
  assert.deepEqual(captureSceneArguments(true, true), []);
  assert.deepEqual(captureSceneArguments(false, false), ['--scene', 'commit-changes', '--open', 'notes.txt', '--no-session-restore']);
  assert.equal(captureSceneArguments(false, true)[1], 'text-viewer');
});
test('实际主题或字体漂移必须拒绝采集', () => {
  const actual = { theme: 'Dark', uiSize: '12px', uiFamily: 'Microsoft YaHei UI', codeSize: 13, codeFamily: 'JetBrains Mono', dpr: 1.75 };
  verifyCaptureAppearance(actual, 'Dark');
  for (const change of [{theme:'Light'}, {uiSize:'14px'}, {codeFamily:'Consolas'}, {codeSize:14}, {dpr:0}]) {
    assert.throws(() => verifyCaptureAppearance({...actual,...change}, 'Dark'));
  }
});
