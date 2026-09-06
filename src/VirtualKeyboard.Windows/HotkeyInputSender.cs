using System.Runtime.InteropServices;
using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Core.Layouts;

namespace VirtualKeyboard.Windows;

public enum HotkeyModifier : ushort
{
    Shift = 0x10,
    Control = 0x11,
    Alt = 0x12,
    Windows = 0x5B,
}

internal readonly record struct ResolvedHotkeyModifier(
    HotkeyModifier Modifier,
    ResolvedKeyInput Key,
    bool IsPhysicallyDown);

internal sealed record HotkeyModifierEvent(
    HotkeyModifier Modifier,
    ResolvedKeyInput Key,
    int DownIndex,
    int UpIndex);

internal sealed class HotkeyInputBatch
{
    internal HotkeyInputBatch(
        NativeInput[] inputs,
        ResolvedKeyInput mainKey,
        int mainDownIndex,
        int mainUpIndex,
        HotkeyModifierEvent[] modifiersPressedByUs)
    {
        Inputs = inputs;
        MainKey = mainKey;
        MainDownIndex = mainDownIndex;
        MainUpIndex = mainUpIndex;
        ModifiersPressedByUs = modifiersPressedByUs;
    }

    internal NativeInput[] Inputs { get; }
    internal ResolvedKeyInput MainKey { get; }
    internal int MainDownIndex { get; }
    internal int MainUpIndex { get; }
    internal IReadOnlyList<HotkeyModifierEvent> ModifiersPressedByUs { get; }

    internal NativeInput[] BuildCleanupForAcceptedPrefix(int acceptedCount)
    {
        int boundedCount = Math.Clamp(acceptedCount, 0, Inputs.Length);
        var cleanup = new List<NativeInput>();
        if (MainDownIndex < boundedCount && MainUpIndex >= boundedCount)
        {
            cleanup.Add(KeyInputBuilder.Build(MainKey, KeyInputTransition.KeyUp)[0]);
        }

        foreach (HotkeyModifierEvent modifier in ModifiersPressedByUs.Reverse())
        {
            if (modifier.DownIndex < boundedCount && modifier.UpIndex >= boundedCount)
            {
                cleanup.Add(KeyInputBuilder.Build(modifier.Key, KeyInputTransition.KeyUp)[0]);
            }
        }
        return cleanup.ToArray();
    }

    internal NativeInput[] BuildBestEffortModifierCleanup() => ModifiersPressedByUs
        .Reverse()
        .Select(static modifier => KeyInputBuilder.Build(modifier.Key, KeyInputTransition.KeyUp)[0])
        .ToArray();
}

internal static class HotkeyInputBuilder
{
    internal static HotkeyInputBatch Build(
        ResolvedKeyInput mainKey,
        IReadOnlyList<ResolvedHotkeyModifier> modifiers)
    {
        ArgumentNullException.ThrowIfNull(modifiers);
        if (modifiers.Count is < 1 or > 4 || modifiers.Select(static item => item.Modifier).Distinct().Count() != modifiers.Count)
        {
            throw new ArgumentException("A hotkey requires one to three unique modifiers.", nameof(modifiers));
        }

        ResolvedHotkeyModifier[] syntheticModifiers = modifiers
            .Where(static modifier => !modifier.IsPhysicallyDown)
            .ToArray();
        var inputs = new List<NativeInput>(checked((syntheticModifiers.Length * 2) + 2));
        var modifierEvents = new HotkeyModifierEvent[syntheticModifiers.Length];

        for (int index = 0; index < syntheticModifiers.Length; index++)
        {
            ResolvedHotkeyModifier modifier = syntheticModifiers[index];
            int downIndex = inputs.Count;
            inputs.Add(KeyInputBuilder.Build(modifier.Key, KeyInputTransition.KeyDown)[0]);
            modifierEvents[index] = new HotkeyModifierEvent(modifier.Modifier, modifier.Key, downIndex, -1);
        }

        int mainDownIndex = inputs.Count;
        inputs.AddRange(KeyInputBuilder.Build(mainKey));
        int mainUpIndex = mainDownIndex + 1;

        for (int index = syntheticModifiers.Length - 1; index >= 0; index--)
        {
            int upIndex = inputs.Count;
            inputs.Add(KeyInputBuilder.Build(syntheticModifiers[index].Key, KeyInputTransition.KeyUp)[0]);
            modifierEvents[index] = modifierEvents[index] with { UpIndex = upIndex };
        }

        return new HotkeyInputBatch(inputs.ToArray(), mainKey, mainDownIndex, mainUpIndex, modifierEvents);
    }
}

