// 只覆盖外部变化队列，不执行全量视觉套件。
module.exports = async function({openScene,check}) {
 const scene=await openScene('scene=main-project');
 const page=scene.page;
 await page.evaluate(()=>{window.__nextChanges=null;});
 const before=await page.evaluate(()=>window.__changesReads||0);
 await page.evaluate(()=>window.__hostPush('workspace-changed',{files:['D:/live-ws/audit-other.txt'],gitMetadata:false}));
 await page.waitForFunction(before=>(window.__changesReads||0)>before,before);
 check('外部文件通知立即消费宿主队列',await page.evaluate(()=>window.__nextChanges===null));
 await page.waitForTimeout(500);
 const count=await page.evaluate(()=>window.__statusCalls||0);
 await page.waitForTimeout(2300);
 check('兜底读取不重放同一外部变化',await page.evaluate(count=>(window.__statusCalls||0)===count,count));
 await page.evaluate(()=>window.__hostPush('workspace-changed',{files:['D:/live-ws/audit-other.txt'],gitMetadata:false}));
 await page.waitForFunction(count=>(window.__statusCalls||0)>count,count);
 check('同路径下一次变化仍更新状态',true);
 await page.waitForTimeout(500);
 const missed=await page.evaluate(()=>{window.__nextChanges={files:['D:/live-ws/missed.txt'],gitMetadata:false};return window.__statusCalls||0;});
 await page.waitForFunction(count=>(window.__statusCalls||0)>count,missed);
 check('没有推送时兜底仍恢复更新',true);
 check('队列更新没有网页错误',scene.errors.length===0);
 await page.close();
};
