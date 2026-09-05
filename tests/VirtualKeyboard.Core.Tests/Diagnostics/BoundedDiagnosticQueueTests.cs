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
}
