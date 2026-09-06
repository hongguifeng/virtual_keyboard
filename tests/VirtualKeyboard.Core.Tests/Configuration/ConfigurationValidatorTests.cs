using VirtualKeyboard.Core.Configuration;

namespace VirtualKeyboard.Core.Tests.Configuration;

public sealed class ConfigurationValidatorTests
{
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

    private static KeyboardConfiguration Default() =>
        new(1, true, true, true, 0.9, 800, 300, 8, "builtin.qwerty.en-US", ManualPositionMode.UntilTargetChanges, false);
}
