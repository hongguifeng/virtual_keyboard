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
| M8 | 兼容验收、打包与发布 | 4-6 人日 | M6（按 2026-09-06 项目决策跳过 M7） | AC-001 至 AC-015 完成 |

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
  - 用户先聚焦 Notepad，键盘自动识别目标并显示（早期托盘/调试捕获入口已在最终流程删除）。
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

- [x] **T1.4（P0，0.75 人日）实现发送前目标校验**
  - 点击前校验 GetForegroundWindow、焦点 HWND 和 SessionId。
  - 用户切换到另一应用后点击 `A`，本次输入必须取消。
  - 校验失败不得调用 SetForegroundWindow。
  - 对应：FR-INP-001，AC-009。
  - 实现：`TargetSessionValidator` 在发送前重新读取前台/进程/焦点并确认 `SessionId` 仍为当前会话；`ValidatedSingleKeyInputSender` 校验失败返回 `TargetInvalid`，不调用底层发送器、不激活目标。`A` 按钮仅通过该入口发送。
  - 验证：Core 30/30、Windows 16/16、Integration 2/2 通过；覆盖会话匹配、会话替换、前台 HWND/进程/焦点 HWND 变化和零底层发送调用。

- [ ] **T1.5（P0，0.5 人日）验证窗口和焦点行为**
  - 自动记录点击前后前台 HWND、焦点 HWND、键盘 HWND。
  - 测试 Notepad、WPF TestHost、Chrome 输入框。
  - 验证键盘 HWND 从不成为前台窗口。
  - 对应：AC-001、AC-005。
  - 自动证据：`OverlayFocusBehaviorTests` 创建真实 WPF 目标窗口与 Overlay HWND，记录点击前/显示后/点击后的前台 HWND、GUI 焦点 HWND、键盘 HWND；验证 `WM_MOUSEACTIVATE → MA_NOACTIVATE`、按钮 Click 单次触发、前台/焦点保持不变，且键盘 HWND 从未成为前台。当前 Windows 测试 58/58 通过。
  - 实机采集：`scripts/verify-t1.5.ps1` 自动准备 WPF TestHost、Notepad、隔离 Chrome input 和发布后的 Overlay；测试者按提示使用真实物理鼠标点击，脚本记录三阶段 HWND、焦点保持、键盘不前台和非敏感输入计数，JSON 证据写入 `artifacts/t1.5/`。可用 `-RepeatCount 100` 执行 Notepad 风险门禁。
  - 未完成原因：合成鼠标→SendInput 链与真人输入语义不同，不能作为端到端通过证据；仍需在 Windows 10 22H2、Windows 11 上分别运行上述真人鼠标矩阵并附证据，完成前不得勾选 T1.5。本地采集不读取或输出窗口标题、输入文本或密码值。

- [x] **T1.6（P0，0.25 人日）实现最小退出清理**
  - 关闭窗口、取消输入队列、释放本程序按下的键。
  - 验证进程退出后无托盘残留或按键状态异常。
  - 对应：FR-APP-003。
  - 实现：关闭按钮通过 `OverlayWindowAdapter.Close` 关闭主窗口；`OnClosed` 幂等释放 HWND hook、Overlay 适配器和诊断资源。当前 M1 切片没有异步输入队列、托盘图标或跨批次保持的修饰键，单键始终以同一批次 KeyDown/KeyUp 成对提交，因此没有额外残留状态。
  - 验证：Core 30/30、Windows 16/16、Integration 3/3 通过；真实 WPF HWND 测试通过关闭按钮触发关闭并确认窗口不可见、资源已进入释放状态。TestHost WPF 31 项 + WinForms 36 项自检通过且进程正常退出。

### M1 风险门禁

- [ ] Notepad 中连续点击 `A` 100 次，目标焦点不丢失且每次最多输入一个字符。
- [ ] 切换到其他窗口后旧目标不接收输入。
- [ ] Overlay 的 NoActivate 行为在 Windows 10 和 Windows 11 均通过。
- [ ] 若门禁失败，暂停后续 UI 开发，先评估原生 HWND 宿主替代方案。

## 7. M2：UIA 检测、分类和状态机

目标：可靠地从全局焦点事件建立或失效目标会话，并驱动自动显示/隐藏。

### TODO

- [x] **T2.1（P0，0.75 人日）实现 UIA 专用 MTA 线程**
  - 创建可启动/停止的 FocusObservationService。
  - 在线程内注册/注销 FocusChanged handler。
  - 回调不访问 WPF UI，异常不会逃逸到进程边界。
  - 对应：FR-FOC-001、FR-APP-003。
  - 实现：`FocusObservationService` 在专用后台 MTA 线程注册/注销 UI Automation FocusChanged handler；事件先进入有界信号再由同一 MTA 线程调用观察者，观察者异常隔离，Start/Stop/Dispose 可重复调用且生命周期等待有 5 秒上限。
  - 验证：Windows.Tests 21/21、完整 Release 构建通过；覆盖 MTA 注册与回调线程、观察者异常隔离、重复启停/重启和注册失败清理。

- [x] **T2.2（P0，0.75 人日）实现 FocusSnapshot 和版本控制**
  - 每个事件生成单调递增 FocusVersion。
  - 只收集允许的元数据，不读取 Value。
  - 忽略虚拟键盘自身进程。
  - 对应：FR-FOC-002、006。
  - 实现：`FocusSnapshot` 使用不可变 `RuntimeIdentity`、封闭的 `FocusControlType` 和版本/时间/窗口/状态字段；`FocusSnapshotVersionGenerator` 以进程内原子计数分配版本，并在创建前过滤无效 HWND、无效 PID和自身进程。`FocusSnapshotFactory` 仅读取 ProcessId、NativeWindowHandle、ControlType、RuntimeId、HasKeyboardFocus、IsEnabled、IsOffscreen、IsPassword；不读取 Name/Value。UIA 读取限定在观察线程，元素失效和 COM 异常转换为无快照结果。
  - 验证：Core 33/33、Windows 26/26、Integration 3/3，完整 Release 构建和 win-x64 发布通过；覆盖版本单调性、RuntimeId 深复制、自进程过滤、真实 WPF HWND 元数据和 MTA 观察线程。BoundingRectangle/caret 锚点留待 T2.4/T3.2，在本任务不读取输入内容。

- [x] **T2.3（P0，1-1.5 人日）实现 EditabilityClassifier**
  - 支持 Editable/NotEditable/Unknown 和稳定 ReasonCode。
  - 实现 ValuePattern、IsReadOnly、TextEditPattern、Edit、Password、caret 证据规则。
  - TextPattern-only 必须返回 Unknown 或 NotEditable，不能返回 Editable。
  - 添加元素失效、COM 异常和无穷/空矩形处理。
  - 对应：FR-FOC-003、004，AC-006、013。
  - 实现：Core `EditabilityClassifier` 按身份、启用/焦点/离屏、只读、密码 Edit、Edit+ValuePattern、Edit/Document+TextEditPattern、caret 和 TextPattern-only 顺序返回封闭的 `ClassificationReasonCode`；桌面/资源管理器选择项等非 Edit 控件不得仅凭 ValuePattern 触发。Windows 证据层不读取 Value/Text，并隔离 UIA/COM 异常。
  - 验证：Core 49/49、Windows 30/30、Integration 3/3，完整 Release 构建和 win-x64 发布通过；覆盖密码 Edit、ValuePattern、TextEditPattern、TextPattern-only、只读优先、焦点/启用/离屏、caret 归属及异常路径。
  - 2026-09-06 反馈修正验证：新增 Other/Pane/Window 即使暴露可写 ValuePattern 也不得触发的回归用例；完整 Release 门禁 Core 219/219、Windows 166/166、Integration 26/26，build/publish 通过且 0 warning/error。

