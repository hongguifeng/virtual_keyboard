using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Layouts;

namespace VirtualKeyboard.Core.Tests.Configuration;

public sealed class ConfigurationValidatorTests
{
    [Theory]
    [InlineData("command", "run")]
    [InlineData("script", "run")]
    [InlineData("modifier", "Control")]
    [InlineData("key", "UnlistedKey")]
    [InlineData("text", "")]
    public void LauncherCustomKeysUseExistingActionWhitelist(string type, string input)
    {
        var configuration = new KeyboardConfiguration(1, true, true, true, 0.9, 800, 300, 8,
            "layout", ManualPositionMode.UntilTargetChanges, false, launcherCustomKeys: [new("Button", type, input)]);
        var result = ConfigurationValidator.Validate(configuration);
        Assert.False(result.IsValid);
        Assert.All(result.Errors, error => Assert.StartsWith("$.launcherCustomKeys[0]", error.Path, StringComparison.Ordinal));
    }

    [Fact]
    public void LauncherCustomKeysHaveIndependentCountAndLengthLimits()
    {
        CustomKeyConfiguration[] keyboardKeys = Enumerable.Range(0, 12).Select(_ => new CustomKeyConfiguration("Key", "key", "Enter")).ToArray();
        var valid = new KeyboardConfiguration(1, true, true, true, 0.9, 800, 300, 8,
            "layout", ManualPositionMode.UntilTargetChanges, false, keyboardKeys, launcherCustomKeys: keyboardKeys);
        Assert.True(ConfigurationValidator.Validate(valid).IsValid);
        var tooMany = new KeyboardConfiguration(1, true, true, true, 0.9, 800, 300, 8,
            "layout", ManualPositionMode.UntilTargetChanges, false, launcherCustomKeys: keyboardKeys.Append(keyboardKeys[0]));
        Assert.Contains(ConfigurationValidator.Validate(tooMany).Errors, error => error.Path == "$.launcherCustomKeys");
        var tooLong = new KeyboardConfiguration(1, true, true, true, 0.9, 800, 300, 8,
            "layout", ManualPositionMode.UntilTargetChanges, false, launcherCustomKeys: [new(new('L', 33), "text", new('T', 257))]);
        var result = ConfigurationValidator.Validate(tooLong);
        Assert.Contains(result.Errors, error => error.Path == "$.launcherCustomKeys[0].label");
        Assert.Contains(result.Errors, error => error.Path == "$.launcherCustomKeys[0].input");
        Assert.DoesNotContain(result.Errors, error => error.Message.Contains(new string('T', 257), StringComparison.Ordinal));
    }

    private static readonly string[] EmptyChord = [];
    private static readonly string[] DuplicateChord = ["LeftWindows", "Tab", "tab"];
    private static readonly string[] UnknownChord = ["LeftWindows", "Power"];

