# Virtual Keyboard 1.0.0 内测使用与支持指南

> 本版本是未签名、框架依赖的 Windows x64 内测包，不是正式发布候选版。

## 1. 安装与启动

1. 安装 .NET 10 Desktop Runtime x64。
2. 获取 `VirtualKeyboard-1.0.0-win-x64-framework-dependent.zip` 和同名 `.sha256` 文件。
3. 在 PowerShell 中复核：

   ```powershell
   Get-FileHash .\VirtualKeyboard-1.0.0-win-x64-framework-dependent.zip -Algorithm SHA256
   ```

4. 把 ZIP 解压到当前用户可读取的独立程序目录，不要直接在 ZIP 内运行。
5. 启动 `VirtualKeyboard.App.exe`。应用常驻通知区域；重复启动只会通知已有实例打开设置。

程序不要求管理员权限。Windows SmartScreen 可能因包未签名而提示；只应在可信内测渠道核对哈希后运行，不要通过“以管理员身份运行”规避提示。

## 2. 基本使用

应用会在可编辑目标获得焦点后自动分类、定位并显示键盘，不需要也不提供手动“捕获目标”步骤。点击目标输入框后即可点击悬浮键盘输入；正常就绪和发送成功不占用额外提示行，失败时只在标题栏显示固定提示，且不会暴露 PID、窗口句柄等调试信息。目标切换或身份校验失败时，本次输入会取消，重新聚焦输入框即可。

内置键盘按标准美式 QWERTY 主键区排列，包含完整标点键和左右成对的 Shift/Ctrl/Alt；缩短后的右 Shift、Up、Delete 与底行方向键共同形成对齐的倒 T。Shift、Ctrl、Alt、Win 第一次点击会发送 KeyDown 并显示明显蓝色高亮，第二次点击发送 KeyUp；目标变化、暂停、打开设置或退出也会自动释放。Shift 保持时，数字行和标点键会按当前 Windows 键盘布局输入对应符号。Fn 是本应用的功能层开关，不是可注入的硬件 Fn；Fn 高亮时数字行标签和输入切换为 F1-F12。Win 与 Fn 在密码目标中隐藏。

重要限制：自动焦点闭环已接入宿主并有自动测试，但尚未在要求的 Notepad、浏览器、VS Code 和跨 Windows 环境取得真人端到端证据，因此仍不能宣称兼容验收通过。

标题栏“×”只隐藏键盘，不退出程序。使用通知区域图标双击或菜单“显示当前键盘”恢复显示。

## 3. 托盘菜单

- 启用/暂停键盘：暂停会使当前目标和待处理输入失效。
- 显示当前键盘：按当前配置尺寸显示悬浮窗口。
- 设置：打开可激活的独立设置窗口；打开期间悬浮键盘隐藏且不会把设置输入框当成目标。
- 重新加载布局：重新读取内置和用户布局；选定布局缺失时回退内置 QWERTY。
- 退出：保存当前配置并有序释放输入、窗口、托盘和单实例资源。

## 4. 设置

设置文件位于 `%LocalAppData%\VirtualKeyboard\config.json`。可配置：

- 启用、自动显示、自动隐藏；
- 透明度 0.30–1.00；
- 宽度 620–2000 DIP、高度 280–1000 DIP、边距 0–128 DIP；
- 布局 ID、手动位置模式、详细诊断开关。

无效数值不会保存。磁盘或权限错误时窗口保持打开，有效编辑值保留在当前进程内存中。配置损坏时应用使用安全默认值，并尝试把原文件复制到 `%LocalAppData%\VirtualKeyboard\recovery`。

当前限制：配置已供自动焦点显示/隐藏、尺寸、透明度、边距和布局选择使用；手动位置持久模式及详细诊断仍缺产品级端到端证据，不能把“字段可保存”等同于全部运行时效果已验收。

悬浮键盘可从四条边或四个角直接拖动调整大小，松开后尺寸会自动保存。设置的自定义键表格最多添加 12 项：`text` 的“输入内容”是 Unicode 文本；`key` 填 `Enter`、`Delete`、`A` 等封闭键名；`hotkey` 在“主键”填 `C`、`S` 等，在修饰键列填 `Control+Shift` 这类组合。按键显示在键盘右侧独立可滚动列，密码输入目标中整列隐藏。自定义内容以明文保存在当前用户的 `config.json` 中，请勿填写密码或密钥。

## 5. 自定义布局

用户布局放在 `%LocalAppData%\VirtualKeyboard\layouts`，每个文件为 UTF-8 JSON。可参考程序目录的 `layouts\builtin\qwerty.en-US.json`。

布局只接受 `text`、`key`、`hotkey`、`modifier` 动作和封闭键名；脚本、命令、未知字段/动作、过深或过大的 JSON 会被拒绝。密码目标只允许标准安全键，短语、任意热键、scanCode 和 Ctrl/Alt 等动作会被隐藏或拒绝。

自定义布局可能包含私人短语。提交支持材料时不要附带用户布局，除非已经自行删除所有敏感文本。

## 6. 已知平台限制

- 仅计划支持 Windows 10 22H2 / Windows 11 x64；当前仓库尚无完整跨系统实机矩阵证据。
- 普通权限程序不能向管理员权限窗口注入输入，不会自动提权或绕过 UIPI。
- 不支持 UAC 安全桌面、锁屏或其他安全桌面输入。
- IME 候选窗、组合文本和第三方 UI Automation provider 行为因应用而异；Unicode `text`、物理 `key` 与 `hotkey` 保持独立路径。
- Chrome/Edge、VS Code、Office、多显示器、混合 DPI 和实体触摸仍需按验收矩阵实测；没有证据时均视为未证明。

## 7. 诊断材料与隐私

结构化日志位于 `%LocalAppData%\VirtualKeyboard\logs`，最多 5 个文件、每个最多 4 MiB。目录不可写时诊断会降级而不阻止应用；没有日志时不要为了“生成日志”反复输入敏感内容。

退出应用后，可只压缩日志文件：

```powershell
$logSource = Join-Path $env:LOCALAPPDATA 'VirtualKeyboard\logs'
Compress-Archive -Path (Join-Path $logSource '*.jsonl') -DestinationPath .\VirtualKeyboard-logs.zip
```

压缩前确认源路径确实是上述 `logs` 目录，不要把整个 `VirtualKeyboard` 用户数据目录打包。

不要发送以下内容：

- `config.json`、`layouts`、`recovery` 目录；
- 输入文本、密码、剪贴板内容、自定义短语；
- 含真实输入内容的截图或录屏。

可安全提供：应用版本、Windows 版本/DPI、目标应用及版本、数字错误码/ReasonCode、复现步骤和已脱敏截图。详细诊断开关不会授权收集输入内容。

## 8. 升级、卸载与回滚

- 升级：从托盘退出，备份旧程序目录，用新 ZIP 完整替换程序文件；保留 `%LocalAppData%\VirtualKeyboard`。
- 卸载：从托盘退出后删除程序目录。用户数据默认保留；确认不再需要后，可手动删除 `%LocalAppData%\VirtualKeyboard`。
- 回滚：退出后恢复上一版本完整程序目录。不要混用不同版本文件。1.0.0 使用 schema v1；未来版本如改变 schema，须先查阅迁移说明。

若退出后仍看到托盘图标，可把鼠标移过该位置触发通知区域刷新；若进程仍存在，请记录情况并提交缺陷，不要直接删除正在使用的程序目录。
