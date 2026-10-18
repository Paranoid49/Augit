// 正常入口长URL核查；只在临时仓库登记URL，不连接网络或保存表单。
const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os'),net=require('node:net');
const assert=require('node:assert/strict'),{execFile}=require('node:child_process'),execute=require('node:util').promisify(execFile);
const {waitAsyncCondition}=require('./wait-async-condition.cjs');
(async()=>{
 const output=path.resolve(process.argv[3]);await fs.mkdir(output);
 const temporary=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-remote-url-'));
 const save=(name,value)=>fs.writeFile(path.join(output,name),JSON.stringify(value,null,2)+String.fromCharCode(10));
 let pid,browser,port;
 try {
  const workspace=path.join(temporary,'workspace');await fs.mkdir(workspace);
  const git=(...args)=>execute('git',args,{cwd:workspace,windowsHide:true});
  await git('init','--initial-branch=main');
  await git('remote','add','origin','https://example.invalid/'+('WideRemote'.repeat(5))+'.git');
  const config=await fs.readFile(path.join(workspace,'.git/config'));
  const settings=path.join(temporary,'settings.json');await fs.copyFile(path.join(process.env.LOCALAPPDATA,'Augit/settings.json'),settings);
  const server=net.createServer();await new Promise(r=>server.listen(0,'127.0.0.1',r));port=server.address().port;await new Promise(r=>server.close(r));
  const launch=require('./daily-launch-command.cjs')(process.cwd(),settings,port,workspace);
  const started=await execute(launch.file,launch.args,{env:launch.env,timeout:120000,windowsHide:true});
  pid=Number(started.stdout.split('AUGIT_PID=')[1]?.trim());assert.ok(Number.isSafeInteger(pid)&&pid>0);
  await save('launch.json',{pid,port,temporary,workspace,launcher:'tools/start-augit.ps1',forcedBuild:true,settingsCopy:true,profile:'default-session',cdp:true});
  console.log(JSON.stringify({pid,port,temporary}));
  const {chromium}=require(process.argv[2]);
  await waitAsyncCondition(async()=>{try{browser=await chromium.connectOverCDP('http://127.0.0.1:'+port);return true;}catch{return false;}},{label:'真实WebView连接',timeout:30000});
  const page=browser.contexts()[0].pages()[0];page.setDefaultTimeout(15000);
  await page.waitForFunction(()=>window.__augitReady&&window.__augitSettingsReady&&window.__augitLive?.remotes?.remotes?.length===1);
  const mapped=await execute('D:/Python/Python3119/python.exe',['-B','tools/audit/read-process-mapped-images.py',String(pid)],{windowsHide:true,env:{...process.env,PYTHONUTF8:'1'}});
  await save('mapped-images.json',JSON.parse(mapped.stdout));
  await page.locator('.top-chip.branch-chip').click();
  await page.locator('.branches-popover [data-popover-action=push]').click();
  await page.locator('.push-dialog a').filter({hasText:/remote/i}).click();
  await page.waitForSelector('.remote-dialog');
  await waitAsyncCondition(()=>page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');const state=await invoke('window/modal-state');return state.depth===2&&!window.__augitNativeBackgroundBusy&&!document.querySelector('[data-native-capture]');}),{label:'远端模态就绪'});
  const geometry=await page.locator('.remote-dialog').evaluate(dialog=>{
   const style=getComputedStyle(dialog),ctx=document.createElement('canvas').getContext('2d'),rows=[...dialog.querySelectorAll('.remote-row')];
   let name=30,url=250;ctx.font='600 '+style.fontSize+' '+style.fontFamily;
   for(const row of rows){name=Math.max(name,Math.ceil(ctx.measureText(row.querySelector('.remote-name').textContent).width));url=Math.max(url,Math.ceil(ctx.measureText(row.querySelector('.remote-url').textContent).width));}
   const rect=node=>{const r=node.getBoundingClientRect();return [r.x,r.y,r.width,r.height];};
   return {dpr:devicePixelRatio,expected:Math.max(624/1.75,name+40+url+34),width:dialog.getBoundingClientRect().width,rows:rows.length,urlWeight:getComputedStyle(rows[0].querySelector('.remote-url')).fontWeight,dialog:rect(dialog),table:rect(dialog.querySelector('.remote-table')),header:rect(dialog.querySelector('.remote-table-head')),row:rect(rows[0]),cells:[...rows[0].children].map(rect),selected:rows[0].getAttribute('aria-selected')};
  });
  await save('geometry.json',geometry);
  assert.equal(geometry.rows,1);assert.equal(geometry.urlWeight,'400');assert.ok(Math.abs(geometry.width-geometry.expected)<0.1,JSON.stringify(geometry));
  await save('runtime-identity.json',await require('./settings-runtime-identity.cjs').collectSettingsRuntimeIdentity(page,process.cwd(),'Debug'));
  if(process.argv.includes('--capture')) {
   const state=await page.evaluate(async()=>{const{invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
   await save('modal-state.json',state);
   console.log(JSON.stringify({checkpoint:'capture',pid,gate:path.join(output,'capture.signal')}));
   await require('./capture-window-checkpoint.cjs').waitForCheckpoint(path.join(output,'capture.signal'));
   await execute('pwsh',['-NoProfile','-File','tools/audit/capture-screen-region.ps1','-ExpectedProcessId',String(pid),'-WindowHandle',String(state.window),'-Padding','0','-PointerClearance','96','-Output',path.join(output,'actual-data.png')],{windowsHide:true,timeout:30000});
  }
  await page.locator('.remote-dialog [data-remote-action=ok]').click();await page.waitForSelector('.remote-dialog',{state:'detached'});await page.keyboard.press('Escape');
  await page.waitForFunction(()=>!window.__augitNativeModalActive);
  assert.deepEqual(await fs.readFile(path.join(workspace,'.git/config')),config);assert.deepEqual(await page.evaluate(()=>window.__augitErrors),[]);
  await save('result.json',{passed:true,configUnchanged:true,geometry,screenCaptured:process.argv.includes('--capture'),pixelRestorationVerified:false});console.log(JSON.stringify({completed:true,geometry}));
 } finally {
  if(browser)await browser.close().catch(()=>{});
  if(pid)await execute('taskkill',['/PID',String(pid),'/T','/F'],{windowsHide:true}).catch(()=>{});
  assert.equal(path.dirname(temporary),os.tmpdir());assert.ok(path.basename(temporary).startsWith('Augit-remote-url-'));
  await fs.rm(temporary,{recursive:true,force:true,maxRetries:8,retryDelay:250});
  await save('cleanup.json',{pid,port,temporary,removed:true});
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
