const assert=require('node:assert/strict');
// 仅用于审计实例：将隐藏与CapturePreview分离，失败也恢复表单并撤盖。
async function exerciseCover({invoke,setHidden,frames,pause,mutate,holdMilliseconds=200},capture) {
 assert.ok(Number.isSafeInteger(holdMilliseconds)&&holdMilliseconds>=0);
 const covered=await invoke('window/modal-cover');
 assert.equal(covered.captureCoverReady,true,'隔离前遮盖必须已就绪');
 try {
  if(mutate)await mutate();
  await setHidden(true);await frames();
  if(capture)await invoke('window/modal-refresh',{depth:0});
  if(holdMilliseconds>0)await pause(holdMilliseconds);
 } finally {
  try {await setHidden(false);await frames();}
  finally {await invoke('window/modal-uncover');}
 }
}
// 不改变表单DOM，单独核对遮盖创建/显示是否已经引起原生表面变化。
async function exerciseUnchangedCover({invoke,pause},createOnly=false) {
 const before=await invoke('window/modal-state');
 let prepared;
 try {
  prepared=await invoke('window/modal-cover');
  assert.equal(prepared.captureCoverReady,!createOnly);
  assert.equal(prepared.captureCoverWindowVisible,!createOnly);
  assert.ok(prepared.captureCoverBytes>0);
  assert.equal(prepared.backgroundImageRevision,before.backgroundImageRevision);
  await pause(200);
 } finally {await invoke('window/modal-uncover');}
 const after=await invoke('window/modal-state');
 assert.equal(after.captureCoverReady,false);
 assert.equal(after.captureCoverWindowVisible,false);
 assert.equal(after.backgroundImageRevision,before.backgroundImageRevision);
 return {before,prepared,after};
}
module.exports={exerciseCover,exerciseUnchangedCover};