- [x] **T2.4（P0，0.75-1 人日）实现 NativeFocusAdapter**
  - 封装 GetForegroundWindow、GetWindowThreadProcessId、GetGUIThreadInfo、ClientToScreen。
  - 获取焦点 HWND、caret、目标线程键盘布局。
  - 原生错误转换为结果类型，不直接抛到协调器。
  - 对应：FR-FOC-004、FR-POS-001。
  - 实现：`NativeFocusAdapter` 集中封装前台窗口、线程/进程、GUI 线程焦点和 caret、`ClientToScreen` 以及目标线程键盘布局；输出 Core `NativeFocusResult`，不改变前台或焦点。caret 转换为屏幕物理像素并复用有限/非零矩形校验，无效 caret 仅降级为空，原生加载和关键调用失败返回封闭状态。
  - 验证：Core 49/49、Windows 34/34、Integration 3/3，完整 Release 构建和 win-x64 发布通过；覆盖焦点身份、caret 两点坐标转换、键盘布局、无效 caret 降级、转换失败和原生 API 不可用。

- [x] **T2.5（P0，1-1.5 人日）实现 TargetStateCoordinator 状态机**
  - 实现 Disabled、Hidden、Evaluating、VisibleTracking、ManuallySuppressed、SettingsOpen、ShuttingDown。
  - 只接受最新版本结果。
  - 重复事件幂等，旧结果不可重新打开窗口。
  - 对应：FR-VIS-001、002、004。
  - 实现：Core `TargetStateCoordinator` 串行维护 Disabled、Hidden、Evaluating、VisibleTracking、ManuallySuppressed、SettingsOpen、ShuttingDown；所有转换返回封闭 `TargetCoordinatorAction`，由 UI 边界执行显示、隐藏、清会话、取消或刷新。只接受严格递增的焦点版本，分类结果必须同时匹配 pending/latest 版本。手动关闭保存目标身份，同目标重复通知保持抑制，新 Editable 目标或用户显式显示解除抑制。
  - 验证：Core 58/58、Windows 34/34、Integration 3/3，完整 Release 构建和 win-x64 发布通过；覆盖所有状态入口、Editable/NotEditable/Unknown、乱序结果、重复通知、手动抑制、新目标、暂停/恢复、设置、销毁和退出。

- [x] **T2.6（P0，0.5 人日）实现焦点防抖和目标失效**
  - 默认 50 ms 稳定窗口。
  - 新事件取消未执行的旧任务。
  - 目标窗口/元素销毁后隐藏并失效会话。
  - 对应：FR-FOC-006。
  - 实现：`FocusObservationService` 在 UIA MTA 消费循环使用固定 50 ms 稳定窗口；窗口内新信号清空 pending 并重新计时，只在稳定后读取最新 `AutomationElement.FocusedElement`。已开始的迟到评估继续由 T2.5 FocusVersion 门禁拒绝。`TargetStateCoordinator.InvalidateCurrentTarget` 处理 provider 无法再提供已销毁元素身份的路径，统一隐藏、清会话并取消 pending。
  - 验证：Core 59/59、Windows 35/35、Integration 3/3，完整 Release 构建和 win-x64 发布通过；覆盖 64 事件 burst 合并为一次、固定稳定窗口、停止可中断等待、匹配目标销毁和无身份 provider 失效。

- [x] **T2.7（P1，0.5 人日）建立 UIA 判定诊断页/导出信息**
  - 显示当前分类、ReasonCode、控件类型、是否使用降级定位。
  - 不显示或记录目标 Value、输入文本。
  - 对应：FR-DIA-003。
  - 实现：Core `FocusDiagnosticReport` 仅包含时间、版本、PID、数字 HWND、封闭枚举和布尔状态；`FocusDiagnosticExporter` 以确定性 JSON 写入调用方提供的流且不关闭流。App `FocusDiagnosticsView` 显示当前分类、`ClassificationReasonCode`、控件类型、密码标志和是否降级，可导出当前报告；该视图供后续设置窗口承载，不放入 NoActivate 键盘交互路径。
  - 验证：Core 63/63、Windows 35/35、Integration 4/4，完整 Release 构建和 win-x64 发布通过；固定 JSON 字段白名单与反射测试证明 DTO 无自由文本属性，界面测试验证展示和导出，不出现 UIA Name/Value/输入文本字段。详细日志仍由现有 sink 开关控制且默认关闭。

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

- [x] **T3.1（P0，0.5 人日）定义并强制坐标类型**
  - 创建 `PhysicalPixelRect`、`DipSize`、`DpiScale` 等不可混用类型。
  - 禁止 Core 定位接口直接接收 WPF Rect/Point。
  - 对应：FR-POS-005。
  - 实现：Core `Geometry` 提供 `PhysicalPixelPoint`、`PhysicalPixelSize`、`PhysicalPixelRect`、`DipSize`、`DpiScale`，定位与 caret 路径已从通用 `ScreenRectangle` 迁移到物理像素强类型。DIP 尺寸构造时拒绝非有限/非正值，DPI scale 仅接受正值并集中执行 DIP↔物理像素转换；Core 无 WPF Rect/Point 引用。
  - 验证：Core 79/79、Windows 35/35、Integration 4/4，完整 Release 构建和 win-x64 发布通过；覆盖 96/120/144/168/192 DPI、非对称 DPI、负桌面坐标、NaN/Infinity、负尺寸、零矩形和超限坐标。

- [x] **T3.2（P0，0.75 人日）实现 AnchorResolver**
  - 依次尝试 UIA selection/caret、Win32 caret、BoundingRectangle、安全默认锚点。
  - 校验 NaN、Infinity、零矩形、离谱坐标和跨目标矩形。
  - 每次降级产生 ReasonCode。
  - 对应：FR-POS-001，AC-007。
  - 实现：Core `AnchorResolver` 严格按 UIA selection/caret、Win32 caret、UIA BoundingRectangle、安全默认点解析首个有效锚点。`AnchorCandidate` 必须归属于当前目标顶层 HWND，矩形必须通过物理像素校验并与目标窗口或工作区相交；安全默认点位于工作区水平中心、垂直 75%。`AnchorFallbackReason` 位标志完整记录被跳过的来源，无有效工作区时返回显式失败。
  - 验证：Core 88/88、Windows 35/35、Integration 4/4，完整 Release 构建和 win-x64 发布通过；覆盖多段 selection 首个有效值、完整降级顺序、ReasonCode 组合、跨目标/屏外矩形、NaN/Infinity、零/负矩形、超限坐标和无有效安全锚点。

- [x] **T3.3（P0，1 人日）实现 PlacementService**
  - 生成 Bottom、Top、Right、Left 候选。
  - 按可见性、锚点重叠、方向偏好和距离评分。
  - Clamp 到 rcWork，支持任务栏在四边。
  - 过大键盘缩放到工作区安全比例。
  - 对应：FR-POS-002、003、006。
  - 实现：Core `PlacementService` 生成 Bottom/Top/Right/Left 原始候选，以原始矩形完全可见、最终不覆盖锚点、可见比例、稳定方向顺序和中心距离排序；选中结果统一 Clamp 到 `rcWork`。期望尺寸超过工作区时按宽高共同约束等比缩小至 95%，结果标记 `WasScaled`；无效锚点、非正工作区/尺寸或非法 margin 返回显式失败。
  - 验证：Core 99/99、Windows 35/35、Integration 4/4，完整 Release 构建和 win-x64 发布通过；覆盖默认下方、靠近四边的方向选择、负坐标副屏、底部任务栏工作区、等比缩放与最终完全可见、NaN/负 margin 和零宽工作区。
  - 后续候选窗避让（REL-020）：实机对比确认微软输入法正常，微信输入法仅在候选窗靠近键盘时异常，手动移远后恢复；自动 Bottom/Top 定位现额外预留 96 DIP、随目标显示器 DPI 缩放的候选窗安全区，且不检测输入法或读取候选内容。验证：Core 230/230、Windows 210/210、Integration 33/33，Release 构建/发布 0 warning/error；微信输入法仍需真人复验。

