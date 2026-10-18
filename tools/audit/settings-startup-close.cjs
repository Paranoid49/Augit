// 初始化关闭审计必须先确认未就绪，再等待真实进程退出；权限错误不能当作退出。
const assert=require('node:assert/strict');
const {waitAsyncCondition}=require('./wait-async-condition.cjs');
async function observeStartupClose({before,pid,close,probe=(id)=>process.kill(id,0),now=Date.now,timeout=15000,polling=25}){
 assert.equal(before.active,true,'必须存在设置子窗');
 assert.equal(before.ready,false,'必须捕获尚未就绪的设置子窗');
 assert.ok(Number.isInteger(pid)&&pid>0,'必须使用本次实例PID');
 const started=now();
 await close();
 await waitAsyncCondition(async()=>{
  try{await probe(pid);return false;}
  catch(error){if(error.code==='ESRCH')return true;throw error;}
 },{label:'初始化期间关闭主窗及设置',timeout,polling});
 return {passed:true,pid,observedExitMilliseconds:now()-started,settingsWasReady:before.ready,
  scriptRegistrationRaceVerified:false,fullPresentationVerified:false,
  limitation:'实际入口未就绪关闭，不人为延长COM等待；进程退出不能证明迟到续接已执行，也不代替桌面呈现验收。'};
}
module.exports={observeStartupClose};
