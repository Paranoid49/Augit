// 隐藏的独立设置页必须能完成初始化，不能依赖可见页面才推进的动画帧。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('web/src/live-data.js','utf8');
const start=source.indexOf('async function bootSettingsSurface()');
const declaration=source.slice(start,source.indexOf('function bindIndependentSettingsEvents()',start));

test('控制器创建区分同步调用和异步等待，保留迟到关闭保护',()=>{
 const native=fs.readFileSync('src/Augit.Shell/ShellWindow.cs','utf8');
 const begin=native.indexOf('TraceSettingsStage("controller-create-start")');
 const end=native.indexOf('ApplySurfaceColor(_options.Theme)',begin);
 const creation=native.slice(begin,end);
 const steps=['TraceSettingsStage("controller-create-start")','Task<CoreWebView2Controller> controllerCreation = _environment.CreateCoreWebView2ControllerAsync(_window);','TraceSettingsStage("controller-create-issued")','_controller = await controllerCreation;','TraceSettingsStage("controller-create-end")','if (_disposed)','_controller?.Close();','_controller = null;'];
 let previous=-1;for(const step of steps){const at=creation.indexOf(step,previous+1);assert.ok(at>previous,step);previous=at;}
 assert.equal(creation.includes('Task.Delay'),false);assert.equal(creation.includes('Task.Run'),false);
});

test('捕获调用返回和异步完成分开计时，销毁保护与揭示顺序不变',()=>{
 const native=fs.readFileSync('src/Augit.Shell/ShellWindow.Settings.cs','utf8');
 const present=native.slice(native.indexOf('private async Task PresentSettingsWindowAsync()'),native.indexOf('private void RevealSettingsWindow()'));
 const steps=['TraceSettingsStage("capture-start")','Task capture = _controller!.CoreWebView2.CapturePreviewAsync','TraceSettingsStage("capture-issued")','await capture;','if (_disposed || _settingsClosing || owner._disposed) return;','TraceSettingsStage("capture-end")','PostMessage(_window, SettingsRevealMessage'];
 let previous=-1;for(const step of steps){const at=present.indexOf(step,previous+1);assert.ok(at>previous,step);previous=at;}
 assert.equal(present.includes('Task.Delay'),false);assert.equal(present.includes('Task.Run'),false);
});

