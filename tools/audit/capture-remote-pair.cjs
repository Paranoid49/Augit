// 真实远端嵌套取证：只取消，不保存或取回；所有退出路径释放本次实例。
const assert=require('node:assert/strict');
const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os'),net=require('node:net');
const {execFile}=require('node:child_process'),execute=require('node:util').promisify(execFile);
const {waitForCheckpoint}=require('./capture-window-checkpoint.cjs');
const {chromium}=require(process.argv[2]);
const output=path.resolve(process.argv[3]),fixture='D:/JetBrains/PyCharm-2026.2.1-reference/fixture';
const behaviorOnly=process.argv.includes('--behavior-only');
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
(async()=>{
 await fs.mkdir(output);
 const temporary=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-remote-pair-'));
 const listener=net.createServer();let shell,browser;
 try {
  const original=await fs.readFile(path.join(fixture,'.git/config'));
  await new Promise(resolve=>listener.listen(0,'127.0.0.1',resolve));
  const port=listener.address().port;await new Promise(resolve=>listener.close(resolve));
  const settings=path.join(temporary,'settings.json');
  // 与用户正常启动共用入口及默认profile，仅复制配置以免取证改变用户会话。
  await fs.copyFile(path.join(process.env.LOCALAPPDATA,'Augit/settings.json'),settings);
  const launch=require('./daily-launch-command.cjs')(process.cwd(),settings,port,fixture);
  const started=await execute(launch.file,launch.args,{env:launch.env,timeout:120000});
  const pid=Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
  assert.ok(Number.isSafeInteger(pid)&&pid>0,'日常启动器未返回实际Augit PID');
  shell={pid,exitCode:null};
  await fs.writeFile(path.join(output,'launch.json'),JSON.stringify({launcher:'tools/start-augit.ps1',workspace:fixture,profile:'default-session',isolatedSettingsCopy:true,forcedBuild:true,sourceWeb:true,command:launch.args,buildOutput:started.stdout},null,2));
  console.log(JSON.stringify({pid:shell.pid,port,temporary}));
  for(let i=0;i<120;i++){try{browser=await chromium.connectOverCDP('http://127.0.0.1:'+port);break;}catch{assert.equal(shell.exitCode,null);await delay(250);}}
  assert.ok(browser);const page=browser.contexts()[0].pages()[0];
  await page.waitForFunction(()=>window.__augitReady&&window.__augitSettingsReady&&window.__augitLive?.remotes);
  assert.ok(!new URL(page.url()).searchParams.has('scene'));
  // 从本实例实际内存映射取程序集路径，再核对磁盘哈希；不能用构建目标身份替代已加载身份。
  const mapped=await execute(process.env.AUGIT_AUDIT_PYTHON||'D:/Python/Python3119/python.exe',['-B','tools/audit/read-process-mapped-images.py',String(pid)],{windowsHide:true,timeout:15000,env:{...process.env,PYTHONUTF8:'1'}});
  const mappedIdentity=JSON.parse(mapped.stdout);
  assert.equal(mappedIdentity.pid,pid);
  for(const name of ['Augit.dll','Augit.Infrastructure.dll'])assert.ok(mappedIdentity.mappedImages.some(item=>item.name===name),'未找到本实例实际映射程序集：'+name);
  await fs.writeFile(path.join(output,'mapped-images.json'),JSON.stringify(mappedIdentity,null,2));
  const checkpoint=async name=>{const gate=path.join(output,name+'.signal');console.log(JSON.stringify({checkpoint:name,gate,pid:shell.pid}));await waitForCheckpoint(gate);};
  const state=()=>page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
  const settle=async depth=>{
   await page.waitForFunction(()=>window.__augitNativeModalActive&&!document.querySelector('[data-native-capture]'));
   for(let i=0;i<100;i++){if((await state()).depth===depth){await delay(500);return;}await delay(100);}throw Error('模态层数未到达 '+depth);
  };
  const reports=[];
  const capture=async(name,selector,depth)=>{
   await settle(depth);const modal=await state();
   const geometry=await page.locator(selector).evaluate(dialog=>{
    const rect=node=>{const r=node.getBoundingClientRect();return [r.x,r.y,r.width,r.height];};
    return {dpr:devicePixelRatio,dialog:rect(dialog),title:dialog.querySelector('.dialog-header')?.textContent.trim(),
     fields:[...dialog.querySelectorAll('input')].map(node=>({field:node.dataset.remoteField,value:node.value,checked:node.checked,focused:node===document.activeElement,rect:rect(node)})),
     buttons:[...dialog.querySelectorAll('button')].map(node=>({text:node.textContent.trim(),action:node.dataset.remoteAction||node.dataset.remoteDefine,disabled:node.disabled,rect:rect(node)})),
     bodyOverflow:dialog.querySelector('.dialog-body').scrollHeight-dialog.querySelector('.dialog-body').clientHeight,
     tableRows:dialog.querySelectorAll('.remote-row[data-remote-index]').length,font:getComputedStyle(dialog).font,url:location.href};
   });
   await fs.writeFile(path.join(output,name+'.state.json'),JSON.stringify({modal,geometry},null,2));
   const identity=await require('./settings-runtime-identity.cjs').collectSettingsRuntimeIdentity(page,process.cwd(),'Debug');
   await fs.writeFile(path.join(output,name+'.runtime.json'),JSON.stringify(identity,null,2));
   if(!behaviorOnly){
    await checkpoint(name);
    await execute('pwsh',['-NoProfile','-File','tools/audit/capture-screen-region.ps1','-ExpectedProcessId',String(shell.pid),'-WindowHandle',String(modal.window),'-Padding','0','-PointerClearance','96','-Output',path.join(output,name+'.png')],{windowsHide:true,timeout:30000});
   }
   reports.push({name,modal,geometry});
  };
  if(!behaviorOnly)await checkpoint('main-active');
  // 走正式Push快捷键及可见链接，不调用审计场景或宿主桩。
  await page.locator('.top-chip.branch-chip').click();
  await page.waitForSelector('.branches-popover');
  await page.locator('.branches-popover [data-popover-action="push"]').click();
  await page.waitForSelector('.push-dialog');await settle(1);
  await page.locator('.push-dialog a[href$="remote.html"]').click();
  await page.waitForSelector('.remote-dialog');await capture('actual-empty','.remote-dialog',2);
  assert.equal(reports[0].geometry.tableRows,0);
  for(const action of ['remove','edit'])assert.equal(await page.locator('.remote-dialog [data-remote-action="'+action+'"]').isDisabled(),true);
  await page.locator('.remote-dialog [data-remote-action="add"]').click();
  const focusBeforeDefine='[data-remote-action="add"]';
  await page.waitForSelector('.remote-define-dialog');await capture('actual-define','.remote-define-dialog',3);
  const fields=reports[1].geometry.fields;
  assert.equal(reports[1].geometry.bodyOverflow,0,'默认字号的完整新增表单不应出现滚动条');
  assert.equal(fields.find(item=>item.field==='name').value,'origin');
  assert.equal(fields.find(item=>item.field==='fetchUrl').focused,true);
  assert.equal(fields.find(item=>item.field==='fetch').checked,true);
  await page.locator('.remote-define-dialog button[data-remote-define="cancel"]').click();
  await page.waitForSelector('.remote-define-dialog',{state:'detached'});await settle(2);
  assert.equal(await page.locator('.remote-dialog').count(),1);
  await page.waitForFunction(selector=>document.activeElement===document.querySelector('.remote-dialog '+selector),focusBeforeDefine);
  await page.locator('.remote-dialog [data-remote-action="ok"]').click();
  await page.waitForSelector('.remote-dialog',{state:'detached'});await settle(1);
  assert.equal(await page.locator('.push-dialog').count(),1);
  await page.keyboard.press('Escape');await page.waitForFunction(()=>!window.__augitNativeModalActive);
  const closed=await state();assert.equal(closed.ownerEnabled,true);assert.equal(closed.snapshotCount,0);
  assert.deepEqual(await fs.readFile(path.join(fixture,'.git/config')),original);
  assert.deepEqual(await page.evaluate(()=>window.__augitErrors),[]);
  await fs.writeFile(path.join(output,'result.json'),JSON.stringify({reports,closed,configUnchanged:true,screenCaptured:!behaviorOnly,pixelRestorationVerified:false},null,2));
  console.log(JSON.stringify({completed:true,captures:behaviorOnly?0:reports.length,configUnchanged:true,nestedClose:true}));
 }finally{
  if(listener.listening)await new Promise(resolve=>listener.close(resolve));
  if(browser)await browser.close().catch(()=>{});
  if(shell?.pid&&shell.exitCode===null)await execute('taskkill.exe',['/PID',String(shell.pid),'/T','/F'],{windowsHide:true}).catch(()=>{});
  assert.ok(path.dirname(temporary)===os.tmpdir()&&path.basename(temporary).startsWith('Augit-remote-pair-'));
  await fs.rm(temporary,{recursive:true,force:true,maxRetries:8,retryDelay:250});
  console.log(JSON.stringify({cleanedPid:shell?.pid,temporaryRemoved:temporary}));
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
