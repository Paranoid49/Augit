// 实际端正常入口采集：真实拖动底部分隔条把面板增高，使终端面板原点与参考对齐后再截图。
// 只改临时设置副本；参考端不重启、不改动历史目录。
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const net = require('node:net');
const assert = require('node:assert/strict');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/terminal-matched-panel');
const panelUp = Number(process.argv.find((value) => value.startsWith('--panel-up='))?.split('=')[1] || 23);
const sideWidth = Number(process.argv.find((value) => value.startsWith('--side-width='))?.split('=')[1] || 0);
const noTerminal = process.argv.includes('--no-terminal');
const maximize = process.argv.includes('--maximize');
const hideBottom = process.argv.includes('--hide-bottom');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

(async () => {
  await fs.mkdir(output, { recursive: true });
  const original = JSON.parse(await fs.readFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), 'utf8'));
  const settingsObject = require('./terminal-capture-options.cjs').dailyTerminalSettings(original, process.argv.concat('--startup-rows'));
  const fixture = 'D:/JetBrains/PyCharm-2026.2.1-reference/fixture';
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-terminal-panel-'));
  let pid = 0;
  let browser = null;
  const report = { launcher: 'tools/start-augit.ps1', forcedBuild: true, cdpAttached: true, workspace: fixture, panelUp };
  let dragged = 0;
  try {
    const settings = path.join(temporary, 'settings.json');
    await fs.writeFile(settings, JSON.stringify(settingsObject));
    const server = net.createServer();
    await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
    const port = server.address().port;
    await new Promise((resolve) => server.close(resolve));
    const launch = require('./daily-launch-command.cjs')(process.cwd(), settings, port, fixture);
    const started = await execute(launch.file, launch.args, { env: launch.env, timeout: 180000 });
    pid = Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
    assert.ok(Number.isSafeInteger(pid) && pid > 0, '日常启动器未返回 PID');
    report.pid = pid;
    report.port = port;
    await fs.writeFile(path.join(output, 'launch.json'), JSON.stringify({
      launcher: 'tools/start-augit.ps1', workspace: fixture, forcedBuild: true, port,
      command: launch.args, output: started.stdout,
    }, null, 2));

    for (let index = 0; index < 160; index += 1) {
      try { browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`); break; }
      catch { await delay(250); }
    }
    assert.ok(browser, '无法连接正常入口 CDP');
    const page = browser.contexts()[0].pages()[0];
    const pageErrors = [];
    page.on('pageerror', (error) => pageErrors.push('pageerror:' + String(error)));
    page.on('console', (message) => { if (message.type() === 'error') pageErrors.push('console:' + message.text()); });
    await page.waitForFunction(() => window.__augitReady === true, null, { timeout: 40000 });
    await page.waitForFunction(() => window.__augitGitReady === true, null, { timeout: 40000 });
    await page.waitForFunction(() => !window.__augitLive?.sessionRestoring, null, { timeout: 40000 });

    // 打开 notes.txt，给终端失焦后点击编辑区，与参考端的编辑区焦点保持一致。
    const treeNotes = page.locator('[data-tree-path="notes.txt"]');
    if (await treeNotes.count()) {
      await treeNotes.first().dblclick();
      await page.waitForSelector('.code-view', { timeout: 15000 });
      await page.waitForFunction(() => (document.querySelector('.code-view')?.textContent || '').includes('First line'), null, { timeout: 15000 });
    }
    if (maximize) {
      const button = page.locator('[data-window-action="maximize"]');
      if (await button.count()) {
        await button.first().click();
        await delay(700);
      }
    }
    if (hideBottom) {
      const historyRail = page.locator('.rail-button[data-tool="history"]');
      if (await historyRail.count()) {
        await historyRail.first().click();
        await delay(300);
        await historyRail.first().click();
        await delay(500);
      }
      let bottom = null;
      try { bottom = await page.locator('.bottom-tool').boundingBox({ timeout: 1500 }); } catch { /* 已隐藏 */ }
      report.bottomHidden = !bottom;
    }
    if (sideWidth > 0) {
      const side = await page.locator('.side-tool').boundingBox();
      assert.ok(side, '找不到项目侧栏');
      await page.mouse.move(side.x + side.width + 2, side.y + side.height / 2);
      await page.mouse.down();
      for (let step = 1; step <= 16; step += 1) {
        const x = side.x + side.width + 2 + (sideWidth - side.width) * step / 16;
        await page.mouse.move(x, side.y + side.height / 2, { steps: 1 });
        await delay(20);
      }
      await page.mouse.up();
      await delay(400);
      const resized = await page.locator('.side-tool').boundingBox();
      report.sideBeforeCss = Math.round(side.width * 10) / 10;
      report.sideAfterCss = resized ? Math.round(resized.width * 10) / 10 : null;
      assert.ok(resized && Math.abs(resized.width - sideWidth) <= 3, `项目侧栏拖动未达目标：期望 ${sideWidth}，实际 ${resized?.width}`);
    }
    if (!noTerminal) {
      await page.locator('.rail-button[aria-label="Terminal"], .rail-button[data-tool="terminal"]').first().click();
      await page.waitForSelector('.xterm', { timeout: 20000 });
      await page.waitForFunction(() => (document.querySelector('.xterm')?.textContent || '').split('Ag 0123456789').length >= 9, null, { timeout: 30000 });
      await delay(700);

      const before = await page.locator('.bottom-tool').boundingBox();
      assert.ok(before, '找不到底部分隔条面板');
      await page.mouse.move(before.x + before.width / 2, before.y - 2);
      await page.mouse.down();
      for (let step = 1; step <= 12; step += 1) {
        await page.mouse.move(before.x + before.width / 2, before.y - 2 - panelUp * step / 12, { steps: 1 });
        await delay(25);
      }
      await page.mouse.up();
      await delay(500);
      const after = await page.locator('.bottom-tool').boundingBox();
      dragged = after ? Math.round(after.height - before.height) : 0;
      report.panelBeforeCss = Math.round(before.height * 10) / 10;
      report.panelAfterCss = after ? Math.round(after.height * 10) / 10 : null;
      report.panelDeltaCss = dragged;
      assert.ok(dragged >= panelUp - 2, `真实拖动未把面板增高：期望约 ${panelUp} CSS，实际 ${dragged}`);
    }

    // 点击编辑区让终端失焦；再把指针移到标题栏，避免终端内鼠标光晕。
    const editor = page.locator('.code-view').first();
    if (await editor.count()) await editor.click({ position: { x: 300, y: 120 } });
    await page.locator('.titlebar').hover();
    await delay(800);

    const geometry = await page.evaluate(async () => {
      const screen = document.querySelector('.xterm-screen')?.getBoundingClientRect();
      const { invoke } = await import('/src/bridge.js');
      const state = await invoke('window/modal-state');
      const bottom = document.querySelector('.bottom-tool')?.getBoundingClientRect();
      return {
        window: typeof state.owner === 'number' ? state.owner : state.owner?.handle,
        dpr: devicePixelRatio,
        terminal: screen ? { x: screen.x, y: screen.y, width: screen.width, height: screen.height } : null,
        bottom: bottom ? { x: bottom.x, y: bottom.y, width: bottom.width, height: bottom.height } : null,
        tabs: Array.from(document.querySelectorAll('.bottom-header .tool-tab')).map((tab) => tab.textContent.trim()),
        status: document.querySelector('.status-bar')?.innerText?.trim() ?? null,
        errors: Array.isArray(window.__augitErrors) ? window.__augitErrors : [],
      };
    });
    assert.deepEqual(geometry.errors, []);
    assert.deepEqual(pageErrors, []);
    assert.ok(Number.isInteger(geometry.window), '未取得主窗 HWND');
    report.geometry = geometry;

    if (noTerminal) {
      await page.screenshot({ path: path.join(output, 'actual-panel.png'), scale: 'device' });
      report.capture = 'cdp-page-screenshot';
    } else {
      const capture = await execute('pwsh', ['-NoProfile', '-File', 'tools/audit/capture-screen-region.ps1',
        '-WindowHandle', String(geometry.window), '-ExpectedProcessId', String(pid), '-Padding', '0',
        '-Output', path.join(output, 'actual-panel.png')], { windowsHide: true, timeout: 30000 });
      report.capture = capture.stdout.trim();
    }
    await fs.writeFile(path.join(output, 'geometry.json'), JSON.stringify(report, null, 2));
    console.log('TERMINAL_PANEL_OK pid=' + pid + ' delta=' + dragged + ' mode=' + (noTerminal ? 'page' : 'screen'));
  } catch (error) {
    report.error = String(error && error.stack || error);
    await fs.writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2)).catch(() => {});
    console.error(report.error);
    process.exitCode = 1;
  } finally {
    try { if (browser) await browser.close(); } catch { /* 继续清理 */ }
    let closed = false;
    if (pid) {
      try {
        await execute('powershell.exe', ['-NoProfile', '-Command',
          '$p=Get-Process -Id ' + pid + ' -ErrorAction Stop; ' +
          'if (-not $p.CloseMainWindow()) { throw "CloseMainWindow=false" }; ' +
          'if (-not $p.WaitForExit(8000)) { throw "still running" }'], { windowsHide: true, timeout: 20000 });
        closed = true;
      } catch {
        try { await execute('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true, timeout: 20000 }); }
        catch { /* 已退出 */ }
      }
    }
    report.closeMethod = closed ? 'CloseMainWindow' : 'taskkill';
    try { await fs.rm(temporary, { recursive: true, force: true }); report.temporaryRemoved = true; }
    catch { report.temporaryRemoved = false; }
    await fs.writeFile(path.join(output, 'cleanup.json'), JSON.stringify(report, null, 2)).catch(() => {});
  }
})();
