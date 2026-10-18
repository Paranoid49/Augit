// 执行真实区域路由；局部补丁不能退化成空参数整页重绘或改动未请求区域。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('web/src/live-data.js','utf8');
const start=source.indexOf('function refreshStatusRegions(...regions)');
const declaration=source.slice(start,source.indexOf('// 比较标签的取消代际',start));
function run(regions,patched){
 const calls={patches:0,restore:0,toolbar:0,renders:[]};
 const context={patchChangesList(){calls.patches++;return patched;},restoreChangesState(){calls.restore++;},syncChangesToolbar(){calls.toolbar++;},refresh(...names){calls.renders.push(names);}};
 vm.createContext(context);vm.runInContext(declaration,context);context.refreshStatusRegions(...regions);return calls;
}
test('只刷新侧栏且已原地应用时不触发整页重绘',()=>{
 assert.deepEqual(run(['side'],true),{patches:1,restore:1,toolbar:1,renders:[]});
});
test('侧栏补丁成功后只刷新剩余目标',()=>{
 assert.deepEqual(run(['side','statusbar'],true).renders,[['statusbar']]);
});
test('非侧栏刷新不触碰改动列表、草稿或焦点',()=>{
 assert.deepEqual(run(['editorContent'],true),{patches:0,restore:0,toolbar:0,renders:[['editorContent']]});
});
test('补丁形态不符仍只替换请求区域',()=>{
 assert.deepEqual(run(['side'],false).renders,[['side']]);
});
