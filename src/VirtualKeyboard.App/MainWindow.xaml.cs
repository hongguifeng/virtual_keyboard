using System.IO;
using System.Windows;
using System.Windows.Input;
using VirtualKeyboard.Core.AutoStart;
using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Core.Layouts;
using VirtualKeyboard.Core.Positioning;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.App;

/// <summary>Non-activating keyboard window driven by automatic focus observation.</summary>
public partial class MainWindow : Window, IDisposable, ITrayCommands
{
    private const double ImeCandidateClearanceDip = 96;

    private readonly OverlayWindowAdapter _overlay;
    private readonly IForegroundTargetCapture _targetCapture;
    private readonly TargetSessionStore _targetSessions;
    private readonly DiagnosticLogger _diagnostics;
    private readonly RollingFileDiagnosticSink? _diagnosticSink;
    private readonly InputFailureFeedbackFactory _failureFeedback;
    private readonly KeyboardController _keyboardController;
    private readonly LayoutActionDispatcher _actionDispatcher;
    private readonly HotkeyInputSender _hotkeySender;
    private readonly InputInjectionService _inputQueue;
    private int _repeatInputInFlight;
    private readonly TargetStateCoordinator _coordinator = new();
    private readonly LatestFocusSnapshotStore _latestFocusSnapshots = new();
    private readonly ConfigurationRepository _configurationRepository;
    private readonly LayoutRepository _layoutRepository;
    private readonly IAutoStartManager? _autoStart;
    private readonly MonitorDpiAdapter _monitorDpi = new();
    private IsolatedFocusObservationService? _focusObservation;
    private PhysicalPixelRect? _persistentManualPosition;
    private PhysicalPixelRect? _lastDpiMoveRequest;
    private bool _disposed;

    public MainWindow()
        : this(new NativeForegroundTargetCapture(), new TargetSessionStore(), new(ConfigurationRepositoryPaths.CreateDefault()), new AutoStartManager(), createFileDiagnostics: true)
    {
    }

    internal MainWindow(IForegroundTargetCapture targetCapture, TargetSessionStore targetSessions)
        : this(targetCapture, targetSessions, new(ConfigurationRepositoryPaths.CreateDefault()), autoStart: null, createFileDiagnostics: false)
    {
    }

    internal MainWindow(IForegroundTargetCapture targetCapture, TargetSessionStore targetSessions, ConfigurationRepository configurationRepository)
        : this(targetCapture, targetSessions, configurationRepository, autoStart: null, createFileDiagnostics: false)
    {
    }

    internal MainWindow(IForegroundTargetCapture targetCapture, TargetSessionStore targetSessions, ConfigurationRepository configurationRepository, IAutoStartManager autoStart)
        : this(targetCapture, targetSessions, configurationRepository, autoStart, createFileDiagnostics: false)
    {
    }

    private MainWindow(IForegroundTargetCapture targetCapture, TargetSessionStore targetSessions, ConfigurationRepository configurationRepository, IAutoStartManager? autoStart, bool createFileDiagnostics)
    {
        InitializeComponent();
        _overlay = new OverlayWindowAdapter(this);
        _overlay.DpiChanged += OnOverlayDpiChanged;
        _overlay.ResizeCompleted += ApplyCompletedResize;
        _targetCapture = targetCapture ?? throw new ArgumentNullException(nameof(targetCapture));
        _targetSessions = targetSessions ?? throw new ArgumentNullException(nameof(targetSessions));
        _configurationRepository = configurationRepository ?? throw new ArgumentNullException(nameof(configurationRepository));
        _autoStart = autoStart;
        ConfigurationLoadResult configurationLoad = _configurationRepository.Load();
        Width = configurationLoad.Configuration.KeyboardWidthDip;
        Height = configurationLoad.Configuration.KeyboardHeightDip;
        Opacity = configurationLoad.Configuration.Opacity;
        if (createFileDiagnostics)
        {
            string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VirtualKeyboard", "logs");
            _diagnosticSink = new RollingFileDiagnosticSink(logDirectory, configurationLoad.Configuration.DetailedDiagnostics, maxFileBytes: 4 * 1024 * 1024, maxFileCount: 5);
        }
        _diagnostics = new DiagnosticLogger(sink: _diagnosticSink);
        _diagnostics.Log(
            configurationLoad.Status == ConfigurationLoadStatus.RecoveredInvalid ? DiagnosticType.ConfigRecovered : DiagnosticType.ConfigLoaded,
            DiagnosticModule.Configuration,
            errorCode: configurationLoad.Issues.Count);
        SyncAutoStartWithRegistry();
        var validator = new TargetSessionValidator(_targetSessions, _targetCapture, _latestFocusSnapshots,
            () => _focusObservation?.IsHealthy ?? true);
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
        if (!configurationLoad.Configuration.Enabled) _coordinator.SetEnabled(false);
        _layoutRepository = new LayoutRepository(LayoutRepositoryPaths.CreateDefault(AppContext.BaseDirectory));
        LoadBuiltInLayout();
        ApplyLanguage();
    }

