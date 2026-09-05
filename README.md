# Virtual Keyboard（Windows 智能悬浮虚拟键盘）

C# + WPF + .NET 10 LTS，MVP 首发平台为 Windows x64（`win-x64`）。
功能范围见《Windows 智能悬浮虚拟键盘软件功能规格说明.md》，实现设计见《Windows 智能悬浮虚拟键盘方案设计文档.md》，当前进展见《Windows 智能悬浮虚拟键盘开发计划 TODO.md》。

> 当前状态：M0（工程与测试基础）进行中——T0.3 构建/测试脚本完成，T0.4 隐私安全日志骨架（`VirtualKeyboard.Core.Diagnostics`，13 个自动测试通过）完成，T0.5 TestHost 待做。产品功能（M1 起）尚未实现。

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

- WPF 控件页：普通/只读 TextBox、PasswordBox、多行编辑框、Button、不可聚焦空白区六类区域；页面右侧实时显示当前键盘焦点与各控件接收的按键计数（PasswordBox 仅计数，不读取/显示密码值）。
- 独立启动：

  ```powershell
  dotnet run --project tests/VirtualKeyboard.TestHost
  ```

- 自动自检（`--selftest`）：启动后顺序执行 31 项检查（控件存在与关键属性、真实 WPF 焦点语义、不可聚焦区拒绝键盘焦点、对五个控件逐个触发真实 WPF PreviewKeyDown routed 事件以覆盖 XAML 事件绑定/处理器映射/按键计数/展示），逐项输出 PASS/FAIL 并给出结论；退出码 0 = 全部通过（该页的自动证据，本次实测全部通过、退出码 0）：

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
tests/VirtualKeyboard.TestHost 测试宿主（T0.5a：WPF 页已实现 + --selftest 自检；WinForms 页 T0.5b 未开始）
scripts/build.ps1              统一构建入口
docs/adr/                      架构决策记录
```
