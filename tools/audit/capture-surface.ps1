# Capture an Augit visual-audit scene as REAL on-screen pixels (CopyFromScreen),
# which is the evidence class the design-system doc requires for font/AA checks.
# Retries until the captured frame is not occluded (occlusion heuristic: mostly-white frame).
param(
  [Parameter(Mandatory=$true)][string]$Exe,
  [Parameter(Mandatory=$true)][string]$Out,
  # Either pass audit-host arguments explicitly, or the Settings/Workspace/Surface triple.
  [string[]]$Arguments = @(),
  [string]$Settings = "",
  [string]$Workspace = "",
  [string]$Surface = "",
  [string]$Dpi = "",
  [string]$WorkDir = "",
  [int]$TimeoutSec = 60,
  [int]$Attempts = 5,
  [int]$SettleMs = 2500,
  [double]$MaxWhitePercent = 10.0
)
$ErrorActionPreference = "Stop"
if ($WorkDir -eq "") { $WorkDir = Split-Path -Parent $Exe }
Set-Location -LiteralPath $WorkDir
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class WinScreen {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  // Per-monitor-v2 awareness must match the app manifest, otherwise GetWindowRect
  // returns DPI-virtualized rectangles and screen capture lands on the wrong pixels.
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
  // NOTE: PowerShell's [ref] only marshals the FIRST of several ref parameters, so the
  // four-ref overload silently returns zeros. Always use the struct overload here.
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll", EntryPoint = "GetWindowRect")] public static extern bool GetWindowRectStruct(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  // The main window reports a degenerate rect while it is still being built,
  // so callers must poll until the size stabilises above the minimum.
  public static IntPtr MainFor(uint target, int minW, int minH) {
    IntPtr best = IntPtr.Zero; int bestArea = 0;
    EnumWindows(delegate(IntPtr h, IntPtr p) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid == target && IsWindowVisible(h)) {
        RECT rc;
        if (GetWindowRectStruct(h, out rc)) {
          int w = rc.R - rc.L, ht = rc.B - rc.T;
          if (w >= minW && ht >= minH && w*ht > bestArea) { best = h; bestArea = w*ht; }
        }
      }
      return true;
    }, IntPtr.Zero);
    return best;
  }
  public static string ClassOf(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
}
'@
[void][WinScreen]::SetProcessDPIAware()
# DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
try { [void][WinScreen]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$a = @()
if ($Arguments.Count -gt 0) {
  $a = $Arguments
} else {
  if ($Settings -eq "" -or $Workspace -eq "" -or $Surface -eq "") {
    Write-Output "MISSING_ARGS"
    exit 5
  }
  $a = @($Settings, $Workspace, $Surface, $Out)
  if ($Dpi -ne "") { $a += "--dpi=$Dpi" }
}
$p = Start-Process -FilePath $Exe -ArgumentList $a -PassThru -WorkingDirectory $WorkDir
$deadline = (Get-Date).AddSeconds($TimeoutSec)
$h = [IntPtr]::Zero
while ((Get-Date) -lt $deadline -and $h -eq [IntPtr]::Zero) {
  try { $p.Refresh() } catch {}
  # Process.MainWindowHandle is the most reliable source; fall back to enumeration.
  try { if ($p.MainWindowHandle -ne 0) { $h = $p.MainWindowHandle } } catch {}
  if ($h -eq [IntPtr]::Zero) { $h = [WinScreen]::MainFor([uint32]$p.Id, 600, 400) }
  if ($h -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 250 }
}
if ($h -eq [IntPtr]::Zero) { Write-Output "NO_WINDOW pid=$($p.Id)"; try { $p.Kill() } catch {}; exit 2 }
Write-Output "WINDOW class=$([WinScreen]::ClassOf($h)) pid=$($p.Id)"
$outAbs = [System.IO.Path]::GetFullPath($Out)
$outDir = Split-Path -Parent $outAbs
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$saved = $false
for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
  try { $p.Refresh() } catch {}
  if ($p.HasExited) { $cand = [WinScreen]::MainFor([uint32]$p.Id, 600, 400); if ($cand -ne [IntPtr]::Zero) { $h = $cand } }
  [void][WinScreen]::ShowWindow($h, 5)
  [void][WinScreen]::BringWindowToTop($h)
  [void][WinScreen]::SetForegroundWindow($h)
  Start-Sleep -Milliseconds $SettleMs
  $rect = New-Object WinScreen+RECT
  if (-not [WinScreen]::GetWindowRectStruct($h, [ref]$rect)) { Write-Output "ATTEMPT $attempt NO_RECT"; continue }
  $l = $rect.L; $t = $rect.T
  $w = $rect.R - $rect.L; $ht = $rect.B - $rect.T
  if ($w -le 0 -or $ht -le 0) { Write-Output "ATTEMPT $attempt BAD_RECT ${w}x${ht}"; continue }
  $bmp = New-Object System.Drawing.Bitmap($w, $ht)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($l, $t, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
  $white = 0; $total = 0
  for ($y = [int]($ht*0.15); $y -lt [int]($ht*0.85); $y += 8) {
    for ($x = [int]($w*0.15); $x -lt [int]($w*0.85); $x += 8) {
      $c = $bmp.GetPixel($x, $y); $total++
      if ($c.R -gt 235 -and $c.G -gt 235 -and $c.B -gt 235) { $white++ }
    }
  }
  $ratio = if ($total -gt 0) { [math]::Round(100.0*$white/$total,1) } else { 100 }
  $fg = ([WinScreen]::GetForegroundWindow() -eq $h)
  Write-Output "ATTEMPT $attempt rect=${w}x${ht} foreground=$fg white=$ratio%"
  if ($ratio -le $MaxWhitePercent) {
    $bmp.Save($outAbs, [System.Drawing.Imaging.ImageFormat]::Png)
    $saved = $true
    $g.Dispose(); $bmp.Dispose()
    break
  }
  $g.Dispose(); $bmp.Dispose()
  Start-Sleep -Milliseconds 900
}
if (-not $saved) {
  # Fallback: the window stays occluded by another foreground app. PrintWindow with
  # PW_RENDERFULLCONTENT is occlusion-proof and, for a WebView2-hosted UI, returns the
  # real composited frame because Chromium renders into its own surface.
  $rect = New-Object WinScreen+RECT
  if ([WinScreen]::GetWindowRectStruct($h, [ref]$rect)) {
    $w = $rect.R - $rect.L; $ht = $rect.B - $rect.T
    if ($w -gt 0 -and $ht -gt 0) {
      $bmp = New-Object System.Drawing.Bitmap($w, $ht)
      $g = [System.Drawing.Graphics]::FromImage($bmp)
      $hdc = $g.GetHdc()
      $ok = [WinScreen]::PrintWindow($h, $hdc, 2)
      $g.ReleaseHdc($hdc)
      $bmp.Save($outAbs, [System.Drawing.Imaging.ImageFormat]::Png)
      $g.Dispose(); $bmp.Dispose()
      Write-Output "PRINTWINDOW_FALLBACK ok=$ok size=${w}x${ht}"
      $saved = $true
    }
  }
}
try { if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 900; if (-not $p.HasExited) { $p.Kill() } } } catch {}
Start-Sleep -Milliseconds 300
if (-not $saved) { Write-Output "OCCLUDED_OR_INVALID"; exit 4 }
Write-Output "SAVED $outAbs"
Write-Output "DONE"
