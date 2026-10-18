// 仅验证跳转行布局；静态几何不能替代实机截图或系统阴影验收。
const assert=require('node:assert/strict');
const path=require('node:path');
const {pathToFileURL}=require('node:url');
const {chromium}=require(process.argv[2]);
(async()=>{
 let browser,passed=0;
 try {
  browser=await chromium.launch({executablePath:process.argv[3],headless:true});
  for(const dpr of [1,1.75,2]) for(const size of [12,40]) for(const theme of ['light','dark']) {
   const context=await browser.newContext({viewport:{width:1180,height:760},deviceScaleFactor:dpr});
   try {
    const page=await context.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));
    const url=pathToFileURL(path.resolve('docs/ux-mockups/go-to-line.html'));
    url.search='theme='+theme+'&ui-size='+size;
    await page.goto(url.href);await page.waitForSelector('body[data-typography-preview="ready"]');
    await page.locator('.goto-line-dialog input').focus();
    const focusColors=await page.locator('.goto-line-dialog input').evaluate(field=>{
     const style=getComputedStyle(field.parentElement,'::after'),probe=document.createElement('span');
     probe.style.color='var(--augit-accent-brand)';field.after(probe);
     const expected=getComputedStyle(probe).color;probe.remove();
     return {border:style.borderTopColor,width:style.borderTopWidth,inset:style.top,expected};
    });
    assert.equal(focusColors.border,focusColors.expected,'跳转行焦点描边须使用实机品牌蓝');
    assert.equal(focusColors.width,'2px','聚焦描边为2逻辑像素');
    assert.equal(focusColors.inset,'2px','聚焦边框内缩2逻辑像素');
    const focusedBox=await page.locator('.goto-line-dialog input').boundingBox();
    await page.locator('.goto-line-dialog input').evaluate(node=>node.blur());
    assert.deepEqual(await page.locator('.goto-line-dialog input').boundingBox(),focusedBox,'焦点切换不能移动输入框');
    const blurred=await page.locator('.goto-line-field').evaluate(node=>{const s=getComputedStyle(node,'::after');return {width:s.borderTopWidth,inset:s.top};});
    assert.deepEqual(blurred,{width:'1px',inset:'3px'});
    await page.locator('.goto-line-dialog input').focus();
    const result=await page.locator('.goto-line-dialog').evaluate(dialog=>{
     const rect=n=>{const r=n.getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height};};
     const mark=dialog.querySelector('.dialog-header .brand-mark'),title=dialog.querySelector('.goto-line-title');
     return {dialog:rect(dialog),field:rect(dialog.querySelector('input')),label:rect(dialog.querySelector('label')),
      mark:mark?rect(mark):null,title:title?rect(title):null,close:rect(dialog.querySelector('.dialog-header .icon-button')),
      buttons:[...dialog.querySelectorAll('.dialog-footer a')].map(n=>({text:n.textContent,...rect(n),scroll:n.scrollWidth,client:n.clientWidth})),
      titleHeight:dialog.querySelector('.dialog-header').clientHeight,titleScroll:dialog.querySelector('.dialog-header').scrollHeight};
    });
    assert.ok(result.mark&&result.title,'标题栏须保留Augit品牌图标和独立标题');
    assert.ok(Math.abs(result.mark.w-16)<.1&&Math.abs(result.mark.h-16)<.1);
    assert.ok(Math.abs(result.mark.x-result.dialog.x-12)<.1);
    assert.ok(Math.abs(result.title.x-result.dialog.x-40)<.1);
    assert.ok(Math.abs(result.close.w-32)<.1);
    assert.ok(Math.abs(result.close.x+result.close.w-result.dialog.x-result.dialog.w)<.1);
    assert.ok(result.title.x+result.title.w<=result.close.x+.1,'标题不能覆盖关闭命中区');
    assert.deepEqual(result.buttons.map(b=>b.text),['OK','Cancel']);
    assert.ok(result.buttons[0].x+result.buttons[0].w<=result.buttons[1].x+.1);
    for(const r of [result.field,result.label,...result.buttons]) {
     assert.ok(r.x>=result.dialog.x-.1&&r.y>=result.dialog.y-.1);
     assert.ok(r.x+r.w<=result.dialog.x+result.dialog.w+.1&&r.y+r.h<=result.dialog.y+result.dialog.h+.1,JSON.stringify({size,theme,dpr,result}));
    }
    assert.ok(result.field.w>=150,'大字号仍保留可用输入宽度');
    assert.ok(result.titleScroll<=result.titleHeight+1,'标题不可纵向裁切');
    for(const b of result.buttons)assert.ok(b.scroll<=b.client+1,'按钮文案不可裁切');
    if(size===12) {
     assert.ok(Math.abs(result.dialog.w-334)<1);
     assert.ok(Math.abs(result.field.w-200)<.1);
     assert.ok(Math.abs(result.field.h-34)<.1);
     assert.ok(Math.abs(result.field.x-result.dialog.x-113)<.1);
     assert.ok(Math.abs(result.field.y-result.dialog.y-52)<.1);
     assert.ok(Math.abs(result.dialog.h-144)<.1);
    }
    const native=await page.locator('.goto-line-dialog').evaluate(dialog=>{
     const read=()=>({box:dialog.getBoundingClientRect().toJSON(),radius:getComputedStyle(dialog).borderRadius,shadow:getComputedStyle(dialog).boxShadow,border:getComputedStyle(dialog,'::after').borderTopColor});
     const before=read();dialog.dataset.nativeWindow='true';const active=read();delete dialog.dataset.nativeWindow;return{before,active,restored:read()};
    });
    assert.deepEqual(native.active.box,native.before.box,'原生描边归属不能改变布局');
    assert.equal(native.active.radius,'0px');assert.equal(native.active.shadow,'none');
    assert.equal(native.active.border,'rgba(0, 0, 0, 0)');assert.deepEqual(native.restored,native.before);
    assert.deepEqual(errors,[]);passed++;
   } finally {await context.close();}
  }
  console.log(JSON.stringify({passed}));
 } finally {await browser?.close();console.log('本次浏览器已关闭；未启动服务器');}
})().catch(error=>{console.error(error);process.exitCode=1;});
