// 真实浏览器Range核对字符步长；finally关闭测试浏览器。
const assert=require('node:assert/strict'),path=require('node:path');
const {pathToFileURL}=require('node:url');
const {chromium}=require(process.argv[2]);
(async()=>{
 const browser=await chromium.launch({executablePath:process.argv[3],headless:true});let passed=0;
 try {
  for(const dpr of [1,1.25,1.75,2]) {
   const context=await browser.newContext({viewport:{width:1280,height:800},deviceScaleFactor:dpr});
   try {
    const page=await context.newPage();
    for(const size of [13,26,40]) for(const [scene,selector] of [['text-viewer','.code-line > span:last-child'],['commit-diff','.diff-code-line .code-glyph'],['conflict-resolver','.conflict-line'],['blame','.blame-row > span']]) {
     const url=pathToFileURL(path.resolve(`docs/ux-mockups/${scene}.html`));
     url.search=`theme=dark&code-size=${size}`;await page.goto(url.href);
     await page.waitForSelector('body[data-typography-preview=ready]');
     await page.evaluate(()=>document.fonts.ready);
     const actual=await page.locator(selector).first().evaluate(view=>{
      const saved=[...view.childNodes];view.textContent='00000000000000000000000000000000';
      const probe=view;
      const text=probe.firstChild,range=document.createRange();range.setStart(text,0);range.setEnd(text,1);const left=range.getBoundingClientRect().left;
      range.setStart(text,31);range.setEnd(text,32);const last=range.getBoundingClientRect().left;
      const style=getComputedStyle(probe);const result={advance:(last-left)/31,size:parseFloat(style.fontSize),spacing:style.letterSpacing};
      if(view.closest('.code-line')) {
       const mark=document.createElement('mark');mark.textContent=view.textContent;view.replaceChildren(mark);
       range.setStart(mark.firstChild,0);range.setEnd(mark.firstChild,1);const start=range.getBoundingClientRect().left;
       range.setStart(mark.firstChild,31);range.setEnd(mark.firstChild,32);result.highlightAdvance=(range.getBoundingClientRect().left-start)/31;
      }
      view.replaceChildren(...saved);return result;
     });
     const expected=Math.round(Math.round(actual.size*dpr)*.6)/dpr;
     assert.ok(Math.abs(actual.advance-expected)<.018,`${scene}/${dpr}: ${JSON.stringify(actual)} expected ${expected}`);passed++;
     if(actual.highlightAdvance!==undefined)assert.ok(Math.abs(actual.highlightAdvance-expected)<.018,'查找高亮保持字符步长');
     if(scene==='text-viewer' && size===13 && dpr===1.75) {
      const checks=await page.evaluate(async()=>{
       const view=document.querySelector('.code-view'),glyph=view.querySelector('.code-line > span:last-child');
       const original=glyph.style.letterSpacing;glyph.style.letterSpacing='0px';
       const negative=parseFloat(getComputedStyle(glyph).letterSpacing)||0;glyph.style.letterSpacing=original;
       await applyTypography({monospaceFamily:'Consolas'});
       const fallback=getComputedStyle(glyph).letterSpacing;
       await applyTypography({monospaceFamily:'JetBrains Mono',codeSize:26});
       const changed=getComputedStyle(glyph).letterSpacing;
       await applyTypography({monospaceFamily:'JetBrains Mono',codeSize:13});
       return {negative,fallback,changed,restored:getComputedStyle(glyph).letterSpacing};
      });
      assert.ok(Math.abs(checks.negative-parseFloat(actual.spacing))>.1,'旧全局零字距不能冒充通过');
      assert.equal(parseFloat(checks.fallback)||0,0,'回退字体清除专用字距');
      assert.ok(Math.abs(parseFloat(checks.changed)-parseFloat(actual.spacing))>.01,'字号变化重新度量');
      assert.ok(Math.abs(parseFloat(checks.restored)-parseFloat(actual.spacing))<.001,'切回恢复原值');
     }
    }
   }finally{await context.close();}
  }
  console.log(JSON.stringify({passed}));
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
