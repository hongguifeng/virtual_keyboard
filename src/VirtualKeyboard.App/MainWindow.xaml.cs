using System.Windows;
using System.Windows.Input;
using VirtualKeyboard.Core.Configuration;
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
    private readonly InputFailureFeedbackFactory _failureFeedback;
    private readonly KeyboardController _keyboardController;
    private readonly LayoutActionDispatcher _actionDispatcher;
    private readonly HotkeyInputSender _hotkeySender;
    private readonly InputInjectionService _inputQueue;
    private readonly TargetStateCoordinator _coordinator = new();
    private readonly ConfigurationRepository _configurationRepository = new(ConfigurationRepositoryPaths.CreateDefault());
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
        _failureFeedback = new InputFailureFeedbackFactory(new ProcessIntegrityInspector(), _diagnostics);
        var keySender = new KeyInputSender(_diagnostics);
        var validatedKeySender = new ValidatedKeyInputSender(validator, keySender, _diagnostics);
        var capsLock = new CapsLockStateService(validatedKeySender);
        _keyboardController = new KeyboardController(capsLock);
        _hotkeySender = new HotkeyInputSender(_diagnostics);
        _inputQueue = new InputInjectionService();
        _actionDispatcher = new LayoutActionDispatcher(
            validator,
            _keyboardController,
            keySender,
            _hotkeySender,
            new UnicodeTextInputSender(_diagnostics));
        _configurationRepository.Load();
        LoadBuiltInLayout();
    }

    internal TargetSession? CurrentTargetSession => _targetSessions.Current;

    internal bool IsDisposed => _disposed;

    internal TargetCoordinatorState CoordinatorState => _coordinator.State;

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

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        OpenSettingsWindow();
    }

    internal bool OpenSettingsWindow()
    {
        if (!BeginSettingsSession()) return false;
        try
        {
            var settings = new SettingsWindow(_configurationRepository) { Owner = this };
            settings.ShowDialog();
            return true;
        }
        finally { EndSettingsSession(); }
    }

    internal bool BeginSettingsSession()
    {
        TargetStateTransition transition = _coordinator.OpenSettings();
        if (!transition.Accepted) return false;
        _inputQueue.SetCurrentSession(0);
        _targetSessions.Clear();
        _keyboardController.ClearTargetSession();
        _overlay.Hide();
        return true;
    }

    internal bool EndSettingsSession() => _coordinator.CloseSettings().Accepted;

    private void OnCaptureTargetClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        TargetCaptureResult result = _targetCapture.Capture();
        if (!result.IsCaptured)
        {
            _overlay.InvalidateManualPosition();
            _targetSessions.Clear();
            _keyboardController.ClearTargetSession();
            ReloadLayoutForTarget(isPassword: false);
            SessionStatusText.Text = $"捕获失败：{result.Status}";
            return;
        }

        _overlay.InvalidateManualPosition();
        TargetSession session = _targetSessions.Replace(result.Snapshot!);
        _inputQueue.SetCurrentSession(session.SessionId);
        _keyboardController.SetTargetSession(session.SessionId);
        LayoutView.UpdateState(_keyboardController.State);
        ReloadLayoutForTarget(session.IsPassword);
        SessionStatusText.Text = $"会话 {session.SessionId} · PID {session.ProcessId}\n前台 0x{session.TopLevelHwnd:X} · 焦点 0x{session.FocusHwnd:X}";
    }

    private async void OnLayoutKeyInvoked(object sender, KeyInvokedEventArgs e)
    {
        _ = sender;
        TargetSession? session = _targetSessions.Current;
        if (session?.IsPassword == true &&
            (!e.Key.SafeForPassword || !PasswordActionPolicy.Check(e.Key.Action).IsAllowed))
        {
            SessionStatusText.Text = "密码输入中此按键不可用";
            return;
        }
        if (session is null)
        {
            SessionStatusText.Text = "请先捕获目标";
            return;
        }

        InputActionKind kind = e.Key.Action.Type switch
        {
            LayoutActionTypes.Text => InputActionKind.Text,
            LayoutActionTypes.Key => InputActionKind.Key,
            LayoutActionTypes.Hotkey => InputActionKind.Hotkey,
            LayoutActionTypes.Modifier => InputActionKind.Modifier,
            _ => InputActionKind.Key,
        };
        QueuedInputResult queued = await _inputQueue.EnqueueAsync(
            session.SessionId,
            kind,
            cancellation => ValueTask.FromResult(_actionDispatcher.Dispatch(session.SessionId, e.Key, cancellation)));
        if (_disposed || _targetSessions.Current?.SessionId != session.SessionId)
        {
            return;
        }
        LayoutView.UpdateState(_keyboardController.State);
        if (queued.Status == InputQueueStatus.Completed && queued.SendResult is { IsSuccess: true })
        {
            SessionStatusText.Text = $"会话 {session.SessionId} · 按键已发送";
            return;
        }

        if (queued.SendResult is InputSendResult result)
        {
            InputFailureFeedback feedback = _failureFeedback.Create(result, session.ProcessId);
            SessionStatusText.Text = feedback.Message;
        }
        else
        {
            SessionStatusText.Text = "输入队列已停止或目标已变化";
        }
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

    private void ReloadLayoutForTarget(bool isPassword)
    {
        if (LayoutView.Layout is not null)
        {
            LayoutView.LoadLayout(LayoutView.Layout, isPassword);
        }
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
        _inputQueue.Dispose();
        _keyboardController.Dispose();
        _hotkeySender.Dispose();
        _overlay.Dispose();
        _diagnostics.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
