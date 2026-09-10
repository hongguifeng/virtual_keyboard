using System.Windows.Automation;
using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows;

/// <summary>Runs UI Automation focus observation and consumer notifications on a dedicated MTA thread.</summary>
public sealed class FocusObservationService : IDisposable
{
    private const int LifecycleTimeoutMilliseconds = 5_000;
    private const int PendingFocusCapacity = 256;
    internal const int FocusStabilityMilliseconds = 50;
    internal const int FocusPollingMilliseconds = 250;
    /// <summary>Additional evaluations per focus/event, spaced by the polling interval.</summary>
    public const int MaxEvaluationRetries = 4;

    private readonly IFocusAutomationSource _source;
    private readonly IFocusSnapshotSource _snapshotSource;
    private readonly Action<FocusChangedNotification>? _observer;
    private readonly Action<int>? _errorObserver;
    private readonly Func<FocusChangedNotification, bool>? _evaluate;
    private readonly object _gate = new();
    private readonly ManualResetEvent _started = new(false);
    private readonly ManualResetEvent _stopRequested = new(false);
    private readonly SemaphoreSlim _focusPending = new(0, PendingFocusCapacity);
    private Thread? _thread;
    private Exception? _startupError;
    private bool _disposed;
    private FocusEventTarget? _eventTarget;
    private long _eventVersion;
    private long _completedCycles;
    private long _progressTimestamp;
    private int _stage;
    public long EventVersion => Volatile.Read(ref _eventVersion);
    public long CompletedCycles => Volatile.Read(ref _completedCycles);
    public long ProgressTimestamp => Volatile.Read(ref _progressTimestamp);
    public int Stage => Volatile.Read(ref _stage);
    private sealed record FocusEventTarget(AutomationElement? Element);

    /// <param name="observer">Optional notification consumer on the MTA thread.</param>
    /// <param name="errorObserver">Receives numeric boundary failures.</param>
    /// <param name="evaluate">Return true to request a bounded retry; never injects input.</param>
    public FocusObservationService(Action<FocusChangedNotification>? observer = null, Action<int>? errorObserver = null, Func<FocusChangedNotification, bool>? evaluate = null)
        : this(
            new SystemFocusAutomationSource(),
            new SystemFocusSnapshotSource(new FocusSnapshotFactory(Environment.ProcessId)),
            observer,
            errorObserver, evaluate)
    {
    }

    internal FocusObservationService(IFocusAutomationSource source, Action<FocusChangedNotification>? observer = null)
        : this(source, NullFocusSnapshotSource.Instance, observer, null)
    {
    }

