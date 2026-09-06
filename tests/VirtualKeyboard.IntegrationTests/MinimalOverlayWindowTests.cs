using System.Runtime.ExceptionServices;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using VirtualKeyboard.App;
using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Layouts;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

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
            Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
            Assert.True(window.MinWidth >= ConfigurationSchemaLimits.MinimumKeyboardWidthDip);
            Assert.True(window.MinHeight >= ConfigurationSchemaLimits.MinimumKeyboardHeightDip);

            var dragArea = Assert.IsType<Grid>(window.FindName("DragArea"));
            var closeButton = Assert.IsType<Button>(window.FindName("CloseButton"));
            var layoutView = Assert.IsType<KeyboardLayoutView>(window.FindName("LayoutView"));
            NonFocusableKeyButton[] keys = Descendants<NonFocusableKeyButton>(layoutView).ToArray();
            var keyAButton = Assert.Single(keys, button => button.Key.Id == "key.a");
            Assert.False(dragArea.Focusable);
            Assert.False(closeButton.Focusable);
            Assert.False(closeButton.IsTabStop);
            Assert.Null(window.FindName("CaptureTargetButton"));
            Assert.False(keyAButton.Focusable);
            Assert.False(keyAButton.IsTabStop);
            Assert.All(keys, button =>
            {
                Assert.False(button.Focusable);
                Assert.False(button.IsTabStop);
                Assert.True(button.MinHeight >= KeyboardLayoutView.MinimumKeyHeight);
            });
        });
    }

    [Fact]
    public void LayoutUsesStarWeightsAndExplicitGestureLifecycle()
    {
        RunOnStaThread(() =>
        {
            using var window = new MainWindow();
            var layoutView = Assert.IsType<KeyboardLayoutView>(window.FindName("LayoutView"));
            Grid firstRow = Assert.IsType<Grid>(layoutView.Children[0]);
            Assert.All(firstRow.ColumnDefinitions, column =>
            {
                Assert.True(column.Width.IsStar);
                Assert.True(column.MinWidth >= KeyboardLayoutView.MinimumKeyWidth);
            });
            Assert.Equal(1.2, firstRow.ColumnDefinitions[0].Width.Value);
            Assert.Equal(1.0, firstRow.ColumnDefinitions[1].Width.Value);

            NonFocusableKeyButton key = Assert.IsType<NonFocusableKeyButton>(firstRow.Children[1]);
            int invoked = 0;
            key.Invoked += (_, _) => invoked++;
            Assert.True(key.BeginGestureForTest());
            Assert.False(key.BeginGestureForTest());
            Assert.True(key.EndGestureForTest(isInside: true));
            Assert.False(key.EndGestureForTest(isInside: true));
            Assert.Equal(1, invoked);

            Assert.True(key.BeginGestureForTest());
            key.CancelGestureForTest();
            Assert.False(key.EndGestureForTest(isInside: true));
            Assert.Equal(1, invoked);
        });
    }

    [Fact]
    public void PasswordLayoutHidesActionsNotSafeForPassword()
    {
        RunOnStaThread(() =>
        {
            var action = new LayoutActionDefinition(LayoutActionTypes.Text, value: "phrase");
            var layout = new KeyboardLayoutDefinition(
                1, "password", "Password", "en-US",
                [new KeyboardLayoutRow([
                    new("safe", "A", 1, true, new(LayoutActionTypes.Key, virtualKey: "A")),
                    new("unsafe", "Phrase", 1, false, action),
                    new("unsafe-hotkey", "Copy", 1, true, new(LayoutActionTypes.Hotkey, virtualKey: "C", modifiers: ["Control"]))])]);
            var view = new KeyboardLayoutView();
            view.LoadLayout(KeyboardLayoutViewModel.Create(layout), passwordTarget: true);

            NonFocusableKeyButton[] keys = Descendants<NonFocusableKeyButton>(view).ToArray();
            Assert.Single(keys);
            Assert.Equal("safe", keys[0].Key.Id);
        });
    }

    [Fact]
    public void ModifierVisualsMirrorControllerSnapshot()
    {
        RunOnStaThread(() =>
        {
            using var window = new MainWindow();
            var view = Assert.IsType<KeyboardLayoutView>(window.FindName("LayoutView"));
            NonFocusableKeyButton[] buttons = Descendants<NonFocusableKeyButton>(view).ToArray();
            NonFocusableKeyButton shift = Assert.Single(buttons, button => button.Key.Id == "key.shift");
            NonFocusableKeyButton control = Assert.Single(buttons, button => button.Key.Id == "key.control");
            NonFocusableKeyButton windows = Assert.Single(buttons, button => button.Key.Id == "key.windows");
            NonFocusableKeyButton function = Assert.Single(buttons, button => button.Key.Id == "key.fn");
            NonFocusableKeyButton numberOne = Assert.Single(buttons, button => button.Key.Id == "key.1");
            NonFocusableKeyButton caps = Assert.Single(buttons, button => button.Key.Id == "key.capsLock");

            view.UpdateState(new(7, 1, true, false, false, true, true, true, true));

            Assert.True(shift.IsModifierActive);
            Assert.False(control.IsModifierActive);
            Assert.True(windows.IsModifierActive);
            Assert.True(function.IsModifierActive);
            Assert.True(caps.IsModifierActive);
            Assert.Equal("F1", numberOne.Content);
            Assert.True(shift.Opacity < 1);
        });
    }

    [Fact]
    public void AutomaticFocusPublishesTargetSessionWithoutCaptureControl()
    {
        RunOnStaThread(() =>
        {
            using var window = new MainWindow(new StubCapture(default), new TargetSessionStore());
            var status = Assert.IsType<TextBlock>(window.FindName("SessionStatusText"));

            Assert.True(ApplyEditableFocus(window, version: 1, runtimeId: 7));

            TargetSession session = Assert.IsType<TargetSession>(window.CurrentTargetSession);
            Assert.Equal(1, session.SessionId);
            Assert.Equal(42, session.ProcessId);
            Assert.Equal("输入目标已就绪", status.Text);
            Assert.Null(window.FindName("CaptureTargetButton"));
        });
    }

    [Fact]
    public void CloseButtonHidesReusableWindowWithoutDisposingOwnedResources()
    {
        RunOnStaThread(() =>
        {
            using var window = new MainWindow();
            window.ShowAt(-12000, -11000, 360, 176);
            var closeButton = Assert.IsType<Button>(window.FindName("CloseButton"));

            closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.False(window.IsVisible);
            Assert.False(window.IsDisposed);
        });
    }

    [Fact]
    public void DisposeTransitionsToShutdownClearsTargetAndStopsInputIdempotently()
    {
        RunOnStaThread(() =>
        {
            var sessions = new TargetSessionStore();
            var window = new MainWindow(new StubCapture(default), sessions);
            Assert.True(ApplyEditableFocus(window, version: 1, runtimeId: 7));
            Assert.NotNull(window.CurrentTargetSession);

            window.Dispose();
            window.Dispose();

            Assert.True(window.IsDisposed);
            Assert.Equal(TargetCoordinatorState.ShuttingDown, window.CoordinatorState);
            Assert.Equal(0, window.InputQueueSessionId);
            Assert.Null(window.CurrentTargetSession);
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
            using var window = new MainWindow(new StubCapture(default), new TargetSessionStore());
            Assert.True(ApplyEditableFocus(window, version: 1, runtimeId: 7));
            window.ShowAt(-12000, -11000, 360, 176);
            Assert.True(window.BeginManualMoveForCurrentSession());
            Assert.True(window.EndManualMoveForCurrentSession());
            Assert.True(window.HasManualPosition(1));

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
            Assert.Equal(new NativeRectangle(-9000, -8000, -7400, -7400), actual);
            Assert.False(window.HasManualPosition(1));
        });
    }

    [Fact]
    public void AutomaticTargetReplacementInvalidatesPreviousManualPosition()
    {
        RunOnStaThread(() =>
        {
            using var window = new MainWindow(new StubCapture(default), new TargetSessionStore());
            Assert.True(ApplyEditableFocus(window, version: 1, runtimeId: 7));
            window.ShowAt(-12000, -11000, 360, 176);
            Assert.True(window.BeginManualMoveForCurrentSession());
            Assert.True(window.EndManualMoveForCurrentSession());
            Assert.True(window.HasManualPosition(1));

            Assert.True(ApplyEditableFocus(window, version: 2, runtimeId: 8));

            Assert.Equal(2, window.CurrentTargetSession!.SessionId);
            Assert.False(window.HasManualPosition(1));
        });
    }

    private static bool ApplyEditableFocus(MainWindow window, long version, int runtimeId)
    {
        var snapshot = new FocusSnapshot(
            version, DateTimeOffset.UtcNow, 42, (nint)100, new RuntimeIdentity([runtimeId]),
            FocusControlType.Edit, true, true, false, false);
        return window.ApplyEvaluatedFocusForTest(new(
            FocusTargetEvaluationStatus.Evaluated,
            snapshot,
            new(version, Editability.Editable, ClassificationReasonCode.ValuePattern, false),
            (nint)101,
            new PhysicalPixelRect(300, 300, 100, 30)));
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

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }
            foreach (T descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class StubCapture(TargetCaptureSnapshot? snapshot) : IForegroundTargetCapture
    {
        public TargetCaptureResult Capture() => snapshot is null
            ? TargetCaptureResult.Failure(TargetCaptureStatus.NoForegroundWindow)
            : TargetCaptureResult.Success(snapshot);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRectangle(int Left, int Top, int Right, int Bottom);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(nint hwnd, int message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRectangle rectangle);
}
