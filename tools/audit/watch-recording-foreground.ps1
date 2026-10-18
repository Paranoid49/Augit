# 只读记录系统前景身份，不激活窗口或发送输入；由启动者结束本采样进程。
$ErrorActionPreference = "Stop"
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class RecordingForegroundRead {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
'@
while ($true) {
    $window = [RecordingForegroundRead]::GetForegroundWindow()
    [uint32]$foregroundProcess = 0
    $null = [RecordingForegroundRead]::GetWindowThreadProcessId($window, [ref]$foregroundProcess)
    $sample = @{utcMilliseconds=[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds();window=$window.ToInt64();processId=$foregroundProcess}
    [Console]::Out.WriteLine(($sample | ConvertTo-Json -Compress))
    [Console]::Out.Flush()
    Start-Sleep -Milliseconds 50
}
