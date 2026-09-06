#requires -version 5.1
<#
Virtual Keyboard 统一构建入口（T0.3）。非交互式；任一步失败立即以非零码退出。

步骤：
  1. 检查 .NET 10 SDK：以 global.json 为唯一版本事实来源（version / rollForward / allowPrerelease），
     结合 `dotnet --version` 的实际解析结果验证；只接受 10.0.4xx feature band 正式版，拒绝预览版和其他 band
  2. dotnet restore（显式使用仓库根 NuGet.Config）
  3. dotnet build -c Release --no-restore
  4. dotnet test -c Release --no-build --no-restore（TRX 结果输出到 artifacts/test-results/）
  5. 非 -SkipPackage 时：dotnet publish App -c Release -r win-x64 --no-restore --self-contained false
  6. 非 -SkipPackage 时：生成版本化便携 ZIP 和 SHA-256 校验文件（输出到 artifacts/release/）
收尾：dotnet build-server shutdown（dotnet 缺失时安全跳过，不产生二次错误、不覆盖原始错误）
#>
param(
  [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
  [string]$Version = '1.0.0',
  [switch]$SkipPackage
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

# 不使用 MSBuild server（避免跨会话/跨版本复用造成的环境差异，提高可复现性）
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$repoRoot     = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$slnPath        = Join-Path $repoRoot 'VirtualKeyboard.sln'
$nugetConfig    = Join-Path $repoRoot 'NuGet.Config'
$globalJsonPath = Join-Path $repoRoot 'global.json'
$appProject     = Join-Path $repoRoot 'src\VirtualKeyboard.App\VirtualKeyboard.App.csproj'
$testProjects   = @(
  (Join-Path $repoRoot 'tests\VirtualKeyboard.Core.Tests\VirtualKeyboard.Core.Tests.csproj'),
  (Join-Path $repoRoot 'tests\VirtualKeyboard.Windows.Tests\VirtualKeyboard.Windows.Tests.csproj'),
  (Join-Path $repoRoot 'tests\VirtualKeyboard.IntegrationTests\VirtualKeyboard.IntegrationTests.csproj')
)
$artifactsRoot  = Join-Path $repoRoot 'artifacts'
$testResultsDir = Join-Path $repoRoot 'artifacts\test-results'
$publishDir     = Join-Path $repoRoot 'artifacts\package\win-x64'
$releaseDir     = Join-Path $repoRoot 'artifacts\release'

function Test-SafeArtifactDirectory {
  <#
  集中的 artifacts 目录安全校验（可独立测试，参数均为显式字面路径）：
    1. 将目标与 artifacts 根规范化为绝对路径；
    2. 目标必须严格位于本仓库 artifacts 目录之下（不能是根本身，更不能在仓库外/兄弟路径）；
    3. 逐级检查 artifacts 根、其到目标的每个现有中间目录和最终目标，任一为 reparse point（符号链接/junction）时拒绝（防止中间 junction 指向仓库外而最终子目录尚不存在时校验漏检）。
  通过时返回 [PSCustomObject] 且 .Path 为规范化后的绝对路径；
  拒绝时 .Path 为 $null，.Reason 为可判定的拒绝原因。
  #>
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$ArtifactsRoot
  )
  try {
    $rootFull   = [System.IO.Path]::GetFullPath($ArtifactsRoot)
    $targetFull = [System.IO.Path]::GetFullPath($Path)
  }
  catch {
    return [PSCustomObject]@{ Path = $null; Reason = "路径无法规范化：$($_.Exception.Message)" }
  }
  $rootPrefix = $rootFull.TrimEnd('\') + '\'
  if ($targetFull -eq $rootFull -or -not $targetFull.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    return [PSCustomObject]@{ Path = $null; Reason = "目标 '$targetFull' 不严格位于本仓库 artifacts 目录 '$rootFull' 之下，拒绝清理。" }
  }
  # 逐级：artifacts 根 + 每个现有中间目录 + 最终目标；任一为 reparse point 即拒绝（不存在的目录无需检查，它没有文件系统对象）
  $cursor = $rootFull
  foreach ($seg in @($targetFull.Substring($rootFull.Length + 1) -split '\\')) {
    $cursor = [System.IO.Path]::Combine($cursor, $seg)
    if (Test-Path -LiteralPath $cursor) {
      $item = Get-Item -LiteralPath $cursor
      if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        return [PSCustomObject]@{ Path = $null; Reason = "'$cursor' 是 reparse point（符号链接/junction），拒绝清理。" }
      }
    }
  }
  return [PSCustomObject]@{ Path = $targetFull; Reason = $null }
}

function Clear-SafeArtifactDirectory {
  <#
  先经 Test-SafeArtifactDirectory 校验，通过后才递归清理目录内容。
  所有删除均使用 LiteralPath（按条目全名），不做任何 glob 拼接。
  -FilesOnly：仅删除目录直属文件并保留目录本身（用于 TRX 结果目录）。
  校验失败时以非零码退出。返回通过校验的规范化路径。
  #>
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$ArtifactsRoot,
    [switch]$FilesOnly
  )
  $check = Test-SafeArtifactDirectory -Path $Path -ArtifactsRoot $ArtifactsRoot
  if ($null -eq $check.Path) {
    Write-Host "错误：artifacts 目录安全校验失败：$($check.Reason)" -ForegroundColor Red
    exit 1
  }
  if (Test-Path -LiteralPath $check.Path) {
    $items = if ($FilesOnly) {
      Get-ChildItem -LiteralPath $check.Path -File -Force
    }
    else {
      Get-ChildItem -LiteralPath $check.Path -Force
    }
    foreach ($item in $items) {
      Remove-Item -LiteralPath $item.FullName -Recurse -Force
    }
  }
  return $check.Path
}