public sealed class HotkeyInputSender : IDisposable
{
    private const uint MapVirtualKeyToScanCodeExtended = 4;
    private const int InvalidParameterError = 87;
    private const int GeneralFailureError = 31;
    private const int NativeUnavailableError = 50;

    private readonly IInputNativeApi _inputApi;
    private readonly IKeyMappingNativeApi _mappingApi;
    private readonly IModifierStateNativeApi _modifierStateApi;
    private readonly DiagnosticLogger? _diagnostics;
    private readonly SyntheticKeySafetyLatch _safetyLatch;
    private readonly object _sendGate = new();
    private readonly Dictionary<HotkeyModifier, ResolvedKeyInput> _heldModifiers = [];

    public HotkeyInputSender(DiagnosticLogger? diagnostics = null)
        : this(
            new NativeInputApi(),
            new SystemKeyMappingNativeApi(),
            new SystemModifierStateNativeApi(),
            diagnostics)
    {
    }

    internal HotkeyInputSender(
        IInputNativeApi inputApi,
        IKeyMappingNativeApi mappingApi,
        IModifierStateNativeApi modifierStateApi,
        DiagnosticLogger? diagnostics = null,
        SyntheticKeySafetyLatch? safetyLatch = null)
    {
        _inputApi = inputApi ?? throw new ArgumentNullException(nameof(inputApi));
        _mappingApi = mappingApi ?? throw new ArgumentNullException(nameof(mappingApi));
        _modifierStateApi = modifierStateApi ?? throw new ArgumentNullException(nameof(modifierStateApi));
        _diagnostics = diagnostics;
        _safetyLatch = safetyLatch ?? new SyntheticKeySafetyLatch(inputApi);
    }

    public InputSendResult Send(
        IReadOnlyList<HotkeyModifier> modifiers,
        WindowsKeyboardKey key,
        nint targetFocusHwnd,
        int targetProcessId = -1,
        CancellationToken cancellationToken = default)
    {
        lock (_sendGate)
        {
            return SendCore(modifiers, key, targetFocusHwnd, targetProcessId, cancellationToken);
        }
    }

