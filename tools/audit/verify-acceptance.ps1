# Key-surface acceptance sweep: run capture-surface.ps1 for every scene and summarize.
# One command for the "on-screen visual acceptance" step of the closing checklist.
param(
  [Parameter(Mandatory=$true)][string]$Exe,
  [Parameter(Mandatory=$true)][string]$OutDir,
  [Parameter(Mandatory=$true)][string]$Workspace,
  [string]$Settings = "",
  [string[]]$Scenes = @(),
  [int]$SettleMs = 2500,
  [double]$MaxWhitePercent = 10.0
)

$DefaultScenes = @(
  'main-project', 'text-viewer', 'markdown-preview', 'json-preview', 'image-preview',
  'file-limit', 'commit-changes', 'commit-diff', 'git-history', 'file-history', 'blame',
  'git-compare', 'conflict-list', 'conflict-resolver', 'stash', 'stash-manager',
  'worktrees', 'remote', 'workspace-open', 'settings', 'terminal', 'quick-open',
  'repository-search', 'operation-result', 'repository-init'
)

# `powershell -File ... -Scenes a,b` arrives as one comma-joined string, so split it.
if ($Scenes.Count -eq 1 -and $Scenes[0] -match ',') {
  $Scenes = $Scenes[0] -split ','
}

if ($Scenes.Count -eq 0) {
  $Scenes = $DefaultScenes
}

if (-not (Test-Path $OutDir)) {
  New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
}

$capture = Join-Path $PSScriptRoot 'capture-surface.ps1'
if (-not (Test-Path $capture)) {
  Write-Output 'CAPTURE_SCRIPT_MISSING'
  exit 2
}

$failed = @()
foreach ($scene in $Scenes) {
  $out = Join-Path $OutDir ('accept-' + $scene + '.bmp')
  $arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $capture,
    '-Exe', $Exe, '-Out', $out, '-Workspace', $Workspace,
    '-Surface', $scene, '-SettleMs', $SettleMs, '-MaxWhitePercent', $MaxWhitePercent
  )
  if ($Settings -ne '') {
    $arguments += @('-Settings', $Settings)
  }

  $lines = & powershell @arguments 2>&1
  $verdict = ($lines | Where-Object { $_ -match '^(OK|OCCLUDED_OR_BLANK|MISSING_ARGS|TIMEOUT|NO_WINDOW)' } | Select-Object -Last 1)
  if (-not $verdict) {
    $verdict = ($lines | Select-Object -Last 1)
  }
  $verdict = "$verdict".Trim()

  # Pass means: the capture was written and the script did not report a blank/occluded frame.
  $passed = ($verdict -notmatch 'OCCLUDED_OR_BLANK|MISSING_ARGS|TIMEOUT|NO_WINDOW|CAPTURE_SCRIPT_MISSING') -and (Test-Path $out)
  if (-not $passed) {
    $failed += $scene
  }

  Write-Output ("{0,-20} {1}" -f $scene, $(if ($passed) { 'PASS' } else { 'FAIL ' + $verdict }))
}

Write-Output ("SUMMARY total={0} passed={1} failed={2}" -f $Scenes.Count, ($Scenes.Count - $failed.Count), $failed.Count)
if ($failed.Count -gt 0) {
  Write-Output ('FAILED_SCENES ' + ($failed -join ','))
  exit 1
}

Write-Output 'ACCEPTANCE_OK'
exit 0
