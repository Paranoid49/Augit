const test=require('node:test'),assert=require('node:assert/strict');
const {observeStartupClose}=require('./settings-startup-close.cjs');
const absent=()=>{throw Object.assign(Error('已退出'),{code:'ESRCH'});};
const options={before:{active:true,ready:false},pid:123,close:async()=>{},probe:absent,timeout:30,polling:1};
test('已经就绪或不存在的窗口不得进入初始化关闭取证',async()=>{
 for(const before of [{active:true,ready:true},{active:false,ready:false}]){
  let closed=false;await assert.rejects(observeStartupClose({...options,before,close:()=>{closed=true;}}));assert.equal(closed,false);
 }
});
test('只有ESRCH确认退出，记录耗时但不扩大竞态与呈现结论',async()=>{
 let calls=0,closed=false;const times=[100,175];
 const result=await observeStartupClose({...options,close:()=>{closed=true;},now:()=>times.shift(),probe:async id=>{assert.equal(id,123);assert.equal(closed,true);if(++calls===3)absent();}});
 assert.equal(calls,3);assert.equal(result.observedExitMilliseconds,75);assert.equal(result.passed,true);
 assert.equal(result.scriptRegistrationRaceVerified,false);assert.equal(result.fullPresentationVerified,false);
});
test('进程仍存在时超时失败',async()=>{
 await assert.rejects(observeStartupClose({...options,probe:async()=>{},timeout:10}),/等待超时/);
});
test('权限异常不得当作进程退出',async()=>{
 const error=Object.assign(Error('访问被拒绝'),{code:'EPERM'});
 await assert.rejects(observeStartupClose({...options,probe:()=>{throw error;}}),error);
});
test('关闭请求失败不得继续出具成功证据',async()=>{
 let probed=false;await assert.rejects(observeStartupClose({...options,close:()=>{throw Error('关闭请求失败');},probe:()=>{probed=true;}}),/关闭请求失败/);assert.equal(probed,false);
});
test('无效PID不得触发关闭',async()=>{
 for(const pid of [0,-1,NaN,1.5])await assert.rejects(observeStartupClose({...options,pid}),/本次实例PID/);
});
