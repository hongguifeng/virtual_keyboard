using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Layouts;
using VirtualKeyboard.Core.Positioning;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.App;

/// <summary>A non-activating button row; input requests carry the displayed target session.</summary>
public partial class KeyboardLauncherWindow : Window, IDisposable
{
    internal const double ButtonSizeDip = 40;
    private const double ButtonGapDip = 4;
    private readonly DispatcherTimer _feedbackTimer;
    private readonly ToolTip _feedback = new() { Focusable = false, Placement = PlacementMode.Bottom, StaysOpen = true };
    private long _presentationVersion;
    private bool _disposed;

    public KeyboardLauncherWindow()
    {
        InitializeComponent();
        Overlay = new OverlayWindowAdapter(this);
        Overlay.DpiChanged += OnDpiChanged;
        _feedback.PlacementTarget = ExpandButton;
        _feedbackTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(4) };
        _feedbackTimer.Tick += OnFeedbackTimeout;
        IsVisibleChanged += OnVisibilityChanged;
    }

    internal OverlayWindowAdapter Overlay { get; }
    internal event Action? ExpandRequested;
    internal event EventHandler<LauncherKeyInvokedEventArgs>? CustomKeyInvoked;
    internal bool IsFeedbackVisible => _feedback.IsOpen;
    internal string FeedbackText => _feedback.Content as string ?? string.Empty;

    internal void LoadKeys(IReadOnlyList<CustomKeyConfiguration> keys, bool passwordTarget, long sessionId)
    {
        ClearPresentation();
        ButtonRow.Children.RemoveRange(1, ButtonRow.Children.Count - 1);
        if (passwordTarget) return;
        long version = _presentationVersion;
        for (int index = 0; index < Math.Min(keys.Count, ConfigurationSchemaLimits.MaximumCustomKeys); index++)
        {
            CustomKeyConfiguration configured = keys[index];
            var key = new KeyViewModel($"key.launcher.{index}", configured.Label, 1, false, configured.ToLayoutAction());
            var button = new NonFocusableKeyButton(key)
            {
                Style = (Style)FindResource("LauncherButtonStyle"),
                Margin = new Thickness(ButtonGapDip, 0, 0, 0),
                ToolTip = configured.Label,
                Content = new TextBlock
                {
                    Text = configured.Label, FontSize = 12, MaxWidth = ButtonSizeDip - 8,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            AutomationProperties.SetName(button, configured.Label);
            AutomationProperties.SetAutomationId(button, $"LauncherCustomKey{index}");
            button.Invoked += (_, args) =>
            {
                if (!_disposed && IsVisible && version == _presentationVersion)
                    CustomKeyInvoked?.Invoke(this, new(sessionId, key, args.IsRepeat));
            };
            ButtonRow.Children.Add(button);
        }
    }

    internal void ClearPresentation()
    {
        _presentationVersion++;
        foreach (NonFocusableKeyButton button in ButtonRow.Children.OfType<NonFocusableKeyButton>())
        {
            button.CancelGesture();
            button.ReleaseMouseCapture();
            button.ReleaseAllTouchCaptures();
        }
        ClearFeedback();
    }

    internal void ShowFeedback(string message)
    {
        ClearFeedback();
        if (_disposed || !IsVisible || string.IsNullOrEmpty(message)) return;
        _feedback.Content = message;
        _feedback.IsOpen = true;
        _feedbackTimer.Start();
    }

    private void ClearFeedback()
    {
        _feedbackTimer.Stop();
        _feedback.IsOpen = false;
        _feedback.Content = null;
    }

    private void OnFeedbackTimeout(object? sender, EventArgs e) => ClearFeedback();

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false) ClearPresentation();
    }

    internal static PhysicalPixelSize GetPhysicalSize(int customKeyCount, VirtualKeyboard.Core.Geometry.DpiScale dpi, PhysicalPixelRect workArea)
    {
        PhysicalPixelSize desired = dpi.ToPhysicalPixels(new(ButtonSizeDip + customKeyCount * (ButtonSizeDip + ButtonGapDip), ButtonSizeDip));
        double scale = Math.Min(1, Math.Min(workArea.Width * 0.95 / desired.Width, workArea.Height * 0.95 / desired.Height));
        return new(desired.Width * scale, desired.Height * scale);
    }

    internal void SetLanguage(UiLanguage language)
    {
        string label = AppStrings.For(language).OpenKeyboard;
        ExpandButton.ToolTip = label;
        AutomationProperties.SetName(ExpandButton, label);
    }

    private void OnExpandClick(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke();

    private void OnDpiChanged(OverlayDpiChangedNotification change)
    {
        MonitorMetricsResult monitor = new MonitorDpiAdapter().Capture(change.SuggestedRectangle, Overlay.Handle);
        if (!monitor.IsCaptured) { Overlay.Hide(); return; }
        PhysicalPixelRect workArea = monitor.Metrics!.WorkArea;
        PhysicalPixelSize size = GetPhysicalSize(ButtonRow.Children.Count - 1, change.DpiScale, workArea);
        double width = size.Width;
        double height = size.Height;
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
        IsVisibleChanged -= OnVisibilityChanged;
        ClearPresentation();
        _feedbackTimer.Tick -= OnFeedbackTimeout;
        Overlay.DpiChanged -= OnDpiChanged;
        Overlay.Dispose();
        Close();
        GC.SuppressFinalize(this);
    }
}

internal sealed class LauncherKeyInvokedEventArgs(long sessionId, KeyViewModel key, bool isRepeat) : EventArgs
{
    internal long SessionId { get; } = sessionId;
    internal KeyInvokedEventArgs Invocation { get; } = new(key, isRepeat);
}
