const {test}=require('node:test');
const assert=require('node:assert/strict');
const {classifyForeground}=require('./recording-foreground.cjs');
test('前后身份相同不能掩盖中途其他应用遮挡',()=>{
 const samples=[{utcMilliseconds:1000,processId:12,window:34},{utcMilliseconds:1050,processId:56,window:78},{utcMilliseconds:1100,processId:12,window:34}];
 const r=classifyForeground(samples,12,34);assert.equal(r.uninterrupted,false);assert.equal(r.interruptions.length,1);assert.equal(r.maximumGapMilliseconds,50);
});
test('同进程其他窗口也不能冒充被测模态前景',()=>{
 assert.equal(classifyForeground([{utcMilliseconds:1000,processId:12,window:35}],12,34).uninterrupted,false);
 assert.equal(classifyForeground([{utcMilliseconds:1000,processId:12,window:34}],12,34).uninterrupted,true);
});
test('缺失损坏及逆序样本不能算通过',()=>{
 assert.throws(()=>classifyForeground([],12,34));
 assert.throws(()=>classifyForeground([{}],12,34));
 assert.throws(()=>classifyForeground([{utcMilliseconds:1000,processId:12,window:34},{utcMilliseconds:900,processId:12,window:34}],12,34));
});
