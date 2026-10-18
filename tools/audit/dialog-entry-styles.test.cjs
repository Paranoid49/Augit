// 同一对话框不能因启动场景名不同而丢失状态样式；仅使用产品原始样式表。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require(process.argv[2]);
(async () => {
  const browser = await chromium.launch({headless:true,executablePath:process.argv[3]||process.env.AUGIT_BROWSER_EXECUTABLE});
  let checked = 0;
  try {
    const css = await fs.readFile(path.resolve('web/src/mockup.css'), 'utf8');
    for (const theme of ['light','dark']) for (const kind of ['reset','rollback']) {
      const page = await browser.newPage();
      try {
        await page.setContent(`<body data-theme="${theme}"><section class="dialog ${kind}-dialog"><div class="dialog-header"><button class="icon-button">X</button></div><button class="primary-button">OK</button><button class="secondary-button">Cancel</button><button class="danger-button">Apply</button></section></body>`);
        await page.addStyleTag({content:css});
        for (const selector of ['.primary-button','.secondary-button','.danger-button','.icon-button']) {
          const button = page.locator(selector);
          for (const state of ['normal','hover','focus','active','disabled']) {
            await page.mouse.up(); await page.mouse.move(0,0);
            await button.evaluate(node=>{node.blur();node.removeAttribute('aria-disabled');});
            if(state==='hover'||state==='active')await button.hover();
            if(state==='focus')await button.focus();
            if(state==='active')await page.mouse.down();
            if(state==='disabled')await button.evaluate(node=>node.setAttribute('aria-disabled','true'));
            const inspect=async scene=>{
              await page.evaluate(value=>document.body.dataset.scene=value,scene);
              // 让产品声明的颜色过渡完成，不改动画或样式。
              await page.waitForTimeout(250);
              return button.evaluate(node=>{const style=getComputedStyle(node);return Object.fromEntries(['backgroundColor','color','outline','outlineOffset','boxShadow','border','width','height'].map(key=>[key,style[key]]));});
            };
            const expected=await inspect(kind),actual=await inspect('main-project');
            assert.deepEqual(actual,expected,`${theme}/${kind}/${selector}/${state} 在正常入口和测试场景不同`);
            checked++;
          }
        }
      } finally {await page.close();}
    }
    console.log(JSON.stringify({checked,passed:true}));
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
