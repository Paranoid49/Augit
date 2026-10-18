// 审计分别记录驱动等待和宿主呈现，不能把点击返回时间当作产品延迟。
const {performance}=require('node:perf_hooks');
function createOpeningTimeline(clock=()=>performance.now()) {
 const start=clock(),events=[];
 return {
  mark(name,detail={}) {
   const milliseconds=clock()-start;
   if(events.length&&milliseconds<events.at(-1).milliseconds)throw Error('开窗计时不得倒退');
   const event={name,milliseconds,...detail};events.push(event);return event;
  },
  snapshot() {return events.map(event=>({...event}));},
 };
}
module.exports={createOpeningTimeline};
