using System.Windows;
using System.Windows.Automation;
using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Positioning;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.App;

/// <summary>A compact, non-activating entry point that never sends input itself.</summary>
public partial class KeyboardLauncherWindow : Window, IDisposable
{
    internal const double ButtonSizeDip = 40;
    private bool _disposed;

    public KeyboardLauncherWindow()
    {
        InitializeComponent();
        Overlay = new OverlayWindowAdapter(this);
        Overlay.DpiChanged += OnDpiChanged;
    }

    internal OverlayWindowAdapter Overlay { get; }
    internal event Action? ExpandRequested;

    internal void SetLanguage(UiLanguage language)
    {
        string label = AppStrings.For(language).OpenKeyboard;
        ExpandButton.ToolTip = label;
        AutomationProperties.SetName(ExpandButton, label);
    }

    private void OnExpandClick(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke();

    private void OnDpiChanged(OverlayDpiChangedNotification change)
    {
        PhysicalPixelSize size = change.DpiScale.ToPhysicalPixels(new(ButtonSizeDip, ButtonSizeDip));
        MonitorMetricsResult monitor = new MonitorDpiAdapter().Capture(change.SuggestedRectangle, Overlay.Handle);
        if (!monitor.IsCaptured) { Overlay.Hide(); return; }
        PhysicalPixelRect workArea = monitor.Metrics!.WorkArea;
        double width = Math.Min(size.Width, workArea.Width);
        double height = Math.Min(size.Height, workArea.Height);
        double x = Math.Clamp(change.SuggestedRectangle.X, workArea.X, workArea.Right - width);
        double y = Math.Clamp(change.SuggestedRectangle.Y, workArea.Y, workArea.Bottom - height);
        Overlay.Move(checked((int)Math.Round(x)), checked((int)Math.Round(y)),
            checked((int)Math.Round(width)), checked((int)Math.Round(height)));
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Overlay.DpiChanged -= OnDpiChanged;
        Overlay.Dispose();
        Close();
        GC.SuppressFinalize(this);
    }
}
