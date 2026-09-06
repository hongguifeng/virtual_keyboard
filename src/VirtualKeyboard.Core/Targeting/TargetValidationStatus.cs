namespace VirtualKeyboard.Core.Targeting;

public enum TargetValidationStatus
{
    Valid,
    NoCurrentSession,
    SessionReplaced,
    TargetUnavailable,
    ForegroundChanged,
    ProcessChanged,
    FocusChanged,
    FocusIdentityUnavailable,
    FocusIdentityStale,
    IdentityChangedRequiresReclassification,
}
