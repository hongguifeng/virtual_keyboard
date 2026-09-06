using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Input;

namespace VirtualKeyboard.Windows;

/// <summary>Sends one virtual-key action as a single non-retried KeyDown/KeyUp batch.</summary>
public sealed class SingleKeyInputSender
{
    private const uint KeyboardInputType = 1;
    private const uint KeyUpFlag = 0x0002;
    private const ushort VirtualKeyA = 0x41;

    private readonly IInputNativeApi _api;
    private readonly DiagnosticLogger? _diagnostics;

    public SingleKeyInputSender(DiagnosticLogger? diagnostics = null)
        : this(new NativeInputApi(), diagnostics)
    {
    }

    internal SingleKeyInputSender(IInputNativeApi api, DiagnosticLogger? diagnostics = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _diagnostics = diagnostics;
    }

    /// <summary>Sends VK_A down then up exactly once; no target activation or retry is performed.</summary>
    public InputSendResult SendA(int targetProcessId = -1)
    {
        NativeInput[] inputs =
        [
            new NativeInput
            {
                Type = KeyboardInputType,
                Data = new NativeInputUnion
                {
                    Keyboard = new NativeKeyboardInput { VirtualKey = VirtualKeyA },
                },
            },
            new NativeInput
            {
                Type = KeyboardInputType,
                Data = new NativeInputUnion
                {
                    Keyboard = new NativeKeyboardInput { VirtualKey = VirtualKeyA, Flags = KeyUpFlag },
                },
            },
        ];

        _diagnostics?.Log(
            DiagnosticType.InputBatchStarted,
            DiagnosticModule.Input,
            targetProcessId: targetProcessId,
            requestedCount: inputs.Length);

        try
        {
            uint sent = _api.SendInput(inputs);
            int sentCount = sent > int.MaxValue ? int.MaxValue : (int)sent;
            int errorCode = sentCount == inputs.Length ? 0 : _api.LastError;
            InputSendStatus status = sentCount switch
            {
                2 => InputSendStatus.Succeeded,
                0 => InputSendStatus.Failed,
                _ => InputSendStatus.PartialFailure,
            };
            var result = new InputSendResult(status, inputs.Length, sentCount, errorCode);
            _diagnostics?.Log(
                status == InputSendStatus.Succeeded ? DiagnosticType.InputBatchSucceeded : DiagnosticType.InputBatchFailed,
                DiagnosticModule.Input,
                targetProcessId: targetProcessId,
                errorCode: errorCode,
                requestedCount: inputs.Length,
                completedCount: sentCount);
            return result;
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException)
        {
            const int unavailableError = 50;
            _diagnostics?.Log(
                DiagnosticType.InputBatchFailed,
                DiagnosticModule.Input,
                targetProcessId: targetProcessId,
                errorCode: unavailableError,
                requestedCount: inputs.Length);
            return new InputSendResult(InputSendStatus.NativeUnavailable, inputs.Length, 0, unavailableError);
        }
    }
}
