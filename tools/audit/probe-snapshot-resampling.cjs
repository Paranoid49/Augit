// 使用既有源PNG隔离浏览器缩放；输出只作采样诊断，不充当系统显示证据。
const assert=require('node:assert/strict');
const fs=require('node:fs/promises');
const path=require('node:path');
const {spawnSync}=require('node:child_process');
const {chromium}=require(process.argv[2]);
(async()=>{
 const output=path.resolve(process.argv[4]);await fs.mkdir(output,{recursive:true});
 const source=path.resolve('artifacts/pixel-followup-2026-10-15/refresh-cover-source-dark-settled/cover-source.png');
 const data=await fs.readFile(source);
 const browser=await chromium.launch({executablePath:process.argv[3],headless:true});
 const results=[];
 try {
  for(const scale of [1,1.25,1.5,1.75,2]) for(const mode of ['auto','pixelated','transform']) {
   const context=await browser.newContext({viewport:{width:1040,height:700},deviceScaleFactor:scale});
   try {
    const page=await context.newPage();
    await page.setContent('<html><body><img></body></html>');
    await page.evaluate(async args=>{
     document.body.style.cssText='margin:0;overflow:hidden';
     const image=document.querySelector('img');
     image.style.cssText='position:absolute;left:'+(-316/args.scale)+'px;top:'+(-145/args.scale)+'px;width:'+(2452/args.scale)+'px;height:'+(1630/args.scale)+'px';
     if(args.mode==='pixelated')image.style.imageRendering='pixelated';
     if(args.mode==='transform'){image.style.width='2452px';image.style.height='1630px';image.style.transformOrigin='0 0';image.style.transform='scale('+(1/args.scale)+')';}
     image.src='data:image/png;base64,'+args.data;await image.decode();
    },{scale,mode,data:data.toString('base64')});
    const geometry=await page.locator('img').evaluate(image=>{const r=image.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,dpr:devicePixelRatio};});
    const target=path.join(output,'scale-'+scale+'-'+mode+'.png');await page.screenshot({path:target});
    const checked=spawnSync('python',['-B','tools/audit/compare-snapshot-resampling.py',source,target,'316','145'],{encoding:'utf8'});
    assert.equal(checked.status,0,checked.stderr);
    results.push({scale,mode,geometry,...JSON.parse(checked.stdout)});
   } finally {await context.close();}
  }
 } finally {await browser.close();}
 await fs.writeFile(path.join(output,'analysis.json'),JSON.stringify({source,method:'isolated-browser-source-crop-comparison',systemPresentationVerified:false,results},null,2)+String.fromCharCode(10));
 console.log(JSON.stringify(results));
})().catch(error=>{console.error(error);process.exitCode=1;});
