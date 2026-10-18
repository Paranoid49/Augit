// 正常入口回归：从 Git Log 提交右键“New Branch…”打开空名称窗口时必须有英文校验。
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const net = require('node:net');
const assert = require('node:assert/strict');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
(async () => {
  const { chromium } = require(process.argv[2]);
  const output = path.resolve(process.argv[3] || 'artifacts/branch-create-empty-validation');
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-branch-validation-'));
  const settings = path.join(temporary, 'settings.json');
  await fs.copyFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), settings);
  const listener = net.createServer();
  await new Promise((resolve) => listener.listen(0, '127.0.0.1', resolve));
  const port = listener.address().port;
  await new Promise((resolve) => listener.close(resolve));
  let pid = 0, browser = null;
  const report = { launcher: 'tools/start-augit.ps1', forcedBuild: true, cdpAttached: true };
  try {
    const launch = require('./daily-launch-command.cjs')(process.cwd(), settings, port, 'D:/JetBrains/PyCharm-2026.2.1-reference/fixture');
    const started = await execute(launch.file, launch.args, { env: launch.env, timeout: 180000 });
    pid = Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
    assert.ok(pid > 0, '日常启动器未返回 PID');
    report.pid = pid;
    report.port = port;
    for (let i = 0; i < 160; i += 1) {
      try { browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`); break; }
      catch { await delay(250); }
    }
    const page = browser.contexts()[0].pages()[0];
    await page.waitForFunction(() => window.__augitReady && window.__augitGitReady);
    await page.locator('.rail-button[data-tool="commit"], .rail-button[aria-label="Git Log"]').first().click();
    await page.waitForSelector('.commit-row', { timeout: 30000 });
    await page.locator('.commit-row').first().click({ button: 'right' });
    await page.waitForSelector('.log-context [data-log-action="new-branch"]', { state: 'attached' });
    await page.locator('.log-context [data-log-action="new-branch"]').evaluate((node) => node.click());
    await page.waitForSelector('[data-compact-dialog]');
    await page.locator('[data-compact-action="confirm"]').click();
    await page.waitForFunction(() => document.querySelector('[data-compact-dialog] .footer-help')?.textContent === 'Name cannot be empty.');
    const state = await page.evaluate(() => ({
      visible: !!document.querySelector('[data-compact-dialog]'),
      help: document.querySelector('[data-compact-dialog] .footer-help')?.textContent || '',
      error: window.__augitCheckoutError,
      pageErrors: window.__augitErrors
    }));
    assert.equal(/[\u4e00-\u9fff]/.test(state.help), false, '校验提示不得含中文');
    assert.equal(state.error, 'Name cannot be empty.');
    assert.deepEqual(state.pageErrors, []);
    await page.locator('[data-compact-field]').fill('bad name');
    await page.locator('[data-compact-action="confirm"]').click();
    await page.waitForFunction(() => document.querySelector('[data-compact-dialog] .footer-help')?.textContent === 'Name cannot contain whitespace.');
    const whitespace = await page.evaluate(() => ({
      help: document.querySelector('[data-compact-dialog] .footer-help')?.textContent || '',
      error: window.__augitCheckoutError,
      pageErrors: window.__augitErrors
    }));
    assert.equal(/[\u4e00-\u9fff]/.test(whitespace.help), false, '空白校验提示不得含中文');
    assert.equal(whitespace.error, 'Name cannot contain whitespace.');
    assert.deepEqual(whitespace.pageErrors, []);
    await page.keyboard.press('Escape');
    await page.waitForFunction(() => !document.querySelector('[data-compact-dialog]'));
    await page.locator('.commit-row').first().click({ button: 'right' });
    await page.waitForSelector('.log-context [data-log-action="new-tag"]', { state: 'attached' });
    await page.locator('.log-context [data-log-action="new-tag"]').evaluate((node) => node.click());
    await page.waitForSelector('[data-compact-dialog]');
    await page.locator('[data-compact-action="confirm"]').click();
    await page.waitForFunction(() => document.querySelector('[data-compact-dialog] .footer-help')?.textContent === 'Name cannot be empty.');
    const tag = await page.evaluate(() => ({
      title: document.querySelector('[data-compact-dialog]')?.getAttribute('aria-label') || '',
      help: document.querySelector('[data-compact-dialog] .footer-help')?.textContent || '',
      error: window.__augitCheckoutError,
      pageErrors: window.__augitErrors
    }));
    assert.match(tag.title, /^Create New Tag On /);
    assert.equal(/[\u4e00-\u9fff]/.test(tag.title + tag.help), false, '标签校验不得含中文');
    assert.equal(tag.error, 'Name cannot be empty.');
    assert.deepEqual(tag.pageErrors, []);
    report.state = state;
    report.whitespace = whitespace;
    report.tag = tag;
    await fs.writeFile(path.join(output, 'result.json'), JSON.stringify(report, null, 2) + '\n');
    console.log(JSON.stringify({ pid, port, ...state, whitespace, tag }));
    await page.keyboard.press('Escape');
    await page.waitForFunction(() => !document.querySelector('[data-compact-dialog]'));
  } finally {
    try { if (browser) await browser.close(); } catch {}
    if (pid) {
      try {
        await execute('powershell.exe', ['-NoProfile', '-Command', `$p=Get-Process -Id ${pid} -ErrorAction Stop; if(-not $p.CloseMainWindow()){throw 'close'}; if(-not $p.WaitForExit(8000)){throw 'exit'}`], { windowsHide: true, timeout: 20000 });
        report.closeMethod = 'CloseMainWindow';
      } catch {
        try { await execute('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true }); report.closeMethod = 'taskkill'; } catch {}
      }
    }
    await fs.rm(temporary, { recursive: true, force: true });
    report.temporaryRemoved = true;
    await fs.writeFile(path.join(output, 'cleanup.json'), JSON.stringify(report, null, 2) + '\n');
  }
})().catch((error) => { console.error(error); process.exitCode = 1; });
