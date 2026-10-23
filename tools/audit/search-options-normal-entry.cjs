// 搜索选项组合的深色 / 窄窗正常入口证据（浅色基线之外）。
//
// 真实外壳、真实 Ctrl+Shift+F、剪贴板粘贴查询、Win32 点击选项；CDP 仅观察结果/几何与截图。
// 覆盖默认、Match case、Words、Regex、Include ignored，并在窄窗下确认浮层不越界。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/search-options-normal-entry-2026-10-24');
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
public static class SearchTop2 {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
try { [void][SearchTop2]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$h = [IntPtr]${hwnd}
[void][SearchTop2]::ShowWindow($h, 5)
[void][SearchTop2]::BringWindowToTop($h)
$top = [SearchTop2]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0003)
[void][SearchTop2]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 350
Write-Output ("TOPMOST=" + $top)`;
  return (await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 })).stdout.trim();
}

async function clickRect(hwnd, rect, label) {
  const result = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.resolve('tools/audit/click-window.ps1'),
    '-X', String(rect.x + rect.width / 2), '-Y', String(rect.y + rect.height / 2),
    '-WindowHandle', String(hwnd), '-AfterMs', '120', '-Clicks', '1', '-NoForeground'], { timeout: 30000 });
  return { label, rect, clickUtc: /click_utc=([^\s]+)/.exec(result.stdout)?.[1] || null };
}

async function keyCombo() {
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SearchCombo { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra); }
"@
[SearchCombo]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
[SearchCombo]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero)
[SearchCombo]::keybd_event(0x46, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 40
[SearchCombo]::keybd_event(0x46, 0, 2, [UIntPtr]::Zero)
[SearchCombo]::keybd_event(0x10, 0, 2, [UIntPtr]::Zero)
[SearchCombo]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)`;
  await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
}

async function pasteText(value) {
  const literal = String(value).replaceAll("'", "''");
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class PasteCombo { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra); }
"@
$old = Get-Clipboard -Raw -ErrorAction SilentlyContinue
Set-Clipboard -Value '${literal}'
[PasteCombo]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
[PasteCombo]::keybd_event(0x56, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 40
[PasteCombo]::keybd_event(0x56, 0, 2, [UIntPtr]::Zero)
[PasteCombo]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)
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

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-search-options-'));
  const workspace = path.join(temporary, 'workspace');
  const settings = path.join(temporary, 'settings.json');
  let browser = null;
  let pid = 0;
  let closed = false;
  const report = { generatedAt: new Date().toISOString(), states: [] };
  try {
    await fs.mkdir(path.join(workspace, 'src'), { recursive: true });
    await fs.mkdir(path.join(workspace, 'ignored'), { recursive: true });
    await fs.writeFile(path.join(workspace, 'src/alpha.txt'), 'Alpha alpha ALPHA alphabet\nsecond Alpha line\n');
    await fs.writeFile(path.join(workspace, 'src/beta.txt'), 'beta Alpha\n');
    await fs.writeFile(path.join(workspace, 'ignored/build.txt'), 'alpha ignored\n');
    await fs.writeFile(path.join(workspace, '.gitignore'), 'ignored/\n');
    await execute('git', ['init', '-b', 'main'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.name', 'Augit Search'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.email', 'search@example.invalid'], { cwd: workspace, windowsHide: true });
    await execute('git', ['add', '.'], { cwd: workspace, windowsHide: true });
    await execute('git', ['commit', '-m', 'test: search options fixture'], { cwd: workspace, windowsHide: true });
    const source = JSON.parse(await fs.readFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), 'utf8'));
    source.lastWorkspace = workspace;
    source.openFiles = [];
    source.activeFile = null;
    source.theme = 'Dark';
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
    await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.resolve('tools/audit/click-window.ps1'),
      '-X', '600', '-Y', '160', '-WindowHandle', String(hwnd), '-AfterMs', '120', '-Clicks', '1', '-NoForeground'], { timeout: 30000 });
    await keyCombo();
    await page.waitForSelector('.search-overlay .search-field', { timeout: 15000 });
    await pasteText('alpha');
    await page.waitForFunction(() => document.querySelectorAll('.search-overlay .search-result').length > 0, null, { timeout: 15000 });
    await delay(500);
    const read = async (label) => {
      const state = await page.evaluate(() => {
        const overlay = document.querySelector('.search-overlay');
        const rect = overlay ? overlay.getBoundingClientRect() : null;
        const pressed = (name) => document.querySelector(`.search-option[aria-label="${name}"]`)?.getAttribute('aria-pressed') || null;
        return {
          label: null,
          results: document.querySelectorAll('.search-overlay .search-result').length,
          value: document.querySelector('.search-overlay .search-field')?.value || '',
          notice: document.querySelector('.search-notice')?.textContent || null,
          pressed: { case: pressed('Match case'), words: pressed('Words'), regex: pressed('Regex') },
          ignored: !!document.querySelector('.search-ignored input')?.checked,
          overlay: rect ? { x: Math.round(rect.x), y: Math.round(rect.y), width: Math.round(rect.width), height: Math.round(rect.height) } : null,
          viewport: { width: window.innerWidth, height: window.innerHeight },
          bodyTheme: document.body.dataset.theme || null,
        };
      });
      state.label = label;
      report.states.push(state);
      return state;
    };
    const optionRect = async (label) => page.evaluate((name) => {
      const node = document.querySelector(`.search-option[aria-label="${name}"]`);
      const rect = node.getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
    }, label);
    const ignoredRect = async () => page.evaluate(() => {
      const node = document.querySelector('.search-ignored input');
      const rect = node.getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
    });
    const inputRect = async () => page.evaluate(() => {
      const node = document.querySelector('.search-overlay .search-field');
      const rect = node.getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
    });
    const selectAll = async () => {
      const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SelectAll { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra); }
