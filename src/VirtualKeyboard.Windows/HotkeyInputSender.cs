using System.Runtime.InteropServices;
using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Input;

namespace VirtualKeyboard.Windows;

public enum HotkeyModifier : ushort
{
    Shift = 0x10,
    Control = 0x11,
    Alt = 0x12,
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
        if (modifiers.Count is < 1 or > 3 || modifiers.Select(static item => item.Modifier).Distinct().Count() != modifiers.Count)
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

public sealed class HotkeyInputSender
{
    private const uint MapVirtualKeyToScanCodeExtended = 4;
    private const int InvalidParameterError = 87;
    private const int GeneralFailureError = 31;
    private const int NativeUnavailableError = 50;

    private readonly IInputNativeApi _inputApi;
    private readonly IKeyMappingNativeApi _mappingApi;
    private readonly IModifierStateNativeApi _modifierStateApi;
    private readonly DiagnosticLogger? _diagnostics;

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
        DiagnosticLogger? diagnostics = null)
    {
        _inputApi = inputApi ?? throw new ArgumentNullException(nameof(inputApi));
        _mappingApi = mappingApi ?? throw new ArgumentNullException(nameof(mappingApi));
        _modifierStateApi = modifierStateApi ?? throw new ArgumentNullException(nameof(modifierStateApi));
        _diagnostics = diagnostics;
    }

    public InputSendResult Send(
        IReadOnlyList<HotkeyModifier> modifiers,
        WindowsKeyboardKey key,
        nint targetFocusHwnd,
        int targetProcessId = -1,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new InputSendResult(InputSendStatus.Cancelled, 0, 0, 0);
        }
        if (modifiers is null || !Enum.IsDefined(key) || targetFocusHwnd == nint.Zero)
        {
            return InvalidInput(targetProcessId);
        }

        HotkeyModifier[] modifierSnapshot = modifiers.ToArray();
        if (modifierSnapshot.Length is < 1 or > 3 ||
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
                    Resolve((ushort)modifier, keyboardLayout, forceExtended: false),
                    (_modifierStateApi.GetAsyncKeyState((int)modifier) & 0x8000) != 0);
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
                TryBestEffortRelease(batch.BuildCleanupForAcceptedPrefix(sentCount));
            }
            LogCompletion(status, targetProcessId, batch.Inputs.Length, sentCount, errorCode);
            return new InputSendResult(status, batch.Inputs.Length, sentCount, errorCode);
        }
        catch (Exception exception)
        {
            TryBestEffortRelease(batch.BuildBestEffortModifierCleanup());
            int errorCode = IsNativeUnavailable(exception) ? NativeUnavailableError : GeneralFailureError;
            InputSendStatus status = IsNativeUnavailable(exception)
                ? InputSendStatus.NativeUnavailable
                : InputSendStatus.Failed;
            LogFailure(targetProcessId, batch.Inputs.Length, 0, errorCode);
            return new InputSendResult(status, batch.Inputs.Length, 0, errorCode);
        }
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

    private void TryBestEffortRelease(NativeInput[] cleanup)
    {
        if (cleanup.Length == 0)
        {
            return;
        }
        try
        {
            _ = _inputApi.SendInput(cleanup);
        }
        catch
        {
            // Best-effort cleanup must not hide the original batch result.
        }
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
