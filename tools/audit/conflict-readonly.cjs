// 正常入口验收：移除三栏冲突编辑器后，冲突列表、普通只读 Diff、
// 外部工具解决后的状态同步与 Continue/Skip/Abort 仍按真实 Git 状态工作。
// 只读检查，不点击接受/保存，不修改用户配置；本次实例与临时仓库在 finally 中清理。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/conflict-readonly');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function git(cwd, ...args) {
  return execute('git', ['-C', cwd, ...args], { windowsHide: true, timeout: 30000 });
}

// 冲突界面文案也必须是英文；夹具正文全英文，因此可直接扫描可见文本。
async function assertNoCjk(targetPage, report, label) {
  const body = await targetPage.evaluate(() => document.body.innerText);
  assert.equal(/[\u3400-\u9fff]/.test(body), false, label + ' 含中文界面文案');
  const attrs = await targetPage.evaluate(() => [...document.querySelectorAll('[title], [aria-label]')]
    .filter((node) => node.offsetWidth || node.offsetHeight || node.getClientRects().length)
    .flatMap((node) => [node.getAttribute('title'), node.getAttribute('aria-label')])
    .filter((value) => value && /[\u3400-\u9fff]/.test(value)));
  assert.deepEqual(attrs, [], label + ' 的 title/aria 含中文：' + attrs.join(' | '));
  report.checks.push({ name: label + '-cjk', bodyHasCjk: false, attrs });
}

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-conflict-readonly-'));
  const workspace = path.join(temporary, 'repo');
  const settings = path.join(temporary, 'settings.json');
  const listener = net.createServer();
  let port = 0;
  let shell = null;
  let browser = null;
  let page = null;
  const report = { launcher: 'tools/start-augit.ps1', forcedBuild: true, cdpAttached: true, checks: [] };
  try {
    // ---- 真实冲突仓库：同一文件在两条分支上修改后 merge ----
    await fs.mkdir(workspace);
    await git(workspace, 'init', '-b', 'main');
    await git(workspace, 'config', 'user.name', 'Augit Audit');
    await git(workspace, 'config', 'user.email', 'augit-audit@example.invalid');
    await fs.writeFile(path.join(workspace, 'conflict.txt'), 'line1\nbase\nline3\n');
    await git(workspace, 'add', 'conflict.txt');
    await git(workspace, 'commit', '-m', 'test: base');
    await git(workspace, 'checkout', '-b', 'feature');
    await fs.writeFile(path.join(workspace, 'conflict.txt'), 'line1\nfeature\nline3\n');
    await git(workspace, 'add', 'conflict.txt');
    await git(workspace, 'commit', '-m', 'test: feature');
    await git(workspace, 'checkout', 'main');
    await fs.writeFile(path.join(workspace, 'conflict.txt'), 'line1\nmain\nline3\n');
    await git(workspace, 'add', 'conflict.txt');
    await git(workspace, 'commit', '-m', 'test: main');
    let mergeFailed = false;
    try {
      await git(workspace, 'merge', 'feature');
    } catch (error) {
      mergeFailed = true;
    }
    assert.ok(mergeFailed, 'merge 必须因冲突而失败');
    const status = await git(workspace, 'status', '--porcelain');
    assert.match(status.stdout, /^UU conflict\.txt/m, '夹具必须处于真实冲突状态');

    // ---- 正常入口：使用默认启动器（强制非增量构建）并只复制真实配置 ----
    await fs.copyFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), settings).catch(async () => {
      await fs.writeFile(settings, '{}\n');
    });
    await new Promise((resolve) => listener.listen(0, '127.0.0.1', resolve));
    port = listener.address().port;
    await new Promise((resolve) => listener.close(resolve));
    const launch = require('./daily-launch-command.cjs')(process.cwd(), settings, port, workspace);
    const started = await execute(launch.file, launch.args, { env: launch.env, timeout: 180000 });
    const pid = Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
    assert.ok(Number.isSafeInteger(pid) && pid > 0, '日常启动器未返回实际 Augit PID');
    shell = pid;
    report.pid = pid;
    report.port = port;
    report.workspace = workspace;
    report.launchOutput = started.stdout.trim();
    await fs.writeFile(path.join(output, 'launch.json'), JSON.stringify({
      launcher: 'tools/start-augit.ps1', workspace, forcedBuild: true, port,
      copiedSettings: true, command: launch.args, output: started.stdout,
    }, null, 2));

    for (let index = 0; index < 160; index += 1) {
      try {
        browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
        break;
      } catch {
        await delay(250);
      }
    }
    assert.ok(browser, '无法连接正常入口 WebView2 调试端口');
    page = browser.contexts()[0].pages()[0];
    const pageErrors = [];
    page.on('pageerror', (error) => pageErrors.push('pageerror:' + String(error)));
    page.on('console', (message) => {
      if (message.type() === 'error') {
        const location = message.location();
        pageErrors.push('console:' + message.text() + (location && location.url ? ' @' + location.url : ''));
      }
    });
    page.on('requestfailed', (request) => {
      pageErrors.push('requestfailed:' + request.url() + ' ' + (request.failure()?.errorText || ''));
    });
    await page.waitForFunction(() => window.__augitReady === true, null, { timeout: 40000 });
    await page.waitForFunction(() => window.__augitGitReady === true, null, { timeout: 40000 });

    // ---- 实际加载产物身份、字体与系统缩放 ----
    const mapped = await execute(process.env.AUGIT_AUDIT_PYTHON || 'D:/Python/Python3119/python.exe',
      ['-B', 'tools/audit/read-process-mapped-images.py', String(pid)],
      { windowsHide: true, timeout: 15000, env: { ...process.env, PYTHONUTF8: '1' } });
    const mappedIdentity = JSON.parse(mapped.stdout);
    assert.equal(mappedIdentity.pid, pid);
    for (const name of ['Augit.dll', 'Augit.Infrastructure.dll']) {
      assert.ok(mappedIdentity.mappedImages.some((item) => item.name === name), '未找到已加载程序集：' + name);
    }
    await fs.writeFile(path.join(output, 'mapped-images.json'), JSON.stringify(mappedIdentity, null, 2));
    const runtime = await page.evaluate(() => ({
      dpr: devicePixelRatio,
      url: location.href,
      hasSceneQuery: new URL(location.href).searchParams.has('scene'),
      codeFont: getComputedStyle(document.querySelector('.diff-code, .code-view, body')).fontFamily,
      bodyFont: getComputedStyle(document.body).fontFamily,
      theme: document.documentElement.dataset.theme || null,
    }));
    await fs.writeFile(path.join(output, 'runtime.json'), JSON.stringify(runtime, null, 2));
    assert.equal(runtime.hasSceneQuery, false, '正常入口不得带审计场景参数');

    // ---- 冲突操作会话：列表、真实动作与无三栏编辑 ----
    await page.waitForSelector('.dialog.conflict-session-dialog', { timeout: 30000 });
    const list = await page.evaluate(() => {
      const dialog = document.querySelector('.dialog.conflict-session-dialog');
      return {
        title: dialog.getAttribute('aria-label'),
        rows: [...dialog.querySelectorAll('[data-conflict-path]')].map((row) => ({
          path: row.dataset.conflictPath, text: row.textContent.replace(/\s+/g, ' ').trim(),
        })),
        actions: [...dialog.querySelectorAll('.dialog-footer button')].map((button) => ({
          action: button.dataset.operationAction || null, disabled: button.disabled, text: button.textContent.trim(),
        })),
        columns: dialog.querySelectorAll('.conflict-columns, .conflict-column').length,
        save: dialog.querySelectorAll('[data-conflict-save]').length,
        editable: dialog.querySelectorAll('[contenteditable]').length,
      };
    });
    assert.equal(list.rows.length, 1, '冲突列表应显示 1 个冲突文件');
    assert.equal(list.rows[0].path, 'conflict.txt');
    assert.equal(list.columns, 0, '操作会话不应再渲染三栏');
    assert.equal(list.save, 0, '操作会话不应有保存入口');
    assert.equal(list.editable, 0, '操作会话不应有可编辑区');
    const abort = list.actions.find((item) => item.action === 'abort');
    const cont = list.actions.find((item) => /^Continue\b/.test(item.text));
    report.checks.push({ name: 'conflict-list', list });
    console.log('CONFLICT_LIST=' + JSON.stringify(list));
    assert.ok(abort && !abort.disabled, '进行中的 merge 必须提供 Abort');
    assert.ok(cont && cont.disabled, '仍有冲突时 Continue 必须禁用');
    await assertNoCjk(page, report, 'conflict-list');

    // ---- 点击冲突文件：普通只读 Diff + 冲突提示，不进入内置编辑 ----
    await page.locator('[data-conflict-path="conflict.txt"]').click();
    await page.waitForSelector('.diff-layout', { timeout: 20000 });
    await page.waitForSelector('.conflict-readonly-notice', { timeout: 20000 });
    await page.waitForFunction(() => {
      const live = window.__augitLive;
      return live && live.editor === 'diff' && live.diff && live.diff.path === 'conflict.txt';
    }, null, { timeout: 20000 });
    const diff = await page.evaluate(() => {
      const dialog = document.querySelector('.dialog.conflict-session-dialog');
      const notice = document.querySelector('.conflict-readonly-notice');
      return {
        dialogOpen: !!dialog,
        columns: document.querySelectorAll('.conflict-columns, .conflict-column').length,
        save: document.querySelectorAll('[data-conflict-save]').length,
        sideActions: document.querySelectorAll('[data-conflict-side], [data-conflict-nav], [data-conflict-whole]').length,
        editable: document.querySelectorAll('[contenteditable]').length,
        codeLines: document.querySelectorAll('.diff-code-line, .diff-line').length,
        notice: notice ? notice.textContent.replace(/\s+/g, ' ').trim() : null,
        tab: (window.__augitLive.tabs || []).find((item) => item.id === window.__augitLive.activeTabId)?.path,
      };
    });
    assert.equal(diff.dialogOpen, false, '打开只读 Diff 后会话窗口应收起');
    assert.equal(diff.columns, 0, '不应出现三栏');
    assert.equal(diff.save, 0, '不应出现冲突保存按钮');
    assert.equal(diff.sideActions, 0, '不应出现逐块/整侧接受动作');
    assert.equal(diff.editable, 0, '冲突文件必须是只读 Diff');
    assert.ok(diff.codeLines > 0, '只读 Diff 应有正文');
    assert.match(diff.notice || '', /Unresolved conflict/);
    assert.match(diff.notice || '', /does not provide a built-in merge editor/);
    await assertNoCjk(page, report, 'conflict-readonly-diff');
    const fonts = await page.evaluate(() => {
      const line = document.querySelector('.diff-code-line');
      const body = document.querySelector('.titlebar, .side-tool, body');
      return {
        codeFont: line ? getComputedStyle(line).fontFamily : null,
        codeSize: line ? getComputedStyle(line).fontSize : null,
        uiFont: body ? getComputedStyle(body).fontFamily : null,
        dpr: devicePixelRatio,
      };
    });
    assert.match(fonts.codeFont || '', /JetBrains Mono/, '只读 Diff 必须使用随包 JetBrains Mono');
    report.checks.push({ name: 'fonts', fonts });
    report.checks.push({ name: 'conflict-readonly-diff', diff });

    // ---- 移除的桥接端点必须真实不可用 ----
    const removed = await page.evaluate(async () => {
      const { invoke } = await import('/src/bridge.js');
      const result = {};
      for (const method of ['git/conflict-load', 'git/conflict-save', 'git/conflict-accept', 'git/conflict-words']) {
        try {
          await invoke(method, { path: 'conflict.txt', resultText: '', side: 'theirs', yours: '', result: '', theirs: '' }, 5000);
          result[method] = 'accepted';
        } catch (error) {
          result[method] = 'rejected:' + String(error && error.message || error);
        }
      }
      return result;
    });
    for (const [method, value] of Object.entries(removed)) {
      assert.notEqual(value, 'accepted', '已移除的端点仍可用：' + method);
    }
    report.checks.push({ name: 'removed-endpoints', removed });

    // ---- 外部工具解决（写文件 + git add）后，界面按真实状态同步 ----
    await page.locator('[data-open-conflict-session]').click();
    await page.waitForSelector('.dialog.conflict-session-dialog', { timeout: 20000 });
    await fs.writeFile(path.join(workspace, 'conflict.txt'), 'line1\nresolved by external tool\nline3\n');
    await git(workspace, 'add', 'conflict.txt');
    const syncStarted = Date.now();
    await page.waitForFunction(() => {
      const dialog = document.querySelector('.dialog.conflict-session-dialog');
      if (!dialog) return false;
      const rows = dialog.querySelectorAll('[data-conflict-path]');
      const continueButton = dialog.querySelector('[data-operation-action="continue"]');
      return rows.length === 0 && continueButton && continueButton.disabled === false;
    }, null, { timeout: 30000 });
    const syncMs = Date.now() - syncStarted;
    const synced = await page.evaluate(() => {
      const dialog = document.querySelector('.dialog.conflict-session-dialog');
      return {
        rows: [...dialog.querySelectorAll('[data-conflict-path]')].map((row) => row.dataset.conflictPath),
        summary: dialog.querySelector('.toolbar')?.textContent.replace(/\s+/g, ' ').trim(),
        actions: [...dialog.querySelectorAll('[data-operation-action]')].map((button) => ({
          action: button.dataset.operationAction, disabled: button.disabled, text: button.textContent.trim(),
        })),
        columns: dialog.querySelectorAll('.conflict-columns, .conflict-column').length,
      };
    });
    assert.deepEqual(synced.rows, [], '外部解决后冲突列表必须清空');
    assert.equal(synced.columns, 0);
    const syncedContinue = synced.actions.find((item) => item.action === 'continue');
    assert.ok(syncedContinue && !syncedContinue.disabled, '冲突全部解决后 Continue 必须可用');
    synced.syncMs = syncMs;
    report.checks.push({ name: 'external-resolution-sync', synced });
    console.log('SYNC_MS=' + syncMs);
    await assertNoCjk(page, report, 'conflict-continue-ready');

    // 显式 --abort-close：在临时仓库执行 Abort，验证操作对话关闭后主窗可正常关闭；默认只读路径不受影响。
    if (process.argv.includes('--abort-close')) {
      await page.evaluate(() => document.querySelector('.dialog.conflict-session-dialog [data-operation-action="abort"]')?.click());
      await page.waitForFunction(() => !document.querySelector('.dialog.conflict-session-dialog'), null, { timeout: 15000 });
      report.checks.push({ name: 'abort-close', aborted: true });
    }
    // 显式 --continue-close：临时仓库完成合并，验证 Continue 语义及结束后正常关窗；默认只读路径不受影响。
    if (process.argv.includes('--continue-close')) {
      const cont = page.locator('.dialog.conflict-session-dialog [data-operation-action="continue"]');
      await cont.click();
      await page.waitForFunction(() => !document.querySelector('.dialog.conflict-session-dialog'), null, { timeout: 30000 });
      report.checks.push({ name: 'continue-close', continued: true });
    }
    const runtimeErrors = await page.evaluate(() => ({
      error: window.__augitError || null,
      errors: Array.isArray(window.__augitErrors) ? window.__augitErrors : [],
    }));
    assert.equal(runtimeErrors.error, null, '正常入口不应记录未处理错误');
    assert.deepEqual(runtimeErrors.errors, [], '正常入口不应有页面内部错误');
    assert.deepEqual(pageErrors, [], '正常入口不应有页面异常：' + pageErrors.join(' | '));
    report.checks.push({ name: 'no-runtime-errors', runtimeErrors, pageErrors });

    await fs.writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2));
    console.log('CONFLICT_READONLY_OK pid=' + pid + ' port=' + port);
  } catch (error) {
    report.error = String(error && error.stack || error);
    await fs.writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2)).catch(() => {});
    console.error(report.error);
    process.exitCode = 1;
  } finally {
    try { if (browser) await browser.close(); } catch { /* 关闭失败仍继续清理 */ }
    if (shell) {
      try {
        await execute('powershell.exe', ['-NoProfile', '-Command',
          '$p=Get-Process -Id ' + shell + ' -ErrorAction Stop; ' +
          'if (-not $p.CloseMainWindow()) { throw "CloseMainWindow=false" }; ' +
          'if (-not $p.WaitForExit(8000)) { throw "still running" }'], { windowsHide: true, timeout: 20000 });
        report.closeMethod = 'CloseMainWindow';
      } catch {
        report.closeMethod = 'taskkill';
        try {
          await execute('taskkill', ['/PID', String(shell), '/T', '/F'], { windowsHide: true, timeout: 20000 });
        } catch { /* 已退出 */ }
      }
    }
    try {
      if (port) {
        let listening = true;
        for (let attempt = 0; attempt < 40 && listening; attempt += 1) {
          listening = await new Promise((resolve) => {
            const socket = net.connect({ port, host: '127.0.0.1' });
            const done = (value) => { socket.destroy(); resolve(value); };
            socket.once('connect', () => done(true));
            socket.once('error', () => done(false));
            socket.setTimeout(800, () => done(false));
          });
          if (listening) await delay(250);
        }
        report.portClosed = !listening;
      }
    } catch { report.portClosed = false; }
    try { await fs.rm(temporary, { recursive: true, force: true }); report.temporaryRemoved = true; }
    catch { report.temporaryRemoved = false; }
    await fs.writeFile(path.join(output, 'cleanup.json'), JSON.stringify(report, null, 2)).catch(() => {});
  }
})();
