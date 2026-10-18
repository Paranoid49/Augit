const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),os=require('node:os'),path=require('node:path');
const {analyzeTrace,analyzeDirectory}=require('./analyze-settings-close.cjs');
const stages=['window/settings-close','close-request','close-start','owner-release-start','owner-enabled','close-cloaked','close-flushed','owner-activated','owner-released','destroy-start','controller-closed','destroyed'];
const fixture=()=>({processId:123,frequency:1000,originTimestamp:100,utc:'2026-10-17T00:00:00Z',samples:stages.map((Stage,i)=>({Stage,Timestamp:100+i*2,Thread:2}))});
test('以单调时钟计算原生关闭子阶段，保留线程身份',()=>{const r=analyzeTrace(fixture());assert.equal(r.closeMilliseconds,22);assert.equal(r.controllerCloseMilliseconds,2);assert.equal(r.ownerReleaseMilliseconds,10);assert.deepEqual(r.threads,[2]);});
test('缺失销毁、倒序与无效频率不能当零耗时通过',()=>{const d=fixture();d.samples.pop();assert.throws(()=>analyzeTrace(d),/destroyed/);const reversed=fixture();reversed.samples[3].Timestamp=99;assert.throws(()=>analyzeTrace(reversed),/时间/);assert.throws(()=>analyzeTrace({...fixture(),frequency:0}),/文件头/);});
test('原生标题关闭先请求取消时，从网页确认后的请求计时',()=>{const d=fixture();d.samples.unshift({Stage:'close-request',Timestamp:100,Thread:2});assert.equal(analyzeTrace(d).closeMilliseconds,22);});
test('最后导出仍缺关闭阶段必须失败，完整旧窗不能掩盖新窗缺失',()=>{
 const dir=fs.mkdtempSync(path.join(os.tmpdir(),'Augit-close-analysis-'));
 try{
  const key='settings-123-'+ 'a'.repeat(32);
  fs.writeFileSync(path.join(dir,key+'-1.json'),JSON.stringify({...fixture(),samples:[]}));
  fs.writeFileSync(path.join(dir,key+'-2.json'),JSON.stringify(fixture()));
  assert.equal(analyzeDirectory(dir).nativeCloseEvidenceComplete,true);
  fs.writeFileSync(path.join(dir,'settings-123-'+ 'b'.repeat(32)+'-1.json'),JSON.stringify({...fixture(),samples:[]}));
  const r=analyzeDirectory(dir);assert.equal(r.nativeCloseEvidenceComplete,false);assert.equal(r.windows.length,2);assert.equal(r.fullPresentationVerified,false);
 }finally{fs.rmSync(dir,{recursive:true,force:true});}
});
