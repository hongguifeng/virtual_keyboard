using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Core.Layouts;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class HotkeyInputSenderTests
{
    [Fact]
    public void SystemModifierStateApiCanReadSupportedKeys()
    {
        var api = new SystemModifierStateNativeApi();
        _ = api.GetAsyncKeyState((int)HotkeyModifier.Control);
        _ = api.GetAsyncKeyState((int)HotkeyModifier.Shift);
        _ = api.GetAsyncKeyState((int)HotkeyModifier.Alt);
        _ = api.GetAsyncKeyState((int)HotkeyModifier.Windows);
    }

    [Fact]
    public void BuilderPressesModifiersInOrderAndReleasesInReverseOrder()
    {
        HotkeyInputBatch batch = HotkeyInputBuilder.Build(
            new(0x1B, 0x01, false),
            [
                new(HotkeyModifier.Control, new(0x11, 0x1D, false), false),
                new(HotkeyModifier.Shift, new(0x10, 0x2A, false), false),
                new(HotkeyModifier.Alt, new(0x12, 0x38, false), false),
            ]);

        Assert.Equal(8, batch.Inputs.Length);
        AssertEvent(batch.Inputs[0], 0x11, 0);
        AssertEvent(batch.Inputs[1], 0x10, 0);
        AssertEvent(batch.Inputs[2], 0x12, 0);
        AssertEvent(batch.Inputs[3], 0x1B, 0);
        AssertEvent(batch.Inputs[4], 0x1B, 0x0002);
        AssertEvent(batch.Inputs[5], 0x12, 0x0002);
        AssertEvent(batch.Inputs[6], 0x10, 0x0002);
        AssertEvent(batch.Inputs[7], 0x11, 0x0002);
        Assert.Equal(
            [HotkeyModifier.Control, HotkeyModifier.Shift, HotkeyModifier.Alt],
            batch.ModifiersPressedByUs.Select(static item => item.Modifier));
    }

    [Fact]
    public void BuilderDoesNotPressOrReleasePhysicalModifier()
    {
        HotkeyInputBatch batch = HotkeyInputBuilder.Build(
            new(0x09, 0x0F, false),
            [
                new(HotkeyModifier.Control, new(0x11, 0x1D, false), true),
                new(HotkeyModifier.Shift, new(0x10, 0x2A, false), false),
            ]);

        Assert.Equal(4, batch.Inputs.Length);
        AssertEvent(batch.Inputs[0], 0x10, 0);
        AssertEvent(batch.Inputs[1], 0x09, 0);
        AssertEvent(batch.Inputs[2], 0x09, 0x0002);
        AssertEvent(batch.Inputs[3], 0x10, 0x0002);
        Assert.Equal(HotkeyModifier.Shift, Assert.Single(batch.ModifiersPressedByUs).Modifier);
        Assert.DoesNotContain(batch.Inputs, static item => item.Data.Keyboard.VirtualKey == 0x11);
    }

    [Fact]
    public void WindowsModifierUsesExtendedKeyEvents()
    {
        var input = new SequencedInputApi(4u);

        InputSendResult result = new HotkeyInputSender(input, ValidMapping(), new FakeModifierStateApi()).Send(
            [HotkeyModifier.Windows],
            WindowsKeyboardKey.D,
            (nint)10);

        Assert.True(result.IsSuccess);
        NativeInput[] batch = Assert.Single(input.Batches);
        AssertEvent(batch[0], 0x5B, 0x0001);
        AssertEvent(batch[3], 0x5B, 0x0003);
    }

    [Fact]
    public void ChordPressesEveryKeyInOrderAndReleasesInReverseOrder()
    {
        var input = new SequencedInputApi(6u);

        InputSendResult result = new HotkeyInputSender(input, ValidMapping(), new FakeModifierStateApi()).SendChord(
            [WindowsKeyboardKey.LeftWindows, WindowsKeyboardKey.Tab, WindowsKeyboardKey.A],
            (nint)10);

        Assert.True(result.IsSuccess);
        NativeInput[] batch = Assert.Single(input.Batches);
        Assert.Equal(new ushort[] { 0x5B, 0x09, 0x41, 0x41, 0x09, 0x5B },
            batch.Select(static item => item.Data.Keyboard.VirtualKey));
        AssertEvent(batch[0], 0x5B, 0x0001);
        AssertEvent(batch[3], 0x41, 0x0002);
        AssertEvent(batch[4], 0x09, 0x0002);
        AssertEvent(batch[5], 0x5B, 0x0003);
    }

    [Theory]
    [InlineData(1, new ushort[] { 0x5B })]
    [InlineData(2, new ushort[] { 0x09, 0x5B })]
    [InlineData(3, new ushort[] { 0x41, 0x09, 0x5B })]
    [InlineData(4, new ushort[] { 0x09, 0x5B })]
    [InlineData(5, new ushort[] { 0x5B })]
    public void PartialChordSendReleasesOnlyKeysStillDown(int acceptedCount, ushort[] expectedCleanup)
    {
        var input = new SequencedInputApi((uint)acceptedCount, (uint)expectedCleanup.Length) { Error = 5 };

        InputSendResult result = new HotkeyInputSender(input, ValidMapping(), new FakeModifierStateApi()).SendChord(
            [WindowsKeyboardKey.LeftWindows, WindowsKeyboardKey.Tab, WindowsKeyboardKey.A],
            (nint)10);

        Assert.Equal(InputSendStatus.PartialFailure, result.Status);
        Assert.Equal(expectedCleanup, input.Batches[1].Select(static item => item.Data.Keyboard.VirtualKey));
        Assert.All(input.Batches[1], static item => Assert.NotEqual(0u, item.Data.Keyboard.Flags & 0x0002));
    }

    [Fact]
    public void ChordExceptionReleasesAllPressedKeysInReverseOrder()
    {
        var input = new SequencedInputApi(new InvalidOperationException("send"), 3u);

        InputSendResult result = new HotkeyInputSender(input, ValidMapping(), new FakeModifierStateApi()).SendChord(
            [WindowsKeyboardKey.LeftWindows, WindowsKeyboardKey.Tab, WindowsKeyboardKey.A],
            (nint)10);

        Assert.Equal(InputSendStatus.Failed, result.Status);
        Assert.Equal(new ushort[] { 0x41, 0x09, 0x5B }, input.Batches[1].Select(static item => item.Data.Keyboard.VirtualKey));
    }

    [Fact]
    public void InvalidChordsAreRejectedBeforeNativeCalls()
    {
        var input = new SequencedInputApi();
        using var sender = new HotkeyInputSender(input, ValidMapping(), new FakeModifierStateApi());

        Assert.Equal(InputSendStatus.InvalidInput, sender.SendChord([], (nint)10).Status);
        Assert.Equal(InputSendStatus.InvalidInput, sender.SendChord([WindowsKeyboardKey.A, WindowsKeyboardKey.A], (nint)10).Status);
        Assert.Equal(InputSendStatus.InvalidInput, sender.SendChord(
            Enumerable.Repeat(WindowsKeyboardKey.A, LayoutSchemaLimits.MaximumChordKeys + 1).ToArray(), (nint)10).Status);
        Assert.Equal(0, input.Calls);
    }

    [Fact]
    public void ChordDoesNotDuplicateOrReleaseLatchedModifier()
    {
        var input = new SequencedInputApi(1u, 2u, 1u);
        using var sender = new HotkeyInputSender(input, ValidMapping(), new FakeModifierStateApi());
        Assert.True(sender.SendModifierTransition(HotkeyModifier.Control, (nint)10, KeyInputTransition.KeyDown).IsSuccess);

        Assert.True(sender.SendChord([WindowsKeyboardKey.Control, WindowsKeyboardKey.A], (nint)10).IsSuccess);

        Assert.Equal(new ushort[] { 0x41, 0x41 }, input.Batches[1].Select(static item => item.Data.Keyboard.VirtualKey));
        sender.ReleaseLatchedModifiers();
        Assert.Equal((ushort)0x11, input.Batches[2][0].Data.Keyboard.VirtualKey);
    }

    [Fact]
    public void ChordDoesNotDuplicateOrReleasePhysicallyHeldModifier()
    {
        var input = new SequencedInputApi(2u);
        var state = new FakeModifierStateApi((int)WindowsKeyboardKey.Control);

        InputSendResult result = new HotkeyInputSender(input, ValidMapping(), state).SendChord(
            [WindowsKeyboardKey.Control, WindowsKeyboardKey.A], (nint)10);

        Assert.True(result.IsSuccess);
        Assert.Equal(new ushort[] { 0x41, 0x41 }, Assert.Single(input.Batches)
            .Select(static item => item.Data.Keyboard.VirtualKey));
    }

    [Fact]
    public void PersistentModifierSendsDownThenMainKeyWithoutDuplicateThenUp()
    {
        var input = new SequencedInputApi(1u, 2u, 1u);
        using var sender = new HotkeyInputSender(input, ValidMapping(), new FakeModifierStateApi());

        Assert.True(sender.SendModifierTransition(HotkeyModifier.Shift, (nint)10, KeyInputTransition.KeyDown).IsSuccess);
        Assert.True(sender.Send([HotkeyModifier.Shift], WindowsKeyboardKey.D1, (nint)10).IsSuccess);
        Assert.True(sender.SendModifierTransition(HotkeyModifier.Shift, (nint)10, KeyInputTransition.KeyUp).IsSuccess);

        Assert.Equal(3, input.Batches.Count);
        AssertEvent(input.Batches[0][0], 0x10, 0);
        Assert.Equal(new ushort[] { 0x31, 0x31 }, input.Batches[1].Select(static item => item.Data.Keyboard.VirtualKey));
        AssertEvent(input.Batches[2][0], 0x10, 0x0002);
    }

    [Fact]
    public void DisposeReleasesPersistentModifiersInReverseOrder()
    {
        var input = new SequencedInputApi(1u, 1u, 2u);
        var sender = new HotkeyInputSender(input, ValidMapping(), new FakeModifierStateApi());
        Assert.True(sender.SendModifierTransition(HotkeyModifier.Control, (nint)10, KeyInputTransition.KeyDown).IsSuccess);
        Assert.True(sender.SendModifierTransition(HotkeyModifier.Shift, (nint)10, KeyInputTransition.KeyDown).IsSuccess);

        sender.Dispose();

        Assert.Equal(new ushort[] { 0x10, 0x11 }, input.Batches[^1].Select(static item => item.Data.Keyboard.VirtualKey));
        Assert.All(input.Batches[^1], static item => Assert.NotEqual(0u, item.Data.Keyboard.Flags & 0x0002));
    }

    [Theory]
    [InlineData(0, new ushort[0])]
    [InlineData(1, new ushort[] { 0x11 })]
    [InlineData(2, new ushort[] { 0x10, 0x11 })]
    [InlineData(3, new ushort[] { 0x1B, 0x10, 0x11 })]
    [InlineData(4, new ushort[] { 0x10, 0x11 })]
    [InlineData(5, new ushort[] { 0x11 })]
    [InlineData(6, new ushort[0])]
    public void CleanupMatchesKeysStillDownInAcceptedPrefix(int acceptedCount, ushort[] expectedKeyUps)
    {
        HotkeyInputBatch batch = HotkeyInputBuilder.Build(
            new(0x1B, 0x01, false),
            [
                new(HotkeyModifier.Control, new(0x11, 0x1D, false), false),
                new(HotkeyModifier.Shift, new(0x10, 0x2A, false), false),
            ]);

        NativeInput[] cleanup = batch.BuildCleanupForAcceptedPrefix(acceptedCount);

        Assert.Equal(expectedKeyUps, cleanup.Select(static item => item.Data.Keyboard.VirtualKey));
        Assert.All(cleanup, static item => Assert.NotEqual(0u, item.Data.Keyboard.Flags & 0x0002));
    }

    [Fact]
    public void SenderReadsPhysicalStateAndSubmitsOneOrderedBatch()
    {
        var input = new SequencedInputApi(6u);
        var state = new FakeModifierStateApi((int)HotkeyModifier.Control);
        var mapping = ValidMapping();

        InputSendResult result = new HotkeyInputSender(input, mapping, state).Send(
            [HotkeyModifier.Control, HotkeyModifier.Shift, HotkeyModifier.Alt],
            WindowsKeyboardKey.Escape,
            (nint)0x1234);

        Assert.True(result.IsSuccess);
        Assert.Equal([(int)HotkeyModifier.Control, (int)HotkeyModifier.Shift, (int)HotkeyModifier.Alt], state.RequestedKeys);
        Assert.Equal(1, input.Calls);
        NativeInput[] batch = input.Batches[0];
        Assert.Equal(6, batch.Length);
        Assert.Equal(new ushort[] { 0x10, 0x12, 0x1B, 0x1B, 0x12, 0x10 },
            batch.Select(static item => item.Data.Keyboard.VirtualKey));
    }

    [Fact]
    public void SenderSnapshotsMutableModifierListBeforeStateReads()
    {
        var modifiers = new List<HotkeyModifier> { HotkeyModifier.Control, HotkeyModifier.Shift };
        var state = new FakeModifierStateApi(onRead: () => modifiers.Clear());
        var input = new SequencedInputApi(6u);

        InputSendResult result = new HotkeyInputSender(input, ValidMapping(), state).Send(
            modifiers,
            WindowsKeyboardKey.Escape,
            (nint)10);

        Assert.True(result.IsSuccess);
        Assert.Equal(new ushort[] { 0x11, 0x10, 0x1B, 0x1B, 0x10, 0x11 },
            input.Batches[0].Select(static item => item.Data.Keyboard.VirtualKey));
    }

    [Fact]
    public void PartialReturnRunsTargetedCleanupWithoutRetryingHotkey()
    {
        var input = new SequencedInputApi(3u, 3u) { Error = 5 };

        InputSendResult result = new HotkeyInputSender(
            input,
            ValidMapping(),
            new FakeModifierStateApi()).Send(
                [HotkeyModifier.Control, HotkeyModifier.Shift],
                WindowsKeyboardKey.Escape,
                (nint)10);

        Assert.Equal(InputSendStatus.PartialFailure, result.Status);
        Assert.Equal(6, result.RequestedEvents);
        Assert.Equal(3, result.SentEvents);
        Assert.Equal(2, input.Calls);
        Assert.Equal(new ushort[] { 0x1B, 0x10, 0x11 },
            input.Batches[1].Select(static item => item.Data.Keyboard.VirtualKey));
        Assert.All(input.Batches[1], static item => Assert.NotEqual(0u, item.Data.Keyboard.Flags & 0x0002));
    }

    [Fact]
    public void ZeroReturnDoesNotSendUnnecessaryCleanupOrRetry()
    {
        var input = new SequencedInputApi(0u) { Error = 5 };

        InputSendResult result = new HotkeyInputSender(
            input,
            ValidMapping(),
            new FakeModifierStateApi()).Send(
                [HotkeyModifier.Control],
                WindowsKeyboardKey.Escape,
                (nint)10);

        Assert.Equal(InputSendStatus.Failed, result.Status);
        Assert.Equal(1, input.Calls);
    }

    [Fact]
    public void UnexpectedSendExceptionBestEffortReleasesOnlySyntheticModifiers()
    {
        var input = new SequencedInputApi(new InvalidOperationException("synthetic"), 2u);

        InputSendResult result = new HotkeyInputSender(
            input,
            ValidMapping(),
            new FakeModifierStateApi((int)HotkeyModifier.Control)).Send(
                [HotkeyModifier.Control, HotkeyModifier.Shift, HotkeyModifier.Alt],
                WindowsKeyboardKey.Escape,
                (nint)10);

        Assert.Equal(InputSendStatus.Failed, result.Status);
        Assert.Equal(31, result.ErrorCode);
        Assert.Equal(2, input.Calls);
        Assert.Equal(new ushort[] { 0x12, 0x10 },
            input.Batches[1].Select(static item => item.Data.Keyboard.VirtualKey));
        Assert.DoesNotContain(input.Batches[1], static item => item.Data.Keyboard.VirtualKey == 0x11);
    }

    [Fact]
    public void CleanupFailureDoesNotHideOriginalPartialResult()
    {
        var input = new SequencedInputApi(1u, new InvalidOperationException("cleanup")) { Error = 5 };
        var sender = new HotkeyInputSender(input, ValidMapping(), new FakeModifierStateApi());

        InputSendResult result = sender.Send(
            [HotkeyModifier.Control],
            WindowsKeyboardKey.Escape,
            (nint)10);

        Assert.Equal(InputSendStatus.PartialFailure, result.Status);
        Assert.Equal(1, result.SentEvents);
        Assert.Equal(2, input.Calls);

        InputSendResult blocked = sender.Send(
            [HotkeyModifier.Shift],
            WindowsKeyboardKey.Tab,
            (nint)10);
        Assert.Equal(InputSendStatus.SafetyFaulted, blocked.Status);
        Assert.Equal(2, input.Calls);

        sender.Dispose();
        Assert.Equal(3, input.Calls);
        Assert.Equal((ushort)HotkeyModifier.Control, Assert.Single(input.Batches[2]).Data.Keyboard.VirtualKey);
        Assert.NotEqual(0u, input.Batches[2][0].Data.Keyboard.Flags & 0x0002);
        sender.Dispose();
        Assert.Equal(3, input.Calls);
    }

    [Fact]
    public void PartialCleanupTracksOnlyModifierKeyUpsNotAcceptedByCleanup()
    {
        var input = new SequencedInputApi(3u, 2u, 1u) { Error = 5 };
        var sender = new HotkeyInputSender(input, ValidMapping(), new FakeModifierStateApi());

        InputSendResult result = sender.Send(
            [HotkeyModifier.Control, HotkeyModifier.Shift],
            WindowsKeyboardKey.Escape,
            (nint)10);

        Assert.Equal(InputSendStatus.PartialFailure, result.Status);
        Assert.Equal(new ushort[] { 0x1B, 0x10, 0x11 },
            input.Batches[1].Select(static item => item.Data.Keyboard.VirtualKey));

        sender.Dispose();
        Assert.Equal(3, input.Calls);
        Assert.Equal((ushort)HotkeyModifier.Control, Assert.Single(input.Batches[2]).Data.Keyboard.VirtualKey);
    }

    [Fact]
    public void FailedMainKeyCleanupAlsoLatchesAndRetriesOnDispose()
    {
        var input = new SequencedInputApi(1u, new InvalidOperationException("cleanup"));
        var sender = new HotkeyInputSender(
            input,
            ValidMapping(),
            new FakeModifierStateApi((int)HotkeyModifier.Control));

        InputSendResult result = sender.Send(
            [HotkeyModifier.Control],
            WindowsKeyboardKey.Escape,
            (nint)10);

        Assert.Equal(InputSendStatus.PartialFailure, result.Status);
        Assert.Equal(2, result.RequestedEvents);
        Assert.Equal(InputSendStatus.SafetyFaulted, sender.Send(
            [HotkeyModifier.Control],
            WindowsKeyboardKey.Tab,
            (nint)10).Status);

        sender.Dispose();
        Assert.Equal(3, input.Calls);
        NativeInput keyUp = Assert.Single(input.Batches[2]);
        Assert.Equal((ushort)WindowsKeyboardKey.Escape, keyUp.Data.Keyboard.VirtualKey);
        Assert.NotEqual(0u, keyUp.Data.Keyboard.Flags & 0x0002);
    }

    [Fact]
    public void DisposeAfterSuccessfulBalancedBatchMakesNoReleaseCall()
    {
        var input = new SequencedInputApi(4u);
        var sender = new HotkeyInputSender(input, ValidMapping(), new FakeModifierStateApi());

        Assert.True(sender.Send(
            [HotkeyModifier.Control],
            WindowsKeyboardKey.Escape,
            (nint)10).IsSuccess);

        sender.Dispose();
        Assert.Equal(1, input.Calls);
        Assert.Equal(InputSendStatus.SafetyFaulted, sender.Send(
            [HotkeyModifier.Control],
            WindowsKeyboardKey.Escape,
            (nint)10).Status);
        Assert.Equal(1, input.Calls);
    }

    [Fact]
    public async Task DisposeWaitsForInFlightBatchBeforeClosingSender()
    {
        using var enteredSend = new ManualResetEventSlim();
        using var allowSendToFinish = new ManualResetEventSlim();
        var input = new BlockingInputApi(enteredSend, allowSendToFinish, 4);
        var sender = new HotkeyInputSender(input, ValidMapping(), new FakeModifierStateApi());

        Task<InputSendResult> sendTask = Task.Run(() => sender.Send(
            [HotkeyModifier.Control],
            WindowsKeyboardKey.Escape,
            (nint)10));
        Assert.True(enteredSend.Wait(TimeSpan.FromSeconds(5)));

        Task disposeTask = Task.Run(sender.Dispose);
        Task firstCompletion = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromMilliseconds(100)));
        Assert.NotSame(disposeTask, firstCompletion);
        allowSendToFinish.Set();

        Assert.True((await sendTask).IsSuccess);
        await disposeTask;
        Assert.Equal(1, input.Calls);
        Assert.Equal(InputSendStatus.SafetyFaulted, sender.Send(
            [HotkeyModifier.Control],
            WindowsKeyboardKey.Escape,
            (nint)10).Status);
    }

    [Fact]
    public void CancellationBeforeOrDuringPreparationMakesNoNativeInputCall()
    {
        using var alreadyCancelled = new CancellationTokenSource();
        alreadyCancelled.Cancel();
        var firstInput = new SequencedInputApi();
        InputSendResult before = new HotkeyInputSender(
            firstInput,
            ValidMapping(),
            new FakeModifierStateApi()).Send(
                [HotkeyModifier.Control],
                WindowsKeyboardKey.Escape,
                (nint)10,
                cancellationToken: alreadyCancelled.Token);
        Assert.Equal(InputSendStatus.Cancelled, before.Status);
        Assert.Equal(0, firstInput.Calls);

        using var cancelledDuringStateRead = new CancellationTokenSource();
        var state = new FakeModifierStateApi(onRead: cancelledDuringStateRead.Cancel);
        var secondInput = new SequencedInputApi();
        InputSendResult during = new HotkeyInputSender(secondInput, ValidMapping(), state).Send(
            [HotkeyModifier.Control],
            WindowsKeyboardKey.Escape,
            (nint)10,
            cancellationToken: cancelledDuringStateRead.Token);
        Assert.Equal(InputSendStatus.Cancelled, during.Status);
        Assert.Equal(0, secondInput.Calls);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void InvalidRequestIsRejectedBeforeNativeCalls(
        IReadOnlyList<HotkeyModifier>? modifiers,
        WindowsKeyboardKey key,
        nint hwnd)
    {
        var input = new SequencedInputApi();
        var mapping = ValidMapping();
        var state = new FakeModifierStateApi();

        InputSendResult result = new HotkeyInputSender(input, mapping, state).Send(
            modifiers!,
            key,
            hwnd);

        Assert.Equal(InputSendStatus.InvalidInput, result.Status);
        Assert.Equal(0, input.Calls);
        Assert.Equal(0, mapping.MapCalls);
        Assert.Empty(state.RequestedKeys);
    }

    public static TheoryData<IReadOnlyList<HotkeyModifier>?, WindowsKeyboardKey, nint> InvalidRequests => new()
    {
        { null, WindowsKeyboardKey.Escape, (nint)10 },
        { Array.Empty<HotkeyModifier>(), WindowsKeyboardKey.Escape, (nint)10 },
        { new[] { HotkeyModifier.Control, HotkeyModifier.Control }, WindowsKeyboardKey.Escape, (nint)10 },
        { new[] { (HotkeyModifier)0xFFFF }, WindowsKeyboardKey.Escape, (nint)10 },
        { new[] { HotkeyModifier.Control }, (WindowsKeyboardKey)0xFFFF, (nint)10 },
        { new[] { HotkeyModifier.Control }, WindowsKeyboardKey.Escape, nint.Zero },
    };

    [Fact]
    public void MappingAndStateNativeFailuresAreControlled()
    {
        var mapping = ValidMapping() with { Failure = new DllNotFoundException("synthetic") };
        InputSendResult mapResult = new HotkeyInputSender(
            new SequencedInputApi(),
            mapping,
            new FakeModifierStateApi()).Send(
                [HotkeyModifier.Control],
                WindowsKeyboardKey.Escape,
                (nint)10);
        Assert.Equal(InputSendStatus.NativeUnavailable, mapResult.Status);

        var state = new FakeModifierStateApi(failure: new EntryPointNotFoundException("synthetic"));
        InputSendResult stateResult = new HotkeyInputSender(
            new SequencedInputApi(),
            ValidMapping(),
            state).Send(
                [HotkeyModifier.Control],
                WindowsKeyboardKey.Escape,
                (nint)10);
        Assert.Equal(InputSendStatus.NativeUnavailable, stateResult.Status);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void MissingThreadLayoutOrScanMappingFailsBeforeInput(
        bool missingThread,
        bool missingLayout,
        bool missingScanCode)
    {
        var mapping = ValidMapping() with
        {
            ThreadId = missingThread ? 0u : 20u,
            KeyboardLayout = missingLayout ? nint.Zero : (nint)0x4090409,
            ReturnZeroMapping = missingScanCode,
        };
        var input = new SequencedInputApi();

        InputSendResult result = new HotkeyInputSender(
            input,
            mapping,
            new FakeModifierStateApi()).Send(
                [HotkeyModifier.Control],
                WindowsKeyboardKey.Escape,
                (nint)10);

        Assert.Equal(InputSendStatus.InvalidInput, result.Status);
        Assert.Equal(0, input.Calls);
    }

    [Fact]
    public void DiagnosticsContainCountsButNoCharacterizedHotkey()
    {
        using var diagnostics = new DiagnosticLogger();
        var input = new SequencedInputApi(4u);

        _ = new HotkeyInputSender(input, ValidMapping(), new FakeModifierStateApi(), diagnostics).Send(
            [HotkeyModifier.Control],
            WindowsKeyboardKey.Escape,
            (nint)10,
            targetProcessId: 42);

        Assert.True(diagnostics.TryReadNext(out DiagnosticEvent? started));
        Assert.True(diagnostics.TryReadNext(out DiagnosticEvent? succeeded));
        Assert.Equal(42, started!.TargetProcessId);
        Assert.Equal(4, started.RequestedCount);
        Assert.Equal(42, succeeded!.TargetProcessId);
        Assert.Equal(4, succeeded.CompletedCount);
        Assert.False(diagnostics.TryReadNext(out _));
    }

    [Fact]
    public void UnreleasedModifierLatchesClosedDiagnosticBeforeOriginalFailure()
    {
        using var diagnostics = new DiagnosticLogger();
        var input = new SequencedInputApi(1u, new InvalidOperationException("cleanup")) { Error = 5 };
        var sender = new HotkeyInputSender(
            input,
            ValidMapping(),
            new FakeModifierStateApi(),
            diagnostics);

        _ = sender.Send(
            [HotkeyModifier.Control],
            WindowsKeyboardKey.Escape,
            (nint)10,
            targetProcessId: 42);

        Assert.True(diagnostics.TryReadNext(out DiagnosticEvent? started));
        Assert.True(diagnostics.TryReadNext(out DiagnosticEvent? safetyFault));
        Assert.True(diagnostics.TryReadNext(out DiagnosticEvent? originalFailure));
        Assert.Equal(DiagnosticType.InputBatchStarted, started!.Type);
        Assert.Equal(DiagnosticType.InputSafetyFaulted, safetyFault!.Type);
        Assert.Equal(1, safetyFault.RequestedCount);
        Assert.Equal(0, safetyFault.CompletedCount);
        Assert.Equal(DiagnosticType.InputBatchFailed, originalFailure!.Type);
        Assert.False(diagnostics.TryReadNext(out _));
    }

    private static void AssertEvent(NativeInput input, ushort virtualKey, uint flags)
    {
        Assert.Equal(virtualKey, input.Data.Keyboard.VirtualKey);
        Assert.Equal(flags, input.Data.Keyboard.Flags);
    }

    private static FakeMappingApi ValidMapping() => new()
    {
        ThreadId = 20,
        ProcessId = 30,
        KeyboardLayout = (nint)0x4090409,
    };

    private sealed class SequencedInputApi(params object[] outcomes) : IInputNativeApi
    {
        private readonly Queue<object> _outcomes = new(outcomes);
        public int Error { get; init; }
        public int Calls => Batches.Count;
        public List<NativeInput[]> Batches { get; } = [];
        public int LastError => Error;

        public uint SendInput(NativeInput[] inputs)
        {
            Batches.Add(inputs.ToArray());
            if (_outcomes.Count == 0)
            {
                return 0;
            }
            object outcome = _outcomes.Dequeue();
            if (outcome is Exception exception)
            {
                throw exception;
            }
            return (uint)outcome;
        }
    }

    private sealed class BlockingInputApi(
        ManualResetEventSlim entered,
        ManualResetEventSlim release,
        uint returnCount) : IInputNativeApi
    {
        public int Calls { get; private set; }
        public int LastError => 0;

        public uint SendInput(NativeInput[] inputs)
        {
            _ = inputs;
            Calls++;
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            return returnCount;
        }
    }

    private sealed record FakeMappingApi : IKeyMappingNativeApi
    {
        public uint ThreadId { get; init; }
        public uint ProcessId { get; init; }
        public nint KeyboardLayout { get; init; }
        public Exception? Failure { get; init; }
        public bool ReturnZeroMapping { get; init; }
        public int MapCalls { get; private set; }

        public uint GetWindowThreadProcessId(nint hwnd, out uint processId)
        {
            _ = hwnd;
            if (Failure is not null) throw Failure;
            processId = ProcessId;
            return ThreadId;
        }

        public nint GetKeyboardLayout(uint threadId)
        {
            _ = threadId;
            return KeyboardLayout;
        }

        public uint MapVirtualKeyEx(uint code, uint mapType, nint keyboardLayout)
        {
            _ = mapType;
            _ = keyboardLayout;
            MapCalls++;
            if (ReturnZeroMapping) return 0;
            return code switch
            {
                0x10 => 0x2A,
                0x11 => 0x1D,
                0x12 => 0x38,
                0x1B => 0x01,
                _ => 0x1C,
            };
        }
    }

    private sealed class FakeModifierStateApi : IModifierStateNativeApi
    {
        private readonly HashSet<int> _downKeys;
        private readonly Action? _onRead;
        private readonly Exception? _failure;

        public FakeModifierStateApi(
            int downKey = -1,
            Action? onRead = null,
            Exception? failure = null)
        {
            _downKeys = downKey < 0 ? [] : [downKey];
            _onRead = onRead;
            _failure = failure;
        }

        public List<int> RequestedKeys { get; } = [];

        public short GetAsyncKeyState(int virtualKey)
        {
            RequestedKeys.Add(virtualKey);
            _onRead?.Invoke();
            if (_failure is not null) throw _failure;
            return _downKeys.Contains(virtualKey) ? unchecked((short)0x8000) : (short)0;
        }
    }
}
