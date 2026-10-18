# Build the Augit shell and capture one scene from it as on-screen pixels.
# 构建不关闭其他实例，截图脚本只清理自身启动的进程。
param(
  [string]$Scene = "main-project",
  [string]$Theme = "dark",
  [string]$Out = "",
  [int]$SettleMs = 3500,
  # Bottom tool windows (Git log, file history, terminal) sit below the fold at the
  # default window height, so auditing them needs a taller window.
  [int]$Height = 760,
  [int]$Attempts = 4,
  # Audit-only logical DPI override (96/120/144). Scales the window so the logical
  # size stays constant; does not touch registry or system display settings.
  [int]$Dpi = 0,
  [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
$repo = 'D:\github\Augit'
$exe = Join-Path $repo 'src\Augit.Shell\bin\Release\net10.0-windows\win-x64\Augit.exe'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if ($Out -eq "") { $Out = Join-Path $repo ("artifacts\shell-{0}-{1}.png" -f $Scene, $Theme) }
if (-not $SkipBuild) {
  & $dotnet build (Join-Path $repo 'src\Augit.Shell\Augit.Shell.csproj') -c Release -p:NuGetAudit=false 2>&1 | Select-Object -Last 4 | ForEach-Object { Write-Output $_ }
  # 构建失败立即退出，避免继续对旧程序取证。
  if ($LASTEXITCODE -ne 0) {
    Write-Output ("BUILD_FAILED exit=" + $LASTEXITCODE)
    exit $LASTEXITCODE
  }
}
$shellArgs = @('--scene', $Scene, '--theme', $Theme, '--height', "$Height")
if ($Dpi -gt 0) { $shellArgs += @('--dpi', "$Dpi") }
& (Join-Path $repo 'tools\audit\capture-surface.ps1') -Exe $exe -Out $Out -Arguments $shellArgs -WorkDir $repo -SettleMs $SettleMs -Attempts $Attempts
# 透传截图结果，包装脚本不管理用户原有实例。
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
