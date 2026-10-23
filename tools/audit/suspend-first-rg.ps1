param(
    [Parameter(Mandatory=$true)][int]$ParentProcessId,
    [Parameter(Mandatory=$true)][string]$Out,
    [int]$DelayMilliseconds = 100,
    [int]$HoldMilliseconds = 17000,
    [int]$WaitMilliseconds = 15000
)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class RgStall {
    [DllImport("ntdll.dll")] public static extern int NtSuspendProcess(IntPtr process);
}
'@
$started = [DateTime]::UtcNow
$deadline = $started.AddMilliseconds($WaitMilliseconds)
$result = [ordered]@{ parentProcessId = $ParentProcessId; startedUtc = $started.ToString('o'); suspended = $null; found = $false; reason = '' }
while ([DateTime]::UtcNow -lt $deadline) {
    # WMI 查询本身可达数百毫秒，会错过短命 rg；先快速取进程，再按本批唯一 rg 处理。
    $process = Get-Process -Name rg -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($process) {
        $result.found = $true
        $result.pid = [int]$process.Id
        $result.foundUtc = [DateTime]::UtcNow.ToString('o')
        if ($DelayMilliseconds -gt 0) { Start-Sleep -Milliseconds $DelayMilliseconds }
        $process = Get-Process -Id $result.pid -ErrorAction SilentlyContinue
        if ($process) {
            $status = [RgStall]::NtSuspendProcess($process.Handle)
            $result.suspended = ($status -eq 0)
            $result.suspendStatus = $status
            $result.suspendedUtc = [DateTime]::UtcNow.ToString('o')
        } else {
            $result.reason = 'rg-exited-before-suspend'
        }
        break
    }
    Start-Sleep -Milliseconds 5
}
if (-not $result.found) { $result.reason = 'rg-not-found' }
[IO.File]::WriteAllText($Out, (($result | ConvertTo-Json) + "`n"), [Text.UTF8Encoding]::new($false))
if ($result.suspended) { Start-Sleep -Milliseconds $HoldMilliseconds }
