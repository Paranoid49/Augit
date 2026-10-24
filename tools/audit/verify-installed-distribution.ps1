# Installed-distribution acceptance: install beta, upgrade to rc, verify integrations and
# settings, then uninstall and confirm byte-for-byte PATH restoration.
# Must run elevated. Only touches the Augit install, its own registry keys/PATH entry and
# an isolated settings file; it never removes shared .NET/WebView2 runtimes.
param(
    [Parameter(Mandatory = $true)][string]$BetaSetup,
    [Parameter(Mandatory = $true)][string]$RcSetup,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$BetaVersion = '1.0.0-beta.1',
    [string]$RcVersion = '1.0.0-rc.1'
)

$ErrorActionPreference = 'Stop'
$BetaSetup = [System.IO.Path]::GetFullPath($BetaSetup)
$RcSetup = [System.IO.Path]::GetFullPath($RcSetup)
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$report = [ordered]@{
    startedAt = (Get-Date).ToString('o')
    elevated = $false
    betaSetup = $BetaSetup
    rcSetup = $RcSetup
    betaSetupSha256 = ''
    rcSetupSha256 = ''
    before = [ordered]@{}
    betaInstall = [ordered]@{}
    betaState = [ordered]@{}
    rcUpgrade = [ordered]@{}
    rcState = [ordered]@{}
    uninstall = [ordered]@{}
    after = [ordered]@{}
    result = 'RUNNING'
    failures = @()
}

function Get-PathValue {
    $key = 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
    $item = Get-Item -LiteralPath $key
    return [ordered]@{
        raw = [string]$item.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        kind = [string]$item.GetValueKind('Path')
    }
}

function Get-AugitUninstallEntries {
    $roots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
    )
    $entries = @()
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($key in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
            $item = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
            if ($item -and $item.DisplayName -like 'Augit*') {
                $entries += [ordered]@{
                    key = $key.PSPath
                    displayName = [string]$item.DisplayName
                    displayVersion = [string]$item.DisplayVersion
                    installLocation = [string]$item.InstallLocation
                    uninstallString = [string]$item.UninstallString
                }
            }
        }
    }
    return $entries
}

