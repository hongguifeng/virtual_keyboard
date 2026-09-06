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
| `FocusObservationService` | 监听 UIA 焦点事件、生成版本化快照 | FR-FOC-001、006 |
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
    int[]? RuntimeId,
    string ControlType,
    bool HasKeyboardFocus,
    bool IsEnabled,
    bool IsOffscreen,
    bool IsPassword,
    NativeRect? BoundingRect);

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

DTO 中不得包含 AutomationElement 的 Value 或用户输入内容。AutomationElement/COM 引用不跨线程长期保存；需要时基于最新焦点重新获取和验证。

## 8. 焦点检测与可编辑性分类

### 8.1 事件来源

主来源：`Automation.AddAutomationFocusChangedEventHandler`。

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
ValuePattern and !IsReadOnly?
        │ yes
        ├──────────────────► Editable(ValuePattern)
        ▼
TextEditPattern available?
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

- `ControlType.Edit` 是强提示但不是无条件结论；显式只读优先。
- 密码字段常因安全原因不暴露 ValuePattern，应使用 `IsPassword + Edit + HasKeyboardFocus` 判定。
- TextEditPattern 比 TextPattern 更接近编辑语义，但仍要求元素启用并持有焦点。
- `ControlType.Document + TextPattern` 可能只是网页正文或阅读器，默认返回 Unknown。
- caret 必须是有限数值、位于合理屏幕范围且与目标顶层窗口关联；零矩形不构成证据。
- `Unknown` 默认不显示，但写入原因码和 provider 元数据以便兼容性改进。
- 不维护应用名称白名单。确需 provider 特例时，必须通过版本化兼容规则、测试和独立配置引入。

### 8.4 目标会话建立

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

## 10. NoActivate 悬浮窗口设计

### 10.1 WPF 配置

- `WindowStyle=None`
- `ShowInTaskbar=False`
- `ShowActivated=False`
- `Topmost=True` 仅用于 WPF 语义；实际置顶通过原生调用并带 `SWP_NOACTIVATE`
- 普通按键 `Focusable=False`、`IsTabStop=False`
- 设置窗口与键盘窗口分离；设置窗口允许正常激活

不为了整体透明度强制设置 `AllowsTransparency=True`。MVP 优先使用 WPF `Opacity` 和普通不透明背景；如必须实现非矩形透明边缘，再单独测试 layered window 的渲染和命中行为。

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

T1.1 的最小实现位于 `VirtualKeyboard.Windows.OverlayWindowAdapter`：窗口在 `SourceInitialized` 后集中设置 `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`，通过 `HwndSource.AddHook` 将 `WM_MOUSEACTIVATE` 返回为 `MA_NOACTIVATE`，并以 `SetWindowPos(HWND_TOPMOST, ..., SWP_NOACTIVATE)` 完成显示、移动和尺寸更新。适配器只接受 UI 线程调用并拒绝非正尺寸。

T1.2 的最小目标捕获实现位于 `VirtualKeyboard.Windows.NativeForegroundTargetCapture`。适配器通过 `GetForegroundWindow` 获取前台顶层 HWND，再用 `GetWindowThreadProcessId` 获取进程 ID 和 GUI 线程，最后通过 `GetGUIThreadInfo` 获取焦点 HWND；任何句柄、线程或进程查询失败都返回可判定的 `TargetCaptureStatus`，不会读取标题、进程名或输入内容，也不会改变前台窗口。捕获本程序自身窗口时返回 `OwnProcess`。`VirtualKeyboard.Core.TargetSessionStore` 使用不可变 DTO 和锁保护的原子替换生成单调 `SessionId`；失败捕获会清空当前会话。`MainWindow` 的“捕获当前目标”按钮只展示 PID/HWND/会话号，保持 NoActivate 约束。`SendInput` 留在 T1.3。

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

```text
physicalPx = round(dip * monitorDpi / 96)
dip        = physicalPx * 96 / monitorDpi
```

目标 DPI 优先通过目标窗口/键盘窗口的 `GetDpiForWindow` 获得，必要时使用显示器 DPI 回退。无有效信息时使用 96 DPI，并记录降级事件。

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

用户手动移动后，将物理矩形与当前 SessionId 绑定。目标会话变化或 DPI 变化时重新进入自动定位。

### 11.4 DPI 变化

