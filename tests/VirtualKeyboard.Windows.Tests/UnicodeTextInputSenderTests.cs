using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class UnicodeTextInputSenderTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("中")]
    [InlineData("é")]
    [InlineData("😀")]
    [InlineData("A中é😀")]
    public void BuilderPreservesEveryUtf16CodeUnitInDownUpOrder(string text)
    {
        NativeInput[] inputs = UnicodeTextInputBuilder.Build(text);
        Assert.Equal(text.Length * 2, inputs.Length);
        for (int index = 0; index < text.Length; index++)
        {
            NativeKeyboardInput down = inputs[index * 2].Data.Keyboard;
            NativeKeyboardInput up = inputs[index * 2 + 1].Data.Keyboard;
            Assert.Equal(1u, inputs[index * 2].Type);
            Assert.Equal((ushort)0, down.VirtualKey);
            Assert.Equal((ushort)text[index], down.ScanCode);
            Assert.Equal(0x0004u, down.Flags);
            Assert.Equal((ushort)text[index], up.ScanCode);
            Assert.Equal(0x0006u, up.Flags);
        }
    }

    [Fact]
    public void SenderSubmitsOneBatchAndChecksShortReturnWithoutRetry()
    {
        var api = new FakeApi { ReturnCount = 3, Error = 5 };
        InputSendResult result = new UnicodeTextInputSender(api).Send("ab");
        Assert.Equal(InputSendStatus.PartialFailure, result.Status);
        Assert.Equal(4, result.RequestedEvents);
        Assert.Equal(3, result.SentEvents);
        Assert.Equal(5, result.ErrorCode);
        Assert.Equal(1, api.Calls);
    }

    [Fact]
    public void EmptyTextSucceedsWithoutCallingNativeApi()
    {
        var api = new FakeApi();
        InputSendResult result = new UnicodeTextInputSender(api).Send(string.Empty);
        Assert.True(result.IsSuccess);
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public void NullAndOversizedTextFailBeforeNativeCall()
    {
        var api = new FakeApi();
        var sender = new UnicodeTextInputSender(api);
        Assert.Equal(InputSendStatus.InvalidInput, sender.Send(null!).Status);
        Assert.Equal(InputSendStatus.InvalidInput, sender.Send(new string('x', UnicodeTextInputBuilder.MaximumCodeUnits + 1)).Status);
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public void DiagnosticsContainCountsButNotText()
    {
        using var diagnostics = new DiagnosticLogger();
        var api = new FakeApi { ReturnCount = 4 };
        _ = new UnicodeTextInputSender(api, diagnostics).Send("密碼", 42);
        Assert.True(diagnostics.TryReadNext(out DiagnosticEvent? started));
        Assert.Equal(4, started!.RequestedCount);
        string json = DiagnosticSerializer.ToJsonLine(started);
        Assert.DoesNotContain("密碼", json, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeUnavailableReturnsControlledFailureWithoutRetry()
    {
        var api = new FakeApi { Failure = new DllNotFoundException("synthetic") };
        InputSendResult result = new UnicodeTextInputSender(api).Send("A");
        Assert.Equal(InputSendStatus.NativeUnavailable, result.Status);
        Assert.Equal(1, api.Calls);
    }

    private sealed class FakeApi : IInputNativeApi
    {
        public uint ReturnCount { get; init; }
        public int Error { get; init; }
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }
        public int LastError => Error;
        public uint SendInput(NativeInput[] inputs)
        {
            _ = inputs;
            Calls++;
            if (Failure is not null) throw Failure;
            return ReturnCount;
        }
    }
}
