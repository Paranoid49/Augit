// 固定ANSI索引与浅深参考色；检查切换不会污染其他实例。
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs/promises');
test('选区采用编辑器浅深方案且区分失焦状态',async()=>{
  const source=await fs.readFile('web/src/terminal-palette.js','utf8');
  const {terminalSelectionPalette}=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
  assert.deepEqual(terminalSelectionPalette('light'),{selectionBackground:'#A6D2FF',selectionInactiveBackground:'#B5B7BD'});
  assert.deepEqual(terminalSelectionPalette('dark'),{selectionBackground:'#214283',selectionInactiveBackground:'#4C4F56'});
  const changed=terminalSelectionPalette('dark');changed.selectionBackground='#000000';
  assert.equal(terminalSelectionPalette('dark').selectionBackground,'#214283');
});
test('ANSI16色对应参考控制台索引且主题结果独立',async()=>{
  const source=await fs.readFile('web/src/terminal-palette.js','utf8');
  const {terminalAnsiPalette}=await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
  const light=terminalAnsiPalette('light'),dark=terminalAnsiPalette('dark');
  assert.equal(Object.keys(light).length,16);
  assert.deepEqual(Object.values(light),['#000000','#CE0505','#067D17','#B28C00','#063FDB','#B309B3','#028E8E','#929292','#656565','#FF1616','#16B42C','#ECC32C','#0060AF','#E617E6','#15C1C1','#C9C9C9']);
  assert.equal(dark.red,'#F0524F');assert.equal(dark.white,'#808080');assert.equal(dark.brightWhite,'#FFFFFF');
  light.red='#000000';assert.equal(terminalAnsiPalette('light').red,'#CE0505');
});
