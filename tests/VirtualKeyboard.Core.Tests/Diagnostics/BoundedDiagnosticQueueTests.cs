namespace VirtualKeyboard.Core.Tests.Diagnostics;

using VirtualKeyboard.Core.Diagnostics;
using Xunit;

/// <summary>有界诊断队列行为测试（NFR-PERF-001）。</summary>
public class BoundedDiagnosticQueueTests
{
    private static readonly long[] NewestSequences = [6L, 7L, 8L, 9L];

    private static DiagnosticEvent E(long seq) =>
        new()
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            EventId = Guid.NewGuid(),
            Type = DiagnosticType.OverlayShown,
            Module = DiagnosticModule.Overlay,
            Sequence = seq,
        };

    [Fact]
    public void QueueIsBounded_DropsOldestAndCounts()
    {
        var q = new BoundedDiagnosticQueue(capacity: 4);
        Assert.Equal(4, q.Capacity);

        for (var i = 0; i < 10; i++)
        {
            q.TryAdd(E(i));
        }

        Assert.Equal(4, q.Count);            // 恒定容量
        Assert.Equal(6, q.DroppedCount);     // 10 - 4 次丢弃
        var seen = new List<long>();
        while (q.TryDequeue(out var e))
        {
            seen.Add(e!.Sequence);
        }

        Assert.Equal(NewestSequences, seen); // 保留最新 4 条
    }

    [Fact]
    public void Drain_ReturnsUpToMax()
    {
        var q = new BoundedDiagnosticQueue(capacity: 10);
        q.TryAdd(E(1));
        q.TryAdd(E(2));
        q.TryAdd(E(3));

        var buf = new List<DiagnosticEvent>();
        var n = q.Drain(buf, 2);
        Assert.Equal(2, n);
        Assert.Equal(1, buf[0].Sequence);
        Assert.Equal(2, buf[1].Sequence);
        Assert.Equal(1, q.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5000)]
    public void CapacityIsClamped(int requested)
    {
        var q = new BoundedDiagnosticQueue(capacity: requested);
        if (requested <= 0)
        {
            Assert.Equal(1, q.Capacity);
        }
        else
        {
            Assert.Equal(BoundedDiagnosticQueue.MaxCapacity, q.Capacity);
        }
    }

    [Fact]
    public void MultiProducer_BoundsCountAndConserves()
    {
        const int capacity = 64;
        const int producers = 4;
        const int perProducer = 20_000;

        var q = new BoundedDiagnosticQueue(capacity: capacity);
        var maxSeen = 0;

        // 监视线程：并发生产期间持续采样 Count，记录观测到的峰值。
        var running = true;
        var monitor = new Thread(() =>
        {
            while (Volatile.Read(ref running))
            {
                var c = q.Count;
                if (c > Volatile.Read(ref maxSeen))
                {
                    Volatile.Write(ref maxSeen, c);
                }
            }
        }) { IsBackground = true };

        var threads = new List<Thread>();
        var start = new ManualResetEventSlim(false);
        for (var p = 0; p < producers; p++)
        {
            var pid = p;
            threads.Add(new Thread(() =>
            {
                start.Wait();
                for (var i = 0; i < perProducer; i++)
                {
                    q.TryAdd(E(pid * 100_000L + i));
                }
            }));
        }

        foreach (var t in threads)
        {
            t.Start();
        }

        monitor.Start();
        start.Set();

        foreach (var t in threads)
        {
            t.Join();
        }

        Volatile.Write(ref running, false);
        monitor.Join();

        // 有界：并发生产期间 Count 任何时刻都不超过容量。
        Assert.True(maxSeen <= capacity, $"Count 峰值 {maxSeen} 超过容量 {capacity}。");
        Assert.Equal(capacity, q.Count);   // 远大于容量后恰好装满

        // 守恒：added = dequeued + dropped。
        const long added = producers * perProducer;
        var buf = new List<DiagnosticEvent>();
        var dequeued = 0;
        while (q.Drain(buf, 1000) > 0)
        {
            dequeued += buf.Count;
            buf.Clear();
        }

        Assert.Equal(0, q.Count);
        Assert.Equal(added, (long)dequeued + q.DroppedCount);
    }

    [Fact]
    public void ConcurrentProduceConsume_NoLossNoDuplicatesAndConserves()
    {
        const int capacity = 16;
        const int producers = 4;
        const int consumers = 3;
        const int perProducer = 10_000;

        var q = new BoundedDiagnosticQueue(capacity: capacity);
        var consumerLists = new List<DiagnosticEvent>[consumers];
        for (var c = 0; c < consumers; c++)
        {
            consumerLists[c] = new List<DiagnosticEvent>();
        }

        var running = true;
        var start = new ManualResetEventSlim(false);

        var producerThreads = new List<Thread>();
        for (var p = 0; p < producers; p++)
        {
            var pid = p;
            producerThreads.Add(new Thread(() =>
            {
                start.Wait();
                for (var i = 0; i < perProducer; i++)
                {
                    q.TryAdd(E(pid * 100_000L + i));
                }
            }));
        }

        var consumerThreads = new List<Thread>();
        for (var c = 0; c < consumers; c++)
        {
            var list = consumerLists[c];
            consumerThreads.Add(new Thread(() =>
            {
                start.Wait();
                var buf = new List<DiagnosticEvent>();
                while (true)
                {
                    if (q.Drain(buf, 256) > 0)
                    {
                        list.AddRange(buf);
                        buf.Clear();
                    }

                    // 生产者全部结束且队列已空时退出。
                    if (!Volatile.Read(ref running) && q.Count == 0)
                    {
                        break;
                    }
                }
            }));
        }

        foreach (var t in producerThreads)
        {
            t.Start();
        }

        foreach (var t in consumerThreads)
        {
            t.Start();
        }

        start.Set();

        foreach (var t in producerThreads)
        {
            t.Join();
        }

        Volatile.Write(ref running, false);

        foreach (var t in consumerThreads)
        {
            t.Join();
        }

        // 兜底：所有消费者退出后队列必为空；若有剩余则全部取出。
        var remaining = new List<DiagnosticEvent>();
        while (q.Drain(remaining, 1000) > 0)
        {
        }

        var all = new List<DiagnosticEvent>(producers * perProducer);
        foreach (var list in consumerLists)
        {
            all.AddRange(list);
        }

        all.AddRange(remaining);

        const long added = producers * perProducer;

        // 守恒：除显式丢弃外不丢失任何事件（added = dequeued + dropped）。
        Assert.Equal(added, (long)all.Count + q.DroppedCount);
        Assert.Equal(0, q.Count);

        // 无损坏、不重复：每个取出事件的 EventId 唯一（Drain 原子性保证同一事件至多被取一次）。
        var seen = new HashSet<Guid>();
        foreach (var e in all)
        {
            Assert.True(seen.Add(e.EventId), "并发取出出现重复 EventId。");
        }
    }
}
