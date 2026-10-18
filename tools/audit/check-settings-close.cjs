// 普通入口和真实仓库；仅写隔离配置，完整等待原生关窗并记录桥接时序。
const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os'),net=require('node:net');
const {spawn,execFile}=require('node:child_process'),execute=require('node:util').promisify(execFile);
const assert=require('node:assert/strict'),delay=ms=>new Promise(r=>setTimeout(r,ms));
(async()=>{
 const {chromium}=require(process.argv[2]),output=path.resolve(process.argv[3]);await fs.mkdir(output,{recursive:true});
 const temp=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-settings-close-'));let shell,browser,page;
 const evidence=new Map();
 const externalProbe=process.argv.includes('--external-change')?path.join(output,'watcher-probe.tmp'):null;
 if(externalProbe){await fs.writeFile(externalProbe,'initial',{flag:'wx'});}
 const unchangedProbe=process.argv.includes('--unchanged-background')?path.resolve('src/Augit.Shell/obj/augit-refresh-probe-'+process.pid+'.tmp'):null;
 if(unchangedProbe)await fs.writeFile(unchangedProbe,'initial',{flag:'wx'});
 try {
  const settings=JSON.parse(await fs.readFile(path.join(process.env.LOCALAPPDATA,'Augit/settings.json'),'utf8'));
  if(process.argv.includes('--many-tabs')){
   settings.openFiles=(await fs.readdir('src/Augit.Core/Documents')).filter(n=>n.endsWith('.cs')).slice(0,12).map(n=>'src/Augit.Core/Documents/'+n);
   settings.openFiles.push('tools/verify-ux-clone.cjs');settings.activeFile='tools/verify-ux-clone.cjs';
   settings.expandedDirectories=['tools','src','src/Augit.Core'].map(n=>path.resolve(n));
  }
  settings.lastWorkspace=path.resolve('.');await fs.writeFile(path.join(temp,'settings.json'),JSON.stringify(settings));
  const server=net.createServer();await new Promise(r=>server.listen(0,'127.0.0.1',r));const port=server.address().port;await new Promise(r=>server.close(r));
  const webArgs=process.argv.includes('--bundled-web')?[]:['--web-root',path.resolve('web')];
  const binary=process.argv.find(arg=>arg.startsWith('--binary='))?.slice(9)||'src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe';
  shell=spawn(path.resolve(binary),[...webArgs,'--workspace',path.resolve('.'),'--settings-file',path.join(temp,'settings.json'),'--user-data-folder',path.join(temp,'profile'),'--browser-args','--remote-debugging-port='+port],{stdio:'ignore'});
  console.log(JSON.stringify({pid:shell.pid,port,temp}));
  for(let i=0;i<120;i++){try{browser=await chromium.connectOverCDP('http://127.0.0.1:'+port);break;}catch{assert.equal(shell.exitCode,null);await delay(250);}}
  assert.ok(browser);page=browser.contexts()[0].pages()[0];page.setDefaultTimeout(30000);
  await page.waitForFunction(()=>window.__augitReady&&window.__augitSettingsReady&&window.__augitGitReady&&!window.__augitLive.sessionRestoring);
  if(process.argv.includes('--delay-cover')||process.argv.includes('--delay-present')||process.argv.includes('--delay-close')){
   // 只延迟审计实例收到的遮盖应答；真实宿主、截图与保存仍完整执行。
   await page.addInitScript(()=>{
    const bridge=chrome.webview,post=bridge.postMessage,add=bridge.addEventListener,ids=new Set(),presentIds=new Set(),closeIds=new Set(),deferred=[],presentDeferred=[],closeDeferred=[];
    window.__holdClose=false;window.__releaseClose=()=>{window.__holdClose=false;for(const run of closeDeferred.splice(0))run();};
    window.__holdPresent=false;window.__releasePresent=()=>{window.__holdPresent=false;for(const run of presentDeferred.splice(0))run();};
    window.__holdCover=false;window.__releaseCover=()=>{window.__holdCover=false;for(const run of deferred.splice(0))run();};
    bridge.postMessage=function(raw){const r=typeof raw==='string'?JSON.parse(raw):raw;if(r.method==='window/modal-cover')ids.add(r.id);if(r.method==='window/modal-present')presentIds.add(r.id);if(r.method==='window/modal-close')closeIds.add(r.id);return post.call(this,raw);};
    bridge.addEventListener=function(name,handler){return add.call(this,name,name==='message'?event=>{const r=typeof event.data==='string'?JSON.parse(event.data):event.data;if(closeIds.has(r.id)&&window.__holdClose){closeDeferred.push(()=>handler(event));window.__heldCloseCount=closeDeferred.length;}else if(presentIds.has(r.id)&&window.__holdPresent){presentDeferred.push(()=>handler(event));window.__heldPresentCount=presentDeferred.length;}else if(ids.has(r.id)&&window.__holdCover){deferred.push(()=>handler(event));window.__heldCoverCount=deferred.length;}else handler(event);}:handler);};
   });
   await page.reload();await page.waitForFunction(()=>window.__augitReady&&window.__augitSettingsReady&&window.__augitGitReady&&!window.__augitLive.sessionRestoring);
  }
  await page.evaluate(()=>{
   const bridge=chrome.webview,post=bridge.postMessage,pending=new Map();window.__closeAudit=[];
   const record=(kind,data)=>window.__closeAudit.push({kind,time:performance.now(),...data});
   bridge.postMessage=function(raw){const r=typeof raw==='string'?JSON.parse(raw):raw;if(r.method?.startsWith('window/modal-')||r.method?.startsWith('settings/')||r.method==='workspace/changes'){pending.set(r.id,r.method);record('request',{method:r.method});}return post.call(this,raw);};
   bridge.addEventListener('message',event=>{const r=typeof event.data==='string'?JSON.parse(event.data):event.data;if(r.event&&window.__traceBackgroundOrigins)record('host-event',{event:r.event,payload:r.payload});if(pending.has(r.id)){record('response',{method:pending.get(r.id),error:r.error,active:r.result?.active,files:r.result?.files,gitMetadata:r.result?.gitMetadata});pending.delete(r.id);}});
   document.addEventListener('click',event=>{const b=event.target.closest('[data-settings-action]');if(b)record('click',{action:b.dataset.settingsAction});},true);
   new MutationObserver(()=>{const count=document.querySelectorAll('.settings-window').length;if(count!==window.__closeCount){window.__closeCount=count;record('dom',{count});}}).observe(document.getElementById('app'),{subtree:true,childList:true});
  });
  if(process.argv.includes('--trace-background-origins'))await page.evaluate(()=>{
   window.__traceBackgroundOrigins=true;
   const original=window.__augitRenderRegions;
   window.__augitRenderRegions=function(...regions){window.__closeAudit.push({kind:'render',time:performance.now(),regions,stack:new Error().stack});return original.apply(this,regions);};
  });
  const results=[];
  if(unchangedProbe){
   await page.locator('.titlebar [aria-label="Settings"]').click();
   await page.waitForFunction(()=>window.__augitNativeModalActive&&!window.__augitNativeBackgroundBusy&&!document.querySelector('[data-native-capture]'));
   await delay(700);
   const batches=[];
   for(const value of ['first ignored edit','second ignored edit']){
    const start=await page.evaluate(()=>window.__closeAudit.length);
    await fs.writeFile(unchangedProbe,value);
    await page.waitForFunction(({start,probe})=>window.__closeAudit.slice(start).some(r=>r.kind==='response'&&r.method==='workspace/changes'&&r.files?.includes(probe)),{start,probe:unchangedProbe});
    await delay(2300);
    const trace=await page.evaluate(start=>window.__closeAudit.slice(start),start);
    batches.push({value,trace});evidence.set('unchanged-background.json',batches);
    assert.equal(trace.filter(r=>r.kind==='request'&&['window/modal-present','window/modal-cover','window/modal-refresh'].includes(r.method)).length,0,'无变化文件通知不能触发设置重新呈现或背景捕获');
   }
   await page.keyboard.press('Escape');await page.waitForFunction(()=>!window.__augitNativeModalActive);
  }
  if(externalProbe){
   const batches=[];
   for(const value of ['first edit','second edit']){
    const start=await page.evaluate(()=>window.__closeAudit.length);
    await fs.writeFile(externalProbe,value);
    await page.waitForFunction(({start,probe})=>window.__closeAudit.slice(start).some(r=>r.kind==='response'&&r.method==='workspace/changes'&&r.files?.includes(probe)),{start,probe:externalProbe});
    // 跨过一个兜底周期；同路径的下一次写入在下一轮单独验证。
    await delay(2300);
    const trace=await page.evaluate(start=>window.__closeAudit.slice(start),start);
    const deliveries=trace.filter(r=>r.kind==='response'&&r.method==='workspace/changes'&&r.files?.includes(externalProbe));
    batches.push({value,deliveries,trace});
    evidence.set('external-change.json',batches);
    assert.equal(deliveries.length,1,'真实文件变化只消费一次，不能被兜底重放');
   }
  }
  if(process.argv.includes('--detached-mutations')){
   await page.locator('.titlebar [aria-label="Settings"]').click();
   await page.waitForFunction(()=>window.__augitNativeModalActive&&!window.__augitNativeBackgroundBusy&&!document.querySelector('[data-native-capture]'));
   await delay(700);
   const traceStart=await page.evaluate(()=>{
    const start=window.__closeAudit.length;
    const dialog=document.querySelector('.settings-window .dialog');
    // 同一任务内修改并移除，观察器收到通知时这些节点已经不参与任何表面绘制。
    for(let i=0;i<8;i++){const node=document.createElement('span');dialog.append(node);node.className='audit-detached';node.remove();}
    return start;
   });
   await delay(1000);
   const trace=await page.evaluate(start=>window.__closeAudit.slice(start),traceStart);
   evidence.set('detached-mutations.json',trace);
   assert.equal(trace.filter(r=>r.kind==='request'&&['window/modal-cover','window/modal-refresh'].includes(r.method)).length,0,'已移除的设置子节点不能触发背景遮盖和刷新');
   await page.keyboard.press('Escape');await page.waitForFunction(()=>!window.__augitNativeModalActive);
  }
  if(process.argv.includes('--background-content')){
   // 检查真实背景PNG，而不只数DOM和窗口；快照残留表单同样会看起来像双窗口。
   const samples=[];
   for(let attempt=0;attempt<3;attempt++){
    await page.locator('.titlebar [aria-label="Settings"]').click();
    await page.waitForFunction(()=>window.__augitNativeModalActive&&!window.__augitNativeBackgroundBusy&&!document.querySelector('[data-native-capture]'));
    const snapshots=browser.contexts().flatMap(context=>context.pages()).filter(candidate=>candidate!==page);
    const images=[];
    for(const candidate of snapshots){
     const data=await candidate.evaluate(async()=>{
      const image=document.querySelector('img');if(!image)return null;await image.decode();
      return {width:image.naturalWidth,height:image.naturalHeight,url:image.src};
     });
     if(data){
      const session=await candidate.context().newCDPSession(candidate);
      try{
       await session.send('Page.enable');const tree=await session.send('Page.getResourceTree');
       const resource=await session.send('Page.getResourceContent',{frameId:tree.frameTree.frame.id,url:data.url});
       assert.equal(resource.base64Encoded,true);images.push({width:data.width,height:data.height});
       evidence.set(`background-content-${attempt}-${images.length}.png`,Buffer.from(resource.content,'base64'));
      }finally{await session.detach();}
     }
    }
    const state=await page.evaluate(async()=>{const{invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
    assert.equal(state.depth,1);assert.equal(await page.locator('.settings-window .dialog').count(),1);
    samples.push({attempt,state,images});
    await page.keyboard.press('Escape');await page.waitForFunction(()=>!window.__augitNativeModalActive);
   }
   evidence.set('background-content.json',samples);
  }
  let navigation;
  if(process.argv.includes('--navigation')){
   await page.locator('.titlebar [aria-label="Settings"]').click();
   await page.waitForFunction(()=>window.__augitNativeModalActive&&!window.__augitNativeBackgroundBusy&&!document.querySelector('[data-native-capture]'));
   await delay(500);
   await page.evaluate(async()=>{
    const {snapshotDepthForMutation}=await import('/src/native-modal-state.js');
    window.__navigationMutations=[];
    new MutationObserver(changes=>{
     const dialogs=[...document.querySelectorAll('.dialog')];
     for(const change of changes){
      const depth=snapshotDepthForMutation(change,dialogs);
      if(depth!==null)window.__navigationMutations.push({time:performance.now(),type:change.type,attribute:change.attributeName,connected:change.target.isConnected,tag:change.target.nodeName,className:change.target.className,region:change.target.closest?.('[data-augit-region]')?.getAttribute('data-augit-region'),depth});
     }
    }).observe(document.getElementById('app'),{subtree:true,childList:true,characterData:true,attributes:true,attributeOldValue:true,attributeFilter:['class','style','hidden','aria-checked','disabled','src','value']});
    window.__navigationTraceStart=window.__closeAudit.length;
   });
   for(const name of ['file-view','git','terminal','appearance']){
    await page.locator('.settings-window [data-settings-page="'+name+'"]').click();
    await delay(350);
   }
   await delay(750);
   navigation=await page.evaluate(()=>({mutations:window.__navigationMutations,trace:window.__closeAudit.slice(window.__navigationTraceStart)}));
   evidence.set('navigation.json',navigation);
   assert.equal(navigation.trace.filter(r=>r.kind==='request'&&['window/modal-present','window/modal-cover','window/modal-refresh'].includes(r.method)).length,0,'分类切换不能重新呈现或捕获背景');
   await page.keyboard.press('Escape');await page.waitForFunction(()=>!window.__augitNativeModalActive);
  }
  if(process.argv.includes('--delay-close')){
   // 只延迟真实关闭应答，验证新表单不会在旧窗口或主窗提前显示。
   await page.locator('.titlebar [aria-label="Settings"]').click();
   await page.waitForFunction(()=>window.__augitNativeModalActive&&!document.querySelector('[data-native-capture]'));
   await page.evaluate(()=>{window.__holdClose=true;window.__heldCloseCount=0;});
   await page.keyboard.press('Escape');await page.waitForFunction(()=>window.__heldCloseCount>0);
   const before=await page.evaluate(()=>window.__closeAudit.filter(r=>r.kind==='request'&&r.method==='window/modal-present').length);
   await page.locator('.titlebar [aria-label="Settings"]').click();
   await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
   const queued=await page.evaluate(()=>({count:document.querySelectorAll('.settings-window').length,hidden:document.querySelector('.settings-window .dialog')?.dataset.nativeCapture,visibility:getComputedStyle(document.querySelector('.settings-window .dialog')).visibility,presents:window.__closeAudit.filter(r=>r.kind==='request'&&r.method==='window/modal-present').length}));
   evidence.set('close-reopen-queued.json',queued);
   assert.equal(queued.count,1);assert.equal(queued.hidden,'true');assert.equal(queued.visibility,'hidden');assert.equal(queued.presents,before);
   await page.evaluate(()=>window.__releaseClose());
   await page.waitForFunction(()=>window.__augitNativeModalActive&&!document.querySelector('[data-native-capture]'));
   const settled=await page.evaluate(async()=>{const{invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
   assert.equal(settled.depth,1);
   await page.keyboard.press('Escape');await page.waitForFunction(()=>!window.__augitNativeModalActive);
  }
  const openings=[];
  for(const action of ['save','cancel','escape','header','apply-save']){
   if(process.argv.includes('--delay-present'))await page.evaluate(()=>{window.__holdPresent=true;window.__heldPresentCount=0;});
   const presentBefore=await page.evaluate(()=>window.__closeAudit.filter(r=>r.kind==='request'&&r.method==='window/modal-present').length);
   await page.locator('.titlebar [aria-label="Settings"]').click();
   if(process.argv.includes('--delay-present')){
    await page.waitForFunction(()=>window.__heldPresentCount>0);
    const repeated=await page.evaluate(()=>{
     const original=document.querySelector('.settings-window .dialog');
     const field=original.querySelector('[data-setting="textFontFamily"]');
     const value=field.value;
     for(let i=0;i<3;i++)document.querySelector('.titlebar [aria-label="Settings"]').dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true}));
     return{same:document.querySelector('.settings-window .dialog')===original,value:field.value,expected:value};
    });
    assert.equal(repeated.same,true,'呈现应答前的重复入口必须复用同一设置节点');
    assert.equal(repeated.value,repeated.expected);
    // 在首次/重开呈现应答前持续改变背景，覆盖过去测试遗漏的重入窗口。
    await page.evaluate(async()=>{for(let i=0;i<8;i++){document.querySelector('.titlebar').classList.toggle('audit-open-mutation');await new Promise(r=>requestAnimationFrame(r));}});
    const pending=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return{state:await invoke('window/modal-state'),requests:window.__closeAudit.filter(r=>r.kind==='request'&&r.method==='window/modal-present').length,hidden:document.querySelector('.settings-window .dialog')?.dataset.nativeCapture,count:document.querySelectorAll('.settings-window').length};});
    assert.equal(pending.requests-presentBefore,1,'一次打开只能提交一次呈现');assert.equal(pending.hidden,'true','呈现应答前不能提前解除截图隐藏');assert.equal(pending.count,1);assert.equal(pending.state.depth,1);
    assert.ok(pending.state.backgroundLoadedRevision>=0,'业务界面移走前背景首图必须已加载');
    openings.push({action,presentRequests:pending.requests-presentBefore,depth:pending.state.depth,hidden:pending.hidden,backgroundLoadedRevision:pending.state.backgroundLoadedRevision});
    await page.evaluate(()=>window.__releasePresent());
   }
   await page.waitForFunction(()=>window.__augitNativeModalActive&&!document.querySelector('[data-native-capture]'));
   if(process.argv.includes('--delay-cover')){
    await page.evaluate(()=>{window.__holdCover=true;document.querySelector('.titlebar').classList.toggle('audit-close-mutation');});
    await page.waitForFunction(()=>window.__heldCoverCount>0);
   }
   if(action==='apply-save'){
    await page.locator('[data-setting="textFontFamily"]').selectOption('Tahoma');
    await page.locator('[data-settings-action="apply"]').click();
    await page.waitForFunction(()=>document.querySelector('.settings-window .dialog')?.dataset.settingsState==='saved');
    assert.equal(await page.locator('.settings-window').count(),1);
   }
   // 窗口打开就操作，不先等待后台快照空闲。
   const start=performance.now();
   if(action==='escape')await page.keyboard.press('Escape');
   else if(action==='header')await page.locator('.settings-window .dialog-header [aria-label="Close"]').click();
   else await page.locator('[data-settings-action="'+(action==='apply-save'?'save':action)+'"]').click();
   await page.waitForFunction(()=>!document.querySelector('.settings-window')&&!window.__augitNativeModalActive,undefined,{timeout:30000});
   const state=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
   assert.equal(state.active,false);assert.equal(state.ownerEnabled,true);assert.equal(state.parentWindow,state.owner);assert.equal(state.snapshotCount,0);
   results.push({action,elapsedMilliseconds:performance.now()-start,state});
   if(process.argv.includes('--delay-cover')){
    await page.locator('.titlebar [aria-label="Settings"]').click();
    await page.waitForFunction(()=>window.__augitNativeModalActive&&!document.querySelector('[data-native-capture]'));
    await page.evaluate(()=>window.__releaseCover());
    await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
    assert.equal(await page.locator('.settings-window').count(),1);
    await page.keyboard.press('Escape');await page.waitForFunction(()=>!window.__augitNativeModalActive);
    await page.evaluate(()=>{window.__heldCoverCount=0;});
   }
  }
  let nested, nativeRace;
  if(process.argv.includes('--nested')){
   assert.ok(process.argv.includes('--delay-cover'),'嵌套竞争须扣住遮盖应答');
   await page.locator('.titlebar [aria-label="Settings"]').click();
   await page.waitForFunction(()=>window.__augitNativeModalActive&&!document.querySelector('[data-native-capture]'));
   const parent=await page.evaluate(async()=>{const{invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
   await page.locator('[data-setting="textFontFamily"]').selectOption('Segoe UI');
   // 原生生命周期探针，不将注入表单计作PyCharm同态页面。
   await page.evaluate(()=>{const layer=document.createElement('div');layer.id='audit-close-nested';layer.dataset.augitOverlay='';layer.innerHTML='<section class="dialog compact-input" style="position:absolute"><div class="dialog-header">嵌套生命周期探针</div><div class="dialog-body"><input value="draft"></div></section>';document.querySelector('.augit-window').append(layer);});
   await page.waitForFunction(()=>window.__augitNativeModalLastState?.depth===2&&!document.querySelector('[data-native-capture]'));
   await page.evaluate(()=>{window.__holdCover=true;window.__heldCoverCount=0;document.querySelector('.titlebar').classList.toggle('audit-nested-dirty');});
   await page.waitForFunction(()=>window.__heldCoverCount>0);
   const start=performance.now();await page.locator('#audit-close-nested').evaluate(node=>node.remove());
   await page.waitForFunction(()=>window.__augitNativeModalLastState?.depth===1&&!document.querySelector('[data-native-capture]'),undefined,{timeout:2000});
   const restored=await page.evaluate(async()=>{const{invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
   nested={elapsedMilliseconds:performance.now()-start,parent:parent.window,restored:restored.window,depth:restored.depth,snapshots:restored.snapshotCount};
   assert.equal(restored.window,parent.window);assert.equal(restored.snapshotCount,1);assert.equal(await page.locator('[data-setting="textFontFamily"]').inputValue(),'Segoe UI');
   await page.evaluate(()=>window.__releaseCover());await page.keyboard.press('Escape');await page.waitForFunction(()=>!window.__augitNativeModalActive);
   nativeRace=await page.evaluate(async()=>{
    const{invoke}=await import('/src/bridge.js');
    const params=(key,ancestors=[])=>({key,ancestors,title:'原生队列竞态',x:50,y:50,width:400,height:200});
    await invoke('window/modal-present',params('audit-parent'));
    const parent=await invoke('window/modal-state');
    await invoke('window/modal-present',params('audit-child',['audit-parent']));
    const capture=invoke('window/modal-cover');
    const start=performance.now();const restored=await invoke('window/modal-present',params('audit-parent'));
    const elapsedMilliseconds=performance.now()-start;
    await capture;const settled=await invoke('window/modal-state');await invoke('window/modal-close');
    await invoke('window/modal-present',params('audit-reopen'));await invoke('window/modal-close');
    const closed=await invoke('window/modal-state');return{parent:parent.window,restored,settled,closed,elapsedMilliseconds};
   });
   assert.equal(nativeRace.restored.window,nativeRace.parent);assert.equal(nativeRace.settled.depth,1);
   assert.equal(nativeRace.settled.captureCoverWindow,0);assert.equal(nativeRace.closed.snapshotCount,0);assert.equal(nativeRace.closed.active,false);
  }
  const report=await page.evaluate(()=>({url:location.href,tabs:window.__augitLive.tabs.length,trace:window.__closeAudit,errors:window.__augitErrors}));
  report.nested=nested;
  report.nativeRace=nativeRace;
  report.results=results;
  report.openings=openings;
  report.closeDispatch=report.trace.filter(r=>r.kind==='request'&&r.method==='window/modal-close').map(r=>{
   // 产品观察器可能先于审计观察器发出请求，两者处于同一次微任务检查点。
   const removed=report.trace.filter(p=>p.kind==='dom'&&p.count===0).sort((a,b)=>Math.abs(a.time-r.time)-Math.abs(b.time-r.time))[0];
   return {removedAt:removed?.time,requestAt:r.time,milliseconds:removed?Math.abs(r.time-removed.time):null};
  });
  evidence.set('result.json',report);
  assert.deepEqual(report.errors,[]);
  // 原生协议探针没有DOM表单，单独核验，不混入用户关闭延迟。
  const userDispatch=nativeRace?report.closeDispatch.slice(0,-2):report.closeDispatch;
  assert.ok(userDispatch.every(r=>r.milliseconds!==null&&r.milliseconds<500),'DOM移除后关窗不能等待后台捕获');
  assert.ok(results.every(r=>r.elapsedMilliseconds<2000),'关闭超出交互回归上限');
  console.log(JSON.stringify({tabs:report.tabs,results:results.map(({action,elapsedMilliseconds})=>({action,elapsedMilliseconds})),closeDispatch:report.closeDispatch,errors:report.errors}));
 }catch(error){if(page)evidence.set('failure.json',await page.evaluate(()=>({trace:window.__closeAudit,errors:window.__augitErrors,busy:window.__augitNativeBackgroundBusy,active:window.__augitNativeModalActive})));throw error;}
 finally{
  if(browser)await browser.close().catch(()=>{});if(shell?.pid&&shell.exitCode===null)await execute('taskkill.exe',['/PID',String(shell.pid),'/T','/F']).catch(()=>{});
  // 证据目录可能在当前仓库内；实例结束后再落盘，避免取证本身触发文件监听。
  for(const [name,value] of evidence)await fs.writeFile(path.join(output,name),Buffer.isBuffer(value)?value:JSON.stringify(value,null,2));
  if(externalProbe)await fs.rm(externalProbe);
  if(unchangedProbe)await fs.rm(unchangedProbe);
  assert.equal(path.dirname(temp),os.tmpdir());assert.ok(path.basename(temp).startsWith('Augit-settings-close-'));
  await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:250});console.log(JSON.stringify({cleaned:shell?.pid,temp}));
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
