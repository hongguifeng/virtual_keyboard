# Windows 智能悬浮虚拟键盘方案设计文档

## 1. 文档信息

| 项目 | 内容 |
|---|---|
| 文档版本 | 1.0 |
| 文档状态 | 开发基线草案 |
| 编制日期 | 2026-09-06 |
| 目标版本 | MVP 1.0 |
| 上游规格 | [软件功能规格说明](./Windows%20智能悬浮虚拟键盘软件功能规格说明.md) |
| 执行计划 | [开发计划 TODO](./Windows%20智能悬浮虚拟键盘开发计划%20TODO.md) |

## 2. 设计目标

本文档定义实现软件功能规格的技术方案，重点解决以下风险：

1. UI Automation 信息不完整、异步事件乱序和 provider 异常。
2. TextPattern 被误判为可编辑元素。
3. 悬浮键盘显示或点击时抢走目标焦点。
4. SendInput 受目标切换、键盘布局和 UIPI 权限边界影响。
5. UIA/Win32 物理像素与 WPF DIP 混用造成多屏定位错误。
6. 自定义布局和配置损坏影响应用启动。

设计优先级依次为：防止误输入、保持目标焦点、判定正确性、位置可见性、兼容覆盖率、视觉效果。

## 3. 架构决策

### ADR-001：MVP 使用 C#、WPF 和 .NET 10 LTS

状态：已采用。

理由：

- WPF 能快速构建键盘、设置和托盘 UI。
- .NET 10 LTS 适合作为 2026 年新项目的统一运行时基线。
- Win32、UI Automation 和 SendInput 可通过集中式 P/Invoke/框架 API 接入。
- MVP 性能目标不要求迁移到 C++。

不在同一版本中并行维护 C++ + WinUI 3 方案。若未来迁移，应作为新的架构决策和独立项目评估。

### ADR-002：自动隐藏遵循键盘焦点，不使用全局低级鼠标钩子

状态：已采用。

理由：FocusChanged 无法证明每次空白点击，但安装全局鼠标钩子会增加权限、性能和误判风险。MVP 将“仍持有输入焦点”视为仍在输入状态。

### ADR-003：可编辑性采用三态分类

状态：已采用。

分类结果为 `Editable`、`NotEditable`、`Unknown`。TextPattern 仅用于读取文本结构或定位，不单独构成 Editable 证据。默认对 Unknown 不自动显示。

### ADR-004：输入分为 Text、Key 和 Hotkey 三条路径

状态：已采用。

- Text 表达“提交这段 Unicode 文本”。
- Key 表达“按下这个物理/逻辑键”。
- Hotkey 表达“执行这个组合键”。

三种语义不相互隐式转换，避免 VkKeyScan 无法处理字符、布局和 IME 的问题。

### ADR-005：原生屏幕几何统一使用物理像素

状态：已采用。

UIA 矩形、Win32 窗口矩形、caret 矩形、显示器工作区和定位算法全部使用物理像素。配置中的尺寸使用 DIP，只在窗口适配器边界进行换算。键盘最终位置使用原生 `SetWindowPos` 应用，避免混用 WPF `Window.Left/Top`。

### ADR-006：默认普通权限运行

状态：已采用。

MVP 不请求管理员权限、不绕过 UIPI、不支持安全桌面。`uiAccess` 仅作为未来经签名、安装和威胁建模后的可选方案。

## 4. 系统上下文

```text
Windows Desktop
│
├─ Target Applications
│  ├─ Win32 / WinForms / WPF
│  └─ Chromium / Electron / Browser
│
├─ Windows UI Automation ──► FocusObservationService
├─ Win32 Focus/Caret APIs ─► NativeFocusAdapter
├─ Monitor/DPI APIs ───────► PlacementService
└─ SendInput ──────────────► InputInjectionService
                                  │
                                  ▼
                         Current validated target

VirtualKeyboard Process
├─ TrayShell / SettingsWindow
├─ TargetStateCoordinator
├─ KeyboardOverlayWindow (NoActivate)
└─ Config, Layout and Diagnostics
```

程序只观察当前焦点和必要的元素元数据，不读取、缓存或记录目标输入内容。

## 5. 总体架构

```text
FocusObservationService
          │ FocusSnapshot(version)
          ▼
EditabilityClassifier ──────► NativeFocusAdapter
          │ ClassificationResult
          ▼
TargetStateCoordinator ─────► TargetSessionStore
       │              │
       │              ├────► PlacementService ──► OverlayWindowAdapter
       │              │
       │              └────► KeyboardViewModel / KeyboardController
       │                                      │
       └──── state & target validation ◄──────┘
                                              │ InputAction
                                              ▼
                                    InputInjectionService
                                              │
                                              ▼
                                           SendInput

ConfigRepository ─────► all configurable services
LayoutRepository ─────► KeyboardController
DiagnosticEventSink ◄─ structured events from every boundary
```

### 5.1 组件职责

| 组件 | 职责 | 对应需求 |
|---|---|---|
| `AppHost` | 单实例、启动、退出、依赖组装 | FR-APP-* |
| `TrayShell` | 托盘菜单与用户命令 | FR-APP-002 |
| `FocusObservationService` | 监听 UIA 焦点事件，以有界轮询兜底并生成去重的版本化快照 | FR-FOC-001、006 |
| `EditabilityClassifier` | 三态可编辑判定和原因码 | FR-FOC-003、004 |
| `NativeFocusAdapter` | 前台窗口、焦点 HWND、caret、线程布局 | FR-FOC-004、FR-POS-001 |
| `TargetStateCoordinator` | 状态机、乱序抑制、显示/隐藏决策 | FR-VIS-* |
| `TargetSessionStore` | 保存和验证当前目标会话 | FR-FOC-005、FR-INP-001 |
| `PlacementService` | 选择锚点、显示器、DPI 和候选位置 | FR-POS-* |
| `OverlayWindowAdapter` | NoActivate、TopMost、原生窗口定位 | FR-VIS-003、005 |
| `KeyboardController` | 布局、Shift/Caps/修饰键和动作生成 | FR-KEY-*、FR-INP-005 |
| `InputInjectionService` | 校验目标并串行执行 Text/Key/Hotkey | FR-INP-* |
| `ConfigRepository` | 版本化配置、原子保存和恢复 | FR-CFG-* |
| `LayoutRepository` | 布局 schema 校验、内置/用户布局加载 | FR-KEY-002、FR-CFG-005 |
| `DiagnosticEventSink` | 隐私安全的结构化日志、轮转和导出 | FR-DIA-* |

## 6. 进程、线程与并发模型

### 6.1 进程模型

MVP 使用单进程。进程内包含：

- WPF UI 主线程：键盘、设置、托盘和状态呈现。
- UIA 专用 MTA 线程：注册事件和读取 AutomationElement 属性。
- 输入串行队列：按顺序执行输入批次，可使用后台 Task，但不得并行注入。
- 日志后台写入器：有界队列、批量落盘。

如果后续发现 UIA provider 能长期阻塞 COM 调用，再将 UIA 观察器迁移到可重启的独立工作进程；该隔离不属于 MVP。

### 6.2 UIA 线程规则

1. UIA 监听器在线程启动时初始化为 MTA。
2. 回调中只创建轻量事件记录，不直接访问 WPF 控件。
3. 元素属性读取、分类和坐标获取都在 UIA 工作线程完成。
4. 结果通过不可变 DTO 传递到协调器，再由 Dispatcher 更新 UI。
5. 捕获 `ElementNotAvailableException`、COM 异常和无效属性值。
6. 每个焦点事件分配单调递增 `FocusVersion`；协调器只接受当前最新版本。

### 6.3 防抖和背压

- 默认焦点稳定窗口：50 ms，可通过内部常量调整，不作为用户设置。
- 新焦点事件到来时取消尚未开始的旧评估。
- 已经执行但迟到的旧结果因版本号不匹配而丢弃。
- UIA 和日志队列必须有上限；队列满时优先保留最新焦点事件和错误事件。
- 显示/隐藏/移动操作必须比较目标状态，避免相同状态重复调用。

T2.6 在 `FocusObservationService` 的专用 MTA 消费循环内实现稳定窗口，而不是把 UIA 工作转交线程池：收到首个信号后等待 50 ms，期间任一新信号都会清空积压并重新计时，稳定后才读取当前焦点和生成快照。停止信号可立即打断等待。若元素在评估或跟踪期间失效，协调器通过匹配身份的 `TargetDestroyed` 或无可用身份的 `InvalidateCurrentTarget` 进入 Hidden，并发出 HideOverlay、ClearTargetSession、CancelPendingWork。

## 7. 核心数据模型

