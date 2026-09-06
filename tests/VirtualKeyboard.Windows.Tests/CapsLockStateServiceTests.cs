using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Core.Layouts;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class CapsLockStateServiceTests
{
    [Fact]
    public void ReadUsesToggleBitOnly()
    {
        CapsLockStateService on = CreateService(new FakeStateApi(unchecked((short)0x8001)), out _, out _);
        CapsLockStateService off = CreateService(new FakeStateApi(unchecked((short)0x8000)), out _, out _);

        Assert.True(on.Read().IsOn);
        Assert.False(off.Read().IsOn);
    }

    [Fact]
    public void ToggleValidatesSessionAndSendsCapsLockToCapturedFocus()
    {
        var state = new FakeStateApi(1);
        CapsLockStateService service = CreateService(state, out TargetSession session, out SendProbe probe);

        CapsLockOperationResult result = service.Toggle(session.SessionId);

        Assert.True(result.IsSuccess);
        Assert.Equal(WindowsKeyboardKey.CapsLock, probe.Key);
        Assert.Equal((nint)101, probe.FocusHwnd);
        Assert.Equal(42, probe.ProcessId);
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public void ReplacedSessionFailsClosedWithoutSendingCapsLock()
    {
        CapsLockStateService service = CreateService(new FakeStateApi(0), out TargetSession session, out SendProbe probe);

        CapsLockOperationResult result = service.Toggle(session.SessionId + 1);

        Assert.Equal(CapsLockOperationStatus.Failed, result.Status);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public void SendFailureDoesNotClaimCapsLockChanged()
    {
        var state = new FakeStateApi(0);
        CapsLockStateService service = CreateService(state, out TargetSession session, out SendProbe probe);
        probe.Result = new(InputSendStatus.Failed, 2, 0, 5);

        CapsLockOperationResult result = service.Toggle(session.SessionId);

        Assert.Equal(CapsLockOperationStatus.Failed, result.Status);
        Assert.False(result.IsOn);
    }

    private static CapsLockStateService CreateService(
        FakeStateApi state,
        out TargetSession session,
        out SendProbe probe)
    {
        var sessions = new TargetSessionStore();
        TargetCaptureSnapshot snapshot = new(DateTimeOffset.UtcNow, 42, (nint)100, (nint)101);
        session = sessions.Replace(snapshot);
        var validator = new TargetSessionValidator(sessions, new StubCapture(snapshot));
        probe = new SendProbe();
        SendProbe capturedProbe = probe;
        var sender = new ValidatedKeyInputSender(
            validator,
            (key, hwnd, transition, processId) => capturedProbe.Send(key, hwnd, transition, processId));
        return new(sender, state);
    }

    private sealed class SendProbe
    {
        public InputSendResult Result { get; set; } = new(InputSendStatus.Succeeded, 2, 2, 0);
        public int Calls { get; private set; }
        public WindowsKeyboardKey Key { get; private set; }
        public nint FocusHwnd { get; private set; }
        public int ProcessId { get; private set; }

        public InputSendResult Send(WindowsKeyboardKey key, nint hwnd, KeyInputTransition transition, int processId)
        {
            Calls++;
            Key = key;
            FocusHwnd = hwnd;
            ProcessId = processId;
            Assert.Equal(KeyInputTransition.Press, transition);
            return Result;
        }
    }

    private sealed class FakeStateApi(short state) : ILockKeyStateNativeApi
    {
        public short GetKeyState(int virtualKey)
        {
            Assert.Equal(0x14, virtualKey);
            return state;
        }
    }

    private sealed class StubCapture(TargetCaptureSnapshot snapshot) : IForegroundTargetCapture
    {
        public TargetCaptureResult Capture() => TargetCaptureResult.Success(snapshot);
    }
}
