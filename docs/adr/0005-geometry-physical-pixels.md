# ADR-005：屏幕几何统一使用物理像素

状态：已采用（Accepted）

日期：2026-09-06

关联：方案设计文档 ADR-005、§12、§13；规格 §6、NFR-PERF-001、AC-011/012；T0.1、T1.3

## 背景

几何数据来源混合 DPI 体系：UIA BoundingRectangle 与 Win32 API 返回物理像素；WPF 布局使用 DIP（Effective DPI 换算）；Windows 多屏 DPI 可各不相同且可运行时变化（DPI awareness 与 Per-Monitor v2）。混用单位会导致键盘错位、缩放时反复重排。

## 决策

- 屏幕几何算法、窗口放置、caret 检测、碰撞检测、动画的**输入输出统一使用物理像素**。
- **配置与持久化尺寸使用 DIP**（`KeyboardSettings` 尺寸、透明度等）；仅在 Windows 适配层边界（窗口宿主、UIA 交互）按 Effective DPI 做 DIP↔物理像素转换。
- 所有屏幕矩形/边界值必须校验“有限、非空、屏幕内”；无效数据拒绝并走降级路径。
- DPI 变化触发一次确定性重排，不叠加隐式动画；MVP 动画仅使用 WPF composition（GPU），禁止线程内逐帧 CPU 位图缩放。

## 后果

- 优点：跨 DPI 多屏下几何一致（AC-011/012）；Core 层几何可完全脱离 WPF 单元测试。
- 约束：所有跨层数据必须显式标注单位（物理像素 vs DIP），防止边界换算遗漏（T1.3 单元覆盖）。
