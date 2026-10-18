const {test}=require('node:test'),assert=require('node:assert/strict');
const {createOpeningTimeline}=require('./settings-opening-timeline.cjs');
test('点击返回和宿主就绪分别保留且不混用时钟',()=>{
 let now=100;const trace=createOpeningTimeline(()=>now);
 trace.mark('click-start');now=2300;trace.mark('click-returned');
 now=2350;trace.mark('host-ready',{presentedMilliseconds:450});
 assert.deepEqual(trace.snapshot().map(e=>[e.name,e.milliseconds]),[['click-start',0],['click-returned',2200],['host-ready',2250]]);
 assert.equal(trace.snapshot()[2].presentedMilliseconds,450);
});
test('快照不能改写证据且单调时钟倒退必须失败',()=>{
 let now=10;const trace=createOpeningTimeline(()=>now);trace.mark('start');
 trace.snapshot()[0].name='changed';assert.equal(trace.snapshot()[0].name,'start');
 now=9;assert.throws(()=>trace.mark('invalid'),/倒退/);
});
