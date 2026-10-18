// 探针进程由本次审计负责；超时与正常退出均释放，不添加常驻诊断服务。
const {spawn}=require('node:child_process'),path=require('node:path');
function classifyResponse(samples,pid){
 if(!Number.isSafeInteger(pid)||pid<=0||!samples.length)throw Error('响应采样缺少身份或数据');
 let previous=-Infinity;
 for(const s of samples){
  if(s.processId!==pid||!Number.isSafeInteger(s.window)||s.window<=0||!Number.isSafeInteger(s.utcMilliseconds)||s.utcMilliseconds<previous||!Number.isFinite(s.elapsedMilliseconds)||s.elapsedMilliseconds<0||typeof s.responded!=='boolean')throw Error('响应采样损坏');
  previous=s.utcMilliseconds;
 }
 return {sampleCount:samples.length,timeouts:samples.filter(s=>!s.responded).length,maximumMilliseconds:Math.max(...samples.map(s=>s.elapsedMilliseconds)),samples,limitation:'WM_NULL发送消息响应，仅反映采样时窗口线程；不证明网页、GPU或全部已投递消息无延迟。'};
}
async function startResponseProbe(pid){
 const child=spawn('pwsh',['-NoProfile','-File',path.join(__dirname,'watch-shell-response.ps1'),'-ExpectedProcessId',String(pid)],{windowsHide:true,stdio:['ignore','pipe','pipe']});
 let pending='',errors='',parseError,readyResolve,readyReject,result;const samples=[];
 const ready=new Promise((resolve,reject)=>{readyResolve=resolve;readyReject=reject;});
 const ended=new Promise(resolve=>{child.once('error',e=>{readyReject(e);resolve();});child.once('close',()=>{if(!samples.length)readyReject(Error('响应采样器提前退出：'+errors));resolve();});});
 child.stderr.on('data',chunk=>errors+=chunk);
 child.stdout.on('data',chunk=>{pending+=chunk;let at;while((at=pending.indexOf('\n'))>=0){const line=pending.slice(0,at).trim();pending=pending.slice(at+1);if(!line)continue;try{samples.push(JSON.parse(line));readyResolve();}catch(e){parseError=e;readyReject(e);}}});
 const timeout=setTimeout(()=>readyReject(Error('响应采样器启动超时')),10000);
 try{await ready;}catch(e){child.kill();await ended;throw e;}finally{clearTimeout(timeout);}
 console.log(JSON.stringify({responseProbePid:child.pid}));
 return {async stop(){if(result)return result;const exitedBeforeStop=child.exitCode!==null;if(!exitedBeforeStop)child.kill();await ended;
  result={...classifyResponse(samples,pid),probePid:child.pid,exitedBeforeStop,parseError:parseError?String(parseError):null,errors};return result;}};
}
module.exports={classifyResponse,startResponseProbe};
