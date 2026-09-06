using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualKeyboard.Core.Layouts;

namespace VirtualKeyboard.Core.Configuration;

public enum ConfigurationLoadStatus
{
    Loaded,
    DefaultMissing,
    RecoveredInvalid,
}

public sealed record ConfigurationIssue(string Path, string Code, string Message);

public sealed record ConfigurationLoadResult(
    ConfigurationLoadStatus Status,
    KeyboardConfiguration Configuration,
    IReadOnlyList<ConfigurationIssue> Issues,
    string? RecoveryFileName);

public sealed record ConfigurationSaveResult(bool IsSaved, IReadOnlyList<ConfigurationIssue> Issues);

public sealed record ConfigurationRepositoryPaths(string ConfigurationFile, string RecoveryDirectory)
{
    public static ConfigurationRepositoryPaths CreateDefault()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VirtualKeyboard");
        return new(Path.Combine(root, "config.json"), Path.Combine(root, "recovery"));
    }
}

public static class ConfigurationDefaults
{
    public static KeyboardConfiguration Create() => new(
        ConfigurationSchemaLimits.SupportedSchemaVersion,
        enabled: true,
        autoShow: true,
        autoHide: true,
        opacity: 0.9,
        keyboardWidthDip: 800,
        keyboardHeightDip: 300,
        marginDip: 8,
        layoutId: "builtin.qwerty.en-US",
        manualPositionMode: ManualPositionMode.UntilTargetChanges,
        detailedDiagnostics: false,
        customKeys: [],
        uiLanguage: UiLanguage.English);
}

/// <summary>Loads recoverably and saves validated configuration with a flushed same-directory replacement.</summary>
public sealed class ConfigurationRepository
{
    private const int MaximumConfigurationBytes = 64 * 1024;
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        AllowTrailingCommas = false,
        MaxDepth = 8,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly string _configurationFile;
    private readonly string _recoveryDirectory;
    private KeyboardConfiguration _current = ConfigurationDefaults.Create();

