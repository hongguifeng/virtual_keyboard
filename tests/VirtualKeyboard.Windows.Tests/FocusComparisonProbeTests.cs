using System.IO;
using System.Text.Json;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class FocusComparisonProbeTests
{
    [Fact]
    public void RowsContainOnlyValueTypesAndAllowListedFields()
    {
        Assert.All(typeof(FocusProbeRow).GetProperties(), property => Assert.True(property.PropertyType.IsValueType));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new FocusProbeRow()));
        string[] expected = ["Sequence", "ObservedAtUtc", "Source", "ProcessId", "ControlTypeId",
            "HasKeyboardFocus", "IsEnabled", "IsOffscreen", "IsPassword", "HasValuePattern",
            "IsReadOnly", "HasTextPattern", "NativeProcessId", "NativeTopLevelHwnd", "NativeFocusHwnd",
            "SameIdentityAsEvent", "EventAgeMs", "DurationMs", "ErrorCode"];
        Assert.Equal(expected.Order(), json.RootElement.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public void WriterCapsRowsAndRejectsWritesAfterClose()
    {
        using var text = new StringWriter();
        using var writer = new FocusProbeWriter(text, 2);
        Assert.True(writer.TryWrite(new()));
        Assert.True(writer.TryWrite(new()));
        Assert.False(writer.TryWrite(new()));
        writer.Dispose();
        Assert.False(writer.TryWrite(new()));
        Assert.Equal(2, text.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void ProbeDurationIsBoundedBeforeCreatingFile()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FocusComparisonProbe.Run("unused", TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => FocusComparisonProbe.Run("unused", TimeSpan.FromMinutes(11)));
    }

    [Fact]
    public void WriteFailureIsReportedAndDoesNotEscapeToWorker()
    {
        using var output = new FailingWriter();
        using var writer = new FocusProbeWriter(output, 2);
        Assert.False(writer.TryWrite(new()));
        Assert.NotEqual(0, writer.WriteError);
        Assert.False(writer.TryWrite(new()));
        Assert.Equal(0, writer.Rows);
    }

    [Fact]
    public void ExistingOutputIsNeverOverwritten()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "synthetic sentinel");
            Assert.Throws<IOException>(() => FocusComparisonProbe.Run(path, TimeSpan.FromSeconds(1)));
            Assert.Equal("synthetic sentinel", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    private sealed class FailingWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("synthetic failure");
    }
}
