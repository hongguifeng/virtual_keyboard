using System.Runtime.InteropServices;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class FocusedElementResolverTests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2201", Justification = "Simulates a UIA provider COM failure.")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrFailedUiaFocusUsesFallback(bool throws)
    {
        int fallbacks = 0;
        Assert.Null(FocusedElementResolver.Resolve(
            () => throws ? throw new COMException("synthetic") : null,
            () => { fallbacks++; return null; }, out bool usedFallback));
        Assert.True(usedFallback);
        Assert.Equal(1, fallbacks);
    }

    [Fact]
    public void ExpectedFallbackFailureReturnsNoTarget()
    {
        Assert.Null(FocusedElementResolver.Resolve(() => null,
            () => throw new InvalidOperationException("synthetic"), out bool usedFallback));
        Assert.True(usedFallback);
    }

    [Fact]
    public void UnexpectedFailureIsNotSilentlySwallowed()
    {
        Assert.Throws<ArgumentException>(() => FocusedElementResolver.Resolve(
            () => throw new ArgumentException("synthetic"), () => null, out _));
    }
}
