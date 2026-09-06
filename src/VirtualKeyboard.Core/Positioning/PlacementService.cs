using VirtualKeyboard.Core.Geometry;

namespace VirtualKeyboard.Core.Positioning;

public enum PlacementDirection
{
    Bottom,
    Top,
    Right,
    Left,
}

public enum PlacementStatus
{
    Placed,
    InvalidInput,
}

public readonly record struct PlacementResult(
    PlacementStatus Status,
    PhysicalPixelRect? Rectangle,
    PlacementDirection Direction,
    bool WasScaled)
{
    public bool IsPlaced => Status == PlacementStatus.Placed && Rectangle is not null;
}

public static class PlacementService
{
    private const double WorkAreaSafetyRatio = 0.95;

    public static PlacementResult Place(
        PhysicalPixelRect anchor,
        PhysicalPixelRect workArea,
        PhysicalPixelSize desiredSize,
        double margin,
        double verticalCandidateClearance = 0)
    {
        if (!anchor.IsValid || !workArea.IsValid || !workArea.Size.IsPositive || !desiredSize.IsPositive ||
            !double.IsFinite(margin) || margin < 0 ||
            !double.IsFinite(verticalCandidateClearance) || verticalCandidateClearance < 0)
            return new(PlacementStatus.InvalidInput, null, PlacementDirection.Bottom, false);

        (PhysicalPixelSize size, bool scaled) = ConstrainSize(desiredSize, workArea.Size);
        Candidate[] candidates =
        [
            Create(PlacementDirection.Bottom, anchor.X + ((anchor.Width - size.Width) / 2), anchor.Bottom + margin + verticalCandidateClearance, size, anchor, workArea, 0),
            Create(PlacementDirection.Top, anchor.X + ((anchor.Width - size.Width) / 2), anchor.Y - size.Height - margin - verticalCandidateClearance, size, anchor, workArea, 1),
            Create(PlacementDirection.Right, anchor.Right + margin, anchor.Y + ((anchor.Height - size.Height) / 2), size, anchor, workArea, 2),
            Create(PlacementDirection.Left, anchor.X - size.Width - margin, anchor.Y + ((anchor.Height - size.Height) / 2), size, anchor, workArea, 3),
        ];

        Candidate selected = candidates
            .OrderByDescending(candidate => candidate.FullyVisible)
            .ThenBy(candidate => candidate.OverlapArea > 0)
            .ThenByDescending(candidate => candidate.VisibleRatio)
            .ThenBy(candidate => candidate.DirectionRank)
            .ThenBy(candidate => candidate.Distance)
            .First();
        return new(PlacementStatus.Placed, selected.Clamped, selected.Direction, scaled);
    }

    private static (PhysicalPixelSize Size, bool Scaled) ConstrainSize(PhysicalPixelSize desired, PhysicalPixelSize workArea)
    {
        double maxWidth = workArea.Width * WorkAreaSafetyRatio;
        double maxHeight = workArea.Height * WorkAreaSafetyRatio;
        double scale = Math.Min(1, Math.Min(maxWidth / desired.Width, maxHeight / desired.Height));
        return (new PhysicalPixelSize(desired.Width * scale, desired.Height * scale), scale < 1);
    }

    private static Candidate Create(
        PlacementDirection direction,
        double x,
        double y,
        PhysicalPixelSize size,
        PhysicalPixelRect anchor,
        PhysicalPixelRect workArea,
        int directionRank)
    {
        var raw = new PhysicalPixelRect(x, y, size.Width, size.Height);
        PhysicalPixelRect clamped = Clamp(raw, workArea);
        double visibleArea = IntersectionArea(raw, workArea);
        double area = size.Width * size.Height;
        return new(
            direction,
            clamped,
            IntersectionArea(clamped, anchor),
            visibleArea >= area,
            area > 0 ? visibleArea / area : 0,
            directionRank,
            Distance(clamped, anchor));
    }

    private static PhysicalPixelRect Clamp(PhysicalPixelRect rectangle, PhysicalPixelRect workArea)
    {
        double x = Math.Clamp(rectangle.X, workArea.X, workArea.Right - rectangle.Width);
        double y = Math.Clamp(rectangle.Y, workArea.Y, workArea.Bottom - rectangle.Height);
        return new(x, y, rectangle.Width, rectangle.Height);
    }

    private static double IntersectionArea(PhysicalPixelRect left, PhysicalPixelRect right)
    {
        double width = Math.Max(0, Math.Min(left.Right, right.Right) - Math.Max(left.X, right.X));
        double height = Math.Max(0, Math.Min(left.Bottom, right.Bottom) - Math.Max(left.Y, right.Y));
        return width * height;
    }

    private static double Distance(PhysicalPixelRect rectangle, PhysicalPixelRect anchor)
    {
        double dx = (rectangle.X + rectangle.Width / 2) - (anchor.X + anchor.Width / 2);
        double dy = (rectangle.Y + rectangle.Height / 2) - (anchor.Y + anchor.Height / 2);
        return (dx * dx) + (dy * dy);
    }

    private readonly record struct Candidate(
        PlacementDirection Direction,
        PhysicalPixelRect Clamped,
        double OverlapArea,
        bool FullyVisible,
        double VisibleRatio,
        int DirectionRank,
        double Distance);
}