- [x] **T3.4（P0，0.75 人日）实现 Monitor/DPI 原生适配**
  - 封装 MonitorFromRect、GetMonitorInfo、GetDpiForWindow 等 API。
  - 支持负坐标显示器。
  - DIP 仅在边界转换为目标显示器物理像素。
  - 对应：FR-POS-004、005。
  - 实现：Windows `MonitorDpiAdapter` 将物理锚点安全转换为 Win32 RECT，以 `MonitorFromRect(MONITOR_DEFAULTTONEAREST)` 选择显示器并读取 `rcMonitor/rcWork`；DPI 依次使用 `GetDpiForWindow`、`GetDpiForMonitor`、96 DPI 受控回退，输出 Core `MonitorMetrics` 和 `DpiSource`。负坐标不归零，非法几何和原生失败转换为封闭 `MonitorMetricsStatus`。
  - 验证：Core 99/99、Windows 39/39、Integration 4/4，完整 Release 构建和 win-x64 发布通过；覆盖负坐标显示器、任务栏工作区、窗口 DPI 优先、非对称显示器 DPI、96 回退、无显示器/信息失败、无效锚点和原生 API 不可用。

- [x] **T3.5（P0，0.5 人日）处理 WM_DPICHANGED**
  - 应用建议矩形作为过渡。
  - 基于当前 TargetSession 重新计算位置和尺寸。
  - 验证跨屏后不会沿用旧 DPI 的物理宽高。
  - 实现：`OverlayWindowAdapter` 在现有 HWND hook 解析 `WM_DPICHANGED`，先以 `SWP_NOACTIVATE` 应用系统建议物理矩形，再发布包含 Core `DpiScale` 和建议矩形的通知；无效消息不应用，消费方异常不逃逸原生窗口过程。`MainWindow` 仅在存在当前 TargetSession 时以配置的 360×176 DIP 和新 DPI 重算物理尺寸，避免复用旧屏物理宽高，并在释放时注销通知。
  - 验证：Core 99/99、Windows 41/41、Integration 5/5，完整 Release 构建和 win-x64 发布通过；真实 WPF HWND 消息验证建议矩形、192 DPI scale、NoActivate、消费方异常隔离，以及当前会话下 360×176 DIP 重算为 720×352 物理像素。

- [x] **T3.6（P1，0.5 人日）实现当前会话的手动位置**
  - 拖动后绑定当前 SessionId。
  - 新目标或 DPI 变化时恢复自动定位。
  - 对应：FR-VIS-006。
  - 实现：Core `ManualPositionTracker` 以物理光标增量计算窗口矩形，并将完成位置严格绑定单一 SessionId；错误会话不能更新/结束。`OverlayWindowAdapter` 通过 `GetCursorPos/GetWindowRect` 和 `SetWindowPos(SWP_NOACTIVATE)` 执行拖动，App 拖动区使用 mouse capture 而非会激活窗口的 `DragMove`。焦点评估失败、新会话、DPI 变化、取消或释放会清除相应状态。
  - 验证：Core 104/104、Windows 42/42、Integration 6/6，完整 Release 构建和 win-x64 发布通过；覆盖物理增量、会话隔离、取消/失效、真实 HWND NoActivate 手动移动，以及新 SessionId/WM_DPICHANGED 清除旧手动位置。
  - 2026-09-06 增补：`Persistent` 模式在进程内保存最后一次手动物理位置，目标替换后按新目标显示器工作区和当前尺寸约束后继续使用；`UntilTargetChanges` 仍在新 SessionId 时恢复自动定位，DPI 改变会清除旧物理坐标。

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

- [x] **T4.1（P0，0.75 人日）实现串行 InputInjectionService**
  - 使用有界串行队列或 SemaphoreSlim。
  - 每个动作绑定 SessionId 和递增 ActionId。
  - 新目标建立时取消尚未开始的旧目标动作。
  - 对应：FR-INP-001。
  - 实现：Core `InputInjectionService` 使用固定容量 `Channel` 和单消费者；多生产者入队时分配单调 ActionId，动作携带 SessionId 与封闭 `InputActionKind`。执行前再次比较当前会话，新 SessionId 建立后尚未开始的旧动作返回 `StaleSession`，已开始动作按自身结果结束。队列满、操作异常、停止分别返回 `QueueFull`、`OperationFailed`、`ServiceStopped`，不重试或并行执行。
  - 验证：Core 109/109、Windows 42/42、Integration 6/6，完整 Release 构建和 win-x64 发布通过；覆盖 10 个并发提交严格串行/顺序/ActionId、会话替换、容量满失败关闭、过期会话、执行异常，以及 Dispose 取消运行中和 pending 动作。

- [x] **T4.2（P0，0.75 人日）增强发送前目标验证**
  - 校验前台顶层 HWND、进程、最新焦点和 RuntimeId。
  - DOM/控件重建导致身份变化时取消本次动作并重新分类。
  - 不通过激活窗口来修复目标。
  - 对应：AC-009。
  - 实现：`TargetSession` 扩展为 FocusVersion、RuntimeIdentity、密码标志和可选物理锚点；`TargetSessionStore` 保留 T1 Win32 弱身份入口并增加从 FocusSnapshot 建立完整会话的入口。`LatestFocusSnapshotStore` 只发布更高版本快照。`TargetSessionValidator` 在既有前台 HWND/进程/焦点 HWND/SessionId 校验后，对完整会话再验证快照版本、RuntimeId、焦点/启用/离屏状态；身份重建返回 `IdentityChangedRequiresReclassification`。发送器取消本次动作并触发重新分类回调，不调用激活 API。
  - 验证：Core 111/111、Windows 48/48、Integration 6/6，完整 Release 构建和 win-x64 发布通过；覆盖完整会话字段、快照版本门禁、RuntimeId 相同/变化/缺失/过旧、失焦元数据、零底层发送及单次重新分类通知。

- [x] **T4.3（P0，0.75 人日）实现 Unicode Text builder**
  - 使用 KEYEVENTF_UNICODE 构建 KeyDown/KeyUp。
  - 覆盖 BMP、中文、重音字符、emoji 和代理对。
  - 批量提交，不使用 VkKeyScan 或 WM_CHAR 回退。
  - 对应：FR-INP-002，AC-012。
  - 实现：`UnicodeTextInputBuilder` 按 .NET 字符串的 UTF-16 code unit 顺序为每个单元生成 `wVk=0`、`KEYEVENTF_UNICODE` Down/Up；代理对不解码或重排。`UnicodeTextInputSender` 最多接受 4096 code unit，空文本零调用成功，非空文本单次 `SendInput`，严格区分成功/短返回/零返回/原生不可用且不重试。诊断仅写 PID、事件计数和错误码。
  - 验证：Core 111/111、Windows 58/58、Integration 6/6，完整 Release 构建和 win-x64 发布通过；覆盖 ASCII、中文、重音字符、emoji、混合文本、空/Null/超长输入、单批提交、部分返回、无重试、原生 API 不可用和日志无文本。

