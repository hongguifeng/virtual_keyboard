using System.Runtime.InteropServices;

namespace VirtualKeyboard.Windows;

internal sealed class NativeForegroundWindowApi : IForegroundWindowApi
{
    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public uint GetWindowThreadProcessId(nint windowHandle, out uint processId) =>
        NativeMethods.GetWindowThreadProcessId(windowHandle, out processId);

    public bool TryGetFocusWindow(uint threadId, out nint focusWindowHandle)
    {
        var info = new GuiThreadInfo
        {
            Size = checked((uint)Marshal.SizeOf<GuiThreadInfo>()),
        };

        bool succeeded = NativeMethods.GetGUIThreadInfo(threadId, ref info);
        focusWindowHandle = succeeded ? info.FocusWindow : nint.Zero;
        return succeeded;
    }

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

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern nint GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    }
}