    /// <summary>Sends and tracks a persistent synthetic modifier transition.</summary>
    public InputSendResult SendModifierTransition(
        HotkeyModifier modifier,
        nint targetFocusHwnd,
        KeyInputTransition transition,
        int targetProcessId = -1)
    {
        lock (_sendGate)
        {
            if (_safetyLatch.IsBlocked)
            {
                return new(InputSendStatus.SafetyFaulted, 0, 0, GeneralFailureError);
            }
            if (!Enum.IsDefined(modifier) || targetFocusHwnd == nint.Zero ||
                transition is not (KeyInputTransition.KeyDown or KeyInputTransition.KeyUp))
            {
                return InvalidInput(targetProcessId);
            }

            bool isDown = transition == KeyInputTransition.KeyDown;
            if (isDown == _heldModifiers.ContainsKey(modifier))
            {
                return new(InputSendStatus.Succeeded, 0, 0, 0);
            }

            ResolvedKeyInput key = default;
            NativeInput? plannedInput = null;
            try
            {
                if (!_heldModifiers.TryGetValue(modifier, out key))
                {
                    uint targetThreadId = _mappingApi.GetWindowThreadProcessId(targetFocusHwnd, out _);
                    nint keyboardLayout = targetThreadId == 0 ? nint.Zero : _mappingApi.GetKeyboardLayout(targetThreadId);
                    if (keyboardLayout == nint.Zero) return InvalidInput(targetProcessId);
                    key = Resolve((ushort)modifier, keyboardLayout, forceExtended: modifier == HotkeyModifier.Windows);
                }

                NativeInput input = KeyInputBuilder.Build(key, transition)[0];
                plannedInput = input;
                uint sent = _inputApi.SendInput([input]);
                if (sent == 1)
                {
                    if (isDown) _heldModifiers.Add(modifier, key);
                    else _heldModifiers.Remove(modifier);
                    LogCompletion(InputSendStatus.Succeeded, targetProcessId, 1, 1, 0);
                    return new(InputSendStatus.Succeeded, 1, 1, 0);
                }

                int errorCode = _inputApi.LastError;
                if (!isDown)
                {
                    _heldModifiers.Remove(modifier);
                    LatchUnreleasedKeys([input], targetProcessId, 1, 0, errorCode);
                }
                LogFailure(targetProcessId, 1, 0, errorCode);
                return new(InputSendStatus.Failed, 1, 0, errorCode);
            }
            catch (ArgumentException)
            {
                return InvalidInput(targetProcessId);
            }
            catch (Exception exception)
            {
                if (plannedInput.HasValue)
                {
                    NativeInput cleanup = isDown
                        ? KeyInputBuilder.Build(key, KeyInputTransition.KeyUp)[0]
                        : plannedInput.Value;
                    _heldModifiers.Remove(modifier);
                    LatchUnreleasedKeys([cleanup], targetProcessId, 1, 0, GeneralFailureError);
                }
                InputSendStatus status = IsNativeUnavailable(exception) ? InputSendStatus.NativeUnavailable : InputSendStatus.Failed;
                int errorCode = status == InputSendStatus.NativeUnavailable ? NativeUnavailableError : GeneralFailureError;
                LogFailure(targetProcessId, plannedInput.HasValue ? 1 : 0, 0, errorCode);
                return new(status, plannedInput.HasValue ? 1 : 0, 0, errorCode);
            }
        }
    }

