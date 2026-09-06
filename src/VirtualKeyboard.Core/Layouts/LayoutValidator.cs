using System.Collections.ObjectModel;

namespace VirtualKeyboard.Core.Layouts;

public static class LayoutSchemaLimits
{
    public const int SupportedSchemaVersion = 1;
    public const int MaximumRows = 16;
    public const int MaximumKeysPerRow = 64;
    public const int MaximumTotalKeys = 256;
    public const int MaximumLayoutIdLength = 128;
    public const int MaximumKeyIdLength = 64;
    public const int MaximumNameLength = 64;
    public const int MaximumCultureLength = 32;
    public const int MaximumLabelLength = 32;
    public const double MaximumKeyWidth = 16;
    public const int MaximumTextLength = 4096;
    public const int MaximumHotkeyModifiers = 3;
}

public sealed record LayoutValidationError(string Path, string Code, string Message);

public sealed class LayoutValidationResult
{
    internal LayoutValidationResult(IEnumerable<LayoutValidationError> errors)
    {
        Errors = new ReadOnlyCollection<LayoutValidationError>(errors.ToArray());
    }

    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<LayoutValidationError> Errors { get; }
}

/// <summary>Applies structural limits and a closed action/key allowlist without exposing sensitive text.</summary>
public static class LayoutValidator
{
    private static readonly HashSet<string> ValidVirtualKeys = CreateVirtualKeySet();
    private static readonly HashSet<string> ValidHotkeyModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shift", "Control", "Alt",
    };
    private static readonly HashSet<string> ValidStateModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shift", "Control", "Alt", "CapsLock",
    };

    public static LayoutValidationResult Validate(KeyboardLayoutDefinition? layout)
    {
        var errors = new List<LayoutValidationError>();
        if (layout is null)
        {
            Add(errors, "$", "layout.required", "Layout is required.");
            return new LayoutValidationResult(errors);
        }

        if (layout.SchemaVersion != LayoutSchemaLimits.SupportedSchemaVersion)
        {
            Add(errors, "$.schemaVersion", "schema.unsupported", "The schema version is not supported.");
        }

        ValidateRequiredText(layout.Id, LayoutSchemaLimits.MaximumLayoutIdLength, "$.id", errors);
        ValidateRequiredText(layout.Name, LayoutSchemaLimits.MaximumNameLength, "$.name", errors);
        ValidateRequiredText(layout.Culture, LayoutSchemaLimits.MaximumCultureLength, "$.culture", errors);

        if (layout.Rows is null)
        {
            Add(errors, "$.rows", "rows.required", "Rows are required.");
            return new LayoutValidationResult(errors);
        }

        if (layout.Rows.Count is < 1 or > LayoutSchemaLimits.MaximumRows)
        {
            Add(errors, "$.rows", "rows.count", $"Rows must contain between 1 and {LayoutSchemaLimits.MaximumRows} items.");
        }

        int totalKeys = 0;
        var keyIds = new HashSet<string>(StringComparer.Ordinal);
        int rowsToValidate = Math.Min(layout.Rows.Count, LayoutSchemaLimits.MaximumRows);
        for (int rowIndex = 0; rowIndex < rowsToValidate; rowIndex++)
        {
            KeyboardLayoutRow? row = layout.Rows[rowIndex];
            string rowPath = $"$.rows[{rowIndex}]";
            if (row?.Keys is null)
            {
                Add(errors, rowPath, "row.required", "A row and its keys are required.");
                continue;
            }

            totalKeys = SaturatingAdd(totalKeys, row.Keys.Count);
            if (row.Keys.Count is < 1 or > LayoutSchemaLimits.MaximumKeysPerRow)
            {
                Add(errors, rowPath, "row.keyCount", $"A row must contain between 1 and {LayoutSchemaLimits.MaximumKeysPerRow} keys.");
            }

            int keysToValidate = Math.Min(row.Keys.Count, LayoutSchemaLimits.MaximumKeysPerRow);
            for (int keyIndex = 0; keyIndex < keysToValidate; keyIndex++)
            {
                ValidateKey(row.Keys[keyIndex], $"{rowPath}[{keyIndex}]", keyIds, errors);
            }
        }

        // Count every row even when detailed validation was capped, so oversized layouts cannot pass.
        for (int rowIndex = rowsToValidate; rowIndex < layout.Rows.Count; rowIndex++)
        {
            totalKeys = SaturatingAdd(totalKeys, layout.Rows[rowIndex]?.Keys?.Count ?? 0);
        }

        if (totalKeys > LayoutSchemaLimits.MaximumTotalKeys)
        {
            Add(errors, "$.rows", "keys.totalCount", $"A layout cannot contain more than {LayoutSchemaLimits.MaximumTotalKeys} keys.");
        }

        return new LayoutValidationResult(errors);
    }

    private static void ValidateKey(
        KeyboardKeyDefinition? key,
        string path,
        HashSet<string> keyIds,
        List<LayoutValidationError> errors)
    {
        if (key is null)
        {
            Add(errors, path, "key.required", "A key is required.");
            return;
        }

        ValidateRequiredText(key.Id, LayoutSchemaLimits.MaximumKeyIdLength, $"{path}.id", errors);
        if (!string.IsNullOrWhiteSpace(key.Id) && !keyIds.Add(key.Id))
        {
            Add(errors, $"{path}.id", "key.idDuplicate", "Key IDs must be unique within a layout.");
        }

        ValidateRequiredText(key.Label, LayoutSchemaLimits.MaximumLabelLength, $"{path}.label", errors);
        if (!double.IsFinite(key.Width) || key.Width <= 0 || key.Width > LayoutSchemaLimits.MaximumKeyWidth)
        {
            Add(errors, $"{path}.width", "key.width", $"Width must be finite, greater than 0, and no greater than {LayoutSchemaLimits.MaximumKeyWidth}.");
        }

        if (key.Action is null)
        {
            Add(errors, $"{path}.action", "action.required", "An action is required.");
            return;
        }

        ValidateAction(key.Action, $"{path}.action", errors);
    }

    private static void ValidateAction(
        LayoutActionDefinition action,
        string path,
        List<LayoutValidationError> errors)
    {
        if (action.Type is not (LayoutActionTypes.Text or LayoutActionTypes.Key or LayoutActionTypes.Hotkey or LayoutActionTypes.Modifier))
        {
            Add(errors, $"{path}.type", "action.type", "The action type is not allowlisted.");
            return;
        }

        switch (action.Type)
        {
            case LayoutActionTypes.Text:
                ValidateTextAction(action, path, errors);
                break;
            case LayoutActionTypes.Key:
                ValidateKeyAction(action, path, errors);
                break;
            case LayoutActionTypes.Hotkey:
                ValidateHotkeyAction(action, path, errors);
                break;
            case LayoutActionTypes.Modifier:
                ValidateModifierAction(action, path, errors);
                break;
            default:
                Add(errors, $"{path}.type", "action.type", "The action type is not allowlisted.");
                break;
        }
    }

    private static void ValidateTextAction(LayoutActionDefinition action, string path, List<LayoutValidationError> errors)
    {
        if (string.IsNullOrEmpty(action.Value) || action.Value.Length > LayoutSchemaLimits.MaximumTextLength)
        {
            Add(errors, $"{path}.value", "action.textLength", $"Text must contain between 1 and {LayoutSchemaLimits.MaximumTextLength} UTF-16 code units.");
        }

        RejectUnexpectedKeyFields(action, path, errors, allowValue: true);
    }

    private static void ValidateKeyAction(LayoutActionDefinition action, string path, List<LayoutValidationError> errors)
    {
        ValidateMainKey(action, path, errors);
        RejectUnexpectedKeyFields(action, path, errors, allowKey: true);
    }

    private static void ValidateHotkeyAction(LayoutActionDefinition action, string path, List<LayoutValidationError> errors)
    {
        ValidateMainKey(action, path, errors);
        if (action.Modifiers is null || action.Modifiers.Count is < 1 or > LayoutSchemaLimits.MaximumHotkeyModifiers)
        {
            Add(errors, $"{path}.modifiers", "action.hotkeyLength", $"A hotkey must contain between 1 and {LayoutSchemaLimits.MaximumHotkeyModifiers} modifiers.");
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < action.Modifiers.Count; index++)
            {
                string? modifier = action.Modifiers[index];
                if (modifier is null || !ValidHotkeyModifiers.Contains(modifier))
                {
                    Add(errors, $"{path}.modifiers[{index}]", "action.modifier", "The hotkey modifier is not allowlisted.");
                }
                else if (!seen.Add(modifier))
                {
                    Add(errors, $"{path}.modifiers[{index}]", "action.modifierDuplicate", "Hotkey modifiers must be unique.");
                }
            }
        }

        RejectUnexpectedKeyFields(action, path, errors, allowKey: true, allowModifiers: true);
    }

    private static void ValidateModifierAction(LayoutActionDefinition action, string path, List<LayoutValidationError> errors)
    {
        if (action.Modifier is null || !ValidStateModifiers.Contains(action.Modifier))
        {
            Add(errors, $"{path}.modifier", "action.modifier", "The state modifier is not allowlisted.");
        }

        RejectUnexpectedKeyFields(action, path, errors, allowModifier: true);
    }

    private static void ValidateMainKey(LayoutActionDefinition action, string path, List<LayoutValidationError> errors)
    {
        bool hasVirtualKey = !string.IsNullOrWhiteSpace(action.VirtualKey);
        bool hasScanCode = action.ScanCode.HasValue;
        if (hasVirtualKey == hasScanCode)
        {
            Add(errors, path, "action.keyEncoding", "Exactly one of virtualKey or scanCode is required.");
            return;
        }

        if (hasVirtualKey && !ValidVirtualKeys.Contains(action.VirtualKey!))
        {
            Add(errors, $"{path}.virtualKey", "action.virtualKey", "The virtual key is not allowlisted.");
        }

        if (hasScanCode && action.ScanCode is < 1 or > ushort.MaxValue)
        {
            Add(errors, $"{path}.scanCode", "action.scanCode", "The scan code must be between 1 and 65535.");
        }
    }

    private static void RejectUnexpectedKeyFields(
        LayoutActionDefinition action,
        string path,
        List<LayoutValidationError> errors,
        bool allowValue = false,
        bool allowKey = false,
        bool allowModifiers = false,
        bool allowModifier = false)
    {
        if (!allowValue && action.Value is not null)
        {
            Add(errors, $"{path}.value", "action.unexpectedField", "The field is not valid for this action type.");
        }
        if (!allowKey && action.VirtualKey is not null)
        {
            Add(errors, $"{path}.virtualKey", "action.unexpectedField", "The field is not valid for this action type.");
        }
        if (!allowKey && action.ScanCode.HasValue)
        {
            Add(errors, $"{path}.scanCode", "action.unexpectedField", "The field is not valid for this action type.");
        }
        if (!allowModifiers && action.Modifiers is not null)
        {
            Add(errors, $"{path}.modifiers", "action.unexpectedField", "The field is not valid for this action type.");
        }
        if (!allowModifier && action.Modifier is not null)
        {
            Add(errors, $"{path}.modifier", "action.unexpectedField", "The field is not valid for this action type.");
        }
    }

    private static void ValidateRequiredText(
        string? value,
        int maximumLength,
        string path,
        List<LayoutValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            Add(errors, path, "string.length", $"The field must contain between 1 and {maximumLength} characters.");
        }
    }

    private static int SaturatingAdd(int left, int right) =>
        left > int.MaxValue - right ? int.MaxValue : left + right;

    private static void Add(List<LayoutValidationError> errors, string path, string code, string message) =>
        errors.Add(new LayoutValidationError(path, code, message));

    private static HashSet<string> CreateVirtualKeySet()
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Space", "Backspace", "Enter", "Tab", "Escape", "PageUp", "PageDown", "End", "Home",
            "Left", "Up", "Right", "Down", "Insert", "Delete",
        };
        for (char letter = 'A'; letter <= 'Z'; letter++)
        {
            keys.Add(letter.ToString());
        }
        for (char digit = '0'; digit <= '9'; digit++)
        {
            keys.Add(digit.ToString());
            keys.Add($"D{digit}");
        }

        return keys;
    }
}
