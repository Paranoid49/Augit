// 在已有浏览器中验证比较字号和模式切换；不启动服务器，退出时释放浏览器。
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, executablePath, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath, headless: true });
  let passed = 0;
  const errors = [];
  try {
    await fs.mkdir(output, { recursive: true });
    for (const dpi of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ viewport: { width: 1180, height: 900 }, deviceScaleFactor: dpi });
      try {
        const page = await context.newPage();
        page.on('pageerror', error => errors.push(error.message));
        for (const scene of ['git-compare', 'commit-diff', 'file-history']) for (const theme of ['light', 'dark']) for (const size of [13, 40]) for (const codeSize of [13, 40]) {
          const label = `${scene}-${theme}-ui${size}-code${codeSize}-${dpi}`;
          const url = pathToFileURL(path.resolve(__dirname, `../docs/ux-mockups/${scene}.html`));
          url.search = new URLSearchParams({ theme, 'ui-size': size, 'code-size': codeSize });
          await page.goto(url.href);
          await page.waitForSelector('body[data-typography-preview="ready"]');
          const layout = page.locator('.diff-layout').first();
          const outer = await layout.boundingBox();
          for (const mode of ['单栏', '双栏']) {
            await layout.getByRole('button', { name: mode, exact: true }).click();
            const state = await layout.evaluate(element => {
              const rect = node => { const r = node.getBoundingClientRect(); return { l: r.left, t: r.top, r: r.right, b: r.bottom }; };
              const font = getComputedStyle(element.querySelector('.reference-source'));
              const canvas = document.createElement('canvas').getContext('2d');
              canvas.font = `${font.fontSize} ${font.fontFamily}`;
              const metrics = canvas.measureText('国Ag');
              return { bounds: rect(element), header: rect(element.querySelector('.reference-filebar')),
                body: rect(element.querySelector('.diff-columns')), height: metrics.fontBoundingBoxAscent + metrics.fontBoundingBoxDescent,
                rows: [...element.querySelectorAll('.reference-before, .reference-after')].map(rect),
                icons: [...element.querySelectorAll('.diff-toolbar button')].map(rect),
                summaries: [...element.querySelectorAll('.comparison-summary, .diff-toolbar > .file-status-modified')].map(rect),
                codeSize: getComputedStyle(element.querySelector('.diff-columns')).fontSize };
            });
            assert.equal(state.codeSize, `${codeSize}px`, label);
            assert.deepEqual(await layout.boundingBox(), outer, `${label} 模式不改变比较外框`);
            assert.ok(Math.abs(state.header.b - state.body.t) < 1, `${label} 文件栏与正文衔接`);
            for (const row of state.rows) {
              assert.ok(row.b - row.t >= state.height, `${label} 引用行高度不足`);
              assert.ok(row.t >= state.header.t && row.b <= state.header.b, `${label} 引用行越界`);
            }
            if (mode === '单栏') assert.ok(state.rows[0].b <= state.rows[1].t, label);
            else assert.equal(state.rows[0].t, state.rows[1].t, label);
            for (const icon of state.icons) {
              assert.equal(icon.b - icon.t, 27, `${label} 图标不跟随字号放大`);
              assert.ok(icon.l >= state.bounds.l && icon.r <= state.bounds.r, `${label} 工具图标越界`);
            }
            for (const text of state.summaries) assert.ok(text.b - text.t >= state.height, `${label} 摘要高度不足`);
            if (mode === '双栏') {
              const content = await layout.evaluate(element => {
                const sides = [...element.querySelectorAll('.diff-side')].map(side => [...side.querySelectorAll('.diff-code-line')]);
                const gutter = element.querySelector('.diff-gutter');
                const textBounds = [...gutter.children].flatMap(column => [...column.childNodes]
                  .filter(node => node.nodeType === Node.TEXT_NODE && node.textContent.trim())
                  .map(node => { const range = document.createRange(); range.selectNodeContents(node); const r = range.getBoundingClientRect(); return [r.left, r.right]; }));
                const bounds = gutter.getBoundingClientRect();
                return { left: sides[0].map(row => row.textContent), right: sides[1].map(row => row.textContent),
                  tops: sides.map(rows => rows.map(row => row.getBoundingClientRect().top)),
                  gutter: [bounds.left, bounds.right], textBounds,
                  headerLeft: element.querySelector('.reference-after').getBoundingClientRect().left,
                  bodyLeft: element.querySelectorAll('.diff-side')[1].getBoundingClientRect().left };
              });
              assert.equal(content.left.length, content.right.length, `${label} 新增行须补齐旧侧`);
              assert.equal(content.left[13], '', `${label} 插入行对应空白旧侧`);
              assert.equal(content.left[14], content.right[14], `${label} 插入后的上下文须继续对齐`);
              assert.deepEqual(content.tops[0], content.tops[1], `${label} 两侧行高与顶部位置一致`);
              assert.ok(Math.abs(content.headerLeft - content.bodyLeft) < 1, `${label} 版本标题随中栏宽度对齐`);
              for (const [left, right] of content.textBounds) {
                assert.ok(left >= content.gutter[0] && right <= content.gutter[1], `${label} 行号字形须落在中栏内`);
              }
            }
          }
          if (dpi === 1 && scene === 'commit-diff') await page.screenshot({ path: path.join(output, `${label}.png`) });
          passed++;
        }
      } finally { await context.close(); }
    }
    assert.deepEqual(errors, []);
    console.log(`PASS=${passed} 比较页独立字号、引用行、图标与单双栏验证通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
