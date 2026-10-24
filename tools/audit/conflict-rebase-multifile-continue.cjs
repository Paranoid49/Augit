// 正常入口验收：rebase 两个文件冲突时外部混合解决，全部解决后由应用内 Continue Rebase 完成。
// 仅操作临时仓库；不修改用户配置，不执行自动解决或内置编辑。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/conflict-rebase-multifile-continue');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function git(cwd, ...args) {
  return execute('git', ['-C', cwd, ...args], { windowsHide: true, timeout: 30000 });
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
    await page.locator('.dialog.conflict-session-dialog [data-operation-action="continue"]').click();
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
