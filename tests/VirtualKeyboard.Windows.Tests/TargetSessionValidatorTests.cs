using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class TargetSessionValidatorTests
{
    [Fact]
    public void MatchingSessionAllowsInput()
    {
        var store = new TargetSessionStore();
        var capture = new StubCapture(Snapshot(42, 100, 101));
        var session = store.Replace(capture.Snapshot);
        var validator = new TargetSessionValidator(store, capture);
        var sendCount = 0;
        var sender = new ValidatedSingleKeyInputSender(validator, _ =>
        {
            sendCount++;
            return new InputSendResult(InputSendStatus.Succeeded, 2, 2, 0);
        });

        var result = sender.SendA(session.SessionId);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, sendCount);
    }

    [Theory]
    [InlineData(TargetValidationStatus.ForegroundChanged)]
    [InlineData(TargetValidationStatus.ProcessChanged)]
    [InlineData(TargetValidationStatus.FocusChanged)]
    public void ChangedTargetCancelsWithoutCallingInput(TargetValidationStatus expected)
    {
        var store = new TargetSessionStore();
        var capture = new StubCapture(Snapshot(42, 100, 101));
        var session = store.Replace(capture.Snapshot);
        capture.Snapshot = expected switch
        {
            TargetValidationStatus.ForegroundChanged => Snapshot(42, 200, 201),
            TargetValidationStatus.ProcessChanged => Snapshot(43, 100, 101),
            _ => Snapshot(42, 100, 202),
        };
        var validator = new TargetSessionValidator(store, capture);
        var sendCount = 0;
        var sender = new ValidatedSingleKeyInputSender(validator, _ =>
        {
            sendCount++;
            return new InputSendResult(InputSendStatus.Succeeded, 2, 2, 0);
        });

        var result = sender.SendA(session.SessionId);

        Assert.Equal(InputSendStatus.TargetInvalid, result.Status);
        Assert.Equal(0, sendCount);
    }

    [Fact]
    public void ReplacedSessionCancelsWithoutCallingInput()
    {
        var store = new TargetSessionStore();
        var capture = new StubCapture(Snapshot(42, 100, 101));
        var oldSession = store.Replace(capture.Snapshot);
        _ = store.Replace(Snapshot(42, 100, 102));
        var validator = new TargetSessionValidator(store, capture);
        var sendCount = 0;
        var sender = new ValidatedSingleKeyInputSender(validator, _ =>
        {
            sendCount++;
            return new InputSendResult(InputSendStatus.Succeeded, 2, 2, 0);
        });

        var result = sender.SendA(oldSession.SessionId);

        Assert.Equal(InputSendStatus.TargetInvalid, result.Status);
        Assert.Equal(0, sendCount);
    }

    private static TargetCaptureSnapshot Snapshot(int processId, int topLevel, int focus) =>
        new(DateTimeOffset.UtcNow, processId, (nint)topLevel, (nint)focus);

    private sealed class StubCapture(TargetCaptureSnapshot initial) : IForegroundTargetCapture
    {
        public TargetCaptureSnapshot Snapshot { get; set; } = initial;

        public TargetCaptureResult Capture() => TargetCaptureResult.Success(Snapshot);
    }
}