- [x] **T4.4（P0，0.75 人日）实现 Key builder**
  - 支持 VK、scan code、扩展键、KeyDown/KeyUp。
  - 获取目标线程 KeyboardLayout，使用 MapVirtualKeyEx。
  - 覆盖 Enter、Tab、Backspace、Escape、方向/导航键预留。
  - 对应：FR-INP-003。
  - 实现：`KeyInputSender` 从目标焦点 HWND 解析线程和 HKL，以 `MapVirtualKeyExW(MAPVK_VK_TO_VSC_EX)` 获取 scan code；支持 Enter、Tab、Backspace、Escape、四方向及 Home/End、PageUp/PageDown、Insert/Delete。方向/导航键自动带 `KEYEVENTF_EXTENDEDKEY`；`KeyInputBuilder` 同时支持 VK/纯 scan-code 编码、默认 Down+Up 和独立 KeyDown/KeyUp。无效键、零 HWND、线程/HKL/映射失败均在原生发送前拒绝，短返回不重试，日志不含字符化按键结果。
  - 验证：Core 111/111、Windows 86/86、Integration 6/6；覆盖普通/扩展键批次快照、scan-code 模式、单独 Down/Up、目标 HWND→线程→HKL→映射调用链、真实 WPF HWND 原生映射、映射失败、短返回、原生不可用、无重试和诊断隐私；完整 Release 构建和 win-x64 发布通过。

- [x] **T4.5（P0，0.75 人日）实现 Hotkey/Modifier builder**
  - 修饰键顺序按下、逆序释放。
  - 记录并只释放本批次合成按下的修饰键。
  - 读取实体键当前状态，测试 Ctrl/Alt/Shift 冲突。
  - 对应：FR-INP-004、005，AC-011。
  - 实现：`HotkeyInputSender` 快照 1-4 个唯一 Ctrl/Shift/Alt/Win，读取 `GetAsyncKeyState` 高位；实体已按住的修饰键不重复 Down、也不由程序 Up。其余修饰键按声明顺序 Down，主键 Down/Up，修饰键逆序 Up，并单批提交。`HotkeyInputBatch` 记录 `ModifiersPressedByUs` 及事件索引；短返回按已接受前缀只清理仍可能按下的键，异常时逆序尽力释放修饰键，原热键不重试。提交前取消返回 `Cancelled` 且零原生调用；T5.4 负责 UI 点击切换状态和 CapsLock 状态同步。
  - 验证：Core 111/111、Windows 114/114、Integration 6/6；覆盖三修饰键顺序/逆序快照、实体 Ctrl/Shift/Alt 冲突、可变列表快照、每个部分前缀的精确清理、零返回、异常与清理异常、取消前/准备中取消、无效/重复修饰键、线程/HKL/scan 映射失败、真实 `GetAsyncKeyState` 入口和诊断隐私；完整 Release 构建和 win-x64 发布通过。

- [x] **T4.6（P0，0.5 人日）实现输入失败和 UIPI 提示**
  - 检查 SendInput 返回数量。
  - 部分失败不重试，保证释放修饰键。
  - 识别可能的完整性级别差异，显示非侵入提示。
  - 不自动提权、不实现 uiAccess。
  - 对应：FR-INP-006，AC-010。
  - 实现：`ProcessIntegrityInspector` 读取当前/目标 Token Integrity RID，区分目标更高、同级/更低、目标访问被拒和未知，并有界分配 Token 缓冲区、确定关闭句柄。`InputFailureFeedbackFactory` 仅对零/短返回执行完整性比较，生成封闭失败类型和固定非侵入提示；目标确实更高与仅访问被拒使用不同措辞，自身 Token 失败不误判目标。M1 状态区已接入；分类诊断只写 PID、数量、错误码和封闭 ReasonCode，不记录进程名、标题或输入内容，不含提权/uiAccess 路径。
  - 验证：Core 111/111、Windows 133/133、Integration 6/6；覆盖 RID 高/同/低比较、当前/目标 Token 失败差异、真实当前进程 Token 读取、确定/可能权限提示、普通零/短返回、目标变化/取消/无效输入/原生不可用、成功零探测零提示和诊断隐私；完整 Release 构建和 win-x64 发布通过。管理员 TestHost 实机负向矩阵仍按 M4/T7.5 门禁执行。

- [x] **T4.7（P0，0.25 人日）实现退出/崩溃边界清理**
  - 正常退出、取消和已捕获异常路径释放由本程序按下的键。
  - 对无法保证继续运行的输入异常采取失败关闭策略。
  - 实现：`SyntheticKeySafetyLatch` 登记 KeyUp 清理批次中未确认送达的后缀，覆盖可能卡住的主键和修饰键。登记后 `HotkeyInputSender` 在任何映射、状态读取或原生调用前返回 `SafetyFaulted`，拒绝继续输入；Dispose 幂等重发一次有界 KeyUp 批次。正常平衡批次不产生额外释放，取消发生在提交前则零原生调用，未知异常和清理异常均保持原始失败结果并进入安全停止。`InputSafetyFaulted` 诊断仅记录 PID、数量和错误码。
  - 验证：Core 111/111、Windows 139/139、Integration 6/6；覆盖每个热键前缀的待释放集合、主键 KeyUp 清理失败、修饰键清理短返回/异常、后续输入零调用、Dispose 退出重试与幂等、Send/Dispose 并发串行、成功退出零额外调用、取消零调用和安全闩锁诊断；完整 Release 构建和 win-x64 发布通过。

### M4 测试清单

- [x] Text/Key/Hotkey 输入数组快照测试通过。
- [x] Unicode 代理对不会截断。
- [ ] 热键异常和取消后没有修饰键卡住。
- [x] SendInput 返回 0 或部分数量时不会重复提交（热键仅允许独立的 KeyUp 安全清理批次）。
- [ ] 管理员 TestHost 负向测试不提权且可诊断。
- [ ] 输入动作切换目标压力测试无误投。

## 10. M5：JSON 布局与完整键盘

目标：交付可配置的 QWERTY 键盘、状态键和安全布局加载。

### TODO

- [x] **T5.1（P0，0.75 人日）定义版本化 Layout schema**
  - 定义 layout、row、key、action、width、safeForPassword。
  - 规定行数、按键数、文本长度、热键长度和可选 `fnVirtualKey` 等上限。
  - 明确拒绝 command/script/未知可执行动作。
  - 对应：FR-KEY-002、003。
  - 实现：Core 中提供集合防御性复制的不可变 layout/row/key/action DTO，以及 schema v1 `LayoutValidator`；限制 16 行、每行 64 键、总计 256 键、文本 4096 UTF-16 code unit、热键 1-3 个唯一修饰键。action 仅接受 text/key/hotkey/modifier，严格校验字段组合、宽度、ID 唯一性和封闭键名，command/script/未知类型整份拒绝；错误只含字段路径和非敏感固定描述。
  - 验证：Core 133/133、Windows 139/139、Integration 6/6；覆盖四类有效 action、版本/行/按键/字符串/宽度边界、ID 唯一性、键编码、热键长度与重复修饰键、混合字段、command/script/未知动作拒绝及 text 错误不泄露内容；完整 Release 构建和 win-x64 发布通过，0 warning/error。

