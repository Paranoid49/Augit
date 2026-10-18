// 复原整改的实时路径：恢复参考外观必须可取消，应用必须保存四个独立字体值，
// 语言、工具轨、主题与提交图的修改不得改动用户正文或请求额外的文件读取。
const assert = require('node:assert/strict');

module.exports = async function verifyRestoration({ openScene, context, check }) {
  await context.setDefaultTimeout(10000);
  const { page, errors } = await openScene('scene=main-project&theme=dark&open=docs/notes.txt');
  try {
    await page.waitForFunction('window.__augitSettingsReady === true');
    const baseline = await page.evaluate(() => ({
      document: window.__augitLive.document?.text,
      settings: JSON.stringify(window.__augitLive.settings),
    }));
    assert.equal(typeof baseline.document, 'string', '正文保留断言必须使用真实文本。');
    await page.locator('.titlebar [aria-label="Settings"]').click();
    await page.waitForSelector('.settings-window [data-restore-reference]');
    const order = await page.locator('.settings-window [data-settings-action]').evaluateAll((nodes) => nodes.map((node) => node.dataset.settingsAction));
    assert.deepEqual(order, ['save', 'cancel', 'apply']);
    check('设置底栏按 Windows 参考顺序 OK / Cancel / Apply', true);
    await page.locator('[data-restore-reference]').click();
    await page.waitForFunction(() => window.__augitLive.settingsDraft.monospaceFontFamily);
    const draft = await page.evaluate(() => ({
      draft: window.__augitLive.settingsDraft,
      settings: JSON.stringify(window.__augitLive.settings),
      written: window.__settingsWritten,
    }));
    assert.equal(draft.draft.fontSize, 12);
    assert.equal(draft.draft.codeFontSize, 13);
    assert.equal(draft.draft.textFontFamily, 'Microsoft YaHei UI');
    // 系统枚举没有JetBrains Mono，随包字体加载后仍可恢复参考外观。
    assert.equal(draft.draft.monospaceFontFamily, 'JetBrains Mono');
    assert.equal(draft.settings, baseline.settings);
    check('恢复参考外观只改草稿，界面与等宽四个设置值独立', true);
    await page.locator('[data-settings-action="cancel"]').click();
    assert.equal(await page.evaluate(() => JSON.stringify(window.__augitLive.settings)), baseline.settings);
    check('取消恢复操作保留原设置', true);

    await page.locator('.titlebar [aria-label="Settings"]').click();
    await page.locator('[data-restore-reference]').click();
    await page.waitForFunction(() => window.__augitLive.settingsDraft.monospaceFontFamily);
    await page.locator('[data-settings-action="apply"]').click();
    await page.waitForFunction('window.__augitLive.settings.fontSize === 12');
    const saved = await page.evaluate(() => ({
      values: window.__settingsWritten,
      bodySize: getComputedStyle(document.body).fontSize,
      family: getComputedStyle(document.body).fontFamily,
      document: window.__augitLive.document?.text,
      applyDisabled: document.querySelector('[data-settings-action="apply"]').disabled,
    }));
    assert.equal(saved.values.fontSize, 12);
    assert.equal(saved.values.codeFontSize, 13);
    assert.equal(saved.bodySize, '12px');
    assert.ok(saved.family.includes('Microsoft YaHei UI'));
    assert.equal(saved.document, baseline.document);
    assert.equal(saved.applyDisabled, true);
    check('应用恢复设置立即生效，保留当前正文，并禁用无改动的 Apply', true);
    await page.locator('[data-settings-action="cancel"]').click();

    const rail = await page.locator('.tool-rail .rail-button').first().evaluate((node) => {
      const rect = node.getBoundingClientRect(), background = getComputedStyle(node, '::before');
      return { width: rect.width, height: rect.height, inset: [background.top, background.right, background.bottom, background.left],
        radius: background.borderRadius, icon: node.querySelector('svg').getBoundingClientRect().width };
    });
    assert.deepEqual(rail, { width: 37, height: 40, inset: ['5px', '2px', '5px', '5px'], radius: '6px', icon: 20 });
    check('工具轨区分容器、背景绘图区与 20px 图标', true);

    const row = page.locator('.side-content.tree .tree-row').nth(1);
    const rowBefore = await row.evaluate((node) => getComputedStyle(node).backgroundColor);
    await row.hover();
    assert.equal(await row.evaluate((node) => getComputedStyle(node).backgroundColor), rowBefore);
    check('项目树悬停与参考一致，不另画行底色', true);

    const graph = await page.evaluate(() => window.__augitBuildCommitGraph([
      { hash: 'a', fullHash: 'full-a', references: ['main'], parents: ['full-b'] },
      { hash: 'b', fullHash: 'full-b', references: [], parents: [] },
    ]));
    assert.equal(graph.columns, 1);
    assert.equal(graph.rows[0].color, 3343801);
    assert.equal(graph.rows[1].color, 3343801);
    check('提交图解析完整父哈希并沿第一父链继承引用名颜色', true);
    const filtered = await page.evaluate(() => {
      const context = { cacheKey: 'restoration-colors' };
      const full = window.__augitBuildCommitGraph([
        { hash: 'a', references: ['main'], parents: ['b'] },
        { hash: 'b', references: [], parents: ['c'] },
      ], context);
      const selected = window.__augitBuildCommitGraph([{ hash: 'b', references: [], parents: ['c'] }], context);
      const next = window.__augitBuildCommitGraph([{ hash: 'c', references: [], parents: [] }], context);
      return { full: full.rows[1].color, selected: selected.rows[0].color, next: next.rows[0].color };
    });
    assert.equal(filtered.selected, filtered.full);
    assert.equal(filtered.next, filtered.full);
    check('已加载提交与页边界第一父在筛选和分页后保留颜色关联', true);

    for (const size of [12, 14, 40]) {
      await page.setViewportSize({ width: 1024, height: 640 });
      await page.evaluate(async (size) => applyTypography({ uiSize: size, uiFamily: size === 14 ? 'Segoe UI' : 'Microsoft YaHei UI' }), size);
      await page.locator('.titlebar [aria-label="Settings"]').click();
      for (const id of ['appearance', 'file-view', 'git', 'terminal']) {
        await page.locator(`[data-settings-page="${id}"]`).click();
        const geometry = await page.locator('.settings-page').evaluate((node) => ({
          overflow: node.scrollWidth - node.clientWidth,
          clipped: [...node.querySelectorAll('input,select')].some((field) => field.clientHeight < parseFloat(getComputedStyle(field).fontSize)),
        }));
        assert.ok(geometry.overflow <= 1, `${size}px / ${id} 设置页横向溢出`);
        assert.equal(geometry.clipped, false, `${size}px / ${id} 设置控件裁切`);
        check(`${size}px / ${id} 设置页说明换行且字段完整`, true);
      }
      await page.locator('[data-settings-action="cancel"]').click();
    }
    await page.setViewportSize({ width: 1646, height: 981 });
    await page.evaluate(() => applyTypography({ uiSize: 12 }));
    const query = '真实提交';
    await page.locator('.history-filters [aria-label="Text or hash"]').fill(query);
    await page.locator('.history-filters [aria-label="Text or hash"]').press('Enter');
    await page.waitForFunction((query) => window.__augitLive.historyFilter?.message === query, query);
    await page.locator('[data-history-text-menu]').click();
    assert.equal(await page.getByRole('menuitem', { name: query, exact: true }).count(), 1);
    await page.getByRole('menuitem', { name: query, exact: true }).click();
    assert.equal(await page.locator('.history-filters [aria-label="Text or hash"]').inputValue(), query);
    check('日志搜索历史下拉选择应用真实文本筛选并保留用户查询原文', true);
    await page.reload();
    await page.waitForFunction('window.__augitReady && window.__augitHistoryReady && window.__augitSettingsReady');
    await page.locator('[data-history-text-menu]').click();
    assert.equal(await page.getByRole('menuitem', { name: query, exact: true }).count(), 1);
    check('日志搜索历史在页面重载后恢复，使用既有本地配置目录', true);
    assert.deepEqual(errors, []);
    check('整改后的实时页面没有脚本异常', true);
  } finally { await page.close(); }
};
