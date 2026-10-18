// 正常启动器无远程调试对照；输入由 computer-use 发出，本脚本只管理实例和只读录像。
const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os');
const {execFile,spawn}=require('node:child_process'),execute=require('node:util').promisify(execFile);
const {createHash}=require('node:crypto');
const {waitForCheckpoint}=require('./capture-window-checkpoint.cjs');
const {recorderArguments,recordingDeadline}=require('./refresh-recorder-arguments.cjs');
const {startForegroundRecording}=require('./recording-foreground.cjs');
function normalEnvironment(source) {
 const result={...source};
 for(const key of Object.keys(result))if(/^WEBVIEW2_(ADDITIONAL_BROWSER_ARGUMENTS|BROWSER_EXECUTABLE_FOLDER|USER_DATA_FOLDER|PIPE_FOR_SCRIPT_DEBUGGER)$/i.test(key))delete result[key];
 return result;
}
function summarizeForeground(samples,pid) {
 if(!samples.length)throw Error('缺少前景采样');
 const interruptions=samples.filter(sample=>sample.processId!==pid);
 return {qualified:interruptions.length===0,expectedProcessId:pid,allowedWindows:[...new Set(samples.filter(s=>s.processId===pid).map(s=>s.window))],interruptions,samples,limitation:'只读50ms身份轮询；同进程窗口还须目视确认，不排除采样间短暂遮挡。'};
}
function recordingOptions(gdiClock) {
 return {desktopDuplication:!gdiClock,method:gdiClock?'ffmpeg-gdigrab-desktop-ffv1':'ffmpeg-ddagrab-desktop-ffv1'};
}
async function run(output,gdiClock=false) {
 output=path.resolve(output);await fs.mkdir(output,{recursive:false});
 const temp=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-no-cdp-'));
 const settings=path.join(temp,'settings.json'),events=[];let pid,recorder,ended,deadline,monitor;
 const save=(name,value)=>fs.writeFile(path.join(output,name),JSON.stringify(value,null,2)+'\n');
 const hash=async file=>createHash('sha256').update(await fs.readFile(file)).digest('hex');
 try {
  const original=path.join(process.env.LOCALAPPDATA,'Augit/settings.json');await fs.copyFile(original,settings);
  if(process.argv.includes('--dark')){const value=JSON.parse(await fs.readFile(settings,'utf8'));value.theme='Dark';await fs.writeFile(settings,JSON.stringify(value));}
  const env=normalEnvironment(process.env);
  const nativeTrace=path.join(output,'native-trace');await fs.mkdir(nativeTrace);
  env.AUGIT_SETTINGS_TRACE_DIRECTORY=nativeTrace;
  const literal=value=>"'"+value.replaceAll("'","''")+"'";
  const command='$ErrorActionPreference="Stop"; $p = & '+literal(path.resolve('tools/start-augit.ps1'))+' -SettingsFile '+literal(settings)+'; Write-Output ("AUGIT_PID=" + $p.Id)';
  const launched=await execute('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-Command',command],{env,timeout:120000,windowsHide:true});
  pid=Number(launched.stdout.match(/AUGIT_PID=(\d+)/)?.[1]);if(!Number.isSafeInteger(pid)||pid<=0)throw Error('启动器未返回PID');
  const dll=path.resolve('src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.dll');
  await save('launch.json',{pid,temp,launcher:'tools/start-augit.ps1',forcedBuild:true,remoteDebugging:false,cdpConnections:0,profile:'default-session',settingsCopy:true,settingsSourceHash:await hash(original),settingsCopyHash:await hash(settings),requestedTheme:process.argv.includes('--dark')?'Dark':'Light',dll,dllSha256:await hash(dll),buildOutput:launched.stdout,webHashes:Object.fromEntries(await Promise.all(['mockup.js','mockup.css','live-data.js','bridge.js'].map(async name=>[name,await hash(path.join('web/src',name))])))});
  const mapped=await execute(process.env.AUGIT_AUDIT_PYTHON||'D:/Python/Python3119/python.exe',['-B','tools/audit/read-process-mapped-images.py',String(pid)],{windowsHide:true,timeout:15000,env:{...env,PYTHONUTF8:'1'}});
  const identity=JSON.parse(mapped.stdout);
  if(identity.pid!==pid||!['Augit.dll','Augit.Infrastructure.dll'].every(name=>identity.mappedImages.some(item=>item.name===name)))throw Error('缺少本实例实际映射身份');
  await save('mapped-images.json',identity);
  console.log(JSON.stringify({pid,temp,output,stage:'等待前景确认后创建record.signal'}));
  await waitForCheckpoint(path.join(output,'record.signal'));
  await execute('pwsh',['-NoProfile','-File','tools/audit/capture-screen-region.ps1','-ExpectedProcessId',String(pid),'-Padding','0','-Output',path.join(output,'before.png')],{windowsHide:true,timeout:30000});
  const source=JSON.parse(await fs.readFile(path.join(output,'before.png.json'),'utf8'));
  monitor=await startForegroundRecording(output,pid,source.windowHandle);
  const recording=recordingOptions(gdiClock);
  const args=recorderArguments(source.frameRectPhysical,path.join(output,'cycle-screen.mkv'),recording.desktopDuplication,true,25);
  recorder=spawn('ffmpeg',args,{windowsHide:true,stdio:['pipe','ignore','pipe']});let log='';
  recorder.stderr.on('data',buffer=>log+=buffer);ended=new Promise((resolve,reject)=>{recorder.once('error',reject);recorder.once('close',resolve);});
  deadline=recordingDeadline(recorder);const started=Date.now();
  console.log(JSON.stringify({stage:'录像已启动',pid,recorderPid:recorder.pid,started}));
  await save('recording-ready.json',{pid,recorderPid:recorder.pid,started});
  // 人工观察不能以录像时限作为应用退出期限；自动封装后仍保留窗口供核对与正常关闭。
  const recordingExit=await ended;if(recordingExit!==0)throw Error('录像未正常封装');
  const foreground=await monitor.stop();monitor=null;
  await save('cycle-screen.json',{method:recording.method,arguments:args,frameRectPhysical:source.frameRectPhysical,width:source.imageSize[0],height:source.imageSize[1],repeatStaticFrames:true,started,events,foreground:summarizeForeground(foreground.samples,pid),remoteDebugging:false,cdpConnections:0,fullPresentationVerified:false,completeCycleVerified:false});
  await save('recording-log.json',{log});
  console.log('录像完成；正常关闭本次Augit后创建exit.signal，异常则按PID清理。');
  await waitForCheckpoint(path.join(output,'exit.signal'));
 } finally {
  deadline?.dispose();if(recorder&&recorder.exitCode===null){recorder.kill();await ended;}
  if(monitor)await monitor.stop();
  if(pid)await execute('taskkill',['/PID',String(pid),'/T','/F'],{windowsHide:true}).catch(()=>{});
  for(const file of ['record.signal','stop.signal','exit.signal','recording-ready.json'])await fs.rm(path.join(output,file),{force:true});
  await fs.rm(temp,{recursive:true,force:true});
  await save('cleanup.json',{pid,temp,finished:Date.now(),temporaryDirectoryRemoved:true});
 }
}
module.exports={normalEnvironment,summarizeForeground,recordingOptions};
if(require.main===module)run(process.argv[2],process.argv.includes('--gdi-clock')).catch(error=>{console.error(error);process.exitCode=1;});
