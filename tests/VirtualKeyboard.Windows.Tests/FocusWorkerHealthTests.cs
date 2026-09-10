using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class FocusWorkerHealthTests
{
    [Fact]
    public void HeartbeatsWithoutCompletedWorkCannotConcealBlockedProvider()
    {
        var health = new FocusWorkerHealth(TimeSpan.FromSeconds(5));
        health.Observe(1, TimeSpan.Zero);
        health.Observe(1, TimeSpan.FromSeconds(4));
        Assert.True(health.IsExpired(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void OnlyIncreasingProgressRenewsDeadline()
    {
        var health = new FocusWorkerHealth(TimeSpan.FromSeconds(5));
        health.Observe(2, TimeSpan.FromSeconds(3));
        health.Observe(1, TimeSpan.FromSeconds(6));
        Assert.False(health.IsExpired(TimeSpan.FromSeconds(7)));
        Assert.True(health.IsExpired(TimeSpan.FromSeconds(8)));
    }

    [Fact]
    public void NoStartupProgressAlsoExpires()
    {
        var health = new FocusWorkerHealth(TimeSpan.FromSeconds(5));
        Assert.True(health.IsExpired(TimeSpan.FromSeconds(5)));
    }
}
