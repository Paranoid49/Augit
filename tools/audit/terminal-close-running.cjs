// 关闭运行中终端的确认 → Stop Command and Close → 子进程终止的正常入口证据。
//
// 真实外壳打开 Terminal，输入 ping -t 127.0.0.1，点击 Local 标签关闭，确认框点
// Stop Command and Close；记录 ping 子进程消失、终端工具关闭。输入/点击均为 Win32。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/terminal-close-running-2026-10-24');
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
public static class TermTop {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
try { [void][TermTop]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$h = [IntPtr]${hwnd}
[void][TermTop]::ShowWindow($h, 5)
[void][TermTop]::BringWindowToTop($h)
$top = [TermTop]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0003)
[void][TermTop]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 350
Write-Output ("TOPMOST=" + $top)`;
  return (await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 })).stdout.trim();
}

async function clickRect(hwnd, rect, label) {
  // click-window.ps1 在 168 DPI 的 PowerShell 进程里按逻辑坐标 SetCursorPos，实际会落到
  // 未缩放的屏幕点（本轮实测 923,573 → 992,623）。这里显式计算客户区原点 + DPI 比例，
  // 在 DPI-aware 进程中点击物理点。
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class TermClick {
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
try { [void][TermClick]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$h = [IntPtr]${hwnd}
$rect = New-Object TermClick+Rect
$origin = New-Object TermClick+Point
[void][TermClick]::GetClientRect($h, [ref]$rect)
[void][TermClick]::ClientToScreen($h, [ref]$origin)
$scale = [TermClick]::GetDpiForWindow($h) / 96.0
$x = [int][Math]::Round($origin.X + ${rect.x + rect.width / 2} * $scale)
$y = [int][Math]::Round($origin.Y + ${rect.y + rect.height / 2} * $scale)
[void][TermClick]::SetCursorPos($x, $y)
[TermClick]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
[TermClick]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
Write-Output ("click_utc=" + [DateTime]::UtcNow.ToString('o') + " screen=" + $x + "," + $y)`;
  const result = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
  return { label, rect, clickUtc: /click_utc=([^\s]+)/.exec(result.stdout)?.[1] || null, screen: /screen=([^\s]+)/.exec(result.stdout)?.[1] || null };
}

