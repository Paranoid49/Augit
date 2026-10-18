// 只读可见buffer和整行DOM边界，包含空白行；不能用历史buffer行号代替屏幕行号。
function collectTerminalViewport(term=window.__auditTerminal){
 const buffer=term.buffer.active;
 const elements=[...term.element.querySelectorAll('.xterm-rows > div')];
 if(elements.length!==term.rows)throw Error('可见DOM行数与终端行数不同');
 const bounds=element=>{const r=element.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height};};
 const rows=elements.map((element,index)=>{
  const bufferIndex=buffer.viewportY+index,line=buffer.getLine(bufferIndex);
  if(!line)throw Error('可见buffer行缺失');
  return {screenRow:index+1,bufferIndex,text:line.translateToString(true),wrapped:line.isWrapped===true,bounds:bounds(element)};
 });
 return {rows,columns:term.cols,rowCount:term.rows,viewportY:buffer.viewportY,baseY:buffer.baseY,
  cursor:{x:buffer.cursorX,y:buffer.cursorY,bufferIndex:buffer.baseY+buffer.cursorY},
  screen:bounds(term.element.querySelector('.xterm-screen')),
  limitation:'边界为CSS像素；整行包含空白和行尾。参考端需独立取得相同可见内容及几何，不能以该快照证明双端配对。'};
}
module.exports={collectTerminalViewport};
