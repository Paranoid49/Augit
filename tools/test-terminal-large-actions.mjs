// 终端大字号动作区布局守护。
//
// ui-size 40 时 --augit-icon-hit 为 91px，旧 header 仅 63px 高：
// More(1508..1599) 与 Hide(1540..1631) 重叠，且两按钮底部 712 超出 header 665。
// 本测试只读共享 JS/CSS，不启动应用。
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const paths = {
  js: path.join(root, 'web/src/mockup.js'),
  css: path.join(root, 'web/src/mockup.css'),
  mockJs: path.join(root, 'docs/ux-mockups/mockup.js'),
  mockCss: path.join(root, 'docs/ux-mockups/mockup.css'),
};
const [js, css, mockJs, mockCss] = await Promise.all([
  readFile(paths.js, 'utf8'), readFile(paths.css, 'utf8'),
  readFile(paths.mockJs, 'utf8'), readFile(paths.mockCss, 'utf8'),
]);

let passed = 0;
const check = (name, condition) => {
  assert.ok(condition, name);
  passed++;
};

check('measureTerminalHeaders 在大命中区启用 terminal-large-actions',
  js.includes('iconHit > 32') && js.includes("classList.toggle('terminal-large-actions', largeActions)"));
check('大字号 header 高度至少容纳命中区',
  js.includes('largeActions ? iconHit + 8 : 0'));
check('大字号会话标签使用独立高度变量',
  /\.terminal-tool\.terminal-large-actions \.terminal-header \.terminal-session[\s\S]*?var\(--terminal-session-height, 28px\)/.test(css));
check('大字号 More 按命中区让位、Hide 仍在 8px',
  /\.terminal-tool\.terminal-large-actions \.terminal-header \.terminal-more\s*\{[^}]*right:\s*calc\(8px \+ var\(--augit-icon-hit, 28px\) \+ 8px\)/.test(css)
  && /\.terminal-tool\.terminal-large-actions \.terminal-header \.terminal-hide\s*\{[^}]*right:\s*8px/.test(css));
check('运行时与视觉稿字节一致',
  createHash('sha256').update(js, 'utf8').digest('hex') === createHash('sha256').update(mockJs, 'utf8').digest('hex')
  && createHash('sha256').update(css, 'utf8').digest('hex') === createHash('sha256').update(mockCss, 'utf8').digest('hex'));

console.log(`PASS ${passed}/5 终端大字号动作区布局守护`);
