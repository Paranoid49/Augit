const assert=require('node:assert/strict'),{test}=require('node:test');
const {ownedTree}=require('./modal-process-memory.cjs');
test('只统计父进程归属，排除其他应用的同名WebView2',()=>{
 const rows=[{pid:30,parent:20,workingSet:3,privateBytes:4},{pid:10,parent:1,workingSet:5,privateBytes:6},{pid:40,parent:2,workingSet:99,privateBytes:99},{pid:20,parent:10,workingSet:7,privateBytes:8}];
 assert.deepEqual(ownedTree(rows,10),{root:10,pids:[30,10,20],processCount:3,workingSetBytes:15,privateBytes:18});
});
test('已退出根进程不把其他树纳入结果',()=>{
 assert.equal(ownedTree([{pid:40,parent:2,workingSet:99,privateBytes:99}],10).processCount,0);
});
