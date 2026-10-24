// 正常入口验收：rebase 两个文件冲突时外部混合解决，全部解决后用**真实前台 OS 鼠标**
// 点击应用内 Continue Rebase 完成；页面 locator 点击不能替代本验收。
// 仅操作临时仓库；不修改用户配置，不执行自动解决或内置编辑。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/conflict-rebase-multifile-continue-os');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function windowProbe(hwnd, points) {
  const pointChecks = points.map((point) => `
$p = New-Object Probe+POINT
$p.X = ${point.x}; $p.Y = ${point.y}
$w = [Probe]::WindowFromPoint($p)
$wpid = 0; [void][Probe]::GetWindowThreadProcessId($w, [ref]$wpid)
$wname = ''
try { $wname = (Get-Process -Id $wpid -ErrorAction Stop).ProcessName } catch { $wname = '?' }
$sb = New-Object System.Text.StringBuilder 256
[void][Probe]::GetClassName($w, $sb, 256)
Write-Output ("POINT ${point.x},${point.y} hwnd=0x" + $w.ToString("X") + " pid=" + $wpid + " process=" + $wname + " class=" + $sb.ToString() + " match=" + ($w -eq $h))`).join('\n');
  const script = `Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class Probe {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern IntPtr GetThreadDpiAwarenessContext();
  [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  public struct RECT { public int L, T, R, B; }
  public struct POINT { public int X, Y; }
}
"@
try { [void][Probe]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$before = [Probe]::GetAwarenessFromDpiAwarenessContext([Probe]::GetThreadDpiAwarenessContext())
try { [void][Probe]::SetThreadDpiAwarenessContext([IntPtr](-4)) } catch { }
$after = [Probe]::GetAwarenessFromDpiAwarenessContext([Probe]::GetThreadDpiAwarenessContext())
$h = [IntPtr]${hwnd}
$wr = New-Object Probe+RECT
[void][Probe]::GetWindowRect($h, [ref]$wr)
$cr = New-Object Probe+RECT
[void][Probe]::GetClientRect($h, [ref]$cr)
$origin = New-Object Probe+POINT
[void][Probe]::ClientToScreen($h, [ref]$origin)
Write-Output ("AWARENESS before=" + $before + " after=" + $after)
Write-Output ("WINDOW rect=" + $wr.L + "," + $wr.T + "," + ($wr.R-$wr.L) + "x" + ($wr.B-$wr.T) + " client=" + $cr.R + "x" + $cr.B + " origin=" + $origin.X + "," + $origin.Y + " dpi=" + [Probe]::GetDpiForWindow($h))
Write-Output ("SCREEN=" + [Probe]::GetSystemMetrics(0) + "x" + [Probe]::GetSystemMetrics(1))
${pointChecks}`;
  const result = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
  return result.stdout.trim();
}

