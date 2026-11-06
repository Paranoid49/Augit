param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$SignaturePath,
    [string]$PublicKeyBase64 = 'MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE+XRC/DPqAQoPdK4k15nw0NIuQ/9bCTJi1I0YUw7WWeaNOtlmWbAg0Dusq8CuNinblCjEye59hZdbI1HHD/EP2w=='
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) { throw "Manifest missing: $ManifestPath" }
if (-not (Test-Path -LiteralPath $SignaturePath -PathType Leaf)) { throw "Signature missing: $SignaturePath" }
$manifestBytes = [System.IO.File]::ReadAllBytes($ManifestPath)
$signatureBytes = [Convert]::FromBase64String((Get-Content -LiteralPath $SignaturePath -Raw).Trim())
$ecdsa = [System.Security.Cryptography.ECDsa]::Create()
try {
    $read = 0
    $ecdsa.ImportSubjectPublicKeyInfo([Convert]::FromBase64String($PublicKeyBase64), [ref]$read)
    $valid = $ecdsa.VerifyData(
        $manifestBytes,
        $signatureBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
} finally {
    $ecdsa.Dispose()
}
if (-not $valid) { throw 'Update manifest signature verification failed.' }
Write-Host 'Update manifest signature: OK'
