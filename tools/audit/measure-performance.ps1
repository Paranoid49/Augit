# Measure one Augit cold-start performance scenario against the real Windows shell.
#
# Scenario classes required by the goal: empty repository, existing repository, large repository.
# Metrics: startup (window / page / first usable), operation response (host round trips and the
# page's own marks), memory (main process + WebView2 tree attributed BY PARENT RELATIONSHIP),
# shutdown time and resource cleanup (no descendant process left).
#
# Attribution matters: counting every msedgewebview2 process mixes in other applications'
# browsers (measured once: 525 MB reported as 822 MB). This script walks the child tree of the
# process it started only.
#
# The script never touches another Augit instance: it starts its own process and only manages
# that process tree. The user settings file is backed up and restored, so a measurement leaves
# no persistent change behind. Keep this file ASCII-only (PowerShell 5.1 reads .ps1 as ANSI).
param(
  [Parameter(Mandatory=$true)][string]$Exe,
  # Workspace to open. Required: the scenario is defined by it.
  [Parameter(Mandatory=$true)][string]$Workspace,
  [string]$Scenario = "custom",
  [int]$Port = 9345,
  [int]$Width = 1180,
  [int]$Height = 760,
  # Idle window before the memory and CPU samples.
  [int]$IdleMs = 5000,
  # A directory (relative to the workspace) with many children, used for one host round trip.
  [string]$LargeDirectory = "",
  # A file (relative to the workspace) to open, used for one document round trip.
  [string]$OpenFile = "",
  # Where to write the JSON result. Empty = print only.
  [string]$Out = "",
  [int]$ReadyTimeoutSec = 90,
  [int]$ShutdownTimeoutSec = 30
)
$ErrorActionPreference = "Stop"

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class AugitPerfWin32 {
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
'@

$exeFull = (Resolve-Path -LiteralPath $Exe).Path
$workspaceFull = (Resolve-Path -LiteralPath $Workspace).Path
$exeDir = Split-Path -Parent $exeFull

# Stale web assets would silently measure an older UI (same guard as capture-surface.ps1):
# compare the repository sources against the copy beside the executable, not the DLL timestamp.
$exeWeb = Join-Path $exeDir "web"
$repoWeb = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "web"
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

# --- settings backup (restored in finally) ---------------------------------------------------
$settingsPath = Join-Path $env:LOCALAPPDATA "Augit\settings.json"
$settingsBackup = $null
$settingsExisted = $false
if (Test-Path -LiteralPath $settingsPath) {
  $settingsBackup = [System.IO.File]::ReadAllBytes($settingsPath)
  $settingsExisted = $true
}

# --- CDP over a persistent websocket ---------------------------------------------------------
function Get-CdpSocket([int]$port, [int]$timeoutSec) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    try {
      $targets = Invoke-RestMethod -Uri "http://127.0.0.1:$port/json/list" -TimeoutSec 3
      $page = $targets | Where-Object { $_.url -like "*index.html*" } | Select-Object -First 1
      if ($page) {
        $ws = New-Object System.Net.WebSockets.ClientWebSocket
        $token = [System.Threading.CancellationToken]::None
        [void]$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $token).GetAwaiter().GetResult()
        return $ws
      }
    } catch { }
    Start-Sleep -Milliseconds 100
  }
  throw "no CDP page target on port $port"
}

function Invoke-Cdp([System.Net.WebSockets.ClientWebSocket]$ws, [string]$expression, [int]$id) {
  $token = [System.Threading.CancellationToken]::None
  $payload = @{ id = $id; method = "Runtime.evaluate"; params = @{ expression = $expression; returnByValue = $true; awaitPromise = $true } } | ConvertTo-Json -Compress -Depth 6
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
  $segment = New-Object System.ArraySegment[byte] -ArgumentList @(,$bytes)
  [void]$ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $token).GetAwaiter().GetResult()
  $buffer = New-Object byte[] 262144
  $builder = New-Object System.Text.StringBuilder
  $deadline = (Get-Date).AddSeconds(30)
  while ((Get-Date) -lt $deadline) {
    $receiveSegment = New-Object System.ArraySegment[byte] -ArgumentList @(,$buffer)
    $result = $ws.ReceiveAsync($receiveSegment, $token).GetAwaiter().GetResult()
    [void]$builder.Append([System.Text.Encoding]::UTF8.GetString($buffer, 0, $result.Count))
    if (-not $result.EndOfMessage) { continue }
    $text = $builder.ToString()
    [void]$builder.Clear()
    $message = $null
    try { $message = $text | ConvertFrom-Json } catch { continue }
    if ($message.id -eq $id) { return $message.result.result.value }
  }
  throw "CDP evaluate timed out: $expression"
}

# Poll an expression until it is truthy, over ONE socket; returns elapsed milliseconds.
function Wait-CdpTruthy([System.Net.WebSockets.ClientWebSocket]$ws, [string]$expression, [System.Diagnostics.Stopwatch]$watch, [int]$timeoutSec) {
  $id = 900
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    $id++
    try {
      $value = Invoke-Cdp $ws "!!($expression)" $id
      if ($value -eq $true) { return [int]$watch.Elapsed.TotalMilliseconds }
    } catch { }
    Start-Sleep -Milliseconds 30
  }
  return -1
}

