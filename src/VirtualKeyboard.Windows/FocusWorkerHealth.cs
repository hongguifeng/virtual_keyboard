namespace VirtualKeyboard.Windows;

/// <summary>A transport heartbeat is not proof that a synchronous UIA call has completed.</summary>
internal sealed class FocusWorkerHealth(TimeSpan timeout)
{
    private long _progress;
    private TimeSpan _lastProgress;

    public bool Observe(long progress, TimeSpan now)
    {
        if (progress <= _progress) return false;
        _progress = progress;
        _lastProgress = now;
        return true;
    }

    public bool IsExpired(TimeSpan now) => now - _lastProgress >= timeout;
}
