// 独立启动审计实例；设置、profile、外壳及WebView2子进程仅归属本次运行。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { spawn, execFile } = require('node:child_process');
const { promisify } = require('node:util');
const net = require('node:net');
const execute = promisify(execFile);

(async () => {
  const repo = path.resolve(__dirname, '../..');
  const audit = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-restoration-'));
  const port = 9338;
  let shell;
  try {
    const probe = net.createServer();
    await new Promise((resolve, reject) => { probe.once('error', reject); probe.listen(port, '127.0.0.1', resolve); });
    await new Promise(resolve => probe.close(resolve));
    const settings = path.join(audit, 'settings.json');
    await fs.writeFile(settings, JSON.stringify({ theme: 'Light', textFontFamily: 'Microsoft YaHei UI', textFontSize: 14, monospaceFontFamily: 'Cascadia Mono', fontSize: 13 }));
    shell = spawn(path.join(repo, 'src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe'), [
      '--workspace', repo, '--open', 'docs/product-spec.md', '--no-session-restore', '--width', '1180', '--height', '760',
      '--settings-file', settings, '--user-data-folder', path.join(audit, 'profile'), '--browser-args', `--remote-debugging-port=${port}`,
    // 本实例用于真实前台截图；仅外壳窗口可见，测试驱动和清理命令保持隐藏。
    ], { cwd: repo, windowsHide: false, stdio: 'ignore' });
    shell.on('error', error => { console.error(error); });
    console.log(`AUDIT_PID=${shell.pid} PORT=${port} TEMP=${audit}`);
    const child = spawn(process.execPath, [path.join(__dirname, 'live-restoration-fixes-shell.cjs'), process.argv[2], `http://127.0.0.1:${port}`, path.join(repo, 'artifacts/restoration-fixes-2026-10-12')], { cwd: repo, windowsHide: true, stdio: 'inherit' });
    const exitCode = await new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', resolve); });
    assert.equal(exitCode, 0, '真实外壳回归失败');
  } finally {
    const resolved = path.resolve(audit);
    assert.ok(resolved.startsWith(path.resolve(os.tmpdir()) + path.sep) && path.basename(resolved).startsWith('Augit-restoration-'));
    try {
      if (shell?.pid && shell.exitCode === null) {
        let killError;
        try { await execute('taskkill.exe', ['/PID', String(shell.pid), '/T', '/F'], { windowsHide: true }); }
        catch (error) { killError = error; }
        // 进程树有子进程自行退出时taskkill可能返回非零；先等根进程退出事件再判断。
        if (shell.exitCode === null) await new Promise(resolve => {
          const finish = () => { clearTimeout(timer); shell.removeListener('exit', finish); resolve(); };
          const timer = setTimeout(finish, 2000);
          shell.once('exit', finish);
        });
        if (shell.exitCode === null) throw killError || new Error('审计外壳未退出。');
      }
    } finally {
      // 即使停止命令失败，也执行本实例临时目录清理，不触碰其它profile。
      await fs.rm(resolved, { recursive: true, force: true, maxRetries: 8, retryDelay: 200 });
      console.log(`TEMP_REMOVED=${audit}`);
    }
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
