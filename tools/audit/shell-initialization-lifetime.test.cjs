// 初始化每个异步边界都可能收到真实窗口销毁；禁止迟到续接访问旧HWND/控制器。
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs');
const shell=fs.readFileSync('src/Augit.Shell/ShellWindow.cs','utf8');
const body=shell.slice(shell.indexOf('private async Task InitializeWebViewAsync()'),shell.indexOf('private void ApplySurfaceColor('));
test('旧设置失败不得通知已关闭所有者或影响后来重开的窗口',()=>{
 const settings=fs.readFileSync('src/Augit.Shell/ShellWindow.Settings.cs','utf8');
 const failure=settings.slice(settings.indexOf('private void FailSettingsInitialization('),settings.indexOf('private void QueueSettingsReply('));
 const guard=failure.indexOf('if (_disposed || _settingsOwner?._disposed == true) return;');
 assert.ok(guard>=0&&guard<failure.indexOf('_settingsOwner?.Notify('));
});
test('环境创建完成后先拒绝已销毁窗口，再创建控制器',()=>{
 const start=body.indexOf('_environment ='),end=body.indexOf('_environment.CreateCoreWebView2ControllerAsync');
 assert.ok(start>=0&&end>start);assert.ok(body.slice(start,end).includes('if (_disposed) return;'));
});
test('脚本注册完成后先拒绝已销毁窗口，再映射导航和注册事件',()=>{
 const start=body.indexOf('await webView.AddScriptToExecuteOnDocumentCreatedAsync'),end=body.indexOf('webView.SetVirtualHostNameToFolderMapping');
 assert.ok(start>=0&&end>start);assert.ok(body.slice(start,end).includes('if (_disposed) return;'));
});
test('关闭期间完成的控制器被释放且不保留字段引用',()=>{
 const start=body.indexOf('if (_disposed)',body.indexOf('_controller = await')),end=body.indexOf('ApplySurfaceColor');
 const release=body.slice(start,end);assert.ok(release.includes('_controller?.Close();'));assert.ok(release.includes('_controller = null;'));
});
test('窗口已关闭时的初始化异常不再通知所有者或弹错误框',()=>{
 const start=body.indexOf('catch (Exception exception)'),end=body.indexOf('FailSettingsInitialization',start);
 assert.ok(start>=0&&end>start);assert.ok(body.slice(start,end).includes('if (_disposed) return;'));
});
