using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class OverlayWindowAdapterTests
{
    private const int ExtendedStyleIndex = -20;
    private const long NoActivateExtendedStyle = 0x08000000L;
    private const long ToolWindowExtendedStyle = 0x00000080L;
    private const int MouseActivateMessage = 0x0021;
    private const int NoActivateMouseResult = 3;

    [Fact]
    public void ShowAtAppliesNativeContractWithoutBecomingForeground()
    {
        RunOnStaThread(() =>
        {
            using var window = new TestWindow();
            using var adapter = new OverlayWindowAdapter(window);

            adapter.ShowAt(-12000, -11000, 240, 120);

            Assert.NotEqual(IntPtr.Zero, adapter.Handle);
            long style = GetWindowLongPtr(adapter.Handle, ExtendedStyleIndex).ToInt64();
            Assert.Equal(NoActivateExtendedStyle, style & NoActivateExtendedStyle);
            Assert.Equal(ToolWindowExtendedStyle, style & ToolWindowExtendedStyle);
            Assert.NotEqual(adapter.Handle, GetForegroundWindow());
            Assert.Equal(new NativeRectangle(-12000, -11000, -11760, -10880), GetRectangle(adapter.Handle));

            IntPtr mouseResult = SendMessage(adapter.Handle, MouseActivateMessage, IntPtr.Zero, IntPtr.Zero);
            Assert.Equal(new IntPtr(NoActivateMouseResult), mouseResult);

            adapter.Move(-10000, -9000, 260, 140);
            Assert.Equal(new NativeRectangle(-10000, -9000, -9740, -8860), GetRectangle(adapter.Handle));
            Assert.NotEqual(adapter.Handle, GetForegroundWindow());
            adapter.Close();
        });
    }

    [Fact]
    public void ShowAtRejectsNonPositiveSizeBeforeCreatingHandle()
    {
        RunOnStaThread(() =>
        {
            using var window = new TestWindow();
            using var adapter = new OverlayWindowAdapter(window);

            Assert.Throws<ArgumentOutOfRangeException>(() => adapter.ShowAt(0, 0, 0, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => adapter.ShowAt(0, 0, 100, 0));
            Assert.Equal(IntPtr.Zero, adapter.Handle);
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

    private static NativeRectangle GetRectangle(IntPtr handle)
    {
        Assert.True(GetWindowRect(handle, out NativeRectangle rectangle));
        return rectangle;
    }

    private sealed class TestWindow : Window, IDisposable
    {
        public void Dispose()
        {
            if (IsVisible)
            {
                Close();
            }

            GC.SuppressFinalize(this);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRectangle(int Left, int Top, int Right, int Bottom);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);
}
