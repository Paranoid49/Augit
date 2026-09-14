// 使用既有浏览器检查查找条的布局、状态、焦点与关闭返回，不启动服务器。
// 用法：node tools/verify-ux-find.cjs <playwright 模块路径> <浏览器 exe 路径> [截图目录] [--interaction-only]
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const fs = require('node:fs/promises');

async function verifyComposition(page) {
  let cases = 0;
  for (const theme of ['light', 'dark']) for (const regex of [false, true]) {
    const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/text-viewer.html'));
    url.search = new URLSearchParams({ theme, 'ui-size': '13' });
    await page.goto(url.href);
    await page.waitForSelector('body[data-typography-preview="ready"]');
    const input = page.getByRole('textbox', { name: '当前文件查找' });
    const waitStatus = async value => {
      try {
        await page.waitForFunction(text => document.querySelector('.find-status')?.textContent === text, value, { timeout: 5000 });
      } catch (cause) {
        throw new Error(`${theme} regex=${regex} 预期 ${value}：${JSON.stringify(await page.evaluate(() => ({
          query: document.querySelector('.current-find input')?.value,
          status: document.querySelector('.find-status')?.textContent,
          resources: window.findResources(), selected: document.querySelector('.find-current')?.dataset.start,
        })))}`, { cause });
      }
    };
    const selected = () => page.locator('.find-current').first().getAttribute('data-start');
    const starts = () => page.evaluate(() => window.findWorkerStarts());
    const draft = value => input.evaluate((element, value) => {
      element.value = value;
      element.dispatchEvent(new InputEvent('input', { bubbles: true, isComposing: true, data: value }));
    }, value);
    await page.getByRole('button', { name: '当前文件搜索', exact: true }).click();
    await waitStatus('1/12');
    if (regex) { await page.getByRole('button', { name: '正则表达式', exact: true }).click(); await waitStatus('1/12'); }
    await input.focus();
    await input.press('Enter'); await waitStatus('2/12');
    const before = await selected(), scans = await starts();
    await input.dispatchEvent('compositionstart');
    for (const text of ['S', 'Sys', 'System']) {
      await draft(text);
      assert.equal(await page.locator('.find-status').textContent(), '', '组词中不显示旧结果数量');
      assert.equal(await selected(), before, '组词中不移动当前正文匹配');
      assert.equal(await starts(), scans, '组词中不启动 Worker');
    }
    const source = await page.locator('.code-line > :last-child').allTextContents();
    const total = [...source.join('\n').matchAll(/System/gi)].length;
    assert.ok(total > 1);
    await input.dispatchEvent('compositionend'); await waitStatus(`1/${total}`);
    const completedScans = await starts();
    await input.dispatchEvent('input', { isComposing: false });
    assert.equal(await starts(), completedScans, '最终 input 通知复用已完成组词查询');
    await input.press('Enter'); await waitStatus(`2/${total}`);
    const retained = await selected();
    await input.dispatchEvent('compositionstart');
    await draft('临时组词'); await draft('System');
    await input.dispatchEvent('compositionend'); await waitStatus(`2/${total}`);
    assert.equal(await selected(), retained, '取消组词保留原匹配');
    assert.equal(await starts(), completedScans, '取消组词不重复扫描');
    await input.dispatchEvent('compositionstart'); await draft('Git');
    const scroll = await page.locator('.code-view').evaluate(code => {
      code.scrollTop = 80;
      code.focus({ preventScroll: true });
      return { x: code.scrollLeft, y: code.scrollTop };
    });
    await waitStatus('1/12');
    assert.deepEqual(await page.locator('.code-view').evaluate(code => ({ x: code.scrollLeft, y: code.scrollTop })), scroll,
      '失焦提交组词不抢正文滚动');
    assert.equal(await page.locator('.code-view').evaluate(code => code === document.activeElement), true);
    await input.focus(); await input.dispatchEvent('compositionstart'); await draft('System');
    await page.getByRole('button', { name: '关闭查找', exact: true }).evaluate(button => button.click());
    assert.equal(await page.locator('.current-find').count(), 0);
    assert.deepEqual(await page.evaluate(() => window.findResources()), { workers: 0, urls: 0 });
    await page.getByRole('button', { name: '当前文件搜索', exact: true }).click();
    await waitStatus(`1/${total}`);
    cases++;
  }
  // 上一条查询尚未就绪时开始组词，必须终止旧 Worker，而不是只忽略输入事件。
  const slowUrl = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/text-viewer.html'));
  slowUrl.search = 'verify-slow-worker=1';
  await page.goto(slowUrl.href);
  await page.waitForSelector('body[data-typography-preview="ready"]');
  await page.getByRole('button', { name: '当前文件搜索', exact: true }).click();
  await page.getByRole('button', { name: '正则表达式', exact: true }).click();
  const pendingInput = page.getByRole('textbox', { name: '当前文件查找' });
  await pendingInput.focus();
  await pendingInput.press('Enter');
  await pendingInput.dispatchEvent('compositionstart');
  assert.deepEqual(await page.evaluate(() => window.findResources()), { workers: 0, urls: 0 }, '组词立即取消旧 Worker');
  await pendingInput.evaluate(element => {
    element.value = 'Git';
    element.dispatchEvent(new InputEvent('input', { bubbles: true, isComposing: true }));
  });
  await page.waitForTimeout(400);
  assert.equal(await page.locator('.find-status').textContent(), '', '旧 Worker 的延迟消息不得恢复数量');
  await pendingInput.dispatchEvent('compositionend');
  await page.waitForFunction(() => document.querySelector('.find-status').textContent === '1/12');
  return cases + 1;
}

