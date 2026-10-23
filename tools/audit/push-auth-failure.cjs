// Push 认证失败（HTTP 401）正常入口证据。
//
// 本地 bare remote 建立 upstream 与一条待推提交，然后把 origin 改成只返回 401 的本地 HTTP
// 端点；真实外壳 Push → 失败 balloon。服务和应用都由本脚本管理，不改产品代码。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const http = require('node:http');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/push-auth-failure-2026-10-24');
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
  return Number((await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 })).stdout.trim());
}

async function topmost(hwnd) {
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class AuthTop {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
try { [void][AuthTop]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$h = [IntPtr]${hwnd}
[void][AuthTop]::ShowWindow($h, 5)
[void][AuthTop]::BringWindowToTop($h)
$top = [AuthTop]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0003)
[void][AuthTop]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 350
Write-Output ("TOPMOST=" + $top)`;
  return (await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 })).stdout.trim();
}

async function clickRect(hwnd, rect, label) {
  const result = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.resolve('tools/audit/click-window.ps1'),
    '-X', String(rect.x + rect.width / 2), '-Y', String(rect.y + rect.height / 2),
    '-WindowHandle', String(hwnd), '-AfterMs', '100', '-Clicks', '1', '-NoForeground'], { timeout: 30000 });
  return { label, rect, clickUtc: /click_utc=([^\s]+)/.exec(result.stdout)?.[1] || null };
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
    env: { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: '--remote-debugging-port=' + port, GIT_TERMINAL_PROMPT: '0', GCM_INTERACTIVE: 'never' },
    timeout: 120000,
  });
  return Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
}

async function git(args, cwd) {
  return (await execute('git', args, { cwd, windowsHide: true, timeout: 120000 })).stdout;
}

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-push-auth-'));
  const seed = path.join(temporary, 'repo');
  const bare = path.join(temporary, 'remote.git');
  const settings = path.join(temporary, 'settings.json');
  let server = null;
  let browser = null;
  let pid = 0;
  let closed = false;
  const report = { generatedAt: new Date().toISOString(), requests: [], runs: [], cdp: 'observation only' };
  try {
    await fs.mkdir(seed);
    await execute('git', ['init', '--bare', bare], { cwd: temporary, windowsHide: true });
    await git(['init', '-b', 'main'], seed);
    await git(['config', 'user.name', 'Push Auth'], seed);
    await git(['config', 'user.email', 'push-auth@example.invalid'], seed);
    await git(['config', 'commit.gpgsign', 'false'], seed);
    await fs.writeFile(path.join(seed, 'base.txt'), 'base\n');
    await git(['add', 'base.txt'], seed);
    await git(['commit', '-m', 'test: initial'], seed);
    await git(['branch', '-M', 'main'], seed);
    await git(['remote', 'add', 'origin', bare], seed);
    await git(['push', '-u', 'origin', 'main'], seed);
    await git(['symbolic-ref', 'HEAD', 'refs/heads/main'], bare);
    await fs.writeFile(path.join(seed, 'local.txt'), 'local\n');
    await git(['add', 'local.txt'], seed);
    await git(['commit', '-m', 'test: local ahead'], seed);
    // 只返回 401 的本地 HTTP 端点；真实 Git 会走 HTTP 认证失败路径。
    server = http.createServer((request, response) => {
      report.requests.push({ method: request.method, url: request.url, at: new Date().toISOString() });
      response.writeHead(401, { 'WWW-Authenticate': 'Basic realm="Augit Auth Probe"', 'Content-Type': 'text/plain' });
      response.end('authentication required\n');
    });
    await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
    const authPort = server.address().port;
    report.authPort = authPort;
    await git(['remote', 'set-url', 'origin', `http://127.0.0.1:${authPort}/repo.git`], seed);
    const source = JSON.parse(await fs.readFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), 'utf8'));
    source.lastWorkspace = seed;
    if (darkNarrow) source.theme = 'Dark';
    source.openFiles = [];
    source.activeFile = null;
    await fs.writeFile(settings, JSON.stringify(source, null, 2) + '\n');
    const listener = net.createServer();
    await new Promise((resolve) => listener.listen(0, '127.0.0.1', resolve));
    const debugPort = listener.address().port;
    await new Promise((resolve) => listener.close(resolve));
    report.port = debugPort;
    pid = await launch(seed, settings, debugPort);
    const hwnd = await waitWindow(pid);
    report.pid = pid;
    report.hwnd = hwnd;
    report.topmost = await topmost(hwnd);
    browser = await connect(debugPort);
    const page = await pageOf(browser);
    await page.waitForFunction('window.__augitReady === true && window.__augitGitReady === true', null, { timeout: 30000 });
    await page.waitForFunction(() => !!document.querySelector('.top-chip.branch-chip'), null, { timeout: 15000 });
    const chipRect = await page.evaluate(() => {
      const node = document.querySelector('.top-chip.branch-chip');
      const r = node.getBoundingClientRect();
      return { x: r.x, y: r.y, width: r.width, height: r.height };
    });
    const chip = await clickRect(hwnd, chipRect, 'branch-chip');
    await page.waitForSelector('[data-popover-action="push"]', { timeout: 10000 });
    const pushRect = await page.evaluate(() => {
      const node = document.querySelector('[data-popover-action="push"]');
      const r = node.getBoundingClientRect();
      return { x: r.x, y: r.y, width: r.width, height: r.height };
    });
    const pushAction = await clickRect(hwnd, pushRect, 'push-action');
    await page.waitForSelector('.push-dialog [data-push-action="confirm"]', { timeout: 15000 });
    await page.waitForFunction(() => !document.querySelector('[data-push-action="confirm"]').disabled, null, { timeout: 15000 });
    const before = await page.evaluate(() => ({
      dialog: !!document.querySelector('.push-dialog'),
      commits: [...document.querySelectorAll('.push-commit')].map((row) => row.innerText.trim()),
      summary: document.querySelector('.push-summary')?.innerText || null,
      remote: window.__augitLive?.push?.upstream || null,
    }));
    const confirmRect = await page.evaluate(() => {
      const node = document.querySelector('[data-push-action="confirm"]');
      const r = node.getBoundingClientRect();
      return { x: r.x, y: r.y, width: r.width, height: r.height };
    });
    const confirm = await clickRect(hwnd, confirmRect, 'confirm-push');
    await page.waitForSelector('.toast .toast-action', { timeout: 45000 });
    await delay(500);
    const failure = await page.evaluate(() => {
      const toast = document.querySelector('.toast');
      return {
        dialog: !!document.querySelector('.push-dialog'),
        title: toast?.querySelector('.toast-title')?.textContent || null,
        text: toast?.querySelector('.toast-text')?.textContent || null,
        action: toast?.querySelector('.toast-action')?.textContent || null,
        actionId: toast?.querySelector('.toast-action')?.dataset.toastAction || null,
      };
    });
    await page.screenshot({ path: path.join(output, 'push-auth-failure.png') });
    let narrowFailure = null;
    if (darkNarrow) {
      const resize = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class AuthNarrow { [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags); [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); }
"@
try { [void][AuthNarrow]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
[void][AuthNarrow]::SetWindowPos([IntPtr]${hwnd}, [IntPtr]::Zero, 0, 0, 1100, 800, (0x0004 -bor 0x0010))`;
      await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', resize], { timeout: 30000 });
      await delay(800);
      narrowFailure = await page.evaluate(() => {
        const toast = document.querySelector('.toast');
        const rect = toast ? toast.getBoundingClientRect() : null;
        return {
          title: toast?.querySelector('.toast-title')?.textContent || null,
          text: toast?.querySelector('.toast-text')?.textContent || null,
          theme: document.body.dataset.theme || null,
          viewport: { width: window.innerWidth, height: window.innerHeight },
          toast: rect ? { x: Math.round(rect.x), y: Math.round(rect.y), width: Math.round(rect.width), height: Math.round(rect.height) } : null,
        };
      });
      await page.screenshot({ path: path.join(output, 'push-auth-failure-dark-narrow.png') });
    }
    report.runs.push({ chip, pushAction, before, confirm, failure, narrowFailure });
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
    if (server) await new Promise((resolve) => server.close(resolve));
    await fs.writeFile(path.join(output, 'push-auth-failure.json'), JSON.stringify(report, null, 2) + '\n');
    await fs.rm(temporary, { recursive: true, force: true }).catch(() => {});
  }
  console.log(JSON.stringify(report.runs[0] || { error: report.error || null, requests: report.requests }, null, 2));
})();
