// 验证 Markdown 三模式、阅读位置、分隔拖动和局部反馈；直接打开文件，不启动服务器。
// 用法：node tools/verify-ux-markdown.cjs <playwright 模块路径> <浏览器 exe 路径> [截图目录]
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function verifySplitterDrag(page) {
  const view = page.locator('.markdown-document');
  const panes = view.locator('.markdown-panes');
  const source = view.locator('.markdown-source');
  const divider = view.locator('.markdown-divider');
  await page.getByRole('button', { name: '左右对照', exact: true }).click();
  const width = () => source.evaluate(element => element.getBoundingClientRect().width);
  const captured = () => divider.evaluate(element => element.hasPointerCapture(Number(element.dataset.testPointerId)));
  const initialWidth = await width();
  await page.evaluate(() => { bindMarkdownModes(); bindMarkdownModes(); });
  assert.equal(await width(), initialWidth, '重新绑定不能重置已选比例');
  await view.evaluate(element => {
    element.testModeChanges = 0;
    element.addEventListener('document-content-changed', () => element.testModeChanges++);
  });
  await view.locator('button[data-markdown-mode="preview"]').evaluate(element => element.click());
  await view.locator('button[data-markdown-mode="split"]').evaluate(element => element.click());
  assert.equal(await view.evaluate(element => element.testModeChanges), 2, '重新绑定必须解除旧监听');
  await divider.evaluate(element => element.addEventListener('pointerdown', event => element.dataset.testPointerId = event.pointerId));
  const begin = async () => {
    const rect = await divider.boundingBox();
    const before = await width();
    await page.mouse.move(rect.x + 4, rect.y + 30);
    await page.mouse.down();
    assert.equal(await captured(), true);
    assert.equal(await width(), before, '按下不能改变对照宽度');
    return { x: rect.x + 4, y: rect.y + 30, before };
  };
  const sameWidth = async (expected, reason) => assert.ok(Math.abs(await width() - expected) < .6, reason);
  await panes.evaluate(element => {
    element.testMutationCount = 0;
    element.testObserver = new MutationObserver(records => element.testMutationCount += records.length);
    element.testObserver.observe(element, { attributes: true, attributeFilter: ['style'] });
  });
  try {
    const available = await panes.evaluate(element => element.clientWidth - 6);
    const start = await begin();
    await page.mouse.move(start.x, start.y);
    await sameWidth(start.before, '首次移动不能跳动');
    await page.mouse.move(start.x + 30, start.y);
    await sameWidth(start.before + 30, '拖动保留按下偏移');
    await page.mouse.move(-25, start.y);
    await sameWidth(240, '负坐标停在左侧最小宽度');
    const mutations = await panes.evaluate(element => element.testMutationCount);
    for (let i = 0; i < 10; i++) await page.mouse.move(-25 - i, start.y);
    assert.equal(await panes.evaluate(element => element.testMutationCount), mutations, '越界重复移动不能改写样式');
    await page.mouse.move(1300, start.y);
    await sameWidth(available - 240, '右侧越界保留预览最小宽度');
    await page.mouse.up();
    assert.equal(await captured(), false);
    await page.mouse.move(start.x, start.y);
    await sameWidth(available - 240, '释放后不再跟随旧移动');

    for (const reason of ['Esc', '取消', '捕获转移', '切换模式', '窗口失焦', '隐藏']) {
      const point = await begin();
      switch (reason) {
        case 'Esc': await page.keyboard.press('Escape'); break;
        case '取消': await divider.evaluate(element => element.dispatchEvent(new PointerEvent('pointercancel', { pointerId: Number(element.dataset.testPointerId) }))); break;
        case '捕获转移': await divider.evaluate(element => element.parentElement.querySelector('.markdown-source').setPointerCapture(Number(element.dataset.testPointerId))); break;
        case '切换模式': await view.locator('button[data-markdown-mode="preview"]').evaluate(element => element.click()); break;
        case '窗口失焦': await page.evaluate(() => window.dispatchEvent(new Event('blur'))); break;
        case '隐藏': await view.evaluate(element => element.hidden = true); break;
      }
      await page.mouse.move(point.x - 80, point.y);
      assert.equal(await captured(), false, reason);
      await page.mouse.up();
      await view.evaluate(element => element.hidden = false);
      await view.locator('button[data-markdown-mode="split"]').evaluate(element => element.click());
      await sameWidth(point.before, `${reason}后保留比例`);
    }
    const point = await begin();
    for (const narrow of [6, 7, 485]) {
      await panes.evaluate((element, value) => element.style.width = `${value}px`, narrow);
      const constrained = await width();
      await page.mouse.move(-25, point.y);
      await sameWidth(constrained, '受限宽度不改变分隔位置');
      await page.mouse.move(1300, point.y);
      await sameWidth(constrained, '奇数宽度的舍入不能改写记忆比例');
    }
    await panes.evaluate(element => element.style.removeProperty('width'));
    await sameWidth(point.before, '恢复宽度保留比例');
    await page.mouse.up();
    assert.equal(await captured(), false);
  } finally {
    await page.mouse.up();
    await panes.evaluate(element => { element.testObserver.disconnect(); delete element.testObserver; delete element.testMutationCount; });
  }
}

