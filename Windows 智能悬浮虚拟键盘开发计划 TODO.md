# Windows 智能悬浮虚拟键盘开发计划 TODO

## 1. 文档信息

| 项目 | 内容 |
|---|---|
| 文档版本 | 1.0 |
| 编制日期 | 2026-09-06 |
| 计划对象 | MVP 1.0 |
| 状态约定 | 未勾选 = 未完成；勾选必须附验证证据 |
| 功能基线 | [软件功能规格说明](./Windows%20智能悬浮虚拟键盘软件功能规格说明.md) |
| 技术基线 | [方案设计文档](./Windows%20智能悬浮虚拟键盘方案设计文档.md) |

## 2. 计划目标与假设

本计划把 MVP 拆为 9 个里程碑。开发顺序优先验证三个最高风险：

1. WPF 悬浮窗口能被点击且不抢走目标焦点。
2. 输入发送前能可靠验证目标，不向已经切换的窗口误输入。
3. UI Automation 能以可接受的误判率识别目标应用中的可编辑元素。

估算假设：

- 1 名熟悉 C#、WPF 和基础 Win32 的全职开发人员。
- 1 人日按 6 小时有效开发/验证时间估算。
- 已具备 Windows 10、Windows 11、双显示器和至少一个触摸/高 DPI 验证环境。
- Chrome/Edge、VS Code 可用于兼容测试；Office 仅在环境可用时验证。
- 不包含产品视觉设计、商业签名证书采购、企业部署系统接入和 uiAccess 实现。

总估算：**32-43 人日**。兼容性问题可能使 UIA 和 DPI 阶段额外增加 5-10 人日，应保留风险缓冲。

## 3. 优先级、完成标准与执行规则

### 3.1 优先级

- `P0`：MVP 发布阻断项。
- `P1`：MVP 应完成；只有明确风险接受后才能延期。
- `P2`：后续版本候选。

### 3.2 任务完成定义

任务只有同时满足以下条件才能勾选：

- 实现已合入主开发分支。
- 对应单元/集成测试通过。
- 静态检查和 Release 构建通过。
- 没有把输入内容、密码或自定义短语写入日志。
- 对应功能在任务指定环境中验证。
- 验证证据已记录到 `artifacts/test-results/` 或任务跟踪系统。
- 影响需求、设计或限制时，三份文档已同步更新。

### 3.3 缺陷发布门禁

以下缺陷在 MVP 发布前必须为 0：

- 把输入发送到错误窗口或旧目标。
- 键盘点击导致目标焦点丢失。
- 退出后修饰键保持按下。
- 键盘完全位于屏幕外且用户无法恢复。
- 日志包含实际输入内容或密码信息。
- 普通权限程序尝试绕过 UIPI 或自动提权。

## 4. 里程碑总览

| 里程碑 | 内容 | 估算 | 依赖 | 退出门禁 |
|---|---|---:|---|---|
| M0 | 工程与测试基础 | 2-3 人日 | 无 | 可构建、可测试、可生成制品 |
| M1 | NoActivate 单键垂直切片 | 3-4 人日 | M0 | Notepad 输入且不失焦、不误投 |
| M2 | UIA 检测、分类和状态机 | 5-7 人日 | M1 | 自动显示/隐藏和三态分类通过 |
| M3 | 定位、多显示器和 DPI | 4-5 人日 | M2 | 全部坐标矩阵通过 |
| M4 | 完整输入引擎 | 4-5 人日 | M1、M2 | Text/Key/Hotkey、权限失败路径通过 |
| M5 | JSON 布局与完整键盘 | 4-5 人日 | M4 | QWERTY 和状态键通过 |
| M6 | 配置、设置和托盘 | 3-4 人日 | M2、M5 | 配置恢复、单实例和托盘通过 |
| M7 | 安全、诊断与稳定性 | 3-4 人日 | M2-M6 | 隐私、压力和恢复测试通过 |
| M8 | 兼容验收、打包与发布 | 4-6 人日 | M7 | AC-001 至 AC-015 完成 |

M3 和 M4 在接口稳定后可部分并行；单人开发时仍建议按表中顺序推进。

## 5. M0：工程与测试基础

目标：建立可重复构建、测试和产出验证证据的工程骨架。

### TODO

- [x] **T0.1（P0，0.5 人日）确认并记录架构决策**
  - 创建 ADR：技术栈、自动隐藏语义、三态分类、输入三分路、物理像素契约、普通权限边界。
  - 确认目标框架为 `net10.0-windows`、首发 RID 为 `win-x64`。
  - 验证：ADR 与方案设计第 3 章一致，无 C++/WinUI/CMake 残留执行项。

- [x] **T0.2（P0，0.5 人日）创建解决方案和项目结构**
  - 创建 `VirtualKeyboard.App`、`VirtualKeyboard.Core`、`VirtualKeyboard.Windows`。
  - 创建 Core、Windows、IntegrationTests、TestHost 测试项目。
  - 配置 nullable、隐式 using、分析器、Release 确定性构建。
  - 验证：Debug/Release 均可从干净环境构建。

