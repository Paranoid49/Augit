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
          // 危险样式只属于 Hard 一档：权威 `GitResetDialog.java:157-159` 按 MIXED → SOFT → HARD
          // 依次添加下拉项 ⇒ 打开时默认落在 MIXED（非破坏性，`GitBundle.properties:1205-1206`）。
          // 原断言直接抓 `.danger-button`，编码的是"打开即 Hard"的旧默认（第 143 轮按权威改正默认后暴露）。
          if (scene === 'reset') await page.selectOption('#reset-mode', 'hard');
          const cancel = page.locator('.dialog-footer .secondary-button');
          const run = page.locator('.dialog-footer .danger-button');
          const close = page.locator('.dialog-header .icon-button');
          const colors = theme === 'light'
            ? { panel: 'rgb(255, 255, 255)', hover: 'rgba(0, 0, 0, 0.07)', pressed: 'rgba(0, 0, 0, 0.125)', danger: 'rgb(197, 78, 88)' /* 权威 `accent-error-bg` = #C54E58（第 109 轮核对；原 #C74440 无依据） */, accent: 'rgb(56, 113, 225)', disabled: 'rgb(247, 248, 249)' /* 禁用底取 `--augit-panel-muted`：浅 = Islands `*.disabledBackground` = `dialog-bg` = `gray-160` #F7F8F9（第 131 轮订正；原 #F5F8FE = Blue13 无依据） */ }
            : { panel: 'rgb(30, 31, 34)', hover: 'rgba(255, 255, 255, 0.086)', pressed: 'rgba(255, 255, 255, 0.15)', danger: 'rgb(219, 92, 92)' /* 权威 expUI_dark `dangerBackground` = Red7 #DB5C5C（ManyIslandsDark 的 `dangerBackground` 同值；原 #E37A7A 在任何调色板里都查不到） */, accent: 'rgb(53, 116, 240)' /* 权威 accent-brand-bg 深色 = Blue6 #3574F0（原 Blue8 #548AF7 无依据） */, disabled: 'rgb(43, 45, 48)' /* expUI_dark 通配 `*.disabledBackground` = Gray2 #2B2D30（原 #25262A 无依据） */ };
          const style = locator => locator.evaluate(node => {
            const css = getComputedStyle(node);
            return { background: css.backgroundColor, outline: css.outlineColor, outlineStyle: css.outlineStyle, radius: css.borderRadius };
          });
          const before = await cancel.boundingBox();
          assert.equal((await style(cancel)).background, colors.panel);
          assert.equal((await style(cancel)).radius, '3px');   // 权威 Button.arc = 6 ⇒ 半径 3（原 5px 无依据）
          assert.equal((await style(run)).background, colors.danger);
          await cancel.hover();
          assert.equal((await style(cancel)).background, colors.hover);
          await cancel.focus();
          assert.equal((await style(cancel)).outline, colors.accent);
          assert.equal((await style(cancel)).outlineStyle, 'solid');
          await page.mouse.down();
          // 按下是独立一档（`--augit-pressed`），不再沿用悬停色。
          try { assert.equal((await style(cancel)).background, colors.pressed); }
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
