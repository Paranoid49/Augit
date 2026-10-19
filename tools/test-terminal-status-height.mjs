// 终端状态栏高度守护。
//
// 2026-10-19 实测：参考终端状态栏上缘 1661、实际 1668（+7 物理 = +4 CSS）。
// 终端打开时把状态栏基准加 4 CSS，主窗/文本状态栏保持原值。
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

check('JS 同时写 status-height 与基准 status-height-base',
  js.includes('length("status-height", statusHeight)')
  && js.includes('length("status-height-base", statusHeight)'));
check('仅在终端打开时把状态栏增加 4 CSS',
  /\.augit-window:has\(\.terminal-tool\)\s*\{[^}]*--augit-status-height:\s*calc\(var\(--augit-status-height-base, 28px\) \+ 4px\)/.test(css));
check('基准缺失时回落到 28px',
  css.includes('var(--augit-status-height-base, 28px)'));
check('终端规则不直接改写 status-height 全局值',
  !/\.augit-window:has\(\.terminal-tool\)[^}]*--augit-status-height-base:/.test(css));
check('终端状态栏有 1 物理px 顶边',
  /\.augit-window:has\(\.terminal-tool\) \.statusbar\s*\{[^}]*border-top:\s*calc\(1px \/ var\(--augit-dpr, 1\)\) solid var\(--augit-terminal-panel-border, #f1f2f4\)/.test(css));
check('深色终端工具有 1 物理px 底边',
  /body\[data-theme="dark"\] \.terminal-tool\s*\{[^}]*border-bottom:\s*calc\(1px \/ var\(--augit-dpr, 1\)\) solid var\(--augit-panel\)/.test(css));
check('运行时与视觉稿字节一致',
  createHash('sha256').update(js, 'utf8').digest('hex') === createHash('sha256').update(mockJs, 'utf8').digest('hex')
  && createHash('sha256').update(css, 'utf8').digest('hex') === createHash('sha256').update(mockCss, 'utf8').digest('hex'));

console.log(`PASS ${passed}/7 终端状态栏高度守护`);
