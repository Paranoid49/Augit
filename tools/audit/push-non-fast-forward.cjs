// Push non-fast-forward 失败 → 外部 rebase → Show details → 重试成功的正常入口证据。
//
// 本地 bare remote + 两个 clone：另一个 clone 先推进 main；工作区 clone 有分叉提交。
// 真实外壳中通过分支 chip → Push 弹层 → 确认触发自然 non-fast-forward；
// 关闭弹层并显示 Push failed balloon 后，外部 `git pull --rebase` 解决分叉，
// 再点 balloon 的 Show details 重开并重试成功。输入全部是 Win32 点击，CDP 只观察。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/push-non-fast-forward-2026-10-24');
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
public static class PushTop {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
try { [void][PushTop]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$h = [IntPtr]${hwnd}
[void][PushTop]::ShowWindow($h, 5)
[void][PushTop]::BringWindowToTop($h)
$top = [PushTop]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0003)
[void][PushTop]::SetForegroundWindow($h)
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
    env: { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: '--remote-debugging-port=' + port },
    timeout: 120000,
  });
  return Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
}

async function git(args, cwd) {
  return (await execute('git', args, { cwd, windowsHide: true, timeout: 120000 })).stdout;
}

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-push-nff-'));
  const remote = path.join(temporary, 'remote.git');
  const seed = path.join(temporary, 'seed');
  const workspace = path.join(temporary, 'workspace');
  const other = path.join(temporary, 'other');
  const settings = path.join(temporary, 'settings.json');
  let browser = null;
  let pid = 0;
  let closed = false;
  const report = { generatedAt: new Date().toISOString(), runs: [], cdp: 'observation only' };
  try {
    await fs.mkdir(seed);
    await execute('git', ['init', '--bare', remote], { cwd: temporary, windowsHide: true });
    await git(['init', '-b', 'main'], seed);
    await git(['config', 'user.name', 'Push NFF'], seed);
    await git(['config', 'user.email', 'push-nff@example.invalid'], seed);
    await git(['config', 'commit.gpgsign', 'false'], seed);
    await fs.writeFile(path.join(seed, 'base.txt'), 'base\n');
    await git(['add', 'base.txt'], seed);
    await git(['commit', '-m', 'test: initial'], seed);
    // 某些全局 init.defaultBranch 配置会让新建仓库仍落在 master；显式改名保证 refspec。
    await git(['branch', '-M', 'main'], seed);
    await git(['remote', 'add', 'origin', remote], seed);
    await git(['push', '-u', 'origin', 'main'], seed);
    // bare remote 的 HEAD 默认仍指向 master；不更新它，clone 会落在未出生的 master，
    // 后续 `git push origin main` 就会出现 src refspec 不存在。
    await git(['symbolic-ref', 'HEAD', 'refs/heads/main'], remote);
    await git(['clone', remote, workspace], temporary);
    await git(['clone', remote, other], temporary);
    for (const repo of [workspace, other]) {
      await git(['config', 'user.name', 'Push NFF'], repo);
      await git(['config', 'user.email', 'push-nff@example.invalid'], repo);
      await git(['config', 'commit.gpgsign', 'false'], repo);
    }
    // 另一 clone 先推进远端。
    await fs.writeFile(path.join(other, 'other.txt'), 'other\n');
    await git(['add', 'other.txt'], other);
    await git(['commit', '-m', 'test: other advances main'], other);
    await git(['push', 'origin', 'main'], other);
    // 工作区 clone 分叉。
    await fs.writeFile(path.join(workspace, 'local.txt'), 'local\n');
    await git(['add', 'local.txt'], workspace);
    await git(['commit', '-m', 'test: local diverges main'], workspace);
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
    await page.waitForFunction('window.__augitReady === true && window.__augitGitReady === true', null, { timeout: 30000 });
    await page.waitForFunction(() => !!document.querySelector('.top-chip.branch-chip'), null, { timeout: 15000 });
    const chipRect = await page.evaluate(() => {
      const node = document.querySelector('.top-chip.branch-chip');
      const rect = node.getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
    });
    const firstChip = await clickRect(hwnd, chipRect, 'branch-chip');
    await page.waitForSelector('[data-popover-action="push"]', { timeout: 10000 });
    const pushActionRect = await page.evaluate(() => {
      const node = document.querySelector('[data-popover-action="push"]');
      const rect = node.getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
    });
    const pushAction = await clickRect(hwnd, pushActionRect, 'push-action');
    await page.waitForSelector('.push-dialog [data-push-action="confirm"]', { timeout: 15000 });
    await page.waitForFunction(() => !document.querySelector('[data-push-action="confirm"]').disabled, null, { timeout: 15000 });
    const before = await page.evaluate(() => ({
      dialog: !!document.querySelector('.push-dialog'),
      confirmDisabled: document.querySelector('[data-push-action="confirm"]').disabled,
      commits: [...document.querySelectorAll('.push-commit')].map((row) => row.innerText.trim()),
      summary: document.querySelector('.push-summary')?.innerText || null,
    }));
    const confirmRect = await page.evaluate(() => {
      const node = document.querySelector('[data-push-action="confirm"]');
      const rect = node.getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
    });
    const confirm = await clickRect(hwnd, confirmRect, 'confirm-push');
    await page.waitForSelector('.toast .toast-action', { timeout: 30000 });
    await delay(300);
    const failure = await page.evaluate(() => {
      const toast = document.querySelector('.toast');
      const action = toast?.querySelector('.toast-action');
      return {
        dialog: !!document.querySelector('.push-dialog'),
        title: toast?.querySelector('.toast-title')?.textContent || null,
        text: toast?.querySelector('.toast-text')?.textContent || null,
        action: action?.textContent || null,
        actionId: action?.dataset.toastAction || null,
        actionRect: action ? (() => { const r = action.getBoundingClientRect(); return { x: r.x, y: r.y, width: r.width, height: r.height }; })() : null,
      };
    });
    await page.screenshot({ path: path.join(output, 'push-failure.png') });
    report.runs.push({ firstChip, pushAction, before, confirm, failure });

    // 外部解决分叉；应用会自己刷新状态。
    const rebaseOutput = await git(['pull', '--rebase', 'origin', 'main'], workspace);
    const remoteLog = await git(['log', '--oneline', '-3', 'main'], remote);
    const localLog = await git(['log', '--oneline', '-3'], workspace);
    report.rebase = { output: rebaseOutput.trim(), remoteLog: remoteLog.trim().split(/\r?\n/), localLog: localLog.trim().split(/\r?\n/) };
    await delay(1200);

    // 点击 balloon 的 Show details，重开失败详情。
    const actionRect = failure.actionRect;
    if (!actionRect) throw new Error('Push failed balloon 没有 Show details');
    const detailsClick = await clickRect(hwnd, actionRect, 'show-details');
    await page.waitForSelector('.push-dialog [data-push-action="confirm"]', { timeout: 15000 });
    await delay(500);
    const reopened = await page.evaluate(() => {
      const dialog = document.querySelector('.push-dialog');
      return {
        dialog: !!dialog,
        notice: dialog?.querySelector('.push-notice')?.textContent || null,
        confirmDisabled: dialog?.querySelector('[data-push-action="confirm"]')?.disabled ?? null,
        commits: [...document.querySelectorAll('.push-commit')].map((row) => row.innerText.trim()),
      };
    });
    await page.waitForFunction(() => !document.querySelector('[data-push-action="confirm"]')?.disabled, null, { timeout: 15000 }).catch(() => {});
    const retryRect = await page.evaluate(() => {
      const node = document.querySelector('[data-push-action="confirm"]');
      const rect = node.getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
    });
    const retryClick = await clickRect(hwnd, retryRect, 'retry-push');
    await page.waitForFunction(() => {
      const toast = document.querySelector('.toast');
      return !!toast && !document.querySelector('.push-dialog')
        && (toast.querySelector('.toast-title')?.textContent || '') === 'Push';
    }, null, { timeout: 30000 });
    await delay(300);
    const retried = await page.evaluate(() => {
      const toast = document.querySelector('.toast');
      return {
        dialog: !!document.querySelector('.push-dialog'),
        title: toast?.querySelector('.toast-title')?.textContent || null,
        text: toast?.querySelector('.toast-text')?.textContent || null,
        action: toast?.querySelector('.toast-action')?.textContent || null,
        branch: document.querySelector('.top-chip.branch-chip')?.textContent || null,
      };
    });
    await page.screenshot({ path: path.join(output, 'push-retry.png') });
    const finalRemoteLog = await git(['log', '--oneline', '-3', 'main'], remote);
    report.runs[0].detailsClick = detailsClick;
    report.runs[0].reopened = reopened;
    report.runs[0].retryClick = retryClick;
    report.runs[0].retried = retried;
    report.finalRemoteLog = finalRemoteLog.trim().split(/\r?\n/);
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
    await fs.writeFile(path.join(output, 'push-non-fast-forward.json'), JSON.stringify(report, null, 2) + '\n');
    await fs.rm(temporary, { recursive: true, force: true }).catch(() => {});
  }
  console.log(JSON.stringify(report.runs[0] || { error: report.error || null }, null, 2));
})();
