param(
    [string]$OutputDirectory = 'artifacts',
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'

function Assert-LockedRuntimeAsset {
    param(
        [string]$Path,
        [long]$ExpectedSize,
        [string]$ExpectedHash
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "锁定的运行时资产不存在：$Path"
    }
    $item = Get-Item -LiteralPath $Path
    if ($item.Length -ne $ExpectedSize) {
        throw "锁定的运行时资产大小不匹配：$Path"
    }
    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not $actualHash.Equals($ExpectedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "锁定的运行时资产哈希不匹配：$Path"
    }
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}
$repositoryPrefix = $repositoryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $outputRoot.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '发布输出目录必须位于项目目录内。'
}
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw '版本号格式无效。'
}

$stagingRoot = Join-Path $outputRoot 'staging'
$portableRoot = Join-Path $stagingRoot 'Augit'
$packageRoot = Join-Path $stagingRoot 'packages'
$portableArchive = Join-Path $outputRoot "Augit-$Version-win-x64-portable.zip"
$installerPath = Join-Path $outputRoot "Augit-$Version-win-x64-setup.exe"
$checksumsPath = Join-Path $outputRoot 'SHA256SUMS.txt'
$stagedPortableArchive = Join-Path $packageRoot ([System.IO.Path]::GetFileName($portableArchive))
$stagedInstallerPath = Join-Path $packageRoot ([System.IO.Path]::GetFileName($installerPath))
$stagedChecksumsPath = Join-Path $packageRoot ([System.IO.Path]::GetFileName($checksumsPath))
$runtimeAssetLockPath = Join-Path $repositoryRoot 'tools\runtime-assets.lock.json'
$runtimeAssetLock = Get-Content -LiteralPath $runtimeAssetLockPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($runtimeAssetLock.version -ne 1 -or @($runtimeAssetLock.assets).Count -eq 0) {
    throw '运行时资产锁文件版本无效或没有资产。'
}
foreach ($asset in $runtimeAssetLock.assets) {
    $sourcePath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $asset.sourcePath))
    if (-not $sourcePath.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "锁定的运行时资产越过项目目录：$($asset.sourcePath)"
    }
    Assert-LockedRuntimeAsset $sourcePath $asset.size $asset.sha256
}

if (Test-Path -LiteralPath $stagingRoot) {
    [System.IO.Directory]::Delete($stagingRoot, $true)
}
New-Item -ItemType Directory -Path $portableRoot -Force | Out-Null
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

try {
    & dotnet restore (Join-Path $repositoryRoot 'Augit.slnx') --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw '解决方案依赖还原失败。'
    }

    $packagingProject = Join-Path $repositoryRoot 'tools\packaging\Augit.Packaging.csproj'
    & dotnet restore $packagingProject --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw '安装器构建依赖还原失败。'
    }

    & dotnet publish (Join-Path $repositoryRoot 'src\Augit.App\Augit.App.csproj') `
        -c Release `
        -r win-x64 `
        --self-contained false `
        --no-restore `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:Version=$Version `
        -o $portableRoot
    if ($LASTEXITCODE -ne 0) {
        throw 'Augit 发布失败。'
    }

    $webViewDocumentation = Join-Path $portableRoot 'Microsoft.Web.WebView2.Core.xml'
    if (Test-Path -LiteralPath $webViewDocumentation) {
        [System.IO.File]::Delete($webViewDocumentation)
    }

    $requiredFiles = @(
        'Augit.exe',
        'Augit.dll',
        'Augit.Core.dll',
        'Augit.Infrastructure.dll',
        'Augit.deps.json',
        'Augit.runtimeconfig.json',
        'native\Scintilla.dll',
        'native\WebView2Loader.dll',
        'terminal\index.html',
        'terminal\terminal.js',
        'terminal\xterm.js',
        'terminal\xterm.css',
        'terminal\addon-fit.js',
        'tools\rg.exe',
        'licenses\InnoSetup-LICENSE.txt',
        'licenses\Markdig-LICENSE.txt',
        'licenses\Microsoft.Web.WebView2-LICENSE.txt',
        'licenses\Microsoft.Web.WebView2-NOTICE.txt',
        'licenses\ripgrep-LICENSE-MIT.txt',
        'licenses\ripgrep-UNLICENSE.txt',
        'licenses\Scintilla-LICENSE.txt',
        'licenses\xterm-addon-fit-LICENSE.txt',
        'licenses\xterm-LICENSE.txt',
        'THIRD-PARTY-NOTICES.md',
        'README.md'
    )
    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $portableRoot $relativePath))) {
            throw "发布产物缺少 $relativePath。"
        }
    }

    foreach ($asset in $runtimeAssetLock.assets) {
        $publishedPath = [System.IO.Path]::GetFullPath((Join-Path $portableRoot $asset.publishPath))
        $portablePrefix = $portableRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
            [System.IO.Path]::DirectorySeparatorChar
        if (-not $publishedPath.StartsWith($portablePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "锁定的发布资产越过发布目录：$($asset.publishPath)"
        }
        Assert-LockedRuntimeAsset $publishedPath $asset.size $asset.sha256
    }

    $forbiddenFiles = @('coreclr.dll', 'hostfxr.dll')
    foreach ($relativePath in $forbiddenFiles) {
        if (Test-Path -LiteralPath (Join-Path $portableRoot $relativePath)) {
            throw "发布产物意外包含 self-contained 运行时文件 $relativePath。"
        }
    }

    if (@(Get-ChildItem -LiteralPath $portableRoot -Recurse -File -Filter '*.pdb').Count -ne 0) {
        throw '发布产物不应包含调试符号。'
    }

    $runtimeConfig = Get-Content -Raw -Encoding UTF8 (Join-Path $portableRoot 'Augit.runtimeconfig.json') |
        ConvertFrom-Json
    if ($runtimeConfig.runtimeOptions.framework.name -ne 'Microsoft.NETCore.App') {
        throw '发布产物必须只依赖基础 .NET Runtime。'
    }

    Compress-Archive -LiteralPath $portableRoot -DestinationPath $stagedPortableArchive -CompressionLevel Optimal

    & dotnet msbuild $packagingProject `
        -nologo `
        -t:BuildInstaller `
        -p:PublishDir=$portableRoot `
        -p:InstallerOutputDir=$packageRoot `
        -p:PackageVersion=$Version
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $stagedInstallerPath)) {
        throw '联网安装器生成失败。'
    }

    $checksumLines = @($stagedPortableArchive, $stagedInstallerPath) | ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([System.IO.Path]::GetFileName($_))"
    }
    [System.IO.File]::WriteAllText(
        $stagedChecksumsPath,
        ($checksumLines -join "`n") + "`n",
        [System.Text.UTF8Encoding]::new($false))

    [System.IO.File]::Copy($stagedPortableArchive, $portableArchive, $true)
    [System.IO.File]::Copy($stagedInstallerPath, $installerPath, $true)
    [System.IO.File]::Copy($stagedChecksumsPath, $checksumsPath, $true)
} finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        [System.IO.Directory]::Delete($stagingRoot, $true)
    }
}
