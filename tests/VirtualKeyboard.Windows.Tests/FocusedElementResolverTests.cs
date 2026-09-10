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

    [Fact]
    public void UnfocusedContainerIsReplacedByFocusedNativeDescendant()
    {
        var container = new Node(false);
        var editor = new Node(true);
        var result = FocusedElementResolver.ResolveFocus(() => container, () => editor,
            node => node.Focused, out bool fallback);
        Assert.Same(editor, result);
        Assert.True(fallback);
    }

    [Fact]
    public void StableFocusDoesNotInvokeNativeFallback()
    {
        var editor = new Node(true);
        Assert.Same(editor, FocusedElementResolver.ResolveFocus(() => editor,
            () => throw new InvalidOperationException("must not run"), node => node.Focused, out bool fallback));
        Assert.False(fallback);
    }

    [Fact]
    public void MissingFocusedDescendantKeepsUnfocusedEvidenceWithoutPromotingContainer()
    {
        var container = new Node(false);
        var result = FocusedElementResolver.ResolveFocus(() => container, () => null,
            node => node.Focused, out bool fallback);
        Assert.Same(container, result);
        Assert.False(result!.Focused);
        Assert.True(fallback);
    }

    [Fact]
    public void EventTargetReplacesUnfocusedQueryAndSurvivesPolling()
    {
        var editor = new Node(true);
        for (int i = 0; i < 3; i++)
        {
            var result = FocusedElementResolver.ResolveEventFocus(() => new Node(false),
                editor, node => node.Focused, () => throw new InvalidOperationException("unexpected fallback"),
                node => node.Focused, out bool fallback, out bool fromEvent);
            Assert.Same(editor, result);
            Assert.True(fromEvent);
            Assert.False(fallback);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidOrDestroyedEventTargetFallsBack(bool destroyed)
    {
        var fallbackNode = new Node(true);
        var result = FocusedElementResolver.ResolveEventFocus(() => new Node(false), new Node(true),
            _ => destroyed ? throw new InvalidOperationException("destroyed") : false,
            () => fallbackNode, node => node.Focused, out bool fallback, out bool fromEvent);
        Assert.Same(fallbackNode, result);
        Assert.False(fromEvent);
        Assert.True(fallback);
    }

    [Fact]
    public void NewFocusedQueryOverridesRetainedEventWithoutAnotherEvent()
    {
        var newer = new Node(true);
        var result = FocusedElementResolver.ResolveEventFocus(() => newer, new Node(true),
            _ => true, () => null, node => node.Focused, out _, out bool fromEvent);
        Assert.Same(newer, result);
        Assert.False(fromEvent);
    }

    [Theory]
    [InlineData(0, 42, true, false)]
    [InlineData(123, 43, true, false)]
    [InlineData(123, 42, false, false)]
    [InlineData(123, 42, true, true)]
    public void NativeFallbackOnlyAcceptsFocusedRootInExpectedProcess(int hwnd, int processId, bool focused, bool accepted)
    {
        int lookups = 0;
        var root = new Node(focused);
        var result = FocusedElementResolver.ResolveNativeFocus((nint)hwnd, 42,
            handle => { Assert.Equal((nint)123, handle); lookups++; return root; },
            _ => processId, node => node.Focused);
        Assert.Equal(accepted, result is not null);
        Assert.Equal(hwnd == 0 ? 0 : 1, lookups);
    }

    private sealed record Node(bool Focused);
}
