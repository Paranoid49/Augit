// 正常入口设置开窗时由 OS 级挂起本实例 WebView2 renderer/GPU，验证 500ms 捕获超时和提前揭示风险。
// 不修改产品代码，不注入测试分支；挂起只持续约 1.2 秒，随后恢复并正常关闭。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const { waitAsyncCondition } = require('./wait-async-condition.cjs');

async function invoke(page, method, params = {}) {
  return page.evaluate(async ({ method, params }) => {
    const { invoke } = await import('/src/bridge.js');
    return invoke(method, params, 0);
  }, { method, params });
}

function summarizeTrace(value) {
  const samples = value.samples || [];
  const index = new Map(samples.map((sample) => [sample.Stage, sample.Timestamp]));
  const milliseconds = (from, to) => index.has(from) && index.has(to)
    ? Math.round((index.get(to) - index.get(from)) / 10) / 1000 : null;
  return {
    processId: value.processId,
    stages: samples.map((sample) => sample.Stage),
    presentedMilliseconds: milliseconds('created', 'presented'),
    captureIssuedMilliseconds: milliseconds('capture-start', 'capture-issued'),
    captureAwaitMilliseconds: milliseconds('capture-issued', 'capture-end'),
    captureTimeout: index.has('capture-timeout'),
    revealedAfterCaptureTimeout: index.has('capture-timeout') && index.has('presented')
  };
}

