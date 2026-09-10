namespace VirtualKeyboard.Core.Targeting;

public enum TargetCoordinatorState
{
    Disabled,
    Hidden,
    Evaluating,
    VisibleTracking,
    ManuallySuppressed,
    SettingsOpen,
    ShuttingDown,
    LauncherTracking,
}

[Flags]
public enum TargetCoordinatorAction
{
    None = 0,
    BeginEvaluation = 1,
    ShowOrUpdateOverlay = 2,
    HideOverlay = 4,
    ClearTargetSession = 8,
    CancelPendingWork = 16,
    RefreshFocus = 32,
    ShowOrUpdateLauncher = 64,
}

public readonly record struct TargetStateTransition(
    TargetCoordinatorState PreviousState,
    TargetCoordinatorState CurrentState,
    long FocusVersion,
    TargetCoordinatorAction Actions,
    bool Accepted);

/// <summary>Serializes target visibility decisions and rejects stale asynchronous results.</summary>
public sealed class TargetStateCoordinator
{
    private readonly object _gate = new();
    private TargetCoordinatorState _state;
    private bool _enabled;
    private long _latestFocusVersion;
    private FocusSnapshot? _pending;
    private FocusSnapshot? _tracking;
    private FocusTargetIdentity? _suppressedTarget;
    private FocusTargetIdentity? _expandedTarget;

    public TargetStateCoordinator(bool enabled = true)
    {
        _enabled = enabled;
        _state = enabled ? TargetCoordinatorState.Hidden : TargetCoordinatorState.Disabled;
    }

    public TargetCoordinatorState State { get { lock (_gate) return _state; } }
    public long LatestFocusVersion { get { lock (_gate) return _latestFocusVersion; } }

