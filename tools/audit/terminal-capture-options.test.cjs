const {test}=require('node:test');
const assert=require('node:assert/strict');
const {terminalCaptureOptions}=require('./terminal-capture-options.cjs');
test('日常采集复制真实配置，仅显式主题及批准的Shell输出可变',()=>{
 const {dailyTerminalSettings}=require('./terminal-capture-options.cjs');
 const original={theme:'Dark',textFontSize:20,terminalShell:'WindowsPowerShell',openFiles:['app.py'],layout:{sideWidth:355}};
 const copied=dailyTerminalSettings(original,[]);assert.deepEqual(copied,original);assert.notEqual(copied.layout,original.layout);
 const rows=dailyTerminalSettings(original,['--startup-rows']);assert.equal(rows.terminalShell,'Custom');assert.equal(rows.textFontSize,20);assert.equal(original.terminalShell,'WindowsPowerShell');
 assert.throws(()=>dailyTerminalSettings(original,['--font-size=13']));assert.throws(()=>dailyTerminalSettings(original,['--theme=bad']));
});
test('同态启动命令只打印八行且保持真实Shell存续',()=>{
 const options=terminalCaptureOptions(['--startup-rows']);
 assert.equal(options.settings.terminalShell,'Custom');
 const command=options.settings.terminalCustomCommand;
 assert.match(command,/^powershell.exe -NoProfile -NoExit -EncodedCommand /);
 assert.equal(Buffer.from(command.split(' ').at(-1),'base64').toString('utf16le'),"1..8 | ForEach-Object { 'Ag 0123456789' }");
 assert.equal(terminalCaptureOptions([]).settings.terminalShell,undefined);
});
test('普通入口恢复真实文件且不使用场景与DPI覆盖',()=>{
 const options=terminalCaptureOptions([]);
 assert.deepEqual(options.sceneArgs,[]);
 assert.deepEqual(options.settings.openFiles,['notes.txt']);
 assert.equal(options.settings.activeFile,'notes.txt');
 assert.equal(options.settings.fontSize,13);
 assert.equal(options.settings.monospaceFontFamily,'JetBrains Mono');
});
test('浅深配置显式携带外观版本，避免深色被旧设置迁移',()=>{
 for(const theme of ['Light','Dark']) {
  const options=terminalCaptureOptions(['--theme='+theme]);
  assert.equal(options.settings.theme,theme);
  assert.equal(options.settings.appearanceVersion,1);
 }
 assert.throws(()=>terminalCaptureOptions(['--theme=invalid']));
});
test('拒绝已从产品删除的可变正文设置',()=>{
 for(const size of ['17','12','0','NaN',''])assert.throws(()=>terminalCaptureOptions(['--font-size='+size]));
 assert.equal(terminalCaptureOptions(['--font-size=13']).fontSize,13);
});
