# Verifies the real window chrome of the shell (goal 1: no duplicated window buttons).
#
# The visual mockup draws its own window buttons in the titlebar, so the native window must
# not carry a Win32 caption. With a caption both sets appear at the same corner. A caption
# always produces a non-client area, so "client area origin == window origin AND client size
# == window size" is an objective proof that no native caption buttons exist.
#
# The same run proves the self-drawn chrome is functional, using REAL mouse input
# (SetCursorPos + mouse_event): the buttons minimize/restore/close, a titlebar drag moves the
# window, a titlebar double-click maximizes it and a border drag resizes it. Button coordinates
# come from the live DOM through the Chrome DevTools Protocol, so the script does not hard-code
# layout constants.
#
# Requirements: Windows, the shell built in Release, and WebView2. Keep this file ASCII-only.
param(
  [Parameter(Mandatory=$true)][string]$Exe,
  [Parameter(Mandatory=$true)][string]$Workspace,
  [string]$OutDir = "",
  [int]$Port = 9333,
  [int]$Width = 1180,
  [int]$Height = 760,
  [int]$SettleMs = 6000
)
$ErrorActionPreference = "Stop"
if ($OutDir -eq "") { $OutDir = Join-Path $env:TEMP "augit-window-chrome" }
if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class WinChrome {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
  public static void AttachAndRaise(IntPtr h) {
    uint fgPid; uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out fgPid);
    uint self = GetCurrentThreadId();
    bool attached = fgThread != 0 && fgThread != self && AttachThreadInput(self, fgThread, true);
    BringWindowToTop(h);
    SetForegroundWindow(h);
    if (attached) AttachThreadInput(self, fgThread, false);
  }
}
'@
try { [void][WinChrome]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { [void][WinChrome]::SetProcessDPIAware() }

$failures = New-Object System.Collections.ArrayList
function Assert-Chrome([string]$label, [bool]$ok, [string]$detail) {
  if ($ok) { Write-Output "PASS $label :: $detail" } else { Write-Output "FAIL $label :: $detail"; [void]$failures.Add($label) }
}

function Get-Rect($handle, [string]$kind) {
  $r = New-Object WinChrome+RECT
  if ($kind -eq "client") { [void][WinChrome]::GetClientRect($handle, [ref]$r) } else { [void][WinChrome]::GetWindowRect($handle, [ref]$r) }
  return $r
}

# --- Chrome DevTools Protocol: evaluate JS in the live WebView2 page -------------------------
function Get-CdpTarget([int]$port) {
  $deadline = (Get-Date).AddSeconds(20)
  while ((Get-Date) -lt $deadline) {
    try {
      $targets = Invoke-RestMethod -Uri "http://127.0.0.1:$port/json/list" -TimeoutSec 3
      $page = $targets | Where-Object { $_.url -like "*index.html*" } | Select-Object -First 1
      if ($page) { return $page.webSocketDebuggerUrl }
    } catch { }
    Start-Sleep -Milliseconds 400
  }
  throw "no CDP page target on port $port"
}

function Invoke-CdpEval([string]$socketUrl, [string]$expression, [int]$id) {
  $ws = New-Object System.Net.WebSockets.ClientWebSocket
  $token = [System.Threading.CancellationToken]::None
  # Void-returning await calls emit a VoidTaskResult object into the pipeline, which would be
  # returned alongside the evaluated value; cast them away.
  [void]$ws.ConnectAsync([Uri]$socketUrl, $token).GetAwaiter().GetResult()
  $payload = @{ id = $id; method = "Runtime.evaluate"; params = @{ expression = $expression; returnByValue = $true; awaitPromise = $true } } | ConvertTo-Json -Compress -Depth 6
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
  $segment = New-Object System.ArraySegment[byte] -ArgumentList @(,$bytes)
  [void]$ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $token).GetAwaiter().GetResult()
  $buffer = New-Object byte[] 65536
  $builder = New-Object System.Text.StringBuilder
  $deadline = (Get-Date).AddSeconds(15)
  while ((Get-Date) -lt $deadline) {
    $receiveSegment = New-Object System.ArraySegment[byte] -ArgumentList @(,$buffer)
    $result = $ws.ReceiveAsync($receiveSegment, $token).GetAwaiter().GetResult()
    [void]$builder.Append([System.Text.Encoding]::UTF8.GetString($buffer, 0, $result.Count))
    if (-not $result.EndOfMessage) { continue }
    $text = $builder.ToString()
    [void]$builder.Clear()
    $message = $null
    try { $message = $text | ConvertFrom-Json } catch { continue }
    if ($message.id -eq $id) {
      [void]$ws.Dispose()
      return $message.result.result.value
    }
  }
  [void]$ws.Dispose()
  throw "CDP evaluate timed out"
}