- [x] **T0.3（P0，0.5 人日）固定 SDK 和构建入口**
  - 添加 `global.json`、`Directory.Build.props`。
  - 提供 PowerShell 构建脚本或统一 `dotnet` 命令说明。
  - 输出测试结果到 `artifacts/test-results/`，构建包到 `artifacts/package/`。
  - 验证：错误 SDK 版本能给出清晰提示。

- [x] **T0.4（P0，0.5 人日）建立隐私安全日志骨架**
  - 定义结构化事件 DTO，不允许接收输入文本字段。
  - 实现有界内存队列和本地滚动文件接口，可先使用测试实现。
  - 添加自动测试，确保 InputAction 文本不会被序列化进日志。
  - 对应：FR-DIA-001、002。
  - 验证：`VirtualKeyboard.Core.Diagnostics`（`DiagnosticEvent` 密封记录仅含非敏感字段/类型上无法携带输入文本；`BoundedDiagnosticQueue` 有界丢最旧；`RollingFileDiagnosticSink` 本地滚动、目录不可写/IO 故障降级 no-op；`DiagnosticSerializer` 白名单 JSONL；`DiagnosticLogger` 线程安全入队 + 可选汇聚）。24 个自动测试通过（Release；含 1 个 2 用例 Theory），详细诊断默认关闭。review 修正：`AppVersion` 改为结构化 `struct`（`ushort` 三元组，类型上无法承载任意文本，序列化为 `{"Major","Minor","Revision"}` 而非字符串）；`RollingFileDiagnosticSink` 用 `lock` 串行化轮转/降级等可变状态（线程安全）；`DiagnosticLogger` 对汇聚点 `Write` 用 `try/catch`+锁隔离，汇聚点故障不传播到调用方（NFR-REL-001）。第二轮 review 修正：`BoundedDiagnosticQueue` 用单一锁保护全部可变状态与读取路径（多生产者/消费者并发安全，丢弃最旧与 added=dequeued+dropped 守恒语义不变），新增 2 个并发守恒/无重复测试。第三轮 review 修正：`RollingFileDiagnosticSink` 整条写入路径（可写状态检查/大小预判/轮转判定(size+行字节)/删除与下移/实际追加/故障降级）全部在同一锁内执行，活跃文件恒为 index 0，单文件不超 `maxFileBytes`、总量严格 ≤ `maxFileBytes`×`maxFileCount`（移除宽限），新增 3 个并发/连续滚动测试，共 20 个测试（另修复 .NET 10 中 `FileInfo.Length` 对不存在文件抛 `FileNotFoundException` 导致的首次写入永久降级缺陷）。第四轮 review 微型修正：文件编码改为显式 no-BOM UTF-8（`new UTF8Encoding(false)`，`Encoding.UTF8` 的 3 字节 preamble 不计入 `GetByteCount` 会破坏严格字节上界）；测试新增断言验证所有生成 JSONL 不以 BOM 开头（已合并进现有测试，仍 20 个测试）。第五轮（测试证据修正）：`DiagnosticPrivacyTests` 移除 `AppVersion` 名称特判与无效哨兵断言，改为反射断言 `DiagnosticEvent` 全部公共实例属性与 `DiagnosticLogger.Log` 全部参数均为值类型（`string`/`object`/`dynamic` 从类型上不存在），并以固定 JSON 顶层字段白名单 + `AppVersion` 子对象仅 3 个数字键精确比对序列化输出；新增 `DiagnosticLoggerTests`：抛异常 sink 的 `Write` 异常不传播出 `Log` 且入队路径不受影响（sink 由 `Log` 直接调用、非队列消费者，事件仍可从 logger 队列读回）、并发 `Log` 时 sink `Write` 最大并发为 1 且调用计数完整；共 24 个测试（未改产品代码）。第六轮（测试缺陷修正，测试数不变）：`ThrowingSink` 测试原以"sink 收到事件"推断入队不受影响，且注释"队列是 Write 的唯一入口"与产品实现不符（sink 由 `Log` 直接调用、并非队列消费者）——改为每次 `Log` 后经 `TryReadNext` 从 logger 队列真实读回事件并逐项核对，真正证明 sink 异常不影响入队；`ProbeSink.Write` 原把统计包在自身私有锁内，最大并发恒 ≤ 1（测试失效）——改为 `Interlocked`/`Volatile`（最大并发用 CAS 更新）+ 有界 `SpinWait` 重叠窗，logger 写入锁若被移除测试将稳定失败；并发测试补 `PendingCount`/`DroppedCount` 断言。

