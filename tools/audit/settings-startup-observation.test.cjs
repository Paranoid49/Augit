// 性能采样只收集已有事件，不插入等待或改变页面生命周期。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('tools/audit/independent-settings.cjs','utf8');
const start=source.indexOf('window.__settingsLongTasks=[];');
const end=source.indexOf('const b=chrome.webview',start);
test('长任务观察记录原始时间且不支持时安全跳过',()=>{
 for(const supported of [true,false]){
  let observed,callback;
  class Observer {
   static supportedEntryTypes=supported?['longtask']:[];
   constructor(value){callback=value;}
   observe(options){observed=options;}
  }
  const context={window:{},PerformanceObserver:Observer};
  vm.createContext(context);vm.runInContext(source.slice(start,end),context);
  assert.equal(Boolean(callback),supported);
  if(supported){assert.equal(observed.type,'longtask');assert.equal(observed.buffered,true);callback({getEntries:()=>[{startTime:100,duration:250,name:'self'}]});}
  assert.equal(JSON.stringify(context.window.__settingsLongTasks),supported?'[{"start":100,"duration":250,"name":"self"}]':'[]');
 }
});
