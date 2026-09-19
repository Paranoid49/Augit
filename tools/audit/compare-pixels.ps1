# Same-engine pixel comparison: "live shell vs visual mockup" measured in ONE engine on ONE OS.
#
# Why: the shell's UI is rendered by WebView2 (Windows) while the static mockups were being
# rendered by Playwright/Chromium (Linux). The same font stack measures 111.2px there and
# 124.2px here (round 242), so text-width and scrollbar differences were pure platform noise
# and could hide real differences. This script removes that noise by rendering the MOCKUP page
# inside the very same WebView2: launch with --web-root <mockups dir> and navigate the page to
# <scene>.html via CDP.
#
# Screenshots come from Page.captureScreenshot (CDP), NOT PrintWindow: PrintWindow on a
# WebView2 window clipped/shifted the bottom ~22px (a statusbar band showed as editor content,
# which looked like a 92% difference -- round 279).
#
# Usage:
#   powershell -File tools/audit/compare-pixels.ps1 -Exe <Augit.exe> -Workspace <dir> `
#       -Scene git-history -Bands 'titlebar:0:44,statusbar:H-24:H' -OutDir D:\tmp\px
# Keep this file ASCII-only.
param(
  [Parameter(Mandatory=$true)][string]$Exe,
  [Parameter(Mandatory=$true)][string]$Workspace,
  [Parameter(Mandatory=$true)][string]$Scene,
  [string]$OutDir = "",
  [string[]]$Bands = @('titlebar:0:44', 'statusbar:H-24:H'),
  [string]$Theme = "dark",
  [int]$Width = 1180,
  [int]$Height = 760,
  [int]$Port = 9470,
  [double]$MaxVisiblePercent = 5.0
)
$ErrorActionPreference = "Stop"
if ($OutDir -eq "") { $OutDir = Join-Path $env:TEMP "augit-pixels" }
if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
. (Join-Path $PSScriptRoot 'cdp-eval.ps1')
Add-Type -AssemblyName System.Drawing

$mockupsRoot = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'docs/ux-mockups'
if (-not (Test-Path -LiteralPath (Join-Path $mockupsRoot ($Scene + '.html')))) {
  Write-Output ("MISSING_MOCKUP " + $Scene); exit 3
}

function Capture([string]$mode, [int]$port, [string]$out) {
  Get-Process Augit -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Seconds 2
  $common = @('--workspace', $Workspace, '--pixel-exact', '--theme', $Theme,
    '--width', "$Width", '--height', "$Height", '--browser-args', "--remote-debugging-port=$port")
  $args = if ($mode -eq 'mockup') { @('--web-root', $mockupsRoot) + $common } else { @('--scene', $Scene) + $common }
  $process = Start-Process -FilePath $Exe -ArgumentList $args -PassThru
  Start-Sleep -Seconds 8
  $socket = Get-CdpSocket $port
  if ($mode -eq 'mockup') {
    # The mockup HTML pages are standalone scenes; the shell always opens index.html, so the
    # page is navigated to the scene file through CDP.
    $nav = '(()=>{location.href="https://augit.local/' + $Scene + '.html?theme=' + $Theme + '";return "nav"})()'
    [void](Invoke-Cdp $socket $nav 1 30)
    Start-Sleep -Seconds 3
  } else {
    Start-Sleep -Seconds 2
  }
  $raw = Invoke-CdpMethod $socket 'Page.captureScreenshot' '{"format":"png"}' 2 60
  $data = ($raw | ConvertFrom-Json).result.data
  [System.IO.File]::WriteAllBytes($out, [Convert]::FromBase64String($data))
  try { $process.Kill() } catch { }
  Start-Sleep -Milliseconds 400
}

$mockPath = Join-Path $OutDir ($Scene + '-mockup.png')
$livePath = Join-Path $OutDir ($Scene + '-live.png')
Capture 'mockup' $Port $mockPath
Capture 'live' ($Port + 1) $livePath

$mock = [System.Drawing.Bitmap]::FromFile($mockPath)
$live = [System.Drawing.Bitmap]::FromFile($livePath)
Write-Output ("SIZES mock=" + $mock.Width + "x" + $mock.Height + " live=" + $live.Width + "x" + $live.Height)
if ($mock.Width -ne $live.Width -or $mock.Height -ne $live.Height) {
  Write-Output "SIZE_MISMATCH"
  $mock.Dispose(); $live.Dispose(); exit 4
}
# -File turns "a,b" into ONE element, so the comma-joined form must be flattened here
# (same trap as -Selectors in dump-live-dom.ps1).
$bandList = @()
foreach ($entry in $Bands) {
  foreach ($piece in ([string]$entry).Split(',')) { if ($piece.Trim() -ne '') { $bandList += $piece.Trim() } }
}
$failures = 0
foreach ($band in $bandList) {
  $parts = $band.Split(':')
  $name = $parts[0]
  $y0 = if ($parts[1] -eq 'H') { $mock.Height } elseif ($parts[1].StartsWith('H-')) { $mock.Height - [int]$parts[1].Substring(2) } else { [int]$parts[1] }
  $y1 = if ($parts[2] -eq 'H') { $mock.Height } elseif ($parts[2].StartsWith('H-')) { $mock.Height - [int]$parts[2].Substring(2) } else { [int]$parts[2] }
  # Two metrics: per-pixel "visible" differences (dominated by TEXT, because the live shell
  # shows real data while the mockup shows sample data) and per-BLOCK mean differences, which
  # average text away and therefore expose LAYOUT/background/border differences.
  $diff = 0; $visible = 0; $max = 0; $total = 0
  $blockTotal = 0; $blockDiff = 0
  $blockSize = 8
  $blockThreshold = 6.0
  for ($by = $y0; $by -lt $y1; $by += $blockSize) {
    for ($bx = 0; $bx -lt $mock.Width; $bx += $blockSize) {
      $sum = 0.0; $count = 0
      for ($y = $by; $y -lt [Math]::Min($by + $blockSize, $y1); $y++) {
        for ($x = $bx; $x -lt [Math]::Min($bx + $blockSize, $mock.Width); $x++) {
          $a = $mock.GetPixel($x, $y); $b = $live.GetPixel($x, $y); $total++; $count++
          $d = [Math]::Max([Math]::Abs($a.R - $b.R), [Math]::Max([Math]::Abs($a.G - $b.G), [Math]::Abs($a.B - $b.B)))
          if ($d -gt 0) { $diff++ }
          if ($d -gt 8) { $visible++ }
          if ($d -gt $max) { $max = $d }
          $sum += $d
        }
      }
      if ($count -gt 0) { $blockTotal++; if (($sum / $count) -gt $blockThreshold) { $blockDiff++ } }
    }
  }
  $blockPercent = if ($blockTotal -gt 0) { [Math]::Round(100.0 * $blockDiff / $blockTotal, 2) } else { 100 }
  $percent = if ($total -gt 0) { [Math]::Round(100.0 * $visible / $total, 2) } else { 100 }
  $verdict = if ($percent -le $MaxVisiblePercent) { 'PASS' } else { 'FAIL'; }
  if ($percent -gt $MaxVisiblePercent) { $failures++ }
  Write-Output ("BAND $name y=$y0..$y1 total=$total diff=$diff visible=$visible visiblePercent=$percent max=$max blockDiffPercent=$blockPercent $verdict")
}
$mock.Dispose(); $live.Dispose()
Write-Output ("SUMMARY failures=" + $failures)
if ($failures -gt 0) { Write-Output 'PIXELS_DIFFER'; exit 1 }
Write-Output 'PIXELS_OK'
exit 0
