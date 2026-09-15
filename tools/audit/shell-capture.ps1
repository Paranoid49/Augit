# Build the Augit shell and capture one scene from it as on-screen pixels.
# Kills stale shell processes first so the output executable is never locked.
param(
  [string]$Scene = "main-project",
  [string]$Theme = "dark",
  [string]$Out = "",
  [int]$SettleMs = 3500,
  [int]$Attempts = 4,
  [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
$repo = 'D:\github\Augit'
$exe = Join-Path $repo 'src\Augit.Shell\bin\Release\net10.0-windows\win-x64\Augit.Shell.exe'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if ($Out -eq "") { $Out = Join-Path $repo ("artifacts\shell-{0}-{1}.png" -f $Scene, $Theme) }
Get-Process Augit.Shell -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
Start-Sleep -Milliseconds 600
if (-not $SkipBuild) {
  & $dotnet build (Join-Path $repo 'src\Augit.Shell\Augit.Shell.csproj') -c Release -p:NuGetAudit=false 2>&1 | Select-Object -Last 4 | ForEach-Object { Write-Output $_ }
}
& (Join-Path $repo 'tools\audit\capture-surface.ps1') -Exe $exe -Out $Out -Arguments '--scene', $Scene, '--theme', $Theme -WorkDir $repo -SettleMs $SettleMs -Attempts $Attempts
Get-Process Augit.Shell -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
