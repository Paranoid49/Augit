import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

// web/src 没有 package.json type=module；把真实源码作为 data URL 模块加载，
// 避免为测试改运行时资产布局。
const source = await readFile(new URL('../web/src/terminal-copy.js', import.meta.url), 'utf8');
const moduleUrl = 'data:text/javascript;base64,' + Buffer.from(source).toString('base64');
const { isTerminalCopyShortcut } = await import(moduleUrl);

const base = { type: 'keydown', ctrlKey: true, shiftKey: true, altKey: false, metaKey: false, code: 'KeyC', key: 'C' };
const checks = [
  ['Ctrl+Shift+C', { ...base }, true],
  ['Ctrl+Shift+c', { ...base, key: 'c' }, true],
  ['Ctrl+Insert', { ...base, shiftKey: false, code: 'Insert', key: 'Insert' }, true],
  ['Ctrl+C 仍是 SIGINT', { ...base, shiftKey: false }, false],
  ['Ctrl+Shift+Insert 不当作复制', { ...base, code: 'Insert', key: 'Insert' }, false],
  ['Alt 组合不拦截', { ...base, altKey: true }, false],
  ['Meta 组合不拦截', { ...base, metaKey: true }, false],
  ['Shift+Insert 无 Ctrl', { ...base, ctrlKey: false, code: 'Insert', key: 'Insert' }, false],
  ['Ctrl+Shift+X', { ...base, code: 'KeyX', key: 'X' }, false],
  ['keyup 不触发', { ...base, type: 'keyup' }, false],
];

for (const [name, event, expected] of checks) {
  assert.equal(isTerminalCopyShortcut(event), expected, name);
}
console.log(`PASS: terminal copy shortcut ${checks.length}/${checks.length}`);