- [x] **T5.2（P0，0.75 人日）实现 LayoutRepository**
  - 加载只读内置布局和 LocalAppData 用户布局。
  - schema 校验失败时保留最后有效布局。
  - 错误提示包含 JSON 字段路径，但不回显 text.value。
  - 对应：FR-CFG-003、005。
  - 实现：`LayoutRepositoryPaths` 固定应用目录只读内置路径与当前用户 LocalAppData 路径；`LayoutRepository` 确定性地先内置后用户加载，拒绝 ID 覆盖，按规范化文件路径保存最后有效快照并串行发布只读字典。JSON 严格区分字段大小写、拒绝未知字段/注释/尾逗号，限制 1 MiB/深度 16，兼容 UTF-8 BOM；问题 DTO 只含来源、文件名、字段路径、固定 code/message 和旧快照保留标志。
  - 验证：Core 145/145、Windows 139/139、Integration 6/6；覆盖内置/用户顺序、内置 ID 优先、UTF-8 BOM、未知字段和可执行动作拒绝、精确字段路径、text.value 脱敏、超大文件、损坏重载保留旧快照、删除文件移除快照及默认路径；完整 Release 构建和 win-x64 发布通过，0 warning/error。

- [x] **T5.3（P0，0.75 人日）实现内置 QWERTY 布局**
  - A-Z、0-9、Space、Backspace、Enter、Tab、Escape。
  - Shift、Ctrl、Alt、Win、Fn、CapsLock 和方向键。
  - 关闭、设置、拖动区域不定义为可注入 action。
  - 对应：FR-KEY-001。
  - 实现：应用 Content 提供 schema v1 `builtin.qwerty.en-US` 五行布局，主键区按标准美式 QWERTY 顺序覆盖 A-Z、0-9、完整 OEM 标点、Space、Backspace、Enter、Tab、Escape、左右 Shift/Control/Alt、Windows、Fn、CapsLock；四方向键在右侧采用倒 T 排列。数字行通过 `fnVirtualKey` 提供 F1-F12，并在 build/publish 时复制到 `layouts\builtin`。Fn 仅切换应用内部功能层，不伪造硬件 Fn；关闭、设置、拖动不进入布局 action。
  - Review 修正：标准字母、数字和 Space 改走 `key` 而非 `text`，Windows 封闭键枚举扩展到 A-Z、D0-D9、Space 和状态键，避免后续 Shift/Ctrl/Alt 依赖跨语义路径的隐式转换。
  - 验证：Core 146/146、Windows 142/142、Integration 6/6；自动加载发布用 JSON，断言 26 个字母、10 个数字全部使用 key、全部必需功能/状态键及无 close/settings/drag action；新增 A、D0、Space 映射覆盖；完整 Release 构建 0 warning/error，win-x64 发布目录已确认包含布局文件。
  - 本次 review：补齐标准美式 OEM 标点、左右修饰键、Shift 双字符图例及右侧倒 T 方向布局，并增加 Win/Fn 功能层；完整 Release 门禁 Core 214/214、Windows 163/163、Integration 26/26，build/publish 通过且 0 warning/error。
  - 2026-09-06 二次反馈修正：右 Shift 权重调整为 1.8，使第四、第五行总权重同为 16；自动测试同时断言归一化中心和 WPF 实际渲染中心，确保 Up 与 Down 完全对齐。

- [x] **T5.4（P0，0.75 人日）实现 KeyboardController 状态**
  - Shift、Ctrl、Alt 点击切换保持策略和 CapsLock 系统同步。
  - 实体键盘改变 CapsLock 后刷新标签。
  - 目标变化或退出时清理瞬时状态。
  - 对应：FR-INP-005。
  - 实现：Core `KeyboardController` 串行维护版本化状态快照；Shift/Control/Alt/Windows 首次点击真实发送 KeyDown、第二次发送 KeyUp，只有成功才更新蓝底白字加粗边框状态，目标清理和退出逆序释放且失败进入安全闩锁。Fn 只选择 `fnVirtualKey`。Windows `CapsLockStateService` 读取并切换系统 toggle bit。
  - 验证：覆盖 Shift/Ctrl/Alt 跨普通动作保持及二次点击释放、Shift+D1/D2 数字行组合、目标切换/清空/退出、无目标拒绝、CapsLock 实体刷新/切换/失败未知态，以及切换前目标复核和零误发；完整 Release 构建和 win-x64 发布通过，0 warning/error。
  - 2026-09-06 反馈修正验证：覆盖真实修饰键 Down→普通键不重复 Down/Up→第二次点击 Up、原生失败不产生虚假高亮及退出逆序释放；完整 Release 门禁 Core 219/219、Windows 166/166、Integration 26/26。

- [x] **T5.5（P0，0.75 人日）实现相对布局和按键交互**
  - 宽度按权重计算，支持最小点击尺寸。
  - MouseDown/Up/Cancel 保证一次点击最多一次动作。
  - 按键不获取焦点，不进入 Tab 导航。
  - 对应：FR-KEY-004、005。
  - 实现：Core 将已验证布局映射为只读 row/key 视图模型，并以 `KeyGestureController` 约束 Idle→Pressed→Release/Cancel；WPF 视图按 JSON width 创建 Star 列、按行创建 Star 高度，最小键 36×36 DIP。所有按键禁止焦点/Tab，重复按下、键外释放、取消和捕获丢失均不会重复触发，按下态使用不透明度反馈。
  - 验证：Core 164/164、Windows 146/146、Integration 7/7；覆盖模型拒绝未验证布局、权重/动作映射、重复 Down、键内/键外 Up、Cancel、WPF Star 权重、最小点击尺寸、全键 NoFocus/NoTab 和 200% DPI 尺寸；完整 Release 构建和 win-x64 发布通过，0 warning/error。
  - Review 修正：动态按键统一接入有界串行队列和 `LayoutActionDispatcher`，每次发送前复核最新目标与密码策略；key/hotkey/text/modifier 保持独立路径，锁存修饰键参与 hotkey，CapsLock 走系统切换。未知动作在消费状态前失败关闭，退出先停止队列并释放热键安全闩锁，不再只有 A 键可发送。
  - Review 验证：Core 184/184、Windows 154/154、Integration 13/13；覆盖标准 key、锁存/声明 hotkey 合并、Unicode text、CapsLock、会话替换零发送/零状态消费及密码 hotkey/unsafe 拒绝；完整 Release 构建和 win-x64 发布通过，0 warning/error。
  - Review 修正：CapsLock 改为由 `KeyboardController` 唯一调用 `CapsLockStateService`，按键状态视觉统一由状态快照刷新；输入队列退出顺序固定为停止队列→清理控制器→释放热键闩锁→关闭诊断。
  - 后续缺陷修正（REL-016）：QWERT、ASDFG、ZXCV 行均补充 Down/Up 映射回归覆盖。按钮生命周期修正经实机反馈未通过，已回退；普通 key 改为按下发送 KeyDown、释放/取消发送 KeyUp，并由 dispatcher 维护释放兜底，针对中文 IME 组合态修复。验证：Core 226/226、Windows 212/212、Integration 33/33，Release 构建/发布 0 warning/error。
  - 后续根因修正（REL-018）：普通键此前虽解析目标布局 scan code，却未设置 `KEYEVENTF_SCANCODE`，导致 Windows 忽略 `wScan` 并走虚拟键翻译；现改为 `wVk=0` 的 scan-code Down/Up，回归覆盖字母三行、数字、标点和导航键的标志位。验证：Core 226/226、Windows 212/212、Integration 33/33，Release 构建/发布 0 warning/error。
  - 后续 IME 修正（REL-019）：REL-017 把普通键 Down/Up 拆到鼠标按下和松开的两个 `SendInput`，候选窗可在两次调用之间创建或更新；现恢复有效点击后的单批次 Press，同时保留 REL-018 的 scan-code 编码。取消、移出和捕获丢失均为零输入。验证：Core 226/226、Windows 210/210、Integration 33/33，Release 构建/发布 0 warning/error；中文 IME 仍需真人鼠标复验。
  - 后续长按删除（REL-021）：Backspace 短按一次；按住 450ms 后从 140ms 间隔开始重复并逐步加速到 45ms 下限，长按释放不补删。移出、取消、隐藏/卸载均停止；重复动作使用单路在途门禁，不向输入队列积压。验证：Core 233/233、Windows 210/210、Integration 35/35，Release 构建/发布 0 warning/error；仍需真人鼠标复验。

