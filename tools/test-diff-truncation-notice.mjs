// Diff 2000 行截断提示守护。
//
// 宿主 ShellBridge.cs 对超大补丁只回传前 2000 行并置 truncated=true。
// 旧 UI 不使用该标志，文件末尾有改动时工具栏仍显示 "0 differences"；
// 2026-10-19 用 8.7MB/92001 行文件在正常入口复现。
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

check('JS 只在 diff.truncated===true 时渲染截断说明',
  js.includes('diff.truncated !== true') && js.includes('liveDiffTruncationNotice'));
check('说明明确写出 2000 行边界与剩余未显示',
  js.includes('Showing the first 2000 rows. Changes in the rest of the file are not shown in this comparison.'));
check('截断时工具栏不再显示前 2000 行的差异计数',
  js.includes('diff.truncated === true ? \'<span class="diff-truncated-label">Truncated</span>\''));
check('正文截断说明有独立样式',
  /\.diff-truncated-notice\s*\{[^}]*margin:/.test(css));
check('运行时与视觉稿字节一致',
  createHash('sha256').update(js, 'utf8').digest('hex') === createHash('sha256').update(mockJs, 'utf8').digest('hex')
  && createHash('sha256').update(css, 'utf8').digest('hex') === createHash('sha256').update(mockCss, 'utf8').digest('hex'));

console.log(`PASS ${passed}/5 Diff 截断提示守护`);
