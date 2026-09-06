using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class NativeForegroundTargetCaptureTests
{
    [Fact]
    public void CaptureReturnsForegroundProcessAndFocus()
    {
        var api = new FakeApi
        {
            Foreground = (nint)100,
            ThreadId = 12,
            ProcessId = 345,
            Focus = (nint)101,
        };
        var capture = new NativeForegroundTargetCapture(api, static () => new DateTimeOffset(2026, 9, 6, 1, 2, 3, TimeSpan.Zero), 999);

        TargetCaptureResult result = capture.Capture();

        Assert.True(result.IsCaptured);
        Assert.Equal(TargetCaptureStatus.Captured, result.Status);
        Assert.Equal(new TargetCaptureSnapshot(
            new DateTimeOffset(2026, 9, 6, 1, 2, 3, TimeSpan.Zero), 345, (nint)100, (nint)101), result.Snapshot);
        Assert.Equal(12u, api.RequestedThreadId);
    }

    [Fact]
    public void CaptureReportsMissingForeground()
    {
        var result = new NativeForegroundTargetCapture(new FakeApi(), static () => DateTimeOffset.UtcNow, 999).Capture();
        Assert.False(result.IsCaptured);
        Assert.Equal(TargetCaptureStatus.NoForegroundWindow, result.Status);
    }

    [Fact]
    public void CaptureReportsProcessLookupFailure()
    {
        var api = new FakeApi { Foreground = (nint)100, ThreadId = 0, ProcessId = 345 };
        var result = new NativeForegroundTargetCapture(api, static () => DateTimeOffset.UtcNow, 999).Capture();
        Assert.Equal(TargetCaptureStatus.ProcessLookupFailed, result.Status);
    }

    [Fact]
    public void CaptureReportsMissingFocus()
    {
        var api = new FakeApi { Foreground = (nint)100, ThreadId = 12, ProcessId = 345, Focus = nint.Zero };
        var result = new NativeForegroundTargetCapture(api, static () => DateTimeOffset.UtcNow, 999).Capture();
        Assert.Equal(TargetCaptureStatus.FocusWindowUnavailable, result.Status);
    }

    [Fact]
    public void CaptureDoesNotCaptureOwnProcess()
    {
        var api = new FakeApi { Foreground = (nint)100, ThreadId = 12, ProcessId = 999, Focus = (nint)101 };
        var result = new NativeForegroundTargetCapture(api, static () => DateTimeOffset.UtcNow, 999).Capture();
        Assert.Equal(TargetCaptureStatus.OwnProcess, result.Status);
    }

    private sealed class FakeApi : IForegroundWindowApi
    {
        public nint Foreground { get; init; }
        public uint ThreadId { get; init; }
        public uint ProcessId { get; init; }
        public nint Focus { get; init; }
        public uint RequestedThreadId { get; private set; }

        public nint GetForegroundWindow() => Foreground;

        public uint GetWindowThreadProcessId(nint windowHandle, out uint processId)
        {
            _ = windowHandle;
            processId = ProcessId;
            return ThreadId;
        }

        public bool TryGetFocusWindow(uint threadId, out nint focusWindowHandle)
        {
            RequestedThreadId = threadId;
            focusWindowHandle = Focus;
            return Focus != nint.Zero;
        }
    }
}
