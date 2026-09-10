# REL-029 输入控件兼容性修复验证

日期：2026-09-11。原故障与操作对照见 [调查记录](search-spinner-investigation.md)。本记录分阶段追加，不将尚未验证的行为记为已修复。

## Spinner 数值输入

- Windows 映射和 Core DTO 保留 Spinner；满足身份、焦点、启用、可见及非只读条件后，以可写 ValuePattern 判为 Editable，不要求 TextPattern。只有范围调节、文本展示或 caret 的 Spinner 不提升，Slider/ListItem/Other 的防误弹规则不变。
- 新增分类回归先在旧逻辑失败：有 TextPattern 返回 Unknown，无 TextPattern 返回 NotEditable；修复后分类测试 36 项通过。类型映射与工作进程协议定向 10 项通过。
- 完整 `scripts/build.ps1 -SkipPackage`：Release 0 警告/错误；Core 285、Windows 253、Integration 100，共 638 项通过，0 跳过。
- 普通权限的修复版生产 `--focus-worker` 在 WorkEnglish Coach 设置页读取真实数值框：北京时间 03:59:17.295，Spinner / Editable / ValuePattern、HasKeyboardFocus=true、UsedEventTarget=true。观测过程未修改设置值；输出仅允许的数字、枚举和布尔元数据，原始证据仅保留在本机 artifacts。
- 此阶段验证分类和生产进程传输；整合版的实际悬浮显示、输入与其他控件验证在后续记录，独立主审查仍待完成。
