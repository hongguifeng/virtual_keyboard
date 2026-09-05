namespace VirtualKeyboard.Core.Tests.Diagnostics;

using System.IO;
using VirtualKeyboard.Core.Diagnostics;
using Xunit;

/// <summary>
/// T0.4 本地滚动文件汇聚点测试（FR-DIA-003 / NFR-PRI-001 / 15.3）：
/// 总大小有界（滚动删除最旧）、详细诊断默认关闭、IO 故障降级不抛异常。
/// </summary>
public class RollingFileDiagnosticSinkTests
{
    [Fact]
    public void Rotation_KeepsFileCountAndTotalSizeBounded()
    {
        var dir = DiagnosticPrivacyTests.CreateTempDir();
        try
        {
            const long maxFileBytes = 1024;
            const int maxFileCount = 3;

            using var sink = new RollingFileDiagnosticSink(
                dir,
                detailedEnabled: false,
                maxFileBytes: maxFileBytes,
                maxFileCount: maxFileCount);
            Assert.True(sink.CanWrite);

            // 每条事件序列化后约 200B；写 30 条必然触发多次滚动。
            for (var i = 0; i < 30; i++)
            {
                sink.Write(new DiagnosticEvent
                {
                    OccurredAtUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                    AppVersion = "0.0.0",
                    EventId = Guid.NewGuid(),
                    Type = DiagnosticType.InputBatchStarted,
                    Module = DiagnosticModule.Input,
                    Level = DiagnosticLevel.Info,
                    Sequence = i,
                });
            }

            var files = Directory
                .GetFiles(dir)
                .Where(f => f.EndsWith(".jsonl", System.StringComparison.Ordinal))
                .ToArray();
            Assert.True(files.Length <= maxFileCount, $"文件数 {files.Length} 超过上限 {maxFileCount}");

            var total = files.Sum(f => new FileInfo(f).Length);
            Assert.True(total <= maxFileBytes * maxFileCount, $"总量 {total}B 超过 {maxFileBytes * maxFileCount}B");

            // 活跃文件存在且含最新事件。
            var active = System.IO.Path.Combine(dir, "diagnostic.0.jsonl");
            Assert.True(File.Exists(active));
            var content = File.ReadAllText(active);
            Assert.Contains("\"Sequence\":29", content);
        }
        finally
        {
            DiagnosticPrivacyTests.DeleteDir(dir);
        }
    }

    [Fact]
    public void MaxFileBytes_IsClampedToMinimum()
    {
        var dir = DiagnosticPrivacyTests.CreateTempDir();
        try
        {
            // 小于 512B 的非法上限被夹到 512，避免单行必滚动的病态配置。
            using var sink = new RollingFileDiagnosticSink(dir, maxFileBytes: 1);
            Assert.True(sink.CanWrite);
            sink.Write(new DiagnosticEvent
            {
                OccurredAtUtc = DateTimeOffset.UtcNow,
                EventId = Guid.NewGuid(),
                Type = DiagnosticType.FocusObserved,
                Module = DiagnosticModule.Focus,
                Sequence = 1,
            });

            // 单条约 200B 的行应能写入（512B 下限）。
            Assert.True(new FileInfo(System.IO.Path.Combine(dir, "diagnostic.0.jsonl")).Length > 0);
        }
        finally
        {
            DiagnosticPrivacyTests.DeleteDir(dir);
        }
    }

    [Fact]
    public void LoggerWithSinkWritesStructuredLine()
    {
        var dir = DiagnosticPrivacyTests.CreateTempDir();
        try
        {
            var sink = new RollingFileDiagnosticSink(dir);
            using var logger = new DiagnosticLogger(queueCapacity: 4, sink: sink);

            logger.Log(
                DiagnosticType.TargetSessionCreated,
                DiagnosticModule.State,
                targetProcessId: 1234,
                appVersion: "0.0.0");
            logger.Log(
                DiagnosticType.InputBatchSucceeded,
                DiagnosticModule.Input,
                durationMs: 42,
                appVersion: "0.0.0");

            var content = File.ReadAllText(System.IO.Path.Combine(dir, "diagnostic.0.jsonl"));
            Assert.Contains("TargetSessionCreated", content);
            Assert.Contains("InputBatchSucceeded", content);
            Assert.Contains("\"DurationMs\":42", content);
            Assert.Contains("\"TargetProcessId\":1234", content);

            // 队列中有待读事件（logger 的有界队列独立于文件汇聚点）。
            Assert.Equal(2, logger.PendingCount);
            Assert.True(logger.TryReadNext(out var e));
            Assert.Equal(DiagnosticType.TargetSessionCreated, e!.Type);
        }
        finally
        {
            DiagnosticPrivacyTests.DeleteDir(dir);
        }
    }
}
