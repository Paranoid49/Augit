// 设置回读执行真实函数，确保未变字体不触发全界面度量。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('web/src/live-data.js','utf8');
const start=source.indexOf('async function loadSettings(');
const declaration=source.slice(start,source.indexOf(String.fromCharCode(10)+'/**',start));
const base={fontSize:12,codeFontSize:13,textFontFamily:'Microsoft YaHei UI',monospaceFontFamily:'JetBrains Mono'};

test('相同字体的后续回读必须等待前次在途度量，再恢复最新面板',async()=>{
 let finish,started,revision=1,measurements=0;const panels=[];
 const signal=new Promise(resolve=>started=resolve),fontTask=new Promise(resolve=>finish=resolve);
 const window={__augitLive:{settings:base},__augitSettingsReady:true};
 const context={window,invoke:async()=>({...base,fontSize:14,projectPanelWidth:revision===1?320:480}),
  applyTypographyPreview:()=>{measurements++;started();return fontTask;},refreshTerminalTypography:()=>{},applySavedPanelSizes:settings=>panels.push(settings.projectPanelWidth)};
 vm.createContext(context);vm.runInContext(declaration,context);
 const first=context.loadSettings(()=>revision===1);await signal;revision=2;
 let secondDone=false;const second=context.loadSettings(()=>revision===2).then(()=>secondDone=true);
 await new Promise(resolve=>setImmediate(resolve));
 assert.equal(secondDone,false,'字体仍在度量时不能提前结束后续回读');assert.deepEqual(panels,[]);assert.equal(measurements,1);
 finish();await Promise.all([first,second]);assert.deepEqual(panels,[480]);
});
test('字体失败释放共享任务且保持未就绪，下一次同配置重新度量',async()=>{
 let count=0;const panels=[],window={__augitLive:{settings:base},__augitSettingsReady:true};
 const context={window,invoke:async()=>({...base,fontSize:14}),applyTypographyPreview:async()=>{if(++count===1)throw Error('字体失败');},refreshTerminalTypography:()=>{},applySavedPanelSizes:()=>panels.push('恢复')};
 vm.createContext(context);vm.runInContext(declaration,context);await context.loadSettings();
 assert.equal(window.__augitSettingsReady,false);assert.equal(window.__augitLive.settingsTypographyTask,undefined);assert.deepEqual(panels,[]);
 await context.loadSettings();assert.equal(count,2);assert.equal(window.__augitSettingsReady,true);assert.deepEqual(panels,['恢复']);
});
test('旧字体任务完成不能清除新字号仍在途的任务',async()=>{
 const finish=[],panels=[];let size=14;
 const window={__augitLive:{settings:base},__augitSettingsReady:true};
 const context={window,invoke:async()=>({...base,fontSize:size}),applyTypographyPreview:()=>new Promise(resolve=>finish.push(resolve)),refreshTerminalTypography:()=>{},applySavedPanelSizes:s=>panels.push(s.fontSize)};
 vm.createContext(context);vm.runInContext(declaration,context);
 const first=context.loadSettings();await new Promise(resolve=>setImmediate(resolve));size=16;
 const second=context.loadSettings();await new Promise(resolve=>setImmediate(resolve));const newer=window.__augitLive.settingsTypographyTask;
 finish[0]();await first;assert.equal(window.__augitLive.settingsTypographyTask,newer);assert.deepEqual(panels,[]);
 finish[1]();await second;assert.equal(window.__augitLive.settingsTypographyTask,undefined);assert.deepEqual(panels,[16]);
});
test('字体等待期间被新回读取代后不能恢复旧面板尺寸',async()=>{
 let revision=1,finishTypography,typographyStarted;const panels=[],terminal=[];
 const started=new Promise(resolve=>typographyStarted=resolve);
 const old={...base,fontSize:14,projectPanelWidth:320},latest={...base,fontSize:16,projectPanelWidth:480};
 const window={__augitLive:{settings:base},__augitSettingsReady:true};
 const context={window,invoke:async()=>old,applyTypographyPreview:()=>new Promise(resolve=>{finishTypography=resolve;typographyStarted();}),
  refreshTerminalTypography:()=>terminal.push(window.__augitLive.settings.fontSize),applySavedPanelSizes:settings=>panels.push(settings.projectPanelWidth)};
 vm.createContext(context);vm.runInContext(declaration,context);
 const first=context.loadSettings(()=>revision===1);await started;
 revision=2;context.invoke=async()=>latest;context.applyTypographyPreview=async()=>{};
 await context.loadSettings(()=>revision===2);finishTypography();await first;
 assert.equal(window.__augitLive.settings,latest);assert.deepEqual(panels,[480]);assert.deepEqual(terminal,[16]);
});
async function reload(previous,next,ready=true,initialSettings){
 const calls=[];
 const window={__augitLive:{settings:previous},__augitSettingsReady:ready};
 const context={window,invoke:async()=>{calls.push('read');return next;},applyTypographyPreview:async()=>calls.push('typography'),refreshTerminalTypography:()=>calls.push('terminal'),applySavedPanelSizes:()=>calls.push('panels')};
 vm.createContext(context);vm.runInContext(declaration,context);await context.loadSettings(undefined,initialSettings);return{calls,window};
}

for(const reason of ['request','workspace','settings'])test('字体等待结束后拒绝过期续接：'+reason,async()=>{
 let valid=true,finish,signal;const started=new Promise(resolve=>signal=resolve),calls=[];
 const window={__augitLive:{settings:base},__augitSettingsReady:false};
 const context={window,invoke:async()=>({...base,fontSize:14}),applyTypographyPreview:()=>new Promise(resolve=>{finish=resolve;signal();}),refreshTerminalTypography:()=>calls.push('terminal'),applySavedPanelSizes:()=>calls.push('panels')};
 vm.createContext(context);vm.runInContext(declaration,context);
 const pending=context.loadSettings(()=>valid);await started;
 if(reason==='request')valid=false;
 if(reason==='workspace')window.__augitLive={settings:{...base}};
 if(reason==='settings')window.__augitLive.settings={...base};
 finish();await pending;assert.deepEqual(calls,[]);assert.equal(window.__augitSettingsReady,false);
});
test('独立设置复用本次初读，后续回读仍取最新配置',async()=>{
 const initial={...base,theme:'Dark'},latest={...base,theme:'Light'};
 const first=await reload(initial,latest,false,initial);
 assert.equal(first.calls.filter(c=>c==='read').length,0);
 assert.equal(first.calls.filter(c=>c==='typography').length,1);
 assert.equal(first.window.__augitLive.settings.theme,'Dark');
 const next=await reload(initial,latest);
 assert.equal(next.calls.filter(c=>c==='read').length,1);
 assert.equal(next.window.__augitLive.settings.theme,'Light');
});
test('初次加载必须度量字体，后续相同字体不重新度量',async()=>{
 assert.ok((await reload(base,base,false)).calls.includes('typography'));
 assert.ok(!(await reload(base,{...base})).calls.includes('typography'));
});
test('每一项字体变化仍重新度量，其他设置保持更新',async()=>{
 for(const key of Object.keys(base))assert.ok((await reload(base,{...base,[key]:typeof base[key]==='number'?base[key]+1:'Tahoma'})).calls.includes('typography'),key);
 const r=await reload(base,{...base,showGitBranchesTags:false});
 assert.equal(r.window.__augitLive.logRefShowTags,false);assert.ok(r.calls.includes('panels'));
});
