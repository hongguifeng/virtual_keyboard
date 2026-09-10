# REL-029 补充调查：VS Code 搜索与数值输入框

日期：2026-09-11。范围：FR-FOC-001/003/004/006、FR-DIA-001/002、NFR-COMP-001；仅调查和文档记录，未修改产品分类、目标校验或输入行为。

现场键盘版本为 `1.0.12+3308b03`，VS Code 为 1.137.0。使用现有 TestHost `--focus-probe`、产品诊断日志、computer-use 界面观察和控件源码交叉验证。以下操作时间为北京时间（UTC+08:00）；原始日志使用 UTC。原始探针文件仅保存在本机 `artifacts/focus-controls-investigation/`，不提交。

## 1. VS Code：单击仍是列表焦点，双击后变为输入框

用户补充：打开标题栏搜索后，单击输入区没有键盘，双击后出现。对同一个窗口、同一输入位置完成独立对照；本组操作仅获取截图，不在步骤之间展开整棵辅助功能树，不输入字符或执行搜索结果。

| 操作时间 | 操作 | 探针和产品日志 | 可见结果 |
| --- | --- | --- | --- |
| 03:36:58.867 | 单击标题栏搜索入口 | 焦点变为 ListItem（50007）；ValuePattern/TextPattern 均不存在；产品分类为 Other / NotEditable / NoEditableEvidence | 搜索面板打开，悬浮按钮未出现 |
| 03:37:14.175 | 单击展开后的输入区 | 焦点持续为 ListItem；直到下一步前仍未出现 Edit | 悬浮按钮未出现 |
| 03:37:30.872 | 双击同一输入位置 | 03:37:31 起事件和查询提供 Edit（50004）、HasKeyboardFocus=true、可写 ValuePattern；03:37:31.569 分类 Editable，03:37:31.580 记录 OverlayShown | 悬浮按钮出现 |
| 03:37:58.806 | Escape | 关闭搜索，焦点返回 Document | 搜索面板关闭 |

`code-search-2.jsonl` 共 333 行，DroppedEvents=0、WorkerStopped=True、WriteError=0，行 ErrorCode 全为 0。双击前的稳定列表阶段包含 107 条 Poll，事件目标也报告列表项；不是只丢失了一次焦点事件。本组时间范围内产品没有 FocusWorkerStalled/Exited/Restarting/Exhausted 事件。

VS Code 已安装 bundle 中的 Quick Input 实现把输入框与结果列表通过 `aria-controls` 关联；输入框持有 DOM 焦点时，列表焦点变化仍会设置输入框的 `aria-activedescendant`，并开启 list focus mode。因此，文本输入宿主和对外报告的活动列表项可能不同。该源码解释与现场 ListItem → Edit 的变化一致；本次未进一步观测双击时 Chromium 内部为何改变 UIA 焦点，不能将其写成已证实的 provider 内部机制。

当前 `FocusedElementResolver.ResolveEventFocus` 接受 HasKeyboardFocus=true 的全局查询结果。列表项在当前映射中是 Other，分类器没有可编辑证据便拒绝；本组事件目标同样是列表项，现有事件 sender 回退无法解决。仅增加重试次数或放宽 ComboBox 规则也不能解决这组稳定样本。

待研究的修复方向是：从活动列表项找到有可信关联的可写输入宿主，并同时验证前台窗口、复合控件关系、身份和发送前目标。需要先增加有界的关系元数据探针，再确定产品语义和目标会话模型。不能直接把 ListItem/Other 视为 Editable，不能只因附近存在 Edit 就选中它，也不能恢复整棵子树搜索或模拟双击来强行恢复焦点。

临时操作：在本机已验证，双击已展开的搜索输入区可触发悬浮按钮；这不是自动识别问题已经修复的证据。

## 2. WorkEnglish Coach：缺少 Spinner 类型支持

对应设置为“请求超时时间（秒）”。WorkEnglish Coach 的 `src/renderer/src/pages/SettingsPage.tsx` 使用 Ant Design `InputNumber`；其 `rc-input-number` 实现给内部 input 设置 `role="spinbutton"`。这是可直接键入数值的输入框。

现场多次焦点事件报告同一类证据：

| 字段 | 观察结果 |
| --- | --- |
| ControlTypeId | 50016（Spinner） |
| HasKeyboardFocus / IsEnabled / IsOffscreen | true / true / false |
| HasValuePattern / IsReadOnly | true / false |
| HasTextPattern | false |
| 产品日志 | UsedEventTarget=true，Other / NotEditable / NoEditableEvidence |

第一组记录在 03:34:31–38 连续捕获六次 Spinner 事件。补充的 `coach-number.jsonl` 为 50 秒、183 行，DroppedEvents=0、WorkerStopped=True、WriteError=0；03:40:15.422 再次捕获 Spinner，03:40:15.565 产品记录上述拒绝结果。界面观察到数值文本可被选中，悬浮按钮未显示。本次没有修改或保存设置值。

全局 UIA 查询有时返回无焦点的只读 Document，但产品日志证明保留的事件目标已经通过校验。此处最终阻断点是类型与分类规则：

1. `FocusControlType` 没有 Spinner；`FocusSnapshotFactory.MapControlType` 将它归入 Other。
2. `EditabilityClassifier` 不允许 Other 仅凭可写 ValuePattern 变为 Editable，符合现有防误弹约束。
3. 样本不提供 TextPattern，直接复制现有 ComboBox 的“双 Pattern”规则仍然无法识别。

建议将后续修复收敛为单独的 Spinner 支持：保留明确类型，以已验证的可写 ValuePattern 为正向证据，继续优先检查身份、焦点、启用、离屏、只读；无可写值证据时不提升。单独的 RangeValuePattern 只能说明数值可调整，不能证明可输入文本，不能据此放行 Slider、只可步进控件或其他类型。

回归测试至少覆盖类型映射、工作进程协议往返、Spinner 可写/只读/禁用/失焦/离屏/无证据，以及 Other、列表项、滑块不被提升。还需 WorkEnglish Coach 现场复验与目标切换防误投验证。

## 3. 验证与剩余工作

- `dotnet build tests/VirtualKeyboard.TestHost/VirtualKeyboard.TestHost.csproj -c Release --nologo`：通过，0 警告/错误。首次 PATH 指向仅安装运行时的 dotnet，找不到 SDK；改用本机已有 .NET 10.0.400 SDK 后通过，未修改 global.json 或安装 SDK。
- `scripts/build.ps1 -SkipPackage`：完整 Release 构建通过，0 警告/错误；Core 277、Windows 251、Integration 100，共 628 项通过、0 跳过。
- `VirtualKeyboard.TestHost.exe --selftest`：退出码 0。
- 第一轮混合现场探针 `code-search-1.jsonl`：395 行，DroppedEvents=0、WriteError=0，但 WorkerStopped=False，未在截止后的退出等待内正常停止，不能算作完整成功采样。随后独立的 VS Code 对照和数值框采样均正常停止；不据首轮结果推断新的产品根因。

本次只新增调查记录、已知限制与待办，没有新增自动测试或修改产品实现。上述测试是现有基线验证，不表示两个兼容性缺陷已修复。未发布新版本；规格和设计的产品语义未变。Spinner 修复、复合控件关系调查及独立主审查仍待完成。
