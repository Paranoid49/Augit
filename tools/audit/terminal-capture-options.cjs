// 终端配对走普通入口，恢复真实文件；固定正文不接受历史审计字号覆盖。
const assert=require('node:assert/strict');
const {captureSettings,captureSceneArguments}=require('./refresh-capture-startup.cjs');
// 实际端使用固定真实Shell启动表达式；参考集成钩子执行同一表达式，不向buffer注入文字。
function terminalRowCommand() {
 const output="1..8 | ForEach-Object { 'Ag 0123456789' }";
 return 'powershell.exe -NoProfile -NoExit -EncodedCommand '+Buffer.from(output,'utf16le').toString('base64');
}
function terminalCaptureOptions(args) {
 const theme=args.find(value=>value.startsWith('--theme='))?.slice(8)||'Light';
 const requested=args.find(value=>value.startsWith('--font-size='));
 assert.ok(!requested||requested==='--font-size=13','产品正文固定JetBrains Mono13，不能把其他字号作为当前验收');
 const workspace='D:/JetBrains/PyCharm-2026.2.1-reference/fixture';
 const settings=captureSettings(theme,workspace,true);
 if(args.includes('--startup-rows'))Object.assign(settings,{terminalShell:'Custom',terminalCustomCommand:terminalRowCommand()});
 return {theme,fontSize:13,settings,sceneArgs:captureSceneArguments(true,true)};
}
function dailyTerminalSettings(original,args) {
 assert.ok(original&&typeof original==='object'&&!Array.isArray(original),'需要真实配置对象');
 assert.ok(!args.some(arg=>arg.startsWith('--font-size=')),'正常入口不得覆盖正文字号');
 const settings=structuredClone(original);
 const theme=args.find(value=>value.startsWith('--theme='))?.slice(8);
 if(theme){assert.ok(['Light','Dark'].includes(theme));settings.theme=theme;}
 if(args.includes('--startup-rows'))Object.assign(settings,{terminalShell:'Custom',terminalCustomCommand:terminalRowCommand()});
 return settings;
}
module.exports={terminalCaptureOptions,terminalRowCommand,dailyTerminalSettings};
