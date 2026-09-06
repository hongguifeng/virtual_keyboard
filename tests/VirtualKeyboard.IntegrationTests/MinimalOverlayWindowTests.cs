using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using VirtualKeyboard.App;

namespace VirtualKeyboard.IntegrationTests;

public sealed class MinimalOverlayWindowTests
{
    [Fact]
    public void WindowAndInteractiveControlsUseNonActivatingConfiguration()
    {
        RunOnStaThread(() =>
        {
            using var window = new MainWindow();

            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.False(window.ShowInTaskbar);
            Assert.False(window.ShowActivated);
            Assert.True(window.Topmost);

            var dragArea = Assert.IsType<Grid>(window.FindName("DragArea"));
            var closeButton = Assert.IsType<Button>(window.FindName("CloseButton"));
            var keyAButton = Assert.IsType<Button>(window.FindName("KeyAButton"));
            Assert.False(dragArea.Focusable);
            Assert.False(closeButton.Focusable);
            Assert.False(closeButton.IsTabStop);
            Assert.False(keyAButton.Focusable);
            Assert.False(keyAButton.IsTabStop);
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
