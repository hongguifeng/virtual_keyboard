using System.Windows;
using System.Windows.Controls;
using System.Reflection;
using VirtualKeyboard.App;
using VirtualKeyboard.Core.Layouts;

namespace VirtualKeyboard.IntegrationTests;

public sealed class TouchInteractionTests
{
    [Fact]
    public void KeyGestureControllerProvidesSameSingleFingerLifecycleAsMouse()
    {
        RunOnStaThread(() =>
        {
            var view = new KeyboardLayoutView();
            var layout = new KeyboardLayoutDefinition(
                1, "touch", "Touch", "en-US",
                [new KeyboardLayoutRow([new("key.a", "A", 1, true, new(LayoutActionTypes.Key, virtualKey: "A"))])]);
            view.LoadLayout(KeyboardLayoutViewModel.Create(layout));
            var button = Assert.IsType<NonFocusableKeyButton>(Assert.IsType<Grid>(view.Children[0]).Children[0]);
            int invoked = 0;
            button.Invoked += (_, _) => invoked++;

            Assert.True(button.BeginGestureForTest());
            Assert.True(button.EndGestureForTest(isInside: true));
            Assert.Equal(1, invoked);

            Assert.True(button.BeginGestureForTest());
            button.CancelGestureForTest();
            Assert.False(button.EndGestureForTest(isInside: true));
            Assert.Equal(1, invoked);
            Assert.False(button.IsGesturePressed);
        });
    }

    [Fact]
    public void BackspaceShortPressInvokesOnceAndHeldPressRepeatsWithoutReleaseClick()
    {
        RunOnStaThread(() =>
        {
            var key = new KeyViewModel(
                "key.backspace", "Backspace", 2, true,
                new LayoutActionDefinition(LayoutActionTypes.Key, virtualKey: "Backspace"));
            var button = new NonFocusableKeyButton(key);
            var repeats = new List<bool>();
            button.Invoked += (_, args) => repeats.Add(args.IsRepeat);

            Assert.True(button.BeginGestureForTest());
            Assert.True(button.EndGestureForTest(isInside: true));
            Assert.Equal([false], repeats);

            Assert.True(button.BeginGestureForTest());
            Assert.True(button.RepeatTickForTest());
            Assert.True(button.RepeatTickForTest());
            Assert.False(button.EndGestureForTest(isInside: true));
            Assert.Equal([false, true, true], repeats);

            Assert.True(button.BeginGestureForTest());
            Assert.True(button.RepeatTickForTest());
            button.CancelGestureForTest();
            Assert.False(button.RepeatTickForTest());
            Assert.False(button.EndGestureForTest(isInside: true));
            Assert.Equal([false, true, true, true], repeats);
        });
    }

    [Fact]
    public void OtherKeysNeverEnterRepeatMode()
    {
        RunOnStaThread(() =>
        {
            var key = new KeyViewModel(
                "key.a", "A", 1, true,
                new LayoutActionDefinition(LayoutActionTypes.Key, virtualKey: "A"));
            var button = new NonFocusableKeyButton(key);
            int invoked = 0;
            button.Invoked += (_, _) => invoked++;

            Assert.True(button.BeginGestureForTest());
            Assert.False(button.RepeatTickForTest());
            Assert.True(button.EndGestureForTest(isInside: true));
            Assert.Equal(1, invoked);
        });
    }

    [Theory]
    [InlineData("OnPreviewMouseLeftButtonDown")]
    [InlineData("OnPreviewMouseLeftButtonUp")]
    [InlineData("OnLostMouseCapture")]
    [InlineData("OnTouchDown")]
    [InlineData("OnTouchMove")]
    [InlineData("OnTouchUp")]
    [InlineData("OnLostTouchCapture")]
    public void KeyButtonExplicitlyOwnsPointerLifecycle(string methodName)
    {
        MethodInfo? method = typeof(NonFocusableKeyButton).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(typeof(NonFocusableKeyButton), method.DeclaringType);
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
