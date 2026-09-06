using VirtualKeyboard.Core.Geometry;

namespace VirtualKeyboard.Core.Positioning;

/// <summary>Tracks a physical-pixel drag and binds the resulting position to exactly one target session.</summary>
public sealed class ManualPositionTracker
{
    private readonly object _gate = new();
    private long _activeSessionId;
    private PhysicalPixelPoint _startPointer;
    private PhysicalPixelRect _startRectangle;
    private PhysicalPixelRect _currentRectangle;
    private long _savedSessionId;
    private PhysicalPixelRect _savedRectangle;

    public void Begin(long sessionId, PhysicalPixelRect windowRectangle, PhysicalPixelPoint cursorPosition)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        if (!windowRectangle.IsValid || !windowRectangle.Size.IsPositive)
            throw new ArgumentOutOfRangeException(nameof(windowRectangle));
        if (!cursorPosition.IsFinite) throw new ArgumentOutOfRangeException(nameof(cursorPosition));
        lock (_gate)
        {
            _activeSessionId = sessionId;
            _startPointer = cursorPosition;
            _startRectangle = windowRectangle;
            _currentRectangle = windowRectangle;
        }
    }

    public bool TryUpdate(long sessionId, PhysicalPixelPoint cursorPosition, out PhysicalPixelRect rectangle)
    {
        lock (_gate)
        {
            if (_activeSessionId == 0 || sessionId != _activeSessionId || !cursorPosition.IsFinite)
            {
                rectangle = default;
                return false;
            }

            rectangle = _startRectangle with
            {
                X = _startRectangle.X + cursorPosition.X - _startPointer.X,
                Y = _startRectangle.Y + cursorPosition.Y - _startPointer.Y,
            };
            if (!rectangle.IsValid)
            {
                rectangle = default;
                return false;
            }

            _currentRectangle = rectangle;
            return true;
        }
    }

    public bool End(long sessionId, out PhysicalPixelRect rectangle)
    {
        lock (_gate)
        {
            if (_activeSessionId == 0 || sessionId != _activeSessionId)
            {
                rectangle = default;
                return false;
            }

            _activeSessionId = 0;
            _savedSessionId = sessionId;
            _savedRectangle = _currentRectangle;
            rectangle = _savedRectangle;
            return true;
        }
    }

    public bool TryGet(long sessionId, out PhysicalPixelRect rectangle)
    {
        lock (_gate)
        {
            if (_savedSessionId == sessionId && sessionId > 0)
            {
                rectangle = _savedRectangle;
                return true;
            }

            rectangle = default;
            return false;
        }
    }

    public void Cancel()
    {
        lock (_gate) _activeSessionId = 0;
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _activeSessionId = 0;
            _savedSessionId = 0;
            _savedRectangle = default;
        }
    }
}