- [x] **T0.5（P0，0.5-1 人日）创建 TestHost**
  - 提供普通 TextBox、只读 TextBox、PasswordBox、多行编辑框、Button、不可聚焦空白区。
  - 提供 WPF 和 WinForms 两类控件页。
  - 页面显示当前焦点和接收到的按键计数，仅用于测试进程。
  - 对应：NFR-COMP-001。
  - 验证：WPF 页 31 项自检；WinForms 页 36 项自检通过真实控件焦点与同步 `WM_KEYDOWN` 覆盖 `Enter`/`Leave`/`KeyDown` 接线、映射、逐项/合计计数和可见展示。两页均不读取密码内容，`--selftest` 共 67 项通过、退出码 0；完整 `scripts/build.ps1 -Configuration Release` 通过。

### M0 退出检查

- [x] `dotnet build -c Release` 成功。（T0.3：`scripts/build.ps1` 实测 0 警告 / 0 错误）
- [x] `dotnet test -c Release` 可生成结果文件。（T0.3：三个测试项目均生成 TRX 于 `artifacts/test-results/`；T0.4 起 Core 诊断 24 个测试通过，其余项目 M1 起有产品用例）
- [x] TestHost 可独立启动。（T0.5：WPF/WinForms 两页可独立启动，`--selftest` 67 项通过、退出码 0）
- [x] 日志隐私约束有自动测试。（T0.4：Core 诊断隐私/有界/序列化/线程安全 24 个测试通过）

## 6. M1：NoActivate 单键垂直切片

目标：在没有完整自动检测和完整布局前，先证明核心窗口与输入路径可行。

### TODO

- [x] **T1.1（P0，0.75 人日）实现最小 OverlayWindowAdapter**
  - 创建无边框 WPF 键盘窗口，只包含拖动区、关闭按钮和 `A` 键。
  - 在 `SourceInitialized` 设置 `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`。
  - 处理 `WM_MOUSEACTIVATE -> MA_NOACTIVATE`。
  - 使用 `SetWindowPos + SWP_NOACTIVATE` 显示和移动。
  - 所有交互控件 `Focusable=false`、`IsTabStop=false`。
  - 对应：FR-VIS-003、005，FR-KEY-005。
  - 验证：`VirtualKeyboard.Windows.Tests` 2/2 通过；`MinimalOverlayWindowTests` 在 STA 线程创建真实 WPF HWND，验证 `WS_EX_NOACTIVATE/WS_EX_TOOLWINDOW`、`WM_MOUSEACTIVATE → MA_NOACTIVATE`、`SetWindowPos` 物理像素矩形、移动不改变前台窗口和非法尺寸边界；`VirtualKeyboard.IntegrationTests` 1/1 验证窗口及交互控件的 NoActivate/不可聚焦配置。

- [x] **T1.2（P0，0.5 人日）建立最小 TargetSession**
  - 用户先聚焦 Notepad，再从托盘/调试入口执行“捕获当前目标并显示”。
  - 保存前台 HWND、进程 ID、焦点 HWND 和会话版本。
  - 不允许键盘自行激活目标。
  - 对应：FR-FOC-005。
  - 实现：`NativeForegroundTargetCapture` 集中封装 `GetForegroundWindow`、`GetWindowThreadProcessId`、`GetGUIThreadInfo`；`TargetSessionStore` 原子发布不可变会话并生成单调 `SessionId`；Overlay 调试入口仅显示数字句柄和 PID，不读取标题或输入内容。
  - 验证：Core 30/30、Windows 7/7、Integration 2/2 通过；覆盖成功捕获、无前台窗口、进程/焦点查询失败、自进程过滤、会话清空与并发替换。

- [x] **T1.3（P0，0.75 人日）实现单键发送和返回值检查**
  - `A` 键生成 KeyDown/KeyUp INPUT 数组。
  - 调用 SendInput，验证返回事件数。
  - 失败不自动重试，记录无内容诊断。
  - 对应：FR-INP-003、006。
  - 实现：`SingleKeyInputSender` 固定生成 VK_A 的两个键盘事件并单次调用 `SendInput`；按返回数量区分成功、完全失败和部分失败，原生 API 不可用时返回 `NativeUnavailable`，不执行激活或重试。诊断仅记录数字字段（目标 PID、请求/完成数量、错误码）。
  - 验证：Core 30/30、Windows 11/11、Integration 2/2 通过；覆盖 KeyDown/KeyUp 顺序、VK_A、KeyUp 标志、原生 `INPUT` x64 尺寸、0/1 返回值、无重试和结构化失败诊断。

- [ ] **T1.4（P0，0.75 人日）实现发送前目标校验**
  - 点击前校验 GetForegroundWindow、焦点 HWND 和 SessionId。
  - 用户切换到另一应用后点击 `A`，本次输入必须取消。
  - 校验失败不得调用 SetForegroundWindow。
  - 对应：FR-INP-001，AC-009。

- [ ] **T1.5（P0，0.5 人日）验证窗口和焦点行为**
  - 自动记录点击前后前台 HWND、焦点 HWND、键盘 HWND。
  - 测试 Notepad、WPF TestHost、Chrome 输入框。
  - 验证键盘 HWND 从不成为前台窗口。
  - 对应：AC-001、AC-005。

