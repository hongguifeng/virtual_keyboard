using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Core.Geometry;

namespace VirtualKeyboard.Core.Tests;

public sealed class TargetSessionStoreTests
{
    [Fact]
    public void ReplacePublishesSessionWithMonotonicIds()
    {
        var store = new TargetSessionStore();
        var first = store.Replace(Snapshot(10, 20, 30));
        var second = store.Replace(Snapshot(11, 21, 31));

        Assert.Equal(1, first.SessionId);
        Assert.Equal(2, second.SessionId);
        Assert.Same(second, store.Current);
        Assert.Equal(11, second.ProcessId);
        Assert.Equal((nint)21, second.TopLevelHwnd);
        Assert.Equal((nint)31, second.FocusHwnd);
    }

    [Fact]
    public void ClearRemovesCurrentSessionWithoutResettingVersion()
    {
        var store = new TargetSessionStore();
        _ = store.Replace(Snapshot(1, 2, 3));
        store.Clear();
        Assert.Null(store.Current);

        var next = store.Replace(Snapshot(4, 5, 6));
        Assert.Equal(2, next.SessionId);
    }

    [Fact]
    public void ConcurrentReplaceKeepsNewestPublishedVersion()
    {
        const int replacementCount = 256;
        var store = new TargetSessionStore();

        Parallel.For(0, replacementCount, index =>
            store.Replace(Snapshot(index + 1, index + 2, index + 3)));

        Assert.Equal(replacementCount, store.Current?.SessionId);
    }

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(-1, 1, 2)]
    public void ReplaceRejectsInvalidProcessId(int processId, int topLevel, int focus)
    {
        var store = new TargetSessionStore();
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Replace(Snapshot(processId, topLevel, focus)));
    }

    [Fact]
    public void ReplaceRejectsZeroHandles()
    {
        var store = new TargetSessionStore();
        Assert.Throws<ArgumentException>(() => store.Replace(Snapshot(1, 0, 2)));
        Assert.Throws<ArgumentException>(() => store.Replace(Snapshot(1, 2, 0)));
    }

    [Fact]
    public void FocusSnapshotSessionPreservesVersionRuntimePasswordAndAnchor()
    {
        var store = new TargetSessionStore();
        var focus = new FocusSnapshot(
            9, DateTimeOffset.UtcNow, 42, (nint)100, new RuntimeIdentity([1, 2]),
            FocusControlType.Edit, true, true, false, true);
        var anchor = new PhysicalPixelRect(10, 20, 1, 18);

        TargetSession session = store.Replace(focus, (nint)101, anchor);

        Assert.Equal(9, session.FocusVersion);
        Assert.Equal(new RuntimeIdentity([1, 2]), session.RuntimeId);
        Assert.True(session.IsPassword);
        Assert.Equal(anchor, session.Anchor);
    }

    [Fact]
    public void LatestFocusStoreRejectsOlderAndDuplicateVersions()
    {
        var store = new LatestFocusSnapshotStore();
        FocusSnapshot newer = Focus(2, [2]);
        Assert.True(store.Publish(newer));
        Assert.False(store.Publish(Focus(1, [1])));
        Assert.False(store.Publish(Focus(2, [3])));
        Assert.Same(newer, store.Current);
        store.Clear();
        Assert.Null(store.Current);
    }

    private static TargetCaptureSnapshot Snapshot(int processId, int topLevel, int focus) =>
        new(new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero), processId, (nint)topLevel, (nint)focus);

    private static FocusSnapshot Focus(long version, int[] runtimeId) =>
        new(version, DateTimeOffset.UtcNow, 42, (nint)100, new RuntimeIdentity(runtimeId), FocusControlType.Edit, true, true, false, false);
}
