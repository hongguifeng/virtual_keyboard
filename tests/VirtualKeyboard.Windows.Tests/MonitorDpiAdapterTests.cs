using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Positioning;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class MonitorDpiAdapterTests
{
    [Fact]
    public void CapturesNegativeCoordinatesAndPrefersWindowDpi()
    {
        var api = ValidApi();
        api.WindowDpi = 144;
        api.MonitorDpiX = 120;
        api.MonitorDpiY = 120;

        MonitorMetricsResult result = new MonitorDpiAdapter(api).Capture(new(-1800, -100, 10, 20), (nint)99);

        Assert.True(result.IsCaptured);
        Assert.Equal(new PhysicalPixelRect(-1920, -200, 1920, 1080), result.Metrics!.MonitorBounds);
        Assert.Equal(new PhysicalPixelRect(-1920, -160, 1920, 1040), result.Metrics.WorkArea);
        Assert.Equal(1.5, result.Metrics.DpiScale.ScaleX);
        Assert.Equal(DpiSource.TargetWindow, result.Metrics.DpiSource);
        Assert.Equal(-1800, api.RequestedAnchor.Left);
    }

    [Fact]
    public void FallsBackToMonitorThenDefaultDpi()
    {
        var monitorApi = ValidApi();
        monitorApi.WindowDpi = 0;
        monitorApi.MonitorDpiX = 120;
        monitorApi.MonitorDpiY = 144;
        MonitorMetrics monitor = new MonitorDpiAdapter(monitorApi).Capture(new(1, 1, 1, 1), nint.Zero).Metrics!;
        Assert.Equal(DpiSource.Monitor, monitor.DpiSource);
        Assert.Equal(1.25, monitor.DpiScale.ScaleX);
        Assert.Equal(1.5, monitor.DpiScale.ScaleY);

        var defaultApi = ValidApi();
        defaultApi.WindowDpi = 0;
        defaultApi.MonitorDpiSucceeds = false;
        MonitorMetrics fallback = new MonitorDpiAdapter(defaultApi).Capture(new(1, 1, 1, 1), nint.Zero).Metrics!;
        Assert.Equal(DpiSource.Default96, fallback.DpiSource);
        Assert.Equal(1, fallback.DpiScale.ScaleX);
    }

    [Fact]
    public void MissingMonitorOrInfoReturnsExplicitStatus()
    {
        var missingMonitor = ValidApi();
        missingMonitor.Monitor = nint.Zero;
        Assert.Equal(MonitorMetricsStatus.MonitorUnavailable,
            new MonitorDpiAdapter(missingMonitor).Capture(new(1, 1, 1, 1), nint.Zero).Status);

        var missingInfo = ValidApi();
        missingInfo.MonitorInfoSucceeds = false;
        Assert.Equal(MonitorMetricsStatus.MonitorInfoUnavailable,
            new MonitorDpiAdapter(missingInfo).Capture(new(1, 1, 1, 1), nint.Zero).Status);
    }

    [Fact]
    public void InvalidAnchorAndNativeFailureDoNotEscape()
    {
        Assert.Equal(MonitorMetricsStatus.InvalidAnchor,
            new MonitorDpiAdapter(ValidApi()).Capture(default, nint.Zero).Status);
        var failed = ValidApi();
        failed.Failure = new DllNotFoundException("synthetic");
        Assert.Equal(MonitorMetricsStatus.NativeUnavailable,
            new MonitorDpiAdapter(failed).Capture(new(1, 1, 1, 1), nint.Zero).Status);
    }

    private static FakeApi ValidApi() => new()
    {
        Monitor = (nint)7,
        MonitorInfo = new(
            new NativeMonitorRect { Left = -1920, Top = -200, Right = 0, Bottom = 880 },
            new NativeMonitorRect { Left = -1920, Top = -160, Right = 0, Bottom = 880 }),
        MonitorInfoSucceeds = true,
        MonitorDpiSucceeds = true,
        MonitorDpiX = 96,
        MonitorDpiY = 96,
    };

    private sealed class FakeApi : IMonitorNativeApi
    {
        public nint Monitor { get; set; }
        public NativeMonitorSnapshot MonitorInfo { get; set; }
        public bool MonitorInfoSucceeds { get; set; }
        public uint WindowDpi { get; set; }
        public bool MonitorDpiSucceeds { get; set; }
        public uint MonitorDpiX { get; set; }
        public uint MonitorDpiY { get; set; }
        public Exception? Failure { get; set; }
        public NativeMonitorRect RequestedAnchor { get; private set; }

        public nint MonitorFromRect(ref NativeMonitorRect rectangle)
        {
            if (Failure is not null) throw Failure;
            RequestedAnchor = rectangle;
            return Monitor;
        }

        public bool TryGetMonitorInfo(nint monitor, out NativeMonitorSnapshot snapshot)
        {
            _ = monitor;
            snapshot = MonitorInfo;
            return MonitorInfoSucceeds;
        }

        public uint GetDpiForWindow(nint window) { _ = window; return WindowDpi; }

        public bool TryGetDpiForMonitor(nint monitor, out uint dpiX, out uint dpiY)
        {
            _ = monitor;
            dpiX = MonitorDpiX;
            dpiY = MonitorDpiY;
            return MonitorDpiSucceeds;
        }
    }
}
