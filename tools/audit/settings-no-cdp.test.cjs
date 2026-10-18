const {test}=require('node:test'),assert=require('node:assert/strict');
const {normalEnvironment,summarizeForeground,recordingOptions}=require('./settings-no-cdp.cjs');
test('GDI时钟选项只改变记录器且如实标识采集来源',()=>{
 const {recorderArguments}=require('./refresh-recorder-arguments.cjs');
 for(const enabled of [true,false]){
  const options=recordingOptions(enabled),args=recorderArguments([0,0,100,100],'test.mkv',options.desktopDuplication,true,25);
  assert.equal(args.includes('gdigrab'),enabled);
  assert.equal(options.method,enabled?'ffmpeg-gdigrab-desktop-ffv1':'ffmpeg-ddagrab-desktop-ffv1');
 }
});
test('无调试对照删除WebView入口覆盖且不修改父环境',()=>{
 const original={PATH:'keep',LOCALAPPDATA:'keep',WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS:'--remote-debugging-port=9222',WEBVIEW2_USER_DATA_FOLDER:'override',webview2_browser_executable_folder:'override',WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER:'pipe'};
 assert.deepEqual(normalEnvironment(original),{PATH:'keep',LOCALAPPDATA:'keep'});assert.ok(original.WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS);
});
test('同实例主窗和设置允许切换，外部窗口使录像失去资格',()=>{
 const samples=[{processId:1,window:10},{processId:1,window:20}];
 assert.equal(summarizeForeground(samples,1).qualified,true);
 assert.equal(summarizeForeground([...samples,{processId:2,window:30}],1).qualified,false);
 assert.throws(()=>summarizeForeground([],1));
});