(async () => {
  const { chromium } = require(process.argv[2]);
  const output = path.resolve(process.argv[3] || 'artifacts/settings-capture-stall');
  await fs.mkdir(output, { recursive: true });
  const nativeTrace = path.join(output, 'native-trace');
  await fs.mkdir(nativeTrace, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-capture-stall-'));
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
    scenario: '挂起 WebView2 renderer/GPU 约1.2s，使 CapturePreviewAsync 超过500ms',
    checks: []
  };
  const save = (name, value) => fs.writeFile(path.join(output, name), JSON.stringify(value, null, 2) + '\n');
  try {
    await fs.copyFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), settings).catch(async () => {
      await fs.writeFile(settings, '{}\n');
    });
    await new Promise((resolve) => listener.listen(0, '127.0.0.1', resolve));
    port = listener.address().port;
    await new Promise((resolve) => listener.close(resolve));
    const launch = require('./daily-launch-command.cjs')(process.cwd(), settings, port);
    launch.env = { ...launch.env, AUGIT_SETTINGS_TRACE_DIRECTORY: nativeTrace };
    const started = await execute(launch.file, launch.args, { env: launch.env, timeout: 180000 });
    pid = Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
    assert.ok(Number.isSafeInteger(pid) && pid > 0, '日常启动器未返回实际 Augit PID');
    report.pid = pid;
    report.port = port;
    await save('launch.json', {
      launcher: 'tools/start-augit.ps1', forcedBuild: true, port,
      traceDirectory: nativeTrace, output: started.stdout
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
    const main = browser.contexts()[0].pages()[0];
    main.setDefaultTimeout(30000);
    const pageErrors = [];
    main.on('pageerror', (error) => pageErrors.push('pageerror:' + String(error)));
    main.on('console', (message) => { if (message.type() === 'error') pageErrors.push('console:' + message.text()); });
    await main.waitForFunction(() => window.__augitReady && window.__augitSettingsReady);
    assert.equal((await invoke(main, 'window/settings-state')).active, false);

    // 在设置页的新文档中拦截 window/settings-present，精确到捕获开始前再挂起。
    await main.context().addInitScript(() => {
      window.__augitPresentSent = false;
      const patch = () => {
        const bridge = window.chrome && window.chrome.webview;
        if (!bridge) return false;
        if (bridge.__augitPresentPatched) return true;
        const post = bridge.postMessage;
        bridge.postMessage = function (raw) {
          try {
            const value = typeof raw === 'string' ? JSON.parse(raw) : raw;
            if (value && value.method === 'window/settings-present') window.__augitPresentSent = true;
          } catch { /* 非 JSON 消息保持原样 */ }
          return post.call(this, raw);
        };
        bridge.__augitPresentPatched = true;
        return true;
      };
      if (!patch()) {
        const timer = setInterval(() => { if (patch()) clearInterval(timer); }, 1);
      }
    });

    const browserSession = await browser.newBrowserCDPSession();
    const beforeInfo = await browserSession.send('SystemInfo.getProcessInfo');
    const beforeIds = new Set(beforeInfo.processInfo.map((item) => item.id));

    // 先预热 PowerShell/Add-Type，再点击；避免 helper 启动耗时错过捕获窗口。
    const helperReady = path.join(output, 'helper-ready.signal');
    const goFile = path.join(output, 'stall-go.signal');
    const pidFile = path.join(output, 'stall-pids.json');
    await fs.rm(helperReady, { force: true });
    await fs.rm(goFile, { force: true });
    await fs.rm(pidFile, { force: true });
    const stall = execute('powershell.exe', [
      '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'tools/audit/suspend-pids-fast.ps1',
      '-ParentProcessId', String(pid), '-OutputDirectory', output, '-PidFile', pidFile,
      '-WindowTitle', 'Settings', '-SuspendMilliseconds', '1200',
      '-GoFile', goFile, '-ReadyFile', helperReady, '-DelayMilliseconds', '10'
    ], { windowsHide: true, timeout: 30000 });
    for (let attempt = 0; attempt < 200; attempt += 1) {
      try { await fs.access(helperReady); break; } catch { await delay(50); }
    }
    assert.ok(await fs.access(helperReady).then(() => true).catch(() => false), '挂起 helper 未预热完成');
    await main.locator('.titlebar [aria-label="Settings"]').click();

    // 设置页 renderer 在 present 前已创建；按 CDP 进程差异定位后写入 PID 文件。
    let targets = null;
    for (let attempt = 0; attempt < 300 && !targets; attempt += 1) {
      const info = await browserSession.send('SystemInfo.getProcessInfo');
      const newRenderer = info.processInfo.find((item) => item.type === 'renderer' && !beforeIds.has(item.id));
      if (newRenderer) {
        const gpu = info.processInfo.find((item) => String(item.type).toLowerCase().includes('gpu'));
        targets = [{ id: newRenderer.id, type: 'renderer' }];
        if (gpu) targets.push({ id: gpu.id, type: 'GPU' });
      } else {
        await delay(10);
      }
    }
    assert.ok(targets, '未定位到设置页 renderer');
    await fs.writeFile(pidFile, targets.map((item) => `${item.id},${item.type}`).join('\n') + '\n');
    report.processTargets = targets;

    let settingsPage = null;
    for (let attempt = 0; attempt < 200 && !settingsPage; attempt += 1) {
      settingsPage = browser.contexts().flatMap((context) => context.pages())
        .find((page) => page.url().includes('surface=settings')) || null;
      if (!settingsPage) await delay(25);
    }
    assert.ok(settingsPage, '未找到设置页 target');
    await settingsPage.waitForFunction(() => window.__augitPresentSent === true, null, { polling: 5, timeout: 15000 });
    await fs.writeFile(goFile, 'go');
    const stallOutput = await stall;
    await save('suspend-stdout.json', { stdout: stallOutput.stdout, stderr: stallOutput.stderr });
    const suspendResult = JSON.parse((await fs.readFile(path.join(output, 'suspend-result.json'), 'utf8')).replace(/^\uFEFF/, ''));
    report.suspend = suspendResult;

    await waitAsyncCondition(async () => {
      const state = await invoke(main, 'window/settings-state');
      return state.active && state.ready;
    }, { label: '设置恢复后原生呈现' });
    const state = await invoke(main, 'window/settings-state');
    report.settingsState = state;
    const settingsPages = browser.contexts().flatMap((context) => context.pages()).filter((page) => page.url().includes('surface=settings'));
    assert.equal(settingsPages.length, 1, '设置页数量必须为 1');
    await settingsPage.screenshot({ path: path.join(output, 'settings-cdp.png') }).catch(() => {});
    report.settingsPage = await settingsPage.evaluate(() => ({
      url: location.href,
      ready: window.__augitReady,
      errors: window.__augitErrors,
      hasForm: !!document.querySelector('.settings-dialog')
    }));

    await settingsPage.locator('[data-settings-action="cancel"]').click();
    await waitAsyncCondition(async () => !(await invoke(main, 'window/settings-state')).active, { label: '设置关闭' });
    await delay(1500);
    const traces = [];
    for (const file of await fs.readdir(nativeTrace)) {
      if (!file.endsWith('.json')) continue;
      const value = JSON.parse(await fs.readFile(path.join(nativeTrace, file), 'utf8'));
      traces.push({ file, ...summarizeTrace(value) });
    }
    report.traces = traces;
    const timeoutTrace = traces.find((trace) => trace.captureTimeout);
    report.captureTimeoutObserved = !!timeoutTrace;
    report.revealedAfterTimeout = !!timeoutTrace?.revealedAfterCaptureTimeout;
    await save('trace-summary.json', traces);
    assert.deepEqual(await main.evaluate(() => window.__augitErrors), []);
    assert.deepEqual(pageErrors, []);
    await save('report.json', report);

    if (browser) { await browser.close(); browser = null; }
    await execute('powershell.exe', ['-NoProfile', '-Command',
      '$p=Get-Process -Id ' + pid + ' -ErrorAction Stop; if (-not $p.CloseMainWindow()) { throw "CloseMainWindow=false" }; if (-not $p.WaitForExit(8000)) { throw "still running" }'],
      { windowsHide: true, timeout: 20000 });
    closedNormally = true;
    report.closeMethod = 'CloseMainWindow';
    console.log('SETTINGS_CAPTURE_STALL_OK pid=' + pid + ' timeout=' + report.captureTimeoutObserved + ' revealed=' + report.revealedAfterTimeout);
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
      try { await execute('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true, timeout: 20000 }); }
      catch { /* 已退出 */ }
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
