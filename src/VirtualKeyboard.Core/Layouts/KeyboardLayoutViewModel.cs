using System.Collections.ObjectModel;

namespace VirtualKeyboard.Core.Layouts;

public sealed record KeyViewModel(
    string Id,
    string Label,
    double Width,
    bool SafeForPassword,
    LayoutActionDefinition Action);

public sealed class KeyboardRowViewModel
{
    internal KeyboardRowViewModel(IEnumerable<KeyViewModel> keys) =>
        Keys = new ReadOnlyCollection<KeyViewModel>(keys.ToArray());

    public IReadOnlyList<KeyViewModel> Keys { get; }
}

public sealed class KeyboardLayoutViewModel
{
    private KeyboardLayoutViewModel(string id, IEnumerable<KeyboardRowViewModel> rows)
    {
        Id = id;
        Rows = new ReadOnlyCollection<KeyboardRowViewModel>(rows.ToArray());
    }

    public string Id { get; }

    public IReadOnlyList<KeyboardRowViewModel> Rows { get; }

    public static KeyboardLayoutViewModel Create(KeyboardLayoutDefinition layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        LayoutValidationResult validation = LayoutValidator.Validate(layout);
        if (!validation.IsValid)
        {
            throw new ArgumentException("A validated layout is required.", nameof(layout));
        }

        IEnumerable<KeyboardRowViewModel> rows = layout.Rows!.Select(row =>
            new KeyboardRowViewModel(row.Keys!.Select(key =>
                new KeyViewModel(key.Id!, key.Label!, key.Width, key.SafeForPassword, key.Action!))));
        return new KeyboardLayoutViewModel(layout.Id!, rows);
    }
}

/// <summary>Small pointer state machine that emits at most one action for each accepted press.</summary>
public sealed class KeyGestureController
{
    private readonly object _gate = new();
    private bool _pressed;

    public bool IsPressed
    {
        get { lock (_gate) return _pressed; }
    }

    public bool Begin()
    {
        lock (_gate)
        {
            if (_pressed)
            {
                return false;
            }
            _pressed = true;
            return true;
        }
    }

    public bool Release(bool isInside)
    {
        lock (_gate)
        {
            if (!_pressed)
            {
                return false;
            }
            _pressed = false;
            return isInside;
        }
    }

    public bool Cancel()
    {
        lock (_gate)
        {
            bool wasPressed = _pressed;
            _pressed = false;
            return wasPressed;
        }
    }
}

/// <summary>Produces a bounded accelerating repeat schedule for a single held key.</summary>
public sealed class AcceleratingKeyRepeatController
{
    public static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(450);
    public static readonly TimeSpan StartingInterval = TimeSpan.FromMilliseconds(140);
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(45);
    private const double AccelerationFactor = 0.85;

    private bool _active;
    private bool _repeated;
    private TimeSpan _nextDelay = InitialDelay;

    public bool HasRepeated => _repeated;

    public TimeSpan Begin()
    {
        _active = true;
        _repeated = false;
        _nextDelay = InitialDelay;
        return _nextDelay;
    }

    public KeyRepeatTick Tick()
    {
        if (!_active) return new(false, _nextDelay);
        _nextDelay = !_repeated
            ? StartingInterval
            : TimeSpan.FromMilliseconds(Math.Max(MinimumInterval.TotalMilliseconds, _nextDelay.TotalMilliseconds * AccelerationFactor));
        _repeated = true;
        return new(true, _nextDelay);
    }

    public bool Release(bool isInside)
    {
        bool invokeSingle = _active && isInside && !_repeated;
        Reset();
        return invokeSingle;
    }

    public void Cancel() => Reset();

    private void Reset()
    {
        _active = false;
        _repeated = false;
        _nextDelay = InitialDelay;
    }
}

public readonly record struct KeyRepeatTick(bool ShouldInvoke, TimeSpan NextDelay);
