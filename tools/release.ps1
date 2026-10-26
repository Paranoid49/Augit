param(
    [string]$OutputDirectory = 'artifacts',
    [string]$Version = '1.0.0-rc.4',
    [string]$RuntimeFrameworkVersion = '10.0.11'
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

function Copy-DistributionDocuments {
    param([string]$Root)

    # 分发产物必须自带用户说明、项目许可证和第三方依赖说明/许可证原文，这些文件不在发布目录里，需要单独复制。
    foreach ($documentName in @('THIRD-PARTY-NOTICES.md', 'README.md', 'LICENSE')) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $documentName) `
            -Destination (Join-Path $Root $documentName)
    }

    $webViewDocumentation = Join-Path $Root 'Microsoft.Web.WebView2.Core.xml'
    if (Test-Path -LiteralPath $webViewDocumentation) {
        [System.IO.File]::Delete($webViewDocumentation)
    }
}

function Assert-DistributionFiles {
    param([string]$Root, [string]$Label)

    $requiredFiles = @(
        'Augit.exe',
        'Augit.dll',
        'Augit.Core.dll',
        'Augit.Infrastructure.dll',
        'Augit.deps.json',
        'Augit.runtimeconfig.json',
        'native\WebView2Loader.dll',
        'web\index.html',
        'web\src\mockup.js',
        'web\src\mockup.css',
        'web\src\live-data.js',
        'web\src\bridge.js',
        'web\vendor\xterm\xterm.js',
        'web\vendor\xterm\xterm.css',
        'web\vendor\xterm\addon-fit.js',
        'web\vendor\jetbrains-mono\JetBrainsMono-Regular.woff2',
        'web\vendor\jetbrains-mono\JetBrainsMono-Bold.woff2',
        'web\vendor\jetbrains-mono\JetBrainsMono-Italic.woff2',
        'web\vendor\jetbrains-mono\JetBrainsMono-BoldItalic.woff2',
        'web\vendor\jetbrains-mono\OFL.txt',
        'tools\rg.exe',
        'licenses\ripgrep-LICENSE-MIT.txt',
        'licenses\ripgrep-UNLICENSE.txt',
        'licenses\Microsoft.Web.WebView2-LICENSE.txt',
        'licenses\Microsoft.Web.WebView2-NOTICE.txt',
        'licenses\xterm-LICENSE.txt',
        'licenses\xterm-addon-fit-LICENSE.txt',
        'licenses\JetBrainsMono-OFL.txt',
        'THIRD-PARTY-NOTICES.md',
        'README.md',
        'LICENSE'
    )
    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $Root $relativePath))) {
            throw "$Label 缺少 $relativePath。"
        }
    }

    foreach ($asset in $runtimeAssetLock.assets) {
        $publishedPath = [System.IO.Path]::GetFullPath((Join-Path $Root $asset.publishPath))
        $rootPrefix = $Root.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
            [System.IO.Path]::DirectorySeparatorChar
        if (-not $publishedPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "$Label 的锁定资产越过发布目录：$($asset.publishPath)"
        }
        Assert-LockedRuntimeAsset $publishedPath $asset.size $asset.sha256
    }

    if (@(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter '*.pdb').Count -ne 0) {
        throw "$Label 不应包含调试符号。"
    }
}

function Assert-FrameworkDependentPublish {
    param([string]$Root, [string]$Label)

    foreach ($relativePath in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')) {
        if (Test-Path -LiteralPath (Join-Path $Root $relativePath)) {
            throw "$Label 意外包含 self-contained 运行时文件 $relativePath。"
        }
    }

    $runtimeConfig = Get-Content -Raw -Encoding UTF8 (Join-Path $Root 'Augit.runtimeconfig.json') |
        ConvertFrom-Json
    if ($runtimeConfig.runtimeOptions.framework.name -ne 'Microsoft.NETCore.App') {
        throw "$Label 必须只依赖基础 .NET Runtime。"
    }
}

function Assert-SelfContainedPublish {
    param([string]$Root, [string]$Label)

    foreach ($relativePath in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Root $relativePath))) {
            throw "$Label 缺少 self-contained 运行时文件 $relativePath。"
        }
    }

    $runtimeConfig = Get-Content -Raw -Encoding UTF8 (Join-Path $Root 'Augit.runtimeconfig.json') |
        ConvertFrom-Json
    $includedFrameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
    if (@($includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' }).Count -eq 0) {
        throw "$Label 必须包含 Microsoft.NETCore.App 运行时。"
    }
    if ($runtimeConfig.runtimeOptions.framework) {
        throw "$Label 不应依赖共享 .NET 框架。"
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
if ($RuntimeFrameworkVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw 'self-contained 运行时版本号格式无效。'
}

$stagingRoot = Join-Path $outputRoot 'staging'
$portableRoot = Join-Path $stagingRoot 'Augit'
$installerRoot = Join-Path $stagingRoot 'installer'
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
New-Item -ItemType Directory -Path $installerRoot -Force | Out-Null
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

    # 安装版保持 framework-dependent：安装器负责检测/下载 .NET 10，保持较小的联网安装包。
    & dotnet publish (Join-Path $repositoryRoot 'src\Augit.Shell\Augit.Shell.csproj') `
        -c Release `
        -r win-x64 `
        --self-contained false `
        --no-restore `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:Version=$Version `
        -o $installerRoot
    if ($LASTEXITCODE -ne 0) {
        throw 'Augit 安装版发布失败。'
    }

    # 便携版为 self-contained：新电脑不需要预装 .NET 10，解压即可运行。
    # self-contained 需要 RID 级依赖图，使用独立的 packages.selfcontained.lock.json，避免改写主锁定文件。
    & dotnet restore (Join-Path $repositoryRoot 'src\Augit.Shell\Augit.Shell.csproj') `
        -r win-x64 `
        -p:SelfContained=true `
        -p:RuntimeFrameworkVersion=$RuntimeFrameworkVersion `
        --locked-mode `
        -p:NuGetLockFilePath=packages.selfcontained.lock.json
    if ($LASTEXITCODE -ne 0) {
        throw '便携版 self-contained 依赖还原失败。'
    }

    & dotnet publish (Join-Path $repositoryRoot 'src\Augit.Shell\Augit.Shell.csproj') `
        -c Release `
        -r win-x64 `
        --self-contained true `
        --no-restore `
        -p:RuntimeFrameworkVersion=$RuntimeFrameworkVersion `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:Version=$Version `
        -o $portableRoot
    if ($LASTEXITCODE -ne 0) {
        throw 'Augit 便携版发布失败。'
    }

    Copy-DistributionDocuments $installerRoot
    Copy-DistributionDocuments $portableRoot

    Assert-DistributionFiles $installerRoot '安装版'
    Assert-FrameworkDependentPublish $installerRoot '安装版'
    Assert-DistributionFiles $portableRoot '便携版'
    Assert-SelfContainedPublish $portableRoot '便携版'

    Compress-Archive -LiteralPath $portableRoot -DestinationPath $stagedPortableArchive -CompressionLevel Optimal

    & dotnet msbuild $packagingProject `
        -nologo `
        -t:BuildInstaller `
        -p:PublishDir=$installerRoot `
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
