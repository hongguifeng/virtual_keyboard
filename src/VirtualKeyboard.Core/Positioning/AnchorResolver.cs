using VirtualKeyboard.Core.Geometry;

namespace VirtualKeyboard.Core.Positioning;

public enum AnchorSource
{
    UiaSelection,
    Win32Caret,
    BoundingRectangle,
    SafeDefault,
}

[Flags]
public enum AnchorFallbackReason
{
    None = 0,
    UiaSelectionRejected = 1,
    Win32CaretRejected = 2,
    BoundingRectangleRejected = 4,
}

public enum AnchorResolutionStatus
{
    Resolved,
    NoValidAnchor,
}

public readonly record struct AnchorCandidate(PhysicalPixelRect Rectangle, nint OwnerTopLevelHwnd);

public readonly record struct AnchorResolutionResult(
    AnchorResolutionStatus Status,
    PhysicalPixelRect? Anchor,
    AnchorSource Source,
    AnchorFallbackReason FallbackReason)
{
    public bool IsResolved => Status == AnchorResolutionStatus.Resolved && Anchor is not null;
}

public static class AnchorResolver
{
    public static AnchorResolutionResult Resolve(
        nint targetTopLevelHwnd,
        PhysicalPixelRect targetWindow,
        PhysicalPixelRect workArea,
        IEnumerable<AnchorCandidate>? uiaSelection,
        AnchorCandidate? win32Caret,
        AnchorCandidate? boundingRectangle)
    {
        if (targetTopLevelHwnd == nint.Zero)
            return Failure(AnchorFallbackReason.UiaSelectionRejected | AnchorFallbackReason.Win32CaretRejected | AnchorFallbackReason.BoundingRectangleRejected);

        AnchorFallbackReason fallback = AnchorFallbackReason.None;
        if (uiaSelection is not null)
        {
            foreach (AnchorCandidate candidate in uiaSelection)
            {
                if (IsTargetCandidate(candidate, targetTopLevelHwnd, targetWindow, workArea))
                    return Success(candidate.Rectangle, AnchorSource.UiaSelection, fallback);
            }
        }

        fallback |= AnchorFallbackReason.UiaSelectionRejected;
        if (win32Caret is AnchorCandidate native && IsTargetCandidate(native, targetTopLevelHwnd, targetWindow, workArea))
            return Success(native.Rectangle, AnchorSource.Win32Caret, fallback);

        fallback |= AnchorFallbackReason.Win32CaretRejected;
        if (boundingRectangle is AnchorCandidate bounds && IsTargetCandidate(bounds, targetTopLevelHwnd, targetWindow, workArea))
            return Success(bounds.Rectangle, AnchorSource.BoundingRectangle, fallback);

        fallback |= AnchorFallbackReason.BoundingRectangleRejected;
        if (!workArea.IsValid)
            return Failure(fallback);

        var safe = new PhysicalPixelRect(
            workArea.X + (workArea.Width / 2),
            workArea.Y + (workArea.Height * 0.75),
            1,
            1);
        return safe.IsValid
            ? Success(safe, AnchorSource.SafeDefault, fallback)
            : Failure(fallback);
    }

    private static bool IsTargetCandidate(
        AnchorCandidate candidate,
        nint targetHwnd,
        PhysicalPixelRect targetWindow,
        PhysicalPixelRect workArea) =>
        candidate.OwnerTopLevelHwnd == targetHwnd &&
        candidate.Rectangle.IsValid &&
        targetWindow.IsValid &&
        workArea.IsValid &&
        (Intersects(candidate.Rectangle, targetWindow) || Intersects(candidate.Rectangle, workArea));

    private static bool Intersects(PhysicalPixelRect left, PhysicalPixelRect right) =>
        left.X < right.Right && left.Right > right.X &&
        left.Y < right.Bottom && left.Bottom > right.Y;

    private static AnchorResolutionResult Success(PhysicalPixelRect anchor, AnchorSource source, AnchorFallbackReason fallback) =>
        new(AnchorResolutionStatus.Resolved, anchor, source, fallback);

    private static AnchorResolutionResult Failure(AnchorFallbackReason fallback) =>
        new(AnchorResolutionStatus.NoValidAnchor, null, AnchorSource.SafeDefault, fallback);
}
