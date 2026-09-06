using VirtualKeyboard.Core.Geometry;

namespace VirtualKeyboard.Core.Positioning;

public enum MonitorMetricsStatus
{
    Captured,
    InvalidAnchor,
    MonitorUnavailable,
    MonitorInfoUnavailable,
    NativeUnavailable,
}

public enum DpiSource
{
    TargetWindow,
    Monitor,
    Default96,
}

public sealed record MonitorMetrics(
    nint MonitorHandle,
    PhysicalPixelRect MonitorBounds,
    PhysicalPixelRect WorkArea,
    DpiScale DpiScale,
    DpiSource DpiSource);

public readonly record struct MonitorMetricsResult(MonitorMetricsStatus Status, MonitorMetrics? Metrics)
{
    public bool IsCaptured => Status == MonitorMetricsStatus.Captured && Metrics is not null;
    public static MonitorMetricsResult Success(MonitorMetrics metrics) => new(MonitorMetricsStatus.Captured, metrics);
    public static MonitorMetricsResult Failure(MonitorMetricsStatus status) => new(status, null);
}
