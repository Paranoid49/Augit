# 只读采集屏幕合成像素；不激活、移动或操作窗口，不使用PrintWindow。
param(
    [long]$WindowHandle = 0,
    [long]$OwnerWindowHandle = 0,
    [Parameter(Mandatory=$true)][int]$ExpectedProcessId,
    [Parameter(Mandatory=$true)][string]$Output,
    [ValidateRange(0,256)][int]$Padding = 64,
    [string]$RegionFrom = '',
    [ValidateSet('Active','Inactive')][string]$ExpectedActivation = 'Active',
    [long]$ExpectedForegroundWindow = 0,
    [int]$ExpectedForegroundProcessId = 0,
    [ValidateRange(-1,512)][int]$PointerClearance = -1
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class AuditScreenRead {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect rect, int size);
}
'@
function Test-PointerClearance([int[]]$Region, [int]$X, [int]$Y, [int]$Margin) {
    if ($Margin -lt 0) { return $true }
    return ($X -lt $Region[0]-$Margin -or $X -ge $Region[0]+$Region[2]+$Margin -or $Y -lt $Region[1]-$Margin -or $Y -ge $Region[1]+$Region[3]+$Margin)
}
function Read-OwnerGeometry {
    if ($OwnerWindowHandle -eq 0) { return $null }
    $owner = [IntPtr]$OwnerWindowHandle
    [uint32]$ownerProcess = 0
    [void][AuditScreenRead]::GetWindowThreadProcessId($owner, [ref]$ownerProcess)
    if ($ownerProcess -ne $ExpectedProcessId -or [AuditScreenRead]::GetWindow($targetWindow, 4) -ne $owner) { throw '指定所有者与实例或窗口关系不符。' }
    $rect = New-Object AuditScreenRead+Rect
    $client = New-Object AuditScreenRead+Rect
    $origin = New-Object AuditScreenRead+Point
    if (-not [AuditScreenRead]::GetWindowRect($owner, [ref]$rect) -or -not [AuditScreenRead]::GetClientRect($owner, [ref]$client) -or -not [AuditScreenRead]::ClientToScreen($owner, [ref]$origin)) { throw '读取所有者坐标失败。' }
    return [ordered]@{window=$OwnerWindowHandle; processId=$ownerProcess; windowRectPhysical=@($rect.Left,$rect.Top,$rect.Right,$rect.Bottom); clientRectPhysical=@($client.Left,$client.Top,$client.Right,$client.Bottom); clientOriginPhysical=@($origin.X,$origin.Y); dpi=[AuditScreenRead]::GetDpiForWindow($owner)}
}
$targetWindow = [IntPtr]$WindowHandle
if ($targetWindow -eq [IntPtr]::Zero) { $targetWindow = [AuditScreenRead]::GetForegroundWindow(); $WindowHandle = $targetWindow.ToInt64() }
$previousDpiContext = [AuditScreenRead]::SetThreadDpiAwarenessContext([IntPtr](-4))
$bitmap = $null
$graphics = $null
try {
    [uint32]$actualProcessId = 0
    [void][AuditScreenRead]::GetWindowThreadProcessId($targetWindow, [ref]$actualProcessId)
    if ($actualProcessId -ne $ExpectedProcessId -or $ExpectedProcessId -le 0) { throw '窗口进程与指定实例不符。' }
    $foregroundWindow = [AuditScreenRead]::GetForegroundWindow()
    [uint32]$foregroundProcessId = 0
    [void][AuditScreenRead]::GetWindowThreadProcessId($foregroundWindow, [ref]$foregroundProcessId)
    if ($ExpectedActivation -eq 'Active') {
        if ($foregroundWindow -ne $targetWindow) {
            # 只报告句柄与实例身份，区分目标失焦、旧句柄和其它进程抢焦点；门禁不放宽。
            throw ('目标不是前景窗口，拒绝采集。 targetWindow={0}; targetProcessId={1}; foregroundWindow={2}; foregroundProcessId={3}' -f $targetWindow.ToInt64(), $actualProcessId, $foregroundWindow.ToInt64(), $foregroundProcessId)
        }
    } else {
        if ($ExpectedForegroundWindow -le 0 -or $ExpectedForegroundProcessId -le 0) { throw '非活动采样必须指定已观察的前景窗口及实例。' }
        if ($foregroundWindow -eq $targetWindow -or $foregroundWindow.ToInt64() -ne $ExpectedForegroundWindow -or $foregroundProcessId -ne $ExpectedForegroundProcessId) { throw '非活动采样的前景窗口或实例不符。' }
    }
    $windowRect = New-Object AuditScreenRead+Rect
    if (-not [AuditScreenRead]::GetWindowRect($targetWindow, [ref]$windowRect)) { throw '读取窗口边界失败。' }
    $frame = New-Object AuditScreenRead+Rect
    if ([AuditScreenRead]::DwmGetWindowAttribute($targetWindow, 9, [ref]$frame, 16) -ne 0) { throw '读取DWM边界失败。' }
    $dpi = [AuditScreenRead]::GetDpiForWindow($targetWindow)
    if ($dpi -eq 0) { throw '读取窗口DPI失败。' }
    $ownerBefore = Read-OwnerGeometry
    if ($RegionFrom) {
        $prior = Get-Content -LiteralPath $RegionFrom -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($prior.capture -ne 'screen' -or $prior.method -ne 'System.Drawing.Graphics.CopyFromScreen') { throw '前图来源不是本采集器。' }
        if ($prior.dpi -ne $dpi) { throw '前后窗口DPI不一致，拒绝复用采样区域。' }
        if (@($prior.regionPhysical).Count -ne 4 -or @($prior.regionPhysical | Where-Object { $_ -isnot [int] -and $_ -isnot [long] }).Count -gt 0) { throw '前图采样区域必须为四个整数。' }
        $region = @($prior.regionPhysical | ForEach-Object { [int]$_ })
    } else {
        $region = @(($frame.Left - $Padding), ($frame.Top - $Padding), ($frame.Right - $frame.Left + 2*$Padding), ($frame.Bottom - $frame.Top + 2*$Padding))
    }
    if ($region.Count -ne 4 -or $region[2] -le 0 -or $region[3] -le 0) { throw '采样区域无效。' }
    $foregroundFrame = New-Object AuditScreenRead+Rect
    if ($ExpectedActivation -eq 'Inactive') {
        if ([AuditScreenRead]::DwmGetWindowAttribute($foregroundWindow, 9, [ref]$foregroundFrame, 16) -ne 0) { throw '读取前景窗口边界失败。' }
        $ownedForeground = ([AuditScreenRead]::GetWindow($foregroundWindow, 4) -eq $targetWindow)
        if (-not $ownedForeground -and $foregroundFrame.Left -lt $region[0]+$region[2] -and $foregroundFrame.Right -gt $region[0] -and $foregroundFrame.Top -lt $region[1]+$region[3] -and $foregroundFrame.Bottom -gt $region[1]) { throw '前景窗口覆盖采样区域，拒绝保存。' }
    }
    $desktop = @([AuditScreenRead]::GetSystemMetrics(76), [AuditScreenRead]::GetSystemMetrics(77), [AuditScreenRead]::GetSystemMetrics(78), [AuditScreenRead]::GetSystemMetrics(79))
    if ($region[0] -lt $desktop[0] -or $region[1] -lt $desktop[1] -or $region[0]+$region[2] -gt $desktop[0]+$desktop[2] -or $region[1]+$region[3] -gt $desktop[1]+$desktop[3]) { throw '采样带超出物理桌面。' }
    $path = [IO.Path]::GetFullPath($Output)
    if ((Test-Path -LiteralPath $path) -or (Test-Path -LiteralPath ($path+'.json'))) { throw '证据文件已存在，不覆盖。' }
    $bitmap = [System.Drawing.Bitmap]::new($region[2], $region[3], [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pointerBefore = New-Object AuditScreenRead+Point
    if (-not [AuditScreenRead]::GetCursorPos([ref]$pointerBefore)) { throw '读取指针位置失败。' }
    if (-not (Test-PointerClearance $region $pointerBefore.X $pointerBefore.Y $PointerClearance)) { throw '指针或其光晕可能覆盖采样区域，拒绝保存。' }
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $capturedAt = [DateTimeOffset]::UtcNow.ToString('o')
    $graphics.CopyFromScreen($region[0], $region[1], 0, 0, [System.Drawing.Size]::new($region[2], $region[3]))
    $pointerAfter = New-Object AuditScreenRead+Point
    if (-not [AuditScreenRead]::GetCursorPos([ref]$pointerAfter)) { throw '读取采样后指针位置失败。' }
    if (-not (Test-PointerClearance $region $pointerAfter.X $pointerAfter.Y $PointerClearance)) { throw '采样期间指针进入保护区域，拒绝保存。' }
    if ([AuditScreenRead]::GetForegroundWindow() -ne $foregroundWindow) { throw '采样期间前景发生变化，拒绝保存。' }
    if ($ExpectedActivation -eq 'Inactive') {
        [uint32]$foregroundProcessAfter = 0
        [void][AuditScreenRead]::GetWindowThreadProcessId($foregroundWindow, [ref]$foregroundProcessAfter)
        $foregroundAfter = New-Object AuditScreenRead+Rect
        if ($foregroundProcessAfter -ne $foregroundProcessId -or [AuditScreenRead]::DwmGetWindowAttribute($foregroundWindow, 9, [ref]$foregroundAfter, 16) -ne 0 -or $foregroundAfter.Left -ne $foregroundFrame.Left -or $foregroundAfter.Top -ne $foregroundFrame.Top -or $foregroundAfter.Right -ne $foregroundFrame.Right -or $foregroundAfter.Bottom -ne $foregroundFrame.Bottom) { throw '采样期间前景实例或边界变化，拒绝保存。' }
    }
    [uint32]$processAfter = 0
    [void][AuditScreenRead]::GetWindowThreadProcessId($targetWindow, [ref]$processAfter)
    $frameAfter = New-Object AuditScreenRead+Rect
    if ($processAfter -ne $ExpectedProcessId -or [AuditScreenRead]::GetDpiForWindow($targetWindow) -ne $dpi) { throw '采样期间进程或DPI发生变化，拒绝保存。' }
    if ([AuditScreenRead]::DwmGetWindowAttribute($targetWindow, 9, [ref]$frameAfter, 16) -ne 0 -or $frameAfter.Left -ne $frame.Left -or $frameAfter.Top -ne $frame.Top -or $frameAfter.Right -ne $frame.Right -or $frameAfter.Bottom -ne $frame.Bottom) { throw '采样期间窗口边界变化，拒绝保存。' }
    $ownerAfter = Read-OwnerGeometry
    if (($ownerBefore | ConvertTo-Json -Compress) -cne ($ownerAfter | ConvertTo-Json -Compress)) { throw '采样期间所有者身份或客户区坐标变化。' }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $hashProvider = [Security.Cryptography.SHA256]::Create()
    $hashStream = [IO.File]::OpenRead($path)
    try { $digest = [BitConverter]::ToString($hashProvider.ComputeHash($hashStream)).Replace('-', '').ToLowerInvariant() }
    finally { $hashStream.Dispose(); $hashProvider.Dispose() }
    $source = [ordered]@{
        capture='screen'; method='System.Drawing.Graphics.CopyFromScreen'; resampled=$false
        capturedAtUtc=$capturedAt; processId=$actualProcessId; windowHandle=$WindowHandle
        dpi=$dpi; scale=$dpi/96.0; regionPhysical=$region; imageSize=@($region[2],$region[3])
        windowRectPhysical=@($windowRect.Left,$windowRect.Top,$windowRect.Right,$windowRect.Bottom)
        frameRectPhysical=@($frame.Left,$frame.Top,$frame.Right,$frame.Bottom)
        frameRectInImage=@(($frame.Left-$region[0]),($frame.Top-$region[1]),($frame.Right-$region[0]),($frame.Bottom-$region[1]))
        foregroundBeforeAndAfter=($ExpectedActivation -eq 'Active'); unoccluded=$null
        activationState=$ExpectedActivation.ToLowerInvariant(); stableActivation=$true
        foregroundWindow=$foregroundWindow.ToInt64(); foregroundProcessId=$foregroundProcessId
        stableProcessFrameAndDpi=$true
        ownerGeometry=$ownerBefore; stableOwnerGeometry=($OwnerWindowHandle -ne 0)
        pointerBeforePhysical=@($pointerBefore.X,$pointerBefore.Y); pointerAfterPhysical=@($pointerAfter.X,$pointerAfter.Y)
        pointerClearancePhysical=$PointerClearance
        contentEvidence='待人工核对；活动状态和前景边界检查不排除系统阴影、置顶窗口或通知遮挡。'
        sha256=$digest
    }
    $json = ($source | ConvertTo-Json -Depth 6).Replace([string][char]13, '')+[char]10
    [IO.File]::WriteAllText($path+'.json', $json, [Text.UTF8Encoding]::new($false))
    Write-Output $json
} finally {
    if ($graphics) { $graphics.Dispose() }
    if ($bitmap) { $bitmap.Dispose() }
    [void][AuditScreenRead]::SetThreadDpiAwarenessContext($previousDpiContext)
}
