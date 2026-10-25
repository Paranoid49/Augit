param(
    [int]$Runs = 3,
    [string]$OutDir = 'D:\github\Augit\artifacts\settings-cold-open-2026-10-24',
    [string]$Workspace = 'D:\github\Augit\.tmp\refresh-onset-build\workspace',
    [string]$SettingsSource = (Join-Path $env:LOCALAPPDATA 'Augit\settings.json')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Drawing;
using System.Runtime.InteropServices;
public static class SettingsCold {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
  public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
  public const byte VK_ESCAPE = 0x1B, VK_TAB = 0x09;
  public const uint KEYUP = 0x0002, WM_CLOSE = 0x0010;
}
'@
try { [void][SettingsCold]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }

function Get-ClientOrigin([IntPtr]$h) {
    $pt = New-Object SettingsCold+POINT
    [void][SettingsCold]::ClientToScreen($h, [ref]$pt)
    return @($pt.X, $pt.Y)
}

function Set-TopmostForeground([IntPtr]$h) {
    [void][SettingsCold]::ShowWindow($h, 5)
    [void][SettingsCold]::BringWindowToTop($h)
    [void][SettingsCold]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0003)
    $fg = [SettingsCold]::GetForegroundWindow()
    $fgThread = 0
    if ($fg -ne [IntPtr]::Zero) {
        $fgPid = 0
        try { $fgThread = [SettingsCold]::GetWindowThreadProcessId($fg, [ref]$fgPid) } catch { $fgThread = 0 }
    }
    $self = [SettingsCold]::GetCurrentThreadId()
    if ($fgThread -ne 0 -and $fgThread -ne $self) { [void][SettingsCold]::AttachThreadInput($self, $fgThread, $true) }
    [void][SettingsCold]::SetForegroundWindow($h)
    Start-Sleep -Milliseconds 350
    if ($fgThread -ne 0 -and $fgThread -ne $self) { [void][SettingsCold]::AttachThreadInput($self, $fgThread, $false) }
}

function Click-Client([IntPtr]$h, [double]$x, [double]$y) {
    $origin = Get-ClientOrigin $h
    $dpi = [SettingsCold]::GetDpiForWindow($h)
    $scale = $dpi / 96.0
    $px = [int][Math]::Round($origin[0] + $x * $scale)
    $py = [int][Math]::Round($origin[1] + $y * $scale)
    [void][SettingsCold]::SetCursorPos($px, $py)
    Start-Sleep -Milliseconds 80
    $clickAt = [DateTime]::UtcNow
    [SettingsCold]::mouse_event([SettingsCold]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 45
    [SettingsCold]::mouse_event([SettingsCold]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
    return $clickAt
}

function Find-Window([int]$appPid, [string]$titlePart) {
    $found = [IntPtr]::Zero
    $callback = [SettingsCold+EnumProc]{
        param($h, $l)
        $p = 0
        [void][SettingsCold]::GetWindowThreadProcessId($h, [ref]$p)
        if ($p -ne $appPid -or -not [SettingsCold]::IsWindowVisible($h)) { return $true }
        $sb = New-Object System.Text.StringBuilder 512
        [void][SettingsCold]::GetWindowText($h, $sb, 512)
        if ($sb.ToString().Contains($titlePart)) { $script:foundWindow = $h; return $false }
        return $true
    }
    $script:foundWindow = [IntPtr]::Zero
    [void][SettingsCold]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:foundWindow
}

function Find-PresentedSettings([int]$appPid) {
    $script:presentedWindow = [IntPtr]::Zero
    $callback = [SettingsCold+EnumProc]{
        param($h, $l)
        $p = 0
        [void][SettingsCold]::GetWindowThreadProcessId($h, [ref]$p)
        if ($p -ne $appPid -or -not [SettingsCold]::IsWindowVisible($h)) { return $true }
        $sb = New-Object System.Text.StringBuilder 512
        [void][SettingsCold]::GetWindowText($h, $sb, 512)
        if (-not $sb.ToString().Contains('Settings')) { return $true }
        $cloaked = 0
        [void][SettingsCold]::DwmGetWindowAttribute($h, 14, [ref]$cloaked, 4)
        if ($cloaked -eq 0) { $script:presentedWindow = $h; return $false }
        return $true
    }
    [void][SettingsCold]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:presentedWindow
}

function Save-WindowPng([IntPtr]$h, [string]$path) {
    $rect = New-Object SettingsCold+RECT
    [void][SettingsCold]::GetWindowRect($h, [ref]$rect)
    $width = $rect.R - $rect.L
    $height = $rect.B - $rect.T
    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    $graphics.CopyFromScreen($rect.L, $rect.T, 0, 0, (New-Object System.Drawing.Size($width, $height)))
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bmp.Dispose()
    return @{ path = $path; width = $width; height = $height; sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash }
}

function Read-NativeTrace([int]$appPid, [string]$traceDir) {
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Date) -lt $deadline) {
        $files = @(Get-ChildItem -LiteralPath $traceDir -Filter '*.json' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
        foreach ($file in $files) {
            try {
                $json = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
                if ($json.processId -ne $appPid -or -not $json.samples) { continue }
                $byName = @{}
                foreach ($sample in $json.samples) { $byName[$sample.Stage] = $sample.Timestamp }
                $ms = { param($from, $to) if ($byName.ContainsKey($from) -and $byName.ContainsKey($to)) { [math]::Round(($byName[$to] - $byName[$from]) / 10000, 1) } else { $null } }
                $origin = [DateTimeOffset]::Parse($json.utc)
                $utcAt = {
                    param($stage)
                    if (-not $byName.ContainsKey($stage)) { return $null }
                    $origin.AddSeconds(($byName[$stage] - $json.originTimestamp) / $json.frequency).ToString('o')
                }
                return [ordered]@{
                    file = $file.FullName
                    createdToPresentedMs = & $ms 'created' 'presented'
                    captureMs = & $ms 'capture-issued' 'capture-end'
                    prepareMs = & $ms 'prepare-start' 'prepared'
                    closeStartToDestroyedMs = & $ms 'close-start' 'destroyed'
                    closeRequestToDestroyedMs = & $ms 'close-request' 'destroyed'
                    presentedUtc = & $utcAt 'presented'
                    closeStartUtc = & $utcAt 'close-start'
                    destroyedUtc = & $utcAt 'destroyed'
                    samples = $json.samples
                }
            } catch { }
        }
        Start-Sleep -Milliseconds 100
    }
    return $null
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$traceDir = Join-Path $OutDir 'native-trace'
New-Item -ItemType Directory -Force -Path $traceDir | Out-Null
$exe = 'D:\github\Augit\src\Augit.Shell\bin\Debug\net10.0-windows\win-x64\Augit.exe'
$webRoot = 'D:\github\Augit\web'
$globalHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $SettingsSource).Hash
$report = [ordered]@{ generatedAt = (Get-Date).ToString('o'); exe = $exe; exeSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $exe).Hash;
    settingsSource = $SettingsSource; settingsSourceSha256 = $globalHash; traceDirectory = $traceDir; runs = @() }
