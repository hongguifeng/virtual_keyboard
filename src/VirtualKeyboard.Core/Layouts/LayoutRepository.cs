using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VirtualKeyboard.Core.Layouts;

public enum LayoutSourceKind
{
    BuiltIn,
    User,
}

public sealed record LayoutLoadIssue(
    LayoutSourceKind Source,
    string FileName,
    string Path,
    string Code,
    string Message,
    bool RetainedPrevious);

public sealed class LayoutReloadResult
{
    internal LayoutReloadResult(
        IReadOnlyDictionary<string, KeyboardLayoutDefinition> layouts,
        IEnumerable<LayoutLoadIssue> issues)
    {
        Layouts = layouts;
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public IReadOnlyDictionary<string, KeyboardLayoutDefinition> Layouts { get; }

    public IReadOnlyList<LayoutLoadIssue> Issues { get; }

    public bool IsFullySuccessful => Issues.Count == 0;
}

public sealed record LayoutRepositoryPaths(string BuiltInDirectory, string UserDirectory)
{
    public static LayoutRepositoryPaths CreateDefault(string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new(
            Path.GetFullPath(Path.Combine(applicationBaseDirectory, "layouts", "builtin")),
            Path.GetFullPath(Path.Combine(localData, "VirtualKeyboard", "layouts")));
    }
}

/// <summary>Loads validated layout snapshots while retaining the last valid value for a rejected file.</summary>
public sealed class LayoutRepository
{
    private const int MaximumLayoutFileBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = false,
        MaxDepth = 16,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string _builtInDirectory;
    private readonly string _userDirectory;
    private readonly object _reloadGate = new();
    private Dictionary<string, CachedLayout> _lastValidByFile = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, KeyboardLayoutDefinition> _layouts =
        new ReadOnlyDictionary<string, KeyboardLayoutDefinition>(
            new Dictionary<string, KeyboardLayoutDefinition>(StringComparer.Ordinal));

