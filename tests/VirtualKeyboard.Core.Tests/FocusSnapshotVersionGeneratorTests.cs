using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Core.Tests;

public sealed class FocusSnapshotVersionGeneratorTests
{
    [Fact]
    public void VersionsIncreaseAndRuntimeIdIsCopied()
    {
        var generator = new FocusSnapshotVersionGenerator(900);
        var runtimeId = new[] { 1, 2, 3 };

        Assert.True(generator.TryCreate(DateTimeOffset.UtcNow, 901, (nint)10, runtimeId, FocusControlType.Edit, true, true, false, false, out var first));
        runtimeId[0] = 99;
        Assert.True(generator.TryCreate(DateTimeOffset.UtcNow, 902, (nint)11, null, FocusControlType.Button, true, true, false, false, out var second));

        Assert.Equal(1, first?.Version);
        Assert.Collection(first!.RuntimeId!.ToArray(),
            value => Assert.Equal(1, value),
            value => Assert.Equal(2, value),
            value => Assert.Equal(3, value));
        int[] exposedCopy = first.RuntimeId.ToArray();
        exposedCopy[0] = 88;
        Assert.Equal(1, first.RuntimeId[0]);
        Assert.Equal(2, second?.Version);
    }

    [Theory]
    [InlineData(900, 900, 10)]
    [InlineData(901, 0, 10)]
    public void SelfProcessOrInvalidIdentityIsIgnored(int processId, int observedProcessId, int hwnd)
    {
        var generator = new FocusSnapshotVersionGenerator(processId);
        Assert.False(generator.TryCreate(DateTimeOffset.UtcNow, observedProcessId, (nint)hwnd, null, FocusControlType.Edit, true, true, false, false, out var snapshot));
        Assert.Null(snapshot);

        Assert.True(generator.TryCreate(DateTimeOffset.UtcNow, processId + 10, (nint)10, null, FocusControlType.Edit, true, true, false, false, out var next));
        Assert.Equal(2, next?.Version);
    }
}
