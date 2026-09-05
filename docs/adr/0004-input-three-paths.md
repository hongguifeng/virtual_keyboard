# ADR-004：输入分 Text、Key、Hotkey/Modifier 三条独立路径

状态：已采用（Accepted）

日期：2026-09-06

关联：方案设计文档 ADR-004、§10；规格 FR-KEY-002/004/005、NFR-SEC-004、AC-009/010/016；T0.1、T3.4

## 背景

键盘按键存在三种语义完全不同的动作：字符输入、按键（回车/Tab/方向键）、热键与组合键。把三者混用一条发送路径或统一映射表，会产生错误的 scancode/Unicode 组合、丢失组合语义，并使失败诊断困难。

## 决策

- **Text**：发送 `SendInput` 的 `INPUT_KEYBOARD` + `KEYEVENTF_UNICODE`，以 Unicode 代码点写入，不依赖 scancode/keyboard layout；必须经 UTF-16 surrogate 校验。
- **Key**：使用稳定 scancode（`MapVirtualKey`/`GetKeyboardType`）+ `KEYEVENTF_SCANCODE` 发送 key-down/key-up。
- **Hotkey/Modifier**：以显式 modifier state machine 组合 modifier down → keydown/keyup → modifier up，保持独立实现路径。
- **禁止使用 `PostMessage(WM_CHAR)` 作为输入回退**；UIPI 阻断时明确失败，不绕过。
- 每条路径独立实现、独立校验、独立诊断（失败原因码区分文本无效/按键无效/目标过期/UIPI/会话失效等）。
- 输入批次开始前与每个 action 前校验最新 TargetSession；失败取消整批。
- 批量输入串行化，批次有数量上限（默认 512，M3 冻结）；批中延迟仅用于输入节奏，不作正确性依赖。

## 后果

- 优点：语义清晰，测试可分别覆盖 AC-009/010/016/017；失败可诊断。
- 约束：三条路径实现与测试量更大（T3.3–T3.9）；组合键 scancode 映射需覆盖不同键盘布局的边界。
