// 执行真实详情读取和刷新函数，完整提交身份须避免同一内容反复读盘和改写DOM。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('web/src/live-data.js','utf8');
function functionSource(start,end){const offset=source.indexOf(start);assert.ok(offset>=0);return source.slice(offset,source.indexOf(end,offset));}
function harness(){
 const hash='a'.repeat(40),files={innerHTML:''},detail={innerHTML:''},calls=[];
 const live={history:{commits:[{fullHash:hash}]}};
 const panel={querySelector:selector=>selector==='[data-live-changed-files]'?files:detail};
 const context={window:{__augitLive:live},document:{querySelector:selector=>selector==='.log-detail-panel'?panel:(files.innerHTML?{}:null)},
  selectedCommitRow:()=>({dataset:{fullHash:live.history.commits[0].fullHash}}),
  invoke:async(method,args)=>{calls.push({method,args});return{available:true,hash:args.revision.slice(0,7),fullHash:args.revision,subject:'提交',author:'作者',date:'2026-10-17',body:'正文',files:[{path:'file.txt',kind:'Added'}]};},
  escapeText:value=>value,commitFilesHtml:()=>'<div class=tree-row>文件</div>',bindCommitFilesScroll:()=>{},restoreCommitFilesScroll:()=>{},applyPendingDetailScroll:()=>{},requestAnimationFrame:callback=>callback()};
 vm.createContext(context);
 vm.runInContext('let commitDetailsToken=0;'+functionSource('async function loadCommitDetails(revision)', '// 提交详情的递增令牌')+functionSource('async function refreshCommitDetails()', '/**'+String.fromCharCode(10)+' * 读取单个提交的详情'),context);
 return{hash,live,calls,context,files,detail};
}
test('详情加载以完整哈希命中，同一提交刷新不重新请求或改写内容',async()=>{
 const h=harness();await h.context.loadCommitDetails(h.hash);const original=h.detail.innerHTML;
 assert.equal(h.context.window.__augitCommitLoaded,h.hash);
 await h.context.refreshCommitDetails();await h.context.refreshCommitDetails();
 assert.equal(h.calls.length,1);assert.equal(h.detail.innerHTML,original);assert.equal(h.live.commitDetailLoading,false);
});
test('选择另一个完整提交仍重新读取且更新身份',async()=>{
 const h=harness();await h.context.loadCommitDetails(h.hash);h.live.history.commits[0].fullHash='b'.repeat(40);
 await h.context.refreshCommitDetails();assert.equal(h.calls.length,2);
 assert.equal(h.context.window.__augitCommitLoaded,'b'.repeat(40));assert.equal(h.live.commitDetails.revision,'b'.repeat(40));
});
test('相同短哈希前缀的不同提交不能复用详情',async()=>{
 const h=harness();await h.context.loadCommitDetails(h.hash);const next='a'.repeat(7)+'b'.repeat(33);
 h.live.history.commits[0].fullHash=next;await h.context.refreshCommitDetails();
 assert.equal(h.calls.length,2);assert.equal(h.context.window.__augitCommitLoaded,next);
});
