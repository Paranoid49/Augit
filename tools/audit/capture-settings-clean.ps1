param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][string]$Output
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CleanDesk {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
    public static IntPtr Find(uint pid, string part) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h)) {
                var text = new StringBuilder(512); GetWindowText(h, text, 512);
                if (text.ToString().Contains(part)) { found = h; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
[CleanDesk]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
$settings = [IntPtr]::Zero
for ($attempt = 0; $attempt -lt 80; $attempt++) {
    $settings = [CleanDesk]::Find([uint32]$ProcessId, 'Settings')
    if ($settings -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 250
}
if ($settings -eq [IntPtr]::Zero) { throw 'Settings window not found' }
[CleanDesk]::SetWindowPos($settings, [IntPtr]::Zero, 530, 193, 1662, 1225, 0x0004) | Out-Null
$rect = New-Object CleanDesk+RECT
[CleanDesk]::GetWindowRect($settings, [ref]$rect) | Out-Null
Start-Sleep -Milliseconds 1000
$bitmap = New-Object System.Drawing.Bitmap(2880, 1716)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size(2880, 1716)))
$graphics.Dispose()
$foreground = Join-Path $Output 'foreground.png'
$bitmap.Save($foreground, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
[CleanDesk]::PostMessage($settings, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
$closed = $false
for ($index = 0; $index -lt 40; $index++) {
    Start-Sleep -Milliseconds 250
    if ([CleanDesk]::Find([uint32]$ProcessId, 'Settings') -eq [IntPtr]::Zero) { $closed = $true; break }
}
Start-Sleep -Milliseconds 700
$bitmap = New-Object System.Drawing.Bitmap(2880, 1716)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size(2880, 1716)))
$graphics.Dispose()
$background = Join-Path $Output 'background.png'
$bitmap.Save($background, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
[pscustomobject]@{
    settingsHandle = $settings.ToInt64()
    left = $rect.Left
    top = $rect.Top
    right = $rect.Right
    bottom = $rect.Bottom
    width = $rect.Right - $rect.Left
    height = $rect.Bottom - $rect.Top
    settingsClosed = $closed
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'window.json') -Encoding UTF8
