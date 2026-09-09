using System.Runtime.InteropServices;
using System.Windows.Automation;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows;

public enum FocusTargetEvaluationStatus
{
    Evaluated,
    FocusUnavailable,
    IdentityMismatch,
    EvidenceUnavailable,
}

public readonly record struct FocusTargetEvaluation(
    FocusTargetEvaluationStatus Status,
    FocusSnapshot Snapshot,
    ClassificationResult Classification,
    nint FocusHwnd,
    PhysicalPixelRect? Anchor,
    bool UsedFallback = false)
{
    public bool IsEvaluated => Status == FocusTargetEvaluationStatus.Evaluated;
    public bool NeedsRetry =>
        (Classification.ReasonCode == ClassificationReasonCode.NoFocusOrDisabled &&
            !Snapshot.HasKeyboardFocus && Snapshot.IsEnabled && !Snapshot.IsOffscreen) || !IsEvaluated || Classification.Value == Editability.Unknown ||
        (Snapshot.ControlType is FocusControlType.Edit or FocusControlType.Document &&
            Classification.ReasonCode == ClassificationReasonCode.NoEditableEvidence);
}

/// <summary>Evaluates the current focused UIA element without reading Name, Value, text, or password content.</summary>
public sealed class FocusTargetEvaluator
{
    private readonly NativeFocusAdapter _nativeFocus = new();
    private readonly EditabilityEvidenceFactory _evidence = new();

    public FocusTargetEvaluation Evaluate(FocusChangedNotification notification) =>
        Evaluate(notification.Snapshot ?? throw new ArgumentException("Missing focus snapshot.", nameof(notification)),
            notification.CapturedElement, notification.UsedFallback);

    public FocusTargetEvaluation Evaluate(FocusSnapshot snapshot) => Evaluate(snapshot, null, false);

    private FocusTargetEvaluation Evaluate(FocusSnapshot snapshot, AutomationElement? capturedElement, bool usedFallback)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        try
        {
            AutomationElement? element = capturedElement ?? FocusedElementResolver.Capture(out usedFallback);
            if (capturedElement is not null && !FocusedElementResolver.IsCurrentEventTarget(capturedElement))
                return Failure(snapshot, FocusTargetEvaluationStatus.IdentityMismatch, usedFallback);
            if (element is null) return Failure(snapshot, FocusTargetEvaluationStatus.FocusUnavailable, usedFallback);
            var current = element.Current;
            int[]? runtimeId = element.GetRuntimeId();
            if (current.ProcessId != snapshot.ProcessId || runtimeId is null || snapshot.RuntimeId is null ||
                !snapshot.RuntimeId.Equals(new RuntimeIdentity(runtimeId)))
                return Failure(snapshot, FocusTargetEvaluationStatus.IdentityMismatch, usedFallback);

            NativeFocusResult native = _nativeFocus.Capture();
            if (!native.IsCaptured || native.Snapshot!.ProcessId != snapshot.ProcessId ||
                native.Snapshot.TopLevelHwnd != snapshot.TopLevelHwnd)
                return Failure(snapshot, FocusTargetEvaluationStatus.IdentityMismatch, usedFallback);

            PhysicalPixelRect? caret = native.Snapshot.CaretRectangle;
            if (!_evidence.TryCreate(element, snapshot, caret, native.Snapshot.TopLevelHwnd, out EditabilityEvidence evidence))
                return Failure(snapshot, FocusTargetEvaluationStatus.EvidenceUnavailable, usedFallback);

            ClassificationResult classification = EditabilityClassifier.Classify(evidence);
            PhysicalPixelRect? bounds = ToPhysicalRect(current.BoundingRectangle);
            PhysicalPixelRect? anchor = caret is { IsValid: true } ? caret : bounds;
            return new(FocusTargetEvaluationStatus.Evaluated, snapshot, classification, native.Snapshot.FocusHwnd, anchor, usedFallback);
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return Failure(snapshot, FocusTargetEvaluationStatus.EvidenceUnavailable, usedFallback);
        }
    }

    private static PhysicalPixelRect? ToPhysicalRect(System.Windows.Rect rectangle)
    {
        var result = new PhysicalPixelRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        return result.IsValid ? result : null;
    }

    private static FocusTargetEvaluation Failure(FocusSnapshot snapshot, FocusTargetEvaluationStatus status, bool usedFallback) =>
        new(status, snapshot, new(snapshot.Version, Editability.Unknown, ClassificationReasonCode.InvalidIdentity, snapshot.IsPassword), nint.Zero, null, usedFallback);
}
