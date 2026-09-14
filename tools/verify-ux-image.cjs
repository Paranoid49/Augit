// 使用共享 PNG 检查初始适应、缩放、移动、焦点和 DPI；不启动服务器。
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
    if (output) await fs.mkdir(output, { recursive: true });
    for (const dpi of [1, 1.25, 1.5]) {
      const context = await browser.newContext({ viewport: { width: 1024, height: 640 }, deviceScaleFactor: dpi });
      try {
        const page = await context.newPage();
        page.on('pageerror', error => errors.push(error.message));
        for (const theme of ['light', 'dark']) for (const size of [13, 40]) {
          const label = `${theme}-${size}-${dpi}`;
          const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/image-preview.html'));
          url.search = new URLSearchParams({ theme, 'ui-size': String(size) });
          await page.goto(url.href);
          await page.waitForSelector('.image-stage[data-ready="true"]');
          await page.waitForSelector('body[data-typography-preview="ready"]');
          const state = () => page.evaluate(() => {
            const stage = document.querySelector('.image-stage'), image = stage.querySelector('img');
            const a = stage.getBoundingClientRect(), b = image.getBoundingClientRect();
            return { width: a.width, height: a.height, left: b.left - a.left, top: b.top - a.top,
              imageWidth: b.width, imageHeight: b.height, natural: [image.naturalWidth, image.naturalHeight],
              scale: Number(stage.dataset.scale), fit: stage.dataset.fit, focus: document.activeElement.getAttribute('aria-label') };
          });
          const initial = await state();
          assert.deepEqual(initial.natural, [1920, 1200], label);
          assert.equal(initial.fit, 'true', label);
          assert.ok(initial.left >= 31 && initial.top >= 31, label);
          const expected = Math.min(1, (Math.round(initial.width * dpi) - Math.round(64 * dpi)) / 1920,
            (Math.round(initial.height * dpi) - Math.round(64 * dpi)) / 1200);
          assert.ok(Math.abs(expected - initial.scale) < .00001, label);
          assert.match(await page.locator('.editor-tab.active').innerText(), /image-sample\.png/, label);
          for (let i = 0; i < 4; i++) await page.getByRole('button', { name: '放大', exact: true }).click();
          let enlarged = await state();
          assert.equal(enlarged.focus, '放大');
          assert.equal(enlarged.fit, 'false');
          assert.ok(enlarged.imageWidth > enlarged.width);
          const stage = page.locator('.image-stage');
          await stage.focus();
          await page.keyboard.press('ArrowLeft');
          const moved = await state();
          assert.ok(moved.left > enlarged.left && moved.left <= 0, label);
          const rect = await stage.boundingBox();
          await page.mouse.move(rect.x + 100, rect.y + 100);
          await page.mouse.down(); await page.mouse.move(rect.x + 550, rect.y + 160); await page.mouse.up();
          assert.ok((await state()).left > moved.left, label);
          await page.mouse.move(rect.x + 200, rect.y + 100);
          await page.mouse.down();
          await page.keyboard.press('Escape');
          const cancelled = await state();
          await page.mouse.move(rect.x + 100, rect.y + 50);
          assert.equal((await state()).left, cancelled.left, label);
          assert.equal((await state()).top, cancelled.top, label);
          await page.mouse.up();
          await page.getByRole('button', { name: '适应区域', exact: true }).click();
          const restored = await state();
          assert.equal(restored.scale, initial.scale, label);
          assert.equal(restored.left, initial.left, label);
          assert.equal(restored.focus, '适应区域', label);
          while ((await state()).scale < 1) await page.getByRole('button', { name: '放大', exact: true }).click();
          const wheel = data => stage.dispatchEvent('wheel', { deltaMode: 0, ...data });
          for (let i = 0; i < 3; i++) await wheel({ deltaY: -10, ctrlKey: true });
          assert.equal((await state()).scale, 1, `${label} 小滚动不能每次跳档`);
          await wheel({ deltaY: -10, ctrlKey: true });
          assert.equal((await state()).scale, 1.25, label);
          await wheel({ deltaY: -120, ctrlKey: true });
          assert.equal((await state()).scale, 3, `${label} 快速三档缩放`);
          await wheel({ deltaY: 80, ctrlKey: true });
          assert.equal((await state()).scale, 1.5, label);
          await wheel({ deltaY: 0, ctrlKey: true });
          assert.equal((await state()).scale, 1.5, `${label} 零增量不缩放`);
          const beforeWheel = await state();
          for (let i = 0; i < 40; i++) await wheel({ deltaY: 1 });
          assert.ok(Math.abs((await state()).top - beforeWheel.top + 48) < .1, `${label} 保留细小平移量`);
          await wheel({ deltaX: 40 });
          assert.ok(Math.abs((await state()).left - beforeWheel.left + 48) < .1, `${label} 触控板横向移动`);
          await wheel({ deltaY: -3, deltaMode: 1, shiftKey: true });
          assert.ok(Math.abs((await state()).left - beforeWheel.left) < .1, `${label} Shift 行单位反向移动`);
          await wheel({ deltaY: -30, ctrlKey: true });
          await page.getByRole('button', { name: '适应区域', exact: true }).click();
          await wheel({ deltaY: -10, ctrlKey: true });
          assert.equal((await state()).fit, 'true', `${label} 适应动作清除缩放余量`);
          await page.getByRole('button', { name: '适应区域', exact: true }).click();
          await page.locator('.editor-tab.active').focus();
          if (output && dpi === 1) await page.screenshot({ path: path.join(output, `mockup-image-${label}.png`) });
          url.searchParams.set('image-state', 'loading');
          await page.goto(url.href);
          await page.waitForSelector('.image-stage[data-ready="true"]');
          await page.waitForSelector('body[data-typography-preview="ready"]');
          const progress = page.getByRole('status', { name: '' }).filter({ hasText: '正在读取文件…' });
          assert.equal(await progress.count(), 1, label);
          const progressBounds = await progress.boundingBox(), stageBounds = await page.locator('.image-stage').boundingBox();
          assert.ok(progressBounds.x >= stageBounds.x && progressBounds.y >= stageBounds.y, label);
          assert.ok(progressBounds.x + progressBounds.width <= stageBounds.x + stageBounds.width + 1, label);
          assert.ok(progressBounds.y + progressBounds.height <= stageBounds.y + stageBounds.height + 1, label);
          if (output && dpi === 1 && size === 13) await page.screenshot({ path: path.join(output, `mockup-image-loading-${label}.png`) });
          passed++;
        }
      } finally { await context.close(); }
    }
    assert.deepEqual(errors, []);
    console.log(`PASS=${passed} 图片连续操作及加载状态组合通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
