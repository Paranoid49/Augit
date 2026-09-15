# Verify that the runtime UI assets (web/src) stay byte-identical to the visual mockups
# (docs/ux-mockups). The mockups are the design baseline and share the same CSS/scene code,
# so divergence would break the "mockups are the product code" contract.
# ASCII only: PowerShell 5.1 reads .ps1 as ANSI.
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$pairs = @(
  @{ Name = "mockup.js";        Runtime = "web\src\mockup.js";        Mockup = "docs\ux-mockups\mockup.js" },
  @{ Name = "mockup.css";       Runtime = "web\src\mockup.css";       Mockup = "docs\ux-mockups\mockup.css" },
  @{ Name = "current-find.js";  Runtime = "web\src\current-find.js";  Mockup = "docs\ux-mockups\current-find.js" },
  @{ Name = "image-preview.js"; Runtime = "web\src\image-preview.js"; Mockup = "docs\ux-mockups\image-preview.js" }
)
$failed = 0
foreach ($pair in $pairs) {
  $runtimePath = Join-Path $repo $pair.Runtime
  $mockupPath = Join-Path $repo $pair.Mockup
  if (-not (Test-Path -LiteralPath $runtimePath)) { Write-Output ("MISSING " + $pair.Runtime); $failed++; continue }
  if (-not (Test-Path -LiteralPath $mockupPath)) { Write-Output ("MISSING " + $pair.Mockup); $failed++; continue }
  $runtimeHash = (Get-FileHash -LiteralPath $runtimePath -Algorithm SHA256).Hash
  $mockupHash = (Get-FileHash -LiteralPath $mockupPath -Algorithm SHA256).Hash
  if ($runtimeHash -eq $mockupHash) {
    Write-Output ("OK      " + $pair.Name)
  } else {
    Write-Output ("DRIFT   " + $pair.Name + " : " + $pair.Runtime + " != " + $pair.Mockup)
    $failed++
  }
}
if ($failed -gt 0) {
  Write-Output ("FAILED: " + $failed + " file(s) differ. Copy the mockup version into web/src.")
  exit 1
}
Write-Output "PASS: runtime UI assets match the visual mockups."
