// 执行产品状态机；扣住旧请求，证明关闭不依赖截图先结束。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('web/src/live-data.js','utf8');
const start=source.indexOf('function bindNativeModalHost()');
const declaration=source.slice(start,source.indexOf(String.fromCharCode(10)+'/**',start));
const tick=async()=>{for(let i=0;i<30;i++)await Promise.resolve();};
function harness(){
 const observers=[],calls=[],holds=new Map();let dialogs=[];
 const window={__augitNativeModalAvailable:true,__augitErrors:[],addEventListener(){}};
 const document={querySelectorAll:()=>dialogs,getElementById:()=>({}),documentElement:{},body:{}};
 const context={window,document,hasHost:()=>true,queueMicrotask,requestAnimationFrame:fn=>queueMicrotask(fn),waitForRestoredModalFrame:()=>Promise.resolve(),subscribe(){},snapshotDepthForMutation:()=>0,
  ResizeObserver:class{observe(){}unobserve(){}disconnect(){}},MutationObserver:class{constructor(fn){observers.push(fn);}observe(){}disconnect(){}},
  invoke:async method=>{calls.push(method);if(holds.has(method))return holds.get(method).promise;return{active:method!=='window/modal-close',captureCoverReady:true};}};
 vm.createContext(context);vm.runInContext(declaration+';bindNativeModalHost();',context);
 const createDialog=()=>({isConnected:true,dataset:{},closest:()=>null,getBoundingClientRect:()=>({x:0,y:0,width:100,height:100}),getAttribute:()=>'',querySelector:()=>null,contains:()=>false});
 return{window,calls,get top(){return dialogs.at(-1);},async open(){dialogs=[createDialog()];observers[0]([]);await tick();},async push(){dialogs.push(createDialog());observers[0]([]);await tick();},async pop(){dialogs.pop().isConnected=false;observers[0]([]);await tick();},async close(){dialogs.forEach(d=>d.isConnected=false);dialogs=[];observers[0]([]);await tick();},async dirty(){observers[0]([{}]);await tick();},hold(method){let resolve;const promise=new Promise(r=>resolve=r);holds.set(method,{promise});return async()=>{holds.delete(method);resolve({active:true,captureCoverReady:true});await tick();};}};
}
for(const phase of ['window/modal-cover','window/modal-refresh'])test('关闭不等待'+phase+'且旧返回不影响新窗',async()=>{
 const h=harness();await h.open();const release=h.hold(phase);await h.dirty();assert.ok(h.calls.includes(phase));
 await h.dirty(); // 保存排版再次触发变化，留下重试标记后仍须立即处理关闭。
 await h.close();assert.ok(h.calls.includes('window/modal-close'));assert.equal(h.window.__augitNativeModalActive,false);
 await h.open();assert.equal(h.window.__augitNativeModalActive,true);const count=h.calls.length;
 await release();assert.equal(h.calls.length,count);assert.equal(h.window.__augitNativeModalActive,true);assert.equal(h.window.__augitNativeBackgroundBusy,false);
});
test('首次呈现尚未返回时关闭，旧结果不能恢复已关窗口状态',async()=>{
 const h=harness(),release=h.hold('window/modal-present');await h.open();await h.close();
 assert.ok(h.calls.includes('window/modal-close'));await release();assert.equal(h.window.__augitNativeModalActive,false);
});
test('重复关闭只发送一次，关闭应答后才允许重新呈现',async()=>{
 const h=harness();await h.open();const release=h.hold('window/modal-close');await h.close();await h.close();await h.open();
 assert.equal(h.calls.filter(m=>m==='window/modal-close').length,1);assert.equal(h.calls.filter(m=>m==='window/modal-present').length,1);
 await release();assert.equal(h.calls.filter(m=>m==='window/modal-present').length,2);
});

test('关闭应答未到时新表单保持隐藏，不能先画进旧原生窗口',async()=>{
 const h=harness();await h.open();const release=h.hold('window/modal-close');await h.close();await h.open();
 assert.equal(h.top.dataset.nativeCapture,'true');
 assert.equal(h.calls.filter(m=>m==='window/modal-present').length,1);
 await release();assert.equal(h.top.dataset.nativeCapture,undefined);
 assert.equal(h.calls.filter(m=>m==='window/modal-present').length,2);
});

for(const phase of ['window/modal-cover','window/modal-refresh'])test('等待'+phase+'时新嵌套表单不得提前出现在父窗',async()=>{
 const h=harness();await h.open();const release=h.hold(phase);await h.dirty();
 await h.push();assert.equal(h.top.dataset.nativeCapture,'true');
 assert.equal(h.calls.filter(m=>m==='window/modal-present').length,1);
 await release();assert.equal(h.top.dataset.nativeCapture,undefined);
 assert.equal(h.calls.filter(m=>m==='window/modal-present').length,2);
});
for(const phase of ['window/modal-cover','window/modal-refresh'])test('嵌套关闭立即回父窗，不等待'+phase,async()=>{
 const h=harness();await h.open();await h.push();const release=h.hold(phase);await h.dirty();await h.dirty();
 const before=h.calls.filter(m=>m==='window/modal-present').length;
 await h.pop();assert.equal(h.calls.filter(m=>m==='window/modal-present').length,before+1);
 const count=h.calls.length;await release();assert.equal(h.calls.length,count);assert.equal(h.window.__augitNativeModalActive,true);
});
test('返回已存在父表单时不重新隐藏或捕获原表单',async()=>{
 const h=harness();await h.open();const parent=h.top;await h.push();const release=h.hold('window/modal-present');
 await h.pop();assert.equal(h.top,parent);assert.equal(parent.dataset.nativeCapture,undefined);await release();
});

test('关闭后重开期间的变化不得重复呈现或提前解除截图隐藏',async()=>{
 const h=harness();await h.open();await h.close();const release=h.hold('window/modal-present');
 await h.open();const count=h.calls.filter(m=>m==='window/modal-present').length;
 await h.dirty();await h.dirty();
 assert.equal(h.calls.filter(m=>m==='window/modal-present').length,count);
 assert.equal(h.top.dataset.nativeCapture,'true');await release();
 assert.equal(h.top.dataset.nativeCapture,undefined);
});

test('原生呈现应答前保留网页外框，应答后标记系统边框归属',async()=>{
 const h=harness(),release=h.hold('window/modal-present');
 await h.open();assert.equal(h.top.dataset.nativeWindow,undefined);
 await release();assert.equal(h.top.dataset.nativeWindow,'true');
});

test('返回父窗尚未应答时的重复变化只提交一次恢复',async()=>{
 const h=harness();await h.open();await h.push();const release=h.hold('window/modal-present');
 await h.pop();const count=h.calls.filter(m=>m==='window/modal-present').length;
 await h.dirty();await h.dirty();
 assert.equal(h.calls.filter(m=>m==='window/modal-present').length,count);await release();
});
