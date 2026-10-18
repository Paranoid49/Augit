// 只采集独立设置窗与关闭后的同位置屏幕，不把窗口内CDP图当作系统阴影。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const net = require('node:net');
const {spawn, execFile} = require('node:child_process');
const {promisify} = require('node:util');
const execute = promisify(execFile);
const {chromium} = require(process.argv[2]);
const output = path.resolve(process.argv[3]);
const theme = process.argv[4] || 'Dark';
const dailyLauncher = require('./shadow-daily-options.cjs').validateDailyShadowOptions(process.argv);
const normalEntry = dailyLauncher || process.argv.includes('--normal-entry');
const softwareRendering = process.argv.includes('--software-rendering');
const disableDirectComposition = process.argv.includes('--disable-direct-composition');
const disableNativeOcclusion = process.argv.includes('--disable-native-occlusion');
const { captureSettings, captureSceneArguments, verifyCaptureAppearance } = require('./refresh-capture-startup.cjs');
const inspectSnapshot = process.argv.includes('--inspect-snapshot');
const uniformBackground = process.argv.includes('--uniform-background');
const gotoLine = process.argv.includes('--goto-line');
const independentSettings = !gotoLine;
const settingsAudit = require('./shadow-settings-surface.cjs');
if (independentSettings) {
  for (const flag of ['--inspect-snapshot','--inspect-cover','--probe-cover-clip','--refresh-video','--place-inside-body'])
    assert.ok(!process.argv.includes(flag), `独立设置不支持旧重挂取证参数：${flag}`);
}
const dialogSelector = gotoLine ? '[data-compact-dialog]' : '.settings-window .dialog';
assert.ok(!gotoLine || uniformBackground, '跳转行取证须先恢复真实正文');
const placeInsideBody = process.argv.includes('--place-inside-body');
const radiusIndex = process.argv.indexOf('--shadow-radius');
const shadowRadius = radiusIndex < 0 ? 128 : Number(process.argv[radiusIndex+1]);
assert.ok(Number.isInteger(shadowRadius) && shadowRadius > 0 && shadowRadius <= 256);
const referenceIndex = process.argv.indexOf('--reference-frame');
const referencePath = referenceIndex < 0 ? null : process.argv[referenceIndex + 1];
assert.ok(referenceIndex < 0 || referencePath && !referencePath.startsWith('--'), '参考尺寸参数须提供原始屏幕元数据路径');
assert.ok(!placeInsideBody || uniformBackground && referencePath, '正文定位须有真实正文及参考尺寸');
assert.ok(['Dark', 'Light'].includes(theme));
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

