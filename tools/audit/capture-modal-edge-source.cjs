// 只读记录当前业务表面及宿主裁切；不改DOM、焦点、缩放或产品状态。
const assert=require('node:assert/strict');
const fs=require('node:fs/promises');
const path=require('node:path');
const crypto=require('node:crypto');
function sourceBox(state){
 const bounds=state?.liveBounds,rect=state?.rect;
 assert.equal(state?.active,true);
 assert.ok(bounds&&rect);
 assert.ok([bounds.x,bounds.y,bounds.width,bounds.height,rect.width,rect.height].every(Number.isSafeInteger));
 const box=[-bounds.x,-bounds.y,-bounds.x+rect.width,-bounds.y+rect.height];
 assert.ok(box[0]>=0&&box[1]>=0&&rect.width>0&&rect.height>0&&box[2]<=bounds.width&&box[3]<=bounds.height);
 return box;
}
async function captureModalEdgeSource(page,state,output){
 const box=sourceBox(state);
 const backgrounds=[];
 for(const candidate of page.context().pages()){
  if(candidate===page)continue;
  const image=await candidate.evaluate(()=>{const node=document.querySelector('img[src^="https://snapshot.augit.local/image.png"]');return node?{url:node.src,width:node.naturalWidth,height:node.naturalHeight,complete:node.complete}:null;});
  if(image)backgrounds.push({page:candidate,image});
 }
 assert.equal(backgrounds.length,1,'首次遮盖前必须只有主窗背景图');
 const background=backgrounds[0],resourceSession=await page.context().newCDPSession(background.page);
 try{
  await resourceSession.send('Page.enable');
  const tree=await resourceSession.send('Page.getResourceTree');
  const resource=await resourceSession.send('Page.getResourceContent',{frameId:tree.frameTree.frame.id,url:background.image.url});
  assert.equal(resource.base64Encoded,true);
  const bytes=Buffer.from(resource.content,'base64');
  assert.equal(crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase(),state.backgroundHash);
  await fs.writeFile(path.join(output,'edge-background.png'),bytes,{flag:'wx'});
  await fs.writeFile(path.join(output,'edge-background.json'),JSON.stringify({method:'cdp-loaded-resource-content',...background.image,sha256:state.backgroundHash},null,2)+String.fromCharCode(10),{flag:'wx'});
 }finally{await resourceSession.detach();}
 const geometry=await page.locator('.dialog').evaluate(node=>{
  const r=node.getBoundingClientRect(),style=getComputedStyle(node);
  return {rect:{x:r.x,y:r.y,width:r.width,height:r.height},dpr:devicePixelRatio,viewport:{width:innerWidth,height:innerHeight},background:style.backgroundColor,borderRadius:style.borderRadius,opacity:style.opacity,nativeWindow:node.dataset.nativeWindow};
 });
 const session=await page.context().newCDPSession(page);
 try{
  const result=await session.send('Page.captureScreenshot',{format:'png',fromSurface:true,captureBeyondViewport:false,optimizeForSpeed:true});
  const png=Buffer.from(result.data,'base64');
  const report={method:'cdp-full-surface-png',sourceBox:box,geometry,native:state,sha256:crypto.createHash('sha256').update(png).digest('hex'),limitation:'读取当前表面可能扰动捕获时序；不代表系统合成图，也不是无CDP性能证据。'};
  await fs.writeFile(path.join(output,'edge-source.png'),png,{flag:'wx'});
  await fs.writeFile(path.join(output,'edge-source.json'),JSON.stringify(report,null,2)+String.fromCharCode(10),{flag:'wx'});
 }finally{await session.detach();}
}
async function captureSettledBackground(page,state,output){
 assert.equal(state.active,true);assert.equal(state.captureCoverReady,false);assert.equal(state.captureCoverWindowVisible,false);
 assert.equal(state.backgroundImageRevision,state.backgroundLoadedRevision);
 const matches=[];
 for(const candidate of page.context().pages()){
  if(candidate===page)continue;
  const info=await candidate.evaluate(()=>{const node=document.querySelector('img[src^="https://snapshot.augit.local/image.png"]');return node?{url:node.src,complete:node.complete,width:node.naturalWidth,height:node.naturalHeight}:null;});
  if(!info)continue;
  const session=await page.context().newCDPSession(candidate);
  try{
   await session.send('Page.enable');
   const tree=await session.send('Page.getResourceTree');
   const resource=await session.send('Page.getResourceContent',{frameId:tree.frameTree.frame.id,url:info.url});
   assert.equal(resource.base64Encoded,true);
   const bytes=Buffer.from(resource.content,'base64'),sha256=crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase();
   if(sha256===state.backgroundHash)matches.push({bytes,info,sha256});
  }finally{await session.detach();}
 }
 assert.equal(matches.length,1,'必须唯一匹配宿主已加载背景哈希');
 const {bytes,info,sha256}=matches[0];assert.equal(info.complete,true);
 await fs.writeFile(path.join(output,'edge-background-settled.png'),bytes,{flag:'wx'});
 await fs.writeFile(path.join(output,'edge-background-settled.json'),JSON.stringify({method:'cdp-loaded-resource-content',...info,sha256,native:state},null,2)+String.fromCharCode(10),{flag:'wx'});
}
function verifyClientPlacement(info,state){
 assert.equal(info.stableOwnerGeometry,true);
 assert.equal(info.ownerGeometry.window,state.owner);
 assert.equal(info.ownerGeometry.processId,info.processId);
 assert.equal(info.ownerGeometry.dpi,info.dpi);
 assert.equal(state.depth,1,'本验收仅覆盖未拖动的首层窗口');
 const box=sourceBox(state),origin=info.ownerGeometry.clientOriginPhysical;
 const expected=[origin[0]+box[0],origin[1]+box[1],origin[0]+box[2],origin[1]+box[3]];
 assert.deepEqual(info.frameRectPhysical,expected,'未触及屏幕边界的首层模态必须使用客户区原点');
 return {expected,actual:info.frameRectPhysical,passed:true};
}
module.exports={sourceBox,captureModalEdgeSource,captureSettledBackground,verifyClientPlacement};
