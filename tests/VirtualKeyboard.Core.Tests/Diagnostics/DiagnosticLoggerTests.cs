namespace VirtualKeyboard.Core.Tests.Diagnostics;

using VirtualKeyboard.Core.Diagnostics;
using Xunit;

/// <summary>
/// T0.4 DiagnosticLogger 行为测试（NFR-REL-001 / 设计文档 16）：
/// 汇聚点写入故障只降级、绝不向调用方传播，且不影响事件入有界队列；
/// 写入路径被单锁串行化（并发 Log 时 sink.Write 最大并发为 1）。
/// </summary>
public class DiagnosticLoggerTests
{
    [Fact]
    public void ThrowingSink_LogDoesNotThrow_EventStaysInQueue()
    {
        // sink 永远抛异常。产品语义（DiagnosticLogger.Log）：
        //  - 事件先 TryAdd 入有界队列；
        //  - sink 不是队列的消费者——Log 在独立锁内把同一事件对象直接交给 sink.Write；
        //  - sink 抛异常只降级，绝不影响入队与主流程（NFR-REL-001 / 设计文档 16）。
        // 断言方式：每次 Log 后从 logger 自己的队列经 TryReadNext 真实读回事件并逐项核对，
        // 证明 sink 异常未影响入队；同时核对该事件与 sink 实际收到的对象是同一个。
        var sink = new ThrowingSink();
        using var logger = new DiagnosticLogger(queueCapacity: 8, sink: sink);

        Assert.Null(Record.Exception(() => logger.Log(
            DiagnosticType.FocusObserved,
            DiagnosticModule.Focus,
            targetProcessId: 7,
            durationMs: 42)));

        // 入队不受 sink 故障影响：事件在队列中且字段原样。
        Assert.Equal(1, logger.PendingCount);
        Assert.True(logger.TryReadNext(out var first));
        Assert.NotNull(first);
        Assert.Equal(DiagnosticType.FocusObserved, first!.Type);
        Assert.Equal(DiagnosticModule.Focus, first.Module);
        Assert.Equal(7, first.TargetProcessId);
        Assert.Equal(42, first.DurationMs);
        Assert.Equal(1, sink.WriteCallCount);
        Assert.Equal(first.EventId, sink.LastEvent!.EventId); // sink 收到的是同一事件（随后抛出）。

        // 第二次 Log：同样不抛，事件继续入队且可读回，队列保持可用。
        Assert.Null(Record.Exception(() => logger.Log(
            DiagnosticType.OverlayShown,
            DiagnosticModule.Overlay,
            targetProcessId: 3,
            durationMs: 5)));
        Assert.Equal(1, logger.PendingCount);
        Assert.True(logger.TryReadNext(out var second));
        Assert.Equal(DiagnosticType.OverlayShown, second!.Type);
        Assert.Equal(3, second.TargetProcessId);
        Assert.Equal(5, second.DurationMs);
        Assert.Equal(2, sink.WriteCallCount);

        Assert.False(logger.TryReadNext(out _)); // 队列已读尽，无残留。
    }

    [Fact]
    public void ConcurrentLog_SinkWriteIsSerialized_CallCountComplete()
    {
        const int threads = 4;
        const int perThread = 250;
        const int total = threads * perThread;

        var sink = new ProbeSink();
        using var logger = new DiagnosticLogger(queueCapacity: 20_000, sink: sink);

        var barrier = new ManualResetEventSlim(false);
        var errors = new List<Exception>();
        var errorGate = new object();
        var workers = new List<Thread>();

        for (var i = 0; i < threads; i++)
        {
            var idx = i;
            workers.Add(new Thread(() =>
            {
                barrier.Wait();
                try
                {
                    for (var k = 0; k < perThread; k++)
                    {
                        // 每个线程独立的 (type, module) 组合，全部是封闭枚举。
                        logger.Log(
                            idx % 2 == 0 ? DiagnosticType.OverlayShown : DiagnosticType.OverlayHidden,
                            idx % 2 == 0 ? DiagnosticModule.Overlay : DiagnosticModule.Core,
                            targetProcessId: idx,
                            durationMs: k);
                    }
                }
                catch (Exception e)
                {
                    lock (errorGate)
                    {
                        errors.Add(e);
                    }
                }
            }));
        }

        foreach (var t in workers)
        {
            t.Start();
        }

        barrier.Set();
        foreach (var t in workers)
        {
            t.Join();
        }

        Assert.Empty(errors);
        Assert.Equal(total, sink.WriteCallCount);
        Assert.True(sink.MaxConcurrency <= 1, $"sink.Write 最大并发 = {sink.MaxConcurrency}（期望 <= 1）");

        // 入队路径同样不受并发写入影响：1000 条全部在队列中，无丢弃、无异常。
        Assert.Equal(total, logger.PendingCount);
        Assert.Equal(0L, logger.DroppedCount);
    }

    /// <summary>
    /// 永远抛异常的 sink：用 Interlocked/Volatile 记录收到的事件与调用次数
    /// （本测试中仅被 Log 的写入锁内串行调用、无竞争，但统一采用线程安全原语以免将来改动破坏测试）。
    /// </summary>
    private sealed class ThrowingSink : IDiagnosticSink
    {
        private int _writeCallCount;
        private DiagnosticEvent? _lastEvent;

        public int WriteCallCount => Volatile.Read(ref _writeCallCount);

        public DiagnosticEvent? LastEvent => Volatile.Read(ref _lastEvent);

        public bool CanWrite => true;

        public bool DetailedEnabled => true;

        public void Write(DiagnosticEvent e)
        {
            Interlocked.Increment(ref _writeCallCount);
            Volatile.Write(ref _lastEvent, e);
            throw new InvalidOperationException("synthetic sink failure");
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 并发探针 sink：自身不带任何锁——活跃数 / 历史最大并发 / 总调用数全部用
    /// Interlocked/Volatile（最大并发用 CAS 更新）。有界 SpinWait 加宽重叠观察窗：
    /// 若 DiagnosticLogger 的写入锁被移除，多线程 Write 会相互重叠，
    /// MaxConcurrency 将 > 1，测试即可稳定失败。
    /// </summary>
    private sealed class ProbeSink : IDiagnosticSink
    {
        private const int SpinRounds = 64;

        private int _active;
        private int _maxActive;
        private int _writeCallCount;
        private DiagnosticEvent? _lastWritten;

        public int WriteCallCount => Volatile.Read(ref _writeCallCount);

        public int MaxConcurrency => Volatile.Read(ref _maxActive);

        /// <summary>最后一次写入的事件（供事后检查）。</summary>
        public DiagnosticEvent? LastWritten => Volatile.Read(ref _lastWritten);

        public bool CanWrite => true;

        public bool DetailedEnabled => true;

        public void Write(DiagnosticEvent e)
        {
            Interlocked.Increment(ref _active);

            // 有界自旋（无锁、不睡眠）：只要与任何其他 Write 重叠，_active 会瞬时 >= 2，
            // 随后被 CAS 提升进 _maxActive。
            for (var i = 0; i < SpinRounds; i++)
            {
                Thread.SpinWait(1);
            }

            var now = Volatile.Read(ref _active);
            while (true)
            {
                var max = Volatile.Read(ref _maxActive);
                if (now <= max)
                {
                    break;
                }

                if (Interlocked.CompareExchange(ref _maxActive, now, max) == max)
                {
                    break;
                }
            }

            Interlocked.Decrement(ref _active);
            Interlocked.Increment(ref _writeCallCount);
            Volatile.Write(ref _lastWritten, e);
        }

        public void Dispose()
        {
        }
    }
}
