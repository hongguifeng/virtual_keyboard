using System.Text;
using VirtualKeyboard.Core.Layouts;

namespace VirtualKeyboard.Core.Tests.Layouts;

public sealed class LayoutRepositoryTests
{
    [Fact]
    public void PackagedQwertyLayoutIsValidAndContainsRequiredKeys()
    {
        string builtInDirectory = Path.Combine(AppContext.BaseDirectory, "layouts", "builtin");
        using var fixture = new RepositoryFixture(builtInDirectory);

        KeyboardLayoutDefinition layout = Assert.Single(fixture.Repository.Reload().Layouts).Value;
        IReadOnlyList<KeyboardLayoutRow> rows = layout.Rows!;
        KeyboardKeyDefinition[] keys = rows.SelectMany(row => row.Keys!).ToArray();

        Assert.Equal("builtin.qwerty.en-US", layout.Id);
        Assert.Equal(26, keys.Count(key => key.Id is { Length: 5 } && key.Id.StartsWith("key.", StringComparison.Ordinal) && char.IsAsciiLetterLower(key.Id[4])));
        Assert.Equal(10, keys.Count(key => key.Id is { Length: 5 } && char.IsAsciiDigit(key.Id[4])));
        Assert.All(
            keys.Where(key => key.Id is { Length: 5 } && char.IsAsciiLetterOrDigit(key.Id[4])),
            key => Assert.Equal(LayoutActionTypes.Key, key.Action!.Type));
        Assert.Contains(keys, key => key.Action?.VirtualKey == "Space" || key.Action?.Value == " ");
        foreach (string id in new[] { "key.backspace", "key.enter", "key.tab", "key.escape", "key.shift", "key.control", "key.alt", "key.windows", "key.fn", "key.left", "key.up", "key.down", "key.right", "key.capsLock" })
        {
            Assert.Contains(keys, key => key.Id == id);
        }
        Assert.Equal(
            Enumerable.Range(1, 12).Select(number => $"F{number}"),
            keys.Where(key => key.Action?.FnVirtualKey is not null).Select(key => key.Action!.FnVirtualKey));
        Assert.Equal(
            ["key.escape", "key.grave", "key.1", "key.2", "key.3", "key.4", "key.5", "key.6", "key.7", "key.8", "key.9", "key.0", "key.minus", "key.equals", "key.backspace"],
            rows[0].Keys!.Select(key => key.Id));
        Assert.Equal(
            ["key.tab", "key.q", "key.w", "key.e", "key.r", "key.t", "key.y", "key.u", "key.i", "key.o", "key.p", "key.openBracket", "key.closeBracket", "key.backslash"],
            rows[1].Keys!.Select(key => key.Id));
        Assert.Equal(["key.left", "key.down", "key.right"], rows[4].Keys!.TakeLast(3).Select(key => key.Id));
        Assert.Equal("key.up", rows[3].Keys![^2].Id);
        Assert.Equal("key.delete", rows[3].Keys![^1].Id);
        Assert.Equal(1.2, rows[3].Keys![^3].Width);
        double upCenter = NormalizedCenter(rows[3].Keys!, "key.up");
        double downCenter = NormalizedCenter(rows[4].Keys!, "key.down");
        Assert.InRange(Math.Abs(upCenter - downCenter), 0, 0.01);
        Assert.DoesNotContain(keys, key => key.Id is "key.close" or "key.settings" or "key.drag");
    }

    private static double NormalizedCenter(IReadOnlyList<KeyboardKeyDefinition> keys, string id)
    {
        double preceding = keys.TakeWhile(key => key.Id != id).Sum(key => key.Width);
        KeyboardKeyDefinition key = Assert.Single(keys, key => key.Id == id);
        return (preceding + (key.Width / 2)) / keys.Sum(item => item.Width);
    }

    [Fact]
    public void ReloadLoadsBuiltInAndUserLayoutsInDeterministicOrder()
    {
        using var fixture = new RepositoryFixture();
        fixture.WriteBuiltIn("z.json", ValidJson("builtin.z"));
        fixture.WriteBuiltIn("a.json", ValidJson("builtin.a"));
        fixture.WriteUser("user.json", ValidJson("user.a"));

        LayoutReloadResult result = fixture.Repository.Reload();

        Assert.True(result.IsFullySuccessful);
        Assert.Equal(["builtin.a", "builtin.z", "user.a"], result.Layouts.Keys);
        Assert.Same(result.Layouts, fixture.Repository.Layouts);
    }

    [Fact]
    public void UserLayoutCannotOverrideBuiltInId()
    {
        using var fixture = new RepositoryFixture();
        fixture.WriteBuiltIn("builtin.json", ValidJson("shared"));
        fixture.WriteUser("user.json", ValidJson("shared", name: "User replacement"));

        LayoutReloadResult result = fixture.Repository.Reload();

        KeyboardLayoutDefinition layout = Assert.Single(result.Layouts).Value;
        Assert.Equal("Layout", layout.Name);
        LayoutLoadIssue issue = Assert.Single(result.Issues);
        Assert.Equal(LayoutSourceKind.User, issue.Source);
        Assert.Equal("$.id", issue.Path);
        Assert.Equal("layout.idConflict", issue.Code);
    }

    [Fact]
    public void InvalidReloadRetainsPreviousLayoutForSameFile()
    {
        using var fixture = new RepositoryFixture();
        string file = fixture.WriteUser("custom.json", ValidJson("user.custom"));
        KeyboardLayoutDefinition first = Assert.Single(fixture.Repository.Reload().Layouts).Value;
        File.WriteAllText(file, "{ invalid", Encoding.UTF8);

        LayoutReloadResult result = fixture.Repository.Reload();

        Assert.Same(first, Assert.Single(result.Layouts).Value);
        LayoutLoadIssue issue = Assert.Single(result.Issues);
        Assert.True(issue.RetainedPrevious);
        Assert.Equal("$", issue.Path);
        Assert.Equal("json.invalid", issue.Code);
    }

