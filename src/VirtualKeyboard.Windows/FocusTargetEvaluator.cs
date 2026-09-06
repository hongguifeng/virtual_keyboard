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
    PhysicalPixelRect? Anchor)
{
    public bool IsEvaluated => Status == FocusTargetEvaluationStatus.Evaluated;
}

/// <summary>Evaluates the current focused UIA element without reading Name, Value, text, or password content.</summary>
public sealed class FocusTargetEvaluator
{
    private readonly NativeFocusAdapter _nativeFocus = new();
    private readonly EditabilityEvidenceFactory _evidence = new();

    public FocusTargetEvaluation Evaluate(FocusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        try
        {
            AutomationElement? element = AutomationElement.FocusedElement;
            if (element is null) return Failure(snapshot, FocusTargetEvaluationStatus.FocusUnavailable);
            var current = element.Current;
            int[]? runtimeId = element.GetRuntimeId();
            if (current.ProcessId != snapshot.ProcessId || runtimeId is null || snapshot.RuntimeId is null ||
                !snapshot.RuntimeId.Equals(new RuntimeIdentity(runtimeId)))
                return Failure(snapshot, FocusTargetEvaluationStatus.IdentityMismatch);

            NativeFocusResult native = _nativeFocus.Capture();
            if (!native.IsCaptured || native.Snapshot!.ProcessId != snapshot.ProcessId ||
                native.Snapshot.TopLevelHwnd != snapshot.TopLevelHwnd)
                return Failure(snapshot, FocusTargetEvaluationStatus.IdentityMismatch);

            PhysicalPixelRect? caret = native.Snapshot.CaretRectangle;
            if (!_evidence.TryCreate(element, snapshot, caret, native.Snapshot.TopLevelHwnd, out EditabilityEvidence evidence))
                return Failure(snapshot, FocusTargetEvaluationStatus.EvidenceUnavailable);

            ClassificationResult classification = EditabilityClassifier.Classify(evidence);
            PhysicalPixelRect? bounds = ToPhysicalRect(current.BoundingRectangle);
            PhysicalPixelRect? anchor = caret is { IsValid: true } ? caret : bounds;
            return new(FocusTargetEvaluationStatus.Evaluated, snapshot, classification, native.Snapshot.FocusHwnd, anchor);
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return Failure(snapshot, FocusTargetEvaluationStatus.EvidenceUnavailable);
        }
    }

    private static PhysicalPixelRect? ToPhysicalRect(System.Windows.Rect rectangle)
    {
        var result = new PhysicalPixelRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        return result.IsValid ? result : null;
    }

    private static FocusTargetEvaluation Failure(FocusSnapshot snapshot, FocusTargetEvaluationStatus status) =>
        new(status, snapshot, new(snapshot.Version, Editability.Unknown, ClassificationReasonCode.InvalidIdentity, snapshot.IsPassword), nint.Zero, null);
}
