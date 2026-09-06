using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Positioning;
using CoreDpiScale = VirtualKeyboard.Core.Geometry.DpiScale;

namespace VirtualKeyboard.Windows;

/// <summary>Applies the native non-activating overlay contract to a WPF window.</summary>
public sealed class OverlayWindowAdapter : IDisposable
{
    private const int ExtendedStyleIndex = -20;
    private const long NoActivateExtendedStyle = 0x08000000L;
    private const long ToolWindowExtendedStyle = 0x00000080L;
    private const int MouseActivateMessage = 0x0021;
    private const int DpiChangedMessage = 0x02E0;
    private const int ExitSizeMoveMessage = 0x0232;
    private const int NoActivateMouseResult = 3;
    private const uint NoActivatePositionFlag = 0x0010;
    private const uint ShowWindowPositionFlag = 0x0040;
    private static readonly IntPtr TopmostWindow = new(-1);

    private readonly Window _window;
    private readonly ManualPositionTracker _manualPosition = new();
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

    /// <summary>Raised after the system suggested rectangle is applied without activation.</summary>
    public event Action<OverlayDpiChangedNotification>? DpiChanged;

    /// <summary>Raised once after an interactive edge resize finishes.</summary>
    public event Action<double, double>? ResizeCompleted;

    public bool BeginManualMove(long sessionId)
    {
        VerifyAccessAndState();
        EnsureAttached();
        if (!GetCursorPos(out NativePoint cursorPosition) || !GetWindowRect(_handle, out NativeSuggestedRect windowRect)) return false;
        _manualPosition.Begin(
            sessionId,
            new PhysicalPixelRect(windowRect.Left, windowRect.Top, windowRect.Right - windowRect.Left, windowRect.Bottom - windowRect.Top),
            new PhysicalPixelPoint(cursorPosition.X, cursorPosition.Y));
        return true;
    }

    public bool UpdateManualMove(long sessionId)
    {
        VerifyAccessAndState();
        if (!GetCursorPos(out NativePoint cursorPosition) ||
            !_manualPosition.TryUpdate(sessionId, new PhysicalPixelPoint(cursorPosition.X, cursorPosition.Y), out PhysicalPixelRect rectangle)) return false;
        SetPosition(
            checked((int)Math.Round(rectangle.X)), checked((int)Math.Round(rectangle.Y)),
            checked((int)Math.Round(rectangle.Width)), checked((int)Math.Round(rectangle.Height)),
            NoActivatePositionFlag);
        return true;
    }

    public bool EndManualMove(long sessionId) => _manualPosition.End(sessionId, out _);

    public bool TryGetManualPosition(long sessionId, out PhysicalPixelRect rectangle) =>
        _manualPosition.TryGet(sessionId, out rectangle);

    public void InvalidateManualPosition() => _manualPosition.Invalidate();

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

    public void Hide()
    {
        VerifyAccessAndState();
        _window.Hide();
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
        _manualPosition.Invalidate();
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

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        _ = hwnd;
        if (message == MouseActivateMessage)
        {
            handled = true;
            return new IntPtr(NoActivateMouseResult);
        }

        if (message == ExitSizeMoveMessage)
        {
            try { ResizeCompleted?.Invoke(_window.ActualWidth, _window.ActualHeight); }
            catch { /* Persistence failures must not escape the native window procedure. */ }
        }


        if (message == DpiChangedMessage && lParam != IntPtr.Zero)
        {
            NativeSuggestedRect suggested = Marshal.PtrToStructure<NativeSuggestedRect>(lParam);
            int width = suggested.Right - suggested.Left;
            int height = suggested.Bottom - suggested.Top;
            if (width > 0 && height > 0)
            {
                SetPosition(suggested.Left, suggested.Top, width, height, NoActivatePositionFlag);
                uint packedDpi = unchecked((uint)wParam.ToInt64());
                uint dpiX = packedDpi & 0xffff;
                uint dpiY = (packedDpi >> 16) & 0xffff;
                if (dpiX > 0 && dpiY > 0)
                {
                    var notification = new OverlayDpiChangedNotification(
                        CoreDpiScale.FromDpi(dpiX, dpiY),
                        new PhysicalPixelRect(suggested.Left, suggested.Top, width, height));
                    try { DpiChanged?.Invoke(notification); }
                    catch { /* A relayout consumer failure must not escape the native window procedure. */ }
                }

                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeSuggestedRect(int Left, int Top, int Right, int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeSuggestedRect rectangle);
}

public readonly record struct OverlayDpiChangedNotification(CoreDpiScale DpiScale, PhysicalPixelRect SuggestedRectangle);
