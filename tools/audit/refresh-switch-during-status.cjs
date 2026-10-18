// 正常入口真实文件通知期间切换已有标签；不延迟桥接、不替换产品状态。
const assert=require('node:assert/strict'),path=require('node:path');
const {waitAsyncCondition}=require('./wait-async-condition.cjs');
function qualifies(row){
 return row.clickedDuringStatus===true&&row.selectedPath==='other.txt'&&row.backgroundTextUpdated===true&&row.pendingDocument===null;
}
async function exerciseSwitch({page,workspace,output,write,documentText}){
 await page.locator('[data-tree-path="other.txt"]').dblclick();
 await page.waitForFunction(()=>window.__augitLive.document?.path==='other.txt'&&document.querySelector('.code-view')?.textContent.includes('Other document'));
 const tabs=await page.evaluate(()=>Object.fromEntries(window.__augitLive.tabs.filter(t=>t.kind==='document').map(t=>[t.path,t.id])));
 assert.ok(tabs['refresh.txt']&&tabs['other.txt']);
 await page.evaluate(otherId=>{
  window.__switchAuditClicks=[];
  document.addEventListener('click',event=>{
   if(event.target.closest('[data-tab-id]')?.dataset.tabId!==otherId)return;
   window.__switchAuditClicks.push({at:performance.now(),duringStatus:window.__refreshAuditRequests.some(r=>r.method==='git/status'&&!Number.isFinite(r.ended))});
  },true);
 },tabs['other.txt']);
 const rows=[];
 for(let generation=1;generation<=4;generation++){
  await page.locator('[data-tab-id="'+tabs['refresh.txt']+'"]').click();
  await page.waitForFunction(()=>window.__augitLive.document?.path==='refresh.txt');
  const offset=await page.evaluate(()=>window.__refreshAuditRequests.length);
  await write(path.join(workspace,'refresh.txt'),documentText(generation));
  await waitAsyncCondition(()=>page.evaluate(offset=>window.__refreshAuditRequests.slice(offset).some(r=>r.method==='git/status'&&!Number.isFinite(r.ended)),offset),{label:'真实状态读取在途',polling:1});
  await page.locator('[data-tab-id="'+tabs['other.txt']+'"]').click();
  await waitAsyncCondition(()=>page.evaluate(({offset,generation})=>{
   const live=window.__augitLive,tab=live.tabs.find(t=>t.path==='refresh.txt');
   return !!tab?.document?.text?.includes('generation '+generation)&&!live.pendingDocument&&window.__refreshAuditRequests.slice(offset).some(r=>r.method==='workspace/list'&&Number.isFinite(r.ended));
  },{offset,generation}),{label:'后台标签获得本批内容',polling:20});
  const row=await page.evaluate(({offset,generation})=>{
   const live=window.__augitLive,click=window.__switchAuditClicks.at(-1);
   return {generation,clickedDuringStatus:click?.duringStatus===true,click,selectedPath:live.document?.path,pendingDocument:live.pendingDocument??null,backgroundTextUpdated:live.tabs.find(t=>t.path==='refresh.txt')?.document?.text?.includes('generation '+generation)===true,requests:window.__refreshAuditRequests.slice(offset)};
  },{offset,generation});
  rows.push(row);await write(path.join(output,'switch-during-status.json'),JSON.stringify({rows,limitation:'只统计确实在状态请求期间发生的真实点击；未注入延时，不覆盖新文件在途的全部竞态。'},null,2)+String.fromCharCode(10));
  assert.equal(row.selectedPath,'other.txt');assert.equal(row.backgroundTextUpdated,true);assert.equal(row.pendingDocument,null);
 }
 assert.ok(rows.some(qualifies),'未命中真实状态等待窗口，不能登记竞态实机通过');
 await page.locator('[data-tab-id="'+tabs['refresh.txt']+'"]').click();
 await page.waitForFunction(()=>document.querySelector('.code-view')?.textContent.includes('generation 4'));
 console.log(JSON.stringify({switchDuringStatus:true,qualifiedSamples:rows.filter(qualifies).length,total:rows.length}));
}
module.exports={qualifies,exerciseSwitch};