"@
[SelectAll]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
[SelectAll]::keybd_event(0x41, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 30
[SelectAll]::keybd_event(0x41, 0, 2, [UIntPtr]::Zero)
[SelectAll]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)`;
      await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
    };
    const replaceQuery = async (text) => {
      await clickRect(hwnd, await inputRect(), 'search-input');
      await selectAll();
      await pasteText(text);
      await delay(900);
    };
    const toggle = async (label) => {
      const rect = label === 'ignored' ? await ignoredRect()
        : await optionRect(label === 'case' ? 'Match case' : label === 'words' ? 'Words' : 'Regex');
      await clickRect(hwnd, rect, label);
      await delay(800);
      return read(label);
    };

    const baseline = await read('baseline');
    await page.screenshot({ path: path.join(output, 'dark-baseline.png') });
    await toggle('case');
    await toggle('case');
    const ignoredOn = await toggle('ignored');
    await page.screenshot({ path: path.join(output, 'dark-ignored.png') });
    await toggle('ignored');
    await replaceQuery('alph');
    const wordsOff = await read('words-off');
    await toggle('words');
    await page.screenshot({ path: path.join(output, 'dark-words.png') });
    await toggle('words');
    await replaceQuery('^Alpha');
    const regexOff = await read('regex-off');
    await toggle('regex');
    await page.screenshot({ path: path.join(output, 'dark-regex.png') });
    await toggle('regex');

    // 窄窗：缩到 980×760 物理；记录浮层是否越界。
    const resize = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Narrow { [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags); [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); }
"@
try { [void][Narrow]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
[void][Narrow]::SetWindowPos([IntPtr]${hwnd}, [IntPtr]::Zero, 0, 0, 980, 760, (0x0004 -bor 0x0010))`;
    await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', resize], { timeout: 30000 });
    await delay(900);
    const narrow = await read('narrow');
    await page.screenshot({ path: path.join(output, 'dark-narrow.png') });
    report.runs = [{ baseline, ignoredOn, wordsOff, regexOff, states: report.states, narrow }];
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
    await fs.writeFile(path.join(output, 'search-options-normal-entry.json'), JSON.stringify(report, null, 2) + '\n');
    await fs.rm(temporary, { recursive: true, force: true }).catch(() => {});
  }
  console.log(JSON.stringify(report.runs?.[0] || { error: report.error || null }, null, 2));
})();
