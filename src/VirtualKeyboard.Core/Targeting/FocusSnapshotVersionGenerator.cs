namespace VirtualKeyboard.Core.Targeting;

/// <summary>Allocates process-local monotonic focus versions and filters self-process observations.</summary>
public sealed class FocusSnapshotVersionGenerator
{
    private long _version;
    private readonly int _currentProcessId;

    public FocusSnapshotVersionGenerator(int currentProcessId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(currentProcessId);
        _currentProcessId = currentProcessId;
    }

    public bool TryCreate(
        DateTimeOffset observedAt,
        int processId,
        nint topLevelHwnd,
        int[]? runtimeId,
        FocusControlType controlType,
        bool hasKeyboardFocus,
        bool isEnabled,
        bool isOffscreen,
        bool isPassword,
        out FocusSnapshot? snapshot)
    {
        long version = checked(Interlocked.Increment(ref _version));
        if (processId <= 0 || processId == _currentProcessId || topLevelHwnd == nint.Zero)
        {
            snapshot = null;
            return false;
        }

        snapshot = new FocusSnapshot(
            version,
            observedAt,
            processId,
            topLevelHwnd,
            runtimeId is null ? null : new RuntimeIdentity(runtimeId),
            controlType,
            hasKeyboardFocus,
            isEnabled,
            isOffscreen,
            isPassword);
        return true;
    }
}