```csharp
public enum Editability
{
    Editable,
    NotEditable,
    Unknown
}

public sealed record FocusSnapshot(
    long Version,
    DateTimeOffset ObservedAt,
    int ProcessId,
    nint TopLevelHwnd,
    RuntimeIdentity? RuntimeId,
    FocusControlType ControlType,
    bool HasKeyboardFocus,
    bool IsEnabled,
    bool IsOffscreen,
    bool IsPassword);

public sealed record ClassificationResult(
    long Version,
    Editability Value,
    string ReasonCode,
    TargetAnchor? Anchor,
    bool IsPassword);

public sealed record TargetSession(
    long SessionId,
    long FocusVersion,
    DateTimeOffset CreatedAt,
    int ProcessId,
    nint TopLevelHwnd,
    int[]? RuntimeId,
    bool IsPassword,
    TargetAnchor Anchor);

public enum InputActionKind
{
    Text,
    Key,
    Hotkey,
    Modifier
}
```

`RuntimeIdentity` 对 RuntimeId 做深复制并只暴露副本；`FocusControlType` 是 Core 内封闭枚举，避免将 UIA 类型泄漏到核心层。`FocusSnapshotVersionGenerator` 使用 `Interlocked.Increment` 分配进程内单调版本号，在创建前过滤无效 PID、零 HWND 和自身进程。`FocusSnapshotFactory` 在 UIA MTA 观察线程读取上述白名单属性，禁止读取或记录 `Name`/`Value`；元素失效、无效操作和 COM 异常均返回无快照结果。BoundingRectangle 与 caret 不属于 T2.2 快照，分别在后续分类/锚点任务中按物理像素契约读取。

DTO 中不得包含 AutomationElement 的 Value 或用户输入内容。AutomationElement/COM 引用不跨线程长期保存；需要时基于最新焦点重新获取和验证。

## 8. 焦点检测与可编辑性分类

### 8.1 事件来源

主来源：`Automation.AddAutomationFocusChangedEventHandler`。观察线程同时每 250 ms 读取一次当前焦点作为 provider 事件缺失时的有界兜底；轮询快照按进程、顶层 HWND、RuntimeId、控件类型和安全状态字段去重，不重复建立目标会话。原生事件仍经过 50 ms 稳定窗口并优先触发评估。

辅助来源：

- `GetForegroundWindow`：确定前台顶层窗口。
- `GetWindowThreadProcessId` 和 `GetGUIThreadInfo`：确定目标线程、焦点 HWND 和 caret。
- 窗口销毁/前台切换事件可通过 WinEvent 补充，但不得将普通标题或进程名作为编辑判定依据。

### 8.2 分类顺序

```text
Validate element lifetime
        │ invalid/destroyed
        ├──────────────────► Unknown
        ▼
Ignore own process?
        │ yes
        ├──────────────────► NotEditable(Self)
        ▼
Enabled + keyboard focus?
        │ no
        ├──────────────────► NotEditable(NoFocusOrDisabled)
        ▼
Explicit read-only evidence?
        │ yes
        ├──────────────────► NotEditable(ReadOnly)
        ▼
Password Edit control?
        │ yes
        ├──────────────────► Editable(PasswordEdit)
        ▼
Edit + ValuePattern and !IsReadOnly?
        │ yes
        ├──────────────────► Editable(ValuePattern)
        ▼
Edit/Document + TextEditPattern available?
        │ yes
        ├──────────────────► Editable(TextEditPattern)
        ▼
Edit/Document + valid caret evidence?
        │ yes
        ├──────────────────► Editable(CaretEvidence)
        ▼
TextPattern only?
        │ yes
        ├──────────────────► Unknown(TextPatternOnly)
        ▼
NotEditable(NoEditableEvidence)
```

### 8.3 规则细节

当前实现由 Core `EditabilityClassifier` 消费不可变 `EditabilityEvidence`，Windows `EditabilityEvidenceFactory` 在 UIA 观察线程填充 Pattern 可用性和只读状态。分类器不读取 Value/Text；ValuePattern 只有与 `ControlType.Edit` 同时出现才是正向证据，TextEditPattern 仅接受 Edit/Document，因而桌面图标、资源管理器文件项等选择型控件即使暴露可写 ValuePattern 也返回 NotEditable；`TextPattern` 单独存在返回 `Unknown(TextPatternOnly)`。caret 证据必须为有限、非零、合理范围矩形且归属于快照顶层 HWND；无效身份返回 `Unknown(InvalidIdentity)`，禁用/失焦/离屏返回 `NotEditable(NoFocusOrDisabled)`。UIA 元素失效、无效操作和 COM 异常均降级为无证据。

- `ControlType.Edit` 是强提示但不是无条件结论；显式只读优先。
- 密码字段常因安全原因不暴露 ValuePattern，应使用 `IsPassword + Edit + HasKeyboardFocus` 判定。
- TextEditPattern 比 TextPattern 更接近编辑语义，但仍要求元素启用并持有焦点。
- `ControlType.Document + TextPattern` 可能只是网页正文或阅读器，默认返回 Unknown。
- caret 必须是有限数值、位于合理屏幕范围且与目标顶层窗口关联；零矩形不构成证据。
- `Unknown` 默认不显示，但写入原因码和 provider 元数据以便兼容性改进。
- 不维护应用名称白名单。确需 provider 特例时，必须通过版本化兼容规则、测试和独立配置引入。

### 8.4 目标会话建立

T2.4 的 `NativeFocusAdapter` 是前台/GUI 线程原生信息的统一只读边界：一次捕获返回前台 HWND、进程/线程 ID、焦点 HWND、目标线程 HKL 和可选 caret 屏幕矩形。`GUITHREADINFO.rcCaret` 仅在 caret HWND 有效时通过 `ClientToScreen` 转换；无效矩形降级为空，API 或坐标转换失败返回 `NativeFocusStatus`，不会向协调器抛出原生加载异常，也不会调用任何激活 API。

分类成功后：

1. 解析元素顶层 HWND 和进程 ID。
2. 获取 RuntimeId；不可用时使用 HWND、进程和焦点版本组成弱身份。
3. 获取锚点候选。
4. 创建新的 `TargetSessionId`。
5. 原子替换旧会话。
6. 通知状态机定位并显示键盘。

目标元素销毁、前台窗口变化或更高版本焦点结果到达时，旧会话立即失效。

## 9. 状态机设计

### 9.1 状态

```text
Disabled
Hidden
Evaluating
VisibleTracking
ManuallySuppressed
SettingsOpen
ShuttingDown
```

### 9.2 关键转换

| 当前状态 | 事件 | 条件 | 下一状态 | 动作 |
|---|---|---|---|---|
| Hidden | FocusObserved | enabled | Evaluating | 启动版本化评估 |
| Evaluating | Classified | Editable 且版本最新 | VisibleTracking | 建会话、定位、无激活显示 |
| Evaluating | Classified | NotEditable/Unknown | Hidden | 清会话、隐藏 |
| VisibleTracking | FocusObserved | 新版本 | Evaluating | 保留窗口直到判定完成或短暂隐藏策略触发 |
| VisibleTracking | TargetDestroyed | 当前目标 | Hidden | 清会话、隐藏 |
| VisibleTracking | UserClose | 任意 | ManuallySuppressed | 保存被抑制 SessionId、隐藏 |
| ManuallySuppressed | DuplicateFocus | 同一目标 | ManuallySuppressed | 不操作 |
| ManuallySuppressed | Classified | 新 Editable 目标 | VisibleTracking | 建新会话并显示 |
| ManuallySuppressed | UserShow | 当前目标仍有效 | VisibleTracking | 定位并显示 |
| 任意 | UserDisable | 任意 | Disabled | 清会话、隐藏、取消输入 |
| 任意 | OpenSettings | 任意 | SettingsOpen | 暂停自身输入检测、显示设置窗口 |
| SettingsOpen | CloseSettings | enabled | Evaluating/Hidden | 重新获取当前外部焦点 |
| 任意 | Exit | 任意 | ShuttingDown | 注销、释放、保存、退出 |

协调器是唯一允许调用 Overlay 的显示、隐藏和移动方法的组件，避免多个模块争用窗口状态。

T2.5 的 Core `TargetStateCoordinator` 将状态与副作用命令分离：状态转换仅返回 `TargetCoordinatorAction` 位标志，WPF/Windows 组合层负责执行 BeginEvaluation、ShowOrUpdateOverlay、HideOverlay、ClearTargetSession、CancelPendingWork、RefreshFocus。`Observe` 只接受严格递增版本，`ApplyClassification` 还必须匹配当前 pending/latest 版本，因此迟到结果和重复结果均无副作用。手动抑制保存 RuntimeId（不可用时使用进程和顶层 HWND 的保守弱身份），同目标通知不会重开键盘；新目标、用户显式显示或暂停后重新启用会解除抑制。

## 10. NoActivate 悬浮窗口设计

### 10.1 WPF 配置

- `WindowStyle=None`
- `ShowInTaskbar=False`
- `ShowActivated=False`
- `Topmost=True` 仅用于 WPF 语义；实际置顶通过原生调用并带 `SWP_NOACTIVATE`
- 普通按键 `Focusable=False`、`IsTabStop=False`
- 设置窗口与键盘窗口分离；设置窗口允许正常激活

