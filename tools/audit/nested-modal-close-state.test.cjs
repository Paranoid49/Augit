// 嵌套模态生命周期断言的负向测试：状态错误必须在采集脚本写证据前失败。
const test = require('node:test');
const assert = require('node:assert/strict');
const { validateNestedModalLifecycle } = require('./nested-modal-close-state.cjs');

function fixture() {
  return {
    parent: { active: true, depth: 1, window: 100, ownerWindow: 10, ownerEnabled: false, snapshotCount: 1 },
    remote: { active: true, depth: 2, window: 200, ownerWindow: 100, parentWindow: 200, ownerEnabled: false, snapshotCount: 2 },
    restored: { active: true, depth: 1, window: 100, ownerWindow: 10, parentWindow: 100, ownerEnabled: false, snapshotCount: 1 },
    closed: { active: false, depth: 0, window: 0, ownerWindow: 0, parentWindow: 10, ownerEnabled: true, snapshotCount: 0 }
  };
}

test('合法嵌套生命周期返回父/子/返回身份', () => {
  const summary = validateNestedModalLifecycle(fixture());
  assert.deepEqual(summary, { parentWindow: 100, childWindow: 200, restoredWindow: 100, finalSnapshots: 0 });
});

test('子窗复用父 HWND 必须失败', () => {
  const states = fixture();
  states.remote.window = 100;
  assert.throws(() => validateNestedModalLifecycle(states), /独立原生 HWND/);
});

test('子窗原生 owner 不指向父模态必须失败', () => {
  const states = fixture();
  states.remote.ownerWindow = 10;
  assert.throws(() => validateNestedModalLifecycle(states), /原生 owner/);
});

test('返回时重建父 HWND 必须失败', () => {
  const states = fixture();
  states.restored.window = 300;
  assert.throws(() => validateNestedModalLifecycle(states), /复用原父 HWND/);
});

test('最终关闭残留快照必须失败', () => {
  const states = fixture();
  states.closed.snapshotCount = 1;
  assert.throws(() => validateNestedModalLifecycle(states), /背景快照/);
});
