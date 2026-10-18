// 系统前景变化使录像失去验收资格；不把其他应用遮挡归因为产品透明帧。
const {spawn}=require('node:child_process');
const path=require('node:path');
const fs=require('node:fs/promises');
function classifyForeground(samples,expectedProcessId,expectedWindow) {
 if(!Number.isSafeInteger(expectedProcessId)||expectedProcessId<=0||!Number.isSafeInteger(expectedWindow)||expectedWindow<=0)throw Error('预期前景身份无效');
 if(!samples.length)throw Error('录像缺少前景采样');
 let previous=-Infinity,maxGap=0;
 for(const sample of samples) {
  if(!Number.isSafeInteger(sample.utcMilliseconds)||!Number.isSafeInteger(sample.processId)||!Number.isSafeInteger(sample.window)||sample.utcMilliseconds<previous)throw Error('前景采样损坏');
  if(previous!==-Infinity)maxGap=Math.max(maxGap,sample.utcMilliseconds-previous);
  previous=sample.utcMilliseconds;
 }
 const interruptions=samples.filter(s=>s.processId!==expectedProcessId||s.window!==expectedWindow);
 return {expectedProcessId,expectedWindow,sampleCount:samples.length,maximumGapMilliseconds:maxGap,interruptions,uninterrupted:interruptions.length===0,limitation:'50ms轮询只记录所采样身份；不能排除采样间短暂遮挡或同进程置顶窗'};
}
async function startForegroundRecording(output,expectedProcessId,expectedWindow) {
 const child=spawn('pwsh',['-NoProfile','-File',path.join(__dirname,'watch-recording-foreground.ps1')],{windowsHide:true,stdio:['ignore','pipe','pipe']});
 console.log(JSON.stringify({foregroundRecorderPid:child.pid}));
 let pending='',errors='',resolveReady,rejectReady,parseError,stopped=false;
 const samples=[],ready=new Promise((resolve,reject)=>{resolveReady=resolve;rejectReady=reject;});
 const ended=new Promise(resolve=>{child.once('error',error=>{rejectReady(error);resolve();});child.once('close',()=>{if(!samples.length)rejectReady(Error('前景采样器提前退出：'+errors));resolve();});});
 child.stderr.on('data',chunk=>{errors+=chunk;});
 child.stdout.on('data',chunk=>{
  pending+=chunk;let newline;
  while((newline=pending.indexOf('\n'))>=0) {
   const line=pending.slice(0,newline).trim();pending=pending.slice(newline+1);
   if(!line)continue;
   try {samples.push(JSON.parse(line));resolveReady();}catch(error){parseError=error;rejectReady(error);}
  }
 });
 const timeout=setTimeout(()=>rejectReady(Error('前景采样器启动超时')),10000);
 try {await ready;}catch(error){child.kill();await ended;throw error;}finally{clearTimeout(timeout);}
 let result;
 return {async stop() {
  if(stopped)return result;stopped=true;
  const exitedBeforeStop=child.exitCode!==null;
  if(!exitedBeforeStop)child.kill();await ended;
  const assessment=classifyForeground(samples,expectedProcessId,expectedWindow);
  result={...assessment,recorderPid:child.pid,exitedBeforeStop,parseError:parseError?String(parseError):null,samples};
  result.qualified=result.uninterrupted&&!exitedBeforeStop&&!parseError;
  await fs.writeFile(path.join(output,'recording-foreground.json'),JSON.stringify(result,null,2)+'\n');
  return result;
 }};
}
module.exports={classifyForeground,startForegroundRecording};
