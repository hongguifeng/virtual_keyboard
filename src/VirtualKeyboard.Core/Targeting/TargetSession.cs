using VirtualKeyboard.Core.Geometry;

namespace VirtualKeyboard.Core.Targeting;

public sealed record TargetSession(
    long SessionId,
    long FocusVersion,
    DateTimeOffset CreatedAt,
    int ProcessId,
    nint TopLevelHwnd,
    nint FocusHwnd,
    RuntimeIdentity? RuntimeId,
    bool IsPassword,
    PhysicalPixelRect? Anchor);
