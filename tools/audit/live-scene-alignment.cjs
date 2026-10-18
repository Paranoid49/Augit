// 场景归属回归：不止检查 SVG，还从真实 Project、Changes、搜索、标签和引用树验证实际选图与状态。
const assert = require('node:assert/strict');
const mapping = [['bridge.js','javascript'], ['main.ts','typescript'], ['theme.css','css'], ['app.py','python'], ['.editorconfig','editorconfig'], ['.gitignore','gitignore']];
const initialize = () => {
  const names = ['bridge.js','main.ts','theme.css','app.py','.editorconfig','.gitignore'];
  window.__treeOverrides = { '': names.map(name => ({ name, path:name,isDirectory:false,canExpand:false })) };
  window.__liveFiles = names.map(name => ({path:name,name,directory:'',group:'Changes',kind:'Modified',workingTree:true,staged:false}));
  const original = window.__hostStub;
  window.__hostStub = async (method, params) => {
    if(method === 'document/read' && names.includes(params.path)) return {path:params.path,name:params.path,kind:'Text',status:'TextReady',text:'first\nsecond\nthird',encoding:'UTF-8',lineEndings:'LF',fileSize:30};
    if(method === 'search/files') return {matches:names.map(name => ({path:name,name,directory:''}))};
    return original(method,params);
  };
};

