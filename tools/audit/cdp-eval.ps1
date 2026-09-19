# Shared Chrome DevTools Protocol helpers for the real-machine audit scripts.
#
# The shell exposes CDP when started with --browser-args --remote-debugging-port=<port>,
# which is how the audit scripts read the REAL DOM instead of guessing from a screenshot.
# Keep this file ASCII-only (same rule as every other .ps1 under tools/audit).
#
# Usage:  . (Join-Path $PSScriptRoot 'cdp-eval.ps1')
#         $socket = Get-CdpSocket 9333
#         $json   = Invoke-Cdp $socket "JSON.stringify(window.__augitErrors||[])" 1

function Get-CdpSocket([int]$port) {
  $deadline = (Get-Date).AddSeconds(25)
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

function Invoke-Cdp([string]$socketUrl, [string]$expression, [int]$id, [int]$timeoutSec = 60) {
  $ws = New-Object System.Net.WebSockets.ClientWebSocket
  $token = [System.Threading.CancellationToken]::None
  [void]$ws.ConnectAsync([Uri]$socketUrl, $token).GetAwaiter().GetResult()
  $payload = @{ id = $id; method = "Runtime.evaluate"; params = @{ expression = $expression; returnByValue = $true; awaitPromise = $true } } | ConvertTo-Json -Compress -Depth 8
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
  $segment = New-Object System.ArraySegment[byte] -ArgumentList @(,$bytes)
  [void]$ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $token).GetAwaiter().GetResult()
  $buffer = New-Object byte[] 1048576
  $builder = New-Object System.Text.StringBuilder
  $deadline = (Get-Date).AddSeconds($timeoutSec)
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
      if ($message.result.exceptionDetails) {
        $details = $message.result.exceptionDetails
        # A page exception carries its reason in description/stackTrace only; reporting text
        # alone yields a useless "Uncaught".
        $reason = if ($details.exception -and $details.exception.description) { $details.exception.description } else { $details.text }
        # Write to the error stream: Write-Output here would be captured by the caller's
        # assignment (or [void]) and the raw protocol message would be lost.
        [Console]::Error.WriteLine("RAW_FAILURE " + ($message | ConvertTo-Json -Depth 8 -Compress))
        throw ("evaluate failed: " + $reason)
      }
      return $message.result.result.value
    }
  }
  [void]$ws.Dispose()
  throw "CDP evaluate timed out"
}

# Generic CDP call (any method, not just Runtime.evaluate). Returns the RAW response text so
# callers can read whatever field they need (e.g. Page.captureScreenshot returns .result.data).
function Invoke-CdpMethod([string]$socketUrl, [string]$method, [string]$paramsJson, [int]$id, [int]$timeoutSec = 60) {
  $ws = New-Object System.Net.WebSockets.ClientWebSocket
  $token = [System.Threading.CancellationToken]::None
  [void]$ws.ConnectAsync([Uri]$socketUrl, $token).GetAwaiter().GetResult()
  $payload = '{"id":' + $id + ',"method":"' + $method + '","params":' + $paramsJson + '}'
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
  $segment = New-Object System.ArraySegment[byte] -ArgumentList @(,$bytes)
  [void]$ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $token).GetAwaiter().GetResult()
  $buffer = New-Object byte[] 8388608
  $builder = New-Object System.Text.StringBuilder
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    $receiveSegment = New-Object System.ArraySegment[byte] -ArgumentList @(,$buffer)
    $result = $ws.ReceiveAsync($receiveSegment, $token).GetAwaiter().GetResult()
    [void]$builder.Append([System.Text.Encoding]::UTF8.GetString($buffer, 0, $result.Count))
    if (-not $result.EndOfMessage) { continue }
    $text = $builder.ToString()
    [void]$builder.Clear()
    $message = $null
    try { $message = $text | ConvertFrom-Json } catch { continue }
    if ($message.id -eq $id) { [void]$ws.Dispose(); return $text }
  }
  [void]$ws.Dispose()
  throw "CDP method timed out: $method"
}
