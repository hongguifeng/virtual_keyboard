using VirtualKeyboard.Core.Layouts;
using System.Globalization;

namespace VirtualKeyboard.Core.Tests.Layouts;

public sealed class LayoutValidatorTests
{
    [Fact]
    public void AllAllowlistedActionShapesAreValid()
    {
        KeyboardLayoutDefinition layout = Layout(
            Key("text", new(LayoutActionTypes.Text, value: "Hello")),
            Key("key-vk", new(LayoutActionTypes.Key, virtualKey: "Enter")),
            Key("key-scan", new(LayoutActionTypes.Key, scanCode: 30)),
            Key("hotkey", new(LayoutActionTypes.Hotkey, virtualKey: "A", modifiers: ["Control", "Shift"])),
            Key("chord", new(LayoutActionTypes.Chord, keys: ["LeftWindows", "Tab", "A"])),
            Key("modifier", new(LayoutActionTypes.Modifier, modifier: "CapsLock")));

        LayoutValidationResult result = LayoutValidator.Validate(layout);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ConstructorSnapshotsRowsKeysAndModifiers()
    {
        var modifiers = new List<string> { "Control" };
        var chordKeys = new List<string> { "LeftWindows", "Tab" };
        var keys = new List<KeyboardKeyDefinition>
        {
            Key("hotkey", new(LayoutActionTypes.Hotkey, virtualKey: "A", modifiers: modifiers)),
            Key("chord", new(LayoutActionTypes.Chord, keys: chordKeys)),
        };
        var rows = new List<KeyboardLayoutRow> { new(keys) };
        var layout = new KeyboardLayoutDefinition(1, "layout", "Layout", "en-US", rows);

        modifiers.Add("Shift");
        chordKeys.Add("A");
        keys.Clear();
        rows.Clear();

        IReadOnlyList<KeyboardLayoutRow> storedRows = Assert.IsAssignableFrom<IReadOnlyList<KeyboardLayoutRow>>(layout.Rows);
        KeyboardLayoutRow storedRow = Assert.Single(storedRows);
        IReadOnlyList<KeyboardKeyDefinition> storedKeys = Assert.IsAssignableFrom<IReadOnlyList<KeyboardKeyDefinition>>(storedRow.Keys);
        Assert.Equal(["Control"], storedKeys[0].Action!.Modifiers);
        Assert.Equal(["LeftWindows", "Tab"], storedKeys[1].Action!.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnsupportedSchemaVersionIsRejected(int version)
    {
        KeyboardLayoutDefinition layout = new(
            version,
            "test.layout",
            "Test layout",
            "en-US",
            [new KeyboardLayoutRow([Key("a", TextAction())])]);

        AssertError(LayoutValidator.Validate(layout), "$.schemaVersion", "schema.unsupported");
    }

    [Fact]
    public void NullAndEmptyRowsAreRejected()
    {
        var missing = new KeyboardLayoutDefinition(1, "layout", "Layout", "en-US", null);
        var empty = new KeyboardLayoutDefinition(1, "layout", "Layout", "en-US", []);
        var emptyRow = new KeyboardLayoutDefinition(1, "layout", "Layout", "en-US", [new KeyboardLayoutRow([])]);

        AssertError(LayoutValidator.Validate(missing), "$.rows", "rows.required");
        AssertError(LayoutValidator.Validate(empty), "$.rows", "rows.count");
        AssertError(LayoutValidator.Validate(emptyRow), "$.rows[0]", "row.keyCount");
    }

    [Fact]
    public void RowAndTotalKeyLimitsAreEnforced()
    {
        KeyboardKeyDefinition[] tooManyInRow = Enumerable.Range(0, LayoutSchemaLimits.MaximumKeysPerRow + 1)
            .Select(index => Key($"r0-{index}", TextAction()))
            .ToArray();
        KeyboardLayoutDefinition wide = new(1, "layout", "Layout", "en-US", [new(tooManyInRow)]);
        KeyboardLayoutRow[] totalRows = Enumerable.Range(0, 5)
            .Select(row => new KeyboardLayoutRow(Enumerable.Range(0, 64).Select(key => Key($"{row}-{key}", TextAction()))))
            .ToArray();
        KeyboardLayoutDefinition oversized = new(1, "layout", "Layout", "en-US", totalRows);

        AssertError(LayoutValidator.Validate(wide), "$.rows[0]", "row.keyCount");
        AssertError(LayoutValidator.Validate(oversized), "$.rows", "keys.totalCount");
    }

    [Fact]
    public void RowLimitIsEnforced()
    {
        KeyboardLayoutRow[] rows = Enumerable.Range(0, LayoutSchemaLimits.MaximumRows + 1)
            .Select(index => new KeyboardLayoutRow([Key(index.ToString(CultureInfo.InvariantCulture), TextAction())]))
            .ToArray();

        AssertError(LayoutValidator.Validate(new(1, "layout", "Layout", "en-US", rows)), "$.rows", "rows.count");
    }

    [Fact]
    public void DuplicateAndOversizedIdsAreRejected()
    {
        KeyboardLayoutDefinition layout = new(
            1,
            new string('l', LayoutSchemaLimits.MaximumLayoutIdLength + 1),
            "Layout",
            "en-US",
            [new([Key("duplicate", TextAction()), Key("duplicate", TextAction())])]);

        LayoutValidationResult result = LayoutValidator.Validate(layout);

        AssertError(result, "$.id", "string.length");
        AssertError(result, "$.rows[0][1].id", "key.idDuplicate");
    }

    [Theory]
    [MemberData(nameof(InvalidWidths))]
    public void InvalidWidthsAreRejected(double width)
    {
        KeyboardLayoutDefinition layout = Layout(Key("a", TextAction()) with { Width = width });

        AssertError(LayoutValidator.Validate(layout), "$.rows[0][0].width", "key.width");
    }

    public static TheoryData<double> InvalidWidths => new()
    {
        double.NaN,
        double.PositiveInfinity,
        0,
        -1,
        LayoutSchemaLimits.MaximumKeyWidth + 0.01,
    };

    [Fact]
    public void OversizedTextIsRejectedWithoutEchoingItsValue()
    {
        string sensitive = new('S', LayoutSchemaLimits.MaximumTextLength + 1);
        KeyboardLayoutDefinition layout = Layout(Key("text", new(LayoutActionTypes.Text, value: sensitive)));

        LayoutValidationResult result = LayoutValidator.Validate(layout);

        LayoutValidationError error = Assert.Single(result.Errors);
        Assert.Equal("$.rows[0][0].action.value", error.Path);
        Assert.Equal("action.textLength", error.Code);
        Assert.DoesNotContain(sensitive, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('S', error.Message);
    }

    [Theory]
    [InlineData("command")]
    [InlineData("script")]
    [InlineData("launchExecutable")]
    [InlineData(null)]
    public void NonAllowlistedActionTypesAreRejected(string? type)
    {
        LayoutValidationResult result = LayoutValidator.Validate(Layout(Key("bad", new(type))));

        AssertError(result, "$.rows[0][0].action.type", "action.type");
    }

    [Fact]
    public void HotkeyModifiersMustBePresentUniqueAndAllowlisted()
    {
        LayoutValidationResult missing = LayoutValidator.Validate(Layout(
            Key("missing", new(LayoutActionTypes.Hotkey, virtualKey: "A"))));
        LayoutValidationResult duplicate = LayoutValidator.Validate(Layout(
            Key("duplicate", new(LayoutActionTypes.Hotkey, virtualKey: "A", modifiers: ["Control", "control"]))));
        LayoutValidationResult unknown = LayoutValidator.Validate(Layout(
            Key("unknown", new(LayoutActionTypes.Hotkey, virtualKey: "A", modifiers: ["Meta"]))));
        LayoutValidationResult longHotkey = LayoutValidator.Validate(Layout(
            Key("long", new(LayoutActionTypes.Hotkey, virtualKey: "A", modifiers: ["Control", "Shift", "Alt", "Control"]))));

        AssertError(missing, "$.rows[0][0].action.modifiers", "action.hotkeyLength");
        AssertError(duplicate, "$.rows[0][0].action.modifiers[1]", "action.modifierDuplicate");
        AssertError(unknown, "$.rows[0][0].action.modifiers[0]", "action.modifier");
        AssertError(longHotkey, "$.rows[0][0].action.modifiers", "action.hotkeyLength");
    }

    [Fact]
    public void ChordKeysMustBePresentBoundedUniqueAndAllowlisted()
    {
        LayoutValidationResult missing = LayoutValidator.Validate(Layout(
            Key("missing", new(LayoutActionTypes.Chord))));
        LayoutValidationResult tooLong = LayoutValidator.Validate(Layout(
            Key("long", new(LayoutActionTypes.Chord, keys: Enumerable.Repeat("A", LayoutSchemaLimits.MaximumChordKeys + 1)))));
        LayoutValidationResult duplicate = LayoutValidator.Validate(Layout(
            Key("duplicate", new(LayoutActionTypes.Chord, keys: ["LeftWindows", "Tab", "tab"]))));
        LayoutValidationResult unknown = LayoutValidator.Validate(Layout(
            Key("unknown", new(LayoutActionTypes.Chord, keys: ["LeftWindows", "Power"]))));

        AssertError(missing, "$.rows[0][0].action.keys", "action.chordLength");
        AssertError(tooLong, "$.rows[0][0].action.keys", "action.chordLength");
        AssertError(duplicate, "$.rows[0][0].action.keys[2]", "action.chordDuplicate");
        AssertError(unknown, "$.rows[0][0].action.keys[1]", "action.chordKey");
    }

    [Fact]
    public void ChordRejectsFieldsFromOtherActionShapes()
    {
        LayoutActionDefinition action = new(LayoutActionTypes.Chord, value: "x", virtualKey: "A", scanCode: 30,
            modifiers: ["Control"], modifier: "Shift", fnVirtualKey: "F1", keys: ["Tab"]);

        LayoutValidationResult result = LayoutValidator.Validate(Layout(Key("mixed", action)));

        Assert.Equal(6, result.Errors.Count(error => error.Code == "action.unexpectedField"));
    }

    [Fact]
    public void KeyRequiresExactlyOneAllowlistedEncoding()
    {
        LayoutValidationResult missing = LayoutValidator.Validate(Layout(Key("missing", new(LayoutActionTypes.Key))));
        LayoutValidationResult both = LayoutValidator.Validate(Layout(Key("both", new(LayoutActionTypes.Key, virtualKey: "A", scanCode: 30))));
        LayoutValidationResult unknown = LayoutValidator.Validate(Layout(Key("unknown", new(LayoutActionTypes.Key, virtualKey: "Power"))));
        LayoutValidationResult invalidScan = LayoutValidator.Validate(Layout(Key("scan", new(LayoutActionTypes.Key, scanCode: 0))));

        AssertError(missing, "$.rows[0][0].action", "action.keyEncoding");
        AssertError(both, "$.rows[0][0].action", "action.keyEncoding");
        AssertError(unknown, "$.rows[0][0].action.virtualKey", "action.virtualKey");
        AssertError(invalidScan, "$.rows[0][0].action.scanCode", "action.scanCode");
    }

    [Fact]
    public void ModifierActionUsesClosedStateModifierSet()
    {
        LayoutValidationResult result = LayoutValidator.Validate(Layout(
            Key("bad", new(LayoutActionTypes.Modifier, modifier: "Meta"))));

        AssertError(result, "$.rows[0][0].action.modifier", "action.modifier");
    }

    [Fact]
    public void WindowsFnAndFunctionLayerKeysAreAllowlisted()
    {
        LayoutValidationResult result = LayoutValidator.Validate(Layout(
            Key("windows", new(LayoutActionTypes.Modifier, modifier: "Windows")),
            Key("fn", new(LayoutActionTypes.Modifier, modifier: "Fn")),
            Key("f1", new(LayoutActionTypes.Key, virtualKey: "D1", fnVirtualKey: "F1"))));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void FunctionLayerKeyMustBeAllowlistedAndOnlyBelongsToKeyAction()
    {
        LayoutValidationResult unknown = LayoutValidator.Validate(Layout(
            Key("unknown", new(LayoutActionTypes.Key, virtualKey: "D1", fnVirtualKey: "Power"))));
        LayoutValidationResult wrongShape = LayoutValidator.Validate(Layout(
            Key("text", new(LayoutActionTypes.Text, value: "x", fnVirtualKey: "F1"))));

        AssertError(unknown, "$.rows[0][0].action.fnVirtualKey", "action.virtualKey");
        AssertError(wrongShape, "$.rows[0][0].action.fnVirtualKey", "action.unexpectedField");
    }

    [Fact]
    public void FieldsFromAnotherActionShapeAreRejected()
    {
        LayoutActionDefinition action = new(
            LayoutActionTypes.Text,
            value: "safe",
            virtualKey: "A",
            scanCode: 30,
            modifiers: ["Control"],
            modifier: "Shift",
            fnVirtualKey: "F1",
            keys: ["Tab"]);

        LayoutValidationResult result = LayoutValidator.Validate(Layout(Key("mixed", action)));

        Assert.Equal(6, result.Errors.Count(error => error.Code == "action.unexpectedField"));
    }

    private static KeyboardLayoutDefinition Layout(params KeyboardKeyDefinition[] keys) =>
        new(1, "test.layout", "Test layout", "en-US", [new(keys)]);

    private static KeyboardKeyDefinition Key(string id, LayoutActionDefinition action) =>
        new(id, id, 1, true, action);

    private static LayoutActionDefinition TextAction() => new(LayoutActionTypes.Text, value: "x");

    private static void AssertError(LayoutValidationResult result, string path, string code) =>
        Assert.Contains(result.Errors, error => error.Path == path && error.Code == code);
}
