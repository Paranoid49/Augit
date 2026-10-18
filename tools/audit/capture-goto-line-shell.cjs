// 同夹具真实跳转行入口取证；检查确认/取消/错误反馈，finally清理本次实例。
const assert=require('node:assert/strict');
const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os'),net=require('node:net');
const {spawn,execFile}=require('node:child_process'),execute=require('node:util').promisify(execFile);
const {chromium}=require(process.argv[2]);
const output=path.resolve(process.argv[3]),delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
(async()=>{
  await fs.mkdir(output,{recursive:true});
  const temporary=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-goto-pair-'));
  const listener=net.createServer();let shell,browser;
  try {
    await new Promise(resolve=>listener.listen(0,'127.0.0.1',resolve));
    const port=listener.address().port;await new Promise(resolve=>listener.close(resolve));
    const settings=path.join(temporary,'settings.json');
    await fs.writeFile(settings,JSON.stringify({theme:'Light',textFontFamily:'Microsoft YaHei UI',textFontSize:12,monospaceFontFamily:'JetBrains Mono',fontSize:13}));
    shell=spawn(path.resolve('src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe'),[
      '--workspace','D:/JetBrains/PyCharm-2026.2.1-reference/fixture',
      '--open','notes.txt','--no-session-restore','--settings-file',settings,
      '--user-data-folder',path.join(temporary,'profile'),'--width','1400','--height','930',
      '--browser-args','--remote-debugging-port='+port],{windowsHide:false,stdio:'ignore'});
    console.log(JSON.stringify({pid:shell.pid,port,temporary}));
    for(let attempt=0;attempt<120;attempt++){
      try{browser=await chromium.connectOverCDP('http://127.0.0.1:'+port);break;}
      catch{assert.equal(shell.exitCode,null);await delay(250);}
    }
    assert.ok(browser);const page=browser.contexts()[0].pages()[0];
    await page.waitForFunction(()=>window.__augitReady&&window.__augitSettingsReady&&document.querySelector('.code-view')?.textContent.includes('Ag 0123456789'));
    assert.ok(!new URL(page.url()).searchParams.has('scene'),'必须从正常入口打开跳转行');
    await page.evaluate(()=>document.fonts.ready);
    const original=await page.locator('.code-view').textContent();
    const state=()=>page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
    const open=async()=>{
      await page.locator('.code-view').focus();await page.keyboard.press('Control+g');
      await page.waitForSelector('[data-compact-dialog]');
      await page.waitForFunction(()=>window.__augitNativeModalActive&&!document.querySelector('[data-native-capture]'));
      await delay(600);
    };
    if(process.argv.includes('--activate-main')) {
      const ready=path.join(output,'main-active-ready');
      console.log(JSON.stringify({activateMain:true,pid:shell.pid,ready}));
      await require('./capture-window-checkpoint.cjs').waitForCheckpoint(ready);
    }
    await open();
    const geometry=await page.evaluate(()=>{
      const dialog=document.querySelector('[data-compact-dialog]'),field=dialog.querySelector('input');
      const rect=node=>{const r=node.getBoundingClientRect();return [r.x,r.y,r.width,r.height];};
      return {dpr:devicePixelRatio,dialog:rect(dialog),field:rect(field),value:field.value,focused:document.activeElement===field,
        mark:rect(dialog.querySelector('.dialog-header .brand-mark')),title:rect(dialog.querySelector('.goto-line-title')),close:rect(dialog.querySelector('.dialog-header .icon-button')),
        buttons:[...dialog.querySelectorAll('.dialog-footer button')].map(node=>({text:node.textContent,rect:rect(node)})),
        font:getComputedStyle(field).font,focusBorder:getComputedStyle(field.parentElement,'::after').borderTopColor,
        focusWidth:getComputedStyle(field.parentElement,'::after').borderTopWidth,
        focusInset:getComputedStyle(field.parentElement,'::after').top,url:location.href,errors:window.__augitErrors};
    });
    assert.equal(geometry.value,'');assert.equal(geometry.focused,true);
    assert.ok(Math.abs(geometry.mark[0]-geometry.dialog[0]-12)<.1);
    assert.ok(Math.abs(geometry.mark[2]-16)<.1&&Math.abs(geometry.mark[3]-16)<.1);
    assert.ok(Math.abs(geometry.title[0]-geometry.dialog[0]-40)<.1);
    assert.ok(Math.abs(geometry.close[2]-32)<.1);
    assert.ok(Math.abs(geometry.close[0]+geometry.close[2]-geometry.dialog[0]-geometry.dialog[2])<.1);
    assert.equal(geometry.focusBorder,'rgb(56, 113, 225)');
    // WebView2在175%系统缩放下将CSS边框宽度向设备像素取整。
    assert.ok(parseFloat(geometry.focusWidth)<=2&&parseFloat(geometry.focusWidth)>2-1/geometry.dpr);
    assert.equal(geometry.focusInset,'2px');
    assert.ok(Math.abs(geometry.field[2]-200)<.1);
    assert.ok(Math.abs(geometry.field[3]-34)<.1);
    assert.deepEqual(geometry.buttons.map(button=>button.text),['OK','Cancel']);
    assert.ok(geometry.buttons[0].rect[0]<geometry.buttons[1].rect[0]);
    for(const action of ['confirm','cancel','close']) {
      await page.keyboard.press('Tab');
      assert.equal(await page.evaluate(()=>document.activeElement.dataset.compactAction),action);
    }
    await page.keyboard.press('Tab');
    assert.equal(await page.locator('[data-compact-field]').evaluate(node=>node===document.activeElement),true);
    await page.keyboard.press('Shift+Tab');
    assert.equal(await page.evaluate(()=>document.activeElement.dataset.compactAction),'close');
    await page.keyboard.press('Tab');
    const modal=await state();assert.equal(modal.depth,1);assert.equal(modal.ownerEnabled,false);
    if(geometry.dpr===1.75) {assert.equal(modal.rect.width,584);assert.equal(modal.rect.height,252);}
    if(process.argv.includes('--wait-foreground')) {
      const gate=path.join(output,'foreground-ready.signal');
      console.log(JSON.stringify({foregroundReady:true,pid:shell.pid,window:modal.window,gate}));
      let ready=false;
      for(let attempt=0;attempt<600;attempt++){
        try{await fs.access(gate);ready=true;break;}catch{await delay(200);}
      }
      assert.ok(ready,'等待前景观察超时');await fs.unlink(gate);await delay(300);
    }
    if(!process.argv.includes('--behavior-only')) await execute('pwsh',['-NoProfile','-File','tools/audit/capture-screen-region.ps1','-ExpectedProcessId',String(shell.pid),
      '-WindowHandle',String(modal.window),'-Padding','0','-Output',path.join(output,'actual-empty.png')],{windowsHide:true,timeout:30000});
    await page.locator('[data-compact-field]').fill('0');await page.keyboard.press('Enter');
    await page.waitForFunction(()=>document.querySelector('[data-compact-dialog] .footer-help')?.textContent==='Line numbers start at 1.');
    assert.equal((await state()).depth,1);
    assert.equal(await page.locator('[data-compact-dialog]').evaluate(dialog=>{
      const help=dialog.querySelector('.footer-help').getBoundingClientRect();
      return [...dialog.querySelectorAll('.dialog-footer button')].every(button=>{
        const r=button.getBoundingClientRect(),d=dialog.getBoundingClientRect();
        return r.top>=help.bottom-.1&&r.bottom<=d.bottom+.1;
      });
    }),true,'错误说明不应挤压或覆盖按钮');
    await page.locator('[data-compact-field]').fill('2');await page.keyboard.press('Enter');
    await page.waitForFunction(()=>!window.__augitNativeModalActive);
    assert.equal(await page.locator('.code-line.active').getAttribute('data-line'),'2');
    assert.equal(await page.locator('.code-view').evaluate(node=>node===document.activeElement),true);
    await open();await page.locator('[data-compact-action="cancel"]').click();
    await page.waitForFunction(()=>!window.__augitNativeModalActive);
    const closed=await state();assert.equal(closed.snapshotCount,0);assert.equal(closed.ownerEnabled,true);
    assert.equal(await page.locator('.code-view').textContent(),original);
    assert.deepEqual(await page.evaluate(()=>window.__augitErrors),[]);
    await fs.writeFile(path.join(output,'actual-state.json'),JSON.stringify({pid:shell.pid,port,temporary,geometry,modal,closed,
      screenCaptureRequested:!process.argv.includes('--behavior-only'),
      checks:{initialFocus:true,buttonLabel:true,invalidKeepsOpen:true,confirmNavigates:true,focusReturns:true,cancelCloses:true,textUnchanged:true}},null,2)+String.fromCharCode(10));
    console.log(JSON.stringify({passed:11,includes:['原七项行为','按钮顺序','键盘正反循环','默认实机尺寸','错误状态布局']}));
  } finally {
    if(listener.listening)await new Promise(resolve=>listener.close(resolve));
    if(browser)try{await browser.close();}catch{}
    if(shell?.pid&&shell.exitCode===null)try{await execute('taskkill.exe',['/PID',String(shell.pid),'/T','/F'],{windowsHide:true});}catch{}
    assert.ok(path.resolve(temporary).startsWith(path.resolve(os.tmpdir())+path.sep)&&path.basename(temporary).startsWith('Augit-goto-pair-'));
    await fs.rm(temporary,{recursive:true,force:true,maxRetries:8,retryDelay:250});
    console.log(JSON.stringify({cleanedPid:shell?.pid,temporaryRemoved:temporary}));
  }
})().catch(error=>{console.error(error);process.exitCode=1;});
