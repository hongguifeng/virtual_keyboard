using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Core.Layouts;
using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows;

/// <summary>Dispatches validated layout actions to the existing Text, Key and Hotkey input paths.</summary>
public sealed class LayoutActionDispatcher
{
    private readonly TargetSessionValidator _validator;
    private readonly KeyboardController _controller;
    private readonly Func<WindowsKeyboardKey, nint, KeyInputTransition, int, InputSendResult> _sendKey;
    private readonly Func<IReadOnlyList<HotkeyModifier>, WindowsKeyboardKey, nint, int, CancellationToken, InputSendResult> _sendHotkey;
    private readonly Func<string, int, InputSendResult> _sendText;
    private readonly Func<long, CapsLockOperationResult>? _toggleCapsLock;

    public LayoutActionDispatcher(
        TargetSessionValidator validator,
        KeyboardController controller,
        KeyInputSender keySender,
        HotkeyInputSender hotkeySender,
        UnicodeTextInputSender textSender,
        CapsLockStateService? capsLock = null)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        ArgumentNullException.ThrowIfNull(keySender);
        ArgumentNullException.ThrowIfNull(hotkeySender);
        ArgumentNullException.ThrowIfNull(textSender);
        _sendKey = keySender.Send;
        _sendHotkey = hotkeySender.Send;
        _sendText = textSender.Send;
        _toggleCapsLock = capsLock is null ? null : capsLock.Toggle;
    }

    internal LayoutActionDispatcher(
        TargetSessionValidator validator,
        KeyboardController controller,
        Func<WindowsKeyboardKey, nint, KeyInputTransition, int, InputSendResult> sendKey,
        Func<IReadOnlyList<HotkeyModifier>, WindowsKeyboardKey, nint, int, CancellationToken, InputSendResult> sendHotkey,
        Func<string, int, InputSendResult> sendText,
        Func<long, CapsLockOperationResult>? toggleCapsLock = null)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _sendKey = sendKey ?? throw new ArgumentNullException(nameof(sendKey));
        _sendHotkey = sendHotkey ?? throw new ArgumentNullException(nameof(sendHotkey));
        _sendText = sendText ?? throw new ArgumentNullException(nameof(sendText));
        _toggleCapsLock = toggleCapsLock;
    }

    public InputSendResult Dispatch(long sessionId, KeyViewModel key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (cancellationToken.IsCancellationRequested)
        {
            return new(InputSendStatus.Cancelled, 0, 0, 0);
        }
        TargetValidationResult validation = _validator.Validate(sessionId);
        if (!validation.IsValid)
        {
            return new(InputSendStatus.TargetInvalid, 0, 0, 0);
        }

        if (validation.Session!.IsPassword &&
            (!key.SafeForPassword || !PasswordActionPolicy.Check(key.Action).IsAllowed))
        {
            return Rejected();
        }

        LayoutActionDefinition action = key.Action;
        if (action.Type == LayoutActionTypes.Modifier)
        {
            return DispatchModifier(sessionId, action.Modifier);
        }

        WindowsKeyboardKey parsedKey = default;
        if (action.Type is LayoutActionTypes.Key or LayoutActionTypes.Hotkey && !TryParseKey(action, out parsedKey))
        {
            return Rejected();
        }

        if (action.Type == LayoutActionTypes.Hotkey && ParseModifiers(action.Modifiers) is null)
        {
            return Rejected();
        }

        KeyboardActionPreparation preparation;
        try
        {
            preparation = _controller.PrepareAction(action);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Rejected();
        }

        TargetSession session = validation.Session;
        if (action.Type == LayoutActionTypes.Text)
        {
            return _sendText(action.Value!, session.ProcessId);
        }

        if (action.Type == LayoutActionTypes.Key)
        {
            HotkeyModifier[] modifiers = ModifiersFrom(preparation);
            return modifiers.Length == 0
                ? _sendKey(parsedKey, session.FocusHwnd, KeyInputTransition.Press, session.ProcessId)
                : _sendHotkey(modifiers, parsedKey, session.FocusHwnd, session.ProcessId, cancellationToken);
        }

        if (action.Type == LayoutActionTypes.Hotkey)
        {
            var declared = ParseModifiers(action.Modifiers);
            return _sendHotkey(
                declared!.Concat(ModifiersFrom(preparation)).Distinct().ToArray(),
                parsedKey,
                session.FocusHwnd,
                session.ProcessId,
                cancellationToken);
        }

        return Rejected();
    }

    private InputSendResult DispatchModifier(long sessionId, string? modifier)
    {
        if (modifier is null)
        {
            return Rejected();
        }
        if (modifier.Equals("CapsLock", StringComparison.OrdinalIgnoreCase))
        {
            return _toggleCapsLock is null
                ? Rejected()
                : _toggleCapsLock(sessionId).IsSuccess
                    ? new(InputSendStatus.Succeeded, 2, 2, 0)
                    : new(InputSendStatus.Failed, 2, 0, 0);
        }

        KeyboardModifier? parsed = modifier.ToLowerInvariant() switch
        {
            "shift" => KeyboardModifier.Shift,
            "control" => KeyboardModifier.Control,
            "alt" => KeyboardModifier.Alt,
            _ => null,
        };
        if (parsed is null)
        {
            return Rejected();
        }
        _controller.ToggleModifier(parsed.Value);
        return new(InputSendStatus.Succeeded, 0, 0, 0);
    }

    private static bool TryParseKey(LayoutActionDefinition action, out WindowsKeyboardKey key)
    {
        key = default;
        return action.VirtualKey is not null &&
            Enum.TryParse(action.VirtualKey, ignoreCase: true, out key) &&
            Enum.IsDefined(key);
    }

    private static HotkeyModifier[]? ParseModifiers(IReadOnlyList<string>? modifiers)
    {
        if (modifiers is null)
        {
            return null;
        }
        var result = new List<HotkeyModifier>(modifiers.Count);
        foreach (string? value in modifiers)
        {
            HotkeyModifier? modifier = value?.ToLowerInvariant() switch
            {
                "shift" => HotkeyModifier.Shift,
                "control" => HotkeyModifier.Control,
                "alt" => HotkeyModifier.Alt,
                _ => null,
            };
            if (modifier is null || result.Contains(modifier.Value))
            {
                return null;
            }
            result.Add(modifier.Value);
        }
        return result.ToArray();
    }

    private static HotkeyModifier[] ModifiersFrom(KeyboardActionPreparation preparation)
    {
        var modifiers = new List<HotkeyModifier>(3);
        if (preparation.UseShift) modifiers.Add(HotkeyModifier.Shift);
        if (preparation.UseControl) modifiers.Add(HotkeyModifier.Control);
        if (preparation.UseAlt) modifiers.Add(HotkeyModifier.Alt);
        return modifiers.ToArray();
    }

    private static InputSendResult Rejected() => new(InputSendStatus.InvalidInput, 0, 0, 87);
}
