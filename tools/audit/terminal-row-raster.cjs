// 同一多行字形检查浏览器舍入相位，直接渲染夹具，不发送终端命令。
const fs=require('node:fs/promises'),path=require('node:path');
const {chromium}=require(process.argv[2]);
(async()=>{
 const browser=await chromium.launch({executablePath:process.argv[3],headless:true});
 try{
  const context=await browser.newContext({viewport:{width:350,height:700},deviceScaleFactor:1.75});
  const page=await context.newPage(),output=process.argv[4];await fs.mkdir(output,{recursive:true});
  await page.setContent('<div id="terminal"></div>');
  await page.addStyleTag({path:'web/vendor/xterm/xterm.css'});
  const font=await fs.readFile('web/vendor/jetbrains-mono/JetBrainsMono-Regular.woff2');
  await page.addStyleTag({content:'@font-face{font-family:"JetBrains Mono";src:url(data:font/woff2;base64,'+font.toString('base64')+')}body{margin:0;background:white}#terminal{position:absolute;left:20px;top:20px}'});
  await page.addScriptTag({path:'web/vendor/xterm/xterm.js'});
  await page.evaluate(async()=>{await document.fonts.load('13px "JetBrains Mono"');window.term=new Terminal({cols:10,rows:20,fontFamily:'JetBrains Mono',fontSize:13,lineHeight:1,augitCellWidth:8,augitCellHeight:18,theme:{background:'#ffffff',foreground:'#000000'}});term.open(document.querySelector('#terminal'));await new Promise(resolve=>term.write(Array(20).fill('dddddddd').join(String.fromCharCode(13,10)),resolve));});
  const results=[];
  await page.waitForSelector('.xterm-rows>div>span');
  const baseline=await page.evaluate(()=>{
    const row=term.element.querySelector('.xterm-rows>div'),style=getComputedStyle(row);
    const marker=document.createElement('span');marker.style.cssText='display:inline-block;width:0;height:0;padding:0;margin:0;vertical-align:baseline';row.firstElementChild.append(marker);
    const offset=marker.getBoundingClientRect().top-row.getBoundingClientRect().top;marker.remove();
    const context=document.createElement('canvas').getContext('2d');context.font=style.fontSize+' '+style.fontFamily;
    const metrics=context.measureText('d');return {offset,font:context.font,fontSize:style.fontSize,lineHeight:style.lineHeight,ascent:metrics.fontBoundingBoxAscent,descent:metrics.fontBoundingBoxDescent,actualAscent:metrics.actualBoundingBoxAscent};
  });
  console.log(JSON.stringify(baseline));await fs.writeFile(path.join(output,'browser-baseline.json'),JSON.stringify(baseline,null,2));
  for(let phase=0;phase<4;phase++){
   await page.evaluate(phase=>document.querySelector('#terminal').style.top=(20+phase/4/devicePixelRatio)+'px',phase);
   await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
   results.push(await page.evaluate(()=>[...document.querySelectorAll('.xterm-rows>div')].map(row=>({top:row.getBoundingClientRect().top,height:row.getBoundingClientRect().height}))));
   await page.screenshot({path:path.join(output,'browser-phase-'+phase+'.png')});
  }
  await fs.writeFile(path.join(output,'browser-rows.json'),JSON.stringify(results,null,2));
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
