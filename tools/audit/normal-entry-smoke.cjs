// 正常入口冒烟：真实仓库下的文件阅读、内置终端、Changes→Diff 与页面错误。
// 不启动测试服务器，不调用场景参数；实例与临时仓库在 finally 中清理。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/normal-entry-smoke');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function git(cwd, ...args) {
  return execute('git', ['-C', cwd, ...args], { windowsHide: true, timeout: 30000 });
}

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-normal-smoke-'));
  const workspace = path.join(temporary, 'repo');
  const settings = path.join(temporary, 'settings.json');
  const listener = net.createServer();
  let port = 0;
  let shell = null;
  let browser = null;
  const report = { launcher: 'tools/start-augit.ps1', forcedBuild: true, cdpAttached: true, checks: [] };
  try {
    await fs.mkdir(workspace);
    await git(workspace, 'init', '-b', 'main');
    await git(workspace, 'config', 'user.name', 'Augit Smoke');
    await git(workspace, 'config', 'user.email', 'augit-smoke@example.invalid');
    await fs.writeFile(path.join(workspace, 'plain.txt'), 'SMOKE_PLAIN line 1\nSMOKE_PLAIN line 2\n');
    await fs.writeFile(path.join(workspace, 'changed.txt'), 'before\n');
    await git(workspace, 'add', '.');
    await git(workspace, 'commit', '-m', 'test: smoke fixture');
    await fs.writeFile(path.join(workspace, 'changed.txt'), 'after external change\n');

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
    await fs.writeFile(path.join(output, 'launch.json'), JSON.stringify({
      launcher: 'tools/start-augit.ps1', workspace, forcedBuild: true, port,
      copiedSettings: true, command: launch.args, output: started.stdout,
    }, null, 2));

    for (let index = 0; index < 160; index += 1) {
      try { browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`); break; }
      catch { await delay(250); }
    }
    assert.ok(browser, '无法连接正常入口 WebView2 调试端口');
    const page = browser.contexts()[0].pages()[0];
    const pageErrors = [];
    page.on('pageerror', (error) => pageErrors.push('pageerror:' + String(error)));
    page.on('console', (message) => {
      if (message.type() === 'error') {
        const location = message.location();
        pageErrors.push('console:' + message.text() + (location && location.url ? ' @' + location.url : ''));
      }
    });
    page.on('requestfailed', (request) => pageErrors.push('requestfailed:' + request.url() + ' ' + (request.failure()?.errorText || '')));

    await page.waitForFunction(() => window.__augitReady === true, null, { timeout: 40000 });
    await page.waitForFunction(() => window.__augitGitReady === true, null, { timeout: 40000 });
    const mapped = await execute(process.env.AUGIT_AUDIT_PYTHON || 'D:/Python/Python3119/python.exe',
      ['-B', 'tools/audit/read-process-mapped-images.py', String(pid)],
      { windowsHide: true, timeout: 15000, env: { ...process.env, PYTHONUTF8: '1' } });
    const mappedIdentity = JSON.parse(mapped.stdout);
    assert.equal(mappedIdentity.pid, pid);
    for (const name of ['Augit.dll', 'Augit.Infrastructure.dll']) {
      assert.ok(mappedIdentity.mappedImages.some((item) => item.name === name), '未找到已加载程序集：' + name);
    }
    await fs.writeFile(path.join(output, 'mapped-images.json'), JSON.stringify(mappedIdentity, null, 2));

    // ① 普通文件只读打开
    await page.locator('[data-tree-path="plain.txt"]').dblclick();
    await page.waitForFunction(() => document.querySelector('.code-view')?.textContent.includes('SMOKE_PLAIN line 2'), null, { timeout: 20000 });
    const fileState = await page.evaluate(() => ({
      readOnly: document.querySelectorAll('.code-view [contenteditable], .code-view[contenteditable]').length === 0,
      font: getComputedStyle(document.querySelector('.code-view')).fontFamily,
      size: getComputedStyle(document.querySelector('.code-view')).fontSize,
    }));
    assert.equal(fileState.readOnly, true, '普通文件必须是只读');
    assert.match(fileState.font, /JetBrains Mono/, '正文必须使用随包 JetBrains Mono');
    report.checks.push({ name: 'plain-file', fileState });

    // ② 内置终端：真实输入与输出，隐藏后恢复
    await page.locator('.rail-button[data-tool="terminal"]').click();
    await page.waitForSelector('.xterm', { timeout: 30000 });
    await page.locator('.xterm').click();
    await delay(1500);
    await page.keyboard.type('echo AUGIT_TERM_OK');
    await page.keyboard.press('Enter');
    await page.waitForFunction(() => document.querySelector('.xterm')?.textContent.includes('AUGIT_TERM_OK'), null, { timeout: 20000 });
    await page.locator('.rail-button[data-tool="history"]').click();
    await page.waitForFunction(() => {
      const terminal = document.querySelector('.xterm');
      return !terminal || terminal.getBoundingClientRect().height === 0 || !terminal.offsetParent;
    }, null, { timeout: 10000 });
    await page.locator('.rail-button[data-tool="terminal"]').click();
    await page.waitForSelector('.xterm', { timeout: 20000 });
    const terminalState = await page.evaluate(() => ({
      hasOutput: document.querySelector('.xterm')?.textContent.includes('AUGIT_TERM_OK') === true,
      visible: document.querySelector('.xterm')?.offsetParent !== null,
    }));
    assert.equal(terminalState.hasOutput, true, '隐藏后恢复必须保留终端输出');
    report.checks.push({ name: 'terminal', terminalState });

    // ③ Changes 单击不改编辑区，Enter 打开只读 Diff
    await page.locator('.rail-button[data-tool="commit"]').click();
    await page.waitForSelector('.changes-list [data-path="changed.txt"]', { timeout: 15000 });
    await page.locator('.changes-list [data-path="changed.txt"]').click();
    await delay(400);
    const afterClick = await page.evaluate(() => ({
      codeView: !!document.querySelector('.code-view'),
      diff: !!document.querySelector('.diff-layout'),
    }));
    assert.equal(afterClick.codeView, true, '单击 Changes 不应替换当前文件');
    assert.equal(afterClick.diff, false, '单击 Changes 不应直接打开 Diff');
    await page.locator('.changes-list [data-path="changed.txt"]').press('Enter');
    await page.waitForSelector('.diff-layout', { timeout: 20000 });
    // .diff-layout 先出现、正文行随后异步渲染；只等容器会把普通加载态误判成空 Diff。
    await page.waitForFunction(() => document.querySelectorAll('.diff-code-line').length > 0, null, { timeout: 20000 });
    const diffState = await page.evaluate(() => ({
      lines: document.querySelectorAll('.diff-code-line').length,
      editable: document.querySelectorAll('.diff-layout [contenteditable]').length,
    }));
    assert.ok(diffState.lines > 0, 'Enter 应打开真实只读 Diff');
    assert.equal(diffState.editable, 0, 'Diff 不得可编辑');
    report.checks.push({ name: 'changes-diff', afterClick, diffState });

    const runtimeErrors = await page.evaluate(() => ({ error: window.__augitError || null, errors: window.__augitErrors || [] }));
    assert.equal(runtimeErrors.error, null, '正常入口不应记录未处理错误');
    assert.deepEqual(runtimeErrors.errors, [], '正常入口不应有页面内部错误');
    assert.deepEqual(pageErrors, [], '正常入口不应有页面异常：' + pageErrors.join(' | '));
    report.checks.push({ name: 'no-runtime-errors', runtimeErrors, pageErrors });
    await fs.writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2));
    console.log('NORMAL_SMOKE_OK pid=' + pid + ' port=' + port);
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
          '$p=Get-Process -Id ' + shell + ' -ErrorAction Stop; if (-not $p.CloseMainWindow()) { throw "CloseMainWindow=false" }; if (-not $p.WaitForExit(8000)) { throw "still running" }'],
          { windowsHide: true, timeout: 20000 });
        report.closeMethod = 'CloseMainWindow';
      } catch {
        report.closeMethod = 'taskkill';
        try { await execute('taskkill', ['/PID', String(shell), '/T', '/F'], { windowsHide: true, timeout: 20000 }); }
        catch { /* 已退出 */ }
      }
    }
    try {
      if (port) {
        let listening = true;
        for (let attempt = 0; attempt < 40 && listening; attempt += 1) {
          listening = await new Promise((resolve) => {
            const socket = net.connect({ port, host: '127.0.0.1' });
            const done = (value) => { socket.destroy(); resolve(value); };
            socket.once('connect', () => done(true));
            socket.once('error', () => done(false));
            socket.setTimeout(800, () => done(false));
          });
          if (listening) await delay(250);
        }
        report.portClosed = !listening;
      }
    } catch { report.portClosed = false; }
    try { await fs.rm(temporary, { recursive: true, force: true }); report.temporaryRemoved = true; }
    catch { report.temporaryRemoved = false; }
    await fs.writeFile(path.join(output, 'cleanup.json'), JSON.stringify(report, null, 2)).catch(() => {});
  }
})();
