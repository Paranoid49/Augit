// 设置脏标记回归：可选文本未设置（null）与输入框空字符串（""）语义相同，
// 不能让 Terminal 页一打开就 Apply 可用/出现蓝点；真实修改仍必须标脏。
import fs from 'node:fs';

const source = fs.readFileSync('D:/github/Augit/web/src/mockup.js', 'utf8');
const keysStart = source.indexOf('const SETTINGS_PAGE_KEYS');
const keysEnd = source.indexOf('};', keysStart) + 2;
const fnStart = source.indexOf('function settingsDirtyPages');
let depth = 0;
let fnEnd = -1;
for (let index = source.indexOf('{', fnStart); index < source.length; index += 1) {
  if (source[index] === '{') depth += 1;
  else if (source[index] === '}') {
    depth -= 1;
    if (depth === 0) { fnEnd = index + 1; break; }
  }
}
const factory = new Function(`${source.slice(keysStart, keysEnd)}\n${source.slice(fnStart, fnEnd)}\nreturn { settingsDirtyPages };`);
const { settingsDirtyPages } = factory();
const dirty = (settings, draft) => [...settingsDirtyPages({ settings, settingsDraft: draft })];
const checks = [
  ['null 与空字符串不算脏', dirty({ terminalCustomCommand: null }, { terminalCustomCommand: '' }).length === 0],
  ['真实自定义命令算脏', dirty({ terminalCustomCommand: null }, { terminalCustomCommand: 'wsl.exe' }).join() === 'terminal'],
  ['字符串修改算脏', dirty({ theme: 'Light' }, { theme: 'Dark' }).join() === 'appearance'],
  ['数字 13 与字符串 "13" 不算脏', dirty({ fontSize: 13 }, { fontSize: '13' }).length === 0],
  ['数字真实修改算脏', dirty({ fontSize: 13 }, { fontSize: 14 }).join() === 'appearance'],
  ['草稿缺键不算脏', dirty({ terminalShell: 'WindowsPowerShell' }, {}).length === 0],
  ['无 settings 安全返回空', [...settingsDirtyPages({})].length === 0],
];
for (const [name, ok] of checks) console.log((ok ? 'PASS ' : 'FAIL ') + name);
const failed = checks.filter(([, ok]) => !ok).length;
console.log(`RESULT ${checks.length - failed}/${checks.length} PASS`);
process.exitCode = failed ? 1 : 0;