    /// <summary>Sends an ordered key chord as Down events followed by reverse-order Up events.</summary>
    public InputSendResult SendChord(
        IReadOnlyList<WindowsKeyboardKey> keys,
        nint targetFocusHwnd,
        int targetProcessId = -1,
        CancellationToken cancellationToken = default)
    {
        lock (_sendGate)
        {
            if (_safetyLatch.IsBlocked) return new(InputSendStatus.SafetyFaulted, 0, 0, GeneralFailureError);
            if (cancellationToken.IsCancellationRequested) return new(InputSendStatus.Cancelled, 0, 0, 0);
            if (keys is null || targetFocusHwnd == nint.Zero) return InvalidInput(targetProcessId);
            WindowsKeyboardKey[] snapshot = keys.ToArray();
            if (snapshot.Length is < 1 or > LayoutSchemaLimits.MaximumChordKeys ||
                snapshot.Distinct().Count() != snapshot.Length ||
                snapshot.Any(static key => !Enum.IsDefined(key) || !LayoutValidator.IsAllowedChordKey(key.ToString())))
            {
                return InvalidInput(targetProcessId);
            }

            ResolvedKeyInput[] resolved;
            try
            {
                uint targetThreadId = _mappingApi.GetWindowThreadProcessId(targetFocusHwnd, out _);
                nint keyboardLayout = targetThreadId == 0 ? nint.Zero : _mappingApi.GetKeyboardLayout(targetThreadId);
                if (keyboardLayout == nint.Zero) return InvalidInput(targetProcessId);
                resolved = snapshot
                    .Where(key => !IsHeldChordKey(key) && !IsPhysicallyHeldChordKey(key))
                    .Select(key => Resolve((ushort)key, keyboardLayout,
                        key is WindowsKeyboardKey.LeftWindows or WindowsKeyboardKey.RightWindows || IsNavigationExtendedKey(key)))
                    .ToArray();
            }
            catch (Exception exception) when (IsNativeUnavailable(exception))
            {
                LogFailure(targetProcessId, 0, 0, NativeUnavailableError);
                return new(InputSendStatus.NativeUnavailable, 0, 0, NativeUnavailableError);
            }
            catch (ArgumentException)
            {
                return InvalidInput(targetProcessId);
            }

            if (resolved.Length == 0) return new(InputSendStatus.Succeeded, 0, 0, 0);
            NativeInput[] inputs = resolved.Select(key => KeyInputBuilder.Build(key, KeyInputTransition.KeyDown)[0])
                .Concat(resolved.Reverse().Select(key => KeyInputBuilder.Build(key, KeyInputTransition.KeyUp)[0]))
                .ToArray();
            if (cancellationToken.IsCancellationRequested) return new(InputSendStatus.Cancelled, 0, 0, 0);
            try
            {
                uint nativeCount = _inputApi.SendInput(inputs);
                int sent = nativeCount > int.MaxValue ? int.MaxValue : (int)nativeCount;
                InputSendStatus status = sent == inputs.Length
                    ? InputSendStatus.Succeeded
                    : sent == 0 ? InputSendStatus.Failed : InputSendStatus.PartialFailure;
                int errorCode = status == InputSendStatus.Succeeded ? 0 : _inputApi.LastError;
                if (status != InputSendStatus.Succeeded)
                {
                    NativeInput[] cleanup = BuildChordCleanup(resolved, sent);
                    int released = TryBestEffortRelease(cleanup);
                    LatchUnreleasedKeys(cleanup.Skip(Math.Clamp(released, 0, cleanup.Length)), targetProcessId, cleanup.Length, released, errorCode);
                }
                LogCompletion(status, targetProcessId, inputs.Length, sent, errorCode);
                return new(status, inputs.Length, sent, errorCode);
            }
            catch (Exception exception)
            {
                NativeInput[] cleanup = resolved.Reverse()
                    .Select(static key => KeyInputBuilder.Build(key, KeyInputTransition.KeyUp)[0]).ToArray();
                int released = TryBestEffortRelease(cleanup);
                int errorCode = IsNativeUnavailable(exception) ? NativeUnavailableError : GeneralFailureError;
                InputSendStatus status = IsNativeUnavailable(exception) ? InputSendStatus.NativeUnavailable : InputSendStatus.Failed;
                LatchUnreleasedKeys(cleanup.Skip(Math.Clamp(released, 0, cleanup.Length)), targetProcessId, cleanup.Length, released, errorCode);
                LogFailure(targetProcessId, inputs.Length, 0, errorCode);
                return new(status, inputs.Length, 0, errorCode);
            }
        }
    }

    /// <summary>Best-effort releases all modifiers held by this sender.</summary>
    public void ReleaseLatchedModifiers()
    {
        lock (_sendGate)
        {
            ReleaseLatchedModifiersCore();
        }
    }

