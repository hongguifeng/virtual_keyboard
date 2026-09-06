using System.Runtime.InteropServices;

namespace VirtualKeyboard.Windows;

internal sealed class NativeInputApi : IInputNativeApi
{
    public int LastError { get; private set; }

    public uint SendInput(NativeInput[] inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        Marshal.SetLastPInvokeError(0);
        uint result = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        LastError = Marshal.GetLastPInvokeError();
        return result;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint inputCount, [In] NativeInput[] inputs, int inputSize);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeInput
{
    public uint Type;
    public NativeInputUnion Data;
}

[StructLayout(LayoutKind.Explicit)]
internal struct NativeInputUnion
{
    [FieldOffset(0)]
    public NativeMouseInput Mouse;

    [FieldOffset(0)]
    public NativeKeyboardInput Keyboard;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMouseInput
{
    public int DeltaX;
    public int DeltaY;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeKeyboardInput
{
    public ushort VirtualKey;
    public ushort ScanCode;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}