- [x] **T5.6（P0，0.5 人日）实现密码目标策略**
  - `safeForPassword=false` 的键隐藏或禁用。
  - 标准安全键可用。
  - 日志不包含控件 Name、Value 或输入动作内容。
  - 对应：FR-INP-007，AC-013。
  - 实现：Core `PasswordActionPolicy` 对 `safeForPassword` 之外再施加封闭语义白名单，仅允许单个标准字符、标准/编辑 key、Shift/CapsLock；默认拒绝短语、hotkey、scanCode、Ctrl/Alt 和未知动作。WPF 密码布局不生成被拒键，分发前再次校验；结果与提示不携带 label、Name、Value 或动作文本。

- [x] **T5.7（P1，0.25-0.5 人日）验证触摸点击**
  - 单指点击等价于鼠标单击。
  - 拖动手柄不触发相邻按键。
  - 如果测试环境缺失，记录为明确 P1 风险而非默认通过。
  - 对应：FR-KEY-006。
  - 实现：按键显式覆盖 TouchDown/Move/Up/LostTouchCapture，复用单次手势状态机、捕获单触点、按触点坐标判定键内释放，并标记 handled 防止鼠标提升双触发；拖动区域不共享按键捕获。
  - 验证：Core 184/184、Windows 146/146、Integration 13/13；自动化覆盖触摸处理入口存在、单次释放、重复 Down、取消和捕获丢失状态；完整 Release 构建和 win-x64 发布通过，0 warning/error。当前环境无实体触摸屏，真实单指和拖动手柄邻键冲突作为明确 P1 实机风险保留，未用合成鼠标冒充实机通过。

### M5 退出检查

- [ ] 内置 QWERTY 可完整输入英文、数字和特殊键。
- [ ] Shift/CapsLock 标签与实际行为一致。
- [ ] 恶意/损坏布局不会执行代码且不阻止应用运行。
- [ ] 密码模式测试和日志扫描通过。

## 11. M6：配置、设置和托盘

目标：用户可以可靠控制应用并持久化设置，配置损坏时可自动恢复。

### TODO

- [x] **T6.1（P0，0.75 人日）实现 Config schema 和验证**
  - schemaVersion、enabled、autoShow、autoHide、尺寸 DIP、opacity、marginDip、layoutId、手动位置模式、诊断开关、自定义键列表。
  - 设置合理范围，opacity 限制 30%-100%。
  - 对应：FR-CFG-001、002。
  - 实现：Core `KeyboardConfiguration` 与 `ManualPositionMode` 不可变模型；Review 将单个自定义文本键升级为最多 12 项的 `customKeys`，支持 text/key/hotkey/chord 封闭动作、32/256 长度限制，并兼容迁移旧单键字段。chord 复用 schema v1 的 modifiers 数组持久化 1–8 个有序完整键名，input 为空。
  - 验证：Core 199/199；覆盖默认配置、版本/模式、NaN/Infinity/所有数值边界、空/超长布局 ID 及错误消息不泄露 ID；完整 Release 构建和 win-x64 发布通过，0 warning/error。

- [x] **T6.2（P0，0.75 人日）实现 ConfigRepository**
  - 使用 `%LocalAppData%\VirtualKeyboard\config.json`。
  - 临时文件 + Flush + 原子替换。
  - 损坏文件复制到 recovery 后加载默认值。
  - 对应：FR-CFG-003、004，AC-014。
  - 实现：Core `ConfigurationRepository` 限制 64 KiB/JSON 深度 8，兼容 BOM、拒绝注释和尾逗号、忽略未知字段；损坏或无效配置备份到 recovery 并回退安全默认值。保存使用同目录随机临时文件、`Flush(true)` 与 `File.Replace`/首次 `File.Move` 原子更新；保存失败删除临时文件并保留内存快照。
  - 验证：Core 206/206；覆盖默认值、camelCase 往返、BOM/未知字段、JSON 损坏恢复、schema 无效脱敏、保存验证拒绝和保存失败内存保持；完整 Release 构建和 win-x64 发布待本任务提交前执行。

- [x] **T6.3（P0，0.75 人日）实现 SettingsWindow**
  - 设置窗口独立且允许激活。
  - 打开期间状态机进入 SettingsOpen，忽略自身输入控件。
  - 保存前验证，保存失败保留内存状态并提示。
  - 实现：独立可激活 WPF 设置窗口覆盖 schema v1 全字段；透明程度改用带百分比的 0%–70% Slider，并反向映射到整窗 Opacity 1.00–0.30。自定义键改为列表加详情编辑器，组合键通过实体键盘录制并自动识别；主键盘右侧每列最多 5 键，超出后自动新增列且无滚动条，密码模式隐藏。打开设置会释放保持修饰键、使输入会话失效并隐藏 Overlay。
  - Review 增补：无边框 Overlay 通过 `WindowChrome` 支持拖动四边/四角缩放，`WM_EXITSIZEMOVE` 后一次性保存最终 DIP 尺寸，继续保持 `WS_EX_NOACTIVATE`。
  - 验证：Core 206/206、Windows 154/154、Integration 18/18；新增 4 项覆盖窗口激活与字段装载、无效设置不落盘、保存失败内存保持/提示、SettingsOpen 生命周期与目标清理；完整 Release 构建和 win-x64 发布通过，0 warning/error。
  - 2026-09-06 二次反馈修正验证：覆盖透明度 Slider 装载、Ctrl+Shift+S 实体组合录制、7 个自定义键按 5+2 自动分列、无滚动容器、密码目标整体折叠及最终缩放尺寸持久化；完整 Release 门禁 Core 219/219、Windows 166/166、Integration 27/27，build/publish 通过且 0 warning/error。
  - 2026-09-06 三次反馈修正：标准区使用 16 份 Star、自定义区每列使用 2.5 份 Star，移除会造成横向溢出的标准键固定最小列宽；620 DIP 窄窗口下两区共同缩放且不覆盖。透明程度 0%/70% 分别保存为整窗 Opacity 1.0/0.3。新增 `WH_KEYBOARD_LL` 完整 chord 录制，抑制录制期间系统处理并支持 Win+Tab，最多 8 个不同封闭键；发送时全部 KeyDown、逆序 KeyUp，覆盖短发送/异常清理、已保持或实体保持修饰键不重复释放及密码目标拒绝。完整 Release 门禁 Core 226/226、Windows 195/195、Integration 29/29，build/publish 通过且 0 warning/error。
  - 2026-09-06 四次反馈修正：确认普通不透明 WPF 窗口仅设置视觉树 Opacity 在当前 WindowChrome 合成链路中表现为变暗；MainWindow 改为 `WindowStyle=None + AllowsTransparency=True` 的 WPF 透明窗口，使 0.30–1.00 Opacity 参与整窗桌面 Alpha 合成，设置关闭后立即应用当前值。集成测试同时约束透明窗口、可缩放模式和 NoActivate 配置；完整 Release 门禁 Core 226/226、Windows 195/195、Integration 29/29（共 450 项），build/publish 通过且 0 warning/error。
  - 2026-09-06 五次反馈修正：设置项改名为“拖动位置保留”，使用“当前输入框/持续保留”中文选项并随选显示行为说明；Persistent 模式补齐跨输入框运行时行为。普通 key 的 SendInput 仅接受 Down 时不重试原批次，而是独立补发 KeyUp，避免 QWERTY/数字键残留为按下状态。完整 Release 门禁 Core 226/226、Windows 202/202、Integration 30/30（共 458 项），build/publish 通过且 0 warning/error。

