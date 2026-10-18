// 冲突处理（2026-10-17 移除三栏编辑器后）：列表、只读 Diff、外部解决后的状态同步。
// 由调用方负责浏览器/服务生命周期；本文件不启动实例、不写文件。
module.exports = async function verifyGoalConflict({ openScene, check }) {
  const scene = await openScene('scene=commit-changes&theme=dark');
  const { page } = scene;
  await page.waitForFunction('window.__augitGitReady === true', null, { timeout: 20000 });

  // 注入进行中的 merge、两个冲突文件，以及一条未合并状态（只读 Diff 横幅依赖它）。
  await page.evaluate(() => {
    window.__liveFiles = [{
      path: 'src/App.cs', name: 'App.cs', directory: 'src', group: 'Changes',
      kind: 'Unmerged', staged: false, workingTree: true,
    }];
    window.__operationSession = {
      kind: 'Merge', inProgress: true, hasConflicts: true, branch: 'dsh',
      canContinue: false, canSkip: false, canAbort: true, supportsContinue: true,
      currentStep: 1, totalSteps: 1,
      conflicts: [
        { path: 'src/App.cs', hasAncestor: true, hasYours: true, hasTheirs: true },
        { path: 'src/Other.cs', hasAncestor: true, hasYours: true, hasTheirs: true },
      ],
    };
    window.__statusOperation = 'Merge';
    window.__statusConflicts = true;
    window.__nextChanges = { files: [], gitMetadata: true };
    window.__augitLive.operationSessionShown = false;
  });
  await page.evaluate(() => window.__augitLoadOperation());
  await page.waitForSelector('.dialog.conflict-session-dialog [data-conflict-path="src/App.cs"]', { timeout: 10000 });
  // 让真实状态路径也带上未合并文件（只读 Diff 横幅依赖 status.files 的 kind=Unmerged）。
  await page.evaluate(() => window.__hostPush('workspace-changed', { files: [], gitMetadata: true }));
  await page.waitForFunction(() => (window.__augitLive.status?.files || [])
    .some((file) => file.path === 'src/App.cs' && file.kind === 'Unmerged'), null, { timeout: 10000 });

  const list = await page.evaluate(() => {
    const dialog = document.querySelector('.dialog.conflict-session-dialog');
    const continueButton = [...dialog.querySelectorAll('button')].find((button) => button.textContent.includes('Continue'));
    return {
      title: dialog.getAttribute('aria-label'),
      rows: [...dialog.querySelectorAll('[data-conflict-path]')].map((row) => row.dataset.conflictPath),
      columns: dialog.querySelectorAll('.conflict-columns, .conflict-column').length,
      save: dialog.querySelectorAll('[data-conflict-save]').length,
      editable: dialog.querySelectorAll('[contenteditable]').length,
      sideActions: dialog.querySelectorAll('[data-conflict-side], [data-conflict-whole]').length,
      continueDisabled: continueButton ? continueButton.disabled : null,
      blocked: (dialog.querySelector('.conflict-blocked') || {}).textContent || null,
    };
  });
  check('§7.13 冲突操作会话只显示列表与真实动作，不再有三栏/保存/编辑: ' + JSON.stringify(list),
    list.title === 'Merge Conflicts' && JSON.stringify(list.rows) === JSON.stringify(['src/App.cs', 'src/Other.cs'])
      && list.columns === 0 && list.save === 0 && list.editable === 0 && list.sideActions === 0
      && list.continueDisabled === true && /Remaining: 2 unresolved conflicts/.test(list.blocked || ''));

  // 点击冲突文件：普通只读 Diff + 冲突提示，会话窗口收起。
  await page.locator('[data-conflict-path="src/App.cs"]').click();
  await page.waitForSelector('.diff-layout', { timeout: 10000 });
  await page.waitForSelector('.conflict-readonly-notice', { timeout: 10000 });
  const diff = await page.evaluate(() => {
    const notice = document.querySelector('.conflict-readonly-notice');
    return {
      dialogOpen: !!document.querySelector('.dialog.conflict-session-dialog'),
      columns: document.querySelectorAll('.conflict-columns, .conflict-column').length,
      save: document.querySelectorAll('[data-conflict-save]').length,
      editable: document.querySelectorAll('[contenteditable]').length,
      sideActions: document.querySelectorAll('[data-conflict-side], [data-conflict-whole], [data-conflict-nav]').length,
      lines: document.querySelectorAll('.diff-code-line').length,
      notice: notice ? notice.textContent.replace(/\s+/g, ' ').trim() : null,
      editor: window.__augitLive.editor,
      diffPath: window.__augitLive.diff && window.__augitLive.diff.path,
    };
  });
  check('§7.13 点击冲突文件打开普通只读 Diff，不进入内置编辑: ' + JSON.stringify(diff),
    diff.dialogOpen === false && diff.columns === 0 && diff.save === 0 && diff.editable === 0
      && diff.sideActions === 0 && diff.lines > 0 && diff.editor === 'diff' && diff.diffPath === 'src/App.cs'
      && /Unresolved conflict/.test(diff.notice || '') && /does not provide a built-in merge editor/.test(diff.notice || ''));

  // 横幅返回冲突列表：窗口重新读取真实会话（不是打开已删除的解决器）。
  await page.locator('[data-open-conflict-session]').click();
  await page.waitForSelector('.dialog.conflict-session-dialog [data-conflict-path="src/App.cs"]', { timeout: 10000 });
  const reopened = await page.evaluate(() => ({
    columns: document.querySelectorAll('.conflict-columns, .conflict-column').length,
    rows: [...document.querySelectorAll('.dialog.conflict-session-dialog [data-conflict-path]')].length,
  }));
  check('§7.13 只读 Diff 的冲突横幅可返回冲突列表: ' + JSON.stringify(reopened), reopened.rows === 2 && reopened.columns === 0);

  // 外部工具解决一个文件：宿主推送 gitMetadata，列表就地更新且不重建为三栏。
  const partial = await page.evaluate(async () => {
    window.__operationSession.conflicts = window.__operationSession.conflicts.slice(0, 1);
    const started = performance.now();
    window.__hostPush('workspace-changed', { files: [], gitMetadata: true });
    while (performance.now() - started < 2000) {
      await new Promise((resolve) => setTimeout(resolve, 10));
      if (document.querySelectorAll('.dialog.conflict-session-dialog [data-conflict-path]').length === 1) break;
    }
    return {
      elapsed: Math.round(performance.now() - started),
      rows: document.querySelectorAll('.dialog.conflict-session-dialog [data-conflict-path]').length,
      summary: (document.querySelector('.dialog.conflict-session-dialog .toolbar strong') || {}).textContent || null,
      columns: document.querySelectorAll('.conflict-columns, .conflict-column').length,
    };
  });
  check('§7.13 外部解决一个文件后列表在 500ms 内更新且保持只读: ' + JSON.stringify(partial),
    partial.rows === 1 && partial.summary === '1 conflicting file' && partial.columns === 0 && partial.elapsed < 500);

  // 全部解决：冲突行清空，Continue 按真实状态变为可用。
  await page.evaluate(() => {
    window.__operationSession.conflicts = [];
    window.__operationSession.hasConflicts = false;
    window.__operationSession.canContinue = true;
    window.__statusConflicts = false;
    window.__nextChanges = { files: [], gitMetadata: true };
    window.__hostPush('workspace-changed', { files: [], gitMetadata: true });
  });
  await page.waitForFunction(() => {
    const dialog = document.querySelector('.dialog.conflict-session-dialog');
    if (!dialog) return false;
    const summary = (dialog.querySelector('.toolbar strong') || {}).textContent || '';
    const button = dialog.querySelector('[data-operation-action="continue"]');
    return summary === 'No unresolved conflicts' && button && button.disabled === false
      && dialog.querySelectorAll('[data-conflict-path]').length === 0;
  }, null, { timeout: 10000 }).catch(() => {});
  const resolved = await page.evaluate(() => {
    const dialog = document.querySelector('.dialog.conflict-session-dialog');
    return {
      summary: dialog ? (dialog.querySelector('.toolbar strong') || {}).textContent || null : null,
      rows: dialog ? dialog.querySelectorAll('[data-conflict-path]').length : null,
      continueDisabled: dialog && dialog.querySelector('[data-operation-action="continue"]')
        ? dialog.querySelector('[data-operation-action="continue"]').disabled : null,
      columns: dialog ? dialog.querySelectorAll('.conflict-columns, .conflict-column').length : null,
    };
  });
  check('§7.13 外部解决全部冲突后列表清空且 Continue 可用: ' + JSON.stringify(resolved),
    resolved.summary === 'No unresolved conflicts' && resolved.rows === 0
      && resolved.continueDisabled === false && resolved.columns === 0);

  // 已删除的解决器端点不应再被页面调用（桩数组不再存在）。
  const removedCalls = await page.evaluate(() => ({
    saves: window.__conflictSaves,
    loads: window.__conflictLoads,
    accepts: window.__conflictAccepts,
    words: window.__conflictWords,
  }));
  check('§7.14 页面不再调用已移除的冲突读写端点: ' + JSON.stringify(removedCalls),
    removedCalls.saves === undefined && removedCalls.loads === undefined
      && removedCalls.accepts === undefined && removedCalls.words === undefined);

  await scene.page.close();
};
