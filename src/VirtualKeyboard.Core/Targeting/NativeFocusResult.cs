namespace VirtualKeyboard.Core.Targeting;

public enum NativeFocusStatus
{
    Captured,
    NoForegroundWindow,
    ProcessLookupFailed,
    GuiThreadInfoUnavailable,
    FocusWindowUnavailable,
    CoordinateConversionFailed,
    NativeUnavailable,
}

public sealed record NativeFocusSnapshot(
    int ProcessId,
    uint ThreadId,
    nint TopLevelHwnd,
    nint FocusHwnd,
    nint KeyboardLayout,
    ScreenRectangle? CaretRectangle);

public readonly record struct NativeFocusResult(NativeFocusStatus Status, NativeFocusSnapshot? Snapshot)
{
    public bool IsCaptured => Status == NativeFocusStatus.Captured && Snapshot is not null;

    public static NativeFocusResult Success(NativeFocusSnapshot snapshot) => new(NativeFocusStatus.Captured, snapshot);
    public static NativeFocusResult Failure(NativeFocusStatus status) => new(status, null);
}
