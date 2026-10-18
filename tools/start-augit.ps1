param(
    [string]$Workspace = (Split-Path $PSScriptRoot -Parent),
    [switch]$AlignAppearance,
    [switch]$PrepareOnly,
    [switch]$SkipBuild,
    [string]$SettingsFile
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$workspacePath = (Resolve-Path -LiteralPath $Workspace).Path
$executable = Join-Path $repoRoot 'src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe'
$webRoot = Join-Path $repoRoot 'web'

# 日常入口沿用默认配置和会话；显式配置路径只供隔离验证或用户指定实例使用。
$settingsPath = if ($SettingsFile) { [IO.Path]::GetFullPath($SettingsFile) } else { Join-Path $env:LOCALAPPDATA 'Augit/settings.json' }
if ($AlignAppearance) {
    if (Get-Process -Name Augit -ErrorAction SilentlyContinue) {
        throw '请先正常关闭 Augit 再对齐外观，避免运行中的窗口覆盖设置。'
    }
    $settings = if (Test-Path -LiteralPath $settingsPath) {
        Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    } else { [pscustomobject]@{} }
    if ($null -eq $settings -or $settings -isnot [pscustomobject]) { throw '设置文件不是有效对象，未修改。' }
    $values = @{ appearanceVersion = 1; theme = 'Light'; textFontFamily = 'Microsoft YaHei UI'; textFontSize = 12; monospaceFontFamily = 'JetBrains Mono'; fontSize = 13 }
    $changed = @($values.Keys | Where-Object { $settings.$_ -cne $values[$_] }).Count -gt 0
    if ($changed) {
        if (Test-Path -LiteralPath $settingsPath) {
            $backup = "$settingsPath.before-startup-alignment-$([guid]::NewGuid().ToString('N')).bak"
            Copy-Item -LiteralPath $settingsPath -Destination $backup
            Write-Host "原配置备份：$backup"
        }
        foreach ($key in $values.Keys) { $settings | Add-Member -NotePropertyName $key -NotePropertyValue $values[$key] -Force }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($settingsPath)) | Out-Null
        $json = ($settings | ConvertTo-Json -Depth 100) -replace "`r`n", "`n"
        [IO.File]::WriteAllText($settingsPath, $json + "`n", [Text.UTF8Encoding]::new($false))
    }
}

if (-not $SkipBuild) {
    # 恢复源码可能保留旧时间戳；不能让增量构建误用较新的试验 DLL。
    & dotnet build (Join-Path $repoRoot 'src/Augit.Shell/Augit.Shell.csproj') --no-restore --no-incremental /nodeReuse:false -v minimal | Out-Host
    if ($LASTEXITCODE -ne 0) { throw '构建失败，未启动旧程序。' }
}
if (-not (Test-Path -LiteralPath $executable)) { throw "找不到程序：$executable" }
if ($PrepareOnly) { return }

# 不加入审计用的 DPI、场景、临时 profile 或会话重置参数。
$launchArgs = @('--web-root', ('"{0}"' -f $webRoot), '--workspace', ('"{0}"' -f $workspacePath))
if ($SettingsFile) { $launchArgs += @('--settings-file', ('"{0}"' -f $settingsPath)) }
$process = Start-Process -FilePath $executable -ArgumentList $launchArgs -WorkingDirectory $repoRoot -WindowStyle Normal -PassThru
Write-Host "Augit PID=$($process.Id)；退出请关闭窗口；必要时停止本实例：taskkill /PID $($process.Id) /T /F"
$process
