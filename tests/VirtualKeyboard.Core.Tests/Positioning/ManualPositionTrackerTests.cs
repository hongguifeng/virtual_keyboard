using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Positioning;

namespace VirtualKeyboard.Core.Tests.Positioning;

public sealed class ManualPositionTrackerTests
{
    [Fact]
    public void DragUsesPhysicalPointerDeltaAndBindsResultToSession()
    {
        var tracker = new ManualPositionTracker();
        tracker.Begin(7, new(100, 200, 400, 200), new(10, 20));

        Assert.True(tracker.TryUpdate(7, new(35, 5), out PhysicalPixelRect moved));
        Assert.Equal(new PhysicalPixelRect(125, 185, 400, 200), moved);
        Assert.True(tracker.End(7, out PhysicalPixelRect saved));
        Assert.Equal(moved, saved);
        Assert.True(tracker.TryGet(7, out PhysicalPixelRect current));
        Assert.Equal(moved, current);
        Assert.False(tracker.TryGet(8, out _));
    }

    [Fact]
    public void OtherSessionCannotUpdateOrEndActiveDrag()
    {
        var tracker = new ManualPositionTracker();
        tracker.Begin(7, new(100, 200, 400, 200), new(10, 20));
        Assert.False(tracker.TryUpdate(8, new(20, 30), out _));
        Assert.False(tracker.End(8, out _));
        Assert.True(tracker.End(7, out _));
    }

    [Fact]
    public void InvalidateClearsActiveAndSavedPosition()
    {
        var tracker = new ManualPositionTracker();
        tracker.Begin(7, new(100, 200, 400, 200), new(10, 20));
        tracker.End(7, out _);
        tracker.Invalidate();
        Assert.False(tracker.TryGet(7, out _));
        Assert.False(tracker.TryUpdate(7, new(30, 40), out _));
    }

    [Fact]
    public void CancelStopsDragWithoutDeletingPreviouslySavedPosition()
    {
        var tracker = new ManualPositionTracker();
        tracker.Begin(7, new(100, 200, 400, 200), new(10, 20));
        tracker.End(7, out _);
        tracker.Begin(7, new(150, 250, 400, 200), new(20, 30));
        tracker.Cancel();
        Assert.False(tracker.TryUpdate(7, new(30, 40), out _));
        Assert.True(tracker.TryGet(7, out PhysicalPixelRect saved));
        Assert.Equal(new PhysicalPixelRect(100, 200, 400, 200), saved);
    }

    [Fact]
    public void InvalidSessionGeometryOrPointerIsRejected()
    {
        var tracker = new ManualPositionTracker();
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Begin(0, new(1, 1, 1, 1), new(1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Begin(1, default, new(1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Begin(1, new(1, 1, 1, 1), new(double.NaN, 1)));
    }
}