    internal FocusObservationService(
        IFocusAutomationSource source,
        IFocusSnapshotSource snapshotSource,
        Action<FocusChangedNotification>? observer = null,
        Action<int>? errorObserver = null, Func<FocusChangedNotification, bool>? evaluate = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));
        _observer = observer;
        _errorObserver = errorObserver;
        _evaluate = evaluate;
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _thread is { IsAlive: true } && !_stopRequested.WaitOne(0);
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is { IsAlive: true })
            {
                return;
            }

            _startupError = null;
            Interlocked.Increment(ref _eventVersion);
            Interlocked.Exchange(ref _eventTarget, null);
            _started.Reset();
            _stopRequested.Reset();
            while (_focusPending.Wait(0))
            {
            }

            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "VirtualKeyboard.UIA.FocusObserver",
            };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        if (!_started.WaitOne(LifecycleTimeoutMilliseconds))
        {
            _stopRequested.Set();
            throw new TimeoutException("UI Automation focus observation did not start within the lifecycle timeout.");
        }

        if (_startupError is not null)
        {
            throw new InvalidOperationException("UI Automation focus observation could not start.", _startupError);
        }
    }

    /// <summary>Requests one bounded evaluation of the currently focused element.</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not { IsAlive: true }) throw new InvalidOperationException("Focus observation is not running.");
            try { _focusPending.Release(); }
            catch (SemaphoreFullException) { }
        }
    }

    /// <summary>Requests shutdown and returns false only if the observer thread misses the bounded timeout.</summary>
    public bool Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
            if (thread is null)
            {
                return true;
            }

            Interlocked.Increment(ref _eventVersion);
            _stopRequested.Set();
        }

        return thread != Thread.CurrentThread && thread.Join(LifecycleTimeoutMilliseconds);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (Stop())
        {
            _started.Dispose();
            _stopRequested.Dispose();
            _focusPending.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    private void Run()
    {
        bool registered = false;
        try
        {
            _source.Register(OnSourceFocusChanged);
            registered = true;
        }
        catch (Exception exception)
        {
            _startupError = exception;
        }
        finally
        {
            _started.Set();
        }

        try
        {
            if (!registered)
            {
                return;
            }

            FocusSnapshot? lastPolledSnapshot = null;
            bool retryPending = false;
            int retryAttempt = 0;
            WaitHandle[] handles = [_stopRequested, _focusPending.AvailableWaitHandle];
            while (true)
            {
                Volatile.Write(ref _progressTimestamp, System.Diagnostics.Stopwatch.GetTimestamp());
                Interlocked.Increment(ref _completedCycles);
                Volatile.Write(ref _stage, 1);
                int waitResult = WaitHandle.WaitAny(handles, FocusPollingMilliseconds);
                if (waitResult == 0)
                {
                    break;
                }

                bool triggeredByEvent = waitResult == 1;
                if (triggeredByEvent)
                {
                    if (!_focusPending.Wait(0))
                    {
                        continue;
                    }

                    bool stop = false;
                    while (true)
                    {
                        int stableWait = WaitHandle.WaitAny(handles, FocusStabilityMilliseconds);
                        if (stableWait == 0)
                        {
                            stop = true;
                            break;
                        }

                        if (stableWait == WaitHandle.WaitTimeout)
                        {
                            break;
                        }

                        while (_focusPending.Wait(0))
                        {
                        }
                    }

                    if (stop)
                    {
                        break;
                    }
                }

                long eventVersion = Volatile.Read(ref _eventVersion);
                FocusEventTarget? eventTarget = Volatile.Read(ref _eventTarget);
                FocusSnapshot? snapshot;
                try
                {
                    Volatile.Write(ref _stage, 2);
                    snapshot = _snapshotSource.Capture(eventTarget?.Element);
                }
                catch (Exception exception)
                {
                    try { _errorObserver?.Invoke(exception.HResult & 0xFFFF); }
                    catch { }
                    continue;
                }

                if (eventVersion != Volatile.Read(ref _eventVersion)) continue;
                if (!_snapshotSource.UsedEventTarget)
                    Interlocked.CompareExchange(ref _eventTarget, null, eventTarget);

                bool sameFocus = SameFocus(snapshot, lastPolledSnapshot);
                if (!triggeredByEvent && sameFocus && !retryPending)
                {
                    continue;
                }

                if (triggeredByEvent || !sameFocus) retryAttempt = 0;
                else if (retryPending) retryAttempt++;
                lastPolledSnapshot = snapshot;
                retryPending = false;
                try
                {
                    var notification = new FocusChangedNotification(
                        DateTimeOffset.UtcNow, Environment.CurrentManagedThreadId, snapshot, retryAttempt)
                    { CapturedElement = _snapshotSource.CapturedElement, UsedEventTarget = _snapshotSource.UsedEventTarget,
                        UsedFallback = _snapshotSource.UsedFallback,
                        IsCurrentCheck = () => eventVersion == Volatile.Read(ref _eventVersion) };
                    _observer?.Invoke(notification);
                    Volatile.Write(ref _stage, 3);
                    bool needsRetry = _evaluate?.Invoke(notification) == true;
                    retryPending = needsRetry && retryAttempt < MaxEvaluationRetries;
                }
                catch (Exception exception)
                {
                    retryPending = retryAttempt < MaxEvaluationRetries;
                    // Numeric ranges distinguish consumer failures without recording exception text.
                    try { _errorObserver?.Invoke(100_000 + (exception.HResult & 0xFFFF)); }
                    catch { }
                }
            }
        }
        finally
        {
            if (registered)
            {
                try
                {
                    _source.Unregister(OnSourceFocusChanged);
                }
                catch
                {
                    // Teardown failures must not escape the observer thread.
                }
            }

            Interlocked.Increment(ref _eventVersion);
            Interlocked.Exchange(ref _eventTarget, null);
            lock (_gate)
            {
                _thread = null;
            }
        }
    }

    private void OnSourceFocusChanged(AutomationElement? element)
    {
        Interlocked.Exchange(ref _eventTarget, new FocusEventTarget(element));
        Interlocked.Increment(ref _eventVersion);
        try
        {
            _focusPending.Release();
        }
        catch (SemaphoreFullException)
        {
            // Bounded backpressure: a burst cannot grow pending work beyond the fixed capacity.
        }
        catch (ObjectDisposedException)
        {
            // A late native callback may race with service disposal.
        }
    }

    private static bool SameFocus(FocusSnapshot? left, FocusSnapshot? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.ProcessId == right.ProcessId &&
            left.TopLevelHwnd == right.TopLevelHwnd &&
            Equals(left.RuntimeId, right.RuntimeId) &&
            left.ControlType == right.ControlType &&
            left.HasKeyboardFocus == right.HasKeyboardFocus &&
            left.IsEnabled == right.IsEnabled &&
            left.IsOffscreen == right.IsOffscreen &&
            left.IsPassword == right.IsPassword;
    }
}

