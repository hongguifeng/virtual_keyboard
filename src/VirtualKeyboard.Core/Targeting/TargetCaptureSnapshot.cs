namespace VirtualKeyboard.Core.Targeting;

public sealed record TargetCaptureSnapshot(
    DateTimeOffset CapturedAt,
    int ProcessId,
    nint TopLevelHwnd,
    nint FocusHwnd);
