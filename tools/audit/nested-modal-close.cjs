// 正常入口真实嵌套模态：Push → Define remote，关闭子窗后返回同一 Push 窗口。
// 只驱动真实 DOM 和真实桥接，不注入表单、不扣留应答、不使用场景参数。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const { waitAsyncCondition } = require('./wait-async-condition.cjs');
const { validateNestedModalLifecycle } = require('./nested-modal-close-state.cjs');

async function git(cwd, ...args) {
  return execute('git', ['-C', cwd, ...args], { windowsHide: true, timeout: 30000 });
}

async function invoke(page, method, params = {}) {
  return page.evaluate(async ({ method, params }) => {
    const { invoke } = await import('/src/bridge.js');
    return invoke(method, params, 0);
  }, { method, params });
}

(async () => {
  const { chromium } = require(process.argv[2]);
  const output = path.resolve(process.argv[3] || 'artifacts/nested-modal-close');
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-nested-modal-'));
  const workspace = path.join(temporary, 'repo');
  const settings = path.join(temporary, 'settings.json');
  const listener = net.createServer();
  let port = 0;
  let pid = 0;
  let browser = null;
  let closedNormally = false;
  const report = {
    launcher: 'tools/start-augit.ps1',
    forcedBuild: true,
    cdpAttached: true,
    scenario: '真实 Push 窗口打开真实 Define remote 嵌套窗口，关闭子窗后检查父窗身份与草稿',
    checks: []
  };
  const save = (name, value) => fs.writeFile(path.join(output, name), JSON.stringify(value, null, 2) + '\n');
  try {
    await fs.mkdir(workspace);
    await git(workspace, 'init', '-b', 'main');
    await git(workspace, 'config', 'user.name', 'Augit Nested Modal');
    await git(workspace, 'config', 'user.email', 'augit-nested@example.invalid');
    await fs.writeFile(path.join(workspace, 'readme.txt'), 'nested modal fixture\n');
    await git(workspace, 'add', '.');
    await git(workspace, 'commit', '-m', 'test: nested modal fixture');
    await fs.copyFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), settings).catch(async () => {
      await fs.writeFile(settings, '{}\n');
    });
    await new Promise((resolve) => listener.listen(0, '127.0.0.1', resolve));
    port = listener.address().port;
    await new Promise((resolve) => listener.close(resolve));

    const launch = require('./daily-launch-command.cjs')(process.cwd(), settings, port, workspace);
    const started = await execute(launch.file, launch.args, { env: launch.env, timeout: 180000 });
    pid = Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
    assert.ok(Number.isSafeInteger(pid) && pid > 0, '日常启动器未返回实际 Augit PID');
    report.pid = pid;
    report.port = port;
    report.workspace = workspace;
    await save('launch.json', {
      launcher: 'tools/start-augit.ps1', workspace, forcedBuild: true, port,
      copiedSettings: true, command: launch.args, output: started.stdout
    });

    const mapped = await execute(process.env.AUGIT_AUDIT_PYTHON || 'D:/Python/Python3119/python.exe',
      ['-B', 'tools/audit/read-process-mapped-images.py', String(pid)],
      { windowsHide: true, timeout: 15000, env: { ...process.env, PYTHONUTF8: '1' } });
    const mappedIdentity = JSON.parse(mapped.stdout);
    assert.equal(mappedIdentity.pid, pid);
    await save('mapped-images.json', mappedIdentity);

    for (let index = 0; index < 160; index += 1) {
      try { browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`); break; }
      catch { await delay(250); }
    }
    assert.ok(browser, '无法连接正常入口 WebView2 调试端口');
    const page = browser.contexts()[0].pages()[0];
    const pageErrors = [];
    page.on('pageerror', (error) => pageErrors.push('pageerror:' + String(error)));
    page.on('console', (message) => {
      if (message.type() === 'error') {
        const location = message.location();
        pageErrors.push('console:' + message.text() + (location && location.url ? ' @' + location.url : ''));
      }
    });
    page.on('requestfailed', (request) => pageErrors.push('requestfailed:' + request.url() + ' ' + (request.failure()?.errorText || '')));
    page.setDefaultTimeout(30000);
    await page.waitForFunction(() => window.__augitReady && window.__augitGitReady && !window.__augitLive.sessionRestoring);
    await waitAsyncCondition(async () => !(await invoke(page, 'window/modal-state')).active, { label: '启动模态清空' });
    report.runtime = await page.evaluate(() => ({
      url: location.href,
      dpr: devicePixelRatio,
      theme: document.body.dataset.theme || 'light',
      font: getComputedStyle(document.body).fontFamily
    }));

    // 真实用户入口：标题栏分支弹层 → Push；无远端时 Push 内显示 Define remote。
    await page.locator('.titlebar .branch-chip').click();
    await page.waitForSelector('.branches-popover [data-popover-action="push"]');
    await page.locator('.branches-popover [data-popover-action="push"]').click();
    await page.waitForSelector('.dialog.push-dialog');
    await waitAsyncCondition(async () => {
      const state = await invoke(page, 'window/modal-state');
      return state.active && state.depth === 1;
    }, { label: 'Push 原生模态呈现' });
    const pushDialog = page.locator('.dialog.push-dialog');
    const pushHandle = await pushDialog.elementHandle();
    const parentState = await invoke(page, 'window/modal-state');
    const parentLast = await page.evaluate(() => window.__augitNativeModalLastState);
    assert.ok(parentState.active && parentState.depth === 1, 'Push 必须是单层活动模态');
    assert.ok(parentState.window > 0, 'Push 必须有原生 HWND');
    const defineLink = pushDialog.locator('a[href$="remote.html"]');
    await defineLink.waitFor();
    report.checks.push({ name: 'push-parent', state: parentState, lastState: parentLast });

    // 打开真实嵌套远端管理窗口。
    await defineLink.click();
    await page.waitForSelector('.dialog.remote-dialog');
    await waitAsyncCondition(async () => {
      const state = await invoke(page, 'window/modal-state');
      return state.active && state.depth === 2;
    }, { label: '远端嵌套模态呈现' });
    const remoteState = await invoke(page, 'window/modal-state');
    const remoteLast = await page.evaluate(() => window.__augitNativeModalLastState);
    assert.equal(await page.locator('.dialog.push-dialog').count(), 1, '父 Push 表单必须保留');
    assert.equal(await page.locator('.dialog.remote-dialog').count(), 1, '恰好一个远端子表单');
    report.checks.push({ name: 'remote-child', state: remoteState, lastState: remoteLast });

    // 子窗的正常关闭动作（远端管理器唯一动作 OK），应恢复同一个父 Push 窗口且不重建父表单。
    await page.locator('.remote-window [data-remote-action="ok"]').click();
    await waitAsyncCondition(async () => {
      const state = await invoke(page, 'window/modal-state');
      return state.active && state.depth === 1 && state.window === parentState.window;
    }, { label: '关闭远端后返回 Push' });
    const restoredState = await invoke(page, 'window/modal-state');
    const pushStillSame = await pushHandle.evaluate((node) => node === document.querySelector('.dialog.push-dialog'));
    assert.equal(pushStillSame, true, '返回时不得重建 Push 表单');
    assert.equal(await page.locator('.dialog.remote-dialog').count(), 0, '子表单必须已移除');
    assert.equal(await page.locator('.dialog.push-dialog [data-push-action="cancel"]').count(), 1, '返回后 Push 动作必须仍可用');
    report.checks.push({ name: 'nested-return', state: restoredState, samePushNode: pushStillSame });

    // 关闭父 Push，检查模态完全释放。
    await page.locator('.dialog.push-dialog [data-push-action="cancel"]').click();
    await waitAsyncCondition(async () => {
      const state = await invoke(page, 'window/modal-state');
      return !state.active && state.ownerEnabled === true && state.snapshotCount === 0;
    }, { label: 'Push 关闭并释放' });
    const closedState = await invoke(page, 'window/modal-state');
    assert.equal(closedState.captureCoverWindow, 0);
    report.lifecycle = validateNestedModalLifecycle({
      parent: parentState, remote: remoteState, restored: restoredState, closed: closedState
    });
    const runtimeErrors = await page.evaluate(() => ({ error: window.__augitError || null, errors: window.__augitErrors || [] }));
    assert.equal(runtimeErrors.error, null, '正常入口不应记录未处理错误');
    assert.deepEqual(runtimeErrors.errors, [], '正常入口不应有页面内部错误');
    assert.deepEqual(pageErrors, [], '正常入口不应有页面异常：' + pageErrors.join(' | '));
    report.checks.push({ name: 'push-close', state: closedState, runtimeErrors, pageErrors });
    await save('report.json', report);

    if (browser) { await browser.close(); browser = null; }
    await execute('powershell.exe', ['-NoProfile', '-Command',
      '$p=Get-Process -Id ' + pid + ' -ErrorAction Stop; if (-not $p.CloseMainWindow()) { throw "CloseMainWindow=false" }; if (-not $p.WaitForExit(8000)) { throw "still running" }'],
      { windowsHide: true, timeout: 20000 });
    closedNormally = true;
    report.closeMethod = 'CloseMainWindow';
    console.log('NESTED_MODAL_CLOSE_OK pid=' + pid + ' port=' + port);
  } catch (error) {
    report.error = String(error && error.stack || error);
    await save('report.json', report).catch(() => {});
    console.error(report.error);
    process.exitCode = 1;
  } finally {
    try { if (browser) await browser.close(); } catch { /* 继续清理 */ }
    if (pid && !closedNormally) {
      try {
        await execute('powershell.exe', ['-NoProfile', '-Command',
          '$p=Get-Process -Id ' + pid + ' -ErrorAction SilentlyContinue; if ($p -and $p.CloseMainWindow()) { [void]$p.WaitForExit(5000) }'],
          { windowsHide: true, timeout: 15000 });
      } catch { /* 继续核实 */ }
      try {
        await execute('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true, timeout: 20000 });
      } catch { /* 已退出 */ }
      report.closeMethod = 'taskkill';
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
    await save('cleanup.json', report).catch(() => {});
  }
})().catch((error) => { console.error(error); process.exitCode = 1; });
