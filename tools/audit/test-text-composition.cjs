// 在隔离浏览器验证探针模式与异常恢复；这些检查不是系统像素验收。
const assert=require('node:assert/strict');
const fs=require('node:fs/promises'),os=require('node:os'),path=require('node:path');
const {probeTextComposition}=require('./probe-text-composition.cjs');
const {chromium}=require(process.argv[2]);
(async()=>{
 const directory=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-text-probe-test-'));
 let browser;
 try {
  browser=await chromium.launch({executablePath:process.argv[3],headless:true});
  const page=await browser.newPage();
  await page.setContent('<div class="scrim" style="background:transparent"></div><section class="dialog" style="border-radius:9px;overflow:hidden"><div class="conflict-page"><div class="conflict-block" style="background:white"><span class="conflict-line">sample</span></div></div></section>');
  await page.evaluate(()=>{window.__augitLive={conflictDraft:{text:'sample'}};});
  const snapshot=()=>page.evaluate(()=>[document.documentElement,document.body,...document.querySelectorAll('.dialog,.scrim,.conflict-block,.conflict-line')].map(node=>node.getAttribute('style')||null));
  const before=await snapshot(),modes=[];
  await probeTextComposition(page,browser,directory,async name=>{
   modes.push(name);
   const state=await page.evaluate(()=>({radius:document.querySelector('.dialog').style.borderRadius,
    scrim:document.querySelector('.scrim').style.visibility,layer:document.querySelector('.conflict-block').style.willChange,
    background:document.documentElement.style.backgroundColor}));
   if(name==='text-no-rounded-clip.png')assert.equal(state.radius,'0px');
   if(name==='text-opaque-root.png')assert.equal(state.background,'rgb(255, 255, 255)');
   if(name==='text-no-scrim.png')assert.equal(state.scrim,'hidden');
   if(name==='text-opaque-text-layer.png')assert.equal(state.layer,'transform');
   if(name==='text-text-layer.png')assert.equal(state.layer,'');
  });
  assert.equal(modes.length,10);assert.deepEqual(await snapshot(),before);
  const report=JSON.parse(await fs.readFile(path.join(directory,'text-composition.json'),'utf8'));
  assert.equal(report.reports.length,10);assert.ok(report.reports.every(item=>item.draft==='sample'));
  let calls=0;
  await assert.rejects(probeTextComposition(page,browser,directory,async()=>{if(++calls===3)throw Error('预期截图失败');}),/预期截图失败/);
  assert.deepEqual(await snapshot(),before);
  console.log(JSON.stringify({passed:2,modes:modes.length,restoredAfterFailure:true}));
 } finally {if(browser)await browser.close();await fs.rm(directory,{recursive:true,force:true});}
})().catch(error=>{console.error(error);process.exitCode=1;});
