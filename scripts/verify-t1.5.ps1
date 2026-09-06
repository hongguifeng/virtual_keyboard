#requires -version 5.1
<#
T1.5 交互式验收脚本。

在当前交互桌面中启动真实目标和发布后的虚拟键盘，通过物理鼠标点击 Overlay，
记录点击前、捕获后、发送后的前台 HWND、焦点 HWND 和键盘 HWND。脚本不会输出
目标输入内容；只比较 TestHost 的按键计数或目标控件长度变化。

脚本只关闭自己启动的进程。Notepad/Chrome 场景会写入合成测试字符，并在完成后
强制关闭本次启动的隔离测试进程，避免保存提示或测试浏览器配置残留。
#>
[CmdletBinding()]
param(
  [ValidateSet('All', 'WpfTestHost', 'Notepad', 'Chrome')]
  [string]$Scenario = 'All',

  [ValidateRange(1, 100)]
  [int]$RepeatCount = 1,

  [ValidateRange(10, 300)]
  [int]$InteractionTimeoutSeconds = 60,

  [string]$DotnetPath,

  [string]$ChromePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$repoRoot = Split-Path -Parent $PSScriptRoot
$appDll = Join-Path $repoRoot 'artifacts\package\win-x64\VirtualKeyboard.App.dll'
$testHostDll = Join-Path $repoRoot 'tests\VirtualKeyboard.TestHost\bin\Release\net10.0-windows\VirtualKeyboard.TestHost.dll'

function Resolve-DotnetPath {
  if ($DotnetPath) {
    $candidates = @((Resolve-Path -LiteralPath $DotnetPath).Path)
  }
  else {
    $candidates = @()
    $userLocalDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $userLocalDotnet) {
      $candidates += $userLocalDotnet
    }
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command -and $command.Source -notin $candidates) {
      $candidates += $command.Source
    }
  }

  foreach ($candidate in $candidates) {
    $runtimes = & $candidate --list-runtimes 2>$null
    if ($LASTEXITCODE -eq 0 -and
        @($runtimes | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App 10\.' }).Count -gt 0) {
      return $candidate
    }
  }

  throw '找不到包含 Microsoft.WindowsDesktop.App 10.x 的 dotnet。请通过 -DotnetPath 指定兼容的 dotnet.exe。'
}

function Resolve-ChromePath {
  if ($ChromePath) {
    return (Resolve-Path -LiteralPath $ChromePath).Path
  }

  $candidates = @(
    (Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Google\Chrome\Application\chrome.exe'),
    (Join-Path $env:LOCALAPPDATA 'Google\Chrome\Application\chrome.exe')
  )
  foreach ($candidate in $candidates) {
    if (Test-Path -LiteralPath $candidate) {
      return $candidate
    }
  }

  throw '找不到 Google Chrome。请通过 -ChromePath 指定 chrome.exe。'
}

function Wait-AutomationRootsByProcessId {
  param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [int]$TimeoutSeconds = 10
  )

  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  do {
    $roots = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
      [System.Windows.Automation.TreeScope]::Children,
      [System.Windows.Automation.Condition]::TrueCondition)
    $matches = @($roots | Where-Object { $_.Current.ProcessId -eq $ProcessId })
    if ($matches.Count -gt 0) {
      return $matches
    }

    # 某些 WPF/WinForms 共存进程在窗口刚创建时不会立即出现在 RootElement 的
    # Children 枚举中；MainWindowHandle 已就绪时可直接从 HWND 建立同一 UIA 根。
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($process) {
      $process.Refresh()
      if ($process.MainWindowHandle -ne 0) {
        $fromHandle = [System.Windows.Automation.AutomationElement]::FromHandle(
          [IntPtr]$process.MainWindowHandle)
        if ($fromHandle) {
          return @($fromHandle)
        }
      }
    }
    Start-Sleep -Milliseconds 100
  } while ((Get-Date) -lt $deadline)

  throw "等待 PID $ProcessId 的顶层窗口超时。"
}