function Get-SettingsHash([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}

function Get-RuntimeSnapshot {
    $dotnet = @(& dotnet --list-runtimes 2>$null | Where-Object { $_ -match '^Microsoft\.(NETCore|WindowsDesktop)\.App' })
    $webview = $null
    $key = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
    if (Test-Path -LiteralPath $key) { $webview = (Get-ItemProperty -LiteralPath $key).pv }
    return [ordered]@{ dotnet = $dotnet; webview2 = [string]$webview }
}

function Wait-Until([scriptblock]$Condition, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Get-FileProductVersion([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return (Get-Item -LiteralPath $path).VersionInfo.ProductVersion
}

function Invoke-Setup([string]$setup, [string]$logName, [string[]]$tasks) {
    $log = Join-Path $OutDir $logName
    $arguments = @('/SILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$log")
    if ($tasks.Count -gt 0) { $arguments += "/TASKS=""$($tasks -join ',')""" }
    $process = Start-Process -FilePath $setup -ArgumentList $arguments -PassThru -Wait
    return [ordered]@{ exitCode = $process.ExitCode; log = $log }
}

$settingsPath = Join-Path $env:LOCALAPPDATA 'Augit\settings.json'
$installDir = Join-Path $env:ProgramFiles 'Augit'
$startMenu = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Augit\Augit.lnk'
$explorerKeys = @(
    'Registry::HKEY_CLASSES_ROOT\Directory\shell\Augit',
    'Registry::HKEY_CLASSES_ROOT\Directory\Background\shell\Augit'
)

try {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    $report.elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $report.elevated) {
        throw 'This verification must run elevated.'
    }
    if (-not (Test-Path -LiteralPath $BetaSetup)) { throw "beta setup missing: $BetaSetup" }
    if (-not (Test-Path -LiteralPath $RcSetup)) { throw "rc setup missing: $RcSetup" }
    if (Test-Path -LiteralPath $installDir) { throw "Augit is already installed: $installDir" }
    if (@(Get-AugitUninstallEntries).Count -gt 0) { throw 'An existing Augit uninstall entry is present.' }

    $report.betaSetupSha256 = (Get-FileHash -LiteralPath $BetaSetup -Algorithm SHA256).Hash.ToLowerInvariant()
    $report.rcSetupSha256 = (Get-FileHash -LiteralPath $RcSetup -Algorithm SHA256).Hash.ToLowerInvariant()
    $report.before = [ordered]@{
        path = Get-PathValue
        uninstall = @(Get-AugitUninstallEntries)
        explorerKeys = @($explorerKeys | ForEach-Object { Test-Path -LiteralPath $_ })
        startMenu = (Test-Path -LiteralPath $startMenu)
        settingsSha256 = Get-SettingsHash $settingsPath
        runtimes = Get-RuntimeSnapshot
        installDir = (Test-Path -LiteralPath $installDir)
        betaSetupSignature = [string](Get-AuthenticodeSignature -LiteralPath $BetaSetup).Status
        rcSetupSignature = [string](Get-AuthenticodeSignature -LiteralPath $RcSetup).Status
        betaSetupProductVersion = (Get-Item -LiteralPath $BetaSetup).VersionInfo.ProductVersion
        rcSetupProductVersion = (Get-Item -LiteralPath $RcSetup).VersionInfo.ProductVersion
    }

    $report.betaInstall = Invoke-Setup $BetaSetup 'beta-install.log' @('addtopath', 'explorercontext')
    if ($report.betaInstall.exitCode -ne 0) { throw "beta installer exit code $($report.betaInstall.exitCode)" }
    if (-not (Test-Path -LiteralPath (Join-Path $installDir 'Augit.exe'))) { throw 'beta install did not create Augit.exe' }
    $betaPath = Get-PathValue
    $report.betaState = [ordered]@{
        exeProductVersion = Get-FileProductVersion (Join-Path $installDir 'Augit.exe')
        readme = (Test-Path -LiteralPath (Join-Path $installDir 'README.md'))
        license = (Test-Path -LiteralPath (Join-Path $installDir 'LICENSE'))
        notices = (Test-Path -LiteralPath (Join-Path $installDir 'THIRD-PARTY-NOTICES.md'))
        uninstall = @(Get-AugitUninstallEntries)
        pathContainsInstallDir = $betaPath.raw -split ';' | Where-Object { $_.TrimEnd('\') -ieq $installDir.TrimEnd('\') } | Measure-Object | Select-Object -ExpandProperty Count
        pathKind = $betaPath.kind
        explorerKeys = @($explorerKeys | ForEach-Object { Test-Path -LiteralPath $_ })
        startMenu = (Test-Path -LiteralPath $startMenu)
        settingsSha256 = Get-SettingsHash $settingsPath
    }
    if ($report.betaState.exeProductVersion -notlike "$BetaVersion*") { throw "beta exe version $($report.betaState.exeProductVersion)" }
    if (-not $report.betaState.readme -or -not $report.betaState.license -or -not $report.betaState.notices) { throw 'beta package documents missing after install' }
    if ($report.betaState.pathContainsInstallDir -ne 1) { throw 'beta PATH entry missing or duplicated' }
    if (@($report.betaState.explorerKeys | Where-Object { -not $_ }).Count -gt 0) { throw 'beta explorer context menu missing' }

    # Launch the installed executable with an isolated settings file and workspace.
    $workspace = Join-Path $OutDir 'workspace'
    if (Test-Path -LiteralPath $workspace) { Remove-Item -LiteralPath $workspace -Recurse -Force }
    New-Item -ItemType Directory -Path $workspace -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $workspace 'README.md') -Value '# installed smoke' -Encoding UTF8
    $isolatedSettings = Join-Path $OutDir 'settings-isolated.json'
    if (Test-Path -LiteralPath $isolatedSettings) { Remove-Item -LiteralPath $isolatedSettings -Force }
    $exe = Join-Path $installDir 'Augit.exe'
    $started = Get-Date
    $process = Start-Process -FilePath $exe -ArgumentList @('--workspace', ('"{0}"' -f $workspace), '--settings-file', ('"{0}"' -f $isolatedSettings)) -PassThru
    $window = Wait-Until { $process.Refresh(); $process.MainWindowHandle -ne 0 } 30
    $report.betaState.launch = [ordered]@{
        pid = $process.Id
        windowAppeared = $window
        windowAppearedMs = [math]::Round(((Get-Date) - $started).TotalMilliseconds, 1)
        windowTitle = $process.MainWindowTitle
    }
    if (-not $window) { throw 'installed beta did not show a window' }
    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(15000)) { $process.Kill(); throw 'installed beta did not close normally' }
    $report.betaState.launch.exitCode = $process.ExitCode
    if ($process.ExitCode -ne 0) { throw "installed beta exit code $($process.ExitCode)" }

    # Upgrade in place. No /TASKS: UsePreviousTasks=yes must retain PATH and context menu.
    $report.rcUpgrade = Invoke-Setup $RcSetup 'rc-upgrade.log' @()
    if ($report.rcUpgrade.exitCode -ne 0) { throw "rc installer exit code $($report.rcUpgrade.exitCode)" }
    $rcPath = Get-PathValue
    $report.rcState = [ordered]@{
        exeProductVersion = Get-FileProductVersion (Join-Path $installDir 'Augit.exe')
        uninstall = @(Get-AugitUninstallEntries)
        pathContainsInstallDir = $rcPath.raw -split ';' | Where-Object { $_.TrimEnd('\') -ieq $installDir.TrimEnd('\') } | Measure-Object | Select-Object -ExpandProperty Count
        pathUnchangedFromBeta = ($rcPath.raw -eq $betaPath.raw)
        explorerKeys = @($explorerKeys | ForEach-Object { Test-Path -LiteralPath $_ })
        startMenu = (Test-Path -LiteralPath $startMenu)
        settingsSha256 = Get-SettingsHash $settingsPath
        readme = (Test-Path -LiteralPath (Join-Path $installDir 'README.md'))
        license = (Test-Path -LiteralPath (Join-Path $installDir 'LICENSE'))
    }
    if ($report.rcState.exeProductVersion -notlike "$RcVersion*") { throw "rc exe version $($report.rcState.exeProductVersion)" }
    if ($report.rcState.pathContainsInstallDir -ne 1) { throw 'rc upgrade duplicated or removed PATH entry' }
    if (-not $report.rcState.pathUnchangedFromBeta) { throw 'rc upgrade changed PATH value' }
    if (@($report.rcState.explorerKeys | Where-Object { -not $_ }).Count -gt 0) { throw 'rc upgrade removed explorer menu' }
    if ($report.rcState.settingsSha256 -ne $report.before.settingsSha256) { throw 'upgrade changed user settings' }

    # Uninstall silently and wait for the directory and registry entries to disappear.
    $uninstaller = Join-Path $installDir 'unins000.exe'
    if (-not (Test-Path -LiteralPath $uninstaller)) { throw 'uninstaller missing' }
    $uninstallProcess = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -PassThru
    [void]$uninstallProcess.WaitForExit(120000)
    $remaining = Wait-Until { -not (Test-Path -LiteralPath $installDir) } 60
    $report.uninstall = [ordered]@{
        exitCode = $uninstallProcess.ExitCode
        directoryRemoved = $remaining
    }
    if (-not $remaining) { throw 'uninstall did not remove the install directory' }

    $afterPath = Get-PathValue
    $report.after = [ordered]@{
        path = $afterPath
        pathRestoredExactly = ($afterPath.raw -eq $report.before.path.raw)
        pathKindRestored = ($afterPath.kind -eq $report.before.path.kind)
        uninstall = @(Get-AugitUninstallEntries)
        explorerKeys = @($explorerKeys | ForEach-Object { Test-Path -LiteralPath $_ })
        startMenu = (Test-Path -LiteralPath $startMenu)
        settingsSha256 = Get-SettingsHash $settingsPath
        settingsPreserved = ((Get-SettingsHash $settingsPath) -eq $report.before.settingsSha256)
        runtimes = Get-RuntimeSnapshot
        runtimesUnchanged = (($null -ne $report.before.runtimes) -and ((Get-RuntimeSnapshot | ConvertTo-Json -Compress) -eq ($report.before.runtimes | ConvertTo-Json -Compress)))
        installDir = (Test-Path -LiteralPath $installDir)
    }
    if (-not $report.after.pathRestoredExactly) { throw 'system PATH was not restored exactly' }
    if (@($report.after.uninstall).Count -ne 0) { throw 'uninstall registry entry remains' }
    if (@($report.after.explorerKeys | Where-Object { $_ }).Count -ne 0) { throw 'explorer context menu remains' }
    if ($report.after.startMenu) { throw 'start menu shortcut remains' }
    if (-not $report.after.settingsPreserved) { throw 'uninstall removed user settings' }
    if (-not $report.after.runtimesUnchanged) { throw 'shared runtimes changed during install/uninstall' }

    $report.result = 'PASS'
} catch {
    $report.failures += $_.Exception.Message
    $report.result = 'FAIL'
    # Best-effort cleanup: only remove the install this script created.
    try {
        if (Test-Path -LiteralPath (Join-Path $installDir 'unins000.exe')) {
            $cleanup = Start-Process -FilePath (Join-Path $installDir 'unins000.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -PassThru
            [void]$cleanup.WaitForExit(120000)
        }
    } catch {
        $report.failures += 'cleanup: ' + $_.Exception.Message
    }
} finally {
    $report.finishedAt = (Get-Date).ToString('o')
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $OutDir 'installed-report.json') -Encoding UTF8
    Write-Output "RESULT $($report.result)"
    foreach ($failure in $report.failures) { Write-Output "FAIL $failure" }
}
if ($report.result -ne 'PASS') { exit 1 }
