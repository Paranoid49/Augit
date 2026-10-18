// 默认隔离配置和profile；日常启动器模式复用真实构建、源网页和默认profile路径。
const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os'),net=require('node:net');
const {spawn,execFile}=require('node:child_process'),execute=require('node:util').promisify(execFile);
const assert=require('node:assert/strict'),delay=ms=>new Promise(r=>setTimeout(r,ms));
const {waitAsyncCondition}=require('./wait-async-condition.cjs');
(async()=>{
 const {chromium}=require(process.argv[2]),output=path.resolve(process.argv[3]);
 const dailyLauncher=process.argv.includes('--daily-launcher');
 const openingOptions=require('./settings-opening-options.cjs').settingsOpeningOptions(process.argv);
 if(dailyLauncher)for(const flag of ['--release','--source-web','--scene-settings'])assert.ok(!process.argv.includes(flag),'日常启动器不能混用入口覆盖：'+flag);
 const temp=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-independent-settings-'));
 const evidence=new Map(),results=[],opens=[];let shell,browser,main,responseProbe;
 evidence.set('opening-times.json',opens);
 evidence.set('opening-options.json',openingOptions);
 try{
  await fs.copyFile(path.join(process.env.LOCALAPPDATA,'Augit/settings.json'),path.join(temp,'settings.json'));
  if(process.argv.includes('--dark')) {
   const settingsPath=path.join(temp,'settings.json'),settings=JSON.parse(await fs.readFile(settingsPath,'utf8'));
   settings.theme='Dark';await fs.writeFile(settingsPath,JSON.stringify(settings));
  }
  const server=net.createServer();await new Promise(r=>server.listen(0,'127.0.0.1',r));const port=server.address().port;await new Promise(r=>server.close(r));
  const configuration=process.argv.includes('--release')?'Release':'Debug';
  const launchArgs=['--workspace',process.cwd(),'--settings-file',path.join(temp,'settings.json'),'--user-data-folder',path.join(temp,'profile'),'--browser-args','--remote-debugging-port='+port];
  if(process.argv.includes('--source-web'))launchArgs.push('--web-root',path.resolve('web'));
  if(process.argv.includes('--scene-settings'))launchArgs.push('--scene','settings');
  if(dailyLauncher){
   const launch=require('./daily-launch-command.cjs')(process.cwd(),path.join(temp,'settings.json'),port);
   const result=await execute(launch.file,launch.args,{env:launch.env,timeout:120000});
   const pid=Number(result.stdout.split('AUGIT_PID=')[1]?.trim().split(/s/)[0]);
   assert.ok(Number.isInteger(pid)&&pid>0,'日常启动器未返回实际Augit PID');
   shell={pid,exitCode:null};
   evidence.set('launch.json',{configuration,launcher:'tools/start-augit.ps1',command:launch.args,
    profile:'default-session',isolatedSettingsCopy:true,forcedBuild:true,sourceWeb:true,buildOutput:result.stdout});
  }else{
   shell=spawn(path.resolve('src/Augit.Shell/bin/'+configuration+'/net10.0-windows/win-x64/Augit.exe'),launchArgs,{stdio:'ignore'});
   evidence.set('launch.json',{configuration,launchArgs,executable:shell.spawnfile,isolatedSettingsCopy:true});
  }
  console.log(JSON.stringify({pid:shell.pid,port,temp}));
  if(dailyLauncher){
   // 只读本实例命令行和加载模块，不能仅用磁盘DLL哈希推断实际加载位置。
   const identity=await execute('powershell.exe',['-NoProfile','-Command',
    '$p=Get-CimInstance Win32_Process -Filter "ProcessId = '+shell.pid+'"; [pscustomobject]@{pid=$p.ProcessId;path=$p.ExecutablePath;commandLine=$p.CommandLine;modules=@((Get-Process -Id '+shell.pid+').Modules | Where-Object {$_.ModuleName -eq "Augit.dll" -or $_.ModuleName -eq "AudioDevProps2.dll"} | Select-Object ModuleName,FileName)} | ConvertTo-Json -Depth 4'],{windowsHide:true,timeout:15000});
   evidence.set('process-identity.json',JSON.parse(identity.stdout));
  }
  for(let i=0;i<120;i++){try{browser=await chromium.connectOverCDP('http://127.0.0.1:'+port);break;}catch{assert.equal(shell.exitCode,null);await delay(250);}}
  assert.ok(browser);main=browser.contexts()[0].pages()[0];main.setDefaultTimeout(15000);
  await main.waitForFunction(()=>window.__augitReady&&window.__augitSettingsReady);
  if(process.argv.includes('--message-probe'))responseProbe=await require('./shell-response-probe.cjs').startResponseProbe(shell.pid);
  const trace=async page=>page.evaluate(()=>{const b=chrome.webview,p=b.postMessage;window.__settingsTrace=[];window.__settingsInputTrace=[];
   const record=(name,detail={})=>window.__settingsInputTrace.push({name,timeOrigin:performance.timeOrigin,milliseconds:performance.now(),...detail});
   document.addEventListener('click',event=>{if(event.target.closest('.titlebar [aria-label=Settings]'))record('settings-click',{trusted:event.isTrusted});},true);
   b.postMessage=function(raw){const r=typeof raw==='string'?JSON.parse(raw):raw;window.__settingsTrace.push(r.method);if(r.method==='window/settings-open')record('settings-open-request');return p.call(this,raw);};});
  await trace(main);
  await browser.contexts()[0].addInitScript(()=>{
   if(!location.search.includes('surface=settings'))return;
   // 只读性能记录，区分字体请求等待、脚本长任务和原生创建；不改变显示或计时条件。
   window.__settingsLongTasks=[];
   if(PerformanceObserver.supportedEntryTypes.includes('longtask'))new PerformanceObserver(list=>{
    for(const entry of list.getEntries())window.__settingsLongTasks.push({start:entry.startTime,duration:entry.duration,name:entry.name});
   }).observe({type:'longtask',buffered:true});
   const b=chrome.webview,post=b.postMessage,add=b.addEventListener,ids=new Set(),timings=new Map();window.__heldWrites=[];window.__settingsStartupRequests=[];window.__settingsRequestTimings=[];
   add.call(b,'message',e=>{const r=typeof e.data==='string'?JSON.parse(e.data):e.data;const t=timings.get(r.id);if(t){t.end=performance.now();t.duration=t.end-t.start;timings.delete(r.id);}});
   b.postMessage=function(raw){const r=typeof raw==='string'?JSON.parse(raw):raw;window.__settingsStartupRequests.push(r.method);const t={method:r.method,start:performance.now()};timings.set(r.id,t);window.__settingsRequestTimings.push(t);if(r.method==='settings/write')ids.add(r.id);return post.call(this,raw);};
   b.addEventListener=function(name,handler){return add.call(this,name,name==='message'?e=>{const r=typeof e.data==='string'?JSON.parse(e.data):e.data;if(window.__holdWriteResponse&&ids.has(r.id))window.__heldWrites.push(()=>handler(e));else handler(e);}:handler);};
  });
  const invoke=async(method,params={})=>main.evaluate(async({method,params})=>{const{invoke}=await import('/src/bridge.js');return invoke(method,params,0);},{method,params});
  if(process.argv.includes('--close-owner-during-startup')){
   assert.ok(dailyLauncher,'初始化关闭证据必须走正常启动器');
   const identity=require('./settings-runtime-identity.cjs').collectSettingsRuntimeIdentity;
   evidence.set('main-runtime-identity.json',await identity(main,process.cwd(),configuration));
   const before=await invoke('window/settings-open');
   evidence.set('startup-close-before.json',before);
   // 与主窗关闭按钮使用同一协议；不等待即将销毁页面的响应，不注入产品延时。
   evidence.set('startup-close-result.json',await require('./settings-startup-close.cjs').observeStartupClose({before,pid:shell.pid,
    close:()=>main.evaluate(()=>chrome.webview.postMessage(JSON.stringify({id:900000001,method:'window/close',params:{}})))}));
   return;
  }
  const open=async()=>{
   const preexisting=(await invoke('window/settings-state')).active;
   await main.evaluate(()=>{window.__settingsInputTrace.length=0;});
   const opening=Date.now(),timeline=require('./settings-opening-timeline.cjs').createOpeningTimeline();
   timeline.mark('click-start');
   await main.locator('.titlebar [aria-label=Settings]').click();
   timeline.mark('click-returned');
   // 创建控制器和字体加载期间也重复请求，不能只检查已经显示的窗口。
   if(!openingOptions.singleOpen){
    const pendingOpens=await Promise.all(Array.from({length:8},()=>invoke('window/settings-open')));
    assert.ok(pendingOpens[0].window>0);
    assert.ok(pendingOpens.every(state=>state.active&&state.window===pendingOpens[0].window));
   }
   timeline.mark('duplicate-requests-returned');
   let readyState;
   await waitAsyncCondition(async()=>{timeline.mark('state-request');readyState=await invoke('window/settings-state');timeline.mark('state-reply',{active:readyState.active,ready:readyState.ready});return readyState.active&&readyState.ready;},{label:'设置原生呈现'});
   const hostReady=Date.now()-opening;
   timeline.mark('host-ready',{presentedMilliseconds:readyState.presentation.presentedMilliseconds});
   if(!preexisting)assert.ok(hostReady>=readyState.presentation.presentedMilliseconds,'外部就绪计时不能早于宿主呈现');
   let pages=[];for(let i=0;i<50;i++){pages=browser.contexts().flatMap(c=>c.pages()).filter(p=>p.url().includes('surface=settings'));if(pages.length)break;await delay(100);}
   if(!pages.length)console.log(JSON.stringify({pages:browser.contexts().flatMap(c=>c.pages()).map(p=>p.url()),targets:await(await fetch('http://127.0.0.1:'+port+'/json')).json()}));
   assert.equal(pages.length,1);
   const page=pages[0];page.setDefaultTimeout(15000);await page.waitForFunction(()=>window.__augitReady,null,{polling:50});await trace(page);
   assert.equal(await page.evaluate(()=>document.activeElement===document.querySelector('[data-settings-filter]')),true,'设置初始键盘焦点必须留在搜索框');
   const currentState=await invoke('window/settings-state');
   assert.equal(currentState.window,readyState.window,'等待后必须仍是同一个设置窗口');
   const nativeTiming=currentState.presentation;
   const startup=await page.evaluate(()=>({requests:window.__settingsStartupRequests,requestTimings:window.__settingsRequestTimings,
    resources:performance.getEntriesByType('resource').map(e=>({name:new URL(e.name).pathname,start:e.startTime,duration:e.duration,transfer:e.transferSize,decoded:e.decodedBodySize,
     fetchStart:e.fetchStart,domainLookupStart:e.domainLookupStart,domainLookupEnd:e.domainLookupEnd,connectStart:e.connectStart,connectEnd:e.connectEnd,requestStart:e.requestStart,responseStart:e.responseStart,responseEnd:e.responseEnd})),
    longTasks:window.__settingsLongTasks,
    navigation:performance.getEntriesByType('navigation').map(e=>({domInteractive:e.domInteractive,domContentLoaded:e.domContentLoadedEventEnd,load:e.loadEventEnd})),
    typography:document.body.dataset.typographyPreview,settingsReady:window.__augitSettingsReady}));
   assert.equal(startup.requests.filter(method=>method==='settings/read').length,1,'首次设置装配只能读取一次配置');
   assert.equal(startup.typography,'ready');assert.equal(startup.settingsReady,true);
   if(process.argv.includes('--expect-minimal-settings')){
    assert.equal(new URL(page.url()).pathname,'/settings-surface.html');
    for(const name of ['/vendor/xterm/xterm.js','/vendor/xterm/xterm.css','/vendor/xterm/addon-fit.js','/src/current-find.js','/src/image-preview.js'])
     assert.equal(startup.resources.some(resource=>resource.name===name),false,'设置不应加载未使用资源：'+name);
    assert.equal(await page.evaluate(()=>typeof window.Terminal),'undefined');
    assert.equal(await main.evaluate(()=>typeof window.Terminal),'function','主界面终端资源必须保留');
   }
   if(process.argv.includes('--capture-first-layout')&&opens.length===0){
    await fs.mkdir(output,{recursive:true});
    await page.screenshot({path:path.join(output,'first-layout-cdp.png')});
    evidence.set('first-layout.json',await page.evaluate(()=>({method:'cdp-layout-diagnostic',systemPresentationEvidence:false,
     url:location.href,dpr:devicePixelRatio,typography:document.body.dataset.typographyPreview,
     dialogs:document.querySelectorAll('.settings-window .dialog').length,width:innerWidth,height:innerHeight})));
   }
   const stages=['controllerStartedMilliseconds','controllerReadyMilliseconds','scriptRegistrationStartedMilliseconds','scriptRegistrationReadyMilliseconds','navigationStartedMilliseconds','prepareStartedMilliseconds','cloakedMilliseconds','boundsSyncedMilliseconds','controllerVisibleMilliseconds','windowShownMilliseconds','preparedMilliseconds','renderedMilliseconds','presentedMilliseconds'];
   for(let i=0;i<stages.length;i++){assert.ok(Number.isFinite(nativeTiming[stages[i]]));if(i)assert.ok(nativeTiming[stages[i]]>=nativeTiming[stages[i-1]],'启动阶段必须单调');}
   const pageTheme=await page.evaluate(()=>document.body.dataset.theme||'light');
   assert.equal(nativeTiming.surfaceColor,pageTheme==='dark'?'#26282C':'#E9EAEE');
   assert.equal(nativeTiming.initialTheme,pageTheme,'重开设置不得沿用过时的启动主题');
   const inputTrace=await main.evaluate(()=>window.__settingsInputTrace);
   opens.push({requestedAtUtcMilliseconds:opening,observedAtUtcMilliseconds:Date.now(),hostReadyMilliseconds:hostReady,cdpPageReadyMilliseconds:Date.now()-opening,nativeTiming,startup,timeline:timeline.snapshot(),inputTrace});return page;
  };
  const closed=async()=>{const started=Date.now();await waitAsyncCondition(async()=>!(await invoke('window/settings-state')).active,{label:'设置原生关闭'});return Date.now()-started;};
  if(process.argv.includes('--preview-reload-cycle')){
   assert.ok(dailyLauncher,'主题回读验收必须走正常启动器');
   const identity=require('./settings-runtime-identity.cjs').collectSettingsRuntimeIdentity;
   evidence.set('main-runtime-identity.json',await identity(main,process.cwd(),configuration));
   const mapped=await execute(process.env.AUGIT_AUDIT_PYTHON||'D:/Python/Python3119/python.exe',['-B','tools/audit/read-process-mapped-images.py',String(shell.pid)],{windowsHide:true,env:{...process.env,PYTHONUTF8:'1'}});
   evidence.set('mapped-images.json',JSON.parse(mapped.stdout));
   // 浅色由缺省属性表示，与产品 currentThemeName 的约定一致。
   const initial=await invoke('settings/read'),initialTheme=await main.evaluate(()=>document.body.dataset.theme||'light'),opposite=initialTheme==='light'?'Dark':'Light',checks=[];
   const observe=async(page,label,expected)=>{
    await main.waitForFunction(theme=>(document.body.dataset.theme||'light')===theme,expected);
    await page.waitForFunction(theme=>(document.body.dataset.theme||'light')===theme,expected);
    const state=await invoke('window/settings-state'),color=expected==='dark'?'#26282C':'#E9EAEE';
    assert.equal(state.surfaceColor,color);assert.equal(state.presentation.surfaceColor,color);
    checks.push({label,mainTheme:await main.evaluate(()=>document.body.dataset.theme||'light'),settingsTheme:await page.evaluate(()=>document.body.dataset.theme||'light'),state});
   };
   const page=await open();evidence.set('settings-runtime-identity.json',await identity(page,process.cwd(),configuration));
   await page.locator('[data-setting=theme]').selectOption(opposite);
   await page.locator('[data-settings-action=apply]').click();
   await page.waitForFunction(()=>document.querySelector('.settings-dialog').dataset.settingsState==='saved');
   await page.locator('[data-setting=theme]').selectOption(initialTheme==='light'?'Light':'Dark');
   await observe(page,'保存后继续预览',initialTheme);
   await page.locator('[data-settings-action=cancel]').click();await closed();
   await main.waitForFunction(theme=>(document.body.dataset.theme||'light')===theme,opposite.toLowerCase());
   const reopened=await open();await reopened.locator('[data-setting=theme]').selectOption(initial.theme);
   await observe(reopened,'重开预览原主题',initialTheme);
   await reopened.locator('[data-settings-action=save]').click();await closed();
   await waitAsyncCondition(async()=>{const saved=await invoke('settings/read');return saved.theme===initial.theme;},{label:'恢复隔离配置原主题'});
   await main.waitForFunction(theme=>(document.body.dataset.theme||'light')===theme,initialTheme);
   assert.deepEqual(await main.evaluate(()=>window.__augitErrors),[]);
   evidence.set('preview-reload-cycle.json',{checks,initialTheme,final:await invoke('settings/read'),passed:true,raceForced:false,limitation:'真实UI保存、继续预览、取消和重开；未重排应答，不证明竞态在实机被触发。'});return;
  }
  if(process.argv.includes('--typography-cycle')){
   assert.ok(dailyLauncher,'字体回读验收必须走正常启动器');
   const identity=require('./settings-runtime-identity.cjs').collectSettingsRuntimeIdentity;
   evidence.set('main-runtime-identity.json',await identity(main,process.cwd(),configuration));
   const mapped=await execute(process.env.AUGIT_AUDIT_PYTHON||'D:/Python/Python3119/python.exe',['-B','tools/audit/read-process-mapped-images.py',String(shell.pid)],{windowsHide:true,env:{...process.env,PYTHONUTF8:'1'}});
   evidence.set('mapped-images.json',JSON.parse(mapped.stdout));
   const initial=await invoke('settings/read'),sizes=[initial.fontSize===14?16:14,initial.fontSize],checks=[];
   const page=await open();evidence.set('settings-runtime-identity.json',await identity(page,process.cwd(),configuration));
   for(const size of sizes){
    await page.locator('[data-setting=fontSize]').fill(String(size));
    await page.locator('[data-settings-action=apply]').click();
    await page.waitForFunction(()=>document.querySelector('.settings-dialog').dataset.settingsState==='saved');
    await main.waitForFunction(size=>window.__augitLive.settings.fontSize===size&&document.body.dataset.typographyPreview==='ready'&&parseFloat(getComputedStyle(document.documentElement).fontSize)===size,size);
    const saved=await invoke('settings/read');assert.equal(saved.fontSize,size);
    checks.push(await main.evaluate(()=>({settings:window.__augitLive.settings,fontSize:getComputedStyle(document.documentElement).fontSize,sideWidth:document.documentElement.style.getPropertyValue('--augit-side-width'),bottomHeight:document.documentElement.style.getPropertyValue('--augit-bottom-height')})));
   }
   // 真正修改字号后点确定，记录即将销毁页面的桥接请求，不改变应答或字体等待。
   const closeRequests=[],prefix='AUGIT_SETTINGS_OK_REQUEST:';
   page.on('console',message=>{if(message.text().startsWith(prefix))closeRequests.push(JSON.parse(message.text().slice(prefix.length)));});
   await page.evaluate(prefix=>{const bridge=chrome.webview,post=bridge.postMessage;bridge.postMessage=function(raw){const request=typeof raw==='string'?JSON.parse(raw):raw;console.debug(prefix+JSON.stringify({method:request.method,milliseconds:performance.now()}));return post.call(this,raw);};},prefix);
   await page.locator('[data-setting=fontSize]').fill(String(sizes[0]));
   const closeStarted=Date.now();await page.locator('[data-settings-action=save]').click();await closed();
   const closeElapsedMilliseconds=Date.now()-closeStarted;
   await main.waitForFunction(size=>window.__augitLive.settings.fontSize===size&&document.body.dataset.typographyPreview==='ready'&&parseFloat(getComputedStyle(document.documentElement).fontSize)===size,sizes[0]);
   evidence.set('ok-close-requests.json',{closeRequests,closeElapsedMilliseconds,expectedFontSize:sizes[0],saved:await invoke('settings/read'),limitation:'附CDP只读请求观测，耗时包含驱动派发，不代表无CDP桌面呈现。'});
   assert.equal(closeRequests.filter(request=>request.method==='settings/write').length,1);
   assert.equal(closeRequests.filter(request=>request.method==='window/settings-close').length,1);
   if(process.argv.includes('--expect-no-close-reload'))assert.equal(closeRequests.filter(request=>request.method==='settings/read').length,0,'确定关闭不得等待子窗再次回读');
   const reopened=await open();assert.equal(Number(await reopened.locator('[data-setting=fontSize]').inputValue()),sizes[0]);
   await reopened.locator('[data-setting=fontSize]').fill(String(initial.fontSize));
   await reopened.locator('[data-settings-action=apply]').click();
   await reopened.waitForFunction(()=>document.querySelector('.settings-dialog').dataset.settingsState==='saved');
   await reopened.locator('[data-settings-action=cancel]').click();await closed();
   const final=await invoke('settings/read');assert.equal(final.fontSize,initial.fontSize);
   assert.equal(final.projectPanelWidth,initial.projectPanelWidth);assert.equal(final.bottomPanelHeight,initial.bottomPanelHeight);
   await main.waitForFunction(size=>window.__augitLive.settings.fontSize===size&&document.body.dataset.typographyPreview==='ready',initial.fontSize);
   assert.deepEqual(await main.evaluate(()=>window.__augitErrors),[]);
   evidence.set('typography-cycle.json',{initial,checks,final,passed:true,raceForced:false,limitation:'正常UI保存与关闭重开回归；未强制重排回读或字体应答，不证明竞态已在实机复现。'});return;
  }
  if(process.argv.includes('--startup-only')){
   const closes=[];
   for(let i=0;i<openingOptions.startupCount;i++){const page=await open();
    if(i===0&&process.argv.includes('--runtime-identity')){
     const identity=require('./settings-runtime-identity.cjs').collectSettingsRuntimeIdentity;
     evidence.set('main-runtime-identity.json',await identity(main,process.cwd(),configuration));
     evidence.set('settings-runtime-identity.json',await identity(page,process.cwd(),configuration));
    }
    const closeStarted=Date.now();
    await page.locator('[data-settings-action=cancel]').click();await closed();closes.push(Date.now()-closeStarted);}
   assert.deepEqual(await main.evaluate(()=>window.__augitErrors),[]);
   evidence.set('startup-only-result.json',{passed:true,opens:opens.length,closes,
    limitation:'关闭耗时为CDP取消点击到设置状态inactive的上界，含驱动派发与25ms轮询，不代表无CDP桌面关闭耗时。'});return;
  }
  if(process.argv.includes('--memory')) {
   const measure=require('./modal-process-memory.cjs').measure;
   const before=await measure(shell.pid),page=await open(),during=await measure(shell.pid);
   await page.locator('[data-settings-action=cancel]').click();await closed();await delay(500);
   const after=await measure(shell.pid);evidence.set('memory.json',{before,during,after,limitation:'真实仓库单次实例父链采样，包含缓存和仓库活动；不是与旧架构配对的内存收益证明。'});
  }
  if(process.argv.includes('--scene-settings')) {
   await waitAsyncCondition(async()=>(await invoke('window/settings-state')).ready,{label:'设置场景呈现'});
   const sceneWindow=await invoke('window/settings-state');
   const scenePage=await open();assert.equal((await invoke('window/settings-state')).window,sceneWindow.window);
   await scenePage.locator('[data-settings-action=cancel]').click();await closed();
   results.push({action:'scene-settings-shared-entry',passed:true});
  }
  if(process.argv.includes('--record-cycle')){
   const recorder=require('./record-settings-cycle.cjs');
   await recorder.recordSettingsCycle({output,shell,main,open,closed,invoke,evidence,...recorder.cycleOptions(process.argv)});
   if(process.argv.includes('--record-only')){
    const state=await invoke('window/modal-state');assert.equal(state.active,false);assert.equal(state.snapshotCount,0);assert.equal(state.ownerEnabled,true);
    assert.equal(browser.contexts().flatMap(c=>c.pages()).filter(p=>p.url().includes('surface=settings')).length,0);
    assert.deepEqual(await main.evaluate(()=>window.__augitErrors),[]);
    evidence.set('record-only-result.json',{passed:true,state});return;
   }
  }
  for(const action of ['cancel','save','escape','title']){
   const settings=await open(),before=await invoke('window/settings-state');
   assert.equal(before.visible,true);
   assert.equal(before.presentation.transitionsDisabledRequestAccepted,true);
   if(action==='cancel'&&process.argv.includes('--record')){
    await fs.mkdir(output,{recursive:true});
    await fs.writeFile(path.join(output,'ready.json'),JSON.stringify({pid:shell.pid,port,temp,state:before}));
    console.log('录屏等待前景观察，创建record-continue文件后采集');
    for(let i=0;i<240;i++){try{await fs.access(path.join(output,'record-continue'));break;}catch{await delay(500);}}
    await settings.locator('[data-settings-filter]').evaluate(n=>n.blur());
    // 整个Augit前景窗口取证，所有者或设置谁在前景均记录真实句柄。
    await execute('pwsh',['-NoProfile','-File','tools/audit/capture-screen-region.ps1','-ExpectedProcessId',String(shell.pid),'-Output',path.join(output,'native.png'),'-Padding','0'],{windowsHide:true,timeout:30000});
    const source=JSON.parse(await fs.readFile(path.join(output,'native.png.json'),'utf8'));
    const recorderArgs=require('./refresh-recorder-arguments.cjs').recorderArguments(source.frameRectPhysical,path.join(output,'refresh-screen.mkv'),true,true);
    const monitor=await require('./recording-foreground.cjs').startForegroundRecording(output,shell.pid,source.windowHandle);
    const recorder=spawn('ffmpeg',recorderArgs,{windowsHide:true,stdio:['pipe','ignore','pipe']});let log='';recorder.stderr.on('data',data=>log+=data);
    const ended=new Promise((resolve,reject)=>{recorder.once('error',reject);recorder.once('close',resolve);});
    const deadline=require('./refresh-recorder-arguments.cjs').recordingDeadline(recorder);
    try{
     await delay(1000);for(let i=0;i<12;i++){await main.evaluate(i=>{const n=document.querySelector('.statusbar');n.dataset.auditRefresh=String(i);n.style.borderTopColor=i%2?'rgb(1,2,3)':'rgb(4,5,6)';},i);await delay(350);}
     await delay(1000);require('./refresh-recorder-arguments.cjs').finishEventRecording(recorder,true);assert.equal(await ended,0);
    }finally{deadline.dispose();if(recorder.exitCode===null){recorder.kill();await ended;}const foreground=await monitor.stop();evidence.set('refresh-screen.json',{method:'ffmpeg-ddagrab-desktop-ffv1',arguments:recorderArgs,frameRectPhysical:source.frameRectPhysical,width:source.imageSize[0],height:source.imageSize[1],foregroundEvidence:foreground,repeatStaticFrames:true,backgroundUpdates:12,recorderPid:recorder.pid});evidence.set('recording-log.json',{log});}
   }
   if(action==='cancel'&&process.argv.includes('--early-checkpoint')){
    await fs.mkdir(output,{recursive:true});await fs.writeFile(path.join(output,'ready.json'),JSON.stringify({pid:shell.pid,port,temp,state:before}));console.log('首次开窗等待观察');
    for(let i=0;i<240;i++){try{await fs.access(path.join(output,'early-continue'));break;}catch{await delay(500);}}
   }
   await Promise.all(Array.from({length:8},()=>invoke('window/settings-open')));
   assert.equal((await invoke('window/settings-state')).window,before.window);
   assert.equal(await main.locator('.settings-window').count(),0);assert.equal(await settings.locator('.settings-window .dialog').count(),1);
   assert.equal((await invoke('window/modal-state')).active,false);assert.equal(browser.contexts().flatMap(c=>c.pages()).length,2);
   for(const name of ['file-view','git','terminal','appearance'])await settings.locator('[data-settings-page='+name+']').click();
   await main.evaluate(()=>{document.querySelector('.statusbar').append(document.createTextNode(''));});await delay(300);
   const body=await settings.evaluate(()=>({errors:window.__augitErrors,error:window.__augitError??null,trace:window.__settingsTrace,hasWorkspace:!!document.querySelector('.app-main')}));
   assert.equal(body.error,null);
   assert.deepEqual(body.errors,[]);assert.equal(body.hasWorkspace,false);assert.ok(body.trace.every(r=>!r.startsWith('window/modal-')&&!r.startsWith('workspace/')&&!r.startsWith('terminal/')));
   const start=Date.now();
   if(action==='escape')await settings.keyboard.press('Escape');
   else if(action==='title')await settings.locator('.dialog-header [aria-label=Close]').click();
   else await settings.locator('[data-settings-action='+action+']').click();
   await closed();results.push({action,elapsedMilliseconds:Date.now()-start,body});
  }
  const settings=await open(),original=await main.evaluate(()=>document.body.dataset.theme||'light');
  await settings.locator('[data-setting=theme]').selectOption(original==='light'?'Dark':'Light');
  await main.waitForFunction(v=>(document.body.dataset.theme||'light')!==v,original);
  const preview=await invoke('window/settings-state'),previewColor=original==='light'?'#26282C':'#E9EAEE';
  assert.equal(preview.surfaceColor,previewColor);assert.equal(preview.presentation.surfaceColor,previewColor);
  await settings.locator('[data-settings-action=cancel]').click();await closed();
  await main.waitForFunction(v=>(document.body.dataset.theme||'light')===v,original);results.push({action:'theme-cancel',passed:true});
  assert.equal((await invoke('window/settings-state')).surfaceColor,original==='light'?'#E9EAEE':'#26282C');
  // 真正制造隔离设置文件不可写；失败必须留窗并保留草稿。
  const failure=await open(),settingsPath=path.join(temp,'settings.json'),backup=path.join(temp,'settings.saved.json');
  await failure.locator('[data-setting=theme]').selectOption(original==='light'?'Dark':'Light');
  await fs.rename(settingsPath,backup);await fs.mkdir(settingsPath);
  try{
   await failure.locator('[data-settings-action=save]').click();
   await failure.waitForFunction(()=>document.querySelector('.settings-dialog').dataset.settingsState==='failed');
   assert.equal((await invoke('window/settings-state')).active,true);assert.equal(await failure.locator('[data-setting=theme]').inputValue(),original==='light'?'Dark':'Light');
  }finally{await fs.rmdir(settingsPath);await fs.rename(backup,settingsPath);}
  await failure.locator('[data-settings-action=save]').click();await closed();results.push({action:'persistence-failure-retry',passed:true});
  const late=await open();await late.evaluate(()=>{window.__holdWriteResponse=true;});
  await late.locator('[data-settings-action=save]').click();await late.waitForFunction(()=>window.__heldWrites.length>0);
  await late.locator('[data-settings-action=cancel]').click();await closed();
  const fresh=await open();assert.equal(await fresh.locator('.settings-dialog').getAttribute('data-settings-state'),'draft');
  await fresh.locator('[data-settings-action=cancel]').click();await closed();results.push({action:'cancel-delayed-save-reopen',passed:true});
  const apply=await open();await apply.locator('[data-setting=theme]').selectOption(original==='light'?'Light':'Dark');
  await apply.locator('[data-settings-action=apply]').click();await apply.waitForFunction(()=>document.querySelector('.settings-dialog').dataset.settingsState==='saved');
  assert.equal((await invoke('window/settings-state')).active,true);await apply.locator('[data-settings-action=cancel]').click();await closed();
  const reopened=await open();assert.equal(await reopened.locator('[data-setting=theme]').inputValue(),original==='light'?'Light':'Dark');
  if(process.argv.includes('--checkpoint')){
   await fs.mkdir(output,{recursive:true});await fs.writeFile(path.join(output,'ready.json'),JSON.stringify({pid:shell.pid,port,temp}));console.log('等待实机观察：创建continue文件后清理');
   for(let i=0;i<600;i++){try{await fs.access(path.join(output,'continue'));break;}catch{await delay(500);}}
  }
  await reopened.locator('[data-settings-action=cancel]').click();await closed();results.push({action:'apply-reopen',passed:true});
  const state=await main.evaluate(()=>({trace:window.__settingsTrace,errors:window.__augitErrors}));
  assert.deepEqual(state.errors,[]);assert.equal(state.trace.filter(r=>r.startsWith('window/modal-')&&r!=='window/modal-state').length,0);
  evidence.set('result.json',{results,...state});console.log(JSON.stringify({results:results.map(({action,elapsedMilliseconds,passed})=>({action,elapsedMilliseconds,passed})),errors:state.errors}));
 }catch(error){
  if(main&&!main.isClosed())evidence.set('failure.json',await main.evaluate(()=>({error:window.__augitError,errors:window.__augitErrors,trace:window.__settingsTrace})));
  if(browser)evidence.set('failure-pages.json',await Promise.all(browser.contexts().flatMap(c=>c.pages()).map(async p=>({url:p.url(),state:await p.evaluate(()=>({ready:window.__augitReady,error:window.__augitError,errors:window.__augitErrors,visibility:document.visibilityState,text:document.body?.innerText.slice(0,3000),trace:window.__settingsTrace})).catch(e=>String(e))}))));
  throw error;
 }
 finally{
  try{if(responseProbe)evidence.set('message-response.json',await responseProbe.stop());}
  catch(error){evidence.set('message-response-failure.json',{error:String(error)});process.exitCode=1;}
  if(browser)await browser.close().catch(()=>{});if(shell?.pid&&shell.exitCode===null)await execute('taskkill.exe',['/PID',String(shell.pid),'/T','/F']).catch(()=>{});
  try{await fs.mkdir(output,{recursive:true});for(const[name,value]of evidence)await fs.writeFile(path.join(output,name),Buffer.isBuffer(value)?value:JSON.stringify(value,null,2));}
  finally{
  assert.equal(path.dirname(temp),os.tmpdir());assert.ok(path.basename(temp).startsWith('Augit-independent-settings-'));
  await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:250});console.log(JSON.stringify({cleaned:shell?.pid,temp}));
  }
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
