const {test}=require('node:test'),assert=require('node:assert/strict');
const{documentText,refreshed,createGitFixture,installRefreshRequestTrace}=require('./refresh-daily-file.cjs');
test('真实文件场景保持行数与字符宽度而改变正文',()=>{
 assert.notEqual(documentText(0),documentText(1));assert.equal(documentText(0).length,documentText(1).length);assert.equal(documentText(1).split('\n').length,41);
});
test('连续写入证据要求真实在途状态请求、两批完成且无重叠',()=>{
 const {activeStatusRequest,burstEvidence}=require('./refresh-daily-file.cjs');
 const row=(started,ended)=>({method:'git/status',timeOrigin:1000,started,ended});
 assert.equal(activeStatusRequest([row(10,undefined)]),true);
 assert.equal(activeStatusRequest([row(10,30)]),false);
 const first=row(10,100),second=row(110,200);
 assert.deepEqual(burstEvidence([first,second],1050),{statusRequests:2,writeDuringStatus:true,statusesSerialized:true});
 assert.equal(burstEvidence([first],1050).statusesSerialized,false);
 assert.equal(burstEvidence([first,row(90,200)],1050).statusesSerialized,false);
 assert.equal(burstEvidence([first,row(110,undefined)],1050).statusesSerialized,false);
 assert.equal(burstEvidence([first,second],1001).writeDuringStatus,false);
});
test('迟到更新诊断记录时间和节点归属，不采集正文',()=>{
 const vm=require('node:vm');let callback;const window={};
 class MutationObserver{constructor(action){callback=action;}observe(root,options){assert.equal(root,'root');assert.equal(options.attributeOldValue,true);}}
 const {installRefreshMutationTrace}=require('./refresh-daily-file.cjs');
 vm.runInNewContext('('+installRefreshMutationTrace.toString()+')()',{window,MutationObserver,document:{getElementById:()=> 'root'},performance:{now:()=>42}});
 const target={nodeType:1,tagName:'DIV',className:'code-line',isConnected:true,textContent:'不能采集',getAttribute:()=> 'new',closest:()=>null};
 callback([{type:'childList',target},{type:'attributes',attributeName:'class',oldValue:'old',target}]);
 assert.equal(window.__refreshAuditMutations.length,2);assert.equal(window.__refreshAuditMutations[0].started,42);
 assert.equal(window.__refreshAuditMutations[1].oldValue,'old');assert.equal(window.__refreshAuditMutations[1].value,'new');
 assert.equal(window.__refreshAuditMutations[0].insideDialog,false);assert.equal(JSON.stringify(window.__refreshAuditMutations).includes('不能采集'),false);
 callback([{type:'childList',target,addedNodes:[{nodeType:1,matches:()=>true,querySelectorAll:()=>[]},{nodeType:3}],removedNodes:[{nodeType:1,matches:()=>false,querySelectorAll:()=>[{}]}]}]);
 assert.equal(window.__refreshAuditMutations[2].addedCodeViews,1);assert.equal(window.__refreshAuditMutations[2].removedCodeViews,1);
});
test('请求诊断保持原始对象和接收者，乱序应答按身份计时且未应答不伪造耗时',()=>{
 const vm=require('node:vm'),sent=[],listeners=[];let now=10;
 const bridge={postMessage(raw){assert.equal(this,bridge);sent.push(raw);return '原返回值';},addEventListener(type,listener){assert.equal(type,'message');listeners.push(listener);}};
 const context={chrome:{webview:bridge},window:{},performance:{now:()=>now,timeOrigin:1000},Date:{now:()=>1000+now}};
 vm.runInNewContext('('+installRefreshRequestTrace.toString()+')()',context);
 const first={id:1,method:'git/status'},second=JSON.stringify({id:2,method:'document/read'});
 assert.equal(bridge.postMessage(first),'原返回值');now=20;bridge.postMessage(second);
 bridge.postMessage({id:3,method:'settings/read'});bridge.postMessage({id:4,method:'window/modal-state'});
 now=35;listeners[0]({data:JSON.stringify({id:2,result:{}})});now=50;listeners[0]({data:{id:1,result:{}}});
 listeners[0]({data:{id:999,result:{}}});now=90;listeners[0]({data:{id:1,result:{}}});
 assert.equal(sent[0],first);assert.equal(sent[1],second);assert.equal(sent.length,4);
 const rows=context.window.__refreshAuditRequests;assert.equal(rows.length,3);
 assert.equal(rows[0].milliseconds,40);assert.equal(rows[1].milliseconds,15);assert.equal(rows[2].milliseconds,undefined);assert.equal(rows[0].timeOrigin,1000);
 assert.equal(rows[0].startedUtcMilliseconds,1010);assert.equal(rows[0].endedUtcMilliseconds,1050);
 assert.equal(rows[2].endedUtcMilliseconds,undefined);
});
test('真实Git夹具只提交指定文件并关闭签名和换行转换',async()=>{
 const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os');
 const execute=require('node:util').promisify(require('node:child_process').execFile);
 const temporary=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-refresh-fixture-test-'));
 try{
  await fs.writeFile(path.join(temporary,'refresh.txt'),documentText(0));await fs.writeFile(path.join(temporary,'unrelated.txt'),'保留');
  await createGitFixture(temporary);const git=async(...args)=>(await execute('git',args,{cwd:temporary,windowsHide:true})).stdout.trim();
  assert.equal(await git('branch','--show-current'),'main');assert.equal(await git('ls-tree','--name-only','HEAD'),'refresh.txt');
  assert.equal(await git('config','--local','commit.gpgsign'),'false');assert.equal(await git('config','--local','core.autocrlf'),'false');
  assert.equal((await execute('git',['show','HEAD:refresh.txt'],{cwd:temporary,windowsHide:true})).stdout,documentText(0));
  assert.equal(await git('status','--porcelain'),'?? unrelated.txt');
 }finally{await fs.rm(temporary,{recursive:true,force:true,maxRetries:5,retryDelay:100});}
});
test('刷新必须新图加载且遮盖已经撤下，旧版本及在途不通过',()=>{
 const before={backgroundImageRevision:2},after={backgroundImageRevision:3,backgroundLoadedRevision:3,captureCoverReady:false,captureCoverWindowVisible:false};
 assert.equal(refreshed(before,after),true);
 for(const patch of [{backgroundImageRevision:2},{backgroundLoadedRevision:2},{captureCoverReady:true},{captureCoverWindowVisible:true}])assert.equal(refreshed(before,{...after,...patch}),false);
});
test('完整呈现计时等待迟到第二链撤盖，不能用首轮完成代替',()=>{
 const {presentationCompletion}=require('./refresh-daily-file.cjs');
 const row=(method,ended)=>({method,timeOrigin:1000,ended});
 const requests=[row('window/modal-cover',20),row('window/modal-refresh',30),row('window/modal-uncover',40)];
 assert.deepEqual(presentationCompletion(requests,1000),{milliseconds:40,coverCount:1});
 requests.push(row('window/modal-cover',undefined));assert.equal(presentationCompletion(requests,1000),null);
 requests[3].ended=50;assert.equal(presentationCompletion(requests,1000),null);
 requests.push(row('window/modal-refresh',60),row('window/modal-uncover',70));
 assert.deepEqual(presentationCompletion(requests,1000),{milliseconds:70,coverCount:2});
 assert.equal(presentationCompletion([],1000),null);
});
