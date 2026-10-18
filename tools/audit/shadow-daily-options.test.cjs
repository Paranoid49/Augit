const {test}=require('node:test'),assert=require('node:assert/strict');
const {validateDailyShadowOptions}=require('./shadow-daily-options.cjs');
test('日常阴影采集拒绝特殊渲染和几何覆盖',()=>{
 for(const flag of ['--software-rendering','--disable-direct-composition','--disable-native-occlusion','--uniform-background','--goto-line','--reference-frame','--position-window','--bundled-web'])
  assert.throws(()=>validateDailyShadowOptions(['--daily-launcher',flag]),/不接受覆盖/);
 assert.equal(validateDailyShadowOptions(['--daily-launcher']),true);
 assert.equal(validateDailyShadowOptions(['--software-rendering']),false);
});
