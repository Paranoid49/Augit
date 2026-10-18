// 诊断：正常入口读取 settings.projectPanelWidth 是否应用到 .side-tool。
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const width = Number(process.argv[3] || 519);
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
(async () => {
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-side-probe-'));
  const settings = path.join(temp, 'settings.json');
  const value = JSON.parse(await fs.readFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), 'utf8'));
  value.projectPanelWidth = width;
  await fs.writeFile(settings, JSON.stringify(value));
  let pid = 0;
  let browser = null;
  try {
    const server = net.createServer();
    await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
    const port = server.address().port;
    await new Promise((resolve) => server.close(resolve));
    const launch = require('./daily-launch-command.cjs')(process.cwd(), settings, port, 'D:/JetBrains/PyCharm-2026.2.1-reference/fixture');
    const started = await execute(launch.file, launch.args, { env: launch.env, timeout: 180000 });
    pid = Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
    for (let i = 0; i < 160; i++) { try { browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`); break; } catch { await delay(250); } }
    const page = browser.contexts()[0].pages()[0];
    await page.waitForFunction(() => window.__augitReady === true, null, { timeout: 40000 });
    await page.waitForFunction(() => !window.__augitLive?.sessionRestoring, null, { timeout: 40000 });
    const result = await page.evaluate(() => ({
      side: document.querySelector('.side-tool')?.getBoundingClientRect().width,
      setting: window.__augitLive?.settings?.projectPanelWidth,
      style: getComputedStyle(document.documentElement).getPropertyValue('--augit-side-width'),
    }));
    console.log(JSON.stringify({ pid, requested: width, ...result }));
  } finally {
    try { if (browser) await browser.close(); } catch { /* 继续 */ }
    if (pid) {
      try {
        await execute('powershell.exe', ['-NoProfile', '-Command', '$p=Get-Process -Id ' + pid + ' -ErrorAction Stop; if(-not $p.CloseMainWindow()){throw "no"}; $p.WaitForExit(8000)|Out-Null'], { windowsHide: true, timeout: 20000 });
      } catch { try { await execute('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true, timeout: 20000 }); } catch { /* 已退出 */ } }
    }
    await fs.rm(temp, { recursive: true, force: true });
  }
})();
