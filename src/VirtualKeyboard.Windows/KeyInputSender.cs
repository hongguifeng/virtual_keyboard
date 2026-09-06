using System.Runtime.InteropServices;
using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Input;

namespace VirtualKeyboard.Windows;

public enum WindowsKeyboardKey : ushort
{
    Backspace = 0x08,
    Tab = 0x09,
    Enter = 0x0D,
    Shift = 0x10,
    Control = 0x11,
    Alt = 0x12,
    CapsLock = 0x14,
    Escape = 0x1B,
    Space = 0x20,
    PageUp = 0x21,
    PageDown = 0x22,
    End = 0x23,
    Home = 0x24,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    Insert = 0x2D,
    Delete = 0x2E,
    D0 = 0x30,
    D1 = 0x31,
    D2 = 0x32,
    D3 = 0x33,
    D4 = 0x34,
    D5 = 0x35,
    D6 = 0x36,
    D7 = 0x37,
    D8 = 0x38,
    D9 = 0x39,
    A = 0x41,
    B = 0x42,
    C = 0x43,
    D = 0x44,
    E = 0x45,
    F = 0x46,
    G = 0x47,
    H = 0x48,
    I = 0x49,
    J = 0x4A,
    K = 0x4B,
    L = 0x4C,
    M = 0x4D,
    N = 0x4E,
    O = 0x4F,
    P = 0x50,
    Q = 0x51,
    R = 0x52,
    S = 0x53,
    T = 0x54,
    U = 0x55,
    V = 0x56,
    W = 0x57,
    X = 0x58,
    Y = 0x59,
    Z = 0x5A,
    LeftWindows = 0x5B,
    RightWindows = 0x5C,
    F1 = 0x70,
    F2 = 0x71,
    F3 = 0x72,
    F4 = 0x73,
    F5 = 0x74,
    F6 = 0x75,
    F7 = 0x76,
    F8 = 0x77,
    F9 = 0x78,
    F10 = 0x79,
    F11 = 0x7A,
    F12 = 0x7B,
    OemSemicolon = 0xBA,
    OemPlus = 0xBB,
    OemComma = 0xBC,
    OemMinus = 0xBD,
    OemPeriod = 0xBE,
    OemQuestion = 0xBF,
    OemTilde = 0xC0,
    OemOpenBrackets = 0xDB,
    OemPipe = 0xDC,
    OemCloseBrackets = 0xDD,
    OemQuotes = 0xDE,
}

public enum KeyInputTransition
{
    Press,
    KeyDown,
    KeyUp,
}

internal enum KeyInputEncoding
{
    VirtualKey,
    ScanCode,
}

internal readonly record struct ResolvedKeyInput(ushort VirtualKey, ushort ScanCode, bool IsExtended);

internal static class KeyInputBuilder
{
    private const uint KeyboardInputType = 1;
    private const uint ExtendedKeyFlag = 0x0001;
    private const uint KeyUpFlag = 0x0002;
    private const uint ScanCodeFlag = 0x0008;

    internal static NativeInput[] Build(
        ResolvedKeyInput key,
        KeyInputTransition transition = KeyInputTransition.Press,
        KeyInputEncoding encoding = KeyInputEncoding.VirtualKey)
    {
        if (key.VirtualKey == 0 || key.ScanCode == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(key));
        }
        if (!Enum.IsDefined(transition))
        {
            throw new ArgumentOutOfRangeException(nameof(transition));
        }
        if (!Enum.IsDefined(encoding))
        {
            throw new ArgumentOutOfRangeException(nameof(encoding));
        }

