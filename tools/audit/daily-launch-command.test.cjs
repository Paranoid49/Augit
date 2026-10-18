const test = require('node:test');
const assert = require('node:assert/strict');
const command = require('./daily-launch-command.cjs');
test('日常入口保留工作区和配置，不引入场景、DPI或样式覆盖', () => {
  const result = command(process.cwd(), "C:/Users/test's folder/settings.json", 54321);
  assert.equal(result.file, 'powershell.exe');
  const script = result.args.at(-1);
  assert.ok(script.includes('tools'));
  assert.ok(script.includes('start-augit.ps1'));
  assert.ok(script.includes("'C:/Users/test''s folder/settings.json'"));
  assert.doesNotMatch(script, /SkipBuild|AlignAppearance|pixel-exact|--scene|--dpi|no-session-restore/);
  assert.equal(result.env.WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS, '--remote-debugging-port=54321');
});
test('拒绝非法调试端口', () => {
  for (const port of [0, 65536, NaN, 1.5, '123']) assert.throws(() => command('.', 'settings.json', port));
});

test('远端取证可选择参考仓库，但仍使用项目内日常启动器', () => {
  const path = require('node:path');
  const result = command(process.cwd(), 'settings.json', 54321, 'reference-fixture');
  const script = result.args.at(-1);
  assert.ok(script.includes(path.resolve(process.cwd(), 'tools/start-augit.ps1')));
  assert.ok(script.includes(" -Workspace '" + path.resolve('reference-fixture') + "'"));
  assert.doesNotMatch(script, /SkipBuild|AlignAppearance|--width|--height|--user-data-folder|--scene|--dpi/);
});

test('日常入口证据不能把实际强制构建误记为跳过构建', () => {
  const source = require('node:fs').readFileSync(require('node:path').join(__dirname, 'compare-startup-paths.cjs'), 'utf8');
  const report = source.split(String.fromCharCode(10)).find(line => line.includes('reports.push('));
  assert.ok(report.includes('tools/start-augit.ps1'));
  assert.ok(!report.includes('SkipBuild'));
});

test('独立设置日常回归执行真实启动器并记录实际进程', async () => {
  const source = require('node:fs').readFileSync(require('node:path').join(__dirname, 'independent-settings.cjs'), 'utf8');
  const start = source.indexOf('  if(dailyLauncher){');
  const block = source.slice(start, source.indexOf('  console.log(JSON.stringify({pid:', start));
  const launch = { file: 'powershell.exe', args: ['真实启动命令'], env: { marker: 'daily' } };
  for (const stdout of ['构建成功\nAUGIT_PID=12345\r\n', '构建成功但没有进程编号']) {
    const evidence = new Map(), calls = [];
    const context = { dailyLauncher: true, configuration: 'Debug', temp: 'temp', port: 54321, evidence,
      process: { cwd: () => 'repo' }, path: require('node:path'), assert,
      require: name => { assert.equal(name, './daily-launch-command.cjs'); return () => launch; },
      execute: async (...args) => { calls.push(args); return { stdout }; },
      spawn: () => { throw new Error('不得绕过日常启动器直接启动EXE'); } };
    const run = require('node:vm').runInNewContext('(async()=>{let shell;' + block + ';return shell;})()', context);
    if (!stdout.includes('AUGIT_PID=')) { await assert.rejects(run, /未返回实际Augit PID/); continue; }
    assert.equal((await run).pid, 12345);
    assert.equal(calls[0][0], launch.file); assert.equal(calls[0][1], launch.args);
    assert.equal(calls[0][2].env, launch.env);
    const recorded = evidence.get('launch.json');
    assert.equal(recorded.profile, 'default-session');
    assert.equal(recorded.forcedBuild, true); assert.equal(recorded.sourceWeb, true);
    assert.equal(recorded.launchArgs, undefined);
  }
});
