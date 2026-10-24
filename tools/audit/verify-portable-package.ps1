# Smoke-verify a built portable package: content inventory, web tree identity,
# real launch from the extracted package, on-screen capture and clean shutdown.
# This script never builds or substitutes the development output.
param(
    [Parameter(Mandatory = $true)][string]$PortableZip,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$ExpectedVersion = '',
    [string]$RepoRoot = '',
    [switch]$SkipLaunch
)

$ErrorActionPreference = 'Stop'
# Native stderr (git warnings) is a terminating error under Stop in Windows
# PowerShell 5.1. Run only the fixture git commands with Continue and check
# $LASTEXITCODE explicitly.
function Invoke-FixtureGit([string[]]$GitArguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & git -c core.autocrlf=false @GitArguments 2>&1
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    if ($code -ne 0) { throw "git $($GitArguments -join ' ') failed ($code): $output" }
    return $output
}
if ($RepoRoot -eq '') {
    $RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
}
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
$PortableZip = [System.IO.Path]::GetFullPath($PortableZip)
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
if (-not (Test-Path -LiteralPath $PortableZip)) { throw "portable zip missing: $PortableZip" }

$report = [ordered]@{
    startedAt = (Get-Date).ToString('o')
    portableZip = $PortableZip
    portableZipSize = (Get-Item -LiteralPath $PortableZip).Length
    portableZipSha256 = ''
    expectedVersion = $ExpectedVersion
    extractedRoot = ''
    missingFiles = @()
    extraTopLevel = @()
    webMismatches = @()
    versionChecks = [ordered]@{}
    launch = [ordered]@{}
    cleanup = [ordered]@{}
    result = 'RUNNING'
}

$extractRoot = Join-Path $OutDir 'portable'
if (Test-Path -LiteralPath $extractRoot) { Remove-Item -LiteralPath $extractRoot -Recurse -Force }
New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
$report.portableZipSha256 = (Get-FileHash -LiteralPath $PortableZip -Algorithm SHA256).Hash.ToLowerInvariant()
Expand-Archive -LiteralPath $PortableZip -DestinationPath $extractRoot -Force

# The archive is expected to contain a single top-level "Augit" directory.
$top = @(Get-ChildItem -LiteralPath $extractRoot)
$packageRoot = $extractRoot
if ($top.Count -eq 1 -and $top[0].PSIsContainer) { $packageRoot = $top[0].FullName }
$report.extractedRoot = $packageRoot
$report.extraTopLevel = @($top | Where-Object { $_.PSIsContainer -and $_.FullName -ne $packageRoot } | ForEach-Object Name)

$required = @(
    'Augit.exe',
    'Augit.dll',
    'Augit.Core.dll',
    'Augit.Infrastructure.dll',
    'Augit.deps.json',
    'Augit.runtimeconfig.json',
    'native\WebView2Loader.dll',
    'Microsoft.Web.WebView2.Core.dll',
    'web\index.html',
    'web\src\mockup.js',
    'web\src\mockup.css',
    'web\src\live-data.js',
    'web\src\bridge.js',
    'web\vendor\xterm\xterm.js',
    'web\vendor\xterm\xterm.css',
    'web\vendor\xterm\addon-fit.js',
    'web\vendor\jetbrains-mono\JetBrainsMono-Regular.woff2',
    'web\vendor\jetbrains-mono\JetBrainsMono-Bold.woff2',
    'web\vendor\jetbrains-mono\JetBrainsMono-Italic.woff2',
    'web\vendor\jetbrains-mono\JetBrainsMono-BoldItalic.woff2',
    'web\vendor\jetbrains-mono\OFL.txt',
    'tools\rg.exe',
    'licenses\ripgrep-LICENSE-MIT.txt',
    'licenses\ripgrep-UNLICENSE.txt',
    'licenses\Microsoft.Web.WebView2-LICENSE.txt',
    'licenses\Microsoft.Web.WebView2-NOTICE.txt',
    'licenses\xterm-LICENSE.txt',
    'licenses\xterm-addon-fit-LICENSE.txt',
    'licenses\JetBrainsMono-OFL.txt',
    'README.md',
    'LICENSE',
    'THIRD-PARTY-NOTICES.md'
)
$report.missingFiles = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $packageRoot $_)) })

