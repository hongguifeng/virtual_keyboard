using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VirtualKeyboard.Windows;
using Xunit.Abstractions;

namespace VirtualKeyboard.Windows.Tests;

public sealed class OverlayFocusBehaviorTests(ITestOutputHelper output)
{
    private const int MouseActivateMessage = 0x0021;

    [Fact]
    public void ButtonClickPreservesForegroundAndFocusHandles()
    {
        RunOnStaThread(() =>
        {
            using var target = new TargetWindow();
            target.Show();
            Assert.True(target.Activate());
            Assert.True(target.Editor.Focus());
            PumpDispatcher();

            FocusSnapshot before = CaptureSnapshot(nint.Zero);
            Assert.NotEqual(nint.Zero, before.ForegroundWindow);
            Assert.NotEqual(nint.Zero, before.FocusWindow);

            var button = new Button { Content = "Probe", Focusable = false, IsTabStop = false };
            var clickCount = 0;
            button.Click += (_, _) => clickCount++;
            using var overlayWindow = new OverlayTestWindow(button);
            using var overlay = new OverlayWindowAdapter(overlayWindow);
            overlay.ShowAt(-12000, -11000, 240, 120);

            FocusSnapshot shown = CaptureSnapshot(overlay.Handle);
            SendMessage(overlay.Handle, MouseActivateMessage, nint.Zero, nint.Zero);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();
            FocusSnapshot clicked = CaptureSnapshot(overlay.Handle);

            output.WriteLine($"before: foreground=0x{before.ForegroundWindow:X}, focus=0x{before.FocusWindow:X}, keyboard=0x{before.KeyboardWindow:X}");
            output.WriteLine($"shown:  foreground=0x{shown.ForegroundWindow:X}, focus=0x{shown.FocusWindow:X}, keyboard=0x{shown.KeyboardWindow:X}");
            output.WriteLine($"clicked: foreground=0x{clicked.ForegroundWindow:X}, focus=0x{clicked.FocusWindow:X}, keyboard=0x{clicked.KeyboardWindow:X}");

            Assert.Equal(1, clickCount);
            Assert.Equal(before.ForegroundWindow, shown.ForegroundWindow);
            Assert.Equal(before.FocusWindow, shown.FocusWindow);
            Assert.Equal(before.ForegroundWindow, clicked.ForegroundWindow);
            Assert.Equal(before.FocusWindow, clicked.FocusWindow);
            Assert.NotEqual(overlay.Handle, shown.ForegroundWindow);
            Assert.NotEqual(overlay.Handle, clicked.ForegroundWindow);
        });
    }

    private static FocusSnapshot CaptureSnapshot(nint keyboardWindow)
    {
        nint foreground = GetForegroundWindow();
        uint threadId = GetWindowThreadProcessId(foreground, out _);
        var info = new GuiThreadInfo { Size = checked((uint)Marshal.SizeOf<GuiThreadInfo>()) };
        Assert.True(GetGUIThreadInfo(threadId, ref info));
        return new FocusSnapshot(foreground, info.FocusWindow, keyboardWindow);
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
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

    private sealed class TargetWindow : Window, IDisposable
    {
        public TargetWindow()
        {
            Editor = new TextBox();
            Content = Editor;
            Width = 240;
            Height = 120;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = 40;
            Top = 40;
        }

        public TextBox Editor { get; }

        public void Dispose()
        {
            Close();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class OverlayTestWindow(Button button) : Window, IDisposable
    {
        public Button ProbeButton { get; } = button;

        protected override void OnInitialized(EventArgs e)
        {
            Content = ProbeButton;
            base.OnInitialized(e);
        }

        public void Dispose()
        {
            Close();
            GC.SuppressFinalize(this);
        }
    }

    private readonly record struct FocusSnapshot(nint ForegroundWindow, nint FocusWindow, nint KeyboardWindow);

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public nint ActiveWindow;
        public nint FocusWindow;
        public nint CaptureWindow;
        public nint MenuOwnerWindow;
        public nint MoveSizeWindow;
        public nint CaretWindow;
        public NativeRectangle CaretRectangle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint windowHandle, int message, nint wParam, nint lParam);
}