    private InputSendResult SendCore(
        IReadOnlyList<HotkeyModifier> modifiers,
        WindowsKeyboardKey key,
        nint targetFocusHwnd,
        int targetProcessId,
        CancellationToken cancellationToken)
    {
        if (_safetyLatch.IsBlocked)
        {
            return new InputSendResult(InputSendStatus.SafetyFaulted, 0, 0, 31);
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return new InputSendResult(InputSendStatus.Cancelled, 0, 0, 0);
        }
        if (modifiers is null || !Enum.IsDefined(key) || targetFocusHwnd == nint.Zero)
        {
            return InvalidInput(targetProcessId);
        }

        HotkeyModifier[] modifierSnapshot = modifiers.ToArray();
        if (modifierSnapshot.Length is < 1 or > 4 ||
            modifierSnapshot.Distinct().Count() != modifierSnapshot.Length ||
            modifierSnapshot.Any(static modifier => !Enum.IsDefined(modifier)))
        {
            return InvalidInput(targetProcessId);
        }

        HotkeyInputBatch batch;
        try
        {
            uint targetThreadId = _mappingApi.GetWindowThreadProcessId(targetFocusHwnd, out _);
            nint keyboardLayout = targetThreadId == 0 ? nint.Zero : _mappingApi.GetKeyboardLayout(targetThreadId);
            if (keyboardLayout == nint.Zero)
            {
                return InvalidInput(targetProcessId);
            }

            ResolvedKeyInput mainKey = Resolve((ushort)key, keyboardLayout, IsNavigationExtendedKey(key));
            var resolvedModifiers = new ResolvedHotkeyModifier[modifierSnapshot.Length];
            for (int index = 0; index < modifierSnapshot.Length; index++)
            {
                HotkeyModifier modifier = modifierSnapshot[index];
                resolvedModifiers[index] = new ResolvedHotkeyModifier(
                    modifier,
                    Resolve((ushort)modifier, keyboardLayout, forceExtended: modifier == HotkeyModifier.Windows),
                    _heldModifiers.ContainsKey(modifier) || (_modifierStateApi.GetAsyncKeyState((int)modifier) & 0x8000) != 0);
            }
            batch = HotkeyInputBuilder.Build(mainKey, resolvedModifiers);
        }
        catch (Exception exception) when (IsNativeUnavailable(exception))
        {
            LogFailure(targetProcessId, 0, 0, NativeUnavailableError);
            return new InputSendResult(InputSendStatus.NativeUnavailable, 0, 0, NativeUnavailableError);
        }
        catch (ArgumentException)
        {
            return InvalidInput(targetProcessId);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new InputSendResult(InputSendStatus.Cancelled, 0, 0, 0);
        }

        _diagnostics?.Log(
            DiagnosticType.InputBatchStarted,
            DiagnosticModule.Input,
            targetProcessId: targetProcessId,
            requestedCount: batch.Inputs.Length);

        try
        {
            uint nativeCount = _inputApi.SendInput(batch.Inputs);
            int sentCount = nativeCount > int.MaxValue ? int.MaxValue : (int)nativeCount;
            InputSendStatus status = sentCount == batch.Inputs.Length
                ? InputSendStatus.Succeeded
                : sentCount == 0 ? InputSendStatus.Failed : InputSendStatus.PartialFailure;
            int errorCode = status == InputSendStatus.Succeeded ? 0 : _inputApi.LastError;
            if (status != InputSendStatus.Succeeded)
            {
                NativeInput[] cleanup = batch.BuildCleanupForAcceptedPrefix(sentCount);
                int cleanupSent = TryBestEffortRelease(cleanup);
                LatchUnreleasedKeys(
                    cleanup.Skip(Math.Clamp(cleanupSent, 0, cleanup.Length)),
                    targetProcessId,
                    cleanup.Length,
                    cleanupSent,
                    errorCode);
            }
            LogCompletion(status, targetProcessId, batch.Inputs.Length, sentCount, errorCode);
            return new InputSendResult(status, batch.Inputs.Length, sentCount, errorCode);
        }
        catch (Exception exception)
        {
            NativeInput[] cleanup = batch.BuildBestEffortModifierCleanup();
            int cleanupSent = TryBestEffortRelease(cleanup);
            int errorCode = IsNativeUnavailable(exception) ? NativeUnavailableError : GeneralFailureError;
            InputSendStatus status = IsNativeUnavailable(exception)
                ? InputSendStatus.NativeUnavailable
                : InputSendStatus.Failed;
            LatchUnreleasedKeys(
                cleanup.Skip(Math.Clamp(cleanupSent, 0, cleanup.Length)),
                targetProcessId,
                cleanup.Length,
                cleanupSent,
                errorCode);
            LogFailure(targetProcessId, batch.Inputs.Length, 0, errorCode);
            return new InputSendResult(status, batch.Inputs.Length, 0, errorCode);
        }
    }