async function main() {
  const [modulePath, browserPath, outputPath, mode] = process.argv.slice(2);
  assert.ok(mode === undefined || mode === '--interaction-only', '未知检查模式。');
  assert.ok(modulePath && browserPath, '请传入现有 Playwright 模块和浏览器路径。');
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  try {
    if (outputPath) await fs.mkdir(outputPath, { recursive: true });
    const page = await browser.newPage();
    await page.addInitScript(() => {
      const workers = new Set(), urls = new Set();
      let starts = 0;
      const NativeWorker = window.Worker;
      window.Worker = class extends NativeWorker {
        constructor(...args) {
          super(...args); workers.add(this); starts++;
          // 只在验证指定的页面模拟慢启动，计算本身仍使用真实浏览器 Worker。
          this.addEventListener('message', event => {
            if (location.search.includes('verify-slow-worker') && event.data.ready && !event.data.delayed) {
              event.stopImmediatePropagation();
              setTimeout(() => this.dispatchEvent(new MessageEvent('message', { data: { ready: true, delayed: true } })), 350);
            }
          });
        }
        terminate() { workers.delete(this); super.terminate(); }
      };
      const create = URL.createObjectURL.bind(URL), revoke = URL.revokeObjectURL.bind(URL);
      URL.createObjectURL = blob => { const url = create(blob); urls.add(url); return url; };
      URL.revokeObjectURL = url => { urls.delete(url); revoke(url); };
      window.findResources = () => ({ workers: workers.size, urls: urls.size });
      window.findWorkerStarts = () => starts;
    });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    let passed = 0;
    for (const size of mode === '--interaction-only' ? [] : [13, 40]) for (const theme of ['light', 'dark'])
      for (const width of [1024, 1180]) for (const state of ['normal', 'empty', 'no-match', 'invalid', 'loading', 'timeout']) {
        const label = `${theme}-${size}-${width}-${state}`;
        await page.setViewportSize({ width, height: width === 1024 ? 640 : 760 });
        const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/text-viewer.html'));
        url.search = new URLSearchParams({ 'ui-size': String(size), theme, 'find-state': state });
        await page.goto(url.href);
        await page.waitForSelector('body[data-typography-preview="ready"]');
        const expectedStatus = { normal: '1/12', empty: '', 'no-match': '0/0', invalid: '正则表达式无效', loading: '正在搜索…', timeout: '查找超时' }[state];
        try {
          await page.waitForFunction(value => document.querySelector('.find-status').textContent === value, expectedStatus, { timeout: 5000 });
        } catch (error) {
          throw new Error(`${label}: ${JSON.stringify(await page.evaluate(() => ({
            status: document.querySelector('.find-status')?.textContent,
            rows: document.querySelectorAll('.code-line').length,
            scripts: [...document.scripts].map(script => script.src),
          })))}; errors=${JSON.stringify(errors)}`, { cause: error });
        }
        const actual = await page.evaluate(() => {
          const rect = element => { const b = element.getBoundingClientRect(); return { left: b.left, right: b.right, top: b.top, bottom: b.bottom, width: b.width, height: b.height }; };
          const bar = document.querySelector('.current-find');
          const status = bar.querySelector('.find-status');
          return {
            bar: rect(bar), parent: rect(document.querySelector('.editor-content')), input: rect(bar.querySelector('input')),
            controls: [...bar.children].map(rect), status: status.textContent,
            focused: document.activeElement.getAttribute('aria-label'), font: getComputedStyle(bar).fontSize,
            code: rect(document.querySelector('.code-view')), bodySize: getComputedStyle(document.querySelector('.code-view')).fontSize,
            toolbar: rect(document.querySelector('.document-toolbar')),
            toolbarButtons: [...document.querySelectorAll('.document-toolbar > .icon-button')].map(rect),
            path: rect(document.querySelector('.document-path')),
            statusPath: document.querySelector('.status-path').textContent,
          };
        });
        assert.equal(actual.focused, '当前文件查找', label);
        assert.equal(actual.font, `${size}px`, label);
        assert.equal(actual.bodySize, '13px', label);
        assert.ok(actual.input.height >= size + 4, label);
          assert.ok(actual.bar.left >= actual.parent.left && actual.bar.right <= actual.parent.right, label);
          assert.equal(actual.bar.width, actual.parent.width, `${label} 查找条必须占满正文宽度`);
        assert.equal(actual.toolbarButtons.length, 4, label);
        assert.ok(actual.toolbar.right <= actual.parent.right, `${label} 路径栏不能撑宽正文`);
        assert.ok(actual.path.right <= actual.toolbarButtons[0].left, `${label} 路径不能遮住工具按钮`);
        for (const button of actual.toolbarButtons)
          assert.ok(button.right <= actual.parent.right && button.top >= actual.toolbar.top && button.bottom <= actual.toolbar.bottom,
            `${label} 文档工具按钮必须完整可见`);
        assert.match(actual.statusPath, /src\s+›\s+Augit.App/, `${label} 状态栏路径必须属于当前文件`);
        for (let i = 0; i < actual.controls.length; i++) {
          const control = actual.controls[i];
          assert.ok(control.top >= actual.bar.top && control.bottom <= actual.bar.bottom, `${label} 控件不越出查找条`);
          if (i) assert.ok(actual.controls[i - 1].right <= control.left + .01, `${label} 控件不重叠`);
        }
        for (const index of [1, 2, 3, 5, 6, 7]) assert.equal(actual.controls[index].width, 28, label);
        assert.equal(actual.status, expectedStatus);
        if (size === 13 && state === 'normal') assert.equal(actual.bar.height, 42, label);
        if (outputPath && width === 1024 && ['normal', 'loading', 'timeout'].includes(state))
          await page.screenshot({ path: path.join(outputPath, `mockup-${label}.png`) });
        await page.keyboard.press('Escape');
        assert.equal(await page.locator('.current-find').count(), 0, label);
        assert.equal(await page.locator('.code-view').evaluate(element => element === document.activeElement), true, label);
        const restoredCode = await page.locator('.code-view').boundingBox();
        const restoredToolbar = await page.locator('.document-toolbar').boundingBox();
        assert.equal(restoredCode.x, actual.code.left, label);
        assert.equal(restoredCode.width, actual.code.width, label);
        assert.equal(restoredCode.y, restoredToolbar.y + restoredToolbar.height, label);
        await page.keyboard.press('Control+f');
        assert.equal(await page.locator('.current-find input').inputValue(), { invalid: '[', timeout: '(a+)+$', empty: '', 'no-match': '不存在的文字' }[state] ?? 'Git', label);
        assert.equal(await page.locator('.current-find input').evaluate(input => input === document.activeElement), true, label);
        passed++;
      }

    // 输入、导航和高亮都以实际正文中的匹配位置验证，不依赖旧的首项需要 Enter 规则。
    const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/text-viewer.html'));
    url.search = 'ui-size=13&theme=light';
    await page.setViewportSize({ width: 1180, height: 760 });
    await page.goto(url.href);
    await page.waitForSelector('body[data-typography-preview="ready"]');
    const original = await page.locator('.code-lines').textContent();
    const source = await page.locator('.code-line > :last-child').allTextContents();
    const expected = [...source.join('\n').matchAll(/Git/gi)].map(match => match.index);
    const input = page.getByRole('textbox', { name: '当前文件查找' });
    const waitStatus = value => page.waitForFunction(text => document.querySelector('.find-status')?.textContent === text, value);
    const selectedStart = async () => Number(await page.locator('.find-current').first().getAttribute('data-start'));
    const currentLines = () => page.locator('.code-line:has(.find-current) .line-number').allTextContents();
    await input.fill('Git'); await waitStatus('1/12');
    assert.equal(await selectedStart(), expected[0]);
    assert.equal(await page.locator('.find-match').count(), expected.length - 1, '其他匹配应独立高亮');
    await input.press('Enter'); await waitStatus('2/12'); assert.equal(await selectedStart(), expected[1]);
    await input.press('Enter'); await waitStatus('3/12'); assert.equal(await selectedStart(), expected[2]);
    await input.press('Shift+Enter'); await waitStatus('2/12'); assert.equal(await selectedStart(), expected[1]);
    assert.equal(await input.evaluate(element => element === document.activeElement), true);
    for (const name of ['区分大小写', '全字匹配', '正则表达式', '上一项', '下一项', '关闭查找', '当前文件查找']) {
      await page.keyboard.press('Tab');
      assert.equal(await page.evaluate(() => document.activeElement.getAttribute('aria-label')), name);
    }
    const closedSelection = await selectedStart();
    await page.keyboard.press('Escape');
    assert.equal(await page.locator('.find-current, .find-match').count(), 0, '关闭后清除查找专用高亮。');
    assert.equal(await page.evaluate(() => window.getSelection().toString().toLowerCase()), 'git', '关闭后保留当前匹配的正文选择。');
    await page.getByRole('button', { name: '当前文件搜索', exact: true }).click();
    assert.equal(await input.inputValue(), 'Git');
    assert.equal(await selectedStart(), closedSelection, '重开保持当前匹配');
    await waitStatus('2/12');
    await input.press('Enter'); await waitStatus('3/12'); assert.equal(await selectedStart(), expected[2]);
    if (outputPath) await page.screenshot({ path: path.join(outputPath, 'mockup-find-continued.png') });
    await page.getByRole('button', { name: '全字匹配', exact: true }).click(); await waitStatus('1/3');
    await input.fill('git');
    await page.getByRole('button', { name: '区分大小写', exact: true }).click(); await waitStatus('0/0');
    await page.getByRole('button', { name: '区分大小写', exact: true }).click(); await waitStatus('1/3');
    await page.getByRole('button', { name: '全字匹配', exact: true }).click(); await waitStatus('1/12');
    await page.getByRole('button', { name: '正则表达式', exact: true }).click();
    await input.fill('['); await waitStatus('正则表达式无效');
    await input.fill('(?=Git)'); await waitStatus('1/12');
    assert.equal(await page.locator('.find-current').getAttribute('data-length'), '0');
    await input.press('Shift+Enter'); await waitStatus('12/12'); assert.equal(await selectedStart(), expected.at(-1));
    await input.fill('Git'); await waitStatus('1/12');
    await input.press('Enter'); await input.press('Enter'); await waitStatus('3/12');
    assert.equal(await selectedStart(), expected[2]);
    await input.fill('Concurrent;\\nusing System.ComponentModel;'); await waitStatus('1/1');
    assert.deepEqual(await currentLines(), ['1', '2']);
    assert.equal(await page.locator('.code-lines').textContent(), original, '跨行标记不得修改正文');
    if (outputPath) await page.screenshot({ path: path.join(outputPath, 'mockup-find-multiline.png') });
    await input.fill('Concurrent;\\n'); await waitStatus('1/1');
    assert.deepEqual(await currentLines(), ['1'], '匹配结束处不能额外标记下一行');
    await input.fill('(.+)+XYZ_UNMATCHED$'); await waitStatus('查找超时');
    assert.deepEqual(await page.evaluate(() => window.findResources()), { workers: 0, urls: 0 });
    await input.fill('missing'); await waitStatus('0/0');
    assert.equal(await page.locator('.find-current, .find-match').count(), 0);
    await input.fill('Git'); await input.press('Escape');
    await page.waitForTimeout(300);
    assert.equal(await page.locator('.current-find').count(), 0, '晚到结果不能重新打开查找');
    assert.equal(await page.locator('.code-lines').textContent(), original);
    assert.deepEqual(await page.evaluate(() => window.findResources()), { workers: 0, urls: 0 });
    url.search = 'ui-size=13&theme=light&verify-slow-worker=1';
    await page.goto(url.href);
    await page.waitForSelector('body[data-typography-preview="ready"]');
    await page.getByRole('button', { name: '正则表达式', exact: true }).click();
    await waitStatus('正在搜索…');
    await waitStatus('1/12');
    assert.deepEqual(await page.evaluate(() => window.findResources()), { workers: 0, urls: 0 }, '慢启动成功后释放资源');
    const compositionCases = await verifyComposition(page);
    assert.deepEqual(errors, [], '视觉稿不得出现脚本错误。');
    console.log(`查找视觉稿检查通过：${passed ? `${passed} 个布局状态；` : '仅复核连续交互；'}连续输入、双向导航、跨行匹配、键盘循环、关闭重开、旧请求失效、真实超时与资源释放；另含 ${compositionCases} 组输入法连续场景。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
