const {test}=require('node:test');
const assert=require('node:assert/strict');
const {placement}=require('./shadow-capture-placement.cjs');
const reference={scale:1.75,frameRectPhysical:[663,315,2218,1530]};
test('分数DPI位置保持整物理像素及完整四侧空间',()=>{
  const body={x:50,y:165,width:1540,height:800};
  const p=placement(reference,body,64);
  assert.equal(p.width*1.75,1555);assert.equal(p.height*1.75,1215);
  assert.equal(p.x*1.75,Math.floor(p.x*1.75));assert.equal(p.y*1.75,Math.floor(p.y*1.75));
  assert.ok((p.x-body.x)*1.75>=64);assert.ok((p.y-body.y)*1.75>=64);
  assert.ok((body.x+body.width-p.x-p.width)*1.75>=64);
  assert.ok((body.y+body.height-p.y-p.height)*1.75>=64);
});
test('完整外沿放不下时拒绝生成位置',()=>{
  assert.throws(()=>placement(reference,{x:50,y:165,width:1540,height:800},128),/拒绝裁切/);
});
test('紧凑窗口支持192像素完整外沿',()=>{
  const p=placement({scale:1.75,frameRectPhysical:[1147,726,1731,978]},
    {x:41,y:120,width:1593,height:823},192);
  assert.equal(p.width*1.75,584);assert.equal(p.height*1.75,252);
  assert.ok((p.y-120)*1.75>=192);
  assert.ok((943-p.y-p.height)*1.75>=192);
});
test('无效DPI半径尺寸不能产生取证布局',()=>{
  const body={x:0,y:0,width:2000,height:2000};
  for(const scale of [0,NaN,Infinity])assert.throws(()=>placement({...reference,scale},body,64));
  for(const radius of [0,-1,1.5])assert.throws(()=>placement(reference,body,radius));
  assert.throws(()=>placement({...reference,frameRectPhysical:[1,1,0,0]},body,64));
});
