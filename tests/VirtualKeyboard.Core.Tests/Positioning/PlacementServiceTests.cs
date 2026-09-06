using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Positioning;

namespace VirtualKeyboard.Core.Tests.Positioning;

public sealed class PlacementServiceTests
{
    private static readonly PhysicalPixelRect WorkArea = new(0, 0, 1920, 1040);
    private static readonly PhysicalPixelSize Keyboard = new(400, 200);

    [Fact]
    public void PrefersBottomWhenItFitsWithoutOverlap()
    {
        PlacementResult result = PlacementService.Place(new(700, 400, 200, 40), WorkArea, Keyboard, 10);
        Assert.Equal(PlacementDirection.Bottom, result.Direction);
        Assert.Equal(new PhysicalPixelRect(600, 450, 400, 200), result.Rectangle);
    }

    [Fact]
    public void VerticalCandidateClearanceKeepsKeyboardAwayFromImeUi()
    {
        PlacementResult result = PlacementService.Place(
            new(700, 400, 200, 40), WorkArea, Keyboard, margin: 10, verticalCandidateClearance: 96);

        Assert.Equal(PlacementDirection.Bottom, result.Direction);
        Assert.Equal(new PhysicalPixelRect(600, 546, 400, 200), result.Rectangle);
    }

    [Fact]
    public void CandidateClearanceIsAlsoReservedAboveAnAnchorNearBottomEdge()
    {
        PlacementResult result = PlacementService.Place(
            new(700, 900, 200, 40), WorkArea, Keyboard, margin: 10, verticalCandidateClearance: 96);

        Assert.Equal(PlacementDirection.Top, result.Direction);
        Assert.Equal(new PhysicalPixelRect(600, 594, 400, 200), result.Rectangle);
    }

    [Theory]
    [InlineData(700, 1000, PlacementDirection.Top)]
    [InlineData(700, 0, PlacementDirection.Bottom)]
    [InlineData(0, 400, PlacementDirection.Right)]
    [InlineData(1880, 400, PlacementDirection.Left)]
    public void ChoosesNonOverlappingDirectionNearEachWorkAreaEdge(double x, double y, PlacementDirection expected)
    {
        PlacementResult result = PlacementService.Place(new(x, y, 40, 30), WorkArea, Keyboard, 10);
        Assert.Equal(expected, result.Direction);
        Assert.True(Contains(WorkArea, result.Rectangle!.Value));
    }

    [Fact]
    public void NegativeCoordinateWorkAreaIsPreserved()
    {
        var workArea = new PhysicalPixelRect(-1920, -200, 1920, 1080);
        PlacementResult result = PlacementService.Place(new(-1500, 100, 200, 40), workArea, Keyboard, 8);
        Assert.True(Contains(workArea, result.Rectangle!.Value));
        Assert.True(result.Rectangle.Value.X < 0);
    }

    [Fact]
    public void OversizedKeyboardScalesProportionallyToSafetyRatio()
    {
        var workArea = new PhysicalPixelRect(0, 0, 1000, 500);
        PlacementResult result = PlacementService.Place(new(400, 200, 50, 20), workArea, new(2000, 1000), 10);
        Assert.True(result.WasScaled);
        Assert.Equal(950, result.Rectangle!.Value.Width);
        Assert.Equal(475, result.Rectangle.Value.Height);
        Assert.True(Contains(workArea, result.Rectangle.Value));
    }

    [Fact]
    public void UsesWorkAreaRatherThanFullMonitorBounds()
    {
        var workAreaWithBottomTaskbar = new PhysicalPixelRect(0, 0, 1920, 1000);
        PlacementResult result = PlacementService.Place(new(800, 950, 100, 30), workAreaWithBottomTaskbar, Keyboard, 10);
        Assert.True(result.Rectangle!.Value.Bottom <= 1000);
        Assert.Equal(PlacementDirection.Top, result.Direction);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1)]
    public void InvalidMarginReturnsFailure(double margin)
    {
        PlacementResult result = PlacementService.Place(new(10, 10, 10, 10), WorkArea, Keyboard, margin);
        Assert.False(result.IsPlaced);
        Assert.Equal(PlacementStatus.InvalidInput, result.Status);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1)]
    public void InvalidCandidateClearanceReturnsFailure(double clearance)
    {
        PlacementResult result = PlacementService.Place(
            new(10, 10, 10, 10), WorkArea, Keyboard, margin: 10, verticalCandidateClearance: clearance);
        Assert.False(result.IsPlaced);
        Assert.Equal(PlacementStatus.InvalidInput, result.Status);
    }

    [Fact]
    public void ZeroWidthWorkAreaCannotProduceAPlacement()
    {
        PlacementResult result = PlacementService.Place(new(10, 10, 1, 10), new(0, 0, 0, 1000), Keyboard, 10);
        Assert.Equal(PlacementStatus.InvalidInput, result.Status);
    }

    private static bool Contains(PhysicalPixelRect outer, PhysicalPixelRect inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
}
