// Diff 失败 → 保留标签 → 再次双击重试成功的正常入口证据。
//
// 受控故障：临时编译一个 git 包装器，只对包含 changed.txt 的 `git diff` 第一次返回 1
// （其余命令原样转发真实 git）。这不是替换产品逻辑，也不改生产超时；用于稳定触发
// `openChangeDiff` 的失败分支，再验证第二次双击走同一条重试路径。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/diff-failure-retry-2026-10-24');
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
public static class FailTop {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
try { [void][FailTop]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$h = [IntPtr]${hwnd}
[void][FailTop]::ShowWindow($h, 5)
[void][FailTop]::BringWindowToTop($h)
$top = [FailTop]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0003)
[void][FailTop]::SetForegroundWindow($h)
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

function wrapperSource(realGit) {
  return `#nullable disable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
class Program {
  static int Main(string[] args) {
    var marker = Environment.GetEnvironmentVariable("AUGIT_DIFF_FAIL_MARKER");
    if (args.Contains("diff") && args.Any(a => a.Contains("changed.txt"))
        && !string.IsNullOrEmpty(marker) && !File.Exists(marker)) {
      File.WriteAllText(marker, "failed-once");
      Console.Error.WriteLine("fatal: forced diff failure for acceptance");
      return 1;
    }
    var psi = new ProcessStartInfo(@"${realGit}") {
      UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
      WorkingDirectory = Environment.CurrentDirectory
    };
    foreach (var arg in args) psi.ArgumentList.Add(arg);
    using var process = Process.Start(psi);
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    Console.Out.Write(stdout); Console.Error.Write(stderr);
    return process.ExitCode;
  }
}
`;
}

async function buildWrapper(root, realGit) {
  const project = path.join(root, 'GitFailWrap');
  await fs.mkdir(project, { recursive: true });
  await fs.writeFile(path.join(project, 'GitFailWrap.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <EnableNETAnalyzers>false</EnableNETAnalyzers>
    <RunAnalyzers>false</RunAnalyzers>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
`);
  await fs.writeFile(path.join(project, 'Program.cs'), wrapperSource(realGit));
  await execute('dotnet', ['build', project, '-c', 'Release', '--nologo', '-p:EnableNETAnalyzers=false', '-p:RunAnalyzers=false', '-p:Nullable=disable', '-p:TreatWarningsAsErrors=false'], { timeout: 180000 });
  return path.join(project, 'bin', 'Release', 'net10.0', 'GitFailWrap.exe');
}

async function launch(workspace, settings, port, marker) {
  const literal = (value) => "'" + String(value).replaceAll("'", "''") + "'";
  const command = '$ErrorActionPreference="Stop"; $p = & ' + literal(path.resolve('tools/start-augit.ps1'))
    + ' -Workspace ' + literal(workspace) + ' -SettingsFile ' + literal(settings)
    + ' -SkipBuild; if (-not $p.Id) { throw "no pid" }; Write-Output ("AUGIT_PID=" + $p.Id)';
  const started = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', command], {
    env: { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: '--remote-debugging-port=' + port, AUGIT_DIFF_FAIL_MARKER: marker },
    timeout: 120000,
  });
  return Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
}

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-diff-fail-'));
  const workspace = path.join(temporary, 'workspace');
  const settings = path.join(temporary, 'settings.json');
  const marker = path.join(temporary, 'diff-failed-once.marker');
  let browser = null;
  let pid = 0;
  let closed = false;
  const report = { generatedAt: new Date().toISOString(), wrapper: null, runs: [], cdp: 'observation only' };
  try {
    await fs.mkdir(workspace);
    const realGit = (await execute('where.exe', ['git'], { timeout: 10000 })).stdout.split(/\r?\n/).find(Boolean);
    const wrapper = await buildWrapper(temporary, realGit);
    report.wrapper = { path: wrapper, realGit, sha256: require('crypto').createHash('sha256').update(await fs.readFile(wrapper)).digest('hex').toUpperCase() };
    await fs.writeFile(path.join(workspace, 'changed.txt'), 'line one\nline two\nline three\n');
    await execute('git', ['init', '-b', 'main'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.name', 'Augit Diff Fail'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.email', 'diff-fail@example.invalid'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'commit.gpgsign', 'false'], { cwd: workspace, windowsHide: true });
    await execute('git', ['add', 'changed.txt'], { cwd: workspace, windowsHide: true });
    await execute('git', ['commit', '-m', 'test: diff failure fixture'], { cwd: workspace, windowsHide: true });
    await fs.writeFile(path.join(workspace, 'changed.txt'), 'line one\nline two changed\nline three\nline four\n');
    const sourceSettings = JSON.parse(await fs.readFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), 'utf8'));
    sourceSettings.gitExecutablePath = wrapper;
    sourceSettings.lastWorkspace = workspace;
    sourceSettings.openFiles = [];
    sourceSettings.activeFile = null;
    await fs.writeFile(settings, JSON.stringify(sourceSettings, null, 2) + '\n');
    const listener = net.createServer();
    await new Promise((resolve) => listener.listen(0, '127.0.0.1', resolve));
    const port = listener.address().port;
    await new Promise((resolve) => listener.close(resolve));
    report.port = port;
    pid = await launch(workspace, settings, port, marker);
    const hwnd = await waitWindow(pid);
    report.pid = pid;
    report.hwnd = hwnd;
    report.topmost = await topmost(hwnd);
    browser = await connect(port);
    const page = await pageOf(browser);
    await page.waitForFunction('window.__augitReady === true && !!window.__augitLive', null, { timeout: 30000 });
    if (!(await page.evaluate(() => document.querySelectorAll('.changes-list .change-file-row').length))) {
      const railRect = await page.evaluate(() => {
        const rail = document.querySelector('.rail-button[data-tool="commit"]');
        if (!rail) return null;
        const rect = rail.getBoundingClientRect();
        return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
      });
      if (!railRect) throw new Error('找不到 Commit 工具轨按钮');
      await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.resolve('tools/audit/click-window.ps1'),
        '-X', String(railRect.x + railRect.width / 2), '-Y', String(railRect.y + railRect.height / 2),
        '-WindowHandle', String(hwnd), '-AfterMs', '400', '-Clicks', '1', '-NoForeground'], { timeout: 30000 });
    }
    await page.waitForFunction(() => !!document.querySelector('.changes-list .change-file-row[data-path="changed.txt"]'), null, { timeout: 30000 });
    const row = await page.evaluate(() => {
      const node = document.querySelector('.changes-list .change-file-row[data-path="changed.txt"]');
      const rect = node.getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
    });
    const doubleClick = async (label) => {
      const result = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.resolve('tools/audit/click-window.ps1'),
        '-X', String(row.x + row.width / 2), '-Y', String(row.y + row.height / 2),
        '-WindowHandle', String(hwnd), '-AfterMs', '80', '-Clicks', '2', '-GapMs', '80', '-NoForeground'], { timeout: 30000 });
      const clickUtc = /click_utc=([^\s]+)/.exec(result.stdout)?.[1] || null;
      return { label, clickUtc };
    };

    const firstClick = await doubleClick('first');
    await page.waitForFunction(() => !!document.querySelector('[data-augit-diff-error]'), null, { timeout: 30000 });
    await delay(1000);
    const rectOf = (selector) => {
      const node = document.querySelector(selector);
      if (!node) return null;
      const rect = node.getBoundingClientRect();
      return { x: Math.round(rect.x), y: Math.round(rect.y), width: Math.round(rect.width), height: Math.round(rect.height), scrollTop: node.scrollTop || 0 };
    };
    const failure = await page.evaluate(() => {
      const rectOf = (selector) => {
        const node = document.querySelector(selector);
        if (!node) return null;
        const rect = node.getBoundingClientRect();
        return { x: Math.round(rect.x), y: Math.round(rect.y), width: Math.round(rect.width), height: Math.round(rect.height), scrollTop: node.scrollTop || 0 };
      };
      return {
        error: document.querySelector('[data-augit-diff-error]')?.textContent || null,
        tabs: (window.__augitLive.tabs || []).map((tab) => ({ kind: tab.kind, path: tab.path, title: tab.title })),
        activeTab: window.__augitLive.activeTabId,
        diffLoading: !!window.__augitLive.diffLoading,
        diffError: window.__augitLive.diffError || null,
        rows: document.querySelectorAll('.diff-code-line').length,
        editor: window.__augitLive.editor,
        layout: {
          editorArea: rectOf('.editor-area'),
          editorTabs: rectOf('.editor-tabs'),
          activeTab: rectOf('.editor-tabs [aria-selected="true"], .editor-tabs .tab.active'),
          diffLayout: rectOf('.editor-content .diff-layout'),
          toolbar: rectOf('.editor-content .diff-toolbar'),
          filebar: rectOf('.editor-content .reference-filebar'),
          notice: rectOf('[data-augit-diff-error]'),
          content: rectOf('.editor-content'),
          bodyScroll: document.scrollingElement ? document.scrollingElement.scrollTop : null,
        },
      };
    });
    await page.screenshot({ path: path.join(output, 'failure.png') });
    await delay(500);
    const settled = await page.evaluate(() => {
      const node = document.querySelector('.editor-content .diff-toolbar');
      const tab = document.querySelector('.editor-tabs [aria-selected="true"], .editor-tabs .tab.active');
      const rect = (el) => el ? { y: Math.round(el.getBoundingClientRect().y), height: Math.round(el.getBoundingClientRect().height) } : null;
      return { toolbar: rect(node), tab: rect(tab), scroll: document.scrollingElement ? document.scrollingElement.scrollTop : null };
    });

    const secondClick = await doubleClick('retry');
    await page.waitForFunction(() => {
      const live = window.__augitLive;
      return !document.querySelector('[data-augit-diff-error]') && !live.diffLoading
        && live.diff && live.diff.path === 'changed.txt' && document.querySelectorAll('.diff-code-line').length > 0;
    }, null, { timeout: 30000 });
    await delay(150);
    const retry = await page.evaluate(() => ({
      error: !!document.querySelector('[data-augit-diff-error]'),
      tabs: (window.__augitLive.tabs || []).map((tab) => ({ kind: tab.kind, path: tab.path, title: tab.title })),
      activeTab: window.__augitLive.activeTabId,
      diffPath: window.__augitLive.diff?.path || null,
      diffLoading: !!window.__augitLive.diffLoading,
      diffError: window.__augitLive.diffError || null,
      rows: document.querySelectorAll('.diff-code-line').length,
      bodyText: (document.querySelector('.diff-columns')?.textContent || '').slice(0, 200),
    }));
    await page.screenshot({ path: path.join(output, 'retry.png') });
    report.runs.push({ firstClick, failure, settled, secondClick, retry });
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
    await fs.writeFile(path.join(output, 'diff-failure-retry.json'), JSON.stringify(report, null, 2) + '\n');
    await fs.rm(temporary, { recursive: true, force: true }).catch(() => {});
  }
  console.log(JSON.stringify(report.runs[0] || { error: report.error || null }, null, 2));
})();
