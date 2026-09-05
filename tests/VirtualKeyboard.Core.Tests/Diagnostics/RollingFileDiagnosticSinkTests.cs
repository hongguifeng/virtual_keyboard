namespace VirtualKeyboard.Core.Tests.Diagnostics;

using System.Text;
using System.Text.Json;
using VirtualKeyboard.Core.Diagnostics;
using Xunit;

/// <summary>
/// T0.4 滚动文件汇聚点测试（FR-DIA-003 / NFR-PRI-001 / NFR-PERF-001 / 设计文档 15.3/16）：
/// 文件数与总大小有确定上界（单文件 ≤ 上限，无宽限）；详细事件默认关闭；不可写目录降级为 no-op；
/// Logger 集成；并发写入完整 JSONL/Sequence 唯一性；并发与连续滚动的严格总量上界。
/// </summary>
public class RollingFileDiagnosticSinkTests
{
    [Fact]
    public void Rotation_KeepsFileCountAndTotalSizeBounded()
    {
        var dir = DiagnosticPrivacyTests.CreateTempDir();
        try
        {
            const int maxFiles = 3;
            var sink = new RollingFileDiagnosticSink(dir, maxFileBytes: 1024, maxFileCount: maxFiles);
            Assert.True(sink.CanWrite);

            for (var i = 0; i < 30; i++)
            {
                sink.Write(new DiagnosticEvent
                {
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    EventId = Guid.NewGuid(),
                    Type = DiagnosticType.FocusObserved,
                    Module = DiagnosticModule.Focus,
                    TargetProcessId = 100 + i,
                    AppVersion = new AppVersion(0, 0, 0),
                    Sequence = i,
                });
            }

            var files = Directory.GetFiles(dir);
            Assert.True(files.Length <= maxFiles, $"文件数 {files.Length} 超出上限 {maxFiles}");

            long total = 0;
            foreach (var f in files)
            {
                var size = new FileInfo(f).Length;
                // 单文件（含活跃文件）也不允许超过单文件上限。
                Assert.True(size <= 1024, $"{System.IO.Path.GetFileName(f)} 大小 {size} 超过单文件上限 1024");
                total += size;
            }

            // 总量必须严格落在 文件数 × 单文件上限 的确定性上界之内（无宽限）。
            Assert.True(total <= (long)maxFiles * 1024, $"总量 {total} 字节超出上界");

            // 所有生成 JSONL 不得以 UTF-8 BOM（EF BB BF）开头，否则 GetByteCount 计量失真。
            AssertNoUtf8BomInAllFiles(dir);
        }
        finally
        {
            DiagnosticPrivacyTests.DeleteDir(dir);
        }
    }

    [Fact]
    public void DetailedDisabled_DropsDetailedEvents()
    {
        var dir = DiagnosticPrivacyTests.CreateTempDir();
        try
        {
            var sink = new RollingFileDiagnosticSink(dir, detailedEnabled: false);
            sink.Write(MakeEvent(DiagnosticLevel.Detailed));
            Assert.False(HasContent(dir));
        }
        finally
        {
            DiagnosticPrivacyTests.DeleteDir(dir);
        }
    }

    [Fact]
    public void NonWritableRoot_DegradesToNoOp()
    {
        var dir = DiagnosticPrivacyTests.CreateTempDir();
        try
        {
            var blocker = System.IO.Path.Combine(dir, "blocker");
            File.WriteAllText(blocker, "x");

            var sink = new RollingFileDiagnosticSink(blocker);
            Assert.False(sink.CanWrite);
            sink.Write(MakeEvent(DiagnosticLevel.Info)); // 不抛
            Assert.False(sink.CanWrite);
        }
        finally
        {
            DiagnosticPrivacyTests.DeleteDir(dir);
        }
    }

    [Fact]
    public void Logger_WithoutSink_DoesNotThrowAndTracksQueue()
    {
        using var logger = new DiagnosticLogger(queueCapacity: 4);
        Assert.Equal(0, logger.PendingCount);
        logger.Log(DiagnosticType.FocusObserved, DiagnosticModule.Focus);
        logger.Log(DiagnosticType.ClassificationCompleted, DiagnosticModule.Classification, targetProcessId: 1);
        Assert.Equal(2, logger.PendingCount);
        Assert.True(logger.TryReadNext(out var e));
        Assert.NotNull(e);
        Assert.Equal(DiagnosticType.FocusObserved, e!.Type);
    }

