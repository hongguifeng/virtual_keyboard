namespace VirtualKeyboard.Core.Targeting;

public sealed record TargetSession(
    long SessionId,
    DateTimeOffset CreatedAt,
    int ProcessId,
    nint TopLevelHwnd,
    nint FocusHwnd);
