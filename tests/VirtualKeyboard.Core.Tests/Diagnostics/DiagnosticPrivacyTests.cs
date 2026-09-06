namespace VirtualKeyboard.Core.Tests.Diagnostics;

using System.Reflection;
using System.Text.Json;
using VirtualKeyboard.Core.Diagnostics;
using Xunit;

/// <summary>
/// T0.4 隐私/序列化测试（FR-DIA-002 / NFR-PRI-001 / 设计文档 15）：
/// 隐私由类型级约束证明——DiagnosticEvent 所有公共实例属性均为值类型、
/// DiagnosticLogger.Log 所有参数均为值类型（string/object/dynamic 从类型上不存在），
/// 并以固定的 JSON 顶层字段白名单精确匹配序列化输出；仅使用假数据，绝不写入真实敏感值。
/// </summary>
public class DiagnosticPrivacyTests
{
    /// <summary>
    /// 固定的 JSON 顶层字段白名单（显式写出，不从 DiagnosticEvent 反射动态生成）：
    /// 序列化输出出现白名单之外的任何键都会使断言失败。
    /// </summary>
    private static readonly HashSet<string> ExpectedJsonKeys = new()
    {
        "OccurredAtUtc", "AppVersion", "EventId", "Type", "Module", "Level",
        "TargetProcessId", "ControlKind", "Verdict", "Reason", "ErrorCode",
        "DurationMs", "RequestedCount", "CompletedCount", "Sequence",
    };

    /// <summary>AppVersion 子对象只允许三个数字键。</summary>
    private static readonly HashSet<string> ExpectedAppVersionKeys = new() { "Major", "Minor", "Revision" };

    [Fact]
    public void DiagnosticEvent_AllPublicInstancePropertiesAreValueTypes()
    {
        // 类型级保证：任何公共实例属性都必须是值类型（含可空值类型、枚举与 struct）。
        // string/object/dynamic 均为引用类型，一旦存在即断言失败——
        // 不做任何属性名特判，AppVersion 若回退为 string 本测试直接失败。
        var props = typeof(DiagnosticEvent).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(props);

        foreach (var p in props)
        {
            Assert.True(
                IsValueOrNullableValueType(p.PropertyType),
                $"属性 {p.Name} 类型为 {p.PropertyType.FullName}：可能承载自由文本（违反 15.2 类型级约束）");
        }
    }

    [Fact]
    public void DiagnosticLogger_LogParameters_AllValueTypes_NoFreeText()
    {
        // Log 的所有参数必须是值类型（封闭枚举/数字/结构化版本号及其可空）：
        // string/object/dynamic 无法作为参数传入——从类型上无法携带用户文本。
        var logs = typeof(DiagnosticLogger).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "Log");
        Assert.NotEmpty(logs);