    public ConfigurationRepository(ConfigurationRepositoryPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(paths.ConfigurationFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(paths.RecoveryDirectory);
        _configurationFile = Path.GetFullPath(paths.ConfigurationFile);
        _recoveryDirectory = Path.GetFullPath(paths.RecoveryDirectory);
    }

    public KeyboardConfiguration Current
    {
        get { lock (_gate) return _current; }
    }

    public ConfigurationLoadResult Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_configurationFile))
            {
                _current = ConfigurationDefaults.Create();
                return new(ConfigurationLoadStatus.DefaultMissing, _current, EmptyIssues(), null);
            }

            if (TryLoadValid(out KeyboardConfiguration? loaded, out List<ConfigurationIssue> issues))
            {
                _current = loaded!;
                return new(ConfigurationLoadStatus.Loaded, _current, Snapshot(issues), null);
            }

            string? recovery = TryRecoverInvalidFile(issues);
            _current = ConfigurationDefaults.Create();
            return new(ConfigurationLoadStatus.RecoveredInvalid, _current, Snapshot(issues), recovery);
        }
    }

    public ConfigurationSaveResult Save(KeyboardConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        lock (_gate)
        {
            ConfigurationValidationResult validation = ConfigurationValidator.Validate(configuration);
            if (!validation.IsValid)
            {
                return new(false, Snapshot(validation.Errors.Select(error => new ConfigurationIssue(error.Path, error.Code, error.Message))));
            }

            // A valid user choice remains active even if persistence is temporarily unavailable.
            _current = configuration;
            string directory = Path.GetDirectoryName(_configurationFile)!;
            string temporary = Path.Combine(directory, $".config.{Guid.NewGuid():N}.tmp");
            try
            {
                Directory.CreateDirectory(directory);
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(configuration, WriteOptions);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(json);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(_configurationFile))
                {
                    File.Replace(temporary, _configurationFile, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporary, _configurationFile);
                }
                return new(true, EmptyIssues());
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                TryDeleteTemporary(temporary);
                return new(false, Snapshot([new("$", "config.saveFailed", "Configuration could not be saved.")]));
            }
        }
    }

    private bool TryLoadValid(out KeyboardConfiguration? configuration, out List<ConfigurationIssue> issues)
    {
        configuration = null;
        issues = [];
        if (!TryReadBounded(out byte[] bytes, out string code))
        {
            issues.Add(new("$", code, "Configuration could not be read."));
            return false;
        }

        try
        {
            int offset = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
            RawConfiguration? raw = JsonSerializer.Deserialize<RawConfiguration>(bytes.AsSpan(offset), ReadOptions);
            if (raw is null)
            {
                issues.Add(new("$", "json.root", "Configuration JSON must be an object."));
                return false;
            }
            if (!raw.HasAllRequiredValues)
            {
                issues.Add(new("$", "config.requiredFields", "Configuration contains missing or invalid required fields."));
                return false;
            }

            configuration = raw.ToConfiguration();
            ConfigurationValidationResult validation = ConfigurationValidator.Validate(configuration);
            issues.AddRange(validation.Errors.Select(error => new ConfigurationIssue(error.Path, error.Code, error.Message)));
            return validation.IsValid;
        }
        catch (JsonException exception)
        {
            issues.Add(new(SanitizePath(exception.Path), "json.invalid", "Configuration JSON is malformed."));
            return false;
        }
    }

    private bool TryReadBounded(out byte[] bytes, out string code)
    {
        bytes = [];
        code = "config.readFailed";
        try
        {
            using FileStream stream = new(_configurationFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            if (stream.Length > MaximumConfigurationBytes)
            {
                code = "config.tooLarge";
                return false;
            }
            using var buffer = new MemoryStream((int)stream.Length);
            byte[] chunk = new byte[4096];
            while (buffer.Length <= MaximumConfigurationBytes)
            {
                int read = stream.Read(chunk, 0, Math.Min(chunk.Length, MaximumConfigurationBytes + 1 - (int)buffer.Length));
                if (read == 0)
                {
                    bytes = buffer.ToArray();
                    return true;
                }
                buffer.Write(chunk, 0, read);
            }
            code = "config.tooLarge";
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string? TryRecoverInvalidFile(List<ConfigurationIssue> issues)
    {
        try
        {
            Directory.CreateDirectory(_recoveryDirectory);
            string name = $"config.invalid.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.json";
            File.Copy(_configurationFile, Path.Combine(_recoveryDirectory, name), overwrite: false);
            return name;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            issues.Add(new("$", "config.recoveryFailed", "Invalid configuration could not be copied to recovery."));
            return null;
        }
    }

    private static void TryDeleteTemporary(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static ReadOnlyCollection<ConfigurationIssue> EmptyIssues() => Array.AsReadOnly(Array.Empty<ConfigurationIssue>());
    private static ReadOnlyCollection<ConfigurationIssue> Snapshot(IEnumerable<ConfigurationIssue> issues) => Array.AsReadOnly(issues.ToArray());

    private static string SanitizePath(string? path) =>
        !string.IsNullOrEmpty(path) && path.Length <= 256 && path.All(character => char.IsAsciiLetterOrDigit(character) || character is '$' or '.' or '[' or ']')
            ? path
            : "$";

    private sealed class RawConfiguration
    {
        public int? SchemaVersion { get; set; }
        public bool? Enabled { get; set; }
        public bool? AutoShow { get; set; }
        public bool? AutoHide { get; set; }
        public double? Opacity { get; set; }
        public double? KeyboardWidthDip { get; set; }
        public double? KeyboardHeightDip { get; set; }
        public double? MarginDip { get; set; }
        public string? LayoutId { get; set; }
        public ManualPositionMode? ManualPositionMode { get; set; }
        public bool? DetailedDiagnostics { get; set; }
        public string? CustomKeyLabel { get; set; }
        public string? CustomKeyText { get; set; }
        public List<RawCustomKey?>? CustomKeys { get; set; }
        public UiLanguage? UiLanguage { get; set; }

        [JsonIgnore]
        public bool HasAllRequiredValues => SchemaVersion.HasValue && Enabled.HasValue && AutoShow.HasValue && AutoHide.HasValue &&
            Opacity.HasValue && KeyboardWidthDip.HasValue && KeyboardHeightDip.HasValue && MarginDip.HasValue &&
            LayoutId is not null && ManualPositionMode.HasValue && DetailedDiagnostics.HasValue &&
            (CustomKeys is null || CustomKeys.All(static key => key?.HasAllRequiredValues == true));

        public KeyboardConfiguration ToConfiguration()
        {
            IEnumerable<CustomKeyConfiguration> customKeys = CustomKeys is not null
                ? CustomKeys.Select(static key => key!.ToConfiguration())
                : !string.IsNullOrWhiteSpace(CustomKeyLabel) && !string.IsNullOrEmpty(CustomKeyText)
                    ? [new(CustomKeyLabel, LayoutActionTypes.Text, CustomKeyText)]
                    : [];
            return new(
                SchemaVersion!.Value, Enabled!.Value, AutoShow!.Value, AutoHide!.Value, Opacity!.Value,
                KeyboardWidthDip!.Value, KeyboardHeightDip!.Value, MarginDip!.Value, LayoutId,
                ManualPositionMode!.Value, DetailedDiagnostics!.Value, customKeys,
                UiLanguage ?? global::VirtualKeyboard.Core.Configuration.UiLanguage.English);
        }
    }

    private sealed class RawCustomKey
    {
        public string? Label { get; set; }
        public string? ActionType { get; set; }
        public string? Input { get; set; }
        public List<string>? Modifiers { get; set; }

        [JsonIgnore]
        public bool HasAllRequiredValues => Label is not null && ActionType is not null && Input is not null;

        public CustomKeyConfiguration ToConfiguration() => new(Label, ActionType, Input, Modifiers);
    }
}
