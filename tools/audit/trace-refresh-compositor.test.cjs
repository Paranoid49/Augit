const test = require('node:test');
const assert = require('node:assert/strict');
const {traceRefreshCompositor} = require('./trace-refresh-compositor.cjs');
test('刷新动作失败也结束追踪、关闭流并分离连接', async () => {
  const calls = []; let complete;
  const client = {
    once: (_, callback) => {complete = callback;},
    send: async (name, args) => {
      calls.push(name);
      if (name === 'Tracing.end') complete({stream:'failed-run-stream'});
      if (name === 'IO.close') assert.equal(args.handle,'failed-run-stream');
    },
    detach: async () => {calls.push('detach');},
  };
  const failure = new Error('实际刷新失败');
  await assert.rejects(traceRefreshCompositor({newBrowserCDPSession:async()=>client},
    {evaluate:async()=>{}}, '.', async()=>{throw failure;}), error=>error===failure);
  assert.deepEqual(calls,['Tracing.start','Tracing.end','IO.close','detach']);
});
test('追踪启动失败仍分离连接，不等待从未开始的追踪', async () => {
  let detached=false;
  await assert.rejects(traceRefreshCompositor({newBrowserCDPSession:async()=>({
    once:()=>{},send:async()=>{throw Error('启动失败');},detach:async()=>{detached=true;}
  })},{},'.',async()=>{}), /启动失败/);
  assert.equal(detached,true);
});
