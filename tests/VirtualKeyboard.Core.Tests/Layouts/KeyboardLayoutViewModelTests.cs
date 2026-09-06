using VirtualKeyboard.Core.Layouts;

namespace VirtualKeyboard.Core.Tests.Layouts;

public sealed class KeyboardLayoutViewModelTests
{
    [Fact]
    public void ValidLayoutMapsRowsKeysWeightsAndActionIdentity()
    {
        var action = new LayoutActionDefinition(LayoutActionTypes.Key, virtualKey: "A");
        var layout = new KeyboardLayoutDefinition(
            1,
            "layout",
            "Layout",
            "en-US",
            [new KeyboardLayoutRow([new("key.a", "A", 1.5, true, action)])]);

        KeyboardLayoutViewModel viewModel = KeyboardLayoutViewModel.Create(layout);

        KeyViewModel key = Assert.Single(Assert.Single(viewModel.Rows).Keys);
        Assert.Equal("layout", viewModel.Id);
        Assert.Equal("key.a", key.Id);
        Assert.Equal("A", key.Label);
        Assert.Equal(1.5, key.Width);
        Assert.True(key.SafeForPassword);
        Assert.Same(action, key.Action);
    }

    [Fact]
    public void InvalidLayoutCannotReachViewModel()
    {
        var layout = new KeyboardLayoutDefinition(99, "layout", "Layout", "en-US", []);

        Assert.Throws<ArgumentException>(() => KeyboardLayoutViewModel.Create(layout));
    }

    [Fact]
    public void GestureEmitsAtMostOncePerPress()
    {
        var gesture = new KeyGestureController();

        Assert.True(gesture.Begin());
        Assert.False(gesture.Begin());
        Assert.True(gesture.Release(isInside: true));
        Assert.False(gesture.Release(isInside: true));
    }

    [Fact]
    public void ReleaseOutsideAndCancellationNeverInvoke()
    {
        var gesture = new KeyGestureController();

        Assert.True(gesture.Begin());
        Assert.False(gesture.Release(isInside: false));
        Assert.True(gesture.Begin());
        Assert.True(gesture.Cancel());
        Assert.False(gesture.Release(isInside: true));
        Assert.False(gesture.Cancel());
    }
}
