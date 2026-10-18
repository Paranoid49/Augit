// 将无CDP原生阶段与同实例WM_NULL样本关联；不把发送消息响应等同于绘制或投递消息响应。
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {classifyResponse}=require('./shell-response-probe.cjs');
function correlateResponse(trace,probe,pid){
 assert.equal(trace.processId,pid,'原生阶段进程不符');
 assert.ok(Number.isFinite(trace.frequency)&&trace.frequency>0,'原生频率无效');
 assert.ok(Number.isSafeInteger(trace.originTimestamp),'原生起点无效');
 assert.match(trace.utc,/(Z|[+-][0-9]{2}:[0-9]{2})$/,'原生锚点缺少时区');
 const utc=Date.parse(trace.utc);assert.ok(Number.isFinite(utc),'原生锚点无效');
 assert.equal(probe.parseError,null,'响应样本解析失败');assert.equal(probe.errors,'','响应探针有错误');
 const summary=classifyResponse(probe.samples,pid),samples=probe.samples;
 for(let i=1;i<samples.length;i++)assert.ok(samples[i].utcMilliseconds>samples[i-1].utcMilliseconds,'响应时钟必须严格递增');
 let previous=trace.originTimestamp;const stages=new Map();
 for(const sample of trace.samples){
  assert.ok(Number.isSafeInteger(sample.Timestamp)&&sample.Timestamp>=previous,'原生阶段时钟倒退');
  assert.ok(!stages.has(sample.Stage),'原生阶段重复');
  stages.set(sample.Stage,utc+(sample.Timestamp-trace.originTimestamp)*1000/trace.frequency);previous=sample.Timestamp;
 }
 const pairs=[['created','presented'],['controller-create-start','controller-create-end'],['script-register-start','script-register-end'],['capture-start','capture-end']];
 const phases=pairs.map(([start,end])=>{
  const from=stages.get(start),to=stages.get(end);assert.ok(Number.isFinite(from)&&Number.isFinite(to)&&to>=from,'阶段缺失或顺序错误');
  const before=samples.findLast(s=>s.utcMilliseconds<=from),after=samples.find(s=>s.utcMilliseconds>=to);
  assert.ok(before&&after,'响应采样没有覆盖完整阶段');
  const inside=samples.filter(s=>s.utcMilliseconds>=from&&s.utcMilliseconds<=to);
  const covered=samples.filter(s=>s.utcMilliseconds>=before.utcMilliseconds&&s.utcMilliseconds<=after.utcMilliseconds);
  const gaps=covered.slice(1).map((s,i)=>s.utcMilliseconds-covered[i].utcMilliseconds);
  return {start,end,fromUtcMilliseconds:from,toUtcMilliseconds:to,durationMilliseconds:to-from,
   sampleCount:inside.length,timeouts:inside.filter(s=>!s.responded).length,
   maximumResponseMilliseconds:inside.length?Math.max(...inside.map(s=>s.elapsedMilliseconds)):null,
   maximumSamplingGapMilliseconds:gaps.length?Math.max(...gaps):null,
   sampledWindows:[...new Set(inside.map(s=>s.window))]};
 });
 return {pid,sampleCount:summary.sampleCount,timeouts:summary.timeouts,maximumResponseMilliseconds:summary.maximumMilliseconds,phases,
  fullPresentationVerified:false,limitation:'UTC解析精度为毫秒；WM_NULL仅证明采样时发送消息得到响应，不能排除间隙阻塞、COM重入或投递消息延迟，也不证明GPU、网页、字体和桌面呈现。'};
}
module.exports={correlateResponse};
if(require.main===module){
 const root=path.resolve(process.argv[2]),read=p=>JSON.parse(fs.readFileSync(p,'utf8'));
 const launch=read(path.join(root,'launch.json'));
 assert.equal(launch.launcher,'tools/start-augit.ps1');assert.equal(launch.remoteDebugging,false);assert.equal(launch.cdpConnections,0);
 const traces=fs.readdirSync(path.join(root,'native-trace')).filter(n=>n.endsWith('.json'));assert.equal(traces.length,1,'需要唯一开窗阶段文件');
 const tracePath=path.join(root,'native-trace',traces[0]),probePath=path.join(root,'response-probe.json');
 const result=correlateResponse(read(tracePath),read(probePath),launch.pid);
 result.sources=[tracePath,probePath].map(file=>({file:path.relative(root,file),sha256:crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')}));
 fs.writeFileSync(path.join(root,'response-stage-analysis.json'),JSON.stringify(result,null,2)+String.fromCharCode(10),{flag:'wx'});
 console.log(JSON.stringify(result));
}
