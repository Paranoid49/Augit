'use strict';

// 只使用 Node 标准库验证初始化握手；真实 Shell 与 WebView2 另由 WinExe 集成测试覆盖。
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');
const source = readFileSync(join(__dirname, 'xterm', 'terminal', 'terminal.js'), 'utf8');

function createHost() {
  const messages = [];
  const frames = new Map();
  let receive, observer, terminal, fits = 0, frameId = 0;
  const element = { style: {} };
  class Terminal {
    constructor(options) { this.options = options; this.cols = 80; this.rows = 24; terminal = this; }
    loadAddon() {}
    open() {}
    onData(handler) { this.input = handler; }
    onResize(handler) { this.resize = handler; }
    write(text) { this.output = (this.output || '') + text; }
    focus() {}
  }
  class FitAddon {
    fit() { fits++; terminal.cols = 100; terminal.rows = 12; terminal.resize({ cols: 100, rows: 12 }); }
  }
  runInNewContext(source, {
    Terminal, FitAddon: { FitAddon },
    document: { documentElement: { style: { setProperty() {} } }, body: { style: {} }, getElementById: () => element },
    window: { chrome: { webview: { postMessage: message => messages.push(message), addEventListener: (_, handler) => { receive = handler; } } } },
    ResizeObserver: class { constructor(callback) { observer = callback; } observe() { observer(); } },
    requestAnimationFrame: callback => { frames.set(++frameId, callback); return frameId; },
    cancelAnimationFrame: id => frames.delete(id)
  });
  return {
    messages, terminal,
    send: data => receive({ data }),
    resize: () => observer(),
    flush() { const pending = [...frames.values()]; frames.clear(); for (const frame of pending) frame(); },
    get fits() { return fits; },
    get pendingFrames() { return frames.size; }
  };
}

test('布局前保留 80×24 且观察器和外观消息不能提前调整行列', () => {
  const host = createHost();
  const ready = host.messages.find(message => message.type === 'ready');
  assert.equal(ready.columns, 80);
  assert.equal(ready.rows, 24);
  assert.equal(host.terminal.options.lineHeight, 1.7);
  host.send({ type: 'configure', fontFamily: 'Consolas', fontSize: 13, theme: {} });
  host.flush();
  assert.equal(host.fits, 0);
});

test('配置字号变化时保持 1.7 倍等宽行高契约', () => {
  const host = createHost();
  host.send({ type: 'configure', fontFamily: 'Consolas', fontSize: 40, lineHeight: 1.7, theme: {} });
  assert.equal(host.terminal.options.fontSize, 40);
  assert.equal(host.terminal.options.lineHeight, 1.7);
});

test('首次实际布局才适配尺寸，连续变化合并到一帧', () => {
  const host = createHost();
  host.send({ type: 'layout', visible: true });
  for (let index = 0; index < 200; index++) host.resize();
  assert.equal(host.pendingFrames, 1);
  host.flush();
  assert.equal(host.fits, 1);
  assert.equal(host.messages.at(-1).type, 'resize');
  assert.equal(host.messages.at(-1).columns, 100);
});

test('无可见尺寸时保持旧行列，恢复后再适配', () => {
  const host = createHost();
  host.send({ type: 'layout', visible: true });
  host.flush();
  host.send({ type: 'layout', visible: false });
  host.resize();
  host.flush();
  assert.equal(host.fits, 1);
  assert.equal(host.terminal.cols, 100);
  host.send({ type: 'layout', visible: true });
  host.flush();
  assert.equal(host.fits, 2);
});

test('布局前输出照常保留且输入消息不被尺寸握手截断', () => {
  const host = createHost();
  host.send({ type: 'output', data: 'C:\\workspace>' });
  assert.equal(host.terminal.output, 'C:\\workspace>');
  host.terminal.input('echo ok\r');
  assert.equal(host.messages.at(-1).data, 'echo ok\r');
  host.send({ type: 'layout', visible: 'true' });
  host.flush();
  assert.equal(host.fits, 0);
});
