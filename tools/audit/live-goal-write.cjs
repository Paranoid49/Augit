// 验证真实网页桥接在长时间 Git 写入与取消期间保持忙状态；页面由调用方 finally 释放。
const assert = require('node:assert/strict');

function pendingWriteHost() {
  const original = window.__hostStub;
  window.__goalWrites = 0;
  window.__goalCancels = 0;
  window.__hostStub = async (method, params) => {
    if (method === 'git/commit-create') {
      window.__goalWrites++;
      return new Promise(resolve => { window.__finishGoalWrite = resolve; });
    }
    if (method === 'write/cancel') {
      window.__goalCancels++;
      return new Promise(resolve => { window.__finishGoalCancel = resolve; });
    }
    return original(method, params);
  };
}

module.exports = async function verifyGoalWrite({ openScene, check }) {
  for (const outcome of ['success', 'cancel']) {
    const scene = await openScene('scene=commit-changes&theme=dark&no-session-restore=1', pendingWriteHost);
    const { page } = scene;
    try {
      await page.locator('.commit-box .message-field').fill('feat: long running hook');
      const include = page.locator('.change-file-row[data-path="src/App.cs"] [role="checkbox"]');
      if (await include.getAttribute('aria-checked') !== 'true') await include.click();
      await page.clock.install();
      await page.locator('.commit-actions .primary-button').first().click();
      await page.waitForFunction(() => window.__goalWrites === 1);
      await page.clock.fastForward(660000);
      const busy = await page.evaluate(() => ({
        action: window.__augitLive.writeOperation,
        requests: window.__goalWrites,
        error: window.__augitCommitError,
        message: document.querySelector('.commit-box .message-field').value,
        disabled: document.querySelector('.commit-actions .primary-button').getAttribute('aria-disabled'),
      }));
      assert.equal(busy.action, 'Commit', JSON.stringify(busy));
      assert.equal(busy.error, null, JSON.stringify(busy));
      assert.equal(busy.message, 'feat: long running hook');
      assert.equal(busy.disabled, 'true');
      await page.locator('.commit-actions .primary-button').first().dispatchEvent('click');
      assert.equal(await page.evaluate(() => window.__goalWrites), 1);
      check(`Git ${outcome} 场景超过十分钟仍保持忙态、草稿并阻止重复提交`, true);

      if (outcome === 'cancel') {
        await page.locator('[data-write-cancel]').click();
        await page.waitForFunction(() => window.__goalCancels === 1);
        await page.clock.fastForward(660000);
        assert.equal(await page.evaluate(() => window.__augitLive.writeCancelling), true);
        assert.equal(await page.evaluate(() => window.__augitLive.writeOperation), 'Commit');
        await page.locator('[data-write-cancel]').dispatchEvent('click');
        assert.equal(await page.evaluate(() => window.__goalCancels), 1);
        check('取消等待超过十分钟仍冻结、只发送一次取消、不伪造完成', true);
        await page.evaluate(() => {
          window.__finishGoalWrite({ available: true, committed: false, cancelled: true, reason: 'Operation cancelled.' });
          window.__finishGoalCancel({ available: true, cancelled: true });
        });
        await page.waitForFunction(() => !window.__augitLive.writeOperation && !window.__augitLive.writeCancelling);
        assert.equal(await page.locator('.commit-box .message-field').inputValue(), 'feat: long running hook');
        assert.equal(await page.evaluate(() => window.__augitCommitResult), null);
        check('宿主实际取消后解除忙态、保留草稿且不报告提交成功', true);
      } else {
        await page.evaluate(() => window.__finishGoalWrite({ available: true, committed: true, commitHash: 'long123' }));
        await page.waitForFunction(() => window.__augitCommitResult?.hash === 'long123');
        assert.equal(await page.evaluate(() => window.__augitLive.writeOperation), null);
        assert.equal(await page.locator('.commit-box .message-field').inputValue(), '');
        check('长时间等待后的真实成功应答仍可应用并清理草稿', true);
      }
      assert.deepEqual(scene.errors, []);
    } finally {
      await page.close();
    }
  }
};
