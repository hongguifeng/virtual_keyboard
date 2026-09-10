# REL-030：UIA 阻塞恢复验证

日期：2026-09-10。本地 1.0.10 修复构建。

## 故障与范围

原键盘进程仍响应窗口消息，但 22:04:18（北京时间）后分类日志停止。多次 `dotnet-stack report` 显示 UIA 观察线程位于 FocusedElementResolver 的原生调用链，另一 UIA 事件线程位于 AutomationElement.FromHandle。同期独立 TestHost 8 秒完成 29 条采样、退出码 0；重启原应用后查询恢复。证据定位到进程内 UIA 查询卡滞，未确定具体 provider 或原生锁；用户确认本轮没有更换显示器。

原实现轮询和重试都在同一 MTA 线程，不能中断尚未返回的 COM 查询。修复将观察/分类放入独立工作进程，父进程根据完成采样的进度判断健康，而不是仅检查进程是否存活。

## 行为与安全边界

- 无采样进展 1 秒后，发送前校验拒绝旧目标；5 秒后超时终止工作进程并清除目标。启动首帧 10 秒、后续管道空闲 2 秒也有截止时间。
- 一次只保留一个受管工作进程，确认旧进程退出后才启动新进程。最多 3 次重启，间隔 1/2/4 秒；连续健康 30 秒重置预算。达到上限后须重新启动应用，不无限积累线程或子进程。
- 事件版本变化清除最新身份；故障代际立即作废，重启后的焦点版本由父进程递增。Dispatcher 应用前再次校验通知，旧结果不能恢复旧目标。
- 协议只传允许的元数据，最大 8192 字符/帧、64 整数 RuntimeId。RuntimeId 不进入日志；不读取用户输入、Name、Value、密码或剪贴板。
- 父进程管道关闭使子进程退出，包含异常父进程退出。后台进程不创建键盘/设置/托盘窗口，不参与用户应用的单实例通知。
- 输入发送仍验证前台 PID/HWND、焦点 HWND、RuntimeId 及当前 TargetSession；没有新增发送回退或激活窗口操作。

## 默认诊断事件

| 事件 | ErrorCode 的含义 |
| --- | --- |
| FocusWorkerStarted | 当前连续失败次数；TargetProcessId 是新工作进程 PID |
| FocusWorkerHeartbeat | 每 30 秒一次；阶段 0=启动，1=等待/防抖，2=采集，3=分类 |
| FocusWorkerStalled | 截止时最后已知阶段；不是 Win32 错误码 |
| FocusWorkerExited | 异常 HResult 低 16 位；终止边界失败时为原生错误码 |
| FocusWorkerRestarting / Recovered / Exhausted | 连续失败次数 |

通信中每 100 ms 的心跳不会全部写盘。采样进度使用工作进程的 Windows QPC 时间戳，迟到的数据不能把过期目标变成新鲜目标。

## 自动验证

- `scripts/build.ps1 -SkipPackage`：Release 零警告/错误；Core 245、Windows 247、Integration 64，共 556 项通过。
- `VirtualKeyboard.TestHost.exe --selftest`：退出码 0。
- `dotnet publish src/VirtualKeyboard.App -c Release -r win-x64 --self-contained false -p:Version=1.0.10 -o artifacts/isolated-validation --nologo`：通过。
- 独立 TestHost fixture 模拟“仍发送心跳但采样冻结”，验证超时杀进程、旧结果失效、第二个进程恢复及父端单调版本；另验证连续退出最多启动 4 次（首次 + 3 次重试）。
- 补充验证排队旧进度不能建立输入目标，以及事件版本变化但尚无新分类时，已发布通知立即失效。
- 生产 `--focus-worker` 入口验证：无窗口、产生真实 UIA 进度；关闭父端 stdin 后 5 秒内退出。协议测试覆盖超长帧、非法元数据、RuntimeId/负坐标/密码标志往返；健康校验测试覆盖旧目标拒绝。

## 本机运行证据

修复构建运行后可见键盘再次出现。以下日志时间为 UTC；原始日志、桌面截图不入库：

- 14:24:15.397：只终止父进程拥有的检测子进程，保留键盘主进程。
- 14:24:15.413：FocusWorkerExited；14:24:15.420：FocusWorkerRestarting。
- 14:24:16.438：新 FocusWorkerStarted；14:24:18.984：FocusWorkerRecovered。
- 14:24:27.637：当前输入框 Edit / Editable / ValuePattern，随后 TargetSessionCreated、OverlayShown；14:24:48.776 再次健康心跳。

这是受控子进程退出与模拟阻塞恢复证据，不宣称已复现原 provider 内部的同一 COM 故障。未注入用户文字、未修改被测文档。长期稳定性、独立主审查及全部软件/显示器矩阵仍待验证。
