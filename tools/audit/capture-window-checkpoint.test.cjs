const { test } = require('node:test');
const assert = require('node:assert/strict');
const { waitForCheckpoint } = require('./capture-window-checkpoint.cjs');
test('信号出现后只继续一次并清理文件', async () => {
  let reads=0, waits=0, removed=0;
  await waitForCheckpoint('signal', { attempts:3, access:async()=>{if(++reads===1)throw Object.assign(new Error(),{code:'ENOENT'});}, pause:async()=>{waits++;}, remove:async()=>{removed++;} });
  assert.deepEqual([reads,waits,removed],[2,1,1]);
});
test('超时仍清理且不自动批准继续', async () => {
  let reads=0, removed=0;
  await assert.rejects(waitForCheckpoint('signal',{attempts:2,access:async()=>{reads++;throw Object.assign(new Error(),{code:'ENOENT'});},pause:async()=>{},remove:async()=>{removed++;}}),/超时/);
  assert.deepEqual([reads,removed],[2,1]);
});
test('访问错误立即停止而非误判为等待', async () => {
  let removed=0;
  await assert.rejects(waitForCheckpoint('signal',{access:async()=>{throw Object.assign(new Error('拒绝访问'),{code:'EACCES'});},remove:async()=>{removed++;}}),/拒绝访问/);
  assert.equal(removed,1);
});
