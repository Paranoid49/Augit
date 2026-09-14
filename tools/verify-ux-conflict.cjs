// 验证三栏冲突稿的字体、操作排布和唯一可编辑结果区；浏览器在结束时退出，不启动服务器。
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
    for (const scale of [1, 1.25, 1.5]) for (const viewport of [{ width: 1024, height: 640 }, { width: 1440, height: 900 }]) {
      const context = await browser.newContext({ viewport, deviceScaleFactor: scale });
      try {
        const page = await context.newPage();
        page.on('pageerror', e => errors.push(e.message));
        for (const theme of ['light', 'dark']) for (const size of [13, 40]) for (const code of [13, 40]) {
          const label = `${theme}-${size}-${code}-${scale}-${viewport.width}`;
          const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/conflict-resolver.html'));
          url.search = new URLSearchParams({ theme, 'ui-size': String(size), 'code-size': String(code) });
          await page.goto(url.href);
          await page.waitForSelector('body[data-typography-preview="ready"]');
          const state = await page.evaluate(() => {
            const dialog = document.querySelector('.dialog');
            const rect = node => { const r = node.getBoundingClientRect(); return { l: r.left, t: r.top, r: r.right, b: r.bottom }; };
            const context = document.createElement('canvas').getContext('2d');
            const buttons = [...dialog.querySelectorAll('.secondary-button, .primary-button')];
            const title = dialog.querySelector('.conflict-file-title');
            const count = dialog.querySelector('.conflict-header .commit-meta');
            const labels = [title, count, ...dialog.querySelectorAll('.conflict-column-title > span')];
            return { bounds: rect(dialog), compact: dialog.classList.contains('compact-conflict-header'),
              title: rect(title), titleText: title.textContent, count: rect(count), labels: labels.map(node => ({ text: node.textContent,
                fits: node.scrollWidth <= node.clientWidth && node.scrollHeight <= node.clientHeight })),
              bodies: [...dialog.querySelectorAll('.conflict-block')].map(rect), buttons: buttons.map(button => {
              const style = getComputedStyle(button);
              context.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
              const range = document.createRange();
              range.selectNodeContents(button);
              return { ...rect(button), text: button.textContent, textWidth: context.measureText(button.textContent).width,
                textBounds: rect(range) };
            }), columns: [...dialog.querySelectorAll('.conflict-column')].map(rect),
            codeSizes: [...dialog.querySelectorAll('.conflict-block')].map(block => parseFloat(getComputedStyle(block).fontSize)),
            editable: [...dialog.querySelectorAll('[contenteditable]')].map(block => block.getAttribute('aria-label')) };
          });
          assert.deepEqual(state.editable, ['最终结果'], label);
          assert.deepEqual(state.codeSizes, [code, code, code], label);
          assert.ok(state.bounds.t >= 0 && state.bounds.b <= viewport.height, label);
          assert.equal(state.compact, size === 40, label);
          assert.equal(state.titleText, size === 40 ? 'NativeGitPanel.cs' : '解决冲突 · NativeGitPanel.cs', label);
          for (const text of state.labels) assert.ok(text.fits, `${label} ${text.text} 被省略或裁切`);
          if (size === 40) assert.ok(state.title.b <= state.count.t, `${label} 文件名占用导航行`);
          assert.ok(state.bodies.every(body => body.b - body.t > 80), `${label} 正文高度不足`);
          assert.equal(state.columns[0].t, state.columns[1].t, label);
          assert.equal(state.columns[1].t, state.columns[2].t, label);
          for (let i = 0; i < state.buttons.length; i++) {
            const a = state.buttons[i];
            assert.ok(a.l >= state.bounds.l && a.r <= state.bounds.r && a.t >= state.bounds.t && a.b <= state.bounds.b, `${label} ${a.text} 越界`);
            assert.ok(a.r - a.l >= a.textWidth + 16, `${label} ${a.text} 文字被挤压`);
            assert.ok(a.textBounds.l >= a.l && a.textBounds.r <= a.r && a.textBounds.t >= a.t && a.textBounds.b <= a.b,
              `${label} ${a.text} 实际文字换行或超出按钮`);
            for (const b of state.buttons.slice(i + 1)) assert.ok(a.r <= b.l || b.r <= a.l || a.b <= b.t || b.b <= a.t, `${label} 操作重叠`);
          }
          const bodies = await page.locator('.conflict-block').evaluateAll(nodes => nodes.map(node => ({
            lines: [...node.querySelectorAll('.conflict-line')].map(line => ({ text: line.textContent,
              color: getComputedStyle(line).backgroundColor, height: line.getBoundingClientRect().height })),
            footerY: node.querySelector('.conflict-line:last-child').getBoundingClientRect().top,
            whitespace: getComputedStyle(node).whiteSpace,
            horizontal: node.scrollWidth > node.clientWidth,
            background: getComputedStyle(node).backgroundColor
          })));
          const ours = ['public async Task RefreshAsync()', '{', '    var snapshot = await LoadStatusAsync();',
            '    if (snapshot != _snapshot)', '        ShowSelectedDiff();', '}'];
          const theirs = ['public async Task RefreshAsync()', '{', '    var status = await LoadStatusAsync();', '    Render(status);', '}'];
          const expected = [ours, [...ours.slice(0, -1), ...theirs.slice(2)], theirs];
          bodies.forEach((body, side) => {
            assert.ok(Math.abs(body.footerY - bodies[1].footerY) < 1, `${label} 短侧留白后公共正文仍错位`);
            assert.deepEqual(body.lines.map(line => line.text), expected[side], `${label} 第 ${side + 1} 栏正文与审计样本不同`);
            assert.equal(body.whitespace, 'pre', `${label} 正文不可自动换行或合并缩进`);
            assert.ok(body.lines.every(line => Math.abs(line.height - code * 1.7) < 1), `${label} 正文行高不一致`);
            for (const index of [0, 1, body.lines.length - 1]) assert.equal(body.lines[index].color, 'rgba(0, 0, 0, 0)', `${label} 公共正文不应着冲突底色`);
            const color = side === 1 ? (theme === 'dark' ? 'rgb(43, 63, 89)' : 'rgb(220, 233, 252)')
              : (theme === 'dark' ? 'rgb(82, 50, 52)' : 'rgb(247, 215, 215)');
            for (const line of body.lines.slice(2, -1)) assert.equal(line.color, color, `${label} 冲突行颜色不同`);
            if (code === 40) assert.ok(body.horizontal, `${label} 长正文必须能够横向滚动`);
          });
          const sides = await page.locator('.conflict-column:not(.result) .conflict-block').allTextContents();
          const beforeActions = await page.locator('.conflict-columns').innerHTML();
          for (const button of await page.locator('.dialog :is(button, a)').all()) {
            await button.focus();
            const focus = await button.evaluate(node => {
              const style = getComputedStyle(node, '::after');
              const neutral = getComputedStyle(node, '::before');
              return { width: style.borderTopWidth, color: style.borderTopColor, top: style.top,
                neutralWidth: neutral.borderTopWidth, neutralColor: neutral.borderTopColor,
                primary: node.classList.contains('primary-button') };
            });
            assert.equal(focus.width, '1px', `${label} 按钮焦点边框缺失`);
            assert.equal(focus.color, theme === 'dark' ? 'rgb(84, 138, 247)' : 'rgb(56, 113, 225)');
            assert.equal(focus.top, '2px');
            if (focus.primary) {
              assert.equal(focus.neutralWidth, '1px');
              assert.equal(focus.neutralColor, theme === 'dark' ? 'rgb(30, 31, 34)' : 'rgb(255, 255, 255)');
            }
            const disabledFocus = await button.evaluate(node => {
              node.setAttribute('disabled', '');
              const content = getComputedStyle(node, '::after').content;
              node.removeAttribute('disabled');
              node.blur();
              return content;
            });
            assert.equal(disabledFocus, 'none', `${label} 禁用不能显示焦点环`);
          }
          const hovered = page.locator('.conflict-header button').first();
          await hovered.hover();
          assert.equal(await hovered.evaluate(node => getComputedStyle(node).backgroundColor),
            theme === 'dark' ? 'rgb(45, 47, 51)' : 'rgb(241, 242, 244)', `${label} 导航悬停底色`);
          assert.equal(await page.locator('.conflict-columns').innerHTML(), beforeActions, `${label} 动作焦点与悬停不能重建正文`);
          await page.mouse.move(1, 1);
          await page.locator('.conflict-column.result .conflict-block').focus();
          if (scale === 1 && code === 13) await page.screenshot({ path: path.join(output, `conflict-${label}.png`) });
          await page.getByRole('textbox', { name: '最终结果', exact: true }).fill('仅编辑中央结果😀');
          assert.deepEqual(await page.locator('.conflict-column:not(.result) .conflict-block').allTextContents(), sides, label);
          passed++;
        }
        for (const theme of ['light', 'dark']) for (const empty of ['left', 'right']) {
          const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/conflict-resolver.html'));
          url.search = new URLSearchParams({ theme, 'empty-side': empty, 'ui-size': '13', 'code-size': '13' });
          await page.goto(url.href);
          await page.waitForSelector('body[data-typography-preview="ready"]');
          const bodies = await page.locator('.conflict-block').evaluateAll(nodes => nodes.map(node => ({
            lines: [...node.querySelectorAll('.conflict-line')].map(line => line.textContent),
            gap: node.querySelector('.conflict-spacer')?.getBoundingClientRect().height ?? 0,
            commonY: [...node.children].find(line => line.textContent === 'public async Task RefreshAsync()').getBoundingClientRect().top,
            lastY: node.lastElementChild.getBoundingClientRect().top,
            lineHeight: parseFloat(getComputedStyle(node).lineHeight),
            editable: node.getAttribute('contenteditable')
          })));
          const common = ['public async Task RefreshAsync()', '{', '    await LoadStatusAsync();', '}'];
          const inserted = ['// 新增说明一', '// 新增说明二', ...common];
          for (const [side, body] of bodies.entries()) {
            const isEmpty = side === (empty === 'left' ? 0 : 2);
            assert.deepEqual(body.lines, isEmpty ? common : inserted);
            assert.ok(Math.abs(body.commonY - bodies[1].commonY) < 0.1, '首行空侧的公共正文错位');
            assert.ok(Math.abs(body.lastY - bodies[1].lastY) < 0.1, '首行空侧的末行错位');
            assert.ok(Math.abs(body.gap - (isEmpty ? body.lineHeight * 2 : 0)) < 0.1);
            assert.ok(Math.abs(body.lineHeight * scale - Math.round(13 * 1.7 * scale)) < 0.05, '行高应按物理像素取整');
            assert.equal(body.editable, side === 1 ? 'plaintext-only' : null);
          }
          if (viewport.width === 1440 && scale === 1) await page.screenshot({ path: path.join(output, `conflict-empty-${empty}-${theme}.png`) });
          passed++;
        }
      } finally { await context.close(); }
    }
    assert.deepEqual(errors, []);
    console.log(`PASS=${passed} 三栏主题、DPI、独立字号、按钮排布、首行空侧与编辑范围验证通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
