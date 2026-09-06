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
    private readonly Func<HotkeyModifier, nint, KeyInputTransition, int, InputSendResult> _sendModifier;
    private readonly Func<IReadOnlyList<WindowsKeyboardKey>, nint, int, CancellationToken, InputSendResult> _sendChord;
    private readonly Func<string, int, InputSendResult> _sendText;
    private readonly object _momentaryGate = new();
    private readonly Dictionary<string, HeldMomentaryKey> _heldMomentaryKeys = [];

    public LayoutActionDispatcher(
        TargetSessionValidator validator,
        KeyboardController controller,
        KeyInputSender keySender,
        HotkeyInputSender hotkeySender,
        UnicodeTextInputSender textSender)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        ArgumentNullException.ThrowIfNull(keySender);
        ArgumentNullException.ThrowIfNull(hotkeySender);
        ArgumentNullException.ThrowIfNull(textSender);
        _sendKey = keySender.Send;
        _sendHotkey = hotkeySender.Send;
        _sendModifier = hotkeySender.SendModifierTransition;
        _sendChord = hotkeySender.SendChord;
        _sendText = textSender.Send;
    }

    internal LayoutActionDispatcher(
        TargetSessionValidator validator,
        KeyboardController controller,
        Func<WindowsKeyboardKey, nint, KeyInputTransition, int, InputSendResult> sendKey,
        Func<IReadOnlyList<HotkeyModifier>, WindowsKeyboardKey, nint, int, CancellationToken, InputSendResult> sendHotkey,
        Func<string, int, InputSendResult> sendText,
        Func<HotkeyModifier, nint, KeyInputTransition, int, InputSendResult>? sendModifier = null,
        Func<IReadOnlyList<WindowsKeyboardKey>, nint, int, CancellationToken, InputSendResult>? sendChord = null)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _sendKey = sendKey ?? throw new ArgumentNullException(nameof(sendKey));
        _sendHotkey = sendHotkey ?? throw new ArgumentNullException(nameof(sendHotkey));
        _sendText = sendText ?? throw new ArgumentNullException(nameof(sendText));
        _sendModifier = sendModifier ?? ((_, _, _, _) => new(InputSendStatus.Succeeded, 1, 1, 0));
        _sendChord = sendChord ?? ((_, _, _, _) => new(InputSendStatus.Succeeded, 2, 2, 0));
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
            return DispatchModifier(validation.Session!, action.Modifier);
        }

        WindowsKeyboardKey parsedKey = default;
        string? requestedVirtualKey = action.VirtualKey;
        if (action.Type == LayoutActionTypes.Key && _controller.State.FunctionLayerActive && action.FnVirtualKey is not null)
        {
            requestedVirtualKey = action.FnVirtualKey;
        }
        if (action.Type is LayoutActionTypes.Key or LayoutActionTypes.Hotkey && !TryParseKey(requestedVirtualKey, out parsedKey))
        {
            return Rejected();
        }

        if (action.Type == LayoutActionTypes.Hotkey && ParseModifiers(action.Modifiers) is null)
        {
            return Rejected();
        }
        WindowsKeyboardKey[]? chordKeys = action.Type == LayoutActionTypes.Chord ? ParseChordKeys(action.Keys) : null;
        if (action.Type == LayoutActionTypes.Chord && chordKeys is null) return Rejected();

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

        if (action.Type == LayoutActionTypes.Chord)
        {
            return _sendChord(
                ChordKeysFrom(preparation).Concat(chordKeys!).Distinct().ToArray(),
                session.FocusHwnd,
                session.ProcessId,
                cancellationToken);
        }

        return Rejected();
    }

    public InputSendResult DispatchKeyTransition(long sessionId, KeyViewModel key, KeyInputTransition transition)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Action.Type != LayoutActionTypes.Key || !Enum.IsDefined(transition) || transition == KeyInputTransition.Press)
            return Rejected();
        if (transition == KeyInputTransition.KeyUp)
        {
            return ReleaseMomentaryKey(key.Id);
        }
        TargetValidationResult validation = _validator.Validate(sessionId);
        if (!validation.IsValid || validation.Session is null) return new(InputSendStatus.TargetInvalid, 0, 0, 0);
        string? requestedVirtualKey = _controller.State.FunctionLayerActive && key.Action.FnVirtualKey is not null
            ? key.Action.FnVirtualKey
            : key.Action.VirtualKey;
        if (!TryParseKey(requestedVirtualKey, out WindowsKeyboardKey parsedKey)) return Rejected();
        if (validation.Session.IsPassword && (!key.SafeForPassword || !PasswordActionPolicy.Check(key.Action).IsAllowed)) return Rejected();
        lock (_momentaryGate)
        {
            if (_heldMomentaryKeys.ContainsKey(key.Id)) return new(InputSendStatus.Succeeded, 0, 0, 0);
            InputSendResult result = _sendKey(parsedKey, validation.Session.FocusHwnd, transition, validation.Session.ProcessId);
            if (result.IsSuccess)
            {
                _heldMomentaryKeys.Add(key.Id, new(parsedKey, validation.Session.FocusHwnd, validation.Session.ProcessId));
            }
            return result;
        }
    }

    public void ReleaseMomentaryKeys()
    {
        lock (_momentaryGate)
        {
            foreach ((string id, HeldMomentaryKey held) in _heldMomentaryKeys.ToArray())
            {
                if (_sendKey(held.Key, held.FocusHwnd, KeyInputTransition.KeyUp, held.ProcessId).IsSuccess)
                    _heldMomentaryKeys.Remove(id);
            }
        }
    }

    private InputSendResult ReleaseMomentaryKey(string id)
    {
        lock (_momentaryGate)
        {
            if (!_heldMomentaryKeys.TryGetValue(id, out HeldMomentaryKey held))
                return new(InputSendStatus.Succeeded, 0, 0, 0);
            InputSendResult result = _sendKey(held.Key, held.FocusHwnd, KeyInputTransition.KeyUp, held.ProcessId);
            if (result.IsSuccess) _heldMomentaryKeys.Remove(id);
            return result;
        }
    }

    private readonly record struct HeldMomentaryKey(WindowsKeyboardKey Key, nint FocusHwnd, int ProcessId);

    private InputSendResult DispatchModifier(TargetSession session, string? modifier)
    {
        if (modifier is null)
        {
            return Rejected();
        }
        if (modifier.Equals("CapsLock", StringComparison.OrdinalIgnoreCase))
        {
            CapsLockOperationResult caps = _controller.ToggleCapsLock();
            return caps.Status switch
            {
                CapsLockOperationStatus.Succeeded => new(InputSendStatus.Succeeded, 2, 2, 0),
                CapsLockOperationStatus.Unavailable => new(InputSendStatus.NativeUnavailable, 2, 0, 50),
                _ => new(InputSendStatus.Failed, 2, 0, 0),
            };
        }

        KeyboardModifier? parsed = modifier.ToLowerInvariant() switch
        {
            "shift" => KeyboardModifier.Shift,
            "control" => KeyboardModifier.Control,
            "alt" => KeyboardModifier.Alt,
            "windows" or "win" => KeyboardModifier.Windows,
            "fn" => KeyboardModifier.Function,
            _ => null,
        };
        if (parsed is null)
        {
            return Rejected();
        }
        if (parsed == KeyboardModifier.Function)
        {
            _controller.ToggleModifier(parsed.Value);
            return new(InputSendStatus.Succeeded, 0, 0, 0);
        }

        HotkeyModifier nativeModifier = parsed.Value switch
        {
            KeyboardModifier.Shift => HotkeyModifier.Shift,
            KeyboardModifier.Control => HotkeyModifier.Control,
            KeyboardModifier.Alt => HotkeyModifier.Alt,
            KeyboardModifier.Windows => HotkeyModifier.Windows,
            _ => throw new ArgumentOutOfRangeException(nameof(modifier)),
        };
        bool isLatched = parsed.Value switch
        {
            KeyboardModifier.Shift => _controller.State.ShiftLatched,
            KeyboardModifier.Control => _controller.State.ControlLatched,
            KeyboardModifier.Alt => _controller.State.AltLatched,
            KeyboardModifier.Windows => _controller.State.WindowsLatched,
            _ => false,
        };
        InputSendResult result = _sendModifier(
            nativeModifier,
            session.FocusHwnd,
            isLatched ? KeyInputTransition.KeyUp : KeyInputTransition.KeyDown,
            session.ProcessId);
        if (result.IsSuccess)
        {
            _controller.ToggleModifier(parsed.Value);
        }
        return result;
    }

    private static bool TryParseKey(string? virtualKey, out WindowsKeyboardKey key)
    {
        key = default;
        return virtualKey is not null &&
            Enum.TryParse(virtualKey, ignoreCase: true, out key) &&
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
                "windows" or "win" => HotkeyModifier.Windows,
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
        var modifiers = new List<HotkeyModifier>(4);
        if (preparation.UseShift) modifiers.Add(HotkeyModifier.Shift);
        if (preparation.UseControl) modifiers.Add(HotkeyModifier.Control);
        if (preparation.UseAlt) modifiers.Add(HotkeyModifier.Alt);
        if (preparation.UseWindows) modifiers.Add(HotkeyModifier.Windows);
        return modifiers.ToArray();
    }

    private static WindowsKeyboardKey[]? ParseChordKeys(IReadOnlyList<string>? keys)
    {
        if (keys is null || keys.Count is < 1 or > LayoutSchemaLimits.MaximumChordKeys) return null;
        var result = new List<WindowsKeyboardKey>(keys.Count);
        foreach (string? value in keys)
        {
            if (value is null || !Enum.TryParse(value, ignoreCase: true, out WindowsKeyboardKey key) ||
                !Enum.IsDefined(key) || !LayoutValidator.IsAllowedChordKey(value) || result.Contains(key)) return null;
            result.Add(key);
        }
        return result.ToArray();
    }

    private static IEnumerable<WindowsKeyboardKey> ChordKeysFrom(KeyboardActionPreparation preparation)
    {
        if (preparation.UseControl) yield return WindowsKeyboardKey.Control;
        if (preparation.UseShift) yield return WindowsKeyboardKey.Shift;
        if (preparation.UseAlt) yield return WindowsKeyboardKey.Alt;
        if (preparation.UseWindows) yield return WindowsKeyboardKey.LeftWindows;
    }

    private static InputSendResult Rejected() => new(InputSendStatus.InvalidInput, 0, 0, 87);
}
