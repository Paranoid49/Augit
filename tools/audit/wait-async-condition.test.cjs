const {test}=require('node:test'),assert=require('node:assert/strict');
const {waitAsyncCondition}=require('./wait-async-condition.cjs');
test('异步假值不能提前完成，直到第三次真值才通过',async()=>{
  let calls=0;
  await waitAsyncCondition(async()=>{await new Promise(r=>setTimeout(r,5));return ++calls===3;},{polling:1});
  assert.equal(calls,3);
});
test('始终为假必须超时',async()=>{
  await assert.rejects(waitAsyncCondition(async()=>false,{timeout:30,polling:1}),/等待超时/);
});
test('永不完成的桥接也受总超时约束',async()=>{
  await assert.rejects(waitAsyncCondition(()=>new Promise(()=>{}),{timeout:30}),/等待超时/);
});
test('桥接异常和非布尔对象不能被当作成功',async()=>{
  await assert.rejects(waitAsyncCondition(async()=>{throw Error('桥接失败');}),/桥接失败/);
  await assert.rejects(waitAsyncCondition(async()=>({ready:false})),/必须返回布尔值/);
});
test('时间参数必须有效',async()=>{
  for(const options of [{timeout:0},{timeout:NaN},{polling:-1}])await assert.rejects(waitAsyncCondition(async()=>true,options),/无效/);
});
