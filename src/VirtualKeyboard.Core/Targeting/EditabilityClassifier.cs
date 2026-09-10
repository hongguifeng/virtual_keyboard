using VirtualKeyboard.Core.Geometry;

namespace VirtualKeyboard.Core.Targeting;

public enum Editability
{
    Editable,
    NotEditable,
    Unknown,
}

public enum ClassificationReasonCode
{
    InvalidIdentity,
    NoFocusOrDisabled,
    ReadOnly,
    PasswordEdit,
    ValuePattern,
    TextEditPattern,
    CaretEvidence,
    TextPatternOnly,
    NoEditableEvidence,
    SearchInputRelationship,
}

public readonly record struct EditabilityEvidence(
    FocusSnapshot Snapshot,
    bool IsValuePatternAvailable,
    bool IsValueReadOnly,
    bool IsTextEditPatternAvailable,
    bool IsTextPatternAvailable,
    PhysicalPixelRect? CaretRectangle,
    nint CaretOwnerHwnd,
    bool HasVerifiedSearchInput = false);

public readonly record struct ClassificationResult(
    long Version,
    Editability Value,
    ClassificationReasonCode ReasonCode,
    bool IsPassword);

public static class EditabilityClassifier
{
    public static ClassificationResult Classify(EditabilityEvidence evidence)
    {
        FocusSnapshot snapshot = evidence.Snapshot;
        if (snapshot.ProcessId <= 0 || snapshot.TopLevelHwnd == nint.Zero)
            return Result(snapshot, Editability.Unknown, ClassificationReasonCode.InvalidIdentity);
        if (!snapshot.IsEnabled || !snapshot.HasKeyboardFocus || snapshot.IsOffscreen)
            return Result(snapshot, Editability.NotEditable, ClassificationReasonCode.NoFocusOrDisabled);
        if (evidence.IsValuePatternAvailable && evidence.IsValueReadOnly)
            return Result(snapshot, Editability.NotEditable, ClassificationReasonCode.ReadOnly);
        if (snapshot.ControlType == FocusControlType.ListItem && !snapshot.IsPassword &&
            snapshot.RuntimeId is { Count: > 0 and <= 64 } && snapshot.InputOwnerRuntimeId is { Count: > 0 and <= 64 } &&
            evidence.HasVerifiedSearchInput)
            return Result(snapshot, Editability.Editable, ClassificationReasonCode.SearchInputRelationship);
        if (snapshot.IsPassword && snapshot.ControlType == FocusControlType.Edit)
            return Result(snapshot, Editability.Editable, ClassificationReasonCode.PasswordEdit);
        // A Spinner may expose writable text through ValuePattern without TextPattern.
        // Range adjustment alone (for example a Slider) is not text-input evidence.
        if (snapshot.ControlType is FocusControlType.Edit or FocusControlType.Spinner &&
            evidence.IsValuePatternAvailable && !evidence.IsValueReadOnly)
            return Result(snapshot, Editability.Editable, ClassificationReasonCode.ValuePattern);
        // Search suggestions can expose a ComboBox rather than Edit. Require both
        // writable value and a text surface; a selection-only dropdown is insufficient.
        if (snapshot.ControlType == FocusControlType.ComboBox && evidence.IsValuePatternAvailable &&
            !evidence.IsValueReadOnly && evidence.IsTextPatternAvailable)
            return Result(snapshot, Editability.Editable, ClassificationReasonCode.ValuePattern);
        if (snapshot.ControlType is FocusControlType.Edit or FocusControlType.Document && evidence.IsTextEditPatternAvailable)
            return Result(snapshot, Editability.Editable, ClassificationReasonCode.TextEditPattern);
        if ((snapshot.ControlType is FocusControlType.Edit or FocusControlType.Document) &&
            evidence.CaretRectangle is { IsValid: true } &&
            evidence.CaretOwnerHwnd == snapshot.TopLevelHwnd)
            return Result(snapshot, Editability.Editable, ClassificationReasonCode.CaretEvidence);
        if (evidence.IsTextPatternAvailable)
            return Result(snapshot, Editability.Unknown, ClassificationReasonCode.TextPatternOnly);
        return Result(snapshot, Editability.NotEditable, ClassificationReasonCode.NoEditableEvidence);
    }

    private static ClassificationResult Result(FocusSnapshot snapshot, Editability value, ClassificationReasonCode reason) =>
        new(snapshot.Version, value, reason, snapshot.IsPassword);
}