test('异步阶段记录实际续接线程，诊断不预设UI线程相同',()=>{
 const shell=fs.readFileSync('src/Augit.Shell/ShellWindow.cs','utf8');
 const settings=fs.readFileSync('src/Augit.Shell/ShellWindow.Settings.cs','utf8');
 for(const name of ['_settingsControllerReadyThread','_settingsScriptReadyThread'])assert.ok(shell.includes(name+' = Environment.CurrentManagedThreadId'));
 assert.ok(settings.includes('_settingsRenderedThread = Environment.CurrentManagedThreadId'));
 for(const name of ['creationThread','controllerReadyThread','scriptReadyThread','renderedThread'])assert.ok(settings.includes(name+' = _settings'));
});
test('导航前脚本注册单独计时，不把COM等待混入字体资源耗时',()=>{
 const native=fs.readFileSync('src/Augit.Shell/ShellWindow.cs','utf8');
 const begin=native.indexOf('_settingsScriptRegistrationStartedMilliseconds =');
 const registration=native.indexOf('await webView.AddScriptToExecuteOnDocumentCreatedAsync(',begin);
 const end=native.indexOf('_settingsScriptRegistrationReadyMilliseconds =',registration);
 const navigation=native.indexOf('_settingsNavigationStartedMilliseconds =',end);
 assert.ok(begin>=0&&registration>begin&&end>registration&&navigation>end);
});
test('准备显示与最终呈现都退出WebView消息回调后执行',()=>{
 const native=fs.readFileSync('src/Augit.Shell/ShellWindow.Settings.cs','utf8');
 const ready=native.slice(native.indexOf('case "window/settings-ready":'),native.indexOf('case "window/settings-present":'));
 assert.ok(ready.includes('PostMessage(_window, SettingsPrepareMessage'),'准备显示必须排队');
 for(const operation of ['ShowWindow(','DwmSetWindowAttribute(','SyncBounds();','IsVisible ='])assert.equal(ready.includes(operation),false,operation);
 const preparation=native.slice(native.indexOf('private void PrepareSettingsWindow()'),native.indexOf('private async Task PresentSettingsWindowAsync()'));
 assert.ok(preparation.includes('_disposed || _settingsClosing'));
 const ordered=['DwmSetWindowAttribute(_window, 13, ref cloak','SyncBounds();','_controller.IsVisible = true','ShowWindow(_window, 4)','NotifyParentWindowPositionChanged()','QueueSettingsReply(_settingsPrepareRequest'];
 let previous=-1;for(const step of ordered){const current=preparation.indexOf(step);assert.ok(current>previous,step);previous=current;}
 const shell=fs.readFileSync('src/Augit.Shell/ShellWindow.cs','utf8');
 assert.ok(shell.includes('case SettingsPrepareMessage:'));assert.ok(shell.includes('shell.PrepareSettingsWindow();'));
});
test('先请求宿主启动合成，再由宿主确认显示，不等待隐藏动画帧',async()=>{
 const calls=[],dialog={dataset:{},getBoundingClientRect(){calls.push('layout');return {};},focus(options){assert.equal(options.preventScroll,true);calls.push('search-focus');}};
 const context={window:{},document:{body:{dataset:{}},fonts:{ready:Promise.resolve()},head:{append(script){script.onload();}},createElement(){return {};},querySelector(){return dialog;},addEventListener(){}},
  invoke:async method=>{calls.push(method);return method==='settings/read'?{theme:'Light'}:{saving:false,page:'appearance'};},openSettingsDialog(){},loadSettings:async(canApply,initial)=>{assert.equal(canApply(),true);assert.equal(initial,context.window.__augitLive.settings);},bindOverlayEscape(){},bindModalBackground(){},subscribe(){},syncSettingsBusy(){},switchSettingsPage(){},previewThemeName:()=> 'light',applyPageTheme(){},requestAnimationFrame(){throw Error('隐藏窗口不得等待动画帧');},setTimeout(){throw Error('不得以固定延时掩盖首次呈现');}};
 vm.createContext(context);vm.runInContext(declaration,context);await context.bootSettingsSurface();
 assert.equal(context.window.__augitReady,true);assert.deepEqual(calls,['settings/read','layout','window/settings-ready','search-focus','window/settings-present']);
});

test('共享脚本仅跳过独立设置空容器度量，普通页面仍初始化字体',()=>{
 const shared=fs.readFileSync('web/src/mockup.js','utf8');
 const bootstrap=shared.slice(shared.lastIndexOf('if (app) {'));
 for(const surface of [true,false]){
  const calls=[];const context={app:{},window:{__augitSettingsSurface:surface},scene:'main-project',
   renderScene:()=>'<main></main>',bindInteractions:()=>calls.push('bind'),applyTypographyPreview:()=>calls.push('typography')};
  vm.createContext(context);vm.runInContext(bootstrap,context);
  assert.equal(context.app.innerHTML,'<main></main>');assert.ok(calls.includes('bind'));
  assert.equal(calls.includes('typography'),!surface);
 }
});

test('原生激活在解除遮蔽前完成，揭示后不得重置控制器表面',()=>{
 const native=fs.readFileSync('src/Augit.Shell/ShellWindow.Settings.cs','utf8');
 const present=native.slice(native.indexOf('private async Task PresentSettingsWindowAsync()'),native.indexOf('private object SettingsPresentationState()'));
 const reveal=present.indexOf('DwmSetWindowAttribute(_window, 13, ref uncloak');
 assert.ok(reveal>0);
 for(const step of ['Show();','SetForegroundWindow(_window);','MoveFocus(','DwmFlush()'])assert.ok(present.indexOf(step)>=0&&present.indexOf(step)<reveal,step);
 const capture=present.indexOf('Task capture = _controller!.CoreWebView2.CapturePreviewAsync');
 assert.ok(capture>present.indexOf('MoveFocus(')&&capture<reveal,'激活及焦点切换后重新确认渲染，不能依赖旧表面');
 assert.ok(present.indexOf('PostMessage(_window, SettingsRevealMessage')>capture);
 const revealBody=present.slice(present.indexOf('private void RevealSettingsWindow()'));
 assert.ok(revealBody.indexOf('DwmFlush()')>=0&&revealBody.indexOf('DwmFlush()')<revealBody.indexOf('ref uncloak'),'设置自身捕获完成后仍须提交桌面合成，再解除遮蔽');
 const after=present.slice(reveal);
 for(const operation of ['SyncBounds();','IsVisible =','NotifyParentWindowPositionChanged','Show();'])assert.equal(after.includes(operation),false,operation);
});

