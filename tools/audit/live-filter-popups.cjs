// 筛选和提示的场景回归：在实际控件中核对键盘、懒加载、关闭代次与焦点，不统一放大图标。
const assert=require('node:assert/strict');
module.exports=async function verifyFilterPopups({openScene,check}) {
  for(const theme of ['light','dark']){
    const scene=await openScene(`scene=git-history&theme=${theme}&ui-font-size=12`,()=>{
      window.__treeOverrides={'':[{name:'deep',path:'deep',isDirectory:true,canExpand:true},{name:'junction',path:'junction',isDirectory:true,canExpand:false,isReparsePoint:true}],
        deep:[{name:'inner',path:'deep/inner',isDirectory:true,canExpand:true}],
        'deep/inner':[{name:'hidden.txt',path:'deep/inner/hidden.txt',isDirectory:false,canExpand:false}]};
      const original=window.__hostStub;
      window.__folderReads=[];
      window.__hostStub=async(method,params)=>{
        if(method==='workspace/list'){window.__folderReads.push(params.path);if(window.__slowPath===params.path)await new Promise(resolve=>setTimeout(resolve,250));}
        return original(method,params);
      };
    });
    const page=scene.page;
    try{
      await page.setViewportSize({width:1640,height:985});
      await page.evaluate(()=>{window.__augitLive.references.branches=[{name:'main',isCurrent:true},{name:'feature/myBranch'},{name:'origin/release',isRemote:true}];window.__augitRenderRegions('bottomTool');});
      const branch=page.locator('.history-filters > [data-filter-key="branch"]');
      await branch.focus();await page.keyboard.press('ArrowDown');
      await page.waitForSelector('.history-branch-menu');
      await page.keyboard.type('fmb');
      assert.deepEqual(await page.locator('.history-branch-menu [data-history-branch]:visible').evaluateAll(rows=>rows.map(row=>row.dataset.historyBranch)),['feature/myBranch']);
      await page.keyboard.press('Escape');
      assert.equal(await page.locator('.history-branch-menu').isVisible(),true);
      await page.keyboard.type('origin/release');await page.keyboard.press('Enter');
      await page.waitForFunction(()=>window.__augitLive.historyFilter.branch==='origin/release');
      await branch.focus();await page.keyboard.press('ArrowDown');
      assert.equal(await page.locator('.history-branch-menu').isVisible(),true);
      await page.keyboard.press('Escape');await branch.focus();await page.keyboard.press('Delete');
      await page.waitForFunction(()=>!window.__augitLive.historyFilter.branch);
      await page.waitForSelector('.history-filters > [data-filter-key="branch"]:not(.selected)');
      assert.equal(await page.evaluate(()=>document.activeElement?.dataset.filterKey),'branch');
      check(`${theme} 分支 Down 打开、输入即匹配驼峰/路径、Esc先清空、Enter应用、Delete清除`,true);
      for(const [key,selector,filter] of [['date','.history-date-menu',{since:'2026-09-01T00:00:00Z'}],['user','.history-user-filter-menu',{authors:['one@example.invalid']}],['path','.history-path-menu',{paths:['deep']}]]){
        await page.evaluate(filter=>{window.__augitLive.historyFilter=filter;window.__augitRenderRegions('bottomTool');},filter);
        const control=page.locator(`.history-filters > [data-filter-key="${key}"]`);
        await control.focus();await page.keyboard.press('ArrowDown');await page.waitForSelector(selector);
        if(key==='date'){
          assert.deepEqual(await page.locator('.history-date-menu .menu-item').allTextContents(),['Select…','Last 24 hours','Last 7 days']);
          assert.equal(await page.locator('.history-date-menu svg').count(),0);
          const anchor=await control.boundingBox(),popup=await page.locator(`section${selector}`).boundingBox();
          assert.ok(Math.abs(anchor.x-popup.x)<=1&&Math.abs(anchor.y+anchor.height+4-popup.y)<=1,JSON.stringify({anchor,popup}));
          assert.equal(popup.width,108);
          assert.deepEqual(await page.locator('.history-date-menu .menu-item').evaluateAll(rows=>rows.map(row=>row.getBoundingClientRect().height)),[24,24,24]);
        }
        await page.keyboard.press('Escape');await control.focus();await page.keyboard.press('Delete');
        await page.waitForSelector(`.history-filters > [data-filter-key="${key}"]:not(.selected)`,{state:'attached'});
        await page.waitForFunction(key=>document.activeElement?.dataset.filterKey===key,key,{timeout:3000}).catch(async error=>{throw new Error(`${key} 清除焦点：${JSON.stringify(await page.evaluate(()=>({active:{tag:document.activeElement.tagName,label:document.activeElement.getAttribute('aria-label'),key:document.activeElement.dataset.filterKey},filter:window.__augitLive.historyFilter,overlays:[...document.querySelectorAll('[data-augit-overlay]')].map(node=>node.className)})))}`,{cause:error});});
        assert.equal(await page.evaluate(()=>document.activeElement?.dataset.filterKey),key);
      }
      check(`${theme} 日期/用户/路径已设值仍可Down打开，Delete分别清除并归还控件焦点`,true);
      const date=page.locator('.history-filters > [data-filter-key="date"]');
      await date.focus();await page.keyboard.press('ArrowDown');await page.locator('[data-history-date="select"]').click();
      const dialog=page.locator('.history-date-range-dialog');
      assert.equal(await dialog.getAttribute('aria-label'),'Select Period');
      assert.deepEqual(await dialog.locator('.history-date-bound span').allTextContents(),['After','Before']);
      assert.equal(await dialog.locator('.dialog-footer button').count(),1);
      assert.equal(await dialog.locator('[data-history-date-field="since"]').isDisabled(),true);
      assert.equal(await dialog.locator('[data-history-date-field="until"]').isDisabled(),true);
      assert.equal(await dialog.locator('[data-history-date-calendar="since"]').isDisabled(),true);
      const bounds=await dialog.boundingBox();assert.equal(bounds.width,504);assert.ok(bounds.height>=130&&bounds.height<=134,JSON.stringify(bounds));
      const fields=await dialog.locator('[data-history-date-field]').evaluateAll(nodes=>nodes.map(node=>node.getBoundingClientRect().top));assert.equal(fields[0],fields[1]);
      await page.keyboard.press('Enter');assert.equal(await dialog.count(),0);
      assert.equal(await page.evaluate(()=>window.__augitLive.historyFilter.since||window.__augitLive.historyFilter.until||null),null);
      await date.focus();await page.keyboard.press('ArrowDown');await page.locator('[data-history-date="select"]').click();
      await page.locator('[data-history-date-enabled="since"]').check();await page.locator('[data-history-date-field="since"]').fill('2026-09-01');
      assert.equal(await page.locator('[data-history-date-calendar="since"]').isDisabled(),false);
      assert.equal(await page.locator('[data-history-date-calendar="until"]').isDisabled(),true);
      await page.keyboard.press('Enter');await page.waitForFunction(()=>!!window.__augitLive.historyFilter.since);
      assert.equal(await page.evaluate(()=>window.__augitLive.historyFilter.until),undefined);
      await page.waitForSelector('.history-filters > [data-filter-key="date"].selected',{state:'attached'});
      let dateClear=date;if(!await dateClear.isVisible()){await page.locator('[aria-label="More History Filters"]').click();dateClear=page.locator('.history-filter-menu [data-filter-key="date"]');}
      await dateClear.focus();await page.keyboard.press('Delete');await page.waitForSelector('.history-filters > [data-filter-key="date"]:not(.selected)',{state:'attached'});
      check(`${theme} 日期菜单108px/24px、范围504×132横向独立开关、仅OK/Enter提交有效端点`,true);
      await page.locator('.rail-button[data-tool="search"]').hover();
      const tip=page.locator('[role="tooltip"]');await tip.waitFor();
      assert.equal(await tip.locator('.help-shortcut').innerText(),'Ctrl+Shift+F');
      assert.equal(await page.locator('.rail-button[data-tool="search"]').getAttribute('title'),null);
      const placement=await tip.evaluate(node=>{const box=node.getBoundingClientRect(),owner=document.querySelector('.rail-button[data-tool="search"]').getBoundingClientRect();return{gap:box.left-owner.right,delta:box.top-owner.top,alignment:node.dataset.alignment};});
      assert.deepEqual(placement,{gap:5,delta:1,alignment:'right'});
      await page.mouse.move(900,400);assert.equal(await tip.count(),0);
      await page.locator('.rail-button[data-tool="project"]').focus();await tip.waitFor();
      await page.locator('.titlebar [aria-label="Settings"]').click();assert.equal(await tip.count(),0);
      await page.locator('[data-settings-action="cancel"]').click();
      check(`${theme} 工具轨使用分层HelpTooltip、已有快捷键和向右5px定位，离开立即清理`,true);
      const pathControl=page.locator('.history-filters > [data-filter-key="path"]');
      await pathControl.focus();await page.keyboard.press('ArrowDown');await page.locator('[data-log-path-action="select"]').click();
      const popup=page.locator('.history-path-text-popover'),text=page.locator('[data-log-path-field]');
      assert.equal(await popup.locator('.dialog-header,button').count(),0);
      assert.equal(await page.locator('.titlebar').evaluate(node=>node.inert),false);
      const pathAnchor=await pathControl.boundingBox(),pathPopup=await popup.boundingBox();
      assert.ok(Math.abs(pathPopup.x-pathAnchor.x)<=1&&Math.abs(pathPopup.y-pathAnchor.y-pathAnchor.height-4)<=1,JSON.stringify({pathAnchor,pathPopup}));
      await text.fill('deep');await page.keyboard.press('Enter');assert.equal(await popup.count(),1);
      await text.fill(' deep\\inner \n\n deep/inner/hidden.txt ');await page.keyboard.press('Control+Enter');
      await page.waitForFunction(()=>window.__augitLive.historyFilter.paths?.length===2);
      assert.deepEqual(await page.evaluate(()=>window.__augitLive.historyFilter.paths),['deep/inner','deep/inner/hidden.txt']);
      await page.waitForSelector('.history-filters > [data-filter-key="path"].selected',{state:'attached'});
      let textControl=pathControl;if(!await textControl.isVisible()){await page.locator('[aria-label="More History Filters"]').click();textControl=page.locator('.history-filter-menu [data-filter-key="path"]');}
      await textControl.focus();await page.keyboard.press('ArrowDown');await page.locator('[data-log-path-action="select"]').click();
      assert.equal(await text.inputValue(),'deep/inner\ndeep/inner/hidden.txt');
      await text.fill('discard');await page.keyboard.press('Escape');assert.equal(await popup.count(),0);
      assert.deepEqual(await page.evaluate(()=>window.__augitLive.historyFilter.paths),['deep/inner','deep/inner/hidden.txt']);
      let textClear=pathControl;if(!await textClear.isVisible()){await page.locator('[aria-label="More History Filters"]').click();textClear=page.locator('.history-filter-menu [data-filter-key="path"]');}
      await textClear.focus();await page.keyboard.press('Delete');await page.waitForSelector('.history-filters > [data-filter-key="path"]:not(.selected)',{state:'attached'});
      check(`${theme} 路径无标题锚定浮层、Enter换行、Ctrl+Enter规范化提交、Esc取消并保留已用筛选`,true);
      await pathControl.focus();await page.keyboard.press('ArrowDown');
      await page.locator('[data-log-path-action="tree"]').click();
      await page.waitForSelector('[data-log-path-row="deep"]');
      assert.equal(await page.locator('[data-log-path-tree="confirm"]').isDisabled(),true);
      assert.equal(await page.locator('.side-content.tree [data-tree-path="deep/inner"]').count(),0);
      await page.locator('[data-log-path-row="deep"] [data-path-disclosure]').click();
      await page.waitForSelector('[data-log-path-row="deep/inner"]');
      await page.locator('[data-log-path-row="deep/inner"]').focus();await page.keyboard.press('ArrowRight');
      await page.waitForSelector('[data-log-path-row="deep/inner/hidden.txt"]');
      await page.locator('[data-log-path-row="deep/inner/hidden.txt"]').focus();await page.keyboard.press(' ');
      await page.locator('[data-log-path-row="deep"] [data-path-disclosure]').click();
      assert.equal(await page.locator('[data-log-path-row="deep/inner/hidden.txt"]').count(),0);
      assert.equal(await page.locator('.history-path-tree-window .footer-help').innerText(),'Selected: 1');
      assert.equal(await page.locator('[data-log-path-row="junction"] .chevron svg').count(),0);
      await page.locator('[data-log-path-tree="confirm"]').click();
      await page.waitForFunction(()=>window.__augitLive.historyFilter.paths?.[0]==='deep/inner/hidden.txt');
      await page.waitForSelector('.history-filters > [data-filter-key="path"].selected',{state:'attached'});
      assert.equal(await page.locator('.side-content.tree [data-tree-path="deep/inner"]').count(),0);
      check(`${theme} 路径树独立读取未展开深层文件、键盘展开勾选、折叠保留草稿且不递归联接`,true);
      let clearControl=pathControl;
      if(!await clearControl.isVisible()){await page.locator('[aria-label="More History Filters"]').click();clearControl=page.locator('.history-filter-menu [data-filter-key="path"]');}
      await clearControl.focus();await page.keyboard.press('Delete');
      await page.waitForSelector('.history-filters > [data-filter-key="path"]:not(.selected)',{state:'attached'});
      assert.equal(await page.evaluate(()=>document.activeElement?.dataset.filterKey),'path');
      await page.evaluate(()=>window.__slowPath='deep');
      await pathControl.focus();await page.keyboard.press('ArrowDown');await page.locator('[data-log-path-action="tree"]').click();
      await page.waitForSelector('[data-log-path-row="deep"]');await page.locator('[data-log-path-row="deep"] [data-path-disclosure]').click();
      await page.keyboard.press('Escape');await page.waitForTimeout(300);
      assert.equal(await page.locator('.history-path-tree-window').count(),0);
      assert.deepEqual(scene.errors,[]);
      check(`${theme} 路径树关闭后的慢响应不重建弹层或影响Project`,true);
      await page.evaluate(()=>{window.__slowPath='deep';window.__augitLive.historyFilter={paths:['deep/inner/hidden.txt']};window.__augitRenderRegions('bottomTool');});
      await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
      let reopenControl=page.locator('.history-filters [data-filter-key="path"]:not([hidden])').first();
      if(!await reopenControl.isVisible()){await page.locator('[aria-label="More History Filters"]').click();reopenControl=page.locator('.history-filter-menu [data-filter-key="path"]');}
      await reopenControl.focus();await page.keyboard.press('ArrowDown');await page.locator('[data-log-path-action="tree"]').click();
      await page.waitForSelector('[data-log-path-row="deep"][aria-expanded="true"]');
      await page.locator('[data-log-path-row="deep"] [data-path-disclosure]').click();
      await page.waitForTimeout(300);
      assert.equal(await page.locator('[data-log-path-row="deep"]').getAttribute('aria-expanded'),'false');
      assert.equal(await page.locator('[data-log-path-row="deep/inner"]').count(),0);
      await page.keyboard.press('Escape');
      check(`${theme} 路径初始选择恢复遇到手动折叠即停止，不被旧异步恢复重新展开`,true);
      await page.evaluate(()=>{
        window.__augitLive.historyFilter={};
        window.__augitLive.references.branches=[{name:'feature/a/b/c/d/e/f/leaf'}];
        window.__augitRenderRegions('bottomTool');
      });
      for(const size of [12,20,40]){
        await page.evaluate(size=>applyTypography({uiSize:size}),size);
        const geometry=await page.evaluate(()=>{
          const row=document.querySelector('.log-ref-panel [data-ref-name="feature/a/b/c/d/e/f/leaf"]');
          const indent=parseFloat(getComputedStyle(row).paddingLeft),depth=+row.style.getPropertyValue('--tree-depth');
          return {indent,depth,font:parseFloat(getComputedStyle(document.body).fontSize)};
        });
        const scale=Math.max(.75,Math.round(size/12*4)/4);
        assert.equal(geometry.indent,2+(Math.max(Math.round(7*scale),Math.floor(Math.round(16*scale)/2))+Math.round(11*scale))*geometry.depth);
        assert.ok(geometry.depth>4);
      }
      let largeDate=page.locator('.history-filters > [data-filter-key="date"]');
      if(!await largeDate.isVisible()){await page.locator('[aria-label="More History Filters"]').click();largeDate=page.locator('.history-filter-menu [data-filter-key="date"]');}
      await largeDate.focus();await page.keyboard.press('ArrowDown');await page.locator('[data-history-date="select"]').click();
      const large=await page.locator('.history-date-range-dialog').evaluate(node=>({width:node.getBoundingClientRect().width,height:node.getBoundingClientRect().height,
        fieldHeight:node.querySelector('[data-history-date-field]').getBoundingClientRect().height,font:parseFloat(getComputedStyle(node).fontSize),overflow:node.scrollWidth>node.clientWidth}));
      assert.ok(large.width<=1640-16&&large.fieldHeight>=large.font&&!large.overflow,JSON.stringify(large));
      await page.keyboard.press('Escape');
      check(`${theme} 多级分支树12/20/40px缩进完整缩放，40px日期表单不裁字或越出窗口`,true);
    }finally{await page.close();}
  }
  const unavailable=await openScene('scene=main-project&theme=dark');
  try{
    await unavailable.page.evaluate(()=>{window.__augitLive.gitUnavailableReason='Git for Windows 2.40 or newer was not found.';window.__augitRenderRegions('rail');});
    const button=unavailable.page.locator('.rail-button[data-tool="commit"]');
    await button.hover();await unavailable.page.waitForSelector('[role="tooltip"] .help-description');
    assert.equal(await button.getAttribute('title'),null);
    assert.match(await unavailable.page.locator('[role="tooltip"] .help-description').innerText(),/Git/i);
    assert.equal(await unavailable.page.locator('[role="tooltip"] .help-shortcut').count(),0);
    check('Git不可用工具轨入口保留禁用原因HelpTooltip，不显示无效快捷键或浏览器title',true);
  }finally{await unavailable.page.close();}
};
