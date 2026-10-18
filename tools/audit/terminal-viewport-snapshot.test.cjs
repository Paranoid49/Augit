const test=require('node:test'),assert=require('node:assert/strict');
const {collectTerminalViewport}=require('./terminal-viewport-snapshot.cjs');
function fixture(){
 const lines=['历史行','第一可见行','','提示符'];
 const row=i=>({getBoundingClientRect:()=>({x:10,y:20+i*18,width:400,height:18})});
 const elements=[row(0),row(1),row(2)];
 return {rows:3,cols:50,buffer:{active:{viewportY:1,baseY:1,cursorX:3,cursorY:2,getLine:i=>i<lines.length?{translateToString:()=>lines[i],isWrapped:i===2}:undefined}},
  element:{querySelectorAll:()=>elements,querySelector:()=>({getBoundingClientRect:()=>({x:10,y:20,width:400,height:54})})}};
}
test('按viewportY对应屏幕行，保留空白与最后提示符及整行边界',()=>{
 const result=collectTerminalViewport(fixture());
 assert.deepEqual(result.rows.map(r=>r.text),['第一可见行','','提示符']);
 assert.deepEqual(result.rows.map(r=>r.bufferIndex),[1,2,3]);
 assert.equal(result.rows[1].wrapped,true);assert.equal(result.rows[2].bounds.width,400);
 assert.equal(result.rows[2].bounds.y+result.rows[2].bounds.height,74);
 assert.deepEqual(result.cursor,{x:3,y:2,bufferIndex:3});
});
test('滚动查看历史时光标仍按baseY计算，不能混用viewportY',()=>{
 const term=fixture();term.buffer.active.viewportY=0;
 const result=collectTerminalViewport(term);assert.equal(result.rows[0].text,'历史行');assert.equal(result.cursor.bufferIndex,3);
});
test('DOM行数不足拒绝不完整证据',()=>{
 const term=fixture();term.rows=4;assert.throws(()=>collectTerminalViewport(term),/DOM行数/);
});
test('buffer缺少最后一行必须失败而不是补空白',()=>{
 const term=fixture();term.buffer.active.viewportY=2;assert.throws(()=>collectTerminalViewport(term),/buffer行缺失/);
});
