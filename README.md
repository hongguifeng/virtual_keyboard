# Virtual Keyboard（Windows 智能悬浮虚拟键盘）

C# + WPF + .NET 10 LTS，MVP 首发平台为 Windows x64（`win-x64`）。
功能范围见《Windows 智能悬浮虚拟键盘软件功能规格说明.md》，实现设计见《Windows 智能悬浮虚拟键盘方案设计文档.md》，当前进展见《Windows 智能悬浮虚拟键盘开发计划 TODO.md》。

> 当前状态：M6 已完成，M7 按项目决策跳过，M8 的验收表、打包和用户文档已完成。发布评审后已接通自动焦点宿主，但仍缺实机矩阵/M7 证据且制品未签名、未有效扫描；该构建仍仅限内部验证。

动态键盘已通过统一动作分发器接入实际输入：每次动作先进入有界串行队列并重新验证 TargetSession，再按 `key`、`hotkey`、`chord`、`text`、`modifier` 独立路径发送。Shift/Ctrl/Alt/Win 锁存、Fn 功能层与 CapsLock 系统切换已接入；目标替换会使尚未执行的旧动作失效，退出会先停止队列再释放热键安全闩锁。

目标会话完全由自动焦点识别建立，最终界面不再包含早期调试用的“捕获当前目标”按钮，也不会向用户显示 PID 或窗口句柄。
修正后的状态视觉由同一个 `KeyboardControllerState` 快照驱动，Shift/Ctrl/Alt/Win/Fn/CapsLock 的活动态不会与实际发送状态分离。

`VirtualKeyboard.Core.Configuration` 已定义 schema v1 配置模型、验证器和 `ConfigurationRepository`：内部整窗不透明度限制为 30%–100%（设置页反向显示为 0%–70% 透明程度），键盘宽度 620–2000 DIP、高度 280–1000 DIP、边距 0–128 DIP，布局 ID 最长 128 字符。仓库从 `%LocalAppData%\\VirtualKeyboard\\config.json` 加载，保存使用同目录临时文件、Flush 和原子替换；损坏配置会备份到 `recovery` 并回退安全默认值，保存失败保留内存配置。

键盘标题栏的“设置”可打开独立、允许激活的设置窗口。“透明程度”滑块范围为 0%–70%：0% 完全不透明，数值越大整个窗口越透明。悬浮窗启用 WPF 透明窗口合成并把 Opacity 作用于整个窗口，不使用背景明暗模拟。自定义键最多配置 12 项，界面只需选择“输入文字”或“录制按键或组合键”；录制会收集从首次按下到全部松开的完整按键集合（例如 `Win+Tab` 或 `Ctrl+Shift+S`），无需理解或填写内部修饰键字段。保存后的按键每列最多 5 个，在标准键盘右侧自动新增列，不使用滚动条；自定义区和标准区随窗口宽度按比例共同缩放、不会互相覆盖，并在密码目标中隐藏。

应用启动后常驻系统托盘。托盘菜单提供启用/暂停、显示当前键盘、设置、重新加载布局和退出；键盘标题栏关闭按钮只隐藏可复用窗口，退出请使用托盘菜单。

应用按“当前用户 + Windows 会话”保持单实例。重复启动不会创建第二套监听器；第二实例会通知已运行实例打开设置窗口，然后立即退出。

退出采用幂等清理：先停止新输入并清除目标/状态，再释放热键安全闩锁、窗口和诊断资源；应用层保存当前配置，最后移除托盘图标和单实例句柄。

项目执行决策：M6 完成后跳过 M7，直接进入 M8。M7 的安全、压力、8 小时稳定性和性能实测均保持未完成，不作为已取得的发布证据。

应用宿主现已启动专用 MTA UIA 焦点观察：原生焦点事件经过稳定窗口，provider 不发事件时由 250 ms 当前焦点轮询兜底，等价快照会被去重。随后读取不含 Name/Value 的元数据和模式证据，三态分类通过后建立版本化目标会话，按目标显示器 DPI/工作区定位 Overlay；非可编辑目标按配置自动隐藏。该闭环已有组件/集成自动测试，但真实应用兼容仍以 M8 矩阵为准。

结构化 JSONL 诊断已接入 `%LocalAppData%\\VirtualKeyboard\\logs`，采用 5×4 MiB 的严格上界；详细事件默认关闭并可由设置动态切换。事件类型只允许枚举、数字、布尔和结构化版本号，类型上不能承载输入文本、密码、剪贴板、短语或 UIA Name/Value。

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

