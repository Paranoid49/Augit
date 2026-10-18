// 仅在隔离审计实例改变绘制条件，逐次恢复样式，不写入业务草稿或参考设置。
const assert=require('node:assert/strict');
const fs=require('node:fs/promises');
const path=require('node:path');
async function probeTextComposition(page,browser,output,capture) {
 const client=await page.context().newCDPSession(page);
 const browserClient=await browser.newBrowserCDPSession();
 let layers=[];
 client.on('LayerTree.layerTreeDidChange',event=>{layers=event.layers||[];});
 const original=await page.evaluate(()=>({text:window.__augitLive.conflictDraft.text,
  styles:[document.documentElement,document.body,document.querySelector('.dialog:has(.conflict-page)'),...document.querySelectorAll('.scrim,.conflict-block,.conflict-line')].map(node=>node.getAttribute('style'))}));
 const reports=[];
 try {
  await client.send('LayerTree.enable');
  const environment={version:await client.send('Browser.getVersion'),gpu:await browserClient.send('SystemInfo.getInfo')};
  for(const mode of ['baseline','no-rounded-clip','opaque-root','no-scrim','opaque-text-layer','opaque-scrim','dialog-layer','square-dialog-layer','text-layer','restored']) {
   await page.evaluate(({mode,styles})=>{
    const nodes=[document.documentElement,document.body,document.querySelector('.dialog:has(.conflict-page)'),...document.querySelectorAll('.scrim,.conflict-block,.conflict-line')];
    nodes.forEach((node,i)=>styles[i]===null?node.removeAttribute('style'):node.setAttribute('style',styles[i]));
    if(mode==='no-rounded-clip'||mode==='opaque-root') {nodes[2].style.borderRadius='0';nodes[2].style.overflow='visible';}
    if(mode==='opaque-root')for(const node of nodes.slice(0,3))node.style.backgroundColor='#fff';
    if(mode==='no-scrim')for(const node of document.querySelectorAll('.scrim'))node.style.visibility='hidden';
    if(mode==='opaque-scrim')for(const node of document.querySelectorAll('.scrim'))node.style.backgroundColor='#fff';
    if(mode==='dialog-layer'||mode==='square-dialog-layer')nodes[2].style.transform='translateZ(0)';
    if(mode==='square-dialog-layer'){nodes[2].style.borderRadius='0';nodes[2].style.overflow='visible';}
    if(mode==='text-layer')for(const node of document.querySelectorAll('.conflict-block'))node.style.transform='translateZ(0)';
    if(mode==='opaque-text-layer')for(const node of document.querySelectorAll('.conflict-block')){node.style.willChange='transform';node.style.transform='translateZ(0)';}
   },{mode,styles:original.styles});
   await page.waitForFunction(()=>!window.__augitNativeBackgroundBusy&&!document.querySelector('[data-native-capture],[data-native-background-capture]'));
   await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
   const state=await page.evaluate(()=>({draft:window.__augitLive.conflictDraft.text,
    nodes:[document.documentElement,document.body,...document.querySelectorAll('.dialog:has(.conflict-page),.conflict-block,.conflict-line')].map(node=>{
     const s=getComputedStyle(node);return {tag:node.tagName,class:node.className,rect:node.getBoundingClientRect().toJSON(),
      background:s.backgroundColor,opacity:s.opacity,transform:s.transform,filter:s.filter,contain:s.contain,
      font:s.font,overflow:s.overflow,borderRadius:s.borderRadius};})}));
   assert.equal(state.draft,original.text);
   // 读取CSSStyleDeclaration后浏览器可能将缺失style序列化为空串；两者均不含声明。
   if(mode==='restored')assert.deepEqual(await page.evaluate(()=>[document.documentElement,document.body,document.querySelector('.dialog:has(.conflict-page)'),...document.querySelectorAll('.scrim,.conflict-block,.conflict-line')].map(node=>node.getAttribute('style')||null)),original.styles.map(value=>value||null));
   const compositing=[];
   for(const layer of layers) {
    let node=null;
    if(layer.backendNodeId)node=await client.send('DOM.describeNode',{backendNodeId:layer.backendNodeId}).then(result=>({name:result.node.nodeName,attributes:result.node.attributes})).catch(error=>({error:error.message}));
    compositing.push({layer,node,reasons:await client.send('LayerTree.compositingReasons',{layerId:layer.layerId}).catch(error=>({error:error.message}))});
   }
   await capture('text-'+mode+'.png');
   reports.push({mode,...state,layers:compositing});
  }
  await fs.writeFile(path.join(output,'text-composition.json'),JSON.stringify({environment,reports},null,2)+String.fromCharCode(10));
 } finally {
  try {await page.evaluate(styles=>[document.documentElement,document.body,document.querySelector('.dialog:has(.conflict-page)'),...document.querySelectorAll('.scrim,.conflict-block,.conflict-line')].forEach((node,i)=>styles[i]===null?node.removeAttribute('style'):node.setAttribute('style',styles[i])),original.styles);}
  finally {await client.detach();await browserClient.detach();}
 }
}
module.exports={probeTextComposition};
