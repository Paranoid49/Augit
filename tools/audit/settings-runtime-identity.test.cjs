const {test}=require('node:test'),assert=require('node:assert/strict');
const {verifyScriptIdentity}=require('./settings-runtime-identity.cjs');
test('脚本身份按实际页面来源匹配且拒绝相似主机和外部脚本',()=>{
 const {isRuntimeScript}=require('./settings-runtime-identity.cjs');
 for(const origin of ['https://augit.local','https://settings.augit.example']){
  assert.equal(isRuntimeScript(origin+'/src/live-data.js',origin),true);
  assert.equal(isRuntimeScript(origin+'.invalid/src/live-data.js',origin),false);
  assert.equal(isRuntimeScript(origin+'/vendor/xterm/xterm.js',origin),false);
 }
 assert.equal(isRuntimeScript('invalid','https://augit.local'),false);
});
test('执行脚本必须有同路径且相同哈希的源码',()=>{
 assert.equal(verifyScriptIdentity([{path:'/src/a.js',sha256:'a'}],{'/src/a.js':'a'}),true);
 assert.throws(()=>verifyScriptIdentity([{path:'/src/a.js',sha256:'old'}],{'/src/a.js':'new'}),/不一致/);
 assert.throws(()=>verifyScriptIdentity([{path:'/src/a.js',sha256:'a'}],{}),/不一致/);
});
test('缺少执行脚本不能以空集合判定一致',()=>assert.throws(()=>verifyScriptIdentity([],{}),/没有取得/));
test('终端字体读取字符节点，空集合不算实际字体证据',async()=>{
 const {collectTerminalFonts}=require('./settings-runtime-identity.cjs');
 const calls=[];
 const client={async send(method,args){calls.push({method,args});if(method==='DOM.querySelectorAll')return {nodeIds:[11,12]};return {fonts:args.nodeId===12?[{familyName:'JetBrains Mono',glyphCount:12,isCustomFont:true}]:[]};}};
 const result=await collectTerminalFonts(client,1);
 assert.equal(calls[0].args.selector,'.xterm-rows > div > span');
 assert.equal(result.actualFontObserved,true);assert.equal(result.samples.length,1);assert.equal(result.samples[0].nodeId,12);
 const empty=await collectTerminalFonts({send:async()=>({nodeIds:[]})},1);
 assert.equal(empty.actualFontObserved,false);assert.deepEqual(empty.samples,[]);
});
