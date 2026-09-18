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

# The shell loads its UI from the 'web' folder NEXT TO THE EXE, not from the repo sources
# (ShellOptions.FindDefaultWebRoot prefers the copy in the output directory). A stale copy
# silently produces evidence for an older UI: observed once, a capture labelled
# "git-history" actually showed a mockup.js from five hours earlier. Refuse to capture
# when any repo source is newer than the copied tree. NOTE: keep this file ASCII-only.
$exeWeb = Join-Path (Split-Path -Parent $Exe) 'web'
$repoWeb = Join-Path $WorkDir 'web'
if ((Test-Path -LiteralPath $exeWeb) -and (Test-Path -LiteralPath $repoWeb)) {
  $copied = (Get-ChildItem -LiteralPath $exeWeb -Recurse -File |
      Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
  $source = (Get-ChildItem -LiteralPath $repoWeb -Recurse -File |
      Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
  if ($source -gt $copied) {
    Write-Output "STALE_WEB_ASSETS source=$($source.ToString('s')) copied=$($copied.ToString('s'))"
    Write-Output "Rebuild the shell so the web copy beside the executable is refreshed."
    exit 6
  }
}

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
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool fAttach);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
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

  # Move the window into the visible screen and raise it: a window placed partly
  # off-screen (seen at 1085,244 / 1435x910) makes every synthetic click land on
  # another window, and the foreground rules then reject SetForegroundWindow - the
  # capture would show whatever window happens to be in front instead.
  try {
    $vis = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $rc = New-Object WinRect
    if ([WinCap]::GetWindowRect($h, [ref]$rc)) {
      $w = [Math]::Min(1400, $vis.Width - 60)
      $hh = [Math]::Min(900, $vis.Height - 120)
      [void][WinCap]::MoveWindow($h, $vis.Left + 20, $vis.Top + 20, $w, $hh, $true)
      Start-Sleep -Milliseconds 400
    }

    $fgHandle = [WinCap]::GetForegroundWindow()
    $fgThread = [WinCap]::GetWindowThreadProcessId($fgHandle, [IntPtr]::Zero)
    $myThread = [WinCap]::GetCurrentThreadId()
    [void][WinCap]::AttachThreadInput($myThread, $fgThread, $true)
    [void][WinCap]::BringWindowToTop($h)
    [void][WinCap]::SetForegroundWindow($h)
    [void][WinCap]::AttachThreadInput($myThread, $fgThread, $false)
    Start-Sleep -Milliseconds 800
  } catch {
    # Raising is best-effort; PrintWindow below is the fallback.
  }
  if ($h -eq [IntPtr]::Zero) { $h = [WinScreen]::MainFor([uint32]$p.Id, 600, 400) }
  if ($h -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 250 }
}
if ($h -eq [IntPtr]::Zero) { Write-Output "NO_WINDOW pid=$($p.Id)"; try { $p.Kill() } catch {}; exit 2 }
Write-Output "WINDOW class=$([WinScreen]::ClassOf($h)) pid=$($p.Id)"
$outAbs = [System.IO.Path]::GetFullPath($Out)
$outDir = Split-Path -Parent $outAbs
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$saved = $false
$savedSource = ""
# A blank frame is a frame whose sampled region is almost entirely white. Both capture
# paths are checked with the same rule: PrintWindow used to be saved unchecked, and it
# returned an all-white frame for a window that had not painted yet (observed once).
function Get-WhiteRatio($bmp, $w, $ht) {
  $white = 0; $total = 0
  for ($y = [int]($ht*0.15); $y -lt [int]($ht*0.85); $y += 8) {
    for ($x = [int]($w*0.15); $x -lt [int]($w*0.85); $x += 8) {
      $c = $bmp.GetPixel($x, $y); $total++
      if ($c.R -gt 235 -and $c.G -gt 235 -and $c.B -gt 235) { $white++ }
    }
  }
  if ($total -le 0) { return 100 }
  return [math]::Round(100.0*$white/$total,1)
}

