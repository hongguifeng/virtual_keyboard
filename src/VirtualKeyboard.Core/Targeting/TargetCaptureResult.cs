namespace VirtualKeyboard.Core.Targeting;

public readonly record struct TargetCaptureResult(TargetCaptureStatus Status, TargetCaptureSnapshot? Snapshot)
{
    public bool IsCaptured => Status == TargetCaptureStatus.Captured && Snapshot is not null;

    public static TargetCaptureResult Success(TargetCaptureSnapshot snapshot) =>
        new(TargetCaptureStatus.Captured, snapshot ?? throw new ArgumentNullException(nameof(snapshot)));

    public static TargetCaptureResult Failure(TargetCaptureStatus status)
    {
        if (status == TargetCaptureStatus.Captured)
        {
            throw new ArgumentException("Captured status requires a snapshot.", nameof(status));
        }

        return new TargetCaptureResult(status, null);
    }
}
