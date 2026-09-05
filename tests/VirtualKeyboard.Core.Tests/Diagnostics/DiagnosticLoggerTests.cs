namespace VirtualKeyboard.Core.Tests.Diagnostics;

using VirtualKeyboard.Core.Diagnostics;
using Xunit;

/// <summary>
/// T0.4 DiagnosticLogger 行为测试（NFR-REL-001 / 设计文档 16）：
/// 写入故障只降级、绝不向调用方传播；写入路径被单锁串行化（并发 Log 时 Write 最大并发为 1）。
/// </summary>
public class DiagnosticLoggerTests
{
    [Fact]
    public void ThrowingSink_LogDoesNotThrow_EnqueuePathUnaffected()
    {
        // sink 永远抛异常：
        //  - Log 不得把异常传播给调用方（NFR-REL-001：写入故障只降级，绝不影响入队与主流程）；
        //  - 事件仍然完成入队并经过队列路径到达 sink（Write 收到的事件字段与原样一致），
        //    证明"入队"语义不受 sink 故障影响（队列是 Write 的唯一入口）；
        //  - 队列本身未被损坏：后续 Log 正常继续。
        var sink = new ThrowingSink();
        using var logger = new DiagnosticLogger(queueCapacity: 8, sink: sink);

        var ex = Record.Exception(() => logger.Log(
            DiagnosticType.FocusObserved,
            DiagnosticModule.Focus,
            targetProcessId: 7,
            durationMs: 42));
        Assert.Null(ex);

        Assert.Equal(1, sink.WriteCallCount);
        Assert.NotNull(sink.LastEvent);
        Assert.Equal(7, sink.LastEvent!.TargetProcessId);
        Assert.Equal(42, sink.LastEvent.DurationMs);
        Assert.Equal(DiagnosticType.FocusObserved, sink.LastEvent.Type);

        // 队列未被 sink 故障破坏：第二次 Log 同样不抛，且事件继续被交付给 sink。
        var ex2 = Record.Exception(() => logger.Log(
            DiagnosticType.OverlayShown,
            DiagnosticModule.Overlay));
        Assert.Null(ex2);
        Assert.Equal(2, sink.WriteCallCount);
        Assert.Equal(DiagnosticType.OverlayShown, sink.LastEvent!.Type);
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
    }

    /// <summary>永远抛异常的 sink，同时记录到达的事件与调用次数。</summary>
    private sealed class ThrowingSink : IDiagnosticSink
    {
        private readonly object _gate = new();
        public int WriteCallCount
        {
            get
            {
                lock (_gate)
                {
                    return _writeCallCount;
                }
            }
        }

        public DiagnosticEvent? LastEvent
        {
            get
            {
                lock (_gate)
                {
                    return _last;
                }
            }
        }

        private int _writeCallCount;
        private DiagnosticEvent? _last;

        public bool CanWrite => true;
        public bool DetailedEnabled => true;

        public void Write(DiagnosticEvent e)
        {
            lock (_gate)
            {
                _writeCallCount++;
                _last = e;
            }

            throw new InvalidOperationException("synthetic sink failure");
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 探测 sink：单次 lock 内计数并记录当前并发；临界区内的 SpinWait 使重叠窗口可被观察到
    /// （若 Log 的写入锁被移除，多线程 Write 的重叠会令 MaxConcurrency > 1）。
    /// </summary>
    private sealed class ProbeSink : IDiagnosticSink
    {
        private readonly object _gate = new();
        private int _active;
        private int _maxActive;
        private int _writes;

        public int WriteCallCount
        {
            get
            {
                lock (_gate)
                {
                    return _writes;
                }
            }
        }

        public int MaxConcurrency
        {
            get
            {
                lock (_gate)
                {
                    return _maxActive;
                }
            }
        }

        public bool CanWrite => true;
        public bool DetailedEnabled => true;

        public void Write(DiagnosticEvent e)
        {
            lock (_gate)
            {
                _active++;
                if (_active > _maxActive)
                {
                    _maxActive = _active;
                }

                _writes++;
                Thread.SpinWait(2); // 加宽重叠检测窗口（不阻塞其他线程的锁外时间）
                _active--;
            }
        }

        public void Dispose()
        {
        }
    }
}
