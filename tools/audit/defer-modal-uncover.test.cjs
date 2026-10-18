const {test}=require('node:test'),assert=require('node:assert/strict'),vm=require('node:vm');
const {installDeferredUncover}=require('./defer-modal-uncover.cjs');
test('只延后撤盖且在渲染回调后的任务原样发送',()=>{
 const sent=[],frames=[],tasks=[],bridge={postMessage(x){sent.push(x);}},window={chrome:{webview:bridge}};
 const original=bridge.postMessage;
 vm.runInNewContext('('+installDeferredUncover+')()',{window,requestAnimationFrame:fn=>(frames.push(fn),frames.length),setTimeout:(fn,ms)=>{assert.equal(ms,0);tasks.push(fn);return tasks.length;},cancelAnimationFrame(){},clearTimeout(){}});
 const msg={id:1,method:'window/modal-uncover'};bridge.postMessage(msg);bridge.postMessage('invalid');
 assert.deepEqual(sent,['invalid']);frames.shift()();assert.equal(sent.length,1);tasks.shift()();assert.equal(sent[1],msg);
 window.__auditDeferredUncover.dispose();assert.equal(bridge.postMessage,original);
});
test('结束诊断时取消等待并交还在途消息',()=>{
 let sent=0,cancelled=0;const bridge={postMessage(){sent++;}},window={chrome:{webview:bridge}};
 vm.runInNewContext('('+installDeferredUncover+')()',{window,requestAnimationFrame:()=>17,setTimeout(){},cancelAnimationFrame:id=>{assert.equal(id,17);cancelled++;},clearTimeout(){}});
 bridge.postMessage({method:'window/modal-uncover'});window.__auditDeferredUncover.dispose();
 assert.equal(sent,1);assert.equal(cancelled,1);
});
