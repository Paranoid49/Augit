// 执行真实变化处理函数：数据未变不能重画整个背景，真实变化仍须及时显示。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('web/src/live-data.js','utf8');
function declaration(start,end){const offset=source.indexOf(start);return source.slice(offset,source.indexOf(end,offset));}
function harness(options={}){
 const calls={regions:[],details:0,status:0,tree:0,document:0,background:0,diff:0,closed:[]};
 const live={root:'D:/repo',status:{branch:'main',files:[]},history:{commits:[]},tree:[{path:'file.txt'}],tabs:[],...options.live};
 const context={window:{__augitLive:live},documentToken:0,selectedHistoryCommit:()=>null,
  loadStatus:async()=>{calls.status++;if(options.status)live.status=options.status;if(options.recover)live.statusError=null;if(options.duringStatus)options.duringStatus(live,context);},
  loadHistory:async()=>{},
  reloadChangedBackgroundDocument:async()=>{calls.background++;return options.backgroundBecameActive===true;},
  loadDiff:async()=>{calls.diff++;},openDocument:async(path,readOptions)=>{calls.document++;assert.equal(readOptions.deferRender,true);return options.documentChanged;},closeTab:id=>{calls.closed.push(id);},
  refreshLoadedDirectories:async()=>{calls.tree++;if(options.tree)live.tree=options.tree;},
  refreshStatusRegions:(...regions)=>calls.regions.push(regions),refreshCommitDetails:()=>{calls.details++;}};
 vm.createContext(context);
 vm.runInContext(declaration('function buildSnapshot(status, history)', '/** 当前已应用快照')+declaration('async function applyWorkspaceChanges(changes)', '/**'+String.fromCharCode(10)+' * 稳定快照'),context);
 return{calls,run:(files=['D:/repo/obj/build'],gitMetadata=false)=>context.applyWorkspaceChanges({files,gitMetadata})};
}
test('无关文件通知仍读取状态和目录，数据相同时不重画背景',async()=>{
 const h=harness();await h.run();assert.equal(h.calls.status,1);assert.equal(h.calls.tree,1);assert.deepEqual(h.calls.regions,[]);assert.equal(h.calls.details,0);
});
test('当前正文同值仍读状态和目录，但不要求重绘正文',async()=>{
 const h=harness({live:{document:{path:'file.txt'}},documentChanged:false});await h.run(['D:/repo/file.txt']);
 assert.equal(h.calls.document,1);assert.equal(h.calls.status,1);assert.equal(h.calls.tree,1);assert.deepEqual(h.calls.regions,[]);
});
test('新增目录只更新项目树，不重建Git历史和标题栏',async()=>{
 const h=harness({tree:[{path:'file.txt'},{path:'new.txt'}]});await h.run();assert.deepEqual(h.calls.regions,[['side']]);assert.equal(h.calls.details,0);
});
test('状态真正变化仍更新改动列表、标题和Git区域',async()=>{
 const h=harness({status:{branch:'main',files:[{path:'new.txt',kind:'Added',group:'Unversioned'}]}});await h.run();assert.ok(h.calls.regions[0].includes('bottomTool'));assert.equal(h.calls.details,1);
});
test('状态相同但错误恢复仍移除过期提示',async()=>{
 const h=harness({live:{statusError:'失败'},recover:true});await h.run();assert.ok(h.calls.regions[0].includes('statusbar'));
});
for(const kind of ['document','diff'])test('当前'+kind+'内容变化不能被相同Git状态吞掉',async()=>{
 const h=harness({live:{[kind]:{path:'file.txt'}}});await h.run(['D:/repo/file.txt']);assert.equal(h.calls[kind],1);assert.ok(h.calls.regions[0].includes('editorContent'));assert.equal(h.calls.details,0);
});
test('Git元数据批次仍走原有完整状态刷新',async()=>{
 const h=harness();await h.run([],true);assert.ok(h.calls.regions[0].includes('bottomTool'));
});
for(const gitMetadata of [false,true])test('当前文件与Git元数据同批只读取一次最新状态 '+gitMetadata,async()=>{
 const h=harness({live:{document:{path:'file.txt'}}});await h.run(['D:/repo/file.txt'],gitMetadata);
 assert.equal(h.calls.status,1);assert.equal(h.calls.document,1);assert.equal(h.calls.tree,1);
});
test('当前文件删除判断仍使用本批最新状态',async()=>{
 const h=harness({live:{document:{path:'file.txt'},tabs:[{id:'document-1',kind:'document',path:'file.txt'}]},status:{branch:'main',files:[{path:'file.txt',kind:'Deleted'}]}});
 await h.run(['D:/repo/file.txt'],true);assert.equal(h.calls.status,1);assert.equal(h.calls.document,0);assert.deepEqual(h.calls.closed,['document-1']);
});
test('状态读取仅在本批复用，连续真实变化不能跨批缓存',async()=>{
 const h=harness({live:{document:{path:'file.txt'}}});await h.run(['D:/repo/file.txt']);await h.run(['D:/repo/file.txt']);
 assert.equal(h.calls.status,2);assert.equal(h.calls.document,2);
});
for(const mode of ['切换到其他正文','新文件读取在途','当前标签关闭'])test('等待状态时'+mode+'不能启动旧正文读取',async()=>{
 const h=harness({live:{document:{path:'file.txt'}},duringStatus:(live,context)=>{
  if(mode==='切换到其他正文')live.document={path:'other.txt'};
  if(mode==='新文件读取在途'){context.documentToken++;live.pendingDocument='other.txt';}
  if(mode==='当前标签关闭')live.document=null;
 }});
 await h.run(['D:/repo/file.txt']);assert.equal(h.calls.document,0);assert.equal(h.calls.status,1);assert.equal(h.calls.tree,1);
 assert.equal(h.calls.background,1);
 assert.ok(h.calls.regions.every(regions=>!regions.includes('editorContent')));
});
test('后台读取期间切回原标签仍渲染新内容',async()=>{
 const h=harness({live:{document:{path:'file.txt'}},duringStatus:live=>live.document={path:'other.txt'},backgroundBecameActive:true});
 await h.run(['D:/repo/file.txt']);assert.equal(h.calls.background,1);assert.ok(h.calls.regions[0].includes('editorContent'));
});
test('切换后旧文件已删除仍关闭旧标签而不读取',async()=>{
 const h=harness({live:{document:{path:'file.txt'},tabs:[{id:'old',kind:'document',path:'file.txt'}]},status:{files:[{path:'file.txt',kind:'Deleted'}]},duringStatus:live=>live.document={path:'other.txt'}});
 await h.run(['D:/repo/file.txt']);assert.deepEqual(h.calls.closed,['old']);assert.equal(h.calls.document,0);assert.equal(h.calls.background,0);
});
test('批次开始前已有新文件读取也不能被旧正文刷新抢占',async()=>{
 const h=harness({live:{document:{path:'file.txt'},pendingDocument:'other.txt'}});
 await h.run(['D:/repo/file.txt']);assert.equal(h.calls.document,0);assert.equal(h.calls.background,1);
});
