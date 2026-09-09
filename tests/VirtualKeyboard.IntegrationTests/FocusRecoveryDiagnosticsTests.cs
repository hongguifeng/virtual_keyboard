using VirtualKeyboard.App;
using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;
using Xunit;

namespace VirtualKeyboard.IntegrationTests;

public sealed class FocusRecoveryDiagnosticsTests
{
    [Theory]
    [InlineData(FocusTargetEvaluationStatus.FocusUnavailable, ReasonCode.FocusUnavailable)]
    [InlineData(FocusTargetEvaluationStatus.IdentityMismatch, ReasonCode.IdentityMismatch)]
    [InlineData(FocusTargetEvaluationStatus.EvidenceUnavailable, ReasonCode.EvidenceUnavailable)]
    public void LogsDistinguishFailureStage(FocusTargetEvaluationStatus status, ReasonCode expected)
    {
        var evaluation = Evaluation(status, Editability.Unknown, ClassificationReasonCode.InvalidIdentity);
        Assert.Equal(expected, MainWindow.GetFocusReason(evaluation));
        Assert.True(evaluation.NeedsRetry);
    }

    [Theory]
    [InlineData(Editability.Editable, ClassificationReasonCode.ValuePattern, ReasonCode.ValuePattern, false)]
    [InlineData(Editability.NotEditable, ClassificationReasonCode.ReadOnly, ReasonCode.ReadOnly, false)]
    [InlineData(Editability.NotEditable, ClassificationReasonCode.NoFocusOrDisabled, ReasonCode.NoFocusOrDisabled, false)]
    [InlineData(Editability.Unknown, ClassificationReasonCode.TextPatternOnly, ReasonCode.TextPatternOnly, true)]
    [InlineData(Editability.NotEditable, ClassificationReasonCode.NoEditableEvidence, ReasonCode.NoEditableEvidence, true)]
    public void LogsExplainClassificationAndRetryPolicy(Editability verdict, ClassificationReasonCode reason,
        ReasonCode expected, bool retry)
    {
        var evaluation = Evaluation(FocusTargetEvaluationStatus.Evaluated, verdict, reason);
        Assert.Equal(expected, MainWindow.GetFocusReason(evaluation));
        Assert.Equal(retry, evaluation.NeedsRetry);
    }

    private static FocusTargetEvaluation Evaluation(FocusTargetEvaluationStatus status,
        Editability verdict, ClassificationReasonCode reason) => new(status,
        new FocusSnapshot(1, DateTimeOffset.UtcNow, 42, (nint)100, new RuntimeIdentity([1]),
            FocusControlType.Edit, true, true, false, false),
        new ClassificationResult(1, verdict, reason, false), (nint)101, null);
}
