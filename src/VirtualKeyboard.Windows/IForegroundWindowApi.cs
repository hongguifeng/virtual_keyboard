namespace VirtualKeyboard.Windows;

internal interface IForegroundWindowApi
{
    nint GetForegroundWindow();

    uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    bool TryGetFocusWindow(uint threadId, out nint focusWindowHandle);
}
