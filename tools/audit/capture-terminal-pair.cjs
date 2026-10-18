// 默认采集自然横幅；--repeat-rows通过真实Shell输出固定八行，不注入渲染buffer。
const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os'),net=require('node:net');
const assert=require('node:assert/strict');
const {execFile}=require('node:child_process'),execute=require('node:util').promisify(execFile);
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
(async()=>{
 const {chromium}=require(process.argv[2]),output=path.resolve(process.argv[3]);
 const original=JSON.parse(await fs.readFile(path.join(process.env.LOCALAPPDATA,'Augit/settings.json'),'utf8'));
 const terminalSettings=require('./terminal-capture-options.cjs').dailyTerminalSettings(original,process.argv);
 const fontSize=13,fixture='D:/JetBrains/PyCharm-2026.2.1-reference/fixture';
 const temporary=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-terminal-pair-'));
 let shell,browser;const server=net.createServer();
 try {
  await fs.mkdir(output,{recursive:true});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));const port=server.address().port;await new Promise(resolve=>server.close(resolve));
  const settings=path.join(temporary,'settings.json');
  await fs.writeFile(settings,JSON.stringify(terminalSettings));
  const launch=require('./daily-launch-command.cjs')(process.cwd(),settings,port,fixture);
  const started=await execute(launch.file,launch.args,{env:launch.env,timeout:120000});
  const pid=Number(started.stdout.split('AUGIT_PID=')[1]?.trim());
  assert.ok(Number.isSafeInteger(pid)&&pid>0,'日常启动器没有返回有效PID');shell={pid,exitCode:null};
  await fs.writeFile(path.join(output,'launch.json'),JSON.stringify({launcher:'tools/start-augit.ps1',workspace:fixture,profile:'default-session',isolatedSettingsCopy:true,forcedBuild:true,sourceWeb:true,command:launch.args,buildOutput:started.stdout},null,2));
  console.log(JSON.stringify({pid:shell.pid,port,temporary}));
  for(let n=0;n<120;n++){try{browser=await chromium.connectOverCDP('http://127.0.0.1:'+port);break;}catch{assert.equal(shell.exitCode,null);await delay(250);}}
  assert.ok(browser);const page=browser.contexts()[0].pages()[0];
  await page.waitForFunction(()=>window.__augitReady&&window.__augitSettingsReady);
  await page.waitForFunction(()=>!window.__augitLive.sessionRestoring);
  const theme=await page.evaluate(()=>window.__augitLive.settings.theme);
  assert.ok(!new URL(page.url()).searchParams.has('scene'),'终端验收必须使用正常入口');
  await page.evaluate(()=>{const bridge=chrome.webview,original=bridge.postMessage;window.__auditTerminalCalls=[];bridge.postMessage=function(message){const r=typeof message==='string'?JSON.parse(message):message;if(r.method?.startsWith('terminal/')&&r.method!=='terminal/read')window.__auditTerminalCalls.push(r);return original.call(this,message);};});
  await page.evaluate(()=>{const Original=window.Terminal;window.__auditTerminalInstances=[];window.Terminal=class extends Original{constructor(options){super(options);window.__auditTerminal=this;window.__auditTerminalInstances.push(this);}};});
  await page.locator('.tool-rail .rail-button[aria-label="Terminal"]').click();
  await page.waitForFunction(()=>window.__augitTerminalReady&&window.__auditTerminal);
  try {await page.waitForFunction(()=>{const b=window.__auditTerminal.buffer.active;return Array.from({length:b.length},(_,i)=>b.getLine(i)?.translateToString(true)).some(s=>/PS [a-z]:/i.test(s));});}
  catch(error){const diagnosis=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return {calls:window.__auditTerminalCalls,instances:window.__auditTerminalInstances.map(t=>({attached:t.element?.isConnected,lines:Array.from({length:t.buffer.active.length},(_,i)=>t.buffer.active.getLine(i)?.translateToString(true))})),raw:await invoke('terminal/read',{offset:0}),status:await invoke('terminal/status'),readError:window.__augitTerminalReadError,errors:window.__augitErrors};});await fs.writeFile(path.join(output,'startup-failure.json'),JSON.stringify(diagnosis,null,2));console.log(JSON.stringify(diagnosis));throw error;}
  await page.evaluate(()=>document.fonts.ready);await delay(700);
  const startupRows=process.argv.includes('--startup-rows');
  const repeatRows=startupRows||process.argv.includes('--repeat-rows');
  const rowCommand="1..8 | ForEach-Object { 'Ag 0123456789' }";
  if(repeatRows) {
   if(!startupRows){
    await page.locator('.xterm-helper-textarea').focus();
    await page.keyboard.type(rowCommand);await page.keyboard.press('Enter');
   }
   await page.waitForFunction(()=>{
    const b=window.__auditTerminal.buffer.active;
    return Array.from({length:b.length},(_,i)=>b.getLine(i)?.translateToString(true)).filter(s=>s==='Ag 0123456789').length===8;
   });
   await delay(500);
   // 仅移动浏览器指针，不改变终端焦点；不能据此认定系统截图没有鼠标光晕。
   await page.locator('.titlebar').hover();await delay(400);
  }
  const geometry=await page.evaluate(async()=>{
   const term=window.__auditTerminal,b=term.buffer.active,r=term.element.querySelector('.xterm-screen').getBoundingClientRect();
   const {invoke}=await import('/src/bridge.js');const state=await invoke('window/modal-state');
   return {window:state.owner,dpr:devicePixelRatio,viewport:{width:innerWidth,height:innerHeight},font:term.options.fontFamily,fontSize:term.options.fontSize,lineHeight:term.options.lineHeight,rows:term.rows,cols:term.cols,screen:{x:r.x,y:r.y,width:r.width,height:r.height},lines:Array.from({length:b.length},(_,i)=>b.getLine(i)?.translateToString(true)),errors:window.__augitErrors};
  });
  assert.deepEqual(geometry.errors,[]);assert.equal(geometry.fontSize,fontSize);
  const viewportRows=await page.evaluate(require('./terminal-viewport-snapshot.cjs').collectTerminalViewport);
  await fs.writeFile(path.join(output,'visible-rows.json'),JSON.stringify(viewportRows,null,2));
  const identity=await require('./settings-runtime-identity.cjs').collectSettingsRuntimeIdentity(page,process.cwd(),'Debug');
  await fs.writeFile(path.join(output,'runtime-identity.json'),JSON.stringify(identity,null,2));
  assert.equal(identity.terminalFontSamples.actualFontObserved,true,'没有取得实际终端字符字体，不能登记完整身份');
  assert.equal(await page.evaluate(()=>window.__augitLive.settings.theme),theme);
  if(theme==='Dark'){
   const colors=await page.evaluate(()=>({background:window.__auditTerminal.options.theme.background,foreground:window.__auditTerminal.options.theme.foreground}));
   assert.deepEqual(colors,{background:'rgb(30, 31, 34)',foreground:'rgb(188, 190, 196)'});
  }
  if(!startupRows)assert.ok(geometry.lines.some(s=>s.includes('Windows PowerShell')),'保留真实启动横幅');
  // 交互取证时留出明确的激活窗口；不降低后续前景身份校验。
  if(process.argv.includes('--wait-foreground')) {
   const ready=path.join(output,'terminal-ready');
   console.log(JSON.stringify({awaitForeground:shell.pid,window:geometry.window,ready}));
   await require('./capture-window-checkpoint.cjs').waitForCheckpoint(ready);
  }
  const pointerArgs=process.argv.includes('--require-pointer-clear')?['-PointerClearance','96']:[];
  await execute('pwsh',['-NoProfile','-File','tools/audit/capture-screen-region.ps1','-WindowHandle',String(geometry.window),'-ExpectedProcessId',String(shell.pid),'-Padding','0',...pointerArgs,'-Output',path.join(output,'actual-'+theme.toLowerCase()+'.png')],{windowsHide:true,timeout:30000});
  if(repeatRows)assert.equal(geometry.lines.filter(line=>line==='Ag 0123456789').length,8,'真实Shell必须恰好输出八行');
  await fs.writeFile(path.join(output,'actual-geometry.json'),JSON.stringify({pid:shell.pid,port,temporary,normalEntry:true,theme,pointerClearance: pointerArgs.length?96:null,outputMode:startupRows?'启动表达式的真实Shell八行输出':repeatRows?'真实Shell八行输出':'自然横幅',command:startupRows?terminalSettings.terminalCustomCommand:repeatRows?rowCommand:null,...geometry},null,2)+String.fromCharCode(10));
  if(process.argv.includes('--raster-size-probe')) {
   await require('./probe-terminal-raster-size.cjs').probeTerminalRasterSize(page,output,filename=>
    execute('pwsh',['-NoProfile','-File','tools/audit/capture-screen-region.ps1','-WindowHandle',String(geometry.window),'-ExpectedProcessId',String(shell.pid),'-Padding','0',...pointerArgs,'-Output',path.join(output,filename)],{windowsHide:true,timeout:30000}));
  }
  if(process.argv.includes('--baseline-probe')) {
   const baseline=await page.evaluate(()=>{
    const term=window.__auditTerminal,rows=term.element.querySelector('.xterm-rows'),row=rows.firstElementChild,style=getComputedStyle(row);
    const canvas=document.createElement('canvas'),context=canvas.getContext('2d');context.font=style.fontSize+' '+style.fontFamily;
    const metrics=context.measureText('d');
    return {rows:rows.getBoundingClientRect().toJSON(),row:row.getBoundingClientRect().toJSON(),font:style.font,lineHeight:style.lineHeight,ascent:metrics.fontBoundingBoxAscent,descent:metrics.fontBoundingBoxDescent,actualAscent:metrics.actualBoundingBoxAscent,dimensions:term._core._renderService.dimensions};
   });
   await fs.writeFile(path.join(output,'baseline.json'),JSON.stringify(baseline,null,2));
   const originCorrection=Math.ceil(baseline.rows.top*geometry.dpr)-baseline.rows.top*geometry.dpr;
   for(const phase of [0,.25,.5,.75,1,1.75,2,originCorrection]) {
    // 只变更审计实例的文字层相位，选区与命中网格不变；不作为产品修复。
    await page.evaluate(phase=>{const rows=window.__auditTerminal.element.querySelector('.xterm-rows');rows.style.position='relative';rows.style.top=(phase/devicePixelRatio)+'px';},phase);
    await delay(150);
    await execute('pwsh',['-NoProfile','-File','tools/audit/capture-screen-region.ps1','-WindowHandle',String(geometry.window),'-ExpectedProcessId',String(shell.pid),'-Padding','0','-Output',path.join(output,'phase-'+phase+'.png')],{windowsHide:true,timeout:30000});
   }
   await fs.writeFile(path.join(output,'origin-correction.json'),JSON.stringify({originCorrection,gridHeight:baseline.dimensions.css.cell.height,physicalHeight:baseline.dimensions.device.cell.height,productChanged:false},null,2));
  }
 } finally {
  if(server.listening)await new Promise(resolve=>server.close(resolve));
  await browser?.close().catch(()=>{});
  if(shell?.pid&&shell.exitCode===null)await execute('taskkill',['/PID',String(shell.pid),'/T','/F'],{windowsHide:true}).catch(()=>{});
  assert.ok(path.resolve(temporary).startsWith(path.resolve(os.tmpdir())+path.sep)&&path.basename(temporary).startsWith('Augit-terminal-pair-'));
  await fs.rm(temporary,{recursive:true,force:true,maxRetries:8,retryDelay:250});
  console.log(JSON.stringify({cleanedPid:shell?.pid,tempRemoved:temporary}));
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
