// 80,000 行普通文本的 UIA 全量枚举耗时（诊断入口，强制 WebView2 accessibility）。
//
// 普通入口没有屏幕阅读器时 WebView2 不暴露 UIA 文本树；这里用
// `--force-renderer-accessibility` 打开可访问树，测量 FindAll 全量枚举与全文文本节点，
// 对比历史“20–30 秒未结束”。该 flag 只影响可访问树，不改变 DOM/虚拟窗口布局。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/large-text-uia-2026-10-24');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

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

async function closeApp(pid) {
  const script = `$p = Get-Process -Id ${pid} -ErrorAction Stop
$p.CloseMainWindow() | Out-Null
$deadline = (Get-Date).AddSeconds(20)
while (-not $p.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
if ($p.HasExited) { exit 0 }
exit 2`;
  try { await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 }); return true; } catch { return false; }
}

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-uia-large-'));
  const workspace = path.join(temporary, 'workspace');
  const settings = path.join(temporary, 'settings.json');
  let browser = null;
  let pid = 0;
  let closed = false;
  const report = { generatedAt: new Date().toISOString(), fixture: {}, runs: [] };
  try {
    await fs.mkdir(workspace);
    const lines = [];
    for (let index = 1; index <= 80000; index++) lines.push(`line ${index} ${'-'.repeat(30)}`);
    const text = lines.join('\n');
    await fs.writeFile(path.join(workspace, 'mid.txt'), text);
    await execute('git', ['init', '-b', 'main'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.name', 'Augit UIA'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.email', 'uia@example.invalid'], { cwd: workspace, windowsHide: true });
    await execute('git', ['add', 'mid.txt'], { cwd: workspace, windowsHide: true });
    await execute('git', ['commit', '-m', 'test: uia fixture'], { cwd: workspace, windowsHide: true });
    const source = JSON.parse(await fs.readFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), 'utf8'));
    source.lastWorkspace = workspace;
    source.openFiles = [];
    source.activeFile = null;
    await fs.writeFile(settings, JSON.stringify(source, null, 2) + '\n');
    report.fixture = { bytes: Buffer.byteLength(text), lines: 80000, settingsSha256: require('crypto').createHash('sha256').update(await fs.readFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'))).digest('hex').toUpperCase() };
    const listener = net.createServer();
    await new Promise((resolve) => listener.listen(0, '127.0.0.1', resolve));
    const port = listener.address().port;
    await new Promise((resolve) => listener.close(resolve));
    report.port = port;
    const literal = (value) => "'" + String(value).replaceAll("'", "''") + "'";
    const command = '$ErrorActionPreference="Stop"; $p = & ' + literal(path.resolve('tools/start-augit.ps1'))
      + ' -Workspace ' + literal(workspace) + ' -SettingsFile ' + literal(settings)
      + ' -SkipBuild; if (-not $p.Id) { throw "no pid" }; Write-Output ("AUGIT_PID=" + $p.Id)';
    const started = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', command], {
      env: { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--force-renderer-accessibility --remote-debugging-port=${port}` },
      timeout: 120000,
    });
    pid = Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
    report.pid = pid;
    browser = await connect(port);
    const page = await pageOf(browser);
    const errors = [];
    page.on('pageerror', (error) => errors.push(String(error && error.stack || error)));
    page.on('console', (message) => { if (message.type() === 'error') errors.push('console: ' + message.text()); });
    report.pageErrors = errors;
    await page.waitForFunction('window.__augitReady === true && !!window.__augitLive', null, { timeout: 30000 });
    const row = await page.evaluate(() => {
      const node = document.querySelector('.side-content.tree .tree-row[data-tree-path="mid.txt"]');
      if (!node) return null;
      const rect = node.getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
    });
    if (!row) throw new Error('项目树找不到 mid.txt');
    await page.locator('.side-content.tree .tree-row[data-tree-path="mid.txt"]').dblclick();
    await page.waitForFunction(() => {
      const code = document.querySelector('.editor-content .code-view');
      return !!code && !!code.__augitVirtual && code.__augitVirtual.lineCount === 80000;
    }, null, { timeout: 30000 });
    const virtual = await page.evaluate(() => {
      const code = document.querySelector('.editor-content .code-view');
      return { lineCount: code.__augitVirtual.lineCount, visibleRows: code.querySelectorAll('.code-line').length, a11yLength: code.querySelector('.virtual-text-accessible').textContent.length, role: code.querySelector('.virtual-text-accessible').getAttribute('role') };
    });
    const a11yStarted = Date.now();
    await page.waitForFunction(() => {
      const code = document.querySelector('.editor-content .code-view');
      return !!code && code.getAttribute('data-accessible-text') === 'ready';
    }, null, { timeout: 15000 }).catch(() => {});
    const accessible = await page.evaluate(() => {
      const code = document.querySelector('.editor-content .code-view');
      const node = code.querySelector('.virtual-text-accessible');
      return { ready: code.getAttribute('data-accessible-text') || null, length: node.textContent.length, accessibleAt: code.__augitVirtual ? code.__augitVirtual.accessibleLaidOutAt : null, mountedAt: code.__augitVirtual ? code.__augitVirtual.mountedAt : null };
    });
    await delay(500);
    const uia = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.resolve('tools/audit/large-text-uia.ps1'), '-ProcessId', String(pid), '-Out', path.join(output, 'uia.json')], { timeout: 120000 });
    const uiaJson = JSON.parse(await fs.readFile(path.join(output, 'uia.json'), 'utf8'));
    report.runs.push({ virtual, accessible, accessibleWaitMs: Date.now() - a11yStarted, uia: uiaJson });
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
    await fs.writeFile(path.join(output, 'large-text-uia.json'), JSON.stringify(report, null, 2) + '\n');
    await fs.rm(temporary, { recursive: true, force: true }).catch(() => {});
  }
  console.log(JSON.stringify(report.runs[0] || { error: report.error || null }, null, 2));
})();
