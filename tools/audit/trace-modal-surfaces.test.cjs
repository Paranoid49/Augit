const {test}=require('node:test'),assert=require('node:assert/strict'),vm=require('node:vm');
const {installModalSurfaceTrace}=require('./trace-modal-surfaces.cjs');
function setup(throwing=false){
 const listeners=new Set(),sent=[];let callback,disconnected=false,hidden=0,time=0;
 const original=function(message){assert.equal(this,bridge);if(throwing)throw Error('发送失败');sent.push(message);return 9;};
 const bridge={postMessage:original,addEventListener:(name,fn)=>listeners.add(fn),removeEventListener:(name,fn)=>listeners.delete(fn)};
 const window={chrome:{webview:bridge}},document={getElementById:()=>({}),querySelectorAll:s=>Array(s==='.dialog'?1:hidden)};
 class MutationObserver{constructor(fn){callback=fn;}observe(root,options){assert.equal(options.attributeOldValue,true);}disconnect(){disconnected=true;}}
 vm.runInNewContext('('+installModalSurfaceTrace+')()',{window,document,MutationObserver,Date:{now:()=>1700000000000+time},performance:{now:()=>++time}});
 return {window,bridge,original,sent,listeners,get disconnected(){return disconnected;},receive:data=>{for(const fn of listeners)fn({data});},mutate(value){hidden=value?1:0;callback([{target:{matches:()=>true,hasAttribute:()=>value},attributeName:'data-native-background-capture',oldValue:value?null:'true'}]);}};
}
test('只保留白名单布尔状态并记录隐藏恢复，转发内容不变',()=>{
 const s=setup(),message={id:1,method:'window/modal-cover',params:{secret:'不能采集'}};
 assert.equal(s.bridge.postMessage(message),9);assert.equal(s.sent[0],message);
 s.receive({id:1,result:{captureCoverReady:true,captureCoverTopmostChild:false,liveControllerVisible:true,secret:'不能采集'}});
 s.mutate(true);s.mutate(false);
 const r=s.window.__auditModalSurfaceTrace.records;assert.equal(r.length,5);
 assert.equal(r[2].native.captureCoverTopmostChild,false);assert.equal(r[3].hiddenDialogs,1);assert.equal(r[4].hiddenDialogs,0);
 assert.ok(!JSON.stringify(r).includes('secret'));assert.ok(!JSON.stringify(r).includes('不能采集'));
 s.window.__auditModalSurfaceTrace.dispose();assert.equal(s.bridge.postMessage,s.original);assert.equal(s.listeners.size,0);assert.equal(s.disconnected,true);
});
test('同步失败清理在途请求且记录数量有界',()=>{
 const s=setup(true);assert.throws(()=>s.bridge.postMessage({id:1,method:'window/modal-refresh'}),/发送失败/);
 s.receive({id:1,result:{active:true}});assert.equal(s.window.__auditModalSurfaceTrace.records.length,3);
 for(let i=0;i<600;i++)s.mutate(i%2===0);assert.equal(s.window.__auditModalSurfaceTrace.records.length,512);
 s.window.__auditModalSurfaceTrace.dispose();
});
test('外层延迟钩子先释放后能够完整恢复原始桥接',()=>{
 const {installDeferredUncover}=require('./defer-modal-uncover.cjs');
 const s=setup(),frames=[],timers=[];
 vm.runInNewContext('('+installDeferredUncover+')()',{window:s.window,requestAnimationFrame:fn=>frames.push(fn),cancelAnimationFrame:()=>{},setTimeout:fn=>timers.push(fn),clearTimeout:()=>{}});
 const request={id:7,method:'window/modal-uncover'};s.bridge.postMessage(request);assert.equal(s.sent.length,0);
 s.window.__auditDeferredUncover.dispose();assert.equal(s.sent[0],request);
 s.window.__auditModalSurfaceTrace.dispose();assert.equal(s.bridge.postMessage,s.original);assert.equal(s.listeners.size,0);
 const source=require('node:fs').readFileSync(require('node:path').join(__dirname,'capture-refresh-screen.cjs'),'utf8');
 const cleanup=source.slice(source.indexOf('  deadline.dispose();'));
 assert.ok(cleanup.indexOf('__auditDeferredUncover?.dispose()')<cleanup.indexOf('__auditModalSurfaceTrace?.dispose()'));
 assert.ok(cleanup.indexOf('__auditModalSurfaceTrace?.dispose()')<cleanup.indexOf('__auditModalRefreshTrace?.dispose()'));
});
