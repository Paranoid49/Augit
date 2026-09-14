// 离线验证视觉稿图标完整性；只使用本机浏览器，不启动服务器或下载依赖。
// 用法：node tools/verify-ux-offline-icons.cjs <Playwright 路径> <浏览器路径> <证据目录>
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

const root = path.resolve(__dirname, '../docs/ux-mockups');
const pendingNames = ['clone-pending', 'conflict-pending', 'rename-pending'];

async function inspect(page) {
  const result = await page.evaluate(() => {
    const icons = [...document.querySelectorAll('svg[data-augit-icon]')];
    return {
      unresolved: document.querySelectorAll('[data-lucide], svg.lucide').length,
      empty: icons.filter(element => !element.querySelector('path, circle, rect, ellipse')).map(element => element.dataset.augitIcon),
      grids: icons.filter(element => !['0 0 16 16', '0 0 12 12', '0 0 18 18'].includes(element.getAttribute('viewBox'))).map(element => element.dataset.augitIcon),
      inaccessible: icons.filter(element => element.getAttribute('aria-hidden') !== 'true').length,
      pending: [...document.querySelectorAll('[data-pending-icon]')].map(element => ({
        name: element.dataset.pendingIcon, text: (element.closest('.menu-item, .tree-row, .check-row') || element.parentElement).textContent.trim(),
      })),
      count: icons.length,
    };
  });
  assert.equal(result.unresolved, 0, '不能遗留需要第二次替换的图标占位。');
  assert.deepEqual(result.empty, [], '图形不能为空。');
  assert.deepEqual(result.grids, [], '只允许已登记的绘图网格。');
  assert.equal(result.inaccessible, 0);
  for (const item of result.pending) {
    assert.ok(pendingNames.includes(item.name), `意外的待校准图标：${item.name}`);
    assert.ok(item.text, `未校准图标必须保留文字动作：${item.name}`);
  }
  return result;
}