        foreach (var m in logs)
        {
            var parameters = m.GetParameters();
            Assert.NotEmpty(parameters);

            foreach (var p in parameters)
            {
                Assert.True(
                    IsValueOrNullableValueType(p.ParameterType),
                    $"Log 参数 {p.Name} 类型为 {p.ParameterType.FullName}：string/object/dynamic 不允许");
            }
        }
    }

    [Fact]
    public void JsonWhitelist_FullyPopulatedEvent_ExactKeySet_MatchesFixedWhitelist()
    {
        // 所有可空字段均赋值：实际顶层 JSON 键集合必须与固定白名单精确相等（不多不少）。
        var e = new DiagnosticEvent
        {
            OccurredAtUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            AppVersion = new AppVersion(1, 2, 3),
            EventId = Guid.Empty,
            Type = DiagnosticType.InputBatchStarted,
            Module = DiagnosticModule.Input,
            Level = DiagnosticLevel.Detailed,
            TargetProcessId = 4242,
            ControlKind = ControlKind.Password,
            Verdict = Verdict.Password,
            Reason = ReasonCode.IoError,
            ErrorCode = 7,
            DurationMs = 123,
            RequestedCount = 2,
            CompletedCount = 1,
            Sequence = 9,
        };

        var json = DiagnosticSerializer.ToJsonLine(e);
        using var doc = JsonDocument.Parse(json);

        var actual = new HashSet<string>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            Assert.True(actual.Add(p.Name), $"JSON 含重复键 {p.Name}");
        }

        Assert.True(
            actual.SetEquals(ExpectedJsonKeys),
            $"顶层键集合与固定白名单不一致：actual=[{string.Join(",", actual.OrderBy(k => k))}]");

        // AppVersion 子对象只允许 Major/Minor/Revision 三个数字键（无自由字符串）。
        var av = doc.RootElement.GetProperty("AppVersion");
        Assert.Equal(JsonValueKind.Object, av.ValueKind);

        var avKeys = new HashSet<string>();
        foreach (var p in av.EnumerateObject())
        {
            Assert.True(avKeys.Add(p.Name), $"AppVersion 含重复键 {p.Name}");
            Assert.True(p.Value.ValueKind == JsonValueKind.Number, $"AppVersion 键 {p.Name} 不是数字");
        }

        Assert.True(
            avKeys.SetEquals(ExpectedAppVersionKeys),
            $"AppVersion 键集合与白名单不一致：actual=[{string.Join(",", avKeys.OrderBy(k => k))}]");
        Assert.Equal(1, av.GetProperty("Major").GetInt32());
        Assert.Equal(2, av.GetProperty("Minor").GetInt32());
        Assert.Equal(3, av.GetProperty("Revision").GetInt32());
    }

    [Fact]
    public void SerializedEvent_StructuredFields_DeterministicContent()
    {
        var e = new DiagnosticEvent
        {
            OccurredAtUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            AppVersion = new AppVersion(0, 0, 0),
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

        // 结构化字段确定性序列化：枚举字符串 + 数字 + 版本号对象。
        Assert.Contains("ClassificationCompleted", json);
        Assert.Contains("PatternMissing", json);
        Assert.Contains("\"TargetProcessId\":4242", json);
        Assert.Contains("\"DurationMs\":12", json);
        Assert.Contains("\"AppVersion\":{\"Major\":0", json);
    }

    [Fact]
    public void PasswordVerdict_IsCarriedOnlyAsClosedEnum()
    {
        // 15.2/15.3：密码结论只以封闭枚举表达；序列化为枚举字符串，不含名称/值。
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
    public void Logger_AcceptsStructuredArgs_QueueAndFileConsistent()
    {
        // Logger 入口只接受封闭枚举/数字/结构化版本号（编译期无法传入任意字符串）；
        // 事件入队并可被读取，随后写入本地文件。
        var dir = CreateTempDir();
        try
        {
            var sink = new RollingFileDiagnosticSink(dir);
            var logger = new DiagnosticLogger(queueCapacity: 8, sink: sink);

            logger.Log(
                DiagnosticType.ClassificationCompleted,
                DiagnosticModule.Focus,
                targetProcessId: 4242,
                verdict: Verdict.Editable,
                durationMs: 7,
                appVersion: new AppVersion(0, 0, 0));

            Assert.Equal(1, logger.PendingCount);
            Assert.True(logger.TryReadNext(out var e));
            Assert.NotNull(e);
            Assert.Equal(DiagnosticType.ClassificationCompleted, e!.Type);

            var file = System.IO.Path.Combine(dir, "diagnostic.0.jsonl");
            var content = File.Exists(file) ? File.ReadAllText(file) : string.Empty;
            Assert.Contains("ClassificationCompleted", content);
        }
        finally
        {
            DeleteDir(dir);
        }
    }

    /// <summary>判断类型是值类型（含枚举/struct）或"值类型的可空"；引用类型（string/object 等）一律 false。</summary>
    private static bool IsValueOrNullableValueType(Type t)
    {
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            return t.GetGenericArguments()[0].IsValueType;
        }

        return t.IsValueType;
    }

    private static DiagnosticEvent MakeEvent(DiagnosticLevel level = DiagnosticLevel.Info) =>
        new()
        {
            OccurredAtUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            AppVersion = new AppVersion(0, 0, 0),
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
