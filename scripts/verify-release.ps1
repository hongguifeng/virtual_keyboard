#requires -version 5.1
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
$repoRoot = Split-Path -Parent $PSScriptRoot
$releaseDir = Join-Path $repoRoot 'artifacts\release'
$publishDir = Join-Path $repoRoot 'artifacts\package\win-x64'
$archiveName = 'VirtualKeyboard-1.0.0-win-x64-framework-dependent.zip'
$archivePath = Join-Path $releaseDir $archiveName
$checksumPath = "$archivePath.sha256"
$exePath = Join-Path $publishDir 'VirtualKeyboard.App.exe'

foreach ($required in @($archivePath, $checksumPath, $exePath)) {
  if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "缺少发布文件：$required" }
}

$expectedHash = ((Get-Content -LiteralPath $checksumPath -Raw) -split '\s+')[0].ToLowerInvariant()
$actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expectedHash -ne $actualHash) { throw '发布 ZIP 的 SHA-256 与校验文件不一致。' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
try {
  $names = @($zip.Entries | ForEach-Object { $_.FullName })
  foreach ($name in $names) {
    if ([System.IO.Path]::IsPathRooted($name) -or $name.Split('/') -contains '..') { throw "ZIP 包含不安全路径：$name" }
    if ($name -match '(^|/)(config\.json|.*\.(log|key|pfx|p12))$') { throw "ZIP 包含禁止的运行时/敏感文件：$name" }
  }
  foreach ($requiredEntry in @('VirtualKeyboard.App.exe', 'VirtualKeyboard.App.dll', 'layouts/builtin/qwerty.en-US.json')) {
    if ($names -notcontains $requiredEntry) { throw "ZIP 缺少必需条目：$requiredEntry" }
  }
}
finally { $zip.Dispose() }

$exeText = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($exePath))
if (-not $exeText.Contains('requestedExecutionLevel level="asInvoker" uiAccess="false"')) {
  throw '可执行文件未嵌入 asInvoker / uiAccess=false 清单。'
}
if ($exeText.Contains('requireAdministrator') -or $exeText.Contains('highestAvailable') -or $exeText.Contains('uiAccess="true"')) {
  throw '可执行文件包含禁止的提权或 uiAccess 声明。'
}

$signature = Get-AuthenticodeSignature -LiteralPath $exePath
$report = [ordered]@{
  checkedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
  archive = $archiveName
  sha256 = $actualHash
  zipEntryCount = $names.Count
  manifest = 'asInvoker; uiAccess=false; PerMonitorV2'
  authenticodeStatus = [string]$signature.Status
  releaseClassification = if ($signature.Status -eq 'Valid') { 'signed-candidate' } else { 'unsigned-internal-test-only' }
}
$reportPath = Join-Path $releaseDir 'release-security.json'
[System.IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json), [System.Text.UTF8Encoding]::new($false))
$report | Format-List
Write-Host "发布安全静态检查通过；报告：$reportPath" -ForegroundColor Green
