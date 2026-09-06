using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows;

/// <summary>Captures foreground and GUI-thread focus handles without changing window activation.</summary>
public sealed class NativeForegroundTargetCapture : IForegroundTargetCapture
{
    private readonly IForegroundWindowApi _api;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly int _currentProcessId;

    public NativeForegroundTargetCapture()
        : this(new NativeForegroundWindowApi(), static () => DateTimeOffset.UtcNow, Environment.ProcessId)
    {
    }

    internal NativeForegroundTargetCapture(
        IForegroundWindowApi api,
        Func<DateTimeOffset> utcNow,
        int currentProcessId)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(currentProcessId);
        _currentProcessId = currentProcessId;
    }

    public TargetCaptureResult Capture()
    {
        try
        {
            nint topLevelWindow = _api.GetForegroundWindow();
            if (topLevelWindow == nint.Zero)
            {
                return TargetCaptureResult.Failure(TargetCaptureStatus.NoForegroundWindow);
            }

            uint threadId = _api.GetWindowThreadProcessId(topLevelWindow, out uint processId);
            if (threadId == 0 || processId == 0 || processId > int.MaxValue)
            {
                return TargetCaptureResult.Failure(TargetCaptureStatus.ProcessLookupFailed);
            }

            if (processId == (uint)_currentProcessId)
            {
                return TargetCaptureResult.Failure(TargetCaptureStatus.OwnProcess);
            }

            if (!_api.TryGetFocusWindow(threadId, out nint focusWindow) || focusWindow == nint.Zero)
            {
                return TargetCaptureResult.Failure(TargetCaptureStatus.FocusWindowUnavailable);
            }

            return TargetCaptureResult.Success(new TargetCaptureSnapshot(
                _utcNow(),
                checked((int)processId),
                topLevelWindow,
                focusWindow));
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException)
        {
            return TargetCaptureResult.Failure(TargetCaptureStatus.CaptureUnavailable);
        }
    }
}
