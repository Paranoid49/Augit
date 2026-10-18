// 执行真实保存函数：独立窗口确定关闭无需再次装配即将销毁的页面。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('web/src/live-data.js','utf8');
const start=source.indexOf('async function saveSettings('),end=source.indexOf('\n/**',start);
function harness({surface=true,saved={saved:true},current=true}={}){
 const calls=[],session={status:'draft',dialog:{querySelector:()=>null}};
 const live={settingsDraft:{fontSize:14}},window={__augitSettingsSurface:surface,__augitLive:live};
 let resolveWrite;
 const context={window,document:{querySelectorAll:()=>[]},settingsWritePending:false,
  currentSettingsSession:()=>session,isCurrentSettingsSession:()=>current,
  syncSettingsBusy:()=>{},commitThemePreview:()=>calls.push('commit'),
  invoke:()=>new Promise(resolve=>{resolveWrite=()=>resolve(saved);}),loadSettings:async()=>calls.push('reload')};
 vm.createContext(context);vm.runInContext(source.slice(start,end),context);
 return {calls,session,live,context,finish:()=>resolveWrite()};
}
test('独立窗口确定成功后直接结束保存，不再回读和度量子窗',async()=>{
 const h=harness(),pending=h.context.saveSettings(h.session,{closeAfterSave:true});
 assert.equal(h.session.status,'saving');assert.deepEqual(h.calls,[]);
 h.finish();await pending;
 assert.deepEqual(h.calls,['commit']);assert.equal(h.session.status,'saved');
 assert.equal(h.context.settingsWritePending,false);assert.equal(Object.keys(h.live.settingsDraft).length,0);
});
for(const options of [{surface:true,closeAfterSave:false},{surface:false,closeAfterSave:true}])test('应用和共用页面保留配置回读：'+JSON.stringify(options),async()=>{
 const h=harness(options),pending=h.context.saveSettings(h.session,options);h.finish();await pending;
 assert.deepEqual(h.calls,['commit','reload']);assert.equal(h.session.status,'saved');
});
test('保存失败不能进入成功关闭路径，草稿仍保留',async()=>{
 const h=harness({saved:{saved:false,reason:'写入失败'}}),pending=h.context.saveSettings(h.session,{closeAfterSave:true});h.finish();
 await assert.rejects(pending,/写入失败/);assert.equal(h.session.status,'failed');assert.deepEqual(h.calls,[]);assert.equal(h.live.settingsDraft.fontSize,14);
});
test('已关闭旧会话的迟到保存不提交预览或回读',async()=>{
 const h=harness({current:false}),pending=h.context.saveSettings(h.session,{closeAfterSave:true});h.finish();await pending;assert.deepEqual(h.calls,[]);
});
test('保存事件显式区分应用与确定',()=>{
 assert.ok(source.includes('saveSettings(session, { closeAfterSave: !applyOnly })'));
});
