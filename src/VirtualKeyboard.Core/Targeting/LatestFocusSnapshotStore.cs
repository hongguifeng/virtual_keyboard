namespace VirtualKeyboard.Core.Targeting;

/// <summary>Publishes only monotonically newer immutable focus snapshots for send-time identity checks.</summary>
public sealed class LatestFocusSnapshotStore
{
    private readonly object _gate = new();
    private FocusSnapshot? _current;

    public FocusSnapshot? Current => Volatile.Read(ref _current);

    public bool Publish(FocusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            if (_current is not null && snapshot.Version <= _current.Version) return false;
            Volatile.Write(ref _current, snapshot);
            return true;
        }
    }

    public void Clear() => Interlocked.Exchange(ref _current, null);
}
