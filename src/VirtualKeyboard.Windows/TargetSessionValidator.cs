using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows;

/// <summary>Rechecks the current session against foreground and focus state without activating a window.</summary>
public sealed class TargetSessionValidator
{
    private readonly TargetSessionStore _sessions;
    private readonly IForegroundTargetCapture _capture;
    private readonly LatestFocusSnapshotStore? _focusSnapshots;
    private readonly Func<bool>? _observationHealthy;

    public TargetSessionValidator(
        TargetSessionStore sessions,
        IForegroundTargetCapture capture,
        LatestFocusSnapshotStore? focusSnapshots = null, Func<bool>? observationHealthy = null)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _focusSnapshots = focusSnapshots;
        _observationHealthy = observationHealthy;
    }

    public TargetValidationResult Validate(long expectedSessionId)
    {
        if (_observationHealthy?.Invoke() == false)
            return TargetValidationResult.Invalid(TargetValidationStatus.FocusIdentityStale);
        TargetSession? session = _sessions.Current;
        if (session is null)
        {
            return TargetValidationResult.Invalid(TargetValidationStatus.NoCurrentSession);
        }

        if (session.SessionId != expectedSessionId)
        {
            return TargetValidationResult.Invalid(TargetValidationStatus.SessionReplaced);
        }

        TargetCaptureResult capture = _capture.Capture();
        if (!capture.IsCaptured)
        {
            return TargetValidationResult.Invalid(TargetValidationStatus.TargetUnavailable);
        }

        TargetCaptureSnapshot snapshot = capture.Snapshot!;
        TargetValidationStatus status = snapshot.TopLevelHwnd != session.TopLevelHwnd
            ? TargetValidationStatus.ForegroundChanged
            : snapshot.ProcessId != session.ProcessId
                ? TargetValidationStatus.ProcessChanged
                : snapshot.FocusHwnd != session.FocusHwnd
                    ? TargetValidationStatus.FocusChanged
                    : TargetValidationStatus.Valid;

        if (status != TargetValidationStatus.Valid)
        {
            return TargetValidationResult.Invalid(status);
        }

        if (session.RuntimeId is not null)
        {
            FocusSnapshot? identity = _focusSnapshots?.Current;
            if (identity is null)
                return TargetValidationResult.Invalid(TargetValidationStatus.FocusIdentityUnavailable);
            if (identity.Version < session.FocusVersion)
                return TargetValidationResult.Invalid(TargetValidationStatus.FocusIdentityStale);
            if (identity.ProcessId != session.ProcessId || identity.TopLevelHwnd != session.TopLevelHwnd ||
                identity.RuntimeId is null || !identity.RuntimeId.Equals(session.RuntimeId) ||
                !identity.HasKeyboardFocus || !identity.IsEnabled || identity.IsOffscreen)
                return TargetValidationResult.Invalid(TargetValidationStatus.IdentityChangedRequiresReclassification);
        }

        TargetSession? latest = _sessions.Current;
        return ReferenceEquals(latest, session)
            ? TargetValidationResult.Valid(session)
            : TargetValidationResult.Invalid(TargetValidationStatus.SessionReplaced);
    }
}
