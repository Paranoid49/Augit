// 仅在隔离审计实例追踪合成器，恢复焦点并关闭追踪流；不修改产品绘制设置。
const fs=require('node:fs/promises'),path=require('node:path');
async function traceLcdText(page,browser,output) {
 const client=await browser.newBrowserCDPSession();
 let started=false,stream;
 try {
  const categories=await client.send('Tracing.getCategories');
  // getCategories可为空；按官方CategoryLayerTree请求，实际事件决定是否取得证据。
  const selected=['cc','disabled-by-default-cc.debug','disabled-by-default-devtools.timeline.layers'];
  const complete=new Promise(resolve=>client.once('Tracing.tracingComplete',resolve));
  await client.send('Tracing.start',{categories:selected.join(','),transferMode:'ReturnAsStream'});started=true;
  // 光标焦点切换产生实际提交，不插入文字或更改任何CSS。
  await page.locator('.conflict-column.result .conflict-block').focus();
  await page.waitForTimeout(350);
  await page.evaluate(()=>document.activeElement?.blur());
  await page.waitForTimeout(350);
  await page.locator('.conflict-column.result .conflict-block').focus();
  await page.waitForTimeout(700);
  await client.send('Tracing.end');started=false;
  stream=(await complete).stream;
  if(!stream)throw Error('追踪未返回可读取流');
  const chunks=[];
  for(;;){const chunk=await client.send('IO.read',{handle:stream});chunks.push(chunk.base64Encoded?Buffer.from(chunk.data,'base64'):Buffer.from(chunk.data));if(chunk.eof)break;}
  await fs.writeFile(path.join(output,'lcd-trace.json'),Buffer.concat(chunks));
  await fs.writeFile(path.join(output,'lcd-trace-categories.json'),JSON.stringify({requested:selected,discovered:categories.categories},null,2)+String.fromCharCode(10));
 } finally {
  try {if(started)await client.send('Tracing.end');if(stream)await client.send('IO.close',{handle:stream});}
  finally {await client.detach();}
 }
}
module.exports={traceLcdText};
