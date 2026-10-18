// 录屏证据必须区分首次、预热复用以及无效基线。
const {test}=require('node:test');
const assert=require('node:assert/strict');
const {coverIdentity}=require('./capture-refresh-screen.cjs');
const fresh={captureCoverBytes:0,captureCoverWindow:0,captureCoverReady:false,captureCoverWindowVisible:false};
test('仅明确零资源状态记为首次',()=>{assert.deepEqual(coverIdentity(fresh),{bytes:0,window:0,visible:false,reused:false});});
test('隐藏复用与部分准备不记为首次',()=>{
 assert.equal(coverIdentity({...fresh,captureCoverBytes:100,captureCoverWindow:42}).reused,true);
 assert.equal(coverIdentity({...fresh,captureCoverWindow:42}).reused,true);
});
test('拒绝缺失或非法资源字段',()=>{
 for(const patch of [{captureCoverBytes:undefined},{captureCoverBytes:-1},{captureCoverWindow:NaN},{captureCoverWindowVisible:undefined}])assert.throws(()=>coverIdentity({...fresh,...patch}));
});
test('拒绝可见遮盖作为基线',()=>{
 assert.throws(()=>coverIdentity({...fresh,captureCoverReady:true}));
 assert.throws(()=>coverIdentity({...fresh,captureCoverWindowVisible:true}));
});
