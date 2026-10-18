// 直接执行产品函数，确保帧回调尚未完成时不会提前撤盖。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('web/src/live-data.js','utf8');
const start=source.indexOf('function waitForRestoredModalFrame()');
const declaration=source.slice(start,source.indexOf('}',start)+1);
test('等待渲染回调后的任务，不能只在回调微任务内完成',async()=>{
 assert.ok(start>=0);const frames=[],tasks=[];let done=false;
 const context={requestAnimationFrame:fn=>frames.push(fn),setTimeout:(fn,ms)=>{assert.equal(ms,0);tasks.push(fn);}};
 vm.createContext(context);vm.runInContext(declaration,context);
 const pending=context.waitForRestoredModalFrame().then(()=>done=true);
 await Promise.resolve();assert.equal(done,false);assert.equal(frames.length,1);assert.equal(tasks.length,0);
 frames.shift()();await Promise.resolve();assert.equal(done,false);assert.equal(tasks.length,1);
 tasks.shift()();await pending;assert.equal(done,true);
});
test('正常刷新恢复分支等待后撤盖且始终清理忙状态',()=>{
 const offset=source.indexOf('// 表单重新合成后才移除原图遮盖');
 const block=source.slice(offset,source.indexOf('    running = true;',offset));
 const wait=block.indexOf('await waitForRestoredModalFrame()'),uncover=block.indexOf('window/modal-uncover');
 assert.ok(wait>=0&&uncover>wait);
 assert.ok(block.includes('if (operation === currentOperation) { window.__augitNativeBackgroundBusy = false; running = false;'));
});