    public void Dispose()
    {
        lock (_sendGate)
        {
            ReleaseLatchedModifiersCore();
            _safetyLatch.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    private void ReleaseLatchedModifiersCore()
    {
        if (_heldModifiers.Count == 0) return;
        NativeInput[] releases = _heldModifiers.Values.Reverse()
            .Select(static key => KeyInputBuilder.Build(key, KeyInputTransition.KeyUp)[0])
            .ToArray();
        _heldModifiers.Clear();
        int released = TryBestEffortRelease(releases);
        LatchUnreleasedKeys(releases.Skip(Math.Clamp(released, 0, releases.Length)), -1, releases.Length, released, GeneralFailureError);
    }

    private bool IsHeldChordKey(WindowsKeyboardKey key) => key switch
    {
        WindowsKeyboardKey.Shift => _heldModifiers.ContainsKey(HotkeyModifier.Shift),
        WindowsKeyboardKey.Control => _heldModifiers.ContainsKey(HotkeyModifier.Control),
        WindowsKeyboardKey.Alt => _heldModifiers.ContainsKey(HotkeyModifier.Alt),
        WindowsKeyboardKey.LeftWindows or WindowsKeyboardKey.RightWindows => _heldModifiers.ContainsKey(HotkeyModifier.Windows),
        _ => false,
    };

    private bool IsPhysicallyHeldChordKey(WindowsKeyboardKey key) => key switch
    {
        WindowsKeyboardKey.Shift or WindowsKeyboardKey.Control or WindowsKeyboardKey.Alt or
            WindowsKeyboardKey.LeftWindows or WindowsKeyboardKey.RightWindows =>
            (_modifierStateApi.GetAsyncKeyState((int)key) & 0x8000) != 0,
        _ => false,
    };

    private static NativeInput[] BuildChordCleanup(ResolvedKeyInput[] keys, int acceptedCount)
    {
        int bounded = Math.Clamp(acceptedCount, 0, keys.Length * 2);
        var cleanup = new List<NativeInput>();
        for (int index = keys.Length - 1; index >= 0; index--)
        {
            int downIndex = index;
            int upIndex = keys.Length + (keys.Length - 1 - index);
            if (downIndex < bounded && upIndex >= bounded)
            {
                cleanup.Add(KeyInputBuilder.Build(keys[index], KeyInputTransition.KeyUp)[0]);
            }
        }
        return cleanup.ToArray();
    }

    private ResolvedKeyInput Resolve(ushort virtualKey, nint keyboardLayout, bool forceExtended)
    {
        uint mappedScanCode = _mappingApi.MapVirtualKeyEx(
            virtualKey,
            MapVirtualKeyToScanCodeExtended,
            keyboardLayout);
        ushort scanCode = (ushort)(mappedScanCode & 0xFF);
        if (scanCode == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(virtualKey));
        }
        bool isExtended = forceExtended || (mappedScanCode & 0xFF00) is 0xE000 or 0xE100;
        return new ResolvedKeyInput(virtualKey, scanCode, isExtended);
    }

    private int TryBestEffortRelease(NativeInput[] cleanup)
    {
        if (cleanup.Length == 0)
        {
            return 0;
        }
        try
        {
            uint released = _inputApi.SendInput(cleanup);
            return released > int.MaxValue ? int.MaxValue : (int)released;
        }
        catch
        {
            // Best-effort cleanup must not hide the original batch result.
            return 0;
        }
    }

    private void LatchUnreleasedKeys(
        IEnumerable<NativeInput> keyUps,
        int targetProcessId,
        int requestedCleanup,
        int completedCleanup,
        int errorCode)
    {
        if (!_safetyLatch.RecordUnreleased(keyUps))
        {
            return;
        }
        _diagnostics?.Log(
            DiagnosticType.InputSafetyFaulted,
            DiagnosticModule.Input,
            targetProcessId: targetProcessId,
            reason: ReasonCode.Unknown,
            errorCode: Math.Max(0, errorCode),
            requestedCount: requestedCleanup,
            completedCount: Math.Clamp(completedCleanup, 0, requestedCleanup));
    }

    private InputSendResult InvalidInput(int targetProcessId)
    {
        LogFailure(targetProcessId, 0, 0, InvalidParameterError);
        return new InputSendResult(InputSendStatus.InvalidInput, 0, 0, InvalidParameterError);
    }

    private void LogCompletion(
        InputSendStatus status,
        int targetProcessId,
        int requestedCount,
        int completedCount,
        int errorCode) =>
        _diagnostics?.Log(
            status == InputSendStatus.Succeeded ? DiagnosticType.InputBatchSucceeded : DiagnosticType.InputBatchFailed,
            DiagnosticModule.Input,
            targetProcessId: targetProcessId,
            errorCode: errorCode,
            requestedCount: requestedCount,
            completedCount: completedCount);

    private void LogFailure(int targetProcessId, int requestedCount, int completedCount, int errorCode) =>
        LogCompletion(InputSendStatus.Failed, targetProcessId, requestedCount, completedCount, errorCode);

    private static bool IsNavigationExtendedKey(WindowsKeyboardKey key) => key is
        WindowsKeyboardKey.PageUp or
        WindowsKeyboardKey.PageDown or
        WindowsKeyboardKey.End or
        WindowsKeyboardKey.Home or
        WindowsKeyboardKey.Left or
        WindowsKeyboardKey.Up or
        WindowsKeyboardKey.Right or
        WindowsKeyboardKey.Down or
        WindowsKeyboardKey.Insert or
        WindowsKeyboardKey.Delete;

    private static bool IsNativeUnavailable(Exception exception) => exception is
        DllNotFoundException or
        EntryPointNotFoundException or
        BadImageFormatException;
}

internal sealed class SyntheticKeySafetyLatch : IDisposable
{
    private readonly object _gate = new();
    private readonly IInputNativeApi _inputApi;
    private readonly List<NativeInput> _unreleased = [];
    private bool _faulted;
    private bool _disposed;

