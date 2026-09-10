# REL-032 悬浮按钮验证

范围：FR-VIS-008、FR-CFG-001、AC-016，以及既有 NoActivate、目标校验、物理像素定位和配置恢复约束。

## 自动验证

2026-09-11，本机 Windows x64，使用 `global.json` 要求的 .NET SDK 10.0.400。

```powershell
.\scripts\build.ps1 -SkipPackage
.\tests\VirtualKeyboard.TestHost\bin\Release\net10.0-windows\VirtualKeyboard.TestHost.exe --selftest
```

- Release 构建：0 warning、0 error。
- Core 266、Windows 251、Integration 80，共 597 项通过，无跳过；TRX 位于忽略目录 `artifacts/test-results/`。
- TestHost WPF 与 WinForms 自检均退出 0（WinForms 36 项）。
- 配置覆盖：默认关闭、schema v1 缺失字段、true/false 往返、非布尔及 null 恢复、中英切换、设置重开、禁用自动显示时保留选择、调整尺寸、启停、自启同步和失败回滚。
- 状态覆盖：先显示按钮、点击展开、同目标新版本维持展开、新目标恢复按钮、无 RuntimeId 保守恢复按钮、失焦/Unknown、手动抑制、托盘显示、设置/暂停/销毁/退出、旧结果和旧按钮版本拒绝。
- Windows 窗口覆盖：只显示 40 DIP 按钮、不可聚焦控件、`WM_MOUSEACTIVATE` 返回 `MA_NOACTIVATE`、100/125/150/175/200% DPI 消息重算尺寸、不污染键盘宽高、负坐标和工作区边缘定位。
- `LauncherAndKeyboardExpansionPreserveNativeForegroundAndEditorFocus` 使用真实 HWND 和 `SendInput` 鼠标点击按钮，断言展开后前台 HWND、焦点 HWND、编辑器键盘焦点均不变。仅测试初始化临时附加前台输入队列以激活测试编辑器，随后立即解除；产品代码没有加入激活或线程附加 API。该测试使用确定性焦点评估，不代替跨软件 UIA 兼容矩阵。
- 首次该原生焦点测试的前置激活受 Windows 后台进程限制，尚未点击按钮就失败。修正测试初始化后通过，未删除或放宽焦点断言；桌面测试集合串行执行，避免自身与其他 WPF 窗口争抢前台。

## 界面检查

使用独立配置加载实际 WPF 窗口并渲染，检查蓝色键盘图标、圆角/边框，以及中文设置页新增复选框的完整显示和纵向间距；未见遮挡或截断。本地渲染文件：`artifacts/launcher-validation/launcher.png`（放大检查）和 `settings-zh.png`，不提交生成物。

曾启动独立自动焦点验证窗口与 TestHost，但本机已有旧版键盘实例同时响应焦点，未把该对照算作新版独占运行的外部端到端证据；验证辅助进程已清理，原有实例及用户配置保留。

## 可重复人工复验

1. 单独运行本次构建，从托盘打开设置，开启“自动显示”和“先在光标附近显示悬浮按钮，点击后展开键盘”，保存。
2. 点击 TestHost/Notepad 的普通输入框：只出现小按钮；点击按钮后键盘展开，输入目标保持焦点，点击字母键后字符进入该输入框。
3. 切换到另一个输入框：重新只显示按钮。切换到只读控件或使目标失效：按钮消失；即使关闭自动隐藏，旧按钮也不能展开。
4. 展开后点击键盘关闭按钮，同目标重复焦点不重开；托盘“显示当前键盘”可显式重开。打开设置、暂停或退出后不遗留按钮。
5. 关闭按钮模式并保存，再聚焦输入框：恢复直接展开。重启后检查开关保持；按 100–200% DPI、不同工作区边缘和跨物理显示器重复步骤 2–3。

独立主审查、完整浏览器/VS Code、跨物理显示器及实体触摸矩阵仍待复验。本次未发布远端版本。
