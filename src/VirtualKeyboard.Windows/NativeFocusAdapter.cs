using System.Runtime.InteropServices;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows;

public sealed class NativeFocusAdapter
{
    private readonly INativeFocusApi _api;

    public NativeFocusAdapter() : this(new SystemNativeFocusApi()) { }

    internal NativeFocusAdapter(INativeFocusApi api) =>
        _api = api ?? throw new ArgumentNullException(nameof(api));

    public NativeFocusResult Capture()
    {
        try
        {
            nint topLevel = _api.GetForegroundWindow();
            if (topLevel == nint.Zero) return NativeFocusResult.Failure(NativeFocusStatus.NoForegroundWindow);
            uint threadId = _api.GetWindowThreadProcessId(topLevel, out uint processId);
            if (threadId == 0 || processId == 0 || processId > int.MaxValue)
                return NativeFocusResult.Failure(NativeFocusStatus.ProcessLookupFailed);
            if (!_api.TryGetGuiThreadInfo(threadId, out NativeGuiThreadSnapshot gui))
                return NativeFocusResult.Failure(NativeFocusStatus.GuiThreadInfoUnavailable);
            if (gui.FocusHwnd == nint.Zero)
                return NativeFocusResult.Failure(NativeFocusStatus.FocusWindowUnavailable);

            PhysicalPixelRect? caret = null;
            if (gui.CaretHwnd != nint.Zero && (gui.CaretRight != gui.CaretLeft || gui.CaretBottom != gui.CaretTop))
            {
                var topLeft = new NativePoint(gui.CaretLeft, gui.CaretTop);
                var bottomRight = new NativePoint(gui.CaretRight, gui.CaretBottom);
                if (!_api.TryClientToScreen(gui.CaretHwnd, ref topLeft) || !_api.TryClientToScreen(gui.CaretHwnd, ref bottomRight))
                    return NativeFocusResult.Failure(NativeFocusStatus.CoordinateConversionFailed);
                caret = new PhysicalPixelRect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
                if (!caret.Value.IsValid) caret = null;
            }

            return NativeFocusResult.Success(new(
                checked((int)processId), threadId, topLevel, gui.FocusHwnd,
                _api.GetKeyboardLayout(threadId), caret));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return NativeFocusResult.Failure(NativeFocusStatus.NativeUnavailable);
        }
    }
}

internal readonly record struct NativeGuiThreadSnapshot(
    nint FocusHwnd, nint CaretHwnd, int CaretLeft, int CaretTop, int CaretRight, int CaretBottom);

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint(int x, int y)
{
    public int X = x;
    public int Y = y;
}

internal interface INativeFocusApi
{
    nint GetForegroundWindow();
    uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    bool TryGetGuiThreadInfo(uint threadId, out NativeGuiThreadSnapshot snapshot);
    bool TryClientToScreen(nint hwnd, ref NativePoint point);
    nint GetKeyboardLayout(uint threadId);
}

internal sealed class SystemNativeFocusApi : INativeFocusApi
{
    public nint GetForegroundWindow() => GetForegroundWindowNative();
    public uint GetWindowThreadProcessId(nint hwnd, out uint processId) => GetWindowThreadProcessIdNative(hwnd, out processId);
    public nint GetKeyboardLayout(uint threadId) => GetKeyboardLayoutNative(threadId);
    public bool TryClientToScreen(nint hwnd, ref NativePoint point) => ClientToScreen(hwnd, ref point);

    public bool TryGetGuiThreadInfo(uint threadId, out NativeGuiThreadSnapshot snapshot)
    {
        var info = new GuiThreadInfo { Size = checked((uint)Marshal.SizeOf<GuiThreadInfo>()) };
        bool ok = GetGUIThreadInfo(threadId, ref info);
        snapshot = ok ? new(info.FocusHwnd, info.CaretHwnd, info.Caret.Left, info.Caret.Top, info.Caret.Right, info.Caret.Bottom) : default;
        return ok;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint ActiveHwnd, FocusHwnd, CaptureHwnd, MenuOwnerHwnd, MoveSizeHwnd, CaretHwnd;
        public NativeRect Caret;
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint GetForegroundWindowNative();
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
    private static extern uint GetWindowThreadProcessIdNative(nint hwnd, out uint processId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint hwnd, ref NativePoint point);
    [DllImport("user32.dll", EntryPoint = "GetKeyboardLayout")]
    private static extern nint GetKeyboardLayoutNative(uint threadId);
}