# --- process tree helpers --------------------------------------------------------------------
function Get-ProcessTree([int]$rootPid) {
  $all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId
  $children = @{}
  foreach ($item in $all) {
    $parent = [int]$item.ParentProcessId
    if (-not $children.ContainsKey($parent)) { $children[$parent] = New-Object System.Collections.ArrayList }
    [void]$children[$parent].Add([int]$item.ProcessId)
  }
  $result = New-Object System.Collections.ArrayList
  $queue = New-Object System.Collections.Queue
  $queue.Enqueue($rootPid)
  while ($queue.Count -gt 0) {
    $current = [int]$queue.Dequeue()
    [void]$result.Add($current)
    if ($children.ContainsKey($current)) {
      foreach ($child in $children[$current]) { $queue.Enqueue($child) }
    }
  }
  return $result
}

function Measure-TreeMemory([int]$rootPid) {
  $ids = Get-ProcessTree $rootPid
  $main = $null
  $treeWs = 0
  $treePrivate = 0
  $webviewWs = 0
  $webviewPrivate = 0
  $webviewCount = 0
  $otherNames = New-Object System.Collections.ArrayList
  foreach ($procId in $ids) {
    $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
    if ($null -eq $proc) { continue }
    if ($procId -eq $rootPid) { $main = $proc }
    $treeWs += [int64]$proc.WorkingSet64
    try { $treePrivate += [int64]$proc.PrivateMemorySize64 } catch { }
    if ($proc.ProcessName -eq "msedgewebview2") {
      $webviewCount++
      $webviewWs += [int64]$proc.WorkingSet64
      try { $webviewPrivate += [int64]$proc.PrivateMemorySize64 } catch { }
    }
    elseif ($procId -ne $rootPid) {
      # Third-party helpers the browser spawns (for example AMD overlay processes) are
      # descendants by parent relationship but are not Augit's own footprint; keep them
      # visible instead of silently adding them to the WebView2 number.
      [void]$otherNames.Add($proc.ProcessName)
    }
  }
  return [pscustomobject]@{
    ProcessCount = $ids.Count
    MainWorkingSetMb = if ($main) { [math]::Round($main.WorkingSet64 / 1MB, 2) } else { $null }
    MainPrivateMb = if ($main) { [math]::Round($main.PrivateMemorySize64 / 1MB, 2) } else { $null }
    WebviewCount = $webviewCount
    WebviewWorkingSetMb = [math]::Round($webviewWs / 1MB, 2)
    WebviewPrivateMb = [math]::Round($webviewPrivate / 1MB, 2)
    TreeWorkingSetMb = [math]::Round($treeWs / 1MB, 2)
    TreePrivateMb = [math]::Round($treePrivate / 1MB, 2)
    OtherDescendants = ($otherNames -join ",")
  }
}

# --- launch ----------------------------------------------------------------------------------
$process = $null
$browserClosed = $false
$result = [ordered]@{
  scenario = $Scenario
  workspace = $workspaceFull
  exe = $exeFull
  theme = "dark"
  width = $Width
  height = $Height
  startedAt = (Get-Date).ToString("s")
}

