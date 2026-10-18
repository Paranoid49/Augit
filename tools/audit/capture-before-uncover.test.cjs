const {test}=require('node:test'),assert=require('node:assert/strict'),vm=require('node:vm');
const {installCaptureBeforeUncover}=require('./capture-before-uncover.cjs');
function fixture() {
 const sent=[];let resolve,reject;
 const bridge={postMessage:message=>sent.push(message)},original=bridge.postMessage;
 const window={chrome:{webview:bridge},__auditCaptureRestoredSurface:()=>new Promise((yes,no)=>{resolve=yes;reject=no;})};
 vm.runInNewContext('('+installCaptureBeforeUncover+')()',{window,Date,Promise,Set,String});
 return {sent,bridge,window,original,resolve:value=>resolve(value),reject:error=>reject(error)};
}
const settle=()=>new Promise(resolve=>setImmediate(resolve));
test('恢复合成图返回前不撤盖，非目标消息原样发送',async()=>{
 const f=fixture(),message={id:1,method:'window/modal-uncover'};
 f.bridge.postMessage(message);f.bridge.postMessage('invalid');await settle();
 assert.deepEqual(f.sent,['invalid']);f.resolve({file:'restored-0.png'});await settle();
 assert.equal(f.sent[1],message);assert.equal(f.window.__auditCaptureBeforeUncover.records[0].result.file,'restored-0.png');
 f.window.__auditCaptureBeforeUncover.dispose();assert.equal(f.bridge.postMessage,f.original);
});
test('捕获失败仍撤盖且记录失败，不伪装通过',async()=>{
 const f=fixture();f.bridge.postMessage({method:'window/modal-uncover'});await settle();
 f.reject(new Error('capture failed'));await settle();
 assert.equal(f.sent.length,1);assert.equal(f.window.__auditCaptureBeforeUncover.records[0].error,'capture failed');
 f.window.__auditCaptureBeforeUncover.dispose();
});
test('在途清理交还消息，迟到捕获不得重复发送',async()=>{
 const f=fixture();f.bridge.postMessage({method:'window/modal-uncover'});await settle();
 f.window.__auditCaptureBeforeUncover.dispose();assert.equal(f.sent.length,1);
 f.resolve({});await settle();assert.equal(f.sent.length,1);assert.equal(f.bridge.postMessage,f.original);
});
