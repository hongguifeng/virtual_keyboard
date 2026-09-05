#requires -version 5.1
<#
Virtual Keyboard 统一构建入口（T0.3）。非交互式；任一步失败立即以非零码退出。

步骤：
  1. 检查 .NET 10 SDK（10.0.4xx feature band，见 global.json；拒绝预览版）
  2. dotnet restore（显式使用仓库根 NuGet.Config）
  3. dotnet build -c Release --no-restore
  4. dotnet test -c Release --no-build --no-restore（TRX 结果输出到 artifacts/test-results/）
  5. dotnet publish App -c Release -r win-x64 --no-restore（输出到 artifacts/package/win-x64/）
收尾：dotnet build-server shutdown
#>
param()

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
$appProject     = Join-Path $repoRoot 'src\VirtualKeyboard.App\VirtualKeyboard.App.csproj'
$testProjects   = @(
  (Join-Path $repoRoot 'tests\VirtualKeyboard.Core.Tests\VirtualKeyboard.Core.Tests.csproj'),
  (Join-Path $repoRoot 'tests\VirtualKeyboard.Windows.Tests\VirtualKeyboard.Windows.Tests.csproj'),
  (Join-Path $repoRoot 'tests\VirtualKeyboard.IntegrationTests\VirtualKeyboard.IntegrationTests.csproj')
)
$testResultsDir = Join-Path $repoRoot 'artifacts\test-results'
$publishDir     = Join-Path $repoRoot 'artifacts\package\win-x64'

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
  $output = & dotnet build-server shutdown 2>&1
  $output | ForEach-Object { Write-Host "  $_" }
  if ($LASTEXITCODE -ne 0) {
    Write-Warning 'dotnet build-server shutdown 返回非零（可能本来就没有 build server，可忽略）。'
  }
}

try {
  # 1. 检查 .NET 10 SDK
  if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host '错误：未找到 dotnet CLI。请安装 .NET 10 SDK（10.0.4xx feature band，非预览版），见 global.json 与 README.md。' -ForegroundColor Red
    exit 1
  }
  $sdkList = & dotnet --list-sdks
  if ($LASTEXITCODE -ne 0) {
    Write-Host '错误：dotnet --list-sdks 执行失败。' -ForegroundColor Red
    exit 1
  }
  $sdkList | ForEach-Object { Write-Host "  $_" }
  $sdk10 = $null
  foreach ($line in $sdkList) {
    if ($line -match '^(10\.0\.\d+)(-preview[^ ]*)?') {
      if ($Matches[2]) {
        continue # 预览版不满足 global.json 的 allowPrerelease=false
      }
      $sdk10 = $Matches[1]
    }
  }
  if (-not $sdk10) {
    Write-Host '错误：未找到 .NET 10 SDK（需要 10.0.4xx feature band 的正式版 SDK；当前已安装列表见上）。请安装后重试，见 global.json 与 README.md。' -ForegroundColor Red
    exit 1
  }
  Write-Host "使用 .NET SDK：$sdk10"

  foreach ($required in @($slnPath, $nugetConfig, $appProject)) {
    if (-not (Test-Path $required)) {
      Write-Host "错误：找不到必需文件 $required（请在仓库根目录运行本脚本）。" -ForegroundColor Red
      exit 1
    }
  }

  # 2. restore（显式使用仓库 NuGet.Config；-r win-x64 使资产文件同时包含 TFM 与 TFM+RID 目标，
  #    满足后续无 RID 的 Release 构建与 win-x64 发布的 --no-restore/--no-build）
  Invoke-Dotnet @('restore', $slnPath, '--configfile', $nugetConfig, '-r', 'win-x64')

  # 3. Release 构建
  Invoke-Dotnet @('build', $slnPath, '-c', 'Release', '--no-restore')

  # 4. 测试（TRX 结果文件输出到 artifacts/test-results/；先清理旧结果，避免重复运行累积）
  New-Item -ItemType Directory -Force -Path $testResultsDir | Out-Null
  Get-ChildItem -Path $testResultsDir -File | Remove-Item -Force
  foreach ($project in $testProjects) {
    Invoke-Dotnet @('test', $project, '-c', 'Release', '--no-build', '--no-restore',
                   '--results-directory', $testResultsDir, '--logger', 'trx')
  }

  # 5. win-x64 发布（framework-dependent，依赖本机 .NET 10 运行时；先清理旧发布输出）
  New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
  Get-ChildItem -Path $publishDir -Force | Remove-Item -Recurse -Force
  Invoke-Dotnet @('publish', $appProject, '-c', 'Release', '-r', 'win-x64',
                 '--no-restore', '-o', $publishDir)

  Write-Host ''
  Write-Host "构建完成。" -ForegroundColor Green
  Write-Host "  测试结果（TRX）：$testResultsDir"
  Write-Host "  win-x64 发布输出：$publishDir"
}
finally {
  Shutdown-BuildServer
}
