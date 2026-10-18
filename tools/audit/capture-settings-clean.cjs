// 实际 Settings 阴影：其他窗口已由外层 PowerShell 隐藏，这里只走正常入口并截图。
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const net = require('node:net');
const assert = require('node:assert/strict');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/settings-shadow-clean');
const fixture = 'D:/JetBrains/PyCharm-2026.2.1-reference/fixture';
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-settings-clean-'));
  const settings = path.join(temporary, 'settings.json');
  await fs.copyFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), settings);
  let pid = 0;
  let browser = null;
  try {
    const server = net.createServer();
    await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
    const port = server.address().port;
    await new Promise((resolve) => server.close(resolve));
    const launch = require('./daily-launch-command.cjs')(process.cwd(), settings, port, fixture);
    const started = await execute(launch.file, launch.args, { env: launch.env, timeout: 180000 });
    pid = Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
    assert.ok(Number.isSafeInteger(pid) && pid > 0, '日常启动器未返回 PID');
    for (let attempt = 0; attempt < 160; attempt += 1) {
      try { browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`); break; }
      catch { await delay(250); }
    }
    assert.ok(browser, '无法连接正常入口 CDP');
    const page = browser.contexts()[0].pages()[0];
    await page.waitForFunction(() => window.__augitReady === true, null, { timeout: 40000 });
    await page.waitForFunction(() => window.__augitGitReady === true, null, { timeout: 40000 });
    await delay(800);
    await page.locator('[aria-label="Settings"]').first().click();
    await delay(1200);
    const result = await execute('powershell.exe', ['-NoProfile', '-File', 'tools/audit/capture-settings-clean.ps1',
      '-ProcessId', String(pid), '-Output', output], { windowsHide: true, timeout: 120000 });
    await fs.writeFile(path.join(output, 'capture.json'), JSON.stringify({
      launcher: 'tools/start-augit.ps1', forcedBuild: true, cdpAttached: true, pid, port, workspace: fixture,
      output: result.stdout,
    }, null, 2) + '\n');
    console.log('SETTINGS_CLEAN_OK pid=' + pid);
  } catch (error) {
    console.error(String(error && error.stack || error));
    process.exitCode = 1;
  } finally {
    try { if (browser) await browser.close(); } catch { /* 继续清理 */ }
    let closed = false;
    if (pid) {
      try {
        await execute('powershell.exe', ['-NoProfile', '-Command',
          '$p=Get-Process -Id ' + pid + ' -ErrorAction Stop; if(-not $p.CloseMainWindow()){throw "no window"}; if(-not $p.WaitForExit(8000)){throw "still running"}'],
        { windowsHide: true, timeout: 20000 });
        closed = true;
      } catch {
        try { await execute('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true, timeout: 20000 }); } catch { /* 已退出 */ }
      }
    }
    try { await fs.rm(temporary, { recursive: true, force: true }); } catch { /* 保留证据 */ }
    await fs.writeFile(path.join(output, 'cleanup.json'), JSON.stringify({ pid, closeMethod: closed ? 'CloseMainWindow' : 'taskkill', temporaryRemoved: true }, null, 2) + '\n').catch(() => {});
  }
})();