public readonly record struct FocusChangedNotification(
    DateTimeOffset OccurredAtUtc,
    int ObserverThreadId,
    FocusSnapshot? Snapshot,
    int RetryAttempt = 0)
{
    internal AutomationElement? CapturedElement { get; init; }
    internal Func<bool>? IsCurrentCheck { get; init; }
    public bool IsCurrent => IsCurrentCheck?.Invoke() ?? true;
    public bool UsedEventTarget { get; init; }
    public bool UsedFallback { get; init; }
}

internal interface IFocusAutomationSource
{
    void Register(Action<AutomationElement?> notification);

    void Unregister(Action<AutomationElement?> notification);
}

internal sealed class SystemFocusAutomationSource : IFocusAutomationSource
{
    private AutomationFocusChangedEventHandler? _handler;

    public void Register(Action<AutomationElement?> notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        _handler = (sender, _) => notification(sender as AutomationElement);
        Automation.AddAutomationFocusChangedEventHandler(_handler);
    }

    public void Unregister(Action<AutomationElement?> notification)
    {
        _ = notification;
        if (_handler is not null)
        {
            Automation.RemoveAutomationFocusChangedEventHandler(_handler);
            _handler = null;
        }
    }
}

internal interface IFocusSnapshotSource
{
    FocusSnapshot? Capture(AutomationElement? eventTarget = null);
    AutomationElement? CapturedElement => null;
    bool UsedEventTarget => false;
    bool UsedFallback => false;
}

internal sealed class SystemFocusSnapshotSource(FocusSnapshotFactory factory) : IFocusSnapshotSource
{
    public AutomationElement? CapturedElement { get; private set; }
    public bool UsedEventTarget { get; private set; }
    public bool UsedFallback { get; private set; }

    public FocusSnapshot? Capture(AutomationElement? eventTarget = null)
    {
        AutomationElement? element = FocusedElementResolver.Capture(eventTarget, out bool fallback, out bool fromEvent);
        CapturedElement = element;
        UsedEventTarget = fromEvent;
        UsedFallback = fallback;
        return element is not null && factory.TryCreate(element, out FocusSnapshot? snapshot) ? snapshot : null;
    }
}

internal sealed class NullFocusSnapshotSource : IFocusSnapshotSource
{
    public static NullFocusSnapshotSource Instance { get; } = new();

    public FocusSnapshot? Capture(AutomationElement? eventTarget = null) => null;
}
