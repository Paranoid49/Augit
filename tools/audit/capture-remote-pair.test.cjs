// 行为模式不得伪造屏幕证据，截图模式继续执行前景和指针门禁。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync(require('node:path').join(__dirname,'capture-remote-pair.cjs'),'utf8');
const start=source.indexOf('   if(!behaviorOnly){');
const block=source.slice(start,source.indexOf('   reports.push',start));
const identityBlock=source.slice(source.indexOf('  const mapped='),source.indexOf('  const checkpoint='));
test('映射身份仅接受本实例的两项实际程序集',async()=>{
 for(const [pid,images,accepted] of [[42,['Augit.dll','Augit.Infrastructure.dll'],true],[43,['Augit.dll','Augit.Infrastructure.dll'],false],[42,['Augit.dll'],false]]){
  const writes=[],calls=[];const run=()=>vm.runInNewContext('(async()=>{'+identityBlock+'})()',{pid:42,process:{env:{}},JSON,assert,path:require('node:path'),output:'evidence',fs:{writeFile:async(...args)=>writes.push(args)},execute:async(...args)=>{calls.push(args);return {stdout:JSON.stringify({pid,mappedImages:images.map(name=>({name}))})};}});
  if(accepted){await run();assert.equal(writes.length,1);assert.equal(calls[0][1][2],'42');}
  else {await assert.rejects(run);assert.equal(writes.length,0);}
 }
});
for(const behaviorOnly of [true,false])test(behaviorOnly?'行为模式不采集屏幕':'截图模式保留严格门禁',async()=>{
 const calls=[];
 await vm.runInNewContext('(async()=>{'+block+'})()',{behaviorOnly,name:'actual-define',
  checkpoint:async name=>calls.push({checkpoint:name}),execute:async(file,args)=>calls.push({file,args}),
  shell:{pid:123},modal:{window:456},path:require('node:path'),output:'evidence'});
 if(behaviorOnly)assert.deepEqual(calls,[]);
 else {assert.equal(calls.length,2);assert.equal(calls[0].checkpoint,'actual-define');
  assert.ok(calls[1].args.includes('-ExpectedProcessId'));assert.ok(calls[1].args.includes('-WindowHandle'));
  const index=calls[1].args.indexOf('-PointerClearance');assert.ok(index>=0);assert.equal(calls[1].args[index+1],'96');}
});
