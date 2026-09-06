using System.Windows;
using System.Windows.Input;
using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.App;

/// <summary>Minimal non-activating keyboard window with explicit target capture.</summary>
public partial class MainWindow : Window, IDisposable
{
    private static readonly DipSize ConfiguredOverlaySize = new(360, 176);
    private readonly OverlayWindowAdapter _overlay;
    private readonly IForegroundTargetCapture _targetCapture;
    private readonly TargetSessionStore _targetSessions;
    private readonly DiagnosticLogger _diagnostics;
    private readonly ValidatedSingleKeyInputSender _validatedInput;
    private bool _disposed;

    public MainWindow()
        : this(new NativeForegroundTargetCapture(), new TargetSessionStore())
    {
    }

    internal MainWindow(IForegroundTargetCapture targetCapture, TargetSessionStore targetSessions)
    {
        InitializeComponent();
        _overlay = new OverlayWindowAdapter(this);
        _overlay.DpiChanged += OnOverlayDpiChanged;
        _targetCapture = targetCapture ?? throw new ArgumentNullException(nameof(targetCapture));
        _targetSessions = targetSessions ?? throw new ArgumentNullException(nameof(targetSessions));
        _diagnostics = new DiagnosticLogger();
        var validator = new TargetSessionValidator(_targetSessions, _targetCapture);
        _validatedInput = new ValidatedSingleKeyInputSender(validator, new SingleKeyInputSender(_diagnostics), _diagnostics);
    }

    internal TargetSession? CurrentTargetSession => _targetSessions.Current;

    internal bool IsDisposed => _disposed;

    internal nint OverlayHandle => _overlay.Handle;

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
        TargetSession? session = _targetSessions.Current;
        if (session is null)
        {
            SessionStatusText.Text = "请先捕获目标";
            return;
        }

        InputSendResult result = _validatedInput.SendA(session.SessionId);
        SessionStatusText.Text = result.IsSuccess
            ? $"会话 {session.SessionId} · A 已发送"
            : $"输入已取消：{result.Status}";
    }

    private void OnOverlayDpiChanged(OverlayDpiChangedNotification change)
    {
        if (_targetSessions.Current is null)
        {
            return;
        }

        PhysicalPixelSize size = change.DpiScale.ToPhysicalPixels(ConfiguredOverlaySize);
        _overlay.Move(
            checked((int)Math.Round(change.SuggestedRectangle.X)),
            checked((int)Math.Round(change.SuggestedRectangle.Y)),
            checked((int)Math.Round(size.Width)),
            checked((int)Math.Round(size.Height)));
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

        _overlay.DpiChanged -= OnOverlayDpiChanged;
        _overlay.Dispose();
        _diagnostics.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
