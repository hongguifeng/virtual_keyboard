# Virtual Keyboard（Windows 智能悬浮虚拟键盘）

C# + WPF + .NET 10 LTS，MVP 首发平台为 Windows x64（`win-x64`）。
功能范围见《Windows 智能悬浮虚拟键盘软件功能规格说明.md》，实现设计见《Windows 智能悬浮虚拟键盘方案设计文档.md》，当前进展见《Windows 智能悬浮虚拟键盘开发计划 TODO.md》。

> 当前状态：M0（工程与测试基础）进行中——T0.3 构建/测试脚本、T0.4 隐私安全日志骨架（13 个自动测试）、T0.5a WPF TestHost 页、T0.5b1 WinForms TestHost 页（--selftest 共 62 项自检）完成；T0.5b2 Win32 钩子/焦点监控未完成。产品功能（M1 起）尚未实现。

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
- WinForms 控件页（T0.5b1）：普通/只读/多行/密码 TextBox + Button + 不可 Tab 聚焦 Label 六个独立控件；与 WPF 页独立（NFR-COMP-001），焦点/按键经逐控件 `Enter`/`Leave`/`KeyPress` 真实事件接线验证。注意：本机 .NET 10 的 WinForms 引用程序集缺少 `IsReadOnly`/`IsEnabled`/`ControlEnter`/`ControlLeave` 等成员，故本页用 `ReadOnly`/`Enabled`/逐控件 `Enter`/`Leave`（不依赖缺失成员）。
- 独立启动：

  ```powershell
  dotnet run --project tests/VirtualKeyboard.TestHost
  ```

- 自动自检（`--selftest`）：启动后先执行 WPF 页 31 项检查（控件存在与关键属性、真实 WPF 焦点语义、不可聚焦区拒绝键盘焦点、五个控件的 PreviewKeyDown routed 事件绑定与计数），再执行 WinForms 页 31 项检查（属性、`Show()` 后真实焦点切换、逐控件 Enter/Leave 事件计数、表单 OnKeyPress 按键计数、布局/句柄/关闭）；逐项输出 PASS/FAIL，退出码 0 = 全部通过（62 项，本次实测全部通过）：

  ```powershell
  dotnet run --project tests/VirtualKeyboard.TestHost -- --selftest
  ```

## 仓库结构

```
src/VirtualKeyboard.App        WPF 宿主应用（键盘、设置、托盘，暂未实现）
src/VirtualKeyboard.Core       平台无关核心（分类、状态、布局、配置，无 WPF/UIA/P-Invoke 引用）
src/VirtualKeyboard.Windows    Windows 适配层（UIA、Win32、SendInput，暂未实现）
tests/VirtualKeyboard.Core.Tests
tests/VirtualKeyboard.Windows.Tests
tests/VirtualKeyboard.IntegrationTests
tests/VirtualKeyboard.TestHost 测试宿主（T0.5a WPF 页 + T0.5b1 WinForms 页均已完成；Win32 钩子/焦点监控 T0.5b2 未开始）
scripts/build.ps1              统一构建入口
docs/adr/                      架构决策记录
```