$rows = @()
try {
    for ($index = 1; $index -le $Runs; $index++) {
        $runSettings = Join-Path $OutDir ("settings-run{0}.json" -f $index)
        $settingsObject = Get-Content -LiteralPath $SettingsSource -Raw | ConvertFrom-Json
        $settingsObject.lastWorkspace = $Workspace
        $settingsObject.openFiles = @()
        $settingsObject.activeFile = $null
        [IO.File]::WriteAllText($runSettings, (($settingsObject | ConvertTo-Json -Depth 100) -replace "`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
        $env:AUGIT_SETTINGS_TRACE_DIRECTORY = $traceDir
        $process = Start-Process -FilePath $exe -ArgumentList @('--web-root', ('"{0}"' -f $webRoot), '--workspace', ('"{0}"' -f $Workspace), '--settings-file', ('"{0}"' -f $runSettings)) -WorkingDirectory 'D:\github\Augit' -PassThru
        $appPid = $process.Id
        $deadline = (Get-Date).AddSeconds(20)
        $main = [IntPtr]::Zero
        while ((Get-Date) -lt $deadline) {
            $main = (Get-Process -Id $appPid -ErrorAction SilentlyContinue).MainWindowHandle
            if ($main -ne [IntPtr]::Zero) { break }
            Start-Sleep -Milliseconds 100
        }
        if ($main -eq [IntPtr]::Zero) { throw "run $index 主窗未出现" }
        $presentedAt = $null
        $settingsHandle = [IntPtr]::Zero
        $clickAttempts = 0
        $clickAt = $null
        for ($attempt = 1; $attempt -le 3 -and $settingsHandle -eq [IntPtr]::Zero; $attempt++) {
            $clickAttempts = $attempt
            Set-TopmostForeground $main
            # The Settings button sits 124 CSS px left of the client right edge. A hard-coded
            # 1522 only works when the window is maximized at 175% DPI; restored windows are
            # narrower and the click missed every round.
            $clientRect = New-Object SettingsCold+RECT
            [void][SettingsCold]::GetClientRect($main, [ref]$clientRect)
            $dpi = [SettingsCold]::GetDpiForWindow($main)
            $settingsX = (($clientRect.R - $clientRect.L) / ($dpi / 96.0)) - 124
            $clickAt = Click-Client $main $settingsX 22
            if ($index -eq 2 -and $attempt -eq 1) { Start-Sleep -Milliseconds 15; [SettingsCold]::keybd_event([SettingsCold]::VK_TAB, 0, 0, [UIntPtr]::Zero); [SettingsCold]::keybd_event([SettingsCold]::VK_TAB, 0, [SettingsCold]::KEYUP, [UIntPtr]::Zero) }
            $pollDeadline = (Get-Date).AddSeconds(2.5)
            while ((Get-Date) -lt $pollDeadline) {
                $settingsHandle = Find-PresentedSettings $appPid
                if ($settingsHandle -ne [IntPtr]::Zero) { $presentedAt = [DateTime]::UtcNow; break }
                Start-Sleep -Milliseconds 2
            }
            if ($settingsHandle -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 300 }
        }
        $run = [ordered]@{ index = $index; pid = $appPid; clickAtUtc = if ($clickAt) { $clickAt.ToString('o') } else { $null }; clickAttempts = $clickAttempts; presentedUtc = if ($presentedAt) { $presentedAt.ToString('o') } else { $null };
            clickedToPresentedMs = if ($presentedAt -and $clickAt) { [math]::Round(($presentedAt - $clickAt).TotalMilliseconds, 1) } else { $null };
            tabBeforePresented = ($index -eq 2); settingsOpened = ($settingsHandle -ne [IntPtr]::Zero) }
        if ($settingsHandle -ne [IntPtr]::Zero) {
            $first = Save-WindowPng $settingsHandle (Join-Path $OutDir ("run{0}-presented.png" -f $index))
            Start-Sleep -Milliseconds 60
            $later = Save-WindowPng $settingsHandle (Join-Path $OutDir ("run{0}-plus60.png" -f $index))
            $run.presentedShot = $first; $run.plus60Shot = $later; $run.sameAt60Ms = ($first.sha256 -eq $later.sha256)
            $closeAt = [DateTime]::UtcNow
            $run.closeRequestedUtc = $closeAt.ToString('o')
            $closePath = if ($index -eq 1) { 'escape' } elseif ($index -eq 2) { 'cancel' } else { 'title-wm-close' }
            if ($closePath -eq 'escape') {
                Set-TopmostForeground $settingsHandle
                # 先把焦点真实点进设置内容，再发 Esc；只有 active 窗口才会收到键盘输入。
                [void](Click-Client $settingsHandle 600 400)
                Start-Sleep -Milliseconds 80
                [SettingsCold]::keybd_event([SettingsCold]::VK_ESCAPE, 0, 0, [UIntPtr]::Zero)
                Start-Sleep -Milliseconds 30
                [SettingsCold]::keybd_event([SettingsCold]::VK_ESCAPE, 0, [SettingsCold]::KEYUP, [UIntPtr]::Zero)
            } elseif ($closePath -eq 'cancel') {
                [void](Click-Client $settingsHandle 900 719)
            } else {
                [void][SettingsCold]::PostMessage($settingsHandle, [SettingsCold]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero)
            }
            $gone = $false
            $goneDeadline = (Get-Date).AddSeconds(1)
            while ((Get-Date) -lt $goneDeadline) {
                if (-not (Find-PresentedSettings $appPid) -and -not (Find-Window $appPid 'Settings')) { $gone = $true; break }
                Start-Sleep -Milliseconds 5
            }
            $run.closePath = $closePath
            $run.settingsWindowGonePoll = $gone
        }
        $run.nativeTrace = Read-NativeTrace $appPid $traceDir
        if ($run.nativeTrace -and $run.nativeTrace.destroyedUtc) {
            $destroyed = [DateTimeOffset]::Parse($run.nativeTrace.destroyedUtc)
            if ($run.closeRequestedUtc) {
                $requested = [DateTimeOffset]::Parse($run.closeRequestedUtc)
                $run.closeMs = [math]::Round(($destroyed - $requested).TotalMilliseconds, 1)
                $run.closed = $run.closeMs -ge 0
            }
            if ($run.clickAtUtc -and $run.nativeTrace.presentedUtc) {
                $run.clickedToPresentedNativeMs = [math]::Round(([DateTimeOffset]::Parse($run.nativeTrace.presentedUtc) - [DateTimeOffset]::Parse($run.clickAtUtc)).TotalMilliseconds, 1)
            }
        }
        [void][SettingsCold]::PostMessage($main, [SettingsCold]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero)
        $exitDeadline = (Get-Date).AddSeconds(20)
        while (-not $process.HasExited -and (Get-Date) -lt $exitDeadline) { Start-Sleep -Milliseconds 50 }
        $run.mainExited = $process.HasExited
        $report.runs += $run
        $rows += [pscustomobject]@{ run = $index; pid = $appPid; opened = $run.settingsOpened; clickToPresented = $run.clickedToPresentedMs; createdToPresented = $run.nativeTrace.createdToPresentedMs; captureMs = $run.nativeTrace.captureMs; closePath = $run.closePath; closeMs = $run.closeMs; sameAt60 = $run.sameAt60Ms; closed = $run.closed; mainExited = $run.mainExited }
    }
} finally {
    Remove-Item Env:AUGIT_SETTINGS_TRACE_DIRECTORY -ErrorAction SilentlyContinue
    if ($process -and -not $process.HasExited) { [void][SettingsCold]::PostMessage($process.MainWindowHandle, [SettingsCold]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero); $process.WaitForExit(10000) | Out-Null }
    $report.globalSettingsAfterSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $SettingsSource).Hash
    [IO.File]::WriteAllText((Join-Path $OutDir 'settings-cold-open.json'), (($report | ConvertTo-Json -Depth 12) + "`n"), [Text.UTF8Encoding]::new($false))
}
$rows | Format-Table -AutoSize
