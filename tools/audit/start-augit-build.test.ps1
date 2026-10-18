$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$launcher = Join-Path $repoRoot 'tools/start-augit.ps1'
$global:augitLauncherProbe = @{ Calls = @(); ExitCode = 0 }
# 只替换本测试进程中的构建命令；不启动窗口、不改用户配置。
function dotnet {
    $global:augitLauncherProbe.Calls += ,@($args)
    $global:LASTEXITCODE = $global:augitLauncherProbe.ExitCode
}
& $launcher -PrepareOnly
if ($global:augitLauncherProbe.Calls.Count -ne 1) { throw '普通入口必须执行构建。' }
$build = $global:augitLauncherProbe.Calls[0]
if ($build[0] -ne 'build' -or $build -notcontains '--no-incremental' -or $build -notcontains '--no-restore' -or $build -notcontains '/nodeReuse:false') {
    throw '普通入口必须强制重新编译，且不保留构建节点。'
}
& $launcher -PrepareOnly -SkipBuild
if ($global:augitLauncherProbe.Calls.Count -ne 1) { throw '显式跳过构建不应重复调用构建。' }
$global:augitLauncherProbe.ExitCode = 1
$failed = $false
try { & $launcher -PrepareOnly } catch {
    if ($_.Exception.Message -ne '构建失败，未启动旧程序。') { throw }
    $failed = $true
}
if (-not $failed) { throw '构建失败必须中止，不能启动旧产物。' }
Write-Host '通过：普通入口重新编译、关闭节点复用、显式跳过、失败阻止启动。'
