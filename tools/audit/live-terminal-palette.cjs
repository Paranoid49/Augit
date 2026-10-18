// 真实外壳/xterm显示验证；直接写入受控ANSI显示样本，不向Shell发送命令。
const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os'),net=require('node:net');
const assert=require('node:assert/strict');
const {spawn,execFile}=require('node:child_process');
const execute=require('node:util').promisify(execFile);
(async()=>{
 const {chromium}=require(process.argv[2]);
 const output=path.resolve(process.argv[3]);await fs.mkdir(output,{recursive:true});
 const temporary=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-terminal-colors-'));
 let shell,browser;
 try {
  await execute('git',['init',temporary],{windowsHide:true});
  const server=net.createServer();await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const port=server.address().port;await new Promise(resolve=>server.close(resolve));
  const settings=path.join(temporary,'settings.json');
  await fs.writeFile(settings,JSON.stringify({appearanceVersion:1,theme:'Dark',monospaceFontFamily:'JetBrains Mono',fontSize:13}));
  shell=spawn(path.resolve('src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe'),['--workspace',temporary,'--web-root',path.resolve('web'),'--no-session-restore','--settings-file',settings,'--user-data-folder',path.join(temporary,'profile'),'--browser-args',`--remote-debugging-port=${port}`],{stdio:'ignore',windowsHide:false});
  console.log(JSON.stringify({pid:shell.pid,port,temporary}));
  for(let i=0;i<120;i++){try{browser=await chromium.connectOverCDP(`http://127.0.0.1:${port}`);break;}catch{if(shell.exitCode!==null)throw Error('外壳提前退出');await new Promise(resolve=>setTimeout(resolve,250));}}
  assert.ok(browser);const page=browser.contexts()[0].pages()[0];
  await page.waitForFunction(()=>window.__augitReady&&window.__augitSettingsReady);
  await page.evaluate(()=>{
   const Original=window.Terminal;
   window.Terminal=class extends Original {constructor(options){super(options);window.__auditTerminal=this;}};
  });
  await page.locator('.tool-rail .rail-button[aria-label="Terminal"]').click();
  await page.waitForFunction(()=>window.__augitTerminalReady&&window.__auditTerminal);
  const records=[];
  for(const theme of ['dark','light']) {
   await page.locator('.titlebar [aria-label="Settings"]').click();
   await page.locator('[data-setting="theme"]').selectOption(theme==='dark'?'Dark':'Light');
   await page.locator('[data-settings-action="save"]').click();
   await page.waitForFunction(()=>!document.querySelector('.settings-window'));
   assert.equal(await page.evaluate(()=>document.querySelector('.terminal-view .xterm')===window.__auditTerminal.element),true);
   await page.evaluate(async ()=>{
    const term=window.__auditTerminal;
    term.clear();
    await new Promise(resolve=>term.write('\x1b[2J\x1b[H'+Array.from({length:16},(_,i)=>`\x1b[${i<8?30+i:90+i-8}mANSI ${String(i).padStart(2,'0')} 中文 ABC\x1b[0m\r\n`).join(''),resolve));
   });
   const record=await page.evaluate(()=>{
    const term=window.__auditTerminal,buffer=term.buffer.active;
    return {theme:document.body.dataset.theme||'light',options:term.options.theme,font:term.options.fontFamily,
     // 使用公开行列数和实际画布边界度量行格；不把xterm的行高系数当成字号倍数。
     metrics:(()=>{const screen=term.element.querySelector('.xterm-screen').getBoundingClientRect();return {
      deviceScale:devicePixelRatio,fontSize:term.options.fontSize,lineHeightOption:term.options.lineHeight,
      rows:term.rows,columns:term.cols,width:screen.width,height:screen.height,
      cellWidth:screen.width/term.cols,cellHeight:screen.height/term.rows,
      gridWidth:term.options.augitCellWidth,
      gridHeight:term.options.augitCellHeight,
      physicalCellHeight:screen.height/term.rows*devicePixelRatio};})(),
     lines:Array.from({length:16},(_,i)=>buffer.getLine(i)?.translateToString(true)),
     indices:Array.from({length:16},(_,i)=>buffer.getLine(i)?.getCell(0)?.getFgColor())};
   });
   assert.deepEqual(record.indices,Array.from({length:16},(_,i)=>i));
   assert.equal(record.theme,theme);
   record.lines.forEach((line,i)=>assert.equal(line,`ANSI ${String(i).padStart(2,'0')} 中文 ABC`));
   assert.equal(record.options.red,theme==='dark'?'#F0524F':'#CE0505');
   await page.evaluate(()=>{window.__auditTerminal.scrollToTop();window.__auditTerminal.focus();window.__auditTerminal.select(0,0,7);});
   await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
   const selectionColor=()=>page.locator('.xterm-selection div').first().evaluate(node=>getComputedStyle(node).backgroundColor);
   record.selectionActive=await selectionColor();
   assert.equal(record.selectionActive,theme==='dark'?'rgb(33, 66, 131)':'rgb(166, 210, 255)');
   await page.locator('.titlebar [aria-label="Settings"]').focus();
   await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
   record.selectionInactive=await selectionColor();
   assert.equal(record.selectionInactive,theme==='dark'?'rgb(76, 79, 86)':'rgb(181, 183, 189)');
   assert.equal(await page.evaluate(()=>window.__auditTerminal.getSelection()),'ANSI 00');
   assert.ok(record.metrics.rows>0&&record.metrics.columns>0,'终端须有有效行列');
   assert.ok(Number.isFinite(record.metrics.cellHeight)&&record.metrics.cellHeight>0,'实际行格须可度量');
   assert.equal(record.metrics.lineHeightOption,1,'终端使用参考基础行距，不再二次放大字体行格');
   assert.equal(record.metrics.gridWidth,Math.round(Math.round(13*record.metrics.deviceScale)*.6)/record.metrics.deviceScale);
   assert.ok(Math.abs(record.metrics.cellWidth-record.metrics.gridWidth)<.02,'实际列格采用参考步长');
   assert.equal(record.metrics.gridHeight,18);assert.ok(Math.abs(record.metrics.cellHeight-18)<.02,'实际行格采用参考字体高度');
   assert.ok(record.metrics.cellHeight>=17&&record.metrics.cellHeight<=19,`默认13px终端须接近参考18px行格，实测${record.metrics.cellHeight}`);
   // 设置提交后的区域刷新可能重挂宿主；只重试已知的节点分离，其他截图错误照常失败。
   for(let attempt=0;attempt<3;attempt++) {
    try {await page.locator('.terminal-view').screenshot({caret:'initial',path:path.join(output,`${theme}.png`)});break;}
    catch(error) {if(attempt===2||!String(error).includes('not attached'))throw error;}
   }
   records.push(record);
  }
  await page.locator('.titlebar [aria-label="Settings"]').click();
  await page.locator('[data-setting="theme"]').selectOption('Dark');
  assert.equal(await page.evaluate(()=>window.__auditTerminal.options.theme.red),'#F0524F');
  await page.locator('[data-settings-action="cancel"]').click();
  assert.equal(await page.evaluate(()=>document.querySelector('.terminal-view .xterm')===window.__auditTerminal.element),true);
  assert.equal(await page.evaluate(()=>window.__auditTerminal.options.theme.red),'#CE0505');
  // 正文已按用户裁决固定13号；不再操作已删除的字号/字体控件。
  await page.locator('.titlebar [aria-label="Settings"]').click();
  await page.locator('[data-settings-page="file-view"]').click();
  assert.equal(await page.locator('[data-fixed-code-font]').innerText(),'JetBrains Mono');
  assert.equal(await page.locator('[data-fixed-code-size]').innerText(),'13');
  assert.equal(await page.locator('[data-setting="codeFontSize"],[data-setting="monospaceFontFamily"]').count(),0);
  await page.locator('[data-settings-action="cancel"]').click();
  assert.equal(await page.evaluate(()=>window.__auditTerminal.options.fontSize),13);
  await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');await invoke('terminal/stop');});
  await fs.writeFile(path.join(output,'palette.json'),JSON.stringify({evidence:'真实xterm受控ANSI输出，非Shell输出或PyCharm配对',records,fixedFontVerified:true},null,2)+'\n');
 } finally {
  await browser?.close().catch(()=>{});
  if(shell?.pid&&shell.exitCode===null)await execute('taskkill',['/PID',String(shell.pid),'/T','/F'],{windowsHide:true}).catch(()=>{});
  await fs.rm(temporary,{recursive:true,force:true,maxRetries:8,retryDelay:250});console.log(`已清理 ${temporary}`);
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
