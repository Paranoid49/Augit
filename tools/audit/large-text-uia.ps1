param(
    [Parameter(Mandatory=$true)][int]$ProcessId,
    [Parameter(Mandatory=$true)][string]$Out
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class UiaKids {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
}
'@
function Scan([IntPtr]$handle, [string]$label) {
    $started = [DateTime]::UtcNow
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $names = 0
    $nameChars = 0
    $hits = @()
    $controls = @{}
    for ($i = 0; $i -lt $all.Count; $i++) {
        $element = $all.Item($i)
        $type = $element.Current.ControlType.ProgrammaticName
        $controls[$type] = 1 + [int]$controls[$type]
        $name = $element.Current.Name
        if (-not [string]::IsNullOrEmpty($name)) {
            $names++
            $nameChars += $name.Length
            if ($name.Contains('line 79999') -or $name.Contains('Read-only document text') -or $name.Contains('79999')) {
                $hits += [pscustomobject]@{ length = $name.Length; controlType = $type; prefix = $name.Substring(0, [Math]::Min(80, $name.Length)) }
            }
        }
    }
    return [ordered]@{
        label = $label
        handle = [int64]$handle
        elapsedMs = [math]::Round(([DateTime]::UtcNow - $started).TotalMilliseconds, 1)
        descendants = $all.Count
        namedElements = $names
        namedChars = $nameChars
        controlTypes = $controls
        hits = $hits
    }
}
$process = Get-Process -Id $ProcessId
$main = $process.MainWindowHandle
$results = @()
$results += Scan $main 'main-window'
$renderHost = [IntPtr]::Zero
$callback = [UiaKids+EnumProc]{
    param($h, $l)
    $class = New-Object System.Text.StringBuilder 256
    [void][UiaKids]::GetClassName($h, $class, 256)
    if ($class.ToString() -eq 'Chrome_RenderWidgetHostHWND') { $script:renderHost = $h; return $false }
    return $true
}
[void][UiaKids]::EnumChildWindows($main, $callback, [IntPtr]::Zero)
if ($renderHost -ne [IntPtr]::Zero) { $results += Scan $renderHost 'render-widget-host' }
$result = [ordered]@{ processId = $ProcessId; mainWindow = [int64]$main; scannedAt = (Get-Date).ToString('o'); results = $results }
[IO.File]::WriteAllText($Out, (($result | ConvertTo-Json -Depth 8) + "`n"), [Text.UTF8Encoding]::new($false))
$result | ConvertTo-Json -Depth 8