# Compare every packaged web file with the repository source of truth, both ways.
$repoWeb = Join-Path $RepoRoot 'web'
$pkgWeb = Join-Path $packageRoot 'web'
function Get-RelativeFileMap([string]$root) {
    $map = @{}
    if (-not (Test-Path -LiteralPath $root)) { return $map }
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
        $relative = $file.FullName.Substring($root.Length).TrimStart('\', '/')
        $map[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    return $map
}
if (Test-Path -LiteralPath $repoWeb) {
    $sourceMap = Get-RelativeFileMap $repoWeb
    $packageMap = Get-RelativeFileMap $pkgWeb
    $all = @($sourceMap.Keys + $packageMap.Keys | Sort-Object -Unique)
    foreach ($relative in $all) {
        $expected = $sourceMap[$relative]
        $actual = $packageMap[$relative]
        if ($null -eq $expected -or $null -eq $actual -or $expected -ne $actual) {
            $report.webMismatches += [ordered]@{
                path = $relative
                expected = $expected
                actual = $actual
            }
        }
    }
}

$exe = Join-Path $packageRoot 'Augit.exe'
if (Test-Path -LiteralPath $exe) {
    $info = (Get-Item -LiteralPath $exe).VersionInfo
    $report.versionChecks.exeProductVersion = $info.ProductVersion
    $report.versionChecks.exeFileVersion = $info.FileVersion
    $report.versionChecks.productName = $info.ProductName
    foreach ($name in 'Augit.dll', 'Augit.Core.dll', 'Augit.Infrastructure.dll') {
        $path = Join-Path $packageRoot $name
        if (Test-Path -LiteralPath $path) {
            $report.versionChecks[$name] = (Get-Item -LiteralPath $path).VersionInfo.ProductVersion
        }
    }
    if ($ExpectedVersion -ne '' -and $info.ProductVersion -notlike "$ExpectedVersion*") {
        throw "product version does not start with $ExpectedVersion : $($info.ProductVersion)"
    }
}

if (-not $SkipLaunch) {
    Add-Type -AssemblyName System.Drawing
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class AugitPortableWin {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")] public static extern bool GetWindowRectStruct(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool fAttach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    public static IntPtr MainFor(uint target, int minW, int minH) {
        IntPtr best = IntPtr.Zero; int bestArea = 0;
        EnumWindows(delegate(IntPtr h, IntPtr p) {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == target && IsWindowVisible(h)) {
                RECT rc;
                if (GetWindowRectStruct(h, out rc)) {
                    int w = rc.R - rc.L, ht = rc.B - rc.T;
                    if (w >= minW && ht >= minH && w * ht > bestArea) { best = h; bestArea = w * ht; }
                }
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }
    public static string ClassOf(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
}
'@
    [void][AugitPortableWin]::SetProcessDPIAware()
    try { [void][AugitPortableWin]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }

    $workspace = Join-Path $OutDir 'workspace'
    if (Test-Path -LiteralPath $workspace) { Remove-Item -LiteralPath $workspace -Recurse -Force }
    New-Item -ItemType Directory -Path $workspace -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $workspace 'README.md'), "# portable smoke`n`n- one`n- two`n")
    [System.IO.File]::WriteAllText((Join-Path $workspace 'sample.txt'), "alpha`nbeta`ngamma`n")
    Push-Location $workspace
    try {
        [void](Invoke-FixtureGit @('init', '-q'))
        [void](Invoke-FixtureGit @('config', 'user.email', 'smoke@example.invalid'))
        [void](Invoke-FixtureGit @('config', 'user.name', 'Smoke'))
        [void](Invoke-FixtureGit @('add', '-A'))
        [void](Invoke-FixtureGit @('commit', '-q', '-m', 'smoke: initial'))
        [System.IO.File]::AppendAllText((Join-Path $workspace 'sample.txt'), "delta`n")
    } finally {
        Pop-Location
    }
    $settingsPath = Join-Path $OutDir 'settings.json'
    if (Test-Path -LiteralPath $settingsPath) { Remove-Item -LiteralPath $settingsPath -Force }

    $arguments = @('--workspace', ('"{0}"' -f $workspace), '--settings-file', ('"{0}"' -f $settingsPath))
    $startedAt = Get-Date
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $packageRoot -PassThru
    $handle = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(45)
    try {
        while ((Get-Date) -lt $deadline -and $handle -eq [IntPtr]::Zero) {
            try { $process.Refresh() } catch { }
            try { if ($process.MainWindowHandle -ne 0) { $handle = $process.MainWindowHandle } } catch { }
            if ($handle -eq [IntPtr]::Zero) {
                $candidate = [AugitPortableWin]::MainFor([uint32]$process.Id, 400, 300)
                if ($candidate -ne [IntPtr]::Zero) { $handle = $candidate }
            }
            if ($handle -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
        }
        $report.launch.pid = $process.Id
        $report.launch.windowAppearedMs = [math]::Round(((Get-Date) - $startedAt).TotalMilliseconds, 1)
        if ($handle -eq [IntPtr]::Zero) { throw 'main window did not appear within 45s' }
        $report.launch.windowClass = [AugitPortableWin]::ClassOf($handle)

        # Raise and capture real on-screen pixels after the page settles.
        $fgPid = [uint32]0
        $fgThread = [AugitPortableWin]::GetWindowThreadProcessId([AugitPortableWin]::GetForegroundWindow(), [ref]$fgPid)
        $self = [AugitPortableWin]::GetCurrentThreadId()
        $attached = $false
        if ($fgThread -ne 0 -and $fgThread -ne $self) { $attached = [AugitPortableWin]::AttachThreadInput($self, $fgThread, $true) }
        [void][AugitPortableWin]::BringWindowToTop($handle)
        [void][AugitPortableWin]::SetForegroundWindow($handle)
        if ($attached) { [void][AugitPortableWin]::AttachThreadInput($self, $fgThread, $false) }
        Start-Sleep -Milliseconds 2500

        $rect = New-Object AugitPortableWin+RECT
        if (-not [AugitPortableWin]::GetWindowRectStruct($handle, [ref]$rect)) { throw 'GetWindowRect failed' }
        $width = $rect.R - $rect.L
        $height = $rect.B - $rect.T
        $report.launch.windowSize = "${width}x${height}"
        $report.launch.foreground = ([AugitPortableWin]::GetForegroundWindow() -eq $handle)
        $screenshot = Join-Path $OutDir 'portable-launch.png'
        $bitmap = New-Object System.Drawing.Bitmap($width, $height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($rect.L, $rect.T, 0, 0, (New-Object System.Drawing.Size($width, $height)))
        $graphics.Dispose()
        $bitmap.Save($screenshot, [System.Drawing.Imaging.ImageFormat]::Png)
        $white = 0
        $dark = 0
        $total = 0
        for ($y = [int]($height * 0.15); $y -lt [int]($height * 0.85); $y += 10) {
            for ($x = [int]($width * 0.15); $x -lt [int]($width * 0.85); $x += 10) {
                $color = $bitmap.GetPixel($x, $y)
                $total++
                if ($color.R -gt 235 -and $color.G -gt 235 -and $color.B -gt 235) { $white++ }
                if ($color.R -lt 128 -and $color.G -lt 128 -and $color.B -lt 128) { $dark++ }
            }
        }
        $bitmap.Dispose()
        $report.launch.screenshot = $screenshot
        $report.launch.screenshotSha256 = (Get-FileHash -LiteralPath $screenshot -Algorithm SHA256).Hash.ToLowerInvariant()
        # A light theme is legitimately white-dominated; only a blank frame is a failure.
        $report.launch.sampledWhitePercent = if ($total -gt 0) { [math]::Round(100.0 * $white / $total, 1) } else { 100 }
        $report.launch.sampledDarkPercent = if ($total -gt 0) { [math]::Round(100.0 * $dark / $total, 2) } else { 0 }
        $report.launch.settingsCreated = (Test-Path -LiteralPath $settingsPath)
    } finally {
        if ($process -and -not $process.HasExited) {
            $closeAt = Get-Date
            try { [void]$process.CloseMainWindow() } catch { }
            if (-not $process.WaitForExit(15000)) {
                $report.cleanup.forcedKill = $true
                try { $process.Kill() } catch { }
                [void]$process.WaitForExit(10000)
            }
            $report.cleanup.closeMs = [math]::Round(((Get-Date) - $closeAt).TotalMilliseconds, 1)
        }
        if ($process) {
            $report.cleanup.exitCode = $process.ExitCode
            $process.Dispose()
        }
    }

    # Confirm no descendant of this instance survives (WebView2 tree included).
    $left = @()
    if ($report.launch.pid) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $left = @(Get-CimInstance Win32_Process | Where-Object {
                    $_.ParentProcessId -eq $report.launch.pid -or $_.ProcessId -eq $report.launch.pid
                } | Select-Object ProcessId, Name)
            if ($left.Count -eq 0) { break }
            Start-Sleep -Milliseconds 250
        } while ((Get-Date) -lt $deadline)
    }
    $report.cleanup.descendantsLeft = @($left)
    $report.launch.workspaceGitStatus = (@(Invoke-FixtureGit @('-C', $workspace, 'status', '--porcelain')) -join "`n")
}

$report.finishedAt = (Get-Date).ToString('o')
$failures = @()
if ($report.missingFiles.Count -gt 0) { $failures += "missing files: $($report.missingFiles -join ', ')" }
if ($report.webMismatches.Count -gt 0) { $failures += "web tree differs from source: $($report.webMismatches.Count) file(s)" }
if (-not $SkipLaunch) {
    if ($report.launch.windowClass -ne 'Augit.Shell.Window') { $failures += "unexpected window class: $($report.launch.windowClass)" }
    if (-not $report.launch.foreground) { $failures += 'window was not foreground; capture is not on-screen evidence' }
    if ($report.launch.sampledWhitePercent -ge 99.5) { $failures += "captured window is blank white: $($report.launch.sampledWhitePercent)%" }
    if ($report.launch.sampledDarkPercent -lt 0.05) { $failures += "captured window has almost no rendered content: dark $($report.launch.sampledDarkPercent)%" }
    if ($report.cleanup.descendantsLeft.Count -gt 0) { $failures += "processes left after close: $($report.cleanup.descendantsLeft.Count)" }
    if ($report.cleanup.exitCode -ne 0) { $failures += "exit code: $($report.cleanup.exitCode)" }
}
$report.result = if ($failures.Count -eq 0) { 'PASS' } else { 'FAIL' }
$report.failures = $failures
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$reportPath = Join-Path $OutDir 'portable-report.json'
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Output "REPORT $reportPath"
Write-Output "RESULT $($report.result)"
foreach ($failure in $failures) { Write-Output "FAIL $failure" }
if ($report.result -ne 'PASS') { exit 1 }
