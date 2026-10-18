const {test}=require('node:test'),assert=require('node:assert/strict');
const {clipOptions}=require('./probe-cover-clip.cjs');
test('小数逻辑区域原样交给合成器且不额外缩放',()=>{
 const rect={x:180.5,y:82.25,width:1040,height:766.25};
 assert.deepEqual(clipOptions(rect,{width:1400,height:930}),{format:'png',fromSurface:true,captureBeyondViewport:false,optimizeForSpeed:true,clip:{...rect,scale:1}});
});
test('拒绝越界或非有限区域，防止截图静默补边',()=>{
 for(const rect of [{x:-1,y:0,width:1,height:1},{x:0,y:0,width:0,height:1},{x:0,y:0,width:NaN,height:1},{x:99,y:0,width:2,height:1},{x:0,y:99,width:1,height:2}])assert.throws(()=>clipOptions(rect,{width:100,height:100}));
});