try {
  $launchArgs = @(
    "--workspace", $workspaceFull,
    "--no-session-restore",
    "--theme", "dark",
    "--width", "$Width",
    "--height", "$Height",
    "--browser-args", "--remote-debugging-port=$Port"
  )
  $watch = [System.Diagnostics.Stopwatch]::StartNew()
  $process = Start-Process -FilePath $exeFull -ArgumentList $launchArgs -PassThru
  $rootPid = $process.Id

  # Startup: window handle (10 ms poll).
  $handle = [IntPtr]::Zero
  $windowDeadline = (Get-Date).AddSeconds($ReadyTimeoutSec)
  while ((Get-Date) -lt $windowDeadline -and $handle -eq [IntPtr]::Zero) {
    Start-Sleep -Milliseconds 10
    try { $process.Refresh() } catch { }
    if ($process.HasExited) { break }
    if ($process.MainWindowHandle -ne 0) { $handle = $process.MainWindowHandle }
  }
  $windowMs = if ($handle -ne [IntPtr]::Zero) { [int]$watch.Elapsed.TotalMilliseconds } else { -1 }
  $result.windowMs = $windowMs

  if ($windowMs -lt 0) {
    $result.error = "no main window"
  }
  else {
    # Startup: page ready, then Git + history usable.
    $ws = Get-CdpSocket $Port 30
    try {
      $result.pageReadyMs = Wait-CdpTruthy $ws "window.__augitReady === true" $watch $ReadyTimeoutSec
      $result.gitReadyMs = Wait-CdpTruthy $ws "window.__augitGitReady === true && window.__augitHistoryReady === true" $watch $ReadyTimeoutSec
      $id = 1
      $result.marks = Invoke-Cdp $ws "JSON.stringify(window.__augitMarks || {})" $id
      $result.live = Invoke-Cdp $ws "JSON.stringify({workspace: (window.__augitLive||{}).name || null, historyRows: ((window.__augitLive||{}).history && (window.__augitLive.history.commits||[]).length) || 0, files: ((window.__augitLive||{}).tree||[]).length, gitUnavailable: !!window.__augitGitUnavailable, error: window.__augitError || null})" ($id + 1)

      # Operation response: one host round trip for a directory listing, measured in the page.
      $result.listRootMs = Invoke-Cdp $ws "(async function(){var t=performance.now(); await window.__augitListDirectory(''); return Math.round(performance.now()-t);})()" ($id + 2)
      if ($LargeDirectory -ne "") {
        $escaped = $LargeDirectory.Replace('\', '\\').Replace("'", "\'")
        $result.listLargeMs = Invoke-Cdp $ws "(async function(){var t=performance.now(); var r=await window.__augitListDirectory('$escaped'); return {ms: Math.round(performance.now()-t), entries: (r&&r.entries)?r.entries.length:-1};})()" ($id + 3)
        $result.listLargeMs = $result.listLargeMs | ConvertTo-Json -Compress
      }
      if ($OpenFile -ne "") {
        $escapedFile = $OpenFile.Replace('\', '\\').Replace("'", "\'")
        $result.openDocument = Invoke-Cdp $ws "(async function(){var t=performance.now(); await window.__augitOpenDocument('$escapedFile'); return {ms: Math.round(performance.now()-t), mark: (window.__augitMarks||{}).open};})()" ($id + 4)
        $result.openDocument = $result.openDocument | ConvertTo-Json -Compress
      }

      # Idle window: memory + CPU delta.
      Start-Sleep -Milliseconds $IdleMs
      $memory = Measure-TreeMemory $rootPid
      $result.processCount = $memory.ProcessCount
      $result.mainWorkingSetMb = $memory.MainWorkingSetMb
      $result.mainPrivateMb = $memory.MainPrivateMb
      $result.webviewCount = $memory.WebviewCount
      $result.webviewWorkingSetMb = $memory.WebviewWorkingSetMb
      $result.webviewPrivateMb = $memory.WebviewPrivateMb
      $result.treeWorkingSetMb = $memory.TreeWorkingSetMb
      $result.treePrivateMb = $memory.TreePrivateMb
      $result.otherDescendants = $memory.OtherDescendants

      $mainProc = Get-Process -Id $rootPid -ErrorAction SilentlyContinue
      if ($mainProc) {
        $cpuBefore = $mainProc.TotalProcessorTime.TotalMilliseconds
        $cpuWatch = [System.Diagnostics.Stopwatch]::StartNew()
        Start-Sleep -Milliseconds 2000
        $mainProc.Refresh()
        $cpuAfter = $mainProc.TotalProcessorTime.TotalMilliseconds
        $result.idleCpuDeltaMs = [math]::Round($cpuAfter - $cpuBefore, 1)
        $result.idleCpuWindowMs = [math]::Round($cpuWatch.Elapsed.TotalMilliseconds, 1)
      }
    }
    finally {
      if ($ws) { $ws.Dispose() }
    }
  }

  # Shutdown.
  $shutdownWatch = [System.Diagnostics.Stopwatch]::StartNew()
  $null = $process.CloseMainWindow()
  $exited = $process.WaitForExit($ShutdownTimeoutSec * 1000)
  $result.shutdownMs = [int]$shutdownWatch.Elapsed.TotalMilliseconds
  $result.forcedClose = -not $exited
  if (-not $exited) {
    $process.Kill()
    $process.WaitForExit(10000) | Out-Null
  }

  # Resource cleanup: the WebView2 tree must be gone shortly after the main process exits.
  $leftover = @()
  $cleanupDeadline = (Get-Date).AddSeconds(15)
  do {
    Start-Sleep -Milliseconds 300
    $leftover = @(Get-ProcessTree $rootPid | Where-Object { $_ -ne $rootPid } | Where-Object {
        $null -ne (Get-Process -Id $_ -ErrorAction SilentlyContinue) })
  } while ($leftover.Count -gt 0 -and (Get-Date) -lt $cleanupDeadline)
  $result.descendantsLeft = $leftover.Count
} finally {
  if ($process -and -not $process.HasExited) {
    try { $process.Kill(); $process.WaitForExit(10000) | Out-Null } catch { }
  }
  # Restore the user settings file exactly as it was.
  if ($settingsExisted -and $settingsBackup) {
    [System.IO.File]::WriteAllBytes($settingsPath, $settingsBackup)
  }
  elseif (-not $settingsExisted -and (Test-Path -LiteralPath $settingsPath)) {
    Remove-Item -LiteralPath $settingsPath -Force -ErrorAction SilentlyContinue
  }
}

$json = $result | ConvertTo-Json -Compress
if ($Out -ne "") {
  $dir = Split-Path -Parent $Out
  if ($dir -ne "" -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
  [System.IO.File]::WriteAllText($Out, $json, (New-Object System.Text.UTF8Encoding($false)))
}
Write-Output ("PERF " + $json)