test('保存续接只投递主题消息，UI消息处理才访问WebView底色',()=>{
 const native=fs.readFileSync('src/Augit.Shell/ShellWindow.Settings.cs','utf8');
 const setter=native.slice(native.indexOf('private void SetSettingsSurfaceTheme('),native.indexOf('private string? SettingsSurfaceColor('));
 assert.ok(setter.includes('PostMessage(_window, SettingsSurfaceThemeMessage'));
 assert.equal(setter.includes('ApplySurfaceColor('),false);
 const shell=fs.readFileSync('src/Augit.Shell/ShellWindow.cs','utf8');
 const handler=shell.slice(shell.indexOf('case SettingsSurfaceThemeMessage:'),shell.indexOf('case SettingsStartupTimeoutMessage:'));
 assert.ok(handler.includes('shell.ApplySurfaceColor(shell._notifiedTheme)'));
});

test('设置关闭先撤下合成表面再激活所有者，异常释放也保持此顺序',()=>{
 const native=fs.readFileSync('src/Augit.Shell/ShellWindow.Settings.cs','utf8');
 const release=native.slice(native.indexOf('private void ReleaseSettingsOwner()'),native.indexOf('private void FailSettingsInitialization('));
 const enable=release.indexOf('EnableWindow(owner._window, true)');
 const cloak=release.indexOf('DwmSetWindowAttribute(_window, 13, ref cloak');
 const flush=release.indexOf('DwmFlush()');
 const activate=release.indexOf('SetForegroundWindow(owner._window)');
 assert.ok(enable>=0&&cloak>enable&&flush>cloak&&activate>flush);
 const shell=fs.readFileSync('src/Augit.Shell/ShellWindow.cs','utf8');
 const dispose=shell.slice(shell.indexOf('public void Dispose()'),shell.indexOf('private static void RegisterWindowClass()'));
 assert.ok(dispose.indexOf('ReleaseSettingsOwner();')<dispose.indexOf('_controller?.Close();'));
});

test('正常原生关闭记录完整销毁阶段并异步导出，不只覆盖异常Dispose',()=>{
 const shell=fs.readFileSync('src/Augit.Shell/ShellWindow.cs','utf8');
 const closing=shell.slice(shell.indexOf('case WmClose:'),shell.indexOf('case WmDestroy:'));
 const destruction=shell.slice(shell.indexOf('case WmDestroy:'),shell.indexOf('default:',shell.indexOf('case WmDestroy:')));
 const closeSteps=['TraceSettingsStage("close-start")','shell.ReleaseSettingsOwner()','TraceSettingsStage("owner-released")','DestroyWindow(window)'];
 const destroySteps=['TraceSettingsStage("destroy-start")','shell._controller?.Close()','TraceSettingsStage("controller-closed")','shell._window = 0','TraceSettingsStage("destroyed")','shell.ExportSettingsStages()'];
 for(const [body,steps]of [[closing,closeSteps],[destruction,destroySteps]]){
  let previous=-1;for(const step of steps){const current=body.indexOf(step);assert.ok(current>previous,step);previous=current;}
  assert.equal(body.includes('await '),false,'诊断不能增加关闭等待');
 }
 const native=fs.readFileSync('src/Augit.Shell/ShellWindow.Settings.cs','utf8');
 const release=native.slice(native.indexOf('private void ReleaseSettingsOwner()'),native.indexOf('private void FailSettingsInitialization('));
 const steps=['TraceSettingsStage("owner-release-start")','EnableWindow(owner._window, true)','TraceSettingsStage("owner-enabled")','DwmSetWindowAttribute(_window, 13, ref cloak','TraceSettingsStage("close-cloaked")','DwmFlush()','TraceSettingsStage("close-flushed")','SetForegroundWindow(owner._window)','TraceSettingsStage("owner-activated")'];
 let previous=-1;for(const step of steps){const current=release.indexOf(step);assert.ok(current>previous,step);previous=current;}
});