    [Fact]
    public void DefaultConfigurationIsValid()
    {
        ConfigurationValidationResult result = ConfigurationValidator.Validate(Default());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UnsupportedVersionAndModeAreRejected()
    {
        KeyboardConfiguration defaults = Default();
        var configuration = new KeyboardConfiguration(2, defaults.Enabled, defaults.AutoShow, defaults.AutoHide, defaults.Opacity,
            defaults.KeyboardWidthDip, defaults.KeyboardHeightDip, defaults.MarginDip, defaults.LayoutId,
            (ManualPositionMode)99, defaults.DetailedDiagnostics);

        ConfigurationValidationResult result = ConfigurationValidator.Validate(configuration);

        Assert.Contains(result.Errors, error => error.Path == "$.schemaVersion");
        Assert.Contains(result.Errors, error => error.Path == "$.manualPositionMode");
    }

    [Fact]
    public void UnsupportedUiLanguageIsRejected()
    {
        KeyboardConfiguration defaults = Default();
        var configuration = new KeyboardConfiguration(defaults.SchemaVersion, defaults.Enabled, defaults.AutoShow,
            defaults.AutoHide, defaults.Opacity, defaults.KeyboardWidthDip, defaults.KeyboardHeightDip,
            defaults.MarginDip, defaults.LayoutId, defaults.ManualPositionMode, defaults.DetailedDiagnostics,
            defaults.CustomKeys, (UiLanguage)99);

        Assert.Contains(ConfigurationValidator.Validate(configuration).Errors, error => error.Path == "$.uiLanguage");
    }

    [Fact]
    public void NumericBoundsRejectNonFiniteValues()
    {
        KeyboardConfiguration configuration = new(1, true, true, true, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.NaN, "layout", ManualPositionMode.UntilTargetChanges, false);

        ConfigurationValidationResult result = ConfigurationValidator.Validate(configuration);

        Assert.Contains(result.Errors, error => error.Path == "$.opacity");
        Assert.Contains(result.Errors, error => error.Path == "$.keyboardWidthDip");
        Assert.Contains(result.Errors, error => error.Path == "$.keyboardHeightDip");
        Assert.Contains(result.Errors, error => error.Path == "$.marginDip");
    }

    [Theory]
    [MemberData(nameof(OutOfRangeNumbers))]
    public void NumericBoundsRejectOutOfRangeValues(double opacity, double width, double height, double margin, string expectedPath)
    {
        KeyboardConfiguration configuration = new(1, true, true, true, opacity, width, height, margin, "layout", ManualPositionMode.UntilTargetChanges, false);

        Assert.Contains(ConfigurationValidator.Validate(configuration).Errors, error => error.Path == expectedPath);
    }

    public static TheoryData<double, double, double, double, string> OutOfRangeNumbers => new()
    {
        { 0.2, 800, 300, 8, "$.opacity" },
        { 1.1, 800, 300, 8, "$.opacity" },
        { 0.9, 200, 300, 8, "$.keyboardWidthDip" },
        { 0.9, 2100, 300, 8, "$.keyboardWidthDip" },
        { 0.9, 800, 100, 8, "$.keyboardHeightDip" },
        { 0.9, 800, 1100, 8, "$.keyboardHeightDip" },
        { 0.9, 800, 300, -1, "$.marginDip" },
        { 0.9, 800, 300, 200, "$.marginDip" },
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LayoutIdIsRequired(string? layoutId)
    {
        var configuration = new KeyboardConfiguration(1, true, true, true, 0.9, 800, 300, 8, layoutId, ManualPositionMode.UntilTargetChanges, false);

        Assert.Contains(ConfigurationValidator.Validate(configuration).Errors, error => error.Path == "$.layoutId");
    }

    [Fact]
    public void ErrorMessagesDoNotContainUntrustedLayoutId()
    {
        string id = new('X', ConfigurationSchemaLimits.MaximumLayoutIdLength + 1);
        KeyboardConfiguration configuration = new(1, true, true, true, 0.9, 800, 300, 8, id, ManualPositionMode.UntilTargetChanges, false);

        ConfigurationValidationError error = Assert.Single(ConfigurationValidator.Validate(configuration).Errors);

        Assert.DoesNotContain(id, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('X', error.Message);
    }

    [Fact]
    public void CustomKeysRequireBoundedFieldsAndValidActions()
    {
        KeyboardConfiguration missingInput = new(1, true, true, true, 0.9, 800, 300, 8, "layout", ManualPositionMode.UntilTargetChanges, false,
            [new("Paste", "text", "")]);
        KeyboardConfiguration oversized = new(1, true, true, true, 0.9, 800, 300, 8, "layout", ManualPositionMode.UntilTargetChanges, false,
            [new(new('L', ConfigurationSchemaLimits.MaximumCustomKeyLabelLength + 1), "text", new('T', ConfigurationSchemaLimits.MaximumCustomKeyTextLength + 1))]);

        Assert.Contains(ConfigurationValidator.Validate(missingInput).Errors, error => error.Code == "config.customKeyInput");
        ConfigurationValidationResult result = ConfigurationValidator.Validate(oversized);
        Assert.Contains(result.Errors, error => error.Path == "$.customKeys[0].label");
        Assert.Contains(result.Errors, error => error.Path == "$.customKeys[0].input");
        Assert.DoesNotContain(result.Errors, error => error.Message.Contains('T'));
    }

    [Fact]
    public void CustomHotkeyAcceptsClosedModifierAndKeyNames()
    {
        KeyboardConfiguration valid = new(1, true, true, true, 0.9, 800, 300, 8, "layout", ManualPositionMode.UntilTargetChanges, false,
            [new("保存", "hotkey", "S", ["Control", "Shift"]), new("回车", "key", "Enter")]);

        Assert.True(ConfigurationValidator.Validate(valid).IsValid);
    }

    [Fact]
    public void CustomChordAcceptsCompleteOrderedKeyCombination()
    {
        KeyboardConfiguration valid = new(1, true, true, true, 0.9, 800, 300, 8, "layout", ManualPositionMode.UntilTargetChanges, false,
            [new("任务视图", LayoutActionTypes.Chord, "", ["LeftWindows", "Tab"])]);

        Assert.True(ConfigurationValidator.Validate(valid).IsValid);
    }

    [Theory]
    [MemberData(nameof(InvalidChords))]
    public void CustomChordRejectsInvalidKeySets(string[] keys)
    {
        KeyboardConfiguration invalid = new(1, true, true, true, 0.9, 800, 300, 8, "layout", ManualPositionMode.UntilTargetChanges, false,
            [new("无效", LayoutActionTypes.Chord, "", keys)]);

        Assert.Contains(ConfigurationValidator.Validate(invalid).Errors, error => error.Code == "config.customKeyAction");
    }

    public static TheoryData<string[]> InvalidChords => new()
    {
        EmptyChord,
        DuplicateChord,
        UnknownChord,
    };

    private static KeyboardConfiguration Default() =>
        new(1, true, true, true, 0.9, 800, 300, 8, "builtin.qwerty.en-US", ManualPositionMode.UntilTargetChanges, false);
}
