const {test}=require('node:test'),assert=require('node:assert/strict');
const{startNativeFrameTrace}=require('./trace-native-modal-frames.cjs');
const tick=async()=>{for(let i=0;i<8;i++)await Promise.resolve();};
function harness(readState,limit=2){const frames=new Map();let id=0,time=0;const trace=startNativeFrameTrace({readState,readDom:()=>({hidden:1}),frame:fn=>{frames.set(++id,fn);return id;},cancelFrame:key=>frames.delete(key),now:()=>++time,limit});return{trace,frames,async run(){const [key,fn]=frames.entries().next().value;frames.delete(key);fn();await tick();}};}
test('请求未返回前不安排下一帧，释放后记录白名单',async()=>{
 let resolve;const h=harness(()=>new Promise(r=>resolve=r));await h.run();assert.equal(h.frames.size,0);
 resolve({active:true,captureCoverTopmostChild:false,secret:'不应记录'});await tick();
 assert.equal(h.frames.size,1);assert.deepEqual(h.trace.records[0].native,{active:true,captureCoverTopmostChild:false});h.trace.dispose();assert.equal(h.frames.size,0);
});
test('记录有上限并显式统计丢弃数量',async()=>{const h=harness(async()=>({active:true}));await h.run();await h.run();await h.run();assert.equal(h.trace.records.length,2);assert.equal(h.trace.dropped,1);h.trace.dispose();});
test('释放后迟到结果不能记录或重新启动',async()=>{let resolve;const h=harness(()=>new Promise(r=>resolve=r));await h.run();h.trace.dispose();resolve({active:true});await tick();assert.equal(h.trace.records.length,0);assert.equal(h.frames.size,0);});
test('读取异常被标记且继续后续采样',async()=>{const h=harness(async()=>{throw Error('失败');});await h.run();assert.equal(h.trace.records[0].failed,true);assert.equal(h.frames.size,1);h.trace.dispose();});
