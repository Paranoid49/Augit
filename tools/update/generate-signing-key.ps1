param(
    [string]$OutputDirectory = 'artifacts/update-signing'
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$ecdsa = [System.Security.Cryptography.ECDsa]::Create(
    [System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
try {
    $privateKey = [Convert]::ToBase64String($ecdsa.ExportPkcs8PrivateKey())
    $publicKey = [Convert]::ToBase64String($ecdsa.ExportSubjectPublicKeyInfo())
} finally {
    $ecdsa.Dispose()
}

$privatePath = Join-Path $OutputDirectory 'update-private.pk8.b64'
$publicPath = Join-Path $OutputDirectory 'update-public.spki.b64'
[System.IO.File]::WriteAllText($privatePath, $privateKey, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($publicPath, $publicKey, [System.Text.UTF8Encoding]::new($false))

Write-Host "Private key (set as GitHub secret AUGIT_UPDATE_SIGNING_KEY): $privatePath"
Write-Host "Public key (embed in UpdateTrust.cs): $publicPath"
Write-Host "Never commit the private key. Store it in a password manager and the GitHub secret only."
