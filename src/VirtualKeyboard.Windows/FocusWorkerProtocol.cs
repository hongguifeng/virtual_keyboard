using System.Text.Json;
using System.IO;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows;

// Private parent/child transport. No Name, Value, text or executable action fields.
internal sealed record FocusWorkerResult(long EventVersion, int RetryAttempt, bool UsedEventTarget,
    int ProcessId, long TopLevelHwnd, int[]? RuntimeId, FocusControlType ControlType,
    bool HasKeyboardFocus, bool IsEnabled, bool IsOffscreen, bool IsPassword,
    FocusTargetEvaluationStatus Status, Editability Verdict, ClassificationReasonCode Reason,
    long FocusHwnd, PhysicalPixelRect? Anchor, bool UsedFallback, long DurationMs)
{
    public int[]? InputOwnerRuntimeId { get; init; }

    public static FocusWorkerResult From(FocusChangedNotification notification, FocusTargetEvaluation evaluation, long eventVersion, long duration)
    {
        var s = evaluation.Snapshot;
        return new(eventVersion, notification.RetryAttempt, notification.UsedEventTarget, s.ProcessId,
            s.TopLevelHwnd.ToInt64(), s.RuntimeId?.ToArray(), s.ControlType, s.HasKeyboardFocus,
            s.IsEnabled, s.IsOffscreen, s.IsPassword, evaluation.Status, evaluation.Classification.Value,
            evaluation.Classification.ReasonCode, evaluation.FocusHwnd.ToInt64(), evaluation.Anchor, evaluation.UsedFallback, duration)
            { InputOwnerRuntimeId = s.InputOwnerRuntimeId?.ToArray() };
    }

    public FocusTargetEvaluation ToEvaluation(long version)
    {
        var snapshot = new FocusSnapshot(version, DateTimeOffset.UtcNow, ProcessId, (nint)TopLevelHwnd,
            RuntimeId is null ? null : new(RuntimeId), ControlType, HasKeyboardFocus, IsEnabled, IsOffscreen, IsPassword)
            { InputOwnerRuntimeId = InputOwnerRuntimeId is null ? null : new(InputOwnerRuntimeId) };
        return new(Status, snapshot, new(version, Verdict, Reason, IsPassword), (nint)FocusHwnd, Anchor, UsedFallback);
    }

    public bool IsValid => ProcessId > 0 && TopLevelHwnd != 0 && RuntimeId is null or { Length: <= 64 } &&
        (InputOwnerRuntimeId is null || (InputOwnerRuntimeId.Length is > 0 and <= 64 &&
            ControlType == FocusControlType.ListItem && RuntimeId is { Length: > 0 and <= 64 } && !IsPassword)) &&
        Enum.IsDefined(ControlType) && Enum.IsDefined(Status) && Enum.IsDefined(Verdict) && Enum.IsDefined(Reason) &&
        RetryAttempt is >= 0 and <= FocusObservationService.MaxEvaluationRetries &&
        (Anchor is null || Anchor.Value.IsValid);
}

internal sealed record FocusWorkerPacket(long Progress, long EventVersion, int Stage, long ResultSequence,
    FocusWorkerResult? Result, int ErrorCode)
{
    // Windows QPC is shared across processes; a queued heartbeat must not renew stale input authority.
    public long ProgressTimestamp { get; init; }
    internal const int MaximumLineLength = 8192;
    public static FocusWorkerPacket Parse(string line)
    {
        if (line.Length > MaximumLineLength) throw new InvalidDataException("Oversized focus packet.");
        var packet = JsonSerializer.Deserialize<FocusWorkerPacket>(line);
        if (packet is null || packet.Progress < 0 || packet.ProgressTimestamp < 0 || packet.EventVersion < 0 || packet.ResultSequence < 0 ||
            packet.Stage is < 0 or > 3 || packet.Result is { IsValid: false })
            throw new InvalidDataException("Invalid focus packet.");
        return packet;
    }
}