function Invoke-Dotnet {
  param([Parameter(Mandatory = $true)][string[]]$Arguments)
  Write-Host "==> dotnet $($Arguments -join ' ')"
  & dotnet @Arguments
  if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED: dotnet $($Arguments -join ' ') (exit code $LASTEXITCODE)" -ForegroundColor Red
    exit $LASTEXITCODE
  }
}

function Shutdown-BuildServer {
  # dotnet 缺失时（例如未安装 CLI）直接安全跳过：不重复调用 dotnet，不产生二次错误，不覆盖原始“未找到 CLI”错误。
  if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host '  (未找到 dotnet CLI，跳过 build-server shutdown。)'
    return
  }
  try {
    $output = & dotnet build-server shutdown 2>&1
    $output | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
      Write-Warning 'dotnet build-server shutdown 返回非零（可能本来就没有 build server，可忽略）。'
    }
  }
  catch {
    Write-Warning "dotnet build-server shutdown 执行异常，已跳过（不影响原始结果）：$($_.Exception.Message)"
  }
}

try {
  # 1. 检查 .NET 10 SDK（global.json 是版本事实来源；以 dotnet --version 的实际解析结果做验证）
  if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host '错误：未找到 dotnet CLI。请安装 .NET 10 SDK（10.0.4xx feature band 正式版，非预览版），见 global.json 与 README.md。' -ForegroundColor Red
    exit 1
  }
  foreach ($required in @($slnPath, $nugetConfig, $globalJsonPath, $appProject)) {
    if (-not (Test-Path $required)) {
      Write-Host "错误：找不到必需文件 $required（请在仓库根目录运行本脚本）。" -ForegroundColor Red
      exit 1
    }
  }
  $globalJson       = Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json
  $reqVersion       = [string]$globalJson.sdk.version
  $rollForward      = [string]$globalJson.sdk.rollForward
  $allowPrerelease  = [bool]$globalJson.sdk.allowPrerelease
  if ($reqVersion -notmatch '^(\d+)\.(\d+)\.(\d)(\d{1,2})$') {
    Write-Host "错误：无法解析 global.json 中的 SDK 版本号 '$reqVersion'（应为 major.minor.featureband.patch，如 10.0.400）。" -ForegroundColor Red
    exit 1
  }
  $reqMajor = [int]$Matches[1]; $reqMinor = [int]$Matches[2]; $reqBand = [int]$Matches[3]; $reqPatch = [int]$Matches[4]

  $sdkList = & dotnet --list-sdks 2>&1
  if ($LASTEXITCODE -eq 0) {
    $sdkList | ForEach-Object { Write-Host "  已安装 SDK：$_" }
  }

  $versionLines = (& dotnet --version 2>&1 | ForEach-Object { [string]$_ })
  if ($LASTEXITCODE -ne 0) {
    Write-Host "错误：dotnet --version 执行失败（可能与 global.json 要求 $reqVersion / rollForward=$rollForward 不匹配）：" -ForegroundColor Red
    $versionLines | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
  }
  $actualVersion = ''
  foreach ($line in $versionLines) {
    if ($line.Trim() -ne '') { $actualVersion = $line.Trim() }
  }
  if ($actualVersion -notmatch '^\d') {
    Write-Host '错误：无法从 dotnet --version 输出中解析 SDK 版本：' -ForegroundColor Red
    $versionLines | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
  }

  # 拆分预览版 / 构建元数据（-xxx 或 +xxx）
  $stableVersion = $actualVersion
  if ($actualVersion -match '^(.*?)\s*[+-].+$') {
    $stableVersion = $Matches[1].Trim()
  }
  if ($stableVersion -ne $actualVersion -and -not $allowPrerelease) {
    Write-Host "错误：dotnet 解析到的 SDK 为预览版 '$actualVersion'，而 global.json 设置 allowPrerelease=false；需要 $($reqMajor).$($reqMinor).$($reqBand)xx feature band 正式版 SDK。" -ForegroundColor Red
    exit 1
  }
  if ($stableVersion -notmatch '^(\d+)\.(\d+)\.(\d)(\d{1,2})$') {
    Write-Host "错误：无法把实际 SDK 版本 '$actualVersion' 解析为 major.minor.featureband.patch 形式。" -ForegroundColor Red
    exit 1
  }
  $actMajor = [int]$Matches[1]; $actMinor = [int]$Matches[2]; $actBand = [int]$Matches[3]; $actPatch = [int]$Matches[4]

  # 按 global.json 的 rollForward 策略判定接受范围（只实现 .NET SDK 语义，不引入与 global.json 分离的硬编码版本）
  $ok = $false
  if ($rollForward -eq 'featureBand' -or $rollForward -eq 'latestFeature') {
    $ok = ($actMajor -eq $reqMajor -and $actMinor -eq $reqMinor)
    if ($rollForward -eq 'featureBand') {
      $ok = $ok -and ($actBand -eq $reqBand)
    }
    else {
      $ok = $ok -and ($actBand -ge $reqBand)
    }
  }
  elseif ($rollForward -eq 'latestMinor' -or $rollForward -eq 'minor') {
    $ok = ($actMajor -eq $reqMajor -and
          ($actMinor -gt $reqMinor -or
           ($actMinor -eq $reqMinor -and
            ($actBand -gt $reqBand -or
             ($actBand -eq $reqBand -and $actPatch -ge $reqPatch)))))
  }
  elseif ($rollForward -eq 'latestMajor' -or $rollForward -eq 'major') {
    $ok = ($actMajor -eq $reqMajor -and
          ($actMinor -gt $reqMinor -or
           ($actMinor -eq $reqMinor -and
            ($actBand -gt $reqBand -or
             ($actBand -eq $reqBand -and $actPatch -ge $reqPatch)))))
  }
  elseif ($rollForward -eq 'latestPatch' -or $rollForward -eq 'disable') {
    $ok = ($actMajor -eq $reqMajor -and $actMinor -eq $reqMinor -and $actBand -eq $reqBand -and $actPatch -ge $reqPatch)
  }
  else {
    Write-Host "错误：不支持的 global.json rollForward 值 '$rollForward'，请检查 global.json。" -ForegroundColor Red
    exit 1
  }
  if (-not $ok) {
    Write-Host "错误：实际 SDK 版本 '$actualVersion' 不符合 global.json 要求（需 $reqVersion，rollForward=$rollForward，allowPrerelease=$allowPrerelease；即 $($reqMajor).$($reqMinor).$($reqBand)xx feature band 正式版，允许 band 内 patch 升级，拒绝预览版和其他 feature band）。请安装符合要求的 SDK 后重试，见 global.json 与 README.md。" -ForegroundColor Red
    exit 1
  }
  Write-Host "使用 .NET SDK：$actualVersion（global.json 要求 $reqVersion，rollForward=$rollForward）"

  # 2. restore（显式使用仓库 NuGet.Config；-r win-x64 使资产文件同时包含 TFM 与 TFM+RID 目标，
  #    满足后续无 RID 的 Release 构建与 win-x64 发布的 --no-restore/--no-build）
  Invoke-Dotnet @('restore', $slnPath, '--configfile', $nugetConfig, '-r', 'win-x64')

  # 3. Release 构建
  Invoke-Dotnet @('build', $slnPath, '-c', 'Release', '--no-restore')

  # 4. 测试（TRX 结果文件输出到 artifacts/test-results/；先经安全目录校验再清理旧结果，避免重复运行累积）
  $testResultsDir = Clear-SafeArtifactDirectory -Path $testResultsDir -ArtifactsRoot $artifactsRoot -FilesOnly
  New-Item -ItemType Directory -Force -Path $testResultsDir | Out-Null
  foreach ($project in $testProjects) {
    Invoke-Dotnet @('test', $project, '-c', 'Release', '--no-build', '--no-restore',
                   '--results-directory', $testResultsDir, '--logger', 'trx')
  }

  if (-not $SkipPackage) {
    # 5. win-x64 发布（framework-dependent：显式 --self-contained false，运行需已安装 .NET 10 桌面运行时）
    $publishDir = Clear-SafeArtifactDirectory -Path $publishDir -ArtifactsRoot $artifactsRoot
    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
    Invoke-Dotnet @('publish', $appProject, '-c', 'Release', '-r', 'win-x64',
                   '--no-restore', '--self-contained', 'false', ("-p:Version=$Version"), '-o', $publishDir)

    # 6. 生成版本化便携 ZIP 与 SHA-256 校验文件（ADR-007）
    $releaseDir = Clear-SafeArtifactDirectory -Path $releaseDir -ArtifactsRoot $artifactsRoot
    New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
    $archiveName = "VirtualKeyboard-$Version-win-x64-framework-dependent.zip"
    $archivePath = Join-Path $releaseDir $archiveName
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $archivePath -CompressionLevel Optimal
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumPath = "$archivePath.sha256"
    [System.IO.File]::WriteAllText($checksumPath, "$archiveHash  $archiveName`n", [System.Text.UTF8Encoding]::new($false))
  }

  Write-Host ''
  Write-Host "构建和测试完成。" -ForegroundColor Green
  Write-Host "  测试结果（TRX）：$testResultsDir"
  if (-not $SkipPackage) {
    Write-Host "  win-x64 发布输出：$publishDir"
    Write-Host "  便携发布包：$archivePath"
    Write-Host "  SHA-256：$checksumPath"
  }
}
finally {
  Shutdown-BuildServer
}
