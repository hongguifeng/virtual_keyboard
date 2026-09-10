using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class TargetSessionValidatorTests
{
    [Fact]
    public void UnhealthyObserverRejectsPreviouslyValidTargetWithoutSending()
    {
        var store = new TargetSessionStore();
        var capture = new StubCapture(Snapshot(42, 100, 101));
        var session = store.Replace(capture.Snapshot);
        var validator = new TargetSessionValidator(store, capture, observationHealthy: () => false);
        Assert.Equal(TargetValidationStatus.FocusIdentityStale, validator.Validate(session.SessionId).Status);
    }
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

    [Fact]
    public void MatchingRuntimeIdentityAllowsInput()
    {
        var sessions = new TargetSessionStore();
        var identities = new LatestFocusSnapshotStore();
        FocusSnapshot focus = Focus(5, [1, 2]);
        TargetSession session = sessions.Replace(focus, (nint)101, null);
        identities.Publish(focus);
        var validator = new TargetSessionValidator(sessions, new StubCapture(Snapshot(42, 100, 101)), identities);

        Assert.True(validator.Validate(session.SessionId).IsValid);
    }

    [Fact]
    public void RebuiltElementCancelsActionAndRequestsReclassification()
    {
        var sessions = new TargetSessionStore();
        var identities = new LatestFocusSnapshotStore();
        FocusSnapshot original = Focus(5, [1, 2]);
        TargetSession session = sessions.Replace(original, (nint)101, null);
        identities.Publish(Focus(6, [9, 9]));
        var validator = new TargetSessionValidator(sessions, new StubCapture(Snapshot(42, 100, 101)), identities);

        TargetValidationResult result = validator.Validate(session.SessionId);

        Assert.False(result.IsValid);
        Assert.True(result.RequiresReclassification);
        Assert.Equal(TargetValidationStatus.IdentityChangedRequiresReclassification, result.Status);
    }

    [Fact]
    public void ValidatedSenderSignalsReclassificationWithoutSendingInput()
    {
        var sessions = new TargetSessionStore();
        var identities = new LatestFocusSnapshotStore();
        FocusSnapshot original = Focus(5, [1]);
        TargetSession session = sessions.Replace(original, (nint)101, null);
        identities.Publish(Focus(6, [2]));
        var validator = new TargetSessionValidator(sessions, new StubCapture(Snapshot(42, 100, 101)), identities);
        int sends = 0;
        int reclassifications = 0;
        var sender = new ValidatedSingleKeyInputSender(
            validator,
            _ => { sends++; return new InputSendResult(InputSendStatus.Succeeded, 2, 2, 0); },
            requestReclassification: () => reclassifications++);

        InputSendResult result = sender.SendA(session.SessionId);

        Assert.Equal(InputSendStatus.TargetInvalid, result.Status);
        Assert.Equal(0, sends);
        Assert.Equal(1, reclassifications);
    }

    [Theory]
    [InlineData(false, TargetValidationStatus.FocusIdentityUnavailable)]
    [InlineData(true, TargetValidationStatus.FocusIdentityStale)]
    public void MissingOrStaleIdentityFailsClosed(bool publishStale, TargetValidationStatus expected)
    {
        var sessions = new TargetSessionStore();
        var identities = new LatestFocusSnapshotStore();
        TargetSession session = sessions.Replace(Focus(5, [1]), (nint)101, null);
        if (publishStale) identities.Publish(Focus(4, [1]));
        var validator = new TargetSessionValidator(sessions, new StubCapture(Snapshot(42, 100, 101)), identities);
        Assert.Equal(expected, validator.Validate(session.SessionId).Status);
    }

    [Fact]
    public void SameRuntimeIdentityWithoutEditableFocusRequiresReclassification()
    {
        var sessions = new TargetSessionStore();
        var identities = new LatestFocusSnapshotStore();
        TargetSession session = sessions.Replace(Focus(5, [1]), (nint)101, null);
        identities.Publish(Focus(6, [1]) with { HasKeyboardFocus = false });
        var validator = new TargetSessionValidator(sessions, new StubCapture(Snapshot(42, 100, 101)), identities);
        Assert.True(validator.Validate(session.SessionId).RequiresReclassification);
    }

    private static TargetCaptureSnapshot Snapshot(int processId, int topLevel, int focus) =>
        new(DateTimeOffset.UtcNow, processId, (nint)topLevel, (nint)focus);

    private static FocusSnapshot Focus(long version, int[] runtimeId) =>
        new(version, DateTimeOffset.UtcNow, 42, (nint)100, new RuntimeIdentity(runtimeId), FocusControlType.Edit, true, true, false, false);

    private sealed class StubCapture(TargetCaptureSnapshot initial) : IForegroundTargetCapture
    {
        public TargetCaptureSnapshot Snapshot { get; set; } = initial;

        public TargetCaptureResult Capture() => TargetCaptureResult.Success(Snapshot);
    }
}
