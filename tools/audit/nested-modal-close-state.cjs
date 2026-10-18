// 嵌套模态状态断言：与采集脚本分离，便于用固定样本做负向测试。
const assert = require('node:assert/strict');

function validateNestedModalLifecycle(states) {
  const { parent, remote, restored, closed } = states;
  assert.ok(parent && remote && restored && closed, '缺少嵌套模态状态');
  assert.equal(parent.active, true, '父模态必须活动');
  assert.equal(parent.depth, 1, '父模态必须是第一层');
  assert.ok(parent.window > 0, '父模态必须有原生 HWND');
  assert.equal(remote.active, true, '子模态必须活动');
  assert.equal(remote.depth, 2, '子模态必须是第二层');
  assert.ok(remote.window > 0, '子模态必须有原生 HWND');
  assert.notEqual(remote.window, parent.window, '子模态必须使用独立原生 HWND');
  assert.equal(remote.ownerWindow, parent.window, '子模态原生 owner 必须指向父模态');
  assert.equal(remote.parentWindow, remote.window, '业务控制器必须重挂到子模态自身');
  assert.equal(restored.active, true, '返回后父模态必须活动');
  assert.equal(restored.depth, 1, '返回后必须回到一层');
  assert.equal(restored.window, parent.window, '返回时必须复用原父 HWND');
  assert.equal(restored.ownerWindow, parent.ownerWindow, '返回后原生 owner 必须回到主窗');
  assert.equal(restored.parentWindow, restored.window, '返回后业务控制器必须重挂到父模态自身');
  assert.equal(closed.active, false, '最终关闭后模态必须不活动');
  assert.equal(closed.ownerEnabled, true, '最终关闭后所有者必须恢复启用');
  assert.equal(closed.snapshotCount, 0, '最终关闭后不得残留背景快照');
  return {
    parentWindow: parent.window,
    childWindow: remote.window,
    restoredWindow: restored.window,
    finalSnapshots: closed.snapshotCount
  };
}

module.exports = { validateNestedModalLifecycle };
