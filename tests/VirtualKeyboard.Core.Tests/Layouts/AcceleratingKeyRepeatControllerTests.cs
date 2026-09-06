using VirtualKeyboard.Core.Layouts;

namespace VirtualKeyboard.Core.Tests.Layouts;

public sealed class AcceleratingKeyRepeatControllerTests
{
    [Fact]
    public void ShortPressInvokesOnceOnRelease()
    {
        var repeat = new AcceleratingKeyRepeatController();

        Assert.Equal(AcceleratingKeyRepeatController.InitialDelay, repeat.Begin());
        Assert.True(repeat.Release(isInside: true));
        Assert.False(repeat.HasRepeated);
    }

    [Fact]
    public void HeldPressAcceleratesToBoundedMinimumAndSuppressesReleaseClick()
    {
        var repeat = new AcceleratingKeyRepeatController();
        repeat.Begin();
        var delays = new List<TimeSpan>();

        for (int index = 0; index < 20; index++)
        {
            KeyRepeatTick tick = repeat.Tick();
            Assert.True(tick.ShouldInvoke);
            delays.Add(tick.NextDelay);
        }

        Assert.Equal(AcceleratingKeyRepeatController.StartingInterval, delays[0]);
        Assert.All(delays.Zip(delays.Skip(1)), pair => Assert.True(pair.Second <= pair.First));
        Assert.All(delays, delay => Assert.True(delay >= AcceleratingKeyRepeatController.MinimumInterval));
        Assert.Equal(AcceleratingKeyRepeatController.MinimumInterval, delays[^1]);
        Assert.False(repeat.Release(isInside: true));
    }

    [Fact]
    public void CancelAndOutsideReleaseNeverInvoke()
    {
        var repeat = new AcceleratingKeyRepeatController();
        repeat.Begin();
        repeat.Cancel();
        Assert.False(repeat.Tick().ShouldInvoke);

        repeat.Begin();
        Assert.False(repeat.Release(isInside: false));
        Assert.False(repeat.Tick().ShouldInvoke);
    }
}