    internal SyntheticKeySafetyLatch(IInputNativeApi inputApi) =>
        _inputApi = inputApi ?? throw new ArgumentNullException(nameof(inputApi));

    internal bool IsBlocked
    {
        get { lock (_gate) return _faulted || _disposed; }
    }

    internal bool RecordUnreleased(IEnumerable<NativeInput> keyUps)
    {
        ArgumentNullException.ThrowIfNull(keyUps);
        lock (_gate)
        {
            if (_disposed) return false;
            foreach (NativeInput keyUp in keyUps)
            {
                if (!_unreleased.Contains(keyUp)) _unreleased.Add(keyUp);
            }
            if (_unreleased.Count == 0) return false;
            _faulted = true;
            return true;
        }
    }

    public void Dispose()
    {
        NativeInput[] cleanup;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            cleanup = _unreleased.ToArray();
            _unreleased.Clear();
        }

        if (cleanup.Length > 0)
        {
            try { _ = _inputApi.SendInput(cleanup); }
            catch { }
        }
        GC.SuppressFinalize(this);
    }
}

internal interface IModifierStateNativeApi
{
    short GetAsyncKeyState(int virtualKey);
}

internal sealed class SystemModifierStateNativeApi : IModifierStateNativeApi
{
    public short GetAsyncKeyState(int virtualKey) => GetAsyncKeyStateNative(virtualKey);

    [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
    private static extern short GetAsyncKeyStateNative(int virtualKey);
}
