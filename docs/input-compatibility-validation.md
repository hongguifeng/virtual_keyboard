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

## 补充原生输入矩阵

`InputCompatibilityTests` 在一个 TestHost 窗口中顺序切换 9 个合成控件，每个场景使用新生产 worker，验证真实 HWND/UIA 和前台焦点。只输出类型、判定、原因码，不读写用户内容。

| 场景 | 真实 UIA 类型 | 判定 / 证据 |
|---|---|---|
| NumericUpDown | Edit 子项 | Editable / ValuePattern |
| 只读 NumericUpDown | Edit 子项 | NotEditable / ReadOnly |
| MaskedTextBox | Edit | Editable / ValuePattern |
| 可编辑 ComboBox | Edit 子项 | Editable / ValuePattern |
| 仅选项 ComboBox | ComboBox | NotEditable / NoEditableEvidence |
| RichTextBox | Document | Editable / CaretEvidence |
| 只读 RichTextBox | Document | NotEditable / ReadOnly |
| TrackBar | Other | NotEditable / NoEditableEvidence |
| ListBox 项 | ListItem | NotEditable / NoEditableEvidence |

- 初版逐个启动测试窗口时，完整门禁有 7 项因未取得前台焦点超时；附加诊断确认实际采到窗口管理器的 Pane，而非预期 TestHost。改为复用窗口、stdin/ready 握手，在测试夹具中建立前台前提后，九场景均通过；没有删除场景或放宽可编辑断言。
- 本次补充证明原生复合控件的真实 Edit 子项与富文本 caret 路径已覆盖；没有仅凭控件名称扩大规则。现代数值 Spinner 和搜索代理关系分别由前述两项修复补齐。
- 同时补齐 SearchInputRelationship 的应用诊断映射，新增回归先确认旧映射错误返回 ElementInvalid，再修复。
- 最终完整 `scripts/build.ps1 -SkipPackage`：Release 0 警告/错误，Core 296、Windows 280、Integration 102，共 678 个测试通过，0 跳过。其中一个集成测试完整验证上述九个场景。TestHost `--selftest`：WPF 31、WinForms 36，均退出 0。
- 矩阵运行命令见 README。尚未覆盖的提供程序包括自绘画布、游戏/终端、远程桌面、特定 Office 单元格原位编辑，以及不暴露可写值/caret/关系的网页编辑器；不根据窗口标题或应用名直接放行。

## 整合版现场验收

- 本地发布并运行 `1.0.13-local`（代码基于 `83ff7fe`），保留原 1.0.12 便携目录；未创建远端发布或推送。
- VS Code 1.137.0：单击标题栏入口即出现悬浮按钮，再单击展开的输入区仍保持显示，全程无双击。04:32:06.855（北京时间）的应用日志记录 ListItem / Editable / SearchInputRelationship，随后创建会话并记录 OverlayShown；现场画面显示输入框下方的展开按钮。
- 自动化工具拒绝点击覆盖在 VS Code 上方的 NoActivate 工具窗口，故没有把自动化截图冒充真实按键验收。
- 用户随后在本任务明确确认：VS Code 搜索框和 WorkEnglish Coach 数值设置框旁点开悬浮键盘后，均能正常输入。两项原始问题的显示与输入现场验收完成，原生 NoActivate、目标切换和旧会话拒绝另有完整自动测试证据。
- 发布后的主进程与独立检测进程均从本地修复版目录运行；原版进程已停止。未改动 WorkEnglish Coach 源码或由代理保存其设置值。
- 独立主审查、其他提供程序及跨物理显示器/触摸矩阵仍未声明完成。