整窗透明度使用 `WindowStyle=None`、`AllowsTransparency=True` 与 WPF `Window.Opacity`。仅设置普通不透明窗口的视觉树 Opacity 在当前 WindowChrome 合成链路上会表现为内容变暗，因此不得作为整窗透明实现。透明窗口仍保留 `WindowChrome` 的 7 DIP 缩放边界以及 Overlay 的 `WS_EX_NOACTIVATE`/`MA_NOACTIVATE` 合约；自动测试必须同时确认透明模式、可缩放模式和不激活属性。

### 10.2 Win32 扩展样式和消息

在 `SourceInitialized` 后获取 HWND，并集中应用：

```text
WS_EX_NOACTIVATE
WS_EX_TOOLWINDOW
```

通过 `HwndSource.AddHook` 处理：

```text
WM_MOUSEACTIVATE -> MA_NOACTIVATE
WM_DPICHANGED    -> 应用建议矩形并重新定位
```

显示和移动使用：

```text
SetWindowPos(
    HWND_TOPMOST,
    physicalPixelRect,
    SWP_NOACTIVATE | SWP_SHOWWINDOW)
```

不得在按键处理过程中调用 `Activate`、`Focus`、`SetForegroundWindow` 或把目标窗口强行带到前台。

T1.1 的最小实现位于 `VirtualKeyboard.Windows.OverlayWindowAdapter`：窗口在 `SourceInitialized` 后集中设置 `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`，通过 `HwndSource.AddHook` 将 `WM_MOUSEACTIVATE` 返回为 `MA_NOACTIVATE`，并以 `SetWindowPos(HWND_TOPMOST, ..., SWP_NOACTIVATE)` 完成显示、移动和尺寸更新。无边框窗口使用 WPF `WindowChrome` 提供 7 DIP 非客户区缩放边界；适配器在 `WM_EXITSIZEMOVE` 后仅发布一次最终 DIP 尺寸，由 App 验证并原子保存，避免拖动期间反复写盘。适配器只接受 UI 线程调用并拒绝非正尺寸。

T1.2 的最小目标捕获实现位于 `VirtualKeyboard.Windows.NativeForegroundTargetCapture`。适配器通过 `GetForegroundWindow` 获取前台顶层 HWND，再用 `GetWindowThreadProcessId` 获取进程 ID 和 GUI 线程，最后通过 `GetGUIThreadInfo` 获取焦点 HWND；任何句柄、线程或进程查询失败都返回可判定的 `TargetCaptureStatus`，不会读取标题、进程名或输入内容，也不会改变前台窗口。捕获本程序自身窗口时返回 `OwnProcess`。`VirtualKeyboard.Core.TargetSessionStore` 使用不可变 DTO 和锁保护的原子替换生成单调 `SessionId`；该适配器在最终实现中用于发送前目标复核，早期 `MainWindow` 手动捕获按钮及 PID/HWND 展示已经删除。

T1.3 的单键发送实现位于 `VirtualKeyboard.Windows.SingleKeyInputSender`。发送器为 `VK_A` 构造一个固定长度为 2 的 `INPUT` 数组（KeyDown 后 KeyUp），通过 `NativeInputApi` 调用一次 `SendInput` 并严格检查返回事件数：2 为成功，0 为失败，1 为部分失败；短返回不重试。原生 API 不可用时返回 `NativeUnavailable`。诊断只写入事件类型、目标 PID、请求/完成数量和错误码等结构化数字，不写入按键文本。发送器不执行目标校验、激活或焦点恢复，UI 接线必须在 T1.4 的发送前校验之后完成。

T1.4 的校验实现位于 `VirtualKeyboard.Windows.TargetSessionValidator` 和 `ValidatedSingleKeyInputSender`。每次 A 键动作先确认期望 `SessionId` 仍是 `TargetSessionStore.Current`，再重新读取前台顶层窗口、进程和 GUI 线程焦点；任一不一致返回 `TargetInvalid` 并取消动作。校验成功后才调用 T1.3 发送器；整个路径不调用 `Activate`、`Focus` 或 `SetForegroundWindow`。早期手动捕获按钮已从最终界面删除，目标会话只由自动焦点评估建立，动态按键均保持 NoActivate。

T1.6 的最小退出路径由 `MainWindow.OnClosed` 统一收口并保持幂等，释放 Overlay 的 `HwndSource` hook 和诊断资源。M1 的单键发送为同步固定批次，不存在后台输入队列或跨批次保持的修饰键；托盘尚未引入，因此当前退出路径没有托盘或合成按键残留。M4 引入串行队列和修饰键后，退出清理将在 T4.7 扩展。

T2.1 的 `FocusObservationService` 将 UI Automation 订阅集中到专用后台 MTA 线程。生产实现通过 `SystemFocusAutomationSource` 注册 `Automation.AddAutomationFocusChangedEventHandler`，回调只设置线程内信号；服务线程消费信号并调用观察者，避免 UIA 回调直接访问 WPF Dispatcher。实测发现部分 provider/桌面环境不产生该事件，因此同一 MTA 线程增加 250 ms 当前焦点轮询兜底，并抑制除版本与时间外完全等价的快照；这保证事件缺失时仍能自动显示，又不产生持续会话替换。注册、消费和注销异常均在服务边界隔离，Start/Stop/Dispose 具备幂等语义，并以 5 秒上限避免生命周期操作无限等待。

TextEditPattern/TextPattern2 等较新的可选 UIA property ID 可能没有在当前托管 UIA 门面中注册；`AutomationProperty.LookupById` 返回空时按“该证据不可用”处理，继续使用标准 ValuePattern/TextPattern 和 caret 证据，不得因可选属性缺失使整次分类异常退出。

T1.5 自动证据由 `VirtualKeyboard.Windows.Tests.OverlayFocusBehaviorTests` 提供：测试在 STA 线程创建真实 WPF 目标窗口和 NoActivate Overlay，调用 `WM_MOUSEACTIVATE` 并触发一次按钮 Click，分别采集前台 HWND、GUI 线程焦点 HWND 和键盘 HWND。断言显示及点击前后前台/焦点句柄保持一致、Overlay HWND 不成为前台，Click 只触发一次。

真实应用矩阵使用 `scripts/verify-t1.5.ps1` 半自动采集：脚本负责启动 WPF TestHost、Notepad、隔离 Chrome input 和发布后的 Overlay，聚焦目标，等待自动目标识别就绪，再由测试者使用真实物理鼠标点击 `A`；每次操作后自动采集前台 HWND、GUI 焦点 HWND、键盘 HWND，并只比较按键计数或文本长度，不保存输入内容。证据写入忽略版本控制的 `artifacts/t1.5/`。不得用 `mouse_event`、UIA InvokePattern 或直接窗口消息冒充此门禁：合成鼠标可能改变激活语义，嵌套的合成鼠标→SendInput 链也可能被系统或自动化宿主过滤。Windows 10 22H2 与 Windows 11 必须分别由真人鼠标执行并留证，才可完成 T1.5/M1 风险门禁。

### 10.3 鼠标与触摸命中

- MouseDown 只改变按压视觉状态。
- MouseUp 仍位于同一按键时提交一次动作。
- 捕获丢失、拖出或窗口隐藏时取消按压。
- 拖动手柄和按键区域使用不同命中区。
- 关闭、设置等系统按键不进入 InputInjectionService。

## 11. 定位与 DPI 设计

### 11.1 坐标契约

| 数据 | 单位 | 备注 |
|---|---|---|
| UIA BoundingRectangle | 屏幕物理像素 | 进入服务时校验有限值 |
| TextPattern selection/caret | 屏幕物理像素 | 可能为空或多段 |
| `GUITHREADINFO.rcCaret` | 客户区坐标 | 转换到屏幕物理像素 |
| `MONITORINFO.rcWork` | 屏幕物理像素 | 支持负坐标 |
| 配置宽高、margin | DIP | 与用户感知尺寸一致 |
| `SetWindowPos` | 屏幕物理像素 | 唯一最终定位出口 |

换算公式：

T3.1 将单位契约落实为 Core 强类型：`PhysicalPixelPoint/Size/Rect` 只用于屏幕和原生定位数据，`DipSize` 只用于用户配置尺寸，`DpiScale` 是两者之间唯一换算入口。类型均不依赖 WPF；物理矩形允许负 X/Y，但拒绝非有限值、负宽高、零矩形和超出合理虚拟桌面范围的数据。UIA/Win32 caret 已统一输出 `PhysicalPixelRect`。

```text
physicalPx = round(dip * monitorDpi / 96)
dip        = physicalPx * 96 / monitorDpi
```

目标 DPI 优先通过目标窗口/键盘窗口的 `GetDpiForWindow` 获得，必要时使用显示器 DPI 回退。无有效信息时使用 96 DPI，并记录降级事件。

T3.4 的 `MonitorDpiAdapter` 是唯一 Monitor/DPI P/Invoke 边界：物理锚点经 floor/ceil 转换为 Win32 RECT，`MonitorFromRect(MONITOR_DEFAULTTONEAREST)` 选择显示器，`GetMonitorInfo` 返回保留负坐标的 monitor/work-area 强类型矩形。DPI 来源以 `DpiSource` 标记为 TargetWindow、Monitor 或 Default96；API 缺失/失败不抛到 Core，返回 `MonitorMetricsStatus`。