module.exports = async function verifySceneAlignment({openScene,check}) {
  for(const theme of ['light','dark']) {
    const project = await openScene(`scene=main-project&theme=${theme}&ui-font-size=12`, initialize);
    const page = project.page;
    try {
      for(const [name,kind] of mapping) {
        assert.equal(await page.locator(`.side-content.tree [data-tree-path="${name}"] .file-type-${kind}`).count(),1,name);
      }
      assert.equal(await page.locator('.side-content.tree [data-tree-path="bridge.js"] svg').last().evaluate(node=>getComputedStyle(node).color),theme==='light'?'rgb(255, 175, 15)':'rgb(242, 197, 92)');
      check(`${theme} Project 根据实际文件名选用六类图形，普通 Git 状态不覆盖类型色`,true);
      await page.locator('.side-content.tree [data-tree-path="bridge.js"]').dblclick();
      await page.waitForSelector('.editor-tabs [data-tab-id] .file-type-javascript');
      await page.evaluate(() => window.__augitOpenDocument('main.ts'));
      assert.equal(await page.locator('.editor-tabs .active .file-type-typescript').count(),1);
      check(`${theme} 真正打开 JS/TS 后标签仍保留文件类型图标`,true);
      const active = page.locator('.editor-tab.active');
      const background = () => active.evaluate(node => getComputedStyle(node,'::before').backgroundColor);
      await page.locator('.code-view').focus();
      const selected = await background();
      assert.equal(selected,theme==='light'?'rgb(227, 235, 254)':'rgb(35, 53, 88)');
      await page.locator('.side-content.tree .root-row').focus();
      assert.equal(await background(),theme==='light'?'rgb(233, 234, 238)':'rgb(38, 40, 44)');
      await page.locator('.code-view').focus();
      await active.hover();
      assert.equal(await background(),selected);
      check(`${theme} 活动卡片包含正文焦点，项目树失焦变中性，选中活动卡片 hover 不覆盖`,true);
      const other = page.locator('.editor-tab:not(.active)[data-tab-id]').first();
      await other.hover();
      const card = await other.evaluate(node => ({content:getComputedStyle(node,'::before').content,
        color:getComputedStyle(node,'::before').backgroundColor,radius:getComputedStyle(node,'::before').borderRadius,inset:getComputedStyle(node,'::before').inset}));
      assert.deepEqual(card,{content:'""',color:theme==='light'?'rgba(0, 0, 0, 0.07)':'rgba(255, 255, 255, 0.09)',radius:'6px',inset:'0px 4px'});
      check(`${theme} 未选中标签绘制相同几何的 Islands 悬停卡片`,true);
      await page.locator('.titlebar [aria-label="Settings"]').click();
      await page.locator('.settings-window input').first().focus();
      assert.notEqual(await background(),selected);
      await page.locator('[data-settings-action="cancel"]').click();
      check(`${theme} 设置模态拥有焦点时标签回到失焦色`,true);
      assert.deepEqual(project.errors,[]);
    } finally { await page.close(); }

    const commit = await openScene(`scene=commit-changes&theme=${theme}&ui-font-size=12`,initialize);
    try {
      for(const [name,kind] of mapping) assert.equal(await commit.page.locator(`.change-file-row[data-path="${name}"] .file-type-${kind}`).count(),1);
      const rows = await commit.page.locator('.changes-list .check-row').evaluateAll(nodes => nodes.map(node=>node.getBoundingClientRect().height));
      assert.ok(rows.length>6 && rows.every(height=>height===24),JSON.stringify(rows));
      const options = await commit.page.locator('.commit-options').evaluate(node=>({
        amend:node.querySelector('.commit-amend').textContent.trim(),last:node.querySelector('.commit-last').textContent.trim(),
        icon:node.querySelector('.commit-last svg')?.dataset.augitIcon || null,color:getComputedStyle(node.querySelector('.commit-count')).color,
      }));
      assert.equal(options.amend,'Amend');assert.equal(options.last,'last commit');assert.equal(options.icon,null);
      assert.equal(await commit.page.locator('.commit-count').isVisible(),false);
      check(`${theme} Changes 实际选图与 Project 相同，分组和文件行均为24px，Amend 与历史入口分层`,true);
      for (const size of [12,20,26,40]) {
        await commit.page.evaluate(size => applyTypography({uiSize:size}),size);
        const layout=await commit.page.locator('.commit-options').evaluate(node=>({
          amendRight:node.querySelector('.commit-amend').getBoundingClientRect().right,
          amendBottom:node.querySelector('.commit-amend').getBoundingClientRect().bottom,
          historyLeft:node.querySelector('.commit-last').getBoundingClientRect().left,
          historyTop:node.querySelector('.commit-last').getBoundingClientRect().top,
          countVisible:node.querySelector('.commit-count').checkVisibility(),
          groupCount:[...document.querySelectorAll('.changes-list .check-group-row .commit-meta')].some(count=>count.checkVisibility()&&/^\d+ files$/.test(count.textContent.trim())),
        }));
        assert.ok(layout.amendRight<=layout.historyLeft || layout.amendBottom<=layout.historyTop,JSON.stringify({size,...layout}));
        assert.equal(layout.countVisible,false);assert.equal(layout.groupCount,true);
      }
      const fits = await commit.page.locator('.changes-list .check-row').evaluateAll(nodes=>nodes.every(node=>node.getBoundingClientRect().height>=parseFloat(getComputedStyle(node).fontSize)));
      assert.equal(fits,true);
      check(`${theme} Changes 12/20/26/40px 历史入口与 Amend 不重叠、数量只显示在分组，40px 不裁切字形`,true);
      await commit.page.evaluate(()=>applyTypography({uiSize:12}));
      await commit.page.locator('.rail-button[aria-label="Terminal"]').click();
      await commit.page.locator('.commit-last').click();
      await commit.page.waitForSelector('.log-list-panel .commit-row');
      const selected=await commit.page.locator('.log-list-panel .commit-row.selected').getAttribute('data-hash');
      await commit.page.locator('.commit-last').click();
      assert.equal(await commit.page.locator('.log-list-panel .commit-row.selected').getAttribute('data-hash'),selected);
      assert.equal(await commit.page.evaluate(()=>window.__augitLive.layout.bottom),'git');
      check(`${theme} 中性历史入口从终端打开 Git 日志，重复点击保留选中提交与窗口`,true);
    } finally {await commit.page.close();}

    const search = await openScene(`scene=quick-open&theme=${theme}`,initialize);
    try {
      await search.page.locator('.search-overlay .search-field').fill('all');
      await search.page.waitForSelector('.search-result');
      for(const [name,kind] of mapping) assert.equal(await search.page.locator(`.search-result[data-search-path="${name}"] .file-type-${kind}`).count(),1);
      check(`${theme} 搜索结果沿用实际文件类型，不回退为通用文档图`,true);
    } finally {await search.page.close();}

    const refs = await openScene(`scene=git-history&theme=${theme}`);
    try {
      await refs.page.evaluate(()=> {
        window.__augitLive.references={branches:[{name:'main',isCurrent:true,isFavorite:false},{name:'favorite',isFavorite:true},{name:'current-favorite',isCurrent:true,isFavorite:true},{name:'ordinary'},{name:'origin/ordinary',isRemote:true}],tags:[{name:'v1'}]};
        window.__augitLive.history.commits[0].references=['HEAD','main','origin/main','tag: v1'];
        window.__augitRenderRegions('bottomTool');
      });
      for(const [name,state] of [['main','current'],['favorite','favorite'],['current-favorite','currentFavorite'],['ordinary','branch'],['origin/ordinary','branch'],['v1','tag']]) {
        assert.equal(await refs.page.locator(`.log-ref-panel [data-ref-name="${name}"] [data-branch-icon="${state}"]`).count(),1,name);
      }
      check(`${theme} 引用树按普通/当前/收藏组合选图，远端普通分支也使用分支节点`,true);
      const defaults=await refs.page.evaluate(()=>[branchTreeIcon({name:'main'},'branch'),branchTreeIcon({name:'origin/main',isRemote:true},'remote')].every(markup=>markup.includes('data-branch-icon="favorite"')));
      assert.equal(defaults,true);
      check(`${theme} main/master 与 origin 对应分支采用源码默认收藏，显式状态优先`,true);
      const labels = refs.page.locator('.commit-row').first().locator('.log-reference-label');
      assert.deepEqual(await labels.evaluateAll(nodes=>nodes.map(node=>node.dataset.logReference)),['main','origin/main','tag: v1']);
      assert.equal(await labels.first().locator('svg').getAttribute('data-reference-layers'),'head local');
      assert.equal(await labels.nth(1).locator('svg').getAttribute('data-reference-layers'),'remote');
      check(`${theme} 日志保留每个引用名称和颜色标记，HEAD 与首个分支以多层标记表达`,true);
      await refs.page.locator('.commit-row').first().click({button:'right'});
      await refs.page.locator('[data-log-action="new-tag"]').click();
      const title=await refs.page.locator('[data-compact-dialog]').getAttribute('aria-label');
      assert.ok(title.startsWith('Create New Tag On '),title);
      assert.equal(await refs.page.locator('[data-compact-dialog] label').innerText(),'Enter the name of new tag');
      check(`${theme} 新建标签通过真实日志动作采用权威标题与字段文案`,true);
    }finally{await refs.page.close();}
  }

  const directory = await openScene('scene=main-project&theme=light',()=> {
    const directories=['.git','.tmp','.workbuddy','artifacts','obj','TestResults','node_modules'];
    window.__treeOverrides={'':directories.map(name=>({name,path:name,isDirectory:true,canExpand:true})),
      obj:Array.from({length:257},(_,index)=>({name:`item-${index}.txt`,path:`obj/item-${index}.txt`,isDirectory:false,canExpand:false}))};
  });
  try {
    for(const name of ['.tmp','.workbuddy','artifacts','obj','TestResults','node_modules'])assert.equal(await directory.page.locator(`[data-tree-path="${name}"]`).count(),1);
    assert.equal(await directory.page.locator('[data-tree-path=".git"]').count(),0);
    await directory.page.locator('[data-tree-path="obj"] .chevron').click();
    await directory.page.waitForSelector('[data-tree-path="obj/item-256.txt"]');
    assert.equal(await directory.page.locator('[data-tree-path^="obj/"]').count(),257);
    await directory.page.keyboard.press('F5');
    await directory.page.waitForSelector('[data-tree-path="obj/item-256.txt"]');
    assert.equal(await directory.page.locator('[data-tree-path^="obj/"]').count(),257);
    check('点目录和构建目录可浏览，257 项目录从展开到 F5 均完整显示并分帧应用',true);
  }finally{await directory.page.close();}

  const results = await openScene('scene=repository-search&theme=light',()=> {
    const original=window.__hostStub;
    window.__limitDocs={'hits.txt':{path:'hits.txt',name:'hits.txt',kind:'Text',status:'TextReady',text:Array.from({length:200},(_,index)=>'line '+(index+1)).join('\n'),encoding:'UTF-8',lineEndings:'LF'}};
    window.__hostStub=(method,params)=>method==='search/text'?{matches:[{path:'hits.txt',name:'hits.txt',directory:'',line:40,text:'hit'},{path:'hits.txt',name:'hits.txt',directory:'',line:150,text:'other hit'}]}:original(method,params);
  });
  try {
    await results.page.locator('.search-overlay .search-field').fill('hit');await results.page.waitForSelector('.search-result[data-search-line="40"]');
    await results.page.locator('.search-result[data-search-line="40"]').dblclick();
    await results.page.waitForSelector('.code-line.active[data-line="40"]');
    const first=await results.page.locator('.code-view').evaluate(node=>node.scrollTop);
    await results.page.locator('.search-result[data-search-line="150"]').focus();await results.page.keyboard.press('Enter');
    await results.page.waitForSelector('.code-line.active[data-line="150"]');
    assert.ok(await results.page.locator('.code-view').evaluate(node=>node.scrollTop)>first);
    assert.equal(await results.page.evaluate(()=>window.__augitLive.tabs.filter(tab=>tab.path==='hits.txt').length),1);
    check('全仓搜索双击和 Enter 定位原文命中行，同文件新命中重定位且复用正式标签',true);
  }finally{await results.page.close();}

  for (const [kind, path, mode] of [['Markdown', 'hits.md', 'markdown'], ['Json', 'hits.json', 'json']]) {
    const preview = await openScene('scene=repository-search&theme=light', () => {});
    try {
      await preview.page.evaluate(({kind,path}) => {
        const text = kind === 'Markdown'
          ? Array.from({length:160},(_,index)=>`## heading ${index+1}`).join('\n')
          : JSON.stringify(Array.from({length:160},(_,index)=>`value ${index+1}`),null,2);
        window.__limitDocs = {[path]: {path,name:path,kind,status:'TextReady',text,
          formatted:kind==='Json'?JSON.stringify(JSON.parse(text)):null,encoding:'UTF-8',lineEndings:'LF',fileSize:text.length}};
        const original = window.__hostStub;
        window.__hostStub = (method,params) => method==='search/text'
          ? {matches:[{path,name:path,directory:'',line:120,text:'hit'}]} : original(method,params);
      }, {kind,path});
      await preview.page.evaluate(path=>window.__augitOpenDocument(path),path);
      await preview.page.locator(`button[data-${mode}-mode="${kind==='Markdown'?'preview':'formatted'}"]`).click();
      await preview.page.locator('.search-overlay .search-field').fill('hit');
      const row=preview.page.locator('.search-result[data-search-line="120"]');
      await row.waitFor();await row.focus();await preview.page.keyboard.press('Enter');
      await preview.page.waitForSelector('.code-line.active[data-line="120"]');
      assert.equal(await preview.page.locator(`button[data-${mode}-mode="source"]`).getAttribute('aria-pressed'),'true');
      assert.equal(await preview.page.locator('.code-view').isVisible(),true);
      assert.ok(await preview.page.locator('.code-view').evaluate(node=>node.scrollTop)>0);
      assert.equal(await preview.page.locator('.search-overlay').isVisible(),true);
      assert.equal(await preview.page.evaluate(()=>!!document.activeElement.closest('.search-overlay')),true);
      check(`${kind} 搜索命中从预览切回磁盘原文并定位，保持搜索浮层和焦点`,true);
      assert.deepEqual(preview.errors,[]);
    }finally{await preview.page.close();}
  }
};
