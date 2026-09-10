using System.Text;
using System.Text.Json;
using VirtualKeyboard.Core.Configuration;

namespace VirtualKeyboard.Core.Tests.Configuration;

public sealed class ConfigurationRepositoryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    [Fact]
    public void MissingFileReturnsSafeDefaults()
    {
        using var fixture = new Fixture();

        ConfigurationLoadResult result = fixture.Repository.Load();

        Assert.Equal(ConfigurationLoadStatus.DefaultMissing, result.Status);
        Assert.Equal("builtin.qwerty.en-US", result.Configuration.LayoutId);
        Assert.False(result.Configuration.ShowLauncherButton);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void SaveThenLoadRoundTripsCamelCaseConfiguration()
    {
        using var fixture = new Fixture();
        KeyboardConfiguration expected = new(1, false, true, false, 0.75, 900, 400, 12, "custom.layout", ManualPositionMode.Persistent, true,
            [new("邮箱", "text", "user@example.com"), new("保存", "hotkey", "S", ["Control", "Shift"]),
             new("任务视图", "chord", "", ["LeftWindows", "Tab"])], UiLanguage.SimplifiedChinese);

        ConfigurationSaveResult saved = fixture.Repository.Save(expected);
        ConfigurationLoadResult loaded = fixture.Repository.Load();

        Assert.True(saved.IsSaved);
        Assert.Equal(ConfigurationLoadStatus.Loaded, loaded.Status);
        Assert.Equal(expected.SchemaVersion, loaded.Configuration.SchemaVersion);
        Assert.Equal(expected.Enabled, loaded.Configuration.Enabled);
        Assert.Equal(expected.AutoShow, loaded.Configuration.AutoShow);
        Assert.Equal(expected.AutoHide, loaded.Configuration.AutoHide);
        Assert.Equal(expected.Opacity, loaded.Configuration.Opacity);
        Assert.Equal(expected.KeyboardWidthDip, loaded.Configuration.KeyboardWidthDip);
        Assert.Equal(expected.KeyboardHeightDip, loaded.Configuration.KeyboardHeightDip);
        Assert.Equal(expected.MarginDip, loaded.Configuration.MarginDip);
        Assert.Equal(expected.LayoutId, loaded.Configuration.LayoutId);
        Assert.Equal(expected.ManualPositionMode, loaded.Configuration.ManualPositionMode);
        Assert.Equal(expected.DetailedDiagnostics, loaded.Configuration.DetailedDiagnostics);
        Assert.Equal(UiLanguage.SimplifiedChinese, loaded.Configuration.UiLanguage);
        Assert.Equal(3, loaded.Configuration.CustomKeys.Count);
        Assert.Equal("邮箱", loaded.Configuration.CustomKeys[0].Label);
        Assert.Equal("user@example.com", loaded.Configuration.CustomKeys[0].Input);
        Assert.Equal(["Control", "Shift"], loaded.Configuration.CustomKeys[1].Modifiers);
        Assert.Empty(loaded.Configuration.CustomKeys[2].Input);
        Assert.Equal(["LeftWindows", "Tab"], loaded.Configuration.CustomKeys[2].Modifiers);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(fixture.ConfigurationFile));
        Assert.True(json.RootElement.TryGetProperty("schemaVersion", out _));
        Assert.True(json.RootElement.TryGetProperty("keyboardWidthDip", out _));
    }

    [Fact]
    public void OlderSchemaOneFileWithoutCustomKeyFieldsLoadsWithDisabledCustomKey()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigurationFile, """
            {"schemaVersion":1,"enabled":true,"autoShow":true,"autoHide":true,"opacity":0.9,
             "keyboardWidthDip":800,"keyboardHeightDip":300,"marginDip":8,"layoutId":"layout",
             "manualPositionMode":"UntilTargetChanges","detailedDiagnostics":false}
            """);

        ConfigurationLoadResult loaded = fixture.Repository.Load();

        Assert.Equal(ConfigurationLoadStatus.Loaded, loaded.Status);
        Assert.Empty(loaded.Configuration.CustomKeys);
        Assert.Equal(UiLanguage.English, loaded.Configuration.UiLanguage);
        Assert.False(loaded.Configuration.ShowLauncherButton);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LauncherModeRoundTripsAsBoolean(bool enabled)
    {
        using var fixture = new Fixture();
        var configuration = new KeyboardConfiguration(1, true, true, true, 0.9, 800, 300, 8,
            "builtin.qwerty.en-US", ManualPositionMode.UntilTargetChanges, false, showLauncherButton: enabled);
        Assert.True(fixture.Repository.Save(configuration).IsSaved);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(fixture.ConfigurationFile));
        Assert.Equal(enabled, json.RootElement.GetProperty("showLauncherButton").GetBoolean());
        Assert.Equal(enabled, fixture.Repository.Load().Configuration.ShowLauncherButton);
    }

    [Theory]
    [InlineData("\"yes\"")]
    [InlineData("1")]
    [InlineData("{}")]
    [InlineData("null")]
    public void InvalidLauncherModeIsRecovered(string value)
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Repository.Save(ConfigurationDefaults.Create()).IsSaved);
        string json = File.ReadAllText(fixture.ConfigurationFile);
        File.WriteAllText(fixture.ConfigurationFile, json.Replace("\"showLauncherButton\": false", $"\"showLauncherButton\": {value}", StringComparison.Ordinal));
        ConfigurationLoadResult result = fixture.Repository.Load();
        Assert.Equal(ConfigurationLoadStatus.RecoveredInvalid, result.Status);
        Assert.False(result.Configuration.ShowLauncherButton);
    }

    [Fact]
    public void LegacySingleCustomTextKeyIsMigratedInMemory()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigurationFile, """
            {"schemaVersion":1,"enabled":true,"autoShow":true,"autoHide":true,"opacity":0.9,
             "keyboardWidthDip":800,"keyboardHeightDip":300,"marginDip":8,"layoutId":"layout",
             "manualPositionMode":"UntilTargetChanges","detailedDiagnostics":false,
             "customKeyLabel":"邮箱","customKeyText":"user@example.com"}
            """);

        CustomKeyConfiguration migrated = Assert.Single(fixture.Repository.Load().Configuration.CustomKeys);

        Assert.Equal("邮箱", migrated.Label);
        Assert.Equal("text", migrated.ActionType);
        Assert.Equal("user@example.com", migrated.Input);
    }

    [Fact]
    public void InvalidJsonIsCopiedToRecoveryAndDefaultsAreReturned()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigurationFile, "{ invalid", Encoding.UTF8);

        ConfigurationLoadResult result = fixture.Repository.Load();

        Assert.Equal(ConfigurationLoadStatus.RecoveredInvalid, result.Status);
        Assert.Equal("builtin.qwerty.en-US", result.Configuration.LayoutId);
        Assert.NotNull(result.RecoveryFileName);
        Assert.True(File.Exists(Path.Combine(fixture.RecoveryDirectory, result.RecoveryFileName!)));
        Assert.Contains(result.Issues, issue => issue.Code == "json.invalid");
    }

    [Fact]
    public void SchemaInvalidFileIsRecoveredAndDoesNotEchoLayoutId()
    {
        using var fixture = new Fixture();
        string sensitive = new('X', ConfigurationSchemaLimits.MaximumLayoutIdLength + 1);
        KeyboardConfiguration invalid = new(1, true, true, true, 0.9, 800, 300, 8, sensitive, ManualPositionMode.UntilTargetChanges, false);
        File.WriteAllText(fixture.ConfigurationFile, JsonSerializer.Serialize(invalid, JsonOptions));

        ConfigurationLoadResult result = fixture.Repository.Load();

        Assert.Equal(ConfigurationLoadStatus.RecoveredInvalid, result.Status);
        Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains('X'));
    }

    [Fact]
    public void SaveRejectsInvalidConfigurationWithoutWritingFile()
    {
        using var fixture = new Fixture();
        KeyboardConfiguration invalid = new(1, true, true, true, 0.1, 800, 300, 8, "layout", ManualPositionMode.UntilTargetChanges, false);

        ConfigurationSaveResult result = fixture.Repository.Save(invalid);

        Assert.False(result.IsSaved);
        Assert.False(File.Exists(fixture.ConfigurationFile));
        Assert.Contains(result.Issues, issue => issue.Path == "$.opacity");
    }

    [Fact]
    public void SaveFailureKeepsValidatedMemoryConfiguration()
    {
        using var fixture = new Fixture();
        KeyboardConfiguration expected = ConfigurationDefaults.Create();
        Directory.CreateDirectory(fixture.ConfigurationFile);

        ConfigurationSaveResult result = fixture.Repository.Save(expected);

        Assert.False(result.IsSaved);
        Assert.Equal(expected, fixture.Repository.Current);
    }

    [Fact]
    public void Utf8BomAndUnknownForwardCompatibleFieldAreAccepted()
    {
        using var fixture = new Fixture();
        string json = """
            {"schemaVersion":1,"enabled":true,"autoShow":true,"autoHide":true,"opacity":0.9,
             "keyboardWidthDip":800,"keyboardHeightDip":300,"marginDip":8,"layoutId":"layout",
             "manualPositionMode":"UntilTargetChanges","detailedDiagnostics":false,"future":true}
            """;
        File.WriteAllText(fixture.ConfigurationFile, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal(ConfigurationLoadStatus.Loaded, fixture.Repository.Load().Status);
    }

    [Fact]
    public void SaveThenLoadRoundTripsAutoStart()
    {
        using var fixture = new Fixture();
        KeyboardConfiguration expected = new(1, false, true, false, 0.75, 900, 400, 12, "custom.layout", ManualPositionMode.Persistent, true,
            [new("邮箱", "text", "user@example.com")], UiLanguage.SimplifiedChinese, autoStart: true);

        ConfigurationSaveResult saved = fixture.Repository.Save(expected);
        ConfigurationLoadResult loaded = fixture.Repository.Load();

        Assert.True(saved.IsSaved);
        Assert.Equal(ConfigurationLoadStatus.Loaded, loaded.Status);
        Assert.True(loaded.Configuration.AutoStart);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(fixture.ConfigurationFile));
        Assert.True(json.RootElement.GetProperty("autoStart").GetBoolean());
    }

    [Fact]
    public void OlderFileWithoutAutoStartDefaultsToOff()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigurationFile, """
            {"schemaVersion":1,"enabled":true,"autoShow":true,"autoHide":true,"opacity":0.9,
             "keyboardWidthDip":800,"keyboardHeightDip":300,"marginDip":8,"layoutId":"layout",
             "manualPositionMode":"UntilTargetChanges","detailedDiagnostics":false}
            """);

        ConfigurationLoadResult loaded = fixture.Repository.Load();

        Assert.Equal(ConfigurationLoadStatus.Loaded, loaded.Status);
        Assert.False(loaded.Configuration.AutoStart);
        Assert.False(ConfigurationDefaults.Create().AutoStart);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"VirtualKeyboard.ConfigTests.{Guid.NewGuid():N}");
        public Fixture()
        {
            ConfigurationFile = Path.Combine(_root, "VirtualKeyboard", "config.json");
            RecoveryDirectory = Path.Combine(_root, "VirtualKeyboard", "recovery");
            Repository = new(new(ConfigurationFile, RecoveryDirectory));
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationFile)!);
        }
        public string ConfigurationFile { get; }
        public string RecoveryDirectory { get; }
        public ConfigurationRepository Repository { get; }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    }
}
