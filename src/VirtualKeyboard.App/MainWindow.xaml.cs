using System.Windows;
using System.Windows.Input;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.App;

/// <summary>Minimal non-activating keyboard window with explicit target capture.</summary>
public partial class MainWindow : Window, IDisposable
{
    private readonly OverlayWindowAdapter _overlay;
    private readonly IForegroundTargetCapture _targetCapture;
    private readonly TargetSessionStore _targetSessions;
    private bool _disposed;

    public MainWindow()
        : this(new NativeForegroundTargetCapture(), new TargetSessionStore())
    {
    }

    internal MainWindow(IForegroundTargetCapture targetCapture, TargetSessionStore targetSessions)
    {
        InitializeComponent();
        _overlay = new OverlayWindowAdapter(this);
        _targetCapture = targetCapture ?? throw new ArgumentNullException(nameof(targetCapture));
        _targetSessions = targetSessions ?? throw new ArgumentNullException(nameof(targetSessions));
    }

    internal TargetSession? CurrentTargetSession => _targetSessions.Current;

    internal void ShowAt(int x, int y, int width, int height) => _overlay.ShowAt(x, y, width, height);

    private void OnDragAreaMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _ = sender;
        _ = e;
        // DragMove 会激活 WPF 窗口；物理像素拖动在 T3.6 通过适配器实现。
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _overlay.Close();
    }

    private void OnCaptureTargetClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        TargetCaptureResult result = _targetCapture.Capture();
        if (!result.IsCaptured)
        {
            _targetSessions.Clear();
            SessionStatusText.Text = $"捕获失败：{result.Status}";
            return;
        }

        TargetSession session = _targetSessions.Replace(result.Snapshot!);
        SessionStatusText.Text = $"会话 {session.SessionId} · PID {session.ProcessId}\n前台 0x{session.TopLevelHwnd:X} · 焦点 0x{session.FocusHwnd:X}";
    }

    private void OnKeyAClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        // SendInput belongs to T1.3.
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _overlay.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
