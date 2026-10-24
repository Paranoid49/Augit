# Click inside a window's client area at logical (CSS) pixel coordinates and optionally capture.
# Coordinates are relative to the client area origin, matching what the web layer lays out.
param(
  [int]$X = 0,
  [int]$Y = 0,
  [string]$ProcessName = "Augit",
  [string]$Out = "",
  [int]$AfterMs = 1500,
  [int64]$WindowHandle = 0,
  [int]$Clicks = 1,
  [int]$GapMs = 80,
  [switch]$NoForeground,
  # Use physical screen coordinates directly (caller calibrates with window.screenX + CSS * dpr).
  [switch]$Absolute,
  [int]$ScreenX = 0,
  [int]$ScreenY = 0
)
$ErrorActionPreference = "Stop"
if (-not $Absolute -and (-not $PSBoundParameters.ContainsKey('X') -or -not $PSBoundParameters.ContainsKey('Y'))) {
  throw "X and Y are required unless -Absolute is used."
}
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Clicker {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  // The PowerShell host may already be system-DPI aware; the process call then fails,
  // but the thread awareness can still be switched.
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll", EntryPoint="GetWindowRect")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool fAttach);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public const uint LEFTDOWN = 0x0002;
  public const uint LEFTUP = 0x0004;
}
'@
try { [void][Clicker]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
try { [void][Clicker]::SetThreadDpiAwarenessContext([IntPtr](-4)) } catch { }
$proc = Get-Process -Name $ProcessName -ErrorAction Stop | Select-Object -First 1
$h = if ($WindowHandle -ne 0) { [IntPtr]$WindowHandle } else { $proc.MainWindowHandle }
if ($h -eq 0) { Write-Output "NO_WINDOW"; exit 2 }
[void][Clicker]::ShowWindow($h, 5)
[void][Clicker]::BringWindowToTop($h)
if ($NoForeground) {
  # The caller already made the target topmost; the real click itself activates it.
  Write-Output "foreground_match=SKIPPED"
} else {
  # Windows refuses focus changes from a background process; attach this thread input queue
  # to the current foreground thread so SetForegroundWindow succeeds.
  $fgBefore = [Clicker]::GetForegroundWindow()
  $fgPid = 0
  $fgThread = 0
  if ($null -ne $fgBefore -and $fgBefore -ne [IntPtr]::Zero) {
    try { $fgThread = [Clicker]::GetWindowThreadProcessId($fgBefore, [ref]$fgPid) } catch { $fgThread = 0 }
  }
  $self = [Clicker]::GetCurrentThreadId()
  $attached = $false
  if ($fgThread -ne 0 -and $fgThread -ne $self) { $attached = [Clicker]::AttachThreadInput($self, $fgThread, $true) }
  [void][Clicker]::SetForegroundWindow($h)
  Start-Sleep -Milliseconds 400
  if ($attached) { [void][Clicker]::AttachThreadInput($self, $fgThread, $false) }
  Start-Sleep -Milliseconds 500
  $fg = [Clicker]::GetForegroundWindow()
  Write-Output ("foreground_match=" + ($fg -eq $h) + " fg=0x" + $fg.ToString("X") + " target=0x" + $h.ToString("X"))
}
$client = New-Object Clicker+POINT
[void][Clicker]::ClientToScreen($h, [ref]$client)
$dpi = [Clicker]::GetDpiForWindow($h)
$scale = if ($dpi -gt 0) { $dpi / 96.0 } else { 1.0 }
if ($Absolute) {
  # The caller already converted window.screenX/screenY, CSS coordinates and devicePixelRatio
  # to physical pixels; do not add ClientToScreen/DPI again.
  $px = $ScreenX
  $py = $ScreenY
  Write-Output "click absolute=($px,$py) dpi=$dpi"
} else {
  $px = [int][Math]::Round($client.X + $X * $scale)
  $py = [int][Math]::Round($client.Y + $Y * $scale)
  Write-Output "click logical=($X,$Y) dpi=$dpi scale=$scale client=($($client.X),$($client.Y)) screen=($px,$py)"
}
[void][Clicker]::SetCursorPos($px, $py)
Start-Sleep -Milliseconds 400
$under = New-Object Clicker+POINT
[void][Clicker]::GetCursorPos([ref]$under)
$hit = [Clicker]::WindowFromPoint($under)
Write-Output ("cursor=(" + $under.X + "," + $under.Y + ") windowFromPoint=0x" + $hit.ToString("X") + " match=" + ($hit -eq $h))
for ($i = 0; $i -lt [Math]::Max(1, $Clicks); $i++) {
  if ($i -eq 0) { Write-Output ("click_utc=" + [DateTime]::UtcNow.ToString('o')) }
  [Clicker]::mouse_event([Clicker]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 60
  [Clicker]::mouse_event([Clicker]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
  if ($i + 1 -lt $Clicks) { Start-Sleep -Milliseconds $GapMs }
}
Start-Sleep -Milliseconds $AfterMs
if ($Out -ne "") {
  $rect = New-Object Clicker+RECT
  [void][Clicker]::GetWindowRect($h, [ref]$rect)
  $w = $rect.R - $rect.L; $ht = $rect.B - $rect.T
  $bmp = New-Object System.Drawing.Bitmap($w, $ht)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($rect.L, $rect.T, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
  $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output "SAVED $Out"
}