        return transition switch
        {
            KeyInputTransition.Press =>
            [
                BuildEvent(key, encoding, isKeyUp: false),
                BuildEvent(key, encoding, isKeyUp: true),
            ],
            KeyInputTransition.KeyDown => [BuildEvent(key, encoding, isKeyUp: false)],
            KeyInputTransition.KeyUp => [BuildEvent(key, encoding, isKeyUp: true)],
            _ => throw new ArgumentOutOfRangeException(nameof(transition)),
        };
    }

    private static NativeInput BuildEvent(ResolvedKeyInput key, KeyInputEncoding encoding, bool isKeyUp)
    {
        uint flags = (key.IsExtended ? ExtendedKeyFlag : 0u) |
            (isKeyUp ? KeyUpFlag : 0u) |
            (encoding == KeyInputEncoding.ScanCode ? ScanCodeFlag : 0u);
        return new NativeInput
        {
            Type = KeyboardInputType,
            Data = new NativeInputUnion
            {
                Keyboard = new NativeKeyboardInput
                {
                    VirtualKey = encoding == KeyInputEncoding.VirtualKey ? key.VirtualKey : (ushort)0,
                    ScanCode = key.ScanCode,
                    Flags = flags,
                },
            },
        };
    }
}

public sealed class KeyInputSender
{
    private const uint MapVirtualKeyToScanCodeExtended = 4;
    private const int InvalidParameterError = 87;
    private const int NativeUnavailableError = 50;

    private readonly IInputNativeApi _inputApi;
    private readonly IKeyMappingNativeApi _mappingApi;
    private readonly DiagnosticLogger? _diagnostics;

    public KeyInputSender(DiagnosticLogger? diagnostics = null)
        : this(new NativeInputApi(), new SystemKeyMappingNativeApi(), diagnostics)
    {
    }

    internal KeyInputSender(
        IInputNativeApi inputApi,
        IKeyMappingNativeApi mappingApi,
        DiagnosticLogger? diagnostics = null)
    {
        _inputApi = inputApi ?? throw new ArgumentNullException(nameof(inputApi));
        _mappingApi = mappingApi ?? throw new ArgumentNullException(nameof(mappingApi));
        _diagnostics = diagnostics;
    }

    public InputSendResult Send(
        WindowsKeyboardKey key,
        nint targetFocusHwnd,
        KeyInputTransition transition = KeyInputTransition.Press,
        int targetProcessId = -1)
    {
        if (!Enum.IsDefined(key) || !Enum.IsDefined(transition) || targetFocusHwnd == nint.Zero)
        {
            return InvalidInput(targetProcessId);
        }

        NativeInput[] inputs;
        try
        {
            uint targetThreadId = _mappingApi.GetWindowThreadProcessId(targetFocusHwnd, out _);
            if (targetThreadId == 0)
            {
                return InvalidInput(targetProcessId);
            }

            nint keyboardLayout = _mappingApi.GetKeyboardLayout(targetThreadId);
            if (keyboardLayout == nint.Zero)
            {
                return InvalidInput(targetProcessId);
            }

            ushort virtualKey = (ushort)key;
            uint mappedScanCode = _mappingApi.MapVirtualKeyEx(
                virtualKey,
                MapVirtualKeyToScanCodeExtended,
                keyboardLayout);
            ushort scanCode = (ushort)(mappedScanCode & 0xFF);
            if (scanCode == 0)
            {
                return InvalidInput(targetProcessId);
            }

            bool isExtended = IsNavigationExtendedKey(key) ||
                (mappedScanCode & 0xFF00) is 0xE000 or 0xE100;
            inputs = KeyInputBuilder.Build(
                new ResolvedKeyInput(virtualKey, scanCode, isExtended),
                transition,
                KeyInputEncoding.ScanCode);
        }
        catch (Exception exception) when (IsNativeUnavailable(exception))
        {
            LogFailure(targetProcessId, 0, 0, NativeUnavailableError);
            return new InputSendResult(InputSendStatus.NativeUnavailable, 0, 0, NativeUnavailableError);
        }
        catch (ArgumentException)
        {
            return InvalidInput(targetProcessId);
        }

        _diagnostics?.Log(
            DiagnosticType.InputBatchStarted,
            DiagnosticModule.Input,
            targetProcessId: targetProcessId,
            requestedCount: inputs.Length);

        try
        {
            uint nativeCount = _inputApi.SendInput(inputs);
            int sentCount = nativeCount > int.MaxValue ? int.MaxValue : (int)nativeCount;
            InputSendStatus status = sentCount == inputs.Length
                ? InputSendStatus.Succeeded
                : sentCount == 0 ? InputSendStatus.Failed : InputSendStatus.PartialFailure;
            int errorCode = status == InputSendStatus.Succeeded ? 0 : _inputApi.LastError;
            if (status == InputSendStatus.PartialFailure && transition == KeyInputTransition.Press && sentCount == 1)
            {
                TryBestEffortRelease(inputs[1]);
            }
            _diagnostics?.Log(
                status == InputSendStatus.Succeeded ? DiagnosticType.InputBatchSucceeded : DiagnosticType.InputBatchFailed,
                DiagnosticModule.Input,
                targetProcessId: targetProcessId,
                errorCode: errorCode,
                requestedCount: inputs.Length,
                completedCount: sentCount);
            return new InputSendResult(status, inputs.Length, sentCount, errorCode);
        }
        catch (Exception exception) when (IsNativeUnavailable(exception))
        {
            LogFailure(targetProcessId, inputs.Length, 0, NativeUnavailableError);
            return new InputSendResult(InputSendStatus.NativeUnavailable, inputs.Length, 0, NativeUnavailableError);
        }
    }