    public LayoutRepository(LayoutRepositoryPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(paths.BuiltInDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(paths.UserDirectory);
        _builtInDirectory = Path.GetFullPath(paths.BuiltInDirectory);
        _userDirectory = Path.GetFullPath(paths.UserDirectory);
    }

    public IReadOnlyDictionary<string, KeyboardLayoutDefinition> Layouts => Volatile.Read(ref _layouts);

    public LayoutReloadResult Reload()
    {
        lock (_reloadGate)
        {
            return ReloadCore();
        }
    }

    private LayoutReloadResult ReloadCore()
    {
        var issues = new List<LayoutLoadIssue>();
        var nextCache = new Dictionary<string, CachedLayout>(StringComparer.OrdinalIgnoreCase);
        var accepted = new Dictionary<string, KeyboardLayoutDefinition>(StringComparer.Ordinal);

        LoadDirectory(LayoutSourceKind.BuiltIn, _builtInDirectory, accepted, nextCache, issues);
        LoadDirectory(LayoutSourceKind.User, _userDirectory, accepted, nextCache, issues);

        _lastValidByFile = nextCache;
        IReadOnlyDictionary<string, KeyboardLayoutDefinition> snapshot =
            new ReadOnlyDictionary<string, KeyboardLayoutDefinition>(accepted);
        Volatile.Write(ref _layouts, snapshot);
        return new LayoutReloadResult(snapshot, issues);
    }

    private void LoadDirectory(
        LayoutSourceKind source,
        string directory,
        Dictionary<string, KeyboardLayoutDefinition> accepted,
        Dictionary<string, CachedLayout> nextCache,
        List<LayoutLoadIssue> issues)
    {
        string[] files;
        try
        {
            files = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                : [];
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            bool retained = RetainDirectoryCache(source, accepted, nextCache);
            issues.Add(new(source, string.Empty, "$", "directory.unavailable", "The layout directory could not be read.", retained));
            return;
        }

        foreach (string file in files)
        {
            LoadFile(source, file, accepted, nextCache, issues);
        }
    }

    private void LoadFile(
        LayoutSourceKind source,
        string file,
        Dictionary<string, KeyboardLayoutDefinition> accepted,
        Dictionary<string, CachedLayout> nextCache,
        List<LayoutLoadIssue> issues)
    {
        string canonicalPath = Path.GetFullPath(file);
        if (!TryReadBounded(canonicalPath, out byte[] bytes, out string readCode))
        {
            RetainFileOrReport(source, canonicalPath, "$", readCode, "The layout file could not be read.", accepted, nextCache, issues);
            return;
        }

        if (!TryDeserialize(bytes, out KeyboardLayoutDefinition? layout, out LayoutValidationError? parseError))
        {
            RetainFileOrReport(source, canonicalPath, parseError!.Path, parseError.Code, parseError.Message, accepted, nextCache, issues);
            return;
        }

        LayoutValidationResult validation = LayoutValidator.Validate(layout);
        if (!validation.IsValid)
        {
            bool retained = TryRetainFile(source, canonicalPath, accepted, nextCache);
            foreach (LayoutValidationError error in validation.Errors)
            {
                issues.Add(new(source, Path.GetFileName(canonicalPath), error.Path, error.Code, error.Message, retained));
            }
            return;
        }

        if (accepted.ContainsKey(layout!.Id!))
        {
            RetainFileOrReport(source, canonicalPath, "$.id", "layout.idConflict", "The layout ID conflicts with an already loaded layout.", accepted, nextCache, issues);
            return;
        }

        var cached = new CachedLayout(source, canonicalPath, layout);
        accepted.Add(layout.Id!, layout);
        nextCache.Add(canonicalPath, cached);
    }

    private void RetainFileOrReport(
        LayoutSourceKind source,
        string canonicalPath,
        string path,
        string code,
        string message,
        Dictionary<string, KeyboardLayoutDefinition> accepted,
        Dictionary<string, CachedLayout> nextCache,
        List<LayoutLoadIssue> issues)
    {
        bool retained = TryRetainFile(source, canonicalPath, accepted, nextCache);

        issues.Add(new(source, Path.GetFileName(canonicalPath), path, code, message, retained));
    }

    private bool TryRetainFile(
        LayoutSourceKind source,
        string canonicalPath,
        Dictionary<string, KeyboardLayoutDefinition> accepted,
        Dictionary<string, CachedLayout> nextCache)
    {
        if (!_lastValidByFile.TryGetValue(canonicalPath, out CachedLayout? cached) ||
            cached.Source != source ||
            accepted.ContainsKey(cached.Layout.Id!))
        {
            return false;
        }

        accepted.Add(cached.Layout.Id!, cached.Layout);
        nextCache[canonicalPath] = cached;
        return true;
    }

    private bool RetainDirectoryCache(
        LayoutSourceKind source,
        Dictionary<string, KeyboardLayoutDefinition> accepted,
        Dictionary<string, CachedLayout> nextCache)
    {
        bool retained = false;
        foreach (CachedLayout cached in _lastValidByFile.Values.Where(item => item.Source == source))
        {
            if (!accepted.ContainsKey(cached.Layout.Id!))
            {
                accepted.Add(cached.Layout.Id!, cached.Layout);
                nextCache[cached.CanonicalPath] = cached;
                retained = true;
            }
        }

        return retained;
    }

    private static bool TryReadBounded(string path, out byte[] bytes, out string errorCode)
    {
        bytes = [];
        errorCode = "file.unavailable";
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            if (stream.Length > MaximumLayoutFileBytes)
            {
                errorCode = "file.tooLarge";
                return false;
            }

            using var buffer = new MemoryStream((int)stream.Length);
            byte[] chunk = new byte[8192];
            while (buffer.Length <= MaximumLayoutFileBytes)
            {
                int remaining = MaximumLayoutFileBytes + 1 - (int)buffer.Length;
                int read = stream.Read(chunk, 0, Math.Min(chunk.Length, remaining));
                if (read == 0)
                {
                    bytes = buffer.ToArray();
                    return true;
                }
                buffer.Write(chunk, 0, read);
            }

            errorCode = "file.tooLarge";
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryDeserialize(
        byte[] bytes,
        out KeyboardLayoutDefinition? layout,
        out LayoutValidationError? error)
    {
        layout = null;
        error = null;
        try
        {
            int offset = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
            RawLayout? raw = JsonSerializer.Deserialize<RawLayout>(bytes.AsSpan(offset), JsonOptions);
            if (raw is null)
            {
                error = new("$", "json.root", "The JSON root must be an object.");
                return false;
            }

            layout = Convert(raw, out error);
            return error is null;
        }
        catch (JsonException exception)
        {
            error = new(SanitizeJsonPath(exception.Path), "json.invalid", "The layout JSON is malformed or contains an unknown field.");
            return false;
        }
    }

    private static KeyboardLayoutDefinition? Convert(RawLayout raw, out LayoutValidationError? error)
    {
        error = null;
        var rows = new List<KeyboardLayoutRow>();
        if (raw.Rows is not null)
        {
            for (int rowIndex = 0; rowIndex < raw.Rows.Count; rowIndex++)
            {
                List<RawKey?>? rawRow = raw.Rows[rowIndex];
                if (rawRow is null)
                {
                    rows.Add(new KeyboardLayoutRow(null));
                    continue;
                }

                var keys = new List<KeyboardKeyDefinition>();
                for (int keyIndex = 0; keyIndex < rawRow.Count; keyIndex++)
                {
                    RawKey? rawKey = rawRow[keyIndex];
                    string keyPath = $"$.rows[{rowIndex}][{keyIndex}]";
                    if (rawKey is null)
                    {
                        error = new(keyPath, "key.required", "A key is required.");
                        return null;
                    }
                    if (!rawKey.SafeForPassword.HasValue)
                    {
                        error = new($"{keyPath}.safeForPassword", "key.safeForPassword", "safeForPassword is required and must be a boolean.");
                        return null;
                    }

                    LayoutActionDefinition? action = Convert(rawKey.Action);
                    keys.Add(new(rawKey.Id, rawKey.Label, rawKey.Width ?? double.NaN, rawKey.SafeForPassword.Value, action));
                }
                rows.Add(new(keys));
            }
        }

        return new(raw.SchemaVersion ?? int.MinValue, raw.Id, raw.Name, raw.Culture, raw.Rows is null ? null : rows);
    }

    private static LayoutActionDefinition? Convert(RawAction? action) => action is null
        ? null
        : new(action.Type, action.Value, action.VirtualKey, action.ScanCode, action.Modifiers?.Select(value => value!), action.Modifier, action.FnVirtualKey,
            action.Keys?.Select(value => value!));

    private static string SanitizeJsonPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 256)
        {
            return "$";
        }

        return path.All(character => char.IsAsciiLetterOrDigit(character) || character is '$' or '.' or '[' or ']')
            ? path
            : "$";
    }

