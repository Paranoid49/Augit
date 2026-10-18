// 正常入口界面英文审计：英文夹具下扫描可见文本和 title，排除正文/终端原始输出。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const cjk = /[\u3400-\u9fff]/;

async function git(cwd, ...args) {
  return execute('git', ['-C', cwd, ...args], { windowsHide: true, timeout: 30000 });
}

async function scan(page, label, ignoredSelectors = []) {
  return page.evaluate(({ label, ignoredSelectors }) => {
    const ignored = ignoredSelectors.join(',');
    const result = [];
    const seen = new Set();
    const consider = (node, value, kind) => {
      const text = String(value || '').replace(/\s+/g, ' ').trim();
      if (!text || !/[\u3400-\u9fff]/.test(text) || seen.has(kind + '|' + text)) return;
      if (ignored && node.closest && node.closest(ignored)) return;
      seen.add(kind + '|' + text);
      result.push({
        kind,
        tag: node.tagName ? node.tagName.toLowerCase() : '',
        className: typeof node.className === 'string' ? node.className : '',
        text: text.slice(0, 300)
      });
    };
    for (const node of document.querySelectorAll('body *')) {
      if (!(node instanceof HTMLElement) || !node.offsetParent) continue;
      const own = [...node.childNodes].filter((child) => child.nodeType === Node.TEXT_NODE).map((child) => child.textContent).join(' ');
      consider(node, own, 'text');
      const title = node.getAttribute('title');
      if (title) consider(node, title, 'title');
      const aria = node.getAttribute('aria-label');
      if (aria) consider(node, aria, 'aria-label');
    }
    return { label, url: location.href, findings: result };
  }, { label, ignoredSelectors });
}

