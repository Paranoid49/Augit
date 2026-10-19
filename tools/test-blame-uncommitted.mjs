// Blame 未提交行回归守卫：工作区行（全零 hash / Not Committed Yet）不显示日期、作者和提交提示，
// 只保留行号，对齐参考 PyCharm Annotate 的空白归属槽。视觉稿与运行时共享文件必须字节一致。
import fs from 'node:fs';

const root = 'D:/github/Augit';
const web = fs.readFileSync(`${root}/web/src/mockup.js`, 'utf8');
const mock = fs.readFileSync(`${root}/docs/ux-mockups/mockup.js`, 'utf8');
const checks = [
  ['web/src 与 docs/ux-mockups mockup.js 字节一致', web === mock],
  ['定义 blameLineUncommitted', web.includes('function blameLineUncommitted(line)')],
  ['全零 hash 与 Not Committed Yet 均判未提交', web.includes('/^0+$/.test(hash)') && web.includes('Not Committed Yet')],
  ['liveBlameView 使用 uncommitted 空白行', web.includes('class="blame-row uncommitted"') && web.includes('blameLineUncommitted(line)')],
  ['未提交行不显示 tooltip/点击', web.includes("row.classList.contains('uncommitted')") && web.includes('row.classList.contains("uncommitted")')],
];
for (const [name, ok] of checks) console.log((ok ? 'PASS ' : 'FAIL ') + name);
const failed = checks.filter(([, ok]) => !ok).length;
console.log(`RESULT ${checks.length - failed}/${checks.length} PASS`);
process.exitCode = failed ? 1 : 0;