    internal TargetSession? CurrentTargetSession => _targetSessions.Current;

    internal bool IsDisposed => _disposed;

    internal TargetCoordinatorState CoordinatorState => _coordinator.State;
    internal long InputQueueSessionId => _inputQueue.CurrentSessionId;
    internal bool IsAutomaticFocusRunning => _focusObservation?.IsRunning == true;
    internal PhysicalPixelRect? LastDpiMoveRequest => _lastDpiMoveRequest;

    bool ITrayCommands.IsEnabled => _configurationRepository.Current.Enabled;
    UiLanguage ITrayCommands.UiLanguage => _configurationRepository.Current.UiLanguage;
    void ITrayCommands.SetEnabled(bool enabled) => SetApplicationEnabled(enabled);
    void ITrayCommands.ShowCurrentKeyboard() => ShowCurrentKeyboard();
    void ITrayCommands.OpenSettings() => OpenSettingsWindow();
    void ITrayCommands.ReloadLayouts() => LoadBuiltInLayout();
    void ITrayCommands.Exit() => Application.Current.Shutdown();

    internal nint OverlayHandle => _overlay.Handle;

    internal void ShowAt(int x, int y, int width, int height) => _overlay.ShowAt(x, y, width, height);

    internal bool BeginManualMoveForCurrentSession() =>
        _targetSessions.Current is TargetSession session && _overlay.BeginManualMove(session.SessionId);

