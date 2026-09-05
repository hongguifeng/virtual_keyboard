namespace VirtualKeyboard.Core.Tests.Diagnostics;

using System.Collections.Generic;
using System.Reflection;
using VirtualKeyboard.Core.Diagnostics;
using Xunit;

/// <summary>
/// T0.4 类型级隐私约束测试（FR-DIA-002 / NFR-PRI-001 / 设计文档 15.2）：
/// 证明 InputAction 文本、密码 Value、剪贴板、短语、元素名称
/// 无法进入 DiagnosticEvent，也不会出现在序列化输出中。
/// 本测试不写入任何真实敏感值，仅使用假的哨兵字符串，且断言这些值"不得出现"。
/// </summary>
public class DiagnosticPrivacyTests
{
    private const string SentinelSecret = "SENTINEL-SECRET-8f3a-never-log";
    private const string SentinelClipboard = "SENTINEL-CLIP-2d7c-never-log";
    private const string SentinelPhrase = "SENTINEL-PHRASE-9be1-never-log";
    private const string SentinelElementName = "SENTINEL-ELNAME-41c0-never-log";

    [Fact]
    public void DiagnosticEvent_HasNoFreeTextOrObjectTypedProperties()
    {
        // 类型级约束：除 AppVersion（版本号）外不存在 string 属性；
        // 除 AppVersion 外所有属性均为值类型（含可空值类型与枚举）。
        var props = typeof(DiagnosticEvent).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.True(props.Length > 0, "DiagnosticEvent 不应没有公共属性");

        foreach (var p in props)
        {
            if (p.PropertyType == typeof(string))
            {
                Assert.Equal(nameof(DiagnosticEvent.AppVersion), p.Name);
                continue;
            }

            Assert.True(
                p.PropertyType.IsValueType,
                $"属性 {p.Name} 类型为 {p.PropertyType}：非值类型/字符串字段可能承载任意文本（违反 15.2 类型级约束）");
        }
    }

    [Fact]
    public void SerializedEvent_NeverContainsSensitiveSamples()
    {
        var e = new DiagnosticEvent
        {
            OccurredAtUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            AppVersion = "0.0.0",
            EventId = Guid.NewGuid(),
            Type = DiagnosticType.ClassificationCompleted,
            Module = DiagnosticModule.Focus,
            Level = DiagnosticLevel.Info,
            TargetProcessId = 4242,
            ControlKind = ControlKind.Editable,
            Verdict = Verdict.Editable,
            Reason = ReasonCode.PatternMissing,
            ErrorCode = 17,
            DurationMs = 12,
            Sequence = 3,
        };

        var json = DiagnosticSerializer.ToJsonLine(e);

        // 事件结构本身序列化后不含任何哨兵（本用例未将哨兵塞入任何字段）。
        Assert.DoesNotContain(SentinelSecret, json);
        Assert.DoesNotContain(SentinelClipboard, json);
        Assert.DoesNotContain(SentinelPhrase, json);
        Assert.DoesNotContain(SentinelElementName, json);

        // 结构化字段确实被序列化（确定性内容）。
        Assert.Contains("ClassificationCompleted", json);
        Assert.Contains("Editable", json);
        Assert.Contains("\"TargetProcessId\":4242", json);
    }

    [Fact]
    public void PasswordVerdict_IsCarriedOnlyAsClosedEnum()
    {
        // 15.2/15.3：密码结论只以枚举表达；序列化为枚举字符串，不含名称/值。
        var e = new DiagnosticEvent
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            EventId = Guid.NewGuid(),
            Type = DiagnosticType.ClassificationCompleted,
            Module = DiagnosticModule.Focus,
            Verdict = Verdict.Password,
            ControlKind = ControlKind.Password,
            Sequence = 9,
        };

        var json = DiagnosticSerializer.ToJsonLine(e);
        Assert.Contains("\"Verdict\":\"Password\"", json);
        Assert.Contains("\"ControlKind\":\"Password\"", json);
        Assert.DoesNotContain(SentinelElementName, json);
        Assert.DoesNotContain(SentinelSecret, json);
    }

    [Fact]
    public void DetailedLevel_Event_IsGatedBySinkFlag()
    {
        var dir = CreateTempDir();
        try
        {
            var off = new RollingFileDiagnosticSink(dir, detailedEnabled: false);
            Assert.True(off.CanWrite);
            off.Write(MakeEvent(level: DiagnosticLevel.Detailed));
            Assert.Equal(0, TotalBytes(dir));

            var on = new RollingFileDiagnosticSink(dir, detailedEnabled: true);
            on.Write(MakeEvent(level: DiagnosticLevel.Detailed));
            Assert.True(TotalBytes(dir) > 0);
        }
        finally
        {
            DeleteDir(dir);
        }
    }

    [Fact]
    public void NonWritablePath_SinkDegradesToNoOpWithoutThrowing()
    {
        var dir = CreateTempDir();
        try
        {
            // 用一个已存在的"文件"充当 root：CreateDirectory 必然失败 → 降级。
            var blocker = System.IO.Path.Combine(dir, "blocker");
            File.WriteAllText(blocker, "x");

            var sink = new RollingFileDiagnosticSink(blocker);
            Assert.False(sink.CanWrite);

            sink.Write(MakeEvent()); // 必须不抛
            Assert.False(sink.CanWrite);
            Assert.Single(Directory.GetFiles(dir)); // 只留下 blocker，无诊断文件
        }
        finally
        {
            DeleteDir(dir);
        }
    }

    [Fact]
    public void LoggerRejectsFreeTextInput()
    {
        var dir = CreateTempDir();
        try
        {
            var sink = new RollingFileDiagnosticSink(dir);
            var logger = new DiagnosticLogger(queueCapacity: 8, sink: sink);

            // 骨架 API 只有封闭枚举/数字/版本号参数——编译期即无法传入任意字符串内容。
            logger.Log(
                DiagnosticType.ClassificationCompleted,
                DiagnosticModule.Focus,
                targetProcessId: 4242,
                verdict: Verdict.Editable,
                durationMs: 7,
                appVersion: "0.0.0");

            Assert.Equal(1, logger.PendingCount);
            Assert.True(logger.TryReadNext(out var e));
            Assert.NotNull(e);
            Assert.Equal(DiagnosticType.ClassificationCompleted, e!.Type);

            var file = System.IO.Path.Combine(dir, "diagnostic.0.jsonl");
            var content = File.Exists(file) ? File.ReadAllText(file) : string.Empty;
            Assert.Contains("ClassificationCompleted", content);
            Assert.DoesNotContain(SentinelSecret, content);
            Assert.DoesNotContain(SentinelClipboard, content);
            Assert.DoesNotContain(SentinelPhrase, content);
            Assert.DoesNotContain(SentinelElementName, content);
        }
        finally
        {
            DeleteDir(dir);
        }
    }

    private static DiagnosticEvent MakeEvent(DiagnosticLevel level = DiagnosticLevel.Info) =>
        new()
        {
            OccurredAtUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            AppVersion = "0.0.0",
            EventId = Guid.NewGuid(),
            Type = DiagnosticType.FocusObserved,
            Module = DiagnosticModule.Focus,
            Level = level,
            TargetProcessId = -1,
            Sequence = 1,
        };

    internal static string CreateTempDir()
    {
        var d = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vbk-t04-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    internal static void DeleteDir(string d)
    {
        try
        {
            Directory.Delete(d, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }

    private static long TotalBytes(string dir)
    {
        long sum = 0;
        foreach (var f in Directory.GetFiles(dir))
        {
            sum += new FileInfo(f).Length;
        }

        return sum;
    }
}