    [Fact]
    public void Logger_WithSink_WritesEventFile()
    {
        var dir = DiagnosticPrivacyTests.CreateTempDir();
        try
        {
            var sink = new RollingFileDiagnosticSink(dir);
            using var logger = new DiagnosticLogger(queueCapacity: 8, sink: sink);
            logger.Log(
                DiagnosticType.InputBatchStarted,
                DiagnosticModule.Input,
                targetProcessId: 7,
                durationMs: 42,
                appVersion: new AppVersion(0, 0, 0));

            var file = System.IO.Path.Combine(dir, "diagnostic.0.jsonl");
            Assert.True(File.Exists(file));
            var content = File.ReadAllText(file);
            Assert.Contains("InputBatchStarted", content);
            Assert.Contains("\"TargetProcessId\":7", content);

            // 写入文件不得以 UTF-8 BOM 开头。
            AssertNoUtf8BomInAllFiles(dir);
        }
        finally
        {
            DiagnosticPrivacyTests.DeleteDir(dir);
        }
    }

    [Fact]
    public void ConcurrentWrites_NoRotation_ExactCompleteParseableJsonl_UniqueSequences()
    {
        // 单文件上限 1MB ≫ 总量（400 × 每行数百字节），全程不发生轮转：
        // 断言恰好 400 条完整 JSONL、全部可解析、Sequence 无重复无丢失。
        var dir = DiagnosticPrivacyTests.CreateTempDir();
        try
        {
            var sink = new RollingFileDiagnosticSink(dir, maxFileBytes: 1_048_576, maxFileCount: 5);
            Assert.True(sink.CanWrite);

            const int threads = 4;
            const int perThread = 100;
            var start = new ManualResetEventSlim(false);
            var workers = new Thread[threads];
            for (var t = 0; t < threads; t++)
            {
                var tid = t;
                workers[t] = new Thread(() =>
                {
                    start.Wait();
                    for (var i = 0; i < perThread; i++)
                    {
                        sink.Write(new DiagnosticEvent
                        {
                            OccurredAtUtc = DateTimeOffset.UtcNow,
                            EventId = Guid.NewGuid(),
                            Type = DiagnosticType.FocusObserved,
                            Module = DiagnosticModule.Focus,
                            Sequence = tid * 1000 + i,
                        });
                    }
                });
            }

            foreach (var worker in workers)
            {
                worker.Start();
            }

            start.Set();

            foreach (var worker in workers)
            {
                worker.Join();
            }

            var files = Directory.GetFiles(dir);
            Assert.Single(files);
            Assert.Equal("diagnostic.0.jsonl", System.IO.Path.GetFileName(files[0]));

            var lines = File.ReadAllLines(files[0], Encoding.UTF8);
            Assert.Equal(threads * perThread, lines.Length);

            var seen = new HashSet<long>();
            foreach (var line in lines)
            {
                // 每行必须是一条完整、可解析的 JSON 对象（无撕裂/半行）。
                using var doc = JsonDocument.Parse(line);
                Assert.Equal("FocusObserved", doc.RootElement.GetProperty("Type").GetString());
                var seq = doc.RootElement.GetProperty("Sequence").GetInt64();
                Assert.True(seen.Add(seq), $"Sequence {seq} 重复或损坏");
            }

            // “恰好全部”：每个 (t,i) 的 Sequence 各出现一次。
            for (var t = 0; t < threads; t++)
            {
                for (var i = 0; i < perThread; i++)
                {
                    Assert.Contains(t * 1000 + i, seen);
                }
            }

            // 单文件不得以 UTF-8 BOM 开头。
            AssertNoUtf8BomInAllFiles(dir);
        }
        finally
        {
            DiagnosticPrivacyTests.DeleteDir(dir);
        }
    }

