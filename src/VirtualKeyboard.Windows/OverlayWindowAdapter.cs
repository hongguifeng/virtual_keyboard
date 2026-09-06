using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VirtualKeyboard.Windows;

/// <summary>Applies the native non-activating overlay contract to a WPF window.</summary>
public sealed class OverlayWindowAdapter : IDisposable
{
    private const int ExtendedStyleIndex = -20;
    private const long NoActivateExtendedStyle = 0x08000000L;
    private const long ToolWindowExtendedStyle = 0x00000080L;
    private const int MouseActivateMessage = 0x0021;
    private const int NoActivateMouseResult = 3;
    private const uint NoActivatePositionFlag = 0x0010;
    private const uint ShowWindowPositionFlag = 0x0040;
    private static readonly IntPtr TopmostWindow = new(-1);

    private readonly Window _window;
    private HwndSource? _source;
    private IntPtr _handle;
    private bool _disposed;

    public OverlayWindowAdapter(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _window.Dispatcher.VerifyAccess();
        _window.ShowActivated = false;
        _window.ShowInTaskbar = false;
        _window.Topmost = true;
        _window.SourceInitialized += OnSourceInitialized;
        _window.Closed += OnWindowClosed;

        if (PresentationSource.FromVisual(_window) is HwndSource source)
        {
            Attach(source);
        }
    }

    /// <summary>Gets the overlay HWND after the WPF source has been initialized.</summary>
    public IntPtr Handle => _handle;

    /// <summary>Shows the overlay at a physical-pixel rectangle without activating it.</summary>
    public void ShowAt(int x, int y, int width, int height)
    {
        VerifyAccessAndState();
        ValidateSize(width, height);
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        EnsureAttached();
        SetPosition(x, y, width, height, NoActivatePositionFlag | ShowWindowPositionFlag);
    }

    /// <summary>Moves and resizes an initialized overlay in physical pixels without activation.</summary>
    public void Move(int x, int y, int width, int height)
    {
        VerifyAccessAndState();
        ValidateSize(width, height);
        EnsureAttached();
        SetPosition(x, y, width, height, NoActivatePositionFlag);
    }

    public void Close()
    {
        VerifyAccessAndState();
        _window.Close();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _window.Dispatcher.VerifyAccess();
        Detach();
        _window.SourceInitialized -= OnSourceInitialized;
        _window.Closed -= OnWindowClosed;
        _disposed = true;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (PresentationSource.FromVisual(_window) is not HwndSource source)
        {
            throw new InvalidOperationException("The overlay HWND source was not created.");
        }

        Attach(source);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        Detach();
    }

    private void Attach(HwndSource source)
    {
        if (_source is not null)
        {
            return;
        }

        _source = source;
        _handle = source.Handle;
        long currentStyle = GetWindowLongPtr(_handle, ExtendedStyleIndex).ToInt64();
        long requiredStyle = currentStyle | NoActivateExtendedStyle | ToolWindowExtendedStyle;
        if (requiredStyle != currentStyle)
        {
            Marshal.SetLastPInvokeError(0);
            IntPtr previous = SetWindowLongPtr(_handle, ExtendedStyleIndex, new IntPtr(requiredStyle));
            int error = Marshal.GetLastPInvokeError();
            if (previous == IntPtr.Zero && error != 0)
            {
                throw new Win32Exception(error, "Failed to apply overlay extended styles.");
            }
        }

        source.AddHook(WindowProcedure);
    }

    private void Detach()
    {
        if (_source is not null)
        {
            _source.RemoveHook(WindowProcedure);
            _source = null;
        }

        _handle = IntPtr.Zero;
    }

    private void EnsureAttached()
    {
        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("The overlay window has not initialized its HWND.");
        }
    }

    private void SetPosition(int x, int y, int width, int height, uint flags)
    {
        if (!SetWindowPos(_handle, TopmostWindow, x, y, width, height, flags))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to position the overlay window.");
        }
    }

    private void VerifyAccessAndState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.Dispatcher.VerifyAccess();
    }

    private static void ValidateSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
    }

    private static IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        _ = hwnd;
        _ = wParam;
        _ = lParam;
        if (message == MouseActivateMessage)
        {
            handled = true;
            return new IntPtr(NoActivateMouseResult);
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