### 11.2 锚点解析

`AnchorResolver` 按以下顺序返回首个有效结果：

1. TextPattern/TextPattern2 selection 的第一个有效可见矩形；折叠 selection 时作为 caret。
2. `GetGUIThreadInfo` 的 caret，使用 `ClientToScreen` 转换。
3. UIA BoundingRectangle。
4. 目标窗口所在显示器工作区的中心下方安全点。

矩形有效性规则：

- 坐标为有限数值，绝对值不超过系统虚拟桌面合理范围。
- 宽高非负；caret 允许窄矩形，但不得是完全无位置的零矩形。
- 与目标显示器或目标窗口有交集；否则降级。

T3.2 的 Core `AnchorResolver` 使用 `AnchorCandidate(PhysicalPixelRect, OwnerTopLevelHwnd)` 强制来源归属，依序返回首个有效 selection、Win32 caret 或 BoundingRectangle；每跳过一层就在封闭的 `AnchorFallbackReason` 位标志中记录。所有候选拒绝后，以有效工作区中心偏下位置生成 1×1 安全锚点；工作区自身无效时返回 `NoValidAnchor`，不会传播 NaN/Infinity 或猜测跨目标矩形。

### 11.3 候选布局算法

1. 用 `MonitorFromRect(anchor, MONITOR_DEFAULTTONEAREST)` 获取目标显示器。
2. 读取 `rcWork` 和目标 DPI。
3. 将配置尺寸、margin 从 DIP 转为物理像素。
4. 若键盘大于工作区安全比例，先按比例缩小到工作区的 95%。
5. 生成 Bottom、Top、Right、Left 四个候选矩形。
6. 对每个候选计算：
   - 工作区内可见比例。
   - 与锚点的重叠面积。
   - 与目标控件关键区域的重叠面积。
   - 距离锚点的距离。
7. 评分优先级：完全可见 > 不覆盖锚点 > 可见比例 > Bottom/Top 偏好 > 距离。
8. 对胜出矩形执行 Clamp。
9. 使用 SetWindowPos 应用，并保存实际物理像素矩形。

T3.3 的 `PlacementService` 将评分落实为稳定字典序：原始候选完全位于工作区优先，其次最终矩形不覆盖锚点、可见比例、Bottom/Top/Right/Left 方向顺序和中心距离。最终结果始终 Clamp 到 `rcWork`。键盘超出工作区时先按 95% 宽高上限等比缩放；工作区或期望尺寸非正时返回 `InvalidInput`，不生成屏外或零尺寸窗口。

用户手动移动后，将物理矩形与当前 SessionId 绑定。目标会话变化或 DPI 变化时重新进入自动定位。

T3.6 由 Core `ManualPositionTracker` 保存拖动起点、物理光标增量和绑定 SessionId；其他会话无法更新或结束该拖动。Overlay 使用 `GetCursorPos/GetWindowRect` 获取原生物理坐标并持续调用带 `SWP_NOACTIVATE` 的定位出口，WPF 拖动区仅捕获鼠标，不调用 `DragMove`。新会话、焦点评估失败、DPI 变化或退出会失效保存位置并恢复自动定位语义。

### 11.4 DPI 变化

- Manifest 声明 `PerMonitorV2`。
- 收到 `WM_DPICHANGED` 时接受系统建议矩形作为过渡，并用当前 TargetSession 重新计算。
- 不把上一显示器的物理宽高直接复用到新显示器。
- 单元测试覆盖 96、120、144、168、192 DPI 和负坐标。

T3.5 由 `OverlayWindowAdapter` 的 HWND hook 处理真实 `WM_DPICHANGED`：先带 `SWP_NOACTIVATE` 应用 lParam 建议矩形作为过渡，再发布 Core 强类型 DPI/矩形通知。App 只有在当前 TargetSession 有效时才以配置 DIP 尺寸和新 `DpiScale` 重算物理宽高；没有会话或重算失败时保留系统建议矩形。消息处理不调用激活 API，事件消费异常被隔离在原生边界。

## 12. 输入注入设计

### 12.1 输入流水线

```text
Keyboard PointerUp
      │
      ▼
KeyboardController creates InputAction
      │
      ├─ password policy check
      ├─ target session snapshot
      ▼
InputInjectionService serialized queue
      │
      ├─ validate foreground/focus/session
      ├─ build INPUT[]
      ├─ SendInput once per batch where possible
      ├─ verify return count
      └─ release only modifiers pressed by this batch
```

T4.1 的 Core `InputInjectionService` 在流水线入口提供固定容量、多生产者单消费者队列。每次提交原子分配 ActionId 并绑定 SessionId/`InputActionKind`；唯一消费者在动作开始前重新比较当前 SessionId，因此会话替换会使尚未开始的旧动作返回 `StaleSession`，而不会中断已经进入受控发送边界的批次。队列满和生命周期停止均失败关闭，不进行无界等待、自动重试或并行注入。

### 12.2 发送前目标校验

校验步骤：

1. SessionId 必须等于协调器当前 SessionId。
2. `GetForegroundWindow` 必须与会话顶层窗口一致；允许的 owner/modal 关系必须显式验证。
3. 重新读取当前焦点元素；RuntimeId 相同则通过。
4. 若 RuntimeId 因 DOM/控件重建变化，则要求顶层窗口和进程相同，并重新分类为 Editable 后建立新会话；本次动作默认取消，避免误输入。
5. 键盘 HWND 不得是前台窗口。

目标验证失败不调用 `SetForegroundWindow`，只返回 `TargetInvalid`。

T4.2 将完整 UIA 身份纳入 `TargetSession`：FocusVersion、深复制 RuntimeIdentity、密码标志和可选物理锚点随会话原子发布；最终用户流程只接受带 RuntimeId 的自动焦点会话。发送前先复核 SessionId、前台 HWND、进程和焦点 HWND，再从只接受更高版本的 `LatestFocusSnapshotStore` 比较 RuntimeId 与焦点/启用/离屏状态。DOM/控件重建或元数据不再可编辑时，本批次返回 TargetInvalid，并以封闭状态请求重新分类；不会尝试激活或修复窗口。

### 12.3 Text 路径

- 枚举 .NET 字符串中的 UTF-16 code unit。
- 每个 code unit 生成 `KEYEVENTF_UNICODE` KeyDown 和 KeyUp，`wVk=0`。
- 代理对按原顺序发送两个 code unit，不拆丢。
- 尽量在一个 `INPUT[]` 中提交，避免和真实键盘事件交错。
- 该路径表达直接文本提交，不保证触发目标 IME 的候选流程。

密码目标执行前，必须拒绝 `safeForPassword=false` 的布局动作。

T4.3 的 `UnicodeTextInputBuilder` 直接枚举 UTF-16 code unit，每个单元生成 `KEYEVENTF_UNICODE` Down 和 `KEYEVENTF_UNICODE|KEYEVENTF_KEYUP`，`wVk=0`；emoji 的高低代理项保持原顺序。`UnicodeTextInputSender` 对非空文本只调用一次 SendInput，短返回失败且不重试；输入长度上限为 4096 code unit，日志 API 从不接收文本参数。不调用 VkKeyScan、IME 模拟或 WM_CHAR 回退。

### 12.4 Key 路径

- 标准键使用 Virtual Key；需要区分左右/扩展键时同时提供 scan code 和 `KEYEVENTF_EXTENDEDKEY`。
- 目标键盘布局通过目标前台线程 `GetKeyboardLayout` 获得。
- `MapVirtualKeyEx` 用于需要的 VK/scan code 转换。
- 每个非状态键默认发送 KeyDown + KeyUp。
- 不用 `PostMessage(WM_CHAR)` 作为回退。

T4.4 的 `KeyInputSender` 接收封闭的 `WindowsKeyboardKey` 和目标焦点 HWND，通过 `GetWindowThreadProcessId` 获取目标 GUI 线程，再以该线程的 `GetKeyboardLayout` 调用 `MapVirtualKeyExW(MAPVK_VK_TO_VSC_EX)`。普通 Enter、Tab、Backspace、Escape、QWERTY 字母、数字和方向/导航键均映射为同一批次的 Down/Up；方向、Home/End、PageUp/PageDown、Insert/Delete 自动携带 scan code 和 `KEYEVENTF_EXTENDEDKEY`。底层 `KeyInputBuilder` 另支持纯 scan-code 编码以及单独 KeyDown/KeyUp，供 T4.5 的修饰键有序批次复用。零 HWND、未知键、无目标线程/HKL 或映射结果为零时在 `SendInput` 前返回 `InvalidInput`。主批次短返回不重复发送按键；若恰好只确认 Down 已送达，则用独立的一事件清理批次尽力补发对应 KeyUp。诊断只包含 PID、事件数量和错误码。

### 12.5 Hotkey 与修饰键

