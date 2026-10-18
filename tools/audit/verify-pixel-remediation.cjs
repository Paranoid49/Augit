// 报告中的上游常量与相对几何独立于当前实现；真实外壳另行采集，静态截图不能替代它。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { inspectScreenshotSource, captureVerifiedScreenshot } = require('./screenshot-provenance.cjs');
const modulePath = process.argv[2] || 'C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright';
const executable = process.argv[3] || 'C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe';
const output = path.resolve(process.argv[4] || 'artifacts/pixel-restoration-2026-10-13');
const { chromium } = require(modulePath);
const rect = node => { const r = node.getBoundingClientRect(); return { x: r.x, y: r.y, w: r.width, h: r.height }; };
const inside = (child, parent) => child.x >= parent.x - .6 && child.y >= parent.y - .6 && child.x + child.w <= parent.x + parent.w + .6 && child.y + child.h <= parent.y + parent.h + .6;
function requireInside(child, parent, label) { assert.ok(inside(child, parent), label + ': ' + JSON.stringify({ child, parent })); }
let passed = 0;
const records = [];
async function verify(page, scene, theme, size) {
  const scale = Math.max(.75, Math.round(size / 12 * 4) / 4);
  const record = { scene, theme, size, dpr: await page.evaluate(() => devicePixelRatio), evidence: '浏览器实际几何，非整页PyCharm配对' };
  if (await page.locator('.check-group-row').count()) {
    const geometry = await page.evaluate(() => {
      const box = node => { const r = node.getBoundingClientRect(); return { x:r.x,y:r.y,w:r.width,h:r.height }; };
      const group = document.querySelector('.check-group-row'), file = document.querySelector('.change-file-row');
      return { group:box(group.querySelector('.fake-check')), file:box(file.querySelector('.fake-check')), row:box(file), list:box(group.closest('.changes-list')), chevron:box(group.querySelector('.change-chevron')), arrow:box(group.querySelector('.change-chevron svg')) };
    });
    assert.equal(geometry.file.x - geometry.group.x, ({12:19,14:24,40:62}[size]), '子框向右缩进，ClassicPainter左缩进受图标半宽约束');
    assert.equal(geometry.row.h, Math.round(24 * scale), '字号档位决定行高，不把UI14压成UI12');
    assert.equal(geometry.arrow.w, Math.round(16 * scale), 'Changes展开画布保持权威16px基准');
    assert.equal(geometry.group.x - geometry.list.x, Math.round(12 * scale) + ({12:19,14:24,40:62}[size]), 'Tree.border与首级renderer偏移分别计算');
    requireInside(geometry.arrow, geometry.chevron, '展开图形必须位于自身槽内');
    record.changes = geometry;
  }
  const closes = page.locator('[data-workspace-diff-tab] .tab-close');
  if (await closes.count()) {
    record.close = { hit: await closes.first().evaluate(rect), svg: await closes.first().locator('svg').evaluate(rect) };
    assert.equal(record.close.svg.w, record.close.svg.h, 'Diff关闭图形不得拉伸');
    requireInside(record.close.svg, record.close.hit, 'Diff关闭图形在点击区内');
  }
  const history = page.locator('.history-search-trigger').first();
  if (await history.count()) {
    record.history = { hit: await history.evaluate(rect), svg: await history.locator('svg').evaluate(rect) };
    requireInside(record.history.svg, record.history.hit, '搜索历史图形在按钮内');
    const label = await history.evaluate(node => { const r=node.closest('.history-search').getBoundingClientRect(); return {x:r.x,y:r.y,w:r.width,h:r.height}; });
    const input = page.locator('.history-search-trigger').first().locator('xpath=..').locator('.search-field');
    const paddingLeft = parseFloat(await input.evaluate(n => getComputedStyle(n).paddingLeft));
    assert.ok(record.history.svg.x >= label.x && record.history.svg.x + record.history.svg.w <= label.x + label.w, '图形不越出搜索框: ' + JSON.stringify({label,history:record.history}));
    assert.ok(paddingLeft >= record.history.hit.w + 2, '搜索文字为历史图形保留独立内距');
  }
  if (scene.startsWith('settings')) {
    const input = page.locator('.settings-nav > .search-field');
    const metrics = await input.evaluate(n => ({ h:n.clientHeight, size:parseFloat(getComputedStyle(n).fontSize) }));
    assert.ok(metrics.h >= metrics.size + 6, '设置搜索框必须容纳字形与内距');
    record.settings = { search:metrics, dialog:await page.locator('.dialog').evaluate(rect) };
    const arrows = page.locator('.settings-breadcrumbs .breadcrumb-arrow svg');
    if (await arrows.count()) assert.equal((await arrows.first().evaluate(rect)).w, Math.round(13 * scale), '面包屑使用紧凑图形角色');
    const nav = await page.locator('.settings-nav').evaluate(rect);
    requireInside(await page.locator('.settings-page-header').evaluate(rect),await page.locator('.settings-page').evaluate(rect),'设置面包屑与导航不得撑宽正文列');
    for (const row of await page.locator('.settings-nav .tree-row').all()) {
      const box=await row.evaluate(rect);
      assert.ok(box.x>=nav.x-.6&&box.x+box.w<=nav.x+nav.w+.6,'设置导航文字不能横向越过列边界');
    }
    for (const label of await page.locator('.settings-page .form-grid > label').all()) {
      if (!await label.isVisible() || !await label.innerText()) continue;
      const text=await label.evaluate(node=>{const range=document.createRange();range.selectNodeContents(node);const r=range.getBoundingClientRect();return{x:r.x,y:r.y,w:r.width,h:r.height};});
      requireInside(text,await label.evaluate(rect),'设置字段标签字形不得侵入相邻输入');
    }
    if(size===40) {
      const field=page.locator('.settings-page .font-setting input').first();
      const number=await field.evaluate(node=>{const style=getComputedStyle(node),canvas=document.createElement('canvas').getContext('2d');canvas.font=style.font;return{width:node.getBoundingClientRect().width,required:canvas.measureText('40').width+parseFloat(style.paddingLeft)+parseFloat(style.paddingRight)+parseFloat(style.borderLeftWidth)+parseFloat(style.borderRightWidth)+16};});
      assert.ok(number.width>=number.required,'UI40两位字号、内距和原生步进槽：'+JSON.stringify(number));
      const columns=await page.locator('.settings-page .form-grid').first().evaluate(node=>getComputedStyle(node).gridTemplateColumns.split(' ').length);
      assert.equal(columns,1,'受限大字号正文标签与字段纵向排布');
      const before=await page.locator('.settings-layout').evaluate(node=>({nav:node.querySelector('.settings-nav-tree').scrollTop,body:node.closest('.dialog-body').scrollTop,header:node.querySelector('.settings-page-header').getBoundingClientRect().top}));
      await field.scrollIntoViewIfNeeded();
      const after=await page.locator('.settings-layout').evaluate(node=>({nav:node.querySelector('.settings-nav-tree').scrollTop,body:node.closest('.dialog-body').scrollTop,page:node.querySelector('.settings-content').scrollTop,header:node.querySelector('.settings-page-header').getBoundingClientRect().top}));
      assert.equal(after.body,0,'正文滚动不得推动整个设置主体');
      assert.equal(after.nav,before.nav,'字体字段滚动不得移动导航');
      assert.equal(after.header,before.header,'字体字段滚动不得移动面包屑');
      assert.equal(await page.locator('.settings-page').evaluate(node=>node.scrollLeft),0,'滚动字体行不能导致正文列横向偏移');
      assert.ok(after.page>0,'超出视口的字体字段须在右侧独立滚动');
      const searchTop=await input.evaluate(node=>node.getBoundingClientRect().top);
      await page.locator('.settings-nav .tree-row').last().scrollIntoViewIfNeeded();
      assert.equal(await input.evaluate(node=>node.getBoundingClientRect().top),searchTop,'分类滚动不得移动设置搜索框');
      assert.equal(await page.locator('.settings-content').evaluate(node=>node.scrollTop),after.page,'分类滚动不得移动右侧草稿');
      // 负向对照复现原隐式auto行高：左侧内容撑大整行，浏览器会改滚整个主体。
      const broken=await page.addStyleTag({content:'.settings-layout{grid-template-rows:auto!important}.settings-nav{display:block!important;min-height:auto!important;overflow:visible!important}.settings-nav-tree{overflow:visible!important}'});
      try {
        await page.locator('.settings-content').evaluate(node=>{node.scrollTop=0;});
        await field.scrollIntoViewIfNeeded();
        const overflow=await page.locator('.settings-layout').evaluate(node=>({visible:node.clientHeight,content:node.scrollHeight}));
        assert.ok(overflow.content>overflow.visible,'负向对照必须检出旧auto行高撑大主体：'+JSON.stringify(overflow));
      } finally {
        await broken.evaluate(node=>node.remove());
        await page.locator('.dialog-body').evaluate(node=>{node.scrollTop=0;});
      }
    }
  }
  if (['stash','reset'].includes(scene)) {
    const buttons = await page.locator('.dialog-footer > button').allTextContents();
    assert.equal(buttons.at(-1), 'Cancel', 'Windows默认确认在取消之前');
  }
  if (size === 12 && await page.locator('.commit-actions > .primary-button').count()) {
    assert.ok((await page.locator('.commit-actions > .primary-button').evaluate(rect)).w >= 78, 'Commit外宽包含72px绘制区与两侧3px边框');
  }
  if (size === 40 && !await page.locator('[data-augit-overlay]').count()) {
    record.editor = await page.locator('.editor-content').evaluate(rect);
    assert.ok(record.editor.h >= 23, '受限窗口保留至少一行编辑正文');
    if (await page.locator('.changes-layout').count()) {
      if (await page.locator('.changes-list').count()) {
        record.list = await page.locator('.changes-list').evaluate(rect);
        assert.ok(record.list.h >= 78, 'Changes自身至少有一行，且可在局部滚动区访问: ' + JSON.stringify({scene,list:record.list}));
        await page.locator('.change-file-row').first().scrollIntoViewIfNeeded();
      } else assert.ok((await page.locator('.empty-tool-state').evaluate(rect)).h >= 78,'空态仍保留同一行高的提交区骨架');
      for (const button of await page.locator('.commit-actions > :is(.primary-button,.secondary-button)').all()) {
        await button.scrollIntoViewIfNeeded();
        const r = await button.evaluate(rect);
        requireInside(r,await page.locator('.side-tool').evaluate(rect),'提交动作必须完整进入所属工具区可视范围');
        assert.equal(await button.evaluate(node=>{const r=node.getBoundingClientRect();return node.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));}),true,'提交动作的中心实际可命中');
      }
    }
    if (await page.locator('.editor-content > .diff-layout .diff-columns').count()) assert.ok((await page.locator('.editor-content > .diff-layout .diff-columns').evaluate(rect)).h >= 23,'Diff真实正文至少一行');
    if (await page.locator('.info-state').count()) {
      const host = await page.locator('.info-state').evaluate(rect), block = await page.locator('.info-state .info-block').evaluate(rect);
      assert.ok(block.x >= host.x && block.x + block.w <= host.x + host.w + .6, '信息块横向留在正文内');
      await page.locator('.info-state .button-row > *').first().scrollIntoViewIfNeeded();
      await page.locator('.info-state .button-row > *').first().click({ trial:true });
      record.info = { host, block };
    }
  }
  record.screenshot = await captureVerifiedScreenshot(page, scene, path.join(output, `${scene}-${theme}-${size}.png`));
  records.push(record); passed++;
}
async function checkboxMatrix(page, theme, size) {
  const scale = Math.max(.75,Math.round(size/12*4)/4);
  await page.evaluate(() => {
    const probe = document.createElement('div'); probe.id = 'pixel-check-probe';
    probe.style.cssText = 'position:fixed;left:10px;top:10px;z-index:1000;display:flex;gap:8px;background:var(--augit-panel)';
    probe.innerHTML = '<button class="fake-check" id="pixel-unchecked"></button><button class="fake-check checked" id="pixel-selected"></button><button class="fake-check mixed" id="pixel-mixed"></button><button class="fake-check checked" disabled></button><button class="fake-check mixed" disabled></button><input type="checkbox"><input type="checkbox" checked><input type="checkbox" checked disabled><input type="checkbox" disabled id="pixel-native-mixed">';
    probe.querySelector('#pixel-native-mixed').indeterminate = true;
    document.querySelector('.augit-window').append(probe);
  });
  const values = await page.locator('#pixel-check-probe > *').evaluateAll(nodes => nodes.map(node => {
    const css=getComputedStyle(node), before=getComputedStyle(node,'::before'), after=getComputedStyle(node,'::after');
    return { canvas:parseFloat(css.width), visible:parseFloat(before.width), edge:before.boxShadow, fill:before.backgroundColor, ink:after.backgroundColor, mark:after.display };
  }));
  values.forEach(value => { assert.equal(value.canvas,Math.round(24*scale)); assert.equal(value.visible,Math.round(16*scale)); });
  assert.ok(values[0].edge.includes(theme==='light'?'168, 173, 189':'90, 93, 99'), '未选框必须用Checkbox专用边框');
  assert.equal(values[3].ink, theme==='light'?'rgb(201, 204, 214)':'rgb(111, 115, 122)', '禁用勾颜色');
  assert.equal(values[4].ink, theme==='light'?'rgb(201, 204, 214)':'rgb(134, 138, 145)', '禁用半选横线颜色');
  assert.equal(values[8].ink, values[4].ink, '原生与自绘禁用半选必须使用相同墨迹颜色');
  await page.locator('#pixel-unchecked').focus();
  assert.equal(await page.locator('#pixel-unchecked').evaluate(n=>parseFloat(getComputedStyle(n,'::before').width)),Math.round(18*scale),'焦点外框为18px基准');
  for (const id of ['pixel-selected', 'pixel-mixed']) {
    await page.locator('#'+id).focus();
    const focus = await page.locator('#'+id).evaluate(n=>({inner:parseFloat(getComputedStyle(n,'::before').width),outer:parseFloat(getComputedStyle(n).getPropertyValue('--augit-check-selected-focus')),image:getComputedStyle(n).backgroundImage}));
    assert.equal(focus.inner, Math.round(16*scale), '已选聚焦时保留16px填充区');
    assert.equal(focus.outer, Math.round(22*scale), '已选/半选聚焦外环为22px基准');
    assert.notEqual(focus.image, 'none', '已选聚焦必须绘制独立外环');
  }
  await page.screenshot({path:path.join(output,`checkbox-${theme}-${size}.png`)});
  await page.locator('#pixel-check-probe').evaluate(n=>n.remove());
  records.push({scene:'checkbox-states',theme,size,values}); passed++;
}
(async()=>{
  await fs.mkdir(output,{recursive:true});
  let browser;
  try {
    browser=await chromium.launch({executablePath:executable,headless:true});
    const all=process.argv.includes('--all');
    const scenes=process.argv.includes('--settings-only')?['settings','settings-dirty','settings-save-failure']:all?(await fs.readdir('docs/ux-mockups')).filter(n=>n.endsWith('.html')&&n!=='index.html').map(n=>n.slice(0,-5)):['commit-changes','commit-diff','settings','settings-dirty','settings-save-failure','git-history','file-limit','image-error','clone','stash','reset'];
    for(const size of [12,40]) for(const theme of ['light','dark']) {
      const context=await browser.newContext({viewport:size===40?{width:1024,height:640}:{width:1180,height:760}});
      try {
        const page=await context.newPage();
        const errors=[]; page.on('pageerror',e=>errors.push(e.message));
        for(const scene of scenes) {
          const url=pathToFileURL(path.resolve('docs/ux-mockups',scene+'.html')); url.search=`theme=${theme}&ui-size=${size}`;
          await page.goto(url.href); await page.waitForSelector('body[data-typography-preview="ready"]');
          if (scene === 'clone' && theme === 'light' && size === 12) {
            await assert.rejects(inspectScreenshotSource(page, 'rollback'), /实际页面身份/);
            await page.locator('.clone-dialog').evaluate(node => node.remove());
            await assert.rejects(inspectScreenshotSource(page, 'clone'), /截图缺少/);
            await page.reload(); await page.waitForSelector('body[data-typography-preview="ready"]');
          }
          await verify(page,scene,theme,size); assert.deepEqual(errors,[]);
          if(scene==='commit-changes') await checkboxMatrix(page,theme,size);
        }
      } finally { await context.close(); }
    }
    console.log(`像素整改几何矩阵通过 ${passed} 个组合；证据目录=${output}`);
  } finally { await browser?.close(); await fs.writeFile(path.join(output,'geometry.json'),JSON.stringify(records,null,2)+'\n'); console.log('本次浏览器已关闭；未启动服务器'); }
})().catch(error=>{console.error(error);process.exitCode=1;});
