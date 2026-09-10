# REL-033 悬浮自定义按钮验证

范围：FR-VIS-009、FR-CFG-001、AC-017；沿用 NoActivate、目标防误投、密码限制、封闭动作与物理像素约束。

## 自动验证

2026-09-11，Windows x64，.NET SDK 10.0.400。先补充配置测试并确认缺少新字段时编译失败，再补充设置及窗口测试后实现功能。

```powershell
dotnet test tests/VirtualKeyboard.Core.Tests/VirtualKeyboard.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~Configuration' --no-restore
dotnet test tests/VirtualKeyboard.IntegrationTests/VirtualKeyboard.IntegrationTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~LauncherWindowTests|FullyQualifiedName~SettingsWindowTests|FullyQualifiedName~AutoStartTests'
.\scripts\build.ps1 -SkipPackage
.\tests\VirtualKeyboard.TestHost\bin\Release\net10.0-windows\VirtualKeyboard.TestHost.exe --selftest
```

- 配置定向测试 50 项、集成定向测试 52 项通过。
- 完整 Release 构建：0 警告、0 错误。Core 277 + Windows 251 + Integration 100 = 628 项通过，0 跳过。TRX 保留在忽略目录 `artifacts/test-results/`。
- TestHost：WPF 页自检通过，WinForms 36 项通过，两页退出码均为 0。
- 配置覆盖：旧文件默认空列表、独立列表及不可变快照、四种动作往返、空元素/缺字段/非法类型/命令动作恢复、分别 12 项上限、标签 32/文本 256 限制、错误不回显内容。
- 编辑器覆盖：跨位置提交、分别添加/编辑/删除、中文切换和重开、录制组合键、切换位置取消录制、与键盘原有布局无重叠；自启同步/失败回滚、启停和尺寸持久化保留新列表。
- 窗口与输入覆盖：40 DIP 等大单行、顺序和间距、text/key/hotkey/chord 各自发送、不展开键盘、旧按钮失效、Backspace 手势/重复清理、前台变化、设置/暂停/展开/退出拒绝旧输入、密码隐藏、失败提示清理，以及排队后前台变化时重新校验。
- DPI 覆盖：原生 `WM_DPICHANGED` 下 0/3 个自定义按钮在 100%、125%、150%、175%、200% 的整行尺寸；12 个自定义按钮在负坐标和小工作区中的等比缩放及位置边界。
- `LauncherAndKeyboardExpansionPreserveNativeForegroundAndEditorFocus(true)` 使用真实 WPF 输入目标、系统 `SendInput` 鼠标点击和真实文字发送，断言文字进入目标、键盘保持收起，前台 HWND、焦点 HWND 与编辑器键盘焦点不变，随后再点击展开并重复焦点断言。测试初始化使用临时输入队列附加，产品没有激活或附加输入队列代码。焦点评估为确定性测试数据，不代表外部 UIA 兼容性全覆盖。

开发期间的首次窗口定向测试因直接调用测试手势缺少 WPF 同步上下文而中止；测试 STA 初始化显式安装 DispatcherSynchronizationContext 后修复。一次原生测试在目标已是前台时仍尝试向自身附加输入队列而失败，修正为仅在两个线程不同时附加。原有及新增焦点断言均保留，最终定向与全量测试全部通过。

## 界面检查

使用隔离配置加载实际 WPF 窗口并通过 RenderTargetBitmap 渲染，检查图标加“问候 / 保存 / Enter”三个按钮的等大排列，以及中英文“显示位置”选择器、列表和录制编辑器。修正共享录制按钮的固定宽度，使英文文案完整显示，复验通过。

本地证据为 `artifacts/launcher-validation/launcher-custom.png`、`settings-launcher-zh.png`、`settings-launcher-en.png`，生成物未提交。英文设置中原有位置保留标签间距偏紧，作为独立 review note 记录，未扩大本任务修改范围。

## 可重复人工复验

1. 单独运行本次构建，打开设置，启用“自动显示”和“先在光标附近显示悬浮按钮”。
2. 在“自定义按键 → 显示位置”选择“悬浮按钮”，分别添加文字、Enter 和录制 Ctrl+S，保存。切换回“键盘内”确认该组内容独立，重开设置后确认两组仍保留。
3. 聚焦 TestHost/Notepad 的普通输入框：展开按钮左侧在先，新按钮在右侧等大并排。点击文字按钮确认直接输入、键盘保持收起，点击展开图标确认键盘正常展开。
4. 按住按钮后切换输入目标，确认旧手势不执行。进入设置、暂停或使目标失效，确认按钮隐藏；密码框中只显示展开按钮。
5. 验证录制组合键的预期应用行为、发送失败时的短暂提示、各显示器边缘和 100–200% DPI。关闭悬浮模式后确认恢复直接展开，重新开启后自定义按钮仍在。

独立主审查，以及完整浏览器、跨物理显示器、实体鼠标和触摸矩阵仍待执行；上述系统合成鼠标测试不替代设计 10.2 的物理设备门禁。未改动用户正在运行的已安装实例，未发布远端版本。
