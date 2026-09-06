using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Core.Diagnostics;

/// <summary>Allow-listed focus classification metadata. No field can carry UIA Name, Value, or input text.</summary>
public readonly record struct FocusDiagnosticReport(
    DateTimeOffset CapturedAtUtc,
    long FocusVersion,
    int ProcessId,
    long TopLevelHwnd,
    FocusControlType ControlType,
    bool HasKeyboardFocus,
    bool IsEnabled,
    bool IsOffscreen,
    bool IsPassword,
    Editability Editability,
    ClassificationReasonCode ReasonCode,
    bool UsedFallback)
{
    public static FocusDiagnosticReport Create(
        FocusSnapshot snapshot,
        ClassificationResult classification,
        bool usedFallback,
        DateTimeOffset? capturedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (classification.Version != snapshot.Version)
            throw new ArgumentException("Classification version must match the focus snapshot.", nameof(classification));

        return new(
            capturedAtUtc ?? DateTimeOffset.UtcNow,
            snapshot.Version,
            snapshot.ProcessId,
            snapshot.TopLevelHwnd.ToInt64(),
            snapshot.ControlType,
            snapshot.HasKeyboardFocus,
            snapshot.IsEnabled,
            snapshot.IsOffscreen,
            snapshot.IsPassword,
            classification.Value,
            classification.ReasonCode,
            usedFallback);
    }
}

public static class FocusDiagnosticExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(FocusDiagnosticReport report) =>
        JsonSerializer.Serialize(report, Options);

    public static void Write(Stream destination, FocusDiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("Destination stream must be writable.", nameof(destination));
        JsonSerializer.Serialize(destination, report, Options);
    }
}
