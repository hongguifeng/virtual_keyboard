using System.Collections.ObjectModel;

namespace VirtualKeyboard.Core.Configuration;

public enum ManualPositionMode
{
    UntilTargetChanges,
    Persistent,
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
        bool detailedDiagnostics)
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
    }

    public int SchemaVersion { get; }
    public bool Enabled { get; }
    public bool AutoShow { get; }
    public bool AutoHide { get; }
    public double Opacity { get; }
    public double KeyboardWidthDip { get; }
    public double KeyboardHeightDip { get; }
    public double MarginDip { get; }
    public string? LayoutId { get; }
    public ManualPositionMode ManualPositionMode { get; }
    public bool DetailedDiagnostics { get; }
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
    public const double MinimumKeyboardWidthDip = 240;
    public const double MaximumKeyboardWidthDip = 2000;
    public const double MinimumKeyboardHeightDip = 120;
    public const double MaximumKeyboardHeightDip = 1000;
    public const double MinimumMarginDip = 0;
    public const double MaximumMarginDip = 128;
    public const int MaximumLayoutIdLength = 128;
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

        return new(errors);
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
