// 只验证 Reset／Rollback 视觉稿的按钮状态，不执行 Git，不启动服务器。
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, executablePath, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath, headless: true });
  let passed = 0;
  try {
    await fs.mkdir(output, { recursive: true });
    for (const dpi of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ deviceScaleFactor: dpi, viewport: { width: 1180, height: 760 } });
      try {
        const page = await context.newPage();
        await page.route('https://unpkg.com/**', route => route.abort());
        for (const scene of ['reset', 'rollback']) for (const theme of ['light', 'dark']) {
          const url = pathToFileURL(path.resolve(__dirname, `../docs/ux-mockups/${scene}.html`));
          url.searchParams.set('theme', theme);
          await page.goto(url.href);
          await page.waitForSelector('body[data-typography-preview="ready"]');
          const cancel = page.locator('.dialog-footer .secondary-button');
          const run = page.locator('.dialog-footer .danger-button');
          const close = page.locator('.dialog-header .icon-button');
          const colors = theme === 'light'
            ? { panel: 'rgb(255, 255, 255)', hover: 'rgb(241, 242, 244)', danger: 'rgb(199, 68, 64)', accent: 'rgb(56, 113, 225)', disabled: 'rgb(245, 248, 254)' }
            : { panel: 'rgb(30, 31, 34)', hover: 'rgb(45, 47, 51)', danger: 'rgb(227, 122, 122)', accent: 'rgb(84, 138, 247)', disabled: 'rgb(37, 38, 42)' };
          const style = locator => locator.evaluate(node => {
            const css = getComputedStyle(node);
            return { background: css.backgroundColor, outline: css.outlineColor, outlineStyle: css.outlineStyle, radius: css.borderRadius };
          });
          const before = await cancel.boundingBox();
          assert.equal((await style(cancel)).background, colors.panel);
          assert.equal((await style(cancel)).radius, '5px');
          assert.equal((await style(run)).background, colors.danger);
          await cancel.hover();
          assert.equal((await style(cancel)).background, colors.hover);
          await cancel.focus();
          assert.equal((await style(cancel)).outline, colors.accent);
          assert.equal((await style(cancel)).outlineStyle, 'solid');
          await page.mouse.down();
          try { assert.equal((await style(cancel)).background, colors.hover); }
          finally { await page.mouse.move(0, 0); await page.mouse.up(); }
          await run.focus();
          assert.equal((await style(run)).outline, colors.accent);
          await run.evaluate(node => node.setAttribute('aria-disabled', 'true'));
          assert.equal((await style(run)).background, colors.disabled);
          assert.equal((await style(run)).outlineStyle, 'none');
          await run.evaluate(node => node.removeAttribute('aria-disabled'));
          await close.hover();
          assert.equal((await style(close)).background, colors.hover);
          await close.evaluate(node => node.setAttribute('aria-disabled', 'true'));
          assert.equal((await style(close)).background, colors.panel);
          await close.evaluate(node => node.removeAttribute('aria-disabled'));
          assert.deepEqual(await cancel.boundingBox(), before);
          if (dpi === 1) await page.screenshot({ path: path.join(output, `${scene}-${theme}.png`) });
          passed++;
        }
      } finally { await context.close(); }
    }
    console.log(`PASS=${passed} Reset／Rollback 两主题三种缩放的按钮状态通过；不代表整页交互或大字号布局通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
