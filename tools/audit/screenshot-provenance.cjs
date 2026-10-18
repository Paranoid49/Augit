// 截图身份以实际DOM及可见特征校验；名称与URL本身不能证明画面内容。
const assert = require('node:assert/strict');
const crypto = require('node:crypto');

const features = {
  'main-project': '.markdown-document', 'text-viewer': '.code-view', 'go-to-line': '.compact-input',
  'markdown-preview': '.markdown-document', 'json-preview': '.json-document', 'image-preview': '.image-stage',
  'file-limit': '.info-state', 'image-error': '.info-state', 'commit-changes': '.changes-list', 'commit-empty': '.empty-tool-state',
  'commit-diff': '.diff-layout', 'diff-status': '.diff-layout', 'diff-boundary': '.diff-layout', 'diff-loading': '.diff-loading-columns',
  'git-history': '.git-toolbar-layout', 'git-history-empty': '.git-toolbar-layout', 'git-history-graph': '.commit-list-graph',
  'file-history': '.history-tool-content .history-columns', 'git-compare': '.diff-layout', 'git-compare-empty': '.comparison-notice',
  'history-diff-loading': '.diff-loading-columns', 'history-diff-failure': '.comparison-notice.failure', 'history-diff-cancelled': '.comparison-notice',
  'branches': '.popover .search-field', 'stash': '.stash-dialog', 'stash-drop-confirm': '.stash-drop-dialog',
  'worktrees': '.worktree-dialog', 'remote': '.remote-dialog', 'reset': '.reset-dialog', 'clone': '.clone-dialog',
  'push': '.push-dialog', 'push-no-remote': '.push-no-remote', 'quick-open': '.search-overlay',
  'quick-open-empty': '.search-overlay', 'repository-search': '.search-overlay.repository-mode', 'search-limited': '.search-overlay.repository-mode',
  'terminal': '.terminal-tool', 'terminal-close': '.terminal-tool + *, .dialog', 'settings': '.settings-layout',
  'settings-dirty': '.settings-layout .settings-dirty', 'settings-save-failure': '.settings-failure',
  'git-unavailable': '.rail-button[aria-disabled="true"]', 'operation-result': '.toast.error', 'operation-progress': '.progress-track',
  'workspace-open': '.dialog .management-content', 'repository-init': '.dialog', 'blame': '.blame-layout',
  'smart-checkout': '.dialog', 'rollback': '.rollback-dialog', 'conflict-list': '.dialog .changes-list',
  'conflict-resolver': '.conflict-columns', 'stash-manager': '.stash-manager-dialog',
  'project-context-menu': '.context-menu', 'changes-context-menu': '.context-menu', 'git-history-menu': '.context-menu',
};

async function inspectScreenshotSource(page, expected) {
  const selector = features[expected];
  assert.ok(selector, `场景缺少内容身份判据：${expected}`);
  const identity = await page.evaluate(() => ({
    scene: document.body.dataset.scene, title: document.title, url: location.href,
    theme: document.body.dataset.theme || 'light', ui: getComputedStyle(document.body).fontSize,
    code: getComputedStyle(document.documentElement).getPropertyValue('--augit-code'),
    codeSize: getComputedStyle(document.documentElement).getPropertyValue('--augit-code-size'),
    viewport: { width: innerWidth, height: innerHeight, dpr: devicePixelRatio },
  }));
  assert.equal(identity.scene, expected, '实际页面身份与拟保存场景必须一致');
  const candidates = page.locator(selector);
  assert.ok(await candidates.count() > 0, `截图缺少${expected}内容：${selector}`);
  assert.ok(await candidates.evaluateAll(nodes => nodes.some(node => {
    const r = node.getBoundingClientRect();
    return node.checkVisibility() && Math.min(r.right, innerWidth) > Math.max(r.left, 0)
      && Math.min(r.bottom, innerHeight) > Math.max(r.top, 0);
  })), `场景特征未进入截图可见区域：${expected}`);
  return { ...identity, feature: selector };
}

async function captureVerifiedScreenshot(page, expected, file, options = {}) {
  const source = await inspectScreenshotSource(page, expected);
  const png = await page.screenshot({ path: file, timeout: 10000, ...options });
  return { ...source, file, sha256: crypto.createHash('sha256').update(png).digest('hex'), bytes: png.length };
}
module.exports = { inspectScreenshotSource, captureVerifiedScreenshot };
