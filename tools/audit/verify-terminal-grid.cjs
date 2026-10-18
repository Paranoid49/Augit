// 无服务器的真实浏览器矩阵；覆盖DOM字距、光标、选区与恢复上游网格。
const fs = require('node:fs/promises'), path = require('node:path');
const assert = require('node:assert/strict');
(async () => {
  const { chromium } = require(process.argv[2]);
  const browser = await chromium.launch({ executablePath: process.argv[3], headless: true });
  const records = [], longLines = [];
  try {
    const font = await fs.readFile('web/vendor/jetbrains-mono/JetBrainsMono-Regular.woff2');
    const geometry = await fs.readFile('web/src/terminal-geometry.js', 'utf8');
    for (const scale of [1, 1.25, 1.5, 1.75, 2, 2.5]) {
      const context = await browser.newContext({ deviceScaleFactor: scale, viewport: { width: 1400, height: 900 } });
      try {
        const page = await context.newPage();
        await page.route('https://augit.test/**', async route => {
          const name=path.basename(new URL(route.request().url()).pathname);
          if (!/^JetBrainsMono-(Regular|Bold|Italic|BoldItalic)[.]woff2$/.test(name)) return route.abort();
          await route.fulfill({contentType:'font/woff2',body:await fs.readFile(path.join('web/vendor/jetbrains-mono',name))});
        });
        await page.setContent('<base href="https://augit.test/"><div class="terminal-view" style="width:1200px;height:650px"></div>');
        await page.addStyleTag({ path: 'web/src/mockup.css' });
        await page.addStyleTag({ path: 'web/vendor/xterm/xterm.css' });
        await page.addStyleTag({ content: '@font-face{font-family:"JetBrains Mono";src:url(data:font/woff2;base64,' + font.toString('base64') + ')}' });
        await page.addScriptTag({ path: 'tools/xterm/xterm-6.0.0/lib/xterm.js' });
        await page.evaluate(()=>{window.UpstreamTerminal=window.Terminal;});
        await page.addScriptTag({ path: 'web/vendor/xterm/xterm.js' });
        await page.evaluate(async source => {
          window.geometry = await import('data:text/javascript;base64,' + btoa(unescape(encodeURIComponent(source))));
          await document.fonts.load('13px "JetBrains Mono"');
          window.term = new Terminal({cols:50,rows:5,fontFamily:'JetBrains Mono',fontSize:13,cursorBlink:false});
          term.open(document.querySelector('.terminal-view'));
          const newline=String.fromCharCode(13,10), escape=String.fromCharCode(27);
          await new Promise(resolve => term.write(Array(3).fill('WWWWWWWWWWWWWWWWWWWW').join(newline)+escape+'[1;21H', resolve));
          term.focus();
        }, geometry);
        for (const size of [9,13,17,40]) for (const theme of ['light','dark']) {
          await page.evaluate(({size,theme}) => {
            document.body.dataset.theme=theme;
            term.options.fontSize=size;
            term.options.augitCellWidth=geometry.terminalCellWidth('JetBrains Mono',size,devicePixelRatio,true);
            term.options.augitCellHeight=geometry.terminalCellHeight('JetBrains Mono',size,devicePixelRatio,true);
            term.options.theme={background:theme==='dark'?'#1e1f22':'#ffffff',foreground:theme==='dark'?'#ffffff':'#000000'};
            term.select(2,0,10);
            term.refresh(0,4);
          }, {size,theme});
          await page.evaluate(() => new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
          const result=await page.evaluate(() => {
            const screen=term.element.querySelector('.xterm-screen').getBoundingClientRect();
            const row=term.element.querySelector('.xterm-rows > div');
            const text=[...row.querySelectorAll('span')].find(s=>s.textContent.includes('WW'));
            const selection=term.element.querySelector('.xterm-selection div').getBoundingClientRect();
            const cursor=term.element.querySelector('.xterm-cursor').getBoundingClientRect();
            const range=document.createRange();range.selectNodeContents(text);
            const glyphWidth=range.getBoundingClientRect().width;
            return {scale:devicePixelRatio,size:term.options.fontSize,theme:document.body.dataset.theme,cell:term.options.augitCellWidth,cellHeight:term.options.augitCellHeight,screenHeight:screen.height,rowHeight:row.getBoundingClientRect().height,selectionHeight:selection.height,screen:screen.width,selection:selection.width,selectionX:selection.x-screen.x,cursorX:cursor.x-screen.x,textLength:text.textContent.length,glyphWidth,selected:term.getSelection()};
          });
          // DOM每段有1/64px布局取整；累计偏移必须小于1物理像素，仍不宣称逐像素一致。
          const close=(actual,expected,label)=>assert.ok(Math.abs(actual-expected)*scale<1,JSON.stringify({label,actual,expected,result}));
          close(result.screen, Math.round(result.cell*50),'网格宽');
          close(result.screenHeight, result.cellHeight*5,'网格高');
          close(result.rowHeight, result.cellHeight,'单行高度');
          close(result.selectionHeight,result.cellHeight,'选区高度');
          close(result.selection, result.screen/50*10,'选区宽');
          close(result.selectionX,result.screen/50*2,'选区位置');
          close(result.cursorX,result.screen/50*20,'光标位置');
          close(result.glyphWidth,result.screen/50*result.textLength,'文字步长');
          assert.equal(result.selected,'WWWWWWWWWW');
          const hit=await page.evaluate(()=>{const r=term.element.querySelector('.xterm-screen').getBoundingClientRect();return {x:r.x,y:r.y,cell:r.width/term.cols,height:r.height/term.rows};});
          await page.mouse.move(hit.x+hit.cell*2.1,hit.y+hit.height*.5);
          await page.mouse.down();
          await page.mouse.move(hit.x+hit.cell*12.1,hit.y+hit.height*.5);
          await page.mouse.up();
          assert.equal(await page.evaluate(()=>term.getSelection()),'WWWWWWWWWW','鼠标命中与文字网格一致');
          await page.mouse.move(hit.x+hit.cell*2.1,hit.y+hit.height*2.5);
          await page.mouse.down();
          await page.mouse.move(hit.x+hit.cell*12.1,hit.y+hit.height*2.5);
          await page.mouse.up();
          const vertical=await page.evaluate(()=>{const r=term.element.querySelector('.xterm-screen').getBoundingClientRect(),s=term.element.querySelector('.xterm-selection div').getBoundingClientRect();return {text:term.getSelection(),top:s.y-r.y};});
          assert.equal(vertical.text,'WWWWWWWWWW','第三行鼠标命中正确');
          close(vertical.top,result.cellHeight*2,'第三行选区顶部');
          records.push(result);
        }
        const longLine=await page.evaluate(async () => {
          term.clearSelection();term.resize(120,5);term.options.fontSize=13;
          term.options.augitCellWidth=geometry.terminalCellWidth('JetBrains Mono',13,devicePixelRatio,true);
          term.options.augitCellHeight=geometry.terminalCellHeight('JetBrains Mono',13,devicePixelRatio,true);
          term.reset();await new Promise(resolve=>term.write('W'.repeat(100),resolve));
          await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));
          const r=term.element.querySelector('.xterm-screen').getBoundingClientRect();
          const c=term.element.querySelector('.xterm-cursor').getBoundingClientRect();
          return {scale:devicePixelRatio,columns:100,cursorErrorPhysical:(c.x-r.x-r.width/term.cols*100)*devicePixelRatio};
        });
        longLines.push(longLine);
        assert.ok(Math.abs(longLine.cursorErrorPhysical)<1,'100列累计光标偏移必须小于1物理px');
        await page.evaluate(()=>term.resize(50,5));
        const fallback=await page.evaluate(() => {
          for(const option of ['augitCellWidth','augitCellHeight']) for(const invalid of [-1,NaN,Infinity,'8']) {
            let rejected=false;try {term.options[option]=invalid;} catch {rejected=true;}
            if(!rejected)throw Error('无效格宽未拒绝');
          }
          term.options.fontFamily='Consolas';term.options.fontSize=13;term.options.augitCellWidth=0;
          term.options.augitCellHeight=0;
          const other=new UpstreamTerminal({cols:50,rows:5,fontFamily:'Consolas',fontSize:13});
          const host=document.createElement('div');document.body.append(host);other.open(host);
          const width=t=>{const r=t.element.querySelector('.xterm-screen').getBoundingClientRect();return {width:r.width,height:r.height};};
          const result={actual:width(term),default:width(other)};other.dispose();host.remove();return result;
        });
        assert.deepEqual(fallback.actual,fallback.default);
      } finally { await context.close(); }
    }
    await fs.mkdir(path.dirname(process.argv[4]),{recursive:true});
    await fs.writeFile(process.argv[4],JSON.stringify({count:records.length,records,longLines},null,2)+String.fromCharCode(10));
    console.log(JSON.stringify({passed:records.length,longLines}));
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
