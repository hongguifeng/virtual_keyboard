using System.Runtime.InteropServices;
using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class SingleKeyInputSenderTests
{
    [Fact]
    public void SendASendsVirtualKeyDownAndUpOnce()
    {
        var api = new FakeInputApi { ReturnCount = 2 };
        var result = new SingleKeyInputSender(api).SendA();

        Assert.True(result.IsSuccess);
        Assert.Equal(InputSendStatus.Succeeded, result.Status);
        Assert.Equal(2, result.RequestedEvents);
        Assert.Equal(2, result.SentEvents);
        Assert.Equal(1, api.Calls);
        Assert.Equal(2, api.LastBatch.Length);
        Assert.All(api.LastBatch, input => Assert.Equal(1u, input.Type));
        Assert.Equal((ushort)0x41, api.LastBatch[0].Data.Keyboard.VirtualKey);
        Assert.Equal((ushort)0x41, api.LastBatch[1].Data.Keyboard.VirtualKey);
        Assert.Equal(0u, api.LastBatch[0].Data.Keyboard.Flags);
        Assert.Equal(0x0002u, api.LastBatch[1].Data.Keyboard.Flags);
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<NativeInput>());
    }

    [Theory]
    [InlineData(0, InputSendStatus.Failed)]
    [InlineData(1, InputSendStatus.PartialFailure)]
    public void SendAReportsShortNativeResultWithoutRetry(uint returnCount, InputSendStatus expectedStatus)
    {
        var api = new FakeInputApi { ReturnCount = returnCount, Error = 5 };
        var result = new SingleKeyInputSender(api).SendA();

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal((int)returnCount, result.SentEvents);
        Assert.Equal(5, result.ErrorCode);
        Assert.Equal(1, api.Calls);
    }

    [Fact]
    public void SendAFailureLogsOnlyStructuredCountsAndError()
    {
        using var diagnostics = new DiagnosticLogger();
        var api = new FakeInputApi { ReturnCount = 1, Error = 5 };

        _ = new SingleKeyInputSender(api, diagnostics).SendA(42);

        Assert.True(diagnostics.TryReadNext(out DiagnosticEvent? started));
        Assert.Equal(DiagnosticType.InputBatchStarted, started?.Type);
        Assert.Equal(42, started?.TargetProcessId);
        Assert.Equal(2, started?.RequestedCount);
        Assert.True(diagnostics.TryReadNext(out DiagnosticEvent? failed));
        Assert.Equal(DiagnosticType.InputBatchFailed, failed?.Type);
        Assert.Equal(42, failed?.TargetProcessId);
        Assert.Equal(2, failed?.RequestedCount);
        Assert.Equal(1, failed?.CompletedCount);
        Assert.Equal(5, failed?.ErrorCode);
        Assert.False(diagnostics.TryReadNext(out _));
    }

    private sealed class FakeInputApi : IInputNativeApi
    {
        public uint ReturnCount { get; init; }
        public int Error { get; init; }
        public int Calls { get; private set; }
        public NativeInput[] LastBatch { get; private set; } = [];
        public int LastError => Error;

        public uint SendInput(NativeInput[] inputs)
        {
            Calls++;
            LastBatch = inputs.ToArray();
            return ReturnCount;
        }
    }
}
