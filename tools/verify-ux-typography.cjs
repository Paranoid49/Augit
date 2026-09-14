// 使用现有 Playwright 和已安装浏览器检查主框架字号，不下载依赖，不启动服务器。
// 用法：node tools/verify-ux-typography.cjs <playwright 模块路径> <浏览器 exe 路径> [截图目录]
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const fs = require('node:fs/promises');

async function main() {
  const [modulePath, browserPath, outputPath] = process.argv.slice(2);
  assert.ok(modulePath && browserPath, '请传入现有 Playwright 模块和浏览器路径。');
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  let passed = 0;
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    if (outputPath) await fs.mkdir(outputPath, { recursive: true });
    for (const size of [9, 13, 19, 40]) {
      for (const theme of ['light', 'dark']) {
        for (const width of [1024, 1180]) {
          const label = `${theme}-${size}-${width}`;
          await page.setViewportSize({ width, height: width === 1024 ? 640 : 760 });
          const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/json-preview.html'));
          url.searchParams.set('theme', theme);
          url.searchParams.set('ui-size', String(size));
          await page.goto(url.href);
          await page.waitForSelector('body[data-typography-preview="ready"]');
          const result = await page.evaluate(() => {
            const rect = selector => {
              const bounds = document.querySelector(selector).getBoundingClientRect();
              return { top: bounds.top, bottom: bounds.bottom, left: bounds.left, right: bounds.right, height: bounds.height, width: bounds.width };
            };
            return {
              title: rect('.titlebar'), tab: rect('.editor-tab'), tabs: rect('.editor-tabs'),
              row: rect('.side-content.tree .tree-row'), header: rect('.side-tool .tool-header'),
              path: rect('.document-toolbar'), status: rect('.statusbar'), rail: rect('.rail-button'),
              codeSize: getComputedStyle(document.querySelector('.code-view')).fontSize,
              bodySize: getComputedStyle(document.body).fontSize,
              workspace: rect('.workspace-chip'), branch: rect('.branch-chip'), context: rect('.titlebar-context'),
              actions: rect('.window-actions'), rootWidth: document.documentElement.scrollWidth,
            };
          });
          assert.equal(result.bodySize, `${size}px`, label);
          assert.equal(result.codeSize, '13px', `${label} 等宽字号保持独立`);
          assert.equal(result.rail.height, 32, label);
          assert.equal(result.rail.width, 32, label);
          assert.equal(result.rootWidth, width, `${label} 不产生横向溢出`);
          assert.ok(result.workspace.right <= result.branch.left, `${label} 工作区与分支不重叠`);
          assert.ok(result.branch.right <= result.context.left, `${label} 分支与当前文件不重叠`);
          assert.ok(result.context.right <= result.actions.left, `${label} 当前文件不遮盖窗口操作`);
          assert.ok(result.tabs.bottom <= result.path.top, `${label} 标签与路径区域不重叠`);
          if (size === 13) {
            assert.equal(result.title.height, 44, label);
            assert.equal(result.tabs.height, 42, label);
            assert.equal(result.status.height, 22, label);
          }
          if (size === 40) {
            for (const area of ['title', 'tab', 'row', 'header', 'path', 'status'])
              assert.ok(result[area].height >= size, `${label} ${area} 容纳放大文字`);
          }
          if (outputPath && (size === 13 || size === 40) && width === 1024)
            await page.screenshot({ path: path.join(outputPath, `mockup-${label}.png`) });
          await page.getByRole('button', { name: '主菜单', exact: true }).click();
          const menu = await page.evaluate(() => {
            const items = [...document.querySelectorAll('.main-menu-entry')];
            return {
              items: items.map(element => {
                const bounds = element.getBoundingClientRect();
                const text = document.createRange();
                text.selectNodeContents(element);
                return { left: bounds.left, right: bounds.right, width: bounds.width, height: bounds.height, textWidth: text.getBoundingClientRect().width };
              }),
              actionsLeft: document.querySelector('.window-actions').getBoundingClientRect().left,
            };
          });
          assert.equal(menu.items.length, 5, label);
          for (let i = 0; i < menu.items.length; i++) {
            const item = menu.items[i];
            assert.ok(item.width >= item.textWidth + 15.9, `${label} 菜单文字完整`);
            if (i) assert.ok(menu.items[i - 1].right <= item.left, label);
          }
          assert.ok(menu.items.at(-1).right <= menu.actionsLeft, `${label} 菜单不遮盖窗口操作`);
          passed++;
        }
      }
    }
    assert.deepEqual(errors, [], '视觉稿不得有脚本错误。');
    console.log(`主框架字号视觉稿检查通过：${passed} 个场景。`);
  } finally {
    await browser.close();
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
