using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Input;

namespace VirtualKeyboard.Windows;

internal static class UnicodeTextInputBuilder
{
    internal const int MaximumCodeUnits = 4096;
    private const uint KeyboardInputType = 1;
    private const uint KeyUpFlag = 0x0002;
    private const uint UnicodeFlag = 0x0004;

    public static NativeInput[] Build(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumCodeUnits) throw new ArgumentOutOfRangeException(nameof(text));
        var inputs = new NativeInput[checked(text.Length * 2)];
        for (int index = 0; index < text.Length; index++)
        {
            ushort codeUnit = text[index];
            inputs[index * 2] = Keyboard(codeUnit, UnicodeFlag);
            inputs[(index * 2) + 1] = Keyboard(codeUnit, UnicodeFlag | KeyUpFlag);
        }

        return inputs;
    }

    private static NativeInput Keyboard(ushort codeUnit, uint flags) => new()
    {
        Type = KeyboardInputType,
        Data = new NativeInputUnion
        {
            Keyboard = new NativeKeyboardInput { VirtualKey = 0, ScanCode = codeUnit, Flags = flags },
        },
    };
}

public sealed class UnicodeTextInputSender
{
    private readonly IInputNativeApi _api;
    private readonly DiagnosticLogger? _diagnostics;

    public UnicodeTextInputSender(DiagnosticLogger? diagnostics = null) : this(new NativeInputApi(), diagnostics) { }
    internal UnicodeTextInputSender(IInputNativeApi api, DiagnosticLogger? diagnostics = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _diagnostics = diagnostics;
    }

    public InputSendResult Send(string text, int targetProcessId = -1)
    {
        NativeInput[] inputs;
        try { inputs = UnicodeTextInputBuilder.Build(text); }
        catch (ArgumentException)
        {
            return new InputSendResult(InputSendStatus.InvalidInput, 0, 0, 0);
        }

        if (inputs.Length == 0) return new InputSendResult(InputSendStatus.Succeeded, 0, 0, 0);
        _diagnostics?.Log(
            DiagnosticType.InputBatchStarted,
            DiagnosticModule.Input,
            targetProcessId: targetProcessId,
            requestedCount: inputs.Length);
        try
        {
            uint nativeCount = _api.SendInput(inputs);
            int sent = nativeCount > int.MaxValue ? int.MaxValue : (int)nativeCount;
            InputSendStatus status = sent == inputs.Length
                ? InputSendStatus.Succeeded
                : sent == 0 ? InputSendStatus.Failed : InputSendStatus.PartialFailure;
            int error = status == InputSendStatus.Succeeded ? 0 : _api.LastError;
            _diagnostics?.Log(
                status == InputSendStatus.Succeeded ? DiagnosticType.InputBatchSucceeded : DiagnosticType.InputBatchFailed,
                DiagnosticModule.Input,
                targetProcessId: targetProcessId,
                errorCode: error,
                requestedCount: inputs.Length,
                completedCount: sent);
            return new InputSendResult(status, inputs.Length, sent, error);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
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
