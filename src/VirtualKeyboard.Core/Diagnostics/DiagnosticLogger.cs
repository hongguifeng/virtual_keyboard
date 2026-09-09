namespace VirtualKeyboard.Core.Diagnostics;

/// <summary>
/// 诊断入口（T0.4 骨架）：各模块上报结构化事件的唯一 API。
/// 事件先入有界队列，再（如配置）写入本地汇聚点；无网络路径。
/// 所有参数均为封闭枚举/数字/版本号——类型上无法传入 InputAction 文本、
/// 密码 Value、剪贴板内容、自定义短语或 AutomationElement Name/Value。
/// </summary>
public sealed class DiagnosticLogger : IDisposable
{
    private readonly BoundedDiagnosticQueue _queue;
    private readonly IDiagnosticSink? _sink;
    private readonly object _sinkGate = new();
    private long _sequence;
    private bool _disposed;

    public DiagnosticLogger(int queueCapacity = 256, IDiagnosticSink? sink = null)
    {
        _queue = new(queueCapacity);
        _sink = sink;
    }

    public int PendingCount => _queue.Count;

    public long DroppedCount => _queue.DroppedCount;

    /// <summary>
    /// 上报一条结构化事件：线程安全入有界队列，并（如配置）写入本地汇聚点。
    /// 汇聚点写入在独立锁内进行且吞掉所有异常——写入故障只降级，绝不影响入队或主流程（设计文档 16）。
    /// </summary>
    public void Log(
        DiagnosticType type,
        DiagnosticModule module,
        DiagnosticLevel level = DiagnosticLevel.Info,
        int targetProcessId = -1,
        ControlKind? controlKind = null,
        Verdict? verdict = null,
        ReasonCode? reason = null,
        int errorCode = 0,
        long durationMs = 0,
        int requestedCount = 0,
        int completedCount = 0,
        AppVersion? appVersion = null,
        long? focusVersion = null, int? retryAttempt = null, bool? usedFallback = null)
    {
        var e = new DiagnosticEvent
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            EventId = Guid.NewGuid(),
            Type = type,
            Module = module,
            Level = level,
            TargetProcessId = targetProcessId,
            ControlKind = controlKind,
            Verdict = verdict,
            Reason = reason,
            ErrorCode = errorCode,
            DurationMs = durationMs,
            RequestedCount = requestedCount,
            CompletedCount = completedCount,
            Sequence = Interlocked.Increment(ref _sequence),
            AppVersion = appVersion,
            FocusVersion = focusVersion,
            RetryAttempt = retryAttempt,
            UsedFallback = usedFallback,
        };

        _queue.TryAdd(e);

        if (_sink is not null)
        {
            lock (_sinkGate)
            {
                try
                {
                    _sink.Write(e);
                }
                catch
                {
                    // 汇聚点故障：仅降级，不影响事件入队与主流程。
                }
            }
        }
    }

    public bool TryReadNext(out DiagnosticEvent? e) => _queue.TryDequeue(out e);

    /// <summary>
    /// 释放对事件队列的引用（纯托管资源，无句柄/非托管资源）。
    /// 不在此处处置外部传入的 <see cref="_sink"/>（生命周期归调用方）。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