- [ ] **T1.6（P0，0.25 人日）实现最小退出清理**
  - 关闭窗口、取消输入队列、释放本程序按下的键。
  - 验证进程退出后无托盘残留或按键状态异常。
  - 对应：FR-APP-003。

### M1 风险门禁

- [ ] Notepad 中连续点击 `A` 100 次，目标焦点不丢失且每次最多输入一个字符。
- [ ] 切换到其他窗口后旧目标不接收输入。
- [ ] Overlay 的 NoActivate 行为在 Windows 10 和 Windows 11 均通过。
- [ ] 若门禁失败，暂停后续 UI 开发，先评估原生 HWND 宿主替代方案。

## 7. M2：UIA 检测、分类和状态机

目标：可靠地从全局焦点事件建立或失效目标会话，并驱动自动显示/隐藏。

### TODO

- [ ] **T2.1（P0，0.75 人日）实现 UIA 专用 MTA 线程**
  - 创建可启动/停止的 FocusObservationService。
  - 在线程内注册/注销 FocusChanged handler。
  - 回调不访问 WPF UI，异常不会逃逸到进程边界。
  - 对应：FR-FOC-001、FR-APP-003。

- [ ] **T2.2（P0，0.75 人日）实现 FocusSnapshot 和版本控制**
  - 每个事件生成单调递增 FocusVersion。
  - 只收集允许的元数据，不读取 Value。
  - 忽略虚拟键盘自身进程。
  - 对应：FR-FOC-002、006。

- [ ] **T2.3（P0，1-1.5 人日）实现 EditabilityClassifier**
  - 支持 Editable/NotEditable/Unknown 和稳定 ReasonCode。
  - 实现 ValuePattern、IsReadOnly、TextEditPattern、Edit、Password、caret 证据规则。
  - TextPattern-only 必须返回 Unknown 或 NotEditable，不能返回 Editable。
  - 添加元素失效、COM 异常和无穷/空矩形处理。
  - 对应：FR-FOC-003、004，AC-006、013。

- [ ] **T2.4（P0，0.75-1 人日）实现 NativeFocusAdapter**
  - 封装 GetForegroundWindow、GetWindowThreadProcessId、GetGUIThreadInfo、ClientToScreen。
  - 获取焦点 HWND、caret、目标线程键盘布局。
  - 原生错误转换为结果类型，不直接抛到协调器。
  - 对应：FR-FOC-004、FR-POS-001。

- [ ] **T2.5（P0，1-1.5 人日）实现 TargetStateCoordinator 状态机**
  - 实现 Disabled、Hidden、Evaluating、VisibleTracking、ManuallySuppressed、SettingsOpen、ShuttingDown。
  - 只接受最新版本结果。
  - 重复事件幂等，旧结果不可重新打开窗口。
  - 对应：FR-VIS-001、002、004。

- [ ] **T2.6（P0，0.5 人日）实现焦点防抖和目标失效**
  - 默认 50 ms 稳定窗口。
  - 新事件取消未执行的旧任务。
  - 目标窗口/元素销毁后隐藏并失效会话。
  - 对应：FR-FOC-006。

- [ ] **T2.7（P1，0.5 人日）建立 UIA 判定诊断页/导出信息**
  - 显示当前分类、ReasonCode、控件类型、是否使用降级定位。
  - 不显示或记录目标 Value、输入文本。
  - 对应：FR-DIA-003。

### M2 测试清单

- [ ] 单元测试覆盖每个分类分支和异常分支。
- [ ] 10,000 个乱序/重复焦点结果不会使旧状态覆盖新状态。
- [ ] TestHost 普通、只读、密码、按钮全部符合预期。
- [ ] 网页正文不会仅因 TextPattern 自动弹出。
- [ ] 点击不改变焦点的空白区域时保持显示，符合 AC-003。
- [ ] 手动关闭后重复焦点通知不重新打开，符合 AC-004。

## 8. M3：定位、多显示器和 DPI

目标：所有窗口定位统一使用物理像素，并在复杂编辑器和混合 DPI 环境中保持可见。

### TODO

- [ ] **T3.1（P0，0.5 人日）定义并强制坐标类型**
  - 创建 `PhysicalPixelRect`、`DipSize`、`DpiScale` 等不可混用类型。
  - 禁止 Core 定位接口直接接收 WPF Rect/Point。
  - 对应：FR-POS-005。

- [ ] **T3.2（P0，0.75 人日）实现 AnchorResolver**
  - 依次尝试 UIA selection/caret、Win32 caret、BoundingRectangle、安全默认锚点。
  - 校验 NaN、Infinity、零矩形、离谱坐标和跨目标矩形。
  - 每次降级产生 ReasonCode。
  - 对应：FR-POS-001，AC-007。

