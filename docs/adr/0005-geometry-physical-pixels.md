# ADR-005：屏幕几何统一使用物理像素

状态：已采用（Accepted）

日期：2026-09-06

关联：方案设计文档 ADR-005、§11；规格 FR-POS-001~006、AC-008；T3.1~T3.6

## 背景

几何数据来源混合 DPI 体系：UIA BoundingRectangle 与 Win32 API 返回物理像素；WPF 布局使用 DIP（Effective DPI 换算）；Windows 多屏 DPI 可各不相同且可运行时变化（DPI awareness 与 Per-Monitor v2）。混用单位会导致键盘错位、缩放时反复重排。

## 决策

- UIA、Win32 原生屏幕几何（窗口矩形、caret 矩形、显示器工作区）与定位算法的**输入输出统一使用物理像素**（FR-POS-001~005）。
- **配置与持久化尺寸使用 DIP**（`KeyboardSettings` 尺寸、margin 等）；仅在 Windows 适配层边界（窗口宿主、UIA 交互）按 Effective DPI 做 DIP↔物理像素转换，键盘最终位置由原生 `SetWindowPos` 应用（FR-POS-005）。
- 所有屏幕矩形/边界值必须校验“有限、非空、屏幕内”，支持负坐标；无效数据拒绝并走降级路径（FR-POS-001~003、AC-008）。
- 收到 `WM_DPICHANGED` 或跨屏时，基于当前 TargetSession 重新计算尺寸与位置，不复用旧显示器的物理宽高（FR-POS-005、AC-008；T3.5）。

## 后果

- 优点：混合 DPI、多显示器与负坐标场景下几何一致（AC-008）；Core 层几何可完全脱离 WPF 单元测试。
- 约束：所有跨层数据必须显式标注单位（物理像素 vs DIP），防止边界换算遗漏（T3.1~T3.6 覆盖坐标类型与适配边界）。
