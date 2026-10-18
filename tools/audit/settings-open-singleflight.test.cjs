// 重复入口必须复用当前设置节点，不能先关闭再创建并丢失草稿。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('web/src/live-data.js','utf8');
const start=source.indexOf('function openSettingsDialog()');
const declaration=source.slice(start,source.indexOf(String.fromCharCode(10)+'/**',start));
test('再次打开设置保留现有表单及草稿，不触发关闭重建',()=>{
 let closed=0,focused=0;const field={focus(){focused++;}},existing={contains:()=>false,querySelector:()=>field};
 const live={settings:{},settingsDraft:{textFontFamily:'Tahoma'}};
 const context={window:{__augitLive:live},document:{activeElement:{},querySelector:selector=>selector==='.augit-window'?{}:existing},rememberDialogFocus(){},closeLiveOverlay(){closed++;throw Error('发生重复关闭');}};
 vm.createContext(context);vm.runInContext(declaration,context);context.openSettingsDialog();
 assert.equal(closed,0);assert.equal(focused,1);assert.equal(live.settingsDraft.textFontFamily,'Tahoma');
});

test('正式宿主设置入口只请求独立窗口，不在主页面插入表单',async()=>{
 const calls=[];const context={window:{__augitSettingsWindowAvailable:true},document:{querySelector(){throw Error('不得构建主窗设置DOM');}},rememberDialogFocus(){},invoke:async(method,params)=>{calls.push({method,params});}};
 vm.createContext(context);vm.runInContext(declaration,context);context.openSettingsDialog();await Promise.resolve();
 assert.equal(calls.length,1);assert.equal(calls[0].method,'window/settings-open');assert.equal(calls[0].params.page,'appearance');
});
