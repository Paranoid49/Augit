// 仅在审计实例记录隐藏标记与宿主可见性，不记录正文、路径或完整桥接结果。
function installModalSurfaceTrace() {
 const bridge=window.chrome.webview,original=bridge.postMessage,records=[],pending=new Map();
 const methods=new Set(['window/modal-cover','window/modal-refresh','window/modal-uncover']);
 const parse=value=>{try{return typeof value==='string'?JSON.parse(value):value;}catch{return null;}};
 const dom=()=>({dialogs:document.querySelectorAll('.dialog').length,hiddenDialogs:document.querySelectorAll('.dialog[data-native-background-capture],.dialog[data-native-capture]').length});
 const stamp=(kind,extra={})=>{records.push({kind,utcMilliseconds:Date.now(),monotonicMilliseconds:performance.now(),...dom(),...extra});if(records.length>512)records.shift();};
 function traced(message) {
  const request=parse(message);
  if(request&&methods.has(request.method)){pending.set(request.id,request.method);stamp('request',{method:request.method});}
  try{return original.call(bridge,message);}
  catch(error){if(request&&pending.has(request.id)){stamp('send-failed',{method:pending.get(request.id)});pending.delete(request.id);}throw error;}
 }
 const received=event=>{
  const message=parse(event.data),method=message&&pending.get(message.id);if(!method)return;
  pending.delete(message.id);const state=message.result||{},native={};
  for(const key of ['active','captureCoverReady','captureCoverWindowVisible','captureCoverTopmostChild','liveControllerVisible'])if(typeof state[key]==='boolean')native[key]=state[key];
  stamp('response',{method,failed:!!message.error,native});
 };
 const observer=new MutationObserver(changes=>{for(const change of changes)if(change.target.matches('.dialog'))stamp('dom-attribute',{attribute:change.attributeName,wasHidden:change.oldValue!==null,isHidden:change.target.hasAttribute(change.attributeName)});});
 observer.observe(document.getElementById('app'),{subtree:true,attributes:true,attributeOldValue:true,attributeFilter:['data-native-background-capture','data-native-capture']});
 bridge.postMessage=traced;bridge.addEventListener('message',received);stamp('installed');
 window.__auditModalSurfaceTrace={records,dispose(){observer.disconnect();if(bridge.postMessage===traced)bridge.postMessage=original;bridge.removeEventListener('message',received);pending.clear();delete window.__auditModalSurfaceTrace;}};
}
module.exports={installModalSurfaceTrace};
