// 执行正式读取函数；旧请求失败不得清除新文档、错误或加载计时器。
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const source=fs.readFileSync('web/src/live-data.js','utf8');
const start=source.indexOf('async function openDocument(path, options = {})');
const declaration=source.slice(start,source.indexOf('try {'+String.fromCharCode(10)+'  await boot();',start));
function harness(){
 const requests=new Map(),calls={renders:[],cleared:[],closed:[]};
 const live={tabs:[],activeTabId:null,status:{files:[]}};
 const context={window:{__augitLive:live,setTimeout},performance,detailViewToken:0,documentToken:0,markdownPreviewHintTimer:null,
  clearTimeout:timer=>calls.cleared.push(timer),applyPendingDocumentStatus:()=>{},
  scheduleMarkdownPreviewHint:token=>context.markdownPreviewHintTimer=token,scheduleImagePreviewHint:()=>{},
  clearImagePreviewState:()=>calls.cleared.push('image'),isMarkdownDocumentVisible:()=>false,
  fetchDocument:path=>new Promise((resolve,reject)=>requests.set(path,{resolve,reject})),
  openDocumentTab:(path,payload)=>{live.document=payload;live.activeTabId=path;live.tabs.push({id:path,path,kind:'document'});},
  closeTab:id=>calls.closed.push(id),refresh:(...regions)=>calls.renders.push(regions),
  refreshAfterEvent:(...regions)=>calls.renders.push(regions)};
 vm.createContext(context);vm.runInContext(declaration,context);
 return {live,calls,context,requests,open:path=>context.openDocument(path,{activate:false,deferRender:true})};
}
test('旧读取晚到失败不能清空后来成功打开的正文',async()=>{
 const h=harness(),old=h.open('old.txt'),current=h.open('new.txt');
 h.requests.get('new.txt').resolve({path:'new.txt',text:'新正文'});await current;
 const cleared=h.calls.cleared.length;
 h.requests.get('old.txt').reject(Error('旧读取失败'));await old;
 assert.equal(h.live.document?.path,'new.txt');assert.equal(h.context.window.__augitError,undefined);
 assert.equal(h.calls.cleared.length,cleared);assert.deepEqual(h.calls.renders,[]);
});
test('旧失败不能清除仍在读取的新请求提示或关闭旧标签',async()=>{
 const h=harness(),old=h.open('old.txt'),current=h.open('new.txt');
 h.live.tabs=[{id:'old',path:'old.txt',kind:'document'}];
 h.live.status.files=[{path:'old.txt',kind:'Deleted'}];
 h.requests.get('old.txt').reject(Error('旧文件删除'));await old;
 assert.equal(h.live.pendingDocument,'new.txt');assert.equal(h.context.markdownPreviewHintTimer,2);
 assert.deepEqual(h.calls.cleared,[]);assert.deepEqual(h.calls.closed,[]);assert.deepEqual(h.calls.renders,[]);
 h.requests.get('new.txt').resolve({path:'new.txt'});await current;
});
test('当前请求失败仍反馈错误并清理加载态',async()=>{
 const h=harness(),current=h.open('new.txt');
 h.requests.get('new.txt').reject(Error('当前读取失败'));await current;
 assert.equal(h.live.pendingDocument,null);assert.equal(h.live.document,null);
 assert.match(h.context.window.__augitError,/当前读取失败/);
 assert.ok(h.calls.renders.some(regions=>regions.includes('editorContent')));
});
