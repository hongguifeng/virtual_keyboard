using System.Collections.ObjectModel;
using VirtualKeyboard.Core.Layouts;

namespace VirtualKeyboard.Core.Configuration;

public enum ManualPositionMode
{
    UntilTargetChanges,
    Persistent,
}

public enum UiLanguage
{
    English,
    SimplifiedChinese,
}

public sealed class CustomKeyConfiguration
{
    public CustomKeyConfiguration(string? label, string? actionType, string? input, IEnumerable<string>? modifiers = null)
    {
        Label = label ?? string.Empty;
        ActionType = actionType ?? string.Empty;
        Input = input ?? string.Empty;
        Modifiers = Array.AsReadOnly((modifiers ?? []).ToArray());
    }

    public string Label { get; }
    public string ActionType { get; }
    public string Input { get; }
    public IReadOnlyList<string> Modifiers { get; }

    public LayoutActionDefinition ToLayoutAction() => ActionType switch
    {
        LayoutActionTypes.Text => new(LayoutActionTypes.Text, value: Input),
        LayoutActionTypes.Key => new(LayoutActionTypes.Key, virtualKey: Input),
        LayoutActionTypes.Hotkey => new(LayoutActionTypes.Hotkey, virtualKey: Input, modifiers: Modifiers),
        LayoutActionTypes.Chord => new(LayoutActionTypes.Chord, keys: Modifiers),
        _ => new(ActionType),
    };
}

public sealed class KeyboardConfiguration
{
    public KeyboardConfiguration(
        int schemaVersion,
        bool enabled,
        bool autoShow,
        bool autoHide,
        double opacity,
        double keyboardWidthDip,
        double keyboardHeightDip,
        double marginDip,
        string? layoutId,
        ManualPositionMode manualPositionMode,
        bool detailedDiagnostics,
        IEnumerable<CustomKeyConfiguration>? customKeys = null,
        UiLanguage uiLanguage = UiLanguage.English,
        bool autoStart = false,
        bool showLauncherButton = false,
        IEnumerable<CustomKeyConfiguration>? launcherCustomKeys = null)
    {
        SchemaVersion = schemaVersion;
        Enabled = enabled;
        AutoShow = autoShow;
        AutoHide = autoHide;
        Opacity = opacity;
        KeyboardWidthDip = keyboardWidthDip;
        KeyboardHeightDip = keyboardHeightDip;
        MarginDip = marginDip;
        LayoutId = layoutId;
        ManualPositionMode = manualPositionMode;
        DetailedDiagnostics = detailedDiagnostics;
        CustomKeys = Array.AsReadOnly((customKeys ?? []).ToArray());
        UiLanguage = uiLanguage;
        AutoStart = autoStart;
        ShowLauncherButton = showLauncherButton;
        LauncherCustomKeys = Array.AsReadOnly((launcherCustomKeys ?? []).ToArray());
    }

    public int SchemaVersion { get; }
    public bool Enabled { get; }
    public bool AutoShow { get; }
    public bool AutoHide { get; }
    /// <summary>Shows a caret-adjacent launcher before expanding the keyboard; defaults to the existing automatic keyboard behavior.</summary>
    public bool ShowLauncherButton { get; }
    public double Opacity { get; }
    public double KeyboardWidthDip { get; }
    public double KeyboardHeightDip { get; }
    public double MarginDip { get; }
    public string? LayoutId { get; }
    public ManualPositionMode ManualPositionMode { get; }
    public bool DetailedDiagnostics { get; }
    public IReadOnlyList<CustomKeyConfiguration> CustomKeys { get; }
    public IReadOnlyList<CustomKeyConfiguration> LauncherCustomKeys { get; }
    public UiLanguage UiLanguage { get; }

    /// <summary>当前用户开机自启（FR-APP-004；默认 false，首启绝不自动启用）。镜像 Windows 当前用户 Run 键的真实状态。</summary>
    public bool AutoStart { get; }
}

public sealed record ConfigurationValidationError(string Path, string Code, string Message);

public sealed class ConfigurationValidationResult
{
    internal ConfigurationValidationResult(IEnumerable<ConfigurationValidationError> errors) =>
        Errors = new ReadOnlyCollection<ConfigurationValidationError>(errors.ToArray());

    public bool IsValid => Errors.Count == 0;
    public IReadOnlyList<ConfigurationValidationError> Errors { get; }
}

public static class ConfigurationSchemaLimits
{
    public const int SupportedSchemaVersion = 1;
    public const double MinimumOpacity = 0.30;
    public const double MaximumOpacity = 1.0;
    public const double MinimumKeyboardWidthDip = 620;
    public const double MaximumKeyboardWidthDip = 2000;
    public const double MinimumKeyboardHeightDip = 280;
    public const double MaximumKeyboardHeightDip = 1000;
    public const double MinimumMarginDip = 0;
    public const double MaximumMarginDip = 128;
    public const int MaximumLayoutIdLength = 128;
    public const int MaximumCustomKeyLabelLength = 32;
    public const int MaximumCustomKeyTextLength = 256;
    public const int MaximumCustomKeys = 12;
}