- [x] **T6.4（P0，0.5 人日）实现托盘菜单**
  - 启用/暂停、显示当前键盘、设置、重新加载布局、退出。
  - 菜单状态与协调器一致。
  - 对应：FR-APP-002。
  - 实现：使用系统 `NotifyIcon`，提供启用/暂停、显示、设置、重载布局、退出和双击显示；启用状态从当前配置刷新并同步协调器，暂停同时失效输入会话/清除目标。应用改为托盘显式退出，标题栏关闭仅隐藏可复用 Overlay，托盘释放时先隐藏图标。
  - 验证：Core 206/206、Windows 154/154、Integration 20/20；新增菜单完整性/状态切换和逐项命令分发测试，并更新关闭按钮测试验证隐藏后资源仍可复用；完整 Release 构建和 win-x64 发布通过，0 warning/error。

- [x] **T6.5（P0，0.5 人日）实现单实例**
  - 使用命名 Mutex 或等价每用户会话机制。
  - 第二次启动通知第一实例打开设置/托盘入口。
  - 对应：FR-APP-001，AC-015。
  - 实现：当前用户和 Windows SessionId 生成脱敏 `Local\\` 命名 Mutex/AutoResetEvent；第二实例只通知并退出，不创建窗口、托盘或监听器。主实例收到事件后切回 WPF Dispatcher 打开设置；所有命名对象和等待注册均幂等释放。
  - 验证：Core 206/206、Windows 154/154、Integration 22/22；新增双实例所有权/激活通知以及主实例 no-op/重复 Dispose 测试；完整 Release 构建和 win-x64 发布通过，0 warning/error。

- [x] **T6.6（P0，0.25 人日）完成有序退出**
  - 禁用新输入、注销 UIA、释放修饰键、保存配置、清理托盘、关闭窗口。
  - 退出步骤可重复调用且幂等。
  - 对应：FR-APP-003。
  - 实现：协调器先进入 ShuttingDown，随后停止输入队列、清除目标、清理控制器、释放热键安全闩锁，再释放 Overlay/诊断；应用层保存当前配置，然后清理托盘和单实例句柄。全部入口均幂等；当前宿主未创建 UIA 订阅，后续接入时预留在输入停止前注销。
  - 验证：Core 206/206、Windows 154/154、Integration 24/24；新增主窗口退出状态/目标/队列清理、托盘重复 Dispose 和单实例句柄释放后重新取得测试；已有 Windows 热键异常/退出释放测试继续覆盖安全闩锁；完整 Release 构建和 win-x64 发布通过，0 warning/error。

- [ ] **T6.7（P2，延期）开机启动**
  - MVP 不默认实现。
  - 后续根据 MSI/MSIX 方案选择 StartupTask 或当前用户启动项。
  - 对应：FR-APP-004。

### M6 退出检查

- [x] 重复启动不会出现两个监听器。（T6.5：命名 Mutex 双实例所有权与事件通知测试）
- [x] 设置修改重启后保持。（T6.2/T6.3：设置保存与新 Repository 加载往返测试）
- [x] 配置损坏、只读目录和磁盘写失败路径通过。（T6.2：损坏恢复、不可用目标和 IO 失败测试）
- [x] 设置窗口不会触发键盘自动套娃。（T6.3：SettingsOpen 忽略观察并清除目标的状态/集成测试）
- [x] 正常退出无残留托盘图标和修饰键。（T6.6：托盘隐藏/幂等释放、输入停止与热键安全闩锁退出测试）

## 12. M7：安全、诊断与稳定性

目标：验证长期运行、异常恢复、日志隐私和安全边界。

> 项目决策（2026-09-06）：M6 完成后跳过 M7，直接执行 M8。以下任务及退出检查全部保持未勾选；其缺失证据必须在 M8 发布评审中列为未证明风险，不得视为豁免或完成。

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
  - 当前证据：已盘点 Windows 11 Pro x64 build 26100，以及 Notepad、Chrome 152、Edge 152、VS Code 1.136.1；未执行真人交互，Windows 10 环境缺失。详见 `docs/release/compatibility-matrix-1.0.0.md`，任务保持未勾选。
  - 发布评审后修正：`REL-001` 已接通 MTA UIA 观察、无内容模式证据分类、RuntimeId 目标会话、DPI/工作区定位、自动显示/隐藏和手动抑制；Windows 155/155、Integration 25/25。状态为待实机验证，不等于 T8.1 完成。
  - 实测修正：发现当前桌面中的 UIA provider 未触发全局 FocusChanged，观察线程增加 250 ms 有界轮询兜底并按焦点身份/状态去重；新增事件缺失和重复快照测试。该修正仍不替代真人或跨系统兼容矩阵，T8.1 保持未勾选。
  - 产品流程修正：删除早期调试用“捕获当前目标”按钮及 PID/HWND 状态展示，目标会话只由自动焦点评估建立；集成测试改走自动评估入口，T8.1 仍保持未勾选。

- [ ] **T8.2（P0，0.75-1 人日）执行 DPI/多屏矩阵**
  - 100%、125%、150%、175%、200%。
  - 单屏、双屏、负坐标、混合 DPI、任务栏四边抽测。
  - 保存截图和最终窗口物理矩形。
  - 当前证据：仅盘点单逻辑屏 3840×2160、工作区 3840×2112、96 DPI（100%）；其他缩放、双屏、负坐标、混合 DPI 和任务栏方向均无实机证据。详见 `docs/release/dpi-matrix-1.0.0.md`，任务保持未勾选。

- [x] **T8.3（P0，0.75 人日）完成 AC-001 至 AC-015 验收表**
  - 每个 AC 记录环境、步骤、预期、实际、证据链接和缺陷 ID。
  - 失败项不得用“尽量兼容”直接豁免；必须修复或形成明确范围变更。
  - 实现：`docs/release/acceptance-1.0.0.md` 为 15 个 AC 逐项记录环境/步骤、预期、实际状态、证据和缺陷；`known-issues-1.0.0.md` 登记 7 个未接受问题（6 个 P0、1 个 P1）。
  - 结果：0 通过、12 部分自动证据、3 未执行；完成的是验收表本身，不是验收通过，发布门禁仍失败。

- [x] **T8.4（P0，0.75-1 人日）选择并实现 MVP 打包**
  - 在自包含 EXE/MSI 与框架依赖部署中选择一种并记录 ADR。
  - 确认 LocalAppData、卸载、升级和用户布局保留策略。
  - 生成 win-x64 Release 包和校验值。
  - 实现：ADR-007 选择 1.0.0 `win-x64` 框架依赖便携 ZIP；统一构建生成版本化 ZIP 和 UTF-8 no-BOM SHA-256 文件。程序目录只读，升级/回滚替换程序目录，卸载默认保留 `%LocalAppData%\\VirtualKeyboard` 用户数据。
  - 验证：Core 206/206、Windows 154/154、Integration 24/24，Release 0 warning/error；win-x64 publish 与 ZIP 生成成功，ZIP 包含 App EXE/DLL 和内置布局，SHA-256 复算与每次构建生成的 `.sha256` 一致。

