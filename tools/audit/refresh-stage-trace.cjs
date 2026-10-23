// 刷新慢样本的阶段诊断：外部写入 → 桥接请求 → 正文 DOM 变化 → 下一帧。
//
// 只用于定位 2026-10-24 采集到的 611/552 ms 慢样本落在哪一段，不修改产品代码。
// 通过 WebView2 调试端口注入 request/MutationObserver 计时；写入由 Node 直接改磁盘文件，
// 与正常入口的外部修改一致。窗口临时置 topmost，避免被其他最大化窗口遮挡导致
// WebView2 节流，从而把环境遮挡误判成产品慢。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/refresh-stage-trace-2026-10-24');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function documentText(generation) {
  return Array.from({ length: 40 }, (_, i) => `Line ${String(i + 1).padStart(2, '0')}  generation ${generation}  Ag 0123456789`).join('\n') + '\n';
}

async function waitWindow(pid) {
  const script = `$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline) {
  $h = (Get-Process -Id ${pid} -ErrorAction SilentlyContinue).MainWindowHandle
  if ($h -ne 0) { Write-Output $h; exit 0 }
  Start-Sleep -Milliseconds 100
}
exit 2`;
  const result = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
  return Number(result.stdout.trim());
}

async function prepareWindow(pid, hwnd) {
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class TraceWin {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
"@
try { [void][TraceWin]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$h = [IntPtr]${hwnd}
[void][TraceWin]::ShowWindow($h, 5)
[void][TraceWin]::BringWindowToTop($h)
$top = [TraceWin]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0003)
[void][TraceWin]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 500
Write-Output ("TOPMOST=" + $top + " FOREGROUND=" + ([TraceWin]::GetForegroundWindow() -eq $h))`;
  const result = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
  return result.stdout.trim();
}

async function closeWindow(pid) {
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CloseTrace { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); }
"@
$p = Get-Process -Id ${pid} -ErrorAction Stop
[void][CloseTrace]::PostMessage($p.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
$deadline = (Get-Date).AddSeconds(20)
while (-not $p.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
if ($p.HasExited) { exit 0 }
exit 2`;
  try {
    await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
    return true;
  } catch {
    return false;
  }
}

async function launch(workspace, settings, port) {
  const literal = (value) => "'" + String(value).replaceAll("'", "''") + "'";
  const command = '$ErrorActionPreference="Stop"; $p = & ' + literal(path.resolve('tools/start-augit.ps1'))
    + ' -Workspace ' + literal(workspace) + ' -SettingsFile ' + literal(settings)
    + ' -SkipBuild; if (-not $p.Id) { throw "no pid" }; Write-Output ("AUGIT_PID=" + $p.Id)';
  const started = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', command], {
    env: { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: '--remote-debugging-port=' + port },
    timeout: 120000,
  });
  const pid = Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
  if (!Number.isSafeInteger(pid) || pid <= 0) throw new Error('启动器未返回 PID: ' + started.stdout);
  return pid;
}

async function connect(port) {
  for (let attempt = 0; attempt < 160; attempt++) {
    try {
      return await chromium.connectOverCDP('http://127.0.0.1:' + port);
    } catch {
      await delay(250);
    }
  }
  throw new Error('无法连接 WebView2 调试端口');
}

async function pageOf(browser) {
  for (let attempt = 0; attempt < 120; attempt++) {
    const page = browser.contexts().flatMap((context) => context.pages()).find((candidate) => candidate.url().includes('index.html'));
    if (page) return page;
    await delay(250);
  }
  throw new Error('找不到 index.html 页面');
}

