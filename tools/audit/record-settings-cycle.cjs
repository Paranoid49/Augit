// 在普通主窗口区域记录完整开关；前景仅允许本次主窗及实际创建的设置窗口。
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict');
const {spawn,execFile}=require('node:child_process'),execute=require('node:util').promisify(execFile);
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const {recorderArguments,recordingDeadline,finishEventRecording}=require('./refresh-recorder-arguments.cjs');
function assessCycleForeground(samples,pid,handles) {
 assert.ok(samples.length>0&&Number.isSafeInteger(pid)&&pid>0);
 assert.ok(handles.length>0&&handles.every(h=>Number.isSafeInteger(h)&&h>0));
 const interruptions=samples.filter(s=>s.processId!==pid||!handles.includes(s.window));
 return {qualified:interruptions.length===0,interruptions,allowedWindows:handles,expectedProcessId:pid};
}
function cycleOptions(argv) {
 const save=argv.includes('--record-save');
 const dirty=argv.includes('--record-dirty');
 assert.ok(!dirty||save,'脏草稿录像必须明确使用OK保存');
 return {closeAction:save?'save':'cancel',dirty,...(argv.includes('--record-phases')?{phases:true}:{})};
}
function requireLiveRecorder(recorder) {
 assert.equal(recorder.exitCode,null,'录像在完整开关结束前提前退出，不能登记全过程');
}
async function closeRecordedSettings({page,closed,invoke,closeAction,revision,expectedShell}) {
 const closing=Date.now();
 await page.locator(`[data-settings-action=${closeAction}]`).click();await closed();
 const closeMilliseconds=Date.now()-closing;
 const after=await invoke('window/settings-state');
 assert.equal(after.revision,revision+(closeAction==='save'?1:0),'OK必须实际提交，Cancel不得写入');
 if(expectedShell!==undefined)assert.equal((await invoke('settings/read')).terminalShell,expectedShell,'隔离配置必须保存草稿');
 return {closeMilliseconds,revision:after.revision};
}
async function recordSettingsCycle({output,shell,main,open,closed,invoke,evidence,closeAction='cancel',dirty=false,phases=false}) {
 assert.ok(['save','cancel'].includes(closeAction));
 assert.ok(!dirty||closeAction==='save');
 await fs.mkdir(output,{recursive:true});
 await fs.writeFile(path.join(output,'cycle-ready.json'),JSON.stringify({pid:shell.pid}));
 console.log('完整开关录像等待前景观察，创建cycle-continue文件后采集');
 await require('./capture-window-checkpoint.cjs').waitForCheckpoint(path.join(output,'cycle-continue'));
 await execute('pwsh',['-NoProfile','-File','tools/audit/capture-screen-region.ps1','-ExpectedProcessId',String(shell.pid),'-Output',path.join(output,'cycle-native.png'),'-Padding','0'],{windowsHide:true,timeout:30000});
 const source=JSON.parse(await fs.readFile(path.join(output,'cycle-native.png.json'),'utf8'));
 const monitor=await require('./recording-foreground.cjs').startForegroundRecording(output,shell.pid,source.windowHandle);
 const args=recorderArguments(source.frameRectPhysical,path.join(output,'cycle-screen.mkv'),true,true,25);
 const recorder=spawn('ffmpeg',args,{windowsHide:true,stdio:['pipe','ignore','pipe']});let log='';recorder.stderr.on('data',b=>log+=b);
 const ended=new Promise((resolve,reject)=>{recorder.once('error',reject);recorder.once('close',resolve);});
 const deadline=recordingDeadline(recorder),handles=[source.windowHandle],events=[],start=Date.now();
 try {
  await delay(1200);
  for(let cycle=0;cycle<1;cycle++){
   events.push({action:'open-request',cycle,milliseconds:Date.now()-start});
   const page=await open(),state=await invoke('window/settings-state');handles.push(state.window);
   events.push({action:'opened',cycle,milliseconds:Date.now()-start,state});
   // 仅诊断时留出无操作区间，区分初次显示与分类切换；不修改产品等待逻辑。
   if(phases){await delay(1200);events.push({action:'idle-after-open',milliseconds:Date.now()-start});}
   let expectedShell;
   if(dirty){
    events.push({action:'terminal-page-request',milliseconds:Date.now()-start});
    await page.locator('[data-settings-page=terminal]').click();
    if(phases){await delay(1200);events.push({action:'idle-after-terminal',milliseconds:Date.now()-start});}
    const field=page.locator('[data-setting=terminalShell]');
    expectedShell=(await field.inputValue())==='CommandPrompt'?'WindowsPowerShell':'CommandPrompt';
    await field.selectOption(expectedShell);
    events.push({action:'appearance-page-request',milliseconds:Date.now()-start});
    await page.locator('[data-settings-page=appearance]').click();
    if(phases){await delay(1200);events.push({action:'idle-after-appearance',milliseconds:Date.now()-start});}
    assert.equal(await page.locator('[data-settings-action=apply]').isEnabled(),true);
    events.push({action:'draft-edited',cycle,milliseconds:Date.now()-start,expectedShell});
   }
   await page.locator('[data-settings-filter]').evaluate(n=>n.blur());
   for(let i=0;i<4;i++){
    await main.evaluate(i=>{let n=document.querySelector('[data-settings-cycle-marker]');if(!n){n=document.createElement('span');n.dataset.settingsCycleMarker='true';document.querySelector('.statusbar').append(n);}n.textContent='Settings background update '+i;},i);
    events.push({action:'background-text',cycle,index:i,milliseconds:Date.now()-start});await delay(200);
   }
   events.push({action:'close-request',cycle,closeAction,milliseconds:Date.now()-start});
   const outcome=await closeRecordedSettings({page,closed,invoke,closeAction,revision:state.revision,expectedShell});
   events.push({action:'closed',cycle,closeAction,...outcome,milliseconds:Date.now()-start});await delay(350);
  }
  await delay(500);requireLiveRecorder(recorder);finishEventRecording(recorder,true);assert.equal(await ended,0);
 }finally {
  deadline.dispose();if(recorder.exitCode===null){recorder.kill();await ended;}
  const foreground=await monitor.stop(),assessment=assessCycleForeground(foreground.samples,shell.pid,handles);
  evidence.set('cycle-screen.json',{method:'ffmpeg-ddagrab-desktop-ffv1',arguments:args,frameRectPhysical:source.frameRectPhysical,width:source.imageSize[0],height:source.imageSize[1],events,closeAction,dirty,phases,foreground:{...assessment,samples:foreground.samples},recorderPid:recorder.pid,repeatStaticFrames:true,limitation:'完整开关录像包含合法状态变化，不能用静态全帧零差异判定；需逐阶段检查。'});
  evidence.set('cycle-recording-log.json',{log});
  assert.ok(assessment.qualified,'开关录像被其它前景窗口干扰');
 }
}
module.exports={recordSettingsCycle,assessCycleForeground,cycleOptions,closeRecordedSettings,requireLiveRecorder};
