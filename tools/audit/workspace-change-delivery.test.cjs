// 执行真实订阅与兜底入口；同一宿主队列只能消费一次，不能推送后再重放。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('web/src/live-data.js','utf8');
const start=source.indexOf('let workspaceChangesRunning = false;');
const declarations=source.slice(start,source.indexOf('/** 把一次变化批次应用到界面。',start));
function harness(){
 let callback,timer,pending=null,hold=null,reads=0;const applied=[],errors=[];
 const context={window:{__augitLive:{},setInterval(fn){timer=fn;return 1;}},document:{hidden:false},hasHost:()=>true,subscribe(name,fn){callback=fn;},clearReportedError(){},notifyErrorOnce(...args){errors.push(args);},describeFailure:x=>x,
 invoke:async()=>{reads++;const batch=pending;pending=null;return{available:true,files:batch?.files||[],gitMetadata:batch?.gitMetadata||false};},
 applyWorkspaceChanges:async batch=>{if(batch.files.length||batch.gitMetadata){const wait=hold;hold=null;if(wait)await wait;applied.push(batch);}}};
 vm.createContext(context);vm.runInContext('let changeTimer=0;const ChangeFallbackIntervalMs=2000;'+declarations+';startWorkspaceChangePolling();',context);
 const settle=async()=>{for(let i=0;i<20;i++)await Promise.resolve();};
 return{applied,errors,settle,get reads(){return reads;},holdApply(fail=false){let release;hold=new Promise((resolve,reject)=>release=()=>fail?reject(new Error('读取失败')):resolve());return release;},queue(batch){pending=batch;},push(batch){callback(batch);},poll(){timer();}};
}
for(const batch of [{files:['D:/repo/file.txt'],gitMetadata:false},{files:[],gitMetadata:true}])test('推送与兜底只应用一遍'+JSON.stringify(batch),async()=>{
 const h=harness();await h.settle();h.queue(batch);h.push(batch);await h.settle();h.poll();await h.settle();
 assert.equal(h.applied.length,1);assert.deepEqual(h.errors,[]);
 h.queue(batch);h.push(batch);await h.settle();assert.equal(h.applied.length,2,'同路径下一次真实变化不能被去重吞掉');
});
test('推送丢失时兜底仍消费待处理变化',async()=>{
 const h=harness();await h.settle();h.queue({files:['D:/repo/missed.txt'],gitMetadata:false});h.poll();await h.settle();assert.equal(h.applied.length,1);
});
test('旧批次应用完成前不读取新批次，连续通知合并唤醒且保持应用顺序',async()=>{
 const h=harness();await h.settle();const release=h.holdApply();
 h.queue({files:['D:/repo/old.txt']});h.push();await h.settle();const reads=h.reads;
 h.queue({files:['D:/repo/new.txt']});h.push();h.poll();h.push();await h.settle();
 assert.equal(h.reads,reads,'旧批次未完成时不能并发消费新状态');
 release();await h.settle();
 assert.deepEqual(h.applied.map(batch=>batch.files[0]),['D:/repo/old.txt','D:/repo/new.txt']);
 assert.equal(h.reads,reads+1,'在途通知只要求下一轮读取，不重放事件内容');
 assert.deepEqual(h.errors,[]);
});
test('旧批次失败后仍处理等待中的变化，后续推送不被锁住',async()=>{
 const h=harness();await h.settle();const release=h.holdApply(true);
 h.queue({files:['D:/repo/failure.txt']});h.push();await h.settle();
 h.queue({files:['D:/repo/recovered.txt']});h.push();release();await h.settle();
 assert.equal(h.errors.length,1);assert.deepEqual(h.applied.map(batch=>batch.files[0]),['D:/repo/recovered.txt']);
 h.queue({files:['D:/repo/later.txt']});h.push();await h.settle();
 assert.deepEqual(h.applied.map(batch=>batch.files[0]),['D:/repo/recovered.txt','D:/repo/later.txt']);
});
