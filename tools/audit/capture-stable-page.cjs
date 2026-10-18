// CDP只采业务表面：拒绝原生背景捕获期间隐藏了表单的截图，不修改产品样式。
const fs = require('node:fs/promises');
const assert = require('node:assert/strict');
module.exports = async function captureStablePage(page, expected, output, timeout = 10000) {
  const guard = await page.evaluateHandle(selector => {
    let epoch = 0;
    const observer = new MutationObserver(records => { epoch += records.length; });
    observer.observe(document.documentElement, {subtree:true, attributes:true, attributeFilter:['data-native-capture','data-native-background-capture']});
    return {
      read() {
        epoch += observer.takeRecords().length;
        const node = document.querySelector(selector);
        let visible = !!node && node.getClientRects().length > 0;
        for (let current = node; visible && current; current = current.parentElement) {
          const style = getComputedStyle(current);
          visible = !current.hidden && style.visibility !== 'hidden' && style.display !== 'none' && Number(style.opacity) !== 0;
        }
        return {epoch, ready:visible && !window.__augitNativeBackgroundBusy && !document.querySelector('[data-native-capture],[data-native-background-capture]')};
      },
      close() { observer.disconnect(); }
    };
  }, expected);
  const deadline = Date.now() + timeout;
  let attempts = 0, rejected = 0;
  try {
    while (Date.now() < deadline) {
      const before = await guard.evaluate(value => value.read());
      if (!before.ready) { await page.waitForTimeout(50); continue; }
      attempts++;
      // 默认hide会改写所有输入框style，反而触发被测产品的背景刷新。
      const buffer = await page.screenshot({caret:'initial', timeout:Math.max(1, deadline-Date.now())});
      const after = await guard.evaluate(value => value.read());
      if (after.ready && after.epoch === before.epoch) {
        await fs.writeFile(output, buffer);
        return {attempts, rejected, captureEpoch:after.epoch, expected};
      }
      rejected++;
      await page.waitForTimeout(50);
    }
    assert.fail(`未取得稳定页面截图：${expected}，尝试${attempts}次，拒绝${rejected}次`);
  } finally {
    try { await guard.evaluate(value => value.close()); } finally { await guard.dispose(); }
  }
};
