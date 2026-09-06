using System.Runtime.ExceptionServices;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using VirtualKeyboard.App;
using VirtualKeyboard.Core.Diagnostics;
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

    [Fact]
    public void DiagnosticsViewDisplaysAllowListedMetadataAndExportsCurrentReport()
    {
        RunOnStaThread(() =>
        {
            var view = new FocusDiagnosticsView();
            var report = new FocusDiagnosticReport(
                DateTimeOffset.UtcNow, 9, 42, 100, FocusControlType.Edit,
                true, true, false, false, Editability.Editable,
                ClassificationReasonCode.ValuePattern, true);

            view.Update(report);

            Assert.Equal(report, view.Current);
            var classification = Assert.IsType<TextBlock>(view.FindName("ClassificationText"));
            var identity = Assert.IsType<TextBlock>(view.FindName("IdentityText"));
            Assert.Contains("Editable", classification.Text, StringComparison.Ordinal);
            Assert.Contains("ValuePattern", classification.Text, StringComparison.Ordinal);
            Assert.Contains("PID 42", identity.Text, StringComparison.Ordinal);
            using var exported = new MemoryStream();
            view.ExportCurrent(exported);
            Assert.True(exported.Length > 0);
        });
    }

    [Fact]
    public void DpiChangedRecalculatesConfiguredDipSizeForCurrentSession()
    {
        RunOnStaThread(() =>
        {
            var target = new TargetCaptureSnapshot(DateTimeOffset.UtcNow, 42, (nint)100, (nint)101);
            using var window = new MainWindow(new StubCapture(target), new TargetSessionStore());
            var capture = Assert.IsType<Button>(window.FindName("CaptureTargetButton"));
            capture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.ShowAt(-12000, -11000, 360, 176);

            var suggested = new NativeRectangle(-9000, -8000, -8600, -7800);
            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRectangle>());
            try
            {
                Marshal.StructureToPtr(suggested, pointer, false);
                SendMessage(window.OverlayHandle, 0x02E0, new((192 << 16) | 192), pointer);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }

            Assert.True(GetWindowRect(window.OverlayHandle, out NativeRectangle actual));
            Assert.Equal(new NativeRectangle(-9000, -8000, -8280, -7648), actual);
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

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRectangle(int Left, int Top, int Right, int Bottom);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(nint hwnd, int message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRectangle rectangle);
}
