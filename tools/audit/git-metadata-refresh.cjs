// 正常入口验收：外部 Git 元数据变化（checkout）在 500ms 内反映到界面。
// 只操作临时仓库；不点击应用内 Git 操作，不改用户配置。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/git-metadata-refresh');
const traceRoot = process.env.AUGIT_REFRESH_TRACE_DIRECTORY
  ? path.resolve(process.env.AUGIT_REFRESH_TRACE_DIRECTORY)
  : null;
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// 与 refresh-content-onset.cjs 同一口径：阶段内用 QPC，跨进程只用 harness mark 对齐。
async function readHostTrace(directory, hostMark, eventEpoch) {
  const files = (await fs.readdir(directory)).filter((name) => /^refresh-.*\.json$/.test(name));
  if (files.length === 0) return { available: false, reason: 'no export file' };
  const data = JSON.parse(await fs.readFile(path.join(directory, files[0]), 'utf8'));
  const frequency = data.frequency;
  if (!Number.isFinite(frequency) || frequency <= 0) return { available: false, reason: 'bad frequency' };
  const samples = Array.isArray(data.samples) ? data.samples : [];
  const mark = samples.find((sample) => sample.stage === 'harness-mark');
  const toMs = (timestamp) => (timestamp / frequency) * 1000;
  const eventFromMark = hostMark && mark && Number.isFinite(hostMark.nodeEpoch)
    ? eventEpoch - hostMark.nodeEpoch
    : null;
  const timeline = samples.map((sample) => {
    const fromMark = mark ? toMs(sample.timestamp) - toMs(mark.timestamp) : null;
    return {
      stage: sample.stage,
      detail: sample.detail === undefined ? null : sample.detail,
      fromMarkMs: fromMark === null ? null : Math.round(fromMark * 10) / 10,
      fromEventMs: fromMark === null || eventFromMark === null
        ? null
        : Math.round((fromMark - eventFromMark) * 10) / 10,
    };
  });
  return {
    available: true,
    file: files[0],
    frequency,
    samples: samples.length,
    markRecorded: !!mark,
    eventFromMarkMs: eventFromMark === null ? null : Math.round(eventFromMark * 10) / 10,
    timeline,
  };
}

