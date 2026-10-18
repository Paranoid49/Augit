const {test}=require('node:test'),assert=require('node:assert/strict');
const {correlateResponse}=require('./correlate-settings-response.cjs');
function fixture(){
 const anchor=Date.parse('2026-10-17T00:00:00Z');
 const trace={processId:42,originTimestamp:1000,frequency:1000,utc:'2026-10-17T00:00:00+00:00',samples:[
  ['created',0],['controller-create-start',10],['controller-create-end',90],['script-register-start',100],['script-register-end',180],['capture-start',200],['capture-end',2900],['presented',3000]
 ].map(([Stage,offset])=>({Stage,Timestamp:1000+offset,Thread:2}))};
 const probe={parseError:null,errors:'',samples:Array.from({length:63},(_,i)=>({processId:42,window:9,utcMilliseconds:anchor-50+i*50,elapsedMilliseconds:2,responded:true}))};
 return {trace,probe};
}
test('长捕获期间窗口响应和采样间隙独立量化',()=>{const {trace,probe}=fixture();const r=correlateResponse(trace,probe,42),capture=r.phases[3];assert.equal(capture.durationMilliseconds,2700);assert.equal(capture.sampleCount,55);assert.equal(capture.timeouts,0);assert.equal(capture.maximumSamplingGapMilliseconds,50);assert.equal(r.fullPresentationVerified,false);});
test('阶段超时与稀疏采样不能冒充连续响应',()=>{const {trace,probe}=fixture();probe.samples=probe.samples.filter((s,i)=>i<10||i>30);probe.samples[12].responded=false;const r=correlateResponse(trace,probe,42);assert.equal(r.phases[3].timeouts,1);assert.equal(r.phases[3].maximumSamplingGapMilliseconds,1100);});
test('拒绝跨实例、错误探针和不完整覆盖',()=>{for(const mutate of [f=>f.trace.processId=5,f=>f.probe.samples[4].processId=5,f=>f.probe.parseError='bad',f=>f.probe.errors='bad',f=>f.probe.samples.splice(-2),f=>f.probe.samples.splice(0,3)]){const f=fixture();mutate(f);assert.throws(()=>correlateResponse(f.trace,f.probe,42));}});
test('拒绝无效锚点、频率、倒退时钟和缺失阶段',()=>{for(const mutate of [f=>f.trace.utc='2026-10-17T00:00:00',f=>f.trace.frequency=0,f=>f.trace.samples[2].Timestamp=1,f=>f.trace.samples.splice(2,1),f=>f.probe.samples[3].utcMilliseconds=f.probe.samples[2].utcMilliseconds]){const f=fixture();mutate(f);assert.throws(()=>correlateResponse(f.trace,f.probe,42));}});
