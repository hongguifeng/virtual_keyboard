namespace VirtualKeyboard.Core.Layouts;

public enum PasswordActionDecision
{
    Allowed,
    Rejected,
}

public readonly record struct PasswordActionCheck(
    PasswordActionDecision Decision,
    string ReasonCode)
{
    public bool IsAllowed => Decision == PasswordActionDecision.Allowed;
}

/// <summary>Conservative password-field allowlist; it never inspects or returns text beyond its shape.</summary>
public static class PasswordActionPolicy
{
    public static PasswordActionCheck Check(LayoutActionDefinition action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action.Type switch
        {
            LayoutActionTypes.Text => IsSingleStandardCharacter(action.Value)
                ? Allowed()
                : Rejected("password.textNotSingleCharacter"),
            LayoutActionTypes.Key => IsAllowedKey(action)
                ? Allowed()
                : Rejected("password.keyNotAllowlisted"),
            LayoutActionTypes.Modifier => IsAllowedModifier(action.Modifier)
                ? Allowed()
                : Rejected("password.modifierNotAllowed"),
            LayoutActionTypes.Hotkey => Rejected("password.hotkeyDisabled"),
            _ => Rejected("password.actionTypeNotAllowed"),
        };
    }

    private static bool IsSingleStandardCharacter(string? value) =>
        value is { Length: 1 } &&
        !char.IsControl(value[0]) &&
        (char.IsLetterOrDigit(value[0]) || char.IsPunctuation(value[0]) || char.IsSymbol(value[0]) || value[0] == ' ');

    private static bool IsAllowedKey(LayoutActionDefinition action)
    {
        if (action.ScanCode.HasValue || action.VirtualKey is null)
        {
            return false;
        }

        // Standard characters and editing/navigation keys are safe; the schema validator has already closed the names.
        return action.VirtualKey.Length == 1 && char.IsAsciiLetterOrDigit(action.VirtualKey[0]) ||
            action.VirtualKey.Length == 2 && action.VirtualKey[0] is 'D' or 'd' && char.IsAsciiDigit(action.VirtualKey[1]) ||
            action.VirtualKey.Equals("Space", StringComparison.OrdinalIgnoreCase) ||
            action.VirtualKey is "Backspace" or "Enter" or "Tab" or "Escape" or
                "Left" or "Right" or "Up" or "Down" or "Home" or "End" or "PageUp" or "PageDown" or "Insert" or "Delete";
    }

    private static bool IsAllowedModifier(string? modifier) =>
        modifier is not null && modifier.Equals("Shift", StringComparison.OrdinalIgnoreCase) ||
        modifier is not null && modifier.Equals("CapsLock", StringComparison.OrdinalIgnoreCase);

    private static PasswordActionCheck Allowed() => new(PasswordActionDecision.Allowed, "password.allowed");

    private static PasswordActionCheck Rejected(string reason) => new(PasswordActionDecision.Rejected, reason);
}
