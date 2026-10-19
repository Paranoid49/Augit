// 大文件 Warning 横幅布局回归：横幅是 .document-view 的 grid item，
// 不给 min-height 会被压成一行文字（约 13px），图标/隐藏/不再显示按钮不可见。
import fs from 'node:fs';

const css = fs.readFileSync('D:/github/Augit/web/src/mockup.css', 'utf8');
const copy = fs.readFileSync('D:/github/Augit/docs/ux-mockups/mockup.css', 'utf8');
const start = css.indexOf('.editor-notification {');
const end = css.indexOf('}', start);
const rule = css.slice(start, end);
const checks = [
  ['横幅规则存在', start >= 0],
  ['横幅有 min-height: 26px', /min-height:\s*26px/.test(rule)],
  ['横幅使用 border-box', /box-sizing:\s*border-box/.test(rule)],
  ['仍保留 6px 10px 内距', /padding:\s*6px 10px/.test(rule)],
  ['横幅有独立 grid 行', css.includes('.document-view:has(> .editor-notification) { grid-template-rows: var(--augit-document-toolbar-height) auto minmax(0, 1fr); }')],
  ['横幅与查找条组合有行', css.includes('.document-view:has(> .editor-notification):has(> .current-find)')],
  ['横幅与 target 组合有行', css.includes('.document-view.has-target:has(> .editor-notification)')],
  ['运行时与视觉稿 CSS 一致', css === copy],
];
for (const [name, ok] of checks) console.log((ok ? 'PASS ' : 'FAIL ') + name);
const failed = checks.filter(([, ok]) => !ok).length;
console.log(`RESULT ${checks.length - failed}/${checks.length} PASS`);
process.exitCode = failed ? 1 : 0;
