$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$launcher = Join-Path $repoRoot 'tools/start-augit.ps1'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('Augit-launch-test-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
try {
    $settingsPath = Join-Path $tempRoot 'settings.json'
    $original = '{"theme":"Dark","textFontSize":14,"fontSize":16,"monospaceFontFamily":"Cascadia Mono","openFiles":["用户文件.txt"],"toolWindows":{"projectPanelWidth":327},"customField":true}'
    [IO.File]::WriteAllText($settingsPath, $original, [Text.UTF8Encoding]::new($false))
    & $launcher -SettingsFile $settingsPath -PrepareOnly -SkipBuild
    if ([IO.File]::ReadAllText($settingsPath) -cne $original) { throw '普通入口不应重置偏好。' }
    & $launcher -SettingsFile $settingsPath -AlignAppearance -PrepareOnly -SkipBuild
    $actual = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($actual.textFontSize -ne 12 -or $actual.fontSize -ne 13 -or $actual.monospaceFontFamily -ne 'JetBrains Mono' -or $actual.theme -ne 'Light') { throw '外观未对齐。' }
    if ($actual.appearanceVersion -ne 1) { throw '显式对齐必须登记外观版本，避免后续再次迁移。' }
    if ($actual.openFiles[0] -ne '用户文件.txt' -or $actual.toolWindows.projectPanelWidth -ne 327 -or -not $actual.customField) { throw '不相关配置丢失。' }
    $backups = @(Get-ChildItem -LiteralPath $tempRoot -Filter '*.bak')
    if ($backups.Count -ne 1 -or [IO.File]::ReadAllText($backups[0].FullName) -cne $original) { throw '原始配置备份不完整。' }
    & $launcher -SettingsFile $settingsPath -AlignAppearance -PrepareOnly -SkipBuild
    if (@(Get-ChildItem -LiteralPath $tempRoot -Filter '*.bak').Count -ne 1) { throw '重复执行不应重复迁移。' }
    Write-Host '通过：普通入口保留偏好、显式外观对齐、其他配置保留、原文备份及重复执行。'
} finally {
    $resolved = [IO.Path]::GetFullPath($tempRoot)
    if (-not $resolved.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'Augit-launch-test-*') { throw '拒绝清理非本次临时目录。' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
