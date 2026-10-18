// 只读探针：比较设置打开前后 CDP 暴露的 WebView2 进程，定位设置页 renderer。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

(async () => {
  const { chromium } = require(process.argv[2]);
  const output = path.resolve(process.argv[3] || 'artifacts/settings-process-probe');
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-settings-process-'));
  const settings = path.join(temporary, 'settings.json');
  const listener = net.createServer();
  let port = 0;
  let pid = 0;
  let browser = null;
  try {
    await fs.copyFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), settings).catch(async () => {
      await fs.writeFile(settings, '{}\n');
    });
    await new Promise((resolve) => listener.listen(0, '127.0.0.1', resolve));
    port = listener.address().port;
    await new Promise((resolve) => listener.close(resolve));
    const launch = require('./daily-launch-command.cjs')(process.cwd(), settings, port);
    const started = await execute(launch.file, launch.args, { env: launch.env, timeout: 180000 });
    pid = Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
    for (let index = 0; index < 160; index += 1) {
      try { browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`); break; }
      catch { await delay(250); }
    }
    const main = browser.contexts()[0].pages()[0];
    main.setDefaultTimeout(30000);
    await main.waitForFunction(() => window.__augitReady && window.__augitSettingsReady);
    const browserSession = await browser.newBrowserCDPSession();
    const before = await browserSession.send('SystemInfo.getProcessInfo');
    await main.locator('.titlebar [aria-label="Settings"]').click();
    let settingsPage = null;
    for (let index = 0; index < 200 && !settingsPage; index += 1) {
      settingsPage = browser.contexts().flatMap((context) => context.pages())
        .find((page) => page.url().includes('surface=settings')) || null;
      if (!settingsPage) await delay(25);
    }
    if (!settingsPage) throw new Error('settings target not found');
    await settingsPage.waitForFunction(() => window.__augitReady, null, { timeout: 15000 });
    const after = await browserSession.send('SystemInfo.getProcessInfo');
    const report = {
      pid, port, before, after,
      beforeIds: before.processInfo.map((item) => item.id),
      afterIds: after.processInfo.map((item) => item.id),
      newProcesses: after.processInfo.filter((item) => !before.processInfo.some((old) => old.id === item.id)),
      limitation: 'CDP SystemInfo 仅暴露本浏览器进程信息；新 renderer 是否为设置页仍需结合页面 target 与时序判断。'
    };
    await fs.writeFile(path.join(output, 'processes.json'), JSON.stringify(report, null, 2) + '\n');
    console.log(JSON.stringify({ pid, before: report.beforeIds, after: report.afterIds, newProcesses: report.newProcesses }));
    await settingsPage.locator('[data-settings-action="cancel"]').click();
    await main.waitForFunction(() => !window.__augitNativeModalActive);
  } finally {
    try { if (browser) await browser.close(); } catch { /* 继续清理 */ }
    if (pid) {
      try {
        await execute('powershell.exe', ['-NoProfile', '-Command',
          '$p=Get-Process -Id ' + pid + ' -ErrorAction Stop; if (-not $p.CloseMainWindow()) { throw "CloseMainWindow=false" }; if (-not $p.WaitForExit(8000)) { throw "still running" }'],
          { windowsHide: true, timeout: 20000 });
      } catch {
        try { await execute('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true, timeout: 20000 }); } catch { /* 已退出 */ }
      }
    }
    await fs.rm(temporary, { recursive: true, force: true });
    await fs.writeFile(path.join(output, 'cleanup.json'), JSON.stringify({ pid, temporaryRemoved: true, port }, null, 2) + '\n');
  }
})().catch((error) => { console.error(error); process.exitCode = 1; });
