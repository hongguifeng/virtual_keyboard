# Virtual Keyboard 1.0.0 AC-001 至 AC-015 验收表

- 填写日期：2026-09-06
- 自动测试基线：Core 206/206、Windows 154/154、Integration 24/24
- 状态定义：`部分` 仅表示组件自动证据存在；不代表场景实机通过。
- 总结：0 通过，12 部分，3 未执行；**不满足发布门禁**。

| AC | 环境与步骤 | 预期 | 实际/状态 | 证据 | 缺陷 |
|---|---|---|---|---|---|
| AC-001 Notepad 端到端 | Win10/11；聚焦 Notepad，自动显示并输入完整 QWERTY | 不抢焦点且输入正确 | 未执行；自动焦点宿主已接通但无真人证据 | Integration NoActivate/布局/自动焦点组件测试；兼容矩阵 | REL-001、002 |
| AC-002 非可编辑元素隐藏 | 在目标应用从编辑框切到 Button/只读控件 | 自动隐藏并清除目标 | 部分；分类器/状态机自动测试通过，宿主未接线 | `EditabilityClassifierTests`、`TargetStateCoordinatorTests` | REL-001、002 |
| AC-003 空白点击语义 | 编辑框后点击同应用空白区 | 按规格隐藏/清除 | 部分；状态规则自动覆盖，无真人 provider 证据 | Core 状态机测试 | REL-001、002 |
| AC-004 手动抑制 | 自动显示后关闭，再聚焦同一/不同目标 | 同目标保持抑制，目标变化恢复 | 部分；Core 抑制与 App 接线自动测试通过，无真人证据 | `TargetStateCoordinatorTests`、Integration | REL-001 |
| AC-005 浏览器输入框 | Chrome/Edge 地址栏、网页 text/password | 分类、定位、输入符合策略 | 未执行 | 环境已盘点，无交互证据 | REL-001、002 |
| AC-006 只读正文不误弹 | 浏览器/应用只读正文和禁用控件 | 不显示 | 部分；分类证据规则自动测试通过 | `EditabilityClassifierTests` | REL-001、002 |
| AC-007 VS Code 定位 | 编辑器、搜索框、命令面板 | 使用 caret/control/window 降级且可见 | 未执行 | VS Code 1.136.1 已安装，无实测 | REL-001、002 |
| AC-008 多显示器与 DPI | 100%–200%、负坐标、混合 DPI、任务栏四边 | 窗口可见且物理矩形正确 | 部分；算法/消息自动测试通过，只有 100% 单屏盘点 | DPI 矩阵；Core/Windows 几何测试 | REL-003 |
| AC-009 目标切换防误输入 | 排队后快速切换窗口/控件 | 旧动作取消，零误投 | 部分；Session/前台/RuntimeId 与队列自动测试通过，无真人压力 | Windows/Core 输入测试 | REL-004 |
| AC-010 权限边界 | 普通 App → 管理员 TestHost、UAC | 不提权、不绕过、明确诊断 | 部分；UIPI 诊断分支自动覆盖，管理员实测未执行 | `InputFailureFeedbackTests` | REL-004 |
| AC-011 修饰键清理 | 热键各前缀失败、取消、退出 | 无合成键卡住 | 部分；安全闩锁自动测试通过，无真人键盘状态证据 | `HotkeyInputSenderTests` | REL-004 |
| AC-012 Unicode | 向真实目标输入 BMP/代理对 | 不截断且顺序正确 | 部分；INPUT 数组自动测试通过，真实应用未执行 | `UnicodeTextInputSenderTests` | REL-002 |
| AC-013 密码字段 | TestHost/浏览器密码框 | 只显示安全键且日志无内容 | 部分；分类/策略自动测试通过，无端到端日志审计 | `PasswordActionPolicyTests`、分类测试 | REL-004、006 |
| AC-014 配置恢复 | 损坏 config 后启动、修改设置后重启 | 默认启动并备份损坏文件 | 部分；Repository 自动恢复/往返通过，进程级手测未执行 | `ConfigurationRepositoryTests` | REL-002 |
| AC-015 单实例与退出 | 启动两个进程，触发输入后退出 | 单监听器、无托盘/UIA/修饰键残留 | 部分；同进程命名对象和 Dispose 自动测试通过，双进程/系统托盘实测未执行 | Integration 单实例/退出测试 | REL-002、004 |

## 结论

该表已完整记录环境、步骤、预期、实际、证据和缺陷，因此 T8.3 的“完成验收表”文档工作完成；但没有任何 AC 获得完整实机通过证据。不得把“部分”汇总为通过，也不得批准发布。