- [ ] **T8.5（P0，0.5 人日）执行发布安全检查**
  - 恶意软件扫描。
  - 签名可用时验证 Authenticode；不可用时明确标记内测包。
  - 验证包未声明管理员权限或 uiAccess。
  - 验证安装目录无运行时写入。
  - 已实现：显式 `asInvoker`/`uiAccess=false`/PerMonitorV2 清单；发布校验脚本验证哈希、ZIP 路径、敏感文件、嵌入权限声明和签名状态。受控启动 3 秒前后发布目录文件哈希变化为 0。
  - 未完成：当前 EXE `NotSigned`；本机 Defender 已禁用且无病毒库版本，MpCmdRun 退出码 0 不计作有效扫描证据。详见 `docs/release/security-review-1.0.0.md`，故本任务保持未勾选。

- [x] **T8.6（P0，0.5 人日）完成用户与支持文档**
  - 安装、启动、托盘、设置、布局、卸载。
  - 明确管理员窗口、安全桌面、IME 和第三方 provider 限制。
  - 提供诊断日志导出步骤，不要求用户提供输入内容。
  - 实现：`docs/user-guide.md` 覆盖便携安装/哈希核对、启动与自动目标识别、托盘、设置、布局、升级/卸载/回滚、权限/安全桌面/IME/provider/DPI 限制，以及只导出日志且排除 config/layout/recovery/输入内容的隐私步骤。
  - 说明：文档明确标记自动焦点监听和磁盘诊断尚未接入宿主、跨系统/应用/多屏/触摸证据缺失以及未签名内测范围，不把字段保存或自动测试写成实机验收完成。
  - 发布评审后修正：自动焦点和磁盘诊断现已接入；指南更新为仅压缩 `%LocalAppData%\\VirtualKeyboard\\logs\\*.jsonl`，继续禁止发送 config/layout/recovery 和任何输入内容。
  - 修正验证：Core 207/207、Windows 155/155、Integration 25/25，完整 Release 构建/发布通过；生产 App 受控启动生成 JSONL（已有 8 行增至 10 行），最后事件 `ClassificationCompleted`，实际顶层字段与固定白名单比对无额外字段。M7 独立隐私审计仍未执行。

- [x] **T8.7（P0，0.5 人日）发布评审**
  - 核对发布门禁、已知问题和风险接受记录。
  - 确认版本号、制品哈希、测试报告、变更记录和回滚方案。
  - 批准后标记 MVP 1.0 候选版。
  - 结果：`docs/release/release-review-1.0.0.md` 已核对版本、制品哈希、自动测试、AC、门禁、已知问题、文档和回滚策略；因 6 个 P0 Open、0 个 AC 完整通过、M7/实机矩阵缺失及未签名/未有效扫描，评审明确拒绝候选版，未标记 MVP 1.0 RC。

### M8 发布清单

- [x] Release 构建和全部 P0 自动测试通过。（Core 206、Windows 154、Integration 24；0 warning/error）
- [ ] AC-001 至 AC-015 全部通过或经过正式范围变更。
- [ ] 缺陷发布门禁为 0。
- [ ] 安装、升级、卸载和配置保留验证通过。
- [ ] 发布包、哈希、测试报告、已知限制和支持文档齐全。

## 14. 验收场景执行表

以下表在测试执行时填写实际状态和证据地址。

| 验收项 | 主要任务 | 状态 | 证据 |
|---|---|---|---|
| AC-001 Notepad 端到端 | T1.1-T1.5、T8.3 | 未执行 | `docs/release/acceptance-1.0.0.md` |
| AC-002 非可编辑元素隐藏 | T2.3、T2.5、T8.3 | 部分（自动化） | 同上；REL-001/002 |
| AC-003 空白点击语义 | T2.5、T8.3 | 部分（自动化） | 同上；REL-001/002 |
| AC-004 手动抑制 | T2.5、T2.6、T8.3 | 部分（自动化） | 同上；REL-001 |
| AC-005 浏览器输入框 | T1.5、T2.3、T8.1 | 未执行 | 同上；REL-001/002 |
| AC-006 只读正文不误弹 | T2.3、T8.1 | 部分（自动化） | 同上；REL-001/002 |
| AC-007 VS Code 定位 | T3.2、T3.3、T8.1 | 未执行 | 同上；REL-001/002 |
| AC-008 多显示器与 DPI | T3.1-T3.5、T8.2 | 部分（自动化） | 同上；REL-003 |
| AC-009 目标切换防误输入 | T1.4、T4.2、T7.3 | 部分（自动化） | 同上；REL-004 |
| AC-010 权限边界 | T4.6、T7.5 | 部分（自动化） | 同上；REL-004 |
| AC-011 修饰键清理 | T4.5、T4.7 | 部分（自动化） | 同上；REL-004 |
| AC-012 Unicode | T4.3、T8.3 | 部分（自动化） | 同上；REL-002 |
| AC-013 密码字段 | T2.3、T5.6、T7.2 | 部分（自动化） | 同上；REL-004/006 |
| AC-014 配置恢复 | T6.1、T6.2 | 部分（自动化） | 同上；REL-002 |
| AC-015 单实例与退出 | T6.5、T6.6 | 部分（自动化） | 同上；REL-002/004 |

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

## 19. 发布后功能迭代

- [x] `REL-022` 设置界面支持 English/简体中文切换，默认 English；语言写入兼容 schema v1 的 `uiLanguage` 字段，旧配置缺失时默认英语；设置窗口即时刷新，保存后主窗口提示和托盘菜单同步更新。自动验证覆盖默认值、旧配置迁移、持久化、非法枚举、设置窗口切换及双语托盘菜单。完整 Release 门禁：Core 234/234、Windows 210/210、Integration 37/37，构建/发布 0 warning/error，发布安全静态检查通过。
- [x] `REL-023` 根 README 从用户视角以英语重写并作为默认页，新增简体中文版本；两版均使用 `docs/keyboard.png` 实际截图，覆盖安装、使用、设置、自定义组合键、权限边界、隐私及源码构建。
- [x] `REL-024` GitHub Actions：main/PR 只执行 Release 构建与测试；仅 `v*` 版本 Tag 生成版本化 ZIP、SHA-256、安全报告并创建 GitHub Release。版本由 Tag 传入构建和校验脚本。已本地验证 `-SkipPackage` 门禁，以及以测试版本 2.3.4 生成并校验版本化包；Core 234/234、Windows 210/210、Integration 37/37，0 warning/error，工作流 YAML 解析通过。
- [x] `REL-025` 现代化视觉：深色圆角键盘 Chrome、分层键帽、悬停/按下/蓝色锁定状态、轻量标题栏，以及浅色卡片式设置窗口；保持 NoActivate、整窗透明、标准排布、缩放与输入语义。已用实际运行窗口检查圆角、对比度、文字和标准排布，并同步更新 README 截图。完整 Release 门禁：Core 234/234、Windows 210/210、Integration 39/39，构建/发布 0 warning/error，发布安全静态检查通过。

## 18. 下一步启动清单

- [ ] 评审并批准三份文档作为 MVP 基线。
- [ ] 确认 Windows 10、Windows 11、双屏/混合 DPI 测试环境。
- [ ] 确认首发包是否需要 MSI，以及是否已有签名证书。
- [ ] 建立源码仓库、主分支规则和缺陷跟踪位置。
- [ ] 从 T0.1 开始执行；M1 风险门禁通过前不投入非核心视觉开发。
