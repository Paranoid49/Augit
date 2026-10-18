// 只分析原生阶段；不将原生销毁完成解释为桌面呈现或无CDP验收。
const fs=require('node:fs'),path=require('node:path');
const required=['window/settings-close','close-request','close-start','owner-release-start','owner-enabled','close-cloaked','close-flushed','owner-activated','owner-released','destroy-start','controller-closed','destroyed'];
function analyzeTrace(data){
 if(!Number.isSafeInteger(data.processId)||data.processId<=0||!Number.isFinite(data.frequency)||data.frequency<=0||!Number.isFinite(data.originTimestamp)||!Array.isArray(data.samples))throw Error('阶段文件头无效');
 for(let i=0;i<data.samples.length;i++){
  const s=data.samples[i];
  if(typeof s.Stage!=='string'||!Number.isFinite(s.Timestamp)||s.Timestamp<data.originTimestamp||!Number.isInteger(s.Thread)||s.Thread<=0||(i&&s.Timestamp<data.samples[i-1].Timestamp))throw Error('阶段时间或线程无效');
 }
 const sample={};let position=-1;
 for(const stage of required){
  const matches=data.samples.map((s,i)=>({s,i})).filter(x=>x.s.Stage===stage);
  // 系统关闭可能先发取消再走真正关闭；以网页确认之后的关闭请求为准。
  const next=matches.find(x=>x.i>position);
  if(!next)throw Error('正常关闭阶段缺失或乱序：'+stage);
  sample[stage]=next.s;position=next.i;
 }
 const elapsed=(from,to)=>(sample[to].Timestamp-sample[from].Timestamp)*1000/data.frequency;
 return {processId:data.processId,utc:data.utc,sampleCount:data.samples.length,
  closeMilliseconds:elapsed('window/settings-close','destroyed'),
  dispatchMilliseconds:elapsed('window/settings-close','close-start'),
  ownerReleaseMilliseconds:elapsed('owner-release-start','owner-released'),
  compositorFlushMilliseconds:elapsed('close-cloaked','close-flushed'),
  ownerActivationMilliseconds:elapsed('close-flushed','owner-activated'),
  controllerCloseMilliseconds:elapsed('destroy-start','controller-closed'),
  threads:[...new Set(required.map(stage=>sample[stage].Thread))]};
}
function analyzeDirectory(directory){
 const groups=new Map();
 for(const name of fs.readdirSync(directory)){
  const match=/^settings-([0-9]+)-([0-9a-f]{32})-([0-9]+)[.]json$/.exec(name);if(!match)continue;
  const key=match[1]+'-'+match[2],revision=Number(match[3]);
  if(!groups.has(key)||groups.get(key).revision<revision)groups.set(key,{name,revision});
 }
 if(!groups.size)throw Error('没有原生设置阶段文件');
 const windows=[...groups.values()].map(({name})=>{
  try{return {file:name,...analyzeTrace(JSON.parse(fs.readFileSync(path.join(directory,name),'utf8'))),complete:true};}
  catch(error){return {file:name,complete:false,error:error.message};}
 });
 return {nativeCloseEvidenceComplete:windows.every(w=>w.complete),windows,fullPresentationVerified:false,
  limitation:'读取每个设置窗口最后一份原生导出；缺少销毁阶段必须失败，原生耗时不等于桌面关闭耗时，也不代替无CDP正常入口验收。'};
}
module.exports={analyzeTrace,analyzeDirectory};
if(require.main===module){const report=analyzeDirectory(path.resolve(process.argv[2]));process.stdout.write(JSON.stringify(report,null,2)+'\n');if(!report.nativeCloseEvidenceComplete)process.exitCode=1;}
