using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class NativeFocusAdapterTests
{
    [Fact]
    public void CaptureReturnsFocusCaretAndKeyboardLayout()
    {
        var api = new FakeApi
        {
            Foreground = (nint)10,
            ThreadId = 20,
            ProcessId = 30,
            Gui = new((nint)11, (nint)12, 1, 2, 4, 8),
            KeyboardLayout = (nint)0x4090409,
        };

        NativeFocusResult result = new NativeFocusAdapter(api).Capture();

        Assert.True(result.IsCaptured);
        Assert.Equal(30, result.Snapshot!.ProcessId);
        Assert.Equal((nint)11, result.Snapshot.FocusHwnd);
        Assert.Equal((nint)0x4090409, result.Snapshot.KeyboardLayout);
        Assert.Equal(new PhysicalPixelRect(101, 202, 3, 6), result.Snapshot.CaretRectangle);
    }

    [Fact]
    public void InvalidCaretIsOmittedWithoutLosingFocusIdentity()
    {
        var api = ValidApi() with { Gui = new((nint)11, (nint)12, 0, 0, 0, 0) };
        NativeFocusResult result = new NativeFocusAdapter(api).Capture();
        Assert.True(result.IsCaptured);
        Assert.Null(result.Snapshot!.CaretRectangle);
    }

    [Fact]
    public void ClientConversionFailureHasExplicitStatus()
    {
        var api = ValidApi() with { ClientToScreenSucceeds = false };
        Assert.Equal(NativeFocusStatus.CoordinateConversionFailed, new NativeFocusAdapter(api).Capture().Status);
    }

    [Fact]
    public void NativeLoadingFailureDoesNotEscape()
    {
        var api = ValidApi() with { Failure = new DllNotFoundException("synthetic") };
        Assert.Equal(NativeFocusStatus.NativeUnavailable, new NativeFocusAdapter(api).Capture().Status);
    }

    private static FakeApi ValidApi() => new()
    {
        Foreground = (nint)10,
        ThreadId = 20,
        ProcessId = 30,
        Gui = new((nint)11, (nint)12, 1, 2, 4, 8),
    };

    private sealed record FakeApi : INativeFocusApi
    {
        public nint Foreground { get; init; }
        public uint ThreadId { get; init; }
        public uint ProcessId { get; init; }
        public NativeGuiThreadSnapshot Gui { get; init; }
        public nint KeyboardLayout { get; init; }
        public bool ClientToScreenSucceeds { get; init; } = true;
        public Exception? Failure { get; init; }

        public nint GetForegroundWindow()
        {
            if (Failure is not null) throw Failure;
            return Foreground;
        }

        public uint GetWindowThreadProcessId(nint hwnd, out uint processId)
        {
            _ = hwnd;
            processId = ProcessId;
            return ThreadId;
        }

        public bool TryGetGuiThreadInfo(uint threadId, out NativeGuiThreadSnapshot snapshot)
        {
            _ = threadId;
            snapshot = Gui;
            return Gui.FocusHwnd != nint.Zero;
        }

        public bool TryClientToScreen(nint hwnd, ref NativePoint point)
        {
            _ = hwnd;
            if (!ClientToScreenSucceeds) return false;
            point.X += 100;
            point.Y += 200;
            return true;
        }

        public nint GetKeyboardLayout(uint threadId)
        {
            _ = threadId;
            return KeyboardLayout;
        }
    }
}
