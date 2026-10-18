// 只读采样原生遮盖状态；同一时刻最多一个请求，避免诊断堆积桥接队列。
function startNativeFrameTrace({readState,readDom,frame,cancelFrame,now,limit=1024}) {
 if(!Number.isSafeInteger(limit)||limit<1)throw Error('采样上限无效');
 const records=[];let disposed=false,handle=null,dropped=0;
 const select=state=>Object.fromEntries(['active','captureCoverReady','captureCoverWindowVisible','captureCoverTopmostChild','liveControllerVisible'].filter(key=>typeof state?.[key]==='boolean').map(key=>[key,state[key]]));
 async function sample(){
  handle=null;if(disposed)return;
  const started=now(),before=readDom();
  try {
   const state=await readState();if(disposed)return;
   const entry={started,ended:now(),before,after:readDom(),native:select(state)};
   if(records.length<limit)records.push(entry);else dropped++;
  }catch(error){if(!disposed){if(records.length<limit)records.push({started,ended:now(),before,failed:true});else dropped++;}}
  finally{if(!disposed)handle=frame(sample);}
 }
 handle=frame(sample);
 return{records,get dropped(){return dropped;},dispose(){disposed=true;if(handle!==null)cancelFrame(handle);handle=null;}};
}
module.exports={startNativeFrameTrace};
