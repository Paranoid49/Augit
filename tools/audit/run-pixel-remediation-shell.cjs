// 本次真实Win32/WebView2审计：配置和profile隔离；每个实例在finally停止并清理。
const assert=require('node:assert/strict');
const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os'),net=require('node:net');
const {spawn,execFile}=require('node:child_process'),{promisify}=require('node:util');
const execute=promisify(execFile);
async function verifyModalRaster(png,geometry) {
 const child=spawn('python',['-B','tools/audit/verify-modal-raster.py',JSON.stringify(geometry)],{windowsHide:true,env:{...process.env,PYTHONUTF8:'1'},stdio:['pipe','pipe','pipe']});
 let output='',error='';child.stdout.on('data',chunk=>output+=chunk);child.stderr.on('data',chunk=>error+=chunk);
 const finished=new Promise((resolve,reject)=>{child.on('error',reject);child.on('close',code=>code===0?resolve():reject(Error(error||`客户区像素核验退出${code}`)));});
 child.stdin.end(png);await finished;return JSON.parse(output);
}
const modulePath=process.argv[2]||'C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright';
const binary=path.resolve(process.argv[3]||'src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe');
const output=path.resolve(process.argv[4]||'artifacts/pixel-restoration-2026-10-13/shell');
const {chromium}=require(modulePath);
const {measure:measureMemory}=require('./modal-process-memory.cjs');
async function freePort(){const server=net.createServer();await new Promise(r=>server.listen(0,'127.0.0.1',r));const port=server.address().port;await new Promise(r=>server.close(r));return port;}
const rect=node=>{const r=node.getBoundingClientRect();return{x:r.x,y:r.y,w:r.width,h:r.height};};
(async()=>{
 const records=[];await fs.mkdir(output,{recursive:true});
 for(const theme of process.argv.includes('--inspect')||process.argv.includes('--single')?['Light']:['Light','Dark']) for(const size of process.argv.includes('--large-only')?[40]:process.argv.includes('--inspect')||process.argv.includes('--single')?[12]:[12,14,40]) {
  const audit=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-pixel-fix-')),port=await freePort();let shell,browser;
  const record={theme,size,evidence:'真实Win32/WebView2，非完整PyCharm同态配对'};
  try {
   const settings=path.join(audit,'settings.json');
   await fs.writeFile(settings,JSON.stringify({appearanceVersion:1,theme,textFontFamily:'Microsoft YaHei UI',textFontSize:size,monospaceFontFamily:'JetBrains Mono',fontSize:13}));
   // WebView2初始化依赖主窗口实际显示；审计外壳可见，驱动和清理进程保持隐藏。
   const fixture=process.argv.includes('--reference-fixture')?'D:/JetBrains/PyCharm-2026.2.1-reference/fixture':process.cwd();
   const shellArgs=['--web-root',process.argv.includes('--published')?path.join(path.dirname(binary),'web'):path.resolve('web'),'--workspace',fixture,'--scene','commit-changes','--open',process.argv.includes('--reference-fixture')?'notes.txt':'.gitignore','--no-session-restore','--settings-file',settings,'--user-data-folder',path.join(audit,'profile'),'--width',size===40?'1024':'1640','--height',size===40?'640':'900','--browser-args',`--remote-debugging-port=${port}`];
   record.workspace=fixture;
   shell=spawn(process.argv.includes('--dotnet')?'dotnet.exe':binary,process.argv.includes('--dotnet')?[path.join(path.dirname(binary),'Augit.dll'),...shellArgs]:shellArgs,{cwd:process.cwd(),windowsHide:false,stdio:'ignore'});
   record.pid=shell.pid;record.port=port;console.log(`AUDIT_PID=${shell.pid} PORT=${port} TEMP=${audit} THEME=${theme} UI=${size}`);
   for(let retry=0;retry<120;retry++){try{browser=await chromium.connectOverCDP(`http://127.0.0.1:${port}`);break;}catch{if(shell.exitCode!==null)throw Error(`审计外壳退出：${shell.exitCode}`);await new Promise(r=>setTimeout(r,250));}}
   assert.ok(browser,'真实外壳必须成功连接');
   const context=browser.contexts()[0],page=context.pages()[0]||await context.waitForEvent('page');
   async function verifySnapshotSizing(label) {
    const images=[];
    for(const candidate of context.pages().filter(candidate=>candidate!==page)) {
     const image=await candidate.evaluate(async()=>{
      const img=document.querySelector('img[src^="https://snapshot.augit.local/image.png"]');
      if(!img)return null;
      await img.decode();
      const rect=img.getBoundingClientRect();
      return {revision:img.dataset.revision,width:rect.width,height:rect.height,scale:window.snapshotCaptureScale,naturalWidth:img.naturalWidth,naturalHeight:img.naturalHeight};
     });
     if(!image)continue;
     assert.ok(Math.abs(image.width*image.scale-image.naturalWidth)<0.02,`${label}快照宽度不可重采样`);
     assert.ok(Math.abs(image.height*image.scale-image.naturalHeight)<0.02,`${label}快照高度不可重采样`);
     images.push(image);
    }
    assert.ok(images.length>0,`${label}必须检查真实背景快照`);
    (record.snapshotSizing??=[]).push({label,images});
   }
   await page.waitForURL(/augit/i);await page.waitForFunction(()=>window.__augitReady&&window.__augitSettingsReady&&window.__augitGitReady);
   assert.equal(await page.evaluate(()=>!!window.__augitLive),true);
   // 新旧配置迁移不能悄悄改变本轮指定条件；必须核对实际渲染后再执行验收。
   record.appearance=await page.evaluate(()=>({theme:window.__augitLive.settings.theme,uiSize:getComputedStyle(document.documentElement).fontSize,codeFamily:window.__augitLive.settings.monospaceFontFamily,codeSize:window.__augitLive.settings.codeFontSize,dpr:devicePixelRatio}));
   assert.equal(record.appearance.theme,theme);
   assert.equal(record.appearance.uiSize,`${size}px`);
   assert.equal(record.appearance.codeFamily,'JetBrains Mono');
   assert.equal(record.appearance.codeSize,13);
   // 畸形窗口请求不能从async void逃逸并终止真实外壳，随后合法请求仍能应答。
   await page.evaluate(()=>{for(const message of ['{',null,[],{id:800001,method:'window/modal-state',params:null},{id:800002,method:'window/modal-present',params:{ancestors:[null]}}]) chrome.webview.postMessage(message);});
   const stateAfterInvalid=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
   assert.equal(stateAfterInvalid.active,false);
   if(size===12&&theme==='Light') {
    // 直接并发请求绕过网页串行器，宿主仍须维持真实所有者链且关闭淘汰旧捕获。
    record.protocolRaces=await page.evaluate(async()=>{
     const {invoke}=await import('/src/bridge.js');
     const params=key=>({key,title:'并发回归',ancestors:key==='protocol-child'?['protocol-parent']:[],x:60,y:60,width:400,height:200});
     await Promise.all([invoke('window/modal-present',params('protocol-parent')),invoke('window/modal-present',params('protocol-child'))]);
     const nested=await invoke('window/modal-state');
     await invoke('window/modal-close');
     const requests=Array.from({length:6},(_,i)=>invoke('window/modal-present',params('obsolete-'+i)));
     const close=invoke('window/modal-close');
     await Promise.all([...requests,close]);
     const closed=await invoke('window/modal-state');
     return {nested,closed};
    });
    assert.equal(record.protocolRaces.nested.depth,2);
    assert.equal(record.protocolRaces.nested.snapshotCount,2);
    assert.notEqual(record.protocolRaces.nested.ownerWindow,record.protocolRaces.nested.owner);
    assert.equal(record.protocolRaces.closed.depth,0);
    assert.equal(record.protocolRaces.closed.snapshotCount,0);
    assert.equal(record.protocolRaces.closed.ownerEnabled,true);
    record.coverCloseRace=await page.evaluate(async()=>{
     const {invoke}=await import('/src/bridge.js');
     await invoke('window/modal-present',{key:'cover-close',title:'遮盖关闭回归',ancestors:[],x:60,y:60,width:400,height:200});
     const pending=invoke('window/modal-cover');
     await invoke('window/modal-close');
     await pending;
     return invoke('window/modal-state');
    });
    assert.equal(record.coverCloseRace.active,false);
    assert.equal(record.coverCloseRace.captureCoverReady,false);
    assert.equal(record.coverCloseRace.captureCoverBytes,0);
    assert.equal(record.coverCloseRace.snapshotCount,0);
   }
   await page.waitForSelector('.code-line > span:last-child');
   await page.evaluate(async()=>{await document.fonts.ready;await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));});
   const fontClient=await context.newCDPSession(page);
   try {
    await fontClient.send('DOM.enable');await fontClient.send('CSS.enable');
    const {root}=await fontClient.send('DOM.getDocument');
    const {nodeId}=await fontClient.send('DOM.querySelector',{nodeId:root.nodeId,selector:'.code-line > span:last-child'});
    record.actualCodeFonts=(await fontClient.send('CSS.getPlatformFontsForNode',{nodeId})).fonts;
    assert.ok(record.actualCodeFonts.some(font=>font.familyName==='JetBrains Mono'&&font.isCustomFont&&font.glyphCount>0),'真实WebView2必须从随包字体绘制正文');
   } finally {await fontClient.detach();}
   record.codeGeometry=await page.locator('.code-line').first().evaluate(row=>{const probe=document.createElement('i');probe.style.cssText='display:inline-block;width:0;height:0;vertical-align:baseline';row.lastElementChild.append(probe);const result={height:row.getBoundingClientRect().height,baseline:probe.getBoundingClientRect().top-row.getBoundingClientRect().top};probe.remove();return result;});
   assert.ok(Math.abs(record.codeGeometry.height-22)<.01,'真实WebView2的JetBrains Mono13行盒');
   assert.ok(Math.abs(record.codeGeometry.baseline-16)<.01,'真实WebView2的JetBrains Mono13基线: '+JSON.stringify(record.codeGeometry));
   record.viewport=await page.evaluate(()=>({w:innerWidth,h:innerHeight,dpr:devicePixelRatio,ui:getComputedStyle(document.body).fontSize,font:getComputedStyle(document.body).fontFamily}));
   record.group=await page.locator('.check-group-row .fake-check').first().evaluate(rect);
   record.file=await page.locator('.change-file-row .fake-check').first().evaluate(rect);
   const scale=Math.max(.75,Math.round(size/12*4)/4);
   record.treeOrigin=await page.locator('.changes-list').evaluate(node=>{const r=node.getBoundingClientRect();return{x:r.x,y:r.y,w:r.width,h:r.height,scrollLeft:node.scrollLeft};});
   record.treeArrow=await page.locator('.check-group-row .change-chevron svg').first().evaluate(rect);
   assert.ok(Math.abs(record.treeArrow.w-Math.round(16*scale))<.01,'展开画布不能缩成8px');
   assert.ok(Math.abs(record.group.x-record.treeOrigin.x+record.treeOrigin.scrollLeft-Math.round(12*scale)-({12:19,14:24,40:62}[size]))<.01,'Tree.border加首级renderer偏移：'+JSON.stringify({group:record.group,origin:record.treeOrigin}));
   assert.ok(Math.abs(record.file.x-record.group.x-({12:19,14:24,40:62}[size]))<.6,'ClassicPainter的图标半宽下限决定一级缩进');
   record.row=await page.locator('.change-file-row').first().evaluate(rect);assert.ok(Math.abs(record.row.h-Math.round(24*scale))<.6);
   if(process.argv.includes('--reference-fixture')) {
    assert.equal(await page.locator('.check-group-row .commit-meta').first().innerText(),'1 file','分组计数遵循参考单复数');
    assert.equal(await page.locator('.change-file-row .tree-path').first().innerText(),'fixture','平铺根文件显示仓库名');
   }
   // 单独保存Changes原始栅格，矩形和实际蓝框分开核对，不缩放截图来凑差值。
   await page.locator('.changes-list').screenshot({path:path.join(output,`changes-${theme}-${size}.png`),timeout:5000});
   if (size===12) await page.screenshot({path:path.join(output,`commit-${theme}-${size}.png`),timeout:5000});
   if(size===40){
    await page.locator('.rail-button[data-tool="history"]').click();
    await page.evaluate(()=>applyTypography({uiSize:40}));
    record.editor=await page.locator('.editor-content').evaluate(rect);assert.ok(record.editor.h>=23);
    record.list=await page.locator('.changes-list').evaluate(rect);assert.ok(record.list.h>=78);
    record.buttons=[];
    const buttons=await page.locator('.commit-actions > :is(.primary-button,.secondary-button)').all();assert.equal(buttons.length,2,'两个真实提交动作必须存在');
    for(const button of buttons){
     await button.scrollIntoViewIfNeeded();const r=await button.evaluate(rect),host=await page.locator('.side-tool').evaluate(rect);
     assert.ok(r.y>=host.y-.6&&r.y+r.h<=host.y+host.h+.6,'真实提交动作可完整进入工具区');record.buttons.push(r);
    }
   }
   // 控件栅格证据使用独立24px画布；裁切仅保存未选框，不缩放或重采样图片。
   await page.evaluate(()=>{const node=document.createElement('span');node.className='fake-check';node.id='audit-checkbox';node.style.cssText='position:fixed;left:10px;top:10px;z-index:9999';document.querySelector('.augit-window').append(node);});
   await page.locator('#audit-checkbox').screenshot({path:path.join(output,`checkbox-${theme}-${size}.png`),timeout:5000});
   record.checkbox=await page.locator('#audit-checkbox').evaluate(node=>({canvas:node.getBoundingClientRect().width,visible:getComputedStyle(node,'::before').width,edge:getComputedStyle(node,'::before').boxShadow}));
   await page.locator('#audit-checkbox').evaluate(n=>n.remove());
   // 已选聚焦保持16px内框并单独绘制22px外环；半选与原生控件共用同一几何。
   await page.evaluate(()=>{const node=document.createElement('button');node.className='fake-check checked';node.id='audit-checkbox-focus';node.style.cssText='position:fixed;left:10px;top:10px;z-index:9999';document.querySelector('.augit-window').append(node);});
   await page.keyboard.press('Tab');await page.locator('#audit-checkbox-focus').focus();
   record.checkboxFocus=await page.locator('#audit-checkbox-focus').evaluate(node=>({inner:getComputedStyle(node,'::before').width,outer:getComputedStyle(node).getPropertyValue('--augit-check-selected-focus'),image:getComputedStyle(node).backgroundImage}));
   assert.equal(parseFloat(record.checkboxFocus.inner),Math.round(16*scale));
   assert.equal(parseFloat(record.checkboxFocus.outer),Math.round(22*scale));
   assert.notEqual(record.checkboxFocus.image,'none');
   await page.locator('#audit-checkbox-focus').screenshot({path:path.join(output,`checkbox-focused-${theme}-${size}.png`),timeout:5000});
   await page.locator('#audit-checkbox-focus').evaluate(n=>n.remove());
   if(process.argv.includes('--memory'))record.memoryBefore=await measureMemory(shell.pid);
   const modalStarted=performance.now();
   await page.locator('.titlebar [aria-label="Settings"]').click();
   const dialog=page.locator('.settings-window .dialog');record.settings=await dialog.evaluate(rect);
   record.settingsScrollBefore=await dialog.evaluate(node=>Object.fromEntries(['.dialog-body','.settings-layout','.settings-nav','.settings-page','.settings-page-header'].map(selector=>{const element=node.querySelector(selector),style=getComputedStyle(element),box=element.getBoundingClientRect();return[selector,{height:box.height,top:box.top,scrollHeight:element.scrollHeight,scrollTop:element.scrollTop,overflow:style.overflow,display:style.display,grid:style.gridTemplateRows}];})));
   assert.ok(Math.abs(record.settings.w-Math.min(1040,record.viewport.w-80))<1);
   assert.ok((await page.locator('.settings-window [data-settings-filter]').evaluate(rect)).h>=size+7);
   const settingsPage=await page.locator('.settings-window .settings-page').evaluate(rect),settingsHeader=await page.locator('.settings-window .settings-page-header').evaluate(rect);
   assert.ok(settingsHeader.x>=settingsPage.x&&settingsHeader.x+settingsHeader.w<=settingsPage.x+settingsPage.w+.6,'面包屑与导航必须留在正文列内');
   record.settingsLabels=await page.locator('.settings-window .form-grid > label').evaluateAll(nodes=>nodes.filter(node=>node.textContent.trim()).map(node=>{const range=document.createRange();range.selectNodeContents(node);const text=range.getBoundingClientRect(),box=node.getBoundingClientRect();return{label:node.textContent,box:{left:box.left,right:box.right},text:{left:text.left,right:text.right}};}));
   for(const label of record.settingsLabels) assert.ok(label.text.left>=label.box.left-.6&&label.text.right<=label.box.right+.6,'设置标签墨迹不越过列边界：'+JSON.stringify(label));
   if(size===40) {
    const number=await page.locator('.settings-window .font-setting input').first().evaluate(node=>{const style=getComputedStyle(node),canvas=document.createElement('canvas').getContext('2d');canvas.font=style.font;return{width:node.getBoundingClientRect().width,required:canvas.measureText('40').width+parseFloat(style.paddingLeft)+parseFloat(style.paddingRight)+parseFloat(style.borderLeftWidth)+parseFloat(style.borderRightWidth)+16};});
    assert.ok(number.width>=number.required,'大字号数字控件须容纳40与步进按钮：'+JSON.stringify(number));
   }
   await page.waitForFunction(()=>Array.isArray(window.__augitLive.fontFamilies));
   record.fonts=await page.evaluate(()=>({jetBrainsMono:window.__augitLive.fontFamilies.includes('JetBrains Mono'),codeFamily:window.__augitLive.settings.monospaceFontFamily,codeSize:window.__augitLive.settings.codeFontSize}));
   assert.equal(await page.locator('.settings-window .settings-layout').count(),1,'截图必须实际包含Settings正文');
   if (await page.evaluate(()=>!!window.__augitNativeModalAvailable)) {
    await page.waitForFunction(()=>window.__augitNativeModalActive&&!document.querySelector('.dialog[data-native-capture]'));
    record.nativeModal=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
    assert.equal(record.nativeModal.active,true);assert.equal(record.nativeModal.depth,1);assert.notEqual(record.nativeModal.window,record.nativeModal.owner);
    assert.equal(record.nativeModal.ownerEnabled,false);assert.equal(record.nativeModal.visible,true);assert.equal(record.nativeModal.ownerWindow,record.nativeModal.owner);assert.equal(record.nativeModal.snapshotCount,1);
    assert.equal(record.nativeModal.parentWindow,record.nativeModal.window,'真实控制器必须重挂到模态窗口');
    assert.ok(Math.abs(record.nativeModal.liveBounds.width-record.viewport.w*record.viewport.dpr)<=record.viewport.dpr,'保留原视口，允许innerWidth的逻辑像素取整');
    record.modalOpenMilliseconds=Math.round(performance.now()-modalStarted);
    if(process.argv.includes('--memory'))record.memoryModal=await measureMemory(shell.pid);
   }
   assert.equal(await page.locator('.settings-window .dialog-header').innerText(), 'Settings — Augit');
   if(process.argv.includes('--cold-refresh')) {
    await page.waitForFunction(()=>!window.__augitNativeBackgroundBusy);
    const prior=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
    assert.equal(prior.captureCoverBytes,0,'首次测量前不得已有遮盖PNG');
    const started=performance.now();
    await page.evaluate(()=>{
     const marker=document.createElement('span');marker.id='audit-cold-refresh';marker.textContent='首次背景刷新';
     marker.style.cssText='position:absolute;left:600px;top:4px;color:#ff0088';document.querySelector('.titlebar').append(marker);
    });
    let current;
    for(let retry=0;retry<250;retry++) {
     current=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
     if(current.backgroundImageRevision>prior.backgroundImageRevision&&current.backgroundLoadedRevision===current.backgroundImageRevision&&!current.captureCoverReady&&!await page.evaluate(()=>window.__augitNativeBackgroundBusy))break;
     await page.waitForTimeout(20);
    }
    assert.ok(current.backgroundImageRevision>prior.backgroundImageRevision);
    assert.equal(current.backgroundLoadedRevision,current.backgroundImageRevision);
    assert.equal(current.captureCoverReady,false);
    assert.ok(current.captureCoverTiming.controllerMilliseconds>0,'首次必须包含控制器创建');
    record.coldRefresh={settledObservedMilliseconds:performance.now()-started,pollingIntervalMilliseconds:20,...current.captureCoverTiming};
    await fs.writeFile(path.join(output,'cold-refresh.json'),JSON.stringify(record.coldRefresh,null,2)+'\n');
    await page.locator('#audit-cold-refresh').evaluate(node=>node.remove());
    await page.waitForFunction(()=>!window.__augitNativeBackgroundBusy);
   }
   if(process.argv.includes('--cover-pixels')) {
    // 三色标记仅用于验证遮盖控制器的原始栅格；不作为系统合成前景无闪烁证据。
    const marker=await dialog.evaluate(node=>{
     const mark=document.createElement('span');mark.id='audit-cover-marker';mark.style.cssText='position:absolute;left:8px;top:8px;width:18px;height:6px;z-index:99999;display:flex';
     for(const color of ['#e91159','#0de3ad','#472bf1']){const tile=document.createElement('i');tile.style.cssText=`flex:0 0 6px;height:6px;background:${color}`;mark.append(tile);}node.append(mark);
     const bounds=mark.getBoundingClientRect(),surface=node.getBoundingClientRect();
     return {x:(bounds.x-surface.x)*devicePixelRatio,y:(bounds.y-surface.y)*devicePixelRatio,dpr:devicePixelRatio};
    });
    await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
    // CDP新页面发现可能迟于宿主加载回调，先登记已存在的主背景目标。
    for(let retry=0;retry<50&&context.pages().length<2;retry++)await page.waitForTimeout(20);
    assert.equal(context.pages().length,2);
    const priorPages=new Set(context.pages());
    try {
     const coverState=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-cover');});
     assert.equal(coverState.captureCoverReady,true);
     assert.ok(coverState.captureCoverWindow>0,'遮盖必须拥有独立子窗');
     assert.equal(coverState.captureCoverWindowVisible,true);
     const covers=context.pages().filter(candidate=>!priorPages.has(candidate));
     assert.equal(covers.length,1,'本次遮盖必须只有一个新控制器');
     await dialog.evaluate(node=>node.dataset.nativeBackgroundCapture='true');
     await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
     assert.equal(await dialog.evaluate(node=>getComputedStyle(node).opacity),'0');
     const png=await covers[0].screenshot({timeout:5000});
     await fs.writeFile(path.join(output,`cover-${theme}-${size}.png`),png);
     await fs.writeFile(path.join(output,'cover-marker.json'),JSON.stringify(marker,null,2));
     record.coverRaster=await verifyModalRaster(png,marker);
     await dialog.evaluate(node=>delete node.dataset.nativeBackgroundCapture);
     await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');await invoke('window/modal-uncover');});
     await dialog.evaluate(node=>node.querySelector('#audit-cover-marker').style.left='28px');
     await covers[0].evaluate(()=>window.__auditOldImage=document.querySelector('img'));
     await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
     const reused=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-cover');});
     assert.equal(reused.captureCoverTiming.controllerMilliseconds,0);
     assert.equal(await covers[0].evaluate(()=>document.querySelector('img').onload===null),true,'已加载图片不能保留引用前一张图片的闭包');
     record.retiredImage=await covers[0].evaluate(()=>{
      const old=window.__auditOldImage;delete window.__auditOldImage;
      return {connected:old.isConnected,hasSource:old.hasAttribute('src'),hasLoadHandler:old.onload!==null};
     });
     assert.deepEqual(record.retiredImage,{connected:false,hasSource:false,hasLoadHandler:false},'替换后旧图须主动释放资源引用');
     await dialog.evaluate(node=>node.dataset.nativeBackgroundCapture='true');
     const updated=await covers[0].screenshot({timeout:5000});
     record.reusedCoverRaster=await verifyModalRaster(updated,{...marker,x:marker.x+20*marker.dpr});
     await fs.writeFile(path.join(output,`cover-reused-${theme}-${size}.png`),updated);
    } finally {
     await dialog.evaluate(node=>{delete node.dataset.nativeBackgroundCapture;node.querySelector('#audit-cover-marker')?.remove();});
     await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
     await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');await invoke('window/modal-uncover');});
    }
   }
   {
    // 已打开表单仅改变尺寸时不需重新捕获所有者，不能隐藏正在输入的表单。
    record.modalResize=await page.evaluate(async()=>{
     const dialog=document.querySelector('.settings-window .dialog');
     const input=dialog.querySelector('[data-settings-filter]');
     input.focus();
     const originalWidth=dialog.style.width,originalMaxWidth=dialog.style.maxWidth;
     const hidden=[];
     const observer=new MutationObserver(records=>{
      for(const mutation of records) if(mutation.oldValue==='true'||dialog.dataset.nativeCapture==='true') hidden.push(performance.now());
     });
     observer.observe(dialog,{attributes:true,attributeFilter:['data-native-capture'],attributeOldValue:true});
     try {
      const {invoke}=await import('/src/bridge.js');
      const before=await invoke('window/modal-state');
      dialog.style.width=(dialog.getBoundingClientRect().width-40)+'px';
      dialog.style.maxWidth=dialog.style.width;
      let after;
      for(let retry=0;retry<100;retry++) {
       await new Promise(resolve=>setTimeout(resolve,20));
       after=await invoke('window/modal-state');
       if(after.rect.width!==before.rect.width&&!dialog.hasAttribute('data-native-capture'))break;
      }
      return {before:before.rect.width,after:after.rect.width,hiddenTransitions:hidden.length,focusRetained:document.activeElement===input,sameWindow:before.window===after.window};
     } finally {observer.disconnect();dialog.style.width=originalWidth;dialog.style.maxWidth=originalMaxWidth;}
    });
    await fs.writeFile(path.join(output,`resize-${theme}-${size}.json`),JSON.stringify(record.modalResize,null,2)+'\n');
    assert.notEqual(record.modalResize.before,record.modalResize.after,'真实独立窗口必须跟随表单尺寸');
    assert.equal(record.modalResize.hiddenTransitions,0,'尺寸变化不应隐藏已打开的表单：'+JSON.stringify(record.modalResize));
    assert.equal(record.modalResize.focusRetained,true,'尺寸变化保留搜索焦点');
    assert.equal(record.modalResize.sameWindow,true,'尺寸变化不重建窗口');
   }
   if(size===12&&theme==='Light') {
    // 背景文本更新必须真实改变PNG，刷新不能抢走正在输入的搜索焦点或重建草稿。
    await page.waitForTimeout(150);
    if(process.argv.includes('--coalesce-refresh')) {
     for(const phase of ['cover','uncover']) {
     await page.waitForFunction(()=>!window.__augitNativeBackgroundBusy);
     const measured=await page.evaluate(async phase=>{
      const {invoke}=await import('/src/bridge.js');
      const before=await invoke('window/modal-state');
      const host=chrome.webview, original=host.postMessage.bind(host);
      const marker=document.createElement('span');marker.id='audit-coalesced-background';
      marker.textContent='first background';marker.style.cssText='position:absolute;left:600px;top:4px;background:#ff0088;color:white';
      let covers=0, refreshes=0, uncovers=0;
      host.postMessage=message=>{
       const request=JSON.parse(message);
       original(message);
       if(request.method==='window/modal-cover') {
        covers++;
        if(phase==='cover'&&covers===1) marker.textContent='latest background';
       }
       if(request.method==='window/modal-refresh') refreshes++;
       if(request.method==='window/modal-uncover') {
        uncovers++;
        if(phase==='uncover'&&uncovers===1) marker.textContent='latest background';
       }
      };
      try {
       const started=performance.now();document.querySelector('.titlebar').append(marker);
       let after,quiet=0;
       for(let attempt=0;attempt<200;attempt++) {
        await new Promise(resolve=>setTimeout(resolve,20));
        after=await invoke('window/modal-state');
        const settled=after.snapshotRevision>before.snapshotRevision&&after.backgroundLoadedRevision>before.backgroundLoadedRevision&&!window.__augitNativeBackgroundBusy&&!after.captureCoverReady;
        quiet=settled?quiet+1:0;if(quiet>=5)break;
       }
       return {covers,refreshes,before:before.snapshotRevision,after:after.snapshotRevision,quiet,
        observedIncludingQuietMilliseconds:performance.now()-started,latest:marker.textContent};
      } finally {host.postMessage=original;}
     },phase);
     record[phase==='cover'?'coalescedRefresh':'lateRefresh']=measured;
     await fs.writeFile(path.join(output,`${phase}-refresh.json`),JSON.stringify(measured,null,2)+'\n');
     assert.equal(measured.quiet,5);
     const expected=phase==='cover'?1:2;
     assert.equal(measured.covers,expected,'准备期间变化须合并，捕获完成后的新变化须保留下一批');
     assert.equal(measured.refreshes,expected);
     assert.equal(measured.after-measured.before,expected);
     assert.equal(measured.latest,'latest background');
     await page.locator('#audit-coalesced-background').evaluate(node=>node.remove());
     await page.waitForFunction(()=>!window.__augitNativeBackgroundBusy);
     }
    }
    const beforeDraft=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
    await page.locator('.settings-window [data-settings-filter]').fill('appearance');
    await page.waitForTimeout(250);
    await page.waitForFunction(()=>!document.querySelector('.dialog[data-native-background-capture]'));
    const beforeRefresh=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
    assert.equal(beforeRefresh.snapshotRevision,beforeDraft.snapshotRevision,'最上层草稿更新不应捕获背景');
    await page.evaluate(()=>{
     const dialog=document.querySelector('.settings-window .dialog');
     const intervals=[];let began=null;
     const observer=new MutationObserver(()=>{
      const hidden=dialog.hasAttribute('data-native-background-capture');
      if(hidden&&began===null)began=performance.now();
      if(!hidden&&began!==null){intervals.push(performance.now()-began);began=null;}
     });
     observer.observe(dialog,{attributes:true,attributeFilter:['data-native-background-capture']});
     window.__auditRefreshTiming={started:performance.now(),intervals,observer};
    });
    // 标记属于根背景，不能挂在会被异步Git状态重绘替换的标题栏中。
    await page.evaluate(()=>{const text=document.createElement('span');text.id='audit-background-update';text.textContent='外部变化回归';text.style.cssText='position:absolute;left:600px;top:4px;background:#ff0088;color:white';document.getElementById('app').append(text);});
    await page.waitForFunction(revision=>window.__augitNativeModalActive&&!document.querySelector('.dialog[data-native-background-capture]')&&window.__augitLive, beforeRefresh.snapshotRevision);
    for(let retry=0;retry<80;retry++) {
     record.nativeBackgroundUpdated=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
     if(record.nativeBackgroundUpdated.snapshotRevision>beforeRefresh.snapshotRevision&&record.nativeBackgroundUpdated.backgroundLoadedRevision>0&&!await page.locator('[data-native-background-capture]').count())break;
     await page.waitForTimeout(100);
    }
    assert.ok(record.nativeBackgroundUpdated.snapshotRevision>beforeRefresh.snapshotRevision);
    await verifySnapshotSizing('主背景更新');
    assert.notEqual(record.nativeBackgroundUpdated.backgroundHash,beforeRefresh.backgroundHash,'快照PNG内容必须实际变化');
    assert.ok(record.nativeBackgroundUpdated.backgroundLoadedRevision>0,'背景控制器必须确认新图片已加载');
    record.backgroundRefreshTiming=await page.evaluate(()=>{
     const timing=window.__auditRefreshTiming;timing.observer.disconnect();delete window.__auditRefreshTiming;
     return {loadedObservedMilliseconds:performance.now()-timing.started,transparentIntervalsMilliseconds:timing.intervals,pollingIntervalMilliseconds:100};
    });
    await fs.writeFile(path.join(output,'background-refresh-timing.json'),JSON.stringify(record.backgroundRefreshTiming,null,2)+'\n');
    if(process.argv.includes('--memory')) {
     await page.waitForFunction(()=>!window.__augitNativeBackgroundBusy);
     record.memoryFirstCover=await measureMemory(shell.pid);
    }
    assert.equal(await page.locator('.settings-window [data-settings-filter]').inputValue(),'appearance');
    assert.equal(await page.locator('.settings-window [data-settings-filter]').evaluate(node=>document.activeElement===node),true);
    if(process.argv.includes('--refresh-samples')) {
     record.refreshSamples=[];
     for(let sample=0;sample<5;sample++) {
      await page.waitForFunction(()=>!window.__augitNativeBackgroundBusy);
      const previous=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
      const started=performance.now();
      await page.locator('#audit-background-update').evaluate((node,value)=>node.textContent='刷新延迟样本 '+value,sample);
      let current;
      for(let retry=0;retry<250;retry++) {
       current=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
       if(current.backgroundImageRevision>previous.backgroundImageRevision&&current.backgroundLoadedRevision===current.backgroundImageRevision&&!current.captureCoverReady)break;
       await page.waitForTimeout(20);
      }
      assert.ok(current.backgroundImageRevision>previous.backgroundImageRevision);
      assert.equal(current.backgroundLoadedRevision,current.backgroundImageRevision);
      assert.ok(current.captureCoverBytes>0,'模态存续期间仅保留当前遮盖PNG以便复用');
      assert.equal(current.captureCoverTiming.controllerMilliseconds,0,'连续刷新复用同一控制器');
      for(const value of Object.values(current.captureCoverTiming))assert.ok(Number.isFinite(value)&&value>=0);
      record.refreshSamples.push({sample,settledObservedMilliseconds:performance.now()-started,pollingIntervalMilliseconds:20,...current.captureCoverTiming});
     }
     await fs.writeFile(path.join(output,'refresh-samples.json'),JSON.stringify(record.refreshSamples,null,2)+'\n');
    }
    // 同一个窗口连续变更不能无限积压PNG；最后一版必须加载且继续保留输入焦点。
    for(let change=0;change<8;change++) {
     await page.locator('#audit-background-update').evaluate((node,value)=>{node.textContent='连续外部变化 '+value;},change);
     await page.waitForTimeout(15);
    }
    await page.waitForFunction(()=>!document.querySelector('[data-native-background-capture]'));
    for(let retry=0;retry<80;retry++) {
     record.nativeBackgroundSettled=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
     if(record.nativeBackgroundSettled.backgroundLoadedRevision===record.nativeBackgroundSettled.backgroundImageRevision&&!await page.locator('[data-native-background-capture]').count())break;
     await page.waitForTimeout(100);
    }
    assert.equal(record.nativeBackgroundSettled.backgroundLoadedRevision,record.nativeBackgroundSettled.backgroundImageRevision);
    assert.ok(record.nativeBackgroundSettled.snapshotStreamBytes<record.nativeBackgroundUpdated.snapshotStreamBytes*2,'变更停止后只保留当前PNG');
    if(process.argv.includes('--memory')) {
     await page.waitForFunction(()=>!window.__augitNativeBackgroundBusy);
     record.memoryRetainedCover=await measureMemory(shell.pid);
     record.retainedCoverState=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
     assert.equal(record.retainedCoverState.captureCoverReady,false);
     assert.equal(record.retainedCoverState.captureCoverWindowVisible,false,'后台保留的遮盖子窗必须隐藏');
     assert.ok(record.retainedCoverState.captureCoverBytes>0);
     if(process.argv.includes('--gc-diagnostic')) {
      // 强制回收只用于定位保留来源，不能作为产品性能验收结果。
      record.gcDiagnostic={acceptanceEvidence:false,pages:[]};
      await page.waitForTimeout(5000);
      record.gcDiagnostic.memoryBeforeCollectionAfterIdle=await measureMemory(shell.pid);
      for(const target of context.pages()) {
       const client=await context.newCDPSession(target);
       try {
        const before={dom:await client.send('Memory.getDOMCounters'),heap:await client.send('Runtime.getHeapUsage')};
        await client.send('HeapProfiler.collectGarbage');
        const after={dom:await client.send('Memory.getDOMCounters'),heap:await client.send('Runtime.getHeapUsage')};
        const images=await target.evaluate(()=>Array.from(document.images,img=>({connected:img.isConnected,width:img.naturalWidth,height:img.naturalHeight,hasLoadHandler:img.onload!==null})));
        record.gcDiagnostic.pages.push({url:target.url(),before,after,images});
       } finally {await client.detach();}
      }
      record.gcDiagnostic.memoryAfterCollection=await measureMemory(shell.pid);
      await page.waitForTimeout(5000);
      record.gcDiagnostic.memoryAfterIdle=await measureMemory(shell.pid);
      assert.ok(record.gcDiagnostic.pages.length>=2,'诊断必须覆盖实际背景页面');
      for(const target of record.gcDiagnostic.pages) for(const stage of [target.before,target.after]) {
       assert.ok(stage.dom.nodes>0&&stage.heap.usedSize>0,'诊断须返回真实DOM和堆计数');
      }
      await fs.writeFile(path.join(output,'gc-diagnostic.json'),JSON.stringify(record.gcDiagnostic,null,2)+'\n');
     }
    }
    assert.equal(await page.locator('.settings-window [data-settings-filter]').evaluate(node=>document.activeElement===node),true);
    await page.locator('.settings-window [data-settings-filter]').fill('');
    await page.locator('#audit-background-update').evaluate(node=>node.remove());
    await page.waitForFunction(()=>!window.__augitNativeBackgroundBusy&&!document.querySelector('[data-native-background-capture]'));
   }
   if(process.argv.includes('--inspect')) {console.log('MODAL_INSPECT='+JSON.stringify(record.nativeModal));await page.waitForTimeout(120000);}
   if (size===12&&theme==='Light') {
    // 独立嵌套窗口不得覆盖父表单身份；取消后返回同一个父窗口且释放截图控制器。
    await page.evaluate(()=>{const parent=document.querySelector('.settings-window');const layer=document.createElement('div');layer.className='overlay-layer';layer.dataset.augitOverlay='';layer.id='audit-nested';layer.innerHTML='<div class="dialog compact-input" style="position:absolute"><div class="dialog-header">Nested reference</div><div class="dialog-body"><input value="kept draft"></div></div>';parent.parentNode.append(layer);});
    for(let retry=0;retry<80;retry++) { record.nativeModalNested=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});if(record.nativeModalNested.depth===2)break;await page.waitForTimeout(100); }
    assert.equal(record.nativeModalNested.depth,2,JSON.stringify(record.nativeModalNested));
    assert.equal(record.nativeModalNested.ownerWindow,record.nativeModal.window,JSON.stringify({before:record.nativeModal,nested:record.nativeModalNested}));assert.equal(record.nativeModalNested.snapshotCount,2);
    // 父表单变化只更新它的快照，不改变主窗口背景；顶层输入始终保持焦点。
    await page.locator('#audit-nested input').focus();
    const nestedBefore=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
    await page.locator('.settings-window .dialog-header').evaluate(node=>node.append(document.createTextNode(' 父层刷新')));
    for(let retry=0;retry<80;retry++) {
     record.nativeNestedBackgroundUpdated=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
     if(record.nativeNestedBackgroundUpdated.snapshotRevision>nestedBefore.snapshotRevision&&!await page.locator('[data-native-background-capture]').count())break;
     await page.waitForTimeout(100);
    }
    assert.ok(record.nativeNestedBackgroundUpdated.snapshotRevision>nestedBefore.snapshotRevision,'父层字符变化必须刷新');
    await verifySnapshotSizing('嵌套父层更新');
    assert.equal(record.nativeNestedBackgroundUpdated.backgroundHash,nestedBefore.backgroundHash,'主窗口背景不参与父层表单刷新');
    assert.equal(await page.locator('#audit-nested input').evaluate(node=>node===document.activeElement),true);
    await page.locator('.settings-window .dialog-header').evaluate(node=>node.lastChild.remove());
    await page.locator('#audit-nested').evaluate(node=>node.remove());
    await page.waitForFunction(()=>window.__augitNativeModalLastState?.depth===1&&!document.querySelector('.dialog[data-native-capture]'));
    const restored=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
    assert.equal(restored.window,record.nativeModal.window);assert.equal(restored.snapshotCount,1);
   }
   record.settingsScreenshotSource=await page.evaluate(()=>({url:location.href,scene:document.body.dataset.scene,ui:getComputedStyle(document.body).fontSize,code:getComputedStyle(document.documentElement).getPropertyValue('--augit-code'),viewport:{width:innerWidth,height:innerHeight,dpr:devicePixelRatio},visibleFeature:'.settings-window .settings-layout'}));
   // Pop返回前一个表单后还要等合成帧，否则PNG可能仍是opacity=0的背景。
   await page.waitForFunction(()=>!document.querySelector('[data-native-capture],[data-native-background-capture]'));
   await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
   assert.equal(await dialog.evaluate(node=>getComputedStyle(node).opacity),'1');
   assert.equal(await dialog.evaluate(node=>getComputedStyle(node).visibility),'visible');
   const screenshotClient=await context.newCDPSession(page);
   try {
    const captureModal=async filename=>{
    // 先用独立三色标记证明PNG实际含当前表单，避免DOM可见却捕获旧surface的误报。
    let contentCheck;
    const marker=await dialog.evaluate(node=>{
     const mark=document.createElement('span');mark.id='audit-modal-raster';mark.style.cssText='position:absolute;left:8px;top:8px;width:18px;height:6px;z-index:99999;display:flex;pointer-events:none';
     for(const color of ['#e91159','#0de3ad','#472bf1']) {const tile=document.createElement('i');tile.style.cssText=`display:block;flex:0 0 6px;height:6px;background:${color}`;mark.append(tile);}node.append(mark);
     const r=mark.getBoundingClientRect();return{x:r.x,y:r.y,dpr:devicePixelRatio};
    });
    try {
     await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
     const marked=await screenshotClient.send('Page.captureScreenshot',{format:'png',fromSurface:false,captureBeyondViewport:false});
     const modalState=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
     contentCheck=await verifyModalRaster(Buffer.from(marked.data,'base64'),{x:marker.x*marker.dpr+modalState.liveBounds.x,y:marker.y*marker.dpr+modalState.liveBounds.y,dpr:marker.dpr});
     assert.match(contentCheck.method,/客户区三色标记/,'Python核验结果须按UTF-8返回中文');
    } finally {await page.locator('#audit-modal-raster').evaluate(node=>node.remove());}
    await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
    // 重挂后的surface捕获可能仍命中所有者旧帧；从view捕获真实模态客户区，黑边不计阴影。
    const captured=await screenshotClient.send('Page.captureScreenshot',{format:'png',fromSurface:false,captureBeyondViewport:false});
    const png=Buffer.from(captured.data,'base64');
    const sha256=require('node:crypto').createHash('sha256').update(png).digest('hex');
    assert.notEqual(sha256,record.nativeModal?.backgroundHash?.toLowerCase(),'表单截图不能仍是所有者背景');
    await fs.writeFile(path.join(output,filename),png);
    return {capture:'CDP view / fromSurface=false；只验模态客户区，外围黑边不作系统阴影证据',clientPixels:{width:record.nativeModal.rect.width,height:record.nativeModal.rect.height},contentCheck,sha256};
    };
    Object.assign(record.settingsScreenshotSource,await captureModal(`settings-${theme}-${size}.png`));record.screenshot=true;
    if(size===40) {
     const fontRow=page.locator('.settings-window .font-setting').first();
     await fontRow.scrollIntoViewIfNeeded();
     record.settingsScrollAfter=await dialog.evaluate(node=>Object.fromEntries(['.dialog-body','.settings-nav','.settings-page-header','.settings-content'].map(selector=>{const element=node.querySelector(selector);return[selector,{scrollTop:element.scrollTop,top:element.getBoundingClientRect().top}];})));
     assert.equal(record.settingsScrollAfter['.dialog-body'].scrollTop,0,'字体行滚动不能带动整个设置主体');
     assert.equal(record.settingsScrollAfter['.settings-nav'].scrollTop,record.settingsScrollBefore['.settings-nav'].scrollTop,'右侧滚动须保留导航位置');
     assert.equal(record.settingsScrollAfter['.settings-nav'].top,record.settingsScrollBefore['.settings-nav'].top,'导航顶边不能随右侧字段滚动');
     assert.equal(record.settingsScrollAfter['.settings-page-header'].top,record.settingsScrollBefore['.settings-page-header'].top,'面包屑顶边不能随字体字段滚动');
     assert.equal(await page.locator('.settings-window .settings-page').evaluate(node=>node.scrollLeft),0,'字体字段滚动不能横向移动正文列');
     record.settingsFontRow=await fontRow.evaluate(node=>{const row=node.getBoundingClientRect(),host=node.closest('.settings-page').getBoundingClientRect(),number=node.querySelector('input').getBoundingClientRect();return{row:{top:row.top,bottom:row.bottom},host:{top:host.top,bottom:host.bottom},number:{value:node.querySelector('input').value,width:number.width,top:number.top,bottom:number.bottom}};});
     assert.equal(record.settingsFontRow.number.value,'40');
     assert.ok(record.settingsFontRow.number.top>=record.settingsFontRow.host.top&&record.settingsFontRow.number.bottom<=record.settingsFontRow.host.bottom,'滚动后字号40控件完整可见');
     record.settingsFontScreenshotSource=await captureModal(`settings-fonts-${theme}-${size}.png`);
    }
   } finally {await screenshotClient.detach();}
   await page.locator('.settings-window [data-settings-action="cancel"]').click();
   await page.waitForFunction(()=>!window.__augitNativeModalActive);
   record.nativeModalClosed=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
   assert.equal(record.nativeModalClosed.active,false);assert.equal(record.nativeModalClosed.ownerEnabled,true);assert.equal(record.nativeModalClosed.snapshotCount,0);
   assert.equal(record.nativeModalClosed.parentWindow,record.nativeModalClosed.owner);
   assert.equal(record.nativeModalClosed.captureCoverReady,false,'关闭后捕获遮盖不能残留');
   assert.equal(record.nativeModalClosed.captureCoverBytes,0,'关闭后捕获遮盖PNG必须释放');
   assert.equal(record.nativeModalClosed.captureCoverWindow,0,'关闭后遮盖子窗必须销毁');
   assert.equal(record.nativeModalClosed.captureCoverWindowVisible,false);
   if(process.argv.includes('--memory'))record.memoryClosed=await measureMemory(shell.pid);
   if(process.argv.includes('--memory-cycles')) {
    // 同一实例重复打开、刷新、关闭；不强制GC，保留自然空闲后的进程明细。
    record.memoryCycles=[];
    for(let cycle=0;cycle<5;cycle++) {
     await page.locator('.titlebar [aria-label="Settings"]').click();
     await page.waitForFunction(()=>window.__augitNativeModalActive&&!document.querySelector('[data-native-capture]'));
     const before=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
     await page.evaluate(value=>{const marker=document.createElement('span');marker.id='audit-cycle-marker';marker.textContent='周期 '+value;document.querySelector('.titlebar').append(marker);},cycle);
     let refreshed;
     for(let retry=0;retry<100;retry++) {
      refreshed=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
      if(refreshed.snapshotRevision>before.snapshotRevision&&!refreshed.captureCoverReady&&!await page.evaluate(()=>!!window.__augitNativeBackgroundBusy))break;
      await page.waitForTimeout(50);
     }
     assert.ok(refreshed.snapshotRevision>before.snapshotRevision,'每轮必须实际更新背景');
     const open=await measureMemory(shell.pid);
     await page.locator('.settings-window [data-settings-action="cancel"]').click();
     await page.waitForFunction(()=>!window.__augitNativeModalActive);
     await page.locator('#audit-cycle-marker').evaluate(node=>node.remove());
     await page.waitForTimeout(5000);
     const closed=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
     assert.equal(closed.snapshotCount,0);assert.equal(closed.captureCoverBytes,0);
     record.memoryCycles.push({cycle,open,closedAfterIdle:await measureMemory(shell.pid)});
    }
    await fs.writeFile(path.join(output,'memory-cycles.json'),JSON.stringify(record.memoryCycles,null,2)+'\n');
   }
   if(process.argv.includes('--reference-diff')) {
    assert.ok(process.argv.includes('--reference-fixture'),'逐行对照必须使用相同参考夹具');
    await page.evaluate(()=>{
     window.__auditDiffSamples=[];
     window.__auditDiffObserver=new MutationObserver(()=>window.__auditDiffSamples.push(document.querySelector('.editor-content')?.textContent||''));
     window.__auditDiffObserver.observe(document.querySelector('.editor-content').parentNode,{childList:true,subtree:true});
    });
    await page.locator('.change-file-row[data-path="app.py"]').dblclick();
    await page.waitForSelector('.editor-content .diff-code-line[data-line]');
    await page.evaluate(async()=>{await document.fonts.ready;await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));});
    record.referenceDiff=await page.locator('.editor-content').evaluate(node=>({
     text:node.innerText,
     rows:Array.from(node.querySelectorAll('.diff-code-line'),row=>({text:row.textContent,line:row.dataset.line,classes:row.className,top:row.getBoundingClientRect().top,height:row.getBoundingClientRect().height})),
     html:node.innerHTML,
    }));
    await page.screenshot({path:path.join(output,`reference-diff-${theme}.png`)});
    await fs.writeFile(path.join(output,`reference-diff-${theme}.json`),JSON.stringify(record.referenceDiff,null,2)+'\n');
    const samples=await page.evaluate(()=>{window.__auditDiffObserver.disconnect();delete window.__auditDiffObserver;const samples=window.__auditDiffSamples;delete window.__auditDiffSamples;return samples;});
    assert.ok(samples.length>0,'必须观察到实际加载期间的正文变化');
    assert.ok(samples.every(text=>!text.includes('<assembly')&&!text.includes('manifestVersion')),'真实差异加载期间不能闪现XML示例');
    assert.ok(record.referenceDiff.rows.some(row=>row.text.trim()==='# changed for pixel audit'),'实际差异须包含夹具新增行：'+JSON.stringify(record.referenceDiff.rows));
    assert.ok(record.referenceDiff.rows.every(row=>!row.text.startsWith('@@')),'完整小文件正文不应多出首片段补丁头');
    assert.equal(await page.locator('.editor-content .diff-summary').innerText(),'1 difference','真实单块摘要使用单数');
    const emptySide=page.locator('.editor-content .diff-side').first().locator('.diff-code-line[data-line=""]');
    assert.equal(await emptySide.count(),1,'新增块旧侧应有一个对齐占位');
    assert.equal(await emptySide.evaluate(node=>getComputedStyle(node).backgroundColor),'rgba(0, 0, 0, 0)','缺失侧不能填充整行新增色');
    const endings=await page.evaluate(()=>({old:window.__augitLive.diff.oldLineEndings,new:window.__augitLive.diff.newLineEndings}));
    await fs.writeFile(path.join(output,'endings.json'),JSON.stringify(endings,null,2)+'\n');
    assert.deepEqual(await page.locator('.editor-content .diff-line-ending').allTextContents(),['LF','Mixed line endings'],'两侧换行标识须匹配原始Blob及磁盘字节：'+JSON.stringify(endings));
    record.lineEndingColors=await page.evaluate(()=>{
     const original=document.body.dataset.theme,fixture=document.createElement('div');
     fixture.hidden=true;document.body.append(fixture);
     const result=[];
     try {
      for(const theme of ['light','dark']) {
       document.body.dataset.theme=theme;
       for(const ending of ['LF','CRLF','CR']) {
        fixture.innerHTML=diffFileHeader('HEAD','Workspace','颜色回归','app.py',{loading:false,oldLineEndings:ending});
        result.push({theme,ending,color:getComputedStyle(fixture.querySelector('.diff-line-ending')).color});
       }
      }
     } finally {document.body.dataset.theme=original;fixture.remove();}
     return result;
    });
    assert.deepEqual(record.lineEndingColors.map(value=>value.color),['rgb(0, 0, 255)','rgb(255, 0, 0)','rgb(255, 0, 255)','rgb(88, 157, 246)','rgb(255, 100, 100)','rgb(151, 118, 169)']);
    record.diffConnectorGeometry=[];
    const columns=page.locator('.editor-content .diff-columns');
    for(const height of [240,600]) {
     await columns.evaluate((node,height)=>node.style.height=height+'px',height);
     const geometry=await columns.evaluate(node=>{
      const line=node.querySelector('.diff-side:last-child .diff-code-line.added').getBoundingClientRect();
      const connector=node.querySelector('.diff-gutter-connector.added').getBoundingClientRect();
      return {lineTop:line.top,lineHeight:line.height,connectorTop:connector.top,connectorHeight:connector.height};
     });
     assert.ok(Math.abs(geometry.lineTop-geometry.connectorTop)<1,'连接区必须对齐新增行顶部');
     assert.ok(Math.abs(geometry.lineHeight-geometry.connectorHeight)<1,'连接区高度必须等于新增行高度');
     record.diffConnectorGeometry.push({height,...geometry});
    }
    await columns.evaluate(node=>node.style.removeProperty('height'));
    record.longDiffScroll=await page.evaluate(async()=>{
     const host=document.createElement('div');host.style.cssText='position:fixed;left:20px;top:100px;width:800px;height:240px;z-index:99999';
     const rows=Array.from({length:100},(_,index)=>({kind:index===50?'Modified':'Context',oldLine:index+1,newLine:index+1,oldText:'old '+index,newText:'new '+index}));
     host.innerHTML=diffBodyParts({rows}).columns;document.body.append(host);
     try {
      const view=host.firstElementChild;view.style.height='240px';
      const style=getComputedStyle(view);view.scrollTop=900;
      await new Promise(resolve=>requestAnimationFrame(resolve));
      const line=view.querySelector('.diff-side .changed').getBoundingClientRect();
      const connector=view.querySelector('.diff-gutter-connector.changed').getBoundingClientRect();
      return {overflow:style.overflowY,scrollTop:view.scrollTop,scrollHeight:view.scrollHeight,clientHeight:view.clientHeight,lineTop:line.top,connectorTop:connector.top};
     } finally {host.id='audit-long-diff';}
    });
    assert.ok(['auto','scroll'].includes(record.longDiffScroll.overflow),'长差异须允许用户滚动：'+JSON.stringify(record.longDiffScroll));
    assert.ok(record.longDiffScroll.scrollTop>0,'长差异须实际滚动');
    assert.ok(Math.abs(record.longDiffScroll.lineTop-record.longDiffScroll.connectorTop)<1,'滚动后连接区必须对齐正文');
    try {
     const longView=page.locator('#audit-long-diff .diff-columns');
     await longView.evaluate(node=>node.scrollTop=0);
     await longView.hover();await page.mouse.wheel(0,600);
     await page.waitForFunction(()=>document.querySelector('#audit-long-diff .diff-columns').scrollTop>0);
     record.longDiffWheel=await longView.evaluate(node=>({scrollTop:node.scrollTop,lineTop:node.querySelector('.diff-side .changed').getBoundingClientRect().top,connectorTop:node.querySelector('.diff-gutter-connector.changed').getBoundingClientRect().top}));
     assert.ok(Math.abs(record.longDiffWheel.lineTop-record.longDiffWheel.connectorTop)<1,'鼠标滚动后正文与连接区对齐');
     await longView.evaluate(node=>{
      node.scrollTop=0;
      for(const side of node.querySelectorAll('.diff-side'))side.firstElementChild.textContent='long '.repeat(150);
     });
     const left=longView.locator('.diff-side').first();
     const beforeHorizontal=await left.evaluate(node=>({overflow:getComputedStyle(node).overflowX,width:node.clientWidth,content:node.scrollWidth}));
     assert.ok(['auto','scroll'].includes(beforeHorizontal.overflow),'长行须允许用户横向滚动：'+JSON.stringify(beforeHorizontal));
     await left.hover({position:{x:100,y:20}});await page.mouse.wheel(400,0);
     await page.waitForFunction(()=>document.querySelector('#audit-long-diff .diff-side').scrollLeft>0);
     record.longDiffHorizontal=await longView.evaluate(node=>({left:node.querySelector('.diff-side').scrollLeft,right:node.querySelector('.diff-side:last-child').scrollLeft,gutter:node.querySelector('.diff-gutter').scrollLeft,container:node.scrollLeft}));
     assert.equal(record.longDiffHorizontal.right,0,'横向滚动不能带动另一侧');
     assert.equal(record.longDiffHorizontal.gutter,0,'行号不能跟随横向滚动');
     assert.equal(record.longDiffHorizontal.container,0,'横向滚动仅作用于目标正文');
     record.sideWheel=[];
     for(const sideIndex of [0,1]) {
      await longView.evaluate(node=>{node.scrollTop=0;for(const side of node.querySelectorAll('.diff-side')){side.scrollTop=0;side.scrollLeft=0;}});
      await longView.locator('.diff-side').nth(sideIndex).hover({position:{x:100,y:20}});
      await page.mouse.wheel(0,400);
      await page.waitForFunction(()=>document.querySelector('#audit-long-diff .diff-columns').scrollTop>0);
      const sample=await longView.evaluate(node=>({parent:node.scrollTop,sides:[...node.querySelectorAll('.diff-side')].map(side=>side.scrollTop),tops:[...node.querySelectorAll('.diff-side .changed,.diff-gutter-connector.changed')].map(line=>line.getBoundingClientRect().top)}));
      assert.deepEqual(sample.sides,[0,0],'纵向滚动必须由共同容器承担');
      assert.ok(Math.max(...sample.tops)-Math.min(...sample.tops)<1,'左右正文和连接区必须同步');
      record.sideWheel.push({sideIndex,...sample});
     }
    } finally {await page.locator('#audit-long-diff').evaluate(node=>node.remove());}
    await page.screenshot({path:path.join(output,`reference-diff-${theme}.png`)});
   }
   assert.deepEqual(await page.evaluate(()=>window.__augitErrors),[],'真实外壳不得留下未处理错误');
   records.push(record);await fs.writeFile(path.join(output,'geometry.json'),JSON.stringify(records,null,2)+'\n');
  } finally {
   if(browser)try{await browser.close();}catch{}
   if(shell?.pid&&shell.exitCode===null)try{await execute('taskkill.exe',['/PID',String(shell.pid),'/T','/F'],{windowsHide:true});}catch{}
   const resolved=path.resolve(audit);assert.ok(resolved.startsWith(path.resolve(os.tmpdir())+path.sep)&&path.basename(resolved).startsWith('Augit-pixel-fix-'));
   await fs.rm(resolved,{recursive:true,force:true,maxRetries:8,retryDelay:250});
   console.log(`CLEANED_PID=${shell?.pid} TEMP_REMOVED=${resolved}`);
  }
 }
 console.log(`真实外壳${records.length}组几何通过；本次实例及临时profile均已清理。`);
})().catch(error=>{console.error(error);process.exitCode=1;});
