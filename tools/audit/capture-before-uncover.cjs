// 仅审计：撤盖前读取恢复后的合成图，用于区分DOM恢复与可捕获表面恢复。
function installCaptureBeforeUncover() {
 const bridge=window.chrome.webview,original=bridge.postMessage,pending=new Set(),records=[];
 function send(item) {
  if(!pending.delete(item))return;
  original.call(bridge,item.message);
 }
 function captured(message) {
  let request;try{request=typeof message==='string'?JSON.parse(message):message;}catch{}
  if(request?.method!=='window/modal-uncover')return original.call(bridge,message);
  const item={message};pending.add(item);
  const record={startedUtcMilliseconds:Date.now()};records.push(record);
  Promise.resolve().then(()=>window.__auditCaptureRestoredSurface()).then(
   result=>{record.result=result;},error=>{record.error=String(error.message||error);}
  ).finally(()=>{record.endedUtcMilliseconds=Date.now();send(item);});
 }
 bridge.postMessage=captured;
 window.__auditCaptureBeforeUncover={records,dispose(){
  if(bridge.postMessage===captured)bridge.postMessage=original;
  for(const item of [...pending])send(item);
  delete window.__auditCaptureBeforeUncover;
 }};
}
module.exports={installCaptureBeforeUncover};
