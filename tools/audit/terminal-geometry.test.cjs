const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { patch } = require('./patch-xterm-cell-width.cjs');
const root = path.resolve(__dirname, '../..');
test('终端步长匹配256组独立JBR度量', async () => {
  const { terminalCellWidth, terminalCellHeight } = await import('../../web/src/terminal-geometry.js');
  const evidence = JSON.parse(fs.readFileSync(path.join(__dirname, 'jetbrains-mono-integer-widths.json')));
  for (const { scale, rows } of evidence) for (const row of rows) {
    assert.ok(Math.abs(terminalCellWidth('"JetBrains Mono", monospace', row.size, scale, true) - row.spaceWidth) < .00001);
    assert.equal(terminalCellHeight('"JetBrains Mono", monospace', row.size, scale, true), row.height);
  }
});
test('未加载字体、其他字体及无效输入不启用参考格宽', async () => {
  const { terminalCellWidth, terminalCellHeight } = await import('../../web/src/terminal-geometry.js');
  for (const args of [['Consolas',13,1.75,true], ['JetBrains Mono',13,1.75,false], ['JetBrains Mono',NaN,1.75,true], ['JetBrains Mono',13,0,true]]) assert.equal(terminalCellWidth(...args), 0);
  assert.equal(terminalCellWidth('JetBrains Mono',13,1.75,true), 8);
  assert.equal(terminalCellHeight('JetBrains Mono',13,1.75,true), 18);
  assert.equal(terminalCellHeight('Consolas',13,1.75,true), 0);
  assert.equal(terminalCellHeight('JetBrains Mono',13,1.75,false), 0);
});
test('运行时补丁可复现且拒绝非锁定输入', () => {
  const source = fs.readFileSync(path.join(root, 'tools/xterm/xterm-6.0.0/lib/xterm.js'));
  assert.deepEqual(patch(source), fs.readFileSync(path.join(root, 'web/vendor/xterm/xterm.js')));
  assert.throws(() => patch(Buffer.concat([source, Buffer.from(' ')])), /哈希不符/);
});
