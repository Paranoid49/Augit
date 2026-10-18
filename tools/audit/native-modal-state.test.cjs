// 文本节点、父表单和顶层草稿须归属到正确快照；不能只测试元素节点。
const assert = require('node:assert/strict');
const {test} = require('node:test');
const fs = require('node:fs');
const source = fs.readFileSync(require('node:path').join(__dirname,'../../web/src/native-modal-state.js'),'utf8');
const modulePromise = import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
const parent = {}, top = {};
const element = dialog => ({nodeType:1,closest:()=>dialog});
const mutation = target => ({type:'characterData',target});

test('已移除表单子节点的延迟通知不刷新背景，连接中的删除通知仍刷新', async()=>{
  const {snapshotDepthForMutation:depth}=await modulePromise;
  const detached={...element(null),isConnected:false,getAttribute:()=> 'changed'};
  assert.equal(depth({type:'attributes',target:detached,attributeName:'class',oldValue:'old'},[parent,top]),null);
  assert.equal(depth(mutation({nodeType:3,isConnected:false,parentElement:null}),[parent,top]),null);
  assert.equal(depth({type:'childList',target:{...element(null),isConnected:true},addedNodes:[],removedNodes:[detached]},[parent,top]),0);
});

test('属性重复写入相同值不刷新背景，真实变化仍按归属刷新', async()=>{
  const {snapshotDepthForMutation:depth}=await modulePromise;
  const target={...element(null),getAttribute:()=> 'same'};
  assert.equal(depth({type:'attributes',target,attributeName:'style',oldValue:'same'},[parent,top]),null);
  assert.equal(depth({type:'attributes',target,attributeName:'style',oldValue:'before'},[parent,top]),0);
});
test('顶层元素和文本草稿不产生背景捕获，父表单只更新自身快照', async()=>{
  const {snapshotDepthForMutation:depth}=await modulePromise;
  assert.equal(depth(mutation(element(top)),[parent,top]),null);
  assert.equal(depth(mutation({nodeType:3,parentElement:element(top)}),[parent,top]),null);
  assert.equal(depth(mutation({nodeType:3,parentElement:element(parent)}),[parent,top]),1);
  assert.equal(depth(mutation(element(null)),[parent,top]),0);
});
test('模态拓扑由呈现协议处理，普通背景节点的混合改动仍捕获', async()=>{
  const {snapshotDepthForMutation:depth}=await modulePromise;
  const overlay={nodeType:1,matches:()=>true}, ordinary={nodeType:1,matches:()=>false};
  const change={type:'childList',target:element(null),addedNodes:[overlay],removedNodes:[]};
  assert.equal(depth(change,[parent,top]),null);
  assert.equal(depth({...change,addedNodes:[overlay,ordinary]},[parent,top]),0);
  assert.equal(depth({type:'attributes',target:element(parent)},[parent,top]),1);
});
