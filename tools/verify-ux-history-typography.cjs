// 检查历史字号适配，使用已有 Playwright 和浏览器；完成后关闭浏览器，不启动服务器。
// 用法：node tools/verify-ux-history-typography.cjs <Playwright 路径> <浏览器路径> <证据目录>
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, executablePath, outputPath] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath, headless: true });
  let passed = 0;
  try {
    await fs.mkdir(outputPath, { recursive: true });
    for (const scale of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ deviceScaleFactor: scale });
      await context.route(/^https?:/, route => route.abort());
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', error => errors.push(error.message));
      for (const scene of ['git-history-graph', 'file-history']) {
        for (const theme of ['light', 'dark']) {
          for (const size of [13, 40]) {
            for (const width of [1024, 1645]) {
              const label = `${scene}-${theme}-${size}-${width}-${scale}`;
              await page.setViewportSize({ width, height: width === 1024 ? 640 : 900 });
              const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups', `${scene}.html`));
              url.searchParams.set('theme', theme);
              url.searchParams.set('ui-size', size);
              await page.goto(url.href);
              await page.waitForSelector('body[data-typography-preview="ready"]');
              const state = await page.evaluate(() => {
                const rect = element => { const r = element.getBoundingClientRect(); return { x: r.x, right: r.right, y: r.y, bottom: r.bottom, height: r.height, width: r.width }; };
                const height = element => {
                  const style = getComputedStyle(element), canvas = document.createElement('canvas').getContext('2d');
                  canvas.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
                  const m = canvas.measureText('国Ag');
                  return m.fontBoundingBoxAscent + m.fontBoundingBoxDescent;
                };
                const title = document.querySelector('.bottom-title');
                const tabs = [...document.querySelectorAll('.bottom-header .tool-tab')];
                const header = document.querySelector('.bottom-header');
                const logStyle = getComputedStyle(tabs[0]);
                const measure = document.createElement('canvas').getContext('2d');
                measure.font = `${logStyle.fontWeight} ${logStyle.fontSize} ${logStyle.fontFamily}`;
                const rootStyle = getComputedStyle(document.body);
                return {
                  header: {
                    inset: rect(title).x - rect(header).x,
                    titleGap: rect(tabs[0]).x - rect(title).right,
                    logWidth: rect(tabs[0]).width,
                    measuredLog: measure.measureText(tabs[0].textContent).width,
                    tabGap: tabs.length > 1 ? rect(tabs[1]).x - rect(tabs[0]).right : null,
                    tabs: tabs.map(tab => {
                      const style = getComputedStyle(tab);
                      return { padding: style.paddingLeft, border: style.borderLeftWidth, radius: style.borderRadius,
                        borderColor: style.borderLeftColor, background: style.backgroundColor,
                        active: tab.classList.contains('active') };
                    }),
                    activeBorder: rootStyle.getPropertyValue('--augit-border-strong').trim(),
                    activeBackground: rootStyle.getPropertyValue('--augit-panel-muted').trim(),
                  },
                  text: [...document.querySelectorAll('.bottom-header .tool-tab, .history-search input, .commit-row, .history-row, .log-ref-panel .tree-row, .changed-files .tree-row')]
                    .map(element => ({ box: rect(element), textHeight: height(element) })),
                  rows: [...document.querySelectorAll('.commit-row')].map(row => ({
                    box: rect(row), graph: rect(row.querySelector('svg')),
                    node: rect(row.querySelector('circle:last-child')),
                  })),
                  icons: [...document.querySelectorAll('.bottom-tool .icon-button svg, .log-filterbar svg:not(.commit-graph-svg)')].map(rect),
                  toolbars: [...document.querySelectorAll('.bottom-header, .history-toolbar')].map(bar => ({
                    bounds: rect(bar), children: [...bar.children].map(rect),
                  })),
                  historyPanes: [...document.querySelectorAll('.history-list-pane, .history-detail-pane > .diff-layout')].map(pane => ({
                    bounds: rect(pane), children: [...pane.children].map(rect),
                  })),
                  frame: {
                    workspace: rect(document.querySelector('.workspace')),
                    editor: rect(document.querySelector('.editor-area')),
                    bottom: rect(document.querySelector('.bottom-tool')),
                    status: rect(document.querySelector('.statusbar')),
                    minimum: parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--augit-bottom-min-height')) || 180,
                  },
                  overflow: document.documentElement.scrollWidth > innerWidth,
                };
              });
              assert.equal(state.overflow, false, `${label}：全局横向溢出。`);
              assert.ok(state.frame.bottom.height >= state.frame.minimum - 1, `${label}：底部工具窗口没有随字高扩展。`);
              assert.ok(state.frame.editor.bottom + 4 <= state.frame.bottom.y + 0.1, `${label}：底部工具窗口覆盖编辑区。`);
              assert.ok(state.frame.bottom.bottom <= state.frame.status.y + 0.1, `${label}：底部工具窗口覆盖状态栏。`);
              assert.ok(Math.abs(state.frame.bottom.bottom - state.frame.workspace.bottom) <= 0.1, `${label}：工具窗口越出主内容区。`);
              assert.equal(state.header.inset, 12, `${label}：Git 标题左留白必须统一为 12px。`);
              assert.equal(state.header.titleGap, 11, `${label}：标题到日志标签的间距不一致。`);
              assert.ok(Math.abs(state.header.logWidth - state.header.measuredLog - 22) < 0.1,
                `${label}：日志标签宽度应为字宽加左右内距及边框。`);
              if (state.header.tabGap !== null) assert.equal(state.header.tabGap, 4, `${label}：文件历史标签间距错误。`);
              for (const tab of state.header.tabs) {
                assert.equal(tab.padding, '10px');
                assert.equal(tab.border, '1px');
                assert.equal(tab.radius, '5px');
                if (tab.active) {
                  const normalize = value => {
                    const compact = value.replace(/\s+/g, '').toLowerCase();
                    const hex = compact.match(/^#([0-9a-f]{6})$/);
                    return hex ? `rgb(${parseInt(hex[1].slice(0, 2), 16)},${parseInt(hex[1].slice(2, 4), 16)},${parseInt(hex[1].slice(4, 6), 16)})` : compact;
                  };
                  assert.equal(normalize(tab.borderColor), normalize(state.header.activeBorder), `${label}：活动标签边框颜色不一致。`);
                  assert.equal(normalize(tab.background), normalize(state.header.activeBackground), `${label}：活动标签背景不一致。`);
                }
              }
              assert.ok(state.text.length > 0);
              for (const item of state.text) assert.ok(item.box.height >= item.textHeight, `${label}：文字被固定高度裁切。`);
              for (const row of state.rows) {
                assert.equal(row.graph.height, row.box.height, `${label}：提交图未适配行高。`);
                assert.ok(row.node.height <= 8.01 && row.node.width <= 8.01, `${label}：节点随字体被拉伸。`);
              }
              for (const icon of state.icons) assert.ok(icon.width <= 16.01 && icon.height <= 16.01, `${label}：图标随字体变大。`);
              for (const bar of state.toolbars) {
                for (const child of bar.children) assert.ok(child.right <= bar.bounds.right + 0.1 && child.bottom <= bar.bounds.bottom + 0.1,
                  `${label}：工具栏文字或按钮超出所属区域。`);
              }
              for (const pane of state.historyPanes) {
                assert.ok(pane.children[0].bottom <= pane.children[1].y + 0.1, `${label}：工具栏覆盖正文或历史行。`);
                assert.ok(pane.bounds.right <= width + 0.1, `${label}：详情被窗口裁切。`);
              }
              if (width === 1024 && scale === 1) await page.screenshot({ path: path.join(outputPath, `${label}.png`) });
              assert.deepEqual(errors, []);
              passed++;
            }
          }
        }
      }
      await context.close();
    }
    console.log(`通过 ${passed} 组历史字号、DPI、窗口和固定图标检查。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
