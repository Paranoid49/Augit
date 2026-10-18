const {test}=require('node:test'),assert=require('node:assert/strict'),{classifyResponse}=require('./shell-response-probe.cjs');
const sample={processId:1,window:2,utcMilliseconds:100,elapsedMilliseconds:0.5,responded:true};
test('响应成功和超时分别登记，不把超时丢弃',()=>{const r=classifyResponse([sample,{...sample,utcMilliseconds:250,elapsedMilliseconds:101,responded:false}],1);assert.equal(r.sampleCount,2);assert.equal(r.timeouts,1);assert.equal(r.maximumMilliseconds,101);});
test('缺失、错实例和逆序样本拒绝验收',()=>{for(const samples of [[],[{...sample,processId:3}],[sample,{...sample,utcMilliseconds:99}],[{...sample,elapsedMilliseconds:NaN}]])assert.throws(()=>classifyResponse(samples,1));});