(async () => {
  const { chromium } = require(process.argv[2]);
  const output = path.resolve(process.argv[3] || 'artifacts/ui-english-audit');
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-ui-english-'));
  const workspace = path.join(temporary, 'repo');
  const settings = path.join(temporary, 'settings.json');
  const listener = net.createServer();
  let port = 0;
  let pid = 0;
  let browser = null;
  const report = { launcher: 'tools/start-augit.ps1', forcedBuild: true, cdpAttached: true, scans: [] };
  try {
    await fs.mkdir(workspace);
    await git(workspace, 'init', '-b', 'main');
    await git(workspace, 'config', 'user.name', 'Augit English Audit');
    await git(workspace, 'config', 'user.email', 'augit-english@example.invalid');
    await fs.writeFile(path.join(workspace, 'plain.txt'), 'English line one\nEnglish line two\n');
    await fs.writeFile(path.join(workspace, 'changed.txt'), 'before\n');
    await git(workspace, 'add', '.');
    await git(workspace, 'commit', '-m', 'test: english ui fixture');
    await fs.writeFile(path.join(workspace, 'changed.txt'), 'after\n');
    await fs.copyFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), settings).catch(async () => {
      await fs.writeFile(settings, '{}\n');
    });
    const requestedTheme = process.argv.includes('--dark') ? 'Dark' : 'Light';
    const settingsValue = JSON.parse(await fs.readFile(settings, 'utf8'));
    settingsValue.theme = requestedTheme;
    await fs.writeFile(settings, JSON.stringify(settingsValue));
    report.theme = requestedTheme;
    await new Promise((resolve) => listener.listen(0, '127.0.0.1', resolve));
    port = listener.address().port;
    await new Promise((resolve) => listener.close(resolve));
    const launch = require('./daily-launch-command.cjs')(process.cwd(), settings, port, workspace);
    const started = await execute(launch.file, launch.args, { env: launch.env, timeout: 180000 });
    pid = Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
    assert.ok(Number.isSafeInteger(pid) && pid > 0, '日常启动器未返回实际 PID');
    report.pid = pid;
    report.port = port;
    for (let index = 0; index < 160; index += 1) {
      try { browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`); break; }
      catch { await delay(250); }
    }
    assert.ok(browser, '无法连接正常入口 CDP');
    const page = browser.contexts()[0].pages()[0];
    page.setDefaultTimeout(30000);
    const errors = [];
    page.on('pageerror', (error) => errors.push(String(error)));
    page.on('console', (message) => { if (message.type() === 'error') errors.push(message.text()); });
    await page.waitForFunction(() => window.__augitReady && window.__augitGitReady && !window.__augitLive.sessionRestoring);

    report.scans.push(await scan(page, 'main'));
    await page.locator('[data-tree-path="plain.txt"]').dblclick();
    await page.waitForFunction(() => document.querySelector('.code-view')?.textContent.includes('English line two'));
    report.scans.push(await scan(page, 'text-viewer', ['.code-view', '.xterm']));

    await page.locator('.rail-button[data-tool="commit"]').click();
    await page.waitForSelector('.changes-list [data-path="changed.txt"]');
    report.scans.push(await scan(page, 'changes', ['.code-view', '.xterm']));
    await page.locator('.changes-list [data-path="changed.txt"]').press('Enter');
    await page.waitForFunction(() => document.querySelectorAll('.diff-code-line').length > 0);
    report.scans.push(await scan(page, 'diff', ['.code-view', '.xterm', '.diff-code-line']));

    await page.locator('.rail-button[data-tool="terminal"]').click();
    await page.waitForSelector('.xterm');
    await delay(1000);
    report.scans.push(await scan(page, 'terminal', ['.xterm']));

    await page.locator('.titlebar [aria-label="Settings"]').click();
    let settingsPage = null;
    for (let index = 0; index < 200 && !settingsPage; index += 1) {
      settingsPage = browser.contexts().flatMap((context) => context.pages())
        .find((candidate) => candidate.url().includes('surface=settings')) || null;
      if (!settingsPage) await delay(25);
    }
    assert.ok(settingsPage, '未找到设置页');
    await settingsPage.waitForFunction(() => window.__augitReady);
    report.scans.push(await scan(settingsPage, 'settings-appearance'));
    for (const name of ['file-view', 'git', 'terminal']) {
      await settingsPage.locator(`[data-settings-page="${name}"]`).click();
      await delay(250);
      report.scans.push(await scan(settingsPage, `settings-${name}`));
    }
    // 持久化失败提示也是界面文案；用不可写设置路径触发真实失败态。
    await settingsPage.locator('[data-settings-page="appearance"]').click();
    await delay(250);
    const originalTheme = await settingsPage.locator('[data-setting="theme"]').inputValue();
    await settingsPage.locator('[data-setting="theme"]').selectOption(originalTheme === 'Light' ? 'Dark' : 'Light');
    const settingsBackup = settings + '.saved.json';
    await fs.rename(settings, settingsBackup);
    await fs.mkdir(settings);
    try {
      await settingsPage.locator('[data-settings-action="save"]').click();
      await settingsPage.waitForFunction(() => document.querySelector('.settings-dialog')?.dataset.settingsState === 'failed');
      report.scans.push(await scan(settingsPage, 'settings-save-failure'));
    } finally {
      await fs.rmdir(settings);
      await fs.rename(settingsBackup, settings);
    }
    await settingsPage.locator('[data-settings-action="cancel"]').click();
    await page.waitForFunction(() => !window.__augitNativeModalActive);

    await page.locator('.rail-button[data-tool="project"]').click();
    await page.waitForSelector('[data-tree-path="plain.txt"]');
    await page.locator('[data-tree-path="plain.txt"]').dblclick();
    await page.waitForSelector('.code-view');
    await page.locator('.code-view').focus();
    await page.keyboard.press('Control+g');
    await page.waitForSelector('[data-compact-dialog]');
    await page.waitForFunction(() => window.__augitNativeModalActive);
    report.scans.push(await scan(page, 'go-to-line'));
    await page.keyboard.press('Escape');
    await page.waitForFunction(() => !window.__augitNativeModalActive);

    // 管理类表单使用产品已暴露的正常入口打开函数；扫描真实 DOM，不改产品状态。
    await page.evaluate(() => window.__augitOpenStashDialog());
    await page.waitForSelector('.stash-dialog');
    await page.waitForFunction(() => window.__augitNativeModalActive);
    report.scans.push(await scan(page, 'stash-create'));
    await page.keyboard.press('Escape');
    await page.waitForFunction(() => !window.__augitNativeModalActive);

    await page.evaluate(() => window.__augitOpenStashManager());
    await page.waitForSelector('.stash-manager-dialog');
    await page.waitForFunction(() => window.__augitNativeModalActive);
    report.scans.push(await scan(page, 'stash-manager'));
    await page.keyboard.press('Escape');
    await page.waitForFunction(() => !window.__augitNativeModalActive);

    await page.evaluate(() => window.__augitOpenWorktreeManager());
    await page.waitForSelector('.worktree-dialog');
    await page.waitForFunction(() => window.__augitNativeModalActive);
    report.scans.push(await scan(page, 'worktree-manager'));
    // New Worktree 空字段校验：只扫描错误提示后取消，不创建。
    await page.locator('.worktree-dialog [data-wtm-action="create"]').click();
    await page.waitForSelector('.worktree-window');
    await page.locator('.worktree-window [data-worktree-action="create"]').click();
    await page.waitForSelector('.worktree-window .worktree-notice:not([hidden])');
    report.scans.push(await scan(page, 'worktree-create-empty'));
    await page.locator('.worktree-window [data-worktree-action="cancel"]').click();
    await page.waitForFunction(() => !document.querySelector('.worktree-window'));
    await page.keyboard.press('Escape');
    await page.waitForFunction(() => !window.__augitNativeModalActive);

    await page.locator('.titlebar .branch-chip').click();
    await page.waitForSelector('.branches-popover [data-popover-action="push"]');
    // 无远程夹具触发真实 fetch 失败；扫描失败弹层文案后再重开做 push。
    await page.locator('.branches-popover [data-popover-action="fetch"]').click();
    await page.waitForSelector('.branches-popover .inline-alert.danger', { timeout: 10000 }).catch(() => null);
    await delay(400);
    report.scans.push(await scan(page, 'branch-fetch-no-remote'));
    await page.keyboard.press('Escape');
    await page.waitForFunction(() => !document.querySelector('.branches-popover'));
    await page.locator('.titlebar .branch-chip').click();
    await page.waitForSelector('.branches-popover [data-popover-action="push"]');
    await page.locator('.branches-popover [data-popover-action="push"]').click();
    await page.waitForSelector('.dialog.push-dialog');
    report.scans.push(await scan(page, 'push'));
    const define = page.locator('.dialog.push-dialog a[href$="remote.html"]');
    if (await define.count()) {
      await define.click();
      await page.waitForSelector('.dialog.remote-dialog');
      report.scans.push(await scan(page, 'remote'));
      // 次模态 Define Remote：清空名称触发真实校验，扫描错误文案后取消。
      await page.locator('.remote-window [data-remote-action="add"]').click();
      await page.waitForSelector('.remote-define-window');
      await page.locator('[data-remote-field="name"]').fill('');
      await page.locator('[data-remote-define="ok"]').click();
      await page.waitForSelector('.remote-define-window .remote-notice:not([hidden])');
      report.scans.push(await scan(page, 'remote-name-empty'));
      await page.locator('.remote-define-window .dialog-footer [data-remote-define="cancel"]').click();
      await page.waitForFunction(() => !document.querySelector('.remote-define-window'));
      await page.locator('.remote-window [data-remote-action="add"]').click();
      await page.waitForSelector('.remote-define-window');
      await page.locator('[data-remote-field="name"]').fill('origin');
      await page.locator('[data-remote-field="fetchUrl"]').fill('');
      await page.locator('[data-remote-define="ok"]').click();
      await page.waitForSelector('.remote-define-window .remote-notice:not([hidden])');
      report.scans.push(await scan(page, 'remote-url-empty'));
      await page.locator('.remote-define-window .dialog-footer [data-remote-define="cancel"]').click();
      await page.waitForFunction(() => !document.querySelector('.remote-define-window'));
      await page.locator('.remote-window [data-remote-action="ok"]').click();
      await page.waitForFunction(() => !document.querySelector('.dialog.remote-dialog'));
    }
    await page.locator('.dialog.push-dialog [data-push-action="cancel"]').click();
    await page.waitForFunction(() => !window.__augitNativeModalActive);

    assert.deepEqual(errors, [], '页面错误：' + errors.join(' | '));
    assert.deepEqual(await page.evaluate(() => window.__augitErrors), []);
    // 负向对照：注入一个可见中文节点，扫描器必须检出，否则“0 发现”不能证明英文通过。
    await page.evaluate(() => {
      const control = document.createElement('span');
      control.id = 'audit-cjk-control';
      control.textContent = '中文负向对照';
      document.querySelector('.statusbar').append(control);
    });
    const negativeControl = await scan(page, 'negative-control');
    await page.evaluate(() => document.getElementById('audit-cjk-control')?.remove());
    report.negativeControlFindings = negativeControl.findings.length;
    assert.ok(report.negativeControlFindings > 0, '英文审计扫描器未检出注入的中文对照');
    report.findings = report.scans.flatMap((entry) => entry.findings.map((finding) => ({ ...finding, scan: entry.label })));
    await fs.writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n');
    assert.equal(report.findings.length, 0, '发现中文界面文案：' + JSON.stringify(report.findings.slice(0, 8)));
    console.log('UI_ENGLISH_OK pid=' + pid + ' scans=' + report.scans.length);
  } catch (error) {
    report.error = String(error && error.stack || error);
    await fs.writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2)).catch(() => {});
    console.error(report.error);
    process.exitCode = 1;
  } finally {
    try { if (browser) await browser.close(); } catch { /* 继续清理 */ }
    if (pid) {
      try {
        await execute('powershell.exe', ['-NoProfile', '-Command',
          '$p=Get-Process -Id ' + pid + ' -ErrorAction Stop; if (-not $p.CloseMainWindow()) { throw "CloseMainWindow=false" }; if (-not $p.WaitForExit(8000)) { throw "still running" }'],
          { windowsHide: true, timeout: 20000 });
        report.closeMethod = 'CloseMainWindow';
      } catch {
        try { await execute('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true, timeout: 20000 }); } catch { /* 已退出 */ }
        report.closeMethod = 'taskkill';
      }
    }
    try { await fs.rm(temporary, { recursive: true, force: true }); report.temporaryRemoved = true; }
    catch { report.temporaryRemoved = false; }
    await fs.writeFile(path.join(output, 'cleanup.json'), JSON.stringify(report, null, 2)).catch(() => {});
  }
})().catch((error) => { console.error(error); process.exitCode = 1; });
