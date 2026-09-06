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
                snapshot.CapturedAt,
                snapshot.ProcessId,
                snapshot.TopLevelHwnd,
                snapshot.FocusHwnd);
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
