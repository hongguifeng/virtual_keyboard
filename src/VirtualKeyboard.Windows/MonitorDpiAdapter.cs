using System.Runtime.InteropServices;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Positioning;

namespace VirtualKeyboard.Windows;

public sealed class MonitorDpiAdapter
{
    private const uint DefaultDpi = 96;
    private readonly IMonitorNativeApi _api;

    public MonitorDpiAdapter() : this(new SystemMonitorNativeApi()) { }
    internal MonitorDpiAdapter(IMonitorNativeApi api) => _api = api ?? throw new ArgumentNullException(nameof(api));

    public MonitorMetricsResult Capture(PhysicalPixelRect anchor, nint targetWindow)
    {
        if (!anchor.IsValid) return MonitorMetricsResult.Failure(MonitorMetricsStatus.InvalidAnchor);
        try
        {
            NativeMonitorRect nativeAnchor = NativeMonitorRect.From(anchor);
            nint monitor = _api.MonitorFromRect(ref nativeAnchor);
            if (monitor == nint.Zero) return MonitorMetricsResult.Failure(MonitorMetricsStatus.MonitorUnavailable);
            if (!_api.TryGetMonitorInfo(monitor, out NativeMonitorSnapshot info))
                return MonitorMetricsResult.Failure(MonitorMetricsStatus.MonitorInfoUnavailable);

            uint dpiX;
            uint dpiY;
            DpiSource source;
            uint windowDpi = targetWindow == nint.Zero ? 0 : _api.GetDpiForWindow(targetWindow);
            if (windowDpi > 0)
            {
                dpiX = dpiY = windowDpi;
                source = DpiSource.TargetWindow;
            }
            else if (_api.TryGetDpiForMonitor(monitor, out dpiX, out dpiY) && dpiX > 0 && dpiY > 0)
            {
                source = DpiSource.Monitor;
            }
            else
            {
                dpiX = dpiY = DefaultDpi;
                source = DpiSource.Default96;
            }

            var bounds = info.MonitorBounds.ToPhysicalRect();
            var workArea = info.WorkArea.ToPhysicalRect();
            if (!bounds.IsValid || !bounds.Size.IsPositive || !workArea.IsValid || !workArea.Size.IsPositive)
                return MonitorMetricsResult.Failure(MonitorMetricsStatus.MonitorInfoUnavailable);
            return MonitorMetricsResult.Success(new(monitor, bounds, workArea, DpiScale.FromDpi(dpiX, dpiY), source));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return MonitorMetricsResult.Failure(MonitorMetricsStatus.NativeUnavailable);
        }
        catch (OverflowException)
        {
            return MonitorMetricsResult.Failure(MonitorMetricsStatus.InvalidAnchor);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMonitorRect
{
    public int Left, Top, Right, Bottom;

    public static NativeMonitorRect From(PhysicalPixelRect rectangle) => new()
    {
        Left = checked((int)Math.Floor(rectangle.X)),
        Top = checked((int)Math.Floor(rectangle.Y)),
        Right = checked((int)Math.Ceiling(rectangle.Right)),
        Bottom = checked((int)Math.Ceiling(rectangle.Bottom)),
    };

    public readonly PhysicalPixelRect ToPhysicalRect() => new(Left, Top, Right - Left, Bottom - Top);
}

internal readonly record struct NativeMonitorSnapshot(NativeMonitorRect MonitorBounds, NativeMonitorRect WorkArea);

internal interface IMonitorNativeApi
{
    nint MonitorFromRect(ref NativeMonitorRect rectangle);
    bool TryGetMonitorInfo(nint monitor, out NativeMonitorSnapshot snapshot);
    uint GetDpiForWindow(nint window);
    bool TryGetDpiForMonitor(nint monitor, out uint dpiX, out uint dpiY);
}

internal sealed class SystemMonitorNativeApi : IMonitorNativeApi
{
    private const uint MonitorDefaultToNearest = 2;

    public nint MonitorFromRect(ref NativeMonitorRect rectangle) => MonitorFromRectNative(ref rectangle, MonitorDefaultToNearest);
    public uint GetDpiForWindow(nint window) => GetDpiForWindowNative(window);

    public bool TryGetMonitorInfo(nint monitor, out NativeMonitorSnapshot snapshot)
    {
        var info = new MonitorInfo { Size = checked((uint)Marshal.SizeOf<MonitorInfo>()) };
        bool success = GetMonitorInfo(monitor, ref info);
        snapshot = success ? new(info.Monitor, info.WorkArea) : default;
        return success;
    }

    public bool TryGetDpiForMonitor(nint monitor, out uint dpiX, out uint dpiY)
    {
        try { return GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) == 0; }
        catch (DllNotFoundException) { dpiX = dpiY = 0; return false; }
        catch (EntryPointNotFoundException) { dpiX = dpiY = 0; return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeMonitorRect Monitor;
        public NativeMonitorRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll", EntryPoint = "MonitorFromRect")]
    private static extern nint MonitorFromRectNative(ref NativeMonitorRect rectangle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static extern uint GetDpiForWindowNative(nint window);
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
