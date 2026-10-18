// 本批次验证查找栏、禁用导航、归属配色/富文本提示和文件历史日期；页面由finally关闭。
const assert = require('node:assert/strict');

module.exports = async function verifyFindBlame({ openScene, check }) {
  for (const mode of ['preview', 'split', 'source']) {
    const scene = await openScene(`scene=main-project&theme=light&open=docs/product-spec.md&markdown-mode=${mode}`);
    const { page } = scene;
    try {
      await page.locator('.markdown-preview').evaluate(node => node.focus());
      await page.keyboard.press('Control+f');
      const input = page.getByRole('textbox', { name: 'Find in File', exact: true });
      await input.waitFor({ state: 'visible' });
      await input.fill('真实');
      await page.waitForFunction(() => /1\//.test(document.querySelector('.find-status')?.textContent));
      assert.equal(await page.locator('.markdown-document').getAttribute('data-markdown-mode'), mode === 'preview' ? 'source' : mode);
      await page.keyboard.press('Control+f');
      assert.equal(await page.locator('.current-find').count(), 1);
      assert.equal(await input.inputValue(), '真实');
      assert.equal(await input.evaluate(node => document.activeElement === node), true);
      await page.keyboard.press('Escape');
      assert.equal(await page.locator('.current-find').count(), 0);
      assert.deepEqual(scene.errors, []);
      check(`Markdown ${mode} 的 Ctrl+F 进入共享查找，重复只聚焦，Esc 关闭`, true);
    } finally {
      await page.close();
    }
  }
  for (const theme of ['light', 'dark']) {
    const scene = await openScene(`scene=main-project&theme=${theme}&open=docs/search-sample.txt`);
    const page = scene.page;
    try {
      await page.keyboard.press('Control+f');
      const input = page.getByRole('textbox', { name: 'Find in File' });
      const prev = page.getByRole('button', { name: 'Previous Match', exact: true });
      const next = page.getByRole('button', { name: 'Next Match', exact: true });
      await input.fill('git');
      await page.waitForFunction(() => document.querySelector('.find-status').textContent === '1/8');
      const geometry = await page.locator('.current-find').evaluate(bar => {
        const shell = bar.querySelector('.find-input-shell'), bounds = shell.getBoundingClientRect();
        return {
          options: shell.querySelectorAll('button').length,
          inside: [...shell.querySelectorAll('button')].every(button => {
            const box = button.getBoundingClientRect();
            return box.left >= bounds.left && box.right <= bounds.right && box.top >= bounds.top && box.bottom <= bounds.bottom;
          }),
          statusAfter: bar.querySelector('.find-status').getBoundingClientRect().left >= bounds.right,
          close: bar.querySelector('[aria-label="Close Find"]').getBoundingClientRect().right <= bar.getBoundingClientRect().right,
        };
      });
      assert.deepEqual(geometry, { options: 3, inside: true, statusAfter: true, close: true });
      assert.equal(await next.isDisabled(), false);
      check(`${theme} 正文开关嵌入输入外壳，计数/导航与右侧关闭入口互不覆盖`, true);
      await input.fill('missing');
      assert.equal(await prev.isDisabled(), true); assert.equal(await next.isDisabled(), true);
      await input.focus();
      for (const name of ['Match case', 'Words', 'Regex', 'Close Find', 'Find in File']) {
        await page.keyboard.press('Tab');
        assert.equal(await page.evaluate(() => document.activeElement.getAttribute('aria-label')), name);
      }
      check(`${theme} 无匹配禁用双向导航，Tab跳过禁用项`, true);
      await page.evaluate(() => { window.__augitFindResultDelay = 400; });
      await input.fill('git');
      assert.equal(await next.isDisabled(), true);
      await input.press('Enter'); await input.press('Shift+Enter');
      await page.evaluate(() => document.querySelector('.current-find [aria-label="Next Match"]').click());
      await page.waitForFunction(() => document.querySelector('.find-status').textContent === '1/8');
      assert.equal(await next.isDisabled(), false);
      await input.press('Enter');
      assert.equal(await page.locator('.find-status').textContent(), '2/8');
      check(`${theme} 搜索中禁止导航且不排队，完成后从首项正常导航`, true);
      await page.evaluate(() => { window.__augitFindResultDelay = 0; });
      await input.dispatchEvent('compositionstart');
      assert.equal(await next.isDisabled(), true);
      await input.dispatchEvent('compositionend');
      assert.equal(await next.isDisabled(), false);
      await page.evaluate(() => { window.__augitFindResultDelay = 400; });
      await page.getByRole('button', { name: 'Regex', exact: true }).click();
      assert.equal(await next.isDisabled(), true);
      await input.press('Enter'); await input.press('Shift+Enter');
      await page.waitForFunction(() => document.querySelector('.find-status').textContent === '1/8');
      assert.equal(await next.isDisabled(), false);
      check(`${theme} 正则Worker在途时不排队，完成后仍是首项`, true);
      await page.evaluate(() => { window.__augitFindResultDelay = 0; });
      await input.fill('[');
      await page.waitForFunction(() => document.querySelector('.find-status').textContent === 'Invalid regular expression');
      assert.equal(await next.isDisabled(), true);
      check(`${theme} IME组词及非法正则禁用导航`, true);
      assert.deepEqual(scene.errors, []);
    } finally { await page.close(); }

    const diff = await openScene(`scene=commit-diff&theme=${theme}&diff=src/App.cs`);
    try {
      await diff.page.locator('.editor-content .diff-toolbar [aria-label="Find"]').click();
      const input = diff.page.locator('.editor-content .current-find input');
      await input.fill('missing');
      assert.equal(await diff.page.locator('.editor-content .current-find [aria-label="Next Match"]').isDisabled(), true);
      await input.fill('line');
      assert.equal(await diff.page.locator('.editor-content .find-input-shell button').count(), 3);
      assert.equal(await diff.page.locator('.editor-content .current-find [aria-label="Next Match"]').isDisabled(), false);
      await input.dispatchEvent('compositionstart');
      assert.equal(await diff.page.locator('.editor-content .current-find [aria-label="Next Match"]').isDisabled(), true);
      await input.dispatchEvent('compositionend');
      check(`${theme} Diff共享输入外壳与导航禁用规则`, true);
      assert.deepEqual(diff.errors, []);
    } finally { await diff.page.close(); }

    const blame = await openScene(`scene=blame&theme=${theme}&blame=docs/notes.txt`, () => {
      window.__blameLines = [
        { number: 1, hash: 'aaa1111', fullHash: 'full-head-hash', author: '<script>author</script>', date: '2026/9/15', dateTime: '2026/9/15 10:00', committerTimestamp: 3000, timestamp: 1, summary: '<img src=x onerror=alert(1)>', content: '第一行' },
        { number: 2, hash: 'bbb2222', fullHash: 'full-bbb2222', author: 'Older', date: '2026/9/14', dateTime: '2026/9/14 9:00', committerTimestamp: 2000, timestamp: 99999, summary: 'Older', content: '第二行' },
        { number: 3, hash: 'ccc3333', fullHash: 'full-ccc3333', author: 'Same time', date: '2026/9/15', dateTime: '2026/9/15 10:00', committerTimestamp: 3000, timestamp: 2, summary: 'Same time', content: '第三行' },
      ];
    });
    try {
      const page = blame.page;
      const colors = await page.locator('.blame-row').evaluateAll(rows => rows.map(row => getComputedStyle(row).backgroundColor));
      assert.equal(colors[0], theme === 'light' ? 'rgb(161, 190, 247)' : 'rgb(47, 81, 148)');
      assert.equal(colors[1], theme === 'light' ? 'rgb(222, 232, 252)' : 'rgb(40, 55, 84)');
      assert.equal(colors[2], colors[0]);
      check(`${theme} 归属按提交者时间分组，同时间同色，作者时间不改变排序`, true);
      const palette = await page.evaluate(() => Array.from({ length: 21 }, (_, index) => getComputedStyle(document.body).getPropertyValue(`--augit-blame-color-${index}`).trim()));
      const anchors = theme === 'light' ? ['#a1bef7', '#c9dafb', '#dee8fc', '#edf2fa', '#f7f8fa'] : ['#2f5194', '#2d416b', '#283754', '#242d42', '#22252e'];
      assert.deepEqual(anchors, [0, 5, 10, 15, 20].map(index => palette[index]));
      check(`${theme} 21色背景保留全部五个参考锚点`, true);
      await page.evaluate(() => {
        const original = window.__hostStub;
        window.__tipReads = 0;
        window.__hostStub = async (method, params) => {
          if (method === 'git/commit' && params.revision === 'full-head-hash') {
            window.__tipReads++;
            await new Promise(resolve => { window.__finishTip = resolve; });
            return { available: true, body: '完整主题\n\n完整消息 <script>literal</script>' };
          }
          return original(method, params);
        };
      });
      await page.locator('.blame-row').first().hover();
      await page.waitForSelector('.blame-tooltip');
      assert.equal(await page.locator('.blame-row').first().getAttribute('title'), null);
      assert.equal(await page.locator('.blame-tooltip-revision').textContent(), 'full-head-hash');
      assert.equal(await page.locator('.blame-tooltip script, .blame-tooltip img').count(), 0);
      assert.ok((await page.locator('.blame-tooltip').textContent()).includes('Author: <script>author</script>'));
      await page.evaluate(() => window.__finishTip());
      await page.waitForFunction(() => document.querySelector('.blame-tooltip-message')?.textContent.includes('完整消息'));
      assert.equal(await page.locator('.blame-tooltip script').count(), 0);
      const body = await page.locator('.blame-document .code-view').textContent();
      await page.keyboard.press('Escape');
      assert.equal(await page.locator('.blame-tooltip').count(), 0);
      await page.locator('.blame-row').first().focus();
      await page.waitForSelector('.blame-tooltip');
      assert.equal(await page.evaluate(() => window.__tipReads), 1);
      check(`${theme} 富文本提示安全转义，完整消息按需读取且同提交复用`, true);
      await page.locator('.blame-tooltip-revision').click();
      await page.waitForFunction(() => document.querySelector('.commit-row.selected')?.dataset.fullHash === 'full-head-hash');
      assert.equal(await page.locator('.blame-document .code-view').textContent(), body);
      assert.equal(await page.locator('.blame-tooltip').count(), 0);
      check(`${theme} 提示提交链接定位真实日志提交且保留只读正文`, true);
      await page.evaluate(() => {
        const original = window.__hostStub;
        window.__hostStub = async (method, params) => {
          if (method === 'git/commit' && params.revision === 'full-bbb2222') {
            await new Promise(resolve => { window.__finishStaleTip = resolve; });
            return { available: true, body: '已关闭视图的晚到消息' };
          }
          return original(method, params);
        };
      });
      await page.locator('.blame-row').nth(1).focus();
      await page.waitForSelector('.blame-tooltip');
      await page.waitForFunction(() => typeof window.__finishStaleTip === 'function');
      await page.getByRole('button', { name: 'Close Blame', exact: true }).click();
      await page.evaluate(async () => { window.__finishStaleTip(); await new Promise(resolve => setTimeout(resolve, 50)); });
      assert.equal(await page.locator('.blame-tooltip').count(), 0);
      check(`${theme} 关闭归属视图清除提示，晚到消息不恢复旧视图`, true);
      assert.deepEqual(blame.errors, []);
    } finally { await blame.page.close(); }
  }

  const history = await openScene('scene=file-history&theme=light&file-history=docs/notes.txt', () => {
    const original = window.__hostStub;
    window.__hostStub = async (method, params) => {
      const result = await original(method, params);
      if (method !== 'git/file-history') return result;
      return { ...result, commits: result.commits.map((commit, index) => ({ ...commit, date: index ? 'Yesterday 9:00' : 'A minute ago', dateTime: index ? '2026/9/14 9:00' : '2026/9/15 10:00' })) };
    };
  });
  try {
    const dates = await history.page.locator('.history-row[data-history-full]').evaluateAll(rows => rows.map(row => ({ text: row.children[1].textContent, title: row.children[1].title })));
    assert.deepEqual(dates, [{ text: 'A minute ago', title: '2026/9/15 10:00' }, { text: 'Yesterday 9:00', title: '2026/9/14 9:00' }]);
    check('文件历史直接显示宿主近期日期，同时保留完整时间提示', true);
    assert.deepEqual(history.errors, []);
  } finally { await history.page.close(); }
};