- [ ] **T3.3（P0，1 人日）实现 PlacementService**
  - 生成 Bottom、Top、Right、Left 候选。
  - 按可见性、锚点重叠、方向偏好和距离评分。
  - Clamp 到 rcWork，支持任务栏在四边。
  - 过大键盘缩放到工作区安全比例。
  - 对应：FR-POS-002、003、006。

- [ ] **T3.4（P0，0.75 人日）实现 Monitor/DPI 原生适配**
  - 封装 MonitorFromRect、GetMonitorInfo、GetDpiForWindow 等 API。
  - 支持负坐标显示器。
  - DIP 仅在边界转换为目标显示器物理像素。
  - 对应：FR-POS-004、005。

- [ ] **T3.5（P0，0.5 人日）处理 WM_DPICHANGED**
  - 应用建议矩形作为过渡。
  - 基于当前 TargetSession 重新计算位置和尺寸。
  - 验证跨屏后不会沿用旧 DPI 的物理宽高。

- [ ] **T3.6（P1，0.5 人日）实现当前会话的手动位置**
  - 拖动后绑定当前 SessionId。
  - 新目标或 DPI 变化时恢复自动定位。
  - 对应：FR-VIS-006。

### M3 测试矩阵

- [ ] 96、120、144、168、192 DPI 单元测试通过。
- [ ] 显示器位于主屏左、右、上方时位置通过。
- [ ] 主/副屏 DPI 不同，目标跨屏后尺寸正确。
- [ ] 输入框靠近四个屏幕边缘时键盘完全可见。
- [ ] VS Code 获得 caret 时按 caret 定位；无 caret 时有确定降级。
- [ ] 键盘配置尺寸超过工作区时仍可恢复和操作。

## 9. M4：完整输入引擎

目标：实现语义明确、可校验、可清理的 Text、Key、Hotkey 和 Modifier 输入路径。

### TODO

- [ ] **T4.1（P0，0.75 人日）实现串行 InputInjectionService**
  - 使用有界串行队列或 SemaphoreSlim。
  - 每个动作绑定 SessionId 和递增 ActionId。
  - 新目标建立时取消尚未开始的旧目标动作。
  - 对应：FR-INP-001。

- [ ] **T4.2（P0，0.75 人日）增强发送前目标验证**
  - 校验前台顶层 HWND、进程、最新焦点和 RuntimeId。
  - DOM/控件重建导致身份变化时取消本次动作并重新分类。
  - 不通过激活窗口来修复目标。
  - 对应：AC-009。

- [ ] **T4.3（P0，0.75 人日）实现 Unicode Text builder**
  - 使用 KEYEVENTF_UNICODE 构建 KeyDown/KeyUp。
  - 覆盖 BMP、中文、重音字符、emoji 和代理对。
  - 批量提交，不使用 VkKeyScan 或 WM_CHAR 回退。
  - 对应：FR-INP-002，AC-012。

- [ ] **T4.4（P0，0.75 人日）实现 Key builder**
  - 支持 VK、scan code、扩展键、KeyDown/KeyUp。
  - 获取目标线程 KeyboardLayout，使用 MapVirtualKeyEx。
  - 覆盖 Enter、Tab、Backspace、Escape、方向/导航键预留。
  - 对应：FR-INP-003。

- [ ] **T4.5（P0，0.75 人日）实现 Hotkey/Modifier builder**
  - 修饰键顺序按下、逆序释放。
  - 记录并只释放本批次合成按下的修饰键。
  - 读取实体键当前状态，测试 Ctrl/Alt/Shift 冲突。
  - 对应：FR-INP-004、005，AC-011。

- [ ] **T4.6（P0，0.5 人日）实现输入失败和 UIPI 提示**
  - 检查 SendInput 返回数量。
  - 部分失败不重试，保证释放修饰键。
  - 识别可能的完整性级别差异，显示非侵入提示。
  - 不自动提权、不实现 uiAccess。
  - 对应：FR-INP-006，AC-010。

- [ ] **T4.7（P0，0.25 人日）实现退出/崩溃边界清理**
  - 正常退出、取消和已捕获异常路径释放由本程序按下的键。
  - 对无法保证继续运行的输入异常采取失败关闭策略。

### M4 测试清单

- [ ] Text/Key/Hotkey 输入数组快照测试通过。
- [ ] Unicode 代理对不会截断。
- [ ] 热键异常和取消后没有修饰键卡住。
- [ ] SendInput 返回 0 或部分数量时不会重复提交。
- [ ] 管理员 TestHost 负向测试不提权且可诊断。
- [ ] 输入动作切换目标压力测试无误投。

## 10. M5：JSON 布局与完整键盘

目标：交付可配置的 QWERTY 键盘、状态键和安全布局加载。

### TODO

- [ ] **T5.1（P0，0.75 人日）定义版本化 Layout schema**
  - 定义 layout、row、key、action、width、safeForPassword。
  - 规定行数、按键数、文本长度、热键长度等上限。
  - 明确拒绝 command/script/未知可执行动作。
  - 对应：FR-KEY-002、003。

