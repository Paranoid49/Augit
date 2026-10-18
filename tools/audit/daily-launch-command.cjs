// 日常启动验收只为读取页面打开调试端口；产品入口、配置和系统缩放保持正式路径。
const path = require('node:path');
module.exports = function dailyLaunchCommand(repo, settings, port, workspace = repo) {
  if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('调试端口无效。');
  const literal = value => "'" + String(value).replaceAll("'", "''") + "'";
  const launcher = path.resolve(repo, 'tools/start-augit.ps1');
  return {
    file: 'powershell.exe',
    args: ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command',
      '$ErrorActionPreference="Stop"; $p = & ' + literal(launcher) +
      ' -Workspace ' + literal(path.resolve(workspace)) +
      ' -SettingsFile ' + literal(settings) +
      '; if (-not $p.Id) { throw "日常启动器未返回进程。" }; Write-Output ("AUGIT_PID=" + $p.Id)'],
    env: { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: '--remote-debugging-port=' + port },
  };
};