- 一个热键构建为单个有序 `INPUT[]`：修饰键按下、普通键按下/释放、修饰键逆序释放。
- 每个批次记录 `ModifiersPressedByUs`，只释放本批次合成按下的键，不释放用户实体键盘原本按住的键。
- 发送前通过 `GetAsyncKeyState` 读取物理状态，用于显示和冲突诊断。
- 异常、取消和进程退出路径都调用安全释放。
- CapsLock 默认发送 `VK_CAPITAL` 改变系统锁定状态，并在随后通过 `GetKeyState` 刷新 UI。

T4.5 的 `HotkeyInputSender` 在入口复制修饰键列表快照，只接受 1-4 个互不重复的 Ctrl、Shift、Alt、Win；通过 `GetAsyncKeyState` 高位读取提交前实体按下状态。实体已按住的修饰键参与系统热键语义，但不进入 `ModifiersPressedByUs`，程序既不重复按下也不释放。其余修饰键按声明顺序 Down，主键 Down/Up，最后逆序 Up，并作为一个 `SendInput` 批次提交。批次记录每个合成 Down/Up 的索引：短返回时根据已接受前缀只补发仍可能按下的主键和修饰键 KeyUp，不重试原热键；未知发送异常时逆序尽力释放本批次计划按下的修饰键，清理失败不覆盖原始结果。取消在原生提交前返回 `Cancelled` 且零输入调用；同步 `SendInput` 提交本身不可中断。调用方列表快照避免并发修改破坏按下/释放配对。Shift/Ctrl/Alt/Win 的点击切换状态和 CapsLock 状态刷新由 T5.4 `KeyboardController` 实现。

T5.4 的 Core `KeyboardController` 用单锁维护版本化不可变状态快照。Shift、Control、Alt、Win 均为独立点击开关：第一次点击由 `HotkeyInputSender.SendModifierTransition` 发送真实 KeyDown，成功后进入保持状态；再次点击发送 KeyUp，成功后才清除状态。保持期间普通按键只发送自身 Down/Up，发送器通过内部 held 集合避免重复合成修饰键，因此 Shift+D1、Shift+D2 等连续组合保持真实系统语义。目标 SessionId 改变、目标清空、暂停、设置或 Dispose 时按逆序发送有界 KeyUp 清理；清理未确认时进入 `SyntheticKeySafetyLatch` 并停止后续输入。活动键使用蓝底、白字、加粗边框，原生转换失败时不产生虚假高亮。Fn 仍是纯应用功能层，不发送不存在的通用硬件 Fn 信号。

CapsLock 不保存在独立虚拟锁中。Windows `CapsLockStateService` 使用 `GetKeyState(VK_CAPITAL)` 低位读取系统 toggle bit；切换时先由通用 `ValidatedKeyInputSender` 复核 SessionId、前台和焦点，再发送成对 CapsLock KeyDown/KeyUp，随后重读系统状态。读取或发送失败时控制器将 CapsLock 标为未知且不猜测新值；实体键盘改变 CapsLock 后，下一次 `RefreshCapsLock` 更新版本和标签数据。

### 12.6 失败和 UIPI

`SendInput` 返回数量少于请求数量时：

1. 将该批次标记为失败，不自动重试，避免重复字符。
2. 记录请求事件数、返回事件数、Win32 错误码、目标进程 ID 和完整性判断；不记录动作文本。
3. 在键盘状态区域显示简短失败提示。
4. 如果目标可能处于更高完整性级别，提示“目标权限高于本程序，无法输入”，不得请求临时提权。

T4.6 的 `ProcessIntegrityInspector` 以 `PROCESS_QUERY_LIMITED_INFORMATION` 和 `TOKEN_QUERY` 读取当前/目标进程 `TokenIntegrityLevel` 的 SID 末级 RID，所有缓冲区与句柄均有确定上限和 `finally` 清理。比较结果为同级/更低、目标更高、目标 Token 访问被拒或未知；只有已证明目标 RID 更高时才给出确定权限提示，只有目标 Token 访问被拒时才提示“可能存在权限边界”，本程序自身 Token 读取失败不得归因于目标。`InputFailureFeedbackFactory` 只在 SendInput 零/短返回时探测完整性，把结果转换为封闭 `InputFailureKind` 与固定提示文本；成功、目标变化、取消、无效请求和原生不可用不打开目标进程。分类通过 `InputFailureClassified` 诊断事件记录 PID、数量、错误码和封闭 ReasonCode，不包含进程名、窗口标题或输入内容。M1 的状态 TextBlock 已接入该反馈，保持 NoActivate，且没有提权或 uiAccess 路径。

T4.7 将无法确认送达的 KeyUp 事件交给 `SyntheticKeySafetyLatch`。热键原批次短返回后，清理批次按“主键（若其 Down 已送达而 Up 未送达）→修饰键逆序”的确定顺序发送；清理短返回或异常时，只登记清理批次未确认送达的后缀。未知主批次异常则对本批次计划按下的修饰键执行逆序 KeyUp，并登记未确认部分。一旦登记任何键，发送器立即 fail-closed，后续请求在目标映射、实体状态读取和 SendInput 之前返回 `SafetyFaulted`；UI 使用固定提示要求退出重启。`HotkeyInputSender` 以同一发送锁串行化完整 Send 与 Dispose，退出必定等待在途同步批次及其清理完成；Dispose 随后幂等地对登记项再发送一次有界 KeyUp 批次，正常平衡批次退出不产生多余释放。串行队列的 Stop/Dispose 继续负责取消尚未开始的动作；同步 SendInput 不做不可控中断。安全闩锁通过 `InputSafetyFaulted` 事件记录数字计数和错误码，不记录按键语义。

## 13. 键盘布局设计

### 13.1 内置与用户布局

```text
Application Install Directory
└─ layouts\builtin\qwerty.en-US.json   (只读)

%LocalAppData%\VirtualKeyboard\
└─ layouts\*.json                      (用户布局)
```

用户布局 ID 与内置布局冲突时不覆盖内置布局；MVP 的自定义布局应使用独立 ID 命名空间，显式 override 仅作为未来扩展讨论，不属于当前 schema。

T5.2 的 `LayoutRepositoryPaths.CreateDefault` 将内置目录固定解析为应用基目录下的 `layouts\builtin`，用户目录固定解析为当前用户 LocalAppData 下的 `VirtualKeyboard\layouts`。Repository 只枚举目录第一层按文件名不区分大小写排序的 `*.json`，始终先处理内置、再处理用户，因此内置 ID 和排序靠前的布局确定性优先；MVP 不开放 override 字段。读取操作不会写入安装目录或用户文件。

每个布局文件限制为 1 MiB，JSON 最大深度为 16，禁止注释、尾随逗号、大小写不匹配字段和未知字段，并兼容 UTF-8 BOM。Repository 以规范化文件路径缓存最后一次有效的不可变快照；显式重新加载时，新文件只有通过 JSON/schema 校验且 ID 不冲突才替换缓存。相同文件损坏、暂时不可读或改成冲突 ID 时继续发布旧快照并标记 `RetainedPrevious`；文件被删除则从快照移除。目录枚举暂时失败时保留该来源现有缓存。Reload 通过单锁串行化，读者获得一次性只读字典快照，不会观察半更新状态。

T5.3 的 `builtin.qwerty.en-US` 是应用项目的 Content，构建与发布均以 `PreserveNewest` 复制到上述只读目录。布局共五行，主键区遵循标准美式 QWERTY 顺序：Escape/重音符与完整数字标点行、Tab/QWERTY/方括号/反斜杠行、CapsLock/ASDF/分号/引号/Enter 行、左右 Shift/ZXCV/逗号/句点/斜杠行，以及左右 Ctrl/Alt、Win、Space、Fn 底行。右 Shift 权重为 1.8，Up 后为 Delete，使第四、第五行总权重同为 16，Up 与 Down 的归一化和实际渲染中心完全一致。数字行的 1-0、减号、等号分别声明 F1-F12 `fnVirtualKey`。字母、数字、标点、空格及编辑/导航键均使用 `key`，使它们可直接参与 Shift/Ctrl/Alt/Win 键盘语义；状态键使用 `modifier`。Windows 封闭键枚举同步覆盖 A-Z、D0-D9、F1-F12、标准 OEM 标点、方向/导航键和所需状态键。Win/Fn 标记为密码目标不安全；其余标准输入键按密码策略处理。关闭、设置和拖动不在 JSON 中，不可能被布局解析成输入 action。

### 13.2 布局 JSON 示例

```json
{
  "schemaVersion": 1,
  "id": "builtin.qwerty.en-US",
  "name": "English QWERTY",
  "culture": "en-US",
  "rows": [
    [
      {
        "id": "key.q",
        "label": "Q",
        "width": 1.0,
        "safeForPassword": true,
        "action": {
          "type": "key",
          "virtualKey": "Q"
        }
      }
    ],
    [
      {
        "id": "key.hello",
        "label": "Hello",
        "width": 2.0,
        "safeForPassword": false,
        "action": {
          "type": "text",
          "value": "Hello"
        }
      }
    ]
  ]
}
```

