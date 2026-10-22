// 校验整窗像素配对用的隔离设置是否把面板几何纳入同态状态。
// 用法：node tools/audit/check-pixel-pair-settings.cjs <settings.json> <workspaceRoot>
// 仅读取，不修改设置；工作区根不一致时 Augit 会按设计忽略保存的面板尺寸。
const fs = require('node:fs');
const path = require('node:path');

function fail(message) {
  console.error('FAIL: ' + message);
  process.exit(1);
}

const [settingsPath, workspaceRoot] = process.argv.slice(2);
if (!settingsPath || !workspaceRoot) {
  fail('用法：node tools/audit/check-pixel-pair-settings.cjs <settings.json> <workspaceRoot>');
}

let settings;
try {
  settings = JSON.parse(fs.readFileSync(path.resolve(settingsPath), 'utf8').replace(/^\uFEFF/, ''));
} catch (error) {
  fail('无法读取设置：' + error.message);
}

const toolWindows = settings.toolWindows;
if (!toolWindows || typeof toolWindows !== 'object') fail('缺少 toolWindows 对象。');
const owner = typeof toolWindows.workspaceRoot === 'string' ? path.resolve(toolWindows.workspaceRoot) : null;
const expected = path.resolve(workspaceRoot);
if (!owner) fail('toolWindows.workspaceRoot 必须显式指向配对工作区，否则面板尺寸会被忽略。');
if (owner.toLowerCase() !== expected.toLowerCase()) {
  fail(`toolWindows.workspaceRoot=${owner} 与配对工作区 ${expected} 不一致。`);
}
for (const key of ['projectPanelWidth', 'bottomPanelHeight']) {
  const value = toolWindows[key];
  if (typeof value !== 'number' || !Number.isFinite(value) || value <= 0) {
    fail(`toolWindows.${key} 必须是正数；当前为 ${JSON.stringify(value)}。`);
  }
}
if (settings.theme !== 'Light' && settings.theme !== 'Dark' && settings.theme !== 'System') {
  fail('theme 必须是 Light、Dark 或 System。');
}
console.log(JSON.stringify({
  ok: true,
  workspaceRoot: expected,
  theme: settings.theme,
  projectPanelWidth: toolWindows.projectPanelWidth,
  bottomPanelHeight: toolWindows.bottomPanelHeight,
  openFiles: Array.isArray(settings.openFiles) ? settings.openFiles : [],
}, null, 2));
