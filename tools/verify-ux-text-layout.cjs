// 验证正文行距、固定行号、横纵向滚动和独立字号；使用既有浏览器，不启动服务器。
// 用法：node tools/verify-ux-text-layout.cjs <playwright 模块路径> <浏览器 exe 路径> [截图目录]
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const fs = require('node:fs/promises');

async function main() {
  const [modulePath, browserPath, outputPath] = process.argv.slice(2);
  assert.ok(modulePath && browserPath, '请传入现有浏览器及 Playwright。');
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  try {
    if (outputPath) await fs.mkdir(outputPath, { recursive: true });
    let passed = 0, horizontal = 0, vertical = 0;
    const errors = [];
    for (const scale of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ viewport: { width: 1024, height: 640 }, deviceScaleFactor: scale });
      try {
        const page = await context.newPage();
        page.on('pageerror', error => errors.push(error.message));
        for (const theme of ['light', 'dark']) for (const uiSize of [13, 40]) for (const codeSize of [13, 26])
          for (const state of ['text', 'formatted', 'source', 'invalid']) {
            const label = `${theme}-${uiSize}-${codeSize}-${scale}-${state}`;
            const url = pathToFileURL(path.resolve(__dirname, `../docs/ux-mockups/${state === 'text' ? 'text-viewer' : 'json-preview'}.html`));
            url.search = new URLSearchParams({ theme, 'ui-size': String(uiSize), 'code-size': String(codeSize), 'json-state': state });
            await page.goto(url.href);
            await page.waitForSelector('body[data-typography-preview="ready"]');
            const layout = await page.evaluate(() => {
              const view = document.querySelector('.code-view');
              const rows = [...view.querySelectorAll('.code-line')];
              const rect = el => { const r = el.getBoundingClientRect(); return { left: r.left, top: r.top, right: r.right, bottom: r.bottom, width: r.width, height: r.height }; };
              const first = rows[0], second = rows[1];
              const gutter = first.querySelector('.line-number');
              const source = first.querySelector('span:last-child');
              return {
                view: rect(view), gutter: rect(gutter), source: rect(source), row: rect(first),
                rowStep: second.getBoundingClientRect().top - first.getBoundingClientRect().top,
                font: getComputedStyle(view).fontSize, uiFont: getComputedStyle(document.documentElement).fontSize,
                horizontal: view.scrollWidth > view.clientWidth, vertical: view.scrollHeight > view.clientHeight,
                text: view.textContent, focus: document.activeElement.getAttribute('aria-label'),
              };
            });
            assert.equal(layout.font, `${codeSize}px`, label);
            assert.equal(layout.uiFont, `${uiSize}px`, label);
            const expected = Math.round(codeSize * 1.7 * scale) / scale;
            assert.ok(Math.abs(layout.row.height - expected) < .02, label);
            assert.ok(Math.abs(layout.rowStep - expected) < .02, label);
            assert.ok(layout.gutter.width >= 28 + codeSize * 1.4, label);
            assert.ok(Math.abs(layout.source.left - layout.gutter.right) < .02, label);
            const after = await page.evaluate(() => {
              const view = document.querySelector('.code-view');
              view.scrollLeft = 100;
              view.scrollTop = 80;
              const gutter = view.querySelector('.line-number').getBoundingClientRect();
              const text = view.querySelector('.code-line > span:last-child').getBoundingClientRect();
              return { left: gutter.left, source: text.left, top: text.top, x: view.scrollLeft, y: view.scrollTop,
                text: view.textContent, focus: document.activeElement.getAttribute('aria-label') };
            });
            assert.equal(after.text, layout.text, label);
            assert.equal(after.focus, layout.focus, `${label} 滚动不抢查找焦点`);
            assert.ok(Math.abs(after.left - layout.gutter.left) < .02, `${label} 行号必须横向固定`);
            assert.ok(Math.abs(after.source - (layout.source.left - after.x)) < .02, label);
            assert.ok(Math.abs(after.top - (layout.source.top - after.y)) < .02, label);
            if (layout.horizontal) { assert.ok(after.x > 0, label); horizontal++; }
            else assert.equal(after.x, 0, `${label} 短内容不保留空白滚动范围`);
            if (layout.vertical) { assert.ok(after.y > 0, label); vertical++; }
            if (state === 'formatted' && codeSize === 13) assert.equal(layout.horizontal, false, label);
            await page.locator('.code-view').evaluate(view => view.scrollTo(0, 0));
            if (outputPath && scale === 1 && uiSize === 13 && codeSize === 13)
              await page.screenshot({ path: path.join(outputPath, `layout-${label}.png`) });
            passed++;
          }
        for (const theme of ['light', 'dark']) for (const scene of ['commit-diff', 'git-compare', 'blame']) {
          const modes = scene === 'blame' ? ['source'] : ['side-by-side', 'unified'];
          for (const mode of modes) {
            const label = `${theme}-${scale}-${scene}-${mode}`;
            const url = pathToFileURL(path.resolve(__dirname, `../docs/ux-mockups/${scene}.html`));
            url.search = new URLSearchParams({ theme, 'ui-size': '13', diffMode: mode });
            await page.goto(url.href);
            await page.waitForSelector('body[data-typography-preview="ready"]');
            const geometry = await page.evaluate(isBlame => {
              const first = document.querySelector(isBlame ? '.code-line' : '.diff-code-line');
              const next = first.nextElementSibling;
              const a = first.getBoundingClientRect(), b = next.getBoundingClientRect();
              const companion = document.querySelector(isBlame ? '.blame-row' : '.diff-gutter > div');
              return { height: a.height, step: b.top - a.top,
                otherHeight: isBlame ? companion.getBoundingClientRect().height : null,
                gutterHeight: companion && !isBlame ? parseFloat(getComputedStyle(companion).lineHeight) : null };
            }, scene === 'blame');
            const expected = Math.round(13 * 1.7 * scale) / scale;
            assert.ok(Math.abs(geometry.height - expected) < .02, label);
            assert.ok(Math.abs(geometry.step - expected) < .02, label);
            if (geometry.otherHeight !== null) assert.ok(Math.abs(geometry.otherHeight - expected) < .02, label);
            if (geometry.gutterHeight !== null) assert.ok(Math.abs(geometry.gutterHeight - expected) < .02, label);
            passed++;
          }
        }
      } finally { await context.close(); }
    }
    assert.ok(horizontal > 0 && vertical > 0, '矩阵必须实际覆盖两种滚动。');
    assert.deepEqual(errors, []);
    console.log(`正文排版检查通过：${passed} 组合，${horizontal} 个横向滚动和 ${vertical} 个纵向滚动场景。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