async function main() {
  const [modulePath, browserPath, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: browserPath, headless: true });
  try {
    if (output) await fs.mkdir(output, { recursive: true });
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    let passed = 0;
    for (const theme of ['light', 'dark']) for (const state of ['ready', 'loading', 'failure']) {
      await page.setViewportSize({ width: 1180, height: 760 });
      const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/markdown-preview.html'));
      url.search = new URLSearchParams({ theme, 'ui-size': '13', 'markdown-state': state });
      await page.goto(url.href);
      await page.waitForSelector('body[data-typography-preview="ready"]');
      const view = page.locator('.markdown-document');
      const source = view.locator('.markdown-source');
      const preview = view.locator('.markdown-preview');
      assert.equal(await view.getAttribute('data-markdown-mode'), 'preview');
      const originalText = await preview.innerText();
      await preview.evaluate(element => element.scrollTop = 350);
      const previewTop = await preview.evaluate(element => element.scrollTop);
      assert.ok(previewTop > 0);
      await page.getByRole('button', { name: '左右对照', exact: true }).click();
      await source.evaluate(element => element.scrollTop = 200);
      const sourceTop = await source.evaluate(element => element.scrollTop);
      const divider = page.getByRole('separator', { name: '调整 Markdown 对照宽度' });
      await divider.focus();
      await page.keyboard.press('ArrowLeft');
      const ratio = await divider.getAttribute('aria-valuenow');
      assert.equal(ratio, '48');
      await page.getByRole('button', { name: '原文', exact: true }).click();
      assert.equal(await source.evaluate(element => element.scrollTop), sourceTop);
      await page.getByRole('button', { name: '左右对照', exact: true }).click();
      assert.equal(await source.evaluate(element => element.scrollTop), sourceTop);
      assert.equal(await preview.evaluate(element => element.scrollTop), previewTop);
      assert.equal(await divider.getAttribute('aria-valuenow'), ratio);
      await page.getByRole('button', { name: '预览', exact: true }).click();
      assert.equal(await preview.evaluate(element => element.scrollTop), previewTop);
      await preview.focus();
      await page.keyboard.type('must-not-edit');
      assert.equal(await preview.innerText(), originalText);
      assert.equal(await view.locator('[contenteditable="true"],textarea').count(), 0);
      const feedback = view.locator('.markdown-feedback');
      assert.equal(await feedback.isVisible(), state !== 'ready');
      if (state !== 'ready') {
        const p = await preview.boundingBox(), f = await feedback.boundingBox();
        assert.ok(f.height < p.height / 3, '局部提示不能遮住整页');
        assert.equal(f.y, p.y);
      }
      await preview.locator('a[href="#section-0"]').click();
      assert.ok(await preview.evaluate(element => element.scrollTop) < previewTop);
      assert.equal(await view.locator('.markdown-blocked a').count(), 0);
      await preview.evaluate(element => element.scrollTop = 0);
      if (output) await page.screenshot({ path: path.join(output, `markdown-${theme}-${state}.png`) });
      if (state === 'ready') await verifySplitterDrag(page);
      passed++;
    }
    assert.deepEqual(errors, []);
    console.log(`Markdown 视觉稿检查通过：${passed} 组三模式连续交互及 2 组拖动边界与收尾。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