    public TargetStateTransition Observe(FocusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            TargetCoordinatorState previous = _state;
            if (!_enabled || _state is TargetCoordinatorState.SettingsOpen or TargetCoordinatorState.ShuttingDown || snapshot.Version <= _latestFocusVersion)
                return Rejected(previous);

            _latestFocusVersion = snapshot.Version;
            _pending = snapshot;
            _state = TargetCoordinatorState.Evaluating;
            return Accepted(previous, TargetCoordinatorAction.BeginEvaluation);
        }
    }

    public TargetStateTransition ApplyClassification(FocusSnapshot snapshot, ClassificationResult classification, bool showLauncherButton = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            TargetCoordinatorState previous = _state;
            if (_state != TargetCoordinatorState.Evaluating || _pending?.Version != snapshot.Version ||
                classification.Version != snapshot.Version || snapshot.Version != _latestFocusVersion)
                return Rejected(previous);

            _pending = null;
            if (classification.Value == Editability.Editable)
            {
                FocusTargetIdentity identity = FocusTargetIdentity.From(snapshot);
                if (_suppressedTarget is not null && _suppressedTarget.Equals(identity))
                {
                    _state = TargetCoordinatorState.ManuallySuppressed;
                    return Accepted(previous, TargetCoordinatorAction.None);
                }

                _suppressedTarget = null;
                _tracking = snapshot;
                if (snapshot.RuntimeId is null || _expandedTarget != identity) _expandedTarget = null;
                if (showLauncherButton && _expandedTarget is null)
                {
                    _state = TargetCoordinatorState.LauncherTracking;
                    return Accepted(previous, TargetCoordinatorAction.ShowOrUpdateLauncher);
                }
                _state = TargetCoordinatorState.VisibleTracking;
                return Accepted(previous, TargetCoordinatorAction.ShowOrUpdateOverlay);
            }

            _tracking = null;
            _expandedTarget = null;
            _state = TargetCoordinatorState.Hidden;
            return Accepted(previous, TargetCoordinatorAction.HideOverlay | TargetCoordinatorAction.ClearTargetSession);
        }
    }

    public TargetStateTransition UserClose()
    {
        lock (_gate)
        {
            TargetCoordinatorState previous = _state;
            if (_state != TargetCoordinatorState.VisibleTracking || _tracking is null) return Rejected(previous);
            _suppressedTarget = FocusTargetIdentity.From(_tracking);
            _expandedTarget = null;
            _state = TargetCoordinatorState.ManuallySuppressed;
            return Accepted(previous, TargetCoordinatorAction.HideOverlay);
        }
    }

    public TargetStateTransition UserShow(long? expectedFocusVersion = null)
    {
        lock (_gate)
        {
            TargetCoordinatorState previous = _state;
            if (expectedFocusVersion.HasValue && expectedFocusVersion.Value != _latestFocusVersion) return Rejected(previous);
            if (_state is not (TargetCoordinatorState.ManuallySuppressed or TargetCoordinatorState.LauncherTracking) || _tracking is null) return Rejected(previous);
            _suppressedTarget = null;
            _expandedTarget = FocusTargetIdentity.From(_tracking);
            _state = TargetCoordinatorState.VisibleTracking;
            return Accepted(previous, TargetCoordinatorAction.ShowOrUpdateOverlay);
        }
    }

    public TargetStateTransition SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            TargetCoordinatorState previous = _state;
            if (_state == TargetCoordinatorState.ShuttingDown || _enabled == enabled) return Rejected(previous);
            _enabled = enabled;
            _expandedTarget = null;
            _pending = null;
            _tracking = null;
            _suppressedTarget = null;
            _state = enabled ? TargetCoordinatorState.Hidden : TargetCoordinatorState.Disabled;
            TargetCoordinatorAction actions = TargetCoordinatorAction.HideOverlay | TargetCoordinatorAction.ClearTargetSession | TargetCoordinatorAction.CancelPendingWork;
            if (enabled) actions |= TargetCoordinatorAction.RefreshFocus;
            return Accepted(previous, actions);
        }
    }

    public TargetStateTransition OpenSettings()
    {
        lock (_gate)
        {
            TargetCoordinatorState previous = _state;
            if (_state is TargetCoordinatorState.SettingsOpen or TargetCoordinatorState.ShuttingDown) return Rejected(previous);
            _expandedTarget = null;
            _pending = null;
            _tracking = null;
            _state = TargetCoordinatorState.SettingsOpen;
            return Accepted(previous, TargetCoordinatorAction.HideOverlay | TargetCoordinatorAction.ClearTargetSession | TargetCoordinatorAction.CancelPendingWork);
        }
    }

    public TargetStateTransition CloseSettings()
    {
        lock (_gate)
        {
            TargetCoordinatorState previous = _state;
            if (_state != TargetCoordinatorState.SettingsOpen) return Rejected(previous);
            _state = _enabled ? TargetCoordinatorState.Hidden : TargetCoordinatorState.Disabled;
            return Accepted(previous, _enabled ? TargetCoordinatorAction.RefreshFocus : TargetCoordinatorAction.None);
        }
    }

    public TargetStateTransition TargetDestroyed(FocusSnapshot target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate)
        {
            TargetCoordinatorState previous = _state;
            if (_tracking is null || !FocusTargetIdentity.From(_tracking).Equals(FocusTargetIdentity.From(target))) return Rejected(previous);
            _expandedTarget = null;
            _tracking = null;
            _pending = null;
            _suppressedTarget = null;
            _state = _enabled ? TargetCoordinatorState.Hidden : TargetCoordinatorState.Disabled;
            return Accepted(previous, TargetCoordinatorAction.HideOverlay | TargetCoordinatorAction.ClearTargetSession | TargetCoordinatorAction.CancelPendingWork);
        }
    }

    /// <summary>Invalidates the current target when the provider can no longer supply element identity.</summary>
    public TargetStateTransition InvalidateCurrentTarget()
    {
        lock (_gate)
        {
            TargetCoordinatorState previous = _state;
            if (_state is TargetCoordinatorState.Disabled or TargetCoordinatorState.Hidden or
                TargetCoordinatorState.SettingsOpen or TargetCoordinatorState.ShuttingDown)
                return Rejected(previous);
            _expandedTarget = null;
            _tracking = null;
            _pending = null;
            _suppressedTarget = null;
            _state = TargetCoordinatorState.Hidden;
            return Accepted(previous, TargetCoordinatorAction.HideOverlay | TargetCoordinatorAction.ClearTargetSession | TargetCoordinatorAction.CancelPendingWork);
        }
    }

    public TargetStateTransition Shutdown()
    {
        lock (_gate)
        {
            TargetCoordinatorState previous = _state;
            if (_state == TargetCoordinatorState.ShuttingDown) return Rejected(previous);
            _state = TargetCoordinatorState.ShuttingDown;
            _expandedTarget = null;
            _pending = null;
            _tracking = null;
            _suppressedTarget = null;
            return Accepted(previous, TargetCoordinatorAction.HideOverlay | TargetCoordinatorAction.ClearTargetSession | TargetCoordinatorAction.CancelPendingWork);
        }
    }

    private TargetStateTransition Accepted(TargetCoordinatorState previous, TargetCoordinatorAction actions) =>
        new(previous, _state, _latestFocusVersion, actions, true);
    private TargetStateTransition Rejected(TargetCoordinatorState previous) =>
        new(previous, _state, _latestFocusVersion, TargetCoordinatorAction.None, false);

    private sealed record FocusTargetIdentity(int ProcessId, nint TopLevelHwnd, RuntimeIdentity? RuntimeId, RuntimeIdentity? InputOwnerRuntimeId)
    {
        public static FocusTargetIdentity From(FocusSnapshot snapshot) => new(snapshot.ProcessId, snapshot.TopLevelHwnd, snapshot.RuntimeId, snapshot.InputOwnerRuntimeId);
    }
}
