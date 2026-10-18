// 同夹具普通文本的真实外壳取证；仅使用现有入口隐藏面板，不改正文布局。
const assert=require('node:assert/strict');
const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os'),net=require('node:net');
const {spawn,execFile}=require('node:child_process'),{promisify}=require('node:util');
const execute=promisify(execFile),{chromium}=require(process.argv[2]);
const output=path.resolve(process.argv[3]);
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
(async()=>{
 const temp=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-text-pair-'));
 const listener=net.createServer();let shell,browser;
 try {
  await fs.mkdir(output,{recursive:true});
  await new Promise(resolve=>listener.listen(0,'127.0.0.1',resolve));
  const port=listener.address().port;await new Promise(resolve=>listener.close(resolve));
  const settings=path.join(temp,'settings.json');
  await fs.writeFile(settings,JSON.stringify({theme:'Dark',textFontFamily:'Microsoft YaHei UI',textFontSize:12,monospaceFontFamily:'JetBrains Mono',fontSize:13,window:{width:1400,height:930,isMaximized:true}}));
  shell=spawn(path.resolve('src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe'),['--web-root',path.resolve('web'),'--workspace','D:/JetBrains/PyCharm-2026.2.1-reference/fixture','--scene','text-viewer','--open','notes.txt','--no-session-restore','--settings-file',settings,'--user-data-folder',path.join(temp,'profile'),'--browser-args',`--remote-debugging-port=${port}`],{windowsHide:false,stdio:'ignore'});
  console.log(JSON.stringify({pid:shell.pid,port,temp}));
  for(let n=0;n<120;n++){try{browser=await chromium.connectOverCDP(`http://127.0.0.1:${port}`);break;}catch{assert.equal(shell.exitCode,null);await delay(250);}}
  assert.ok(browser);const page=browser.contexts()[0].pages()[0];
  await page.waitForFunction(()=>window.__augitReady&&window.__augitSettingsReady&&document.querySelector('.code-view')?.textContent.includes('Ag 0123456789'));
  for(const region of ['side','bottom']) {
   for(let n=0;n<2;n++) {
    const layout=await page.evaluate(()=>window.__augitLive.layout);
    if(layout[region+'Hidden']||layout.collapsed===region||region==='bottom'&&!layout.bottom)break;
    const tool=region==='side'?layout.side:layout.bottom==='git'?'history':layout.bottom;
    await page.locator(`.tool-rail [data-tool="${tool}"]`).click();await delay(150);
   }
  }
  await page.locator('.code-view').focus();
  await page.evaluate(()=>document.fonts.ready);await delay(600);
  const readGeometry=()=>page.evaluate(()=>{
   const rect=n=>{const r=n.getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height};};
   return {viewport:{width:innerWidth,height:innerHeight,dpr:devicePixelRatio},layout:window.__augitLive.layout,settings:window.__augitLive.settings,
    rows:[...document.querySelectorAll('.code-view .code-line')].map(n=>{const style=getComputedStyle(n.lastElementChild);return{text:n.textContent,rect:rect(n),content:rect(n.lastElementChild),style:{fontFamily:style.fontFamily,fontSize:style.fontSize,fontWeight:style.fontWeight,letterSpacing:style.letterSpacing,lineHeight:style.lineHeight}};}),
    focus:document.activeElement?.className,errors:window.__augitErrors};
  });
  const state=await page.evaluate(async()=>{const{invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
  if(process.argv.includes('--wait-foreground')) {
   console.log(JSON.stringify({foregroundReady:true,pid:shell.pid,window:state.owner}));
   const gate=path.join(output,'foreground-ready.signal');
   for(let attempt=0;attempt<900;attempt++){try{await fs.access(gate);break;}catch{await delay(200);}}
   await fs.access(gate);await fs.unlink(gate);
  }
  await page.evaluate(async()=>{const{invoke}=await import('/src/bridge.js');const current=await invoke('window/query');if(!current.maximized)await invoke('window/maximize');});
  await delay(600);
  const geometry=await readGeometry();
  await execute('pwsh',['-NoProfile','-File','tools/audit/capture-screen-region.ps1','-WindowHandle',String(state.owner),'-ExpectedProcessId',String(shell.pid),'-Padding','0','-Output',path.join(output,'actual.png')],{windowsHide:true,timeout:30000});
  assert.deepEqual(geometry.errors,[]);
  await fs.writeFile(path.join(output,'actual-geometry.json'),JSON.stringify({pid:shell.pid,port,temp,...geometry},null,2)+String.fromCharCode(10));
 }finally{
  if(listener.listening)await new Promise(resolve=>listener.close(resolve));
  if(browser)try{await browser.close();}catch{}
  if(shell?.pid&&shell.exitCode===null)try{await execute('taskkill.exe',['/PID',String(shell.pid),'/T','/F'],{windowsHide:true});}catch{}
  assert.ok(path.resolve(temp).startsWith(path.resolve(os.tmpdir())+path.sep)&&path.basename(temp).startsWith('Augit-text-pair-'));
  await fs.rm(temp,{recursive:true,force:true,maxRetries:8,retryDelay:250});
  console.log(JSON.stringify({cleanedPid:shell?.pid,tempRemoved:temp}));
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
