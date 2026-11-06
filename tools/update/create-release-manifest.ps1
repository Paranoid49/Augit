param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$Tag = '',
    [string]$Repository = 'Paranoid49/Augit',
    [string]$NotesUrl = '',
    [string]$PrivateKeyBase64 = $env:AUGIT_UPDATE_SIGNING_KEY
)

$ErrorActionPreference = 'Stop'
if ($Tag -eq '') { $Tag = "v$Version" }
if ($NotesUrl -eq '') { $NotesUrl = "https://github.com/$Repository/releases/tag/$Tag" }
if ($PrivateKeyBase64 -eq '') {
    throw 'AUGIT_UPDATE_SIGNING_KEY is required to sign update.json.'
}
if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
    throw "Portable zip missing: $ZipPath"
}

$zip = Get-Item -LiteralPath $ZipPath
$hash = (Get-FileHash -LiteralPath $zip.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$assetName = $zip.Name
$url = "https://github.com/$Repository/releases/download/$Tag/$assetName"
$manifest = [ordered]@{
    schema = 1
    version = $Version
    tag = $Tag
    asset = $assetName
    url = $url
    size = $zip.Length
    sha256 = $hash
    notesUrl = $NotesUrl
    publishedAt = (Get-Date).ToUniversalTime().ToString('o')
}
$json = $manifest | ConvertTo-Json -Compress
$jsonBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($json)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$manifestPath = Join-Path $OutputDirectory 'update.json'
[System.IO.File]::WriteAllBytes($manifestPath, $jsonBytes)

$ecdsa = [System.Security.Cryptography.ECDsa]::Create()
try {
    $privateBytes = [Convert]::FromBase64String($PrivateKeyBase64.Trim())
    $bytesRead = 0
    $ecdsa.ImportPkcs8PrivateKey($privateBytes, [ref]$bytesRead)
    $signature = $ecdsa.SignData(
        $jsonBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
} finally {
    $ecdsa.Dispose()
}
$signaturePath = Join-Path $OutputDirectory 'update.json.sig'
[System.IO.File]::WriteAllText(
    $signaturePath,
    [Convert]::ToBase64String($signature) + "`n",
    [System.Text.UTF8Encoding]::new($false))
Write-Host "update manifest: $manifestPath"
Write-Host "update signature: $signaturePath"
Write-Host "sha256: $hash size: $($zip.Length)"
