const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const capture = require('./capture-stable-page.cjs');
test('截图必须跨整个采样时段保持表单可见', async t => {
  const {chromium} = require(process.env.AUGIT_PLAYWRIGHT);
  const browser = await chromium.launch({executablePath:process.env.AUGIT_BROWSER_EXECUTABLE, headless:true});
  const temp = await fs.mkdtemp(path.join(os.tmpdir(),'Augit-stable-capture-test-'));
  try {
    const page = await browser.newPage();
    await page.setContent('<style>[data-native-background-capture]{visibility:hidden}</style><section class="dialog">可见表单</section>');
    await t.test('正常截图保存原始PNG', async () => {
      const output = path.join(temp,'stable.png');
      const result = await capture(page,'.dialog',output);
      assert.equal(result.attempts,1);
      assert.equal(result.rejected,0);
      assert.equal((await fs.readFile(output)).subarray(1,4).toString(),'PNG');
    });
    await t.test('截图前后均恢复正常也拒绝中途隐藏帧', async () => {
      let calls = 0, rejectedBuffer;
      const wrapped = {
        evaluateHandle:page.evaluateHandle.bind(page),
        waitForTimeout:page.waitForTimeout.bind(page),
        async screenshot(options) {
          if (++calls !== 1) return page.screenshot(options);
          await page.evaluate(() => document.querySelector('.dialog').dataset.nativeBackgroundCapture='true');
          rejectedBuffer = await page.screenshot(options);
          await page.evaluate(() => delete document.querySelector('.dialog').dataset.nativeBackgroundCapture);
          return rejectedBuffer;
        }
      };
      const output = path.join(temp,'recovered.png');
      const result = await capture(wrapped,'.dialog',output);
      assert.equal(result.attempts,2);
      assert.equal(result.rejected,1);
      assert.ok(!(await fs.readFile(output)).equals(rejectedBuffer));
    });
    await t.test('取证不改写后台输入框样式', async () => {
      await page.evaluate(() => {
        const input = document.createElement('input');
        document.body.append(input);
        window.styleMutations = 0;
        window.styleProbe = new MutationObserver(records => window.styleMutations += records.length);
        window.styleProbe.observe(input,{attributes:true,attributeFilter:['style']});
      });
      await capture(page,'.dialog',path.join(temp,'no-style-mutation.png'));
      assert.equal(await page.evaluate(() => window.styleMutations),0);
      await page.evaluate(() => window.styleProbe.disconnect());
    });
    await t.test('无布局包装层须以内部实际表单作为取证目标', async () => {
      await page.evaluate(() => {
        const wrapper = document.createElement('section');
        wrapper.className = 'overlay-layer';
        wrapper.style.display = 'contents';
        document.body.append(wrapper);
        wrapper.append(document.querySelector('.dialog'));
      });
      const output = path.join(temp,'wrapper.png');
      await assert.rejects(capture(page,'.overlay-layer',output,150),/未取得稳定页面截图/);
      await assert.rejects(fs.access(output));
      const result = await capture(page,'.overlay-layer .dialog',output);
      assert.equal(result.attempts,1);
    });
    await t.test('持续隐藏超时且不写成功截图', async () => {
      await page.evaluate(() => document.querySelector('.dialog').dataset.nativeBackgroundCapture='true');
      const output = path.join(temp,'invalid.png');
      await assert.rejects(capture(page,'.dialog',output,150),/未取得稳定页面截图/);
      await assert.rejects(fs.access(output));
    });
  } finally {
    await browser.close();
    await fs.rm(temp,{recursive:true,force:true});
  }
});
