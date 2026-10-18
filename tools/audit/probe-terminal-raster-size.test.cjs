// 探针只改变隔离实例；异常必须恢复字号，不能改写Shell输出。
const test=require('node:test'),assert=require('node:assert/strict');
const fs=require('node:fs/promises'),os=require('node:os'),path=require('node:path');
const {probeTerminalRasterSize}=require('./probe-terminal-raster-size.cjs');
function fixture(){
 const terminal={size:13,width:8,height:18,scale:1.75,lines:['Ag 0123456789']};
 return {terminal,page:{async evaluate(fn,arg){
  if(typeof arg==='number'){terminal.size=arg;return;}
  if(fn.toString().includes('requestAnimationFrame'))return;
  return {...terminal,lines:[...terminal.lines]};
 }}};
}
test('四种诊断保留网格及内容，最终恢复13号',async()=>{
 const output=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-raster-probe-test-'));
 try{const {page,terminal}=fixture(),seen=[];
  await probeTerminalRasterSize(page,output,async name=>seen.push([name,terminal.size]));
  assert.deepEqual(seen.map(x=>x[1]),[13,22/1.75,23/1.75,13]);
  assert.equal(terminal.size,13);
  const report=JSON.parse(await fs.readFile(path.join(output,'raster-size-probe.json'),'utf8'));
  assert.equal(report.reports.length,4);assert.equal(report.productChanged,false);
 }finally{await fs.rm(output,{recursive:true,force:true});}
});
test('截图失败仍恢复字号，不伪造完成报告',async()=>{
 const output=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-raster-probe-test-'));
 try{const {page,terminal}=fixture();let calls=0;
  await assert.rejects(probeTerminalRasterSize(page,output,async()=>{if(++calls===2)throw Error('截图失败');}),/截图失败/);
  assert.equal(terminal.size,13);assert.equal(terminal.width,8);assert.equal(terminal.height,18);
  await assert.rejects(fs.access(path.join(output,'raster-size-probe.json')));
 }finally{await fs.rm(output,{recursive:true,force:true});}
});
