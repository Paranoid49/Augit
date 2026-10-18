// 在Node侧等待异步条件的最终布尔值；Promise对象本身不能作为成功条件。
const {performance}=require('node:perf_hooks');
async function waitAsyncCondition(predicate,{timeout=15000,polling=50,label='异步条件'}={}) {
  if(typeof predicate!=='function'||!Number.isFinite(timeout)||timeout<=0||!Number.isFinite(polling)||polling<0)
    throw new TypeError('等待条件或时间参数无效。');
  const deadline=performance.now()+timeout;
  for(;;){
    const remaining=deadline-performance.now();
    if(remaining<=0)throw new Error(label+'等待超时。');
    let timer;
    const expired=new Promise((_,reject)=>{timer=setTimeout(()=>reject(new Error(label+'等待超时。')),remaining);});
    let value;
    try{value=await Promise.race([Promise.resolve().then(predicate),expired]);}
    finally{clearTimeout(timer);}
    if(value===true)return;
    if(typeof value!=='boolean')throw new TypeError(label+'必须返回布尔值。');
    await new Promise(resolve=>setTimeout(resolve,Math.min(polling,Math.max(0,deadline-performance.now()))));
  }
}
module.exports={waitAsyncCondition};