    [Fact]
    public void SequentialRolling_SingleFileNeverExceedsMax_TotalStrictlyBounded()
    {
        // 连续滚动（单线程）：每个文件（含活跃文件）≤ 单文件上限，文件数 ≤ 上限，
        // 总量严格 ≤ maxFileBytes × maxFileCount（无宽限）；且活跃文件恒为 index 0。
        var dir = DiagnosticPrivacyTests.CreateTempDir();
        try
        {
            const long maxFileBytes = 800;
            const int maxFiles = 5;
            var sink = new RollingFileDiagnosticSink(dir, maxFileBytes: maxFileBytes, maxFileCount: maxFiles);
            Assert.True(sink.CanWrite);

            const int writes = 40;
            for (var i = 0; i < writes; i++)
            {
                sink.Write(new DiagnosticEvent
                {
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    EventId = Guid.NewGuid(),
                    Type = DiagnosticType.FocusObserved,
                    Module = DiagnosticModule.Focus,
                    Sequence = i,
                });
            }

            var files = Directory.GetFiles(dir);
            Assert.True(files.Length <= maxFiles, $"文件数 {files.Length} 超出上限 {maxFiles}");

            long total = 0;
            foreach (var f in files)
            {
                var size = new FileInfo(f).Length;
                Assert.True(size <= maxFileBytes, $"{System.IO.Path.GetFileName(f)} 大小 {size} 超过单文件上限 {maxFileBytes}");
                total += size;
            }

            Assert.True(total <= maxFileBytes * maxFiles, $"总量 {total} 字节超出上界 {maxFileBytes * maxFiles}");

            // 所有文件（含滚动后的历史文件）不得以 UTF-8 BOM 开头。
            AssertNoUtf8BomInAllFiles(dir);

            // 活跃文件恒为 index 0：最后一笔写入（Sequence 39）必须落在 diagnostic.0.jsonl 的末行。
            var active = System.IO.Path.Combine(dir, "diagnostic.0.jsonl");
            Assert.True(File.Exists(active));
            var lastLine = File.ReadAllLines(active, Encoding.UTF8).Last();
            using var doc = JsonDocument.Parse(lastLine);
            Assert.Equal((long)(writes - 1), doc.RootElement.GetProperty("Sequence").GetInt64());
        }
        finally
        {
            DiagnosticPrivacyTests.DeleteDir(dir);
        }
    }

    [Fact]
    public void ConcurrentRolling_SingleFileNeverExceedsMax_TotalStrictlyBounded()
    {
        // 并发滚动（4 线程，小单文件上限强制触发轮转）：
        // 文件数与总字节必须严格 ≤ maxFileBytes × maxFileCount（不允许额外宽限）。
        var dir = DiagnosticPrivacyTests.CreateTempDir();
        try
        {
            const long maxFileBytes = 800;
            const int maxFiles = 5;
            var sink = new RollingFileDiagnosticSink(dir, maxFileBytes: maxFileBytes, maxFileCount: maxFiles);
            Assert.True(sink.CanWrite);

            const int threads = 4;
            const int perThread = 20;
            var start = new ManualResetEventSlim(false);
            var workers = new Thread[threads];
            for (var t = 0; t < threads; t++)
            {
                var tid = t;
                workers[t] = new Thread(() =>
                {
                    start.Wait();
                    for (var i = 0; i < perThread; i++)
                    {
                        sink.Write(new DiagnosticEvent
                        {
                            OccurredAtUtc = DateTimeOffset.UtcNow,
                            EventId = Guid.NewGuid(),
                            Type = DiagnosticType.FocusObserved,
                            Module = DiagnosticModule.Focus,
                            Sequence = tid * 1000 + i,
                        });
                    }
                });
            }

            foreach (var worker in workers)
            {
                worker.Start();
            }

            start.Set();

            foreach (var worker in workers)
            {
                worker.Join();
            }

            sink.Dispose();

            var files = Directory.GetFiles(dir);
            Assert.True(files.Length <= maxFiles, $"文件数 {files.Length} 超出上限 {maxFiles}");

            long total = 0;
            foreach (var f in files)
            {
                var size = new FileInfo(f).Length;
                Assert.True(size <= maxFileBytes, $"{System.IO.Path.GetFileName(f)} 大小 {size} 超过单文件上限 {maxFileBytes}");
                total += size;
            }

            Assert.True(total <= maxFileBytes * maxFiles, $"总量 {total} 字节超出上界 {maxFileBytes * maxFiles}");

            // 所有文件（含滚动后的历史文件）不得以 UTF-8 BOM 开头。
            AssertNoUtf8BomInAllFiles(dir);
        }
        finally
        {
            DiagnosticPrivacyTests.DeleteDir(dir);
        }
    }

    private static DiagnosticEvent MakeEvent(DiagnosticLevel level) =>
        new()
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            EventId = Guid.NewGuid(),
            Type = DiagnosticType.FocusObserved,
            Module = DiagnosticModule.Focus,
            Level = level,
            Sequence = 1,
        };

    /// <summary>断言目录下所有文件都不以 UTF-8 BOM（EF BB BF）开头（no-BOM 写入，GetByteCount 与实际字节一致）。</summary>
    private static void AssertNoUtf8BomInAllFiles(string dir)
    {
        foreach (var f in Directory.GetFiles(dir))
        {
            var bytes = File.ReadAllBytes(f);
            Assert.False(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                $"{System.IO.Path.GetFileName(f)} 以 UTF-8 BOM 开头");
        }
    }

    private static bool HasContent(string dir)
    {
        foreach (var f in Directory.GetFiles(dir))
        {
            if (new FileInfo(f).Length > 0)
            {
                return true;
            }
        }

        return false;
    }
}
