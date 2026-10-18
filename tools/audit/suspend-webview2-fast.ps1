# 预热后按固定短延迟挂起已缓存的 WebView2 renderer/GPU，避免 CIM 查询错过捕获窗口。
param(
    [Parameter(Mandatory = $true)][int]$ParentProcessId,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$WindowTitle = "Settings",
    [int]$SuspendMilliseconds = 1000,
    [int]$WaitWindowSeconds = 10,
    [string]$GoFile = "",
    [string]$ReadyFile = "",
    [int]$DelayMilliseconds = 30
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class FastStall {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("ntdll.dll")] public static extern int NtSuspendProcess(IntPtr process);
    [DllImport("ntdll.dll")] public static extern int NtResumeProcess(IntPtr process);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    public static IntPtr FindWindowForProcess(uint processId, string title) {
        IntPtr result = IntPtr.Zero;
        EnumWindows((handle, state) => {
            uint owner;
            GetWindowThreadProcessId(handle, out owner);
            if (owner == processId) {
                var text = new StringBuilder(512);
                GetWindowText(handle, text, 512);
                if (text.ToString().IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0) {
                    result = handle;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@

try { [void][FastStall]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function Get-Descendants([int]$RootProcessId) {
    $all = @(Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, CommandLine)
    $result = New-Object System.Collections.ArrayList
    $queue = New-Object System.Collections.Queue
    $queue.Enqueue($RootProcessId)
    while ($queue.Count -gt 0) {
        $parent = [int]$queue.Dequeue()
        foreach ($process in $all | Where-Object { $_.ParentProcessId -eq $parent }) {
            [void]$result.Add($process)
            $queue.Enqueue([int]$process.ProcessId)
        }
    }
    return $result
}

# 在点击设置前缓存本实例的 renderer/GPU；避免窗口出现后再查 CIM 而错过捕获窗口。
$cached = @(Get-Descendants $ParentProcessId | Where-Object {
    $_.Name -eq 'msedgewebview2.exe' -and $_.CommandLine -match '--type=(renderer|gpu-process)'
} | ForEach-Object {
    [pscustomobject]@{
        ProcessId = [int]$_.ProcessId
        Type = if ($_.CommandLine -match '--type=([a-z-]+)') { $Matches[1] } else { 'unknown' }
        Handle = (Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue).Handle
    }
} | Where-Object { $_.Handle -and $_.Handle -ne [IntPtr]::Zero })
if ($cached.Count -eq 0) { throw '预热时没有找到 WebView2 renderer/GPU' }
if ($ReadyFile) { Set-Content -LiteralPath $ReadyFile -Value 'ready' -Encoding ascii }

if ($GoFile) {
    $deadline = [DateTime]::UtcNow.AddSeconds($WaitWindowSeconds)
    while (-not (Test-Path -LiteralPath $GoFile)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw '等待 go 信号超时' }
        Start-Sleep -Milliseconds 5
    }
}
$startedAt = [DateTime]::UtcNow
$windowHandle = [FastStall]::FindWindowForProcess([uint32]$ParentProcessId, $WindowTitle)
for ($attempt = 0; $attempt -lt 250 -and $windowHandle -eq [IntPtr]::Zero; $attempt++) {
    Start-Sleep -Milliseconds 20
    $windowHandle = [FastStall]::FindWindowForProcess([uint32]$ParentProcessId, $WindowTitle)
}
if ($windowHandle -eq [IntPtr]::Zero) { throw '等待设置窗口超时' }
$visibleAt = [DateTime]::UtcNow
if ($DelayMilliseconds -gt 0) { Start-Sleep -Milliseconds $DelayMilliseconds }

$suspended = @()
foreach ($item in $cached) {
    $process = Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue
    if (-not $process) { continue }
    $status = [FastStall]::NtSuspendProcess($process.Handle)
    $suspended += [pscustomobject]@{ ProcessId = $item.ProcessId; Type = $item.Type; SuspendStatus = $status }
}
if ($suspended.Count -eq 0) { throw '没有可挂起的缓存进程' }
$suspendedAt = [DateTime]::UtcNow
Start-Sleep -Milliseconds ([Math]::Max(0, $SuspendMilliseconds - 200))

function Capture-Window([int64]$Handle, [string]$Path) {
    $rect = New-Object FastStall+RECT
    if (-not [FastStall]::GetWindowRect([IntPtr]$Handle, [ref]$rect)) { throw '读取设置窗口矩形失败' }
    $width = $rect.R - $rect.L
    $height = $rect.B - $rect.T
    if ($width -le 0 -or $height -le 0) { throw '设置窗口矩形无效' }
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.L, $rect.T, 0, 0, $bitmap.Size)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
    return [pscustomobject]@{ Left = $rect.L; Top = $rect.T; Width = $width; Height = $height }
}

$stalledCapture = Join-Path $OutputDirectory 'stalled-settings.png'
$stalledRect = Capture-Window $windowHandle $stalledCapture
Start-Sleep -Milliseconds 200

foreach ($item in $suspended) {
    $process = Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue
    if ($process) { [void][FastStall]::NtResumeProcess($process.Handle) }
}
$resumedAt = [DateTime]::UtcNow
Start-Sleep -Milliseconds 700
$resumedCapture = Join-Path $OutputDirectory 'resumed-settings.png'
try { $resumedRect = Capture-Window $windowHandle $resumedCapture } catch { $resumedRect = $null }

$result = [pscustomobject]@{
    parentProcessId = $ParentProcessId
    windowHandle = [int64]$windowHandle
    startedAtUtc = $startedAt.ToString('o')
    windowVisibleAtUtc = $visibleAt.ToString('o')
    suspendedAtUtc = $suspendedAt.ToString('o')
    resumedAtUtc = $resumedAt.ToString('o')
    delayMilliseconds = $DelayMilliseconds
    plannedSuspendMilliseconds = $SuspendMilliseconds
    cached = $cached
    suspended = $suspended
    stalledCapture = $stalledCapture
    stalledRect = $stalledRect
    resumedCapture = $resumedCapture
    resumedRect = $resumedRect
    limitation = '只挂起本实例缓存的 renderer/GPU；延迟由 go 信号后窗口检测控制，仍可能有毫秒级抖动。'
}
$json = $result | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'suspend-result.json'), $json + "`n", [Text.UTF8Encoding]::new($false))