async function git(cwd, ...args) {
  return execute('git', ['-C', cwd, ...args], { windowsHide: true, timeout: 30000 });
}

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-git-metadata-'));
  const workspace = path.join(temporary, 'repo');
  const settings = path.join(temporary, 'settings.json');
  const listener = net.createServer();
  let port = 0;
  let shell = null;
  let browser = null;
  let hostMark = null;
  let runTraceDir = null;
  let gitDoneAt = null;
  const report = { launcher: 'tools/start-augit.ps1', forcedBuild: true, cdpAttached: true, checks: [] };
  try {
    await fs.mkdir(workspace);
    await git(workspace, 'init', '-b', 'main');
    await git(workspace, 'config', 'user.name', 'Augit Audit');
    await git(workspace, 'config', 'user.email', 'augit-audit@example.invalid');
    await fs.writeFile(path.join(workspace, 'tracked.txt'), 'base\n');
    await git(workspace, 'add', 'tracked.txt');
    await git(workspace, 'commit', '-m', 'test: base');

    await fs.copyFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), settings).catch(async () => {
      await fs.writeFile(settings, '{}\n');
    });
    await new Promise((resolve) => listener.listen(0, '127.0.0.1', resolve));
    port = listener.address().port;
    await new Promise((resolve) => listener.close(resolve));
    if (traceRoot) {
      // 宿主只接受绝对且已存在的目录；必须在启动前设置，启动器与 Augit 才会继承。
      runTraceDir = path.join(traceRoot, 'checkout');
      await fs.mkdir(runTraceDir, { recursive: true });
      process.env.AUGIT_REFRESH_TRACE_DIRECTORY = runTraceDir;
    }
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
    const page = browser.contexts()[0].pages()[0];
    const pageErrors = [];
    page.on('pageerror', (error) => pageErrors.push('pageerror:' + String(error)));
    page.on('console', (message) => {
      if (message.type() === 'error') pageErrors.push('console:' + message.text());
    });
    await page.waitForFunction(() => window.__augitReady === true, null, { timeout: 40000 });
    await page.waitForFunction(() => window.__augitGitReady === true, null, { timeout: 40000 });
    assert.equal(await page.evaluate(() => window.__augitLive?.branch), 'main');

    const startedAt = Date.now();
    if (runTraceDir) {
      const before = Date.now();
      const pageMark = await page.evaluate(() => new Promise((resolve) => {
        const id = 910000 + Math.floor(Math.random() * 100000);
        const sent = Date.now();
        const handler = (event) => {
          const response = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
          if (!response || response.id !== id) return;
          chrome.webview.removeEventListener('message', handler);
          resolve({ recorded: !!(response.result && response.result.recorded), sent, received: Date.now() });
        };
        chrome.webview.addEventListener('message', handler);
        chrome.webview.postMessage(JSON.stringify({
          id,
          method: 'audit/refresh-mark',
          params: { epochMs: String(Date.now()) },
        }));
        setTimeout(() => {
          chrome.webview.removeEventListener('message', handler);
          resolve({ recorded: false, sent, received: Date.now() });
        }, 2000);
      }));
      const after = Date.now();
      hostMark = {
        recorded: pageMark.recorded,
        sent: pageMark.sent,
        roundTripMs: pageMark.received - pageMark.sent,
        nodeEpoch: pageMark.sent + Math.max(0, (after - before) / 2),
      };
    }
    await git(workspace, 'checkout', '-b', 'external-branch');
    gitDoneAt = Date.now();
    await page.waitForFunction(() => window.__augitLive?.branch === 'external-branch', null, { polling: 10, timeout: 5000 });
    const observedAt = Date.now();
    const latency = observedAt - gitDoneAt;
    report.checks.push({ name: 'external-checkout', branch: 'external-branch', gitMilliseconds: gitDoneAt - startedAt, uiMilliseconds: latency });
    assert.ok(latency < 500, `外部 checkout 到界面更新应 <500ms，实际 ${latency}ms`);

    const errors = await page.evaluate(() => ({
      error: window.__augitError || null,
      errors: Array.isArray(window.__augitErrors) ? window.__augitErrors : [],
    }));
    assert.equal(errors.error, null);
    assert.deepEqual(errors.errors, []);
    assert.deepEqual(pageErrors, []);
    report.checks.push({ name: 'no-runtime-errors', errors, pageErrors });

    const mapped = await execute(process.env.AUGIT_AUDIT_PYTHON || 'D:/Python/Python3119/python.exe',
      ['-B', 'tools/audit/read-process-mapped-images.py', String(pid)],
      { windowsHide: true, timeout: 15000, env: { ...process.env, PYTHONUTF8: '1' } });
    const mappedIdentity = JSON.parse(mapped.stdout);
    assert.equal(mappedIdentity.pid, pid);
    for (const name of ['Augit.dll', 'Augit.Infrastructure.dll']) {
      assert.ok(mappedIdentity.mappedImages.some((item) => item.name === name), '未找到已加载程序集：' + name);
    }
    await fs.writeFile(path.join(output, 'mapped-images.json'), JSON.stringify(mappedIdentity, null, 2));
    await fs.writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2));
    console.log('GIT_METADATA_OK latency=' + latency + ' pid=' + pid + ' port=' + port);
  } catch (error) {
    report.error = String(error && error.stack || error);
    await fs.writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2)).catch(() => {});
    console.error(report.error);
    process.exitCode = 1;
  } finally {
    try { if (browser) await browser.close(); } catch { /* 继续清理 */ }
    if (shell) {
      try {
        await execute('powershell.exe', ['-NoProfile', '-Command',
          '$p=Get-Process -Id ' + shell + ' -ErrorAction Stop; ' +
          'if (-not $p.CloseMainWindow()) { throw "CloseMainWindow=false" }; ' +
          'if (-not $p.WaitForExit(8000)) { throw "still running" }'], { windowsHide: true, timeout: 20000 });
        report.closeMethod = 'CloseMainWindow';
      } catch {
        report.closeMethod = 'taskkill';
        try { await execute('taskkill', ['/PID', String(shell), '/T', '/F'], { windowsHide: true, timeout: 20000 }); }
        catch { /* 已退出 */ }
      }
    }
    try {
      let listening = true;
      for (let index = 0; index < 20 && listening; index += 1) {
        const server = net.createServer();
        try {
          await new Promise((resolve, reject) => server.once('error', reject).listen(port, '127.0.0.1', resolve));
          server.close();
          listening = false;
        } catch {
          await delay(250);
        }
      }
      report.portClosed = !listening;
    } catch { report.portClosed = false; }
    try { await fs.rm(temporary, { recursive: true, force: true }); report.temporaryRemoved = true; }
    catch { report.temporaryRemoved = false; }
    if (runTraceDir && gitDoneAt !== null) {
      report.hostMark = hostMark;
      report.hostTrace = await readHostTrace(runTraceDir, hostMark, gitDoneAt).catch((error) => ({
        available: false,
        reason: String(error && error.message || error),
      }));
    }
    await fs.writeFile(path.join(output, 'cleanup.json'), JSON.stringify(report, null, 2)).catch(() => {});
  }
})();
