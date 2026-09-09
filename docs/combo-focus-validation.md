# REL-029：可编辑 ComboBox 识别验证

日期：2026-09-09。范围：FR-FOC-003、FR-FOC-004；本地 1.0.9 验证构建。

## 故障证据

在用户现有豆包浏览器两次点击 GitHub 全局搜索，UIA 报告 ControlType=50003（ComboBox）、已启用、持有键盘焦点、未离屏，且同时存在可写 ValuePattern 和 TextPattern。原代码把 ComboBox 映射为 Other，拒绝其 ValuePattern 正向证据，最后返回 Unknown/TextPatternOnly。同页普通仓库筛选输入框是 Edit（50004），能正常显示键盘。

本地 focus-probe 采样 422 行，无丢弃、无写入错误、工作线程正常停止。原始日志不入库；未采集 Name、Value、用户文字或密码。TextPatternOnly 原因码不表示 ValuePattern 一定不存在，也可能是其类型条件不满足。

## 修复边界

Windows 层保留 ComboBox 类型；Core 仅在身份、焦点、启用、离屏、只读等安全门通过后，以可写 ValuePattern 与 TextPattern 的组合接受 ComboBox。仅有其中一种证据仍不足，Other 不提升。现有诊断字段直接记录 ComboBox / ValuePattern，不需要增加文本日志。未改变目标会话校验、NoActivate、输入发送、DPI 或重试策略。

## 现场步骤与结果

普通权限运行本地发布输出，使用 computer-use 点击现有界面，不输入文字或提交搜索，不编辑被测文件。以下时间均为 UTC：

| 操作 | 可见结果与诊断 |
| --- | --- |
| 15:09:06 点击 GitHub 全局搜索输入区 | 键盘出现；ComboBox / Editable / ValuePattern，UsedFallback=false、UsedEventTarget=false，随后 OverlayShown |
| Escape 退出搜索 | 键盘隐藏 |
| 点击左侧普通仓库筛选输入框 | 键盘出现 |
| 15:11:46 在现有 LLM Proxy 页面打开语言下拉框 | 键盘不出现；ComboBox / NotEditable / NoEditableEvidence；Escape 关闭，未改变选项 |
| 15:14:02 切回 VS Code，点击已有查找框 | 键盘出现；Edit / Editable / ValuePattern，UsedEventTarget=true，随后 OverlayShown |

## 自动验证

- `scripts/build.ps1 -SkipPackage`：Release 零警告/错误；Core 245、Windows 238、Integration 59，共 542 项通过。
- `VirtualKeyboard.TestHost.exe --selftest`：退出码 0。
- `dotnet publish src/VirtualKeyboard.App -c Release -r win-x64 --self-contained false -p:Version=1.0.9 -o artifacts/combo-validation --nologo`：通过。
- 新测试覆盖 ComboBox 组合证据、单一/无证据、只读、禁用、失焦、离屏、Other 不提升及 UIA 类型映射。

此证据仅覆盖本机当前 provider 和所列控件，不能保证所有网页或所有下拉框实现。错误暴露 Pattern 的 provider 仍可能误判；缺失组合证据的自定义输入框仍可能不触发。长期稳定性和独立主审查待完成；本地验证构建未发布为 GitHub Release。
