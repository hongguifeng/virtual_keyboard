using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using VirtualKeyboard.App;
using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.IntegrationTests;

[CollectionDefinition("Launcher desktop", DisableParallelization = true)]
public sealed class LauncherDesktopDefinition;

[Collection("Launcher desktop")]
public sealed class LauncherWindowTests
{
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
            using var fixture = new Fixture();
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
            main.ApplyEvaluatedFocusForTest(Evaluation(3, 10));
            main.Dispose();
            Assert.False(main.Launcher.IsVisible);
            Assert.False(main.ExpandLauncher());
        });
    }

    [Fact]
    public void LauncherAndKeyboardExpansionPreserveNativeForegroundAndEditorFocus()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            var editor = new TextBox();
            var target = new Window { Content = editor, Width = 320, Height = 140 };
            target.Show();
            try
            {
                // Test setup only: a background test runner needs the foreground input queue
                // to activate its own editor. Product overlays never attach or activate targets.
                uint currentThread = GetCurrentThreadId();
                uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                Assert.True(AttachThreadInput(currentThread, foregroundThread, true));
                try
                {
                    Assert.True(target.Activate());
                    Assert.True(editor.Focus());
                }
                finally { Assert.True(AttachThreadInput(currentThread, foregroundThread, false)); }
                PumpDispatcher();
                nint targetHwnd = new WindowInteropHelper(target).Handle;
                Assert.Equal(targetHwnd, GetForegroundWindow());
                nint focus = GetFocus();
                fixture.Capture.Result = TargetCaptureResult.Success(new(DateTimeOffset.UtcNow, Environment.ProcessId, targetHwnd, focus));
                using var main = fixture.CreateWindow();
                var snapshot = new FocusSnapshot(1, DateTimeOffset.UtcNow, Environment.ProcessId, targetHwnd,
                    new RuntimeIdentity([10]), FocusControlType.Edit, true, true, false, false);
                main.ApplyEvaluatedFocusForTest(new(FocusTargetEvaluationStatus.Evaluated, snapshot,
                    new(1, Editability.Editable, ClassificationReasonCode.ValuePattern, false), focus, new PhysicalPixelRect(300, 300, 1, 20)));
                PumpDispatcher();
                Assert.Equal(targetHwnd, GetForegroundWindow());
                Assert.Equal(focus, GetFocus());
                ClickNativeWindow(main.Launcher!.Overlay.Handle);
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
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(168)]
    [InlineData(192)]
    public void LauncherDpiChangesKeepButtonSizeAndWorkingAreaBounds(int dpi)
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
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
                Assert.Equal(40 * dpi / 96, actual.Right - actual.Left);
                Assert.Equal(40 * dpi / 96, actual.Bottom - actual.Top);
                Assert.Equal(800, fixture.Repository.Current.KeyboardWidthDip);
                Assert.Equal(300, fixture.Repository.Current.KeyboardHeightDip);
            }
            finally { Marshal.FreeHGlobal(memory); }
        });
    }

    private static FocusTargetEvaluation Evaluation(long version, int runtimeId, bool editable = true)
    {
        var snapshot = new FocusSnapshot(version, DateTimeOffset.UtcNow, 42, (nint)100, new RuntimeIdentity([runtimeId]),
            editable ? FocusControlType.Edit : FocusControlType.Button, true, true, false, false);
        return new(FocusTargetEvaluationStatus.Evaluated, snapshot,
            new(version, editable ? Editability.Editable : Editability.NotEditable, ClassificationReasonCode.ValuePattern, false),
            (nint)101, new PhysicalPixelRect(300, 300, 1, 20));
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void ClickNativeWindow(nint hwnd)
    {
        Assert.True(GetWindowRect(hwnd, out NativeRect rect));
        Assert.True(GetCursorPos(out NativePoint saved));
        var point = new NativePoint { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 };
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
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF launcher test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"VirtualKeyboard.LauncherTests.{Guid.NewGuid():N}");
        public Fixture(bool mode = true, bool autoShow = true, bool autoHide = true)
        {
            Repository = new(new(Path.Combine(_root, "config.json"), Path.Combine(_root, "recovery")));
            Assert.True(Repository.Save(new(1, true, autoShow, autoHide, 0.9, 800, 300, 8,
                "builtin.qwerty.en-US", ManualPositionMode.UntilTargetChanges, false, showLauncherButton: mode)).IsSaved);
        }
        public ConfigurationRepository Repository { get; }
        public StubCapture Capture { get; } = new();
        public MainWindow CreateWindow() => new(Capture, new TargetSessionStore(), Repository);
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
