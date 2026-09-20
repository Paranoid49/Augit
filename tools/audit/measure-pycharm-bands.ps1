# PyCharm screenshot measurement helper (ASCII only, per tools/audit rules).
#
# Why this exists: measuring PyCharm surfaces from screenshots went through several failed methods
# (round 412: "white area" detection is useless because popups and the editor are both white;
# round 415: "text bands" give line spacing, NOT row height). The methods that DID work are encoded
# here so the next round does not re-derive them:
#
#   -Mode edge   : along a scan line, report the first/last pixel darker than the background
#                  (this is how the Branches popup bounds and the search field box were measured).
#                  Run it on >=2 parallel lines and only trust values that agree.
#   -Mode bands  : report text bands (dark-pixel runs) with midpoints and pitches. Use for line
#                  spacing ONLY; never publish a band pitch as a row height.
#   -Mode colour : report the run of pixels matching a colour test (e.g. blue selection: B-R > 12).
#                  Caveat printed by the tool: a colour run can span several rows, so it must be
#                  cross-checked against a border/divider before being called a row height.
#
# Usage examples:
#   powershell -File tools/audit/measure-pycharm-bands.ps1 -Image a.png -Mode edge -Axis row -At 900 -From 1000 -To 2200
#   powershell -File tools/audit/measure-pycharm-bands.ps1 -Image a.png -Mode bands -Axis row -At 0 -From 1180 -To 1420
#   powershell -File tools/audit/measure-pycharm-bands.ps1 -Image a.png -Mode colour -Axis row -At 800 -From 1150 -To 1500
param(
  [Parameter(Mandatory = $true)][string]$Image,
  [Parameter(Mandatory = $true)][ValidateSet('edge', 'bands', 'colour')][string]$Mode,
  [Parameter(Mandatory = $true)][ValidateSet('row', 'col')][string]$Axis,
  [int]$At = 0,
  [Parameter(Mandatory = $true)][int]$From,
  [Parameter(Mandatory = $true)][int]$To,
  [int]$Threshold = 244,
  [int]$Dark = 150,
  [double]$Dpi = 1.75
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile($Image)
try {
  Write-Output ("IMAGE " + $Image + " " + $img.Width + "x" + $img.Height + " mode=" + $Mode + " axis=" + $Axis + " at=" + $At)

  if ($Mode -eq 'edge') {
    # For Axis=row: scan x from From..To at fixed y=At. For Axis=col: scan y at fixed x=At.
    $min = -1; $max = -1
    for ($i = $From; $i -le $To; $i++) {
      $p = if ($Axis -eq 'row') { $img.GetPixel($i, $At) } else { $img.GetPixel($At, $i) }
      if ($p.R -lt $Threshold -or $p.G -lt $Threshold -or $p.B -lt $Threshold) {
        if ($min -lt 0) { $min = $i }
        $max = $i
      }
    }
    if ($min -lt 0) { Write-Output 'NO_EDGE (scan line is entirely background-coloured)'; exit 0 }
    $size = $max - $min + 1
    Write-Output ("EDGE first=" + $min + " last=" + $max + " size=" + $size + " CSS=" + [Math]::Round($size / $Dpi, 1))
    # Round 76 lesson: a scan line that starts or ends still inside non-background pixels is CLIPPED,
    # so "size" is a lower bound rather than a measurement. Two of three lines came back clipped and
    # I nearly published the number anyway - hence this warning is printed by the tool, not by the caller.
    if ($min -eq $From) { Write-Output 'WARN CLIPPED_AT_START (first == From): size is a lower bound, widen the range' }
    if ($max -eq $To) { Write-Output 'WARN CLIPPED_AT_END (last == To): size is a lower bound, widen the range' }
    Write-Output 'NOTE: run at least two parallel lines and only trust agreeing values.'
    exit 0
  }

  if ($Mode -eq 'bands') {
    $rows = @()
    for ($i = $From; $i -le $To; $i++) {
      $c = 0
      for ($j = 0; $j -lt 700; $j += 2) {
        $x = if ($Axis -eq 'row') { $j + 100 } else { $At }
        $y = if ($Axis -eq 'row') { $i } else { $j + 100 }
        $p = $img.GetPixel($x, $y)
        if ($p.R -lt $Dark -and $p.G -lt $Dark) { $c++ }
      }
      if ($c -gt 2) { $rows += $i }
    }
    $mids = @(); $start = -1; $prev = -2
    foreach ($y in $rows) {
      if ($y -ne $prev + 1) { if ($start -ge 0) { $mids += [int](($start + $prev) / 2) }; $start = $y }
      $prev = $y
    }
    if ($start -ge 0) { $mids += [int](($start + $prev) / 2) }
    $pitch = @()
    for ($k = 1; $k -lt $mids.Count; $k++) { $pitch += ($mids[$k] - $mids[$k - 1]) }
    Write-Output ("BANDS count=" + $mids.Count + " mids=" + ($mids -join ','))
    Write-Output ("PITCH phys=" + ($pitch -join ',') + " (first CSS=" + $(if ($pitch.Count -gt 0) { [Math]::Round($pitch[0] / $Dpi, 1) } else { 'n/a' }) + ")")
    Write-Output 'NOTE: text-band pitch is LINE SPACING, not row height. A commit row that shows two text lines has pitch < row height.'
    exit 0
  }

  # Mode=colour: default test is "blue-ish" (B - R > 12), which is how PyCharm selection bands read.
  $hits = @()
  for ($i = $From; $i -le $To; $i++) {
    $p = if ($Axis -eq 'row') { $img.GetPixel($At, $i) } else { $img.GetPixel($i, $At) }
    if (([int]$p.B - [int]$p.R) -gt 12) { $hits += $i }
  }
  if ($hits.Count -eq 0) { Write-Output 'NO_COLOUR_RUN'; exit 0 }
  $size = $hits[-1] - $hits[0] + 1
  $mid = [int](($hits[0] + $hits[-1]) / 2)
  $c = if ($Axis -eq 'row') { $img.GetPixel($At, $mid) } else { $img.GetPixel($mid, $At) }
  Write-Output ("COLOUR run=" + $hits[0] + ".." + $hits[-1] + " size=" + $size + " CSS=" + [Math]::Round($size / $Dpi, 1) + " midColour=" + $c.R + ',' + $c.G + ',' + $c.B)
  Write-Output 'CAVEAT: a colour run can span several rows (selection + details pane). Do NOT publish it as a row height unless a border/divider confirms the boundary.'
}
finally { $img.Dispose() }
