// 真实外壳场景验证：读取本轮隔离实例的图标归属、密度和焦点；不写字体或执行 Git 写操作。
const fs=require('node:fs/promises');
const path=require('node:path');
const assert=require('node:assert/strict');
(async()=>{
  const [modulePath,endpoint,output]=process.argv.slice(2);
  const {chromium}=require(path.resolve(modulePath));
  const browser=await chromium.connectOverCDP(endpoint);
  try{
    const page=browser.contexts()[0].pages().find(page=>page.url().includes('augit'));
    await page.reload();
    await page.waitForFunction(()=>window.__augitReady&&window.__augitSettingsReady&&window.__augitHistoryReady);
    await fs.mkdir(output,{recursive:true});
    const values={};
    values.project=await page.evaluate(()=>({size:getComputedStyle(document.body).fontSize,dpi:devicePixelRatio,
      types:[...document.querySelectorAll('.side-content.tree .file-type-icon')].map(node=>({path:node.closest('[data-tree-path]').dataset.treePath,type:node.getAttribute('class')})),
      directories:window.__augitLive.tree.filter(row=>row.isDirectory).map(row=>row.path),
      tab:document.querySelector('.editor-tab.active svg').getAttribute('class')}));
    assert.equal(values.project.tab,'file-type-icon file-type-javascript');
    for(const name of ['.tmp','.workbuddy','artifacts','obj','TestResults'])assert.ok(values.project.directories.includes(name),name);
    await page.screenshot({path:path.join(output,'augit-project.png')});
    await page.locator('.code-view').focus();
    values.focus=await page.locator('.editor-tab.active').evaluate(node=>getComputedStyle(node,'::before').backgroundColor);
    assert.equal(values.focus,'rgb(227, 235, 254)');
    await page.locator('.rail-button[data-tool="commit"]').click();
    await page.waitForSelector('.change-file-row');
    values.commit=await page.locator('.changes-layout').evaluate(node=>({
      heights:[...node.querySelectorAll('.check-row')].slice(0,12).map(row=>row.getBoundingClientRect().height),
      amend:node.querySelector('.commit-amend').textContent.trim(),last:node.querySelector('.commit-last').textContent.trim(),
      types:[...node.querySelectorAll('.file-type-icon')].map(svg=>({path:svg.closest('[data-path]').dataset.path,type:svg.getAttribute('class')}))}));
    assert.ok(values.commit.heights.every(height=>Math.abs(height-24)<.1));
    assert.equal(values.commit.amend,'Amend last commit');assert.equal(values.commit.last,'');
    assert.ok(values.commit.types.some(item=>item.path==='web/src/mockup.js'&&item.type.includes('javascript')));
    await page.screenshot({path:path.join(output,'augit-commit.png')});
    values.log=await page.evaluate(()=>({branches:[...document.querySelectorAll('.log-ref-panel [data-branch-icon]')].map(node=>({name:node.closest('[data-ref-name]').dataset.refName,icon:node.dataset.branchIcon})),
      references:[...document.querySelectorAll('.commit-row:first-child [data-log-reference]')].map(node=>({name:node.dataset.logReference,layers:node.querySelector('svg').dataset.referenceLayers})),errors:window.__augitErrors||[]}));
    assert.ok(values.log.branches.some(item=>item.name==='dsh'&&item.icon==='current'));
    assert.deepEqual(values.log.errors,[]);
    await fs.writeFile(path.join(output,'augit-scenes.json'),JSON.stringify(values,null,2)+'\n');
    console.log(JSON.stringify({project:values.project,commitRows:values.commit.heights,focus:values.focus,log:values.log}));
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