- [ ] **T5.2（P0，0.75 人日）实现 LayoutRepository**
  - 加载只读内置布局和 LocalAppData 用户布局。
  - schema 校验失败时保留最后有效布局。
  - 错误提示包含 JSON 字段路径，但不回显 text.value。
  - 对应：FR-CFG-003、005。

- [ ] **T5.3（P0，0.75 人日）实现内置 QWERTY 布局**
  - A-Z、0-9、Space、Backspace、Enter、Tab、Escape。
  - Shift、Ctrl、Alt、CapsLock。
  - 关闭、设置、拖动区域不定义为可注入 action。
  - 对应：FR-KEY-001。

- [ ] **T5.4（P0，0.75 人日）实现 KeyboardController 状态**
  - 一次性 Shift、Ctrl/Alt 锁存策略和 CapsLock 系统同步。
  - 实体键盘改变 CapsLock 后刷新标签。
  - 目标变化或退出时清理瞬时状态。
  - 对应：FR-INP-005。

- [ ] **T5.5（P0，0.75 人日）实现相对布局和按键交互**
  - 宽度按权重计算，支持最小点击尺寸。
  - MouseDown/Up/Cancel 保证一次点击最多一次动作。
  - 按键不获取焦点，不进入 Tab 导航。
  - 对应：FR-KEY-004、005。

- [ ] **T5.6（P0，0.5 人日）实现密码目标策略**
  - `safeForPassword=false` 的键隐藏或禁用。
  - 标准安全键可用。
  - 日志不包含控件 Name、Value 或输入动作内容。
  - 对应：FR-INP-007，AC-013。

- [ ] **T5.7（P1，0.25-0.5 人日）验证触摸点击**
  - 单指点击等价于鼠标单击。
  - 拖动手柄不触发相邻按键。
  - 如果测试环境缺失，记录为明确 P1 风险而非默认通过。
  - 对应：FR-KEY-006。

### M5 退出检查

- [ ] 内置 QWERTY 可完整输入英文、数字和特殊键。
- [ ] Shift/CapsLock 标签与实际行为一致。
- [ ] 恶意/损坏布局不会执行代码且不阻止应用运行。
- [ ] 密码模式测试和日志扫描通过。

## 11. M6：配置、设置和托盘

目标：用户可以可靠控制应用并持久化设置，配置损坏时可自动恢复。

### TODO

- [ ] **T6.1（P0，0.75 人日）实现 Config schema 和验证**
  - schemaVersion、enabled、autoShow、autoHide、尺寸 DIP、opacity、marginDip、layoutId、手动位置模式、诊断开关。
  - 设置合理范围，opacity 限制 30%-100%。
  - 对应：FR-CFG-001、002。

- [ ] **T6.2（P0，0.75 人日）实现 ConfigRepository**
  - 使用 `%LocalAppData%\VirtualKeyboard\config.json`。
  - 临时文件 + Flush + 原子替换。
  - 损坏文件复制到 recovery 后加载默认值。
  - 对应：FR-CFG-003、004，AC-014。

- [ ] **T6.3（P0，0.75 人日）实现 SettingsWindow**
  - 设置窗口独立且允许激活。
  - 打开期间状态机进入 SettingsOpen，忽略自身输入控件。
  - 保存前验证，保存失败保留内存状态并提示。

- [ ] **T6.4（P0，0.5 人日）实现托盘菜单**
  - 启用/暂停、显示当前键盘、设置、重新加载布局、退出。
  - 菜单状态与协调器一致。
  - 对应：FR-APP-002。

- [ ] **T6.5（P0，0.5 人日）实现单实例**
  - 使用命名 Mutex 或等价每用户会话机制。
  - 第二次启动通知第一实例打开设置/托盘入口。
  - 对应：FR-APP-001，AC-015。

- [ ] **T6.6（P0，0.25 人日）完成有序退出**
  - 禁用新输入、注销 UIA、释放修饰键、保存配置、清理托盘、关闭窗口。
  - 退出步骤可重复调用且幂等。
  - 对应：FR-APP-003。

- [ ] **T6.7（P2，延期）开机启动**
  - MVP 不默认实现。
  - 后续根据 MSI/MSIX 方案选择 StartupTask 或当前用户启动项。
  - 对应：FR-APP-004。

### M6 退出检查

- [ ] 重复启动不会出现两个监听器。
- [ ] 设置修改重启后保持。
- [ ] 配置损坏、只读目录和磁盘写失败路径通过。
- [ ] 设置窗口不会触发键盘自动套娃。
- [ ] 正常退出无残留托盘图标和修饰键。

## 12. M7：安全、诊断与稳定性

目标：验证长期运行、异常恢复、日志隐私和安全边界。

### TODO

