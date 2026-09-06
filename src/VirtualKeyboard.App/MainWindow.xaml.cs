using System.Windows;
using System.Windows.Input;
using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Core.Layouts;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.App;

/// <summary>Minimal non-activating keyboard window with explicit target capture.</summary>
public partial class MainWindow : Window, IDisposable
{
    private static readonly DipSize ConfiguredOverlaySize = new(760, 340);
    private readonly OverlayWindowAdapter _overlay;
    private readonly IForegroundTargetCapture _targetCapture;
    private readonly TargetSessionStore _targetSessions;
    private readonly DiagnosticLogger _diagnostics;
    private readonly ValidatedSingleKeyInputSender _validatedInput;
    private readonly InputFailureFeedbackFactory _failureFeedback;
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
        _failureFeedback = new InputFailureFeedbackFactory(new ProcessIntegrityInspector(), _diagnostics);
        LoadBuiltInLayout();
    }

    internal TargetSession? CurrentTargetSession => _targetSessions.Current;

    internal bool IsDisposed => _disposed;

    internal nint OverlayHandle => _overlay.Handle;

    internal void ShowAt(int x, int y, int width, int height) => _overlay.ShowAt(x, y, width, height);

    internal bool BeginManualMoveForCurrentSession() =>
        _targetSessions.Current is TargetSession session && _overlay.BeginManualMove(session.SessionId);

    internal bool EndManualMoveForCurrentSession() =>
        _targetSessions.Current is TargetSession session && _overlay.EndManualMove(session.SessionId);

    internal bool HasManualPosition(long sessionId) => _overlay.TryGetManualPosition(sessionId, out _);

    private void OnDragAreaMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement area && BeginManualMoveForCurrentSession())
        {
            area.CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnDragAreaMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not UIElement area || !area.IsMouseCaptured) return;
        if (e.LeftButton != MouseButtonState.Pressed || _targetSessions.Current is not TargetSession session)
        {
            area.ReleaseMouseCapture();
            _overlay.InvalidateManualPosition();
            return;
        }

        _overlay.UpdateManualMove(session.SessionId);
        e.Handled = true;
    }

    private void OnDragAreaMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement area || !area.IsMouseCaptured) return;
        if (_targetSessions.Current is TargetSession session)
        {
            _overlay.UpdateManualMove(session.SessionId);
            EndManualMoveForCurrentSession();
        }

        area.ReleaseMouseCapture();
        e.Handled = true;
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
            _overlay.InvalidateManualPosition();
            _targetSessions.Clear();
            SessionStatusText.Text = $"捕获失败：{result.Status}";
            return;
        }

        _overlay.InvalidateManualPosition();
        TargetSession session = _targetSessions.Replace(result.Snapshot!);
        SessionStatusText.Text = $"会话 {session.SessionId} · PID {session.ProcessId}\n前台 0x{session.TopLevelHwnd:X} · 焦点 0x{session.FocusHwnd:X}";
    }

    private void OnLayoutKeyInvoked(object sender, KeyInvokedEventArgs e)
    {
        _ = sender;
        if (!string.Equals(e.Key.Id, "key.a", StringComparison.Ordinal))
        {
            SessionStatusText.Text = $"{e.Key.Label} 尚未接入输入分发";
            return;
        }
        TargetSession? session = _targetSessions.Current;
        if (session is null)
        {
            SessionStatusText.Text = "请先捕获目标";
            return;
        }

        InputSendResult result = _validatedInput.SendA(session.SessionId);
        if (result.IsSuccess)
        {
            SessionStatusText.Text = $"会话 {session.SessionId} · A 已发送";
            return;
        }

        InputFailureFeedback feedback = _failureFeedback.Create(result, session.ProcessId);
        SessionStatusText.Text = feedback.Message;
    }

    private void LoadBuiltInLayout()
    {
        var repository = new LayoutRepository(LayoutRepositoryPaths.CreateDefault(AppContext.BaseDirectory));
        LayoutReloadResult loaded = repository.Reload();
        if (loaded.Layouts.TryGetValue("builtin.qwerty.en-US", out KeyboardLayoutDefinition? layout))
        {
            LayoutView.LoadLayout(KeyboardLayoutViewModel.Create(layout));
            return;
        }

        SessionStatusText.Text = loaded.Issues.Count == 0
            ? "未找到内置键盘布局"
            : $"布局加载失败：{loaded.Issues[0].Path} · {loaded.Issues[0].Code}";
    }

    private void OnOverlayDpiChanged(OverlayDpiChangedNotification change)
    {
        _overlay.InvalidateManualPosition();
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