async function pasteAndEnter(text) {
  const literal = String(text).replaceAll("'", "''");
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class TermKeys { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra); }
"@
$old = Get-Clipboard -Raw -ErrorAction SilentlyContinue
Set-Clipboard -Value '${literal}'
[TermKeys]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
[TermKeys]::keybd_event(0x56, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 40
[TermKeys]::keybd_event(0x56, 0, 2, [UIntPtr]::Zero)
[TermKeys]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 120
[TermKeys]::keybd_event(0x0D, 0, 0, [UIntPtr]::Zero)
[TermKeys]::keybd_event(0x0D, 0, 2, [UIntPtr]::Zero)
if ($null -ne $old) { Set-Clipboard -Value $old }`;
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

async function pingUnder(pid) {
  const script = `$all = Get-CimInstance Win32_Process -Filter "Name='ping.exe'" -ErrorAction SilentlyContinue
$result = @()
foreach ($p in $all) {
  $cursor = $p
  for ($i = 0; $i -lt 6 -and $cursor; $i++) {
    $parent = Get-CimInstance Win32_Process -Filter ("ProcessId=" + [int]$cursor.ParentProcessId) -ErrorAction SilentlyContinue
    if (-not $parent) { break }
    if ([int]$parent.ProcessId -eq ${pid}) { $result += [pscustomobject]@{ ping = [int]$p.ProcessId; parent = [int]$p.ParentProcessId; command = $p.CommandLine }; break }
    $cursor = $parent
  }
}
$result | ConvertTo-Json -Compress`;
  const stdout = (await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 })).stdout.trim();
  if (!stdout) return null;
  const parsed = JSON.parse(stdout);
  return Array.isArray(parsed) ? parsed[0] : parsed;
}

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-term-close-'));
  const workspace = path.join(temporary, 'workspace');
  const settings = path.join(temporary, 'settings.json');
  let browser = null;
  let pid = 0;
  let closed = false;
  const report = { generatedAt: new Date().toISOString(), steps: [] };
  try {
    await fs.mkdir(workspace);
    await fs.writeFile(path.join(workspace, 'README.md'), '# terminal close\n');
    await execute('git', ['init', '-b', 'main'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.name', 'Terminal Close'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.email', 'term-close@example.invalid'], { cwd: workspace, windowsHide: true });
    await execute('git', ['add', 'README.md'], { cwd: workspace, windowsHide: true });
    await execute('git', ['commit', '-m', 'test: terminal close fixture'], { cwd: workspace, windowsHide: true });
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
    const railRect = await page.evaluate(() => {
      const node = document.querySelector('.rail-button[data-tool="terminal"]');
      const r = node.getBoundingClientRect();
      return { x: r.x, y: r.y, width: r.width, height: r.height };
    });
    const rail = await clickRect(hwnd, railRect, 'terminal-rail');
    await page.waitForSelector('.terminal-tool .terminal-view', { timeout: 20000 });
    await delay(2500);
    const viewRect = await page.evaluate(() => {
      const node = document.querySelector('.terminal-tool .terminal-view');
      const r = node.getBoundingClientRect();
      return { x: r.x, y: r.y, width: r.width, height: r.height };
    });
    const focus = await clickRect(hwnd, viewRect, 'terminal-view');
    await pasteAndEnter('ping -t 127.0.0.1');
    await delay(2000);
    const pingBefore = await pingUnder(pid);
    const running = await page.evaluate(() => ({
      terminal: !!document.querySelector('.terminal-tool'),
      session: document.querySelector('.terminal-session')?.textContent || null,
      close: !!document.querySelector('.terminal-session-close'),
      view: !!document.querySelector('.terminal-tool .terminal-view'),
    }));
    await page.screenshot({ path: path.join(output, 'terminal-running.png') });
    const closeRect = await page.evaluate(() => {
      const node = document.querySelector('.terminal-session-close');
      const r = node.getBoundingClientRect();
      return { x: r.x, y: r.y, width: r.width, height: r.height };
    });
    const closeClick = await clickRect(hwnd, closeRect, 'close-terminal');
    await page.waitForSelector('[data-terminal-close="confirm"]', { timeout: 15000 });
    const confirm = await page.evaluate(() => {
      const dialog = document.querySelector('[data-terminal-close="confirm"]')?.closest('.dialog');
      return {
        text: dialog?.innerText || null,
        confirmLabel: document.querySelector('[data-terminal-close="confirm"]')?.textContent || null,
        cancelLabel: document.querySelector('[data-terminal-close="cancel"]')?.textContent || null,
      };
    });
    await page.screenshot({ path: path.join(output, 'terminal-close-confirm.png') });
    const stopRect = await page.evaluate(() => {
      const node = document.querySelector('[data-terminal-close="confirm"]');
      const r = node.getBoundingClientRect();
      return { x: r.x, y: r.y, width: r.width, height: r.height };
    });
    const stop = await clickRect(hwnd, stopRect, 'stop-and-close');
    await page.waitForFunction(() => !document.querySelector('[data-terminal-close="confirm"]'), null, { timeout: 15000 });
    await delay(2500);
    let pingAfter = await pingUnder(pid);
    const deadline = Date.now() + 8000;
    while (pingAfter && Date.now() < deadline) {
      await delay(300);
      pingAfter = await pingUnder(pid);
    }
    const after = await page.evaluate(() => ({
      terminal: !!document.querySelector('.terminal-tool'),
      session: document.querySelector('.terminal-session')?.textContent || null,
      close: !!document.querySelector('.terminal-session-close'),
      view: !!document.querySelector('.terminal-view'),
      dialog: !!document.querySelector('[data-terminal-close]'),
    }));
    await page.screenshot({ path: path.join(output, 'terminal-closed.png') });
    report.steps.push({ rail, focus, running, pingBefore, closeClick, confirm, stop, pingAfter, after });
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
    await fs.writeFile(path.join(output, 'terminal-close-running.json'), JSON.stringify(report, null, 2) + '\n');
    await fs.rm(temporary, { recursive: true, force: true }).catch(() => {});
  }
  console.log(JSON.stringify(report.steps[0] || { error: report.error || null }, null, 2));
})();
