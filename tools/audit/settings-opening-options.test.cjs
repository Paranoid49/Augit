const {test}=require('node:test'),assert=require('node:assert/strict');
const {settingsOpeningOptions}=require('./settings-opening-options.cjs');
test('保留原压力流程和三次默认专项',()=>{assert.deepEqual(settingsOpeningOptions([]),{singleOpen:false,startupCount:3});});
test('普通单击专项显式标记十次',()=>{assert.deepEqual(settingsOpeningOptions(['--single-open','--startup-only','--startup-count=10']),{singleOpen:true,startupCount:10});});
test('无效次数不能默默放大测试或误标证据',()=>{
 for(const args of [['--startup-count=10'],['--startup-only','--startup-count=0'],['--startup-only','--startup-count=31'],['--startup-only','--startup-count=NaN'],['--startup-only','--startup-count=1.5'],['--startup-only','--startup-count=2','--startup-count=3']])assert.throws(()=>settingsOpeningOptions(args));
});