- [ ] **T7.1（P0，0.75 人日）完成结构化诊断实现**
  - 实现事件 ID、ReasonCode、耗时、错误码、进程数字 ID。
  - 日志按 7 天或 20 MB 轮转。
  - 详细诊断默认关闭。
  - 对应：FR-DIA-001、003。

- [ ] **T7.2（P0，0.5 人日）执行日志隐私审计**
  - 自动扫描日志，确认没有输入文本、剪贴板、密码 Value、自定义短语。
  - 密码模式下确认不记录 AutomationElement Name。
  - 对应：FR-DIA-002、NFR-PRI-001。

- [ ] **T7.3（P0，0.75 人日）执行焦点和状态压力测试**
  - 10,000 次重复/乱序焦点事件。
  - 快速切换 Notepad、浏览器、VS Code、桌面。
  - 验证队列有界、无旧结果覆盖、无误投。
  - 对应：NFR-REL-001。

- [ ] **T7.4（P0，0.75-1 人日）执行 8 小时稳定性测试**
  - 定时切换焦点、显示隐藏、输入和跨屏。
  - 记录内存、句柄、线程、CPU 和异常数量。
  - 不允许明显单调内存/句柄增长。
  - 对应：NFR-PERF-001、NFR-REL-001。

- [ ] **T7.5（P0，0.5 人日）执行权限与安全负向测试**
  - 普通权限到管理员 TestHost。
  - UAC 提示出现时程序不尝试交互。
  - 恶意布局、超长文本、超多按键被拒绝。
  - 对应：NFR-SEC-001、AC-010。

- [ ] **T7.6（P1，0.5 人日）性能测量和优化**
  - 收集焦点到显示 P50/P95/P99。
  - 测量空闲 CPU 和稳态工作集。
  - 对超标项目定位 UIA 查询、Dispatcher 或重绘热点。
  - 对应：NFR-PERF-001。

### M7 退出检查

- [ ] 无 P0 可靠性、安全或隐私缺陷。
- [ ] 日志隐私扫描通过。
- [ ] P95 显示延迟、CPU 和工作集达到规格目标或有批准的风险接受记录。
- [ ] 8 小时稳定性报告归档。

## 13. M8：兼容验收、打包与发布

目标：完成规格 AC-001 至 AC-015，产出可安装、可回滚、可诊断的 MVP。

### TODO

- [ ] **T8.1（P0，1-1.5 人日）执行 Windows/应用兼容矩阵**
  - Windows 10 22H2、Windows 11。
  - Notepad、TestHost、Chrome/Edge、VS Code。
  - Office 在环境具备时测试并标注版本。
  - 记录每个 provider 的分类 ReasonCode 和定位降级路径。

- [ ] **T8.2（P0，0.75-1 人日）执行 DPI/多屏矩阵**
  - 100%、125%、150%、175%、200%。
  - 单屏、双屏、负坐标、混合 DPI、任务栏四边抽测。
  - 保存截图和最终窗口物理矩形。

- [ ] **T8.3（P0，0.75 人日）完成 AC-001 至 AC-015 验收表**
  - 每个 AC 记录环境、步骤、预期、实际、证据链接和缺陷 ID。
  - 失败项不得用“尽量兼容”直接豁免；必须修复或形成明确范围变更。

- [ ] **T8.4（P0，0.75-1 人日）选择并实现 MVP 打包**
  - 在自包含 EXE/MSI 与框架依赖部署中选择一种并记录 ADR。
  - 确认 LocalAppData、卸载、升级和用户布局保留策略。
  - 生成 win-x64 Release 包和校验值。

- [ ] **T8.5（P0，0.5 人日）执行发布安全检查**
  - 恶意软件扫描。
  - 签名可用时验证 Authenticode；不可用时明确标记内测包。
  - 验证包未声明管理员权限或 uiAccess。
  - 验证安装目录无运行时写入。

- [ ] **T8.6（P0，0.5 人日）完成用户与支持文档**
  - 安装、启动、托盘、设置、布局、卸载。
  - 明确管理员窗口、安全桌面、IME 和第三方 provider 限制。
  - 提供诊断日志导出步骤，不要求用户提供输入内容。

- [ ] **T8.7（P0，0.5 人日）发布评审**
  - 核对发布门禁、已知问题和风险接受记录。
  - 确认版本号、制品哈希、测试报告、变更记录和回滚方案。
  - 批准后标记 MVP 1.0 候选版。

### M8 发布清单

- [ ] Release 构建和全部 P0 自动测试通过。
- [ ] AC-001 至 AC-015 全部通过或经过正式范围变更。
- [ ] 缺陷发布门禁为 0。
- [ ] 安装、升级、卸载和配置保留验证通过。
- [ ] 发布包、哈希、测试报告、已知限制和支持文档齐全。

## 14. 验收场景执行表

以下表在测试执行时填写实际状态和证据地址。

