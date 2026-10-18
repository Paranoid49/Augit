# 验证 web 资源镜像：源目录与输出目录必须拥有完全相同的相对文件路径集合（Windows 大小写不敏感），
# 且每个文件的 SHA256 完全一致。只读检查，不修改任何文件、不启动应用。
#
# 为什么需要它：壳程序从“可执行文件旁边的 web 目录”加载界面，早期脚本只用“最大 mtime”判断新旧，
# 于是“源文件被删除后输出目录残留旧文件”“同长度同 mtime 但内容不同”这两类问题都会漏过，
# 审计证据可能来自一个旧界面。本脚本用文件集合 + 内容哈希做完整镜像判定。
#
# 目录约定：SourceDirectory 固定为仓库自带的 web 目录，OutputDirectory 为 Exe 所在目录的 web。
# 本文件为中文注释，必须保持 UTF-8 BOM + LF，否则 PowerShell 5.1 会按 ANSI 解码而解析失败。
param(
  [Parameter(Mandatory=$true)][string]$SourceDirectory,
  [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = "Stop"

# 归一化为绝对路径；不用 GetRelativePath 等较新 API，兼容 PowerShell 5.1。
function Get-NormalizedDirectoryPath([string]$path, [string]$label) {
  if ([string]::IsNullOrWhiteSpace($path)) { throw ("目录参数为空: " + $label) }
  return ([System.IO.Path]::GetFullPath($path)).TrimEnd('\', '/')
}

# 路径比较在两个方向上都不区分大小写（Windows 语义）。
function Test-PathContains([string]$parent, [string]$child) {
  $prefix = $parent + [char]92
  return $child.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
}

# 收集完整相对路径集合，键统一使用小写以便大小写不敏感比较。
function Get-RelativeFileMap([string]$root) {
  $map = @{}
  foreach ($file in (Get-ChildItem -LiteralPath $root -Recurse -File -Force)) {
    $relative = $file.FullName.Substring($root.Length).TrimStart('\', '/')
    $map[$relative.ToLowerInvariant()] = $relative
  }
  return $map
}

function Get-FileSha256([string]$path) {
  $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
  try {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($stream) } finally { $sha.Dispose() }
  } finally {
    $stream.Dispose()
  }
  $builder = New-Object System.Text.StringBuilder
  foreach ($byte in $hash) { [void]$builder.Append($byte.ToString("x2")) }
  return $builder.ToString()
}

$source = Get-NormalizedDirectoryPath $SourceDirectory "SourceDirectory"
$output = Get-NormalizedDirectoryPath $OutputDirectory "OutputDirectory"

# 拒绝把同一目录、祖先目录或子目录当作镜像目标：那种比较必然自证通过，等于没有验证。
if ($source.Equals($output, [System.StringComparison]::OrdinalIgnoreCase)) {
  throw ("目录配置错误 [SAME_DIRECTORY]: SourceDirectory 与 OutputDirectory 相同: " + $source)
}
if (Test-PathContains $source $output) {
  throw ("目录配置错误 [OUTPUT_INSIDE_SOURCE]: OutputDirectory 位于 SourceDirectory 之内: " + $output)
}
if (Test-PathContains $output $source) {
  throw ("目录配置错误 [SOURCE_INSIDE_OUTPUT]: SourceDirectory 位于 OutputDirectory 之内: " + $source)
}

$failures = New-Object System.Collections.ArrayList

if (-not (Test-Path -LiteralPath $source -PathType Container)) {
  [void]$failures.Add("[MISSING_SOURCE_DIRECTORY] " + $source)
} elseif (-not (Test-Path -LiteralPath $output -PathType Container)) {
  # 源存在就必须验证输出：输出目录缺失同样是失败，而不是跳过。
  [void]$failures.Add("[MISSING_OUTPUT_DIRECTORY] " + $output)
} else {
  $sourceFiles = Get-RelativeFileMap $source
  $outputFiles = Get-RelativeFileMap $output

  foreach ($key in ($sourceFiles.Keys | Sort-Object)) {
    $relative = $sourceFiles[$key]
    if (-not $outputFiles.ContainsKey($key)) {
      [void]$failures.Add("[MISSING_IN_OUTPUT] " + $relative)
      continue
    }
    $sourceHash = Get-FileSha256 (Join-Path $source $relative)
    $outputHash = Get-FileSha256 (Join-Path $output $outputFiles[$key])
    if ($sourceHash -ne $outputHash) {
      [void]$failures.Add("[CONTENT_DIFFERS] " + $relative)
    }
  }
  foreach ($key in ($outputFiles.Keys | Sort-Object)) {
    if (-not $sourceFiles.ContainsKey($key)) {
      [void]$failures.Add("[EXTRA_IN_OUTPUT] " + $outputFiles[$key])
    }
  }

  if ($failures.Count -eq 0) {
    Write-Output ("WEB_ASSETS_MIRROR_OK files=" + $sourceFiles.Count)
    return
  }
}

foreach ($failure in $failures) { Write-Output ("MIRROR_FAIL " + $failure) }
throw ("web 资源镜像验证失败: " + ($failures -join '; ') + "；source=" + $source + " output=" + $output)
