using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Positioning;

namespace VirtualKeyboard.Core.Tests.Positioning;

public sealed class AnchorResolverTests
{
    private static readonly nint Target = (nint)10;
    private static readonly PhysicalPixelRect Window = new(100, 100, 800, 600);
    private static readonly PhysicalPixelRect WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void UsesFirstValidUiaSelectionRectangle()
    {
        AnchorCandidate[] selection =
        [
            new(new(double.NaN, 1, 2, 3), Target),
            new(new(150, 160, 2, 20), Target),
            new(new(200, 200, 2, 20), Target),
        ];

        AnchorResolutionResult result = Resolve(selection);
        Assert.Equal(AnchorSource.UiaSelection, result.Source);
        Assert.Equal(new PhysicalPixelRect(150, 160, 2, 20), result.Anchor);
        Assert.Equal(AnchorFallbackReason.None, result.FallbackReason);
    }

    [Fact]
    public void FallsBackInDocumentedOrderAndRecordsEverySkippedSource()
    {
        var native = new AnchorCandidate(new(300, 300, 1, 18), Target);
        var bounds = new AnchorCandidate(new(120, 120, 500, 40), Target);
        AnchorResolutionResult nativeResult = Resolve([], native, bounds);
        Assert.Equal(AnchorSource.Win32Caret, nativeResult.Source);
        Assert.Equal(AnchorFallbackReason.UiaSelectionRejected, nativeResult.FallbackReason);

        AnchorResolutionResult boundsResult = Resolve([], null, bounds);
        Assert.Equal(AnchorSource.BoundingRectangle, boundsResult.Source);
        Assert.Equal(AnchorFallbackReason.UiaSelectionRejected | AnchorFallbackReason.Win32CaretRejected, boundsResult.FallbackReason);
    }

    [Fact]
    public void RejectsCrossTargetAndOffscreenRectanglesThenUsesSafeDefault()
    {
        var otherTarget = new AnchorCandidate(new(150, 150, 2, 20), (nint)99);
        var outside = new AnchorCandidate(new(5000, 5000, 2, 20), Target);
        AnchorResolutionResult result = Resolve([otherTarget], outside, null);

        Assert.Equal(AnchorSource.SafeDefault, result.Source);
        Assert.Equal(new PhysicalPixelRect(960, 780, 1, 1), result.Anchor);
        Assert.Equal(
            AnchorFallbackReason.UiaSelectionRejected | AnchorFallbackReason.Win32CaretRejected | AnchorFallbackReason.BoundingRectangleRejected,
            result.FallbackReason);
    }

    [Theory]
    [InlineData(double.NaN, 1, 1, 1)]
    [InlineData(double.PositiveInfinity, 1, 1, 1)]
    [InlineData(1, 1, 0, 0)]
    [InlineData(1, 1, -1, 1)]
    [InlineData(20_000_000, 1, 1, 1)]
    public void RejectsInvalidCaretGeometry(double x, double y, double width, double height)
    {
        var invalid = new AnchorCandidate(new(x, y, width, height), Target);
        Assert.Equal(AnchorSource.SafeDefault, Resolve([invalid], invalid, invalid).Source);
    }

    [Fact]
    public void InvalidWorkAreaReturnsExplicitFailure()
    {
        AnchorResolutionResult result = AnchorResolver.Resolve(Target, Window, default, null, null, null);
        Assert.False(result.IsResolved);
        Assert.Equal(AnchorResolutionStatus.NoValidAnchor, result.Status);
    }

    private static AnchorResolutionResult Resolve(
        IEnumerable<AnchorCandidate>? selection = null,
        AnchorCandidate? native = null,
        AnchorCandidate? bounds = null) =>
        AnchorResolver.Resolve(Target, Window, WorkArea, selection, native, bounds);
}
