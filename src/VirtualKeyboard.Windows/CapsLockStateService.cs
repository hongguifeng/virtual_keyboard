using System.Runtime.InteropServices;
using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Core.Layouts;

namespace VirtualKeyboard.Windows;

public sealed class CapsLockStateService : ICapsLockStateService
{
    private const int VirtualKeyCapsLock = 0x14;
    private readonly ValidatedKeyInputSender _sender;
    private readonly ILockKeyStateNativeApi _stateApi;

    public CapsLockStateService(ValidatedKeyInputSender sender)
        : this(sender, new SystemLockKeyStateNativeApi())
    {
    }

    internal CapsLockStateService(ValidatedKeyInputSender sender, ILockKeyStateNativeApi stateApi)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _stateApi = stateApi ?? throw new ArgumentNullException(nameof(stateApi));
    }

    public CapsLockOperationResult Read()
    {
        try
        {
            return new(CapsLockOperationStatus.Succeeded, (_stateApi.GetKeyState(VirtualKeyCapsLock) & 1) != 0);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return new(CapsLockOperationStatus.Unavailable, false);
        }
    }

    public CapsLockOperationResult Toggle(long targetSessionId)
    {
        if (targetSessionId <= 0)
        {
            return new(CapsLockOperationStatus.Failed, Read().IsOn);
        }

        InputSendResult send = _sender.Send(targetSessionId, WindowsKeyboardKey.CapsLock);
        if (!send.IsSuccess)
        {
            CapsLockOperationResult current = Read();
            return new(send.Status == InputSendStatus.NativeUnavailable
                ? CapsLockOperationStatus.Unavailable
                : CapsLockOperationStatus.Failed, current.IsOn);
        }

        return Read();
    }
}

internal interface ILockKeyStateNativeApi
{
    short GetKeyState(int virtualKey);
}

internal sealed class SystemLockKeyStateNativeApi : ILockKeyStateNativeApi
{
    public short GetKeyState(int virtualKey) => GetKeyStateNative(virtualKey);

    [DllImport("user32.dll", EntryPoint = "GetKeyState")]
    private static extern short GetKeyStateNative(int virtualKey);
}
