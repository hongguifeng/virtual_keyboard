namespace VirtualKeyboard.Core.Tests.Diagnostics;

using System.Reflection;
using System.Text.Json;
using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Targeting;

public sealed class FocusDiagnosticReportTests
{
    private static readonly HashSet<string> AllowedKeys =
    [
        "CapturedAtUtc", "FocusVersion", "ProcessId", "TopLevelHwnd", "ControlType",
        "HasKeyboardFocus", "IsEnabled", "IsOffscreen", "IsPassword", "Editability",
        "ReasonCode", "UsedFallback",
    ];

    [Fact]
    public void ReportPublicPropertiesCannotCarryFreeText()
    {
        foreach (PropertyInfo property in typeof(FocusDiagnosticReport).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            Assert.True(property.PropertyType.IsValueType, $"{property.Name} must remain a value type.");
    }

    [Fact]
    public void ExportContainsExactlyTheAllowListedFields()
    {
        FocusDiagnosticReport report = Report();
        using JsonDocument document = JsonDocument.Parse(FocusDiagnosticExporter.ToJson(report));
        HashSet<string> keys = document.RootElement.EnumerateObject().Select(item => item.Name).ToHashSet();

        Assert.True(keys.SetEquals(AllowedKeys));
        Assert.Equal("Edit", document.RootElement.GetProperty("ControlType").GetString());
        Assert.Equal("Editable", document.RootElement.GetProperty("Editability").GetString());
        Assert.Equal(100L, document.RootElement.GetProperty("TopLevelHwnd").GetInt64());
    }

    [Fact]
    public void ReportRejectsMismatchedClassificationVersion()
    {
        FocusSnapshot snapshot = Snapshot();
        var classification = new ClassificationResult(8, Editability.Editable, ClassificationReasonCode.ValuePattern, false);
        Assert.Throws<ArgumentException>(() => FocusDiagnosticReport.Create(snapshot, classification, false));
    }

    [Fact]
    public void WriteLeavesDestinationOpen()
    {
        using var destination = new MemoryStream();
        FocusDiagnosticExporter.Write(destination, Report());
        destination.WriteByte(0);
        Assert.True(destination.Length > 1);
    }

    private static FocusDiagnosticReport Report()
    {
        FocusSnapshot snapshot = Snapshot();
        return FocusDiagnosticReport.Create(
            snapshot,
            new ClassificationResult(snapshot.Version, Editability.Editable, ClassificationReasonCode.ValuePattern, false),
            true,
            new DateTimeOffset(2026, 9, 6, 1, 2, 3, TimeSpan.Zero));
    }

    private static FocusSnapshot Snapshot() =>
        new(7, DateTimeOffset.UtcNow, 42, (nint)100, null, FocusControlType.Edit, true, true, false, false);
}
