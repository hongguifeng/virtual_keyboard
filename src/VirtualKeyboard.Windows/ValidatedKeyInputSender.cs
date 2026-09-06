using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows;

/// <summary>Validates the latest target immediately before sending a closed virtual-key action.</summary>
public sealed class ValidatedKeyInputSender
{
    private readonly TargetSessionValidator _validator;
    private readonly Func<WindowsKeyboardKey, nint, KeyInputTransition, int, InputSendResult> _send;
    private readonly DiagnosticLogger? _diagnostics;
    private readonly Action? _requestReclassification;

    public ValidatedKeyInputSender(
        TargetSessionValidator validator,
        KeyInputSender sender,
        DiagnosticLogger? diagnostics = null,
        Action? requestReclassification = null)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        ArgumentNullException.ThrowIfNull(sender);
        _send = sender.Send;
        _diagnostics = diagnostics;
        _requestReclassification = requestReclassification;
    }

    internal ValidatedKeyInputSender(
        TargetSessionValidator validator,
        Func<WindowsKeyboardKey, nint, KeyInputTransition, int, InputSendResult> send,
        DiagnosticLogger? diagnostics = null,
        Action? requestReclassification = null)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _diagnostics = diagnostics;
        _requestReclassification = requestReclassification;
    }

    public InputSendResult Send(
        long expectedSessionId,
        WindowsKeyboardKey key,
        KeyInputTransition transition = KeyInputTransition.Press)
    {
        TargetValidationResult validation = _validator.Validate(expectedSessionId);
        if (!validation.IsValid)
        {
            if (validation.RequiresReclassification)
            {
                _requestReclassification?.Invoke();
            }
            _diagnostics?.Log(
                DiagnosticType.InputBatchFailed,
                DiagnosticModule.Input,
                reason: ReasonCode.ValidationFailed);
            return new InputSendResult(InputSendStatus.TargetInvalid, 0, 0, 0);
        }

        TargetSession session = validation.Session!;
        return _send(key, session.FocusHwnd, transition, session.ProcessId);
    }
}