# --- Real mouse input -----------------------------------------------------------------------
function Move-Mouse([int]$x, [int]$y) {
  [void][WinChrome]::SetCursorPos($x, $y)
  Start-Sleep -Milliseconds 40
}

function Invoke-RealClick([int]$x, [int]$y, [int]$pairs = 1) {
  Move-Mouse $x $y
  for ($i = 0; $i -lt $pairs; $i++) {
    [WinChrome]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 40
    [WinChrome]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 60
  }
}

function Invoke-RealDrag([int]$fromX, [int]$fromY, [int]$toX, [int]$toY, [int]$steps = 12) {
  Move-Mouse $fromX $fromY
  [WinChrome]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 120
  for ($i = 1; $i -le $steps; $i++) {
    $x = [int]($fromX + ($toX - $fromX) * $i / $steps)
    $y = [int]($fromY + ($toY - $fromY) * $i / $steps)
    [void][WinChrome]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 45
  }

  [WinChrome]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 250
}

function Save-Corner([IntPtr]$handle, [string]$path, [int]$w, [int]$ht) {
  # PrintWindow(PW_RENDERFULLCONTENT) is occlusion-proof and returns the composited frame,
  # so the saved evidence is the real window content even when another window is in front.
  $bmp = New-Object System.Drawing.Bitmap($w, $ht)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  [void][WinChrome]::PrintWindow($handle, $hdc, 2)
  $g.ReleaseHdc($hdc)
  # Crop the top-right corner: that is where both button sets appear when the frame is native.
  $cropW = [Math]::Min(240, $w)
  $cropH = [Math]::Min(48, $ht)
  $crop = New-Object System.Drawing.Bitmap($cropW, $cropH)
  $gc = [System.Drawing.Graphics]::FromImage($crop)
  $gc.DrawImage($bmp, (New-Object System.Drawing.Rectangle(0, 0, $cropW, $cropH)), (New-Object System.Drawing.Rectangle(($w - $cropW), 0, $cropW, $cropH)), [System.Drawing.GraphicsUnit]::Pixel)
  $crop.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $gc.Dispose(); $crop.Dispose()
  # A taller crop of the same corner: when the frame is native the native buttons sit in the
  # caption band and the self-drawn ones right below it, so only a taller crop shows both rows
  # at once and makes "two sets of buttons" visible in the evidence image.
  $tallH = [Math]::Min(110, $ht)
  $tall = New-Object System.Drawing.Bitmap($cropW, $tallH)
  $gt = [System.Drawing.Graphics]::FromImage($tall)
  $gt.DrawImage($bmp, (New-Object System.Drawing.Rectangle(0, 0, $cropW, $tallH)), (New-Object System.Drawing.Rectangle(($w - $cropW), 0, $cropW, $tallH)), [System.Drawing.GraphicsUnit]::Pixel)
  $tall.Save(($path -replace '\.png$', '-wide.png'), [System.Drawing.Imaging.ImageFormat]::Png)
  $gt.Dispose(); $tall.Dispose(); $g.Dispose(); $bmp.Dispose()
}

# --- Launch --------------------------------------------------------------------------------
Get-Process Augit -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$launchArgs = @("--workspace", $Workspace, "--pixel-exact", "--theme", "dark", "--width", "$Width", "--height", "$Height", "--browser-args", "--remote-debugging-port=$Port")
$process = Start-Process -FilePath $Exe -ArgumentList $launchArgs -PassThru
$handle = [IntPtr]::Zero
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline -and $handle -eq [IntPtr]::Zero) {
  Start-Sleep -Milliseconds 300
  try { $process.Refresh() } catch { }
  if ($process.MainWindowHandle -ne 0) { $handle = $process.MainWindowHandle }
}
if ($handle -eq [IntPtr]::Zero) { Write-Output "NO_WINDOW"; exit 2 }
Start-Sleep -Milliseconds $SettleMs

# Move into the visible screen and raise: click coordinates are screen coordinates.
$virtual = [System.Windows.Forms.SystemInformation]::VirtualScreen
[void][WinChrome]::MoveWindow($handle, $virtual.Left + 40, $virtual.Top + 40, $Width, $Height, $true)
Start-Sleep -Milliseconds 600
[WinChrome]::AttachAndRaise($handle)
Start-Sleep -Milliseconds 600