async function git(cwd, ...args) {
  return execute('git', ['-C', cwd, ...args], { windowsHide: true, timeout: 30000 });
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

async function setTopmost(pid, hwnd) {
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class OsTopWin {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
"@
try { [void][OsTopWin]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$h = [IntPtr]${hwnd}
[void][OsTopWin]::ShowWindow($h, 5)
$ok = [OsTopWin]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0003)
Write-Output ("TOPMOST=" + $ok)`;
  const result = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
  return result.stdout.trim();
}

async function foreground(pid, hwnd) {
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class OsFgWin {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
}
"@
try { [void][OsFgWin]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$h = [IntPtr]${hwnd}
[void][OsFgWin]::BringWindowToTop($h)
$fg = [OsFgWin]::GetForegroundWindow()
$fgPid = 0; $fgThread = [OsFgWin]::GetWindowThreadProcessId($fg, [ref]$fgPid)
$targetPid = 0; [void][OsFgWin]::GetWindowThreadProcessId($h, [ref]$targetPid)
$self = [OsFgWin]::GetCurrentThreadId()
if ($fgThread -ne 0 -and $fgThread -ne $self) { [void][OsFgWin]::AttachThreadInput($self, $fgThread, $true) }
[void][OsFgWin]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 300
if ($fgThread -ne 0 -and $fgThread -ne $self) { [void][OsFgWin]::AttachThreadInput($self, $fgThread, $false) }
Start-Sleep -Milliseconds 300
Write-Output ("FOREGROUND=" + ([OsFgWin]::GetForegroundWindow() -eq $h) + " fgPid=" + $fgPid + " targetPid=" + $targetPid)`;
  const result = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
  return result.stdout.trim();
}

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-conflict-skip-'));
  const workspace = path.join(temporary, 'repo');
  const settings = path.join(temporary, 'settings.json');
  const listener = net.createServer();
  let port = 0;
  let shell = null;
  let browser = null;
  let page = null;
  const report = { launcher: 'tools/start-augit.ps1', forcedBuild: true, cdpAttached: true, checks: [] };
  try {
    await fs.mkdir(workspace);
    await git(workspace, 'init', '-b', 'main');
    await git(workspace, 'config', 'user.name', 'Augit Audit');
    await git(workspace, 'config', 'user.email', 'augit-audit@example.invalid');
    await fs.writeFile(path.join(workspace, 'a.txt'), 'a-base\n');
    await fs.writeFile(path.join(workspace, 'b.txt'), 'b-base\n');
    await git(workspace, 'add', 'a.txt', 'b.txt');
    await git(workspace, 'commit', '-m', 'test: base');
    await git(workspace, 'checkout', '-b', 'feature');
    await fs.writeFile(path.join(workspace, 'a.txt'), 'a-feature\n');
    await fs.writeFile(path.join(workspace, 'b.txt'), 'b-feature\n');
    await git(workspace, 'add', 'a.txt', 'b.txt');
    await git(workspace, 'commit', '-m', 'test: feature-conflict');
    await fs.writeFile(path.join(workspace, 'other.txt'), 'feature-second\n');
    await git(workspace, 'add', 'other.txt');
    await git(workspace, 'commit', '-m', 'test: feature-second');
    await git(workspace, 'checkout', 'main');
    await fs.writeFile(path.join(workspace, 'a.txt'), 'a-main\n');
    await fs.writeFile(path.join(workspace, 'b.txt'), 'b-main\n');
    await git(workspace, 'add', 'a.txt', 'b.txt');
    await git(workspace, 'commit', '-m', 'test: main');
    await git(workspace, 'checkout', 'feature');
    let rebaseFailed = false;
    try {
      await git(workspace, 'rebase', 'main');
    } catch {
      rebaseFailed = true;
    }
    assert.ok(rebaseFailed, 'rebase 必须因冲突而中断');
    const conflictStatus = await git(workspace, 'status', '--porcelain');
    assert.match(conflictStatus.stdout, /^UU a\.txt/m, 'a.txt 必须冲突');
    assert.match(conflictStatus.stdout, /^UU b\.txt/m, 'b.txt 必须冲突');
    report.checks.push({ name: 'rebase-conflict-fixture', status: conflictStatus.stdout.trim() });

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
      if (message.type() === 'error') pageErrors.push('console:' + message.text());
    });
    page.on('requestfailed', (request) => pageErrors.push('requestfailed:' + request.url()));
    await page.waitForFunction(() => window.__augitReady === true, null, { timeout: 40000 });
    await page.waitForFunction(() => window.__augitGitReady === true, null, { timeout: 40000 });
    await page.waitForSelector('.dialog.conflict-session-dialog', { timeout: 20000 });

    const actions = await page.evaluate(() => [...document.querySelectorAll('.dialog.conflict-session-dialog [data-operation-action]')]
      .map((button) => ({ action: button.dataset.operationAction, disabled: button.disabled, text: button.textContent.trim() })));
    report.checks.push({ name: 'rebase-actions', actions });
    const cjkBody = await page.evaluate(() => document.body.innerText);
    assert.equal(/[\u3400-\u9fff]/.test(cjkBody), false, 'rebase 冲突列表含中文界面文案');
    const cjkAttrs = await page.evaluate(() => [...document.querySelectorAll('[title], [aria-label]')]
      .filter((node) => node.offsetWidth || node.offsetHeight || node.getClientRects().length)
      .flatMap((node) => [node.getAttribute('title'), node.getAttribute('aria-label')])
      .filter((value) => value && /[\u3400-\u9fff]/.test(value)));
    assert.deepEqual(cjkAttrs, [], 'rebase 冲突列表 title/aria 含中文');
    report.checks.push({ name: 'rebase-conflict-cjk', bodyHasCjk: false, attrs: cjkAttrs });
    assert.ok(actions.some((item) => item.action === 'skip'), 'rebase 冲突必须提供 Skip');
    const cont = actions.find((item) => item.action === 'continue');
    assert.equal(cont, undefined, 'rebase 冲突未解决时不应提供 Continue');
    const conflictState = await page.evaluate(() => ({
      rows: [...document.querySelectorAll('.dialog.conflict-session-dialog [data-conflict-path], .dialog.conflict-session-dialog .conflict-row')].length,
      text: document.querySelector('.dialog.conflict-session-dialog')?.innerText || '',
    }));
    report.checks.push({ name: 'multifile-conflict-ui', rows: conflictState.rows, text: conflictState.text });
    assert.match(conflictState.text, /a\.txt/);
    assert.match(conflictState.text, /b\.txt/);
    await page.screenshot({ path: path.join(output, 'before-external-resolve.png') });

    const remove = await git(workspace, 'rm', 'a.txt');
    report.checks.push({ name: 'external-remove', stdout: remove.stdout.trim(), stderr: remove.stderr.trim() });
    await delay(800);
    const mid = await page.evaluate(() => ({
      dialog: !!document.querySelector('.dialog.conflict-session-dialog'),
      text: document.querySelector('.dialog.conflict-session-dialog')?.innerText || '',
      actions: [...document.querySelectorAll('.dialog.conflict-session-dialog [data-operation-action]')].map((b) => ({ action: b.dataset.operationAction, disabled: b.disabled, text: b.textContent.trim() })),
    }));
    report.checks.push({ name: 'mid-external-resolve', ...mid });
    assert.ok(mid.dialog, '外部解决一个文件后冲突窗应仍打开');
    assert.doesNotMatch(mid.text, /a\.txt/, '已删除的 a.txt 应从冲突列表消失');
    assert.match(mid.text, /b\.txt/, '未解决的 b.txt 应仍在列表');
    await fs.writeFile(path.join(workspace, 'b.txt'), 'b-resolved\n');
    await git(workspace, 'add', 'b.txt');
    report.checks.push({ name: 'external-edit-add', file: 'b.txt', content: 'b-resolved' });
    await page.waitForFunction(() => {
      const button = document.querySelector('.dialog.conflict-session-dialog [data-operation-action="continue"]');
      return !!button && !button.disabled;
    }, null, { timeout: 20000 });
    const ready = await page.evaluate(() => ({
      text: document.querySelector('.dialog.conflict-session-dialog')?.innerText || '',
      actions: [...document.querySelectorAll('.dialog.conflict-session-dialog [data-operation-action]')].map((b) => ({ action: b.dataset.operationAction, disabled: b.disabled, text: b.textContent.trim() })),
    }));
    report.checks.push({ name: 'ready-to-continue', ...ready });
    assert.ok(ready.actions.some((a) => a.action === 'continue' && !a.disabled), '全部解决后 Continue Rebase 必须启用');
    await page.screenshot({ path: path.join(output, 'ready-continue.png') });
    // 真实前台 OS 鼠标：先置 topmost/前台，再按按钮 CSS 矩形中心点击；
    // 同时记录 pointerdown/click 命中数，用于区分「输入未命中」与「产品未处理」。
    const hwnd = await waitWindow(pid);
    const button = await page.evaluate(() => {
      const node = document.querySelector('.dialog.conflict-session-dialog [data-operation-action="continue"]');
      if (!node) return null;
      const rect = node.getBoundingClientRect();
      const x = rect.x + rect.width / 2;
      const y = rect.y + rect.height / 2;
      window.__osContinueHits = { pointerdown: 0, click: 0 };
      window.__osDocHits = { pointerdown: 0, mousedown: 0, click: 0, last: null };
      document.addEventListener('pointerdown', (event) => {
        window.__osDocHits.pointerdown += 1;
        window.__osDocHits.last = {
          x: event.clientX,
          y: event.clientY,
          target: event.target && event.target.tagName
            ? event.target.tagName + '.' + String(event.target.className || '')
            : null,
        };
      }, true);
      document.addEventListener('mousedown', () => { window.__osDocHits.mousedown += 1; }, true);
      document.addEventListener('click', () => { window.__osDocHits.click += 1; }, true);
      node.addEventListener('pointerdown', () => { window.__osContinueHits.pointerdown += 1; });
      node.addEventListener('click', () => { window.__osContinueHits.click += 1; });
      const hit = document.elementFromPoint(x, y);
      return {
        x,
        y,
        width: rect.width,
        height: rect.height,
        viewport: { width: window.innerWidth, height: window.innerHeight },
        dpr: window.devicePixelRatio,
        screenX: window.screenX,
        screenY: window.screenY,
        hitAction: hit && hit.closest('[data-operation-action]')
          ? hit.closest('[data-operation-action]').dataset.operationAction
          : null,
      };
    });
    assert.ok(button, '找不到 Continue 按钮');
    assert.equal(button.hitAction, 'continue', '按钮中心必须命中 Continue：' + JSON.stringify(button));
    const topmost = await setTopmost(pid, hwnd);
    const foregroundBefore = await foreground(pid, hwnd);
    assert.match(foregroundBefore, /FOREGROUND=True/, '目标窗口必须在前台：' + foregroundBefore);
    const absoluteX = Math.round((button.screenX + button.x) * button.dpr);
    const absoluteY = Math.round((button.screenY + button.y) * button.dpr);
    const probe = await windowProbe(hwnd, [
      { x: absoluteX, y: absoluteY },
      { x: Math.round(button.x * button.dpr) + 120, y: Math.round(button.y * button.dpr) + 87 },
    ]);
    report.checks.push({ name: 'window-geometry', hwnd, button, absoluteX, absoluteY, probe });
    const click = await execute('powershell.exe', [
      '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.resolve('tools/audit/click-window.ps1'),
      '-Absolute', '-ScreenX', String(absoluteX), '-ScreenY', String(absoluteY),
      '-WindowHandle', String(hwnd), '-AfterMs', '900', '-Clicks', '1',
    ], { timeout: 30000 });
    const clickOutput = click.stdout.trim();
    assert.match(clickOutput, /foreground_match=True/, '点击前目标窗口必须真的在前台：' + clickOutput);
    await delay(200);
    const afterClick = await page.evaluate(() => ({
      hits: window.__osContinueHits || null,
      documentHits: window.__osDocHits || null,
      dialog: !!document.querySelector('.dialog.conflict-session-dialog'),
      text: document.querySelector('.dialog.conflict-session-dialog')?.innerText || '',
    }));
    await page.screenshot({ path: path.join(output, 'after-os-click.png') });
    report.checks.push({
      name: 'os-mouse-continue',
      hwnd,
      topmost,
      foregroundBefore,
      button,
      click: clickOutput,
      afterClick,
    });
    if (!afterClick.hits || afterClick.hits.pointerdown === 0) {
      throw new Error('OS 鼠标未命中 Continue（pointerdown=0）；这是输入未命中，不是按钮处理失败：'
        + clickOutput + ' | probe=' + probe);
    }
    await page.waitForFunction(() => !document.querySelector('.dialog.conflict-session-dialog'), null, { timeout: 30000 });
    await delay(500);
    const afterStatus = await git(workspace, 'status', '--porcelain');
    assert.equal(afterStatus.stdout.trim(), '', '外部解决后工作区必须干净');
    const branch = await git(workspace, 'branch', '--show-current');
    assert.equal(branch.stdout.trim(), 'feature', '外部 continue 后应仍在 feature');
    const log = await git(workspace, 'log', '--oneline', '--all');
    assert.match(log.stdout, /test: feature-second/, 'continue 后必须继续应用后续提交');
    const show = await git(workspace, 'show', '--stat', '--oneline', 'HEAD');
    report.checks.push({ name: 'external-resolve-state', status: afterStatus.stdout.trim(), branch: branch.stdout.trim(), log: log.stdout.trim(), head: show.stdout.trim() });
    await page.screenshot({ path: path.join(output, 'after-external-resolve.png') });

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
      dpr: devicePixelRatio, url: location.href,
      hasSceneQuery: new URL(location.href).searchParams.has('scene'),
      codeFont: getComputedStyle(document.querySelector('.diff-code, .code-view, body')).fontFamily,
      bodyFont: getComputedStyle(document.body).fontFamily,
      theme: document.documentElement.dataset.theme || null,
    }));
    await fs.writeFile(path.join(output, 'runtime.json'), JSON.stringify(runtime, null, 2));
    assert.equal(runtime.hasSceneQuery, false, '正常入口不得带审计场景参数');
    assert.deepEqual(pageErrors, [], '正常入口不应有页面异常：' + pageErrors.join(' | '));
    report.checks.push({ name: 'no-runtime-errors', pageErrors });
    await fs.writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2));
    console.log('CONFLICT_REBASE_CONTINUE_OK pid=' + pid + ' port=' + port);
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
    await fs.writeFile(path.join(output, 'cleanup.json'), JSON.stringify(report, null, 2)).catch(() => {});
  }
})();