public static class ConfigurationValidator
{
    public static ConfigurationValidationResult Validate(KeyboardConfiguration? configuration)
    {
        var errors = new List<ConfigurationValidationError>();
        if (configuration is null)
        {
            errors.Add(new("$", "config.required", "Configuration is required."));
            return new(errors);
        }

        if (configuration.SchemaVersion != ConfigurationSchemaLimits.SupportedSchemaVersion)
        {
            Add(errors, "$.schemaVersion", "schema.unsupported", "The configuration schema version is not supported.");
        }
        ValidateFiniteRange(configuration.Opacity, ConfigurationSchemaLimits.MinimumOpacity, ConfigurationSchemaLimits.MaximumOpacity, "$.opacity", "config.opacity", errors);
        ValidateFiniteRange(configuration.KeyboardWidthDip, ConfigurationSchemaLimits.MinimumKeyboardWidthDip, ConfigurationSchemaLimits.MaximumKeyboardWidthDip, "$.keyboardWidthDip", "config.keyboardWidth", errors);
        ValidateFiniteRange(configuration.KeyboardHeightDip, ConfigurationSchemaLimits.MinimumKeyboardHeightDip, ConfigurationSchemaLimits.MaximumKeyboardHeightDip, "$.keyboardHeightDip", "config.keyboardHeight", errors);
        ValidateFiniteRange(configuration.MarginDip, ConfigurationSchemaLimits.MinimumMarginDip, ConfigurationSchemaLimits.MaximumMarginDip, "$.marginDip", "config.margin", errors);

        if (string.IsNullOrWhiteSpace(configuration.LayoutId) || configuration.LayoutId.Length > ConfigurationSchemaLimits.MaximumLayoutIdLength)
        {
            Add(errors, "$.layoutId", "config.layoutId", $"Layout ID must contain between 1 and {ConfigurationSchemaLimits.MaximumLayoutIdLength} characters.");
        }
        if (!Enum.IsDefined(configuration.ManualPositionMode))
        {
            Add(errors, "$.manualPositionMode", "config.manualPositionMode", "The manual position mode is not supported.");
        }
        if (!Enum.IsDefined(configuration.UiLanguage))
        {
            Add(errors, "$.uiLanguage", "config.uiLanguage", "The UI language is not supported.");
        }
        ValidateCustomKeys(configuration.CustomKeys, "$.customKeys", errors);
        ValidateCustomKeys(configuration.LauncherCustomKeys, "$.launcherCustomKeys", errors);

        return new(errors);
    }

    private static void ValidateCustomKeys(IReadOnlyList<CustomKeyConfiguration> keys, string path, List<ConfigurationValidationError> errors)
    {
        if (keys.Count > ConfigurationSchemaLimits.MaximumCustomKeys)
        {
            Add(errors, path, "config.customKeysCount", $"Custom keys cannot exceed {ConfigurationSchemaLimits.MaximumCustomKeys} items.");
        }
        for (int index = 0; index < Math.Min(keys.Count, ConfigurationSchemaLimits.MaximumCustomKeys); index++)
        {
            ValidateCustomKey(keys[index], $"{path}[{index}]", errors);
        }
    }

    private static void ValidateCustomKey(CustomKeyConfiguration key, string path, List<ConfigurationValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(key.Label) || key.Label.Length > ConfigurationSchemaLimits.MaximumCustomKeyLabelLength)
        {
            Add(errors, $"{path}.label", "config.customKeyLabel", $"Custom key label must contain between 1 and {ConfigurationSchemaLimits.MaximumCustomKeyLabelLength} characters.");
        }
        if ((key.ActionType != LayoutActionTypes.Chord && string.IsNullOrEmpty(key.Input)) || key.Input.Length > ConfigurationSchemaLimits.MaximumCustomKeyTextLength)
        {
            Add(errors, $"{path}.input", "config.customKeyInput", $"Custom key input must contain between 1 and {ConfigurationSchemaLimits.MaximumCustomKeyTextLength} characters.");
        }
        if (key.ActionType is not (LayoutActionTypes.Text or LayoutActionTypes.Key or LayoutActionTypes.Hotkey or LayoutActionTypes.Chord))
        {
            Add(errors, $"{path}.actionType", "config.customKeyAction", "Custom key action must be text, key, hotkey, or chord.");
            return;
        }
        if (key.ActionType is not (LayoutActionTypes.Hotkey or LayoutActionTypes.Chord) && key.Modifiers.Count > 0)
        {
            Add(errors, $"{path}.modifiers", "config.customKeyModifiers", "Only hotkey or chord actions may declare key lists.");
            return;
        }
        if (key.ActionType is not (LayoutActionTypes.Text or LayoutActionTypes.Chord) && key.Input.Length > LayoutSchemaLimits.MaximumLabelLength)
        {
            Add(errors, $"{path}.input", "config.customKeyInput", "Custom key virtual key name is too long.");
            return;
        }

        var synthetic = new KeyboardLayoutDefinition(1, "custom.validation", "Custom", "en-US",
            [new KeyboardLayoutRow([new KeyboardKeyDefinition("custom", key.Label, 1, false, key.ToLayoutAction())])]);
        if (!LayoutValidator.Validate(synthetic).IsValid)
        {
            Add(errors, path, "config.customKeyAction", "Custom key action is invalid.");
        }
    }

    private static void ValidateFiniteRange(double value, double minimum, double maximum, string path, string code, List<ConfigurationValidationError> errors)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            Add(errors, path, code, $"Value must be finite and between {minimum} and {maximum}.");
        }
    }

    private static void Add(List<ConfigurationValidationError> errors, string path, string code, string message) =>
        errors.Add(new ConfigurationValidationError(path, code, message));
}
