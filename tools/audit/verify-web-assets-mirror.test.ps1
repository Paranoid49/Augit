# 在独立临时项目中验证资源镜像、内容门禁和危险路径保护，不修改产品源码资产。
param([string]$TempRoot = $env:TEMP)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$verifier = Join-Path $PSScriptRoot 'verify-web-assets-mirror.ps1'
$tempBase = [IO.Path]::GetFullPath($TempRoot).TrimEnd('\', '/')
$testRoot = Join-Path $tempBase ('augit-web-mirror-test-' + [guid]::NewGuid().ToString('N'))
$assertions = 0
$utf8 = New-Object Text.UTF8Encoding($false)
function Assert-Mirror([bool]$condition, [string]$message) {
  if (!$condition) { throw $message }
  $script:assertions++
}
function Write-Asset([string]$path, [string]$text) {
  [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
  [IO.File]::WriteAllText($path, $text, $utf8)
}
function Assert-VerifierFailure([string]$source, [string]$output, [string]$category) {
  $failure = $null
  try { & $verifier -SourceDirectory $source -OutputDirectory $output | Out-Null }
  catch { $failure = $_.Exception.Message }
  Assert-Mirror ($null -ne $failure -and $failure.Contains($category)) "门禁应拒绝 $category，实际：$failure"
}
function Invoke-Target([string]$project, [string]$target, [string]$property = '') {
  $arguments = @('msbuild', $project, '-nologo', '-v:quiet', "-t:$target", '-nr:false')
  if ($property) { $arguments += "-p:$property" }
  $text = & dotnet @arguments 2>&1
  $resultCode = $LASTEXITCODE
  return @{ Code = $resultCode; Text = ($text | Out-String) }
}
function New-Project([string]$root, [string]$targetXml) {
  $project = Join-Path $root 'src\Augit.Shell\Mirror.proj'
  Write-Asset (Join-Path $root 'web\index.html') 'version-one'
  Write-Asset (Join-Path $root 'web\src\app.js') 'script-one'
  # 不使用 SDK：空 Build/Publish 目标仅触发实际产品的 AfterTargets 复制代码。
  $projectXml = @"
<Project>
  <PropertyGroup>
    <OutDir>out\</OutDir>
    <PublishDir>publish\</PublishDir>
  </PropertyGroup>
  <Target Name="Build" />
  <Target Name="Publish" />
  $targetXml
</Project>
"@
  Write-Asset $project $projectXml
  return $project
}
try {
  [IO.Directory]::CreateDirectory($testRoot) | Out-Null
  $source = Join-Path $testRoot 'fixture\source'
  $output = Join-Path $testRoot 'fixture\output'
  Write-Asset (Join-Path $source 'app.js') 'AAAA'
  Write-Asset (Join-Path $output 'app.js') 'AAAA'
  $success = & $verifier -SourceDirectory $source -OutputDirectory $output
  Assert-Mirror ($success -contains 'WEB_ASSETS_MIRROR_OK files=1') '相同文件应通过并报告文件数'
  Assert-VerifierFailure (Join-Path $testRoot 'absent-source') $output 'MISSING_SOURCE_DIRECTORY'
  Assert-VerifierFailure $source (Join-Path $testRoot 'absent-output') 'MISSING_OUTPUT_DIRECTORY'
  Remove-Item -LiteralPath (Join-Path $output 'app.js')
  Assert-VerifierFailure $source $output 'MISSING_IN_OUTPUT'
  Write-Asset (Join-Path $output 'app.js') 'AAAA'
  Write-Asset (Join-Path $output 'old.js') 'unused'
  Assert-VerifierFailure $source $output 'EXTRA_IN_OUTPUT'
  Remove-Item -LiteralPath (Join-Path $output 'old.js')
  Write-Asset (Join-Path $output 'app.js') 'BBBB'
  [IO.File]::SetLastWriteTimeUtc((Join-Path $output 'app.js'), [IO.File]::GetLastWriteTimeUtc((Join-Path $source 'app.js')))
  Assert-VerifierFailure $source $output 'CONTENT_DIFFERS'
  Assert-VerifierFailure $source $source 'SAME_DIRECTORY'
  Assert-VerifierFailure $source (Join-Path $source 'nested') 'OUTPUT_INSIDE_SOURCE'
  Assert-VerifierFailure $source (Split-Path -Parent $source) 'SOURCE_INSIDE_OUTPUT'

  $document = New-Object Xml.XmlDocument
  $document.Load((Join-Path $repo 'src\Augit.Shell\Augit.Shell.csproj'))
  $targetXml = (@('CopyWebAssets', 'CopyWebAssetsToPublishDirectory') | ForEach-Object {
    $node = $document.SelectSingleNode("/Project/Target[@Name='$_']")
    if (!$node) { throw "缺少实际复制目标 $_" }
    $node.OuterXml
  }) -join "`n"
  foreach ($target in @('Build', 'Publish')) {
    $fixtureRoot = Join-Path $testRoot $target
    $project = New-Project $fixtureRoot $targetXml
    $propertyName = if ($target -eq 'Build') { 'OutDir' } else { 'PublishDir' }
    $defaultOutput = if ($target -eq 'Build') { 'out\web' } else { 'publish\web' }
    $assetOutput = Join-Path (Split-Path -Parent $project) $defaultOutput
    $assetSource = Join-Path $fixtureRoot 'web'
    $result = Invoke-Target $project $target
    Assert-Mirror ($result.Code -eq 0) "$target 首次复制失败：$($result.Text)"
    & $verifier -SourceDirectory $assetSource -OutputDirectory $assetOutput | Out-Null
    $assertions++
    # 保持长度与时间不变，只改变内容；删除源文件并在输出造额外旧文件。
    $index = Join-Path $assetSource 'index.html'
    $mtime = [IO.File]::GetLastWriteTimeUtc($index)
    Write-Asset $index 'version-two'
    [IO.File]::SetLastWriteTimeUtc($index, $mtime)
    Remove-Item -LiteralPath (Join-Path $assetSource 'src\app.js')
    Write-Asset (Join-Path $assetOutput 'obsolete.js') 'old'
    $result = Invoke-Target $project $target
    Assert-Mirror ($result.Code -eq 0) "$target 再复制失败：$($result.Text)"
    & $verifier -SourceDirectory $assetSource -OutputDirectory $assetOutput | Out-Null
    $assertions++
    Assert-Mirror ([IO.File]::ReadAllText((Join-Path $assetOutput 'index.html')) -eq 'version-two') "$target 同长度同时间内容没有更新"
    Assert-Mirror (!(Test-Path -LiteralPath (Join-Path $assetOutput 'src\app.js'))) "$target 删除的源文件残留"
    Assert-Mirror (!(Test-Path -LiteralPath (Join-Path $assetOutput 'obsolete.js'))) "$target 额外输出文件残留"
    # 独立绝对输出路径应正确解析。
    $absoluteOutput = Join-Path $testRoot "$target-absolute"
    $result = Invoke-Target $project $target "$propertyName=$absoluteOutput"
    Assert-Mirror ($result.Code -eq 0) "$target 绝对输出失败：$($result.Text)"
    & $verifier -SourceDirectory $assetSource -OutputDirectory (Join-Path $absoluteOutput 'web') | Out-Null
    $assertions++
    foreach ($kind in @('same', 'inside', 'ancestor')) {
      $dangerRoot = Join-Path $testRoot "$target-$kind"
      if ($kind -eq 'ancestor') { $dangerRoot = Join-Path $dangerRoot 'web' }
      $dangerProject = New-Project $dangerRoot $targetXml
      $dangerSource = Join-Path $dangerRoot 'web'
      $dangerOutputBase = switch ($kind) {
        'same' { $dangerRoot }
        'inside' { Join-Path $dangerSource 'nested' }
        'ancestor' { Split-Path -Parent $dangerRoot }
      }
      $result = Invoke-Target $dangerProject $target "$propertyName=$dangerOutputBase"
      Assert-Mirror ($result.Code -ne 0 -and $result.Text.Contains('CopyWebAssets')) "$target $kind 应由复制目标拒绝：$($result.Text)"
      Assert-Mirror ([IO.File]::ReadAllText((Join-Path $dangerSource 'index.html')) -eq 'version-one') "$target $kind 损坏源文件"
      Assert-Mirror ([IO.File]::ReadAllText((Join-Path $dangerSource 'src\app.js')) -eq 'script-one') "$target $kind 损坏源子目录"
    }
  }
  # 四个实际入口应在缺输出目录时以 6 退出，不能尝试启动占位可执行文件。
  $fakeExe = Join-Path $testRoot 'gate\Fake.exe'
  Write-Asset $fakeExe 'not-an-executable'
  foreach ($scriptName in @('capture-surface.ps1', 'dump-live-dom.ps1', 'verify-window-chrome.ps1', 'measure-performance.ps1')) {
    $gateArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot $scriptName), '-Exe', $fakeExe, '-Workspace', $testRoot)
    switch ($scriptName) {
      'capture-surface.ps1' { $gateArguments += @('-Surface', 'main-project', '-Out', (Join-Path $testRoot 'capture.png')) }
      'dump-live-dom.ps1' { $gateArguments += @('-Scene', 'main-project', '-Out', (Join-Path $testRoot 'dump.json')) }
      'verify-window-chrome.ps1' { $gateArguments += @('-OutDir', (Join-Path $testRoot 'chrome')) }
      'measure-performance.ps1' { $gateArguments += @('-Out', (Join-Path $testRoot 'performance.json')) }
    }
    $gateText = & powershell @gateArguments 2>&1
    $gateCode = $LASTEXITCODE
    Assert-Mirror ($gateCode -eq 6) "$scriptName 门禁没有按 6 退出，实际 $gateCode $gateText"
    Assert-Mirror (($gateText | Out-String).Contains('MISSING_OUTPUT_DIRECTORY')) "$scriptName 没有报告缺失资源目录"
  }
  Write-Output "WEB_ASSETS_MIRROR_TEST_OK assertions=$assertions"
} finally {
  # 递归删除前验证绝对目标只在本批唯一临时目录，不能删除临时根或其他任务目录。
  $normalized = [IO.Path]::GetFullPath($testRoot).TrimEnd('\', '/')
  if (!$normalized.StartsWith($tempBase + [char]92, [StringComparison]::OrdinalIgnoreCase) -or !(Split-Path -Leaf $normalized).StartsWith('augit-web-mirror-test-')) {
    throw "拒绝清理不属于本批的临时目录：$normalized"
  }
  if (Test-Path -LiteralPath $normalized) { Remove-Item -LiteralPath $normalized -Recurse -Force }
}
