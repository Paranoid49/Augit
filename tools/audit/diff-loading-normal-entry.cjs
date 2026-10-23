// Diff 加载态 / 二进制的正常入口证据（真实外壳 + 项目树/Changes 双击）。
//
// 夹具：9 MB 文本（100,000 行，每 5 行改一处）+ 1 MB 二进制；
// 通过 Changes 列表真实鼠标双击打开工作区 Diff，记录：
//   - 点击→Loading.../diff-progress 出现、点击→正文出现的时间；
//   - git/diff 请求起止；
//   - Loading 期间的渲染器截图；
//   - 二进制 Diff 的摘要页；
//   - 加载完成后进度条/骨架是否消失。
//
// CDP 只读 DOM/桥接状态并截图；打开文件用 Win32 mouse_event 双击。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const crypto = require('node:crypto');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/diff-loading-normal-entry-2026-10-24');
const darkNarrow = process.argv.includes('--dark-narrow');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

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

async function topmost(pid, hwnd) {
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class DiffTop {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
try { [void][DiffTop]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$h = [IntPtr]${hwnd}
[void][DiffTop]::ShowWindow($h, 5)
[void][DiffTop]::BringWindowToTop($h)
$top = [DiffTop]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0003)
[void][DiffTop]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 350
Write-Output ("TOPMOST=" + $top)`;
  return (await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 })).stdout.trim();
}

async function closeApp(pid) {
  const script = `$p = Get-Process -Id ${pid} -ErrorAction Stop
$p.CloseMainWindow() | Out-Null
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

function bigText(lines) {
  const out = [];
  for (let index = 0; index < lines; index++) {
    out.push(index % 5 === 0 ? `line ${index} changed ${'-'.repeat(50)}` : `line ${index} context ${'-'.repeat(50)}`);
  }
  return out.join('\n') + '\n';
}

function installDiffProbe() {
  const bridge = chrome.webview;
  const original = bridge.postMessage;
  const pending = new Map();
  window.__diffProbe = { requests: [], events: {} };
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
    if (request.method === 'git/diff') {
      const item = { id: request.id, start: performance.now(), startEpoch: Date.now() };
      pending.set(request.id, item);
      window.__diffProbe.requests.push(item);
    }
    return original.call(this, raw);
  };
  const sample = () => {
    const events = window.__diffProbe.events;
    const progress = document.querySelector('.diff-progress');
    const message = document.querySelector('.diff-loading-message');
    const layout = document.querySelector('.diff-layout');
    const columns = document.querySelector('.diff-columns');
    const notice = document.querySelector('.diff-notice, .diff-status-notice, .diff-filebar');
    const now = Date.now();
    if (!events.progress && progress) events.progress = now;
    if (!events.loadingMessage && message) events.loadingMessage = now;
    if (!events.layout && layout) events.layout = now;
    if (!events.columns && columns) events.columns = now;
    if (!events.progressGone && events.progress && !progress) events.progressGone = now;
    if (!events.messageGone && events.loadingMessage && !message) events.messageGone = now;
    if (!events.notice && notice) events.notice = now;
    if (now - (window.__diffProbe.startedAt || now) < 12000) window.__diffProbe.frame = requestAnimationFrame(sample);
  };
  window.__diffProbe.startedAt = Date.now();
  window.__diffProbe.frame = requestAnimationFrame(sample);
}

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-diff-loading-'));
  const workspace = path.join(temporary, 'workspace');
  const settings = path.join(temporary, 'settings.json');
  let browser = null;
  let pid = 0;
  let closed = false;
  const report = { generatedAt: new Date().toISOString(), fixtures: {}, runs: [], cdp: 'observation/input only' };
  try {
    await fs.mkdir(workspace);
    const text = bigText(100000);
    await fs.writeFile(path.join(workspace, 'large.txt'), text);
    const binary = crypto.randomBytes(1024 * 1024);
    await fs.writeFile(path.join(workspace, 'image.bin'), binary);
    await execute('git', ['init', '-b', 'main'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.name', 'Augit Diff'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.email', 'diff@example.invalid'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'commit.gpgsign', 'false'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'core.autocrlf', 'false'], { cwd: workspace, windowsHide: true });
    await execute('git', ['add', '.'], { cwd: workspace, windowsHide: true });
    await execute('git', ['commit', '-m', 'test: diff loading fixture'], { cwd: workspace, windowsHide: true });
    // 修改一半行并改二进制，制造可测量的 git/diff 工作量。
    const changed = text.split('\n').map((line, index) => index % 10 === 0 ? `${line} +changed` : line).join('\n');
    await fs.writeFile(path.join(workspace, 'large.txt'), changed);
    const binary2 = crypto.randomBytes(1024 * 1024);
    await fs.writeFile(path.join(workspace, 'image.bin'), binary2);
    report.fixtures = {
      textBytes: Buffer.byteLength(changed),
      textLines: 100000,
      binaryBytes: binary2.length,
      settingsSha256: crypto.createHash('sha256').update(await fs.readFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'))).digest('hex').toUpperCase(),
    };
    await fs.copyFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), settings);
    const settingsObject = JSON.parse(await fs.readFile(settings, 'utf8'));
    settingsObject.lastWorkspace = workspace;
    settingsObject.openFiles = [];
    settingsObject.activeFile = null;
    if (darkNarrow) settingsObject.theme = 'Dark';
    await fs.writeFile(settings, JSON.stringify(settingsObject, null, 2) + '\n');
    const listener = net.createServer();
    await new Promise((resolve) => listener.listen(0, '127.0.0.1', resolve));
    const port = listener.address().port;
    await new Promise((resolve) => listener.close(resolve));
    report.port = port;
    pid = await launch(workspace, settings, port);
    const hwnd = await waitWindow(pid);
    report.pid = pid;
    report.hwnd = hwnd;
    report.topmost = await topmost(pid, hwnd);
    browser = await connect(port);
    const page = await pageOf(browser);
    await page.waitForFunction('window.__augitReady === true && !!window.__augitLive', null, { timeout: 30000 });
    const changesReady = await page.evaluate(() => document.querySelectorAll('.changes-list .change-file-row').length);
    if (!changesReady) {
      const railRect = await page.evaluate(() => {
        const rail = document.querySelector('.rail-button[data-tool="commit"]');
        if (!rail) return null;
        const rect = rail.getBoundingClientRect();
        return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
      });
      if (!railRect) throw new Error('找不到 Commit 工具轨按钮');
      await execute('powershell.exe', [
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.resolve('tools/audit/click-window.ps1'),
        '-X', String(railRect.x + railRect.width / 2), '-Y', String(railRect.y + railRect.height / 2),
        '-WindowHandle', String(hwnd), '-AfterMs', '400', '-Clicks', '1', '-NoForeground',
      ], { timeout: 30000 });
    }
    await page.waitForFunction(() => document.querySelectorAll('.changes-list .change-file-row').length >= 2, null, { timeout: 30000 });
    await page.evaluate(installDiffProbe);
    const rowRects = await page.evaluate(() => {
      const read = (path) => {
        const row = document.querySelector(`.changes-list .change-file-row[data-path="${path}"]`);
        if (!row) return null;
        const rect = row.getBoundingClientRect();
        return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
      };
      return { text: read('large.txt'), binary: read('image.bin') };
    });
    report.rows = rowRects;
    if (!rowRects.text) throw new Error('Changes 列表找不到 large.txt');
    const click = async (rect, key) => {
      const result = await execute('powershell.exe', [
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.resolve('tools/audit/click-window.ps1'),
        '-X', String(rect.x + rect.width / 2), '-Y', String(rect.y + rect.height / 2),
        '-WindowHandle', String(hwnd), '-AfterMs', '80', '-Clicks', '2', '-GapMs', '80', '-NoForeground',
      ], { timeout: 30000 });
      const clickUtc = /click_utc=([^\s]+)/.exec(result.stdout)?.[1] || null;
      return { key, clickUtc, clickEpoch: clickUtc ? Date.parse(clickUtc) : Date.now() };
    };
    const textClick = await click(rowRects.text, 'text');
    const captureWindow = async (name) => {
      await execute('powershell.exe', [
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.resolve('tools/audit/capture-screen-region.ps1'),
        '-ExpectedProcessId', String(pid), '-Output', path.join(output, name), '-Padding', '0',
      ], { timeout: 30000 }).catch(() => {});
    };
    // 原生截图不等 WebView 主线程：慢 diff 正在解析/排版时仍能看到当时桌面内容。
    await delay(220);
    await captureWindow('loading-250.png');
    await delay(430);
    await captureWindow('loading-700.png');
    const loadingState = await page.evaluate(() => {
      const events = window.__diffProbe.events;
      return {
        progress: !!document.querySelector('.diff-progress'),
        message: document.querySelector('.diff-loading-message')?.textContent || null,
        events,
        requests: window.__diffProbe.requests,
      };
    });
    await page.waitForFunction(() => {
      const layout = document.querySelector('.diff-layout');
      return !!layout && !document.querySelector('.diff-progress') && !document.querySelector('.diff-loading-message')
        && (document.querySelector('.diff-columns') || document.querySelector('.diff-status-notice, .diff-notice'));
    }, null, { timeout: 60000 });
    const textDone = await page.evaluate(() => {
      const events = window.__diffProbe.events;
      const requests = window.__diffProbe.requests.map((item) => ({
        start: Math.round(item.start), end: Number.isFinite(item.end) ? Math.round(item.end) : null,
        duration: Number.isFinite(item.end) ? Math.round(item.end - item.start) : null,
      }));
      const layout = document.querySelector('.diff-layout');
      return {
        events,
        requests,
        path: layout ? layout.getAttribute('data-diff-path') : null,
        progress: !!document.querySelector('.diff-progress'),
        message: !!document.querySelector('.diff-loading-message'),
        rows: document.querySelectorAll('.diff-code-line').length,
        bodyText: (document.querySelector('.diff-columns') || {}).textContent?.slice(0, 200) || null,
      };
    });
    await page.screenshot({ path: path.join(output, 'diff-loaded.png') });
    let darkNarrowState = null;
    if (darkNarrow) {
      const resize = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class DiffNarrow { [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags); [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); }
"@
try { [void][DiffNarrow]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
[void][DiffNarrow]::SetWindowPos([IntPtr]${hwnd}, [IntPtr]::Zero, 0, 0, 1100, 800, (0x0004 -bor 0x0010))`;
      await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', resize], { timeout: 30000 });
      await delay(800);
      darkNarrowState = await page.evaluate(() => {
        const layout = document.querySelector('.editor-content .diff-layout');
        const toolbar = document.querySelector('.editor-content .diff-toolbar');
        const filebar = document.querySelector('.editor-content .reference-filebar');
        const rect = (node) => node ? (() => { const r = node.getBoundingClientRect(); return { x: Math.round(r.x), y: Math.round(r.y), width: Math.round(r.width), height: Math.round(r.height) }; })() : null;
        return {
          theme: document.body.dataset.theme || null,
          viewport: { width: window.innerWidth, height: window.innerHeight },
          layout: rect(layout), toolbar: rect(toolbar), filebar: rect(filebar),
          summary: document.querySelector('.diff-toolbar .diff-summary')?.textContent || null,
          truncation: document.querySelector('.diff-truncation')?.innerText || null,
          rows: document.querySelectorAll('.diff-code-line').length,
        };
      });
      await page.screenshot({ path: path.join(output, 'diff-dark-narrow.png') });
      report.darkNarrow = darkNarrowState;
    }
    report.runs.push({ key: 'text', click: textClick, loading: loadingState, done: textDone });

    if (rowRects.binary) {
      const binaryClick = await click(rowRects.binary, 'binary');
      await page.waitForFunction(() => {
        const text = document.querySelector('.editor-content')?.textContent || '';
        return text.includes('Binary') || text.includes('binary');
      }, null, { timeout: 30000 }).catch(() => {});
      const binaryDone = await page.evaluate(() => ({
        editor: window.__augitLive.editor,
        text: (document.querySelector('.editor-content')?.textContent || '').slice(0, 600),
        rows: document.querySelectorAll('.diff-code-line').length,
      }));
      await page.screenshot({ path: path.join(output, 'binary.png') });
      report.runs.push({ key: 'binary', click: binaryClick, done: binaryDone });
    }
    await browser.close();
    browser = null;
    closed = await closeApp(pid);
    report.closedNormally = closed;
    await delay(500);
    report.pidAlive = (() => { try { process.kill(pid, 0); return true; } catch { return false; } })();
  } catch (error) {
    report.error = String(error && error.stack || error);
    process.exitCode = 1;
  } finally {
    if (browser) await browser.close().catch(() => {});
    if (pid && !closed) await closeApp(pid).catch(() => {});
    await fs.writeFile(path.join(output, 'diff-loading.json'), JSON.stringify(report, null, 2) + '\n');
    await fs.rm(temporary, { recursive: true, force: true }).catch(() => {});
  }
  console.log(JSON.stringify({
    pid: report.pid,
    fixtures: report.fixtures,
    runs: report.runs.map((run) => ({
      key: run.key,
      clickUtc: run.click.clickUtc,
      events: run.loading ? run.loading.events : null,
      requests: run.loading ? run.loading.requests : null,
      progress: run.loading ? run.loading.progress : null,
      message: run.loading ? run.loading.message : null,
      done: run.done,
    })),
    closedNormally: report.closedNormally,
    error: report.error || null,
  }, null, 2));
})();
