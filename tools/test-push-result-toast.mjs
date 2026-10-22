// Push 结果提示回归：成功后必须给出可复验的结果反馈，失败按参考关闭对话框并显示 balloon；
// 成功优先显示 upstream（origin/main），缺失时用通用完成文案；提示接入 showToast。
import fs from 'node:fs';

const source = fs.readFileSync('D:/github/Augit/web/src/live-data.js', 'utf8');
const mockup = fs.readFileSync('D:/github/Augit/web/src/mockup.js', 'utf8');
const start = source.indexOf('function pushSuccessText');
if (start < 0) throw new Error('pushSuccessText 不存在');
let depth = 0;
let end = -1;
for (let index = source.indexOf('{', start); index < source.length; index += 1) {
  if (source[index] === '{') depth += 1;
  else if (source[index] === '}') {
    depth -= 1;
    if (depth === 0) { end = index + 1; break; }
  }
}
const fn = new Function(source.slice(start, end) + '; return pushSuccessText;')();
const checks = [
  ['origin/main 生成 Pushed to origin/main.', fn('origin/main') === 'Pushed to origin/main.'],
  ['upstream 首尾空白被裁剪', fn('  origin/main  ') === 'Pushed to origin/main.'],
  ['空 upstream 用通用完成文案', fn('') === 'Push completed.'],
  ['null 安全', fn(null) === 'Push completed.'],
  ['undefined 安全', fn(undefined) === 'Push completed.'],
  ['成功路径调用 showToast success', source.includes('showToast({ title: "Push", text: pushSuccessText(pushedUpstream), kind: "success" })')],
  ['成功路径使用 live.push.upstream', source.includes('const pushedUpstream = live && live.push && live.push.upstream;')],
  ['失败路径关闭对话框并显示 Push failed', source.includes('title: "Push failed"') && source.includes('window.__augitCommitResult = { pushed: false, reason: failure };')],
  ['失败 balloon 动作为 Show details', source.includes('action: { id: "push-details", label: "Show details" }')],
  ['Show details 重新打开带错误的 Push 对话框', source.includes('void openPushDialog({ failure });') && source.includes('const failure = options && typeof options.failure === "string"')],
  ['toast 无 href 动作渲染为按钮', mockup.includes('button type="button" class="toast-action" data-toast-action=')],
  ['toast 动作按当前 action.id 核对', source.includes('toast.action.id !== action.dataset.toastAction')],
];
for (const [name, ok] of checks) console.log((ok ? 'PASS ' : 'FAIL ') + name);
const failed = checks.filter(([, ok]) => !ok).length;
console.log(`RESULT ${checks.length - failed}/${checks.length} PASS`);
process.exitCode = failed ? 1 : 0;
