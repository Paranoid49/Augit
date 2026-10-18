// 采集只隔离存储；正常入口不携带场景或恢复覆盖，外观版本必须显式声明。
const assert = require('node:assert/strict');
const path = require('node:path');
function captureSettings(theme, workspace, normalEntry = false) {
  assert.ok(['Light', 'Dark'].includes(theme), '采集主题必须明确');
  return { appearanceVersion: 1, theme, textFontFamily: 'Microsoft YaHei UI', textFontSize: 12,
    monospaceFontFamily: 'JetBrains Mono', fontSize: 13,
    ...(normalEntry ? { lastWorkspace: path.resolve(workspace), openFiles: ['notes.txt'], activeFile: 'notes.txt' } : {}) };
}
function captureSceneArguments(normalEntry, uniformBackground) {
  return normalEntry ? [] : ['--scene', uniformBackground ? 'text-viewer' : 'commit-changes', '--open', 'notes.txt', '--no-session-restore'];
}
function verifyCaptureAppearance(actual, theme) {
  assert.equal(actual.theme, theme);
  assert.equal(actual.uiSize, '12px');
  assert.match(actual.uiFamily, /Microsoft YaHei UI/);
  assert.equal(actual.codeSize, 13);
  assert.equal(actual.codeFamily, 'JetBrains Mono');
  assert.ok(Number.isFinite(actual.dpr) && actual.dpr > 0);
}
// 软件渲染只供隔离诊断，不能当作普通启动验收。
function captureBrowserArguments(port, softwareRendering = false, disableDirectComposition = false, disableNativeOcclusion = false) {
  assert.ok(Number.isInteger(port) && port > 0 && port <= 65535, '调试端口无效');
  assert.equal(typeof softwareRendering, 'boolean');
  assert.equal(typeof disableDirectComposition, 'boolean');
  assert.equal(typeof disableNativeOcclusion, 'boolean');
  assert.ok([softwareRendering, disableDirectComposition, disableNativeOcclusion].filter(Boolean).length <= 1, '合成诊断不能叠加');
  return '--remote-debugging-port=' + port + (softwareRendering ? ' --disable-gpu' : '')
    + (disableDirectComposition ? ' --disable-direct-composition' : '')
    + (disableNativeOcclusion ? ' --disable-features=CalculateNativeWinOcclusion' : '');
}
module.exports = { captureSettings, captureSceneArguments, verifyCaptureAppearance, captureBrowserArguments };
