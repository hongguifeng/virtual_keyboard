# ADR-001：MVP 使用 C#、WPF 和 .NET 10 LTS

状态：已采用（Accepted）

日期：2026-09-06

关联：方案设计文档 ADR-001、§17、§19；规格 §3.2、NFR-SEC-001；T0.1

## 背景

MVP 需要一个 Windows 桌面运行时，能够承载悬浮键盘、设置窗口和托盘 UI，同时接入 Win32、UI Automation 和 SendInput。候选方向包括 C# + WPF、C++ + Win32/WinUI 3 等。

## 决策

- MVP 使用 **C#、WPF**，目标框架为 **`net10.0-windows`**。
- 运行时基线固定为 **.NET 10 LTS**。
- 首发 RID 为 **`win-x64`**；SDK 版本由 `global.json` 管理（T0.3 落地）。
- Win32、UI Automation 和 SendInput 通过集中在 `VirtualKeyboard.Windows` 适配层的 P/Invoke / 框架 API 接入；`VirtualKeyboard.Core` 不引用 WPF、UIAutomationClient 或具体 P/Invoke 类型。
- MVP 性能目标（NFR-PERF-001）不要求迁移到 C++。

## 后果

- 优点：WPF 快速构建键盘/设置/托盘 UI；.NET 10 LTS 作为 2026 年新项目统一运行时基线；Core/Windows 分层便于无桌面单元测试。
- 约束：MVP 首发仅 `win-x64`；不在同一版本并行维护 C++ + WinUI 3 方案；如未来迁移，必须作为新的架构决策和独立项目评估（见 TODO §17“暂不实施清单”）。
