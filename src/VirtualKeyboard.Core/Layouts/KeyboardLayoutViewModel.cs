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