### 13.3 校验规则

- `schemaVersion` 当前只接受整数 `1`；布局必须有 1-16 行，每行 1-64 键，总计不得超过 256 键。
- layout `id` 长度为 1-128，key `id` 长度为 1-64，key `id` 在整个布局内区分大小写且唯一；`name`、`culture`、`label` 上限分别为 64、32、32 个 UTF-16 code unit，且均不得为空白。
- `width` 必须是有限正数且不大于 `16`。`safeForPassword` 为每个 key 必填布尔语义，密码目标执行动作前仍由控制器执行该标志门禁。
- action 是严格字段联合，只接受小写 `text`、`key`、`hotkey`、`chord`、`modifier`；混入其他 action 类型的字段也视为无效。
- `text` 只使用 `value`，长度为 1-4096 UTF-16 code unit；日志和校验错误仅给出 `$.rows[n][n].action.value` 路径，不得回显内容。
- `key` 必须且只能声明 `virtualKey` 或 `scanCode` 之一，并可选声明一个 `fnVirtualKey`。`scanCode` 范围为 1-65535；两个 virtual key 字段都使用封闭集合：A-Z、0-9、F1-F12、标准美式主键区 OEM 标点、Space、Backspace、Enter、Tab、Escape 和方向/导航键。
- `hotkey` 使用与 `key` 相同的一个主键，并声明 1-3 个按顺序排列、互不重复的 `Shift`、`Control`、`Alt`、`Windows` 修饰键；与界面保持状态合并后的发送批次最多包含四个修饰键。
- `chord` 只使用 `keys`，包含 1–8 个按 KeyDown 先后排列且大小写不敏感去重的封闭键；允许普通键、Shift/Control/Alt 和左右 Windows 键。发送器先按序构造全部 KeyDown，再逆序构造全部 KeyUp，短发送只释放已成功按下且尚未释放的键。
- `modifier` 只接受 `Shift`、`Control`、`Alt`、`Windows`、`Fn`、`CapsLock` 状态名。Fn 不进入 SendInput 修饰键数组，只选择 `fnVirtualKey`；动作名和键名按 schema 规定的大小写解析，修饰键名和 virtual key 名由校验器按 ASCII 大小写不敏感匹配。
- 出现 `command`、脚本或未知可执行动作时拒绝整个布局。
- T5.1 的 `KeyboardLayoutDefinition`、`KeyboardLayoutRow`、`KeyboardKeyDefinition` 和 `LayoutActionDefinition` 在构造时复制集合，调用方后续修改源集合不会改变已验证模型。`LayoutValidator` 返回稳定错误 code、具体 JSON 字段路径及固定非敏感消息。
- `LayoutLoadIssue` 仅携带来源枚举、文件名、JSON 字段路径、稳定错误 code、固定消息和是否保留旧快照；不携带布局文本值或完整本机路径。校验失败继续使用最后一个有效布局，后续 UI 可直接使用这些非敏感字段显示错误。

### 13.4 视图生成

`KeyboardController` 将布局模型映射为不可变 `KeyViewModel` 集合。宽度采用 Grid 星号或等价权重算法；最小点击尺寸、间距和字体由主题资源控制。视图只绑定动作 ID，不直接持有原生 VK 常量处理逻辑。

T5.5 的 `KeyboardLayoutViewModel.Create` 只接受再次通过 schema 校验的布局，并生成只读 row/key 集合；action 对象保持语义身份，不在视图层解释为原生常量。WPF `KeyboardLayoutView` 为每行分配等权 Star 高度、为每键按 `width` 分配 Star 列宽；按键保持 36 DIP 最小高度，但列不设置会导致横向溢出的固定最小宽度。完整窗口下限为 620×280 DIP，默认配置为 800×300 DIP。

`NonFocusableKeyButton` 固定 `Focusable=false`、`IsTabStop=false`，并完全接管鼠标按下/释放和捕获生命周期，不让 WPF `Button` 默认 Click 生命周期与自定义手势并行。其 `KeyGestureController` 只接受 Idle→Pressed→Release/Cancel：重复 Down 被忽略，只有曾成功 Begin 且在键内 Release 才发出一次 `KeyInvoked`；键外释放、鼠标捕获丢失和 Cancel 都恢复视觉状态且不触发。按下时通过不透明度提供明确视觉反馈，动作事件只携带经过验证的 `KeyViewModel`。

M5 review 修正增加 Windows `LayoutActionDispatcher`。动态 `KeyInvoked` 先进入 Core `InputInjectionService` 有界串行队列；消费者同步复核 SessionId、前台、焦点和密码策略，再把标准 key 送入 `KeyInputSender`，把显式或保持的 Shift/Ctrl/Alt/Win 组合送入 `HotkeyInputSender`，把 Unicode text 保持在 `UnicodeTextInputSender`；Fn 选择 key 的 `fnVirtualKey`，其他 modifier 更新控制器或经验证切换系统 CapsLock。未知键/修饰键在发送前拒绝。UI 不再硬编码仅发送 A；状态文本也不回显 label 或 text。退出顺序为停止队列、清理控制器、Dispose 热键安全闩锁、最后关闭诊断。
动作完成或失败后，`KeyboardLayoutView.UpdateState` 使用同一 `KeyboardControllerState` 更新状态键视觉；因此再次点击释放、目标切换或 CapsLock 刷新不会留下过时高亮。

T5.6 的 Core `PasswordActionPolicy` 不信任布局作者单独声明的 `safeForPassword`：两者必须同时通过。密码模式仅允许一个 Unicode 标准字符、封闭的 A-Z/D0-D9/OEM 标点/Space 与编辑导航 key，以及 Shift/CapsLock；所有 hotkey、多字符 text、scanCode、Control/Alt/Win/Fn 和未知动作默认拒绝。WPF 生成密码布局时直接排除不通过的键，事件分发边界在发送前再次执行同一策略，避免仅靠可见性形成安全边界。`PasswordActionCheck` 只返回枚举与固定 reason code，不返回文本；状态提示也不拼接 key label、目标 Name 或 Value。

T5.7 在 `NonFocusableKeyButton` 显式覆盖 TouchDown/Move/Up/LostTouchCapture。TouchDown 捕获单一触点并进入与鼠标相同的 `KeyGestureController`；TouchUp 使用相对触点坐标判定是否仍在键内，先结束手势再释放捕获；丢失捕获统一取消。各触摸事件标记 handled，防止 WPF 将同一触摸继续提升为鼠标点击而重复执行。拖动区域只处理自身鼠标手势，不共享按键的触摸捕获。自动测试环境无实体触摸设备，真实单指点击和拖动手柄邻键冲突保留为 P1 实机验收项。

## 14. 配置设计

### 14.1 路径

```text
%LocalAppData%\VirtualKeyboard\
├─ config.json
├─ layouts\
├─ logs\
└─ recovery\
```

### 14.2 配置示例

```json
{
  "schemaVersion": 1,
  "enabled": true,
  "autoShow": true,
  "autoHide": true,
  "opacity": 0.9,
  "keyboardWidthDip": 800,
  "keyboardHeightDip": 300,
  "marginDip": 8,
  "layoutId": "builtin.qwerty.en-US",
  "manualPositionMode": "UntilTargetChanges",
  "detailedDiagnostics": false
}
```

### 14.3 读写策略

1. 启动时读取并按 schemaVersion 迁移。
2. 对字段执行范围校验，未知字段按前向兼容策略保留或忽略。
3. 修改设置先写同目录临时文件并 Flush。
4. 使用原子替换更新 config.json。
5. 读取失败时把原文件复制到 recovery，并加载默认值。
6. 保存失败时保留内存设置，通知用户，程序继续运行。

配置模型与运行时模型分离；运行时始终获得经过验证的不可变配置快照。

T6.1 的 Core `KeyboardConfiguration` 为不可变运行时快照，除基础字段外包含 `customKeys`。`customKeys` 最多 12 项，每项标签不超过 32、输入不超过 256 UTF-16 code unit，动作仅允许 `text`、`key`、`hotkey`、`chord` 并复用布局封闭键名/修饰键校验。为兼容 schema v1，`chord` 的有序完整键列表存放在现有 `modifiers` 数组且 `input` 为空，转换为布局动作时映射到 `action.keys`。验证错误不回显布局 ID 或自定义内容。旧 schema v1 的单个 customKeyLabel/customKeyText 会在内存中迁移为一个 text 项，完全缺失时按空列表加载。

T6.2 的 `ConfigurationRepository` 使用 `%LocalAppData%\\VirtualKeyboard\\config.json` 和同目录 `recovery` 子目录。读取限制为 64 KiB、JSON 深度 8，兼容 UTF-8 BOM，拒绝注释/尾逗号并忽略未知字段以保持前向兼容；反序列化后再次执行 schema 验证。损坏或无效文件先复制为带 UTC 时间和随机后缀的恢复文件，再返回安全默认配置；恢复失败也不会阻止启动。保存先验证，在目标目录创建随机临时文件并 `Flush(true)`，随后使用 `File.Replace`（首次保存使用 `File.Move`）完成原子更新；任意 IO/权限失败删除临时文件、保留已验证的内存快照并返回脱敏固定错误。仓库通过锁串行化 `Current`、`Load` 与 `Save`。

