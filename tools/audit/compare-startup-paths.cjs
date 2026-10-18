// 使用同一配置副本比较正常入口和场景入口，不写用户配置、不修改产品样式。
const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os'),net=require('node:net');
const {spawn,execFile}=require('node:child_process'),execute=require('node:util').promisify(execFile);
const assert=require('node:assert/strict');
const captureStablePage=require('./capture-stable-page.cjs');
const {chromium}=require(process.argv[2]);
const delay=ms=>new Promise(r=>setTimeout(r,ms));
(async()=>{
 const output=path.resolve(process.argv[3]);await fs.mkdir(output,{recursive:true});
 const original=await fs.readFile(path.join(process.env.LOCALAPPDATA,'Augit/settings.json'));
 const screenshotSession=process.argv.includes('--screenshot-session');
 const uiSizeArg=process.argv.find(arg=>arg.startsWith('--ui-size='));
 const uiSize=uiSizeArg?Number(uiSizeArg.split('=')[1]):null;
 if(uiSize!==null)assert.ok(Number.isFinite(uiSize)&&uiSize>=9&&uiSize<=40);
 const copiedSettings=JSON.parse(original);
 if(screenshotSession) {
  copiedSettings.appearanceVersion=1;
  copiedSettings.lastWorkspace=path.resolve('.');
  copiedSettings.openFiles=['DocumentClassification.cs','DocumentKind.cs','DocumentLineEndings.cs','JsonDisplayFormatter.cs','DocumentLimits.cs'].map(n=>'src/Augit.Core/Documents/'+n);
  copiedSettings.activeFile=copiedSettings.openFiles[0];
  copiedSettings.expandedDirectories=['docs'];
  copiedSettings.monospaceFontFamily='JetBrains Mono';copiedSettings.fontSize=13;
  if(uiSize!==null)copiedSettings.textFontSize=uiSize;
 }
 const reports=[];
 const onlyMode=process.argv.find(arg=>arg.startsWith('--only-mode='))?.split('=')[1];
 if(onlyMode)assert.ok(['normal','explicit-main'].includes(onlyMode));
 const binary=path.resolve(process.argv.find(arg=>arg.startsWith('--binary='))?.slice('--binary='.length)||'src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe');
 const modes=onlyMode?[onlyMode]:process.argv.includes('--real-default')?['normal']:process.argv.includes('--all-surfaces')?['normal','explicit-main']:process.argv.includes('--profile-comparison')?['normal','daily-profile']:process.argv.includes('--normal-only')?['normal']:screenshotSession?['normal','text-viewer','pixel-exact']:['normal','explicit-main','text-viewer'];
 for(const mode of modes) {
  const temp=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-startup-compare-'));let shell,browser;
  try {
   const server=net.createServer();await new Promise(r=>server.listen(0,'127.0.0.1',r));const port=server.address().port;await new Promise(r=>server.close(r));
   await fs.writeFile(path.join(temp,'settings.json'),screenshotSession?JSON.stringify(copiedSettings):original);
   const args=['--web-root',path.resolve('web'),'--workspace',path.resolve('.'),'--browser-args','--remote-debugging-port='+port];
   if(process.argv.includes('--bundled-web'))args.splice(0,2);
   if(!process.argv.includes('--real-default')) {
    args.push('--settings-file',path.join(temp,'settings.json'));
    if(mode!=='daily-profile')args.push('--user-data-folder',path.join(temp,'profile'));
   }
   if(mode==='explicit-main'||mode==='text-viewer')args.push('--scene',mode==='explicit-main'?'main-project':'text-viewer');
   if(mode==='pixel-exact')args.push('--pixel-exact');
   if(process.argv.includes('--daily-launcher')) {
    assert.equal(mode,'normal','日常启动器仅接受正常入口');
    assert.ok(!process.argv.includes('--real-default'),'日常全页巡检使用配置副本，不能改写用户标签');
    const launch=require('./daily-launch-command.cjs')(path.resolve('.'),path.join(temp,'settings.json'),port);
    const result=await execute(launch.file,launch.args,{env:launch.env});
    const pid=Number(result.stdout.match(/AUGIT_PID=(\d+)/)?.[1]);
    assert.ok(pid,'日常启动器未返回实际Augit PID');
    shell={pid,exitCode:null};
   } else shell=spawn(binary,args,{stdio:'ignore'});
   console.log(JSON.stringify({mode,pid:shell.pid,port,temp}));
   for(let n=0;n<120;n++){try{browser=await chromium.connectOverCDP('http://127.0.0.1:'+port);break;}catch{assert.equal(shell.exitCode,null);await delay(250);}}
   assert.ok(browser);const page=browser.contexts()[0].pages()[0];
   const cdp=await page.context().newCDPSession(page),scripts=[];
   cdp.on('Debugger.scriptParsed',entry=>{if(/\/src\/(mockup|live-data|bridge)\.js/.test(entry.url))scripts.push(entry);});
   await cdp.send('Debugger.enable');
   await page.waitForFunction(()=>window.__augitReady&&window.__augitSettingsReady&&window.__augitGitReady);
   await page.evaluate(()=>document.fonts.ready);await delay(1200);
   const inspect=()=>page.evaluate(()=>{
    const selectors=['.augit-window','.titlebar','.tool-rail','.side-tool','.tree-row','.editor-tabs','.editor-tab','.code-line','.bottom-tool','.log-filterbar','.commit-row','.statusbar'];
    return {url:location.href,scene:document.body.dataset.scene,dpr:devicePixelRatio,viewport:[innerWidth,innerHeight],visualScale:visualViewport.scale,layout:window.__augitLive.layout,
     settings:window.__augitLive.settings,tabs:window.__augitLive.tabs.map(x=>({id:x.id,path:x.path})),activeTabId:window.__augitLive.activeTabId,
     metrics:Object.fromEntries(selectors.map(s=>{const n=document.querySelector(s);if(!n)return [s,null];const r=n.getBoundingClientRect(),c=getComputedStyle(n);return [s,{x:r.x,y:r.y,w:r.width,h:r.height,font:c.font,fontFamily:c.fontFamily,fontSize:c.fontSize,fontWeight:c.fontWeight,lineHeight:c.lineHeight,color:c.color,background:c.backgroundColor}];})),errors:window.__augitErrors};
   });
   const before=await inspect();await page.screenshot({caret:'initial',path:path.join(output,mode+'-initial.png')});
   if(mode==='text-viewer'&&!await page.locator('.bottom-tool').count()) {
    await page.locator('.tool-rail [data-tool="history"]').click();await page.waitForSelector('.commit-row');await delay(700);
   }
   const aligned=await inspect();await page.screenshot({caret:'initial',path:path.join(output,mode+'-aligned.png')});
   const hashes=await page.evaluate(async()=>{
    const result={};for(const file of ['mockup.css','mockup.js','live-data.js']) {
     const data=await(await fetch('/src/'+file,{cache:'no-store'})).arrayBuffer();result[file]=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',data)),b=>b.toString(16).padStart(2,'0')).join('');
    }return result;
   });
   const executedScripts={};
   for(const script of scripts){const source=await cdp.send('Debugger.getScriptSource',{scriptId:script.scriptId});executedScripts[script.url]=require('node:crypto').createHash('sha256').update(source.scriptSource).digest('hex');}
   await cdp.detach();
   const surfaces={};
   if(process.argv.includes('--surfaces')) {
    const capture=async name=>{
     await delay(350);
     const capture=await captureStablePage(page,name.startsWith('settings-')?'.settings-window .dialog':'.change-file-row',path.join(output,mode+'-'+name+'.png'));
     await fs.writeFile(path.join(output,mode+'-'+name+'-capture.json'),JSON.stringify(capture,null,2));
     surfaces[name]=await page.evaluate(()=>Object.fromEntries(['.titlebar','.side-tool','.check-group-row','.change-file-row','.settings-window .dialog','.settings-nav','.settings-content','.bottom-tool'].map(s=>{const e=document.querySelector(s);if(!e)return[s,null];const r=e.getBoundingClientRect(),c=getComputedStyle(e);return[s,{x:r.x,y:r.y,w:r.width,h:r.height,font:c.font,color:c.color}];})));
    };
    if(!await page.locator('.change-file-row').first().isVisible())await page.locator('.tool-rail [data-tool="commit"]').click();
    await page.waitForSelector('.change-file-row');await capture('commit');
    await page.locator('.titlebar [aria-label="Settings"]').click();await page.waitForSelector('.settings-window');await capture('settings-appearance');
    if(process.argv.includes('--probe-cover-clip'))await require('./probe-cover-clip.cjs').probeCoverClip(page,output);
    for(const name of ['file-view','git','terminal']) {
     await page.locator('[data-settings-page="'+name+'"]').click();
     if(name==='file-view') {
      assert.equal(await page.locator('[data-fixed-code-font]').innerText(),'JetBrains Mono');
      assert.equal(await page.locator('[data-fixed-code-size]').innerText(),'13');
      assert.equal(await page.locator('[data-setting="monospaceFontFamily"], [data-setting="codeFontSize"]').count(),0);
     }
     await capture('settings-'+name);
    }
    await page.locator('[data-settings-action="cancel"]').click();
    assert.deepEqual(await page.evaluate(()=>window.__augitErrors),[]);
   }
   const allSurfaces=process.argv.includes('--all-surfaces')?await require('./startup-surfaces.cjs')(page,output,mode):null;
   reports.push({mode,binary,args:process.argv.includes('--daily-launcher')?['tools/start-augit.ps1','-Workspace',path.resolve('.'),'-SettingsFile',path.join(temp,'settings.json')]:args,before,aligned,hashes,executedScripts,surfaces,allSurfaces});
   await fs.writeFile(path.join(output,'comparison.json'),JSON.stringify(reports,null,2));
  } finally {
   if(browser)await browser.close().catch(()=>{});
   if(shell?.pid&&shell.exitCode===null)await execute('taskkill.exe',['/PID',String(shell.pid),'/T','/F']).catch(()=>{});
   assert.ok(path.resolve(temp).startsWith(path.resolve(os.tmpdir())+path.sep)&&path.basename(temp).startsWith('Augit-startup-compare-'));
   await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:250});console.log(JSON.stringify({cleaned:shell?.pid,temp}));
  }
 }
 // 相同缩放与布局必须加载相同资源并保持相同度量；强制100%只核对资源。
 const baseline=reports.find(report=>report.mode==='normal')||reports[0];
 for(const report of reports) {
  assert.deepEqual(report.hashes,baseline.hashes,'不同入口加载了不同资源');
  if(process.argv.includes('--surfaces'))assert.deepEqual(report.surfaces,baseline.surfaces,'普通会话和隔离测试的页面度量不同');
  if(process.argv.includes('--all-surfaces'))assert.deepEqual(report.allSurfaces,baseline.allSurfaces,'普通入口和测试入口的逐页度量不同');
  if(report.mode!=='pixel-exact')assert.deepEqual(report.aligned.metrics,baseline.aligned.metrics,'同布局入口的运行时度量不同');
 }
 if(!process.argv.includes('--real-default'))assert.ok(original.equals(await fs.readFile(path.join(process.env.LOCALAPPDATA,'Augit/settings.json'))),'用户配置被外部改变，不能声称基线固定');
 console.log(JSON.stringify(reports.map(r=>({mode:r.mode,scene:r.before.scene,viewport:r.before.viewport,layout:r.before.layout,metrics:r.aligned.metrics,hashes:r.hashes})),null,2));
})().catch(e=>{console.error(e);process.exitCode=1;});
