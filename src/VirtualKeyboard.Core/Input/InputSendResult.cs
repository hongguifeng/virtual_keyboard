namespace VirtualKeyboard.Core.Input;

public readonly record struct InputSendResult(
    InputSendStatus Status,
    int RequestedEvents,
    int SentEvents,
    int ErrorCode)
{
    public bool IsSuccess => Status == InputSendStatus.Succeeded;
}