T6.3 的 WPF `SettingsWindow` 是独立、可激活的模态窗口，编辑 schema v1 的全部用户字段。界面 Slider 表示 0.00–0.70 的“透明程度”，保存时用 `opacity = 1 - transparency` 转换为 WPF 整窗 `Opacity` 0.30–1.00；MainWindow 启用 `AllowsTransparency=True`，不使用背景色或亮度模拟透明，设置关闭后立即重载当前 Opacity。自定义键采用左侧列表加右侧详情编辑器，界面只暴露“输入文字”和“录制按键或组合键”。`KeyboardChordRecorder` 使用 `WH_KEYBOARD_LL` 捕获并抑制录制期间的 KeyDown/KeyUp：记录最多 8 个不同封闭键的首次 KeyDown 顺序，全部释放后生成 `chord`，因此 `Win+Tab` 不会先触发系统任务视图；失败、取消、切换项目/模式或关闭设置都会卸载 hook。保存仍经过统一 schema 验证。主窗口以现有 `TargetStateCoordinator.OpenSettings/CloseSettings` 包围整个模态生命周期；进入时使输入队列会话失效、释放真实保持修饰键、清除目标并隐藏 Overlay。`CustomKeyColumnView` 使用五行 Grid，每列最多 5 键，第 6/11 项自动创建第二/第三列，不使用滚动容器；主键区使用 16 份 Star、自定义区每列使用 2.5 份 Star，内部各列等分，使两区随窗口宽度同步缩放且不重叠。密码目标时整体折叠，每项继续复用目标复核、串行队列和 text/key/hotkey/chord 发送路径。

设置中的 `ManualPositionMode` 使用面向用户的中文选项与随选说明，不直接显示枚举名。“当前输入框”把拖动结果绑定当前 TargetSession，目标替换后回到锚点自动布局；“持续保留”在进程内保存最后一次手动物理像素位置，目标替换时沿用坐标，并按新目标所在显示器工作区及当前 DPI 尺寸进行边界约束。DPI 改变时清除旧物理位置，避免跨缩放复用错误坐标。

T6.4 使用 Windows Desktop 框架自带 `NotifyIcon` 实现系统托盘，不增加第三方依赖。`TrayIconController` 只通过 `ITrayCommands` 调用宿主，菜单固定为启用/暂停、显示当前键盘、设置、重新加载布局和退出；启用项每次操作后从 ConfigurationRepository 的当前快照刷新。启用切换同步持久化配置和 `TargetStateCoordinator`，暂停时使输入队列会话失效、清除目标/瞬时状态并隐藏窗口；布局重载复用单一 `LayoutRepository`，首选配置 layoutId，缺失时回退内置 QWERTY。应用采用显式退出生命周期，退出前隐藏并释放 NotifyIcon；标题栏关闭仅隐藏 Overlay，使托盘可再次显示同一窗口。

T6.5 的 `SingleInstanceCoordinator` 以当前域/用户名和 Windows SessionId 的 SHA-256 截断摘要构造 `Local\\` 命名对象，避免在对象名中暴露原始账户信息。命名 Mutex 的首个持有者是主实例；同名 AutoResetEvent 是有界激活通道。第二实例不创建主窗口、托盘或监听器，只设置事件后退出；主实例通过已注册等待接收事件，再切换到 WPF Dispatcher 打开设置窗口。注册、事件句柄和 Mutex 均在应用退出时释放，Dispose 幂等。

T6.6 将退出固化为单向、幂等生命周期。`MainWindow.Dispose` 先把协调器置为 `ShuttingDown`，注销并释放 `FocusObservationService`，随后停止 `InputInjectionService`（拒绝新动作并终结队列）、清除 TargetSession、清理 `KeyboardController` 瞬时状态、释放 `HotkeyInputSender` 安全闩锁，再解除 DPI 事件并释放 Overlay 和诊断资源。应用退出阶段保存 Repository 当前快照，然后隐藏/释放 NotifyIcon，最后释放单实例等待和命名句柄。

项目执行决策（2026-09-06）：M6 完成后跳过 M7，直接进入 M8。该决策只改变执行顺序，不改变发布质量事实；T7.1–T7.6 及 M7 退出检查保持未完成，M8 发布评审必须把缺失的隐私审计、压力、8 小时稳定性、权限负向和性能数据列为未证明项，不能用既有单元/集成测试替代。

T8.4 采用 ADR-007 的 `win-x64` 框架依赖便携 ZIP，应用版本固定为 1.0.0。统一构建先发布到 `artifacts/package/win-x64`，再生成 `artifacts/release/VirtualKeyboard-1.0.0-win-x64-framework-dependent.zip` 及 UTF-8 no-BOM `.sha256` 文件。运行时写入只允许 `%LocalAppData%\\VirtualKeyboard`；升级和回滚只替换程序目录，卸载默认保留用户配置、布局、恢复文件和诊断数据。

T8.5 增加显式应用清单与 `scripts/verify-release.ps1`。脚本校验 ZIP 哈希/安全路径/敏感文件、必需运行文件、EXE 嵌入的普通权限声明和 Authenticode 状态，并输出 `release-security.json`。当前 EXE 为 `asInvoker`、`uiAccess=false`、PerMonitorV2，受控启动前后发布目录哈希无变化；但本机 Defender 被禁用且 EXE 未签名，因此安全检查报告结论为“仅限未签名内测”，T8.5 保持未完成。

T8.1/T8.2/T8.3 的验收制品位于 `docs/release`。当前环境只确认 Windows 11 Pro build 26100、单逻辑屏 3840×2160、96 DPI，以及 Chrome/Edge/VS Code 已安装；未执行真人应用交互或跨系统/多屏矩阵。验收表逐项区分“部分自动证据”和“完整通过”，并把 M7 缺失、跨环境缺失、诊断未接线及未签名/未扫描登记为 P0 Open；0 个 AC 完整通过。

发布评审后的 `REL-001` 修正把现有能力接入 App：`FocusObservationService` 在专用 MTA 线程合并事件并支持启动刷新；`FocusTargetEvaluator` 只读取进程、RuntimeId、ControlType、焦点/启用/离屏/密码标志、模式可用性、只读标志、边界矩形和 Win32 caret，不读取 Name/Value/Text。分类结果经单调版本协调器进入 WPF Dispatcher，建立带 RuntimeId 的 TargetSession，更新发送前身份快照，并使用 MonitorDpiAdapter + PlacementService 应用配置尺寸、透明度和边距。NotEditable/Unknown 使会话失效并按 autoHide 隐藏；手动关闭/托盘显示复用 ManuallySuppressed 状态。退出在停止输入前注销 UIA。

发布评审后的 `REL-006` 修正把 `RollingFileDiagnosticSink` 接入生产 App 的 `%LocalAppData%\\VirtualKeyboard\\logs`。宿主使用 4 MiB×5 文件的严格 20 MiB 上界，目录/IO 故障自动降级；配置加载、焦点、分类、会话和 Overlay 事件经 `DiagnosticLogger` 写入，FocusObserved 作为 Detailed 事件默认过滤。设置变更通过线程安全 `SetDetailedEnabled` 动态切换详细事件，不重建被输入发送器持有的 Logger。所有事件继续受 `DiagnosticEvent` 值类型字段与序列化白名单约束，日志导出只包含 `*.jsonl`。

## 15. 诊断、隐私与安全设计

### 15.1 事件模型

建议事件：

- `FocusObserved`
- `ClassificationCompleted`
- `TargetSessionCreated/Invalidated`
- `OverlayShown/Hidden/Moved`
- `InputBatchStarted/Succeeded/Failed`
- `ConfigLoaded/Recovered/SaveFailed`
- `LayoutLoaded/Rejected`
- `UnhandledBoundaryException`

每条事件包含时间、应用版本、事件 ID、模块、耗时、ReasonCode、错误码和必要的数字身份信息。

### 15.2 敏感数据规则

以下数据不得传入日志 API：

- InputAction 的文本内容。
- AutomationElement 的 Value、Name（密码目标一律禁止；普通目标默认也不记录 Name）。
- 剪贴板、按键序列的字符化结果、自定义短语。

日志 API 使用专门 DTO，从类型设计上不接受 `InputAction.TextValue`。

### 15.3 日志保留

- 默认 Info 日志保留 7 天或总计 20 MB，以先到者为准。
- 详细 UIA 元数据诊断默认关闭。
- 导出前再次执行敏感字段扫描。
- MVP 不上传网络。

