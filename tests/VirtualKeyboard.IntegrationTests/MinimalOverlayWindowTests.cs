using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using VirtualKeyboard.App;
using VirtualKeyboard.Core.Targeting;

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
            var captureTargetButton = Assert.IsType<Button>(window.FindName("CaptureTargetButton"));
            var keyAButton = Assert.IsType<Button>(window.FindName("KeyAButton"));
            Assert.False(dragArea.Focusable);
            Assert.False(closeButton.Focusable);
            Assert.False(closeButton.IsTabStop);
            Assert.False(captureTargetButton.Focusable);
            Assert.False(captureTargetButton.IsTabStop);
            Assert.False(keyAButton.Focusable);
            Assert.False(keyAButton.IsTabStop);
        });
    }

    [Fact]
    public void CaptureButtonPublishesAndDisplaysTargetSession()
    {
        RunOnStaThread(() =>
        {
            var snapshot = new TargetCaptureSnapshot(
                new DateTimeOffset(2026, 9, 6, 1, 2, 3, TimeSpan.Zero),
                42,
                (nint)100,
                (nint)101);
            using var window = new MainWindow(new StubCapture(snapshot), new TargetSessionStore());
            var captureButton = Assert.IsType<Button>(window.FindName("CaptureTargetButton"));
            var status = Assert.IsType<TextBlock>(window.FindName("SessionStatusText"));

            captureButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            TargetSession session = Assert.IsType<TargetSession>(window.CurrentTargetSession);
            Assert.Equal(1, session.SessionId);
            Assert.Equal(42, session.ProcessId);
            Assert.Contains("会话 1", status.Text, StringComparison.Ordinal);
            Assert.Contains("PID 42", status.Text, StringComparison.Ordinal);
            Assert.Contains("0x64", status.Text, StringComparison.Ordinal);
            Assert.Contains("0x65", status.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CloseButtonClosesWindowAndDisposesOwnedResources()
    {
        RunOnStaThread(() =>
        {
            using var window = new MainWindow();
            window.ShowAt(-12000, -11000, 360, 176);
            var closeButton = Assert.IsType<Button>(window.FindName("CloseButton"));

            closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.False(window.IsVisible);
            Assert.True(window.IsDisposed);
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

    private sealed class StubCapture(TargetCaptureSnapshot snapshot) : IForegroundTargetCapture
    {
        public TargetCaptureResult Capture() => TargetCaptureResult.Success(snapshot);
    }
}
