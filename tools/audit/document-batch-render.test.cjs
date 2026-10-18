// 执行真实读取函数：后台批次统一渲染，普通打开及失败反馈保持原路径。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('web/src/live-data.js','utf8'),start=source.indexOf('async function openDocument(path, options = {})');
const declaration=source.slice(start,source.indexOf('try {'+String.fromCharCode(10)+'  await boot();',start));
function harness(failed=false,payload={path:'file.txt'}){
 const calls={renders:[],opened:[],status:0},live={tabs:[{id:'a',kind:'document',path:'file.txt'}],activeTabId:'a',status:{files:[]}};
 const context={window:{__augitLive:live,setTimeout},performance,detailViewToken:0,documentToken:0,markdownPreviewHintTimer:null,clearTimeout,
  applyPendingDocumentStatus:()=>calls.status++,scheduleMarkdownPreviewHint:()=>{},scheduleImagePreviewHint:()=>{},clearImagePreviewState:()=>{},
  fetchDocument:async()=>{if(failed)throw Error('读取失败');return payload;},toLiveDocument:value=>value,
  openDocumentTab:(...args)=>calls.opened.push(args),refreshAfterEvent:(...regions)=>calls.renders.push(regions),refresh:(...regions)=>calls.renders.push(regions),isMarkdownDocumentVisible:()=>false};
 vm.createContext(context);vm.runInContext(declaration,context);return {calls,live,open:options=>context.openDocument('file.txt',options)};
}
test('后台批次成功读取后交由调用方统一重绘',async()=>{
 const h=harness();await h.open({activate:false,deferRender:true});assert.equal(h.calls.opened.length,1);assert.equal(h.live.pendingDocument,null);assert.deepEqual(h.calls.renders,[]);
});
test('同值纯文本批次仍实际读取，但不改加载状态和标签模型',async()=>{
 const payload={path:'file.txt',kind:'Text',status:'TextReady',editor:'text',text:'相同内容',encoding:'UTF-8'};
 const h=harness(false,payload);h.live.tabs[0].document={...payload};h.live.document=h.live.tabs[0].document;
 assert.equal(await h.open({activate:false,deferRender:true}),false);
 assert.equal(h.calls.status,0);assert.equal(h.calls.opened.length,0);assert.equal(h.live.pendingDocument,null);
});
for(const kind of ['Markdown','Json','Png'])test('非纯文本仍保留原读取及更新流程：'+kind,async()=>{
 const payload={path:'file.txt',kind,status:'TextReady',text:'相同内容'};
 const h=harness(false,payload);h.live.tabs[0].document={...payload};h.live.document=h.live.tabs[0].document;
 assert.notEqual(await h.open({activate:false,deferRender:true}),false);assert.equal(h.calls.status,1);assert.equal(h.calls.opened.length,1);
});
for(const field of ['text','encoding','fileSize','lineEndings','status'])test('正文或显示元数据变化不能被同值判断吞掉：'+field,async()=>{
 const payload={path:'file.txt',kind:'Text',status:'TextReady',editor:'text',text:'原文',encoding:'UTF-8',fileSize:8,lineEndings:'LF'};
 const h=harness(false,{...payload,[field]:field==='fileSize'?9:'变化'});h.live.tabs[0].document=payload;h.live.document=payload;
 assert.notEqual(await h.open({activate:false,deferRender:true}),false);assert.equal(h.calls.opened.length,1);
});
test('普通打开仍等待对应区域重绘',async()=>{
 const h=harness();h.live.activeTabId='other';await h.open({activate:true});assert.equal(h.calls.opened.length,1);assert.deepEqual(h.calls.renders,[['side','editorContent','editorTabs','statusbar','titlebar']]);
});
test('批次读取失败仍保留原错误反馈与清理',async()=>{
 const h=harness(true);await h.open({activate:false,deferRender:true});assert.equal(h.live.pendingDocument,null);assert.equal(h.calls.opened.length,0);assert.ok(h.calls.renders.some(regions=>regions.includes('editorContent')));
});