T2.7 增加独立于通用事件日志的 `FocusDiagnosticReport`：报告字段固定为采集时间、FocusVersion、PID、数字 HWND、ControlType、焦点/启用/离屏/密码状态、Editability、ClassificationReasonCode 和 UsedFallback。`FocusDiagnosticExporter` 只序列化该白名单 DTO；`FocusDiagnosticsView` 是供设置窗口承载的只读视图，显示同一组字段并导出当前报告。报告类型没有 string/object 扩展字段，因此不能承载 AutomationElement Name/Value 或用户输入内容。

### 15.4 威胁与缓解

| 风险 | 缓解 |
|---|---|
| 输入发送到错误窗口 | TargetSession、前台/焦点双重校验、失败时取消 |
| 高权限窗口注入失败 | 普通权限边界、明确提示、不自动提权 |
| 恶意布局执行代码 | action 白名单、拒绝 command/script、长度和数量限制 |
| 记录密码或输入内容 | 类型级日志约束、密码模式、无网络上传 |
| 配置覆盖安装文件 | 用户数据写 LocalAppData，安装目录只读 |
| 修饰键卡住 | 批次跟踪、finally 释放、退出释放 |
| UIA provider 阻塞 UI | 专用 MTA 线程、结果版本化、UI 无同步等待 |

## 16. 错误处理与恢复

| 故障 | 行为 | 用户可见性 |
|---|---|---|
| AutomationElement 已销毁 | 返回 Unknown，失效当前会话 | 通常静默，诊断可见 |
| UIA 属性读取异常 | 捕获并记录原因码，不崩溃 | 重复失败时状态提示 |
| 找不到有效锚点 | 使用显示器安全默认位置 | 无需中断输入 |
| SendInput 部分/全部失败 | 不重试该批次，释放修饰键 | 键盘状态提示 |
| 布局无效 | 保留最后有效布局 | 显示字段错误 |
| 配置损坏 | 备份并加载默认值 | 启动后提示 |
| 设置保存失败 | 保留内存值，允许重试 | 设置窗口提示 |
| 日志目录不可写 | 降级为内存限长缓冲 | 诊断页提示 |

禁止通过全局异常捕获后继续执行未知状态的输入批次。输入边界异常必须失败关闭该批次。

## 17. 项目结构

```text
VirtualKeyboard.sln
│
├─ src\
│  ├─ VirtualKeyboard.App\
│  │  ├─ App.xaml
│  │  ├─ AppHost.cs
│  │  ├─ Tray\
│  │  ├─ Views\
│  │  │  ├─ KeyboardOverlayWindow.xaml
│  │  │  └─ SettingsWindow.xaml
│  │  └─ ViewModels\
│  │
│  ├─ VirtualKeyboard.Core\
│  │  ├─ Focus\
│  │  ├─ State\
│  │  ├─ Positioning\
│  │  ├─ Keyboard\
│  │  ├─ Input\
│  │  ├─ Layouts\
│  │  ├─ Configuration\
│  │  └─ Diagnostics\
│  │
│  └─ VirtualKeyboard.Windows\
│     ├─ Automation\
│     ├─ Native\
│     ├─ WindowsInputInjector.cs
│     └─ OverlayWindowAdapter.cs
│
├─ assets\layouts\builtin\
│  └─ qwerty.en-US.json
│
├─ tests\
│  ├─ VirtualKeyboard.Core.Tests\
│  ├─ VirtualKeyboard.Windows.Tests\
│  ├─ VirtualKeyboard.IntegrationTests\
│  └─ VirtualKeyboard.TestHost\
│
├─ docs\
└─ Directory.Build.props
```

### 17.1 依赖方向

```text
VirtualKeyboard.App ───────► VirtualKeyboard.Core
        │                            ▲
        └────► VirtualKeyboard.Windows
                                      │
                                      └──── implements Core interfaces
```

`Core` 不引用 WPF、UIAutomationClient 或具体 P/Invoke 类型。原生结构在 Windows 项目内部转换为 Core DTO。

## 18. 测试设计

### 18.1 单元测试

- EditabilityClassifier：ValuePattern、TextEditPattern、TextPattern-only、密码、只读、失效元素。
- TargetStateCoordinator：所有状态转换、乱序版本、重复事件和手动抑制。
- PlacementService：四向候选、Clamp、负坐标、过大键盘、混合 DPI。
- KeyboardController：Shift/Caps/修饰键状态和密码动作过滤。
- LayoutRepository：schema、边界、未知动作、最后有效布局。
- ConfigRepository：迁移、损坏恢复、原子保存失败。
- Input batch builder：Unicode 代理对、扩展键、热键按下/释放顺序。

### 18.2 Windows 集成测试

- TestHost 提供普通、只读、密码、多行、WPF、WinForms 和自定义 UIA provider 控件。
- 验证 Overlay HWND 永不成为前台窗口。
- 验证 SetWindowPos 使用的物理矩形和工作区约束。
- 验证 SendInput 数量、顺序和修饰键清理。
- 使用独立测试进程验证目标切换和进程退出。

### 18.3 手工兼容矩阵

| 场景 | Win10 22H2 | Win11 | DPI/多屏 | 结果记录 |
|---|---:|---:|---:|---|
| Notepad | 必测 | 必测 | 100%/150% | 截图、日志、检查表 |
| Edge/Chrome input、textarea | 选一必测 | 两者抽测 | 125%/200% | 检查表 |
| VS Code editor | 必测 | 必测 | 跨屏 | caret/降级原因 |
| WPF/WinForms TestHost | 必测 | 必测 | 全 DPI | 自动结果 |
| 密码框 | 必测 | 必测 | 单屏 | 日志隐私检查 |
| 管理员目标 | 必测失败路径 | 必测失败路径 | 任意 | 不提权、可诊断 |
| Office | 环境具备时 | 环境具备时 | 抽测 | 兼容性报告 |

### 18.4 性能和稳定性

- 10,000 次模拟焦点事件不出现无界队列、崩溃或旧结果覆盖。
- 连续运行 8 小时，切换目标和显示/隐藏，不出现明显内存持续增长。
- 收集焦点到显示延迟，计算 P50/P95/P99。
- 验证空闲 CPU 和稳态工作集满足 NFR-PERF-001。

## 19. 构建、打包与发布

### 19.1 构建

- SDK：固定 `.NET 10` SDK feature band，通过 `global.json` 管理。
- 平台：首发 `win-x64`。
- 配置：Debug、Release。
- 启用 nullable、warnings as errors（项目代码）、确定性构建和 SourceLink（如有源代码托管）。

### 19.2 打包

MVP 可先发布签名的自包含或依赖框架 EXE/MSI。MSIX 是否采用由部署需求决定，但必须验证：

- LocalAppData 配置路径可写。
- 托盘、开机启动和更新策略。
- UI Automation 与 SendInput 的全信任桌面能力。
- 卸载是否保留用户布局由安装器策略明确。

### 19.3 发布门禁

- 所有 P0 单元和集成测试通过。
- AC-001 至 AC-015 有验收记录。
- 无已知会导致错误目标输入、焦点丢失或修饰键卡住的缺陷。
- 发布二进制完成恶意软件扫描和代码签名验证。
- 隐私日志抽查不包含实际输入内容。

## 20. 需求到设计追踪

| 需求组 | 主要设计章节/组件 |
|---|---|
| FR-APP-* | AppHost、TrayShell；第 17、19 章 |
| FR-FOC-* | FocusObservationService、EditabilityClassifier；第 6、8 章 |
| FR-VIS-* | TargetStateCoordinator、OverlayWindowAdapter；第 9、10 章 |
| FR-POS-* | PlacementService、NativeFocusAdapter；第 11 章 |
| FR-KEY-* | KeyboardController、LayoutRepository；第 10、13 章 |
| FR-INP-* | TargetSessionStore、InputInjectionService；第 12、15 章 |
| FR-CFG-* | ConfigRepository；第 14、16 章 |
| FR-DIA-* | DiagnosticEventSink；第 15 章 |
| NFR-* | 第 6、15、18、19 章 |

## 21. 主要风险与验证顺序

| 风险 | 影响 | 首次验证点 | 失败后的策略 |
|---|---|---|---|
| WPF NoActivate 点击仍影响焦点 | 核心体验失败 | 第一个 Notepad 单键垂直切片 | 改为更底层 HWND 自绘/托管宿主 |
| Chromium/VS Code UIA 信息不足 | 兼容率不足 | 焦点分类原型 | caret 证据、provider 诊断、受控兼容规则 |
| SendInput 被 UIPI 拦截 | 某些窗口不能输入 | 权限负向测试 | 明确边界；未来评估 uiAccess |
| 混合 DPI 定位偏移 | 键盘屏外或遮挡 | Placement 原型 | 全程物理像素、SetWindowPos、补测试 |
| UIA provider 卡住 | 响应变慢 | 稳定性测试 | 专用线程；未来拆工作进程 |
| Unicode/IME 行为不一致 | 非英文体验不一致 | Text/Key 分路测试 | 标注直接文本与物理按键语义，按应用记录限制 |

验证顺序必须先覆盖“单键输入但不失焦”的垂直切片，再扩展自动检测、定位和完整布局。不得在核心焦点与目标校验尚未通过时优先投入动画或主题。
