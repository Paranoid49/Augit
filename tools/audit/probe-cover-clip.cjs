// 隔离诊断：捕获当前表单原始PNG，与整页PNG裁取逐像素核对后才考虑产品优化。
const assert=require('node:assert/strict');
const fs=require('node:fs/promises');
const path=require('node:path');

function clipOptions(rect,viewport) {
 for(const key of ['x','y','width','height'])assert.ok(Number.isFinite(rect[key]),'裁切坐标必须有限');
 assert.ok(rect.x>=0&&rect.y>=0&&rect.width>0&&rect.height>0,'裁切必须有正尺寸');
 assert.ok(rect.x+rect.width<=viewport.width&&rect.y+rect.height<=viewport.height,'裁切不得超出当前视口');
 return {format:'png',fromSurface:true,captureBeyondViewport:false,optimizeForSpeed:true,clip:{x:rect.x,y:rect.y,width:rect.width,height:rect.height,scale:1}};
}

async function probeCoverClip(page,selector,output) {
 const geometry=await page.locator(selector).evaluate(node=>{const r=node.getBoundingClientRect();return{rect:{x:r.x,y:r.y,width:r.width,height:r.height},viewport:{width:innerWidth,height:innerHeight},dpr:devicePixelRatio};});
 const clipped=clipOptions(geometry.rect,geometry.viewport);
 const r=geometry.rect,s=geometry.dpr;
 const aligned=clipOptions({x:Math.round(r.x*s)/s,y:Math.round(r.y*s)/s,width:Math.round(r.width*s)/s,height:Math.round(r.height*s)/s},geometry.viewport);
 const full={...clipped};delete full.clip;
 const session=await page.context().newCDPSession(page);
 const captures=[];
 try {
  for(let pair=0;pair<4;pair++){
   for(const kind of pair%2?['aligned','clip','full']:['full','clip','aligned']){
    const started=performance.now();
    const result=await session.send('Page.captureScreenshot',kind==='aligned'?aligned:kind==='clip'?clipped:full);
    const elapsedMilliseconds=performance.now()-started;
    assert.equal(typeof result.data,'string');
    const png=Buffer.from(result.data,'base64'),file=`cover-probe-${pair}-${kind}.png`;
    await fs.writeFile(path.join(output,file),png);
    captures.push({pair,kind,file,bytes:png.length,elapsedMilliseconds});
   }
  }
 } finally {await session.detach();}
 const report={geometry,captures,method:'cdp-raw-png',systemScreenshot:false,productChanged:false};
 await fs.writeFile(path.join(output,'cover-clip-probe.json'),JSON.stringify(report,null,2)+'\n');
 return report;
}
module.exports={clipOptions,probeCoverClip};
