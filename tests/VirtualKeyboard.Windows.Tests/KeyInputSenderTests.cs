using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Interop;
using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class KeyInputSenderTests
{
    [Fact]
    public void SystemMappingApiResolvesRealWindowThreadLayoutAndScanCode()
    {
        RunOnStaThread(() =>
        {
            var window = new Window { Width = 120, Height = 80, ShowInTaskbar = false };
            try
            {
                window.Show();
                nint hwnd = new WindowInteropHelper(window).Handle;
                var api = new SystemKeyMappingNativeApi();

                uint threadId = api.GetWindowThreadProcessId(hwnd, out uint processId);
                nint keyboardLayout = api.GetKeyboardLayout(threadId);
                uint scanCode = api.MapVirtualKeyEx(
                    (uint)WindowsKeyboardKey.Enter,
                    4,
                    keyboardLayout);

                Assert.NotEqual(0u, threadId);
                Assert.Equal((uint)Environment.ProcessId, processId);
                Assert.NotEqual(nint.Zero, keyboardLayout);
                Assert.NotEqual(0u, scanCode & 0xFF);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void BuilderCreatesVirtualKeyPressInDownUpOrder()
    {
        NativeInput[] inputs = KeyInputBuilder.Build(new(0x0D, 0x1C, false));

        Assert.Equal(2, inputs.Length);
        AssertKeyboard(inputs[0], 0x0D, 0x1C, 0);
        AssertKeyboard(inputs[1], 0x0D, 0x1C, 0x0002);
    }

    [Fact]
    public void BuilderMarksExtendedVirtualKeyOnBothTransitions()
    {
        NativeInput[] inputs = KeyInputBuilder.Build(new(0x25, 0x4B, true));

        AssertKeyboard(inputs[0], 0x25, 0x4B, 0x0001);
        AssertKeyboard(inputs[1], 0x25, 0x4B, 0x0003);
    }

    [Fact]
    public void BuilderSupportsPureScanCodeEncoding()
    {
        NativeInput[] inputs = KeyInputBuilder.Build(
            new(0x27, 0x4D, true),
            KeyInputTransition.Press,
            KeyInputEncoding.ScanCode);

        AssertKeyboard(inputs[0], 0, 0x4D, 0x0009);
        AssertKeyboard(inputs[1], 0, 0x4D, 0x000B);
    }

    [Theory]
    [InlineData(KeyInputTransition.KeyDown, 0u)]
    [InlineData(KeyInputTransition.KeyUp, 0x0002u)]
    public void BuilderSupportsIndividualTransitions(KeyInputTransition transition, uint expectedFlags)
    {
        NativeInput input = Assert.Single(KeyInputBuilder.Build(new(0x09, 0x0F, false), transition));
        AssertKeyboard(input, 0x09, 0x0F, expectedFlags);
    }

    [Theory]
    [InlineData(WindowsKeyboardKey.Enter, 0x1C)]
    [InlineData(WindowsKeyboardKey.Tab, 0x0F)]
    [InlineData(WindowsKeyboardKey.Backspace, 0x0E)]
    [InlineData(WindowsKeyboardKey.Escape, 0x01)]
    [InlineData(WindowsKeyboardKey.Space, 0x39)]
    [InlineData(WindowsKeyboardKey.D0, 0x0B)]
    [InlineData(WindowsKeyboardKey.A, 0x1E)]
    public void SenderMapsCommonKeysUsingTargetThreadLayout(WindowsKeyboardKey key, uint scanCode)
    {
        var input = new FakeInputApi { ReturnCount = 2 };
        var mapping = ValidMapping() with { MappedScanCode = scanCode };

        InputSendResult result = new KeyInputSender(input, mapping).Send(key, (nint)0x1234);

        Assert.True(result.IsSuccess);
        Assert.Equal((nint)0x1234, mapping.RequestedHwnd);
        Assert.Equal(mapping.ThreadId, mapping.RequestedLayoutThreadId);
        Assert.Equal((uint)(ushort)key, mapping.RequestedVirtualKey);
        Assert.Equal(4u, mapping.RequestedMapType);
        Assert.Equal(mapping.KeyboardLayout, mapping.RequestedMapLayout);
        Assert.Equal(1, input.Calls);
        Assert.All(input.LastBatch, item => Assert.Equal(0u, item.Data.Keyboard.Flags & 0x0001));
    }

    [Theory]
    [InlineData(WindowsKeyboardKey.Left)]
    [InlineData(WindowsKeyboardKey.Up)]
    [InlineData(WindowsKeyboardKey.Right)]
    [InlineData(WindowsKeyboardKey.Down)]
    [InlineData(WindowsKeyboardKey.Home)]
    [InlineData(WindowsKeyboardKey.End)]
    [InlineData(WindowsKeyboardKey.PageUp)]
    [InlineData(WindowsKeyboardKey.PageDown)]
    [InlineData(WindowsKeyboardKey.Insert)]
    [InlineData(WindowsKeyboardKey.Delete)]
    public void SenderMarksNavigationKeysExtended(WindowsKeyboardKey key)
    {
        var input = new FakeInputApi { ReturnCount = 2 };
        var mapping = ValidMapping() with { MappedScanCode = 0x4B };

        InputSendResult result = new KeyInputSender(input, mapping).Send(key, (nint)10);

        Assert.True(result.IsSuccess);
        Assert.Equal(0x0001u, input.LastBatch[0].Data.Keyboard.Flags);
        Assert.Equal(0x0003u, input.LastBatch[1].Data.Keyboard.Flags);
    }

    [Fact]
    public void ExtendedPrefixFromMapVirtualKeyIsHonored()
    {
        var input = new FakeInputApi { ReturnCount = 2 };
        var mapping = ValidMapping() with { MappedScanCode = 0xE01C };

        _ = new KeyInputSender(input, mapping).Send(WindowsKeyboardKey.Enter, (nint)10);

        Assert.All(input.LastBatch, item => Assert.Equal((ushort)0x1C, item.Data.Keyboard.ScanCode));
        Assert.Equal(0x0001u, input.LastBatch[0].Data.Keyboard.Flags);
        Assert.Equal(0x0003u, input.LastBatch[1].Data.Keyboard.Flags);
    }

    [Theory]
    [InlineData(KeyInputTransition.KeyDown, 0u)]
    [InlineData(KeyInputTransition.KeyUp, 0x0002u)]
    public void SenderSupportsSingleDownOrUpBatch(KeyInputTransition transition, uint expectedFlags)
    {
        var input = new FakeInputApi { ReturnCount = 1 };

        InputSendResult result = new KeyInputSender(input, ValidMapping()).Send(
            WindowsKeyboardKey.Tab,
            (nint)10,
            transition);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.RequestedEvents);
        AssertKeyboard(Assert.Single(input.LastBatch), 0x09, 0x1C, expectedFlags);
    }

    [Fact]
    public void InvalidRequestOrMappingFailsBeforeSendInput()
    {
        foreach ((WindowsKeyboardKey key, nint hwnd, FakeMappingApi mapping) in new[]
        {
            ((WindowsKeyboardKey)0xFFFF, (nint)10, ValidMapping()),
            (WindowsKeyboardKey.Enter, nint.Zero, ValidMapping()),
            (WindowsKeyboardKey.Enter, (nint)10, ValidMapping() with { ThreadId = 0 }),
            (WindowsKeyboardKey.Enter, (nint)10, ValidMapping() with { KeyboardLayout = nint.Zero }),
            (WindowsKeyboardKey.Enter, (nint)10, ValidMapping() with { MappedScanCode = 0 }),
        })
        {
            var input = new FakeInputApi();
            InputSendResult result = new KeyInputSender(input, mapping).Send(key, hwnd);
            Assert.Equal(InputSendStatus.InvalidInput, result.Status);
            Assert.Equal(87, result.ErrorCode);
            Assert.Equal(0, input.Calls);
        }
    }

    [Theory]
    [InlineData(0, InputSendStatus.Failed)]
    [InlineData(1, InputSendStatus.PartialFailure)]
    public void ShortNativeReturnFailsWithoutRetry(uint returnCount, InputSendStatus expectedStatus)
    {
        var input = new FakeInputApi { ReturnCount = returnCount, Error = 5 };

        InputSendResult result = new KeyInputSender(input, ValidMapping()).Send(
            WindowsKeyboardKey.Enter,
            (nint)10);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal((int)returnCount, result.SentEvents);
        Assert.Equal(5, result.ErrorCode);
        Assert.Equal(1, input.Calls);
    }

    [Fact]
    public void NativeLoadingFailuresAreControlledOnMappingAndSending()
    {
        var mappingFailure = ValidMapping() with { Failure = new DllNotFoundException("synthetic") };
        var neverCalled = new FakeInputApi();
        InputSendResult mapResult = new KeyInputSender(neverCalled, mappingFailure).Send(
            WindowsKeyboardKey.Enter,
            (nint)10);
        Assert.Equal(InputSendStatus.NativeUnavailable, mapResult.Status);
        Assert.Equal(0, neverCalled.Calls);

        var sendFailure = new FakeInputApi { Failure = new EntryPointNotFoundException("synthetic") };
        InputSendResult sendResult = new KeyInputSender(sendFailure, ValidMapping()).Send(
            WindowsKeyboardKey.Enter,
            (nint)10);
        Assert.Equal(InputSendStatus.NativeUnavailable, sendResult.Status);
        Assert.Equal(1, sendFailure.Calls);
    }

    [Fact]
    public void DiagnosticsContainOnlyTargetAndCounts()
    {
        using var diagnostics = new DiagnosticLogger();
        var input = new FakeInputApi { ReturnCount = 1, Error = 5 };

        _ = new KeyInputSender(input, ValidMapping(), diagnostics).Send(
            WindowsKeyboardKey.Delete,
            (nint)10,
            targetProcessId: 42);

        Assert.True(diagnostics.TryReadNext(out DiagnosticEvent? started));
        Assert.True(diagnostics.TryReadNext(out DiagnosticEvent? failed));
        Assert.Equal(42, started!.TargetProcessId);
        Assert.Equal(2, started.RequestedCount);
        Assert.Equal(42, failed!.TargetProcessId);
        Assert.Equal(1, failed.CompletedCount);
        Assert.Equal(5, failed.ErrorCode);
        Assert.False(diagnostics.TryReadNext(out _));
    }

    private static void AssertKeyboard(
        NativeInput input,
        ushort virtualKey,
        ushort scanCode,
        uint flags)
    {
        Assert.Equal(1u, input.Type);
        Assert.Equal(virtualKey, input.Data.Keyboard.VirtualKey);
        Assert.Equal(scanCode, input.Data.Keyboard.ScanCode);
        Assert.Equal(flags, input.Data.Keyboard.Flags);
    }

    private static FakeMappingApi ValidMapping() => new()
    {
        ThreadId = 20,
        ProcessId = 30,
        KeyboardLayout = (nint)0x4090409,
        MappedScanCode = 0x1C,
    };

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class FakeInputApi : IInputNativeApi
    {
        public uint ReturnCount { get; init; }
        public int Error { get; init; }
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public NativeInput[] LastBatch { get; private set; } = [];
        public int LastError => Error;

        public uint SendInput(NativeInput[] inputs)
        {
            Calls++;
            LastBatch = inputs.ToArray();
            if (Failure is not null) throw Failure;
            return ReturnCount;
        }
    }

    private sealed record FakeMappingApi : IKeyMappingNativeApi
    {
        public uint ThreadId { get; init; }
        public uint ProcessId { get; init; }
        public nint KeyboardLayout { get; init; }
        public uint MappedScanCode { get; init; }
        public Exception? Failure { get; init; }
        public nint RequestedHwnd { get; private set; }
        public uint RequestedLayoutThreadId { get; private set; }
        public uint RequestedVirtualKey { get; private set; }
        public uint RequestedMapType { get; private set; }
        public nint RequestedMapLayout { get; private set; }

        public uint GetWindowThreadProcessId(nint hwnd, out uint processId)
        {
            if (Failure is not null) throw Failure;
            RequestedHwnd = hwnd;
            processId = ProcessId;
            return ThreadId;
        }

        public nint GetKeyboardLayout(uint threadId)
        {
            RequestedLayoutThreadId = threadId;
            return KeyboardLayout;
        }

        public uint MapVirtualKeyEx(uint code, uint mapType, nint keyboardLayout)
        {
            RequestedVirtualKey = code;
            RequestedMapType = mapType;
            RequestedMapLayout = keyboardLayout;
            return MappedScanCode;
        }
    }
}
