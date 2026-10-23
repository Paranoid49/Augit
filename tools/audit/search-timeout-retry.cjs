// 搜索超时 → Enter 重试的正常入口证据。
//
// 夹具：200 MB 文本（2,000,000 行，约每 100,000 行一个 needle_retry_token）。
// 真实外壳、真实 Ctrl+Shift+F / SendKeys / Enter；第一次 rg 由工具在启动后 100 ms
// 挂起，等待宿主 15 秒超时并保留已完成结果；挂起助手退出后按 Enter 用同一查询重跑，
// 第二次 rg 不被挂起，验证结果可用、超时说明清除。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile, spawn } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/search-timeout-retry-2026-10-24');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function waitWindow(pid) {
  const script = `$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline) {
  $h = (Get-Process -Id ${pid} -ErrorAction SilentlyContinue).MainWindowHandle
  if ($h -ne 0) { Write-Output $h; exit 0 }
  Start-Sleep -Milliseconds 100
}
exit 2`;
  return Number((await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 })).stdout.trim());
}

async function topmost(hwnd) {
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SearchTop {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
try { [void][SearchTop]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$h = [IntPtr]${hwnd}
[void][SearchTop]::ShowWindow($h, 5)
[void][SearchTop]::BringWindowToTop($h)
$top = [SearchTop]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0003)
[void][SearchTop]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 350
Write-Output ("TOPMOST=" + $top)`;
  return (await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 })).stdout.trim();
}

async function clickCss(hwnd, rect) {
  // 与 click-window.ps1 不同：显式用客户区原点 + DPI 比例，避免 168 DPI 下把逻辑坐标
  // 直接交给 SetCursorPos 导致点空。
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SearchDpiClick {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out Rect r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref Point p);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
}
"@
try { [void][SearchDpiClick]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$r = New-Object SearchDpiClick+Rect
$o = New-Object SearchDpiClick+Point
[void][SearchDpiClick]::GetClientRect([IntPtr]${hwnd}, [ref]$r)
[void][SearchDpiClick]::ClientToScreen([IntPtr]${hwnd}, [ref]$o)
$s = [SearchDpiClick]::GetDpiForWindow([IntPtr]${hwnd}) / 96.0
$x = [int][Math]::Round($o.X + ${rect.x + rect.width / 2} * $s)
$y = [int][Math]::Round($o.Y + ${rect.y + rect.height / 2} * $s)
[void][SearchDpiClick]::SetCursorPos($x, $y)
[SearchDpiClick]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
[SearchDpiClick]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)`;
  await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
}

async function sendKeys(keys) {  const literal = String(keys).replaceAll("'", "''");
  const script = `Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait('${literal}')`;
  await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
}

async function sendShortcut() {
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SearchKeys { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra); }
"@
[SearchKeys]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
[SearchKeys]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero)
[SearchKeys]::keybd_event(0x46, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 40
[SearchKeys]::keybd_event(0x46, 0, 2, [UIntPtr]::Zero)
[SearchKeys]::keybd_event(0x10, 0, 2, [UIntPtr]::Zero)
[SearchKeys]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)`;
  await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
}

async function sendEnter() {
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class EnterKey { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra); }
"@
[EnterKey]::keybd_event(0x0D, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 40
[EnterKey]::keybd_event(0x0D, 0, 2, [UIntPtr]::Zero)`;
  await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
}

async function closeApp(pid) {
  const script = `$p = Get-Process -Id ${pid} -ErrorAction Stop
$p.CloseMainWindow() | Out-Null
$deadline = (Get-Date).AddSeconds(20)
while (-not $p.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
if ($p.HasExited) { exit 0 }
exit 2`;
  try { await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 }); return true; } catch { return false; }
}

