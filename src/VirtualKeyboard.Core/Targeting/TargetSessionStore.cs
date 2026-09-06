namespace VirtualKeyboard.Core.Targeting;

/// <summary>Atomically publishes immutable target sessions with monotonic identifiers.</summary>
public sealed class TargetSessionStore
{
    private readonly object _gate = new();
    private TargetSession? _current;
    private long _lastSessionId;

    public TargetSession? Current => Volatile.Read(ref _current);

    public TargetSession Replace(TargetCaptureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Validate(snapshot);
        lock (_gate)
        {
            long sessionId = checked(++_lastSessionId);
            var session = new TargetSession(
                sessionId,
                0,
                snapshot.CapturedAt,
                snapshot.ProcessId,
                snapshot.TopLevelHwnd,
                snapshot.FocusHwnd,
                null,
                false,
                null);
            Volatile.Write(ref _current, session);
            return session;
        }
    }

    public TargetSession Replace(FocusSnapshot snapshot, nint focusHwnd, VirtualKeyboard.Core.Geometry.PhysicalPixelRect? anchor)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Version <= 0) throw new ArgumentOutOfRangeException(nameof(snapshot));
        if (snapshot.ProcessId <= 0 || snapshot.TopLevelHwnd == nint.Zero || focusHwnd == nint.Zero)
            throw new ArgumentException("Focus target identity must be complete.", nameof(snapshot));
        if (anchor is { IsValid: false }) throw new ArgumentOutOfRangeException(nameof(anchor));
        lock (_gate)
        {
            long sessionId = checked(++_lastSessionId);
            var session = new TargetSession(
                sessionId,
                snapshot.Version,
                snapshot.ObservedAt,
                snapshot.ProcessId,
                snapshot.TopLevelHwnd,
                focusHwnd,
                snapshot.RuntimeId,
                snapshot.IsPassword,
                anchor);
            Volatile.Write(ref _current, session);
            return session;
        }
    }

    public void Clear() => Interlocked.Exchange(ref _current, null);

    private static void Validate(TargetCaptureSnapshot snapshot)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshot.ProcessId);
        if (snapshot.TopLevelHwnd == 0)
        {
            throw new ArgumentException("Top-level window handle must be non-zero.", nameof(snapshot));
        }

        if (snapshot.FocusHwnd == 0)
        {
            throw new ArgumentException("Focus window handle must be non-zero.", nameof(snapshot));
        }
    }
}