    [Fact]
    public void RemovedFileRemovesItsCachedLayout()
    {
        using var fixture = new RepositoryFixture();
        string file = fixture.WriteUser("custom.json", ValidJson("user.custom"));
        Assert.Single(fixture.Repository.Reload().Layouts);
        File.Delete(file);

        LayoutReloadResult result = fixture.Repository.Reload();

        Assert.Empty(result.Layouts);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("command")]
    [InlineData("script")]
    [InlineData("unknown")]
    public void ExecutableAndUnknownActionsAreRejected(string actionType)
    {
        using var fixture = new RepositoryFixture();
        fixture.WriteUser("unsafe.json", ValidJson("unsafe", actionType: actionType));

        LayoutReloadResult result = fixture.Repository.Reload();

        Assert.Empty(result.Layouts);
        LayoutLoadIssue issue = Assert.Single(result.Issues);
        Assert.Equal("$.rows[0][0].action.type", issue.Path);
        Assert.Equal("action.type", issue.Code);
        Assert.False(issue.RetainedPrevious);
    }

    [Fact]
    public void ValidationIssueDoesNotEchoTextValue()
    {
        using var fixture = new RepositoryFixture();
        string sensitive = new('Q', LayoutSchemaLimits.MaximumTextLength + 1);
        fixture.WriteUser("private.json", ValidJson("private", textValue: sensitive));

        LayoutReloadResult result = fixture.Repository.Reload();

        LayoutLoadIssue issue = Assert.Single(result.Issues);
        Assert.Equal("$.rows[0][0].action.value", issue.Path);
        Assert.DoesNotContain(sensitive, issue.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('Q', issue.Message);
    }

    [Fact]
    public void MissingSafeForPasswordReportsExactFieldPath()
    {
        using var fixture = new RepositoryFixture();
        fixture.WriteUser("missing.json", """
            {"schemaVersion":1,"id":"missing","name":"Layout","culture":"en-US","rows":[[
              {"id":"key.a","label":"A","width":1,"action":{"type":"text","value":"a"}}
            ]]}
            """);

        LayoutLoadIssue issue = Assert.Single(fixture.Repository.Reload().Issues);

        Assert.Equal("$.rows[0][0].safeForPassword", issue.Path);
        Assert.Equal("key.safeForPassword", issue.Code);
    }

    [Fact]
    public void UnknownJsonFieldRejectsWholeLayout()
    {
        using var fixture = new RepositoryFixture();
        fixture.WriteUser("unknown-field.json", ValidJson("unknown").Replace(
            "\"rows\":",
            "\"surprise\": true, \"rows\":",
            StringComparison.Ordinal));

        LayoutReloadResult result = fixture.Repository.Reload();

        Assert.Empty(result.Layouts);
        Assert.Equal("json.invalid", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void OversizedFileIsRejectedBeforeJsonParsing()
    {
        using var fixture = new RepositoryFixture();
        string path = Path.Combine(fixture.UserDirectory, "large.json");
        File.WriteAllBytes(path, new byte[(1024 * 1024) + 1]);

        LayoutLoadIssue issue = Assert.Single(fixture.Repository.Reload().Issues);

        Assert.Equal("file.tooLarge", issue.Code);
        Assert.Empty(fixture.Repository.Layouts);
    }

    [Fact]
    public void DefaultPathsUseReadOnlyInstallAndCurrentUserLocations()
    {
        string applicationDirectory = Path.Combine(Path.GetTempPath(), "vk-app");

        LayoutRepositoryPaths paths = LayoutRepositoryPaths.CreateDefault(applicationDirectory);

        Assert.Equal(Path.GetFullPath(Path.Combine(applicationDirectory, "layouts", "builtin")), paths.BuiltInDirectory);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VirtualKeyboard",
                "layouts")),
            paths.UserDirectory);
    }

    private static string ValidJson(
        string id,
        string name = "Layout",
        string actionType = LayoutActionTypes.Text,
        string textValue = "a") => $$"""
        {
          "schemaVersion": 1,
          "id": "{{id}}",
          "name": "{{name}}",
          "culture": "en-US",
          "rows": [[{
            "id": "key.a",
            "label": "A",
            "width": 1,
            "safeForPassword": true,
            "action": { "type": "{{actionType}}", "value": "{{textValue}}" }
          }]]
        }
        """;

    private sealed class RepositoryFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"VirtualKeyboard.Tests.{Guid.NewGuid():N}");

        public RepositoryFixture(string? builtInDirectory = null)
        {
            BuiltInDirectory = builtInDirectory ?? Path.Combine(_root, "app", "layouts", "builtin");
            UserDirectory = Path.Combine(_root, "local", "VirtualKeyboard", "layouts");
            Directory.CreateDirectory(BuiltInDirectory);
            Directory.CreateDirectory(UserDirectory);
            Repository = new(new(BuiltInDirectory, UserDirectory));
        }

        public string BuiltInDirectory { get; }

        public string UserDirectory { get; }

        public LayoutRepository Repository { get; }

        public string WriteBuiltIn(string fileName, string json) => Write(BuiltInDirectory, fileName, json);

        public string WriteUser(string fileName, string json) => Write(UserDirectory, fileName, json);

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private static string Write(string directory, string fileName, string json)
        {
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, json, Encoding.UTF8);
            return path;
        }
    }
}
