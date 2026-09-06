namespace VirtualKeyboard.Core.Targeting;

public readonly record struct TargetValidationResult(TargetValidationStatus Status, TargetSession? Session)
{
    public bool IsValid => Status == TargetValidationStatus.Valid && Session is not null;

    public bool RequiresReclassification => Status == TargetValidationStatus.IdentityChangedRequiresReclassification;

    public static TargetValidationResult Valid(TargetSession session) =>
        new(TargetValidationStatus.Valid, session ?? throw new ArgumentNullException(nameof(session)));

    public static TargetValidationResult Invalid(TargetValidationStatus status)
    {
        if (status == TargetValidationStatus.Valid)
        {
            throw new ArgumentException("Valid status requires a target session.", nameof(status));
        }

        return new TargetValidationResult(status, null);
    }
}
