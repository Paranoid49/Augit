// 真实WebView2设置与日历取证；不保存设置或执行Git写操作，所有浏览器连接在finally释放。
const assert=require('node:assert/strict');
const fs=require('node:fs/promises');
const path=require('node:path');
(async()=>{
  const [modulePath,endpoint,output]=process.argv.slice(2);
  const {chromium}=require(path.resolve(modulePath));
  const browser=await chromium.connectOverCDP(endpoint);
  try{
    const page=browser.contexts()[0].pages().find(page=>/augit/i.test(page.url()));
    assert.ok(page,'只能连接本轮外壳实例');
    const errors=[];page.on('pageerror',error=>errors.push(error.message));
    await page.reload();await page.waitForFunction(()=>window.__augitReady&&window.__augitSettingsReady&&window.__augitHistoryReady);
    await fs.mkdir(output,{recursive:true});
    const result=await page.evaluate(()=>({font:getComputedStyle(document.body).fontFamily,size:getComputedStyle(document.body).fontSize,dpr:devicePixelRatio,width:innerWidth,height:innerHeight}));
    await page.locator('.titlebar [aria-label="Settings"]').click();await page.waitForFunction(()=>window.__augitLive.fontFamilies?.length>0);
    result.settings=await page.evaluate(()=>({
      breadcrumb:document.querySelector('.settings-breadcrumbs').textContent,
      fonts:window.__augitLive.fontFamilies.length,
      headingCount:document.querySelectorAll('.settings-page h2').length,
      fontTag:document.querySelector('[data-setting="textFontFamily"]').tagName,
      selected:document.querySelector('[data-setting="textFontFamily"]').value,
      band:(()=>{const node=document.querySelector('.settings-nav .selected'),rect=node.getBoundingClientRect(),parent=node.parentElement.getBoundingClientRect();return{width:rect.width,parent:parent.width,left:rect.left-parent.left,radius:getComputedStyle(node).borderRadius};})(),
    }));
    assert.equal(result.settings.fontTag,'SELECT');assert.equal(result.settings.headingCount,0);assert.equal(result.settings.band.left,0);
    await page.screenshot({path:path.join(output,'augit-settings.png')});
    await page.locator('[data-settings-page="file-view"]').click();await page.locator('[data-settings-nav="back"]').click();
    assert.equal(await page.locator('.settings-page').getAttribute('data-settings-page-body'),'appearance');
    await page.locator('[data-settings-action="cancel"]').click();
    await page.locator('.rail-button[data-tool="commit"]').click();
    const file=page.locator('.change-file-row[data-path="web/src/mockup.js"]');await file.dblclick();await page.waitForSelector('.editor-content .diff-toolbar [aria-label="Settings"]');
    await page.locator('.editor-content .diff-toolbar [aria-label="Settings"]').click();
    result.diff=await page.locator('.diff-settings-menu .menu-item').allTextContents();assert.deepEqual(result.diff,['Show Whitespaces','Show Line Numbers']);
    await page.screenshot({path:path.join(output,'augit-diff-settings.png')});await page.keyboard.press('Escape');
    await page.locator('.rail-button[data-tool="history"]').click();
    let date=page.locator('.history-filters > [data-filter-key="date"]');if(!await date.isVisible()){await page.locator('[aria-label="More History Filters"]').click();date=page.locator('.history-filter-menu [data-filter-key="date"]');}
    await date.focus();await page.keyboard.press('ArrowDown');await page.locator('[data-history-date="select"]').click();
    await page.locator('[data-history-date-enabled="since"]').check();await page.locator('[data-history-date-calendar="since"]').click();
    result.calendar=await page.locator('.history-calendar').evaluate(node=>({width:node.getBoundingClientRect().width,height:node.getBoundingClientRect().height,grid:node.querySelectorAll('[data-calendar-date]').length,days:[...node.querySelectorAll('.calendar-weekdays span')].map(node=>node.textContent),
      leftDelta:node.getBoundingClientRect().left-document.querySelector('[data-history-date-field="since"]').getBoundingClientRect().left,
      topDelta:node.getBoundingClientRect().top-document.querySelector('[data-history-date-field="since"]').getBoundingClientRect().bottom}));
    assert.equal(result.calendar.grid,42);assert.equal(result.calendar.days[0],'Mon');
    assert.ok(Math.abs(result.calendar.leftDelta)<1&&Math.abs(result.calendar.topDelta-1)<1);
    await page.screenshot({path:path.join(output,'augit-calendar.png')});await page.keyboard.press('Escape');await page.keyboard.press('Escape');
    result.errors=errors;assert.deepEqual(errors,[]);
    await fs.writeFile(path.join(output,'augit-settings-scenes.json'),JSON.stringify(result,null,2)+'\n');console.log(JSON.stringify(result));
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
