// 正常入口打开真实文件和跳转行；只修改本批临时文件触发产品监听，不注入刷新标记。
const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os'),net=require('node:net');
const assert=require('node:assert/strict');
const {execFile,spawn}=require('node:child_process'),execute=require('node:util').promisify(execFile);
const {waitAsyncCondition}=require('./wait-async-condition.cjs');
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
function documentText(generation){return Array.from({length:40},(_,i)=>'Line '+String(i+1).padStart(2,'0')+'  generation '+generation+'  Ag 0123456789').join('\n')+'\n';}
function refreshed(before,after){return after.backgroundImageRevision>before.backgroundImageRevision&&after.backgroundLoadedRevision===after.backgroundImageRevision&&after.captureCoverReady===false&&after.captureCoverWindowVisible===false;}
function activeStatusRequest(requests){return requests.some(item=>item.method==='git/status'&&Number.isFinite(item.started)&&!Number.isFinite(item.ended));}
function burstEvidence(requests,writtenAt){
 const statuses=requests.filter(item=>item.method==='git/status');
 return {statusRequests:statuses.length,writeDuringStatus:statuses.some(item=>item.timeOrigin+item.started<=writtenAt&&item.timeOrigin+item.ended>=writtenAt),
  statusesSerialized:statuses.length>=2&&statuses.every((item,index)=>Number.isFinite(item.ended)&&(index===0||item.timeOrigin+item.started>=statuses[index-1].timeOrigin+statuses[index-1].ended))};
}
function presentationCompletion(requests,started){
 const modal=requests.filter(item=>['window/modal-cover','window/modal-refresh','window/modal-uncover'].includes(item.method));
 if(!modal.length||modal.some(item=>!Number.isFinite(item.ended)))return null;
 const last=modal.at(-1);
 if(last.method!=='window/modal-uncover')return null;
 return {milliseconds:last.timeOrigin+last.ended-started,coverCount:modal.filter(item=>item.method==='window/modal-cover').length};
}
async function createGitFixture(workspace){
 const git=(...args)=>execute('git',args,{cwd:workspace,windowsHide:true});
 await git('init','-b','main');await git('config','user.name','刷新审计');await git('config','user.email','refresh@example.invalid');
 await git('config','commit.gpgsign','false');await git('config','core.autocrlf','false');await git('add','refresh.txt');await git('commit','-m','test: refresh fixture');
}
function installRefreshRequestTrace(){
 const bridge=chrome.webview,post=bridge.postMessage,pending=new Map();window.__refreshAuditRequests=[];
 bridge.addEventListener('message',event=>{const response=typeof event.data==='string'?JSON.parse(event.data):event.data;const item=pending.get(response.id);if(item){item.ended=performance.now();item.endedUtcMilliseconds=Date.now();item.milliseconds=item.ended-item.started;pending.delete(response.id);}});
 bridge.postMessage=function(raw){const request=typeof raw==='string'?JSON.parse(raw):raw;
  if(['git/status','document/read','workspace/changes','workspace/list'].includes(request.method)||request.method.startsWith('window/modal-')){const item={id:request.id,method:request.method,started:performance.now(),startedUtcMilliseconds:Date.now(),timeOrigin:performance.timeOrigin};pending.set(request.id,item);window.__refreshAuditRequests.push(item);}return post.call(this,raw);};
}
function installRefreshMutationTrace(){
 // 只记录节点类型和属性，不采集正文；诊断迟到背景更新的来源。
 window.__refreshAuditMutations=[];
 new MutationObserver(changes=>{for(const change of changes){
  const target=change.target.nodeType===1?change.target:change.target.parentElement;
  const codeViews=nodes=>[...(nodes||[])].reduce((count,node)=>count+(node.nodeType===1?(node.matches('.code-view')?1:0)+node.querySelectorAll('.code-view').length:0),0);
  window.__refreshAuditMutations.push({started:performance.now(),type:change.type,tag:target?.tagName,
   addedCodeViews:codeViews(change.addedNodes),removedCodeViews:codeViews(change.removedNodes),
   className:typeof target?.className==='string'?target.className:null,attribute:change.attributeName,
   oldValue:change.type==='attributes'?change.oldValue:null,
   value:change.type==='attributes'?target?.getAttribute(change.attributeName):null,
   connected:target?.isConnected,insideDialog:!!target?.closest('.dialog')});
 }}).observe(document.getElementById('app'),{childList:true,subtree:true,characterData:true,attributes:true,attributeOldValue:true,attributeFilter:['class','style','hidden','aria-checked','disabled','src','value']});
}
async function run(modulePath,output){
 output=path.resolve(output);await fs.mkdir(output);
 const temporary=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-daily-refresh-'));
 const workspace=path.join(temporary,'workspace'),settings=path.join(temporary,'settings.json');
 let pid,browser,recorder,ended,monitor,deadline;const events=[];
 const save=(name,value)=>fs.writeFile(path.join(output,name),JSON.stringify(value,null,2)+'\n');
 try{
  await fs.mkdir(workspace);await fs.writeFile(path.join(workspace,'refresh.txt'),documentText(0));
  if(process.argv.includes('--switch-during-status'))await fs.writeFile(path.join(workspace,'other.txt'),'Other document remains selected');
  const gitFixture=process.argv.includes('--git-fixture');
  if(gitFixture)await createGitFixture(workspace);
  const scopeRegression=process.argv.includes('--scope-regression');
  if(scopeRegression){
   assert.ok(gitFixture,'侧栏范围回归需要真实Git夹具');
   await fs.writeFile(path.join(workspace,'refresh.txt'),documentText(1));
   await fs.writeFile(path.join(workspace,'untracked.txt'),'侧栏保留项');
  }
  await fs.copyFile(path.join(process.env.LOCALAPPDATA,'Augit/settings.json'),settings);
  const listener=net.createServer();await new Promise(resolve=>listener.listen(0,'127.0.0.1',resolve));const port=listener.address().port;await new Promise(resolve=>listener.close(resolve));
  const launch=require('./daily-launch-command.cjs')(process.cwd(),settings,port,workspace);
  launch.env={...require('./settings-no-cdp.cjs').normalEnvironment(process.env),WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS:'--remote-debugging-port='+port};
  const launched=await execute(launch.file,launch.args,{env:launch.env,windowsHide:true,timeout:120000});
  pid=Number(launched.stdout.match(/AUGIT_PID=(\d+)/)?.[1]);assert.ok(Number.isSafeInteger(pid)&&pid>0);
  await save('launch.json',{pid,port,temporary,workspace,launcher:'tools/start-augit.ps1',forcedBuild:true,settingsCopy:true,profile:'default-session',cdp:true,buildOutput:launched.stdout});
  console.log(JSON.stringify({pid,port,temporary}));
  const {chromium}=require(modulePath);
  for(let i=0;i<120&&!browser;i++){try{browser=await chromium.connectOverCDP('http://127.0.0.1:'+port);}catch{await delay(250);}}assert.ok(browser);
  const page=browser.contexts()[0].pages()[0];page.setDefaultTimeout(15000);
  await page.waitForFunction(()=>window.__augitReady&&window.__augitSettingsReady);
  // 新目录沿正常产品流程出现初始化提示；明确取消，不创建仓库也不绕过遮罩。
  if(!gitFixture){await page.locator('[data-repo-init-action=cancel]').click();await page.waitForSelector('.repository-init-window',{state:'detached'});}
  await page.waitForFunction(()=>!window.__augitNativeModalActive);
  await page.locator('[data-tree-path="refresh.txt"]').dblclick();
  await page.waitForFunction(g=>document.querySelector('.code-view')?.textContent.includes('generation '+g),scopeRegression?1:0);
  if(scopeRegression){
   await page.locator('.rail-button[data-tool=commit]').click();
   await page.waitForFunction(()=>document.querySelectorAll('.side-tool .changes-list .check-group-row').length===2);
  }
  await save('runtime.json',await require('./settings-runtime-identity.cjs').collectSettingsRuntimeIdentity(page,process.cwd(),'Debug'));
  // 本批修改位于Infrastructure；记录真实进程已加载模块及其哈希，不能只登记Shell DLL。
  const identity=await execute('powershell.exe',['-NoProfile','-Command',
   '$p=Get-CimInstance Win32_Process -Filter "ProcessId = '+pid+'"; [pscustomobject]@{pid=$p.ProcessId;path=$p.ExecutablePath;commandLine=$p.CommandLine;modules=@((Get-Process -Id '+pid+').Modules | Where-Object {$_.ModuleName -in @("Augit.dll","Augit.Infrastructure.dll","AudioDevProps2.dll")} | ForEach-Object {[pscustomobject]@{name=$_.ModuleName;path=$_.FileName;sha256=(Get-FileHash -LiteralPath $_.FileName -Algorithm SHA256).Hash}})} | ConvertTo-Json -Depth 4'],{windowsHide:true,timeout:15000});
  const processIdentity=JSON.parse(identity.stdout);assert.equal(processIdentity.pid,pid);
  await save('process-identity.json',processIdentity);
  const mapped=await execute(process.env.AUGIT_AUDIT_PYTHON||'D:/Python/Python3119/python.exe',['-B','tools/audit/read-process-mapped-images.py',String(pid)],{windowsHide:true,timeout:15000,env:{...process.env,PYTHONUTF8:'1'}});
  const mappedIdentity=JSON.parse(mapped.stdout);assert.equal(mappedIdentity.pid,pid);await save('mapped-images.json',mappedIdentity);
  // 只读桥接请求与应答时间，不改变响应、产品计时或刷新条件。
  await page.evaluate(installRefreshRequestTrace);
  if(process.argv.includes('--trace-surfaces'))await page.evaluate(require('./trace-modal-surfaces.cjs').installModalSurfaceTrace);
  if(process.argv.includes('--trace-mutations'))await page.evaluate(installRefreshMutationTrace);
  if(process.argv.includes('--switch-during-status')){
   await require('./refresh-switch-during-status.cjs').exerciseSwitch({page,workspace,output,write:fs.writeFile,documentText});
   assert.deepEqual(await page.evaluate(()=>window.__augitErrors),[]);return;
  }
  await page.locator('.code-view').focus();await page.keyboard.press('Control+g');
  const state=()=>page.evaluate(async()=>{const{invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
  await waitAsyncCondition(async()=>{const s=await state();return s.active&&s.depth===1;},{label:'跳转行原生窗口'});
  await page.waitForFunction(()=>!window.__augitNativeBackgroundBusy&&!document.querySelector('[data-native-capture]'));await delay(700);
  if(process.argv.includes('--same-document')){
   // 写回真实相同字节，由系统监听触发；只观察现有正文与表单节点。
   await page.locator('.dialog input').fill('17');
   const anchors=await page.evaluateHandle(()=>({code:document.querySelector('.code-view'),input:document.querySelector('.dialog input')}));
   const before=await state(),offset=await page.evaluate(()=>window.__refreshAuditRequests.length);
   await fs.writeFile(path.join(workspace,'refresh.txt'),documentText(0));
   await waitAsyncCondition(()=>page.evaluate(offset=>window.__refreshAuditRequests.slice(offset).some(r=>r.method==='document/read'&&Number.isFinite(r.ended)),offset),{label:'相同正文已实际重读'});
   await delay(600);await waitAsyncCondition(()=>page.evaluate(()=>!window.__augitNativeBackgroundBusy),{label:'相同正文刷新静置'});
   const after=await state(),requests=await page.evaluate(offset=>window.__refreshAuditRequests.slice(offset),offset);
   const identity=await anchors.evaluate(a=>({codePreserved:a.code===document.querySelector('.code-view'),inputPreserved:a.input===document.querySelector('.dialog input'),focused:document.activeElement===a.input,value:a.input.value}));
   await anchors.dispose();await save('same-document.json',{before,after,requests,identity});
   if(process.argv.includes('--expect-same-document-noop')){
    assert.equal(identity.codePreserved,true);assert.equal(identity.inputPreserved,true);assert.equal(identity.focused,true);assert.equal(identity.value,'17');
    assert.equal(requests.filter(r=>['window/modal-cover','window/modal-refresh','window/modal-uncover'].includes(r.method)).length,0);
    assert.equal(before.backgroundImageRevision,after.backgroundImageRevision);
   }
   await fs.writeFile(path.join(workspace,'refresh.txt'),documentText(1));
   await page.waitForFunction(()=>document.querySelector('.code-view')?.textContent.includes('generation 1'));
   await waitAsyncCondition(async()=>refreshed(after,await state()),{label:'真实正文变化必须刷新'});
   await waitAsyncCondition(()=>page.evaluate(()=>!window.__augitNativeBackgroundBusy),{label:'真实正文变化静置'});
   await save('changed-document.json',{state:await state(),visible:await page.locator('.code-view').innerText()});
   await page.keyboard.press('Escape');await waitAsyncCondition(async()=>!(await state()).active,{label:'关闭跳转行'});
   const closed=await state();await save('closed.json',closed);assert.equal(closed.snapshotCount,0);assert.equal(closed.captureCoverWindow,0);assert.equal(closed.ownerEnabled,true);
   assert.deepEqual(await page.evaluate(()=>window.__augitErrors),[]);return;
  }
  if(scopeRegression){
   // 只保存真实节点身份；外部新增空目录不改变Git文件状态，不注入任何产品标志。
   await page.locator('.dialog input').fill('17');
   const anchors=await page.evaluateHandle(()=>({root:document.querySelector('.augit-window'),dialog:document.querySelector('.dialog'),input:document.querySelector('.dialog input')}));
   const before=await state(),offset=await page.evaluate(()=>window.__refreshAuditRequests.length);
   const mutationOffset=await page.evaluate(()=>window.__refreshAuditMutations?.length||0);
   await fs.mkdir(path.join(workspace,'new-empty-directory'));
   await waitAsyncCondition(async()=>{const requests=await page.evaluate(offset=>window.__refreshAuditRequests.slice(offset),offset);return requests.some(r=>r.method==='workspace/list'&&Number.isFinite(r.ended));},{label:'空目录变化已读取',timeout:10000,polling:25});
   await delay(500);
   await waitAsyncCondition(()=>page.evaluate(()=>!window.__augitNativeBackgroundBusy),{label:'空目录刷新静置'});
   const identity=await anchors.evaluate(a=>({rootPreserved:a.root===document.querySelector('.augit-window'),dialogPreserved:a.dialog===document.querySelector('.dialog'),inputPreserved:a.input===document.querySelector('.dialog input'),focusPreserved:document.activeElement===a.input,inputValue:document.querySelector('.dialog input')?.value}));
   await anchors.dispose();const after=await state();
   const requests=await page.evaluate(offset=>window.__refreshAuditRequests.slice(offset),offset);
   const mutations=await page.evaluate(offset=>(window.__refreshAuditMutations||[]).slice(offset),mutationOffset);
   await save('scope-regression.json',{before,after,identity,requests,mutations});
   assert.equal(identity.rootPreserved,true);assert.equal(identity.dialogPreserved,true);assert.equal(identity.inputPreserved,true);assert.equal(identity.focusPreserved,true);
   assert.equal(after.window,before.window);
   assert.equal(identity.inputValue,'17');
   if(process.argv.includes('--verify-noop')){
    assert.equal(requests.filter(r=>['window/modal-cover','window/modal-refresh','window/modal-uncover'].includes(r.method)).length,0,'可见数据同值时不应捕获背景');
    assert.equal(after.backgroundImageRevision,before.backgroundImageRevision);
    assert.deepEqual(mutations,[],'同值侧栏不应产生背景变更');
    const changedOffset=await page.evaluate(()=>window.__refreshAuditRequests.length);
    await fs.writeFile(path.join(workspace,'new-visible.txt'),'真实新增文件');
    await page.waitForSelector('.changes-list [data-path="new-visible.txt"]');
    await waitAsyncCondition(async()=>refreshed(after,await state()),{label:'真实新增文件刷新背景',timeout:10000});
    await waitAsyncCondition(()=>page.evaluate(()=>!window.__augitNativeBackgroundBusy),{label:'真实新增文件刷新静置'});
    const changed=await state(),changedRequests=await page.evaluate(offset=>window.__refreshAuditRequests.slice(offset),changedOffset);
    const visible=await page.evaluate(()=>({rows:[...document.querySelectorAll('.changes-list .change-file-row')].map(row=>row.dataset.path),draft:document.querySelector('.dialog input')?.value,focused:document.activeElement===document.querySelector('.dialog input')}));
    await save('visible-change.json',{before:after,after:changed,requests:changedRequests,visible});
    assert.ok(visible.rows.includes('new-visible.txt'));assert.equal(visible.draft,'17');assert.equal(visible.focused,true);assert.equal(changed.window,before.window);
   }
   await page.keyboard.press('Escape');await waitAsyncCondition(async()=>!(await state()).active,{label:'关闭跳转行'});
   const closed=await state();await save('closed.json',closed);
   assert.equal(closed.snapshotCount,0);assert.equal(closed.captureCoverWindow,0);assert.equal(closed.ownerEnabled,true);
   assert.deepEqual(await page.evaluate(()=>window.__augitErrors),[]);
   console.log(JSON.stringify({scopeRegression:true,identity}));return;
  }
  const initial=await state();
  if(process.argv.includes('--trace-edge-source'))await require('./capture-modal-edge-source.cjs').captureModalEdgeSource(page,initial,output);
  const recordOwner=process.argv.includes('--record-owner');
  const captureHandle=recordOwner?initial.owner:initial.window;
  await execute('pwsh',['-NoProfile','-File','tools/audit/capture-screen-region.ps1','-WindowHandle',String(captureHandle),...(recordOwner?['-ExpectedActivation','Inactive','-ExpectedForegroundWindow',String(initial.window),'-ExpectedForegroundProcessId',String(pid)]:['-OwnerWindowHandle',String(initial.owner)]),'-ExpectedProcessId',String(pid),'-Padding','0','-Output',path.join(output,'before.png')],{windowsHide:true,timeout:30000});
  const source=JSON.parse(await fs.readFile(path.join(output,'before.png.json'),'utf8'));
  if(process.argv.includes('--verify-client-placement'))await save('client-placement.json',require('./capture-modal-edge-source.cjs').verifyClientPlacement(source,initial));
  monitor=await require('./recording-foreground.cjs').startForegroundRecording(output,pid,initial.window);
  const desktopDuplication=!process.argv.includes('--gdi-clock');
  const args=require('./refresh-recorder-arguments.cjs').recorderArguments(source.frameRectPhysical,path.join(output,'refresh-screen.mkv'),desktopDuplication,desktopDuplication,25);
  recorder=spawn('ffmpeg',args,{windowsHide:true,stdio:['pipe','pipe','pipe']});let progress='',log='';
  recorder.stdout.on('data',chunk=>progress+=chunk);recorder.stderr.on('data',chunk=>log+=chunk);
  ended=new Promise((resolve,reject)=>{recorder.once('error',reject);recorder.once('close',resolve);});
  deadline=require('./refresh-recorder-arguments.cjs').recordingDeadline(recorder);
  await waitAsyncCondition(async()=>/frame=\s*[1-9]/.test(progress),{label:'录像首帧'});await delay(400);
  for(let generation=1;generation<=4;generation++){
   const before=await state(),requestOffset=await page.evaluate(()=>window.__refreshAuditRequests.length),started=Date.now();
   const burst=process.argv.includes('--burst'),finalGeneration=burst?generation*2:generation;
   let secondWriteStarted=null;
   await fs.writeFile(path.join(workspace,'refresh.txt'),documentText(burst?finalGeneration-1:finalGeneration));
   if(burst){
    // 只观察真实请求，不延迟桥接应答或注入产品状态；在读取期间再次修改本批文件。
    await waitAsyncCondition(async()=>activeStatusRequest(await page.evaluate(offset=>window.__refreshAuditRequests.slice(offset),requestOffset)),{label:'首批状态查询在途',timeout:10000,polling:5});
    secondWriteStarted=Date.now();
    await fs.writeFile(path.join(workspace,'refresh.txt'),documentText(finalGeneration));
   }
   let after;await waitAsyncCondition(async()=>{after=await state();return refreshed(before,after)&&await page.evaluate(g=>document.querySelector('.code-view')?.textContent.includes('generation '+g),finalGeneration);},{label:'真实文件背景刷新',timeout:18000,polling:25});
   const finished=Date.now();
   // 首轮撤盖可能立即排队迟到的提交详情；保留旧观察点，同时单列完整静置窗口的最后撤盖。
   await delay(400);
   let requests,completion,settled;
   await waitAsyncCondition(async()=>{
    settled=await state();requests=await page.evaluate(offset=>window.__refreshAuditRequests.slice(offset),requestOffset);
    completion=presentationCompletion(requests,started);
    return completion!==null&&refreshed(before,settled)&&await page.evaluate(g=>!window.__augitNativeBackgroundBusy&&document.querySelector('.code-view')?.textContent.includes('generation '+g),finalGeneration);
   },{label:'后续背景刷新全部撤盖',timeout:18000,polling:25});
   const burstResult=burst?burstEvidence(requests,secondWriteStarted):null;
   events.push({generation,finalGeneration,secondWriteStarted,burstResult,started,ended:finished,observedMilliseconds:finished-started,presentationCompletion:completion,settledObservedMilliseconds:Date.now()-started,requests,before,after,settled});await save('events.json',events);
   if(burst){assert.equal(burstResult.writeDuringStatus,true);assert.equal(burstResult.statusesSerialized,true);}
  }
  await delay(700);
  await save('complete-requests.json',await page.evaluate(()=>window.__refreshAuditRequests));
  if(process.argv.includes('--trace-surfaces'))await save('surface-trace.json',await page.evaluate(()=>window.__auditModalSurfaceTrace.records));
  if(process.argv.includes('--trace-mutations'))await save('mutations.json',await page.evaluate(()=>window.__refreshAuditMutations));
  require('./refresh-recorder-arguments.cjs').finishEventRecording(recorder,desktopDuplication);assert.equal(await ended,0,log);assert.equal(deadline.timedOut,false);
  const foreground=await monitor.stop();monitor=null;
  await save('recording.json',{source,args,foreground,events,method:desktopDuplication?'ffmpeg-ddagrab-desktop-ffv1':'ffmpeg-gdigrab-desktop-ffv1',recorderPid:recorder.pid,recordOwner,fullPresentationVerified:false,limitation:'真实文件触发和宿主完成上界；全窗逐帧比较另行执行，CDP及录屏负载不代表无测量扰动。'});
  await fs.writeFile(path.join(output,'recording.log'),log+'\n'+progress);assert.equal(foreground.qualified,true);
  if(process.argv.includes('--trace-edge-source'))await require('./capture-modal-edge-source.cjs').captureSettledBackground(page,await state(),output);
  await page.keyboard.press('Escape');await waitAsyncCondition(async()=>!(await state()).active,{label:'关闭跳转行'});
  const closed=await state();assert.equal(closed.snapshotCount,0);assert.equal(closed.captureCoverWindow,0);assert.equal(closed.ownerEnabled,true);
  await save('closed.json',closed);assert.deepEqual(await page.evaluate(()=>window.__augitErrors),[]);
  console.log(JSON.stringify({completed:true,refreshMilliseconds:events.map(e=>e.observedMilliseconds)}));
 }catch(error){await save('failure.json',{message:error.message,stack:error.stack,pid,events});throw error;}
 finally{
  deadline?.dispose();if(recorder&&recorder.exitCode===null){recorder.kill();await ended;}if(monitor)await monitor.stop();
  if(browser)await browser.close().catch(()=>{});if(pid)await execute('taskkill',['/PID',String(pid),'/T','/F'],{windowsHide:true}).catch(()=>{});
  assert.equal(path.dirname(temporary),os.tmpdir());assert.ok(path.basename(temporary).startsWith('Augit-daily-refresh-'));
  await fs.rm(temporary,{recursive:true,force:true,maxRetries:8,retryDelay:250});await save('cleanup.json',{pid,temporary,removed:true});
 }
}
module.exports={documentText,refreshed,activeStatusRequest,burstEvidence,presentationCompletion,createGitFixture,installRefreshRequestTrace,installRefreshMutationTrace};
if(require.main===module)run(process.argv[2],process.argv[3]).catch(error=>{console.error(error);process.exitCode=1;});
