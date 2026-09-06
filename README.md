# Virtual Keyboard（Windows 智能悬浮虚拟键盘）

C# + WPF + .NET 10 LTS，MVP 首发平台为 Windows x64（`win-x64`）。
功能范围见《Windows 智能悬浮虚拟键盘软件功能规格说明.md》，实现设计见《Windows 智能悬浮虚拟键盘方案设计文档.md》，当前进展见《Windows 智能悬浮虚拟键盘开发计划 TODO.md》。

> 当前状态：M4 的 T4.1-T4.7 输入引擎及 M5 的 T5.1-T5.7 布局、状态、视图、密码与触摸实现任务已完成；完整键盘端到端输入及实体触摸屏仍待验收，权限/目标切换实机门禁与 M1-M3 交互式矩阵也仍待验收。

动态键盘已通过统一动作分发器接入实际输入：每次动作先进入有界串行队列并重新验证 TargetSession，再按 `key`、`hotkey`、`text`、`modifier` 独立路径发送。Shift/Ctrl/Alt 锁存与 CapsLock 系统切换已接入；目标替换会使尚未执行的旧动作失效，退出会先停止队列再释放热键安全闩锁。
修正后的状态视觉由同一个 `KeyboardControllerState` 快照驱动，Shift/Ctrl/Alt/CapsLock 的活动态不会与实际发送状态分离。

`VirtualKeyboard.Core.Configuration` 已定义 schema v1 配置模型、验证器和 `ConfigurationRepository`：透明度限制 30%–100%，键盘宽度 240–2000 DIP、高度 120–1000 DIP、边距 0–128 DIP，布局 ID 最长 128 字符。仓库从 `%LocalAppData%\\VirtualKeyboard\\config.json` 加载，保存使用同目录临时文件、Flush 和原子替换；损坏配置会备份到 `recovery` 并回退安全默认值，保存失败保留内存配置。

键盘标题栏的“设置”可打开独立、允许激活的设置窗口。窗口覆盖启用、自动显示/隐藏、尺寸、透明度、边距、布局、手动位置模式和详细诊断；保存前验证，持久化失败时窗口保持打开并提示，编辑后的有效配置仍保留在内存中。

应用启动后常驻系统托盘。托盘菜单提供启用/暂停、显示当前键盘、设置、重新加载布局和退出；键盘标题栏关闭按钮只隐藏可复用窗口，退出请使用托盘菜单。

应用按“当前用户 + Windows 会话”保持单实例。重复启动不会创建第二套监听器；第二实例会通知已运行实例打开设置窗口，然后立即退出。

退出采用幂等清理：先停止新输入并清除目标/状态，再释放热键安全闩锁、窗口和诊断资源；应用层保存当前配置，最后移除托盘图标和单实例句柄。

项目执行决策：M6 完成后跳过 M7，直接进入 M8。M7 的安全、压力、8 小时稳定性和性能实测均保持未完成，不作为已取得的发布证据。

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
- 版本化便携 ZIP 与 SHA-256：`artifacts/release/`；选择、升级、卸载和回滚策略见 [ADR-007](docs/adr/0007-framework-dependent-portable-package.md)。
- 以上目录均由 `.gitignore` 忽略，不进入仓库。

当前 1.0.0 制品没有 Authenticode 签名，只能作为内测包。发布安全检查及未满足门禁见 [安全检查报告](docs/release/security-review-1.0.0.md)。

安装、启动、托盘、设置、布局、诊断隐私、升级/卸载和平台限制见 [内测使用与支持指南](docs/user-guide.md)。

M8 当前验收事实见 [兼容矩阵](docs/release/compatibility-matrix-1.0.0.md)、[DPI/多屏矩阵](docs/release/dpi-matrix-1.0.0.md)、[AC-001 至 AC-015 验收表](docs/release/acceptance-1.0.0.md)和[发布阻断清单](docs/release/known-issues-1.0.0.md)。

## 布局 schema

`VirtualKeyboard.Core.Layouts` 提供版本 1 的不可变布局 DTO、严格验证器和 `LayoutRepository`。布局限制为最多 16 行、每行 64 键、合计 256 键；动作仅允许 `text`、`key`、`hotkey`、`modifier`，不提供命令或脚本入口。Repository 先加载安装目录的只读内置布局，再加载 `%LocalAppData%\VirtualKeyboard\layouts` 用户布局；内置 ID 优先，单文件不超过 1 MiB。重载失败会保留同一文件最后一次有效快照，错误包含 JSON 风格字段路径，但不会回显 `text.value`。托盘命令和界面提示将在后续 UI 任务中接入。

内置 `builtin.qwerty.en-US` 随应用构建和 win-x64 发布到 `layouts\builtin\qwerty.en-US.json`，包含 A-Z、0-9、Space、Backspace、Enter、Tab、Escape、Shift、Ctrl、Alt 和 CapsLock。标准键统一走可受 Shift/Ctrl/Alt 影响的 `key` 路径，自定义 Unicode 文本才走 `text`。关闭、设置和拖动是窗口 UI 行为，不在布局中声明输入 action。

`KeyboardController` 保存与目标会话绑定的一次性 Shift/Ctrl/Alt 状态：Ctrl/Alt 在下一输入动作取用后清除，Shift 只在可打印键或文本动作后清除；目标替换、清空及退出都会清理瞬时状态。CapsLock 使用经过目标复核的系统按键切换，并在刷新时读取真实系统 toggle bit，不用虚拟状态猜测。

`KeyboardLayoutView` 从 JSON 对应的不可变视图模型生成五行按键，行列都使用星号权重，最小按键尺寸为 36×36 DIP。每个按键均不可聚焦、不可进入 Tab 导航；按下时显示状态，释放到键外、丢失鼠标捕获或取消不会触发动作，同一按下最多触发一次。

密码目标采用双层白名单：视图不生成不安全按键，动作分发前再次检查。只允许单个标准字符、封闭的标准/编辑键以及 Shift/CapsLock；自定义短语、hotkey、不透明扫描码、Ctrl/Alt 和未知动作均默认拒绝。判定结果只包含封闭 reason code，不携带控件 Name、Value 或动作文本。

按键显式处理单指 TouchDown/Move/Up/LostCapture，并复用鼠标的单次手势状态机；触摸事件标记 handled，避免 WPF 鼠标提升造成双触发。当前机器没有可自动驱动的实体触摸屏，因此真实触摸与拖动冲突仍作为明确 P1 实机风险，不能用合成鼠标结果冒充通过。

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
src/VirtualKeyboard.App        WPF 宿主应用（键盘、设置和系统托盘）
src/VirtualKeyboard.Core       平台无关核心（分类、状态、布局、配置，无 WPF/UIA/P-Invoke 引用）
src/VirtualKeyboard.Windows    Windows 适配层（目标捕获、UIA、Overlay、定位和 SendInput）
tests/VirtualKeyboard.Core.Tests
tests/VirtualKeyboard.Windows.Tests
tests/VirtualKeyboard.IntegrationTests
tests/VirtualKeyboard.TestHost WPF/WinForms 测试宿主与自检（T0.5）
scripts/build.ps1              统一构建入口
docs/adr/                      架构决策记录
```
