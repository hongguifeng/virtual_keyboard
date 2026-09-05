namespace VirtualKeyboard.Core.Tests.Diagnostics;

using VirtualKeyboard.Core.Diagnostics;
using Xunit;

/// <summary>
/// T0.4 滚动文件汇聚点测试（FR-DIA-003 / NFR-PRI-001 / NFR-PERF-001 / 设计文档 15.3/16）：
/// 文件数与总大小有确定上界；详细事件默认关闭；不可写目录降级为 no-op。
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
                total += new FileInfo(f).Length;
            }

            // 总量必须落在 文件数 × 单文件上限 的确定性上界之内（允许最后一个文件的写入余量）。
            Assert.True(total <= (long)maxFiles * 1024 + 8192, $"总量 {total} 字节超出上界");
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
