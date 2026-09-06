using VirtualKeyboard.App;

namespace VirtualKeyboard.IntegrationTests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public void SecondCoordinatorSignalsPrimaryWithoutBecomingPrimary()
    {
        string scope = $"VirtualKeyboard.Tests.{Guid.NewGuid():N}";
        using var activated = new ManualResetEventSlim();
        using var primary = SingleInstanceCoordinator.CreateForTest(scope, activated.Set);
        using var secondary = SingleInstanceCoordinator.CreateForTest(scope, static () => { });

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        secondary.NotifyPrimary();

        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void PrimaryNotificationIsANoOpAndDisposeIsIdempotent()
    {
        string scope = $"VirtualKeyboard.Tests.{Guid.NewGuid():N}";
        int activations = 0;
        var primary = SingleInstanceCoordinator.CreateForTest(scope, () => Interlocked.Increment(ref activations));

        primary.NotifyPrimary();
        primary.Dispose();
        primary.Dispose();

        Assert.True(primary.IsPrimary);
        Assert.Equal(0, Volatile.Read(ref activations));

        using var replacement = SingleInstanceCoordinator.CreateForTest(scope, static () => { });
        Assert.True(replacement.IsPrimary);
    }
}
