// 仅在隔离审计实例改变字形字号；固定网格，逐次截图后恢复，不写产品配置。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
async function probeTerminalRasterSize(page, output, capture) {
  const original = await page.evaluate(() => {
    const t = window.__auditTerminal;
    return {size:t.options.fontSize,width:t.options.augitCellWidth,height:t.options.augitCellHeight,
      scale:devicePixelRatio,lines:Array.from({length:t.buffer.active.length},(_,i)=>t.buffer.active.getLine(i)?.translateToString(true))};
  });
  const reports = [];
  const sizes = [['baseline',original.size],['device-floor',Math.floor(original.size*original.scale)/original.scale],
    ['device-round',Math.round(original.size*original.scale)/original.scale],['restored',original.size]];
  try {
    for (const [mode,size] of sizes) {
      await page.evaluate(size => {const t=window.__auditTerminal;t.options.fontSize=size;t.refresh(0,t.rows-1);},size);
      await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
      const state=await page.evaluate(()=>{
        const t=window.__auditTerminal,r=t.element.querySelector('.xterm-rows').getBoundingClientRect();
        const row=t.element.querySelector('.xterm-rows>div'),style=getComputedStyle(row);
        return {size:t.options.fontSize,width:t.options.augitCellWidth,height:t.options.augitCellHeight,
          scale:devicePixelRatio,rect:r.toJSON(),fontSize:style.fontSize,
          lines:Array.from({length:t.buffer.active.length},(_,i)=>t.buffer.active.getLine(i)?.translateToString(true))};
      });
      assert.equal(state.width,original.width);assert.equal(state.height,original.height);
      assert.equal(state.scale,original.scale);assert.deepEqual(state.lines,original.lines);
      await capture('raster-'+mode+'.png');reports.push({mode,...state});
    }
    await fs.writeFile(path.join(output,'raster-size-probe.json'),JSON.stringify({diagnosticOnly:true,productChanged:false,original,reports},null,2)+'\n');
  } finally {
    await page.evaluate(size=>{const t=window.__auditTerminal;t.options.fontSize=size;t.refresh(0,t.rows-1);},original.size);
  }
}
module.exports={probeTerminalRasterSize};
