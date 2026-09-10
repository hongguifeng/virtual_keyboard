# Windows 智能悬浮虚拟键盘

[English](README.md)

Virtual Keyboard 是一款轻量的 Windows 悬浮虚拟键盘。它会在可编辑输入框附近自动出现，不抢占目标应用的焦点，并把按键发送到你正在使用的输入框。

![Virtual Keyboard 在 Windows 上的实际运行效果](docs/keyboard.png)

## 主要功能

- 在支持的可编辑输入框中自动出现，离开输入场景时自动隐藏。
- 使用标准美式 QWERTY 排布，包含方向键、Win、Fn、修饰键和编辑键。
- Shift、Ctrl、Alt、Win、Fn 和 Caps Lock 单击锁定并明显高亮，再次单击释放。
- 长按 Backspace 连续删除，并逐步加快删除速度。
- 为输入法候选窗预留空间，兼容微信输入法等第三方中文输入法的候选界面。
- 支持拖动标题移动，以及拖动边缘或四角调整大小。
- 最多添加 12 个自定义文字或录制组合键，并自动排列在键盘右侧的新列中。
- 界面支持 English 和简体中文，默认使用 English。
- 常驻系统托盘，按 Windows 用户分别保存设置。

## 系统要求

- 64 位 Windows 10 或 Windows 11。
- 使用框架依赖包时需要安装 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。
- 普通 Windows 桌面会话。本软件不会绕过安全桌面或更高权限应用的安全边界。

## 安装和启动

1. 从 [GitHub Releases](https://github.com/hongguifeng/virtual_keyboard/releases) 下载最新的 `VirtualKeyboard-*-win-x64-framework-dependent.zip` 和对应 `.sha256` 文件。
2. 建议校验 SHA-256。
3. 将 ZIP 解压到有写入权限的文件夹。
4. 运行 `VirtualKeyboard.App.exe`。

软件启动后常驻系统托盘。关闭键盘窗口只会隐藏键盘；需要完全退出时，请使用托盘菜单中的“退出”。

> 当前版本没有 Authenticode 数字签名，Windows 可能显示安全警告。请只从本仓库下载发布包并校验哈希。

## 日常使用

点击受支持的可编辑输入框，键盘会出现在附近。普通按键直接单击即可；Shift、Ctrl、Alt、Win 或 Fn 单击一次为保持状态，再次单击释放。Fn 会把数字行映射为 F1–F12，不等同于电脑厂商的硬件 Fn 键。

拖动标题区域可以移动键盘，拖动任意边缘或四角可以调整大小，最终尺寸会自动保存。

托盘菜单可以暂停或启用自动行为、显示键盘、打开设置、重新加载布局以及退出软件。

## 设置说明

- “界面语言”在 English 和简体中文之间切换。设置窗口会立即更新，保存后主键盘和托盘菜单也会使用所选语言。
- “自动显示 / 自动隐藏”控制键盘是否跟随输入焦点显示或隐藏。
- “宽度 / 高度 / 边距”控制键盘尺寸和与目标输入框的距离。
- “透明程度”范围为 0%（完全不透明）到 70%（最透明）。
- “拖动位置保留”可以只为当前输入框保留位置，或在不同输入框之间持续保留。
- “开机启动”在登录 Windows 时自动启动虚拟键盘（写入当前用户启动项，无需管理员权限）；首次启动默认关闭。
- “自定义按键”可以输入 Unicode 文字，也可以录制 `Win+Tab`、`Ctrl+Shift+S` 等组合键。录制时按下组合中的全部按键，再全部松开即可完成。
- “详细诊断”只会在本地日志中增加非敏感焦点元数据。软件不会记录输入文字、密码、剪贴板内容、UI Automation 名称或值。

设置和日志保存在 `%LocalAppData%\VirtualKeyboard\`。密码输入框中不会显示自定义按键。

## 已知边界

- 焦点检测由独立后台进程执行，辅助功能查询卡住时可自动恢复，因此看到两个应用进程属于正常情况。连续恢复失败达到上限后，请重新启动应用并保留诊断日志；见[恢复验证](docs/focus-worker-validation.md)。
- 带建议的可编辑搜索框（ComboBox）需由应用同时提供可写 ValuePattern 和 TextPattern 才能识别；GitHub 搜索框修复及实测范围见[验证记录](docs/combo-focus-validation.md)。
- 输入受 Windows 正常权限边界限制。普通权限启动的键盘不能向管理员权限窗口输入。
- 不支持安全桌面、UAC 提示和厂商专用硬件 Fn 行为。
- 不同软件暴露可编辑控件的方式不同，兼容性可能存在差异。报告问题时请提供应用名称、Windows 版本和复现步骤，但不要提供密码或敏感输入内容。
- 在代码签名和剩余实体设备兼容矩阵完成前，当前发布包仅用于测试。

更多信息请参阅[用户指南](docs/user-guide.md)和[已知问题](docs/release/known-issues-1.0.0.md)。

## 从源码构建

安装 `global.json` 指定的正式版 .NET 10 SDK，然后在 PowerShell 中执行：

```powershell
.\scripts\build.ps1
```

脚本会还原依赖、执行 Release 构建和全部测试、发布 `win-x64`，并在 `artifacts/` 下生成 ZIP 和 SHA-256 文件。CI 会在推送和拉取请求时执行构建与测试；只有匹配 `v*` 的版本标签才会触发发布打包。

架构和贡献者资料请参阅[功能规格说明](Windows%20智能悬浮虚拟键盘软件功能规格说明.md)、[方案设计文档](Windows%20智能悬浮虚拟键盘方案设计文档.md)、[开发计划](Windows%20智能悬浮虚拟键盘开发计划%20TODO.md)和 [ADR 索引](docs/adr/README.md)。

## 隐私与安全

Virtual Keyboard 完全在本地工作，不会把输入数据发送到服务器。布局和配置会按封闭动作 schema 校验，不支持任意 Shell 命令或脚本。


1.0.6 改善切换软件后的焦点识别恢复，并补充分类原因、重试和回退日志。更新前请从托盘退出旧版本；排查步骤见 [用户指南](docs/user-guide.md)。

1.0.7 补充无焦点容器的子元素解析及焦点状态日志；请解压到构建输出目录之外试用。

开发排查可运行独立只读 [焦点对照探针](docs/focus-probe.md)，对比 UIA 事件目标与后续查询，不显示键盘或发送输入。