function Find-Descendant {
  param(
    [Parameter(Mandatory = $true)]$Root,
    $ControlType,
    [string]$AutomationId,
    [switch]$AllowFirst
  )

  $all = $Root.FindAll(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
  $matches = @($all | Where-Object {
      ($null -eq $ControlType -or $_.Current.ControlType -eq $ControlType) -and
      ([string]::IsNullOrEmpty($AutomationId) -or $_.Current.AutomationId -eq $AutomationId)
    })
  if ($matches.Count -eq 1 -or ($AllowFirst -and $matches.Count -gt 0)) {
    return $matches[0]
  }

  throw "UIA 元素匹配数量错误：AutomationId='$AutomationId'，Count=$($matches.Count)。"
}

function Wait-Descendant {
  param(
    [Parameter(Mandatory = $true)]$Root,
    $ControlType,
    [string]$AutomationId,
    [switch]$AllowFirst,
    [int]$TimeoutSeconds = 10
  )

  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  do {
    try {
      return Find-Descendant -Root $Root -ControlType $ControlType `
        -AutomationId $AutomationId -AllowFirst:$AllowFirst
    }
    catch {
      Start-Sleep -Milliseconds 100
    }
  } while ((Get-Date) -lt $deadline)

  throw "等待 UIA 元素超时：AutomationId='$AutomationId'。"
}

function Find-RootContainingAutomationId {
  param(
    [Parameter(Mandatory = $true)]$Roots,
    [Parameter(Mandatory = $true)][string]$AutomationId
  )

  foreach ($root in $Roots) {
    try {
      $null = Find-Descendant -Root $root -AutomationId $AutomationId -AllowFirst
      return $root
    }
    catch {
      # 继续检查同一进程的其他顶层窗口，例如 TestHost 的 WinForms 页。
    }
  }
  throw "PID $($Roots[0].Current.ProcessId) 的窗口中找不到 AutomationId='$AutomationId'。"
}

function Wait-ChromeEditor {
  param(
    [Parameter(Mandatory = $true)]$Root,
    [int]$TimeoutSeconds = 10
  )

  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  do {
    $all = $Root.FindAll(
      [System.Windows.Automation.TreeScope]::Descendants,
      [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($element in $all) {
      if ($element.Current.ControlType -ne [System.Windows.Automation.ControlType]::Edit -or
          -not $element.Current.IsKeyboardFocusable -or
          -not $element.Current.IsEnabled -or
          $element.Current.IsOffscreen) {
        continue
      }

      $pattern = $null
      if ($element.TryGetCurrentPattern(
          [System.Windows.Automation.ValuePattern]::Pattern,
          [ref]$pattern)) {
        return $element
      }
    }
    Start-Sleep -Milliseconds 100
  } while ((Get-Date) -lt $deadline)

  throw '等待 Chrome 网页输入框超时。'
}

function Get-FocusSnapshot {
  param([Parameter(Mandatory = $true)][IntPtr]$KeyboardHwnd)

  $foreground = [VirtualKeyboardT15Native]::GetForegroundWindow()
  [uint32]$foregroundProcessId = 0
  $threadId = [VirtualKeyboardT15Native]::GetWindowThreadProcessId(
    $foreground,
    [ref]$foregroundProcessId)
  $info = New-Object VirtualKeyboardT15Native+GuiThreadInfo
  $info.Size = [Runtime.InteropServices.Marshal]::SizeOf($info)
  if ($threadId -eq 0 -or -not [VirtualKeyboardT15Native]::GetGUIThreadInfo($threadId, [ref]$info)) {
    throw 'GetGUIThreadInfo 失败。'
  }

  return [pscustomobject]@{
    Foreground = $foreground
    Focus = $info.FocusWindow
    Keyboard = $KeyboardHwnd
    ForegroundProcessId = $foregroundProcessId
  }
}

function Format-Hwnd {
  param([IntPtr]$Handle)
  return '0x{0:X}' -f $Handle.ToInt64()
}

function Stop-OwnedProcess {
  param($Process)
  if ($null -eq $Process) {
    return
  }

  $liveProcess = Get-Process -Id $Process.Id -ErrorAction SilentlyContinue
  if ($liveProcess) {
    Stop-Process -Id $liveProcess.Id -Force
  }
}

function Start-Keyboard {
  $process = Start-Process -FilePath $script:resolvedDotnet -ArgumentList $appDll -PassThru
  try {
    $roots = Wait-AutomationRootsByProcessId -ProcessId $process.Id
    $root = Find-RootContainingAutomationId -Roots $roots -AutomationId 'CaptureTargetButton'
    return [pscustomobject]@{
      Process = $process
      Root = $root
      Hwnd = [IntPtr]$root.Current.NativeWindowHandle
      CaptureButton = Find-Descendant -Root $root -ControlType ([System.Windows.Automation.ControlType]::Button) -AutomationId 'CaptureTargetButton'
      KeyAButton = Find-Descendant -Root $root -ControlType ([System.Windows.Automation.ControlType]::Button) -AutomationId 'KeyAButton'
      Status = Find-Descendant -Root $root -ControlType ([System.Windows.Automation.ControlType]::Text) -AutomationId 'SessionStatusText'
    }
  }
  catch {
    Stop-OwnedProcess $process
    throw
  }
}

function Invoke-OverlayProbe {
  param(
    [Parameter(Mandatory = $true)]$Keyboard,
    [Parameter(Mandatory = $true)][scriptblock]$ReadMetric,
    [Parameter(Mandatory = $true)][scriptblock]$MetricPassed,
    [Parameter(Mandatory = $true)][string]$ScenarioName
  )

  $metricBefore = & $ReadMetric
  $before = Get-FocusSnapshot -KeyboardHwnd $Keyboard.Hwnd

  Write-Host '  请用真实鼠标点击 Overlay 的“捕获当前目标”（不要点击终端或目标窗口）。' -ForegroundColor Cyan
  $captureDeadline = (Get-Date).AddSeconds($InteractionTimeoutSeconds)
  do {
    $captureStatus = $Keyboard.Status.Current.Name
    if ($captureStatus.StartsWith('会话 ', [StringComparison]::Ordinal)) { break }
    Start-Sleep -Milliseconds 100
  } while ((Get-Date) -lt $captureDeadline)
  if (-not $captureStatus.StartsWith('会话 ', [StringComparison]::Ordinal)) {
    throw "等待真实鼠标捕获操作超时（$InteractionTimeoutSeconds 秒）。"
  }
  $captured = Get-FocusSnapshot -KeyboardHwnd $Keyboard.Hwnd

  Write-Host "  请用真实鼠标点击 Overlay 的“A” $RepeatCount 次；脚本将自动检测结果。" -ForegroundColor Cyan
  $metricAfter = $metricBefore
  for ($expectedCount = 1; $expectedCount -le $RepeatCount; $expectedCount++) {
    $inputDeadline = (Get-Date).AddSeconds($InteractionTimeoutSeconds)
    do {
      $metricAfter = & $ReadMetric
      $observedCount = $metricAfter - $metricBefore
      if ($observedCount -gt $expectedCount) {
        throw "第 $expectedCount 次观察到计数跳跃到 $observedCount，存在重复投递或点击过快。"
      }
      if ($observedCount -eq $expectedCount) { break }
      Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $inputDeadline)
    if ($observedCount -ne $expectedCount) {
      throw "等待第 $expectedCount 次真实鼠标输入超时（$InteractionTimeoutSeconds 秒）。"
    }
  }
  $sent = Get-FocusSnapshot -KeyboardHwnd $Keyboard.Hwnd
  $sendStatus = $Keyboard.Status.Current.Name

  $foregroundPreserved =
    $before.Foreground -eq $captured.Foreground -and
    $before.Foreground -eq $sent.Foreground
  $focusPreserved =
    $before.Focus -eq $captured.Focus -and
    $before.Focus -eq $sent.Focus
  $keyboardNeverForeground =
    $Keyboard.Hwnd -ne $before.Foreground -and
    $Keyboard.Hwnd -ne $captured.Foreground -and
    $Keyboard.Hwnd -ne $sent.Foreground
  $metricOk = & $MetricPassed $metricBefore $metricAfter $RepeatCount
  $statusOk = $captureStatus.StartsWith('会话 ', [StringComparison]::Ordinal) -and
    $sendStatus.EndsWith('A 已发送', [StringComparison]::Ordinal)
  $passed = $foregroundPreserved -and $focusPreserved -and
    $keyboardNeverForeground -and $metricOk -and $statusOk

  [pscustomobject]@{
    Scenario = $ScenarioName
    Repetitions = $RepeatCount
    BeforeForeground = Format-Hwnd $before.Foreground
    BeforeFocus = Format-Hwnd $before.Focus
    CapturedForeground = Format-Hwnd $captured.Foreground
    CapturedFocus = Format-Hwnd $captured.Focus
    SentForeground = Format-Hwnd $sent.Foreground
    SentFocus = Format-Hwnd $sent.Focus
    Keyboard = Format-Hwnd $Keyboard.Hwnd
    ForegroundPreserved = $foregroundPreserved
    FocusPreserved = $focusPreserved
    KeyboardNeverForeground = $keyboardNeverForeground
    InputMetricPassed = $metricOk
    StatusPassed = $statusOk
    Passed = $passed
  }
}

function Invoke-WpfTestHostScenario {
  $hostProcess = $null
  $keyboard = $null
  try {
    $hostProcess = Start-Process -FilePath $script:resolvedDotnet -ArgumentList $testHostDll -PassThru
    $roots = Wait-AutomationRootsByProcessId -ProcessId $hostProcess.Id
    $root = Find-RootContainingAutomationId -Roots $roots -AutomationId 'txtNormal'
    $editor = Find-Descendant -Root $root -ControlType ([System.Windows.Automation.ControlType]::Edit) -AutomationId 'txtNormal'

    if (-not [VirtualKeyboardT15Native]::SetForegroundWindow([IntPtr]$root.Current.NativeWindowHandle)) {
      throw '无法将 WPF TestHost 置于前台。'
    }
    $editor.SetFocus()
    Start-Sleep -Milliseconds 200

    $readLength = {
      $freshRoots = Wait-AutomationRootsByProcessId -ProcessId $hostProcess.Id
      $freshRoot = Find-RootContainingAutomationId -Roots $freshRoots -AutomationId 'txtNormal'
      $freshEditor = Find-Descendant -Root $freshRoot -ControlType ([System.Windows.Automation.ControlType]::Edit) -AutomationId 'txtNormal'
      $pattern = [System.Windows.Automation.ValuePattern]$freshEditor.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)
      return $pattern.Current.Value.Length
    }
    $metricPassed = { param($before, $after, $expected) ($after - $before) -eq $expected }

    $keyboard = Start-Keyboard
    return Invoke-OverlayProbe -Keyboard $keyboard -ReadMetric $readLength -MetricPassed $metricPassed -ScenarioName 'WPF TestHost'
  }
  finally {
    if ($keyboard) { Stop-OwnedProcess $keyboard.Process }
    Stop-OwnedProcess $hostProcess
  }
}

function Invoke-NotepadScenario {
  $launcher = $null
  $notepadProcess = $null
  $keyboard = $null
  try {
    $existingIds = @(Get-Process Notepad -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    $launcher = Start-Process -FilePath (Get-Command notepad.exe).Source -PassThru
    $deadline = (Get-Date).AddSeconds(10)
    do {
      $notepadProcess = Get-Process Notepad -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -notin $existingIds -and $_.MainWindowHandle -ne 0 } |
        Select-Object -First 1
      if (-not $notepadProcess) { Start-Sleep -Milliseconds 100 }
    } while (-not $notepadProcess -and (Get-Date) -lt $deadline)
    if (-not $notepadProcess) { throw '等待新 Notepad 窗口超时。' }

    $roots = Wait-AutomationRootsByProcessId -ProcessId $notepadProcess.Id
    $root = $roots[0]
    $editor = Wait-Descendant -Root $root -ControlType ([System.Windows.Automation.ControlType]::Document) -AllowFirst
    if (-not [VirtualKeyboardT15Native]::SetForegroundWindow([IntPtr]$root.Current.NativeWindowHandle)) {
      throw '无法将 Notepad 置于前台。'
    }
    $editor.SetFocus()
    Start-Sleep -Milliseconds 200

    $focusHwnd = [IntPtr]$editor.Current.NativeWindowHandle
    if ($focusHwnd -eq [IntPtr]::Zero) { throw 'Notepad 编辑区未暴露原生 HWND。' }
    $readLength = { [int][VirtualKeyboardT15Native]::SendMessage($focusHwnd, 0x000E, [IntPtr]::Zero, [IntPtr]::Zero) }
    $metricPassed = { param($before, $after, $expected) ($after - $before) -eq $expected }

    $keyboard = Start-Keyboard
    return Invoke-OverlayProbe -Keyboard $keyboard -ReadMetric $readLength -MetricPassed $metricPassed -ScenarioName 'Notepad'
  }
  finally {
    if ($keyboard) { Stop-OwnedProcess $keyboard.Process }
    Stop-OwnedProcess $notepadProcess
    Stop-OwnedProcess $launcher
  }
}

function Invoke-ChromeScenario {
  $keyboard = $null
  $chromeProcesses = @()
  $profileDirectory = $null
  try {
    $resolvedChrome = Resolve-ChromePath
    $existingIds = @(Get-Process chrome -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    $profileDirectory = Join-Path ([IO.Path]::GetTempPath()) ("virtual-keyboard-t15-{0}" -f [Guid]::NewGuid().ToString('N'))
    $html = '<!doctype html><meta charset="utf-8"><input id="t15Input" autofocus aria-label="T1.5 test input">'
    $url = 'data:text/html;charset=utf-8,' + [Uri]::EscapeDataString($html)
    $null = Start-Process -FilePath $resolvedChrome -ArgumentList @(
      '--new-window',
      '--no-first-run',
      '--disable-default-apps',
      "--user-data-dir=$profileDirectory",
      "--app=$url"
    ) -PassThru

    $deadline = (Get-Date).AddSeconds(15)
    $chromeWindowProcess = $null
    do {
      $chromeProcesses = @(Get-Process chrome -ErrorAction SilentlyContinue |
          Where-Object { $_.Id -notin $existingIds })
      $chromeWindowProcess = $chromeProcesses | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
      if (-not $chromeWindowProcess) { Start-Sleep -Milliseconds 100 }
    } while (-not $chromeWindowProcess -and (Get-Date) -lt $deadline)
    if (-not $chromeWindowProcess) { throw '等待隔离 Chrome 窗口超时。' }

    $roots = Wait-AutomationRootsByProcessId -ProcessId $chromeWindowProcess.Id
    $root = $roots[0]
    $editor = Wait-ChromeEditor -Root $root
    if (-not [VirtualKeyboardT15Native]::SetForegroundWindow([IntPtr]$root.Current.NativeWindowHandle)) {
      throw '无法将 Chrome 置于前台。'
    }
    $editor.SetFocus()
    Start-Sleep -Milliseconds 200

    $readLength = {
      $pattern = [System.Windows.Automation.ValuePattern]$editor.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)
      return $pattern.Current.Value.Length
    }
    $metricPassed = { param($before, $after, $expected) ($after - $before) -eq $expected }

    $keyboard = Start-Keyboard
    return Invoke-OverlayProbe -Keyboard $keyboard -ReadMetric $readLength -MetricPassed $metricPassed -ScenarioName 'Chrome input'
  }
  finally {
    if ($keyboard) { Stop-OwnedProcess $keyboard.Process }
    foreach ($process in $chromeProcesses) { Stop-OwnedProcess $process }
    if ($profileDirectory) {
      $fullProfile = [IO.Path]::GetFullPath($profileDirectory)
      $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
      if ($fullProfile.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -and
          [IO.Path]::GetFileName($fullProfile).StartsWith('virtual-keyboard-t15-', [StringComparison]::Ordinal)) {
        if (Test-Path -LiteralPath $fullProfile) {
          Remove-Item -LiteralPath $fullProfile -Recurse -Force
        }
      }
      else {
        Write-Warning "拒绝清理未通过安全校验的 Chrome 测试目录：$fullProfile"
      }
    }
  }
}

if (-not [Environment]::UserInteractive) {
  throw 'T1.5 必须在交互式 Windows 桌面会话中运行。'
}
foreach ($requiredFile in @($appDll, $testHostDll)) {
  if (-not (Test-Path -LiteralPath $requiredFile)) {
    throw "找不到 '$requiredFile'。请先执行 scripts\build.ps1。"
  }
}

Add-Type -AssemblyName UIAutomationClient
if (-not ('VirtualKeyboardT15Native' -as [type])) {
  Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class VirtualKeyboardT15Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr ActiveWindow;
        public IntPtr FocusWindow;
        public IntPtr CaptureWindow;
        public IntPtr MenuOwnerWindow;
        public IntPtr MoveSizeWindow;
        public IntPtr CaretWindow;
        public Rect CaretRectangle;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);
}
'@
}

$script:resolvedDotnet = Resolve-DotnetPath
$scenarioNames = if ($Scenario -eq 'All') {
  @('WpfTestHost', 'Notepad', 'Chrome')
}
else {
  @($Scenario)
}

$results = @()
foreach ($scenarioName in $scenarioNames) {
  Write-Host "==> T1.5 $scenarioName"
  try {
    $result = switch ($scenarioName) {
      'WpfTestHost' { Invoke-WpfTestHostScenario }
      'Notepad' { Invoke-NotepadScenario }
      'Chrome' { Invoke-ChromeScenario }
    }
    $results += $result
    $result | Format-List
  }
  catch {
    Write-Host "FAILED: $scenarioName - $($_.Exception.Message)" -ForegroundColor Red
    $results += [pscustomobject]@{ Scenario = $scenarioName; Passed = $false; Error = $_.Exception.Message }
  }
}

$failed = @($results | Where-Object { -not $_.Passed })
$evidenceDirectory = Join-Path $repoRoot 'artifacts\t1.5'
$null = New-Item -ItemType Directory -Path $evidenceDirectory -Force
$evidencePath = Join-Path $evidenceDirectory ("t1.5-{0}.json" -f (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'))
$operatingSystem = Get-CimInstance Win32_OperatingSystem
[pscustomobject]@{
  SchemaVersion = 1
  RecordedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
  OperatingSystem = [pscustomobject]@{
    Caption = $operatingSystem.Caption
    Version = $operatingSystem.Version
    BuildNumber = $operatingSystem.BuildNumber
  }
  InteractionSource = 'HumanPhysicalMouse'
  Results = $results
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $evidencePath -Encoding UTF8

Write-Host "==> T1.5 汇总：$($results.Count - $failed.Count)/$($results.Count) 场景通过"
Write-Host "==> 非敏感证据：$evidencePath"
if ($failed.Count -gt 0) {
  exit 1
}
