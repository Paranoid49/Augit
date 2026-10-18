const {test}=require('node:test');
const assert=require('node:assert/strict');
const {exerciseCover,exerciseUnchangedCover}=require('./refresh-cover-isolation.cjs');
function harness(fail=false) {
 const events=[];
 return {events,api:{invoke:async(method,args)=>{events.push(method);if(fail&&method==='window/modal-refresh')throw Error('捕获失败');return {captureCoverReady:true};},setHidden:async value=>events.push(value?'hide':'restore'),frames:async()=>events.push('frames'),pause:async ms=>{assert.equal(ms,200);events.push('hold');}}};
}
test('只隐藏分支不触发背景捕获且恢复后撤盖',async()=>{
 const h=harness();await exerciseCover(h.api,false);
 assert.deepEqual(h.events,['window/modal-cover','hide','frames','hold','restore','frames','window/modal-uncover']);
});
test('捕获分支保持相同隐藏等待及恢复次序',async()=>{
 const h=harness();await exerciseCover(h.api,true);
 assert.deepEqual(h.events,['window/modal-cover','hide','frames','window/modal-refresh','hold','restore','frames','window/modal-uncover']);
});
test('捕获失败仍恢复表单和撤盖',async()=>{
 const h=harness(true);await assert.rejects(exerciseCover(h.api,true),/捕获失败/);
 assert.deepEqual(h.events.slice(-3),['restore','frames','window/modal-uncover']);
});
test('新背景只在遮盖就绪后修改，随后捕获',async()=>{
 const h=harness();h.api.mutate=async()=>h.events.push('mutate');
 await exerciseCover(h.api,true);
 assert.deepEqual(h.events,['window/modal-cover','mutate','hide','frames','window/modal-refresh','hold','restore','frames','window/modal-uncover']);
});
test('无额外等待对照仍保留捕获前后帧与异常恢复',async()=>{
 const h=harness();h.api.holdMilliseconds=0;await exerciseCover(h.api,true);
 assert.deepEqual(h.events,['window/modal-cover','hide','frames','window/modal-refresh','restore','frames','window/modal-uncover']);
});

for(const createOnly of [false,true])test('表单不变时隔离遮盖'+(createOnly?'创建':'显示'),async()=>{
 const events=[];
 await exerciseUnchangedCover({invoke:async method=>{events.push(method);const shown=method==='window/modal-cover'&&!createOnly;return{captureCoverReady:shown,captureCoverWindowVisible:shown,captureCoverBytes:10,backgroundImageRevision:0};},pause:async ms=>{assert.equal(ms,200);events.push('hold');}},createOnly);
 assert.deepEqual(events,['window/modal-state','window/modal-cover','hold','window/modal-uncover','window/modal-state']);
});

test('创建隔离误显示遮盖时失败且执行撤盖',async()=>{
 const events=[];
 await assert.rejects(exerciseUnchangedCover({invoke:async method=>{events.push(method);return{captureCoverReady:true,captureCoverWindowVisible:true};},pause:async()=>{}},true));
 assert.equal(events.at(-1),'window/modal-uncover');
});
