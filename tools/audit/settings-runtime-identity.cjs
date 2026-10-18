// 区分磁盘、虚拟主机资源与实际执行脚本；读取不修改页面状态或配置。
const fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto');
const digest=data=>crypto.createHash('sha256').update(data).digest('hex');
function isRuntimeScript(url,origin) {
 try {const parsed=new URL(url);return parsed.origin===origin&&parsed.pathname.startsWith('/src/');}
 catch {return false;}
}
function verifyScriptIdentity(executed,source) {
 if(!executed.length)throw Error('没有取得实际执行的脚本');
 for(const item of executed) {
  if(!source[item.path]||item.sha256!==source[item.path])throw Error('实际执行脚本与源码不一致：'+item.path);
 }
 return true;
}
async function collectSettingsRuntimeIdentity(page,root,configuration) {
 const client=await page.context().newCDPSession(page),scripts=[];
 const expectedOrigin=new URL(page.url()).origin;
 client.on('Debugger.scriptParsed',script=>{if(isRuntimeScript(script.url,expectedOrigin))scripts.push(script);});
 try {
  await client.send('Debugger.enable');
  const source={},executed=[];
  for(const script of scripts){
   const pathname=new URL(script.url).pathname;
   source[pathname]??=digest(await fs.readFile(path.join(root,'web',pathname)));
   const result=await client.send('Debugger.getScriptSource',{scriptId:script.scriptId});
   executed.push({path:pathname,sha256:digest(result.scriptSource)});
  }
  verifyScriptIdentity(executed,source);
  const assets=await page.evaluate(async()=>{
   const result={};
   for(const url of ['/src/mockup.css','/vendor/jetbrains-mono/JetBrainsMono-Regular.woff2']){
    const response=await fetch(url,{cache:'no-store'});if(!response.ok)throw Error('资源读取失败：'+url);
    const bytes=await response.arrayBuffer();result[url]=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes)),b=>b.toString(16).padStart(2,'0')).join('');
   }
   return result;
  });
  for(const[url,hash]of Object.entries(assets))if(hash!==digest(await fs.readFile(path.join(root,'web',url))))throw Error('资源与源码不一致：'+url);
  await client.send('DOM.enable');await client.send('CSS.enable');
  const document=await client.send('DOM.getDocument'),fonts={};
  for(const selector of ['.dialog-title','[data-settings-filter]','.settings-nav','.code-line','.remote-title','.remote-empty','[data-remote-field]']){
   const{nodeId}=await client.send('DOM.querySelector',{nodeId:document.root.nodeId,selector});
   if(nodeId)fonts[selector]=(await client.send('CSS.getPlatformFontsForNode',{nodeId})).fonts;
  }
  const terminalFontSamples=await collectTerminalFonts(client,document.root.nodeId);
  const pageState=await page.evaluate(()=>({url:location.href,dpr:devicePixelRatio,width:innerWidth,height:innerHeight,
   theme:document.body.dataset.theme,typography:document.body.dataset.typographyPreview,settings:window.__augitLive?.settings,
   bodyFont:getComputedStyle(document.body).font,fontsStatus:document.fonts.status}));
  const binary=path.join(root,'src/Augit.Shell/bin',configuration,'net10.0-windows/win-x64/Augit.dll');
  return {binary:{path:binary,sha256:digest(await fs.readFile(binary))},executed,source,assets,fonts,terminalFontSamples,pageState,
   limitation:'DLL为启动器构建目标的磁盘身份；进程路径另由启动记录核对。字体列表仅覆盖实际存在的采样节点，不外推未打开页面。'};
 }finally{await client.detach();}
}
// CDP只报告节点直接文字，行容器空数组不能证明终端实际字体；采样真实字符span。
async function collectTerminalFonts(client,rootNodeId) {
 const selector='.xterm-rows > div > span';
 const {nodeIds}=await client.send('DOM.querySelectorAll',{nodeId:rootNodeId,selector});
 const samples=[];
 for(const nodeId of nodeIds.slice(0,32)) {
  const {fonts}=await client.send('CSS.getPlatformFontsForNode',{nodeId});
  if(fonts.some(font=>font.glyphCount>0))samples.push({nodeId,fonts});
 }
 return {selector,candidateCount:nodeIds.length,sampledCount:Math.min(nodeIds.length,32),samples,actualFontObserved:samples.length>0};
}
module.exports={collectSettingsRuntimeIdentity,verifyScriptIdentity,collectTerminalFonts,isRuntimeScript};
