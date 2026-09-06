namespace VirtualKeyboard.Core.Targeting;

public enum TargetCaptureStatus
{
    Captured,
    NoForegroundWindow,
    ProcessLookupFailed,
    OwnProcess,
    FocusWindowUnavailable,
    CaptureUnavailable,
}