$windowRect = Get-Rect $handle "window"
$clientRect = Get-Rect $handle "client"
$origin = New-Object WinChrome+POINT
[void][WinChrome]::ClientToScreen($handle, [ref]$origin)
$clientW = $clientRect.R - $clientRect.L
$clientH = $clientRect.B - $clientRect.T
Write-Output ("INFO window=" + $windowRect.L + "," + $windowRect.T + " " + ($windowRect.R - $windowRect.L) + "x" + ($windowRect.B - $windowRect.T))
Write-Output ("INFO client=" + $origin.X + "," + $origin.Y + " " + $clientW + "x" + $clientH)

# A1: no non-client area at all -> the native caption (and its own buttons) cannot exist.
Assert-Chrome "window-has-no-native-frame" `
  (($origin.X -eq $windowRect.L) -and ($origin.Y -eq $windowRect.T) -and ($clientW -eq ($windowRect.R - $windowRect.L)) -and ($clientH -eq ($windowRect.B - $windowRect.T))) `
  "clientOrigin=$($origin.X),$($origin.Y) windowOrigin=$($windowRect.L),$($windowRect.T) client=${clientW}x${clientH} window=$($windowRect.R - $windowRect.L)x$($windowRect.B - $windowRect.T)"

# A2: the three self-drawn buttons live inside the client area, top-right.
$socket = Get-CdpTarget $Port
$dots = Invoke-CdpEval $socket "JSON.stringify(Array.from(document.querySelectorAll('.window-dot')).map(function(d){var r=d.getBoundingClientRect();return {a:d.dataset.windowAction,x:Math.round(r.x),y:Math.round(r.y),w:Math.round(r.width),h:Math.round(r.height),icon:(d.querySelector('svg')||{dataset:{}}).dataset.augitIcon};}))" 1
# ConvertFrom-Json emits a JSON array as ONE pipeline object, so wrapping it in @() would
# produce a single-element array holding the real array (the first version of this script
# did exactly that and every index lookup returned an array instead of a dot).
$dotList = $dots | ConvertFrom-Json
$actions = ($dotList | ForEach-Object { $_.a }) -join ","
$closeRight = -1
if ($dotList.Count -ge 3) {
  $lastDot = $dotList[$dotList.Count - 1]
  $closeRight = $clientW - ([int]$lastDot.x + [int]$lastDot.w)
}
Assert-Chrome "three-self-drawn-window-buttons" `
  (($dotList.Count -eq 3) -and ($actions -eq "minimize,maximize,close") -and ($dotList[0].y -ge 0) -and (($dotList[0].y + $dotList[0].h) -le 48) -and ($closeRight -ge 0) -and ($closeRight -le 24)) `
  "actions=$actions dots=$dots closeRightInset=$closeRight"

Save-Corner $handle (Join-Path $OutDir "corner.png") ($windowRect.R - $windowRect.L) ($windowRect.B - $windowRect.T)
Write-Output ("INFO corner=" + (Join-Path $OutDir "corner.png"))

# Screen coordinates of each button centre (page viewport origin == client origin).
function Dot-Centre([string]$action) {
  $dot = $dotList | Where-Object { $_.a -eq $action } | Select-Object -First 1
  return @([int]($origin.X + $dot.x + $dot.w / 2), [int]($origin.Y + $dot.y + $dot.h / 2))
}

# Every click needs the CURRENT geometry: maximizing moves the buttons (the client grows to the
# whole work area), so coordinates sampled before a state change are stale and would click the
# editor instead of the button (that mistake made three assertions fail once).
function Get-DotCentre([string]$action, [int]$cdpId) {
  $json = Invoke-CdpEval $socket "JSON.stringify(Array.from(document.querySelectorAll('.window-dot')).map(function(d){var r=d.getBoundingClientRect();return {a:d.dataset.windowAction,x:Math.round(r.x+r.width/2),y:Math.round(r.y+r.height/2)};}))" $cdpId
  $list = $json | ConvertFrom-Json
  $dot = $list | Where-Object { $_.a -eq $action } | Select-Object -First 1
  $point = New-Object WinChrome+POINT
  [void][WinChrome]::ClientToScreen($handle, [ref]$point)
  return @([int]($point.X + $dot.x), [int]($point.Y + $dot.y))
}

