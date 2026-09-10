namespace VirtualKeyboard.Core.Targeting;

/// <summary>Immutable, privacy-safe focus metadata; it intentionally contains no element Name or Value.</summary>
public sealed record FocusSnapshot(
    long Version,
    DateTimeOffset ObservedAt,
    int ProcessId,
    nint TopLevelHwnd,
    RuntimeIdentity? RuntimeId,
    FocusControlType ControlType,
    bool HasKeyboardFocus,
    bool IsEnabled,
    bool IsOffscreen,
    bool IsPassword);

public enum FocusControlType
{
    Unknown,
    Edit,
    Document,
    Button,
    Window,
    Pane,
    Text,
    Custom,
    Other,
    ComboBox,
    Spinner,
}

/// <summary>Immutable copy of a UI Automation runtime identifier.</summary>
public sealed class RuntimeIdentity : IEquatable<RuntimeIdentity>
{
    private readonly int[] _values;

    public RuntimeIdentity(IEnumerable<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values.ToArray();
    }

    public int Count => _values.Length;

    public int this[int index] => _values[index];

    public int[] ToArray() => _values.ToArray();

    public bool Equals(RuntimeIdentity? other) =>
        other is not null && _values.AsSpan().SequenceEqual(other._values);

    public override bool Equals(object? obj) => Equals(obj as RuntimeIdentity);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (int value in _values)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
