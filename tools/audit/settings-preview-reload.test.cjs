// 执行真实事件绑定：持久化回读与临时预览必须各自保留正确顺序。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('web/src/live-data.js','utf8'),start=source.indexOf('function bindIndependentSettingsEvents()');
const declaration=source.slice(start,source.indexOf('async function boot()',start));
function harness(){
 const events={},reads=[],themes=[],window={__augitLive:{settings:{theme:'Light'}}};let focus=0;
 const context={window,subscribe:(name,fn)=>events[name]=fn,
  loadSettings:canApply=>new Promise(resolve=>reads.push(theme=>{if(canApply())window.__augitLive.settings={theme};resolve();})),
  previewThemeName:theme=>theme.toLowerCase(),applyPageTheme:theme=>themes.push(theme),restoreDialogFocus:()=>focus++,showToast:()=>{}};
 vm.createContext(context);vm.runInContext(declaration,context);context.bindIndependentSettingsEvents();
 return {events,reads,themes,window,get focus(){return focus;}};
}
const settled=()=>new Promise(resolve=>setImmediate(resolve));
test('提交回读晚到不能覆盖之后的新预览，但持久化配置仍更新',async()=>{
 const h=harness();h.events['settings/committed']();h.events['settings/preview']({theme:'dark'});h.reads.shift()('Light');await settled();
 assert.deepEqual(h.themes,['dark']);assert.equal(h.window.__augitLive.settings.theme,'Light');
});
test('取消后的新回读恢复持久化主题并恢复焦点',async()=>{
 const h=harness();h.events['settings/preview']({theme:'dark'});h.events['settings/closed']();h.reads.shift()('Light');await settled();
 assert.deepEqual(h.themes,['dark','light']);assert.equal(h.focus,1);
});
test('关闭回读晚到不能盖过重开窗口的新预览',async()=>{
 const h=harness();h.events['settings/closed']();h.events['settings/preview']({theme:'dark'});h.reads.shift()('Light');await settled();assert.deepEqual(h.themes,['dark']);
});
test('无效预览不废弃正常回读，更新回读仍淘汰旧结果',async()=>{
 const h=harness();h.events['settings/committed']();h.events['settings/preview']({theme:'invalid'});h.events['settings/closed']();
 h.reads[1]('Dark');await settled();h.reads[0]('Light');await settled();assert.deepEqual(h.themes,['dark']);assert.equal(h.window.__augitLive.settings.theme,'Dark');
});
