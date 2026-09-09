# REL-029：事件目标修复与现场验证

2026-09-09，普通权限，UI 操作由 computer-use 完成；未输入文本、修改被测文件或操作终端。范围：FR-FOC-001/003/006、FR-VIS-001/003、FR-DIA-001/002，产品语义未变。

## 修复前证据

独立探针 `artifacts/focus-probe-cua-01.jsonl` 第 613/614 行：14:10:20 UTC，同一 VS Code 前台窗口，事件目标为 Edit（50004）、持有焦点、ValuePattern 非只读；1 ms 后全局查询为 Document（50030）、没有焦点、只读，RuntimeId 不同。第二次 `focus-probe-cua-02.jsonl` 第 198/199 行重复该现象。Edge 事件与查询一致。日志不记录 Name、Value、输入文本或 RuntimeId。

## 实现

回调只保存最新 sender 引用和推进事件版本，不访问 UIA 属性或 WPF。MTA 在稳定窗口后选择目标：正常聚焦的全局查询优先；否则尝试事件目标（即时焦点、启用、未离屏、前台 PID 与顶层 HWND 均须一致）；再走原有 native 回退。轮询继续验证该引用，新事件替换，验证不再使用时释放。分类使用采集时的同一元素并再次检查身份、即时焦点与原生目标；不放宽可编辑证据。

新事件、停止和重启作废旧通知；采集完成、分类完成与 Dispatcher 应用前检查有效性。诊断新增布尔 `UsedEventTarget`，保留原有版本、原因、耗时和回退字段。

## 可重复步骤与证据

1. 点击 VS Code 查找框，保持焦点至少 10 秒：键盘显示并保持。14:22:25 UTC 日志为 Edit / ValuePattern / UsedEventTarget=true，随后多轮轮询未重复建立会话。
2. 点击 VS Code File 菜单：键盘隐藏；Escape 关闭菜单后点击编辑区：14:23:29 UTC 再次记录 ValuePattern / UsedEventTarget=true 和 OverlayShown，截图确认键盘显示。
3. 切到 Edge 地址栏：键盘显示；点击正文空白区：14:24:08 UTC TextPatternOnly，键盘隐藏，重试到 4 次停止。
4. 切回 VS Code 编辑区：14:24:28 UTC 再次显示。

上述记录来自本机 1.0.8 验证构建；原始文件在 artifacts 和本机滚动日志中，不提交日志或截图中的用户内容。

最终构建在 14:26 UTC 通过 `scripts/build.ps1 -SkipPackage`：Release 零警告/错误，Core 236、Windows 237、Integration 59（532）全通过。TestHost --selftest 退出码 0；1.0.8 publish 与 verify-release.ps1 通过。最终包运行后 14:28:56 UTC 再次点击 VS Code 查找框，UsedEventTarget=true / ValuePattern，随后 OverlayShown，截图确认显示。SHA-256：`3aefdecc1c8b7b722a3727c57d3cc47b91ccf60f8135840855b3b644ffa74376`。

## 限制与 review

- 本轮验证自动显示/隐藏和切换恢复，没有在用户文档中注入文字；输入防误投和 NoActivate 由现有自动测试覆盖。
- VS Code 的 accessibilitySupport 已在前期试验设为 on，本轮未改设置；不能据此声称 off 模式兼容。
- 同步 UIA provider 仍可能阻塞；这次不实现 COM 硬超时或进程隔离。
- 其他应用、长期稳定性和独立主审查仍待验证，不以本轮成功推断所有间歇问题已经消失。
