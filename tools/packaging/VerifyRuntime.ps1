param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [Parameter(Mandatory = $true)]
    [ValidateSet('SHA256', 'SHA512')]
    [string]$Algorithm,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedHash,

    [long]$ExpectedSize = 0
)

if ($ExpectedSize -gt 0 -and (Get-Item -LiteralPath $Path).Length -ne $ExpectedSize) {
    exit 9
}

$hashAlgorithm = if ($Algorithm -eq 'SHA256') {
    [Security.Cryptography.SHA256]::Create()
} else {
    [Security.Cryptography.SHA512]::Create()
}
$stream = [IO.File]::OpenRead($Path)
try {
    $actualHash = [BitConverter]::ToString($hashAlgorithm.ComputeHash($stream)).Replace('-', '')
} finally {
    $stream.Dispose()
    $hashAlgorithm.Dispose()
}
if (-not $actualHash.Equals($ExpectedHash, [StringComparison]::OrdinalIgnoreCase)) {
    exit 10
}

$env:PSModulePath = Join-Path $PSHOME 'Modules'
Import-Module Microsoft.PowerShell.Security -ErrorAction Stop
$signature = Microsoft.PowerShell.Security\Get-AuthenticodeSignature -LiteralPath $Path
if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid) {
    exit 11
}

if ($null -eq $signature.SignerCertificate -or
    $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {
    exit 12
}

exit 0
