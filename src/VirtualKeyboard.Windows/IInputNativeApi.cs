namespace VirtualKeyboard.Windows;

internal interface IInputNativeApi
{
    uint SendInput(NativeInput[] inputs);

    int LastError { get; }
}
