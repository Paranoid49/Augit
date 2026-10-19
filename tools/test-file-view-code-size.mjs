// File View 等宽字体/字号按 2026-10-15 用户裁决固定 JetBrains Mono 13：
// 设置页只读展示，不提供等宽字体或字号选择；不要为了对齐参考编辑器而重新引入输入框。
import fs from 'node:fs';

const source = fs.readFileSync('D:/github/Augit/web/src/mockup.js', 'utf8');
const mockupCopy = fs.readFileSync('D:/github/Augit/docs/ux-mockups/mockup.js', 'utf8');
const checks = [
  ['File View 固定显示 JetBrains Mono', source.includes('<span data-fixed-code-font>JetBrains Mono</span>')],
  ['File View 固定显示 Size 13', source.includes('<span data-fixed-code-size>13</span>')],
  ['File View 不提供等宽字体下拉', !source.includes("fontSelect('monospaceFontFamily'")],
  ['File View 不提供 codeFontSize 输入', !source.includes('id="code-font-size"') && !source.includes('attr("codeFontSize")')],
  ['等宽字号仍固定 13（应用层）', source.includes('root.setProperty("--augit-code-size"')],
  ['视觉稿与运行时 mockup.js 一致', source === mockupCopy],
];
for (const [name, ok] of checks) console.log((ok ? 'PASS ' : 'FAIL ') + name);
const failed = checks.filter(([, ok]) => !ok).length;
console.log(`RESULT ${checks.length - failed}/${checks.length} PASS`);
process.exitCode = failed ? 1 : 0;
