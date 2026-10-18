// 复核持久化配色、双状态、工具轨状态组合和中文内容几何；服务器及浏览器由调用套件清理。
const assert = require('node:assert/strict');

module.exports = async function verifyReferenceSurfaces({ openScene, check }) {
  const { page, errors } = await openScene('scene=commit-changes&theme=light&open=docs/notes.txt');
  try {
    await page.waitForFunction('window.__augitSettingsReady');
    const colors = await page.evaluate(() => {
      const live = window.__augitLive, original = live.rootPath;
      const stored = localStorage.getItem('augit.project.colors');
      try {
        localStorage.removeItem('augit.project.colors');
        live.rootPath = 'D:\\演示\\工程'; const first = projectColorIndex();
        live.rootPath = 'd:/演示/工程/'; const same = projectColorIndex();
        live.rootPath = 'D:/second'; const next = projectColorIndex();
        live.rootPath = original; return { first, same, next };
      } finally {
        live.rootPath = original;
        if (stored === null) localStorage.removeItem('augit.project.colors');
        else localStorage.setItem('augit.project.colors', stored);
      }
    });
    assert.deepEqual(colors, { first: 4, same: 4, next: 5 });
    check('项目颜色按持久索引分配，Windows 路径大小写和分隔符不改变颜色', true);
    const savedColor = await page.locator('.titlebar').getAttribute('data-project-color');
    await page.reload();
    await page.waitForFunction('window.__augitReady && window.__augitSettingsReady && window.__augitHistoryReady');
    assert.equal(await page.locator('.titlebar').getAttribute('data-project-color'), savedColor);
    check('重新载入工作区后恢复已经分配的项目颜色', true);
    const gradient = await page.locator('.titlebar').evaluate(node => ({
      image: getComputedStyle(node).backgroundImage,
      color: getComputedStyle(node).getPropertyValue('--augit-title-glow').trim(),
    }));
    assert.notEqual(gradient.image, 'none', JSON.stringify(gradient));
    check('真实标题栏计算样式绘制项目渐变', true);
    const scrollbar = await page.evaluate(() => {
      const defaults = getComputedStyle(document.body);
      const tabs = document.querySelector('.editor-tabs');
      return { ordinary: defaults.getPropertyValue('--augit-scrollbar-thumb').trim(),
        hover: defaults.getPropertyValue('--augit-scrollbar-thumb-hover').trim(),
        tab: getComputedStyle(tabs, '::-webkit-scrollbar-thumb').backgroundColor };
    });
    assert.equal(scrollbar.ordinary, '#73737333');
    assert.equal(scrollbar.hover, '#73737347');
    assert.equal(scrollbar.tab, 'rgb(171, 171, 171)');
    check('普通区域与标签栏使用独立滑块颜色', true);

    const states = await page.evaluate(() => ['MM', 'AM', 'D?', ' M'].map((value) => {
      const file = { path: '中文文件.txt', name: '中文文件.txt', directory: '', kind: 'Modified', checked: false,
        indexStatus: value[0], workTreeStatus: value[1] };
      const template = document.createElement('template'); template.innerHTML = liveChangeFileRow(file, 'Changes', '');
      const row = template.content.firstElementChild;
      return { text: gitStatusDescription(file), aria: row.getAttribute('aria-label'), title: row.querySelector('.tree-name').title };
    }));
    assert.deepEqual(states.map(item => item.text), [
      'Index: Modified; Working tree: Modified', 'Index: Added; Working tree: Modified',
      'Index: Deleted; Working tree: Unversioned', 'Index: Unchanged; Working tree: Modified',
    ]);
    assert.ok(states.every(item => item.aria.includes(item.text) && item.title === item.text));
    check('MM、AM、删除后同路径未跟踪和仅工作区修改都提供双状态说明', true);

    for (const theme of ['light', 'dark']) {
      await page.evaluate((theme) => {
        document.body.dataset.theme = theme;
        const fixture = document.createElement('a'); fixture.id = 'rail-state-fixture'; fixture.href = '#';
        fixture.className = 'rail-button'; fixture.innerHTML = icon('folder', true);
        fixture.addEventListener('click', event => event.preventDefault());
        document.querySelector('.tool-rail').append(fixture);
      }, theme);
      const button = page.locator('#rail-state-fixture');
      const color = () => button.evaluate(node => getComputedStyle(node, '::before').backgroundColor);
      const token = name => page.evaluate((name) => {
        const probe = document.createElement('i'); probe.style.color = `var(${name})`; document.body.append(probe);
        const value = getComputedStyle(probe).color; probe.remove(); return value;
      }, name);
      await button.hover();
      assert.equal(await color(), await token('--augit-action-button-hover'));
      await page.mouse.down();
      assert.equal(await color(), await token('--augit-pressed'));
      await page.mouse.up();
      for (const classes of ['visible', 'visible active']) {
        await button.evaluate((node, classes) => node.className = 'rail-button ' + classes, classes);
        await button.hover(); await page.mouse.down();
        assert.equal(await color(), await token(classes.includes('active') ? '--augit-accent-brand' : '--augit-pressed'));
        await page.mouse.up();
      }
      await button.evaluate(node => { node.setAttribute('aria-disabled', 'true'); node.focus(); });
      assert.equal(await color(), 'rgba(0, 0, 0, 0)');
      assert.equal(await button.evaluate(node => getComputedStyle(node, '::after').content), 'none');
      await button.evaluate(node => node.remove());
      check(`${theme} 工具轨悬停、按下、可见、激活和禁用组合保持状态优先级`, true);
    }
    await page.evaluate(() => applyTypography({ uiSize: 14, uiFamily: 'Segoe UI' }));
    const gaps = await page.locator('.changes-list .change-file-row').evaluateAll(rows => rows.map(row => {
      const icon = row.querySelector('.file-icon').getBoundingClientRect();
      const name = row.querySelector('.tree-name').getBoundingClientRect(); return name.x - icon.right;
    }));
    assert.ok(gaps.length && gaps.every(gap => Math.abs(gap - 2) < .1));
    check('旧 Segoe UI 14px 配置下 Changes 图标与文件名间距为 2px', true);
    await page.keyboard.press('Control+f');
    await page.waitForSelector('.current-find [aria-label="Find in File"]');
    assert.equal(await page.locator('.current-find [aria-label="Close Find"]').count(), 1);
    await page.locator('.current-find [aria-label="Close Find"]').click();
    check('动态当前文件查找使用英文动作名称且关闭入口接线正常', true);
    await page.locator('.top-button[aria-label="Main Menu"]').click();
    await page.locator('.main-menu-bar .main-menu-entry', { hasText: 'Git' }).click();
    await page.locator('[data-main-menu-action="stash-manager"]').click();
    await page.waitForSelector('.stash-manager-dialog');
    check('英文 Git 菜单通过稳定动作标识打开 Stash 管理，避免与创建入口混淆', true);
    await page.keyboard.press('Escape');
    await page.evaluate(() => window.__augitOpenRemoteManager());
    await page.waitForSelector('.remote-window [data-remote-entry]');
    assert.equal(await page.locator('.remote-window [data-remote-entry] svg').count(), 0);
    assert.ok((await page.locator('.remote-window [data-remote-entry]').allTextContents()).some(text => text.includes('origin')));
    check('远端行遵循参考文字呈现，不插入额外云图标且保留对象名称', true);
    assert.deepEqual(errors, []);
  } finally { await page.close(); }
};
