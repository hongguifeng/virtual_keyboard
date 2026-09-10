# REL-029 输入控件兼容性修复验证

日期：2026-09-11。原故障与操作对照见 [调查记录](search-spinner-investigation.md)。本记录分阶段追加，不将尚未验证的行为记为已修复。

## Spinner 数值输入

- Windows 映射和 Core DTO 保留 Spinner；满足身份、焦点、启用、可见及非只读条件后，以可写 ValuePattern 判为 Editable，不要求 TextPattern。只有范围调节、文本展示或 caret 的 Spinner 不提升，Slider/ListItem/Other 的防误弹规则不变。
- 新增分类回归先在旧逻辑失败：有 TextPattern 返回 Unknown，无 TextPattern 返回 NotEditable；修复后分类测试 36 项通过。类型映射与工作进程协议定向 10 项通过。
- 完整 `scripts/build.ps1 -SkipPackage`：Release 0 警告/错误；Core 285、Windows 253、Integration 100，共 638 项通过，0 跳过。
- 普通权限的修复版生产 `--focus-worker` 在 WorkEnglish Coach 设置页读取真实数值框：北京时间 03:59:17.295，Spinner / Editable / ValuePattern、HasKeyboardFocus=true、UsedEventTarget=true。观测过程未修改设置值；输出仅允许的数字、枚举和布尔元数据，原始证据仅保留在本机 artifacts。
- 此阶段验证分类和生产进程传输；整合版的实际悬浮显示、输入与其他控件验证在后续记录，独立主审查仍待完成。

## VS Code 搜索组合控件

- 原生 UIA 探针确认：输入宿主有 ControllerFor，指向当前焦点 ListItem 的 List 祖先；ARIA 属性声明 haspopup=listbox。宿主 HasKeyboardFocus=false，未提供活动 caret；实现不伪造宿主焦点。托管 AutomationProperty.LookupById(30104) 返回 null，因此增加 SDK COM 适配层。
- 仅检查最多 4 层局部祖先、每层 16 个直接子项和 8 个关系。输入宿主必须唯一、同进程、可聚焦、可见、启用、非密码、具有可写 ValuePattern 和 TextPattern；普通列表、无关系、歧义及只读/失效均拒绝。
- 首个分类回归先在旧逻辑返回 NotEditable 失败。修复后分类 11 项、关系解析 24 项通过；新增双身份 IPC 和发送前宿主替换/丢失取消测试。
- 真实 VS Code 1.137.0：北京时间 04:15:48.672 单击标题栏搜索入口后，修复版生产 worker 返回 ListItem / Editable / SearchInputRelationship，HasKeyboardFocus=true；未进行双击。这是生产识别证据，实际按钮与输入将在整合验证补充。
- 完整 `scripts/build.ps1 -SkipPackage`：Release 0 警告/错误，Core 296、Windows 280、Integration 100，共 676 项通过，0 跳过。
- UIA 未暴露这种关系的其他组合控件仍保持保守拒绝；未证明所有 Chromium/ARIA 组合控件都使用同一关系。独立主审查待完成。
