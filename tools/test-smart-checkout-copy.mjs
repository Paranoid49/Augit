// Smart Checkout 影响说明的换行守护。
//
// 背景：说明段落复用了提交区的 `.commit-meta`，该类为单行摘要设置了 `white-space: nowrap`。
// 正常入口实测（2026-10-19）句子在对话框右缘被截成 “... and resto”，没有展示 restore 影响。
// 本测试只读共享视觉稿/运行时文件，不启动应用、不执行 Git。
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
  readFile(paths.js, 'utf8'),
  readFile(paths.css, 'utf8'),
  readFile(paths.mockJs, 'utf8'),
  readFile(paths.mockCss, 'utf8'),
]);

let passed = 0;
const check = (name, condition) => {
  assert.ok(condition, name);
  passed++;
};

check('说明文案在两份 mockup.js 中完整存在',
  js.includes('Local changes to these files would be overwritten by checkout. Augit can stash them, check out, and restore them afterward.')
  && mockJs.includes('Local changes to these files would be overwritten by checkout. Augit can stash them, check out, and restore them afterward.'));
check('说明段落带 smart-checkout-impact 类，避免复用单行摘要类',
  js.includes('class="commit-meta smart-checkout-impact"') && mockJs.includes('class="commit-meta smart-checkout-impact"'));
check('CSS 给影响说明允许换行',
  /\.smart-checkout-dialog \.smart-checkout-impact\s*\{[^}]*white-space:\s*normal;[^}]*overflow-wrap:\s*anywhere;/s.test(css)
  && /\.smart-checkout-dialog \.smart-checkout-impact\s*\{[^}]*white-space:\s*normal;[^}]*overflow-wrap:\s*anywhere;/s.test(mockCss));
check('`.commit-meta` 的单行摘要规则未被全局改坏',
  /\.commit-meta\s*\{[^}]*white-space:\s*nowrap;/s.test(css)
  && /\.commit-meta\s*\{[^}]*white-space:\s*nowrap;/s.test(mockCss));

const digest = (text) => createHash('sha256').update(text, 'utf8').digest('hex');
check('运行时与视觉稿字节一致', digest(js) === digest(mockJs) && digest(css) === digest(mockCss));

console.log(`PASS ${passed}/5 Smart Checkout 影响说明换行守护`);