- Manifest 声明 `PerMonitorV2`。
- 收到 `WM_DPICHANGED` 时接受系统建议矩形作为过渡，并用当前 TargetSession 重新计算。
- 不把上一显示器的物理宽高直接复用到新显示器。
- 单元测试覆盖 96、120、144、168、192 DPI 和负坐标。

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

### 12.2 发送前目标校验

校验步骤：

1. SessionId 必须等于协调器当前 SessionId。
2. `GetForegroundWindow` 必须与会话顶层窗口一致；允许的 owner/modal 关系必须显式验证。
3. 重新读取当前焦点元素；RuntimeId 相同则通过。
4. 若 RuntimeId 因 DOM/控件重建变化，则要求顶层窗口和进程相同，并重新分类为 Editable 后建立新会话；本次动作默认取消，避免误输入。
5. 键盘 HWND 不得是前台窗口。

目标验证失败不调用 `SetForegroundWindow`，只返回 `TargetInvalid`。

### 12.3 Text 路径

- 枚举 .NET 字符串中的 UTF-16 code unit。
- 每个 code unit 生成 `KEYEVENTF_UNICODE` KeyDown 和 KeyUp，`wVk=0`。
- 代理对按原顺序发送两个 code unit，不拆丢。
- 尽量在一个 `INPUT[]` 中提交，避免和真实键盘事件交错。
- 该路径表达直接文本提交，不保证触发目标 IME 的候选流程。

密码目标执行前，必须拒绝 `safeForPassword=false` 的布局动作。

### 12.4 Key 路径

- 标准键使用 Virtual Key；需要区分左右/扩展键时同时提供 scan code 和 `KEYEVENTF_EXTENDEDKEY`。
- 目标键盘布局通过目标前台线程 `GetKeyboardLayout` 获得。
- `MapVirtualKeyEx` 用于需要的 VK/scan code 转换。
- 每个非状态键默认发送 KeyDown + KeyUp。
- 不用 `PostMessage(WM_CHAR)` 作为回退。

### 12.5 Hotkey 与修饰键

- 一个热键构建为单个有序 `INPUT[]`：修饰键按下、普通键按下/释放、修饰键逆序释放。
- 每个批次记录 `ModifiersPressedByUs`，只释放本批次合成按下的键，不释放用户实体键盘原本按住的键。
- 发送前通过 `GetAsyncKeyState` 读取物理状态，用于显示和冲突诊断。
- 异常、取消和进程退出路径都调用安全释放。
- CapsLock 默认发送 `VK_CAPITAL` 改变系统锁定状态，并在随后通过 `GetKeyState` 刷新 UI。

### 12.6 失败和 UIPI

`SendInput` 返回数量少于请求数量时：

1. 将该批次标记为失败，不自动重试，避免重复字符。
2. 记录请求事件数、返回事件数、Win32 错误码、目标进程 ID 和完整性判断；不记录动作文本。
3. 在键盘状态区域显示简短失败提示。
4. 如果目标可能处于更高完整性级别，提示“目标权限高于本程序，无法输入”，不得请求临时提权。

## 13. 键盘布局设计

### 13.1 内置与用户布局

```text
Application Install Directory
└─ layouts\builtin\qwerty.en-US.json   (只读)

%LocalAppData%\VirtualKeyboard\
└─ layouts\*.json                      (用户布局)
```

用户布局 ID 与内置布局冲突时，默认不覆盖内置布局；通过独立命名空间或显式 override 字段处理。

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

- `schemaVersion` 必须受支持。
- `id` 在布局内唯一，长度受限。
- 每行至少一个按键，行数和总按键数设安全上限。
- `width` 必须为有限正数，并设置合理最大值。
- action 类型必须属于白名单。
- `text.value` 限制长度；日志和错误信息不得回显其内容。
- `hotkey` 键数设置上限且必须能映射。
- 出现 `command`、脚本或未知可执行动作时拒绝整个布局。
- 校验失败继续使用最后一个有效布局，并向用户显示具体字段路径和非敏感错误。

### 13.4 视图生成

`KeyboardController` 将布局模型映射为不可变 `KeyViewModel` 集合。宽度采用 Grid 星号或等价权重算法；最小点击尺寸、间距和字体由主题资源控制。视图只绑定动作 ID，不直接持有原生 VK 常量处理逻辑。

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
