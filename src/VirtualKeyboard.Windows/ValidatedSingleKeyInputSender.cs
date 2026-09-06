using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows;

/// <summary>Guards the T1 single-key sender with a fresh target-session validation.</summary>
public sealed class ValidatedSingleKeyInputSender
{
    private readonly TargetSessionValidator _validator;
    private readonly Func<int, InputSendResult> _sendA;
    private readonly DiagnosticLogger? _diagnostics;

    public ValidatedSingleKeyInputSender(
        TargetSessionValidator validator,
        SingleKeyInputSender sender,
        DiagnosticLogger? diagnostics = null)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        ArgumentNullException.ThrowIfNull(sender);
        _diagnostics = diagnostics;
        _sendA = sender.SendA;
    }

    internal ValidatedSingleKeyInputSender(
        TargetSessionValidator validator,
        Func<int, InputSendResult> sendA,
        DiagnosticLogger? diagnostics = null)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _sendA = sendA ?? throw new ArgumentNullException(nameof(sendA));
        _diagnostics = diagnostics;
    }

    public InputSendResult SendA(long expectedSessionId)
    {
        TargetValidationResult validation = _validator.Validate(expectedSessionId);
        if (!validation.IsValid)
        {
            _diagnostics?.Log(
                DiagnosticType.InputBatchFailed,
                DiagnosticModule.Input,
                reason: ReasonCode.ValidationFailed);
            return new InputSendResult(InputSendStatus.TargetInvalid, 0, 0, 0);
        }

        return _sendA(validation.Session!.ProcessId);
    }
}