async function main() {
  const [modulePath, executablePath, output] = process.argv.slice(2);
  assert.ok(modulePath && executablePath && output, '请提供已有 Playwright、浏览器和证据目录。');
  const files = (await fs.readdir(root)).filter(name => name.endsWith('.html')).sort();
  for (const name of files) {
    const source = await fs.readFile(path.join(root, name), 'utf8');
    assert.doesNotMatch(source, /<script[^>]+src=["'](?:https?:)?\/\//i, `${name} 不得引用远端脚本。`);
  }
  for (const name of ['mockup.js', 'mockup.css']) {
    assert.doesNotMatch(await fs.readFile(path.join(root, name), 'utf8'), /lucide/i, `${name} 不得遗留图标库依赖。`);
  }
  await fs.mkdir(output, { recursive: true });
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(executablePath), headless: true });
  const report = { scenes: [], catalog: [], workflows: [], network: [], errors: [] };
  try {
    const context = await browser.newContext({ viewport: { width: 1180, height: 760 } });
    try {
      await context.route(/^https?:\/\//, route => {
        report.network.push(route.request().url());
        return route.abort();
      });
      const page = await context.newPage();
      page.on('pageerror', error => report.errors.push({ url: page.url(), message: error.message }));
      const open = async (name, theme) => {
        const url = pathToFileURL(path.join(root, name));
        url.search = new URLSearchParams({ theme, 'ui-size': '13' });
        await page.goto(url.href);
        if (name !== 'index.html') await page.waitForSelector('body[data-typography-preview="ready"]');
      };
      for (const theme of ['light', 'dark']) for (const name of files) {
        await open(name, theme);
        const result = await inspect(page);
        if (name !== 'index.html') assert.ok(result.count > 10, `${name} 主框架图标缺失。`);
        report.scenes.push({ name, theme, icons: result.count, pending: result.pending });
        if (['main-project.html', 'git-history.html'].includes(name)) {
          await page.screenshot({ path: path.join(output, `${name.slice(0, -5)}-${theme}.png`) });
        }
      }
      for (const theme of ['light', 'dark']) {
        await open('main-project.html', theme);
        for (let index = 0; index < 2; index++) {
          await page.locator('[data-action="menu"]').click();
          await inspect(page);
          assert.equal(await page.locator('.window-dot svg').count(), 3, '重建标题栏必须直接生成窗口图标。');
          assert.deepEqual(await page.locator('.window-dot').allTextContents(), ['', '', '']);
        }
        await open('git-history.html', theme);
        await page.locator('[data-history-path]').first().click();
        await page.locator('.changed-files').press('Enter');
        assert.equal(await page.locator('[data-history-comparison] svg[data-augit-icon="x"]').count(), 1);
        await page.locator('[data-history-path]').nth(1).click();
        await inspect(page);
        await page.locator('[data-history-path]').first().click({ button: 'right' });
        await inspect(page);
        assert.ok(await page.locator('.history-files-menu').isVisible(), '动态右键菜单必须保留可见动作。');
        assert.ok(await page.locator('.history-files-menu').textContent(), '无图标菜单仍须有文字动作。');
        await open('text-viewer.html', theme);
        await page.keyboard.press('Control+f');
        assert.equal(await page.locator('.current-find svg').count(), 6, '动态查找条的开关、导航和关闭必须完整。');
        await inspect(page);
        report.workflows.push(theme);
      }
    } finally { await context.close(); }

    for (const theme of ['light', 'dark']) for (const size of [13, 40]) for (const scale of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ viewport: { width: 1180, height: 760 }, deviceScaleFactor: scale });
      try {
        await context.route(/^https?:\/\//, route => { report.network.push(route.request().url()); return route.abort(); });
        const page = await context.newPage();
        page.on('pageerror', error => report.errors.push({ url: page.url(), message: error.message }));
        const url = pathToFileURL(path.join(root, 'main-project.html'));
        url.search = new URLSearchParams({ theme, 'ui-size': String(size) });
        await page.goto(url.href);
        await page.waitForSelector('body[data-typography-preview="ready"]');
        const result = await page.evaluate(() => {
          const probe = document.createElement('div');
          probe.style.cssText = 'display:flex;flex-wrap:wrap;position:fixed;inset:80px 20px auto;z-index:999;background:var(--augit-panel)';
          probe.innerHTML = Object.keys(toolbarIconShapes).map(name => `<button class="toolbar-button" aria-label="${name}">${icon(name)}</button>`).join('');
          document.body.append(probe);
          const errors = [];
          for (const button of probe.children) {
            const svg = button.firstElementChild, box = svg.getBBox(), bounds = svg.getBoundingClientRect();
            if (bounds.width !== 16 || bounds.height !== 16) errors.push(`${button.ariaLabel}：图形被字号或布局拉伸`);
            if (box.width + box.height <= 0 || box.x < 0 || box.y < 0 || box.x + box.width > 16.01 || box.y + box.height > 16.01) errors.push(`${button.ariaLabel}：无效几何边界`);
            for (const disabled of [false, true]) {
              button.disabled = disabled;
              if (getComputedStyle(svg).stroke !== getComputedStyle(button).color) errors.push(`${button.ariaLabel}：状态色未继承`);
            }
          }
          for (const name of ['missing-icon', '__proto__', 'toString']) {
            try { icon(name); errors.push(`${name}：未知名称未报错`); } catch { /* 未登记名称必须显式失败。 */ }
          }
          const count = probe.children.length;
          probe.remove();
          return { errors, count };
        });
        assert.deepEqual(result.errors, [], `${theme}/${size}/${scale}`);
        report.catalog.push({ theme, size, scale, icons: result.count });
      } finally { await context.close(); }
    }
    assert.deepEqual(report.errors, [], '不允许静默脚本错误。');
    assert.deepEqual(report.network, [], '断网运行时不应发出任何远端请求。');
    console.log(`离线图标：${report.scenes.length} 个场景/主题、${report.catalog.length} 个尺寸组合、${report.workflows.length} 个动态流程通过；远端请求 0。`);
  } finally {
    await browser.close();
    await fs.writeFile(path.join(output, 'results.json'), JSON.stringify(report, null, 2) + '\n');
  }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
