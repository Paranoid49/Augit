// 系统桌面无损采样；标记只存在于隔离实例，不改变产品资源。
const assert=require('node:assert/strict');
const fs=require('node:fs/promises');
const path=require('node:path');
const {spawn}=require('node:child_process');
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));

// 缺失状态不能默认成首次；复用与首次记录在录像清单中，避免跨条件比较延迟。
function coverIdentity(state) {
 assert.ok(Number.isSafeInteger(state.captureCoverBytes)&&state.captureCoverBytes>=0,'缺少有效遮盖字节数');
 assert.ok(Number.isSafeInteger(state.captureCoverWindow)&&state.captureCoverWindow>=0,'缺少有效遮盖窗口身份');
 assert.equal(typeof state.captureCoverWindowVisible,'boolean');
 assert.equal(state.captureCoverReady,false,'录屏基线不得已有可见遮盖');
 assert.equal(state.captureCoverWindowVisible,false,'录屏基线的遮盖子窗必须隐藏');
 return {bytes:state.captureCoverBytes,window:state.captureCoverWindow,visible:false,
   reused:state.captureCoverBytes>0||state.captureCoverWindow!==0};
}

async function captureRefreshScreen(page,state,source,output) {
 let restoredSession;
 const initialState=await state();
 const initialCover=coverIdentity(initialState);
 const unchangedCover=process.argv.includes('--isolate-unchanged-cover');
 const createOnly=process.argv.includes('--isolate-create-only');
 const isolateHide=process.argv.includes('--isolate-cover-hide');
 const isolateReplace=process.argv.includes('--isolate-cover-replace');
 const noHold=process.argv.includes('--isolate-no-hold');
 assert.ok(!noHold||isolateReplace,'无额外等待只用于换图对照');
 const isolateCapture=process.argv.includes('--isolate-cover-capture')||isolateReplace;
 assert.ok(!(isolateHide&&isolateCapture),'隔离模式不能同时指定');
 assert.ok(!(unchangedCover&&createOnly)&&(!(unchangedCover||createOnly)||!isolateHide&&!isolateCapture),'表单不变诊断不能混用其他隔离');
 if(isolateReplace)await page.evaluate(()=>{
  // CSSOM修改不触发产品DOM观察器，避免额外自动刷新混入手动隔离流程。
  const style=document.createElement('style');style.id='audit-isolated-background';
  document.head.append(style);style.sheet.insertRule('.titlebar::after { content: ""; position: absolute; left: 600px; top: 4px; color: #ff0088; }');
 });
 const probes=await page.locator('.settings-window .dialog').evaluate(node=>{
  const surface=node.getBoundingClientRect(),points=[];
  for(const [index,color] of ['#e91159','#0de3ad','#472bf1'].entries()) {
   const mark=document.createElement('i');mark.dataset.auditScreenProbe='';
   mark.style.cssText=`position:absolute;pointer-events:none;z-index:2147483647;width:12px;height:12px;background:${color};left:${20+index*(surface.width-60)/2}px;top:${20+index*(surface.height-60)/2}px`;
   node.append(mark);
   const rect=mark.getBoundingClientRect();
   points.push({x:Math.round((rect.x-surface.x+6)*devicePixelRatio),y:Math.round((rect.y-surface.y+6)*devicePixelRatio),color});
  }
  return points;
 });
 await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
 const [left,top,right,bottom]=source.frameRectPhysical;
 const video=path.join(output,'refresh-screen.mkv');
 const desktopDuplication=process.argv.includes('--desktop-duplication');
 const repeatStaticFrames=process.argv.includes('--repeat-static-frames');
 assert.ok(!repeatStaticFrames||desktopDuplication,'静止重复帧仅用于桌面复制');
 const args=require('./refresh-recorder-arguments.cjs').recorderArguments(source.frameRectPhysical,video,desktopDuplication,repeatStaticFrames);
 const foregroundMonitor=await require('./recording-foreground.cjs').startForegroundRecording(output,source.processId,source.windowHandle);
 const recorder=spawn('ffmpeg',args,{windowsHide:true,stdio:[desktopDuplication?'pipe':'ignore','pipe','pipe']});
 console.log(JSON.stringify({recorderPid:recorder.pid,video}));
 let progress='',log='',ready=false;
 recorder.stdout.on('data',chunk=>{progress+=chunk;ready=/frame=s*[1-9]/.test(progress);});
 recorder.stderr.on('data',chunk=>log+=chunk);
 const ended=new Promise((resolve,reject)=>{recorder.once('error',reject);recorder.once('close',code=>resolve(code));});
 // 无桌面更新时事件源可能等不到录像截止时间；限制墙钟等待并仍由finally回收。
 const deadline=require('./refresh-recorder-arguments.cjs').recordingDeadline(recorder);
 const startedUtc=new Date().toISOString(),events=[];
 try {
  await page.evaluate(require('./trace-modal-refresh.cjs').installModalRefreshTrace);
  if(process.argv.includes('--trace-native-frames'))await page.evaluate(async source=>{
   const{invoke}=await import('/src/bridge.js');
   const start=(0,eval)('('+source+')');
   window.__auditNativeFrames=start({readState:()=>invoke('window/modal-state'),
    readDom:()=>({hidden:document.querySelectorAll('.dialog[data-native-background-capture],.dialog[data-native-capture]').length,busy:!!window.__augitNativeBackgroundBusy}),
    frame:requestAnimationFrame,cancelFrame:cancelAnimationFrame,now:Date.now});
  },require('./trace-native-modal-frames.cjs').startNativeFrameTrace.toString());
  if(process.argv.includes('--trace-surfaces'))await page.evaluate(require('./trace-modal-surfaces.cjs').installModalSurfaceTrace);
  if(process.argv.includes('--defer-uncover'))await page.evaluate(require('./defer-modal-uncover.cjs').installDeferredUncover);
  if(process.argv.includes('--capture-before-uncover')) {
   restoredSession=await page.context().newCDPSession(page);let sequence=0;
   await page.exposeFunction('__auditCaptureRestoredSurface',async()=>{
    const file='restored-surface-'+(sequence++)+'.png';
    const result=await restoredSession.send('Page.captureScreenshot',{format:'png',fromSurface:true,captureBeyondViewport:false,optimizeForSpeed:true});
    await fs.writeFile(path.join(output,file),Buffer.from(result.data,'base64'));
    return {file,method:'cdp-restored-surface',systemScreenshot:false};
   });
   await page.evaluate(require('./capture-before-uncover.cjs').installCaptureBeforeUncover);
  }
  for(let n=0;n<100&&!ready;n++){assert.equal(recorder.exitCode,null,log);await delay(100);}
  assert.ok(ready,'屏幕录像未取得第一帧');
  await delay(500);
  for(let sample=0;sample<6;sample++) {
   const before=await state(),start=performance.now(),startedAtUtc=new Date().toISOString();
   if(unchangedCover||createOnly){
    const result=await require('./refresh-cover-isolation.cjs').exerciseUnchangedCover({
     invoke:(method,params)=>page.evaluate(async({method,params})=>{const{invoke}=await import('/src/bridge.js');return invoke(method,params);},{method,params}),pause:delay
    },createOnly);
    events.push({sample,mode:createOnly?'create-only':'unchanged-cover',startedAtUtc,endedAtUtc:new Date().toISOString(),observedMilliseconds:performance.now()-start,...result});
    await delay(400);continue;
   }
   if(isolateHide||isolateCapture) {
    await page.evaluate(()=>{window.__auditModalRefreshTrace.records.length=0;});
    await require('./refresh-cover-isolation.cjs').exerciseCover({
     invoke:(method,params)=>page.evaluate(async({method,params})=>{const {invoke}=await import('/src/bridge.js');return invoke(method,params);},{method,params}),
     setHidden:value=>page.locator('.settings-window .dialog').evaluate((node,value)=>{if(value)node.dataset.nativeBackgroundCapture='true';else delete node.dataset.nativeBackgroundCapture;},value),
     frames:()=>page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))),
     pause:delay,holdMilliseconds:noHold?0:200,
     mutate:isolateReplace?()=>page.evaluate(sample=>{
      document.getElementById('audit-isolated-background').sheet.cssRules[0].style.content='"隔离替换 '+sample+'"';
     },sample):undefined
    },isolateCapture);
    const after=await state(),phases=await page.evaluate(()=>window.__auditModalRefreshTrace.records);
    assert.deepEqual(phases.map(p=>p.method),isolateCapture?['window/modal-cover','window/modal-refresh','window/modal-uncover']:['window/modal-cover','window/modal-uncover']);
    assert.equal(after.captureCoverReady,false);
    if(isolateHide)assert.equal(after.backgroundImageRevision,before.backgroundImageRevision,'只隐藏不得刷新背景');
    if(isolateReplace) {
     assert.ok(after.backgroundImageRevision>before.backgroundImageRevision,'新背景必须真正替换PNG');
     assert.equal(after.backgroundLoadedRevision,after.backgroundImageRevision,'新图必须加载完成');
    }
    events.push({sample,mode:isolateReplace?'hidden-replace':isolateCapture?'hidden-capture':'hidden-only',holdMilliseconds:noHold?0:200,startedAtUtc,endedAtUtc:new Date().toISOString(),observedMilliseconds:performance.now()-start,before,after,bridgePhases:phases});
    await delay(400);continue;
   }
   const mutationStarted=await page.evaluate(sample=>{
    window.__auditModalRefreshTrace.records.length=0;
    const start=performance.now();
    let mark=document.getElementById('audit-screen-background');
    if(!mark){mark=document.createElement('span');mark.id='audit-screen-background';mark.style.cssText='position:absolute;left:600px;top:4px;color:#ff0088';document.querySelector('.titlebar').append(mark);}
    mark.textContent=`刷新录屏 ${sample}`;
    return start;
   },sample);
   let after;
   for(let retry=0;retry<200;retry++){
    after=await state();
    if(after.backgroundImageRevision>before.backgroundImageRevision&&after.backgroundImageRevision===after.backgroundLoadedRevision&&!after.captureCoverReady&&!await page.evaluate(()=>!!window.__augitNativeBackgroundBusy))break;
    await delay(10);
   }
   assert.ok(after.backgroundImageRevision>before.backgroundImageRevision);
   assert.equal(after.backgroundImageRevision,after.backgroundLoadedRevision);
   assert.equal(after.captureCoverReady,false);
   const phases=await page.evaluate(()=>window.__auditModalRefreshTrace.records);
   assert.deepEqual(phases.map(phase=>phase.method),['window/modal-cover','window/modal-refresh','window/modal-uncover']);
   for(const phase of phases)assert.ok(!phase.failed&&phase.ended>=phase.started,'刷新阶段必须成功应答');
   events.push({sample,startedAtUtc,endedAtUtc:new Date().toISOString(),observedMilliseconds:performance.now()-start,timing:after.captureCoverTiming,
    priorCoverBytes:before.captureCoverBytes,priorCoverWindow:before.captureCoverWindow,
    bridgePhases:phases.map(phase=>({method:phase.method,startedUtcMilliseconds:phase.startedUtcMilliseconds,
     endedUtcMilliseconds:phase.endedUtcMilliseconds,startAfterMutation:phase.started-mutationStarted,
     endAfterMutation:phase.ended-mutationStarted,roundTripMilliseconds:phase.ended-phase.started}))});
   await delay(400);
  }
  if(desktopDuplication) {
   await delay(700);
   require('./refresh-recorder-arguments.cjs').finishEventRecording(recorder,true);
  }
  assert.equal(await ended,0,log);
  assert.equal(deadline.timedOut,false,'桌面采集超过30秒墙钟限制');
  const foregroundEvidence=await foregroundMonitor.stop();
  const manifest={foregroundEvidence,method:desktopDuplication?'ffmpeg-ddagrab-desktop-ffv1':'ffmpeg-gdigrab-desktop-ffv1',requestedFps:60,startedUtc,
   captureBeforeUncover:process.argv.includes('--capture-before-uncover'),
   initialCover,isolationHoldMilliseconds:unchangedCover||createOnly?200:isolateHide||isolateCapture?(noHold?0:200):null,isolationMode:createOnly?'create-only':unchangedCover?'unchanged-cover':isolateReplace?'hidden-replace':isolateCapture?'hidden-capture-held':isolateHide?'hidden-only-held':null,
   recorderPid:recorder.pid,repeatStaticFrames,deferredUncover:process.argv.includes('--defer-uncover'),frameRectPhysical:source.frameRectPhysical,width:right-left,height:bottom-top,
   probes,events,arguments:args,limitation:desktopDuplication?`Desktop Duplication独立采集，dup_frames=${repeatStaticFrames?1:0}；重复帧不代表新Present。本批限已核实零原点单显示器，不与GDI时间戳直接相减。`:'GDI轮询不是显示器Present事件追踪；需检查实际帧时间间隔与遮挡，不能仅凭60fps参数证明没有漏帧。'};
  await fs.writeFile(path.join(output,'refresh-screen.json'),JSON.stringify(manifest,null,2)+'\n');
  await fs.writeFile(path.join(output,'refresh-screen.log'),log+'\n'+progress);
  assert.equal(foregroundEvidence.qualified,true,'录像期间前景身份不连续，保留原图但不得作为产品闪帧验收');
  return manifest;
 } finally {
  deadline.dispose();
  if(recorder.exitCode===null){recorder.kill();await ended;}
  await foregroundMonitor.stop();
  if(process.argv.includes('--trace-native-frames')){
   const trace=await page.evaluate(()=>{const trace=window.__auditNativeFrames;if(!trace)return null;trace.dispose();delete window.__auditNativeFrames;return{records:trace.records,dropped:trace.dropped};});
   await fs.writeFile(path.join(output,'native-frame-trace.json'),JSON.stringify({ ...trace,limitation:'逐请求采样而非DWM Present追踪；started/ended为查询时间边界，不证明两次查询之间层级不变。'},null,2)+'\n');
  }
  // 钩子按安装的逆序释放，避免外层恢复一个已经解除的内层包装。
  if(restoredSession) {
   const records=await page.evaluate(()=>window.__auditCaptureBeforeUncover?.records||[]);
   await fs.writeFile(path.join(output,'restored-surface-trace.json'),JSON.stringify({records},null,2)+'\n');
   await page.evaluate(()=>window.__auditCaptureBeforeUncover?.dispose());
   await restoredSession.detach();
  }
  await page.evaluate(()=>window.__auditDeferredUncover?.dispose());
  if(process.argv.includes('--trace-surfaces')) {
   const records=await page.evaluate(()=>window.__auditModalSurfaceTrace?.records||[]);
   await fs.writeFile(path.join(output,'modal-surface-trace.json'),JSON.stringify({records,limitation:'DOM观察回调和宿主响应是观测点，不是合成器Present事件。'},null,2)+'\n');
   await page.evaluate(()=>window.__auditModalSurfaceTrace?.dispose());
  }
  await page.evaluate(()=>window.__auditModalRefreshTrace?.dispose());
  await page.evaluate(()=>{document.querySelectorAll('[data-audit-screen-probe],#audit-screen-background,#audit-isolated-background').forEach(node=>node.remove());});
  await page.waitForFunction(()=>!window.__augitNativeBackgroundBusy);
  await fs.writeFile(path.join(output,'refresh-screen.log'),log+'\n'+progress);
 }
}
module.exports={captureRefreshScreen,coverIdentity};
