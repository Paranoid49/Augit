// 验证审计钩子透明转发、数据最小化及关闭清理。
const {test}=require('node:test'),assert=require('node:assert/strict'),vm=require('node:vm');
const {installModalRefreshTrace}=require('./trace-modal-refresh.cjs');
function setup(throwing=false) {
 const sent=[],listeners=new Set();let time=0;
 const original=function(message){assert.equal(this,bridge);if(throwing)throw Error('发送失败');sent.push(message);return 7;};
 const bridge={postMessage:original,addEventListener:(name,fn)=>listeners.add(fn),removeEventListener:(name,fn)=>listeners.delete(fn)};
 const window={chrome:{webview:bridge}};
 vm.runInNewContext(`(${installModalRefreshTrace})()`,{window,performance:{now:()=>++time},Date:{now:()=>1700000000000+time}});
 return {window,bridge,original,sent,listeners,receive:data=>{for(const fn of listeners)fn({data});}};
}
test('仅记录刷新方法时间，不记录参数或结果，原样转发',()=>{
 const s=setup(),request=JSON.stringify({id:1,method:'window/modal-cover',params:{secret:'不可记录'}});
 assert.equal(s.bridge.postMessage(request),7);
 s.bridge.postMessage('{');s.bridge.postMessage(JSON.stringify({id:2,method:'git/status'}));
 s.receive('{');s.receive({id:2,result:{secret:'不可记录'}});s.receive(JSON.stringify({id:1,result:{secret:'不可记录'}}));
 const records=s.window.__auditModalRefreshTrace.records;assert.equal(records.length,1);
 assert.equal(records[0].ended-records[0].started,1);assert.equal(records[0].failed,false);
 assert.equal(records[0].startedUtcMilliseconds,1700000000001);
 assert.equal(records[0].endedUtcMilliseconds,1700000000002);
 assert.ok(!JSON.stringify(records).includes('secret'));assert.equal(s.sent[0],request);
 s.window.__auditModalRefreshTrace.dispose();assert.equal(s.bridge.postMessage,s.original);assert.equal(s.listeners.size,0);
 assert.equal(s.window.__auditModalRefreshTrace,undefined);
});
test('保留同步发送失败与宿主错误并清除在途记录',()=>{
 const s=setup(true);assert.throws(()=>s.bridge.postMessage({id:3,method:'window/modal-refresh'}),/发送失败/);
 const entry=s.window.__auditModalRefreshTrace.records[0];assert.equal(entry.failed,true);assert.equal(entry.ended,2);
 assert.equal(entry.endedUtcMilliseconds,1700000000002);
 s.receive({id:3});assert.equal(entry.ended,2);s.window.__auditModalRefreshTrace.dispose();
 const other=setup();other.bridge.postMessage({id:4,method:'window/modal-uncover'});other.receive({id:4,error:'错误'});
 assert.equal(other.window.__auditModalRefreshTrace.records[0].failed,true);other.window.__auditModalRefreshTrace.dispose();
});
