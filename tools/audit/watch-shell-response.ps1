# 只向本次实例发送无操作消息，观察原生窗口线程响应，不激活或修改窗口。
param([Parameter(Mandatory=$true)][int]$ExpectedProcessId)
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ShellResponseProbe {
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SendMessageTimeout(IntPtr h, uint m, UIntPtr w, IntPtr l, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint processId);
}
'@
$watch=[Diagnostics.Stopwatch]::StartNew()
while($watch.Elapsed.TotalSeconds -lt 180){
 $instance=Get-Process -Id $ExpectedProcessId -ErrorAction SilentlyContinue
 if(-not $instance){break}
 $target=$instance.MainWindowHandle
 [uint32]$owner=0
 if($target -ne [IntPtr]::Zero){
  [void][ShellResponseProbe]::GetWindowThreadProcessId($target,[ref]$owner)
  if($owner -ne $ExpectedProcessId){throw '响应探针窗口身份不符'}
  $utc=[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
  $elapsed=[Diagnostics.Stopwatch]::StartNew()
  [UIntPtr]$result=[UIntPtr]::Zero
  $ok=[ShellResponseProbe]::SendMessageTimeout($target,0,[UIntPtr]::Zero,[IntPtr]::Zero,2,100,[ref]$result)
  $elapsed.Stop()
  [pscustomobject]@{utcMilliseconds=$utc;window=$target.ToInt64();processId=$owner;elapsedMilliseconds=$elapsed.Elapsed.TotalMilliseconds;responded=($ok -ne [IntPtr]::Zero)} | ConvertTo-Json -Compress
 }
 Start-Sleep -Milliseconds 50
}
