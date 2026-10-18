// 本轮审查的交错场景；复用真实桥接夹具，所有页面由本模块关闭，服务和浏览器由主套件关闭。
const assert = require('node:assert/strict');

module.exports = async function verifyReview({ openScene, check }) {
  const treeRow = (page, path) => page.locator(`.side-content.tree [data-tree-path="${path}"]`);
  const root = await openScene('scene=main-project&theme=light&open=docs/notes.txt');
  try {
    await treeRow(root.page, '').locator('.tree-name').click();
    const frame = await root.page.evaluate(() => ({
      side: document.querySelector('.side-tool').getBoundingClientRect().x,
      rail: document.querySelector('.tool-rail').getBoundingClientRect().width,
      row: document.querySelector('.root-row').getBoundingClientRect().height,
    }));
    assert.equal(frame.side, 41); assert.equal(frame.rail, 37); assert.equal(frame.row, 24);
    check('12px 基线项目树行高 24、工具轨列宽 37、项目表面从 x=41 开始', true);
    await root.page.evaluate(() => window.__augitRenderRegions('side'));
    assert.equal(await treeRow(root.page, '').getAttribute('aria-selected'), 'true');
    await root.page.keyboard.press('F5');
    await root.page.waitForFunction(() => document.querySelector('.root-row')?.getAttribute('aria-selected') === 'true');
    assert.equal(await root.page.evaluate(() => window.__augitLive.treeSelectedPath), '');
    check('根目录选择在区域重建和 F5 增量刷新后保持，空字符串不回落到活动文档', true);
    await treeRow(root.page, '').locator('.chevron').click();
    await root.page.waitForFunction(() => document.querySelector('.root-row')?.getAttribute('aria-expanded') === 'false');
    assert.equal(await root.page.locator('.side-content.tree .tree-row').count(), 1);
    await root.page.keyboard.press('F5');
    assert.equal(await treeRow(root.page, '').getAttribute('aria-expanded'), 'false');
    check('根目录折叠的 ARIA 与可见子行一致，F5 不重新展开', true);
    assert.deepEqual(root.errors, []);
  } finally { await root.page.close(); }

  const slow = await openScene('scene=main-project&theme=light');
  try {
    await slow.page.evaluate(() => {
      const original = window.__hostStub;
      window.__reviewDirectoryCalls = 0;
      window.__hostStub = async (method, params) => {
        if (method !== 'workspace/list' || params.path !== 'docs') return original(method, params);
        const call = ++window.__reviewDirectoryCalls;
        if (call === 1) await new Promise(resolve => { window.__releaseOldDirectory = resolve; });
        return { entries: [{ name: call === 1 ? 'stale.txt' : 'latest.txt', path: 'docs/' + (call === 1 ? 'stale.txt' : 'latest.txt'), isDirectory: false }] };
      };
    });
    await treeRow(slow.page, 'docs').locator('.chevron').click();
    await slow.page.waitForFunction(() => window.__reviewDirectoryCalls === 1);
    await slow.page.keyboard.press('F5');
    await slow.page.waitForFunction(() => window.__augitLive.tree.some(row => row.path === 'docs/latest.txt'));
    await slow.page.evaluate(() => window.__releaseOldDirectory());
    await slow.page.waitForFunction(() => document.querySelector('[data-tree-path="docs/latest.txt"]'));
    await treeRow(slow.page, 'docs').locator('.chevron').click();
    await treeRow(slow.page, 'docs').locator('.chevron').click();
    assert.equal(await treeRow(slow.page, 'docs/stale.txt').count(), 0);
    assert.equal(await treeRow(slow.page, 'docs/latest.txt').count(), 1);
    check('旧展开响应晚于 F5 返回时不覆盖新缓存，折叠再展开仍只显示最新目录', true);
    assert.deepEqual(slow.errors, []);
  } finally { await slow.page.close(); }

  const restored = await openScene('scene=main-project&theme=light', () => {
    window.__settingsOverride = { openFiles: [], expandedDirectories: ['docs', 'docs/api'] };
    const original = window.__hostStub;
    window.__restoreDirectoryCalls = [];
    window.__hostStub = async (method, params) => {
      if (method === 'workspace/list' && params.path === 'docs') {
        window.__restoreDirectoryCalls.push(params.path);
        await new Promise(resolve => { window.__releaseRestoreDirectory = resolve; });
      } else if (method === 'workspace/list' && params.path === 'docs/api') window.__restoreDirectoryCalls.push(params.path);
      return original(method, params);
    };
  });
  try {
    await restored.page.waitForFunction(() => !!window.__releaseRestoreDirectory);
    assert.equal(await treeRow(restored.page, 'docs').getAttribute('aria-expanded'), 'true');
    await treeRow(restored.page, 'docs').locator('.chevron').click();
    await restored.page.evaluate(() => window.__releaseRestoreDirectory());
    await restored.page.waitForFunction(() => document.querySelector('[data-tree-path="docs"]')?.getAttribute('aria-expanded') === 'false');
    assert.deepEqual(await restored.page.evaluate(() => window.__restoreDirectoryCalls), ['docs']);
    check('无标签时也恢复目录；用户折叠使旧恢复任务停止，不再恢复子目录', true);
    assert.deepEqual(restored.errors, []);
  } finally { await restored.page.close(); }

  const batch = await openScene('scene=main-project&theme=light', () => {
    window.__treeOverrides = { '': Array.from({ length: 150 }, (_, index) => ({
      name: `file-${String(index).padStart(3, '0')}.txt`, path: `file-${String(index).padStart(3, '0')}.txt`, isDirectory: false,
    })) };
  });
  try {
    await batch.page.evaluate(() => {
      window.__reviewRaf = requestAnimationFrame;
      window.__reviewFrames = [];
      window.requestAnimationFrame = callback => { window.__reviewFrames.push(callback); return window.__reviewFrames.length; };
      window.__treeOverrides[''] = window.__treeOverrides[''].map(row => ({ ...row, name: 'updated-' + row.name }));
    });
    await batch.page.keyboard.press('F5');
    await batch.page.waitForFunction(() => document.querySelector('[data-tree-path="file-000.txt"] .tree-name')?.textContent.startsWith('updated-'));
    await batch.page.evaluate(() => {
      const target = document.querySelector('[data-tree-path="file-140.txt"]');
      target.querySelector('.tree-name').click(); target.focus();
      const callbacks = window.__reviewFrames.splice(0);
      for (let count = 0; callbacks.length && count < 30; count++) {
        callbacks.shift()(performance.now()); callbacks.push(...window.__reviewFrames.splice(0));
      }
      window.requestAnimationFrame = window.__reviewRaf;
    });
    const state = await batch.page.evaluate(() => ({
      focused: document.activeElement.dataset.treePath,
      selected: [...document.querySelectorAll('.side-content.tree .tree-row.selected')].map(row => row.dataset.treePath),
      name: document.querySelector('[data-tree-path="file-140.txt"] .tree-name').textContent,
    }));
    assert.deepEqual(state.selected, ['file-140.txt']);
    assert.equal(state.focused, 'file-140.txt'); assert.equal(state.name, 'updated-file-140.txt');
    check('超过 60 行的后续帧重建保留用户刚选择的行和焦点', true);
    assert.deepEqual(batch.errors, []);
  } finally { await batch.page.close(); }

  for (const scene of ['text-viewer', 'commit-diff']) {
    const opened = await openScene(scene === 'commit-diff' ? 'scene=commit-diff&theme=dark&diff=src%2FApp.cs' : 'scene=text-viewer&theme=dark&open=docs/notes.txt');
    const page = opened.page;
    try {
      if (scene === 'commit-diff') {
        await page.waitForSelector('.diff-columns');
      }
      await page.locator('.editor-content').click({ position: { x: 10, y: 90 } });
      await page.keyboard.press('Control+f');
      assert.equal(await page.locator('.editor-content .current-find').count(), 1, JSON.stringify(await page.evaluate(() => ({
        scene: document.body.dataset.scene, editor: window.__augitLive.editor,
        active: document.activeElement?.outerHTML.slice(0, 140), error: window.__augitError,
        overlays: [...document.querySelectorAll('[data-augit-overlay], .dialog')].map(node => node.className),
        find: [...document.querySelectorAll('.editor-content [aria-label="Find in File"], .editor-content [aria-label="Find"]')].map(node => node.outerHTML.slice(0, 200)),
      }))));
      await page.locator('.current-find [aria-label="Close Find"]').click();
      for (const action of ['settings', 'quick']) {
        if (action === 'settings') await page.locator('.titlebar [aria-label="Settings"]').click();
        else await page.keyboard.press('Control+p');
        const input = action === 'settings' ? page.locator('.settings-window input').first() : page.locator('.search-overlay input').first();
        await input.focus();
        const before = await input.evaluate(node => document.activeElement === node);
        await page.keyboard.press('Control+f');
        assert.equal(before, true); assert.equal(await input.evaluate(node => document.activeElement === node), true);
        assert.equal(await page.locator('.editor-content .current-find').count(), 0);
        if (action === 'settings') await page.locator('[data-settings-action="cancel"]').click();
        else await page.keyboard.press('Escape');
      }
      check(`${scene} Ctrl+F 不越过设置与快速打开抢后台正文焦点`, true);
      const blocked = await page.evaluate(() => {
        const field = document.createElement('textarea'); document.querySelector('.side-tool').append(field); field.focus();
        field.dispatchEvent(new KeyboardEvent('keydown', { key: 'f', ctrlKey: true, bubbles: true, cancelable: true }));
        const kept = document.activeElement === field && !document.querySelector('.editor-content .current-find');
        field.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true }));
        const code = document.querySelector('.diff-columns, .code-view'); code.focus();
        code.dispatchEvent(new KeyboardEvent('keydown', { key: 'f', ctrlKey: true, bubbles: true, cancelable: true }));
        const ime = !document.querySelector('.editor-content .current-find');
        field.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true })); field.remove();
        const consumed = new KeyboardEvent('keydown', { key: 'f', ctrlKey: true, bubbles: true, cancelable: true });
        consumed.preventDefault(); code.dispatchEvent(consumed);
        return { kept, ime, consumed: !document.querySelector('.editor-content .current-find') };
      });
      assert.deepEqual(blocked, { kept: true, ime: true, consumed: true });
      check(`${scene} Ctrl+F 尊重其它输入区、输入法会话和已消费事件`, true);
      assert.deepEqual(opened.errors, []);
    } finally { await page.close(); }
  }

  const failure = await openScene('scene=settings-save-failure&theme=dark');
  try {
    const actions = await failure.page.evaluate(() => {
      const live = window.__augitLive;
      try {
        window.__augitLive = null;
        const template = document.createElement('template'); template.innerHTML = renderScene();
        return [...template.content.querySelectorAll('[data-settings-action]')].map(node => node.dataset.settingsAction);
      } finally { window.__augitLive = live; }
    });
    assert.deepEqual(actions, ['save', 'cancel', 'apply']);
    const colors = await failure.page.evaluate(() => {
      const style = getComputedStyle(document.body);
      return ['--augit-scrollbar-thumb', '--augit-scrollbar-thumb-hover'].map(name => style.getPropertyValue(name).trim());
    });
    assert.deepEqual(colors, ['#80808059', '#8080808c']);
    check('静态设置失败保留 OK/Cancel/Apply，深色滑块采用 Many Islands Dark 的覆盖值', true);
  } finally { await failure.page.close(); }

  const quick = await openScene('scene=quick-open&theme=light');
  try {
    await quick.page.evaluate(() => {
      const original = window.__hostStub;
      window.__hostStub = (method, params) => method === 'search/files'
        ? { matches: [{ path: params.query === 'notes' ? 'docs/notes.txt' : 'docs/product-spec.md', name: params.query === 'notes' ? 'notes.txt' : 'product-spec.md' }] }
        : original(method, params);
    });
    const field = quick.page.locator('.search-overlay .search-field');
    await field.fill('notes');
    await quick.page.waitForSelector('.search-result');
    await field.press('Enter');
    await quick.page.waitForFunction(() => window.__augitLive.tabs?.some(tab => tab.path === 'docs/notes.txt'));
    assert.equal(await quick.page.evaluate(() => window.__augitLive.tabs.find(tab => tab.path === 'docs/notes.txt').preview), false);
    await field.fill('product');
    await quick.page.waitForSelector('.search-result[data-search-path="docs/product-spec.md"]');
    await quick.page.locator('.search-result[data-search-path="docs/product-spec.md"]').focus();
    await quick.page.keyboard.press('Enter');
    await quick.page.waitForFunction(() => window.__augitLive.tabs?.some(tab => tab.path === 'docs/product-spec.md'));
    assert.equal(await quick.page.evaluate(() => window.__augitLive.tabs.find(tab => tab.path === 'docs/product-spec.md').preview), false);
    assert.equal(await quick.page.evaluate(() => window.__augitLive.tabs.filter(tab => tab.kind === 'document').length), 2);
    check('快速打开输入框和结果行 Enter 均正式打开，下一结果不替换已确认标签', true);
  } finally { await quick.page.close(); }

  for (const theme of ['light', 'dark']) for (const size of [13, 40]) {
    const conflict = await openScene(`scene=conflict-resolver&theme=${theme}&code-font-size=${size}`);
    try {
      const body = await conflict.page.evaluate(() => [...document.querySelectorAll('.conflict-block')].map(node => {
        const style = getComputedStyle(node), canvas = document.createElement('canvas').getContext('2d');
        canvas.font = `${style.fontSize} ${style.fontFamily}`;
        const glyph = canvas.measureText('Ag'), height = glyph.fontBoundingBoxAscent + glyph.fontBoundingBoxDescent;
        return { font: parseFloat(style.fontSize), line: parseFloat(style.lineHeight),
          expected: Math.ceil(Math.ceil(height * devicePixelRatio) * 1.2) / devicePixelRatio,
          editable: node.isContentEditable };
      }));
      assert.equal(body.length, 3); assert.deepEqual(body.map(item => item.editable), [false, true, false]);
      assert.ok(body.every(item => item.font === size && Math.abs(item.line - item.expected) < .05), JSON.stringify(body));
      check(`${theme}/${size}px 三栏正文按字体实际度量×1.2 取整，唯一结果区可编辑`, true);
    } finally { await conflict.page.close(); }
  }
};
