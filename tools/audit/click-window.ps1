# Click inside a window's client area at logical (CSS) pixel coordinates and optionally capture.
# Coordinates are relative to the client area origin, matching what the web layer lays out.
param(
  [Parameter(Mandatory=$true)][int]$X,
  [Parameter(Mandatory=$true)][int]$Y,
  [string]$ProcessName = "Augit.Shell",
  [string]$Out = "",
  [int]$AfterMs = 1500
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Clicker {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
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
$proc = Get-Process -Name $ProcessName -ErrorAction Stop | Select-Object -First 1
$h = $proc.MainWindowHandle
if ($h -eq 0) { Write-Output "NO_WINDOW"; exit 2 }
[void][Clicker]::ShowWindow($h, 5)
[void][Clicker]::BringWindowToTop($h)
# Windows 会拒绝非前台进程直接抢焦点；把本线程输入队列接到当前前台线程后即可成功。
$fgBefore = [Clicker]::GetForegroundWindow()
$targetPid = 0; $fgPid = 0
$targetThread = [Clicker]::GetWindowThreadProcessId($h, [ref]$targetPid)
$fgThread = [Clicker]::GetWindowThreadProcessId($fgBefore, [ref]$fgPid)
$self = [Clicker]::GetCurrentThreadId()
$attached = $false
if ($fgThread -ne 0 -and $fgThread -ne $self) { $attached = [Clicker]::AttachThreadInput($self, $fgThread, $true) }
[void][Clicker]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 400
if ($attached) { [void][Clicker]::AttachThreadInput($self, $fgThread, $false) }
Start-Sleep -Milliseconds 500
$fg = [Clicker]::GetForegroundWindow()
Write-Output ("foreground_match=" + ($fg -eq $h) + " fg=0x" + $fg.ToString("X") + " target=0x" + $h.ToString("X"))
$client = New-Object Clicker+POINT
[void][Clicker]::ClientToScreen($h, [ref]$client)
$dpi = [Clicker]::GetDpiForWindow($h)
$scale = if ($dpi -gt 0) { $dpi / 96.0 } else { 1.0 }
$px = [int][Math]::Round($client.X + $X * $scale)
$py = [int][Math]::Round($client.Y + $Y * $scale)
Write-Output "click logical=($X,$Y) dpi=$dpi scale=$scale screen=($px,$py)"
[void][Clicker]::SetCursorPos($px, $py)
Start-Sleep -Milliseconds 400
$under = New-Object Clicker+POINT
[void][Clicker]::GetCursorPos([ref]$under)
$hit = [Clicker]::WindowFromPoint($under)
Write-Output ("cursor=(" + $under.X + "," + $under.Y + ") windowFromPoint=0x" + $hit.ToString("X") + " match=" + ($hit -eq $h))
[Clicker]::mouse_event([Clicker]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 60
[Clicker]::mouse_event([Clicker]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
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
