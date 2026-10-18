// 用正式产品动作逐页取证，不注入 CSS、字体、缩放、宿主桩或测试正文。
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const captureStablePage = require('./capture-stable-page.cjs');
module.exports = async function captureStartupSurfaces(page, output, prefix) {
  const surfaces = {};
  const selectors = ['.titlebar','.side-tool','.tree-row','.check-group-row','.change-file-row','.editor-tabs','.editor-content','.document-toolbar','.code-line','.diff-layout','.diff-columns','.markdown-panes','.markdown-preview','.json-document','.image-stage','.info-block','.bottom-tool','.log-filterbar','.commit-row','.current-find','.search-overlay','.search-query','.search-results','.branches-popover','.dialog','.dialog-header','.dialog-body','.dialog-footer','.settings-nav','.settings-page-header','.settings-page','.terminal-header','.terminal-view','.xterm-screen','.statusbar'];
  async function capture(name, expected) {
    await page.locator(expected).first().waitFor({state:'visible'});
    await page.evaluate(()=>document.fonts.ready);
    await page.waitForTimeout(450);
    await page.mouse.move(1,1);
    let measured = null;
    for(let attempt=0;attempt<100&&!measured;attempt++) {
    await page.waitForFunction(()=>!document.querySelector('[data-native-capture],[data-native-background-capture]')&&!window.__augitNativeBackgroundBusy);
    measured = await page.evaluate(selectors=>{
      // 度量与条件在同一任务内读取，避免先等待、后读取时恰好进入下一次背景捕获。
      if(window.__augitNativeBackgroundBusy||document.querySelector('[data-native-capture],[data-native-background-capture]'))return null;
      const live=window.__augitLive;
      const metrics=Object.fromEntries(selectors.map(selector=>{
        const nodes=[...document.querySelectorAll(selector)].filter(node=>node.getClientRects().length&&getComputedStyle(node).visibility!=='hidden');
        return [selector,nodes.slice(0,3).map(node=>{const r=node.getBoundingClientRect(),s=getComputedStyle(node);return {x:r.x,y:r.y,w:r.width,h:r.height,font:s.font,lineHeight:s.lineHeight,color:s.color,background:s.backgroundColor,border:s.border,padding:s.padding,gap:s.gap};})];
      }));
      return {appearance:{theme:live.settings.theme,uiSize:getComputedStyle(document.documentElement).fontSize,uiFamily:live.settings.textFontFamily,codeSize:live.settings.codeFontSize,codeFamily:live.settings.monospaceFontFamily,dpr:devicePixelRatio,scale:visualViewport.scale},metrics,terminal:document.querySelector('.terminal-tool')?window.__augitTerminalFont():null,errors:window.__augitErrors||[]};
    },selectors);
    if(!measured)await page.waitForTimeout(50);
    }
    assert.ok(measured,name+' 未取得稳定的表单度量');
    surfaces[name]=measured;
    assert.deepEqual(surfaces[name].errors,[],name+' 不应有页面异常');
    assert.equal(surfaces[name].appearance.codeFamily,'JetBrains Mono');
    assert.equal(surfaces[name].appearance.codeSize,13);
    assert.equal(surfaces[name].appearance.scale,1);
    const capture = await captureStablePage(page,expected,path.join(output,prefix+'-'+name+'.png'));
    await fs.writeFile(path.join(output,prefix+'-'+name+'-capture.json'),JSON.stringify(capture,null,2));
    await fs.writeFile(path.join(output,prefix+'-surfaces.json'),JSON.stringify(surfaces,null,2));
    console.log(JSON.stringify({mode:prefix,surface:name}));
  }
  async function close(selector) {
    if(selector)await page.locator(selector).first().click();else await page.keyboard.press('Escape');
    await page.waitForFunction(()=>!window.__augitNativeModalActive);
    await page.waitForTimeout(100);
  }
  async function mainMenu(action) {
    for(let attempt=0;attempt<3;attempt++) {
      try {
        if(!await page.locator('.main-menu-bar').count())await page.locator('.titlebar [data-action="menu"]').click({timeout:2500});
        await page.locator('.main-menu-entry').filter({hasText:/^Git$/}).click({timeout:2500});
        break;
      } catch(error) {
        // 只重试只读菜单导航；异步标题刷新会移除旧菜单，不能重复执行动作。
        if(attempt===2)throw error;
        await page.waitForTimeout(400);
      }
    }
    await page.locator('[data-main-menu-action="'+action+'"]').click();
  }
  async function document(file,expected) {
    // 该公开入口复用双击文件的正式打开路径，仅省去目录展开。
    await page.evaluate(file=>window.__augitOpenDocument(file),file);
    await page.locator(expected).first().waitFor({state:'visible'});
  }
  await document('src/Augit.Core/Documents/DocumentClassification.cs','.code-view');
  if(!await page.locator('.side-content.tree').isVisible())await page.locator('[data-tool="project"]').click();
  await capture('project-text','.code-view');
  await page.locator('.code-view').focus();await page.keyboard.press('Control+f');
  await capture('current-find','.current-find');await page.keyboard.press('Escape');
  await page.locator('.code-view').focus();await page.keyboard.press('Control+g');
  await capture('goto-empty','[data-compact-dialog]');
  await page.locator('[data-compact-field]').fill('0');await page.keyboard.press('Enter');
  await capture('goto-invalid','[data-compact-dialog] .footer-help');await close('[data-compact-action="cancel"]');
  await page.locator('.code-view').focus();await page.keyboard.press('Control+p');
  await capture('quick-open','.search-overlay');
  await page.locator('.search-overlay .search-field').fill('DocumentClassification');
  await capture('quick-results','.search-result');await close();
  await page.locator('.code-view').focus();await page.keyboard.press('Control+Shift+f');
  await capture('repository-search','.search-overlay');await close();
  await page.locator('.titlebar .branch-chip').click();await capture('branches','.branches-popover');await close();
  if(!await page.locator('.change-file-row').first().isVisible())await page.locator('[data-tool="commit"]').click();
  await capture('commit','.change-file-row');
  await page.locator('.change-file-row[data-path="web/src/mockup.css"] .tree-name').click();
  await page.locator('[data-action="rollback-change"]').click();
  await capture('rollback','.rollback-dialog');await close('[data-rollback-action="cancel"]');
  await page.locator('.change-file-row[data-path="web/src/mockup.css"] .tree-name').dblclick();
  await page.waitForFunction(()=>document.querySelector('.diff-layout')&&!window.__augitLive.diffLoading);
  await capture('workspace-diff','.diff-layout');
  if(!await page.locator('.commit-row').first().isVisible())await page.locator('[data-tool="history"]').click();
  await capture('git-log','.commit-row');
  await document('docs/development-validation.md','.markdown-document');
  for(const mode of ['source','split','preview']) {
    await page.locator('.editor-tabs [data-markdown-mode="'+mode+'"]').click();
    await capture('markdown-'+mode,'.markdown-document');
  }
  await document('global.json','.json-document');await capture('json','.json-document');
  await document('web/image-sample.png','.image-stage');await capture('image','.image-stage');
  await document('web/vendor/jetbrains-mono/JetBrainsMono-Regular.woff2','.info-block');await capture('file-info','.info-block');
  await document('src/Augit.Core/Documents/DocumentClassification.cs','.code-view');
  await page.locator('.titlebar [aria-label="Settings"]').click();
  for(const category of ['appearance','file-view','git','terminal']) {
    await page.locator('[data-settings-page="'+category+'"]').click();
    await capture('settings-'+category,'.settings-window .dialog');
    if(category==='file-view') {
      assert.equal(await page.locator('[data-fixed-code-font]').innerText(),'JetBrains Mono');
      assert.equal(await page.locator('[data-setting="monospaceFontFamily"], [data-setting="codeFontSize"]').count(),0);
    }
  }
  await close('[data-settings-action="cancel"]');
  await page.evaluate(()=>window.__augitOpenWorkspaceDialog());await capture('workspace-open','.workspace-open-window .dialog');
  await page.locator('[data-workspace-action="clone"]').click();await capture('clone','.clone-dialog');
  await close('.clone-dialog .secondary-button');
  await mainMenu('stash-create');await capture('stash-create','.stash-dialog');await close('[data-stash-create-action="cancel"]');
  await mainMenu('stash-manager');await capture('stash-manager','.stash-manager-dialog');await close('[data-stash-action="close"]');
  await mainMenu('worktree-manager');await capture('worktrees','.worktree-dialog');await close('[data-wtm-action="close"]');
  await mainMenu('reset');await capture('reset','.reset-dialog');await close('.reset-dialog .secondary-button');
  await mainMenu('push');await capture('push','.push-dialog');await close('[data-push-action="cancel"]');
  await page.evaluate(()=>window.__augitOpenRemoteManager());await capture('remotes','.remote-dialog');
  await page.locator('[data-remote-action="add"]').click();await capture('remote-add','.remote-define-window .dialog');
  await page.locator('button[data-remote-define="cancel"]').click();await close('[data-remote-action="ok"]');
  await page.locator('[data-tool="terminal"]').click();
  await page.waitForFunction(()=>window.__augitTerminalFont()!==null);
  await capture('terminal','.xterm-screen');
  await page.locator('[aria-label="Close Terminal"]').click();
  if(await page.locator('[data-terminal-close="confirm"]').isVisible())await page.locator('[data-terminal-close="confirm"]').click();
  return surfaces;
};