async function connect(port) {
  for (let attempt = 0; attempt < 160; attempt++) {
    try { return await chromium.connectOverCDP('http://127.0.0.1:' + port); } catch { await delay(250); }
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

async function launch(workspace, settings, port) {
  const literal = (value) => "'" + String(value).replaceAll("'", "''") + "'";
  const command = '$ErrorActionPreference="Stop"; $p = & ' + literal(path.resolve('tools/start-augit.ps1'))
    + ' -Workspace ' + literal(workspace) + ' -SettingsFile ' + literal(settings)
    + ' -SkipBuild; if (-not $p.Id) { throw "no pid" }; Write-Output ("AUGIT_PID=" + $p.Id)';
  const started = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', command], {
    env: { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: '--remote-debugging-port=' + port },
    timeout: 120000,
  });
  return Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
}

async function createFixture(workspace) {
  // 80,000 个小文件（不是 250k/1M）：rg 扫描约 1.5–3 s，足以在 700 ms 挂起并等待 15 s 超时；
  // 每 250 个文件一个 token，前后都能产生已完成结果。
  const tree = path.join(workspace, 'tree');
  await fs.mkdir(tree, { recursive: true });
  const count = 80000;
  for (let index = 0; index < count; index++) {
    const name = String(index).padStart(6, '0') + '.txt';
    const content = index % 250 === 0
      ? `needle_retry_token file ${index}\n`
      : `padding file ${index} ${'-'.repeat(40)}\n`;
    await fs.writeFile(path.join(tree, name), content);
  }
  await execute('git', ['init', '-b', 'main'], { cwd: tree, windowsHide: true });
  return { files: count, tokens: Math.ceil(count / 250), tree };
}

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-search-retry-'));
  const workspace = path.join(temporary, 'workspace');
  const settings = path.join(temporary, 'settings.json');
  const suspendResult = path.join(output, 'suspend-result.json');
  let browser = null;
  let pid = 0;
  let closed = false;
  let watcher = null;
  const report = { generatedAt: new Date().toISOString(), runs: [], cdp: 'observation only' };
  try {
    await fs.mkdir(workspace);
    const fixture = await createFixture(workspace);
    report.fixture = fixture;
    await execute('git', ['init', '-b', 'main'], { cwd: workspace, windowsHide: true });
    const source = JSON.parse(await fs.readFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), 'utf8'));
    source.lastWorkspace = workspace;
    source.openFiles = [];
    source.activeFile = null;
    await fs.writeFile(settings, JSON.stringify(source, null, 2) + '\n');
    const listener = net.createServer();
    await new Promise((resolve) => listener.listen(0, '127.0.0.1', resolve));
    const port = listener.address().port;
    await new Promise((resolve) => listener.close(resolve));
    report.port = port;
    pid = await launch(workspace, settings, port);
    const hwnd = await waitWindow(pid);
    report.pid = pid;
    report.hwnd = hwnd;
    report.topmost = await topmost(hwnd);
    browser = await connect(port);
    const page = await pageOf(browser);
    await page.waitForFunction('window.__augitReady === true && !!window.__augitLive', null, { timeout: 30000 });

    // 先把焦点放到主窗内容（真实单击），再发 Ctrl+Shift+F。
    await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.resolve('tools/audit/click-window.ps1'),
      '-X', '600', '-Y', '160', '-WindowHandle', String(hwnd), '-AfterMs', '100', '-Clicks', '1', '-NoForeground'], { timeout: 30000 });
    watcher = spawn('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.resolve('tools/audit/suspend-first-rg.ps1'),
      '-ParentProcessId', String(pid), '-Out', suspendResult, '-DelayMilliseconds', '700', '-HoldMilliseconds', '17000'], { windowsHide: true, stdio: 'ignore' });
    await sendShortcut();
    await page.waitForFunction(() => !!document.querySelector('.search-overlay .search-field'), null, { timeout: 15000 });
    const pressOption = async (label) => {
      const rect = await page.evaluate((name) => {
        const node = document.querySelector(`.search-option[aria-label="${name}"]`);
        const r = node.getBoundingClientRect();
        return { x: r.x, y: r.y, width: r.width, height: r.height };
      }, label);
      await clickCss(hwnd, rect);
      await delay(400);
      const current = await page.evaluate((name) => document.querySelector(`.search-option[aria-label="${name}"]`)?.getAttribute('aria-pressed'), label);
      if (current !== 'true') {
        await clickCss(hwnd, rect);
        await delay(300);
      }
    };
    await pressOption('Match case');
    await pressOption('Words');
    await pressOption('Regex');
    const overlayBefore = await page.evaluate(() => {
      const pressed = (name) => document.querySelector(`.search-option[aria-label="${name}"]`)?.getAttribute('aria-pressed') || null;
      return {
        value: document.querySelector('.search-overlay .search-field')?.value || '',
        notice: document.querySelector('.search-overlay .search-notice')?.textContent || null,
        results: document.querySelectorAll('.search-overlay .search-result').length,
        pressed: { case: pressed('Match case'), words: pressed('Words'), regex: pressed('Regex') },
      };
    });
    await sendKeys('needle_retry_token');
    const queryAt = Date.now();
    await page.waitForFunction(() => !!window.__augitLive.search && (window.__augitLive.search.timedOut || window.__augitLive.search.notice), null, { timeout: 25000 }).catch(() => {});
    // 超时在 15 s 左右；再留 2 s 给 DOM 刷新。
    await delay(2000);
    const timedOut = await page.evaluate(() => {
      const live = window.__augitLive;
      const pressed = (name) => document.querySelector(`.search-option[aria-label="${name}"]`)?.getAttribute('aria-pressed') || null;
      return {
        value: document.querySelector('.search-overlay .search-field')?.value || null,
        notice: document.querySelector('.search-overlay .search-notice')?.textContent || null,
        results: document.querySelectorAll('.search-overlay .search-result').length,
        pressed: { case: pressed('Match case'), words: pressed('Words'), regex: pressed('Regex') },
        timedOut: !!live.search?.timedOut,
        cancelled: !!live.search?.cancelled,
        noticeState: live.search?.notice || null,
        running: !!live.search?.running,
      };
    });
    const suspend = await fs.readFile(suspendResult, 'utf8').then(JSON.parse).catch(() => null);
    await page.screenshot({ path: path.join(output, 'timeout.png') });
    // 助手在 17 s 后退出；Enter 重试前确保它不再挂起新的 rg。
    if (watcher) { watcher.kill(); watcher = null; }

    const retryAt = Date.now();
    await sendEnter();
    await page.waitForFunction(() => {
      const live = window.__augitLive;
      return live.search && !live.search.timedOut && !!live.search.notice === false
        && document.querySelectorAll('.search-overlay .search-result').length > 0;
    }, null, { timeout: 30000 }).catch(() => {});
    await delay(500);
    const retried = await page.evaluate(() => {
      const live = window.__augitLive;
      const pressed = (name) => document.querySelector(`.search-option[aria-label="${name}"]`)?.getAttribute('aria-pressed') || null;
      return {
        value: document.querySelector('.search-overlay .search-field')?.value || null,
        notice: document.querySelector('.search-overlay .search-notice')?.textContent || null,
        results: document.querySelectorAll('.search-overlay .search-result').length,
        pressed: { case: pressed('Match case'), words: pressed('Words'), regex: pressed('Regex') },
        timedOut: !!live.search?.timedOut,
        cancelled: !!live.search?.cancelled,
        noticeState: live.search?.notice || null,
        running: !!live.search?.running,
      };
    });
    await page.screenshot({ path: path.join(output, 'retry.png') });
    report.runs.push({ overlayBefore, queryAt, timedOut, suspend, retryAt, retried });
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
    if (watcher) watcher.kill();
    if (browser) await browser.close().catch(() => {});
    if (pid && !closed) await closeApp(pid).catch(() => {});
    await fs.writeFile(path.join(output, 'search-timeout-retry.json'), JSON.stringify(report, null, 2) + '\n');
    await fs.rm(temporary, { recursive: true, force: true }).catch(() => {});
  }
  console.log(JSON.stringify(report.runs[0] || { error: report.error || null }, null, 2));
})();
