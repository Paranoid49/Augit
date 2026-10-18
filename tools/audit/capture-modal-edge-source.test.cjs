const {test}=require('node:test');
const assert=require('node:assert/strict');
const {sourceBox}=require('./capture-modal-edge-source.cjs');
test('末态背景按宿主哈希识别，不能误用复用遮盖图，失败也释放会话',async()=>{
 const fs=require('node:fs/promises'),os=require('node:os'),path=require('node:path'),crypto=require('node:crypto');
 const {captureSettledBackground}=require('./capture-modal-edge-source.cjs');
 for(const duplicates of [false,true]){
  const folder=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-settled-background-test-'));
  const target=Buffer.from('当前背景'),cover=Buffer.from('旧遮盖'),sessions=[];
  const candidates=[target,duplicates?target:cover].map(bytes=>({bytes,evaluate:async()=>({url:'https://snapshot.augit.local/image.png?v=4',complete:true,width:20,height:20})}));
  const page={context:()=>({pages:()=>[page,...candidates],newCDPSession:async candidate=>{const session={detached:false,detach:async()=>session.detached=true,send:async method=>method==='Page.enable'?{}:method==='Page.getResourceTree'?{frameTree:{frame:{id:'main'}}}:{base64Encoded:true,content:candidate.bytes.toString('base64')}};sessions.push(session);return session;}})};
  const state={active:true,captureCoverReady:false,captureCoverWindowVisible:false,backgroundImageRevision:4,backgroundLoadedRevision:4,backgroundHash:crypto.createHash('sha256').update(target).digest('hex').toUpperCase()};
  try{
   if(duplicates)await assert.rejects(captureSettledBackground(page,state,folder));
   else{await captureSettledBackground(page,state,folder);assert.deepEqual(await fs.readFile(path.join(folder,'edge-background-settled.png')),target);}
   assert.equal(sessions.length,2);assert.ok(sessions.every(s=>s.detached));
   await assert.rejects(captureSettledBackground(page,{...state,backgroundLoadedRevision:3},folder));
  }finally{await fs.rm(folder,{recursive:true,force:true});}
 }
});
test('最大化外框负偏移不能替代客户区原点，普通窗口也使用实测原点',()=>{
 const {verifyClientPlacement}=require('./capture-modal-edge-source.cjs');
 const state={active:true,depth:1,owner:10,liveBounds:{x:-1148,y:-733,width:2880,height:1716},rect:{width:585,height:252}};
 const info={stableOwnerGeometry:true,processId:20,dpi:168,ownerGeometry:{window:10,processId:20,dpi:168,clientOriginPhysical:[0,0]},frameRectPhysical:[1136,721,1721,973]};
 assert.throws(()=>verifyClientPlacement(info,state));
 assert.equal(verifyClientPlacement({...info,frameRectPhysical:[1148,733,1733,985]},state).passed,true);
 assert.equal(verifyClientPlacement({...info,ownerGeometry:{...info.ownerGeometry,clientOriginPhysical:[100,200]},frameRectPhysical:[1248,933,1833,1185]},state).passed,true);
 assert.throws(()=>verifyClientPlacement({...info,stableOwnerGeometry:false},state));
});
test('以宿主实际物理边界定位源图，不对DOM分数坐标配准',()=>{
 assert.deepEqual(sourceBox({active:true,liveBounds:{x:-1148,y:-733,width:2880,height:1716},rect:{width:585,height:252}}),[1148,733,1733,985]);
});
test('拒绝失活、缺少身份尺寸、分数偏移和越界',()=>{
 const state={active:true,liveBounds:{x:-10,y:-20,width:100,height:100},rect:{width:50,height:40}};
 for(const value of [{...state,active:false},{...state,rect:null},{...state,liveBounds:{...state.liveBounds,x:-10.5}},{...state,rect:{width:100,height:40}}])assert.throws(()=>sourceBox(value));
});
test('读取已加载背景验证哈希，失败和成功均释放CDP会话',async()=>{
 const fs=require('node:fs/promises'),os=require('node:os'),path=require('node:path'),crypto=require('node:crypto');
 const {captureModalEdgeSource}=require('./capture-modal-edge-source.cjs');
 for(const mismatch of [false,true]){
  const directory=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-edge-capture-test-'));
  const backgroundBytes=Buffer.from('背景资源'),sourceBytes=Buffer.from('业务资源'),sessions=[];
  const background={evaluate:async()=>({url:'https://snapshot.augit.local/image.png',width:20,height:20,complete:true})};
  const state={active:true,backgroundHash:mismatch?'wrong':crypto.createHash('sha256').update(backgroundBytes).digest('hex').toUpperCase(),liveBounds:{x:-1,y:-2,width:20,height:20},rect:{width:10,height:10}};
  const page={locator:()=>({evaluate:async()=>({dpr:1})}),context:()=>({pages:()=>[page,background],newCDPSession:async target=>{
   const session={detached:false,detach:async()=>session.detached=true,send:async method=>{
    if(method==='Page.enable')return {};
    if(method==='Page.getResourceTree')return {frameTree:{frame:{id:'frame'}}};
    if(method==='Page.getResourceContent'){assert.equal(target,background);return {base64Encoded:true,content:backgroundBytes.toString('base64')};}
    assert.equal(method,'Page.captureScreenshot');assert.equal(target,page);return {data:sourceBytes.toString('base64')};
   }};sessions.push(session);return session;
  }})};
  try{
   if(mismatch)await assert.rejects(captureModalEdgeSource(page,state,directory));
   else{await captureModalEdgeSource(page,state,directory);assert.deepEqual(await fs.readFile(path.join(directory,'edge-background.png')),backgroundBytes);assert.deepEqual(await fs.readFile(path.join(directory,'edge-source.png')),sourceBytes);}
   assert.ok(sessions.every(session=>session.detached));assert.equal(sessions.length,mismatch?1:2);
  }finally{await fs.rm(directory,{recursive:true,force:true});}
 }
});
