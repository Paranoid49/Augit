// 真实网页装配路径的像素整改回归；桩仅提供数据，几何判据来自报告与上游常量。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { captureVerifiedScreenshot } = require('./screenshot-provenance.cjs');
const output = path.resolve('artifacts/pixel-restoration-2026-10-13/live');
const box = node => {const r=node.getBoundingClientRect();return{x:r.x,y:r.y,w:r.width,h:r.height};};
const inside = (a,b) => a.x>=b.x-.6&&a.y>=b.y-.6&&a.x+a.w<=b.x+b.w+.6&&a.y+a.h<=b.y+b.h+.6;
module.exports = async ({openScene,check}) => {
  await fs.mkdir(output,{recursive:true});
  const records=[];
  for (const theme of ['light','dark']) for (const size of [12,14,40]) {
    const scene=await openScene(`scene=commit-changes&open=docs/notes.txt&theme=${theme}&ui-font-size=${size}`);
    const {page,errors}=scene;
    try {
      await page.setViewportSize(size===40?{width:1024,height:640}:{width:1640,height:900});
      await page.evaluate(()=>applyTypography({uiSize:parseFloat(getComputedStyle(document.documentElement).fontSize)}));
      const scale=Math.max(.75,Math.round(size/12*4)/4);
      const geometry=await page.evaluate(()=>{
        const r=node=>{const b=node.getBoundingClientRect();return{x:b.x,y:b.y,w:b.width,h:b.height};};
        return{group:r(document.querySelector('.check-group-row .fake-check')),file:r(document.querySelector('.change-file-row .fake-check')),
          list:r(document.querySelector('.changes-list')),main:r(document.querySelector('.app-main')),editor:r(document.querySelector('.editor-content')),
          row:r(document.querySelector('.change-file-row')),caption:document.querySelector('.commit-amend').textContent,last:document.querySelector('.commit-last').textContent};
      });
      assert.equal(geometry.file.x-geometry.group.x,({12:19,14:24,40:62}[size]));
      assert.equal(geometry.row.h,Math.round(24*scale));
      assert.deepEqual(await page.evaluate(()=>[0,1,2].map(changesFileCount)),['No files','1 file','2 files']);
      assert.deepEqual(await page.evaluate(()=>[changeParentPresentation({directory:''}),changeParentPresentation({directory:'src/nested'})]),[await page.evaluate(()=>window.__augitLive.workspaceName),'src\\nested']);
      assert.equal(geometry.caption,'Amend');assert.equal(geometry.last,'last commit');
      assert.ok(geometry.editor.h>=23);
      if(size===40) {
        assert.ok(geometry.list.h>=78);
        for(const button of await page.locator('.commit-actions > :is(.primary-button,.secondary-button)').all()) {
          await button.scrollIntoViewIfNeeded();assert.ok(inside(await button.evaluate(box),await page.locator('.side-tool').evaluate(box)));
          assert.equal(await button.evaluate(node => {const r=node.getBoundingClientRect();return node.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));}),true,'动作中心实际可命中');
        }
      }
      // 单击选择保持勾选与现有普通文档；键盘明确打开才建立比较。
      const before=await page.evaluate(()=>({checks:[...document.querySelectorAll('.change-file-row .fake-check')].map(n=>n.getAttribute('aria-checked')),active:window.__augitLive.activeTabId}));
      await page.locator('.change-file-row .tree-name').first().click();
      assert.deepEqual(await page.evaluate(()=>({checks:[...document.querySelectorAll('.change-file-row .fake-check')].map(n=>n.getAttribute('aria-checked')),active:window.__augitLive.activeTabId})),before);
      await page.locator('.change-file-row').first().focus();await page.keyboard.press('Enter');await page.waitForSelector('.diff-layout');
      const close=page.locator('.editor-tab.active .tab-close');
      const svg=await close.locator('svg').evaluate(box);assert.equal(svg.w,svg.h);assert.ok(inside(svg,await close.evaluate(box)));
      if(size===40) {
        await page.locator('.rail-button[data-tool="history"]').click();
        await page.evaluate(()=>applyTypography({uiSize:40}));
        assert.ok((await page.locator('.diff-columns').first().evaluate(box)).h>=23,'真正的Diff正文不能被标题/工具栏挤成零');
      }
      await page.locator('.titlebar [aria-label="Settings"]').click();
      await page.setViewportSize({width:1640,height:900});
      const dialog=page.locator('.settings-window .dialog');
      geometry.liveSettings=await dialog.evaluate(box);
      assert.equal(geometry.liveSettings.w,1040,'真实Settings入口与dialog-xl场景使用同一宽度');
      const search=page.locator('.settings-window [data-settings-filter]');
      assert.ok((await search.evaluate(box)).h>=size+8);
      // 标题拖动不重建背景；越界时回到宿主边界，拖动中的Esc仅结束拖动。
      const header=await dialog.locator('.dialog-header').boundingBox();
      await page.mouse.move(header.x+20,header.y+12);await page.mouse.down();await page.mouse.move(header.x+60,header.y+42);
      await page.keyboard.press('Escape');await page.mouse.up();assert.equal(await dialog.count(),1);
      const moved=await dialog.evaluate(box);assert.ok(moved.x!==geometry.liveSettings.x||moved.y!==geometry.liveSettings.y);
      await page.setViewportSize({width:1024,height:640});
      assert.ok(inside(await dialog.evaluate(box),{x:0,y:0,w:1024,h:640}),'拖动过的模态在宿主缩小时仍适屏');
      await page.locator('[data-settings-action="cancel"]').click();
      assert.equal(await dialog.count(),0);
      assert.deepEqual(errors,[]);
      check(`${theme}/UI${size} 真实装配缩进、字号、单击/Enter、关闭图形、Settings宽度及拖动适屏`,true);
      // 负向对照：临时破坏缩进，只在当前测试页；断言必须能辨认报告中的方向错误。
      if(size===12&&theme==='light') {
        const negative=await page.addStyleTag({content:'.changes-list .change-file-row{padding-left:7px!important}'});
        const delta=await page.evaluate(()=>document.querySelector('.change-file-row .fake-check').getBoundingClientRect().x-document.querySelector('.check-group-row .fake-check').getBoundingClientRect().x);
        assert.notEqual(delta,19);check('负向对照：移除层级修复时缩进判据失败',true);await negative.evaluate(n=>n.remove());
      }
      // 此时实际画面已是Diff，保留入口scene并另记可见特征，避免commit-changes名称冒充截图内容。
      const source = await captureVerifiedScreenshot(page, 'commit-changes', path.join(output,`live-${theme}-${size}.png`), { timeout:5000 });
      assert.equal(await page.locator('.diff-layout').count(), 1, '实际截图必须包含键盘打开的Diff');
      source.visibleState = 'Changes与已打开Diff'; source.additionalFeature = '.diff-layout';
      records.push({theme,size,...geometry,screenshotSource:source});
    } finally {await page.close();}
  }
  for(const name of ['stash','reset']) {
    const scene=await openScene(`scene=${name}&theme=light&ui-font-size=12`);
    try {
      const footer=scene.page.locator(`.${name}-dialog .dialog-footer`);
      const text=await footer.locator('button').allTextContents();assert.equal(text.at(-1),'Cancel');
      assert.ok((await footer.locator('.secondary-button').evaluate(box)).w>=78);
      check(`真实${name}的确认/Cancel顺序与按钮最小绘制区`,true);
    } finally {await scene.page.close();}
  }
  await fs.writeFile(path.join(output,'geometry.json'),JSON.stringify(records,null,2)+'\n');
};
