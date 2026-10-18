// 浏览器执行真实产品函数，验证同值更新静止、真实状态和错误恢复仍生效。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs');
const source=fs.readFileSync('web/src/live-data.js','utf8');
function declaration(start,end){const offset=source.indexOf(start),stop=source.indexOf(end,offset);assert.ok(offset>=0&&stop>offset);return source.slice(offset,stop);}
const functions=declaration('function syncChangesToolbar()', '/** 状态变化后的刷新')+declaration('function restoreChangesState()', '/** 记住改动列表的滚动位置');
test('同值补丁不变更DOM，真实状态和错误恢复继续更新',async()=>{
 const {chromium}=require(process.env.AUGIT_PLAYWRIGHT_MODULE||'playwright');
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 try{
  const page=await browser.newPage();
  await page.setContent('<div class="side-tool"><div class="changes-layout"><div class="toolbar"><button data-action="show-change-diff"></button><button data-action="rollback-change"></button></div><div class="changes-list">'+['Changes','Unversioned Files'].map((g,i)=>'<div class="check-group-row" data-group="'+g+'"><span class="commit-meta"></span><span class="fake-check"></span></div><div class="change-file-row" data-path="'+i+'.txt"><span class="tree-name"></span></div>').join('')+'</div><div class="commit-box"><span class="commit-count"></span><textarea class="message-field"></textarea><span class="commit-feedback"></span></div></div></div>');
  await page.evaluate(()=>{
   window.__augitLive={status:{files:[{path:'0.txt',name:'0.txt',kind:'Modified',group:'Changes',checked:true},{path:'1.txt',name:'1.txt',kind:'Unversioned',group:'UnversionedFiles',checked:false}]},commitDraft:'保留草稿',changesScrollTop:0,selectedChangePath:'0.txt'};
   window.gitStatusDescription=file=>file.kind;window.changeAccessibleName=(kind,name)=>kind+' '+name;
   window.statusRefreshNotice=()=>'<div class="status-stale">'+window.__augitLive.statusError+'</div>';
  });
  const mockup=fs.readFileSync('web/src/mockup.js','utf8');
  const countStart=mockup.indexOf('function changesFileCount(count)');
  await page.addScriptTag({content:mockup.slice(countStart,mockup.indexOf('// 单仓库',countStart))+functions});
  const result=await page.evaluate(()=>{
   const apply=()=>{if(!patchChangesList())throw Error('夹具必须命中原地补丁');restoreChangesState();syncChangesToolbar();};
   apply();const row=document.querySelector('.change-file-row'),draft=document.querySelector('textarea');draft.focus();draft.setSelectionRange(1,3);
   if([...document.querySelectorAll('.check-group-row .commit-meta')].some(node=>node.textContent!=='1 file'))throw Error('首次补丁必须保留共享渲染的单数计数');
   const observer=new MutationObserver(()=>{});observer.observe(document.body,{subtree:true,childList:true,characterData:true,attributes:true});
   apply();const unchanged=observer.takeRecords().map(r=>({type:r.type,attribute:r.attributeName,className:r.target.className}));
   window.__augitLive.status.files[0].kind='Deleted';window.__augitLive.status.files[0].checked=false;window.__augitLive.selectedChangePath=null;window.__augitCommitError='提交失败';window.__augitLive.statusError='状态读取失败';apply();
   const changed={kind:document.querySelector('.tree-name').className,checked:document.querySelector('.fake-check').getAttribute('aria-checked'),disabled:[...document.querySelectorAll('button')].every(b=>b.disabled),feedback:document.querySelector('.commit-feedback').textContent,stale:!!document.querySelector('.status-stale')};
   window.__augitLive.statusError=null;window.__augitCommitError='';apply();observer.takeRecords();apply();
   const recovered=observer.takeRecords().map(r=>({type:r.type,attribute:r.attributeName}));observer.disconnect();
   return {unchanged,changed,recovered,stale:!!document.querySelector('.status-stale'),rowPreserved:row===document.querySelector('.change-file-row'),draft:draft.value,selection:[draft.selectionStart,draft.selectionEnd],focused:document.activeElement===draft};
  });
  assert.deepEqual(result.unchanged,[]);assert.deepEqual(result.recovered,[]);
  assert.deepEqual(result.changed,{kind:'tree-name file-status-deleted',checked:'false',disabled:true,feedback:'提交失败',stale:true});
  assert.equal(result.stale,false);assert.equal(result.rowPreserved,true);assert.equal(result.draft,'保留草稿');assert.deepEqual(result.selection,[1,3]);assert.equal(result.focused,true);
 }finally{await browser.close();}
});