| 验收项 | 主要任务 | 状态 | 证据 |
|---|---|---|---|
| AC-001 Notepad 端到端 | T1.1-T1.5、T8.3 | 未执行 |  |
| AC-002 非可编辑元素隐藏 | T2.3、T2.5、T8.3 | 未执行 |  |
| AC-003 空白点击语义 | T2.5、T8.3 | 未执行 |  |
| AC-004 手动抑制 | T2.5、T2.6、T8.3 | 未执行 |  |
| AC-005 浏览器输入框 | T1.5、T2.3、T8.1 | 未执行 |  |
| AC-006 只读正文不误弹 | T2.3、T8.1 | 未执行 |  |
| AC-007 VS Code 定位 | T3.2、T3.3、T8.1 | 未执行 |  |
| AC-008 多显示器与 DPI | T3.1-T3.5、T8.2 | 未执行 |  |
| AC-009 目标切换防误输入 | T1.4、T4.2、T7.3 | 未执行 |  |
| AC-010 权限边界 | T4.6、T7.5 | 未执行 |  |
| AC-011 修饰键清理 | T4.5、T4.7 | 未执行 |  |
| AC-012 Unicode | T4.3、T8.3 | 未执行 |  |
| AC-013 密码字段 | T2.3、T5.6、T7.2 | 未执行 |  |
| AC-014 配置恢复 | T6.1、T6.2 | 未执行 |  |
| AC-015 单实例与退出 | T6.5、T6.6 | 未执行 |  |

## 15. 需求到任务追踪

| 需求组 | 实现任务 | 主要验证任务 |
|---|---|---|
| FR-APP-* | T0.2、T1.6、T6.4-T6.6 | T8.3、T8.6 |
| FR-FOC-* | T2.1-T2.7 | T7.3、T8.1 |
| FR-VIS-* | T1.1、T2.5-T2.6、T3.6 | T1.5、T8.3 |
| FR-POS-* | T3.1-T3.6 | T8.2 |
| FR-KEY-* | T5.1-T5.7 | T8.1、T8.3 |
| FR-INP-* | T1.3-T1.4、T4.1-T4.7、T5.6 | T7.3、T7.5、T8.3 |
| FR-CFG-* | T5.2、T6.1-T6.3 | T6 退出检查、T8.3 |
| FR-DIA-* | T0.4、T2.7、T7.1-T7.2 | T8.5 |
| NFR-PERF/REL | 全阶段 | T7.3、T7.4、T7.6 |
| NFR-SEC/PRI | T0.4、T4.6、T5.6、T7.2、T7.5 | T8.5 |
| NFR-COMP | T0.5、T2-T5 | T8.1、T8.2 |

## 16. 风险与预留

| 风险 | 概率 | 影响 | 触发信号 | 处理与预留 |
|---|---|---|---|---|
| Chromium/VS Code 不暴露足够编辑证据 | 中 | 高 | 大量 Unknown | 增加 caret 证据和 provider 诊断，预留 2-4 人日 |
| WPF NoActivate 与特定控件交互异常 | 中 | 高 | M1 焦点门禁失败 | 改用原生 HWND 宿主，预留 3-5 人日 |
| UIA provider 长时间阻塞 | 中 | 中高 | P95 延迟、线程堆积 | MTA 隔离；后续工作进程，MVP 预留 1-2 人日诊断 |
| 混合 DPI/负坐标偏移 | 中 | 高 | M3 矩阵失败 | 统一物理像素，预留 2 人日 |
| Unicode 与目标应用行为不一致 | 中 | 中 | 某些应用丢字符 | 保持 Text/Key 语义分离，记录应用限制 |
| 权限边界被误解为缺陷 | 高 | 中 | 管理员窗口失败 | 产品文档和错误提示明确，不临时扩大范围 |
| 缺少 Win10/触摸/Office 环境 | 中 | 中 | 测试无法执行 | 提前在 M0 确认设备；否则记录发布风险 |

## 17. 暂不实施清单

以下任务不应混入 MVP，除非完成正式范围变更：

- [ ] `P2` 任意 Shell command、脚本或程序启动按键。
- [ ] `P2` uiAccess、自动提权或安全桌面输入。
- [ ] `P2` 候选词、预测、云同步和输入内容遥测。
- [ ] `P2` 完整布局拖拽编辑器。
- [ ] `P2` 动画、复杂主题、磁吸和智能避让第三方窗口。
- [ ] `P2` C++/WinUI 3 迁移。
- [ ] `P2` UIA 独立工作进程；仅在稳定性数据证明必要时立项。

## 18. 下一步启动清单

- [ ] 评审并批准三份文档作为 MVP 基线。
- [ ] 确认 Windows 10、Windows 11、双屏/混合 DPI 测试环境。
- [ ] 确认首发包是否需要 MSI，以及是否已有签名证书。
- [ ] 建立源码仓库、主分支规则和缺陷跟踪位置。
- [ ] 从 T0.1 开始执行；M1 风险门禁通过前不投入非核心视觉开发。
