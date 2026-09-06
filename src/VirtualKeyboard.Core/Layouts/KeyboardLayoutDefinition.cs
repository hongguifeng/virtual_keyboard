using System.Collections.ObjectModel;

namespace VirtualKeyboard.Core.Layouts;

/// <summary>Immutable, platform-neutral representation of a versioned keyboard layout.</summary>
public sealed class KeyboardLayoutDefinition
{
    public KeyboardLayoutDefinition(
        int schemaVersion,
        string? id,
        string? name,
        string? culture,
        IEnumerable<KeyboardLayoutRow>? rows)
    {
        SchemaVersion = schemaVersion;
        Id = id;
        Name = name;
        Culture = culture;
        Rows = Snapshot(rows);
    }

    public int SchemaVersion { get; }

    public string? Id { get; }

    public string? Name { get; }

    public string? Culture { get; }

    public IReadOnlyList<KeyboardLayoutRow>? Rows { get; }

    private static ReadOnlyCollection<KeyboardLayoutRow>? Snapshot(IEnumerable<KeyboardLayoutRow>? values) =>
        values is null ? null : Array.AsReadOnly(values.ToArray());
}

/// <summary>A layout row whose keys retain their declared left-to-right order.</summary>
public sealed class KeyboardLayoutRow
{
    public KeyboardLayoutRow(IEnumerable<KeyboardKeyDefinition>? keys)
    {
        Keys = keys is null ? null : Array.AsReadOnly(keys.ToArray());
    }

    public IReadOnlyList<KeyboardKeyDefinition>? Keys { get; }
}

/// <summary>Visual key metadata and its single allowlisted action.</summary>
public sealed record KeyboardKeyDefinition(
    string? Id,
    string? Label,
    double Width,
    bool SafeForPassword,
    LayoutActionDefinition? Action);

public static class LayoutActionTypes
{
    public const string Text = "text";
    public const string Key = "key";
    public const string Hotkey = "hotkey";
    public const string Modifier = "modifier";
}

/// <summary>
/// Union-like action schema. The validator requires exactly the fields belonging to <see cref="Type"/>.
/// It intentionally provides no command or script execution member.
/// </summary>
public sealed class LayoutActionDefinition
{
    public LayoutActionDefinition(
        string? type,
        string? value = null,
        string? virtualKey = null,
        int? scanCode = null,
        IEnumerable<string>? modifiers = null,
        string? modifier = null,
        string? fnVirtualKey = null)
    {
        Type = type;
        Value = value;
        VirtualKey = virtualKey;
        ScanCode = scanCode;
        Modifiers = modifiers is null ? null : Array.AsReadOnly(modifiers.ToArray());
        Modifier = modifier;
        FnVirtualKey = fnVirtualKey;
    }

    public string? Type { get; }

    public string? Value { get; }

    public string? VirtualKey { get; }

    public int? ScanCode { get; }

    public IReadOnlyList<string>? Modifiers { get; }

    public string? Modifier { get; }

    /// <summary>Optional function-layer virtual key used while the internal Fn toggle is active.</summary>
    public string? FnVirtualKey { get; }
}
