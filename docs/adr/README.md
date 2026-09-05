# 架构决策记录（ADR）索引

本目录记录项目架构决策，与 `Windows 智能悬浮虚拟键盘方案设计文档.md` 第 3 章保持一致。
判定规则变更、技术栈迁移等必须通过新增 ADR 并同步更新三份基线文档（见 `AGENTS.md` §2/§4）。

| ADR | 标题 | 状态 |
| --- | --- | --- |
| [0001](0001-tech-stack-csharp-wpf-net10.md) | MVP 使用 C#、WPF 和 .NET 10 LTS | 已采用 |
| [0002](0002-auto-hide-follows-keyboard-focus.md) | 自动隐藏遵循键盘焦点，不使用全局低级鼠标钩子 | 已采用 |
| [0003](0003-editability-three-state-classification.md) | 可编辑性采用三态分类（Editable / NotEditable / Unknown） | 已采用 |
| [0004](0004-input-three-paths.md) | 输入分 Text、Key、Hotkey/Modifier 三条独立路径 | 已采用 |
| [0005](0005-geometry-physical-pixels.md) | 屏幕几何统一使用物理像素 | 已采用 |
| [0006](0006-normal-user-permission-boundary.md) | MVP 以普通用户权限运行 | 已采用 |
