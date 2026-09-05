# ADR-004：输入分 Text、Key、Hotkey/Modifier 三条独立路径

状态：已采用（Accepted）

日期：2026-09-06

关联：方案设计文档 ADR-004、§12（输入注入设计）；规格 FR-KEY-003、FR-INP-001~006、AC-009~012；T4.1~T4.7

## 背景

键盘按键存在三种语义完全不同的动作：文本输入、按键（回车/Tab/方向键）、热键与组合键。把三者混用一条发送路径或统一映射表，会产生错误的 scancode/Unicode 组合、丢失组合语义，并使失败诊断困难。`VkKeyScan` 无法处理任意 Unicode 文本，布局与 IME 行为也各不相同，因此三种语义不相互隐式转换。

## 决策

- **Text**：以 `SendInput` 的 `INPUT_KEYBOARD` + `KEYEVENTF_UNICODE` 发送，按 UTF-16 code unit 逐个发送，surrogate pair 保持原顺序、不拆丢；`wVk=0`，不依赖 scancode/keyboard layout；尽量在一个 `INPUT[]` 中提交，避免与真实键盘事件交错。该路径表达直接文本提交，不承诺触发目标应用的 IME 组合流程。
- **Key**：标准键使用 Virtual Key；需要区分左右或扩展键时同时提供 scan code 并置 `KEYEVENTF_EXTENDEDKEY`。目标键盘布局通过目标前台线程 `GetKeyboardLayout` 获得，`MapVirtualKeyEx` 用于所需的 VK/scan code 转换。每个非状态键默认成对发送 KeyDown/KeyUp。
- **Hotkey/Modifier**：以显式 modifier state machine 构建一个有序 `INPUT[]`：修饰键按下 → 普通键按下/释放 → 修饰键逆序释放。只释放本批次合成按下的修饰键，不释放用户实体键盘原本按住的键；异常、取消和进程退出路径必须安全释放。
- **禁止使用 `PostMessage(WM_CHAR)` 作为输入回退**；UIPI 阻断时明确失败，不绕过，不自动提权。
- 每条路径独立实现、独立校验、独立诊断（失败原因码区分文本无效/按键无效/目标过期/UIPI/会话失效等），日志不记录输入内容。
- 每个输入批次开始前必须校验最新目标（前台顶层窗口、进程、最新焦点和 RuntimeId）；校验失败取消本批，不通过激活目标窗口强行发送。
- 批量输入经串行队列处理；批次部分失败不自动重试，避免重复输入。

## 后果

- 优点：语义清晰，AC-009/010/011/012 可按路径分别测试；失败可诊断。
- 约束：三条路径需要独立的 builder、校验与测试（T4.1~T4.7）；Key 路径需覆盖不同键盘布局下 VK/scan code 转换以及左右/扩展键的边界。
