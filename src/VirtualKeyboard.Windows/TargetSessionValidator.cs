using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows;

/// <summary>Rechecks the current session against foreground and focus state without activating a window.</summary>
public sealed class TargetSessionValidator
{
    private readonly TargetSessionStore _sessions;
    private readonly IForegroundTargetCapture _capture;

    public TargetSessionValidator(TargetSessionStore sessions, IForegroundTargetCapture capture)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
    }

    public TargetValidationResult Validate(long expectedSessionId)
    {
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

        TargetSession? latest = _sessions.Current;
        return ReferenceEquals(latest, session)
            ? TargetValidationResult.Valid(session)
            : TargetValidationResult.Invalid(TargetValidationStatus.SessionReplaced);
    }
}