for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
  try { $p.Refresh() } catch {}
  if ($p.HasExited) { $cand = [WinScreen]::MainFor([uint32]$p.Id, 600, 400); if ($cand -ne [IntPtr]::Zero) { $h = $cand } }
  [void][WinScreen]::ShowWindow($h, 5)
  [void][WinScreen]::BringWindowToTop($h)
  # Windows refuses SetForegroundWindow from a background process, which left the window
  # occluded and made the on-screen capture class unavailable. Attaching this thread's
  # input queue to the current foreground thread makes the call succeed.
  $targetPid = 0; $fgPid = 0
  $fgThread = [WinScreen]::GetWindowThreadProcessId([WinScreen]::GetForegroundWindow(), [ref]$fgPid)
  $self = [WinScreen]::GetCurrentThreadId()
  $attached = $false
  if ($fgThread -ne 0 -and $fgThread -ne $self) { $attached = [WinScreen]::AttachThreadInput($self, $fgThread, $true) }
  [void][WinScreen]::SetForegroundWindow($h)
  Start-Sleep -Milliseconds 400
  if ($attached) { [void][WinScreen]::AttachThreadInput($self, $fgThread, $false) }
  Start-Sleep -Milliseconds 500
  Start-Sleep -Milliseconds $SettleMs
  $rect = New-Object WinScreen+RECT
  if (-not [WinScreen]::GetWindowRectStruct($h, [ref]$rect)) { Write-Output "ATTEMPT $attempt NO_RECT"; continue }
  $l = $rect.L; $t = $rect.T
  $w = $rect.R - $rect.L; $ht = $rect.B - $rect.T
  if ($w -le 0 -or $ht -le 0) { Write-Output "ATTEMPT $attempt BAD_RECT ${w}x${ht}"; continue }

  # 1) Real on-screen pixels. Requires the window to actually be foreground: the white
  # check only catches BRIGHT occluders, so a dark occluding window passes it and the
  # saved frame would belong to another app (observed once).
  $bmp = New-Object System.Drawing.Bitmap($w, $ht)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($l, $t, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
  $ratio = Get-WhiteRatio $bmp $w $ht
  $fg = ([WinScreen]::GetForegroundWindow() -eq $h)
  Write-Output "ATTEMPT $attempt rect=${w}x${ht} foreground=$fg screenWhite=$ratio%"
  if ($fg -and $ratio -le $MaxWhitePercent) {
    $bmp.Save($outAbs, [System.Drawing.Imaging.ImageFormat]::Png)
    $saved = $true; $savedSource = "screen"
    $g.Dispose(); $bmp.Dispose()
    break
  }
  $g.Dispose(); $bmp.Dispose()

  # 2) PrintWindow with PW_RENDERFULLCONTENT is occlusion-proof and returns the real
  # composited frame for a WebView2-hosted UI. It is NOT on-screen evidence, so the saved
  # source is reported; and it can be blank before the page paints, hence the same check.
  $bmp = New-Object System.Drawing.Bitmap($w, $ht)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  $ok = [WinScreen]::PrintWindow($h, $hdc, 2)
  $g.ReleaseHdc($hdc)
  $printRatio = Get-WhiteRatio $bmp $w $ht
  Write-Output "ATTEMPT $attempt printwindowOk=$ok printwindowWhite=$printRatio%"
  if ($ok -and $printRatio -le $MaxWhitePercent) {
    $bmp.Save($outAbs, [System.Drawing.Imaging.ImageFormat]::Png)
    $saved = $true; $savedSource = "printwindow"
    $g.Dispose(); $bmp.Dispose()
    break
  }
  $g.Dispose(); $bmp.Dispose()
  Start-Sleep -Milliseconds 1200
}
try { if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 900; if (-not $p.HasExited) { $p.Kill() } } } catch {}
Start-Sleep -Milliseconds 300
if (-not $saved) { Write-Output "OCCLUDED_OR_BLANK"; exit 4 }
Write-Output "SAVED $outAbs via=$savedSource"
Write-Output "DONE"