    private sealed record CachedLayout(LayoutSourceKind Source, string CanonicalPath, KeyboardLayoutDefinition Layout);

    private sealed class RawLayout
    {
        [JsonPropertyName("schemaVersion")]
        public int? SchemaVersion { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("culture")]
        public string? Culture { get; set; }

        [JsonPropertyName("rows")]
        public List<List<RawKey?>?>? Rows { get; set; }
    }

    private sealed class RawKey
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("width")]
        public double? Width { get; set; }

        [JsonPropertyName("safeForPassword")]
        public bool? SafeForPassword { get; set; }

        [JsonPropertyName("action")]
        public RawAction? Action { get; set; }
    }

    private sealed class RawAction
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("value")]
        public string? Value { get; set; }

        [JsonPropertyName("virtualKey")]
        public string? VirtualKey { get; set; }

        [JsonPropertyName("scanCode")]
        public int? ScanCode { get; set; }

        [JsonPropertyName("modifiers")]
        public List<string?>? Modifiers { get; set; }

        [JsonPropertyName("modifier")]
        public string? Modifier { get; set; }

        [JsonPropertyName("fnVirtualKey")]
        public string? FnVirtualKey { get; set; }

        [JsonPropertyName("keys")]
        public List<string?>? Keys { get; set; }
    }
}
