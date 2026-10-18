// 仅用于诊断：将撤盖消息移到下一次渲染回调后的任务，不改变产品资源。
function installDeferredUncover() {
 const bridge=window.chrome.webview,original=bridge.postMessage,pending=new Set();
 function deferred(message) {
  let request;try{request=typeof message==='string'?JSON.parse(message):message;}catch{}
  if(request?.method!=='window/modal-uncover')return original.call(bridge,message);
  const item={message,frame:null,timer:null};pending.add(item);
  item.frame=requestAnimationFrame(()=>{item.frame=null;item.timer=setTimeout(()=>{pending.delete(item);original.call(bridge,message);},0);});
 }
 bridge.postMessage=deferred;
 window.__auditDeferredUncover={dispose(){
  if(bridge.postMessage===deferred)bridge.postMessage=original;
  for(const item of pending){if(item.frame!==null)cancelAnimationFrame(item.frame);if(item.timer!==null)clearTimeout(item.timer);original.call(bridge,item.message);}
  pending.clear();delete window.__auditDeferredUncover;
 }};
}
module.exports={installDeferredUncover};
