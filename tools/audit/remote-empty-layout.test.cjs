// 空态使用实际共享渲染函数；不能用单独拼出的测试表单替代产品。
const {test}=require('node:test'),assert=require('node:assert/strict');
const path=require('node:path'),{pathToFileURL}=require('node:url');
test('长URL列宽使用与名称相同的粗体度量，显示字重保持普通',async()=>{
 const {chromium}=require(process.env.AUGIT_PLAYWRIGHT_MODULE||'playwright');
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 try {
  const page=await browser.newPage({viewport:{width:1800,height:1000},deviceScaleFactor:1.75});
  await page.goto(pathToFileURL(path.resolve('docs/ux-mockups/remote.html')).href);
  await page.waitForSelector('body[data-typography-preview=ready]');
  const result=await page.evaluate(()=>{
   const url='https://example.invalid/'+('WideRemote'.repeat(5))+'.git';
   document.querySelector('.remote-dialog .dialog-body').innerHTML=remoteManagerTable([{name:'origin',url}],0);measureRemoteDialog();
   const dialog=document.querySelector('.remote-dialog'),style=getComputedStyle(dialog),context=document.createElement('canvas').getContext('2d');
   context.font=`600 ${style.fontSize} ${style.fontFamily}`;
   const name=Math.max(30,Math.ceil(context.measureText('origin').width))+40;
   const boldUrl=Math.max(250,Math.ceil(context.measureText(url).width));
   const expected=Math.max(624/1.75,name+boldUrl+34);
   return {expected,width:dialog.getBoundingClientRect().width,weight:getComputedStyle(dialog.querySelector('.remote-url')).fontWeight};
  });
  assert.ok(Math.abs(result.width-result.expected)<0.1,JSON.stringify(result));
  assert.equal(result.weight,'400');
 } finally {await browser.close();}
});
test('远端两列分配与真实JTable伸缩样本一致',()=>{
 const source=require('node:fs').readFileSync(path.resolve('web/src/mockup.js'),'utf8');
 const body=source.match(/function remoteNameColumnWidth\(width, name, url\) \{([\s\S]*?)\n\}/);
 assert.ok(body);const widthOf=new Function('width','name','url',body[1]);
 for(const [total,name,url] of [[330,75,255],[331,76,255],[300,66,234],[100,28,72],[30,15,15]]){
  assert.equal(widthOf(total,70,250),name);assert.equal(total-widthOf(total,70,250),url);
 }
});
test('远端空态按StatusText上三分之一定位，表格外框完整且数据独立滚动',async()=>{
 const {chromium}=require(process.env.AUGIT_PLAYWRIGHT_MODULE||'playwright');
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 try{
  const page=await browser.newPage({viewport:{width:1100,height:700},deviceScaleFactor:1.75});
  for(const theme of ['light','dark'])for(const size of [12,40]){
   const url=pathToFileURL(path.resolve('docs/ux-mockups/remote.html'));url.searchParams.set('theme',theme);url.searchParams.set('ui-size',String(size));
   await page.goto(url.href);await page.waitForSelector('body[data-typography-preview=ready]');
   await page.evaluate(()=>{document.querySelector('.remote-dialog .dialog-body').innerHTML=remoteManagerTable([], -1);measureRemoteDialog();});
   const state=await page.evaluate(()=>{
    const table=document.querySelector('.remote-table'),head=document.querySelector('.remote-table-head'),empty=document.querySelector('.remote-empty');
    const t=table.getBoundingClientRect(),h=head.getBoundingClientRect(),e=empty.getBoundingClientRect(),s=getComputedStyle(empty),frame=getComputedStyle(document.querySelector('.remote-page'));
    const text=document.createRange();text.selectNodeContents(empty);const ink=text.getBoundingClientRect();
    const expectedTextTop=e.top+(e.height-ink.height)/3;
    if(Math.abs(ink.top-expectedTextTop)>1)throw Error('StatusText文字应在剩余高度三分之一处：'+JSON.stringify({actual:ink.top,expected:expectedTextTop}));
    return {text:empty.textContent,center:s.justifyContent,vertical:s.alignItems,left:e.left,right:e.right,top:e.top,bottom:e.bottom,table:[t.left,t.right,t.bottom],headBottom:h.bottom,borders:[frame.borderTopWidth,frame.borderLeftWidth,frame.borderRightWidth,frame.borderBottomWidth],disabled:[...document.querySelectorAll('[data-remote-action=edit],[data-remote-action=remove]')].every(n=>n.disabled)};
   });
   assert.equal(state.text,'Nothing to show');assert.ok(state.disabled);
   const disabledColors=await page.locator('.remote-toolbar button:disabled svg').evaluateAll(nodes=>nodes.map(node=>getComputedStyle(node).color));
   assert.equal(disabledColors.length,2);
   assert.ok(disabledColors.every(color=>color===(theme==='dark'?'rgb(90, 90, 90)':'rgb(189, 189, 189)')),'禁用图标应使用安装版GrayFilter灰阶，而不是Label禁用文字颜色');
   assert.ok(state.borders.every(v=>parseFloat(v)>0));assert.ok(Math.abs(state.top-state.headBottom)<1);assert.ok(Math.abs(state.bottom-state.table[2])<1);
   const chrome=await page.locator('.remote-dialog').evaluate(node=>{
    const rect=node.getBoundingClientRect(),header=node.querySelector('.dialog-header'),toolbar=node.querySelector('.remote-toolbar');
    return {width:rect.width,height:rect.height,header:header.getBoundingClientRect().height,toolbar:toolbar.getBoundingClientRect().height,weight:getComputedStyle(header).fontWeight,brand:header.querySelectorAll('.brand-mark').length,title:header.querySelector('.remote-title')?.textContent,buttons:[...toolbar.querySelectorAll('button')].map(button=>{const r=button.getBoundingClientRect(),t=toolbar.getBoundingClientRect();return {width:r.width,height:r.height,inside:r.top>=t.top-1&&r.bottom<=t.bottom+1};})};
   });
   assert.equal(chrome.brand,1);assert.equal(chrome.title,'Git Remotes');assert.equal(chrome.weight,'400');
   // 安装版组件实测：外层无布局边框，24px动作区之外另有1px分隔线，表头默认25px。
   const boxes=await page.locator('.remote-dialog').evaluate(node=>{
    const d=node.getBoundingClientRect(),t=node.querySelector('.remote-page').getBoundingClientRect();
    const h=node.querySelector('.remote-table-head').getBoundingClientRect(),body=node.querySelector('.remote-empty').getBoundingClientRect();
    return {border:getComputedStyle(node).borderWidth,left:t.left-d.left,top:t.top-d.top,head:h.height,emptyTop:body.top-h.top};
   });
   assert.equal(boxes.border,'0px');assert.equal(boxes.left,12);
   for(const button of chrome.buttons){assert.ok(button.inside);assert.ok(Math.abs(button.width-26)<0.1);assert.ok(Math.abs(button.height-24)<0.1);}
   if(size===12){assert.ok(Math.abs(chrome.width-624/1.75)<0.1);assert.ok(Math.abs(chrome.height-220)<0.1);assert.equal(chrome.header,40);assert.equal(chrome.toolbar,25);assert.equal(boxes.top,48);assert.equal(boxes.head,25);assert.equal(boxes.emptyTop,25);}
   for(const viewportWidth of [400,1100]){
    await page.setViewportSize({width:viewportWidth,height:700});
    await page.waitForFunction(()=>{const table=document.querySelector('.remote-table'),head=table.querySelector('.remote-table-head');return Math.abs(head.children[0].getBoundingClientRect().width-remoteNameColumnWidth(table.clientWidth,70,250))<0.1;});
   }
   const scroll=await page.evaluate(()=>{
    document.querySelector('.remote-dialog .dialog-body').innerHTML=remoteManagerTable(Array.from({length:30},(_,i)=>({name:'remote-'+i,url:'https://example.invalid/repository.git'})),0);measureRemoteDialog();
    const table=document.querySelector('.remote-table'),toolbar=document.querySelector('.remote-toolbar'),footer=document.querySelector('.dialog-footer');const before=[toolbar.getBoundingClientRect().top,footer.getBoundingClientRect().top];table.scrollTop=300;
    const textLeft=node=>node.getBoundingClientRect().left+parseFloat(getComputedStyle(node).paddingLeft);
    const head=table.querySelector('.remote-table-head'),row=table.querySelector('.remote-row');
    return {scrolled:table.scrollTop>0,before,after:[toolbar.getBoundingClientRect().top,footer.getBoundingClientRect().top],empty:document.querySelector('.remote-empty')!==null,
     columns:[...head.children].map((cell,index)=>({heading:textLeft(cell),value:textLeft(row.children[index]),headLeft:cell.getBoundingClientRect().left,rowLeft:row.children[index].getBoundingClientRect().left}))};
   });
   assert.ok(scroll.scrolled);assert.deepEqual(scroll.after,scroll.before);assert.equal(scroll.empty,false);
   const enabled=await page.locator('.remote-toolbar button svg').evaluateAll(nodes=>nodes.map(node=>({color:getComputedStyle(node).color,disabled:node.closest('button').disabled})));
   assert.ok(enabled.every(item=>!item.disabled&&item.color===(theme==='dark'?'rgb(195, 197, 203)':'rgb(108, 112, 126)')),'选择远端后动作恢复正常图标颜色');
   for(const column of scroll.columns){
    assert.ok(Math.abs(column.headLeft-column.rowLeft)<0.1,'表头与数据必须共享列边界');
    assert.equal(column.heading-column.headLeft,8,'JBTable表头renderer左右内距8');
    assert.equal(column.value-column.rowLeft,2,'无外边框SimpleColoredComponent正文左右内距2');
   }
  }
 }finally{await browser.close();}
});
