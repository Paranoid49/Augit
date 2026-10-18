const {test}=require('node:test'),assert=require('node:assert/strict');
const {qualifies}=require('./refresh-switch-during-status.cjs');
test('实机竞态证据必须同时覆盖点击时序、前台和后台内容',()=>{
 const good={clickedDuringStatus:true,selectedPath:'other.txt',backgroundTextUpdated:true,pendingDocument:null};
 assert.equal(qualifies(good),true);
 for(const patch of [{clickedDuringStatus:false},{selectedPath:'refresh.txt'},{backgroundTextUpdated:false},{pendingDocument:'old.txt'}])assert.equal(qualifies({...good,...patch}),false);
});