    private void TryBestEffortRelease(NativeInput keyUp)
    {
        try
        {
            _ = _inputApi.SendInput([keyUp]);
        }
        catch
        {
            // Cleanup is best-effort; the original partial-send result remains authoritative.
        }
    }

    private InputSendResult InvalidInput(int targetProcessId)
    {
        LogFailure(targetProcessId, 0, 0, InvalidParameterError);
        return new InputSendResult(InputSendStatus.InvalidInput, 0, 0, InvalidParameterError);
    }

    private void LogFailure(int targetProcessId, int requestedCount, int completedCount, int errorCode) =>
        _diagnostics?.Log(
            DiagnosticType.InputBatchFailed,
            DiagnosticModule.Input,
            targetProcessId: targetProcessId,
            errorCode: errorCode,
            requestedCount: requestedCount,
            completedCount: completedCount);

    private static bool IsNavigationExtendedKey(WindowsKeyboardKey key) => key is
        WindowsKeyboardKey.PageUp or
        WindowsKeyboardKey.PageDown or
        WindowsKeyboardKey.End or
        WindowsKeyboardKey.Home or
        WindowsKeyboardKey.Left or
        WindowsKeyboardKey.Up or
        WindowsKeyboardKey.Right or
        WindowsKeyboardKey.Down or
        WindowsKeyboardKey.Insert or
        WindowsKeyboardKey.Delete;

    private static bool IsNativeUnavailable(Exception exception) => exception is
        DllNotFoundException or
        EntryPointNotFoundException or
        BadImageFormatException;
}

internal interface IKeyMappingNativeApi
{
    uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    nint GetKeyboardLayout(uint threadId);
    uint MapVirtualKeyEx(uint code, uint mapType, nint keyboardLayout);
}

internal sealed class SystemKeyMappingNativeApi : IKeyMappingNativeApi
{
    public uint GetWindowThreadProcessId(nint hwnd, out uint processId) =>
        GetWindowThreadProcessIdNative(hwnd, out processId);

    public nint GetKeyboardLayout(uint threadId) => GetKeyboardLayoutNative(threadId);

    public uint MapVirtualKeyEx(uint code, uint mapType, nint keyboardLayout) =>
        MapVirtualKeyExNative(code, mapType, keyboardLayout);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
    private static extern uint GetWindowThreadProcessIdNative(nint hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetKeyboardLayout")]
    private static extern nint GetKeyboardLayoutNative(uint threadId);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW", CharSet = CharSet.Unicode)]
    private static extern uint MapVirtualKeyExNative(uint code, uint mapType, nint keyboardLayout);
}
