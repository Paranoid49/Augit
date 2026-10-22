// 字体恢复、Markdown排版与标签动作、UI图标族缩放的实际浏览器回归。
const assert = require('node:assert/strict');

module.exports = async function verifyRestorationFixes({ openScene, check }) {
  for (const available of [true, false]) {
    const scene = await openScene('scene=git-history&ui-font-size=12');
    const { page } = scene;
    try {
      // 阻断真实WOFF2请求后重载，防止系统字体已安装掩盖随包资源缺失。
      if (!available) {
        await page.route('**/vendor/jetbrains-mono/*.woff2', route => route.abort());
        await page.reload();
        await page.waitForFunction(() => window.__augitReady && window.__augitSettingsReady);
      }
      await page.evaluate(() => {
        window.__augitLive.fontFamilies = ['Consolas', 'Microsoft YaHei UI'];
      });
      await page.locator('.titlebar [aria-label="Settings"]').click();
      await page.locator('[data-restore-reference]').focus();
      await page.keyboard.press('Enter');
      await page.waitForFunction(() => window.__augitLive.settingsDraft.monospaceFontFamily);
      assert.equal(await page.evaluate(() => window.__augitLive.settingsDraft.monospaceFontFamily), available ? 'JetBrains Mono' : 'Consolas');
      if (!available) assert.match(await page.locator('.settings-window .footer-help').innerText(), /unavailable.*Consolas/);
      if (available) {
        const client = await page.context().newCDPSession(page);
        await client.send('DOM.enable');
        await client.send('CSS.enable');
        await page.evaluate(async () => { const probe = document.createElement('span'); probe.id = 'bundled-font-probe'; probe.style.cssText = 'position:fixed;left:10px;top:10px;z-index:99999;font:13px "JetBrains Mono"'; probe.textContent = 'Ag 0123456789'; document.querySelector('.augit-window').append(probe); await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); });
        const { root } = await client.send('DOM.getDocument');
        const { nodeId } = await client.send('DOM.querySelector', { nodeId: root.nodeId, selector: '#bundled-font-probe' });
        const { fonts } = await client.send('CSS.getPlatformFontsForNode', { nodeId });
        assert.ok(fonts.some(font => font.isCustomFont && font.familyName === 'JetBrains Mono' && font.glyphCount > 0), JSON.stringify(fonts));
        await client.detach();
      }
      assert.equal(await page.evaluate(() => window.__augitLive.settings.monospaceFontFamily), 'Cascadia Mono');
      await page.locator('[data-settings-action="cancel"]').click();
      assert.equal(await page.evaluate(() => window.__augitLive.settings.monospaceFontFamily), 'Cascadia Mono');
      check(`系统枚举无JetBrains Mono时${available ? '实际使用随包字体' : '资源失败显示Consolas回退'}，Cancel不持久化`, true);
    } finally { await page.close(); }
  }

  await require('./live-bundled-fonts.cjs')({ openScene, check });

  for (const theme of ['light', 'dark']) for (const size of [12, 20, 40]) {
    const scene = await openScene(`scene=markdown-preview&open=docs/product-spec.md&theme=${theme}&ui-font-size=${size}&code-font-size=17`, () => {
      const original = window.__hostStub;
      window.__hostStub = async (method, params) => {
        const result = await original(method, params);
        if (method === 'document/read' && params.path.endsWith('.md')) {
          return { ...result, text: '# One\n\n## Two\n\n### Three\n\n#### Four\n\n##### Five\n\n###### Six\n\nBody `inline`\n\n- List\n\n> Quote\n\n| A | B |\n| --- | --- |\n| X | Y |\n\n```\ncode block\n```\n\n' + Array.from({ length: 90 }, (_, index) => `Reading position ${index}\n\n`).join('') };
        }
        return result;
      };
    });
    const { page } = scene;
    try {
      await page.setViewportSize({ width: 1640, height: 985 });
      await page.waitForSelector('.markdown-preview h6');
      const metrics = await page.locator('.markdown-preview').evaluate(preview => {
        const value = node => {
          const css = getComputedStyle(node);
          return { size: parseFloat(css.fontSize), family: css.fontFamily, padding: parseFloat(css.paddingLeft), line: parseFloat(css.lineHeight) };
        };
        return { preview: value(preview), headings: [...preview.querySelectorAll('h1,h2,h3,h4,h5,h6')].map(value),
          body: value(preview.querySelector('p')), code: value(preview.querySelector('pre')), inline: value(preview.querySelector('p code')),
          table: value(preview.querySelector('table')), list: value(preview.querySelector('li')), quote: value(preview.querySelector('blockquote')) };
      });
      const ratios = [2.2, 1.8, 1.3, 1, 1, 1];
      metrics.headings.forEach((heading, index) => {
        assert.ok(Math.abs(heading.size - 13 * ratios[index]) < .01);
        assert.equal(heading.family, metrics.preview.family);
      });
      assert.equal(metrics.preview.size, 13);
      assert.ok(metrics.preview.family.includes('Helvetica'), metrics.preview.family);
      for (const key of ['body', 'list', 'table', 'quote']) assert.equal(metrics[key].size, 13);
      assert.equal(metrics.preview.padding, 26);
      assert.equal(metrics.code.size, 11.05);
      assert.equal(metrics.inline.size, 11.7);
      assert.ok(Math.abs(metrics.code.line - 11.05 * 1.45) < .01);
      assert.equal(await page.locator('.markdown-document > .document-toolbar').count(), 0);
      assert.equal(await page.locator('.editor-tabs .markdown-tab-actions button').count(), 3);
      // 预览状态下Ctrl+F仍须借标签动作切到原文，不能保留已移除的正文按钮查询。
      await page.keyboard.press('Control+f');
      await page.waitForSelector('.markdown-document > .current-find');
      assert.equal(await page.locator('.markdown-document').getAttribute('data-markdown-mode'), 'source');
      await page.keyboard.press('Escape');
      await page.locator('.editor-tabs [data-markdown-mode="preview"]').click();
      const source = page.locator('.markdown-source'), preview = page.locator('.markdown-preview');
      await preview.evaluate(node => { node.scrollTop = 250; });
      const previewTop = await preview.evaluate(node => node.scrollTop);
      await page.locator('.editor-tabs [data-markdown-mode="split"]').click();
      await source.evaluate(node => { node.scrollTop = 180; });
      const sourceTop = await source.evaluate(node => node.scrollTop);
      await page.locator('.editor-tabs [data-markdown-mode="source"]').focus();
      await page.keyboard.press('Enter');
      assert.equal(await page.locator('.markdown-document').getAttribute('data-markdown-mode'), 'source');
      await page.locator('.editor-tabs [data-markdown-mode="preview"]').click();
      assert.equal(await preview.evaluate(node => node.scrollTop), previewTop);
      await page.evaluate(() => window.__augitRenderRegions('editorTabs'));
      await page.locator('.editor-tabs [data-markdown-mode="split"]').click();
      assert.equal(await source.evaluate(node => node.scrollTop), sourceTop);
      assert.equal(await preview.evaluate(node => node.scrollTop), previewTop);
      await page.keyboard.press('Control+f');
      await page.waitForSelector('.markdown-document > .current-find');
      check(`${theme}/${size}px Markdown h1–h6、正文、代码字号归属及标签动作、局部刷新阅读位置和Ctrl+F`, true);

      await page.evaluate(() => window.__augitOpenDocument('docs/notes.txt'));
      const scale = Math.max(.75, Math.round(size / 12 * 4) / 4);
      const icons = await page.evaluate(() => {
        const box = node => { const rect = node.getBoundingClientRect(); return { width: rect.width, height: rect.height }; };
        const tree = document.querySelector('.side-content.tree .file-type-icon');
        const tab = document.querySelector('.editor-tab > .file-type-icon');
        const rail = document.querySelector('.tool-rail .rail-button > svg');
        const body = document.querySelector('.code-view');
        return { tree: box(tree), tab: box(tab), rail: box(rail), hit: box(rail.parentElement), code: getComputedStyle(body).fontSize,
          system: box(document.querySelector('.window-actions .window-dot svg')), strokeScale: tree.getScreenCTM().a, vector: getComputedStyle(tree).vectorEffect };
      });
      assert.equal(icons.tree.width, Math.round(16 * scale));
      assert.equal(icons.tab.width, Math.round(13 * scale));
      assert.equal(icons.rail.width, Math.round(20 * scale));
      assert.ok(icons.hit.width >= icons.rail.width && icons.hit.height >= icons.rail.height);
      assert.equal(icons.strokeScale, icons.tree.width / 16);
      assert.notEqual(icons.vector, 'non-scaling-stroke');
      assert.equal(icons.code, '17px');
      assert.equal(icons.system.width, 16);
      assert.deepEqual(scene.errors, []);
      check(`${theme}/${size}px 树16、标签13、工具轨20基准图形与槽位同步缩放，系统图形和等宽字号独立`, true);
    } finally { await page.close(); }
  }

  // 不能只核对SVG尺寸：大字号的图形必须完整落在自己的命中区，兄弟按钮不能重叠。
  for (const theme of ['light', 'dark']) for (const size of [13, 32, 40]) {
    for (const sceneName of ['repository-search', 'commit-changes', 'commit-diff']) {
      const scene = await openScene(`scene=${sceneName}&theme=${theme}&ui-font-size=${size}${sceneName === 'commit-diff' ? '&diff=src%2FApp.cs' : ''}`);
      const { page } = scene;
      try {
        await page.setViewportSize({ width: 1640, height: 985 });
        if (sceneName === 'repository-search') await page.waitForSelector('.search-overlay[data-layout-ready="true"]');
        else await page.waitForSelector(sceneName === 'commit-changes' ? '.commit-box' : '.diff-toolbar');
        for (const width of sceneName === 'repository-search' ? [1180, 320] : [1640]) {
          await page.setViewportSize({ width, height: 985 });
          await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
          const geometry = await page.evaluate(sceneName => {
            const selector = sceneName === 'repository-search' ? '.search-option, .search-ignored'
              : sceneName === 'commit-changes' ? '.commit-actions > *, .commit-last' : '.diff-toolbar button';
            const rect = node => { const b = node.getBoundingClientRect(); return { left:b.left, right:b.right, top:b.top, bottom:b.bottom, width:b.width, height:b.height }; };
            return [...document.querySelectorAll(selector)].filter(node => node.checkVisibility()).map(node => ({
              label:node.getAttribute('aria-label') || node.textContent.trim(), box:rect(node),
              icon:node.querySelector('svg') ? rect(node.querySelector('svg')) : null,
              parent:rect(node.parentElement),
            }));
          }, sceneName);
          for (const item of geometry) if (item.icon) {
            const { icon, box } = item;
            assert.ok(icon.left >= box.left - 1 && icon.right <= box.right + 1 && icon.top >= box.top - 1 && icon.bottom <= box.bottom + 1, JSON.stringify({ sceneName, theme, size, width, item }));
            assert.equal(Math.round(icon.width), Math.round(16 * Math.max(.75, Math.round(size / 12 * 4) / 4)));
          }
          for (let a = 0; a < geometry.length; a++) for (let b = a + 1; b < geometry.length; b++) {
            const first = geometry[a].box, second = geometry[b].box;
            assert.ok(Math.min(first.right, second.right) - Math.max(first.left, second.left) < 1 || Math.min(first.bottom, second.bottom) - Math.max(first.top, second.top) < 1,
              JSON.stringify({ sceneName, theme, size, width, first:geometry[a], second:geometry[b] }));
          }
          if (sceneName === 'repository-search') for (const { box, parent } of geometry) {
            assert.ok(box.left >= parent.left - 1 && box.right <= parent.right + 1 && box.top >= parent.top - 1 && box.bottom <= parent.bottom + 1, JSON.stringify({ size, width, box, parent }));
          }
        }
        assert.deepEqual(scene.errors, []);
        check(`${theme}/${size}px ${sceneName}图形位于命中区、按钮不重叠且窄搜索不溢出`, true);
      } finally { await page.close(); }
    }
  }
};
