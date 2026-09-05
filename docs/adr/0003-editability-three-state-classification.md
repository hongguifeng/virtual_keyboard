# ADR-003：可编辑性采用三态分类（Editable / NotEditable / Unknown）

状态：已采用（Accepted）

日期：2026-09-06

关联：方案设计文档 ADR-003、§8；规格 FR-FOC-003、004、AC-006、AC-013；T0.1、T2.3

## 背景

UI Automation 信息不完整：不同 provider（Win32、WPF/WinForms、Chromium/Electron）暴露的 pattern、属性差异大。二态分类（可编辑/不可编辑）会把证据不足的元素错误地归入“可编辑”或“不可编辑”任一侧，导致误弹键盘或漏弹。

## 决策

- 分类结果为三态：`Editable`、`NotEditable`、`Unknown`，每次分类携带稳定 `ReasonCode`。
- 正向证据包括：`ValuePattern.IsReadOnly == false`、`TextEditPattern`、可编辑 Edit 控件、密码 Edit 控件、Edit/Document + 有效 caret/selection 坐标。
- **TextPattern 或 TextPattern2 单独存在不构成 Editable 证据**；“仅 TextPattern”默认归入 `Unknown(TextPatternOnly)`。
- 元素无效/已销毁、禁用、无键盘焦点、显式只读、离屏或属于本程序时不得判为 Editable。
- **`Unknown` 默认不自动显示键盘**，但写入原因码和 provider 元数据以便兼容性改进。
- 不维护应用名称白名单；确需 provider 特例时，必须通过版本化兼容规则、测试和独立配置引入。

## 后果

- 优点：防止只读网页正文/文档误弹（AC-006）；Unknown 原因码支撑 M2/M8 兼容率改进与诊断。
- 约束：部分 provider 信息不足的编辑器可能不自动弹出，属受控降级；判定规则变更必须走规格 §11 需求变更流程，并同步三份基线文档。
