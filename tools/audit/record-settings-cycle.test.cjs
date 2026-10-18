const {test}=require('node:test'),assert=require('node:assert/strict');
const {assessCycleForeground,cycleOptions,closeRecordedSettings}=require('./record-settings-cycle.cjs');
test('提前正常退出的录像也必须拒绝全过程声明',()=>{
 const {requireLiveRecorder}=require('./record-settings-cycle.cjs');
 requireLiveRecorder({exitCode:null});
 for(const exitCode of [0,1])assert.throws(()=>requireLiveRecorder({exitCode}),/提前退出/);
});
test('OK实际点击后等待关闭并核对版本与持久值，拒绝假保存和双写',async()=>{
 for(const next of [4,5,6]){
  const calls=[];
  const options={page:{locator:s=>({click:async()=>calls.push(s)})},closed:async()=>calls.push('closed'),
   invoke:async method=>{calls.push(method);return method==='settings/read'?{terminalShell:'CommandPrompt'}:{revision:next};},
   closeAction:'save',revision:4,expectedShell:'CommandPrompt'};
  if(next===5){await closeRecordedSettings(options);assert.deepEqual(calls,['[data-settings-action=save]','closed','window/settings-state','settings/read']);}
  else await assert.rejects(closeRecordedSettings(options));
 }
});
test('Cancel不允许版本变化，保存后的错误持久值也拒绝',async()=>{
 const options={page:{locator:()=>({click:async()=>{}})},closed:async()=>{},
  invoke:async method=>method==='settings/read'?{terminalShell:'Wrong'}:{revision:5},closeAction:'cancel',revision:4};
 await assert.rejects(closeRecordedSettings(options));
 await assert.rejects(closeRecordedSettings({...options,closeAction:'save',expectedShell:'CommandPrompt'}));
 assert.equal((await closeRecordedSettings({...options,revision:5})).revision,5);
});
test('默认录像仍取消，明确保存才能录制脏草稿',()=>{
 assert.deepEqual(cycleOptions([]),{closeAction:'cancel',dirty:false});
 assert.deepEqual(cycleOptions(['--record-save','--record-dirty']),{closeAction:'save',dirty:true});
 assert.deepEqual(cycleOptions(['--record-save']),{closeAction:'save',dirty:false});
 assert.deepEqual(cycleOptions(['--record-save','--record-dirty','--record-phases']),{closeAction:'save',dirty:true,phases:true});
 assert.throws(()=>cycleOptions(['--record-dirty']));
});
test('开关过程仅允许实际登记的本实例主窗和设置窗',()=>{
 const result=assessCycleForeground([{processId:11,window:20},{processId:11,window:21},{processId:11,window:20}],11,[20,21]);
 assert.equal(result.qualified,true);
});
test('同进程未知窗口与其它应用前景均使录像失去资格',()=>{
 for(const sample of [{processId:11,window:22},{processId:12,window:20}])assert.equal(assessCycleForeground([sample],11,[20,21]).qualified,false);
});
test('缺少采样或窗口身份不得算作通过',()=>{
 assert.throws(()=>assessCycleForeground([],11,[20]));
 assert.throws(()=>assessCycleForeground([{processId:11,window:20}],11,[]));
});
