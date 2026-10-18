# 按外部给出的 PID 文件挂起指定 WebView2 renderer/GPU；用于精确验证捕获超时。
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][int]$ParentProcessId,
    [Parameter(Mandatory = $true)][string]$PidFile,
    [Parameter(Mandatory = $true)][string]$GoFile,
    [string]$ReadyFile = "",
    [string]$WindowTitle = "Settings",
    [int]$SuspendMilliseconds = 1200,
    [int]$DelayMilliseconds = 30,
    [int]$WaitWindowSeconds = 10
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class PidStall {
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

try { [void][PidStall]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
if ($ReadyFile) { Set-Content -LiteralPath $ReadyFile -Value 'ready' -Encoding ascii }

$deadline = [DateTime]::UtcNow.AddSeconds($WaitWindowSeconds)
while (-not (Test-Path -LiteralPath $GoFile)) {
    if ([DateTime]::UtcNow -gt $deadline) { throw '等待 go 信号超时' }
    Start-Sleep -Milliseconds 5
}
if ($DelayMilliseconds -gt 0) { Start-Sleep -Milliseconds $DelayMilliseconds }
$targets = @(Get-Content -LiteralPath $PidFile | ForEach-Object {
    if (-not $_) { return }
    $parts = $_ -split ','
    [pscustomobject]@{ id = [int]$parts[0]; type = [string]$parts[1] }
})
$windowHandle = [PidStall]::FindWindowForProcess([uint32]$ParentProcessId, $WindowTitle)
for ($attempt = 0; $attempt -lt 200 -and $windowHandle -eq [IntPtr]::Zero; $attempt++) {
    Start-Sleep -Milliseconds 10
    $windowHandle = [PidStall]::FindWindowForProcess([uint32]$ParentProcessId, $WindowTitle)
}
if ($windowHandle -eq [IntPtr]::Zero) { throw '等待设置窗口超时' }

$suspended = @()
foreach ($target in $targets) {
    $process = Get-Process -Id ([int]$target.id) -ErrorAction SilentlyContinue
    if (-not $process) { continue }
    $status = [PidStall]::NtSuspendProcess($process.Handle)
    $suspended += [pscustomobject]@{ ProcessId = [int]$target.id; Type = $target.type; SuspendStatus = $status }
}
if ($suspended.Count -eq 0) { throw '没有可挂起的 PID' }
$suspendedAt = [DateTime]::UtcNow
Start-Sleep -Milliseconds ([Math]::Max(0, $SuspendMilliseconds - 200))

function Capture-Window([int64]$Handle, [string]$Path) {
    $rect = New-Object PidStall+RECT
    if (-not [PidStall]::GetWindowRect([IntPtr]$Handle, [ref]$rect)) { throw '读取窗口矩形失败' }
    $width = $rect.R - $rect.L
    $height = $rect.B - $rect.T
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
    if ($process) { [void][PidStall]::NtResumeProcess($process.Handle) }
}
$resumedAt = [DateTime]::UtcNow
Start-Sleep -Milliseconds 700
$resumedCapture = Join-Path $OutputDirectory 'resumed-settings.png'
try { $resumedRect = Capture-Window $windowHandle $resumedCapture } catch { $resumedRect = $null }

$result = [pscustomobject]@{
    parentProcessId = $ParentProcessId
    windowHandle = [int64]$windowHandle
    targets = $targets
    suspended = $suspended
    suspendedAtUtc = $suspendedAt.ToString('o')
    resumedAtUtc = $resumedAt.ToString('o')
    delayMilliseconds = $DelayMilliseconds
    plannedSuspendMilliseconds = $SuspendMilliseconds
    stalledCapture = $stalledCapture
    stalledRect = $stalledRect
    resumedCapture = $resumedCapture
    resumedRect = $resumedRect
    limitation = '只挂起指定 PID；延迟由 go 信号控制，仍可能有毫秒级抖动。'
}
$json = $result | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'suspend-result.json'), $json + "`n", [Text.UTF8Encoding]::new($false))
