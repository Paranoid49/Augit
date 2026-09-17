# Encoding guard for the PowerShell scripts in this repo.
# Two mistakes have already been made here:
#   1. tools/release.ps1 carries non-ASCII text, so it MUST keep its UTF-8 BOM;
#      without the BOM PowerShell 5.1 decodes it as ANSI and stops parsing.
#   2. tools/audit/*.ps1 are ASCII-only on purpose; one non-ASCII byte makes
#      PowerShell 5.1 mis-decode them and break parsing.
# BOM-less + non-ASCII is the combination that fails, so this script rejects it.
# NOTE: keep this file ASCII-only too, otherwise it flags itself.
$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$failures = @()

$scripts = Get-ChildItem -LiteralPath (Join-Path $repo 'tools') -Recurse -Filter '*.ps1' -File |
    Where-Object { $_.FullName -notlike '*\ripgrep\*' }

foreach ($script in $scripts) {
    $bytes = [System.IO.File]::ReadAllBytes($script.FullName)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $nonAscii = 0
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -gt 127) { $nonAscii++ }
    }

    # Decode the file the way PowerShell 5.1 would (BOM decides, otherwise ANSI)
    # and parse it for real. Measured: the same bytes parse with 0 errors when a
    # BOM is present and fail with "unexpected token '}'" when it is not.
    $tokens = $null
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$parseErrors)

    $relative = $script.FullName.Substring($repo.Length).TrimStart('\', '/')
    Write-Output ("{0,-42} bom={1} nonAsciiBytes={2} parseErrors={3}" -f $relative, $hasBom, $nonAscii, $parseErrors.Count)
    if (-not $hasBom -and $nonAscii -gt 0) {
        $failures += "$relative has $nonAscii non-ASCII bytes without a BOM; PowerShell 5.1 would decode it as ANSI and fail to parse."
    }
    foreach ($parseError in $parseErrors) {
        # ErrorId is ASCII, Message is localized; print the ASCII one for stable output.
        $failures += "$relative does not parse under PowerShell 5.1: line $($parseError.Extent.StartLineNumber) $($parseError.ErrorId) $($parseError.Extent.Text)"
    }
}

if ($failures.Count -gt 0) {
    Write-Output ''
    foreach ($failure in $failures) { Write-Output "FAIL: $failure" }
    Write-Output "PASS:False $($failures.Count) script(s) violate the encoding rule."
    exit 1
}

Write-Output "PASS: all $($scripts.Count) PowerShell scripts honour the encoding rule (BOM-less files stay ASCII)."