    internal bool EndManualMoveForCurrentSession()
    {
        if (_targetSessions.Current is not TargetSession session || !_overlay.EndManualMove(session.SessionId)) return false;
        _persistentManualPosition = _configurationRepository.Current.ManualPositionMode == ManualPositionMode.Persistent &&
            _overlay.TryGetManualPosition(session.SessionId, out PhysicalPixelRect rectangle)
                ? rectangle
                : null;
        return true;
    }

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
        _coordinator.UserClose();
        _overlay.Hide();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        OpenSettingsWindow();
    }

    internal bool OpenSettingsWindow()
    {
        bool wasEnabled = _configurationRepository.Current.Enabled;
        if (!BeginSettingsSession()) return false;
        try
        {
            var settings = new SettingsWindow(_configurationRepository, _autoStart);
            // WPF 限制：从未 Show 过的窗口不能被设为 Owner，否则 InvalidOperationException。
            // 应用刚启动（键盘从未弹出）时主窗口尚未显示，此时把设置窗口作为独立顶层窗口打开，
            // 不设 Owner；主窗口显示过（IsLoaded）后再设 Owner。
            if (IsLoaded) settings.Owner = this;
            settings.ShowDialog();
            return true;
        }
        finally
        {
            EndSettingsSession();
            bool isEnabled = _configurationRepository.Current.Enabled;
            if (wasEnabled != isEnabled) _coordinator.SetEnabled(isEnabled);
            Opacity = _configurationRepository.Current.Opacity;
            if (_configurationRepository.Current.ManualPositionMode != ManualPositionMode.Persistent) _persistentManualPosition = null;
            _diagnosticSink?.SetDetailedEnabled(_configurationRepository.Current.DetailedDiagnostics);
            LoadBuiltInLayout();
            ApplyLanguage();
        }
    }

    internal bool BeginSettingsSession()
    {
        TargetStateTransition transition = _coordinator.OpenSettings();
        if (!transition.Accepted) return false;
        _inputQueue.SetCurrentSession(0);
        _latestFocusSnapshots.Clear();
        _targetSessions.Clear();
        ClearKeyboardState();
        _overlay.Hide();
        return true;
    }

    internal bool EndSettingsSession() => _coordinator.CloseSettings().Accepted;

    internal void SetApplicationEnabled(bool enabled)
    {
        KeyboardConfiguration current = _configurationRepository.Current;
        if (current.Enabled == enabled) return;
        ConfigurationSaveResult saved = _configurationRepository.Save(new(
            current.SchemaVersion, enabled, current.AutoShow, current.AutoHide, current.Opacity,
            current.KeyboardWidthDip, current.KeyboardHeightDip, current.MarginDip, current.LayoutId,
            current.ManualPositionMode, current.DetailedDiagnostics, current.CustomKeys, current.UiLanguage,
            current.AutoStart));
        if (!saved.IsSaved) _diagnostics.Log(DiagnosticType.ConfigSaveFailed, DiagnosticModule.Configuration, reason: ReasonCode.IoError);
        _coordinator.SetEnabled(enabled);
        if (!enabled)
        {
            _inputQueue.SetCurrentSession(0);
            _latestFocusSnapshots.Clear();
            _targetSessions.Clear();
            ClearKeyboardState();
            _overlay.Hide();
        }
    }

    internal void ShowCurrentKeyboard()
    {
        if (!_configurationRepository.Current.Enabled) return;
        _coordinator.UserShow();
        KeyboardConfiguration configuration = _configurationRepository.Current;
        _overlay.ShowAt(40, 40, checked((int)Math.Round(configuration.KeyboardWidthDip)), checked((int)Math.Round(configuration.KeyboardHeightDip)));
    }

    internal void StartAutomaticFocusObservation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_focusObservation is not null) return;
        _focusObservation = new IsolatedFocusObservationService(
            Path.Combine(AppContext.BaseDirectory, "VirtualKeyboard.App.exe"), OnFocusChanged,
            fault =>
            {
                _latestFocusSnapshots.Clear();
                if (fault)
                {
                    _targetSessions.Clear();
                    Dispatcher.BeginInvoke(() => { if (!_disposed) ClearAutomaticTarget(); });
                }
            },
            (status, pid, code) => _diagnostics.Log(status switch
            {
                FocusWorkerStatus.Started => DiagnosticType.FocusWorkerStarted,
                FocusWorkerStatus.Heartbeat => DiagnosticType.FocusWorkerHeartbeat,
                FocusWorkerStatus.Stalled => DiagnosticType.FocusWorkerStalled,
                FocusWorkerStatus.Restarting => DiagnosticType.FocusWorkerRestarting,
                FocusWorkerStatus.Recovered => DiagnosticType.FocusWorkerRecovered,
                FocusWorkerStatus.Exhausted => DiagnosticType.FocusWorkerExhausted,
                _ => DiagnosticType.FocusWorkerExited,
            }, DiagnosticModule.Focus, targetProcessId: pid, errorCode: code));
        _focusObservation.Start();
    }

    private void OnFocusChanged(RemoteFocusNotification notification)
    {
        if (_disposed || !notification.IsCurrent()) return;
        if (notification.Evaluation is not FocusTargetEvaluation evaluation)
        {
            _latestFocusSnapshots.Clear();
            Dispatcher.BeginInvoke(() => { if (notification.IsCurrent()) ClearAutomaticTarget(); });
            return;
        }
        FocusSnapshot snapshot = evaluation.Snapshot;
        _latestFocusSnapshots.Publish(snapshot);
        _diagnostics.Log(DiagnosticType.FocusObserved, DiagnosticModule.Focus, DiagnosticLevel.Detailed, targetProcessId: snapshot.ProcessId);
        TargetStateTransition observed = _coordinator.Observe(snapshot);
        if (!observed.Accepted) return;
        _diagnostics.Log(DiagnosticType.ClassificationCompleted, DiagnosticModule.Classification,
            durationMs: notification.DurationMs,
            errorCode: (int)evaluation.Status,
            focusVersion: snapshot.Version, retryAttempt: notification.RetryAttempt,
            usedFallback: evaluation.UsedFallback, usedEventTarget: notification.UsedEventTarget,
            focusControlType: snapshot.ControlType, hasKeyboardFocus: snapshot.HasKeyboardFocus,
            isEnabled: snapshot.IsEnabled, isOffscreen: snapshot.IsOffscreen,
            reason: GetFocusReason(evaluation),
            targetProcessId: snapshot.ProcessId,
            controlKind: evaluation.Classification.Value switch
            {
                Editability.Editable when snapshot.IsPassword => ControlKind.Password,
                Editability.Editable => ControlKind.Editable,
                Editability.NotEditable => ControlKind.NotEditable,
                _ => ControlKind.Unknown,
            },
            verdict: evaluation.Classification.Value switch
            {
                Editability.Editable when snapshot.IsPassword => Verdict.Password,
                Editability.Editable => Verdict.Editable,
                Editability.NotEditable => Verdict.NotEditable,
                _ => Verdict.Unknown,
            });
        if (!notification.IsCurrent()) return;
        TargetStateTransition classified = _coordinator.ApplyClassification(snapshot, evaluation.Classification);
        Dispatcher.BeginInvoke(() => { if (notification.IsCurrent()) ApplyAutomaticFocus(evaluation, classified); });
        if (evaluation.NeedsRetry || notification.RetryAttempt > 0)
            _diagnostics.Log(evaluation.NeedsRetry
                ? notification.RetryAttempt < FocusObservationService.MaxEvaluationRetries
                    ? DiagnosticType.FocusRetryScheduled : DiagnosticType.FocusRetryExhausted
                : DiagnosticType.FocusRetryRecovered, DiagnosticModule.Focus,
                targetProcessId: snapshot.ProcessId, reason: GetFocusReason(evaluation),
                focusVersion: snapshot.Version, retryAttempt: notification.RetryAttempt, errorCode: (int)evaluation.Status);
    }

    internal static ReasonCode GetFocusReason(FocusTargetEvaluation evaluation) => evaluation.Status switch
    {
        FocusTargetEvaluationStatus.FocusUnavailable => ReasonCode.FocusUnavailable,
        FocusTargetEvaluationStatus.IdentityMismatch => ReasonCode.IdentityMismatch,
        FocusTargetEvaluationStatus.EvidenceUnavailable => ReasonCode.EvidenceUnavailable,
        _ => evaluation.Classification.ReasonCode switch
        {
            ClassificationReasonCode.NoFocusOrDisabled => ReasonCode.NoFocusOrDisabled,
            ClassificationReasonCode.ReadOnly => ReasonCode.ReadOnly,
            ClassificationReasonCode.PasswordEdit => ReasonCode.PasswordEdit,
            ClassificationReasonCode.ValuePattern => ReasonCode.ValuePattern,
            ClassificationReasonCode.TextEditPattern => ReasonCode.TextEditPattern,
            ClassificationReasonCode.CaretEvidence => ReasonCode.CaretEvidence,
            ClassificationReasonCode.TextPatternOnly => ReasonCode.TextPatternOnly,
            ClassificationReasonCode.NoEditableEvidence => ReasonCode.NoEditableEvidence,
            _ => ReasonCode.ElementInvalid,
        },
    };

    internal void ApplyAutomaticFocus(FocusTargetEvaluation evaluation, TargetStateTransition transition)
    {
        if (_disposed || !transition.Accepted || _latestFocusSnapshots.Current?.Version != evaluation.Snapshot.Version) return;
        KeyboardConfiguration configuration = _configurationRepository.Current;
        if (transition.Actions.HasFlag(TargetCoordinatorAction.ShowOrUpdateOverlay) && evaluation.IsEvaluated &&
            evaluation.Classification.Value == Editability.Editable && evaluation.FocusHwnd != nint.Zero &&
            evaluation.Anchor is { IsValid: true } anchor)
        {
            _overlay.InvalidateManualPosition();
            _hotkeySender.ReleaseLatchedModifiers();
            TargetSession session = _targetSessions.Replace(evaluation.Snapshot, evaluation.FocusHwnd, anchor);
            _diagnostics.Log(DiagnosticType.TargetSessionCreated, DiagnosticModule.State, targetProcessId: session.ProcessId);
            _inputQueue.SetCurrentSession(session.SessionId);
            _keyboardController.SetTargetSession(session.SessionId);
            ReloadLayoutForTarget(session.IsPassword);
            LayoutView.UpdateState(_keyboardController.State);
            TitleStatusText.Text = string.Empty;
            if (!configuration.AutoShow) return;
            MonitorMetricsResult monitor = _monitorDpi.Capture(anchor, session.TopLevelHwnd);
            if (!monitor.IsCaptured) { ClearAutomaticTarget(); return; }
            PhysicalPixelSize desired = monitor.Metrics!.DpiScale.ToPhysicalPixels(new(configuration.KeyboardWidthDip, configuration.KeyboardHeightDip));
            PlacementResult placement = PlacementService.Place(
                anchor,
                monitor.Metrics.WorkArea,
                desired,
                configuration.MarginDip * monitor.Metrics.DpiScale.ScaleX,
                ImeCandidateClearanceDip * monitor.Metrics.DpiScale.ScaleY);
            if (!placement.IsPlaced) { ClearAutomaticTarget(); return; }
            PhysicalPixelRect rectangle = configuration.ManualPositionMode == ManualPositionMode.Persistent &&
                _persistentManualPosition is PhysicalPixelRect saved
                    ? RestoreManualPosition(saved, placement.Rectangle!.Value, monitor.Metrics.WorkArea)
                    : placement.Rectangle!.Value;
            Opacity = configuration.Opacity;
            _overlay.ShowAt(checked((int)Math.Round(rectangle.X)), checked((int)Math.Round(rectangle.Y)),
                checked((int)Math.Round(rectangle.Width)), checked((int)Math.Round(rectangle.Height)));
            _diagnostics.Log(DiagnosticType.OverlayShown, DiagnosticModule.Overlay, targetProcessId: session.ProcessId);
            return;
        }
        if (transition.Actions.HasFlag(TargetCoordinatorAction.ClearTargetSession))
        {
            _inputQueue.SetCurrentSession(0);
            _targetSessions.Clear();
            ClearKeyboardState();
        }
        if (configuration.AutoHide && transition.Actions.HasFlag(TargetCoordinatorAction.HideOverlay)) _overlay.Hide();
    }

    internal bool ApplyEvaluatedFocusForTest(FocusTargetEvaluation evaluation)
    {
        _latestFocusSnapshots.Publish(evaluation.Snapshot);
        TargetStateTransition observed = _coordinator.Observe(evaluation.Snapshot);
        if (!observed.Accepted) return false;
        TargetStateTransition classified = _coordinator.ApplyClassification(evaluation.Snapshot, evaluation.Classification);
        ApplyAutomaticFocus(evaluation, classified);
        return classified.Accepted;
    }

    private void ClearAutomaticTarget()
    {
        if (_disposed || _coordinator.State is TargetCoordinatorState.SettingsOpen or TargetCoordinatorState.ShuttingDown) return;
        _coordinator.InvalidateCurrentTarget();
        _latestFocusSnapshots.Clear();
        _inputQueue.SetCurrentSession(0);
        _targetSessions.Clear();
        ClearKeyboardState();
        if (_configurationRepository.Current.AutoHide) _overlay.Hide();
    }

    internal ConfigurationSaveResult SaveCurrentConfiguration() => _configurationRepository.Save(_configurationRepository.Current);

    /// <summary>
    /// 开机自启同步（FR-APP-004，设计 14.4）：注册表是自启唯一事实来源，配置只是镜像。
    /// 启动时若注册表读取成功且状态与配置不同（例如用户在任务管理器删除了自启项），
    /// 把配置镜像更新为注册表事实并持久化。
    /// 注册表读取失败（如无用户 Run 键）时保持现有配置（默认关闭），绝不自动启用。
    /// </summary>
    internal void SyncAutoStartWithRegistry()
    {
        if (_autoStart is null) return;
        if (!_autoStart.TryGetEnabled(out bool actual)) return;
        KeyboardConfiguration current = _configurationRepository.Current;
        if (current.AutoStart == actual) return;
        ConfigurationSaveResult saved = _configurationRepository.Save(new(
            current.SchemaVersion, current.Enabled, current.AutoShow, current.AutoHide, current.Opacity,
            current.KeyboardWidthDip, current.KeyboardHeightDip, current.MarginDip, current.LayoutId,
            current.ManualPositionMode, current.DetailedDiagnostics, current.CustomKeys, current.UiLanguage,
            autoStart: actual));
        if (saved.IsSaved && actual)
        {
            _diagnostics.Log(DiagnosticType.AutoStartSync, DiagnosticModule.Configuration);
        }
        else if (!saved.IsSaved)
        {
            _diagnostics.Log(DiagnosticType.ConfigSaveFailed, DiagnosticModule.Configuration, reason: ReasonCode.IoError);
        }
    }

    private void ClearKeyboardState()
    {
        _hotkeySender.ReleaseLatchedModifiers();
        _keyboardController.ClearTargetSession();
    }

    private async void OnLayoutKeyInvoked(object sender, KeyInvokedEventArgs e)
    {
        _ = sender;
        TargetSession? session = _targetSessions.Current;
        if (session?.IsPassword == true &&
            (!e.Key.SafeForPassword || !PasswordActionPolicy.Check(e.Key.Action).IsAllowed))
        {
            TitleStatusText.Text = AppStrings.For(_configurationRepository.Current.UiLanguage).PasswordUnavailable;
            return;
        }
        if (session is null)
        {
            TitleStatusText.Text = AppStrings.For(_configurationRepository.Current.UiLanguage).SelectEditable;
            return;
        }

        if (e.IsRepeat && Interlocked.CompareExchange(ref _repeatInputInFlight, 1, 0) != 0) return;
        try
        {
            InputActionKind kind = e.Key.Action.Type switch
            {
                LayoutActionTypes.Text => InputActionKind.Text,
                LayoutActionTypes.Key => InputActionKind.Key,
                LayoutActionTypes.Hotkey => InputActionKind.Hotkey,
                LayoutActionTypes.Chord => InputActionKind.Hotkey,
                LayoutActionTypes.Modifier => InputActionKind.Modifier,
                _ => InputActionKind.Key,
            };
            QueuedInputResult queued = await _inputQueue.EnqueueAsync(
                session.SessionId,
                kind,
                cancellation => ValueTask.FromResult(_actionDispatcher.Dispatch(session.SessionId, e.Key, cancellation)));
            if (_disposed || _targetSessions.Current?.SessionId != session.SessionId) return;
            LayoutView.UpdateState(_keyboardController.State);
            if (queued.Status == InputQueueStatus.Completed && queued.SendResult is { IsSuccess: true })
            {
                TitleStatusText.Text = string.Empty;
                return;
            }

            if (queued.SendResult is InputSendResult result)
            {
                InputFailureFeedback feedback = _failureFeedback.Create(result, session.ProcessId);
                TitleStatusText.Text = AppStrings.For(_configurationRepository.Current.UiLanguage).InputFailure(feedback);
            }
            else
            {
                TitleStatusText.Text = AppStrings.For(_configurationRepository.Current.UiLanguage).QueueStopped;
            }
        }
        finally
        {
            if (e.IsRepeat) Volatile.Write(ref _repeatInputInFlight, 0);
        }
    }

    private void LoadBuiltInLayout()
    {
        LayoutReloadResult loaded = _layoutRepository.Reload();
        string layoutId = _configurationRepository.Current.LayoutId ?? "builtin.qwerty.en-US";
        if (loaded.Layouts.TryGetValue(layoutId, out KeyboardLayoutDefinition? layout) ||
            loaded.Layouts.TryGetValue("builtin.qwerty.en-US", out layout))
        {
            LayoutView.LoadLayout(KeyboardLayoutViewModel.Create(layout));
            LoadConfiguredCustomKeys(passwordTarget: false);
            return;
        }

        AppStrings strings = AppStrings.For(_configurationRepository.Current.UiLanguage);
        TitleStatusText.Text = loaded.Issues.Count == 0
            ? strings.LayoutMissing
            : strings.LayoutFailed(loaded.Issues[0].Path, loaded.Issues[0].Code);
    }

    private void ReloadLayoutForTarget(bool isPassword)
    {
        if (LayoutView.Layout is not null)
        {
            LayoutView.LoadLayout(LayoutView.Layout, isPassword);
            LoadConfiguredCustomKeys(isPassword);
        }
    }

    private void LoadConfiguredCustomKeys(bool passwordTarget)
    {
        CustomKeysView.LoadKeys(_configurationRepository.Current.CustomKeys, passwordTarget);
        int customColumns = CustomKeysView.ColumnCount;
        StandardKeyboardColumn.Width = new GridLength(16, GridUnitType.Star);
        CustomKeyboardColumn.Width = new GridLength(customColumns * 2.5, GridUnitType.Star);
    }

    private void OnOverlayDpiChanged(OverlayDpiChangedNotification change)
    {
        _overlay.InvalidateManualPosition();
        _persistentManualPosition = null;
        if (_targetSessions.Current is null)
        {
            return;
        }

        KeyboardConfiguration configuration = _configurationRepository.Current;
        PhysicalPixelSize size = change.DpiScale.ToPhysicalPixels(new(configuration.KeyboardWidthDip, configuration.KeyboardHeightDip));
        int x = checked((int)Math.Round(change.SuggestedRectangle.X));
        int y = checked((int)Math.Round(change.SuggestedRectangle.Y));
        int width = checked((int)Math.Round(size.Width));
        int height = checked((int)Math.Round(size.Height));
        _lastDpiMoveRequest = new(x, y, width, height);
        _overlay.Move(x, y, width, height);
    }

    private static PhysicalPixelRect RestoreManualPosition(
        PhysicalPixelRect saved,
        PhysicalPixelRect sizedFallback,
        PhysicalPixelRect workArea)
    {
        double x = Math.Clamp(saved.X, workArea.X, workArea.Right - sizedFallback.Width);
        double y = Math.Clamp(saved.Y, workArea.Y, workArea.Bottom - sizedFallback.Height);
        return new(x, y, sizedFallback.Width, sizedFallback.Height);
    }

    internal void ApplyCompletedResize(double widthDip, double heightDip)
    {
        if (_disposed || !double.IsFinite(widthDip) || !double.IsFinite(heightDip)) return;
        KeyboardConfiguration current = _configurationRepository.Current;
        double width = Math.Clamp(widthDip, ConfigurationSchemaLimits.MinimumKeyboardWidthDip, ConfigurationSchemaLimits.MaximumKeyboardWidthDip);
        double height = Math.Clamp(heightDip, ConfigurationSchemaLimits.MinimumKeyboardHeightDip, ConfigurationSchemaLimits.MaximumKeyboardHeightDip);
        ConfigurationSaveResult saved = _configurationRepository.Save(new(
            current.SchemaVersion, current.Enabled, current.AutoShow, current.AutoHide, current.Opacity,
            width, height, current.MarginDip, current.LayoutId, current.ManualPositionMode,
            current.DetailedDiagnostics, current.CustomKeys, current.UiLanguage,
            current.AutoStart));
        if (!saved.IsSaved)
        {
            _diagnostics.Log(DiagnosticType.ConfigSaveFailed, DiagnosticModule.Configuration, reason: ReasonCode.IoError);
        }
    }

    private void ApplyLanguage() =>
        SettingsButton.Content = AppStrings.For(_configurationRepository.Current.UiLanguage).Settings;

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

        _disposed = true;
        _coordinator.Shutdown();
        _focusObservation?.Dispose();
        _focusObservation = null;
        _inputQueue.Dispose();
        _latestFocusSnapshots.Clear();
        _targetSessions.Clear();
        _hotkeySender.ReleaseLatchedModifiers();
        _keyboardController.Dispose();
        _hotkeySender.Dispose();
        _overlay.DpiChanged -= OnOverlayDpiChanged;
        _overlay.ResizeCompleted -= ApplyCompletedResize;
        _overlay.Dispose();
        _diagnostics.Dispose();
        _diagnosticSink?.Dispose();
        GC.SuppressFinalize(this);
    }
}
