// 验证空类别发现不误判、不吞读取失败，并释放追踪流和调试会话。
const {test}=require('node:test'),assert=require('node:assert/strict');
const fs=require('node:fs/promises'),os=require('node:os'),path=require('node:path');
const {traceLcdText}=require('./trace-lcd-text.cjs');
async function exercise(failRead) {
 const directory=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-lcd-test-'));
 const calls=[];let complete;
 const client={once:(name,fn)=>{complete=fn;},detach:async()=>calls.push('detach'),send:async(method,args)=>{
  calls.push(method);
  if(method==='Tracing.getCategories')return {categories:[]};
  if(method==='Tracing.start')assert.ok(args.categories.includes('disabled-by-default-cc.debug'));
  if(method==='Tracing.end')complete({stream:'stream1'});
  if(method==='IO.read'){if(failRead)throw Error('读取失败');return {data:'{"traceEvents":[]}',eof:true};}
  return {};
 }};
 const page={locator:()=>({focus:async()=>{}}),waitForTimeout:async()=>{},evaluate:async()=>{}};
 try {
  const operation=traceLcdText(page,{newBrowserCDPSession:async()=>client},directory);
  if(failRead)await assert.rejects(operation,/读取失败/);
  else {await operation;assert.deepEqual(JSON.parse(await fs.readFile(path.join(directory,'lcd-trace.json'),'utf8')),{traceEvents:[]});}
  assert.equal(calls.filter(x=>x==='Tracing.end').length,1);
  assert.deepEqual(calls.slice(-2),['IO.close','detach']);
 } finally {await fs.rm(directory,{recursive:true,force:true});}
}
test('按权威类别启动并保存原始流',()=>exercise(false));
test('读取错误仍关闭流与会话',()=>exercise(true));