function installTrace() {
  const bridge = chrome.webview;
  const original = bridge.postMessage;
  const pending = new Map();
  window.__stageTrace = { requests: [], cycle: null };
  bridge.addEventListener('message', (event) => {
    const response = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
    const item = pending.get(response.id);
    if (!item) return;
    item.end = performance.now();
    item.endEpoch = Date.now();
    pending.delete(response.id);
  });
  bridge.postMessage = function (raw) {
    const request = typeof raw === 'string' ? JSON.parse(raw) : raw;
    if (['git/status', 'document/read', 'workspace/changes'].includes(request.method)) {
      const item = { id: request.id, method: request.method, start: performance.now(), startEpoch: Date.now() };
      pending.set(request.id, item);
      window.__stageTrace.requests.push(item);
    }
    return original.call(this, raw);
  };
  new MutationObserver(() => {
    const cycle = window.__stageTrace.cycle;
    if (!cycle || cycle.mutation) return;
    const code = document.querySelector('.editor-content .code-view');
    if (!code || !code.textContent.includes(`generation ${cycle.target}`)) return;
    cycle.mutation = performance.now();
    cycle.mutationEpoch = Date.now();
    requestAnimationFrame(() => {
      cycle.paint = performance.now();
      cycle.paintEpoch = Date.now();
    });
  }).observe(document.getElementById('app'), { childList: true, subtree: true, characterData: true });
}

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-refresh-stage-'));
  const workspace = path.join(temporary, 'workspace');
  const settings = path.join(temporary, 'settings.json');
  const report = { generatedAt: new Date().toISOString(), launcher: 'tools/start-augit.ps1 -SkipBuild', cycles: [] };
  let browser = null;
  let pid = 0;
  let closed = false;
  try {
    await fs.mkdir(workspace);
    await fs.writeFile(path.join(workspace, 'refresh.txt'), documentText(0));
    await execute('git', ['init', '-b', 'main'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.name', 'Augit Stage'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.email', 'stage@example.invalid'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'commit.gpgsign', 'false'], { cwd: workspace, windowsHide: true });
    await execute('git', ['add', 'refresh.txt'], { cwd: workspace, windowsHide: true });
    await execute('git', ['commit', '-m', 'test: refresh stage fixture'], { cwd: workspace, windowsHide: true });
    await fs.copyFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), settings);
    const listener = net.createServer();
    await new Promise((resolve) => listener.listen(0, '127.0.0.1', resolve));
    const port = listener.address().port;
    await new Promise((resolve) => listener.close(resolve));
    report.port = port;
    pid = await launch(workspace, settings, port);
    const hwnd = await waitWindow(pid);
    report.pid = pid;
    report.hwnd = hwnd;
    browser = await connect(port);
    const page = await pageOf(browser);
    await page.waitForFunction('window.__augitReady === true && !!window.__augitLive', null, { timeout: 30000 });
    const foreground = await prepareWindow(pid, hwnd);
    report.foreground = foreground;
    await page.locator('.side-content.tree .tree-row[data-tree-path="refresh.txt"]').dblclick();
    await page.waitForFunction(() => window.__augitLive.document && window.__augitLive.document.path === 'refresh.txt', null, { timeout: 30000 });
    await page.evaluate(installTrace);
    for (let cycle = 1; cycle <= 12; cycle++) {
      const marker = await page.evaluate((target) => {
        window.__stageTrace.cycle = {
          target,
          start: performance.now(),
          epoch: Date.now(),
          mutation: null,
          mutationEpoch: null,
          paint: null,
          paintEpoch: null,
        };
        window.__stageTrace.cycle.requestStartIndex = window.__stageTrace.requests.length;
        return { start: window.__stageTrace.cycle.start, epoch: window.__stageTrace.cycle.epoch };
      }, cycle);
      const writeEpoch = Date.now();
      await fs.writeFile(path.join(workspace, 'refresh.txt'), documentText(cycle));
      await page.waitForFunction((target) => {
        const value = window.__stageTrace.cycle;
        return value && value.target === target && !!value.mutation;
      }, cycle, { timeout: 5000 }).catch(() => {});
      await delay(250);
      const cycleTrace = await page.evaluate(() => {
        const cycleValue = window.__stageTrace.cycle;
        const requests = window.__stageTrace.requests.slice(cycleValue.requestStartIndex || 0)
          .filter((item) => item.start >= cycleValue.start - 5)
          .map((item) => ({
            method: item.method,
            start: Math.round(item.start - cycleValue.start),
            end: Number.isFinite(item.end) ? Math.round(item.end - cycleValue.start) : null,
            duration: Number.isFinite(item.end) ? Math.round(item.end - item.start) : null,
          }));
        return {
          target: cycleValue.target,
          start: cycleValue.start,
          epoch: cycleValue.epoch,
          mutation: cycleValue.mutation,
          mutationEpoch: cycleValue.mutationEpoch,
          paint: cycleValue.paint,
          paintEpoch: cycleValue.paintEpoch,
          requests,
          generation: (window.__augitLive.document && (window.__augitLive.document.text || '').match(/generation (\d+)/) || [])[1] || null,
          codeText: (document.querySelector('.editor-content .code-view') || {}).textContent || '',
        };
      });
      const writePerformance = marker.start + (writeEpoch - marker.epoch);
      const toMs = (value) => value === null || value === undefined ? null : Math.round(value - writePerformance);
      const read = cycleTrace.requests.filter((item) => item.method === 'document/read');
      const status = cycleTrace.requests.filter((item) => item.method === 'git/status');
      const changes = cycleTrace.requests.filter((item) => item.method === 'workspace/changes');
      report.cycles.push({
        cycle,
        writeOffsetFromStart: Math.round(writeEpoch - marker.epoch),
        mutationMs: toMs(cycleTrace.mutation),
        paintMs: toMs(cycleTrace.paint),
        mutationToPaintMs: cycleTrace.mutation && cycleTrace.paint ? Math.round(cycleTrace.paint - cycleTrace.mutation) : null,
        generation: cycleTrace.generation,
        hasTarget: cycleTrace.codeText.includes(`generation ${cycle}`),
        requests: cycleTrace.requests,
        read: read.length ? { start: read[0].start, end: read[0].end, duration: read[0].duration } : null,
        status: status.length ? { start: status[0].start, end: status[0].end, duration: status[0].duration } : null,
        changes: changes.length ? { start: changes[0].start, end: changes[0].end, duration: changes[0].duration } : null,
      });
      await delay(1200);
    }
    await browser.close();
    browser = null;
    closed = await closeWindow(pid);
    report.closedNormally = closed;
    await delay(500);
    report.pidAlive = (() => { try { process.kill(pid, 0); return true; } catch { return false; } })();
  } catch (error) {
    report.error = String(error && error.stack || error);
    process.exitCode = 1;
  } finally {
    if (browser) await browser.close().catch(() => {});
    if (pid && !closed) await closeWindow(pid).catch(() => {});
    await fs.writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n');
    await fs.rm(temporary, { recursive: true, force: true }).catch(() => {});
  }
  console.log(JSON.stringify({
    pid: report.pid,
    foreground: report.foreground,
    cycles: report.cycles.map((cycle) => ({
      cycle: cycle.cycle,
      mutationMs: cycle.mutationMs,
      paintMs: cycle.paintMs,
      mutationToPaintMs: cycle.mutationToPaintMs,
      generation: cycle.generation,
      read: cycle.read,
      status: cycle.status,
      changes: cycle.changes,
    })),
    closedNormally: report.closedNormally,
    error: report.error || null,
  }, null, 2));
})();
