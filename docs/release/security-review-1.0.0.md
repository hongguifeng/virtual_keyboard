# Virtual Keyboard 1.0.0 发布安全检查

- 检查日期：2026-09-06
- 制品：`VirtualKeyboard-1.0.0-win-x64-framework-dependent.zip`
- SHA-256：`7c1bab9e85c34c2bd4e591decef602893cc7126a7f358b748f3ddf977cbc6373`
- 结论：**未通过发布门禁，仅限未签名内测**

## 已证明

- `scripts/verify-release.ps1` 复算 ZIP 哈希成功，并校验 ZIP 无绝对路径、`..` 路径、用户配置、日志、私钥或证书文件。
- 发布 EXE 嵌入 `requestedExecutionLevel level="asInvoker" uiAccess="false"` 和 PerMonitorV2 清单，不含 `requireAdministrator`、`highestAvailable` 或 `uiAccess=true`。
- 受控启动 3 秒时进程持续运行；启动前后发布目录全部文件 SHA-256 对比，变化数为 0。
- `Get-AuthenticodeSignature` 返回 `NotSigned`，校验脚本将制品分类为 `unsigned-internal-test-only`。

## 未证明/阻断项

- 本机 Windows Defender 报告 `AntivirusEnabled=False`、`RealTimeProtectionEnabled=False`，且无签名版本。`MpCmdRun -DisableRemediation` 虽返回退出码 0，但不能作为有效恶意软件扫描证据。
- 当前没有 Authenticode 代码签名证书，无法验证可信签名链和时间戳。

正式候选版发布前必须在已启用且病毒库更新的安全产品上重新扫描最终哈希对应制品；如仍无签名证书，发布评审必须明确批准“未签名内测包”范围，不能面向正式用户宣称已签名。
