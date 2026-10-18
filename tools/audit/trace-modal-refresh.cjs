// 仅在隔离审计页面注入，记录刷新方法往返时间；不保存参数、结果或用户内容。
function installModalRefreshTrace() {
 const bridge=window.chrome.webview,original=bridge.postMessage;
 const methods=new Set(['window/modal-cover','window/modal-refresh','window/modal-uncover']);
 const records=[],pending=new Map();
 const parse=value=>{try{return typeof value==='string'?JSON.parse(value):value;}catch{return null;}};
 function traced(message) {
  const request=parse(message);let entry;
  if(request&&methods.has(request.method)) {
   entry={method:request.method,started:performance.now(),startedUtcMilliseconds:Date.now(),ended:null,endedUtcMilliseconds:null,failed:false};
   records.push(entry);pending.set(request.id,entry);
  }
  try{return original.call(bridge,message);}
  catch(error){if(entry){entry.ended=performance.now();entry.endedUtcMilliseconds=Date.now();entry.failed=true;pending.delete(request.id);}throw error;}
 }
 const received=event=>{
  const message=parse(event.data),entry=message&&pending.get(message.id);
  if(!entry)return;
  entry.ended=performance.now();entry.endedUtcMilliseconds=Date.now();entry.failed=!!message.error;pending.delete(message.id);
 };
 bridge.postMessage=traced;bridge.addEventListener('message',received);
 window.__auditModalRefreshTrace={records,dispose(){
  if(bridge.postMessage===traced)bridge.postMessage=original;
  bridge.removeEventListener('message',received);pending.clear();delete window.__auditModalRefreshTrace;
 }};
}
module.exports={installModalRefreshTrace};
