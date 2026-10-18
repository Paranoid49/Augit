# 在正常入口设置开窗时短暂挂起本实例的 WebView2 renderer/GPU，验证 500ms 捕获超时与提前揭示。
# 仅挂起本实例的子进程；结束后恢复。输出窗口截图和挂起清单，不修改产品状态。
param(
    [Parameter(Mandatory = $true)][int]$ParentProcessId,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$WindowTitle = "Settings",
    [int]$SuspendMilliseconds = 1000,
    [int]$WaitWindowSeconds = 10,
    [string]$GoFile = "",
    [string]$ReadyFile = "",
    [int]$DelayMilliseconds = 0
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class StallWin {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
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

try { [void][StallWin]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
if ($ReadyFile) { Set-Content -LiteralPath $ReadyFile -Value 'ready' -Encoding ascii }
if ($GoFile) {
    $goDeadline = [DateTime]::UtcNow.AddSeconds($WaitWindowSeconds)
    while (-not (Test-Path -LiteralPath $GoFile)) {
        if ([DateTime]::UtcNow -gt $goDeadline) { throw '等待 go 信号超时' }
        Start-Sleep -Milliseconds 5
    }
    if ($DelayMilliseconds -gt 0) { Start-Sleep -Milliseconds $DelayMilliseconds }
}

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

function Capture-Window([int64]$Handle, [string]$Path) {
    $rect = New-Object StallWin+RECT
    if (-not [StallWin]::GetWindowRect([IntPtr]$Handle, [ref]$rect)) { throw '读取设置窗口矩形失败' }
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

$startedAt = [DateTime]::UtcNow
$window = $null
for ($attempt = 0; $attempt -lt [Math]::Max(1, $WaitWindowSeconds * 50); $attempt++) {
    $handle = [StallWin]::FindWindowForProcess([uint32]$ParentProcessId, $WindowTitle)
    if ($handle -ne [IntPtr]::Zero) {
        $window = [pscustomobject]@{ Handle = [int64]$handle; Title = $WindowTitle }
        break
    }
    Start-Sleep -Milliseconds 20
}
if (-not $window) { throw '等待设置窗口超时' }
$visibleAt = [DateTime]::UtcNow
# 等过 settings-ready/prepare 后的激活窗口消息，避免在页面就绪前挂起。
Start-Sleep -Milliseconds 40

$descendants = Get-Descendants $ParentProcessId
$suspended = @()
$targets = @($descendants | Where-Object {
    $_.CommandLine -match '--type=(renderer|gpu-process)' -and $_.Name -eq 'msedgewebview2.exe'
})
foreach ($target in $targets) {
    $process = Get-Process -Id $target.ProcessId -ErrorAction SilentlyContinue
    if (-not $process) { continue }
    $status = [StallWin]::NtSuspendProcess($process.Handle)
    $suspended += [pscustomobject]@{
        ProcessId = $target.ProcessId
        Type = if ($target.CommandLine -match '--type=([a-z-]+)') { $Matches[1] } else { 'unknown' }
        SuspendStatus = $status
    }
}
if ($suspended.Count -eq 0) { throw '没有找到可挂起的 WebView2 renderer/GPU 进程' }

$suspendedAt = [DateTime]::UtcNow
Start-Sleep -Milliseconds ([Math]::Max(0, $SuspendMilliseconds - 200))
$stalledCapture = Join-Path $OutputDirectory 'stalled-settings.png'
$stalledRect = Capture-Window $window.Handle $stalledCapture
Start-Sleep -Milliseconds 200

foreach ($item in $suspended) {
    $process = Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue
    if ($process) { [void][StallWin]::NtResumeProcess($process.Handle) }
}
$resumedAt = [DateTime]::UtcNow
Start-Sleep -Milliseconds 700
$resumedCapture = Join-Path $OutputDirectory 'resumed-settings.png'
$resumedRect = $null
try { $resumedRect = Capture-Window $window.Handle $resumedCapture } catch { }

$result = [pscustomobject]@{
    parentProcessId = $ParentProcessId
    windowHandle = $window.Handle
    windowTitle = $window.Title
    startedAtUtc = $startedAt.ToString('o')
    windowVisibleAtUtc = $visibleAt.ToString('o')
    suspendedAtUtc = $suspendedAt.ToString('o')
    resumedAtUtc = $resumedAt.ToString('o')
    plannedSuspendMilliseconds = $SuspendMilliseconds
    suspended = $suspended
    stalledCapture = $stalledCapture
    stalledRect = $stalledRect
    resumedCapture = $resumedCapture
    resumedRect = $resumedRect
    limitation = '只挂起本实例 renderer/GPU，模拟真实慢捕获；不是正常性能样本，也不证明根因。'
}
$json = $result | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'suspend-result.json'), $json + "`n", [Text.UTF8Encoding]::new($false))
