// Stash 两个复选同一行的回归守卫：Keep index 与 Include untracked files 必须同处一行，
// 否则默认高度下 Include untracked 会被 dialog-body 折叠/截断。视觉稿与运行时共享文件必须字节一致。
import fs from 'node:fs';

const root = 'D:/github/Augit';
const web = fs.readFileSync(`${root}/web/src/mockup.js`, 'utf8');
const mock = fs.readFileSync(`${root}/docs/ux-mockups/mockup.js`, 'utf8');
const css = fs.readFileSync(`${root}/web/src/mockup.css`, 'utf8');
const mockCss = fs.readFileSync(`${root}/docs/ux-mockups/mockup.css`, 'utf8');
const checks = [
  ['mockup.js 两份字节一致', web === mock],
  ['mockup.css 两份字节一致', css === mockCss],
  ['live/static Stash 都有 stash-check-row', (web.match(/class="stash-check-row"/g) || []).length >= 2],
  ['stash-check-row 包裹两个复选', web.includes('<div class="stash-check-row"><label class="check-line"') && web.includes('Include untracked files</label></div>')],
  ['CSS 让 check-row 跨满两列', css.includes('.stash-dialog .stash-check-row { grid-column: 1 / -1;')],
];
for (const [name, ok] of checks) console.log((ok ? 'PASS ' : 'FAIL ') + name);
const failed = checks.filter(([, ok]) => !ok).length;
console.log(`RESULT ${checks.length - failed}/${checks.length} PASS`);
process.exitCode = failed ? 1 : 0;