当前发布决定见 [1.0.0 发布评审](docs/release/release-review-1.0.0.md)，版本变更见 [CHANGELOG](CHANGELOG.md)。

## 布局 schema

`VirtualKeyboard.Core.Layouts` 提供版本 1 的不可变布局 DTO、严格验证器和 `LayoutRepository`。布局限制为最多 16 行、每行 64 键、合计 256 键；动作仅允许 `text`、`key`、`hotkey`、`chord`、`modifier`，不提供命令或脚本入口。`chord.keys` 可按顺序保存 1–8 个互不重复的封闭键。Repository 先加载安装目录的只读内置布局，再加载 `%LocalAppData%\VirtualKeyboard\layouts` 用户布局；内置 ID 优先，单文件不超过 1 MiB。重载失败会保留同一文件最后一次有效快照，错误包含 JSON 风格字段路径，但不会回显 `text.value`。

内置 `builtin.qwerty.en-US` 随应用构建和 win-x64 发布到 `layouts\builtin\qwerty.en-US.json`。主键区按标准美式 QWERTY 顺序排列，包含完整数字与标点行、三行字母/标点区、左右 Shift/Ctrl/Alt、Space、Win、Fn、CapsLock，方向键在右侧采用倒 T 排列，Up 与 Down 使用相同水平中心。Fn 是应用内部功能层开关：数字行 1-0、减号、等号切换为 F1-F12，不尝试发送厂商专用物理 Fn。标准键统一走可受 Shift/Ctrl/Alt/Win 影响的 `key` 路径，自定义 Unicode 文本才走 `text`。关闭、设置和拖动是窗口 UI 行为，不在布局中声明输入 action。

`KeyboardController` 保存与目标会话绑定的 Shift/Ctrl/Alt/Win/Fn 点击开关状态。Shift/Ctrl/Alt/Win 第一次点击发送真实 KeyDown，第二次发送 KeyUp，只有发送成功才改变蓝底白字的高亮；普通按键不会自动清除。目标替换、暂停、设置和退出会强制释放程序保持的修饰键。Fn 仍是应用内功能层，CapsLock 读取并切换系统 toggle bit。

QWERTY 字母、数字、标点和编辑键都是瞬时普通键：先按目标线程当前键盘布局解析 scan code，再使用 `KEYEVENTF_SCANCODE`；仅在一次有效鼠标/触摸点击完成后，把 KeyDown/KeyUp 放在同一个 `SendInput` 批次中提交，不会像修饰键一样锁存。单批次避免一个普通键跨越中文 IME 候选窗的创建或更新边界，也不让键盘事件与其他输入交错。

自动弹出只接受真实 Edit 的可写 ValuePattern，或 Edit/Document 的 TextEdit/caret 证据；桌面图标和资源管理器文件项等选择型控件不会仅凭 ValuePattern 触发，文件重命名进入 Edit 后仍可正常触发。

自动定位在光标的上方或下方额外保留 96 DIP 的 IME 候选窗安全区，再叠加设置中的边距；空间不足时继续按下、上、右、左四向评分并限制在目标显示器工作区。该规则不读取候选词，也不依赖某个输入法的窗口类，可避免微信输入法等第三方候选窗覆盖键盘上排并抢走点击。

`KeyboardLayoutView` 从 JSON 对应的不可变视图模型生成五行按键，行列都使用星号权重，最小按键尺寸为 36×36 DIP。每个按键均不可聚焦、不可进入 Tab 导航；按下时显示状态，释放到键外、丢失鼠标捕获或取消不会触发动作，同一按下最多触发一次。

悬浮键盘的四边和四角可直接拖动缩放；交互结束后最终宽高以 DIP 写回配置。窗口仍保持 NoActivate，不会因缩放抢走目标输入框焦点。标准布局的最小可用尺寸为 620×280 DIP。

密码目标采用双层白名单：视图不生成不安全按键，动作分发前再次检查。只允许单个标准字符、封闭的字母/数字/OEM 标点/编辑键以及 Shift/CapsLock；自定义短语、hotkey、不透明扫描码、Ctrl/Alt/Win/Fn 和未知动作均默认拒绝。判定结果只包含封闭 reason code，不携带控件 Name、Value 或动作文本。

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
