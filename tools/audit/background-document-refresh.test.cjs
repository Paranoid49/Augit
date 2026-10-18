// 执行正式后台读取函数，覆盖切换、新读取、关闭、失败与晚到结果。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('web/src/live-data.js','utf8');
const start=source.indexOf('async function reloadChangedBackgroundDocument(');
const declaration=source.slice(start,source.indexOf('/** 把一次变化批次',start));
function harness(){
 const tab={id:'old',kind:'document',path:'old.txt',document:{text:'旧版本'},editor:'text',preview:true};
 const live={tabs:[tab],activeTabId:'other',pendingDocument:null,document:{path:'other.txt'}};
 let resolve,reject;const calls={sync:0,errors:0,clear:0,fetch:0};
 const context={window:{__augitLive:live},fetchDocument:()=>{calls.fetch++;return new Promise((a,b)=>{resolve=a;reject=b;});},
  toLiveDocument:payload=>({...payload,editor:'text'}),clearReportedError:()=>calls.clear++,syncActiveTab:()=>calls.sync++,
  notifyErrorOnce:()=>calls.errors++,describeFailure:value=>value};
 vm.createContext(context);vm.runInContext(declaration,context);
 return {tab,live,context,calls,run:()=>context.reloadChangedBackgroundDocument(live,'old.txt'),resolve:value=>resolve(value),reject:error=>reject(error)};
}
test('后台标签获得新内容但不改变前台正文、选择或提示',async()=>{
 const h=harness(),front=h.live.document;h.live.pendingDocument='new.txt';const pending=h.run();h.resolve({path:'old.txt',text:'新版本'});assert.equal(await pending,false);
 assert.equal(h.tab.document.text,'新版本');assert.equal(h.tab.preview,true);assert.equal(h.live.document,front);assert.equal(h.live.pendingDocument,'new.txt');assert.equal(h.live.activeTabId,'other');assert.equal(h.calls.sync,0);
});
for(const mode of ['关闭','替换','换工作区','同文件新读取'])test('后台晚到结果遇到'+mode+'必须丢弃',async()=>{
 const h=harness(),old=h.tab.document,pending=h.run();
 if(mode==='关闭')h.live.tabs=[];if(mode==='替换')h.tab.document={text:'更新版本'};if(mode==='换工作区')h.context.window.__augitLive={};if(mode==='同文件新读取')h.live.pendingDocument='old.txt';
 h.resolve({path:'old.txt',text:'迟到版本'});assert.equal(await pending,false);assert.notEqual(h.tab.document.text,'迟到版本');assert.equal(h.calls.sync,0);
});
test('读取期间切回原标签只同步该标签且要求重绘',async()=>{
 const h=harness(),pending=h.run();h.live.activeTabId='old';h.resolve({path:'old.txt',text:'新版本'});assert.equal(await pending,true);assert.equal(h.calls.sync,1);
});
test('同文件已有前台读取时不重复读取',async()=>{
 const h=harness();h.live.pendingDocument='old.txt';assert.equal(await h.run(),false);assert.equal(h.calls.fetch,0);
});
test('当前失败保留旧正文并反馈，已关闭后的失败不干扰用户',async()=>{
 for(const closed of [false,true]){const h=harness(),old=h.tab.document,pending=h.run();if(closed)h.live.tabs=[];h.reject(Error('读取失败'));assert.equal(await pending,false);assert.equal(h.tab.document,old);assert.equal(h.calls.errors,closed?0:1);}
});
test('响应路径错误不能写入原标签',async()=>{
 const h=harness(),old=h.tab.document,pending=h.run();h.resolve({path:'wrong.txt'});assert.equal(await pending,false);assert.equal(h.tab.document,old);assert.equal(h.calls.errors,1);
});
