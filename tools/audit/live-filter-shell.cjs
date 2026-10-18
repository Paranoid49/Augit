// 连接本轮隔离WebView2实例核对真实日期/路径弹层；只读工作区，不保存用户设置或执行Git写操作。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
(async () => {
  const [modulePath, endpoint, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.connectOverCDP(endpoint);
  try {
    const page = browser.contexts()[0].pages().find(page => /augit/i.test(page.url()));
    assert.ok(page, '只能连接本轮Augit页面');
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.reload();
    await page.waitForFunction(() => window.__augitReady && window.__augitSettingsReady && window.__augitHistoryReady);
    await fs.mkdir(output, { recursive: true });
    const result = await page.evaluate(() => ({ font:getComputedStyle(document.body).fontFamily,size:getComputedStyle(document.body).fontSize,dpr:devicePixelRatio,width:innerWidth,height:innerHeight }));
    const control = async key => {
      let button = page.locator(`.history-filters > [data-filter-key="${key}"]`);
      if (!await button.isVisible()) {
        await page.locator('[aria-label="More History Filters"]').click();
        button = page.locator(`.history-filter-menu [data-filter-key="${key}"]`);
      }
      await button.focus(); await page.keyboard.press('ArrowDown');
    };
    await control('date');
    result.menu = await page.locator('section.history-date-menu').evaluate(node => ({width:node.getBoundingClientRect().width,rows:[...node.querySelectorAll('.menu-item')].map(row=>({height:row.getBoundingClientRect().height,text:row.textContent}))}));
    await page.screenshot({path:path.join(output,'augit-date-menu.png')});
    await page.locator('[data-history-date="select"]').click();
    result.range = await page.locator('.history-date-range-dialog').evaluate(node => ({width:node.getBoundingClientRect().width,height:node.getBoundingClientRect().height,
      labels:[...node.querySelectorAll('.history-date-bound span')].map(label=>label.textContent),buttons:[...node.querySelectorAll('.dialog-footer button')].map(button=>button.textContent)}));
    assert.ok(Math.abs(result.range.width-504)<.001); assert.ok(Math.abs(result.range.height-132)<2);
    await page.screenshot({path:path.join(output,'augit-date-range.png')}); await page.keyboard.press('Escape');
    await control('path');await page.locator('[data-log-path-action="select"]').click();
    result.path = await page.locator('.history-path-text-popover').evaluate(node => ({width:node.getBoundingClientRect().width,height:node.getBoundingClientRect().height,title:!!node.querySelector('.dialog-header'),buttons:node.querySelectorAll('button').length,help:node.querySelector('.history-path-popup-help').textContent}));
    assert.equal(result.path.title,false);assert.equal(result.path.buttons,0);
    await page.screenshot({path:path.join(output,'augit-path-input.png')});await page.keyboard.press('Escape');
    await control('path');await page.locator('[data-log-path-action="tree"]').click();
    await page.waitForSelector('[data-log-path-row="docs"]');
    await page.locator('[data-log-path-row="docs"] [data-path-disclosure]').click();
    await page.waitForSelector('[data-log-path-row="docs/product-spec.md"]');
    result.tree = {loadedDeepPath:await page.locator('[data-log-path-row="docs/product-spec.md"]').count(),projectDeepPath:await page.locator('.side-content.tree [data-tree-path="docs/product-spec.md"]').count()};
    assert.equal(result.tree.loadedDeepPath,1);
    await page.screenshot({path:path.join(output,'augit-path-tree.png')});await page.keyboard.press('Escape');
    await page.locator('.rail-button[data-tool="search"]').hover();await page.waitForSelector('[role="tooltip"]');
    result.tooltip=await page.locator('[role="tooltip"]').innerText();await page.screenshot({path:path.join(output,'augit-rail-tooltip.png')});
    await page.mouse.move(700,100);result.errors=errors;assert.deepEqual(errors,[]);
    await fs.writeFile(path.join(output,'augit-filter-scenes.json'),JSON.stringify(result,null,2)+'\n');
    console.log(JSON.stringify(result));
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode=1; });