(async () => {
  await fs.mkdir(output, {recursive: false});
  const audit = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-shadow-pair-'));
  const server = net.createServer();
  let shell, browser, focusBrowser, expectedFrame;
  try {
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const port = server.address().port;
    await new Promise(resolve => server.close(resolve));
    const settings = path.join(audit, 'settings.json');
    const workspace = 'D:/JetBrains/PyCharm-2026.2.1-reference/fixture';
    if(dailyLauncher)await fs.copyFile(path.join(process.env.LOCALAPPDATA,'Augit/settings.json'),settings);
    else await fs.writeFile(settings, JSON.stringify(captureSettings(theme, workspace, normalEntry)));
    const browserArguments = require('./refresh-capture-startup.cjs').captureBrowserArguments(port, softwareRendering, disableDirectComposition, disableNativeOcclusion);
    const launchArgs = ['--workspace',workspace,
       ...captureSceneArguments(normalEntry, uniformBackground),
       '--settings-file',settings,'--user-data-folder',path.join(audit,'profile'),
       '--width',uniformBackground ? '1640' : '1400','--height',uniformBackground ? '970' : '930','--browser-args',browserArguments];
    const bundledWeb = process.argv.includes('--bundled-web');
    if (!bundledWeb) launchArgs.push('--web-root',path.resolve('web'));
    if(dailyLauncher){
      const launch=require('./daily-launch-command.cjs')(process.cwd(),settings,port);
      launch.env={...require('./settings-no-cdp.cjs').normalEnvironment(process.env),WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS:'--remote-debugging-port='+port};
      const result=await execute(launch.file,launch.args,{env:launch.env,timeout:120000,windowsHide:true});
      const pid=Number(result.stdout.match(/AUGIT_PID=(\d+)/)?.[1]);
      assert.ok(Number.isSafeInteger(pid)&&pid>0,'日常启动器未返回PID');shell={pid,exitCode:null};
      await fs.writeFile(path.join(output,'launch.json'),JSON.stringify({pid,launcher:'tools/start-augit.ps1',forcedBuild:true,profile:'default-session',settingsCopy:true,remoteDebuggingPort:port,buildOutput:result.stdout},null,2)+'\n');
    }else{
      await fs.writeFile(path.join(output,'launch.json'),JSON.stringify({launchArgs,normalEntry,bundledWeb},null,2)+'\n');
      shell = spawn(path.resolve('src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe'), launchArgs,
        {windowsHide:false, stdio:'ignore'});
    }
    console.log(JSON.stringify({pid:shell.pid,port,temp:audit,theme}));
    for (let attempt=0;attempt<120;attempt++) {
      try {browser=await chromium.connectOverCDP(`http://127.0.0.1:${port}`);break;}
      catch {assert.equal(shell.exitCode,null,'采集实例意外退出');await delay(250);}
    }
    assert.ok(browser,'调试接口未就绪');
    if (softwareRendering || disableDirectComposition || disableNativeOcclusion) {
      const session = await browser.newBrowserCDPSession();
      try {
        const system = await session.send('SystemInfo.getInfo');
        const featureStatus = system.gpu?.featureStatus;
        await fs.writeFile(path.join(output, 'rendering-mode.json'), JSON.stringify({
          softwareRendering, disableDirectComposition, disableNativeOcclusion, browserArguments, featureStatus, auxAttributes: system.gpu?.auxAttributes,
          diagnosticOnly: true, normalRenderingVerified: false
        }, null, 2) + String.fromCharCode(10));
        if (softwareRendering) assert.equal(featureStatus?.gpu_compositing, 'disabled_software', '软件合成参数必须实际生效');
        if (disableDirectComposition) assert.equal(system.gpu?.auxAttributes?.directComposition, false, 'DirectComposition禁用参数必须实际生效');
        if (disableNativeOcclusion) {
          assert.equal(featureStatus?.gpu_compositing, 'enabled', '遮挡诊断必须保留GPU合成');
          assert.equal(system.gpu?.auxAttributes?.directComposition, true, '遮挡诊断必须保留DirectComposition');
        }
      } finally { await session.detach(); }
    }
    const page=browser.contexts()[0].pages()[0];
    await page.waitForFunction(()=>window.__augitReady&&window.__augitSettingsReady&&window.__augitGitReady);
    await page.evaluate(() => document.fonts.ready);
    const appearance = await page.evaluate(() => ({ theme: window.__augitLive.settings.theme,
      uiSize: getComputedStyle(document.body).fontSize, uiFamily: getComputedStyle(document.body).fontFamily,
      codeFamily: window.__augitLive.settings.monospaceFontFamily, codeSize: window.__augitLive.settings.codeFontSize,
      dpr: devicePixelRatio, url: location.href }));
    verifyCaptureAppearance(appearance, theme);
    if(dailyLauncher)await fs.writeFile(path.join(output,'runtime-main.json'),JSON.stringify(await require('./settings-runtime-identity.cjs').collectSettingsRuntimeIdentity(page,process.cwd(),'Debug'),null,2)+'\n');
    if (normalEntry) assert.ok(!new URL(appearance.url).searchParams.has('scene'));
    if (uniformBackground) {
      // 使用真实文件查看器并关闭侧栏，不注入背景、不修改截图像素。
      try {
        await page.waitForFunction(()=>!window.__augitLive.sessionRestoring&&document.querySelector('.code-view')?.textContent.includes('Ag 0123456789'));
      } catch(error) {
        const failure=await page.evaluate(()=>({restoring:window.__augitLive.sessionRestoring,tabs:window.__augitLive.tabs,settings:window.__augitLive.settings,errors:window.__augitErrors,body:document.body.innerText}));
        await fs.writeFile(path.join(output,'restore-failure.json'),JSON.stringify(failure,null,2)+'\n');
        throw error;
      }
      for (const region of ['side','bottom']) {
        // 首击先激活非当前工具区，第二击才收起，遵守真实入口语义。
        for (let attempt=0;attempt<2;attempt++) {
          const layout=await page.evaluate(()=>window.__augitLive.layout);
          if (layout[region+'Hidden']||layout.collapsed===region||region==='bottom'&&!layout.bottom) break;
          const tool=region==='side'?layout.side:layout.bottom==='git'?'history':layout.bottom;
          await page.locator(`.tool-rail [data-tool="${tool}"]`).click();
          await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
        }
      }
      await page.waitForSelector('.code-view');
      await page.evaluate(() => document.fonts.ready);
    }
    let capturePlacement;
    if (placeInsideBody) {
      const reference=JSON.parse(await fs.readFile(referencePath,'utf8'));
      const body=await page.locator('.code-view').evaluate(node=>{const r=node.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height};});
      console.log(JSON.stringify({body,layout:await page.evaluate(()=>window.__augitLive.layout),shadowRadius}));
      assert.equal(reference.scale,appearance.dpr);
      capturePlacement=require('./shadow-capture-placement.cjs').placement(reference,body,shadowRadius);
      const p=capturePlacement;
      // 首次创建前设置取证几何；已有原生模态只跟随尺寸变化，不跟随DOM位置。
      await page.addStyleTag({content:`${dialogSelector} {position:fixed!important;left:${p.x}px!important;top:${p.y}px!important;width:${p.width}px!important;height:${p.height}px!important;margin:0!important;transform:none!important;}`});
      await fs.writeFile(path.join(output,'placement.json'),JSON.stringify({referencePath,body,...p,scope:'仅取证几何，不计默认布局验收'},null,2)+'\n');
    }
    const state=()=>page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-state');});
    if (process.argv.includes('--activate-main')) {
      // 在主窗仍可交互时确认前景，避免激活已被模态禁用的所有者。
      const ready = path.join(output, 'main-active-ready');
      console.log(JSON.stringify({ activateMain: true, pid: shell.pid, ready }));
      await require('./capture-window-checkpoint.cjs').waitForCheckpoint(ready);
    }
    let dialogPage = page;
    try {
      // 原生检查点先把指针留在采样区外；键盘开关不能再把光晕搬回待测阴影。
      if (gotoLine) { await page.locator('.code-view').focus(); await page.keyboard.press('Control+g'); }
      else await page.locator('.titlebar [aria-label="Settings"]').click({timeout:5000});
    }
    catch(error) {
      // 首次输入可能已触发重挂，随后CDP重试被新表单遮挡；只接受确实打开的目标。
      if (independentSettings) {
        if (!(await settingsAudit.settingsState(page)).settings.active) throw error;
      } else if(!await page.evaluate(selector=>window.__augitNativeModalActive&&!!document.querySelector(selector),dialogSelector))throw error;
    }
    if (independentSettings) {
      dialogPage = await settingsAudit.settingsSurface(browser,page);
      if(dailyLauncher)await fs.writeFile(path.join(output,'runtime-settings.json'),JSON.stringify(await require('./settings-runtime-identity.cjs').collectSettingsRuntimeIdentity(dialogPage,process.cwd(),'Debug'),null,2)+'\n');
    }
    else await page.waitForFunction(()=>window.__augitNativeModalActive&&!document.querySelector('.dialog[data-native-capture]'));
    if (referencePath) {
      const reference = JSON.parse(await fs.readFile(referencePath, 'utf8'));
      assert.equal(reference.capture, 'screen');
      assert.equal(reference.resampled, false);
      assert.ok(reference.frameRectPhysical?.length === 4 && reference.frameRectPhysical.every(Number.isInteger)
        && Number.isFinite(reference.scale) && reference.scale > 0);
      expectedFrame = [reference.frameRectPhysical[2]-reference.frameRectPhysical[0],
        reference.frameRectPhysical[3]-reference.frameRectPhysical[1]];
      assert.ok(expectedFrame.every(value => value > 0), '参考窗口尺寸必须为正数');
      // 同尺寸仅控制取证窗口几何，不改变阴影、正文、系统DPI或原始PNG。
      if (!independentSettings) await page.locator(dialogSelector).evaluate((dialog, reference) => {
        if (devicePixelRatio !== reference.scale) throw Error('参考与实际DPI不同');
        const [left, top, right, bottom] = reference.frameRectPhysical;
        dialog.style.width = (right - left) / devicePixelRatio + 'px';
        dialog.style.height = (bottom - top) / devicePixelRatio + 'px';
      }, reference);
      await page.waitForFunction(() => !window.__augitNativeBackgroundBusy && !document.querySelector('[data-native-capture],[data-native-background-capture]'));
      await delay(700);
    }
    const modal=independentSettings ? settingsAudit.verifyState(await settingsAudit.settingsState(page),true) : await state();
    if (!independentSettings) assert.equal(modal.depth,1);
    assert.equal(modal.ownerEnabled,false);
    // 不把两帧就绪视为系统动画结束；这里额外留出稳定时间并以第二份背景核验。
    await delay(700);
    if (process.argv.includes('--position-window')) {
      // 只暂停以便通过原生标题栏移动窗口；超时仍进入finally，不改DOM或PNG。
      const ready = path.join(output, 'position-ready');
      console.log(JSON.stringify({positionWindow:modal.window, ready}));
      let positioned = false;
      for (let attempt=0;attempt<240;attempt++) {
        try {await fs.access(ready); positioned=true; break;} catch {await delay(500);}
      }
      assert.ok(positioned, '等待窗口定位超时');
      await fs.unlink(ready);
      await delay(700);
    }
    if (inspectSnapshot) {
      // 记录快照控制器布局与CDP诊断图；此图不计原始响应或系统截图。
      const candidates = browser.contexts().flatMap(context => context.pages()).filter(candidate => candidate !== page);
      const snapshots = [];
      for (const candidate of candidates) {
        const source = await candidate.evaluate(async () => {
          const image = document.querySelector('img[src^="https://snapshot.augit.local/image.png"]');
          if (!image) return null;
          await image.decode();
          const rect = image.getBoundingClientRect();
          return {method:'cdp-snapshot-controller', naturalWidth: image.naturalWidth, naturalHeight: image.naturalHeight,
            rect: {x:rect.x,y:rect.y,width:rect.width,height:rect.height},
            devicePixelRatio, innerWidth, innerHeight, revision:image.dataset.revision};
        });
        if (!source) continue;
        assert.ok(Math.abs(source.rect.width * source.devicePixelRatio - source.naturalWidth) < 0.02,'快照显示宽度必须保持原始物理像素');
        assert.ok(Math.abs(source.rect.height * source.devicePixelRatio - source.naturalHeight) < 0.02,'快照显示高度必须保持原始物理像素');
        const name = `snapshot-source-${snapshots.length}`;
        await candidate.screenshot({path:path.join(output,name+'.png')});
        snapshots.push({name,...source});
      }
      assert.equal(snapshots.length,1,'独立Settings必须恰有一个背景快照');
      await fs.writeFile(path.join(output,'snapshot-source.json'),JSON.stringify(snapshots,null,2)+'\n');
    }
    async function capture(name, windowHandle, region, inactiveFocus) {
      const target=path.join(output,name+'.png');
      const args=['-NoProfile','-File','tools/audit/capture-screen-region.ps1','-ExpectedProcessId',String(shell.pid),'-WindowHandle',String(windowHandle),'-Output',target];
      args.push('-PointerClearance','128');
      if(region)args.push('-RegionFrom',region);else args.push('-Padding',String(shadowRadius));
      if(inactiveFocus)args.push('-ExpectedActivation','Inactive','-ExpectedForegroundWindow',String(inactiveFocus.windowHandle),
        '-ExpectedForegroundProcessId',String(inactiveFocus.processId));
      await execute('pwsh',args,{windowsHide:true,timeout:30000});
      return JSON.parse(await fs.readFile(target+'.json','utf8'));
    }
    if (independentSettings) {
      // 检查点可能激活所有者；走产品重复入口激活唯一设置窗，不放宽前景门禁。
      await page.evaluate(async()=>{const{invoke}=await import('/src/bridge.js');await invoke('window/settings-open');});
      const current = settingsAudit.verifyState(await settingsAudit.settingsState(page),true);
      assert.equal(current.window,modal.window,'重复入口不得更换设置窗口');
      await delay(700);
    }
    const foreground=await capture('settings',modal.window);
    if(process.argv.includes('--probe-cover-clip')) {
      await page.waitForFunction(()=>!window.__augitNativeBackgroundBusy);
      await require('./probe-cover-clip.cjs').probeCoverClip(page,dialogSelector,output);
    }
    if (expectedFrame) assert.deepEqual([foreground.frameRectPhysical[2]-foreground.frameRectPhysical[0],
      foreground.frameRectPhysical[3]-foreground.frameRectPhysical[1]],expectedFrame,'实际系统外框须与参考尺寸完全一致');
    if(process.argv.includes('--inspect-cover')) {
      // 调试接口读取加载的源PNG；控制器图不冒充原始系统合成截图。
      await page.waitForFunction(()=>!window.__augitNativeBackgroundBusy);
      await delay(500);
      const beforeCover=await state();
      try {
        const covered=await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');return invoke('window/modal-cover');});
        assert.equal(covered.captureCoverReady,true);
        await fs.writeFile(path.join(output,'cover-state.json'),JSON.stringify({beforeCover,covered},null,2)+'\n');
        let covers=[];
        for(let attempt=0;attempt<50&&!covers.length;attempt++) {
          covers=[];
          for(const candidate of browser.contexts().flatMap(context=>context.pages()).filter(candidate=>candidate!==page)) {
            const matches=await candidate.evaluate(({width,height})=>!!document.querySelector('img')
              &&Math.abs(innerWidth*devicePixelRatio-width)<2&&Math.abs(innerHeight*devicePixelRatio-height)<2,covered.rect);
            if(matches)covers.push(candidate);
          }
          if(!covers.length)await delay(20);
        }
        assert.equal(covers.length,1);
        const decoded=await covers[0].evaluate(async()=>{
          const image=document.querySelector('img');await image.decode();
          const rect=image.getBoundingClientRect();
          return {url:image.src,width:image.naturalWidth,height:image.naturalHeight,
            rect:{x:rect.x,y:rect.y,width:rect.width,height:rect.height},dpr:devicePixelRatio,
            method:'cdp-loaded-resource-content',imageRendering:getComputedStyle(image).imageRendering};
        });
        const session=await covers[0].context().newCDPSession(covers[0]);
        try {
          await session.send('Page.enable');
          const tree=await session.send('Page.getResourceTree');
          const resource=await session.send('Page.getResourceContent',{frameId:tree.frameTree.frame.id,url:decoded.url});
          assert.equal(resource.base64Encoded,true);
          await fs.writeFile(path.join(output,'cover-source.png'),Buffer.from(resource.content,'base64'));
        } finally {await session.detach();}
        await fs.writeFile(path.join(output,'cover-source.json'),JSON.stringify(decoded,null,2)+'\n');
        await capture('cover-visible-live',modal.window);
        await page.locator('.settings-window .dialog').evaluate(node=>node.dataset.nativeBackgroundCapture='true');
        await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
        await capture('cover-hidden-live',modal.window);
      } finally {
        await page.locator('.settings-window .dialog').evaluate(node=>delete node.dataset.nativeBackgroundCapture);
        await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
        await page.evaluate(async()=>{const {invoke}=await import('/src/bridge.js');await invoke('window/modal-uncover');});
      }
    }
    async function openFocusWindow(name) {
      // 独立空白测试窗口只负责转移焦点；强制1倍缩放，放在采样区右方，采集器另行拒绝重叠。
      focusBrowser=await chromium.launch({headless:false,
        executablePath:'C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe',
        args:['--window-position=2360,20','--window-size=500,300','--force-device-scale-factor=1']});
      const focusPage=await focusBrowser.newPage({viewport:null});
      await focusPage.goto('about:blank');
      await focusPage.bringToFront();
      const session=await focusBrowser.newBrowserCDPSession();
      const focusSession=await focusPage.context().newCDPSession(focusPage);
      console.log(JSON.stringify({focusBounds:await focusSession.send('Browser.getWindowForTarget')}));
      const processInfo=await session.send('SystemInfo.getProcessInfo');
      const focusPid=processInfo.processInfo.find(item=>item.type==='browser').id;
      console.log(JSON.stringify({focusBrowserPid:focusPid}));
      await delay(700);
      const focusPath=path.join(output,name+'.png');
      await execute('pwsh',['-NoProfile','-File','tools/audit/capture-screen-region.ps1',
        '-ExpectedProcessId',String(focusPid),'-Padding','0','-Output',focusPath],{windowsHide:true,timeout:30000});
      return JSON.parse(await fs.readFile(focusPath+'.json','utf8'));
    }
    let inactiveFocus, inactive;
    if(process.argv.includes('--inactive')) {
      inactiveFocus=await openFocusWindow('focus-window');
      inactive=await capture('settings-inactive',modal.window,path.join(output,'settings.png.json'),inactiveFocus);
      assert.equal(inactive.activationState,'inactive');
    }
    if(process.argv.includes('--verify-screen-capture')) {
      const checked=await execute('python',['-B','tools/audit/test_screen_region.py',
        '--window',String(modal.window),'--process',String(shell.pid),
        '--region',path.join(output,'settings.png.json')],{windowsHide:true,timeout:60000});
      await fs.writeFile(path.join(output,'capture-tests.txt'),checked.stdout+checked.stderr);
    }
    if(process.argv.includes('--refresh-video')) {
      const captureRefresh = () => require('./capture-refresh-screen.cjs').captureRefreshScreen(page,state,foreground,output);
      if (process.argv.includes('--trace-compositor'))
        await require('./trace-refresh-compositor.cjs').traceRefreshCompositor(browser,page,output,captureRefresh);
      else await captureRefresh();
      await capture('settings-after-video',modal.window);
    }
    if(gotoLine)await page.keyboard.press('Escape');
    else {
      assert.deepEqual(await dialogPage.evaluate(()=>window.__augitErrors),[]);
      await dialogPage.locator('.settings-window [data-settings-action="cancel"]').click();
      await require('./wait-async-condition.cjs').waitAsyncCondition(()=>page.evaluate(async()=>{const{invoke}=await import('/src/bridge.js');return !(await invoke('window/settings-state')).active;}),{label:'阴影采集后设置关闭'});
      settingsAudit.verifyState(await settingsAudit.settingsState(page),false);
    }
    await page.waitForFunction(()=>!window.__augitNativeModalActive);
    const closed=await state();
    assert.equal(closed.snapshotCount,0);assert.equal(closed.ownerEnabled,true);
    assert.equal(closed.captureCoverWindow,0,'关闭后捕获遮盖子窗必须销毁');
    assert.equal(closed.captureCoverWindowVisible,false);
    await delay(700);
    const source=path.join(output,'settings.png.json');
    let inactiveBackground, inactiveRepeat;
    if(inactiveFocus) {
      // 模态关闭会激活所有者；新测试窗口获得焦点后重新采身份，不假定CDP激活标签等于系统前景。
      await focusBrowser.close();focusBrowser=null;
      const backgroundFocus=await openFocusWindow('focus-background-window');
      inactiveBackground=await capture('background-inactive',modal.owner,source,backgroundFocus);
      inactiveRepeat=await capture('background-inactive-repeat',modal.owner,source,backgroundFocus);
      assert.equal(inactiveBackground.sha256,inactiveRepeat.sha256,'非活动背景重复采样必须一致');
      await focusBrowser.close();focusBrowser=null;
      await page.bringToFront();await delay(700);
    }
    const background=await capture('background',modal.owner,source);
    await delay(250);
    const repeat=await capture('background-repeat',modal.owner,source);
    assert.deepEqual(foreground.regionPhysical,background.regionPhysical);
    assert.equal(background.sha256,repeat.sha256,'稳定背景重复采样必须一致');
    assert.deepEqual(await page.evaluate(()=>window.__augitErrors),[]);
    const result={theme,appearance,normalEntry,softwareRendering,disableDirectComposition,disableNativeOcclusion,browserArguments,dialogKind:gotoLine?'goto-line':'settings',pid:shell.pid,port,temp:audit,modal,closed,foreground,background,repeat,
      inactiveFocus,inactive,inactiveBackground,inactiveRepeat,
      contentReview:'待人工核对原始PNG；不是双端像素通过结论'};
    await fs.writeFile(path.join(output,'capture.json'),JSON.stringify(result,null,2)+'\n');
    console.log(JSON.stringify({captured:true,theme,backgroundStable:true}));
  } catch (error) {
    await fs.writeFile(path.join(output,'failure.json'),JSON.stringify({message:error.message,pid:shell?.pid,stack:error.stack},null,2)+'\n',{flag:'wx'});
    throw error;
  } finally {
    if(focusBrowser)try {await focusBrowser.close();}catch {}
    if(server.listening)await new Promise(resolve=>server.close(resolve));
    if(browser)try {await browser.close();}catch {}
    if(shell?.pid&&shell.exitCode===null)try {await execute('taskkill.exe',['/PID',String(shell.pid),'/T','/F'],{windowsHide:true});}catch {}
    const resolved=path.resolve(audit);
    assert.ok(resolved.startsWith(path.resolve(os.tmpdir())+path.sep)&&path.basename(resolved).startsWith('Augit-shadow-pair-'));
    await fs.rm(resolved,{recursive:true,force:true,maxRetries:8,retryDelay:250});
    console.log(JSON.stringify({cleanedPid:shell?.pid,tempRemoved:resolved}));
  }
})().catch(error=>{console.error(error);process.exitCode=1;});