# A3: minimize button really minimizes; restore afterwards.
$min = Get-DotCentre "minimize" 10
Invoke-RealClick $min[0] $min[1]
Start-Sleep -Milliseconds 700
$iconic = [WinChrome]::IsIconic($handle)
Assert-Chrome "minimize-button-minimizes" $iconic "IsIconic=$iconic at $($min[0]),$($min[1])"
[void][WinChrome]::ShowWindow($handle, 9)
Start-Sleep -Milliseconds 500
[WinChrome]::AttachAndRaise($handle)
Start-Sleep -Milliseconds 400

# A4: maximize button maximizes and the icon switches to the restore glyph.
$max = Get-DotCentre "maximize" 11
Invoke-RealClick $max[0] $max[1]
Start-Sleep -Milliseconds 900
$zoomed = [WinChrome]::IsZoomed($handle)
# Index 1 is the maximize dot (order: minimize, maximize, close) - avoids nested quotes.
$iconAfterMax = Invoke-CdpEval $socket "JSON.stringify({icon:document.querySelectorAll('.window-dot')[1].querySelector('svg').dataset.augitIcon,live:!!(window.__augitLive&&window.__augitLive.windowMaximized)})" 12
Assert-Chrome "maximize-button-maximizes" $zoomed "IsZoomed=$zoomed at $($max[0]),$($max[1]) icon=$iconAfterMax"

# A5: a maximized window must fill the work area exactly - a frame-width overflow would push
# the right-hand buttons off screen (the classic frameless maximize bug).
$maxWindow = Get-Rect $handle "window"
$maxClient = Get-Rect $handle "client"
$maxOrigin = New-Object WinChrome+POINT
[void][WinChrome]::ClientToScreen($handle, [ref]$maxOrigin)
$work = [System.Windows.Forms.Screen]::FromHandle($handle).WorkingArea
$fillsWork = ($maxOrigin.X -eq $work.Left) -and ($maxOrigin.Y -eq $work.Top) `
  -and (($maxClient.R - $maxClient.L) -eq $work.Width) -and (($maxClient.B - $maxClient.T) -eq $work.Height)
Assert-Chrome "maximized-client-fills-work-area" $fillsWork `
  "clientOrigin=$($maxOrigin.X),$($maxOrigin.Y) client=$($maxClient.R - $maxClient.L)x$($maxClient.B - $maxClient.T) work=$($work.Left),$($work.Top) $($work.Width)x$($work.Height) window=$($maxWindow.L),$($maxWindow.T) $($maxWindow.R - $maxWindow.L)x$($maxWindow.B - $maxWindow.T)"
Save-Corner $handle (Join-Path $OutDir "corner-maximized.png") ($maxWindow.R - $maxWindow.L) ($maxWindow.B - $maxWindow.T)

