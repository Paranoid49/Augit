// 验证真实共享表单的内容高度；默认不溢出，受限视口只滚动正文。
const {test}=require('node:test'),assert=require('node:assert/strict');
const path=require('node:path'),{pathToFileURL}=require('node:url');
test('Define Remote完整内容适配字号和主题，限高不挤出底栏',async()=>{
 const {chromium}=require(process.env.AUGIT_PLAYWRIGHT_MODULE||'playwright');
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 try {
  const page=await browser.newPage({viewport:{width:1400,height:1000}});
  for(const theme of ['light','dark'])for(const size of [12,13,20,40]){
   const url=pathToFileURL(path.resolve('docs/ux-mockups/remote.html'));
   url.searchParams.set('theme',theme);url.searchParams.set('ui-size',String(size));
   await page.goto(url.href);await page.waitForSelector('body[data-typography-preview=ready]');
   await page.evaluate(()=>{
    const layer=document.createElement('div');layer.className='overlay-layer live-overlay remote-define-window';
    layer.innerHTML=dialog('Define Remote',remoteDefineBody({name:'origin',fetchUrl:'',pushUrl:'',fetch:true}),'<button class=primary-button>OK</button><button class=secondary-button>Cancel</button>',false,'remote-define-dialog');
    document.querySelector('.augit-window').append(layer);measureRemoteDialog();
   });
   const measure=()=>page.locator('.remote-define-dialog').evaluate(node=>{
    const body=node.querySelector('.dialog-body'),form=node.querySelector('.remote-define-form'),footer=node.querySelector('.dialog-footer');
    const b=body.getBoundingClientRect(),f=form.getBoundingClientRect(),r=node.getBoundingClientRect(),end=footer.getBoundingClientRect();
    return {scroll:body.scrollHeight-body.clientHeight,contentBottom:f.bottom,bodyBottom:b.bottom,height:r.height,footerBottom:end.bottom,dialogBottom:r.bottom,viewport:innerHeight};
   });
   const normal=await measure();
   assert.equal(normal.scroll,0,JSON.stringify({theme,size,...normal}));
   await page.setViewportSize({width:900,height:460});
   const limited=await measure();
   assert.ok(limited.footerBottom<=limited.dialogBottom+1&&limited.dialogBottom<=limited.viewport+1,JSON.stringify({theme,size,...limited}));
   await page.setViewportSize({width:1400,height:1000});
  }
 } finally {await browser.close();}
});
