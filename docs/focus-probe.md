# REL-029 焦点对照探针

这是独立 TestHost 诊断模式，产品自动检测实现不变，不显示测试窗口、不发键、不读取输入文本，不改变 VS Code 或浏览器设置。

## 运行

先 Release 构建 TestHost，再运行：

```powershell
.\tests\VirtualKeyboard.TestHost\bin\Release\net10.0-windows\VirtualKeyboard.TestHost.exe --focus-probe .\artifacts\focus-probe-new.jsonl 90
```

输出文件必须不存在，父目录必须已存在；不覆盖已有日志。时间范围 1–600 秒。终端摘要包括 Rows、DroppedEvents、WorkerStopped、WriteError。退出码 0=工作线程停止且写入正常，2=参数/文件/写入错误，3=provider 工作线程未在截止后 1 秒内退出。应在独立 TestHost 进程运行，进程退出结束残留后台线程；这不是产品工作进程隔离方案。

单次最多 4096 行，事件队列最多 64 个，队列满丢弃新事件并计数。每条记录只含固定数量的枚举、数字、布尔值，约束文件大小为数 MiB。不要在采集同时运行会操作焦点的自动化测试。

## 观察什么

EventTarget 是 UIA 事件中的 sender；CurrentAfterEvent 是处理该事件后重新查询的 FocusedElement；Poll 为每次 250 ms 队列等待超时后的补充查询，事件密集时不是固定频率采样。

同一事件的两行 Sequence 相邻；SameIdentityAsEvent 比较内存中的 RuntimeId，不把 RuntimeId 数组写到日志。EventAgeMs 包含排队时间，DurationMs 是该行读取耗时。原生焦点信息在读取 UIA 前捕获，并非原子快照，需要结合时间、PID 和转换中的状态判断。当前数据不能测量 Overlay 端到端延迟，也不能单凭一次不同身份证明误判。

ControlTypeId 使用原始 UIA 数字类型，避免 Other 合并掉 MenuBar 等真实类型。记录 HasKeyboardFocus、IsEnabled、IsOffscreen、IsPassword、Value/Text 模式可用性及 ValuePattern.IsReadOnly，不读取 Name、Value、Text 内容、窗口标题或剪贴板。密码元素不读取 Value/Text 模式。失败只记录 HRESULT。日志保存在指定本地文件，不上传。

本阶段只比较 UIA 事件/查询和原生焦点 HWND；尚未实现 WinEvent/MSAA 对象通道和 TextPattern2 caret。只有发现 UIA 事件目标不足的证据后才扩大探针范围，不能把这个阶段宣称为最终跨应用修复。

## 场景

VS Code 编辑区、搜索框、终端、聊天输入框、菜单；浏览器普通输入框、页面正文和地址栏；Notepad/TestHost 可编辑/只读控件。每个场景停留几秒，记录大致时间和区域类别，不输入真实敏感内容。先保持同一版本及辅助功能设置，再做单独 A/B 实验，避免混淆变量。

## 现场结果

2026-09-09 第一轮 90 秒：403 行，42 对事件/查询，23 对身份不一致，0 丢失事件、0 读取错误、0 写入错误，WorkerStopped=True，单行读取最大 131 ms。样本含浏览器、系统界面及聊天应用，未覆盖 VS Code 故障现场。身份不一致可能只是正常快速切换，不应直接解读为误判或宣称已找到根因。原始日志仅保留在本机 artifacts，不提交。

正负场景的可靠性及用户实际修复效果尚未验收。