# A6: the same button restores, and the restored size is the size from before.
$maxAgain = Get-DotCentre "maximize" 13
Invoke-RealClick $maxAgain[0] $maxAgain[1]
Start-Sleep -Milliseconds 900
$restoredRect = Get-Rect $handle "window"
$restored = -not [WinChrome]::IsZoomed($handle)
$iconAfterRestore = Invoke-CdpEval $socket "document.querySelectorAll('.window-dot')[1].querySelector('svg').dataset.augitIcon" 14
Assert-Chrome "maximize-button-restores" ($restored -and ($iconAfterRestore -eq "window-maximize") -and (($restoredRect.R - $restoredRect.L) -eq $Width)) `
  "restored=$restored icon=$iconAfterRestore size=$($restoredRect.R - $restoredRect.L)x$($restoredRect.B - $restoredRect.T) at $($maxAgain[0]),$($maxAgain[1])"

# The drag must start on a genuinely blank part of the titlebar: the titlebar also holds the
# main menu, workspace, branch and quick-open controls, and a press on those must stay with the
# control (the first version of this script pressed the centred quick-open link and the window
# correctly did not move - the script was wrong, not the app).
function Get-BlankTitlebarPoint([int]$cdpId) {
  $json = Invoke-CdpEval $socket "(()=>{const CONTROLS='.window-dot, .top-button, .top-chip, .titlebar-context, .main-menu-bar, .main-menu-entry, a, button, input, select, textarea';const bar=document.querySelector('.titlebar');const r=bar.getBoundingClientRect();for(let x=Math.round(r.width*0.15);x<r.width*0.9;x+=4){const el=document.elementFromPoint(x,Math.round(r.height/2));if(el&&bar.contains(el)&&!el.closest(CONTROLS))return JSON.stringify({x:x,y:Math.round(r.height/2),tag:el.tagName});}return 'none';})()" $cdpId
  return $json | ConvertFrom-Json
}

# A7: dragging a blank part of the titlebar moves the window (host move loop).
[WinChrome]::AttachAndRaise($handle)
Start-Sleep -Milliseconds 300
$before = Get-Rect $handle "window"
$blank = Get-BlankTitlebarPoint 20
$blankOrigin = New-Object WinChrome+POINT
[void][WinChrome]::ClientToScreen($handle, [ref]$blankOrigin)
$dragX = [int]($blankOrigin.X + $blank.x)
$dragY = [int]($blankOrigin.Y + $blank.y)
Invoke-RealDrag $dragX $dragY ($dragX + 120) ($dragY + 70)
Start-Sleep -Milliseconds 400
$after = Get-Rect $handle "window"
$movedX = $after.L - $before.L
$movedY = $after.T - $before.T
Assert-Chrome "titlebar-drag-moves-window" (($movedX -ge 90) -and ($movedY -ge 50)) "moved=$movedX,$movedY from $dragX,$dragY blank=$($blank.x),$($blank.y) tag=$($blank.tag) (window $($before.L),$($before.T) -> $($after.L),$($after.T))"

# A8: double-clicking the titlebar maximizes (the system move loop turns it into a caption
# double click), which is what users expect from a native titlebar. Start from restored.
[WinChrome]::AttachAndRaise($handle)
Start-Sleep -Milliseconds 300
$blankBefore = Get-Rect $handle "window"
$blank2 = Get-BlankTitlebarPoint 21
$blankOrigin2 = New-Object WinChrome+POINT
[void][WinChrome]::ClientToScreen($handle, [ref]$blankOrigin2)
$blankX = [int]($blankOrigin2.X + $blank2.x)
$blankY = [int]($blankOrigin2.Y + $blank2.y)
$wasZoomed = [WinChrome]::IsZoomed($handle)
Invoke-RealClick $blankX $blankY 2
Start-Sleep -Milliseconds 900
$zoomedByDoubleClick = [WinChrome]::IsZoomed($handle)
Assert-Chrome "titlebar-double-click-maximizes" ((-not $wasZoomed) -and $zoomedByDoubleClick) "wasZoomed=$wasZoomed IsZoomed=$zoomedByDoubleClick at $blankX,$blankY"
if ($zoomedByDoubleClick) {
  $restoreDot = Get-DotCentre "maximize" 15
  Invoke-RealClick $restoreDot[0] $restoreDot[1]
  Start-Sleep -Milliseconds 800
  [WinChrome]::AttachAndRaise($handle)
  Start-Sleep -Milliseconds 300
}

# A9: dragging the left border resizes the window (only meaningful while not maximized).
$resizeBefore = Get-Rect $handle "window"
$edgeX = $resizeBefore.L + 3
$edgeY = [int](($resizeBefore.T + $resizeBefore.B) / 2)
Invoke-RealDrag $edgeX $edgeY ($edgeX - 80) $edgeY
Start-Sleep -Milliseconds 400
$resizeAfter = Get-Rect $handle "window"
# Dragging the left border outwards moves the left edge left AND grows the width; asserting only
# one of the two would also pass for a window that merely slides sideways.
$grewWidth = (($resizeAfter.R - $resizeAfter.L) - ($resizeBefore.R - $resizeBefore.L)) -ge 50
$movedEdge = ($resizeBefore.L - $resizeAfter.L) -ge 50
Assert-Chrome "border-drag-resizes-window" ($grewWidth -and $movedEdge) "zoomed=$([WinChrome]::IsZoomed($handle)) left $($resizeBefore.L) -> $($resizeAfter.L) width $($resizeBefore.R - $resizeBefore.L) -> $($resizeAfter.R - $resizeAfter.L)"

# A10: the close button closes the application.
$errors = Invoke-CdpEval $socket "JSON.stringify(window.__augitErrors||[])" 16
Assert-Chrome "no-page-errors" (($errors -eq "[]") -or ($errors -eq $null)) "errors=$errors"
$close = Get-DotCentre "close" 17
Invoke-RealClick $close[0] $close[1]
Start-Sleep -Milliseconds 1500
$exited = $process.HasExited
Assert-Chrome "close-button-closes-app" $exited "HasExited=$exited at $($close[0]),$($close[1])"
if (-not $exited) { try { $process.Kill() } catch { } }

Write-Output ("SUMMARY failures=" + $failures.Count)
if ($failures.Count -gt 0) { Write-Output ("FAILED: " + ($failures -join ", ")); exit 1 }
Write-Output "WINDOW_CHROME_OK"
exit 0
