# Virtual Keyboard（Windows 智能悬浮虚拟键盘）

C# + WPF + .NET 10 LTS，MVP 首发平台为 Windows x64（`win-x64`）。
功能范围见《Windows 智能悬浮虚拟键盘软件功能规格说明.md》，实现设计见《Windows 智能悬浮虚拟键盘方案设计文档.md》，当前进展见《Windows 智能悬浮虚拟键盘开发计划 TODO.md》。

> 当前状态：M4（完整输入引擎）进行中，T4.1-T4.4 已完成；M1-M3 的交互式矩阵仍待验收。下一开发任务为 T4.5 Hotkey/Modifier builder。

## 先决条件

- **操作系统**：Windows（64 位）。
- **.NET SDK**：10.0.4xx feature band 的正式版 SDK（本机已验证 10.0.400）。
  - `global.json` 固定 `10.0.400`，`rollForward: featureBand` 允许使用 10.0.4xx band 内更高的 patch 版本；不允许跨 band，也不允许预览版（`allowPrerelease: false`）。
  - 安装：`winget install Microsoft.DotNet.SDK.10`，或从 [.NET 下载页](https://dotnet.microsoft.com/download) 获取 .NET 10 SDK。
  - 用其他版本 SDK 运行本仓库时，`dotnet` CLI 会明确提示找不到匹配的 SDK（见 `global.json`）。
- **PowerShell**：Windows PowerShell 5.1 或更高（构建脚本无第三方依赖，非交互式）。

## 构建

在仓库根目录执行统一构建入口（检查 SDK → restore → Release 构建 → Release 测试 → win-x64 publish；任一步失败立即以非零码退出）：

```powershell
.\scripts\build.ps1
```

也可以手动分步执行（等价于脚本内部步骤）：

```powershell
dotnet restore VirtualKeyboard.sln -r win-x64
dotnet build VirtualKeyboard.sln -c Release --no-restore
dotnet test tests\VirtualKeyboard.Core.Tests -c Release --no-build --results-directory artifacts\test-results --logger trx
# …对 tests\ 下另外两个 xUnit 测试项目（Windows.Tests、IntegrationTests）重复上一条；TestHost 不是测试项目，不可用于 dotnet test
dotnet publish src\VirtualKeyboard.App\VirtualKeyboard.App.csproj -c Release -r win-x64 -o artifacts\package\win-x64
```

## 输出路径

- 测试结果（TRX）：`artifacts/test-results/`
- win-x64 发布（framework-dependent，运行需已安装 .NET 10 桌面运行时）：`artifacts/package/win-x64/`
- 以上目录均由 `.gitignore` 忽略，不进入仓库。

## 测试宿主（TestHost）

M1/M2 验证用的纯测试宿主（无产品逻辑）：

- WPF 控件页（T0.5a）：普通/只读 TextBox、PasswordBox、多行编辑框、Button、不可聚焦空白区六类区域；页面右侧实时显示当前键盘焦点与各控件接收的按键计数（PasswordBox 仅计数，不读取/显示密码值）。
- WinForms 控件页（T0.5b1）：普通/只读/多行/密码 TextBox、Button、不可聚焦空白区六类区域；页面显示当前焦点和五类控件/合计按键计数。焦点使用逐控件 `Enter`/`Leave` 事件，按键计数使用逐控件 `KeyDown` 事件；密码框仅计数，不读取或显示密码值。
- 独立启动：

  ```powershell
  dotnet run --project tests/VirtualKeyboard.TestHost
  ```

- 自动自检（`--selftest`）：启动后执行 WPF 页 31 项检查和 WinForms 页 36 项检查。WPF 通过 routed `PreviewKeyDown` 覆盖 XAML 事件绑定；WinForms 向五个真实控件句柄发送同步 `WM_KEYDOWN`，覆盖 `KeyDown` 接线、控件映射、逐项/合计计数、焦点与可见展示。逐项输出 PASS/FAIL，退出码 0 表示 67 项全部通过：

  ```powershell
  dotnet run --project tests/VirtualKeyboard.TestHost -- --selftest
  ```

- T1.5 真人鼠标交互验收：先执行完整构建/发布，再从普通 PowerShell 运行下列脚本。脚本依次准备 WPF TestHost、Notepad 和隔离 Chrome input，测试者只按屏幕提示用真实鼠标点击 Overlay；脚本自动采集三阶段前台/焦点/键盘 HWND 和非敏感输入计数，并写入 `artifacts/t1.5/`。合成鼠标不能用于替代此门禁，因为嵌套的合成鼠标→`SendInput` 链与真人输入语义不同。

  ```powershell
  .\scripts\verify-t1.5.ps1 -Scenario All
  # M1 的 100 次 Notepad 风险门禁
  .\scripts\verify-t1.5.ps1 -Scenario Notepad -RepeatCount 100
  ```

  T1.5 只有在 Windows 10 22H2 和 Windows 11 分别留下通过证据后才可勾选；脚本不读取或输出输入文本、密码、窗口标题。

## 仓库结构

```
src/VirtualKeyboard.App        WPF 宿主应用（键盘、设置、托盘，暂未实现）
src/VirtualKeyboard.Core       平台无关核心（分类、状态、布局、配置，无 WPF/UIA/P-Invoke 引用）
src/VirtualKeyboard.Windows    Windows 适配层（目标捕获、UIA、Overlay、定位和 SendInput）
tests/VirtualKeyboard.Core.Tests
tests/VirtualKeyboard.Windows.Tests
tests/VirtualKeyboard.IntegrationTests
tests/VirtualKeyboard.TestHost WPF/WinForms 测试宿主与自检（T0.5）
scripts/build.ps1              统一构建入口
docs/adr/                      架构决策记录
```
