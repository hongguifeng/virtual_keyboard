using System.IO;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using VirtualKeyboard.App;
using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Core.Layouts;
using VirtualKeyboard.Core.Positioning;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.IntegrationTests;

[CollectionDefinition("Launcher desktop", DisableParallelization = true)]
public sealed class LauncherDesktopDefinition;

[Collection("Launcher desktop")]
public sealed class LauncherWindowTests
{
    [Fact]
    public void CustomButtonsAreEqualSquaresInOneRowAndExecuteEveryActionWithoutExpanding()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture(keys:
                [new("Phrase", "text", "Hello 世界"), new("Enter", "key", "Enter"),
                 new("Save", "hotkey", "S", ["Control"]), new("Switch", "chord", "", ["Control", "Tab"])]);
            using var main = fixture.CreateWindow();
            main.ApplyEvaluatedFocusForTest(Evaluation(1, 10));
            KeyboardLauncherWindow launcher = main.Launcher!;
            PumpDispatcher();
            var expand = Assert.IsType<Button>(launcher.FindName("ExpandButton"));
            NonFocusableKeyButton[] buttons = LauncherButtons(launcher);
            Assert.Equal(4, buttons.Length);
            double previousRight = expand.TranslatePoint(new(expand.ActualWidth, 0), launcher).X;
            foreach (NonFocusableKeyButton button in buttons)
            {
                Assert.False(button.Focusable);
                Assert.False(button.IsTabStop);
                Assert.Equal(expand.ActualWidth, button.ActualWidth);
                Assert.Equal(expand.ActualHeight, button.ActualHeight);
                Point origin = button.TranslatePoint(new(), launcher);
                Assert.True(origin.X > previousRight);
                Assert.Equal(expand.TranslatePoint(new(), launcher).Y, origin.Y);
                previousRight = button.TranslatePoint(new(button.ActualWidth, 0), launcher).X;
                Assert.True(button.BeginGestureForTest());
                Assert.True(button.EndGestureForTest(true));
            }
            WaitUntil(() => fixture.Sent.Count == 4);
            Assert.Equal(["text:Hello 世界", "key:Enter", "hotkey:Control+S", "chord:Control+Tab"], fixture.Sent);
            Assert.False(main.IsVisible);
            Assert.True(launcher.IsVisible);
            Assert.Equal(TargetCoordinatorState.LauncherTracking, main.CoordinatorState);
        });
    }

    [Fact]
    public void TargetChangeCancelsHeldButtonAndDetachedButtonsCannotSendToNewTarget()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture(keys: [new("Erase", "key", "Backspace")]);
            using var main = fixture.CreateWindow();
            main.ApplyEvaluatedFocusForTest(Evaluation(1, 10));
            NonFocusableKeyButton old = Assert.Single(LauncherButtons(main.Launcher!));
            Assert.True(old.BeginGestureForTest());
            main.ApplyEvaluatedFocusForTest(Evaluation(2, 20));
            Assert.False(old.IsGesturePressed);
            Assert.False(old.EndGestureForTest(true));
            Assert.False(old.RepeatTickForTest());
            old.BeginGestureForTest();
            old.EndGestureForTest(true);
            PumpDispatcher();
            Assert.Empty(fixture.Sent);
            Assert.Equal(20, main.CurrentTargetSession!.RuntimeId![0]);
        });
    }

    [Theory]
    [InlineData("foreground")]
    [InlineData("settings")]
    [InlineData("pause")]
    [InlineData("expand")]
    [InlineData("dispose")]
    public void UnavailableLauncherCannotDispatchCustomInput(string change)
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture(keys: [new("Enter", "key", "Enter")]);
            using var main = fixture.CreateWindow();
            main.ApplyEvaluatedFocusForTest(Evaluation(1, 10));
            NonFocusableKeyButton old = Assert.Single(LauncherButtons(main.Launcher!));
            old.BeginGestureForTest();
            switch (change)
            {
                case "foreground": fixture.Capture.Result = TargetCaptureResult.Success(new(DateTimeOffset.UtcNow, 43, (nint)200, (nint)201)); break;
                case "settings": main.BeginSettingsSession(); break;
                case "pause": main.SetApplicationEnabled(false); break;
                case "expand": main.ExpandLauncher(); break;
                case "dispose": main.Dispose(); break;
            }
            old.EndGestureForTest(true);
            PumpDispatcher();
            Assert.Empty(fixture.Sent);
        });
    }

    [Fact]
    public void PasswordTargetOnlyShowsExpandButtonAndFailureFeedbackClearsOnTargetChange()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture(keys: [new("Phrase", "text", "private-test-value")]);
            fixture.SendResult = new(InputSendStatus.Failed, 2, 0, 0);
            using var main = fixture.CreateWindow();
            main.ApplyEvaluatedFocusForTest(Evaluation(1, 10));
            NonFocusableKeyButton button = Assert.Single(LauncherButtons(main.Launcher!));
            button.BeginGestureForTest();
            button.EndGestureForTest(true);
            WaitUntil(() => main.Launcher!.IsFeedbackVisible);
            Assert.DoesNotContain("private-test-value", main.Launcher!.FeedbackText, StringComparison.Ordinal);
            main.ApplyEvaluatedFocusForTest(Evaluation(2, 20, password: true));
            Assert.Empty(LauncherButtons(main.Launcher!));
            Assert.False(main.Launcher!.IsFeedbackVisible);
            Assert.True(main.Launcher.IsVisible);
            Assert.Single(fixture.Sent);
        });
    }

    [Fact]
    public void QueuedCustomActionRevalidatesForegroundAtDispatch()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture(keys: [new("Enter", "key", "Enter")]);
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            bool releasedInTime = false;
            fixture.BeforeSend = () => { entered.Set(); releasedInTime = release.Wait(TimeSpan.FromSeconds(3)); };
            using var main = fixture.CreateWindow();
            main.ApplyEvaluatedFocusForTest(Evaluation(1, 10));
            NonFocusableKeyButton button = Assert.Single(LauncherButtons(main.Launcher!));
            button.BeginGestureForTest();
            button.EndGestureForTest(true);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            try
            {
                button.BeginGestureForTest();
                button.EndGestureForTest(true);
                fixture.Capture.Result = TargetCaptureResult.Success(new(DateTimeOffset.UtcNow, 43, (nint)200, (nint)201));
            }
            finally { release.Set(); }
            WaitUntil(() => main.Launcher!.IsFeedbackVisible);
            Assert.True(releasedInTime);
            Assert.Single(fixture.Sent);
            Assert.Contains("target changed", main.Launcher!.FeedbackText, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(96, 1920, 1080)]
    [InlineData(144, 480, 300)]
    [InlineData(192, 300, 30)]
    public void FullLauncherRowFitsNegativeWorkingAreaWithUniformButtonScale(int dpi, int width, int height)
    {
        var area = new PhysicalPixelRect(-1920, -100, width, height);
        PhysicalPixelSize size = KeyboardLauncherWindow.GetPhysicalSize(12, VirtualKeyboard.Core.Geometry.DpiScale.FromDpi((uint)dpi, (uint)dpi), area);
        Assert.Equal((40d + 12 * 44) / 40, size.Width / size.Height, precision: 9);
        PlacementResult placed = PlacementService.Place(new(-1910, -90, 1, 20), area, size, 8);
        Assert.True(placed.IsPlaced);
        PhysicalPixelRect rect = placed.Rectangle!.Value;
        Assert.InRange(rect.X, area.X, area.Right - rect.Width);
        Assert.InRange(rect.Y, area.Y, area.Bottom - rect.Height);
    }

    [Fact]
    public void EditableFocusShowsOnlySmallNonActivatingButtonAndClickExpandsCurrentTarget()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            using var main = fixture.CreateWindow();
            Assert.True(main.ApplyEvaluatedFocusForTest(Evaluation(1, 10)));
            KeyboardLauncherWindow launcher = Assert.IsType<KeyboardLauncherWindow>(main.Launcher);
            Assert.True(launcher.IsVisible);
            Assert.False(main.IsVisible);
            Assert.False(launcher.ShowActivated);
            Assert.False(launcher.ShowInTaskbar);
            Assert.True(launcher.Topmost);
            Assert.Equal(ResizeMode.NoResize, launcher.ResizeMode);
            var button = Assert.IsType<Button>(launcher.FindName("ExpandButton"));
            Assert.False(button.Focusable);
            Assert.False(button.IsTabStop);
            Assert.Equal((nint)3, SendMessage(launcher.Overlay.Handle, 0x0021, 0, 0));
            Assert.True(GetWindowRect(launcher.Overlay.Handle, out NativeRect rectangle));
            Assert.InRange(rectangle.Right - rectangle.Left, 40, 80);
            Assert.InRange(rectangle.Bottom - rectangle.Top, 40, 80);
            Assert.InRange(rectangle.Top, 320, 352);
            Assert.Equal(800, fixture.Repository.Current.KeyboardWidthDip);
            Assert.Equal(300, fixture.Repository.Current.KeyboardHeightDip);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(launcher.IsVisible);
            Assert.True(main.IsVisible);
            Assert.Equal(TargetCoordinatorState.VisibleTracking, main.CoordinatorState);
            Assert.Equal(10, main.CurrentTargetSession!.RuntimeId![0]);
            Assert.False(main.ExpandLauncher());

            Assert.True(main.ApplyEvaluatedFocusForTest(Evaluation(2, 10)));
            Assert.True(main.IsVisible);
            Assert.False(launcher.IsVisible);
            Assert.True(main.ApplyEvaluatedFocusForTest(Evaluation(3, 20)));
            Assert.False(main.IsVisible);
            Assert.True(launcher.IsVisible);
            Assert.False(main.ApplyEvaluatedFocusForTest(Evaluation(2, 10)));
            Assert.Equal(20, main.CurrentTargetSession!.RuntimeId![0]);
            main.Close();
        });
    }

    [Fact]
    public void ClickRejectsForegroundChangeBeforeNextFocusEvaluation()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            using var main = fixture.CreateWindow();
            main.ApplyEvaluatedFocusForTest(Evaluation(1, 10));
            fixture.Capture.Result = TargetCaptureResult.Success(new(DateTimeOffset.UtcNow, 43, (nint)200, (nint)201));
            Assert.False(main.ExpandLauncher());
            Assert.False(main.IsVisible);
            Assert.False(main.Launcher!.IsVisible);
            Assert.Null(main.CurrentTargetSession);
            Assert.Equal(0, main.InputQueueSessionId);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidTargetsAlwaysHideLauncherEvenWithAutoHideDisabled(bool autoHide)
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture(autoHide: autoHide);
            using var main = fixture.CreateWindow();
            main.ApplyEvaluatedFocusForTest(Evaluation(1, 10));
            Assert.True(main.Launcher!.IsVisible);
            main.ApplyEvaluatedFocusForTest(Evaluation(2, 20, editable: false));
            Assert.False(main.Launcher.IsVisible);
            Assert.Null(main.CurrentTargetSession);
            Assert.False(main.ExpandLauncher());
        });
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ConfigurationControlsAutomaticPresentation(bool mode, bool autoShow)
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture(mode: mode, autoShow: autoShow);
            using var main = fixture.CreateWindow();
            main.ApplyEvaluatedFocusForTest(Evaluation(1, 10));
            Assert.Equal(autoShow, main.IsVisible);
            Assert.False(main.Launcher?.IsVisible ?? false);
            main.ShowCurrentKeyboard();
            Assert.True(main.IsVisible);
            Assert.False(main.Launcher?.IsVisible ?? false);
            main.Close();
        });
    }

    [Fact]
    public void SettingsDisableAndShutdownHideLauncherAndConfigurationCopiesPreserveMode()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture(keys: [new("Enter", "key", "Enter")]);
            using var main = fixture.CreateWindow();
            main.ApplyEvaluatedFocusForTest(Evaluation(1, 10));
            Assert.True(main.BeginSettingsSession());
            Assert.False(main.Launcher!.IsVisible);
            Assert.False(main.ExpandLauncher());
            Assert.True(main.EndSettingsSession());
            main.ApplyEvaluatedFocusForTest(Evaluation(2, 10));
            Assert.True(main.Launcher.IsVisible);
            main.SetApplicationEnabled(false);
            Assert.False(main.Launcher.IsVisible);
            main.SetApplicationEnabled(true);
            main.ApplyCompletedResize(900, 350);
            Assert.True(fixture.Repository.Load().Configuration.ShowLauncherButton);
            Assert.Equal("Enter", Assert.Single(fixture.Repository.Current.LauncherCustomKeys).Input);
            main.ApplyEvaluatedFocusForTest(Evaluation(3, 10));
            main.Dispose();
            Assert.False(main.Launcher.IsVisible);
            Assert.False(main.ExpandLauncher());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LauncherAndKeyboardExpansionPreserveNativeForegroundAndEditorFocus(bool customButton)
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture(keys: customButton ? [new("Text", "text", "launcher")] : []);
            var editor = new TextBox();
            var target = new Window { Content = editor, Width = 320, Height = 140 };
            target.Show();
            try
            {
                // Test setup only: a background test runner needs the foreground input queue
                // to activate its own editor. Product overlays never attach or activate targets.
                uint currentThread = GetCurrentThreadId();
                uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                bool needsAttach = foregroundThread != 0 && foregroundThread != currentThread;
                if (needsAttach) Assert.True(AttachThreadInput(currentThread, foregroundThread, true));
                try
                {
                    Assert.True(target.Activate());
                    Assert.True(editor.Focus());
                }
                finally { if (needsAttach) Assert.True(AttachThreadInput(currentThread, foregroundThread, false)); }
                PumpDispatcher();
                nint targetHwnd = new WindowInteropHelper(target).Handle;
                Assert.Equal(targetHwnd, GetForegroundWindow());
                nint focus = GetFocus();
                fixture.Capture.Result = TargetCaptureResult.Success(new(DateTimeOffset.UtcNow, Environment.ProcessId, targetHwnd, focus));
                using var main = new MainWindow(fixture.Capture, new TargetSessionStore(), fixture.Repository);
                var snapshot = new FocusSnapshot(1, DateTimeOffset.UtcNow, Environment.ProcessId, targetHwnd,
                    new RuntimeIdentity([10]), FocusControlType.Edit, true, true, false, false);
                main.ApplyEvaluatedFocusForTest(new(FocusTargetEvaluationStatus.Evaluated, snapshot,
                    new(1, Editability.Editable, ClassificationReasonCode.ValuePattern, false), focus, new PhysicalPixelRect(300, 300, 1, 20)));
                PumpDispatcher();
                Assert.Equal(targetHwnd, GetForegroundWindow());
                Assert.Equal(focus, GetFocus());
                if (customButton)
                {
                    ClickNativeWindow(main.Launcher!.Overlay.Handle, 64d / 84);
                    WaitUntil(() => editor.Text == "launcher");
                    Assert.False(main.IsVisible);
                    Assert.True(main.Launcher.IsVisible);
                    Assert.Equal(targetHwnd, GetForegroundWindow());
                    Assert.Equal(focus, GetFocus());
                    Assert.True(editor.IsKeyboardFocused);
                }
                ClickNativeWindow(main.Launcher!.Overlay.Handle, customButton ? 20d / 84 : 0.5);
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                while (!main.IsVisible && deadline.Elapsed < TimeSpan.FromSeconds(2)) PumpDispatcher();
                PumpDispatcher();
                Assert.True(main.IsVisible);
                Assert.Equal(targetHwnd, GetForegroundWindow());
                Assert.Equal(focus, GetFocus());
                Assert.True(editor.IsKeyboardFocused);
                main.Close();
            }
            finally { target.Close(); }
        });
    }

    [Theory]
    [InlineData(96, 0)]
    [InlineData(120, 0)]
    [InlineData(144, 0)]
    [InlineData(168, 0)]
    [InlineData(192, 0)]
    [InlineData(96, 3)]
    [InlineData(120, 3)]
    [InlineData(144, 3)]
    [InlineData(168, 3)]
    [InlineData(192, 3)]
    public void LauncherDpiChangesKeepButtonSizeAndWorkingAreaBounds(int dpi, int customKeys)
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture(keys: Enumerable.Range(0, customKeys).Select(index => new CustomKeyConfiguration($"Key {index}", "key", "Enter")));
            using var main = fixture.CreateWindow();
            main.ApplyEvaluatedFocusForTest(Evaluation(1, 10));
            KeyboardLauncherWindow launcher = main.Launcher!;
            var suggested = new NativeRect { Left = 300, Top = 320, Right = 340, Bottom = 360 };
            nint memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
            try
            {
                Marshal.StructureToPtr(suggested, memory, false);
                SendMessage(launcher.Overlay.Handle, 0x02E0, (nint)(dpi | (dpi << 16)), memory);
                Assert.True(GetWindowRect(launcher.Overlay.Handle, out NativeRect actual));
                Assert.Equal((40 + 44 * customKeys) * dpi / 96, actual.Right - actual.Left);
                Assert.Equal(40 * dpi / 96, actual.Bottom - actual.Top);
                Assert.Equal(800, fixture.Repository.Current.KeyboardWidthDip);
                Assert.Equal(300, fixture.Repository.Current.KeyboardHeightDip);
            }
            finally { Marshal.FreeHGlobal(memory); }
        });
    }

    private static FocusTargetEvaluation Evaluation(long version, int runtimeId, bool editable = true, bool password = false)
    {
        var snapshot = new FocusSnapshot(version, DateTimeOffset.UtcNow, 42, (nint)100, new RuntimeIdentity([runtimeId]),
            editable ? FocusControlType.Edit : FocusControlType.Button, true, true, false, password);
        return new(FocusTargetEvaluationStatus.Evaluated, snapshot,
            new(version, editable ? Editability.Editable : Editability.NotEditable, ClassificationReasonCode.ValuePattern, password),
            (nint)101, new PhysicalPixelRect(300, 300, 1, 20));
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static NonFocusableKeyButton[] LauncherButtons(KeyboardLauncherWindow launcher) =>
        Assert.IsType<StackPanel>(launcher.FindName("ButtonRow")).Children.OfType<NonFocusableKeyButton>().ToArray();

    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && deadline.Elapsed < TimeSpan.FromSeconds(3)) PumpDispatcher();
        Assert.True(condition());
        PumpDispatcher();
    }

    private static void ClickNativeWindow(nint hwnd, double horizontalFraction = 0.5)
    {
        Assert.True(GetWindowRect(hwnd, out NativeRect rect));
        Assert.True(GetCursorPos(out NativePoint saved));
        var point = new NativePoint { X = rect.Left + (int)((rect.Right - rect.Left) * horizontalFraction), Y = (rect.Top + rect.Bottom) / 2 };
        Assert.Equal(hwnd, WindowFromPoint(point));
        try
        {
            Assert.True(SetCursorPos(point.X, point.Y));
            NativeInput[] input = [new() { Mouse = new() { Flags = 0x0002 } }, new() { Mouse = new() { Flags = 0x0004 } }];
            Assert.Equal(2u, SendInput(2, input, Marshal.SizeOf<NativeInput>()));
            PumpDispatcher();
        }
        finally
        {
            NativeInput[] release = [new() { Mouse = new() { Flags = 0x0004 } }];
            Assert.Equal(1u, SendInput(1, release, Marshal.SizeOf<NativeInput>()));
            Assert.True(SetCursorPos(saved.X, saved.Y));
        }
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try { action(); } catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF launcher test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"VirtualKeyboard.LauncherTests.{Guid.NewGuid():N}");
        public Fixture(bool mode = true, bool autoShow = true, bool autoHide = true, IEnumerable<CustomKeyConfiguration>? keys = null)
        {
            Repository = new(new(Path.Combine(_root, "config.json"), Path.Combine(_root, "recovery")));
            Assert.True(Repository.Save(new(1, true, autoShow, autoHide, 0.9, 800, 300, 8,
                "builtin.qwerty.en-US", ManualPositionMode.UntilTargetChanges, false, showLauncherButton: mode, launcherCustomKeys: keys)).IsSaved);
        }
        public ConfigurationRepository Repository { get; }
        public StubCapture Capture { get; } = new();
        public ConcurrentQueue<string> Sent { get; } = new();
        public InputSendResult SendResult { get; set; } = new(InputSendStatus.Succeeded, 2, 2, 0);
        public Action? BeforeSend { get; set; }
        public MainWindow CreateWindow() => new(Capture, new TargetSessionStore(), Repository,
            (validator, controller) => new LayoutActionDispatcher(validator, controller,
                (key, _, _, _) => Record($"key:{key}"),
                (modifiers, key, _, _, _) => Record($"hotkey:{string.Join('+', modifiers)}+{key}"),
                (value, _) => Record($"text:{value}"),
                sendChord: (keys, _, _, _) => Record($"chord:{string.Join('+', keys)}")));
        private InputSendResult Record(string action) { BeforeSend?.Invoke(); Sent.Enqueue(action); return SendResult; }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }

    private sealed class StubCapture : IForegroundTargetCapture
    {
        public TargetCaptureResult Result { get; set; } = TargetCaptureResult.Success(new(DateTimeOffset.UtcNow, 42, (nint)100, (nint)101));
        public TargetCaptureResult Capture() => Result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput { public uint Type; public NativeMouseInput Mouse; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool attached);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out NativeRect rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, NativeInput[] input, int size);
}
