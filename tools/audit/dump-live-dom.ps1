# Dumps the layout signature of the LIVE shell (WebView2, real data) for one scene and region.
#
# The shell exposes the Chrome DevTools Protocol when started with
#   --browser-args --remote-debugging-port=<port>
# which is how this script reads the real DOM instead of guessing from a screenshot.
#
# The extraction script is tools/audit/dom-signature.js; compare-dom.cjs runs the same script
# in Chromium against the static mockup page and diffs the two results, so "live shell vs
# visual mockup" becomes a machine-checkable list of geometry/typography differences.
#
# Keep this file ASCII-only.
param(
  [Parameter(Mandatory=$true)][string]$Exe,
  [Parameter(Mandatory=$true)][string]$Workspace,
  [Parameter(Mandatory=$true)][string]$Scene,
  [Parameter(Mandatory=$true)][string]$Out,
  # Accepts a comma-joined list; PowerShell -File turns "a,b" into a 2-element array, so both
  # shapes are flattened below.
  [string[]]$Selectors = @(".augit-window"),
  [string]$Theme = "dark",
  # Data rows differ between the static mockup (sample data) and the live shell (the real
  # repository), so their subtrees are recorded as containers only.
  [string]$Prune = ".tree-row,.commit-row,.code-line,.diff-row,.diff-row-pair,.blame-row,.history-row,.change-row,.search-result-row",
  # The comparison must run both sides with the SAME interface font (spec 151): the live shell
  # uses the user's saved settings, the static mockup uses the design baseline. These values are
  # injected as CSS variables for the comparison run only; no file is modified.
  [string]$FontFamily = "Microsoft YaHei UI",
  [string]$FontSize = "13px",
  [string]$CodeFontSize = "13px",
  [int]$Port = 9444,
  [int]$Width = 1180,
  [int]$Height = 760,
  [int]$SettleMs = 9000,
  # Optional: save the real window pixels (PrintWindow PW_RENDERFULLCONTENT) next to the dump,
  # so a comparison has both the DOM signature and the actual rendering of the same run.
  [string]$Capture = "",
  # Optional workspace-relative document to open before extracting. The shell restores the
  # previous session otherwise, and a restored binary document legitimately has no
  # encoding/line-ending facts in the status bar, which reads as a fake difference.
  [string]$Open = "",
  # Content switches the shell already supports. Document pages (blame / file history / diff /
  # conflict resolver) render an empty state without them, and comparing an empty live state
  # against a mockup full of sample content only measures the state difference, not fidelity.
  [string]$Blame = "",
  [string]$FileHistory = "",
  [string]$Diff = "",
  [string]$Conflict = "",
  # Optional query text typed into the overlay's input before extracting. Search/quick-open
  # pages render an EMPTY result list until a query arrives (spec: the overlay opens with an
  # empty query and focuses the input), so comparing result rows requires typing one.
  [string]$SetQuery = ""
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$signaturePath = Join-Path $PSScriptRoot "dom-signature.js"
if (-not (Test-Path -LiteralPath $signaturePath)) { Write-Output "MISSING_SIGNATURE $signaturePath"; exit 3 }

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class LiveDom {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static void Raise(IntPtr h) {
    uint pid; uint fg = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
    uint self = GetCurrentThreadId();
    bool attached = fg != 0 && fg != self && AttachThreadInput(self, fg, true);
    BringWindowToTop(h); SetForegroundWindow(h);
    if (attached) AttachThreadInput(self, fg, false);
  }
}
'@
try { [void][LiveDom]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }

. (Join-Path $PSScriptRoot 'cdp-eval.ps1')

# --- Stale web assets guard ----------------------------------------------------------------
# The shell loads its UI from the 'web' folder NEXT TO THE EXE, not from the repository
# sources. A stale copy silently produces evidence for an older UI (hit twice while building
# this script: a mockup.js fix looked broken because the copied tree predated it).
$exeWeb = Join-Path (Split-Path -Parent $Exe) "web"
$repoWeb = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "web"
if ((Test-Path -LiteralPath $exeWeb) -and (Test-Path -LiteralPath $repoWeb)) {
  $copied = (Get-ChildItem -LiteralPath $exeWeb -Recurse -File | Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
  $source = (Get-ChildItem -LiteralPath $repoWeb -Recurse -File | Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
  if ($source -gt $copied) {
    Write-Output "STALE_WEB_ASSETS source=$($source.ToString('s')) copied=$($copied.ToString('s'))"
    Write-Output "Rebuild the shell (dotnet build src/Augit.Shell/Augit.Shell.csproj -c Release) so the web copy beside the executable is refreshed."
    exit 6
  }
}

Get-Process Augit -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$launch = @("--workspace", $Workspace, "--scene", $Scene, "--pixel-exact", "--theme", $Theme, "--width", "$Width", "--height", "$Height", "--browser-args", "--remote-debugging-port=$Port")
if ($Open -ne "") { $launch += @("--open", $Open) }
if ($Blame -ne "") { $launch += @("--blame", $Blame) }
if ($FileHistory -ne "") { $launch += @("--file-history", $FileHistory) }
if ($Diff -ne "") { $launch += @("--diff", $Diff) }
if ($Conflict -ne "") { $launch += @("--conflict", $Conflict) }
$process = Start-Process -FilePath $Exe -ArgumentList $launch -PassThru
$handle = [IntPtr]::Zero
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline -and $handle -eq [IntPtr]::Zero) {
  Start-Sleep -Milliseconds 300
  try { $process.Refresh() } catch { }
  if ($process.MainWindowHandle -ne 0) { $handle = $process.MainWindowHandle }
}
if ($handle -eq [IntPtr]::Zero) { Write-Output "NO_WINDOW"; exit 2 }
Add-Type -AssemblyName System.Windows.Forms
$virtual = [System.Windows.Forms.SystemInformation]::VirtualScreen
[void][LiveDom]::MoveWindow($handle, $virtual.Left + 40, $virtual.Top + 40, $Width, $Height, $true)
[LiveDom]::Raise($handle)
Start-Sleep -Milliseconds $SettleMs

$socket = Get-CdpSocket $Port
# The scene may still be filling in Git data; wait for the readiness flags the shell sets.
[void](Invoke-Cdp $socket "new Promise(r=>{const t=Date.now();const t2=setInterval(()=>{if((window.__augitReady&&window.__augitGitReady&&window.__augitHistoryReady)||Date.now()-t>25000){clearInterval(t2);r(true)}},200)})" 1 40)

# Read as UTF-8 explicitly: Windows PowerShell 5.1 decodes a BOM-less file with the ANSI code
# page, which turned the extractor's comment text into mojibake and made the injected script a
# SyntaxError ("Unexpected token 'const'") on the first run of this script.
if ($SetQuery -ne "") {
  $queryJson = ConvertTo-Json $SetQuery -Compress
  $typed = Invoke-Cdp $socket "(()=>{const i=document.querySelector('.search-overlay .search-field');if(!i)return 'no-input';i.value=$queryJson;i.dispatchEvent(new Event('input',{bubbles:true}));return 'typed:'+i.value})()" 8
  Write-Output ("SET_QUERY " + $typed)
  Start-Sleep -Milliseconds 1500
  [void](Invoke-Cdp $socket "new Promise(r=>{const t=Date.now();const i=setInterval(()=>{if(window.__augitSearchReady||Date.now()-t>8000){clearInterval(i);r(true)}},200)})" 9 20)
}

$signature = Get-Content -Raw -Encoding UTF8 -LiteralPath $signaturePath
# Equalize typography through the page's OWN pipeline instead of injecting CSS variables:
# the interface font/size come from the user's saved settings on the live side and from the
# design baseline on the mockup side, and the derived heights (title/tab/tree/status) are
# recomputed from the real font metrics. Injecting variables directly would equalize the text
# but leave those derived sizes at the wrong value, producing fake differences.
# URLSearchParams encodes the values itself: passing an already-escaped family would be encoded
# a second time and the page would receive the percent-encoded text as the family name
# (observed as "Microsoft%20YaHei%20UI..." in the computed font).
$familyLiteral = $FontFamily -replace '["'']', ''
$equalize = "(()=>{const q=new URLSearchParams(location.search);q.set('ui-size','$([int]($FontSize -replace 'px',''))');q.set('code-size','$([int]($CodeFontSize -replace 'px',''))');q.set('ui-family','$familyLiteral');q.set('code-family','Cascadia Mono');location.search=q.toString();return true})()"
[void](Invoke-Cdp $socket $equalize 5)
Start-Sleep -Milliseconds 1500
# The navigation is a real page load: wait for the shell to report ready again.
[void](Invoke-Cdp $socket "new Promise(r=>{const t=Date.now();const t2=setInterval(()=>{if((window.__augitReady&&window.__augitGitReady&&window.__augitHistoryReady)||Date.now()-t>25000){clearInterval(t2);r(true)}},200)})" 6 40)
$effective = Invoke-Cdp $socket "getComputedStyle(document.documentElement).fontSize + '|' + getComputedStyle(document.body).fontFamily" 7
Write-Output ("TYPOGRAPHY " + $effective)
# NOTE: PowerShell variable names are case-insensitive, so a local named $selectors would BE
# the $Selectors parameter - assigning @() to it wiped the argument before enumeration and the
# dump silently contained nothing (regions=0). Use a distinct name.
$regionSelectors = @()
foreach ($entry in $Selectors) {
  foreach ($piece in ([string]$entry).Split(',')) {
    if ($piece.Trim() -ne "") { $regionSelectors += $piece.Trim() }
  }
}
# The signature travels as a JSON STRING, not as a returned object graph: a deep result
# (hundreds of nodes x ~40 fields) has to survive CDP -> PowerShell -> JSON again, and that
# round trip is where this script first failed with a contentless "Uncaught". A plain string
# is transported verbatim, and the report is assembled as text.
$id = 10
$parts = New-Object System.Collections.ArrayList
foreach ($selector in $regionSelectors) {
  $trimmed = $selector.Trim()
  if ($trimmed -eq "") { continue }
  $selectorJson = ConvertTo-Json $trimmed -Compress
  $pruneJson = ConvertTo-Json $Prune -Compress
  $expression = "JSON.stringify((" + $signature + ")(" + $selectorJson + ",{prune:" + $pruneJson + "}))"
  $rows = Invoke-Cdp $socket $expression $id
  $id++
  if ($null -eq $rows -or $rows -eq "") { $rows = "null" }
  [void]$parts.Add((ConvertTo-Json $trimmed -Compress) + ":" + $rows)
}
$errors = Invoke-Cdp $socket "JSON.stringify(window.__augitErrors||[])" $id
$document = '{"scene":' + (ConvertTo-Json $Scene -Compress) `
  + ',"theme":' + (ConvertTo-Json $Theme -Compress) `
  + ',"width":' + $Width + ',"height":' + $Height `
  + ',"errors":' + (ConvertTo-Json $errors -Compress) `
  + ',"prune":' + (ConvertTo-Json $Prune -Compress) `
  + ',"fontFamily":' + (ConvertTo-Json $FontFamily -Compress) `
  + ',"fontSize":' + (ConvertTo-Json $FontSize -Compress) `
  + ',"regions":{' + ($parts -join ",") + '}}'

if ($Capture -ne "") {
  $rect = New-Object LiveDom+RECT
  if ([LiveDom]::GetWindowRect($handle, [ref]$rect)) {
    $width = $rect.R - $rect.L
    $height = $rect.B - $rect.T
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    # PrintWindow with PW_RENDERFULLCONTENT returns the composited WebView2 frame even when the
    # window is not the foreground window, so the evidence is reproducible unattended.
    [void][LiveDom]::PrintWindow($handle, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $captureDir = Split-Path -Parent $Capture
    if ($captureDir -and -not (Test-Path -LiteralPath $captureDir)) { New-Item -ItemType Directory -Path $captureDir -Force | Out-Null }
    $bitmap.Save($Capture, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
    Write-Output ("CAPTURED " + $Capture + " " + $width + "x" + $height)
  } else {
    Write-Output "CAPTURE_FAILED no-window-rect"
  }
}

$outDir = Split-Path -Parent $Out
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
# Write UTF-8 WITHOUT a BOM: Set-Content -Encoding UTF8 on Windows PowerShell adds one, and the
# Node side then fails with "Unexpected token '\ufeff'".
[System.IO.File]::WriteAllText($Out, $document, (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("SAVED " + $Out + " regions=" + $parts.Count + " errors=" + $errors)
try { if (-not $process.HasExited) { $process.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 800; if (-not $process.HasExited) { $process.Kill() } } } catch { }
Write-Output "DONE"
