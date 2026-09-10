using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Core.Geometry;

namespace VirtualKeyboard.Core.Tests;

public sealed class EditabilityClassifierTests
{
    private static FocusSnapshot Snapshot(FocusControlType type = FocusControlType.Edit, bool password = false, bool enabled = true, bool focused = true, bool offscreen = false) =>
        new(1, DateTimeOffset.UtcNow, 42, (nint)10, null, type, focused, enabled, offscreen, password);

    [Fact] public void ValuePatternWritableIsEditable() => Assert.Equal(Editability.Editable, Classify(Snapshot(), value: true).Value);
    [Theory]
    [InlineData(FocusControlType.Other)]
    [InlineData(FocusControlType.Pane)]
    [InlineData(FocusControlType.Window)]
    public void WritableValuePatternOnNonEditControlsIsNotAnInputTarget(FocusControlType type)
    {
        ClassificationResult result = Classify(Snapshot(type), value: true);

        Assert.Equal(Editability.NotEditable, result.Value);
        Assert.Equal(ClassificationReasonCode.NoEditableEvidence, result.ReasonCode);
    }
    [Fact] public void TextPatternOnlyIsUnknown() => Assert.Equal(Editability.Unknown, Classify(Snapshot(), text: true).Value);
    [Fact] public void PasswordEditIsEditableWithoutValuePattern() => Assert.Equal(Editability.Editable, Classify(Snapshot(password: true)).Value);
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void DisabledUnfocusedOrOffscreenIsNotEditable(bool enabled, bool focused, bool offscreen)
    {
        ClassificationResult result = Classify(Snapshot(enabled: enabled, focused: focused, offscreen: offscreen), value: true);
        Assert.Equal(Editability.NotEditable, result.Value);
        Assert.Equal(ClassificationReasonCode.NoFocusOrDisabled, result.ReasonCode);
    }
    [Fact] public void ReadOnlyWinsOverOtherEvidence() => Assert.Equal(ClassificationReasonCode.ReadOnly, Classify(Snapshot(), value: true, readOnly: true, textEdit: true, text: true, caret: new(1, 1, 1, 1)).ReasonCode);

    [Fact]
    public void TextEditPatternIsEditable()
    {
        ClassificationResult result = Classify(Snapshot(FocusControlType.Document), textEdit: true);
        Assert.Equal(Editability.Editable, result.Value);
        Assert.Equal(ClassificationReasonCode.TextEditPattern, result.ReasonCode);
    }

    [Theory]
    [InlineData(double.NaN, 1, 1, 1)]
    [InlineData(double.PositiveInfinity, 1, 1, 1)]
    [InlineData(1, 1, 0, 0)]
    [InlineData(1, 1, -1, 1)]
    [InlineData(20_000_000, 1, 1, 1)]
    public void InvalidCaretDoesNotMakeDocumentEditable(double x, double y, double width, double height)
    {
        ClassificationResult result = Classify(Snapshot(FocusControlType.Document), caret: new(x, y, width, height));
        Assert.Equal(Editability.NotEditable, result.Value);
    }

    [Fact]
    public void CaretMustBelongToTargetWindow()
    {
        var evidence = new EditabilityEvidence(Snapshot(), false, false, false, false, new(1, 1, 1, 1), (nint)99);
        Assert.Equal(Editability.NotEditable, EditabilityClassifier.Classify(evidence).Value);
    }

    [Fact]
    public void InvalidIdentityIsUnknownAndPreservesVersion()
    {
        FocusSnapshot snapshot = Snapshot() with { Version = 88, TopLevelHwnd = nint.Zero };
        ClassificationResult result = Classify(snapshot, value: true);
        Assert.Equal(Editability.Unknown, result.Value);
        Assert.Equal(ClassificationReasonCode.InvalidIdentity, result.ReasonCode);
        Assert.Equal(88, result.Version);
    }

    [Fact]
    public void ControlWithoutEvidenceIsNotEditable()
    {
        ClassificationResult result = Classify(Snapshot(FocusControlType.Button));
        Assert.Equal(Editability.NotEditable, result.Value);
        Assert.Equal(ClassificationReasonCode.NoEditableEvidence, result.ReasonCode);
    }

    private static ClassificationResult Classify(
        FocusSnapshot snapshot,
        bool value = false,
        bool readOnly = false,
        bool textEdit = false,
        bool text = false,
        PhysicalPixelRect? caret = null) =>
        EditabilityClassifier.Classify(new(snapshot, value, readOnly, textEdit, text, caret, snapshot.TopLevelHwnd));

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, false)]
    public void ComboBoxRequiresWritableValueAndTextEvidence(bool value, bool text, bool readOnly, bool editable)
    {
        var result = Classify(Snapshot(FocusControlType.ComboBox), value: value, text: text, readOnly: readOnly);
        Assert.Equal(editable, result.Value == Editability.Editable);
        if (editable) Assert.Equal(ClassificationReasonCode.ValuePattern, result.ReasonCode);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ComboBoxSafetyFlagsOverrideWritableEvidence(bool enabled, bool focused, bool offscreen)
    {
        var result = Classify(Snapshot(FocusControlType.ComboBox, enabled: enabled, focused: focused, offscreen: offscreen), value: true, text: true);
        Assert.Equal(Editability.NotEditable, result.Value);
        Assert.Equal(ClassificationReasonCode.NoFocusOrDisabled, result.ReasonCode);
    }

    [Fact]
    public void OtherControlsAreNotPromotedByCombinedPatterns() =>
        Assert.NotEqual(Editability.Editable, Classify(Snapshot(FocusControlType.Other), value: true, text: true).Value);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WritableSpinnerAcceptsTextInputWithoutRequiringTextPattern(bool text)
    {
        var result = Classify(Snapshot(FocusControlType.Spinner), value: true, text: text);
        Assert.Equal(Editability.Editable, result.Value);
        Assert.Equal(ClassificationReasonCode.ValuePattern, result.ReasonCode);
    }

    [Theory]
    [InlineData(true, true, false, true, true, Editability.NotEditable, ClassificationReasonCode.ReadOnly)]
    [InlineData(false, true, false, true, false, Editability.NotEditable, ClassificationReasonCode.NoFocusOrDisabled)]
    [InlineData(true, false, false, true, false, Editability.NotEditable, ClassificationReasonCode.NoFocusOrDisabled)]
    [InlineData(true, true, true, true, false, Editability.NotEditable, ClassificationReasonCode.NoFocusOrDisabled)]
    [InlineData(true, true, false, false, false, Editability.NotEditable, ClassificationReasonCode.NoEditableEvidence)]
    public void SpinnerSafetyAndMissingTextInputEvidenceRemainRejected(bool enabled, bool focused,
        bool offscreen, bool value, bool readOnly, Editability verdict, ClassificationReasonCode reason)
    {
        var result = Classify(Snapshot(FocusControlType.Spinner, enabled: enabled, focused: focused,
            offscreen: offscreen), value: value, readOnly: readOnly);
        Assert.Equal(verdict, result.Value);
        Assert.Equal(reason, result.ReasonCode);
    }

    [Fact]
    public void SpinnerTextSurfaceOrCaretAloneDoesNotProveTextInput()
    {
        Assert.NotEqual(Editability.Editable, Classify(Snapshot(FocusControlType.Spinner), text: true,
            textEdit: true, caret: new(1, 1, 1, 1)).Value);
        Assert.Equal(Editability.Unknown, Classify(Snapshot(FocusControlType.Spinner) with { ProcessId = 0 }, value: true).Value);
    }
}
