namespace VirtualKeyboard.Core.Layouts;

public enum KeyboardModifier
{
    Shift,
    Control,
    Alt,
}

public enum CapsLockOperationStatus
{
    Succeeded,
    Failed,
    Unavailable,
}

public readonly record struct CapsLockOperationResult(CapsLockOperationStatus Status, bool IsOn)
{
    public bool IsSuccess => Status == CapsLockOperationStatus.Succeeded;
}

public interface ICapsLockStateService
{
    CapsLockOperationResult Read();

    CapsLockOperationResult Toggle(long targetSessionId);
}

public readonly record struct KeyboardControllerState(
    long Version,
    long TargetSessionId,
    bool ShiftLatched,
    bool ControlLatched,
    bool AltLatched,
    bool IsCapsLockKnown,
    bool IsCapsLockOn);

public readonly record struct KeyboardActionPreparation(
    bool UseShift,
    bool UseControl,
    bool UseAlt,
    bool IsPrintable,
    KeyboardControllerState StateAfterPreparation);

/// <summary>Owns toggle modifier state bound to one target and mirrors the system CapsLock state.</summary>
public sealed class KeyboardController : IDisposable
{
    private readonly object _gate = new();
    private readonly ICapsLockStateService _capsLock;
    private long _version;
    private long _targetSessionId;
    private bool _shift;
    private bool _control;
    private bool _alt;
    private bool _capsKnown;
    private bool _capsOn;
    private bool _disposed;

    public KeyboardController(ICapsLockStateService capsLock)
    {
        _capsLock = capsLock ?? throw new ArgumentNullException(nameof(capsLock));
    }

    public KeyboardControllerState State
    {
        get
        {
            lock (_gate)
            {
                return Snapshot();
            }
        }
    }

    public KeyboardControllerState SetTargetSession(long sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_targetSessionId != sessionId)
            {
                _targetSessionId = sessionId;
                ClearTransient();
                _version++;
            }

            ApplyCapsLockResult(ReadCapsLockSafely());
            return Snapshot();
        }
    }

    public KeyboardControllerState ClearTargetSession()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_targetSessionId != 0 || _shift || _control || _alt)
            {
                _targetSessionId = 0;
                ClearTransient();
                _version++;
            }
            return Snapshot();
        }
    }

    public KeyboardControllerState ToggleModifier(KeyboardModifier modifier)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            switch (modifier)
            {
                case KeyboardModifier.Shift:
                    _shift = !_shift;
                    break;
                case KeyboardModifier.Control:
                    _control = !_control;
                    break;
                case KeyboardModifier.Alt:
                    _alt = !_alt;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(modifier));
            }
            _version++;
            return Snapshot();
        }
    }

    public KeyboardControllerState RefreshCapsLock()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            ApplyCapsLockResult(ReadCapsLockSafely());
            return Snapshot();
        }
    }

    public CapsLockOperationResult ToggleCapsLock()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            CapsLockOperationResult result;
            try
            {
                result = _targetSessionId == 0
                    ? new(CapsLockOperationStatus.Failed, _capsOn)
                    : _capsLock.Toggle(_targetSessionId);
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
            {
                result = new(CapsLockOperationStatus.Unavailable, _capsOn);
            }
            ApplyCapsLockResult(result);
            return result;
        }
    }

    public KeyboardActionPreparation PrepareAction(LayoutActionDefinition action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Type == LayoutActionTypes.Modifier ||
            action.Type is not (LayoutActionTypes.Text or LayoutActionTypes.Key or LayoutActionTypes.Hotkey))
        {
            throw new ArgumentException("Only validated input actions can be prepared.", nameof(action));
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_targetSessionId == 0)
            {
                throw new InvalidOperationException("An active target session is required.");
            }
            bool printable = IsPrintable(action);
            bool useShift = _shift;
            bool useControl = _control;
            bool useAlt = _alt;
            return new(useShift, useControl, useAlt, printable, Snapshot());
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _targetSessionId = 0;
            ClearTransient();
            _version++;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private static bool IsPrintable(LayoutActionDefinition action)
    {
        if (action.Type == LayoutActionTypes.Text)
        {
            return true;
        }
        if (action.Type != LayoutActionTypes.Key || action.VirtualKey is null)
        {
            return false;
        }

        return action.VirtualKey.Length == 1 && char.IsAsciiLetterOrDigit(action.VirtualKey[0]) ||
            action.VirtualKey.Equals("Space", StringComparison.OrdinalIgnoreCase) ||
            action.VirtualKey.Length == 2 && action.VirtualKey[0] is 'D' or 'd' && char.IsAsciiDigit(action.VirtualKey[1]);
    }

    private CapsLockOperationResult ReadCapsLockSafely()
    {
        try
        {
            return _capsLock.Read();
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return new(CapsLockOperationStatus.Unavailable, _capsOn);
        }
    }

    private void ApplyCapsLockResult(CapsLockOperationResult result)
    {
        bool known = result.IsSuccess;
        bool on = known ? result.IsOn : _capsOn;
        if (_capsKnown != known || _capsOn != on)
        {
            _capsKnown = known;
            _capsOn = on;
            _version++;
        }
    }

    private void ClearTransient()
    {
        _shift = false;
        _control = false;
        _alt = false;
    }

    private KeyboardControllerState Snapshot() => new(
        _version,
        _targetSessionId,
        _shift,
        _control,
        _alt,
        _capsKnown,
        _capsOn);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
